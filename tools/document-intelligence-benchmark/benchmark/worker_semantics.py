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


def field(value: str | None, region: dict | None, *, area: bool = False, cell_crop_ocr: str | None = None) -> dict:
    raw = " ".join(str(value or "").split())
    crop = " ".join(str(cell_crop_ocr or "").split()) or None
    chosen = crop or raw
    normalized = normalize_area_evidence(chosen) if area else {"rawOcrText": raw, "normalizedValue": chosen or None, "normalizationReason": None}
    return {
        "rawOcr": raw,
        "pageAssignedOcr": raw,
        "cellCropOcr": crop,
        "normalizedSuggestion": normalized["normalizedValue"],
        "normalizationReason": normalized["normalizationReason"],
        "sourceRegion": region,
        "recognitionWarnings": [] if not crop or crop == raw else ["Page-assigned OCR and cell-crop OCR disagree; human review required"],
    }


def award_candidate(page: int, table_id: int, row_id: int, cells: dict[int, dict], roles: dict[str, int]) -> dict | None:
    khasra = field(cells.get(roles["khasra"], {}).get("text"), cells.get(roles["khasra"], {}).get("region"), cell_crop_ocr=cells.get(roles["khasra"], {}).get("cellCropOcr"))
    number, qualifier = strict_khasra(khasra["rawOcr"])
    if not number:
        return None
    recorded = field(cells.get(roles["recordedArea"], {}).get("text"), cells.get(roles["recordedArea"], {}).get("region"), area=True, cell_crop_ocr=cells.get(roles["recordedArea"], {}).get("cellCropOcr"))
    awarded = field(cells.get(roles["awardedArea"], {}).get("text"), cells.get(roles["awardedArea"], {}).get("region"), area=True, cell_crop_ocr=cells.get(roles["awardedArea"], {}).get("cellCropOcr"))
    rectangle = field(cells.get(roles.get("rectangle"), {}).get("text"), cells.get(roles.get("rectangle"), {}).get("region")) if "rectangle" in roles else None
    warnings = ["Geometry-backed OCR suggestion; human review required"]
    if rectangle is None or not rectangle["normalizedSuggestion"]:
        warnings.append("Rectangle/Mustatil not structurally present; not inherited")
    if not recorded["normalizedSuggestion"] or not awarded["normalizedSuggestion"]:
        warnings.append("Recorded and awarded area are separate fields; one or both require review")
    return {
        "candidateType": "AwardKhasra",
        "structuredPayload": {"tableType": "AwardLandTable", "tableId": table_id, "rowId": row_id, "rectangle": rectangle, "khasraNumber": number, "qualifier": qualifier, "recordedArea": recorded, "awardedArea": awarded, "sourceCells": {"khasra": khasra, "recordedArea": recorded, "awardedArea": awarded}},
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
