"""Counts describe coverage only, never a quality/performance claim."""
from collections import Counter
from annotations.seed_review import MATTERS, PASSAGES, QUARANTINED


def count(values):
    return dict(sorted(Counter(values).items()))


def statistics(examples, splits):
    matters = {e["matter_id"] for e in examples}
    sources = {p["source_version_id"] for e in examples for p in e["provenance"]}
    passages = {p["passage_id"] for e in examples for p in e["provenance"]}
    return {"matters": len(matters), "orders": len(sources), "verified_examples": len(examples),
            "quarantined_examples": len(QUARANTINED),
            "by_task": count(e["task"] for e in examples),
            "by_semantic_role_unique_passages": count(PASSAGES[p][3] for p in passages),
            "office_action": count(e["outcome"] for e in examples if e["task"] == "office_action_detection"),
            "compliance_state": count(e["outcome"] for e in examples if e["task"] == "compliance_state"),
            "by_language": count(e["language"] for e in examples),
            "matter_source_status": count(MATTERS[m]["source_status"] for m in matters),
            "source_sha_count": len({p["sha256"] for e in examples for p in e["provenance"]}),
            "leakage_groups": len(set(splits["leakage_groups"].values())),
            "splits": {s: sum(v == s for v in splits["assignments"].values()) for s in ("development", "train", "validation", "blind")},
            "evaluation_ready": False, "training_started": False}


if __name__ == "__main__":
    import json
    from foundation import ROOT, reviewed_examples, write_json
    write_json(ROOT / "generated/stats.json", statistics(reviewed_examples(), json.loads((ROOT / "generated/splits.json").read_text(encoding="utf-8"))))
