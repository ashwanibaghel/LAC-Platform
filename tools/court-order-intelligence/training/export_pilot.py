"""Audit gate and separate Pilot V1 export; frozen T1 files are never written."""
import hashlib
import importlib
import json
from collections import Counter
from pathlib import Path
from annotations import pilot_review as corpus
from foundation import ROOT, compile_example, write_json, write_jsonl
from validate_gold import validate_example
from build_splits import build
from schema.contracts import ANCHOR_SCHEMA, ANSWER_SCHEMA, TASKS
from semantics import dates_in, identity

OUT = ROOT / "pilot-v1"


def prepare_sources():
    sources = {}
    for version, (date, passages) in corpus.REVIEWED.items():
        path = ROOT / "local-private/pilot-source-audit" / (version + ".json")
        record = json.loads(path.read_text(encoding="utf-8"))
        first = record["pages"]["1"]
        if identity(record["case"]) not in identity(first):
            raise ValueError("Exact requested case absent from first-page caption")
        if date not in dates_in(" ".join(record["pages"].values())):
            raise ValueError("Reviewed date absent from native source")
        for page, text, *_ in passages:
            if text not in record["pages"].get(str(page), ""):
                raise ValueError(f"Unbound exact reviewed passage: {version}/{page}")
        sources[version] = {"matter_id": record["case"], "order_date": date,
            "page_count": len(record["pages"]), "sha256": record["sha256"], "url": record["url"],
            "register_status": "Disposed" if version in {"wpc2466-2026", "wpc8404-2024"} or version.startswith("wpc10308-2024") else "Pending",
            "coverage": record["coverage"], "all_native_pages_read": True,
            "reviewed_by": corpus.REVIEW["reviewer"], "pdf_retained": False}
    write_json(corpus.LEDGER, sources)
    importlib.reload(corpus)


def export():
    prepare_sources()
    all_examples = [compile_example(a, corpus) for a in corpus.EXAMPLES]
    examples = [e for e in all_examples if e["state"] == "VERIFIED_GOLD"]
    for example in examples:
        validate_example(example, corpus)
    assignments = {matter: "train" for matter in corpus.MATTERS}
    assignments.update({"W.P.(C) 6384/2024": "validation", "W.P.(C) 11284/2026": "validation",
                        "W.P.(C) 6203/2026": "blind", "W.P.(C) 8404/2024": "blind"})
    inventory = {"version": "pilot-v1-frozen-1", "frozen_on": "2026-10-02",
        "matters": {m: {"split": s, "frozen": True, "inspected_after_freeze": False,
            "source_audited_before_freeze": True} for m, s in assignments.items()},
        "old_development_only": ["W.P.(C) 14604/2025", "W.P.(C) 8664/2021", "W.P.(C) 940/2015"]}
    previous = json.loads((OUT / "splits.json").read_text()) if (OUT / "splits.json").exists() else None
    splits = build(all_examples, corpus.MATTERS, inventory, previous)
    for split in ("train", "validation", "blind"):
        write_jsonl(OUT / (split + ".jsonl"), [e for e in examples if splits["assignments"][e["id"]] == split])
    write_json(OUT / "splits.json", splits)
    write_json(OUT / "target.schemas.json", {"anchors": ANCHOR_SCHEMA, "claims": ANSWER_SCHEMA, "tasks": TASKS})
    stats = {"matters": len(corpus.MATTERS), "orders": len(corpus.SOURCES), "verified_examples": len(examples),
        "excluded_annotations": len(corpus.QUARANTINED), "by_task": dict(Counter(e["task"] for e in examples)),
        "by_language": dict(Counter(e["language"] for e in examples)),
        "register_status_not_current_status": dict(Counter(m["source_status"] for m in corpus.MATTERS.values())),
        "split_matters": dict(Counter(assignments.values())),
        "split_examples": dict(Counter(splits["assignments"][e["id"]] for e in examples)),
        "leakage_groups": len(set(splits["leakage_groups"].values())),
        "complete_order_history_claim": False, "training_started": False,
        "review_is_not_human_legal_certification": True}
    write_json(OUT / "stats.json", stats)
    write_json(OUT / "excluded.json", corpus.QUARANTINED)
    files = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in OUT.iterdir() if p.is_file() and p.name != "manifest.json"}
    write_json(OUT / "manifest.json", {"version": "pilot-v1", "files": files,
        "annotations_sha256": hashlib.sha256(Path(corpus.__file__).read_bytes()).hexdigest(),
        "source_ledger_sha256": hashlib.sha256(corpus.LEDGER.read_bytes()).hexdigest(),
        "private_workbook_included": False, "model_id": "Qwen/Qwen3-4B-Instruct-2507",
        "revision": "cdbee75f17c01a7cc42f958dc650907174af0554"})
    print(json.dumps(stats, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    export()
