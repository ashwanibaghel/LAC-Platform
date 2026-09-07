"""Targeted local table-structure benchmark for a single rendered page."""
from __future__ import annotations

import argparse
import json
import os
import time
from pathlib import Path

os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
os.environ.setdefault("DO_NOT_TRACK", "1")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", required=True, type=Path)
    parser.add_argument("--output", type=Path, help="Ignored local directory for geometry JSON")
    args = parser.parse_args()
    from PIL import Image
    import torch
    from transformers import AutoImageProcessor, TableTransformerForObjectDetection

    model_id = "microsoft/table-transformer-structure-recognition-v1.1-all"
    started = time.perf_counter()
    processor = AutoImageProcessor.from_pretrained(model_id)
    # The published model config has a legacy null `dilation` field. Newer
    # Transformers validates this as a bool, so make the model's default
    # explicit rather than altering document data or model weights.
    model = TableTransformerForObjectDetection.from_pretrained(model_id, dilation=False)
    # Older Table Transformer processor metadata may retain only longest_edge;
    # Transformers 4.x requires a complete DETR resize specification.
    if set(processor.size) == {"longest_edge"}:
        processor.size = {"shortest_edge": 800, "longest_edge": processor.size["longest_edge"]}
    image = Image.open(args.image).convert("RGB")
    inputs = processor(images=image, return_tensors="pt")
    outputs = model(**inputs)
    result = processor.post_process_object_detection(
        outputs, threshold=0.5, target_sizes=torch.tensor([image.size[::-1]])
    )[0]
    labels = [model.config.id2label[int(label)] for label in result["labels"]]
    summary = {
        "engine": "Table Transformer structure recognition",
        "seconds": round(time.perf_counter() - started, 3),
        "detections": len(labels),
        "labels": {label: labels.count(label) for label in sorted(set(labels))},
    }
    if args.output:
        args.output.mkdir(parents=True, exist_ok=True)
        geometry = []
        for label, score, box in zip(labels, result["scores"].tolist(), result["boxes"].tolist()):
            left, top, right, bottom = box
            geometry.append({
                "label": label,
                "score": round(float(score), 5),
                "box": {"x": left, "y": top, "width": right - left, "height": bottom - top},
            })
        (args.output / "geometry.json").write_text(json.dumps({"summary": summary, "geometry": geometry}, indent=2), encoding="utf-8")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
