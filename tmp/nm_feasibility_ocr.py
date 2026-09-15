"""Disposable, read-only NM printed-layer feasibility measurement."""
from __future__ import annotations

from collections import defaultdict
from pathlib import Path
import json
import re
import time

from PIL import Image
from rapidocr import EngineType, RapidOCR


ROOT = Path(r"C:\LAC-Platform\tmp\nm-feasibility")
PAGES = (1, 10, 20, 30, 40, 50, 60, 70, 75)
VARIANT_PAGES = (1, 20, 50)
VARIANTS = ("original", "dark-low-saturation", "adaptive-grayscale")


def words_for(image_path: Path, engine: RapidOCR) -> tuple[list[dict], float]:
    image = Image.open(image_path).convert("RGB")
    started = time.perf_counter()
    output = engine(image)
    elapsed = time.perf_counter() - started
    words = []
    texts = list(output.txts) if output.txts is not None else []
    boxes = list(output.boxes) if output.boxes is not None else []
    scores = list(output.scores) if output.scores is not None else []
    for text, box, score in zip(texts, boxes, scores):
        raw = " ".join(str(text).split())
        if not raw:
            continue
        xs, ys = [float(point[0]) for point in box], [float(point[1]) for point in box]
        words.append({"text": raw, "confidence": round(float(score), 4),
                      "box": {"x": min(xs), "y": min(ys), "width": max(xs) - min(xs), "height": max(ys) - min(ys)}})
    return words, elapsed


def metric(words: list[dict], elapsed: float) -> dict:
    joined = " ".join(word["text"].lower() for word in words)
    header_hits = sum(needle in joined for needle in ("name", "owner", "khasra", "area", "total", "compensation"))
    numeric_tokens = sum(bool(re.search(r"\d", word["text"])) for word in words)
    return {"wordCount": len(words), "meanConfidence": round(sum(word["confidence"] for word in words) / len(words), 3) if words else 0,
            "headerSignals": header_hits, "numericTokens": numeric_tokens, "ocrSeconds": round(elapsed, 2)}


def broad_record_bands(words: list[dict], page_height: int) -> list[dict]:
    # This is deliberately broad/diagnostic. It only uses the serial column and
    # local Y order; it does not reconstruct form rows or use fixed templates.
    serials = []
    for word in words:
        text, box = word["text"], word["box"]
        if box["x"] < 95 and box["y"] > 135 and re.fullmatch(r"\d{1,3}", text):
            serials.append((box["y"], text))
    starts = []
    for y, text in sorted(serials):
        if not starts or y - starts[-1][0] > 45:
            starts.append((y, text))
    bands = []
    for index, (start, serial) in enumerate(starts):
        end = starts[index + 1][0] - 1 if index + 1 < len(starts) else page_height - 35
        if end - start < 45:
            continue
        members = [word for word in words if start - 16 <= word["box"]["y"] < end]
        bands.append({"serialOcr": serial, "yStart": round(start, 1), "yEnd": round(end, 1),
                      "wordCount": len(members), "tokens": members})
    return bands


def main() -> None:
    engine = RapidOCR(params={"Det.engine_type": EngineType.TORCH, "Cls.engine_type": EngineType.TORCH, "Rec.engine_type": EngineType.TORCH})
    result = {"variantComparison": {}, "pages": {}}
    for page in VARIANT_PAGES:
        comparison = {}
        for variant in VARIANTS:
            words, elapsed = words_for(ROOT / f"page-{page}" / f"{variant}.png", engine)
            comparison[variant] = metric(words, elapsed)
        result["variantComparison"][str(page)] = comparison
    for page in PAGES:
        image_path = ROOT / f"page-{page}" / "original.png"
        words, elapsed = words_for(image_path, engine)
        width, height = Image.open(image_path).size
        result["pages"][str(page)] = {"metric": metric(words, elapsed), "width": width, "height": height,
                                       "words": words, "broadRecordBands": broad_record_bands(words, height)}
    (ROOT / "rapidocr-measurement.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    aggregate = {"variantComparison": result["variantComparison"],
                 "pageMetrics": {page: value["metric"] for page, value in result["pages"].items()},
                 "bandsPerPage": {page: len(value["broadRecordBands"]) for page, value in result["pages"].items()}}
    (ROOT / "rapidocr-summary.json").write_text(json.dumps(aggregate, indent=2), encoding="utf-8")
    print(json.dumps({"pages": len(PAGES), "variantPages": len(VARIANT_PAGES), "rawOutput": "local tmp only"}))


if __name__ == "__main__":
    main()
