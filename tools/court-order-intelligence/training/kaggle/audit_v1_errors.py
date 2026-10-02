"""Read-only, split-aware V1 diagnosis. Never changes targets or training data.

The original wrong_role_or_scope score compares whole tuples, including FIELD.
This audit separates category/field/scope mistakes and parser rejection; original
published scores remain untouched. Blind IDs stay in the local audit only.
"""
import argparse
import hashlib
import json
from collections import Counter
from pathlib import Path

from smoke_contract import parse_runtime_output


def audit(rows, records, schemas, runtime):
    summary, details = {}, []
    seen = set()
    for row in rows:
        key = (row['model'], row['id'])
        if key in seen:
            raise ValueError('Duplicate model/example output')
        seen.add(key)
        record = records[row['id']]
        if row['split'] != record['_split'] or row['matter_id'] != record['matter_id']:
            raise ValueError('Output identity/split mismatch')
        counter = summary.setdefault(row['model'], {}).setdefault(row['split'], Counter())
        counter['examples'] += 1
        counter['exact_target'] += bool(row['exact_target'])
        if row['exact_target']:
            continue
        counter['failed_examples'] += 1
        buckets, differences, error = [], {}, None
        try:
            prediction = parse_runtime_output(row['raw_output'], record, schemas, runtime)
        except Exception as failure:
            # Frozen parser messages are contract diagnostics, not model prompts.
            error = str(failure).splitlines()[0][:240]
            buckets.append('parser_rejection')
            prediction = row.get('parsed_output') or {}
        if record['contract'] == 'anchors':
            buckets.append('attribution_extraction')
            expected = {x['anchorId']: x for x in record['expected']['facts']}
            actual = {x['anchorId']: x for x in prediction.get('facts', [])}
            for name in ('category', 'field', 'scope'):
                mismatches = [i for i in expected.keys() & actual.keys()
                              if actual[i].get(name) != expected[i][name]]
                differences[name + '_mismatches'] = len(mismatches)
                counter[name + '_mismatches'] += len(mismatches)
                if mismatches:
                    buckets.append('role' if name == 'category' else name)
            differences['missing_anchors'] = len(expected.keys() - actual.keys())
            differences['extra_anchors'] = len(actual.keys() - expected.keys())
        else:
            wanted = {c['factId'] for c in record['expected']['claims']}
            got = {c['factId'] for c in prediction.get('claims', [])}
            differences = {'missing_facts': sorted(wanted - got),
                           'extra_facts': sorted(got - wanted)}
            task = record['task']
            if task == 'multi_order_current_position':
                buckets.append('multi_order_synthesis')
            elif task == 'office_action_detection':
                buckets.append('office_action')
                if wanted - got:
                    buckets.append('office_action_miss')
                if got - wanted:
                    buckets.append('office_action_extra')
            elif task == 'date_specific_retrieval_or_QA':
                buckets.append('date_qa')
            else:
                buckets.append(task)
        for bucket in set(buckets):
            counter['bucket:' + bucket] += 1
        details.append({'model': row['model'], 'split': row['split'], 'id': row['id'],
                        'matter_id': row['matter_id'], 'buckets': sorted(set(buckets)),
                        'differences': differences, 'parser_diagnostic': error,
                        'training_reuse_allowed': False})
    return {'summary': summary, 'failures': details,
            'buckets_overlap': True, 'targets_modified': False,
            'blind_policy': 'Observed V1 blind remains excluded from training; fresh untouched V2 blind required.'}


def load_records(bundle):
    records = {}
    for split in ('validation', 'blind'):
        for line in (bundle / (split + '.jsonl')).read_text(encoding='utf-8').splitlines():
            record = json.loads(line)
            if record['id'] in records:
                raise ValueError('Duplicate held-out ID')
            record['_split'] = split
            records[record['id']] = record
    return records


def verify_artifacts(directory):
    hashes = json.loads((directory / 'artifact-checksums.json').read_text())
    missing = []
    for relative, expected in hashes.items():
        target = (directory / relative).resolve()
        if not target.is_relative_to(directory.resolve()):
            raise ValueError('Artifact path outside frozen directory')
        if not target.is_file():
            missing.append(relative)
        elif hashlib.sha256(target.read_bytes()).hexdigest() != expected:
            raise ValueError('Artifact hash mismatch: ' + relative)
    if missing:
        raise ValueError('Missing frozen artifacts: ' + ', '.join(missing))
    return len(hashes)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--bundle', type=Path, required=True)
    parser.add_argument('--artifacts', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--require-complete-freeze', action='store_true')
    parser.add_argument('--public-summary', type=Path)
    args = parser.parse_args()
    count = verify_artifacts(args.artifacts) if args.require_complete_freeze else None
    rows = json.loads((args.artifacts / 'evaluation-outputs.json').read_text())
    report = audit(rows, load_records(args.bundle),
                   json.loads((args.bundle / 'target.schemas.json').read_text()), args.bundle / 'runtime')
    report['verified_artifact_count'] = count
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    if args.public_summary:
        hashes = json.loads((args.artifacts / 'artifact-checksums.json').read_text())
        public = {key: value for key, value in report.items() if key != 'failures'}
        public['adapter_sha256'] = hashes['adapter/adapter_model.safetensors']
        public['source_artifact_manifest_sha256'] = hashlib.sha256(
            (args.artifacts / 'artifact-checksums.json').read_bytes()).hexdigest()
        public['evaluation_output_sha256'] = hashes['evaluation-outputs.json']
        args.public_summary.parent.mkdir(parents=True, exist_ok=True)
        args.public_summary.write_text(json.dumps(public, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report['summary'], indent=2))
