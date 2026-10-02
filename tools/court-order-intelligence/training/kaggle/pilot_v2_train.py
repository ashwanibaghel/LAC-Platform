"""One V2 fit; stock/V1/V2 use identical frozen tasks, grammar and safety layer."""
import gc
import hashlib
import json
import shutil
import sys
import time
from contextlib import nullcontext
from pathlib import Path

from pilot_v2_contract import verify_bundle, verify_config, locate_v1_adapter
from smoke_contract import MODEL, REVISION, parse_runtime_output
from reload_smoke import inference_messages, schema_prefix
from pilot_train import score, aggregate
from train_smoke import main as train, save


def detailed_score(record, payload, accepted):
    result = score(record, payload, accepted)
    result['wrong_category'] = result['wrong_field'] = result['wrong_scope'] = 0
    result['submission_as_court'] = 0
    if record['contract'] == 'anchors':
        expected = {r['anchorId']: r for r in record['expected']['facts']}
        for fact in (payload or {}).get('facts', []):
            gold = expected.get(fact['anchorId'])
            if gold:
                result['wrong_category'] += fact['category'] != gold['category']
                result['wrong_field'] += fact['field'] != gold['field']
                result['wrong_scope'] += fact['scope'] != gold['scope']
                result['submission_as_court'] += 'SUBMISSION' in gold['category'] and fact['category'] in ('COURT_DIRECTION', 'COURT_FINDING', 'COURT_OBSERVATION')
    if record['task'] == 'office_action_detection':
        wanted = {c['factId'] for c in record['expected']['claims']}
        got = {c['factId'] for c in (payload or {}).get('claims', [])}
        result['office_action_recall'] = [len(wanted & got) if accepted else 0, len(wanted)]
        result['office_action_precision'] = [len(wanted & got) if accepted else 0, len(got)]
        result['missed_office_actions'] = len(wanted - got) if accepted else len(wanted)
    return result


def metrics(rows):
    report = aggregate(rows)
    for key in ('wrong_category', 'wrong_field', 'wrong_scope', 'submission_as_court', 'missed_office_actions'):
        report[key] = sum(r.get(key, 0) for r in rows)
    for key in ('office_action_recall', 'office_action_precision'):
        report[key] = [sum(r.get(key, [0, 0])[i] for r in rows) for i in (0, 1)]
    return report


