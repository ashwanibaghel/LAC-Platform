"""Join local RapidOCR word boxes to Table Transformer geometry without guessing."""
from __future__ import annotations

import argparse
import json
from dataclasses import asdict
from pathlib import Path

from .normalized import BoundingBox, Word
from .table_geometry import join_words_to_grid


def box(value: dict) -> BoundingBox:
    return BoundingBox(float(value["x"]), float(value["y"]), float(value["width"]), float(value["height"]))


def word(value: dict) -> Word:
    points = value["box"]
    xs, ys = [float(point[0]) for point in points], [float(point[1]) for point in points]
    return Word(str(value["txt"]), BoundingBox(min(xs), min(ys), max(xs) - min(xs), max(ys) - min(ys)), float(value["score"]))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--geometry", required=True, type=Path)
    parser.add_argument("--ocr", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    geometry = json.loads(args.geometry.read_text(encoding="utf-8"))["geometry"]
    words = [word(item) for item in json.loads(args.ocr.read_text(encoding="utf-8"))]
    tables = [item for item in geometry if item["label"] == "table"]
    if not tables:
        raise ValueError("No table bounding box detected")

    reports = []
    for index, detected_table in enumerate(sorted(tables, key=lambda item: item["score"], reverse=True)):
        table_box = box(detected_table["box"])
        def inside_table(item: dict) -> bool:
            candidate = box(item["box"])
            centre_x, centre_y = candidate.x + candidate.width / 2, candidate.y + candidate.height / 2
            return table_box.x <= centre_x <= table_box.x + table_box.width and table_box.y <= centre_y <= table_box.y + table_box.height
        rows = sorted((box(item["box"]) for item in geometry if item["label"] == "table row" and inside_table(item)), key=lambda item: item.y)
        columns = sorted((box(item["box"]) for item in geometry if item["label"] == "table column" and inside_table(item)), key=lambda item: item.x)
        table, uncertain = join_words_to_grid(table_box=table_box, row_boxes=rows, column_boxes=columns, words=words)
        reports.append({
            "table_index": index,
            "rows": table.rows,
            "columns": table.columns,
            "populated_cells": len(table.cells),
            "assigned_words": sum(len(cell.text.split()) for cell in table.cells),
            "uncertain_words": len(uncertain),
            "table": {
                "bounding_box": table_box.__dict__,
                "cells": [asdict(cell) for cell in table.cells],
            },
        })
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"tables": reports}, indent=2), encoding="utf-8")
    print(json.dumps({
        "tables": [{key: value for key, value in report.items() if key != "table"} for report in reports]
    }, indent=2))


if __name__ == "__main__":
    main()
