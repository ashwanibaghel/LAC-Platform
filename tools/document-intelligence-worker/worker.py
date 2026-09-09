"""Local-only v1 document worker: RapidOCR words bound to Table Transformer cells."""
from __future__ import annotations

import argparse
import json
import sys
import time
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
            if page_likely_has_table([word.text for word in words]):
                if geometry_engine is None:
                    geometry_engine = TableTransformerGeometry()
                geometry = geometry_engine.detect(image)
                page_candidates, page_counts = structured_from_geometry(page_number, geometry, words, image, ocr)
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
