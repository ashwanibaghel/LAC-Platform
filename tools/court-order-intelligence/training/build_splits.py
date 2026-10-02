"""Reviewed matter-level assignment only; protected splits cannot drift silently."""
from foundation import ROOT
from leakage import groups, require_one_split
import json

SPLITS = ("development", "train", "validation", "blind")


def build(examples, matters, inventory, previous=None):
    grouping = groups(examples, matters)
    assignments = {}
    for example in examples:
        matter = example["matter_id"]
        record = inventory["matters"][matter]
        split = record["split"]
        if split not in SPLITS:
            raise ValueError("Unknown split")
        if matters[matter]["classification"] == "DEVELOPMENT" and split != "development":
            raise ValueError("Previously inspected development matter is not unseen evaluation")
        if split in ("validation", "blind"):
            if not record["frozen"] or record["inspected_after_freeze"]:
                raise ValueError("Protected split unfrozen/contaminated; explicit demotion required")
        assignments[example["id"]] = split
    require_one_split(grouping, assignments)
    result = {"schema_version": 1, "assignments": assignments, "leakage_groups": grouping,
              "matter_inventory": inventory, "protected_evaluation_ready": any(v in ("validation", "blind") for v in assignments.values())}
    if previous is not None:
        for key, split in previous["assignments"].items():
            if split in ("validation", "blind") and (
                    assignments.get(key) != split or previous["leakage_groups"][key] != grouping.get(key)):
                raise ValueError("Frozen protected split/group changed; explicit new reviewed split version required")
    return result


if __name__ == "__main__":
    from annotations.seed_review import MATTERS
    from foundation import reviewed_examples, write_json
    inventory = json.loads((ROOT / "annotations/split_inventory.json").read_text(encoding="utf-8"))
    path = ROOT / "generated/splits.json"
    previous = json.loads(path.read_text(encoding="utf-8")) if path.exists() else None
    write_json(path, build(reviewed_examples(), MATTERS, inventory, previous))
