"""Run PP-StructureV3 locally on selected rendered Award pages.

The input PDF never leaves this process. Model downloads are permitted only during
setup; use --offline after the model cache has been populated.
"""
from __future__ import annotations

import argparse
import json
import os
import time
from pathlib import Path

os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
os.environ.setdefault("DO_NOT_TRACK", "1")
os.environ.setdefault("PADDLE_PDX_DISABLE_TELEMETRY", "1")
# Some Windows CPU builds of Paddle 3.x route this document-layout graph through
# oneDNN and fail before inference. Keep the benchmark on the generic CPU path.
os.environ.setdefault("FLAGS_use_mkldnn", "0")
os.environ.setdefault("FLAGS_enable_pir_api", "0")
os.environ.setdefault("PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK", "True")

import fitz  # PyMuPDF

from .interpret import interpret
from .normalized import Block, BoundingBox, DocumentPage


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Offline local PP-StructureV3 benchmark")
    parser.add_argument("--pdf", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--pages", help="Comma-separated one-based pages; omit for all")
    parser.add_argument("--offline", action="store_true", help="Refuse any missing model download")
    parser.add_argument("--profile", choices=["default", "mobile"], default="mobile",
                        help="mobile keeps layout and table structure but avoids server OCR models")
    parser.add_argument("--without-tables", action="store_true",
                        help="Benchmark layout/OCR only when the optional table stack is unavailable")
    parser.add_argument("--ocr-only", action="store_true",
                        help="Fallback baseline: direct PaddleOCR without layout/table modules")
    return parser.parse_args()


def selected_pages(document: fitz.Document, raw: str | None) -> list[int]:
    if not raw:
        return list(range(1, document.page_count + 1))
    values = [int(value.strip()) for value in raw.split(",")]
    if any(value < 1 or value > document.page_count for value in values):
        raise ValueError(f"--pages must be between 1 and {document.page_count}")
    return values


def jsonable(value):
    if hasattr(value, "tolist"):
        return value.tolist()
    if hasattr(value, "item"):
        return value.item()
    if isinstance(value, Path):
        return str(value)
    raise TypeError(f"Cannot serialize {type(value).__name__}")


def result_dict(result):
    value = getattr(result, "json", None)
    if callable(value):
        value = value()
    if value is None:
        value = getattr(result, "_data", result)
    if isinstance(value, str):
        return json.loads(value)
    return value


def text_from_result(payload: object) -> str:
    """Keep this intentionally conservative: raw engine JSON is the source of truth."""
    if isinstance(payload, dict):
        strings: list[str] = []
        for key, value in payload.items():
            if key.lower() in {"rec_texts", "text", "markdown"}:
                if isinstance(value, str):
                    strings.append(value)
                elif isinstance(value, list):
                    strings.extend(str(item) for item in value if isinstance(item, (str, int, float)))
            elif isinstance(value, (dict, list)):
                nested = text_from_result(value)
                if nested:
                    strings.append(nested)
        return "\n".join(strings)
    if isinstance(payload, list):
        return "\n".join(text_from_result(item) for item in payload)
    return ""


def main() -> None:
    args = arguments()
    if not args.pdf.is_file():
        raise FileNotFoundError(args.pdf)
    if args.offline:
        os.environ["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True"
        os.environ["HF_HUB_OFFLINE"] = "1"

    # Import only after offline/telemetry environment is set.
    from paddleocr import PPStructureV3, PaddleOCR

    args.output.mkdir(parents=True, exist_ok=True)
    page_dir = args.output / "pages"
    page_dir.mkdir(exist_ok=True)
    document = fitz.open(args.pdf)
    pages = selected_pages(document, args.pages)
    pipeline_args = dict(
        device="cpu",
        use_doc_orientation_classify=False,
        use_doc_unwarping=False,
        use_textline_orientation=False,
        use_seal_recognition=False,
        use_formula_recognition=False,
        use_chart_recognition=False,
        use_region_detection=False,
        use_table_recognition=not args.without_tables,
    )
    if args.profile == "mobile":
        # This remains PP-StructureV3 with layout and table modules enabled. Only
        # the generic server OCR models are substituted with official mobile ones
        # to make the CPU pilot viable.
        pipeline_args.update(
            layout_detection_model_name="PP-DocLayout-S",
            text_detection_model_name="PP-OCRv5_mobile_det",
            text_recognition_model_name="en_PP-OCRv5_mobile_rec",
        )
        if not args.without_tables:
            pipeline_args["wired_table_structure_recognition_model_name"] = "SLANet_plus"
    else:
        pipeline_args["lang"] = "en"
    if args.ocr_only:
        # Keep this as an explicitly labelled OCR baseline. It does not claim to
        # reconstruct tables; it lets us distinguish OCR runtime health from the
        # PP-StructureV3 layout failure on this Windows CPU build.
        pipeline = PaddleOCR(
            device="cpu",
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False,
            text_detection_model_name="PP-OCRv5_mobile_det",
            text_recognition_model_name="en_PP-OCRv5_mobile_rec",
        )
    else:
        pipeline = PPStructureV3(**pipeline_args)
    normalized: list[DocumentPage] = []
    measurements: list[dict] = []
    for page_number in pages:
        page = document.load_page(page_number - 1)
        pix = page.get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
        image_path = page_dir / f"page-{page_number}.png"
        pix.save(image_path)
        started = time.perf_counter()
        results = list(pipeline.predict(str(image_path)))
        elapsed = time.perf_counter() - started
        raw = [result_dict(result) for result in results]
        (args.output / f"page-{page_number}.raw.json").write_text(json.dumps(raw, default=jsonable, indent=2), encoding="utf-8")
        text = "\n".join(text_from_result(item) for item in raw).strip()
        normalized.append(DocumentPage(page_number, page.rect.width, page.rect.height,
                                      [Block("Other", text, BoundingBox(0, 0, page.rect.width, page.rect.height))]))
        measurements.append({"page": page_number, "seconds": round(elapsed, 3), "raw_result_count": len(raw), "characters": len(text)})

    (args.output / "normalized-pages.json").write_text(json.dumps([item.to_dict() for item in normalized], indent=2), encoding="utf-8")
    candidates = [candidate.__dict__ for candidate in interpret(normalized)]
    (args.output / "experimental-candidates.json").write_text(json.dumps(candidates, indent=2), encoding="utf-8")
    engine = "PaddleOCR direct OCR baseline" if args.ocr_only else "PaddleOCR PP-StructureV3"
    (args.output / "metrics.json").write_text(json.dumps({"engine": engine, "pages": measurements}, indent=2), encoding="utf-8")
    print(json.dumps({"pages": len(pages), "seconds": round(sum(x["seconds"] for x in measurements), 3), "output": str(args.output)}, indent=2))


if __name__ == "__main__":
    main()
