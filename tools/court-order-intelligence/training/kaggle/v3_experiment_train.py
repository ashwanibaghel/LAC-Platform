"""Bounded experimental V3 fit. Never a quality/office acceptance claim.

Requires the successful T4 proof for identical gold, runtime and loss code.
Loads a fresh pinned base, not the six-step proof adapter. No live Court access.
"""
import gc
import hashlib
import importlib.metadata
import json
from pathlib import Path
import sys

PURPOSE = 'V3_EXPERIMENT_VERIFIED_POOL_NOT_QUALITY_ACCEPTANCE'


def make_training_data(rows, encoded, budget):
    """All gold once, then weighted repetitions, within the same finite budget.

    Pure weighted sampling would expose only 188/244 at this budget. Never
    silently leave the other 56 verified records untrained. The immutable map
    stream still uses Trainer's sole skip mechanism; no moving dataset cursor.
    """
    from collections import Counter
    from v3_trainer import CurriculumDataset
    class CoverageDataset(CurriculumDataset):
        def state_at(self, step):
            if not 0 <= step <= self.steps:
                raise ValueError('Coverage checkpoint step outside budget')
            consumed = step * self.accumulation
            return dict(version='v3-full-coverage-then-weighted-1', seed=self.seed,
                        dataset_sha256=self.curriculum.fingerprint,
                        stream_sha256=hashlib.sha256(json.dumps(self.ids).encode()).hexdigest(),
                        weights=self.curriculum.weights, logical_sample_index=consumed,
                        consumed_ids_sha256=hashlib.sha256(json.dumps(self.ids[:consumed]).encode()).hexdigest())
    data = CoverageDataset(rows, encoded, 20261003, budget['trainer_steps'], 4)
    coverage = sorted(encoded, key=lambda key: hashlib.sha256(f'20261003:{key}'.encode()).hexdigest())
    if len(data.ids) < len(coverage):
        raise ValueError('Budget cannot cover all gold')
    data.ids = tuple(coverage) + data.ids[:len(data.ids)-len(coverage)]
    by_id = {row['id']: row for row in rows}
    selected = [by_id[key] for key in data.ids]
    data.report = dict(strategy='all-originals-once-then-weighted', original_record_count=len(rows),
        logical_exposures=len(data.ids), unique_records_exposed=len(set(data.ids)),
        equivalent_dataset_passes=len(data.ids)/len(rows),
        task_exposures=dict(Counter(row['task'] for row in selected)),
        matter_exposures=dict(Counter(row['matter_id'] for row in selected)),
        leakage_group_exposures=dict(Counter(row.get('leakage_group', row['matter_id']) for row in selected)),
        positive_empty_exposures=dict(Counter(row['task'] + '|' + ('positive' if
            row['expected'].get('claims', row['expected'].get('facts')) else 'empty') for row in selected)))
    return data


def verify_experiment(root):
    manifest = json.loads((root / 'dataset-manifest.json').read_text())
    if (manifest['purpose'] != PURPOSE or manifest['private'] is not True
            or manifest['pdfs_included'] or manifest['private_workbook_included']
            or manifest['weights_included'] or manifest['quality_acceptance_passed']):
        raise ValueError('Verified public-only experimental bundle required')
    for relative, sha in manifest['files'].items():
        path = (root / relative).resolve()
        if not path.is_relative_to(root.resolve()) or hashlib.sha256(path.read_bytes()).hexdigest() != sha:
            raise ValueError('Experiment bundle checksum/path mismatch')
    actual = {p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file()}
    if actual != set(manifest['files']) | {'dataset-manifest.json'}:
        raise ValueError('Unlisted experiment files')
    proof = json.loads((root / 'gpu-preflight-proof.json').read_text())
    original_bytes = (root / 'preflight-manifest.json').read_bytes()
    original = json.loads(original_bytes)
    if (proof['result'] != 'PASS' or not proof['resume_ids_exact']
            or proof['logical_steps'] != 6 or proof['consumed_microbatches'] != 24
            or proof['full_v3_training_launched'] is not False
            or proof['bundle_manifest_sha256'] != hashlib.sha256(original_bytes).hexdigest()
            or proof['encoded_candidate_count'] != manifest['candidate_count']
            or proof['examples_dropped'] or proof['evidence_truncated']
            or proof['chosen_context'] < proof['required_context']
            or proof['exact_masked_sft_loss_proof']['result'] != 'PASS'
            or not proof['exact_masked_sft_loss_proof']['loss_and_all_gradients_equivalent']):
        raise ValueError('Complete matching hardware/loss/resume proof required')
    if not any(p.get('context') == proof['chosen_context'] and p.get('forward_backward')
               and p.get('safe_headroom') for p in proof['memory_probes']):
        raise ValueError('Safe complete-context headroom proof required')
    for name, sha in original['files'].items():
        if manifest['files'].get(name) != sha:
            raise ValueError('Training data/runtime differs from GPU-proven bundle')
    rows = [json.loads(line) for line in (root / 'train.jsonl').read_text().splitlines()]
    if len(rows) != manifest['candidate_count'] or len({r['id'] for r in rows}) != len(rows):
        raise ValueError('Experiment inventory differs')
    return rows, manifest, proof


