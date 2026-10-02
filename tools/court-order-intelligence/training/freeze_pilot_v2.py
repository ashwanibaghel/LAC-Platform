"""One-way checksum freeze after source, runtime, context and leakage gates."""
import hashlib
import json
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

from audit_v2_foundation import ROOT, related_identities
from audit_v2_v1_overlap import bounded_duplicate
from foundation import write_json, write_jsonl
from leakage import require_one_split


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_frozen(out):
    manifest = json.loads((out / 'manifest.json').read_text(encoding='utf-8'))
    if not manifest['protected_splits_frozen']:
        raise ValueError('Protected split freeze absent')
    for name, checksum in manifest['files'].items():
        path = (out / name).resolve()
        if not path.is_relative_to(out.resolve()) or sha(path) != checksum:
            raise ValueError('Frozen V2 file changed: ' + name)
    for name, checksum in manifest['review_files'].items():
        if sha(ROOT / name) != checksum:
            raise ValueError('Frozen V2 review changed: ' + name)
    return manifest


def check_v1_passages(examples, old):
    prior = {(p['text'], split) for split, records in old.items() for e in records for p in e['provenance']}
    current = {(p['text'], e['matter_id'], e['split']) for e in examples for p in e['provenance']}
    for text, matter, split in sorted(current):
        for other, previous_split in sorted(prior):
            if (split != 'train' or previous_split != 'train') and bounded_duplicate(text, other):
                raise ValueError(f'V1 protected passage overlap: {matter} ({split}) / V1 {previous_split}; no split reassignment')


def main():
    out = ROOT / 'pilot-v2'
    if (out / 'manifest.json').exists():
        print(json.dumps({'existing_freeze_verified': True, 'counts': verify_frozen(out)['counts']}))
        return
    audit = json.loads((out / 'pre-freeze-audit.json').read_text(encoding='utf-8'))
    context = json.loads((out / 'all-splits-context-audit.json').read_text())
    overlap = json.loads((out / 'v1-native-overlap-audit.json').read_text())
    if not all([audit['leakage_group_one_split'], context['all_splits_checked'], overlap['protected_boundary_pass'], overlap['fresh_training_overlap_pass']]):
        raise ValueError('Incomplete frozen-data safety gates')
    examples = []
    for split in ('train', 'validation', 'blind'):
        name = split + '-candidate.jsonl'
        if sha(out / name) != context['input_sha256'][name]:
            raise ValueError('Candidate changed after tokenizer audit')
        examples.extend(json.loads(line) for line in (out / name).read_text(encoding='utf-8').splitlines())
    if context['examples_checked'] != len(examples) or not all(r['training_fits'] and r['existing_pilot_inference_prompt_fits'] for r in context['results']):
        raise ValueError('Context gate failed; no truncation permitted')
    require_one_split({e['id']: e['leakage_group'] for e in examples}, {e['id']: e['split'] for e in examples})
    old = {s: [json.loads(line) for line in (ROOT / 'pilot-v1' / (s + '.jsonl')).read_text(encoding='utf-8').splitlines()]
           for s in ('train', 'validation', 'blind')}
    check_v1_passages(examples, old)
    files = {}
    for split in ('train', 'validation', 'blind'):
        name = split + '.jsonl'
        if (out / name).exists():
            raise ValueError('Refusing to overwrite previously exported set')
        write_jsonl(out / name, [e for e in examples if e['split'] == split])
        files[name] = sha(out / name)
    source_versions = {p['source_version_id'] for e in examples for p in e['provenance']}
    ledger = {}
    for version in sorted(source_versions):
        record = json.loads((ROOT / 'local-private/pilot-v2-source-audit' / (version + '.json')).read_text(encoding='utf-8'))
        ledger[version] = {k: record[k] for k in ('case', 'url', 'sha256', 'reserved_split')}
        ledger[version]['related_identities'] = sorted(related_identities(record))
        ledger[version]['native_source_record_sha256'] = sha(ROOT / 'local-private/pilot-v2-source-audit' / (version + '.json'))
    write_json(out / 'source-ledger.json', ledger)
    for name in ('source-ledger.json', 'all-splits-context-audit.json', 'pre-freeze-audit.json', 'v1-native-overlap-audit.json'):
        files[name] = sha(out / name)
    review_names = ['annotations/v2_review.py', 'annotations/v2_tasks.py', 'annotations/v2_eval_review.py']
    inventory = {e['matter_id']: {'split': e['split'], 'leakage_group': e['leakage_group']} for e in examples}
    manifest = {'purpose': ['PILOT_V2', 'BOUNDED_EVALUATION', 'NO_DEPLOYMENT'],
                'protected_splits_frozen': True, 'frozen_at_utc': datetime.now(timezone.utc).isoformat(),
                'model': context['model'], 'revision': context['revision'], 'files': files,
                'review_files': {n: sha(ROOT / n) for n in review_names},
                'counts': dict(Counter(e['split'] for e in examples)), 'matter_inventory': inventory,
                'leakage_group_one_split': True, 'v1_native_and_passage_overlap_pass': True,
                'gold_authored_before_model_outputs': True, 'post_blind_tuning_allowed': False,
                'private_workbook_included': False, 'full_native_pages_included': False,
                'training_started': False, 'gpu_launch_allowed': True,
                'claims_inputs': 'Reviewed structured evidence, not end-to-end extraction evaluation',
                'training_current_position_examples': sum(e['split'] == 'train' and e['task'] == 'multi_order_current_position' for e in examples),
                'training_current_position_matters': sorted({e['matter_id'] for e in examples if e['split'] == 'train' and e['task'] == 'multi_order_current_position'})}
    write_json(out / 'manifest.json', manifest)
    print(json.dumps({'freeze': 'PASS', 'counts': manifest['counts'], 'current_position': manifest['training_current_position_examples']}))


if __name__ == '__main__':
    main()
