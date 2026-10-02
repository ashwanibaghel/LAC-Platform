"""Offline deterministic audited JSONL export. No artifact/model/workbook promotion."""
import json
import hashlib
from annotations.seed_review import MATTERS, SOURCES, QUARANTINED, REVIEW
from foundation import ROOT, reviewed_examples, write_json, write_jsonl, evidence
from schema.contracts import EXAMPLE, ANCHOR_SCHEMA, ANSWER_SCHEMA, TASKS
from build_splits import build, SPLITS
from dataset_stats import statistics
from validate_gold import validate_example


def export(output=None):
    output = output or ROOT / "generated"
    examples = reviewed_examples()
    for example in examples:
        validate_example(example)
    inventory = json.loads((ROOT / "annotations/split_inventory.json").read_text(encoding="utf-8"))
    previous_path = output / "splits.json"
    previous = json.loads(previous_path.read_text(encoding="utf-8")) if previous_path.exists() else None
    splits = build(examples, MATTERS, inventory, previous)
    write_json(output / "splits.json", splits)
    for split in SPLITS:
        write_jsonl(output / f"{split}.jsonl", [e for e in examples if splits["assignments"][e["id"]] == split])
    write_json(output / "stats.json", statistics(examples, splits))
    write_json(output / "source_versions.json", {key: {**value, "review": REVIEW,
        "passages": [evidence(pid) for pid in sorted({p["passage_id"] for e in examples for p in e["provenance"] if p["source_version_id"] == key})]}
        for key, value in sorted(SOURCES.items())})
    write_json(output / "quarantined.json", QUARANTINED)
    write_json(output / "example.schema.json", EXAMPLE)
    write_json(output / "target.schemas.json", {"tasks": TASKS, "anchors": ANCHOR_SCHEMA, "claims": ANSWER_SCHEMA})
    payloads = ["splits.json", "stats.json", "source_versions.json", "quarantined.json", "example.schema.json", "target.schemas.json"] + [s + ".jsonl" for s in SPLITS]
    write_json(output / "manifest.json", {
        "schema_version": 1, "annotation_version": REVIEW["annotation_version"],
        "source_branch": "codex/court-intelligence-training-v1",
        "product_baseline": "0cea02e9b3c1d0fa7623ffb1f69e9681fa71279e",
        "model": json.loads((ROOT / "model_metadata.json").read_text(encoding="utf-8")),
        "reviewed_annotations_sha256": hashlib.sha256((ROOT / "annotations/seed_review.py").read_bytes()).hexdigest(),
        "files": {name: hashlib.sha256((output / name).read_bytes()).hexdigest() for name in payloads},
        "no_pristine_evaluation_yet": True, "private_dataset_default": True,
        "excluded": ["office workbook/private inventory", "PDF binaries", "inference artifacts", "unreviewed/quarantined/rejected examples", "weights", "secrets"],
        "source_reproduction": "Each bounded passage belongs to exact audited binary SHA; a changed download is a new version and cannot substitute for this version."})
    return output


if __name__ == "__main__":
    print(f"Exported reviewed development-only seed to {export()}")