def main(bundle, output):
    if not Path('/kaggle/working').is_dir() or output.exists():
        raise ValueError('Fresh Kaggle experiment output required')
    rows, manifest, proof = verify_experiment(bundle)
    output.mkdir()
    meta = dict(purpose=PURPOSE, result='RUNNING', stage='PREFLIGHT', source_sha=manifest['source_sha'],
                quality_claim=False, fresh_holdout_evaluation=False, full_v3_training_launched=False,
                bundle_manifest_sha256=hashlib.sha256((bundle / 'dataset-manifest.json').read_bytes()).hexdigest())
    def save():
        (output / 'run-metadata.json').write_text(json.dumps(meta, indent=2, sort_keys=True) + '\n')
        print(json.dumps({k: meta[k] for k in ('stage', 'result', 'full_v3_training_launched')}), flush=True)
    save()
    try:
        import torch
        from transformers import AutoTokenizer, AutoModelForCausalLM, BitsAndBytesConfig, TrainingArguments, TrainerCallback, set_seed
        from peft import prepare_model_for_kbit_training, get_peft_model, LoraConfig, PeftModel
        from smoke_contract import MODEL, REVISION, encode
        from reload_smoke import inference_messages, schema_prefix
        from v3_contract import parse_v3_output, target_schemas_v3
        from v3_trainer import CurriculumTrainer
        from v3_sampler import calculate_budget
        from v3_sft_loss import verify_exact_loss
        if not torch.cuda.is_available():
            raise ValueError('GPU absent')
        gpu = torch.cuda.get_device_properties(0)
        if gpu.name != proof['gpu'] or gpu.total_memory < proof['vram_bytes']:
            raise ValueError('Hardware differs from proof')
        packages = {p: importlib.metadata.version(p) for p in proof['packages']}
        if packages != proof['packages']:
            raise ValueError('Runtime differs from hardware proof')
        meta.update(gpu=gpu.name, vram_bytes=gpu.total_memory, packages=packages,
                    exact_masked_sft_loss_proof=verify_exact_loss(), model_id=MODEL, revision=REVISION)
        schemas = target_schemas_v3()
        if schemas != json.loads((bundle / 'target.schemas.json').read_text()):
            raise ValueError('V3 schema changed')
        for row in rows:
            parse_v3_output(json.dumps(row['expected']), row, schemas, bundle)
        set_seed(20261003)
        tokenizer = AutoTokenizer.from_pretrained(MODEL, revision=REVISION, trust_remote_code=False)
        if tokenizer.pad_token_id is None:
            tokenizer.pad_token = tokenizer.eos_token
        cap = proof['chosen_context']
        encoded = {r['id']: encode(tokenizer, r, cap) for r in rows}
        if any(value is None for value in encoded.values()):
            raise ValueError('No truncation/drop allowed')
        prompts = [len(tokenizer.apply_chat_template(inference_messages(r, schemas), tokenize=True,
                   add_generation_prompt=True)) for r in rows]
        if max(prompts) + 512 > cap:
            raise ValueError('Inference reserve does not fit')
        budget = calculate_budget(len(rows), 4, 3)
        data = make_training_data(rows, encoded, budget)
        meta.update(budget=budget, exposure=data.report, encoded_candidate_count=len(encoded),
                    examples_dropped=0, evidence_truncated=False, context=cap)
        dtype = torch.bfloat16 if proof['bf16'] else torch.float16
        def load_base():
            base = AutoModelForCausalLM.from_pretrained(MODEL, revision=REVISION, trust_remote_code=False,
                torch_dtype=dtype, quantization_config=BitsAndBytesConfig(load_in_4bit=True,
                    bnb_4bit_quant_type='nf4', bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=dtype),
                device_map={'': 0}, attn_implementation='sdpa')
            if not base.is_loaded_in_4bit or base.config._commit_hash != REVISION:
                raise ValueError('Immutable NF4 base not confirmed')
            return base
        model = load_base()
        model.config.use_cache = False
        model = prepare_model_for_kbit_training(model, use_gradient_checkpointing=True,
                            gradient_checkpointing_kwargs={'use_reentrant': False})
        model = get_peft_model(model, LoraConfig(r=8, lora_alpha=16, lora_dropout=.05,
            target_modules=['q_proj', 'k_proj', 'v_proj', 'o_proj'], bias='none', task_type='CAUSAL_LM'))
        def collate(samples):
            # batch one; no altered/evidence-shortened input
            return {k: torch.tensor([s[k] for s in samples]) for k in
                    ('input_ids', 'attention_mask', 'labels', 'curriculum_index')}
        class Pause(TrainerCallback):
            def on_step_end(self, args, state, control, **kw):
                if state.global_step == 10:
                    control.should_save = control.should_training_stop = True
        def trainer():
            args = TrainingArguments(output_dir=str(output / 'checkpoints'), max_steps=budget['trainer_steps'],
                per_device_train_batch_size=1, gradient_accumulation_steps=4, learning_rate=5e-5,
                warmup_steps=5, save_strategy='steps', save_steps=50, save_total_limit=2,
                logging_steps=1, report_to='none', disable_tqdm=True,
                fp16=not proof['bf16'], bf16=proof['bf16'], gradient_checkpointing=True,
                gradient_checkpointing_kwargs={'use_reentrant': False}, optim='adamw_torch',
                dataloader_num_workers=0, remove_unused_columns=False, ignore_data_skip=False,
                seed=20261003, data_seed=20261003, push_to_hub=False)
            return CurriculumTrainer(model=model, args=args, train_dataset=data, data_collator=collate)
        meta.update(stage='V3_FULL_FIT', full_v3_training_launched=True)
        save()
        first = trainer()
        first.add_callback(Pause())
        first.train()
        consumed = list(first.consumed_ids)
        checkpoint = output / 'checkpoints/checkpoint-10'
        del first
        gc.collect()
        resumed = trainer()
        resumed.train(resume_from_checkpoint=str(checkpoint))
        consumed += resumed.consumed_ids
        if consumed != list(data.ids) or resumed.state.global_step != budget['trainer_steps']:
            raise ValueError('Actual fit resume differs/double-skips')
        state = resumed.optimizer.state_dict()['state']
        updates = {int(s['step']) for s in state.values() if 'step' in s}
        if len(updates) != 1:
            raise ValueError('Optimizer durable update counts differ')
        applied = next(iter(updates))
        model.save_pretrained(output / 'adapter', safe_serialization=True)
        tokenizer.save_pretrained(output / 'adapter')
        meta.update(stage='ADAPTER_RELOAD', fit_complete=True, consumed_microbatches=len(consumed),
                    logical_steps=resumed.state.global_step, applied_optimizer_updates=applied,
                    scaler_skipped_updates=resumed.state.global_step-applied, resume_ids_exact=True,
                    trainer_log=resumed.state.log_history)
        save()
        del resumed, model, state
        gc.collect()
        torch.cuda.empty_cache()
        base = load_base()
        model = PeftModel.from_pretrained(base, str(output / 'adapter'), is_trainable=False)
        model.eval()
        meta['adapter_reload_passed'] = True
        evaluations = []
        # TRAIN representative integration checks only, never blind accuracy.
        for task in sorted({r['task'] for r in rows}):
            record = next(r for r in rows if r['task'] == task)
            prompt = tokenizer.apply_chat_template(inference_messages(record, schemas), tokenize=True,
                add_generation_prompt=True, return_tensors='pt').to('cuda:0')
            with torch.inference_mode():
                generated = model.generate(input_ids=prompt, attention_mask=torch.ones_like(prompt),
                    do_sample=False, max_new_tokens=512, pad_token_id=tokenizer.eos_token_id,
                    prefix_allowed_tokens_fn=schema_prefix(tokenizer, schemas[record['contract']]))
            text = tokenizer.decode(generated[0, prompt.shape[1]:], skip_special_tokens=True).strip()
            entry = dict(id=record['id'], task=task, raw_output=text, parser_accepted=False)
            try:
                entry['parsed_output'] = parse_v3_output(text, record, schemas, bundle)
                entry['parser_accepted'] = True
            except Exception as error:
                entry['error_category'] = type(error).__name__
            evaluations.append(entry)
        (output / 'representative-inference.json').write_text(json.dumps(evaluations, indent=2) + '\n')
        meta.update(stage='COMPLETE', result='TRAINING_COMPLETE_NOT_QUALITY_ACCEPTED',
                    representative_parser_passed=all(e['parser_accepted'] for e in evaluations))
        save()
        hashes = {p.relative_to(output).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                  for p in output.rglob('*') if p.is_file()}
        (output / 'artifact-checksums.json').write_text(json.dumps(hashes, indent=2, sort_keys=True) + '\n')
    except Exception as error:
        meta.update(result='FAIL', failure_type=type(error).__name__,
                    failure=str(error)[:350] if isinstance(error, ValueError) else type(error).__name__)
        save()
        raise RuntimeError('V3 experimental fit failed; see non-secret metadata') from None


if __name__ == '__main__':
    main(Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve())
