"""Conservative geometry-backed LAC table interpretation for the local worker."""
from __future__ import annotations

import re

from .cell_safety_v12 import normalize_area_evidence

STRICT_KHASRA = re.compile(r"^(?P<number>[1-9]\d{0,2}//[1-9]\d{0,2}(?:/[1-9]\d{0,2})*)(?:\s*(?P<qualifier>min))?$", re.I)
CWP = re.compile(r"\b(?:CWP|W\.?P\.?)\s*(?:No\.?\s*)?\d{1,6}\s*/\s*\d{4}\b", re.I)


def page_likely_has_table(words: list[str]) -> bool:
    text = " ".join(words).lower()
    signals = sum(bool(re.search(pattern, text)) for pattern in (
        r"khasra", r"total\s+area", r"area\s+awarded", r"\bcwp\b", r"\bblock\b", r"s\.?no",
    ))
    strict_occurrences = sum(strict_khasra(word)[0] is not None for word in words)
    # Continuation tables often repeat values but not a full header.  They are
    # only *selected* here; structured output still requires geometry + roles.
    return len(words) >= 20 and (signals >= 2 or strict_occurrences >= 4)


def strict_khasra(value: str) -> tuple[str | None, str | None]:
    compact = " ".join(str(value or "").split())
    match = STRICT_KHASRA.fullmatch(compact)
    if not match:
        return None, None
    return match.group("number"), "min" if match.group("qualifier") else None


def table_kind(header_cells: dict[int, str]) -> tuple[str | None, dict[str, int]]:
    roles: dict[str, int] = {}
    for column, value in header_cells.items():
        text = value.lower()
        if re.search(r"khasra|killa", text): roles.setdefault("khasra", column)
        elif re.search(r"rec\s*no|rectangle|mustatil", text): roles.setdefault("rectangle", column)
        elif re.search(r"total\s*area|recorded\s*area", text): roles.setdefault("recordedArea", column)
        elif re.search(r"area\s*awarded|awarded", text): roles.setdefault("awardedArea", column)
        elif re.search(r"cwp|case\s*no", text): roles.setdefault("caseNumber", column)
        elif re.search(r"status", text): roles.setdefault("status", column)
        elif re.fullmatch(r"area", text.strip()): roles.setdefault("area", column)
        elif re.search(r"block|class", text): roles.setdefault("block", column)

    if {"khasra", "recordedArea", "awardedArea"}.issubset(roles):
        return "AwardLandTable", roles
    if "khasra" in roles and "caseNumber" in roles:
        return "CourtCwpTable", roles
    if "khasra" in roles and "block" in roles:
        return "LandClassification", roles
    if "claim" in " ".join(header_cells.values()).lower():
        return "WeakClaimTable", roles
    return None, roles


