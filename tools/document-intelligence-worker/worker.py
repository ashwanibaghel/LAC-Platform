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


def crop_ocr(image, cell: dict, ocr) -> str | None:
    """Recognition-only unified crop; never used outside Table Transformer cells."""
    from benchmark.cell_crop_pipeline_v9 import normalize_from_page

    _, normalized, _ = normalize_from_page(image, cell["region"])
    output = ocr(normalized)
    texts = list(output.txts) if output.txts is not None else []
    return " ".join(" ".join(str(text).split()) for text in texts if str(text).strip()) or None


def structured_from_geometry(page: int, geometry: list[dict], words, image, ocr) -> tuple[list[dict], dict[str, int]]:
    from benchmark.worker_semantics import award_candidate, award_table_groups, classification_candidate, court_candidate, table_kind

    candidates: list[dict] = []
    counts = {"tables": 0, "awardRows": 0, "courtRows": 0, "classificationRows": 0}
    tables = [item for item in geometry if item["label"] == "table"]
    for table_id, item in enumerate(tables, 1):
        rows, column_count = rows_for_table(geometry, item["box"], words, page, table_id)
        if not rows:
            continue
        header_cells = header_cells_for_table(geometry, item["box"], words)
        kind, roles = table_kind(header_cells)
        if kind is None:
            continue
        header_index = min(rows)
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
                        cells[khasra_column]["cellCropOcr"] = crop_ocr(image, cells[khasra_column], ocr)
                    for role in ("recordedArea", "awardedArea"):
                        column = group[role]
                        if column in cells and normalize_area_evidence(cells[column]["text"])["status"] != "Valid":
                            cells[column]["cellCropOcr"] = crop_ocr(image, cells[column], ocr)
                    candidate = award_candidate(page, table_id, row_id, cells, group)
                    if candidate:
                        candidates.append(candidate)
                        counts["awardRows"] += 1
                continue
            elif kind == "CourtCwpTable":
                candidate = court_candidate(page, table_id, row_id, cells, roles)
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


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--crop", action="store_true")
    parser.add_argument("--pdf", type=Path)
    parser.add_argument("--page", type=int)
    parser.add_argument("--region")
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
        from benchmark.normalized import BoundingBox, Word
        from benchmark.table_transformer_geometry import TableTransformerGeometry
        from benchmark.worker_semantics import page_likely_has_table

        started = time.perf_counter()
        ocr = RapidOCR(params={"Det.engine_type": EngineType.TORCH, "Cls.engine_type": EngineType.TORCH, "Rec.engine_type": EngineType.TORCH})
        document = fitz.open(pdf)
        geometry_engine = None
        candidates: list[dict] = []
        table_pages = 0
        totals = {"tables": 0, "awardRows": 0, "courtRows": 0, "classificationRows": 0}

        for page_number, pdf_page in enumerate(document, 1):
            pixmap = pdf_page.get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
            image = Image.frombytes("RGB", [pixmap.width, pixmap.height], pixmap.samples)
            output = ocr(image)
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

            page_candidates: list[dict] = []
            # Core/header and statutory suggestions are label-led narrative
            # extraction.  They deliberately do not depend on Award table
            # geometry and cannot influence Khasra interpretation.
            page_candidates.extend(narrative_core_and_statutory_candidates(page_number, words))
            page_candidates.extend(valuation_and_compensation_candidates(page_number, words))
            page_candidates.extend(possession_candidates(page_number, words))
            if page_likely_has_table([word.text for word in words]):
                if geometry_engine is None:
                    geometry_engine = TableTransformerGeometry()
                geometry = geometry_engine.detect(image)
                geometry_candidates, page_counts = structured_from_geometry(page_number, geometry, words, image, ocr)
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

        result = {
            "contractVersion": CONTRACT_VERSION,
            "documentId": data["documentId"],
            "status": "Completed",
            "pagesProcessed": len(document),
            "candidates": candidates,
            "warnings": ["Structured candidates require detected table geometry, header roles, and human review."],
            "metrics": {"runtimeSeconds": round(time.perf_counter() - started, 2), "engine": "RapidOCR local + Table Transformer geometry", "tablePagesDetected": table_pages, **totals},
        }
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result), encoding="utf-8")
        return 0
    except Exception as error:
        return fail(f"local worker failed: {type(error).__name__}: {str(error)[:240]}")


if __name__ == "__main__":
    sys.exit(main())
