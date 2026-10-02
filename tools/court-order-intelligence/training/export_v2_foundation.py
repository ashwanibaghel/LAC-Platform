"""Partial TRAIN-only V2 export; cannot produce a Kaggle-ready/frozen bundle."""
import json
from collections import Counter
from types import SimpleNamespace

from annotations import v2_tasks
from audit_v2_foundation import audit, ROOT
from foundation import compile_example, write_json, write_jsonl
from validate_gold import validate_example
from anchors import expand
from leakage import groups


def prepare():
    report = audit()
    sources = {version: {'matter_id': s['case'], 'order_date': s['order_date'],
                         'page_count': s['pages'], 'sha256': s['sha256'], 'url': s['url']}
               for version, s in report['ledger'].items()}
    corpus = SimpleNamespace(SOURCES=sources, PASSAGES=v2_tasks.PASSAGES,
                             EXAMPLES=v2_tasks.EXAMPLES, REVIEW=v2_tasks.REVIEW,
                             VERIFIED_IDS=v2_tasks.VERIFIED_IDS)
    return corpus, report


def export():
    corpus, source_report = prepare()
    records = {version: json.loads((ROOT / 'local-private/pilot-v2-source-audit' / (version + '.json')).read_text(encoding='utf-8'))
               for version in corpus.SOURCES}
    examples, quarantined = [], []
    for annotation in corpus.EXAMPLES:
        example = compile_example(annotation, corpus)
        try:
            validate_example(example, corpus)
            if 'anchors' in example['input']:
                version = example['provenance'][0]['source_version_id']
                expand(example['target'], example['input']['anchors'],
                       {int(k): v for k, v in records[version]['pages'].items()})
        except ValueError as error:
            quarantined.append({'id': example['id'], 'state': 'QUARANTINED',
                                'reason': str(error), 'truthful_labels_changed': False})
            continue
        examples.append(example)
    matters = {s['matter_id']: {'connections': [], 'common_judgments': []} for s in corpus.SOURCES.values()}
    for s in source_report['ledger'].values():
        matters[s['case']]['connections'].extend(s['related_identities'])
    grouping = groups(examples, matters)
    out = ROOT / 'pilot-v2'
    write_jsonl(out / 'train-foundation.jsonl', examples)
    write_json(out / 'task-quarantine.json', quarantined)
    stats = {'state': 'PARTIAL_TRAIN_FOUNDATION_NOT_TRAINING_READY',
             'training_started': False, 'protected_splits_frozen': False,
             'model': source_report['model'], 'revision': source_report['revision'],
             'verified_training_foundation_examples': len(examples),
             'quarantined_task_examples': len(quarantined),
             'tasks': dict(Counter(e['task'] for e in examples)),
             'languages': dict(Counter(e['language'] for e in examples)),
             'positive_office_action_examples': sum(e['task'] == 'office_action_detection' and bool(e['target']['claims']) for e in examples),
             'multi_order_position_examples': sum(e['task'] == 'multi_order_current_position' for e in examples),
             'leakage_groups': grouping,
             'runtime_claims_inputs_are_reviewed_structured_evidence_not_end_to_end_extraction': True,
             'workbook_uploaded': False, 'gpu_launch_allowed': False}
    write_json(out / 'task-foundation-stats.json', stats)
    print(json.dumps({k: v for k, v in stats.items() if k != 'leakage_groups'}, indent=2))
    return examples, quarantined, corpus


if __name__ == '__main__':
    export()
