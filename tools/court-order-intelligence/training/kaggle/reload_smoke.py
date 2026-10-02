"""Inference-only contract proof from a trusted private T2 kernel output.

No training, output repair, gold-target substitution or product runtime changes.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time


def inference_messages(record, schemas):
    messages = [dict(message) for message in record["messages"][:-1]]
    # HF generate has no runtime grammar parameter. Supply the same exact
    # contract explicitly, never a target/example answer or replacement output.
    messages[0]["content"] += "\nExact output JSON Schema:\n" + json.dumps(
        schemas[record["contract"]], separators=(",", ":"), ensure_ascii=False)
    return messages


def schema_prefix(tokenizer, schema):
    from lmformatenforcer import JsonSchemaParser
    from lmformatenforcer.integrations.transformers import build_transformers_prefix_allowed_tokens_fn
    return build_transformers_prefix_allowed_tokens_fn(tokenizer, JsonSchemaParser(schema))


def main():
    bundles = [p.parent for p in Path("/kaggle/input").rglob("dataset-manifest.json")
               if p.parent.name == "lac-t2-bundle"]
    sources = [p.parent for p in Path("/kaggle/input").rglob("run-metadata.json")
               if p.parent.name == "lac-t2-smoke"]
    if len(bundles) != 1 or len(sources) != 1:
        raise RuntimeError("Mount exactly one trusted completed T2 kernel output")
    bundle, source = bundles[0], sources[0]
    if "--prepared" not in sys.argv:
        subprocess.run([sys.executable, "-m", "pip", "install", "--disable-pip-version-check",
                        "-r", str(bundle / "requirements.txt"), "lm-format-enforcer==0.11.3"], check=True)
        environment = dict(os.environ)
        environment["CUDA_VISIBLE_DEVICES"] = "0"
        subprocess.run([sys.executable, __file__, "--prepared"], check=True, env=environment)
        return
    sys.path.insert(0, str(bundle))
    from smoke_contract import MODEL, REVISION, parse_runtime_output, verify_bundle, verify_config
    records, _ = verify_bundle(bundle)
    config = verify_config(json.loads((source / "training-config.json").read_text()))
    previous = json.loads((source / "run-metadata.json").read_text())
    if (previous["revision"] != REVISION or not previous["resume_result"]["passed"]
            or not previous["adapter_reload_passed"]):
        raise RuntimeError("Completed bounded training/reload proof required")
    hashes = json.loads((source / "artifact-checksums.json").read_text())
    for path in (source / "adapter").iterdir():
        if path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest() != hashes[
                str(path.relative_to(source))]:
            raise RuntimeError("Adapter artifact integrity failure")
    adapter_hash = hashes["adapter/adapter_model.safetensors"]
    output = Path("/kaggle/working/lac-t2-contract-proof")
    output.mkdir()  # Never overwrite a previous proof.
    import torch
    from huggingface_hub import HfApi
    from transformers import AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig
    from peft import PeftModel
    if HfApi().model_info(MODEL, revision=REVISION).sha != REVISION:
        raise RuntimeError("Immutable base revision mismatch")
    started = time.monotonic()
    dtype = torch.bfloat16 if torch.cuda.is_bf16_supported(including_emulation=False) else torch.float16
    tokenizer = AutoTokenizer.from_pretrained(MODEL, revision=REVISION, trust_remote_code=False)
    quant = BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type="nf4",
        bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=dtype)
    base = AutoModelForCausalLM.from_pretrained(MODEL, revision=REVISION,
        trust_remote_code=False, torch_dtype=dtype, quantization_config=quant,
        device_map={"": 0}, attn_implementation="sdpa")
    model = PeftModel.from_pretrained(base, str(source / "adapter"), is_trainable=False)
    model.eval()
    schemas = json.loads((bundle / "target.schemas.json").read_text())
    evaluations = []
    for task in config["representative_tasks"]:
        record = next(r for r in records if r["task"] == task)
        prompt = tokenizer.apply_chat_template(inference_messages(record, schemas), tokenize=True,
            add_generation_prompt=True, return_tensors="pt").to("cuda:0")
        with torch.inference_mode():
            generated = model.generate(input_ids=prompt, attention_mask=torch.ones_like(prompt),
                do_sample=False, max_new_tokens=config["max_new_tokens"], pad_token_id=tokenizer.eos_token_id,
                prefix_allowed_tokens_fn=schema_prefix(tokenizer, schemas[record["contract"]]))
        text = tokenizer.decode(generated[0, prompt.shape[1]:], skip_special_tokens=True).strip()
        entry = {"id": record["id"], "task": task, "raw_output": text, "parser_accepted": False}
        try:
            entry["parsed_output"] = parse_runtime_output(text, record, schemas, bundle / "runtime")
            entry["parser_accepted"] = True
        except Exception as error:
            entry["error_category"] = type(error).__name__
        evaluations.append(entry)
    report = {"purpose": config["purpose"], "training_steps_in_this_proof": 0,
        "model_id": MODEL, "revision": REVISION, "adapter_sha256": adapter_hash,
        "schema_supplied_to_prompt": True, "output_repaired": False,
        "schema_constrained_decoding": "lm-format-enforcer==0.11.3; unchanged T1 schema",
        "adapter_reload_passed": True, "representative_outputs": evaluations,
        "runtime_parser_gate_passed": all(e["parser_accepted"] for e in evaluations),
        "gpu_process_elapsed_seconds_not_account_quota": round(time.monotonic() - started, 3),
        "peak_gpu_memory_bytes": torch.cuda.max_memory_allocated(0),
        "quality_metric": None, "blind_performance": None}
    (output / "smoke-evaluation.json").write_text(json.dumps(report, indent=2) + "\n")
    checksums = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in output.iterdir()}
    (output / "artifact-checksums.json").write_text(json.dumps(checksums, indent=2) + "\n")
    print(json.dumps({"parser_gate_passed": report["runtime_parser_gate_passed"], "training_steps": 0}))


if __name__ == "__main__":
    main()