def evaluate(bundle, output, v1_adapter):
    import torch
    from transformers import AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig
    from peft import PeftModel
    config = verify_config(json.loads((bundle / 'training_config_pilot_v2.json').read_text()))
    schemas = json.loads((bundle / 'target.schemas.json').read_text())
    metadata = json.loads((output / 'run-metadata.json').read_text())
    if not metadata['adapter_reload_passed'] or not metadata['resume_result']['passed'] or metadata['skipped_oversize_ids']:
        raise ValueError('V2 training/reload/full-data gate incomplete')
    state = torch.load(output / 'checkpoints/checkpoint-80/optimizer.pt', map_location='cpu', weights_only=True)
    updates = {int(x['step']) for x in state['state'].values() if 'step' in x}
    if len(updates) != 1:
        raise ValueError('Inconsistent durable optimizer steps')
    applied = next(iter(updates))
    metadata.update({'trainer_global_steps': 80, 'applied_optimizer_updates': applied,
                     'scaler_skipped_updates': 80 - applied, 'pilot_training_gate': 'PASS' if applied >= 76 else 'FAIL_RECURRENT_SKIPS'})
    save(output / 'run-metadata.json', metadata)
    if applied < 76:
        raise ValueError('Recurrent optimizer skips; do not claim a valid Pilot fit')
    del state
    gc.collect()
    torch.cuda.empty_cache()
    dtype = torch.bfloat16 if torch.cuda.is_bf16_supported(including_emulation=False) else torch.float16
    tokenizer = AutoTokenizer.from_pretrained(MODEL, revision=REVISION, trust_remote_code=False)
    quant = BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type='nf4', bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=dtype)
    base = AutoModelForCausalLM.from_pretrained(MODEL, revision=REVISION, trust_remote_code=False,
        torch_dtype=dtype, quantization_config=quant, device_map={'': 0}, attn_implementation='sdpa')
    model = PeftModel.from_pretrained(base, str(output / 'adapter'), adapter_name='pilot_v2', is_trainable=False)
    model.load_adapter(str(v1_adapter), adapter_name='pilot_v1', is_trainable=False)
    model.eval()
    outputs, summary = [], {}
    started = time.monotonic()
    for mode in ('stock_4b', 'pilot_v1', 'pilot_v2'):
        summary[mode] = {}
        if mode != 'stock_4b':
            model.set_adapter(mode)
        with model.disable_adapter() if mode == 'stock_4b' else nullcontext():
            for split in ('v1_regression', 'validation', 'blind'):
                rows = []
                for line in (bundle / (split + '.jsonl')).read_text().splitlines():
                    record = json.loads(line)
                    prompt = tokenizer.apply_chat_template(inference_messages(record, schemas), tokenize=True,
                        add_generation_prompt=True, return_tensors='pt').to('cuda:0')
                    if prompt.shape[1] > config['max_sequence_length']:
                        raise ValueError('Held-out prompt exceeds frozen cap; no truncation')
                    with torch.inference_mode():
                        tokens = model.generate(input_ids=prompt, attention_mask=torch.ones_like(prompt),
                            do_sample=False, max_new_tokens=512, pad_token_id=tokenizer.eos_token_id,
                            prefix_allowed_tokens_fn=schema_prefix(tokenizer, schemas[record['contract']]))
                    raw = tokenizer.decode(tokens[0, prompt.shape[1]:], skip_special_tokens=True).strip()
                    payload, accepted, error = None, False, None
                    try:
                        payload = parse_runtime_output(raw, record, schemas, bundle / 'runtime')
                        accepted = True
                    except Exception as failure:
                        error = type(failure).__name__
                        try:
                            payload = json.loads(raw)
                        except ValueError:
                            pass
                    result = detailed_score(record, payload, accepted)
                    rows.append(result)
                    outputs.append({'model': mode, 'split': split, 'raw_output': raw,
                                    'parsed_output': payload, 'parser_error': error, **result})
                    save(output / 'evaluation-outputs.json', outputs)
                summary[mode][split] = metrics(rows)
                save(output / 'pilot-v2-evaluation-partial.json', summary)
    save(output / 'pilot-v2-evaluation.json', {'base': MODEL, 'revision': REVISION,
        'first_frozen_evaluation': True, 'post_blind_tuning': False, 'output_repair': False,
        'same_4bit_base_and_safety_layer': True, 'claims_input_is_oracle_structured_evidence': True,
        'v1_regression_is_validation_plus_blind_not_81_blind': True,
        'metrics': summary, 'elapsed_seconds': round(time.monotonic() - started, 3),
        'deployment_gate': 'NOT_APPROVED_PENDING_SOURCE_CHAIN_REVIEW'})


if __name__ == '__main__':
    bundle = Path(sys.argv[sys.argv.index('--bundle') + 1]).resolve()
    output = Path(sys.argv[sys.argv.index('--output') + 1]).resolve()
    # Fail before training if V1 comparison cannot be performed as specified.
    v1_adapter = locate_v1_adapter('/kaggle/input')
    verify_bundle(bundle)
    try:
        train(verify_bundle, verify_config, 'training_config_pilot_v2.json')
        evaluate(bundle, output, v1_adapter)
    finally:
        if output.is_dir():
            save(output / 'artifact-checksums.json', {p.relative_to(output).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in output.rglob('*') if p.is_file() and p.name != 'artifact-checksums.json'})
            shutil.make_archive(str(output) + '-artifacts', 'zip', output)
