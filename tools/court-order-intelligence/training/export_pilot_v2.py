"""Fail-closed V2 pre-freeze audit. Protected gold never enters training.

No inference, network, model weights or GPU operation. Full native sources stay
local-private; the exported examples contain only reviewed public passages.
"""
import json
from collections import Counter
from types import SimpleNamespace

from annotations import v2_eval_review
from audit_v2_foundation import ROOT, audit_record, protected_matters, related_identities
from export_v2_foundation import prepare
from foundation import compile_example, canonical, digest, write_json, write_jsonl
from validate_gold import validate_example
from anchors import expand
from leakage import groups, require_one_split
from v2_candidates import CANDIDATES, FOLLOWUPS


def audited_examples():
    training, report = prepare()
    records = {p.stem: json.loads(p.read_text(encoding='utf-8'))
               for p in (ROOT / 'local-private/pilot-v2-source-audit').glob('*.json')}
    candidates = {key: (case, split) for key, case, split, _ in CANDIDATES + FOLLOWUPS}
    sources = {}
    v1 = json.loads((ROOT / 'pilot-v1/splits.json').read_text())
    protected = set(v1['matter_inventory']['matters']) | set(v1['matter_inventory']['old_development_only'])
    protected |= {'W.P.(C) 2686/2018', 'W.P.(C) 7817/2017', 'W.P.(C) 5796/2026'}
    protected |= {s['matter_id'] for s in training.SOURCES.values()}
    for version, (date, passages) in v2_eval_review.REVIEWED.items():
        record = records[version]
        case, split = candidates[version]
        audit_record(record, case, date, passages, protected, required_split=split)
        sources[version] = {'matter_id': case, 'order_date': date, 'page_count': len(record['pages']),
                            'sha256': record['sha256'], 'url': record['url']}
    evaluation = SimpleNamespace(SOURCES=sources, PASSAGES=v2_eval_review.PASSAGES,
        EXAMPLES=v2_eval_review.EXAMPLES, REVIEW=v2_eval_review.REVIEW,
        VERIFIED_IDS=v2_eval_review.VERIFIED_IDS)
    examples, quarantine = [], list(v2_eval_review.QUARANTINED)
    for corpus in (training, evaluation):
        for annotation in corpus.EXAMPLES:
            example = compile_example(annotation, corpus)
            try:
                validate_example(example, corpus)
                if 'anchors' in example['input']:
                    version = example['provenance'][0]['source_version_id']
                    expand(example['target'], example['input']['anchors'],
                           {int(k): v for k, v in records[version]['pages'].items()})
            except ValueError as error:
                quarantine.append({'id': example['id'], 'reason': str(error),
                                   'state': 'QUARANTINED', 'truthful_labels_changed': False})
                continue
            example['split'] = candidates[example['provenance'][0]['source_version_id']][1]
            examples.append(example)
    matters = {e['matter_id']: {'connections': [], 'common_judgments': []} for e in examples}
    for record in records.values():
        if record['case'] in matters:
            matters[record['case']]['connections'].extend(related_identities(record))
    grouping = groups(examples, matters)
    require_one_split(grouping, {e['id']: e['split'] for e in examples})
    for e in examples:
        e['leakage_group'] = grouping[e['id']]
    return examples, quarantine


def main():
    examples, quarantine = audited_examples()
    out = ROOT / 'pilot-v2'
    # Preparation is separate from irreversible freeze; context checks must
    # pass on all protected records before accepting a freeze manifest.
    for split in ('train', 'validation', 'blind'):
        write_jsonl(out / (split + '-candidate.jsonl'), [e for e in examples if e['split'] == split])
    report = {'state': 'PRE_FREEZE_ALL_SPLITS_AUDITED', 'gpu_launch_allowed': False,
              'counts': dict(Counter(e['split'] for e in examples)),
              'tasks': {s: dict(Counter(e['task'] for e in examples if e['split'] == s))
                        for s in ('train', 'validation', 'blind')},
              'quarantined': quarantine, 'protected_gold_never_trained': True,
              'leakage_group_one_split': True}
    write_json(out / 'pre-freeze-audit.json', report)
    print(json.dumps({k: v for k, v in report.items() if k != 'quarantined'}, indent=2))


if __name__ == '__main__':
    main()
