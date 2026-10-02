"""One bounded Pilot fit + first frozen stock/adapter evaluation, no tuning loop.

Reuse the proven T2 trainer/checkpoint/memory implementation through explicit
Pilot verifiers. Evaluation targets are never passed to generation. Stock and
adapter use the same quantized base, schema grammar and frozen runtime parser.
"""
import hashlib
import json
import sys
from contextlib import nullcontext
from pathlib import Path
import shutil
import time

from pilot_contract import verify_bundle, verify_config
from smoke_contract import MODEL, REVISION, parse_runtime_output
from reload_smoke import inference_messages, schema_prefix
from train_smoke import main as train, save


def score(record, prediction, accepted):
    expected = record["expected"]
    result = {"id": record["id"], "task": record["task"], "matter_id": record["matter_id"],
              "language": record["language"], "parser_accepted": accepted,
              "exact_target": accepted and prediction == expected}
    if record["contract"] == "anchors":
        wanted = {(r["anchorId"], r["category"], r["field"], r["scope"]) for r in expected["facts"]}
        got = {(r["anchorId"], r["category"], r["field"], r["scope"]) for r in (prediction or {}).get("facts", [])}
        result["semantic_role_correct"] = [len(wanted & got), len(wanted)]
        result["court_direction_recall"] = [len({r for r in wanted & got if r[1] == "COURT_DIRECTION"}), len({r for r in wanted if r[1] == "COURT_DIRECTION"})]
        result["court_direction_precision"] = [len({r for r in wanted & got if r[1] == "COURT_DIRECTION"}), len({r for r in got if r[1] == "COURT_DIRECTION"})]
        result["wrong_role_or_scope"] = len(got - wanted)
    else:
        wanted = {r["factId"] for r in expected["claims"]}
        got = {r["factId"] for r in (prediction or {}).get("claims", [])}
        result["selected_fact_recall"] = [len(got & wanted), len(wanted)]
        result["selected_fact_precision"] = [len(got & wanted), len(got)]
        result["unexpected_selection"] = len(got - wanted)
        result["unsupported_negative_answer"] = int(not wanted and bool(got))
    return result


def aggregate(rows):
    report = {"examples": len(rows), "parser_accepted": sum(r["parser_accepted"] for r in rows),
              "exact_target": sum(r["exact_target"] for r in rows), "by_task": {}}
    for task in sorted({r["task"] for r in rows}):
        selected = [r for r in rows if r["task"] == task]
        report["by_task"][task] = {"exact_target": [sum(r["exact_target"] for r in selected), len(selected)]}
    for key in ("semantic_role_correct", "court_direction_recall", "court_direction_precision", "selected_fact_recall", "selected_fact_precision"):
        report[key] = [sum(r.get(key, [0, 0])[i] for r in rows) for i in (0, 1)]
    for key in ("wrong_role_or_scope", "unexpected_selection", "unsupported_negative_answer"):
        report[key] = sum(r.get(key, 0) for r in rows)
    return report


