"""Local-only v1 document worker: RapidOCR words bound to Table Transformer cells."""
from __future__ import annotations

import argparse
import json
import sys
import time
import re
from datetime import date
from pathlib import Path

CONTRACT_VERSION = 1
BENCHMARK_ROOT = Path(__file__).resolve().parents[1] / "document-intelligence-benchmark"
sys.path.insert(0, str(BENCHMARK_ROOT))


def fail(message: str) -> int:
    print(message, file=sys.stderr)
    return 2


def region(box) -> dict:
    return {"x": float(box.x), "y": float(box.y), "width": float(box.width), "height": float(box.height)}


def ocr_words(output):
    """Convert one RapidOCR response into the shared worker token shape."""
    try:
        from benchmark.normalized import BoundingBox, Word
    except ModuleNotFoundError:
        # Tests may already have loaded the worker-local ``benchmark`` namespace.
        sys.path.insert(0, str(BENCHMARK_ROOT / "benchmark"))
        from normalized import BoundingBox, Word
    texts = list(output.txts) if output.txts is not None else []
    boxes = list(output.boxes) if output.boxes is not None else []
    scores = list(output.scores) if output.scores is not None else []
    words = []
    for text, box, score in zip(texts, boxes, scores):
        raw = " ".join(str(text).split())
        if not raw:
            continue
        xs = [float(point[0]) for point in box]
        ys = [float(point[1]) for point in box]
        words.append(Word(raw, BoundingBox(min(xs), min(ys), max(xs) - min(xs), max(ys) - min(ys)), float(score)))
    return words


def inside(outer: dict, inner: dict) -> bool:
    x, y = inner["x"] + inner["width"] / 2, inner["y"] + inner["height"] / 2
    return outer["x"] <= x <= outer["x"] + outer["width"] and outer["y"] <= y <= outer["y"] + outer["height"]


def rows_for_table(geometry: list[dict], table_box: dict, words, page: int, table_id: int):
    from benchmark.normalized import BoundingBox
    from benchmark.table_geometry import join_words_to_grid

    rows = [BoundingBox(**item["box"]) for item in geometry if item["label"] == "table row" and inside(table_box, item["box"])]
    columns = [BoundingBox(**item["box"]) for item in geometry if item["label"] == "table column" and inside(table_box, item["box"])]
    if len(rows) < 2 or len(columns) < 2:
        return []
    rows.sort(key=lambda value: value.y)
    columns.sort(key=lambda value: value.x)
    table, _ = join_words_to_grid(table_box=BoundingBox(**table_box), row_boxes=rows, column_boxes=columns, words=words)
    grouped: dict[int, dict[int, dict]] = {}
    for cell in table.cells:
        grouped.setdefault(cell.row, {})[cell.column] = {
            "text": cell.text,
            "region": region(cell.bounding_box),
            "confidence": None,
            "page": page,
            "tableId": table_id,
        }
    return grouped, len(columns)


def header_cells_for_table(geometry: list[dict], table_box: dict, words) -> dict[int, str]:
    """Bind header OCR to Table Transformer header + column geometry only."""
    columns = [item["box"] for item in geometry if item["label"] == "table column" and inside(table_box, item["box"])]
    columns.sort(key=lambda value: value["x"])
    headers = [item["box"] for item in geometry if item["label"] in {"table column header", "table spanning cell"} and inside(table_box, item["box"])]
    values: dict[int, list[str]] = {}
    for word in words:
        word_box = region(word.bounding_box)
        if not any(inside(header, word_box) for header in headers):
            continue
        matching = [index for index, column in enumerate(columns) if inside(column, word_box)]
        if len(matching) == 1:
            values.setdefault(matching[0], []).append(word.text)
    return {column: " ".join(text) for column, text in values.items()}


def crop_ocr(image, cell: dict, ocr, crop_cache: dict[tuple[float, float, float, float], str | None], counters: dict[str, int]) -> str | None:
    """Recognition-only unified crop; never used outside Table Transformer cells."""
    from benchmark.cell_crop_pipeline_v9 import normalize_from_page

    box = cell["region"]
    key = (float(box["x"]), float(box["y"]), float(box["width"]), float(box["height"]))
    if key in crop_cache:
        counters["cellOcrCacheHits"] += 1
        return crop_cache[key]
    started = time.perf_counter()
    _, normalized, _ = normalize_from_page(image, cell["region"])
    output = ocr(normalized)
    counters["selectiveCellOcrSeconds"] += time.perf_counter() - started
    counters["cellOcrCalls"] += 1
    texts = list(output.txts) if output.txts is not None else []
    crop_cache[key] = " ".join(" ".join(str(text).split()) for text in texts if str(text).strip()) or None
    return crop_cache[key]


