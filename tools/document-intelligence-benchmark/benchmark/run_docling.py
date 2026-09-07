"""Local-only Docling comparison adapter. No database writes and no hosted inference."""
from __future__ import annotations

import argparse
import json
import os
import time
from pathlib import Path

os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
os.environ.setdefault("DO_NOT_TRACK", "1")


def parse_pages(value: str) -> list[int]:
    pages = sorted({int(part.strip()) for part in value.split(",") if part.strip()})
    if not pages or any(page < 1 for page in pages):
        raise ValueError("--pages must contain one or more positive page numbers")
    return pages


def create_sample_pdf(source: Path, pages: list[int], destination: Path) -> None:
    """Create an ignored local-only PDF containing only the requested source pages."""
    import fitz

    source_document = fitz.open(source)
    try:
        if any(page > source_document.page_count for page in pages):
            raise ValueError(f"Requested page is outside 1-{source_document.page_count}")
        sample_document = fitz.open()
        try:
            for page in pages:
                sample_document.insert_pdf(source_document, from_page=page - 1, to_page=page - 1)
            sample_document.save(destination)
        finally:
            sample_document.close()
    finally:
        source_document.close()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pdf", required=True, type=Path)
    parser.add_argument("--pages", required=True, help="Comma-separated one-based source page numbers")
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    if not args.pdf.is_file():
        raise FileNotFoundError(args.pdf)

    args.output.mkdir(parents=True, exist_ok=True)
    pages = parse_pages(args.pages)
    sample_pdf = args.output / "sample-pages.pdf"
    create_sample_pdf(args.pdf, pages, sample_pdf)

    from docling.document_converter import DocumentConverter

    started = time.perf_counter()
    result = DocumentConverter().convert(str(sample_pdf))
    elapsed = time.perf_counter() - started
    # Raw structured document is intentionally local/ignored; the adapter is kept
    # small until we have proved the engine can complete on pilot hardware.
    (args.output / "document.md").write_text(result.document.export_to_markdown(), encoding="utf-8")
    metrics = {"engine": "Docling", "source_pages": pages, "seconds": round(elapsed, 3)}
    (args.output / "metrics.json").write_text(json.dumps(metrics, indent=2), encoding="utf-8")
    print(json.dumps(metrics, indent=2))


if __name__ == "__main__":
    main()
