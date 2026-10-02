"""Bounded T4 memory + actual CUDA Curriculum/Trainer proof, NOT a V3 fit.

Six logical steps only. A deliberate gradient overflow at step three exercises
the REAL CUDA scaler skip; it is proof instrumentation, never production train.
No blind/validation records, inference, quality claims or deployment.
"""
import gc
import hashlib
import importlib.metadata
import json
from pathlib import Path
import sys

PURPOSE = 'V3_T4_PREFLIGHT_NOT_TRAINING_NOT_QUALITY'


def verify_bundle(root):
    manifest = json.loads((root / 'dataset-manifest.json').read_text())
    if (manifest['purpose'] != PURPOSE or manifest['private'] is not True
            or manifest['pdfs_included'] or manifest['private_workbook_included']
            or manifest['weights_included']):
        raise ValueError('Public-only private preflight bundle required')
    for relative, sha in manifest['files'].items():
        path = (root / relative).resolve()
        if not path.is_relative_to(root.resolve()) or hashlib.sha256(path.read_bytes()).hexdigest() != sha:
            raise ValueError('Preflight bundle checksum/path mismatch')
    actual = {p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file()}
    if actual != set(manifest['files']) | {'dataset-manifest.json'}:
        raise ValueError('Unlisted preflight bundle files')
    records = [json.loads(s) for s in (root / 'train.jsonl').read_text().splitlines()]
    if len(records) != manifest['candidate_count'] or len({r['id'] for r in records}) != len(records):
        raise ValueError('Candidate inventory changed')
    return records, manifest