def structured_from_geometry(page: int, geometry: list[dict], words, image, ocr, crop_cache: dict[tuple[float, float, float, float], str | None], counters: dict[str, int], enable_court: bool = True) -> tuple[list[dict], dict[str, int]]:
    from benchmark.worker_semantics import award_candidate, award_table_groups, classification_candidate, court_candidate, infer_court_table_kind, table_kind

    candidates: list[dict] = []
    counts = {"tables": 0, "awardRows": 0, "courtRows": 0, "classificationRows": 0,
              "courtTableSeconds": 0.0}
    tables = [item for item in geometry if item["label"] == "table"]
    counters["geometryTables"] += len(tables)
    for table_id, item in enumerate(tables, 1):
        rows, column_count = rows_for_table(geometry, item["box"], words, page, table_id)
        if not rows:
            counters["tablesWithoutGridRows"] += 1
            continue
        counters["tablesWithGridRows"] += 1
        header_cells = header_cells_for_table(geometry, item["box"], words)
        # Some scanned Court grids receive row/column geometry but no separate
        # Table-Transformer header box. The first *same-table grid row* is a
        # defensible Court-only label fallback; it never reads a neighbouring
        # table or promotes an Award/Khasra grid to a new interpretation path.
        grid_header = {column: cell["text"] for column, cell in rows[min(rows)].items() if cell.get("text")}
        merged_headers = {**grid_header, **header_cells}
        header_text = " ".join(merged_headers.values()).lower()
        if re.search(r"cwp|case\s*no", header_text):
            counters["courtHeaderSignals"] += 1
        if re.search(r"khasra|killa", header_text):
            counters["khasraHeaderSignals"] += 1
        kind, roles = infer_court_table_kind(merged_headers, rows, min(rows))
        if kind is None:
            fallback_kind, fallback_roles = infer_court_table_kind(grid_header, rows, min(rows))
            if fallback_kind != "CourtCwpTable":
                counters["tablesUnclassified"] += 1
                continue
            header_cells, kind, roles = grid_header, fallback_kind, fallback_roles
            counters["headerFallbackInvocations"] += 1
        else:
            header_cells = merged_headers
        header_index = min(rows)
        roles["headerLabels"] = list(header_cells.values())
        counts["tables"] += 1
        for row_id, cells in sorted(rows.items()):
            if row_id <= header_index:
                continue
            candidate = None
            if kind == "AwardLandTable":
                from benchmark.worker_semantics import strict_khasra
                from benchmark.cell_safety_v12 import normalize_area_evidence
                for group in award_table_groups(header_cells, column_count):
                    # A candidate is built from one physical row and one repeated
                    # schema only. Missing area cells stay missing; they never
                    # borrow an aligned value from another subtable.
                    if strict_khasra(cells.get(group["khasra"], {}).get("text", ""))[0] is None:
                        continue
                    khasra_column = group["khasra"]
                    if khasra_column in cells:
                        cells[khasra_column]["cellCropOcr"] = crop_ocr(image, cells[khasra_column], ocr, crop_cache, counters)
                    for role in ("recordedArea", "awardedArea"):
                        column = group[role]
                        if column in cells and normalize_area_evidence(cells[column]["text"])["status"] != "Valid":
                            cells[column]["cellCropOcr"] = crop_ocr(image, cells[column], ocr, crop_cache, counters)
                    candidate = award_candidate(page, table_id, row_id, cells, group)
                    if candidate:
                        candidates.append(candidate)
                        counts["awardRows"] += 1
                continue
            elif kind == "CourtCwpTable":
                counters["courtTableRowsInspected"] += 1
                if not enable_court:
                    continue
                started = time.perf_counter()
                candidate = court_candidate(page, table_id, row_id, cells, roles)
                counts["courtTableSeconds"] += time.perf_counter() - started
                if candidate:
                    counts["courtRows"] += 1
            elif kind == "LandClassification":
                candidate = classification_candidate(page, table_id, row_id, cells, roles)
                if candidate:
                    counts["classificationRows"] += 1
            if candidate:
                candidates.append(candidate)
    return candidates, counts


def _lines(words):
    """Return conservative OCR lines with a source rectangle for each line."""
    grouped = []
    for word in sorted(words, key=lambda value: (value.bounding_box.y, value.bounding_box.x)):
        line = next((item for item in grouped if abs(item["y"] - word.bounding_box.y) <= max(8, word.bounding_box.height * .7)), None)
        if line is None:
            line = {"y": word.bounding_box.y, "words": []}
            grouped.append(line)
        line["words"].append(word)
    for line in grouped:
        values = sorted(line["words"], key=lambda value: value.bounding_box.x)
        left = min(value.bounding_box.x for value in values); top = min(value.bounding_box.y for value in values)
        right = max(value.bounding_box.x + value.bounding_box.width for value in values); bottom = max(value.bounding_box.y + value.bounding_box.height for value in values)
        yield " ".join(value.text for value in values), {"x": float(left), "y": float(top), "width": float(right-left), "height": float(bottom-top)}