def award_table_groups(header_cells: dict[int, str], column_count: int | None = None) -> list[dict[str, int]]:
    """Return independently-addressable repeated Award table schemas.

    Award pages can print two (or more) ``RecN | Khasra | Total | Awarded``
    schemas beside one another.  A semantic role is therefore not a unique
    column identity.  Each returned mapping contains the physical column for
    one schema only; callers must still keep it within a single grid row.
    """
    columns = sorted(header_cells)
    classified: dict[int, str] = {}
    for column in columns:
        text = header_cells[column].lower()
        if re.search(r"khasra|killa", text):
            classified[column] = "khasra"
        elif re.search(r"total\s*area|recorded\s*area", text):
            classified[column] = "recordedArea"
        elif re.search(r"area\s*awarded|awarded", text):
            classified[column] = "awardedArea"
        elif re.search(r"rec\s*no|rectangle|mustatil", text):
            classified[column] = "rectangle"

    khasra_columns = [column for column in columns if classified.get(column) == "khasra"]
    groups: list[dict[str, int]] = []
    for group_number, khasra_column in enumerate(khasra_columns, 1):
        next_khasra = next((column for column in khasra_columns if column > khasra_column), None)
        end = next_khasra if next_khasra is not None else float("inf")
        following = [column for column in columns if khasra_column < column < end]
        recorded = next((column for column in following if classified.get(column) == "recordedArea"), None)
        awarded = next((column for column in following if recorded is not None and column > recorded and classified.get(column) == "awardedArea"), None)
        # If an expected local header was missed, the next repeated schema's
        # Area Awarded header is not a substitute.  The fixed Award layout has
        # adjacent semantic columns (with at most one detector-only gap).
        if recorded is None or awarded is None or recorded - khasra_column > 2 or awarded - recorded > 2:
            continue
        group = {"logicalGroupId": group_number, "khasra": khasra_column,
                 "recordedArea": recorded, "awardedArea": awarded}
        # A rectangle column belongs to the nearest schema on its right.
        prior = [column for column in columns if column < khasra_column and classified.get(column) == "rectangle"]
        if prior:
            group["rectangle"] = prior[-1]
        groups.append(group)
    # Structure recognition occasionally sees only the right-hand repeated
    # header while it still supplies the full two-up grid. Recover the missing
    # peer schema by *physical grid stride*, never by flattening values. The
    # Award layouts supported here are RecN|Khasra|Total|Awarded (4 columns)
    # or Khasra|Total|Awarded (3 columns). A stride is accepted only when the
    # complete grid and the recognised role offsets prove that repetition.
    if len(groups) == 1 and column_count:
        anchor = groups[0]
        inferred: list[dict[str, int]] | None = None
        for stride, khasra_offset, rectangle_offset in ((4, 1, 0), (3, 0, None)):
            if column_count < stride * 2 or column_count % stride != 0:
                continue
            if anchor["khasra"] % stride != khasra_offset:
                continue
            if anchor["recordedArea"] - anchor["khasra"] != 1 or anchor["awardedArea"] - anchor["recordedArea"] != 1:
                continue
            inferred = []
            for base in range(0, column_count, stride):
                group = {"logicalGroupId": base // stride + 1, "khasra": base + khasra_offset,
                         "recordedArea": base + khasra_offset + 1, "awardedArea": base + khasra_offset + 2}
                if rectangle_offset is not None:
                    group["rectangle"] = base + rectangle_offset
                inferred.append(group)
            break
        if inferred is not None:
            return inferred
    return groups


def field(value: str | None, region: dict | None, *, area: bool = False, cell_crop_ocr: str | None = None, cell_identity: dict | None = None) -> dict:
    raw = " ".join(str(value or "").split())
    crop = " ".join(str(cell_crop_ocr or "").split()) or None
    chosen = crop or raw
    normalized = normalize_area_evidence(chosen) if area else {"rawOcrText": raw, "normalizedValue": chosen or None, "normalizationReason": None}
    result = {
        "rawOcr": raw,
        "pageAssignedOcr": raw,
        "cellCropOcr": crop,
        "normalizedSuggestion": normalized["normalizedValue"],
        "normalizationReason": normalized["normalizationReason"],
        "sourceRegion": region,
        "recognitionWarnings": [] if not crop or crop == raw else ["Page-assigned OCR and cell-crop OCR disagree; human review required"],
    }
    if cell_identity:
        result["tableId"] = cell_identity["tableId"]
        result["rowId"] = cell_identity["rowId"]
        result["columnIndex"] = cell_identity["columnIndex"]
        result["logicalGroupId"] = cell_identity["logicalGroupId"]
        result["columnXRange"] = None if region is None else {"x": region["x"], "width": region["width"]}
    return result