def main(bundle, output):
    if not Path('/kaggle/working').is_dir() or output.exists():
        raise ValueError('Fresh Kaggle-only preflight output required')
    rows, manifest = verify_bundle(bundle)
    output.mkdir()
    def save(value):
        (output / 'v3-preflight-result.json').write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')
        print(json.dumps({k: v for k, v in value.items() if k in ('stage', 'result', 'gpu', 'chosen_context')}), flush=True)
    meta = dict(purpose=PURPOSE, stage='GPU_CHECK', result='RUNNING', source_sha=manifest['source_sha'],
                bundle_manifest_sha256=hashlib.sha256((bundle / 'dataset-manifest.json').read_bytes()).hexdigest(),
                full_v3_training_launched=False, quality_claim=False, memory_probes=[])
    try:
        import torch
        from transformers import AutoTokenizer, AutoModelForCausalLM, BitsAndBytesConfig, TrainingArguments, TrainerCallback, set_seed
        from peft import prepare_model_for_kbit_training, get_peft_model, LoraConfig
        from smoke_contract import MODEL, REVISION, encode
        from reload_smoke import inference_messages
        from v3_contract import parse_v3_output, target_schemas_v3
        from v3_trainer import CurriculumDataset, CurriculumTrainer
        from v3_sft_loss import masked_causal_loss, verify_exact_loss
        if not torch.cuda.is_available():
            raise ValueError('CUDA GPU absent')
        gpu = torch.cuda.get_device_properties(0)
        meta.update(gpu=gpu.name, vram_bytes=gpu.total_memory, cuda=torch.version.cuda,
                    bf16=torch.cuda.is_bf16_supported(including_emulation=False))
        if 'T4' not in gpu.name:
            raise ValueError('This bounded memory acceptance is specifically T4')
        versions = {p: importlib.metadata.version(p) for p in
                    ('torch', 'transformers', 'peft', 'bitsandbytes', 'accelerate', 'datasets', 'jsonschema')}
        expected = dict(torch='2.6.0', transformers='4.56.2', peft='0.17.1', bitsandbytes='0.47.0',
                        accelerate='1.10.1', datasets='4.1.1', jsonschema='4.25.1')
        if any(versions[k].split('+')[0] != v for k, v in expected.items()):
            raise ValueError('Pinned dependency mismatch')
        meta['packages'] = versions
        meta['exact_masked_sft_loss_proof'] = verify_exact_loss()
        save(meta)
        schemas = target_schemas_v3()
        if schemas != json.loads((bundle / 'target.schemas.json').read_text()):
            raise ValueError('V3 schemas differ')
        for row in rows:
            parse_v3_output(json.dumps(row['expected']), row, schemas, bundle)
        set_seed(20261003)
        tokenizer = AutoTokenizer.from_pretrained(MODEL, revision=REVISION, trust_remote_code=False)
        if tokenizer.pad_token_id is None:
            tokenizer.pad_token = tokenizer.eos_token
        encoded = {r['id']: encode(tokenizer, r, 4096) for r in rows}
        if any(v is None for v in encoded.values()):
            raise ValueError('Candidate exceeds 4096; no truncation/drop permitted')
        prompts = [len(tokenizer.apply_chat_template(inference_messages(r, schemas), tokenize=True,
                    add_generation_prompt=True)) for r in rows]
        required = max(max(len(v['input_ids']) for v in encoded.values()), max(prompts) + 512)
        if required > 4096:
            raise ValueError('Inference reserve exceeds 4096; no truncation')
        meta.update(stage='MODEL_LOAD', required_context=required, encoded_candidate_count=len(encoded),
                    examples_dropped=0, evidence_truncated=False)
        save(meta)
        dtype = torch.bfloat16 if meta['bf16'] else torch.float16
        model = AutoModelForCausalLM.from_pretrained(MODEL, revision=REVISION, trust_remote_code=False,
            torch_dtype=dtype, quantization_config=BitsAndBytesConfig(load_in_4bit=True,
                bnb_4bit_quant_type='nf4', bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=dtype),
            device_map={'': 0}, attn_implementation='sdpa')
        if not getattr(model, 'is_loaded_in_4bit', False):
            raise ValueError('NF4 loading not confirmed')
        if model.config._commit_hash != REVISION:
            raise ValueError('Immutable loaded model revision differs')
        model.config.use_cache = False
        model = prepare_model_for_kbit_training(model, use_gradient_checkpointing=True,
                            gradient_checkpointing_kwargs={'use_reentrant': False})
        model = get_peft_model(model, LoraConfig(r=8, lora_alpha=16, lora_dropout=.05,
            target_modules=['q_proj', 'k_proj', 'v_proj', 'o_proj'], bias='none', task_type='CAUSAL_LM'))
        def collate(samples):
            width = max(len(s['input_ids']) for s in samples)
            result = {k: torch.tensor([s[k] + [padding] * (width - len(s[k])) for s in samples])
                      for k, padding in (('input_ids', tokenizer.pad_token_id), ('attention_mask', 0), ('labels', -100))}
            if 'curriculum_index' in samples[0]:
                result['curriculum_index'] = torch.tensor([s['curriculum_index'] for s in samples])
            return result
        # 3072 memory fixture is explicitly NOT shortened judicial evidence.
        # At 4096 use the complete largest real record plus non-evidence padding.
        chosen = None
        for cap in (3072, 4096):
            torch.cuda.reset_peak_memory_stats()
            if cap == 3072 and required > cap:
                sample = dict(input_ids=[tokenizer.eos_token_id] * cap, attention_mask=[1] * cap,
                              labels=[-100] * (cap - 32) + [tokenizer.eos_token_id] * 32)
                purpose = 'MEMORY_FIXTURE_ONLY_NOT_TRUNCATED_GOLD'
            else:
                original = max(encoded.values(), key=lambda r: len(r['input_ids']))
                if len(original['input_ids']) > cap:
                    raise ValueError('No fitting complete gold record')
                pad = cap - len(original['input_ids'])
                sample = {k: list(original[k]) + [v] * pad for k, v in
                          (('input_ids', tokenizer.eos_token_id), ('attention_mask', 1), ('labels', -100))}
                purpose = 'COMPLETE_NATIVE_RECORD_PLUS_MEMORY_PADDING'
            probe = dict(context=cap, purpose=purpose, all_candidates_and_reserve_fit=required <= cap)
            batch = loss = None
            try:
                model.train()
                batch = {k: v.to('cuda:0') for k, v in collate([sample]).items()}
                with torch.autocast('cuda', dtype=dtype):
                    loss, loss_outputs = masked_causal_loss(model, batch)
                if not torch.isfinite(loss):
                    raise ValueError('Nonfinite memory probe')
                loss.backward()
                free, _ = torch.cuda.mem_get_info()
                probe.update(forward_backward=True, peak_allocated=torch.cuda.max_memory_allocated(),
                             peak_reserved=torch.cuda.max_memory_reserved(), free_bytes=free,
                             safe_headroom=free >= 2 * 1024**3)
            except torch.cuda.OutOfMemoryError:
                probe.update(forward_backward=False, safe_headroom=False, failure='CUDA_OOM')
            finally:
                model.zero_grad(set_to_none=True)
                batch = loss = None
                loss_outputs = None
                gc.collect()
                torch.cuda.empty_cache()
            meta['memory_probes'].append(probe)
            meta['stage'] = 'MEMORY_PREFLIGHT'
            save(meta)
            if probe.get('forward_backward') and probe.get('safe_headroom') and required <= cap:
                chosen = cap
                break
        if chosen is None:
            raise ValueError('No complete-context safe-headroom cap')
        meta.update(stage='ACTUAL_CURRICULUM_TRAINER', chosen_context=chosen)
        save(meta)
        data = CurriculumDataset(rows, encoded, 20261003, 6, 4)
        class Pause(TrainerCallback):
            def on_step_end(self, args, state, control, **kw):
                if state.global_step == 2:
                    control.should_save = control.should_training_stop = True
        class InstrumentedTrainer(CurriculumTrainer):
            def training_step(self, model, inputs, num_items_in_batch=None):
                index = int(inputs['curriculum_index'].item())
                loss = super().training_step(model, inputs, num_items_in_batch)
                if index == 11 and not meta.get('forced_cuda_overflow'):
                    # Last microbatch of step three. Real GradScaler rejects the
                    # update, while all four curriculum samples were consumed.
                    with torch.no_grad():
                        grad = next(p.grad for p in model.parameters() if p.requires_grad and p.grad is not None)
                        grad.fill_(float('inf'))
                    meta['forced_cuda_overflow'] = True
                return loss
        def trainer():
            args = TrainingArguments(output_dir=str(output / 'checkpoints'), max_steps=6,
                per_device_train_batch_size=1, gradient_accumulation_steps=4, learning_rate=5e-5,
                save_strategy='no', logging_steps=1, report_to='none', disable_tqdm=True,
                fp16=not meta['bf16'], bf16=meta['bf16'], gradient_checkpointing=True,
                gradient_checkpointing_kwargs={'use_reentrant': False}, optim='adamw_torch',
                dataloader_num_workers=0, remove_unused_columns=False, ignore_data_skip=False,
                seed=20261003, data_seed=20261003, push_to_hub=False)
            return InstrumentedTrainer(model=model, args=args, train_dataset=data, data_collator=collate)
        if meta['bf16']:
            raise ValueError('Expected T4 FP16 GradScaler for real skip proof')
        first = trainer()
        first.add_callback(Pause())
        first.train()
        consumed = list(first.consumed_ids)
        checkpoint = output / 'checkpoints/checkpoint-2'
        del first
        resumed = trainer()
        resumed.train(resume_from_checkpoint=str(checkpoint))
        consumed += resumed.consumed_ids
        if consumed != list(data.ids) or resumed.state.global_step != 6:
            raise ValueError('Real Trainer resume stream diverged/double-skipped')
        state = resumed.optimizer.state_dict()['state']
        updates = {int(s['step']) for s in state.values() if 'step' in s}
        if len(updates) != 1:
            raise ValueError('Durable optimizer update counts disagree')
        applied = next(iter(updates))
        if not meta.get('forced_cuda_overflow') or not 0 < applied < 6:
            raise ValueError('Real GradScaler skipped-update proof not exercised')
        meta.update(stage='COMPLETE', result='PASS', consumed_ids=consumed,
                    consumed_microbatches=len(consumed), logical_steps=6, applied_updates=applied,
                    scaler_skipped_updates=6-applied, resume_ids_exact=True,
                    curriculum_state=data.state_at(6), exposure=data.report)
        save(meta)
    except Exception as error:
        # Provider/transport exceptions may carry URLs; persist their type only.
        meta.update(result='FAIL', failure_type=type(error).__name__,
                    failure=str(error)[:350] if isinstance(error, ValueError) else type(error).__name__)
        save(meta)
        raise RuntimeError('Bounded V3 preflight failed; see non-secret result artifact') from None


if __name__ == '__main__':
    main(Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve())