def narrative_core_and_statutory_candidates(page: int, words) -> list[dict]:
    """Strict, label-led document facts. Identifiers are copied, never repaired."""
    lines = list(_lines(words)); result = []; seen = set()
    page_text = "\n".join(text for text, _ in lines)
    framework = ("National Highways Act" if re.search(r"national\s+highways?|\bnh\s+act\b", page_text, re.I)
                 else "Land Acquisition Act" if re.search(r"land\s+acquisition\s+act|\bla\s+act\b", page_text, re.I) else None)
    award_type = "Supplementary" if re.search(r"\bsupplementary\s+award\b", page_text, re.I) else "Main" if re.search(r"\bmain\s+award\b", page_text, re.I) else None
    parent = None
    if award_type == "Supplementary":
        match = re.search(r"\b(?:parent|main)\s+award\s*(?:no\.?|number)?\s*[:.-]?\s*([A-Za-z0-9][A-Za-z0-9/.-]{1,49})", page_text, re.I)
        parent = match.group(1) if match else None
    core = None
    for text, box in lines:
        match = re.search(r"\baward\s*(?:no\.?|number|nos\.?)[\s:#.-]*([A-Za-z0-9][A-Za-z0-9/.-]{1,49})", text, re.I)
        if not match:
            continue
        number = match.group(1)
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9/.-]{1,49}", number):
            continue
        date = None
        nearby = " ".join(value for value, _ in lines if value == text or "award date" in value.lower())
        date_match = re.search(r"\b(\d{1,2}[./-]\d{1,2}[./-]\d{4})\b", nearby)
        date = date_match.group(1) if date_match else None
        core = {"awardNumber": number, "awardDate": date, "awardType": award_type, "natureOfAcquisition": None, "purpose": None, "awardedAreaText": None, "parentAwardReferenceSuggestion": parent}
        result.append({"candidateType": "AwardCore", "structuredPayload": core, "page": page, "sourceRegion": box, "rawSourceText": text, "rawOcr": number, "normalizedSuggestion": number, "normalizationReason": None, "confidence": None, "interpretationWarnings": ["Label-led local OCR suggestion; verify Award identifier exactly as printed."]})
        break
    for text, box in lines:
        village = re.search(r"\b(?:name\s+of\s+village|village|revenue\s+estate|mauza)\s*[:.-]\s*([A-Za-z][A-Za-z .'-]{1,99})", text, re.I)
        if village:
            value = village.group(1).strip().rstrip(".")
            result.append({"candidateType": "AwardVillage", "structuredPayload": {"villageName": value}, "page": page, "sourceRegion": box, "rawSourceText": text, "rawOcr": value, "normalizedSuggestion": value, "normalizationReason": None, "confidence": None, "interpretationWarnings": ["Village/revenue-estate label requires human confirmation."]})
            break
    # Add narrative values to the same Award-core suggestion only when their
    # labels are explicit; this never guesses a missing value.
    if core is not None:
        for text, _ in lines:
            for key, label in (("natureOfAcquisition", r"nature\s+of\s+acquisition"), ("purpose", r"purpose\s+of\s+acquisition"), ("awardedAreaText", r"(?:awarded|acquisition)\s+area")):
                match = re.search(label + r"\s*[:.-]\s*(.{1,180})$", text, re.I)
                if match and not core.get(key):
                    core[key] = match.group(1).strip()
    for text, box in lines:
        section = re.search(r"\b(?:u\s*/?\s*s\.?|under\s+section|section|sec\.?)\s*(3\s*[ad]|17\s*\(\s*1\s*\)|17|4|6)(?!\d)", text, re.I)
        if not section:
            continue
        section_value = "Section " + re.sub(r"\s+", "", section.group(1)).upper()
        after = text[section.end():]
        number = re.search(r"^\s*(?:(?:notification|notice)\s*)?(?:no\.?\s*)?([A-Za-z][A-Za-z0-9()./&-]{2,149})", after, re.I)
        date = re.search(r"\b(\d{1,2}[./-]\d{1,2}[./-]\d{4})\b", after)
        if not number or not re.search(r"\d", number.group(1)) or not re.search(r"[/.-]", number.group(1)):
            result.append({"candidateType": "UnmappedAwardFinding", "structuredPayload": {"category": "Statutory reference", "summary": "A legal provision was detected without a safe notification identifier."}, "page": page, "sourceRegion": box, "rawSourceText": text, "rawOcr": None, "normalizedSuggestion": None, "normalizationReason": None, "confidence": None, "interpretationWarnings": ["Legal reference retained; no Notification was fabricated."]})
            continue
        identifier = number.group(1).rstrip(".")
        key = f"{framework}|{section_value}|{identifier}|{date.group(1) if date else ''}"
        if key in seen:
            continue
        seen.add(key)
        result.append({"candidateType": "Notification", "structuredPayload": {"legalFramework": framework, "section": section_value, "notificationNumber": identifier, "notificationDate": date.group(1) if date else None}, "page": page, "sourceRegion": box, "rawSourceText": text, "rawOcr": identifier, "normalizedSuggestion": identifier, "normalizationReason": None, "confidence": None, "interpretationWarnings": ["Statutory notification occurrence requires human verification; exact digits were not repaired."]})
    return result


def valuation_and_compensation_candidates(page: int, words) -> list[dict]:
    """Label-led review suggestions from the already-produced page OCR words."""
    result = []
    for text, box in _lines(words):
        lower = text.lower()
        amount = re.search(r"(?:rs\.?|inr)?\s*(\d+(?:,\d{3})*(?:\.\d+)?)", text, re.I)
        percentage = re.search(r"\b(\d+(?:\.\d+)?)\s*%", text)
        section = re.search(r"\b(?:u\s*/?\s*s\.?|section|sec\.?)\s*([0-9]+[a-z]?(?:\s*\([^)]*\))?)", text, re.I)
        legal = section.group(0) if section else None
        value = amount.group(1).replace(",", "") if amount else None
        if re.search(r"\b(?:market\s+value|market\s+rate|rate\s+per|assessed\s+value|value\s+assessed)\b", lower):
            unit = re.search(r"\bper\s+([A-Za-z ]{2,30})", text, re.I)
            result.append({"candidateType":"ValuationRule","structuredPayload":{"ruleType":"Structure assessed value" if "structure" in lower else "Market value", "rateAmount":value, "rateUnit":("per " + unit.group(1).strip()) if unit else None, "legalSection":legal, "sourceLabel":text},"page":page,"sourceRegion":box,"rawSourceText":text,"rawOcr":value,"normalizedSuggestion":value,"normalizationReason":None,"confidence":None,"interpretationWarnings":["Valuation amount/rate is source evidence only; verify digits and unit."]})
        compensation = "Solatium" if "solatium" in lower else "AdditionalAmount" if "additional amount" in lower else "Interest" if re.search(r"\binterest\b", lower) else None
        if compensation:
            result.append({"candidateType":"CompensationRule","structuredPayload":{"ruleType":compensation,"ratePercent":percentage.group(1) if percentage else None,"rateAmount":value,"legalSection":legal,"appliesToText":text},"page":page,"sourceRegion":box,"rawSourceText":text,"rawOcr":percentage.group(1) if percentage else value,"normalizedSuggestion":percentage.group(1) if percentage else value,"normalizationReason":None,"confidence":None,"interpretationWarnings":["Rule and any source-calculated amount remain separate; no calculation was performed."]})
        if re.search(r"\b(?:amount\s+already\s+(?:paid|received)|balance\s+amount|grand\s+(?:award|total)|total\s+award|market\s+value\s+amount|solatium\s+amount|interest\s+amount)\b", lower):
            result.append({"candidateType":"UnmappedAwardFinding","structuredPayload":{"category":"Award summary component","summary":"Source-labelled calculation component retained for human reconciliation."},"page":page,"sourceRegion":box,"rawSourceText":text,"rawOcr":value,"normalizedSuggestion":value,"normalizationReason":None,"confidence":None,"interpretationWarnings":["Summary component preserved exactly; arithmetic is not auto-reconciled."]})
        if re.search(r"\b(?:structure|tree|well|tubewell)\b", lower) and re.search(r"\b(?:assessed|valuation|value)\b", lower):
            result.append({"candidateType":"SupplementaryMatter","structuredPayload":{"matterType":"Structure / asset valuation","description":text,"khasraReferenceSuggestion":None},"page":page,"sourceRegion":box,"rawSourceText":text,"rawOcr":value,"normalizedSuggestion":value,"normalizationReason":None,"confidence":None,"interpretationWarnings":["Source-reported valuation name/context is not an owner or interested-person relationship."]})
    return result


