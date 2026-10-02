"""Separate bounded Pilot gates. T2 smoke assertions remain unchanged."""
import hashlib
import json
from smoke_contract import MODEL, REVISION


def verify_bundle(path):
    path = path.resolve()
    manifest = json.loads((path / "dataset-manifest.json").read_text())
    if manifest["purpose"] != ["PILOT_V1", "BOUNDED_EVALUATION", "NO_DEPLOYMENT"] or not manifest["private"]:
        raise ValueError("Reviewed private pilot bundle required")
    for relative, sha in manifest["files"].items():
        file = (path / relative).resolve()
        if not file.is_relative_to(path) or hashlib.sha256(file.read_bytes()).hexdigest() != sha:
            raise ValueError("Pilot integrity/path failed")
    sets = {name: [json.loads(line) for line in (path / (name + ".jsonl")).read_text().splitlines()]
            for name in ("train", "validation", "blind")}
    seen = set()
    for name, records in sets.items():
        matters = {r["matter_id"] for r in records}
        if seen & matters:
            raise ValueError("Matter leakage")
        seen |= matters
        if len(records) != manifest["counts"][name]:
            raise ValueError("Pilot count mismatch")
    return sets["train"], manifest


def verify_config(config):
    if config["model_id"] != MODEL or config["revision"] != REVISION:
        raise ValueError("Wrong pinned base")
    if not 12 < config["max_steps"] <= 120 or not 0 < config["checkpoint_probe_steps"] < config["max_steps"]:
        raise ValueError("Unbounded pilot steps")
    if config["batch_size"] != 1 or config["max_sequence_length"] != 2048 or config["gradient_accumulation_steps"] != 4:
        raise ValueError("Unreviewed memory configuration")
    if config["quantization"] != "4bit-nf4-double-quant" or not config["gradient_checkpointing"] or config["lora_r"] != 8:
        raise ValueError("Unreviewed QLoRA setup")
    if not 0 < config["learning_rate"] <= 0.00005 or config["max_new_tokens"] > 512:
        raise ValueError("Unsafe pilot optimizer/generation bound")
    return config
