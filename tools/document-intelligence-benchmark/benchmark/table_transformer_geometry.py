"""Reusable local Table Transformer geometry for benchmark and worker callers."""
from __future__ import annotations

import os

os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
os.environ.setdefault("DO_NOT_TRACK", "1")

DETECTOR_ID = "microsoft/table-transformer-detection"
STRUCTURE_ID = "microsoft/table-transformer-structure-recognition-v1.1-all"


def complete_resize(processor) -> None:
    if set(processor.size) == {"longest_edge"}:
        processor.size = {"shortest_edge": 800, "longest_edge": processor.size["longest_edge"]}


def infer(processor, model, image, torch, threshold: float) -> list[dict]:
    inputs = processor(images=image, return_tensors="pt")
    outputs = model(**inputs)
    result = processor.post_process_object_detection(
        outputs, threshold=threshold, target_sizes=torch.tensor([image.size[::-1]])
    )[0]
    return [
        {
            "label": model.config.id2label[int(label)],
            "score": round(float(score), 5),
            "box": {"x": left, "y": top, "width": right - left, "height": bottom - top},
        }
        for label, score, (left, top, right, bottom) in zip(
            result["labels"].tolist(), result["scores"].tolist(), result["boxes"].tolist()
        )
    ]


class TableTransformerGeometry:
    """Lazy local models, shared by a conservative sequential page worker."""

    def __init__(self) -> None:
        import torch
        from transformers import AutoImageProcessor, TableTransformerForObjectDetection

        self.torch = torch
        self.detector_processor = AutoImageProcessor.from_pretrained(DETECTOR_ID)
        self.structure_processor = AutoImageProcessor.from_pretrained(STRUCTURE_ID)
        complete_resize(self.detector_processor)
        complete_resize(self.structure_processor)
        self.detector = TableTransformerForObjectDetection.from_pretrained(DETECTOR_ID)
        self.structurer = TableTransformerForObjectDetection.from_pretrained(STRUCTURE_ID)
        self.detector.eval()
        self.structurer.eval()

    def detect(self, image, threshold: float = 0.7) -> list[dict]:
        geometry: list[dict] = []
        with self.torch.inference_mode():
            detected = infer(self.detector_processor, self.detector, image, self.torch, threshold)
            for table in detected:
                if table["label"] != "table":
                    continue
                x, y = max(0, int(table["box"]["x"])), max(0, int(table["box"]["y"]))
                right = min(image.width, int(table["box"]["x"] + table["box"]["width"]))
                bottom = min(image.height, int(table["box"]["y"] + table["box"]["height"]))
                if right <= x or bottom <= y:
                    continue
                geometry.append({"label": "table", "score": table["score"], "box": {"x": x, "y": y, "width": right - x, "height": bottom - y}})
                for item in infer(self.structure_processor, self.structurer, image.crop((x, y, right, bottom)), self.torch, 0.5):
                    if item["label"] == "table":
                        continue
                    item["box"]["x"] += x
                    item["box"]["y"] += y
                    geometry.append(item)
        return geometry
