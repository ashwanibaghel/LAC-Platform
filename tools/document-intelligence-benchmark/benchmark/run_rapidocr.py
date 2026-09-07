"""Run Docling's local RapidOCR dependency as a strictly labelled OCR baseline."""
from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

import fitz


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pdf", required=True, type=Path)
    parser.add_argument("--pages", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    pages = sorted({int(part.strip()) for part in args.pages.split(",") if part.strip()})
    if not pages or any(page < 1 for page in pages):
        raise ValueError("--pages must contain positive one-based page numbers")

    from rapidocr import EngineType, RapidOCR

    args.output.mkdir(parents=True, exist_ok=True)
    document = fitz.open(args.pdf)
    if any(page > document.page_count for page in pages):
        raise ValueError(f"Requested page exceeds {document.page_count}")
    engine = RapidOCR(params={
        "Det.engine_type": EngineType.TORCH,
        "Cls.engine_type": EngineType.TORCH,
        "Rec.engine_type": EngineType.TORCH,
    })
    metrics = []
    for number in pages:
        pixmap = document.load_page(number - 1).get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
        image_path = args.output / f"page-{number}.png"
        pixmap.save(image_path)
        started = time.perf_counter()
        result = engine(str(image_path))
        elapsed = time.perf_counter() - started
        payload = result.to_json()
        (args.output / f"page-{number}.raw.json").write_text(json.dumps(payload, indent=2, default=str), encoding="utf-8")
        texts = result.txts or []
        metrics.append({"page": number, "seconds": round(elapsed, 3), "lines": len(texts), "characters": sum(len(str(text)) for text in texts)})
    document.close()
    summary = {"engine": "RapidOCR local OCR baseline", "pages": metrics}
    (args.output / "metrics.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