def _strict_possession_date(text: str) -> tuple[str | None, str | None]:
    """Normalize only an unambiguous numeric day-month-year source date."""
    match = re.search(r"\b(\d{1,2})[./-](\d{1,2})[./-](\d{4})\b", text)
    if not match:
        return None, None
    try:
        value = date(int(match.group(3)), int(match.group(2)), int(match.group(1)))
    except ValueError:
        return None, "A possession date was present but could not be safely normalized."
    return value.isoformat(), None


def _possession_status(text: str) -> tuple[str, str] | None:
    """Return a conservative normalized event type with exact source wording."""
    patterns = (
        (r"\bpossession\s+(?:could\s+not|cannot|was\s+not)\s+be\s+taken\b", "Possession not taken"),
        (r"\bpossession\s+(?:is|was|has\s+been)\s+stayed\b|\bstay\s+of\s+possession\b", "Possession stayed"),
        (r"\bbalance\s+possession\b", "Balance possession"),
        (r"\bpartial\s+possession\b|\bpossession\s+of\s+part\b", "Partial possession"),
        (r"\bphysical\s+possession\s+(?:has\s+been|was\s+)?(?:taken|taken\s+over)\b", "Physical possession taken"),
        (r"\bpossession\s+(?:has\s+been|was\s+)?taken(?:\s+over)?\b|\btaken\s+over\b", "Possession taken"),
    )
    for pattern, event_type in patterns:
        match = re.search(pattern, text, re.I)
        if match:
            return event_type, match.group(0)
    return None


def _possession_area(text: str) -> tuple[str | None, str | None]:
    """Keep only an area stated on the same possession occurrence line."""
    match = re.search(r"\b(?:area|land)\s*(?:of|:|-)?\s*(\d+(?:\s*[-–—]\s*\d+){1,2}|\d+(?:\.\d+)?\s*(?:acre|acres|bigha|biswa|sq\.?\s*yards?))\b", text, re.I)
    if not match:
        return None, None
    raw = re.sub(r"\s+", " ", match.group(1)).strip()
    unit = next((name for name in ("acre", "bigha", "biswa", "sq yards") if name in raw.lower().replace(".", "")), None)
    return raw, unit


def _possession_khasras(text: str) -> str | None:
    match = re.search(r"\b(?:khasra|killa)\s*(?:no\.?|number)?\s*[:.-]?\s*([0-9][0-9/,.\-\s]*(?:\bmin\b)?)", text, re.I)
    return re.sub(r"\s+", " ", match.group(1)).strip().rstrip(".,;") if match else None


def possession_candidates(page: int, words) -> list[dict]:
    """Explicit, source-occurrence-preserving possession review suggestions."""
    result = []
    for text, box in _lines(words):
        status = _possession_status(text)
        if status is None:
            continue
        event_type, exact_status = status
        normalized_date, date_warning = _strict_possession_date(text)
        area_text, area_unit = _possession_area(text)
        khasras = _possession_khasras(text)
        warnings = ["Possession source occurrence requires human verification; no Award or Village area was used."]
        if date_warning:
            warnings.append(date_warning)
        if khasras:
            warnings.append("Khasra references are source text only; no Khasra relationship was inferred.")
        result.append({
            "candidateType": "PossessionEvent",
            "structuredPayload": {
                "possessionDate": normalized_date,
                "eventType": event_type,
                "status": exact_status,
                "possessionAreaText": area_text,
                "possessionAreaUnit": area_unit,
                "khasraReferences": khasras,
            },
            "page": page,
            "sourceRegion": box,
            "rawSourceText": text,
            "rawOcr": text,
            "normalizedSuggestion": normalized_date or event_type,
            "normalizationReason": "Unambiguous numeric possession date" if normalized_date else None,
            "confidence": None,
            "interpretationWarnings": warnings,
        })
    return result


_NARRATIVE_CASE = re.compile(r"\b(?P<label>CWP|W\.?P\.?\s*\(?C\)?|Writ\s+Petition|Case\s+No\.?)\s*(?:No\.?\s*)?(?P<number>\d{1,6}\s*/\s*\d{4})\b", re.I)


def court_case_candidates(page: int, words) -> list[dict]:
    """Extract only identifiable local source case occurrences, never legal effect."""
    result = []
    for text, box in _lines(words):
        match = _NARRATIVE_CASE.search(text)
        if not match:
            continue
        label = re.sub(r"\s+", " ", match.group("label")).strip()
        case_type = "CWP" if label.lower() == "cwp" else "W.P.(C)" if re.match(r"w\.?p", label, re.I) else "Writ Petition" if "writ" in label.lower() else "Case"
        raw_identifier = match.group(0)
        status = None
        status_match = re.search(r"\b(stay\s+granted|stay\s+vacated|pending|dismissed|disposed|status\s+quo)\b|\b(?:status|order)\s*[:.-]\s*(.{1,160}?)(?=\s*,?\s*(?:khasra|killa|(?:total\s+)?area)\b|$)", text, re.I)
        if status_match:
            status = (status_match.group(1) or status_match.group(2)).strip().rstrip(".,;")
        khasra_match = re.search(r"\b(?:khasra|killa)\s*(?:no\.?|number)?\s*[:.-]?\s*([0-9][0-9/,\.\-\s]*(?:\bmin\b)?)", text, re.I)
        area_match = re.search(r"\b(?:total\s+)?area\s*[:.-]?\s*(\d+(?:\s*[-–—]\s*\d+){1,2}|\d+(?:\.\d+)?\s*(?:acre|acres|bigha|biswa|sq\.?\s*yards?))", text, re.I)
        result.append({
            "candidateType": "CourtCase",
            "structuredPayload": {"caseNumber": raw_identifier, "caseType": case_type, "courtName": None, "status": status,
                                  "khasraReferences": re.sub(r"\s+", " ", khasra_match.group(1)).strip().rstrip(".,;") if khasra_match else None,
                                  "relatedAreaText": re.sub(r"\s+", " ", area_match.group(1)).strip() if area_match else None,
                                  "parties": None},
            "page": page, "sourceRegion": box, "rawSourceText": text, "rawOcr": raw_identifier,
            "normalizedSuggestion": raw_identifier, "normalizationReason": None, "confidence": None,
            "interpretationWarnings": ["Case reference is source evidence only; no stay or legal effect is inferred.", "Any Khasra or area is source text only; no canonical relationship was created."]
        })
    return result


