"""CLI wrapper around the reusable local Table Transformer geometry engine."""
from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

from table_transformer_geometry import TableTransformerGeometry


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--threshold", type=float, default=0.7)
    args = parser.parse_args()

    from PIL import Image

    started = time.perf_counter()
    image = Image.open(args.image).convert("RGB")
    geometry = TableTransformerGeometry().detect(image, args.threshold)
    summary = {
        "engine": "Table Transformer detection plus structure recognition",
        "seconds": round(time.perf_counter() - started, 3),
        "tables_found": sum(item["label"] == "table" for item in geometry),
        "labels": {label: sum(item["label"] == label for item in geometry) for label in sorted({item["label"] for item in geometry})},
    }
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "geometry.json").write_text(json.dumps({"summary": summary, "geometry": geometry}, indent=2), encoding="utf-8")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