def evaluate(bundle, output):
    import torch
    from transformers import AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig
    from peft import PeftModel
    config = verify_config(json.loads((bundle / "training_config_pilot.json").read_text()))
    schemas = json.loads((bundle / "target.schemas.json").read_text())
    metadata = json.loads((output / "run-metadata.json").read_text())
    if not metadata["adapter_reload_passed"] or not metadata["resume_result"]["passed"]:
        raise ValueError("Training/reload gate incomplete")
    state = torch.load(output / "checkpoints/checkpoint-80/optimizer.pt", map_location="cpu", weights_only=True)
    steps = {int(x["step"]) for x in state["state"].values() if "step" in x}
    if len(steps) != 1:
        raise ValueError("Inconsistent durable optimizer update counters")
    applied = next(iter(steps))
    metadata.update({"trainer_global_steps": 80, "applied_optimizer_updates": applied,
        "scaler_skipped_updates": 80 - applied, "pilot_training_gate": "PASS" if applied >= 76 else "FAIL_RECURRENT_SKIPS"})
    save(output / "run-metadata.json", metadata)
    dtype = torch.bfloat16 if torch.cuda.is_bf16_supported(including_emulation=False) else torch.float16
    tokenizer = AutoTokenizer.from_pretrained(MODEL, revision=REVISION, trust_remote_code=False)
    quant = BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type="nf4", bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=dtype)
    base = AutoModelForCausalLM.from_pretrained(MODEL, revision=REVISION, trust_remote_code=False,
        torch_dtype=dtype, quantization_config=quant, device_map={"": 0}, attn_implementation="sdpa")
    model = PeftModel.from_pretrained(base, str(output / "adapter"), is_trainable=False)
    model.eval()
    started = time.monotonic()
    evaluations = []
    summary = {}
    for mode in ("stock_4b", "fine_tuned_4b"):
        summary[mode] = {}
        with model.disable_adapter() if mode == "stock_4b" else nullcontext():
            for split in ("validation", "blind"):
                rows = []
                records = [json.loads(line) for line in (bundle / (split + ".jsonl")).read_text().splitlines()]
                for record in records:
                    # Expected targets exist only in the scorer, never the prompt.
                    prompt = tokenizer.apply_chat_template(inference_messages(record, schemas), tokenize=True,
                        add_generation_prompt=True, return_tensors="pt").to("cuda:0")
                    if prompt.shape[1] > config["max_sequence_length"]:
                        raise ValueError("Held-out evidence exceeds reviewed context; never truncate")
                    with torch.inference_mode():
                        generated = model.generate(input_ids=prompt, attention_mask=torch.ones_like(prompt),
                            do_sample=False, max_new_tokens=512, pad_token_id=tokenizer.eos_token_id,
                            prefix_allowed_tokens_fn=schema_prefix(tokenizer, schemas[record["contract"]]))
                    raw = tokenizer.decode(generated[0, prompt.shape[1]:], skip_special_tokens=True).strip()
                    payload, accepted, error = None, False, None
                    try:
                        payload = parse_runtime_output(raw, record, schemas, bundle / "runtime")
                        accepted = True
                    except Exception as failure:
                        error = type(failure).__name__
                        try:
                            payload = json.loads(raw)
                        except ValueError:
                            pass
                    result = score(record, payload, accepted)
                    rows.append(result)
                    evaluations.append({"model": mode, "split": split, "raw_output": raw,
                        "parsed_output": payload, "parser_error": error, **result})
                    save(output / "evaluation-outputs.json", evaluations)
                summary[mode][split] = aggregate(rows)
    report = {"base": MODEL, "revision": REVISION, "first_frozen_evaluation": True,
        "same_4bit_base_and_safety_layer": True, "stock_method": "PEFT disable_adapter on freshly loaded pinned base",
        "schema_constrained_decoding": "lm-format-enforcer==0.11.3",
        "post_blind_tuning": False, "output_repair": False, "metrics": summary,
        "elapsed_seconds": round(time.monotonic() - started, 3),
        "deployment_gate": "NOT_APPROVED_PENDING_MANUAL_SOURCE_CHAIN_REVIEW"}
    save(output / "pilot-evaluation.json", report)


if __name__ == "__main__":
    # CLI arguments are the proven trainer's --bundle/--output/ack arguments.
    bundle = Path(sys.argv[sys.argv.index("--bundle") + 1]).resolve()
    output = Path(sys.argv[sys.argv.index("--output") + 1]).resolve()
    train(verify_bundle, verify_config, "training_config_pilot.json")
    try:
        evaluate(bundle, output)
    finally:
        save(output / "artifact-checksums.json", {str(p.relative_to(output)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in output.rglob("*") if p.is_file() and p.name != "artifact-checksums.json"})
        shutil.make_archive(str(output) + "-artifacts", "zip", output)