def nm_pilot_candidates(page: int, words, image_width: int, image_height: int) -> list[dict]:
    """Conservative, layout-free NM source bands for assisted review only.

    This is intentionally not a form reconstruction: neighbouring OCR lines are
    retained as one reviewable source band and every value remains raw OCR.
    """
    lines = list(_lines(words))
    result = []
    starts = [i for i, (text, _) in enumerate(lines) if _nm_owner_start(text)]
    if not starts:
        return [_nm_band(page, i + 1, [line], True) for i, line in enumerate(lines) if line[0].strip()]
    # Header/weak text before the first recognised owner must remain visible to
    # a reviewer. It is never silently swallowed by the first owner block.
    for i, line in enumerate(lines[:starts[0]]):
        if line[0].strip():
            result.append(_nm_band(page, -(i + 1), [line], True))
    for block_number, start in enumerate(starts, 1):
        end = starts[block_number] if block_number < len(starts) else len(lines)
        result.append(_nm_band(page, block_number, lines[start:end]))
    return result


def _nm_owner_start(text: str) -> bool:
    lower = text.lower()
    if "name of owner" in lower or "running total" in lower:
        return False
    return bool(re.search(r"\b[A-Za-z]{3,}(?:\s+[A-Za-z]{3,}){0,4}\s+(?:s/o|w/o|d/o|m/o)\s+[A-Za-z]{3,}", text, re.I))


def _nm_band(page: int, serial: int, band, force_fragment: bool = False) -> dict:
    text = " ".join(value[0] for value in band)
    left = min(value[1]["x"] for value in band); top = min(value[1]["y"] for value in band)
    right = max(value[1]["x"] + value[1]["width"] for value in band); bottom = max(value[1]["y"] + value[1]["height"] for value in band)
    # These are only hints for the reviewer.  A raw token is retained even if
    # it is not a valid Khasra, because it must never be silently repaired.
    khasras = re.findall(r"\b\d{1,3}\s*/\s*/\s*\d{1,3}(?:\s*/\s*\d{1,3})?(?:\s+min)?\b", text, re.I)
    raw_khasra = ", ".join(khasras) if khasras else "OCR fragment — select canonical Khasra manually"
    areas = re.findall(r"\b\d+\s*[-–—]\s*\d+(?:\s*[-–—]\s*\d+)?\b", text)
    money = re.search(r"(?:rs\.?|₹)\s*([0-9][0-9,]*(?:\.\d{1,2})?)", text, re.I)
    person = re.sub(r"\s+", " ", text).strip()[:240]
    # A band is safe only when it has all independent business anchors.  This
    # intentionally rejects most weak NM OCR instead of inventing a record.
    serial_anchor = bool(re.match(r"^\s*(?:s\.?\s*)?\d{1,4}\b", text, re.I))
    name_anchor = _nm_owner_start(text)
    field_count = int(bool(khasras)) + int(bool(areas)) + int(bool(money))
    vertical_span = bottom - top
    safe = not force_fragment and name_anchor and field_count >= 2
    state = "SafeForReview" if safe else "FragmentOnly"
    candidate_type = "NmReviewRow" if safe else "UnassignedSourceFragment"
    return {
        "candidateType": candidate_type,
        "structuredPayload": {
            "sourceRow": f"pilot-{page}-{serial}", "recordedPersonText": person,
            "rawKhasrasText": raw_khasra, "rawAreaText": areas[0] if areas else None,
            "rawShareText": None, "entitlementAmount": money.group(1).replace(",", "") if money else None,
            "entitlementBasisText": None,
            "khasras": [{"rawKhasraText": raw_khasra, "qualifier": "min" if re.search(r"\bmin\b", raw_khasra, re.I) else None,
                         "rawAreaText": areas[0] if areas else None, "rawShareText": None}],
            "groupingState": state,
            "groupingReasons": {"serialAnchor": serial_anchor, "nameAnchor": name_anchor, "fieldCount": field_count, "verticalSpan": round(vertical_span, 1)}
        },
        "page": page,
        "sourceRegion": {"x": left, "y": top, "width": right-left, "height": bottom-top, "rotationDegrees": 90},
        "rawSourceText": text, "rawOcr": text, "normalizedSuggestion": None,
        "normalizationReason": None, "confidence": None,
        "interpretationWarnings": ["Safe source group requires independent serial, name and field anchors; no canonical Khasra is selected."] if safe else ["Unassigned source fragment: evidence was insufficient to form a safe logical NM record."],
    }


_NM_AREA = re.compile(r"^\s*(\d+)\s*[-–—]+\s*(\d+)\s*$")

def nm_token(*args):
    try:
        from benchmark.nm_semantics import NmToken
    except ModuleNotFoundError:
        from nm_semantics import NmToken
    return NmToken(*args)


def normalize_nm_area(value: str | None) -> str | None:
    if value is None:
        return None
    match = _NM_AREA.fullmatch(value)
    return None if match is None else f"{match.group(1)}-{match.group(2)}"


