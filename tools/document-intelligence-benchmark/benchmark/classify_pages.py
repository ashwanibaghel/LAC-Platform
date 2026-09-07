"""Conservative local page classification from OCR text; no record creation."""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


RULES = (
    ("CWP/court table", (r"\bcwp\b", r"status quo", r"court")),
    ("claims", (r"claim and evidence", r"name of claimant", r"\bclaim\b")),
    ("land classification", (r"classification of land", r"\bblock\b")),
    ("Award Khasra table", (r"khasra", r"total area", r"area awarded")),
    ("area reconciliation", (r"corrigendum", r"clerical mistake", r"field book", r"difference")),
    ("valuation/compensation", (r"market value", r"compensation", r"solatium", r"interest")),
    ("possession", (r"possession",)),
    ("notification", (r"notification", r"u/s")),
)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--ocr", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    pages = []
    for path in sorted(args.ocr.glob("page-*.raw.json"), key=lambda item: int(re.search(r"(\d+)", item.name).group(1))):
        values = json.loads(path.read_text(encoding="utf-8"))
        text = "\n".join(str(item.get("txt", "")) for item in values)
        concepts = [name for name, patterns in RULES if any(re.search(pattern, text, re.I) for pattern in patterns)]
        if not concepts:
            concepts = ["narrative" if len(values) < 120 else "unknown"]
        table_signals = sum(bool(re.search(pattern, text, re.I)) for pattern in (r"khasra", r"total area", r"area awarded", r"\bcwp\b", r"claim", r"\bblock\b", r"s\.no"))
        pages.append({
            "page": int(re.search(r"(\d+)", path.name).group(1)),
            "ocr_lines": len(values),
            "characters": sum(len(str(item.get("txt", ""))) for item in values),
            "concepts": concepts,
            "table_candidate": table_signals >= 2 and len(values) >= 30,
        })
    args.output.write_text(json.dumps({"pages": pages}, indent=2), encoding="utf-8")
    print(json.dumps({"pages": len(pages), "table_candidates": [page["page"] for page in pages if page["table_candidate"]]}, indent=2))


if __name__ == "__main__":
    main()
