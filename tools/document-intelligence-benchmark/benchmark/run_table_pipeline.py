"""Local Table Transformer detection followed by cropped structure recognition."""
from __future__ import annotations

import argparse
import json
import os
import time
from pathlib import Path

os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
os.environ.setdefault("DO_NOT_TRACK", "1")


def complete_resize(processor) -> None:
    if set(processor.size) == {"longest_edge"}:
        processor.size = {"shortest_edge": 800, "longest_edge": processor.size["longest_edge"]}


def infer(processor, model, image, torch, threshold: float) -> list[dict]:
    inputs = processor(images=image, return_tensors="pt")
    outputs = model(**inputs)
    result = processor.post_process_object_detection(
        outputs, threshold=threshold, target_sizes=torch.tensor([image.size[::-1]])
    )[0]
    values = []
    for label, score, coordinates in zip(result["labels"].tolist(), result["scores"].tolist(), result["boxes"].tolist()):
        left, top, right, bottom = coordinates
        values.append({
            "label": model.config.id2label[int(label)],
            "score": round(float(score), 5),
            "box": {"x": left, "y": top, "width": right - left, "height": bottom - top},
        })
    return values


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--threshold", type=float, default=0.7)
    args = parser.parse_args()

    from PIL import Image
    import torch
    from transformers import AutoImageProcessor, TableTransformerForObjectDetection

    started = time.perf_counter()
    detector_id = "microsoft/table-transformer-detection"
    structure_id = "microsoft/table-transformer-structure-recognition-v1.1-all"
    detector_processor = AutoImageProcessor.from_pretrained(detector_id)
    structure_processor = AutoImageProcessor.from_pretrained(structure_id)
    complete_resize(detector_processor)
    complete_resize(structure_processor)
    detector = TableTransformerForObjectDetection.from_pretrained(detector_id, dilation=False)
    structurer = TableTransformerForObjectDetection.from_pretrained(structure_id, dilation=False)
    image = Image.open(args.image).convert("RGB")
    detected = infer(detector_processor, detector, image, torch, args.threshold)
    output_geometry: list[dict] = []
    table_count = 0
    for table in detected:
        if table["label"] != "table":
            continue
        x, y = max(0, int(table["box"]["x"])), max(0, int(table["box"]["y"]))
        right = min(image.width, int(table["box"]["x"] + table["box"]["width"]))
        bottom = min(image.height, int(table["box"]["y"] + table["box"]["height"]))
        if right <= x or bottom <= y:
            continue
        table_count += 1
        output_geometry.append({"label": "table", "score": table["score"], "box": {"x": x, "y": y, "width": right - x, "height": bottom - y}})
        for structure in infer(structure_processor, structurer, image.crop((x, y, right, bottom)), torch, 0.5):
            # The detector's outer box is the sole table identity. The
            # structure model may emit a nested generic table label; keeping it
            # would make the strict joiner treat one table as two candidates.
            if structure["label"] == "table":
                continue
            structure["box"]["x"] += x
            structure["box"]["y"] += y
            output_geometry.append(structure)
    summary = {
        "engine": "Table Transformer detection plus structure recognition",
        "seconds": round(time.perf_counter() - started, 3),
        "tables_found": table_count,
        "labels": {label: sum(1 for item in output_geometry if item["label"] == label) for label in sorted({item["label"] for item in output_geometry})},
    }
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "geometry.json").write_text(json.dumps({"summary": summary, "geometry": output_geometry}, indent=2), encoding="utf-8")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