def targeted_area_from_words(page: int, words, left: int, top: int):
    """Return one Area observation only when the crop contains no competitor."""
    normalized = [(word, normalize_nm_area(word.text)) for word in words]
    valid = [(word, value) for word, value in normalized if value is not None]
    numeric = [word for word in words if any(char.isdigit() for char in word.text)]
    if len(valid) == 1 and len(numeric) == 1:
        word, value = valid[0]
        return word.text, nm_token(page, word.text, word.bounding_box.x + left, word.bounding_box.y + top, word.bounding_box.width, word.bounding_box.height, word.confidence), word.confidence
    # Split reassembly is safe only for one number, one dash, one number.
    if len(words) == 3 and re.fullmatch(r"\d+", words[0].text) and re.fullmatch(r"[-–—]+", words[1].text) and re.fullmatch(r"\d+", words[2].text):
        raw = " ".join(word.text for word in words)
        value = normalize_nm_area(raw)
        if value is not None:
            first, last = words[0], words[-1]
            confidence = min(word.confidence for word in words)
            return raw, nm_token(page, raw, first.bounding_box.x + left, first.bounding_box.y + top, last.bounding_box.x + last.bounding_box.width - first.bounding_box.x, max(word.bounding_box.height for word in words), confidence), confidence
    return None


def targeted_area_crop_bounds(parcel, row_parcels, schema, image):
    area = next(((left, right) for role, left, right in schema.bands if role == "area"), None)
    if area is None or parcel.khasra_token is None:
        return None
    rows = sorted((item.khasra_token for item in row_parcels if item.khasra_token is not None), key=lambda token: token.y + token.height / 2)
    current = parcel.khasra_token
    index = rows.index(current)
    centre = current.y + current.height / 2
    previous = rows[index - 1].y + rows[index - 1].height / 2 if index else None
    following = rows[index + 1].y + rows[index + 1].height / 2 if index + 1 < len(rows) else None
    top = current.y - 6 if previous is None else (previous + centre) / 2 - 6
    bottom = current.y + current.height + 6 if following is None else (centre + following) / 2 + 6
    # A six-pixel source-space pad preserves punctuation touching a printed
    # column rule without reaching an adjacent semantic column.
    left, right = max(0, int(area[0] - 6)), min(image.width, int(area[1] + 6))
    return left, max(0, int(top)), right, min(image.height, int(bottom))


def recover_missing_nm_areas(page: int, blocks, schema, image, ocr):
    for block in blocks:
        for parcel in block.parcels:
            if parcel.raw_area is not None or parcel.inherited:
                continue
            bounds = targeted_area_crop_bounds(parcel, block.parcels, schema, image)
            if bounds is None:
                continue
            left, top, right, bottom = bounds
            if right <= left or bottom <= top:
                continue
            recovered = targeted_area_from_words(page, ocr_words(ocr(image.crop((left, top, right, bottom)))), left, top)
            if recovered is not None:
                parcel.raw_area, parcel.area_token, parcel.area_confidence = recovered
                parcel.area_extraction_method = "TargetedAreaCropOcr"
        if all(parcel.inherited or parcel.raw_area is not None for parcel in block.parcels):
            block.exceptions = [reason for reason in block.exceptions if reason != "ParcelAreaMismatch"]


