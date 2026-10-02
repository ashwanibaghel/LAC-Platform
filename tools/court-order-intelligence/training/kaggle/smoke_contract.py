"""CPU-testable smoke gates. No model access, training or network at import."""
import hashlib
import json
from pathlib import Path
import sys
import jsonschema

MODEL = "Qwen/Qwen3-4B-Instruct-2507"
REVISION = "cdbee75f17c01a7cc42f958dc650907174af0554"
MARKERS = {"SMOKE_ONLY", "NOT_EVALUATION", "NOT_BLIND", "NOT_QUALITY_EVIDENCE"}


def verify_bundle(path):
    manifest = json.loads((path / "dataset-manifest.json").read_text(encoding="utf-8"))
    if set(manifest["purpose"]) != MARKERS or manifest["private"] is not True:
        raise ValueError("Not an explicitly private non-evaluation smoke bundle")
    for relative, sha in manifest["files"].items():
        file = (path / relative).resolve()
        if not file.is_relative_to(path.resolve()) or hashlib.sha256(file.read_bytes()).hexdigest() != sha:
            raise ValueError("Bundle integrity/path check failed")
    rows = [json.loads(line) for line in (path / "smoke.jsonl").read_text(encoding="utf-8").splitlines()]
    if len(rows) != manifest["example_count"] or any(set(r["purpose"]) != MARKERS for r in rows):
        raise ValueError("Smoke data count/markers changed")
    return rows, manifest


def encode(tokenizer, record, max_length):
    prefix = tokenizer.apply_chat_template(record["messages"][:-1], tokenize=True, add_generation_prompt=True)
    full = tokenizer.apply_chat_template(record["messages"], tokenize=True, add_generation_prompt=False)
    if full[:len(prefix)] != prefix:
        raise ValueError("Pinned chat template assistant boundary is not a token prefix")
    if len(full) > max_length:
        return None  # Report skip; NEVER truncate evidence or expected JSON.
    if len(full) <= len(prefix):
        raise ValueError("No assistant target tokens")
    return {"input_ids": full, "attention_mask": [1] * len(full),
            "labels": [-100] * len(prefix) + full[len(prefix):]}


def parse_runtime_output(text, record, schemas, runtime_dir):
    payload = json.loads(text)  # No extracting JSON from prose or target fallback.
    jsonschema.validate(payload, schemas[record["contract"]])
    if record["contract"] == "anchors":
        sys.path.insert(0, str(runtime_dir))
        from anchors import expand
        anchors = record["input"]["anchors"]
        pages = {}
        for anchor in anchors:
            page = anchor["page"]
            pages[page] = pages.get(page, "") + " " + anchor["text"]
        expand(payload, anchors, pages)
    else:
        entries = record["input"]["availableEvidence"]
        for claim in payload["claims"]:
            if claim["factId"] >= len(entries):
                raise ValueError("Unretrieved fact ID")
    return payload


def verify_config(config):
    if config["model_id"] != MODEL or config["revision"] != REVISION:
        raise ValueError("Exact model/revision required")
    if set(config["purpose"]) != MARKERS:
        raise ValueError("Not a smoke-only config")
    if not 1 <= config["checkpoint_probe_steps"] < config["max_steps"] <= 12:
        raise ValueError("Smoke/checkpoint step bounds violated")
    if config["batch_size"] != 1 or not 256 <= config["max_sequence_length"] <= 2048:
        raise ValueError("Unsafe smoke batch/context")
    if not 1 <= config["gradient_accumulation_steps"] <= 8:
        raise ValueError("Unsafe accumulation")
    if config["oom_sequence_lengths"] != [1024, 512] or config["max_new_tokens"] > 512:
        raise ValueError("Unbounded fallback/generation")
    if (config["quantization"] != "4bit-nf4-double-quant" or
            config["gradient_checkpointing"] is not True or
            config["lora_r"] != 8 or config["lora_alpha"] != 16 or
            config["target_modules"] != ["q_proj", "k_proj", "v_proj", "o_proj"]):
        raise ValueError("Not the reviewed bounded QLoRA smoke configuration")
    return config
