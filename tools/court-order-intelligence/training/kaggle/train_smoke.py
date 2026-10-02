"""Kaggle-only bounded QLoRA plumbing proof. NEVER a quality/evaluation run."""
import argparse
import gc
import hashlib
import importlib.metadata
import json
from pathlib import Path
import shutil
import time

from smoke_contract import MODEL, REVISION, encode, parse_runtime_output, verify_bundle, verify_config
from reload_smoke import inference_messages, schema_prefix


def save(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2) + "\n", encoding="utf-8")


def main():
    cli = argparse.ArgumentParser()
    cli.add_argument("--bundle", type=Path, required=True)
    cli.add_argument("--output", type=Path, required=True)
    cli.add_argument("--ack-private-notebook", action="store_true", required=True)
    cli.add_argument("--resume", type=Path, help="Trusted previous checkpoint with optimizer/scheduler/RNG state")
    args = cli.parse_args()
    bundle, output = args.bundle.resolve(), args.output.resolve()
    if not Path("/kaggle/working").is_dir() or not output.is_relative_to(Path("/kaggle/working")):
        raise RuntimeError("Kaggle GPU runtime/output required; no local/offline pretend run")
    if output.exists() and any(output.iterdir()):
        raise RuntimeError("Use a fresh output directory; previous artifacts must remain recoverable")
    records, manifest = verify_bundle(bundle)
    config = verify_config(json.loads((bundle / "training_config_smoke.json").read_text(encoding="utf-8")))
    schemas = json.loads((bundle / "target.schemas.json").read_text(encoding="utf-8"))
    import torch
    if not torch.cuda.is_available():
        raise RuntimeError("GPU absent; GPU smoke did not execute")
    from huggingface_hub import HfApi
    from transformers import (AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig,
                              Trainer, TrainerCallback, TrainingArguments, set_seed)
    from peft import LoraConfig, PeftModel, get_peft_model, prepare_model_for_kbit_training
    from datasets import IterableDataset
    # Do not guess backend compatibility from hardware name. Execute the probe.
    gpu = torch.cuda.get_device_properties(0)
    bf16 = torch.cuda.is_bf16_supported(including_emulation=False)
    dtype = torch.bfloat16 if bf16 else torch.float16
    versions = {p: importlib.metadata.version(p) for p in
                ("torch", "transformers", "peft", "bitsandbytes", "accelerate", "datasets", "jsonschema")}
    expected = {"torch": "2.6.0", "transformers": "4.56.2", "peft": "0.17.1",
                "bitsandbytes": "0.47.0", "accelerate": "1.10.1", "datasets": "4.1.1", "jsonschema": "4.25.1"}
    if any(versions[k].split("+")[0] != value for k, value in expected.items()):
        raise RuntimeError("Pinned packages not installed; restart Kaggle kernel after install")
    output.mkdir(parents=True)
    started = time.monotonic()
    metadata = {"purpose": config["purpose"], "result": "RUNNING", "stage": "model_revision_verification",
        "gpu": gpu.name, "vram_bytes": gpu.total_memory, "cuda_version": torch.version.cuda,
        "bf16_native_supported": bf16, "selected_device": 0, "visible_gpu_count": torch.cuda.device_count(),
        "packages": versions, "model_id": MODEL, "revision": REVISION, "ooms": [],
        "private_notebook_operator_acknowledged": True, "quality_claim": False, "blind_claim": False}
    save(output / "run-metadata.json", metadata)
    shutil.copyfile(bundle / "dataset-manifest.json", output / "dataset-manifest.json")
    save(output / "training-config.json", config)

    def persist(stage):
        metadata["stage"] = stage
        metadata["elapsed_seconds"] = round(time.monotonic() - started, 3)
        metadata["peak_gpu_memory_bytes"] = torch.cuda.max_memory_allocated(0)
        save(output / "run-metadata.json", metadata)

    class DurableLog(TrainerCallback):
        def on_log(self, training_args, state, control, logs=None, **kwargs):
            with (output / "training-log.jsonl").open("a", encoding="utf-8") as log:
                log.write(json.dumps({"step": state.global_step, "elapsed_seconds": round(time.monotonic() - started, 3),
                                      "logs": logs or {}}) + "\n")

    class CheckpointPause(TrainerCallback):
        def on_step_end(self, training_args, state, control, **kwargs):
            if state.global_step == config["checkpoint_probe_steps"]:
                control.should_save = True
                control.should_training_stop = True
            return control

    def load_base():
        quant = BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type="nf4",
            bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=dtype)
        return AutoModelForCausalLM.from_pretrained(MODEL, revision=REVISION,
            trust_remote_code=False, torch_dtype=dtype, quantization_config=quant,
            device_map={"": 0}, attn_implementation="sdpa")

    def collate(samples):
        width = max(len(s["input_ids"]) for s in samples)
        return {key: torch.tensor([s[key] + [padding] * (width - len(s[key])) for s in samples])
                for key, padding in (("input_ids", tokenizer.pad_token_id), ("attention_mask", 0), ("labels", -100))}

    def trainer(model, data, steps, checkpoint_dir):
        arguments = TrainingArguments(output_dir=str(checkpoint_dir), max_steps=steps,
            per_device_train_batch_size=1, gradient_accumulation_steps=config["gradient_accumulation_steps"],
            learning_rate=config["learning_rate"], save_strategy="steps", save_steps=config["save_steps"],
            save_total_limit=3, save_only_model=False, logging_steps=1, report_to="none",
            fp16=not bf16, bf16=bf16, gradient_checkpointing=True,
            gradient_checkpointing_kwargs={"use_reentrant": False}, optim="adamw_torch",
            dataloader_num_workers=0, remove_unused_columns=False, seed=config["seed"],
            data_seed=config["seed"], disable_tqdm=True, push_to_hub=False)
        return Trainer(model=model, args=arguments, train_dataset=data, data_collator=collate,
                       callbacks=[DurableLog()])

    try:
        if HfApi().model_info(MODEL, revision=REVISION).sha != REVISION:
            raise RuntimeError("Immutable revision verification failed")
        metadata["immutable_revision_verified"] = True
        set_seed(config["seed"])
        tokenizer = AutoTokenizer.from_pretrained(MODEL, revision=REVISION, trust_remote_code=False)
        if tokenizer.pad_token_id is None:
            tokenizer.pad_token = tokenizer.eos_token
        tokenizer.padding_side = "right"
        persist("4bit_model_load")
        model = load_base()
        if not getattr(model, "is_loaded_in_4bit", False):
            raise RuntimeError("4-bit loading not confirmed")
        model.config.use_cache = False
        model = prepare_model_for_kbit_training(model, use_gradient_checkpointing=True,
            gradient_checkpointing_kwargs={"use_reentrant": False})
        model = get_peft_model(model, LoraConfig(r=config["lora_r"], lora_alpha=config["lora_alpha"],
            lora_dropout=config["lora_dropout"], target_modules=config["target_modules"],
            bias="none", task_type="CAUSAL_LM"))
        metadata["trainable_parameters"] = sum(p.numel() for p in model.parameters() if p.requires_grad)
        persist("forward_backward_probe")
        lengths = [config["max_sequence_length"]] + config["oom_sequence_lengths"]
        encoded, retained = None, None
        for limit in lengths:
            candidates = [(r, encode(tokenizer, r, limit)) for r in records]
            retained = [r for r, tokens in candidates if tokens is not None]
            encoded = [tokens for _, tokens in candidates if tokens is not None]
            if not encoded or not set(config["representative_tasks"]).issubset({r["task"] for r in retained}):
                raise RuntimeError("Bounded context cannot retain representative tasks; no silent truncation")
            try:
                model.train()
                batch = {k: v.to("cuda:0") for k, v in collate([max(encoded, key=lambda x: len(x["input_ids"]))]).items()}
                with torch.autocast("cuda", dtype=dtype):
                    result = model(**batch)
                if not torch.isfinite(result.loss):
                    raise RuntimeError("Nonfinite probe loss")
                result.loss.backward()
                model.zero_grad(set_to_none=True)
                del result, batch
                config["actual_max_sequence_length"] = limit
                break
            except torch.cuda.OutOfMemoryError:
                metadata["ooms"].append({"stage": "forward_backward_probe", "sequence_length": limit})
                model.zero_grad(set_to_none=True)
                # Drop CUDA references before bounded smaller-context retry.
                result, batch = None, None
                gc.collect()
                torch.cuda.empty_cache()
        else:
            raise RuntimeError("Bounded probe retries exhausted")
        metadata["probe_passed"] = True
        metadata["actual_example_count"] = len(retained)
        metadata["skipped_oversize_ids"] = [r["id"] for r in records if r not in retained]
        save(output / "training-config.json", config)
        # The pinned Trainer loses a trailing partial accumulation after a
        # mid-epoch data skip on resume (22 examples / accumulation=4). A cyclic
        # iterable uses the exact original examples and max_steps as its bound;
        # it does not manufacture or truncate examples. Both Trainers skip the
        # same deterministic stream, and each update has four microbatches.
        def smoke_stream():
            while True:
                yield from encoded
        data = IterableDataset.from_generator(smoke_stream)
        metadata["sampling"] = "deterministic cyclic original 22-example stream; no added records"
        checkpoints = output / "checkpoints"
        training_start = time.monotonic()
        if args.resume:
            # Previous checkpoints are trusted local artifacts, never arbitrary
            # downloaded pickle files. Verify accompanying metadata below.
            checkpoint = args.resume.resolve()
            previous = json.loads((checkpoint.parent.parent / "training-config.json").read_text(encoding="utf-8"))
            if previous != config:
                raise RuntimeError("Resume config differs from exact checkpoint settings")
            old_manifest = (checkpoint.parent.parent / "dataset-manifest.json").read_bytes()
            if old_manifest != (output / "dataset-manifest.json").read_bytes():
                raise RuntimeError("Resume dataset/source bundle differs")
        else:
            persist("initial_training")
            # Preserve the exact six-step scheduler across the deliberate pause.
            first = trainer(model, data, config["max_steps"], checkpoints)
            first.add_callback(CheckpointPause())
            first.train()
            checkpoint = checkpoints / f"checkpoint-{config['checkpoint_probe_steps']}"
            del first
        required = ("trainer_state.json", "optimizer.pt", "scheduler.pt", "adapter_model.safetensors", "rng_state.pth")
        if any(not (checkpoint / name).is_file() for name in required):
            raise RuntimeError("Recoverable checkpoint incomplete")
        before_step = json.loads((checkpoint / "trainer_state.json").read_text())["global_step"]
        if not 0 < before_step < config["max_steps"]:
            raise RuntimeError("Checkpoint is not resumable to smoke max steps")
        metadata["checkpoint_created"] = str(checkpoint)
        persist("checkpoint_resume")
        resumed = trainer(model, data, config["max_steps"], checkpoints)
        resumed.train(resume_from_checkpoint=str(checkpoint))
        metadata["resume_result"] = {"checkpoint_step": before_step, "final_step": resumed.state.global_step,
                                     "passed": resumed.state.global_step == config["max_steps"]}
        metadata["training_seconds"] = round(time.monotonic() - training_start, 3)
        if not metadata["resume_result"]["passed"]:
            raise RuntimeError("Resume did not reach expected final global step")
        adapter = output / "adapter"
        model.save_pretrained(adapter, safe_serialization=True)
        tokenizer.save_pretrained(adapter)
        adapter_config = json.loads((adapter / "adapter_config.json").read_text())
        # Some PEFT releases omit the base revision. Pin it explicitly in the
        # adapter metadata, and still always pass immutable revision on reload.
        adapter_config["revision"] = REVISION
        save(adapter / "adapter_config.json", adapter_config)
        metadata["adapter_save_location"] = str(adapter)
        del resumed, model
        gc.collect()
        torch.cuda.empty_cache()
        persist("adapter_reload")
        base = load_base()
        model = PeftModel.from_pretrained(base, str(adapter), is_trainable=False)
        model.eval()
        metadata["adapter_reload_passed"] = True
        persist("representative_inference")
        evaluations = []
        for task in config["representative_tasks"]:
            record = next(r for r in retained if r["task"] == task)
            prompt = tokenizer.apply_chat_template(inference_messages(record, schemas), tokenize=True,
                add_generation_prompt=True, return_tensors="pt").to("cuda:0")
            with torch.inference_mode():
                generated = model.generate(input_ids=prompt, attention_mask=torch.ones_like(prompt),
                    do_sample=False, max_new_tokens=config["max_new_tokens"], pad_token_id=tokenizer.pad_token_id,
                    prefix_allowed_tokens_fn=schema_prefix(tokenizer, schemas[record["contract"]]))
            text = tokenizer.decode(generated[0, prompt.shape[1]:], skip_special_tokens=True).strip()
            entry = {"id": record["id"], "task": task, "raw_output": text, "parser_accepted": False}
            try:
                entry["parsed_output"] = parse_runtime_output(text, record, schemas, bundle / "runtime")
                entry["parser_accepted"] = True
            except (ValueError, KeyError, json.JSONDecodeError) as error:
                entry["error_category"] = type(error).__name__
            except Exception as error:
                # Schema exceptions are recorded; no model-output repair/replay.
                entry["error_category"] = type(error).__name__
            evaluations.append(entry)
        accepted = all(e["parser_accepted"] for e in evaluations)
        save(output / "smoke-evaluation.json", {"purpose": config["purpose"], "representative_outputs": evaluations,
            "runtime_parser_gate_passed": accepted, "quality_metric": None, "blind_performance": None})
        metadata["result"] = "PASS" if accepted else "INCOMPLETE_PARSER_GATE"
        metadata["gpu_process_elapsed_seconds_not_account_quota"] = round(time.monotonic() - started, 3)
        persist("complete")
    except Exception as error:
        metadata["result"] = "FAILED"
        metadata["failure_category"] = type(error).__name__  # No auth/token values or traceback in artifact.
        persist(metadata["stage"])
        raise RuntimeError(f"Smoke failed at {metadata['stage']}: {type(error).__name__}; see safe run metadata") from None
    finally:
        files = {str(p.relative_to(output)): hashlib.sha256(p.read_bytes()).hexdigest()
                 for p in sorted(output.rglob("*")) if p.is_file()}
        save(output / "artifact-checksums.json", files)
        # Contains only run/adapter/checkpoint output, never the base-model cache.
        shutil.make_archive(str(output) + "-artifacts", "zip", output)
    print(json.dumps({"result": metadata["result"], "artifacts": str(output), "quality_claim": False}))


if __name__ == "__main__":
    main()