def nm_semantic_candidates(page: int, words, image_width: int, image=None, ocr=None) -> list[dict]:
    """Emit staging-only semantic observations. Missing schema is an exception."""
    try:
        from benchmark.nm_semantics import NmToken, detect_column_schema, semantic_owner_blocks
    except ModuleNotFoundError:
        # The benchmark package is also available in a developer checkout.
        # Keep the semantic module local to the worker when running directly.
        import sys
        sys.path.insert(0, str(Path(__file__).parent / "benchmark"))
        from nm_semantics import NmToken, detect_column_schema, semantic_owner_blocks
    tokens = [NmToken(page, word.text, word.bounding_box.x, word.bounding_box.y, word.bounding_box.width, word.bounding_box.height, getattr(word, "confidence", None)) for word in words]
    schema = detect_column_schema(page, tokens, image_width)
    if schema is None:
        return [{"candidateType": "NmSemanticException", "structuredPayload": {"reason": "PageSchemaMissing", "detail": "Printed column anchors were insufficient; no owner block was inferred."}, "page": page, "sourceRegion": None, "rawSourceText": None, "rawOcr": None, "normalizedSuggestion": None, "normalizationReason": None, "confidence": None, "interpretationWarnings": ["Page retained as semantic exception; no canonical or review row was created."]}]
    grouped = {}
    for token in tokens:
        role = schema.column_for(token)
        if role is None:
            continue
        # OCR word heights vary within a printed row.  Bucket by a fixed
        # source-space tolerance so adjacent register rows are never merged
        # merely because their glyph heights differ.
        key = (role, round(token.y / 12))
        grouped.setdefault(key, []).append(token)
    semantic_tokens = []
    for (role, _), group in grouped.items():
        group.sort(key=lambda item: item.x)
        first = group[0]
        confidences = [item.confidence for item in group if item.confidence is not None]
        semantic_tokens.append(NmToken(page, " ".join(item.text for item in group), first.x, min(item.y for item in group), max(item.right for item in group) - first.x, max(item.height for item in group), min(confidences) if confidences else None))
    blocks = semantic_owner_blocks(semantic_tokens, schema)
    if image is not None and ocr is not None:
        recover_missing_nm_areas(page, blocks, schema, image, ocr)
    output = []
    for block in blocks:
        box = {"x": min(token.x for token in block.source_tokens), "y": min(token.y for token in block.source_tokens), "width": max(token.right for token in block.source_tokens) - min(token.x for token in block.source_tokens), "height": max(token.y + token.height for token in block.source_tokens) - min(token.y for token in block.source_tokens), "rotationDegrees": 90}
        source = lambda token: None if token is None else {"page": token.page, "rawSourceText": token.text, "sourceRegion": token.region()}
        payload = {"sourceSequence": block.sequence, "recordedNameRaw": block.recorded_name, "fatherOrSpouseRaw": block.parentage, "residenceRaw": block.residence, "shareRaw": block.share_raw, "fieldSources": {"recordedName": source(block.name_token), "fatherOrSpouse": source(block.parentage_token), "residence": source(block.residence_token), "share": source(block.share_token)}, "status": "AutoStructured" if block.auto_structured else "Exception", "parcelCountAsRecorded": block.parcel_count_as_recorded, "totalAreaAsRecorded": block.total_area_as_recorded, "parcels": [{"rawKhasraText": parcel.raw_khasra, "rawAreaText": parcel.raw_area, "normalizedAreaText": normalize_nm_area(parcel.raw_area), "areaExtractionMethod": parcel.area_extraction_method, "areaOcrConfidence": parcel.area_confidence, "landClassRaw": parcel.land_class, "isInherited": parcel.inherited, "khasraSource": source(parcel.khasra_token), "areaSource": source(parcel.area_token), "landClassSource": source(parcel.land_class_token)} for parcel in block.parcels], "components": {role: {"rawAmountText": value, "source": source(token)} for role, (value, token) in block.components.items()}, "relations": [] if block.ditto_token is None else [{"relationType": "ParcelInheritedFromPreviousOwnerBlock", "relatedSourceSequence": block.inherited_from_sequence, "marker": source(block.ditto_token)}], "exceptions": block.exceptions, "schema": schema.confidence}
        output.append({"candidateType": "NmSemanticOwnerBlock", "structuredPayload": payload, "page": page, "sourceRegion": box, "rawSourceText": " ".join(token.text for token in block.source_tokens), "rawOcr": None, "normalizedSuggestion": None, "normalizationReason": None, "confidence": None, "interpretationWarnings": ["Semantic staging only; canonical NM facts are not created."]})
    if not output:
        output.append({"candidateType": "NmSemanticException", "structuredPayload": {"reason": "OwnerUnreadable", "detail": "A page schema was found but no coherent owner block could be isolated."}, "page": page, "sourceRegion": None, "rawSourceText": None, "rawOcr": None, "normalizedSuggestion": None, "normalizationReason": None, "confidence": None, "interpretationWarnings": ["Page retained as semantic exception."]})
    return output


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--crop", action="store_true")
    parser.add_argument("--pdf", type=Path)
    parser.add_argument("--page", type=int)
    parser.add_argument("--region")
    parser.add_argument("--pages", help="Comma-separated one-based pages for local diagnostic runs")
    parser.add_argument("--disable-court", action="store_true", help="Diagnostic only: retain perception but skip Court interpretation")
    args = parser.parse_args()
    if args.crop:
        if not args.pdf or not args.pdf.is_file() or args.pdf.suffix.lower() != ".pdf" or not args.region or not args.page or args.page < 1:
            return fail("invalid local crop request")
        try:
            import fitz
            from PIL import Image
            box = {str(key).lower(): value for key, value in json.loads(args.region).items()}
            x, y, width, height = (float(box[key]) for key in ("x", "y", "width", "height"))
            if min(x, y) < 0 or width <= 0 or height <= 0 or max(x + width, y + height, width, height) > 20000:
                return fail("invalid local crop region")
            document = fitz.open(args.pdf)
            if args.page > len(document):
                return fail("local crop page out of bounds")
            pixmap = document[args.page - 1].get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
            image = Image.frombytes("RGB", [pixmap.width, pixmap.height], pixmap.samples)
            # Worker geometry is measured against this same 2x page raster.
            padding = 12
            left, top = max(0, int(x) - padding), max(0, int(y) - padding)
            right, bottom = min(image.width, int(x + width) + padding), min(image.height, int(y + height) + padding)
            if right <= left or bottom <= top:
                return fail("local crop region outside raster")
            args.output.parent.mkdir(parents=True, exist_ok=True)
            image.crop((left, top, right, bottom)).save(args.output, "PNG")
            return 0
        except Exception as error:
            return fail(f"local crop failed: {type(error).__name__}: {str(error)[:160]}")
    if args.input is None:
        return fail("worker input JSON is required")
    try:
        data = json.loads(args.input.read_text(encoding="utf-8"))
    except Exception:
        return fail("invalid worker input JSON")
    if data.get("contractVersion") != CONTRACT_VERSION:
        return fail("unsupported contract version")
    pdf = Path(data.get("filePath", ""))
    if not pdf.is_file() or pdf.suffix.lower() != ".pdf":
        return fail("local PDF not found")

    try:
        import fitz
        from PIL import Image
        from rapidocr import EngineType, RapidOCR
        from benchmark.normalized import BoundingBox
        from benchmark.table_transformer_geometry import TableTransformerGeometry
        from benchmark.worker_semantics import page_likely_has_table

        started = time.perf_counter()
        stages = {key: 0.0 for key in ("pageRendering", "rapidOcr", "tableLayout", "awardKhasraInterpretation", "valuationCompensationInterpretation", "possessionInterpretation", "courtNarrativeInterpretation", "courtTableInterpretation", "selectiveCellOcr", "serialization")}
        counters = {key: 0 for key in ("pageOcrCalls", "tableTransformerCalls", "cellOcrCalls", "cellOcrCacheHits", "courtTableRowsInspected", "headerFallbackInvocations", "geometryTables", "tablesWithoutGridRows", "tablesWithGridRows", "tablesUnclassified", "courtHeaderSignals", "khasraHeaderSignals")}
        counters["selectiveCellOcrSeconds"] = 0.0
        ocr = RapidOCR(params={"Det.engine_type": EngineType.TORCH, "Cls.engine_type": EngineType.TORCH, "Rec.engine_type": EngineType.TORCH})
        document = fitz.open(pdf)
        selected_pages = list(range(1, len(document) + 1))
        configured_pages = data.get("selectedPages")
        if configured_pages:
            selected_pages = sorted({int(value) for value in configured_pages})
        if args.pages:
            selected_pages = sorted({int(value.strip()) for value in args.pages.split(",") if value.strip()})
            if not selected_pages or min(selected_pages) < 1 or max(selected_pages) > len(document):
                return fail("selected pages are outside the local PDF")
        geometry_engine = None
        candidates: list[dict] = []
        table_pages = 0
        totals = {"tables": 0, "awardRows": 0, "courtRows": 0, "classificationRows": 0}

        for page_number in selected_pages:
            pdf_page = document[page_number - 1]
            stage_started = time.perf_counter()
            # Pilot staging needs broad source bands, not production table cells.
            # A 1x raster keeps the nine-page local NM pilot responsive.
            raster_scale = 1 if data.get("options", {}).get("nmPilot") else 2
            pixmap = pdf_page.get_pixmap(matrix=fitz.Matrix(raster_scale, raster_scale), alpha=False)
            image = Image.frombytes("RGB", [pixmap.width, pixmap.height], pixmap.samples)
            if data.get("options", {}).get("nmPilot") or data.get("options", {}).get("nmSemantic"):
                # NM scans are sideways. Rotation is the only permitted
                # normalization in this pilot; no page-to-page registration.
                image = image.rotate(90, expand=True)
            stages["pageRendering"] += time.perf_counter() - stage_started
            stage_started = time.perf_counter()
            output = ocr(image)
            counters["pageOcrCalls"] += 1
            stages["rapidOcr"] += time.perf_counter() - stage_started
            words = ocr_words(output)

            if data.get("options", {}).get("nmSemantic"):
                # Semantic NM is a separate staging protocol. It receives the
                # same populated OCR token stream as every other worker mode.
                candidates.extend(nm_semantic_candidates(page_number, words, image.width, image, ocr))
                continue

            page_candidates: list[dict] = []
            if data.get("options", {}).get("nmPilot"):
                # NM pilot mode deliberately avoids Table Transformer and every
                # rejected registration strategy. It emits review staging only.
                candidates.extend(nm_pilot_candidates(page_number, words, image.width, image.height))
                continue
            # Core/header and statutory suggestions are label-led narrative
            # extraction.  They deliberately do not depend on Award table
            # geometry and cannot influence Khasra interpretation.
            stage_started = time.perf_counter()
            page_candidates.extend(narrative_core_and_statutory_candidates(page_number, words))
            stages["awardKhasraInterpretation"] += time.perf_counter() - stage_started
            stage_started = time.perf_counter()
            page_candidates.extend(valuation_and_compensation_candidates(page_number, words))
            stages["valuationCompensationInterpretation"] += time.perf_counter() - stage_started
            stage_started = time.perf_counter()
            page_candidates.extend(possession_candidates(page_number, words))
            stages["possessionInterpretation"] += time.perf_counter() - stage_started
            stage_started = time.perf_counter()
            if not args.disable_court:
                page_candidates.extend(court_case_candidates(page_number, words))
            stages["courtNarrativeInterpretation"] += time.perf_counter() - stage_started
            if page_likely_has_table([word.text for word in words]):
                if geometry_engine is None:
                    geometry_engine = TableTransformerGeometry()
                stage_started = time.perf_counter()
                geometry = geometry_engine.detect(image)
                counters["tableTransformerCalls"] += 1
                stages["tableLayout"] += time.perf_counter() - stage_started
                crop_cache: dict[tuple[float, float, float, float], str | None] = {}
                geometry_candidates, page_counts = structured_from_geometry(page_number, geometry, words, image, ocr, crop_cache, counters, not args.disable_court)
                # Court parsing is pure same-row interpretation. Crop OCR is
                # timed at the call site, so geometry parsing is not falsely
                # attributed to either stage.
                stages["courtTableInterpretation"] += page_counts["courtTableSeconds"]
                page_candidates.extend(geometry_candidates)
                if page_counts["tables"]:
                    table_pages += 1
                    for key in totals:
                        totals[key] += page_counts[key]
            candidates.extend(page_candidates)
            if words:
                candidates.append({
                    "candidateType": "UnmappedAwardFinding",
                    "structuredPayload": {"category": "Local OCR narrative", "summary": "Page OCR retained outside geometry-backed structured rows"},
                    "page": page_number,
                    "sourceRegion": {"x": 0, "y": 0, "width": image.width, "height": image.height},
                    "rawSourceText": "",
                    "rawOcr": None,
                    "normalizedSuggestion": None,
                    "normalizationReason": None,
                    "confidence": None,
                    "interpretationWarnings": ["Narrative evidence retained; not promoted without table geometry"],
                })

        stages["selectiveCellOcr"] = counters["selectiveCellOcrSeconds"]
        stage_started = time.perf_counter()
        result = {
            "contractVersion": CONTRACT_VERSION,
            "documentId": data["documentId"],
            "status": "Completed",
            "pagesProcessed": len(selected_pages),
            "candidates": candidates,
            "warnings": ["Structured candidates require detected table geometry, header roles, and human review."],
            "metrics": {"runtimeSeconds": round(time.perf_counter() - started, 2), "engine": "RapidOCR local source-band grouping" if data.get("options", {}).get("nmPilot") else "RapidOCR local + Table Transformer geometry", "pagesSelected": len(selected_pages), "courtInterpretationEnabled": not args.disable_court, "tablePagesDetected": table_pages, **totals, "stageSeconds": {}, **counters},
        }
        stages["serialization"] += time.perf_counter() - stage_started
        args.output.parent.mkdir(parents=True, exist_ok=True)
        stage_started = time.perf_counter()
        result["metrics"]["stageSeconds"] = {key: round(value, 3) for key, value in stages.items()}
        json.dumps(result)
        stages["serialization"] += time.perf_counter() - stage_started
        result["metrics"]["stageSeconds"] = {key: round(value, 3) for key, value in stages.items()}
        args.output.write_text(json.dumps(result), encoding="utf-8")
        return 0
    except Exception as error:
        return fail(f"local worker failed: {type(error).__name__}: {str(error)[:240]}")


if __name__ == "__main__":
    sys.exit(main())