def award_candidate(page: int, table_id: int, row_id: int, cells: dict[int, dict], roles: dict[str, int]) -> dict | None:
    group_id = roles.get("logicalGroupId", 1)
    def source(column: int) -> dict:
        cell = cells.get(column, {})
        return {"tableId": table_id, "rowId": row_id, "columnIndex": column, "logicalGroupId": group_id}
    khasra = field(cells.get(roles["khasra"], {}).get("text"), cells.get(roles["khasra"], {}).get("region"), cell_crop_ocr=cells.get(roles["khasra"], {}).get("cellCropOcr"), cell_identity=source(roles["khasra"]))
    number, qualifier = strict_khasra(khasra["rawOcr"])
    if not number:
        return None
    recorded = field(cells.get(roles["recordedArea"], {}).get("text"), cells.get(roles["recordedArea"], {}).get("region"), area=True, cell_crop_ocr=cells.get(roles["recordedArea"], {}).get("cellCropOcr"), cell_identity=source(roles["recordedArea"]))
    awarded = field(cells.get(roles["awardedArea"], {}).get("text"), cells.get(roles["awardedArea"], {}).get("region"), area=True, cell_crop_ocr=cells.get(roles["awardedArea"], {}).get("cellCropOcr"), cell_identity=source(roles["awardedArea"]))
    rectangle = field(cells.get(roles.get("rectangle"), {}).get("text"), cells.get(roles.get("rectangle"), {}).get("region"), cell_identity=source(roles["rectangle"])) if "rectangle" in roles else None
    warnings = ["Geometry-backed OCR suggestion; human review required"]
    if rectangle is None or not rectangle["normalizedSuggestion"]:
        warnings.append("Rectangle/Mustatil not structurally present; not inherited")
    if not recorded["normalizedSuggestion"] or not awarded["normalizedSuggestion"]:
        warnings.append("Recorded and awarded area are separate fields; one or both require review")
    return {
        "candidateType": "AwardKhasra",
        "structuredPayload": {"tableType": "AwardLandTable", "tableId": table_id, "rowId": row_id, "logicalGroupId": group_id, "rectangle": rectangle, "khasraNumber": number, "qualifier": qualifier, "recordedArea": recorded, "awardedArea": awarded, "sourceCells": {"khasra": khasra, "recordedArea": recorded, "awardedArea": awarded}},
        "page": page,
        "sourceRegion": cells[roles["khasra"]]["region"],
        "rawSourceText": khasra["rawOcr"],
        "rawOcr": khasra["rawOcr"],
        "normalizedSuggestion": f"{number}{' min' if qualifier else ''}",
        "normalizationReason": None,
        "confidence": cells[roles["khasra"]].get("confidence"),
        "interpretationWarnings": warnings,
    }


def court_candidate(page: int, table_id: int, row_id: int, cells: dict[int, dict], roles: dict[str, int]) -> dict | None:
    case = field(cells.get(roles["caseNumber"], {}).get("text"), cells.get(roles["caseNumber"], {}).get("region"), cell_crop_ocr=cells.get(roles["caseNumber"], {}).get("cellCropOcr"))
    case_match = CWP.search(case["rawOcr"])
    khasra = field(cells.get(roles["khasra"], {}).get("text"), cells.get(roles["khasra"], {}).get("region"), cell_crop_ocr=cells.get(roles["khasra"], {}).get("cellCropOcr"))
    number, qualifier = strict_khasra(khasra["rawOcr"])
    if not case_match or not number:
        return None
    status = field(cells.get(roles.get("status"), {}).get("text"), cells.get(roles.get("status"), {}).get("region")) if "status" in roles else None
    area = field(cells.get(roles.get("area"), {}).get("text"), cells.get(roles.get("area"), {}).get("region"), area=True) if "area" in roles else None
    return {"candidateType": "CourtCase", "structuredPayload": {"tableType": "CourtCwpTable", "tableId": table_id, "rowId": row_id, "caseNumber": case, "khasraNumber": number, "qualifier": qualifier, "area": area, "status": status, "stay": None}, "page": page, "sourceRegion": case["sourceRegion"], "rawSourceText": case["rawOcr"], "rawOcr": case["rawOcr"], "normalizedSuggestion": case_match.group(0), "normalizationReason": None, "confidence": cells[roles["caseNumber"]].get("confidence"), "interpretationWarnings": ["Court case is source evidence only; no stay is inferred", "Human review required"]}


def classification_candidate(page: int, table_id: int, row_id: int, cells: dict[int, dict], roles: dict[str, int]) -> dict | None:
    khasra = field(cells.get(roles["khasra"], {}).get("text"), cells.get(roles["khasra"], {}).get("region"))
    number, qualifier = strict_khasra(khasra["rawOcr"])
    if not number:
        return None
    block = field(cells.get(roles["block"], {}).get("text"), cells.get(roles["block"], {}).get("region"))
    area = field(cells.get(roles.get("area"), {}).get("text"), cells.get(roles.get("area"), {}).get("region"), area=True) if "area" in roles else None
    return {"candidateType": "LandClassification", "structuredPayload": {"tableType": "LandClassification", "tableId": table_id, "rowId": row_id, "khasraNumber": number, "qualifier": qualifier, "area": area, "block": block}, "page": page, "sourceRegion": khasra["sourceRegion"], "rawSourceText": khasra["rawOcr"], "rawOcr": khasra["rawOcr"], "normalizedSuggestion": number, "normalizationReason": None, "confidence": cells[roles["khasra"]].get("confidence"), "interpretationWarnings": ["Classification is geometry-backed but requires human review"]}
