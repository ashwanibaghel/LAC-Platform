"""Compile only explicit targeted V3 source annotations, never infer gold.

Offline, append-source review; current outputs are CANDIDATES, not a final
freeze or GPU authorization. Legacy artifacts and selected targets unchanged.
"""
from copy import deepcopy
from datetime import datetime
import json
from types import SimpleNamespace

from foundation import ROOT, compile_example, canonical, digest, write_json, write_jsonl
from annotations import v3_targeted_review as review
from annotations.v3_reuse_context import enrich
from anchors import source_header
from schema.contracts import TASKS, ANCHOR_SCHEMA, ANSWER_SCHEMA
from v3_contract import parse_v3_output, context_for_v3
from audit_v3_composite import audit, counts, group_sources, rows, verify_frozen


def compile_targeted():
    records, sources = {}, {}
    for key, (day, _) in review.REVIEWED.items():
        source = json.loads((ROOT / 'local-private/pilot-v3-source-audit' / (key + '.json')).read_text(encoding='utf-8'))
        _, native = source_header({int(p): t for p, t in source['pages'].items()})
        import re
        stamp = re.search(r'%\s*(\d{2}\.\d{2}\.\d{4})', next(iter(native.values()))) if native else None
        if not stamp or datetime.strptime(stamp[1], '%d.%m.%Y').date().isoformat() != day:
            raise ValueError('Native order date differs from targeted annotation')
        records[key] = source
        sources[key] = dict(matter_id=source['case'], order_date=day, sha256=source['sha256'], url=source['url'])
    corpus = SimpleNamespace(SOURCES=sources, PASSAGES=review.PASSAGES,
        REVIEW=dict(reviewer='Codex independent full-native-source audit', method='Explicit page/passages; no model/gold repair',
                    reviewed_on='2026-10-03', annotation_version='v3-targeted-native-review-1'),
        VERIFIED_IDS={a[0] for a in review.EXAMPLES})
    accepted, quarantine = [], []
    for annotation in review.EXAMPLES:
        original = compile_example(annotation, corpus)
        example = deepcopy(original)
        for p, entry in zip(example['provenance'], example['input'].get('availableEvidence', [])):
            version, page, text, role, field, scope, actor = review.PASSAGES[p['passage_id']]
            source = records[version]
            if (p['sha256'] != source['sha256'] or text not in source['pages'][str(page)]
                    or p['url'] != source['url'] or p['matter_id'] != source['case']):
                raise ValueError('Targeted native evidence binding failed')
            entry.update(field=field, actor=actor)
        # Anchors also require independent exact native binding, not just SHA.
        for p in example['provenance']:
            if p['text'] not in records[p['source_version_id']]['pages'][str(p['page'])]:
                raise ValueError('Targeted anchor passage absent from native page')
        example = enrich(example, review.PASSAGES, records)
        if 'availableEvidence' in example['input']:
            as_of = max(p['order_date'] for p in example['provenance'])
            for p, entry in zip(example['provenance'], example['input']['availableEvidence']):
                if p['passage_id'] in review.OPEN:
                    entry.update(directionLifecycle='OPEN' if p['order_date'] == as_of else 'UNKNOWN',
                                 lifecycleAsOf=as_of, lifecycleBasis='Explicit as-issued source review; not live completion')
        example['input'] = context_for_v3(example['input'])
        example.update(contract=TASKS[example['task']], reuseOrigin='pilot-v3-targeted',
                       strongCurrentPositionReview=review.STRONG.get(example['id']))
        if example['target'] != original['target'] or example['provenance'] != original['provenance']:
            raise ValueError('Targeted compiler changed reviewed gold')
        try:
            parse_v3_output(canonical(example['target']), example,
                            {'anchors': ANCHOR_SCHEMA, 'claims': ANSWER_SCHEMA}, ROOT.parent)
            accepted.append(example)
        except ValueError as error:
            quarantine.append(dict(id=example['id'], reason=str(error), training_eligible=False, targets_changed=False))
    return accepted, quarantine


def main():
    frozen = verify_frozen()
    legacy, _, prior = audit()
    new, quarantine = compile_targeted()
    from annotations.v3_rejected_holdout_reservations import REJECTED
    sources = {}
    for directory in ('pilot-source-audit', 'pilot-v2-source-audit', 'pilot-v3-source-audit'):
        for path in (ROOT / 'local-private' / directory).glob('*.json'):
            if directory == 'pilot-v3-source-audit' and path.stem in REJECTED:
                continue  # Quarantined reservation is neither training nor held-out gold.
            sources[directory + ':' + path.stem] = json.loads(path.read_text(encoding='utf-8'))
    protected_rows, protected = [], set()
    for version in ('pilot-v1', 'pilot-v2'):
        for split in ('validation', 'blind'):
            for row in rows(ROOT / version / (split + '.jsonl')):
                protected.add(row['matter_id'])
                protected_rows.append(dict(matter_id=row['matter_id'], provenance=row['provenance']))
    protected.update(json.loads((ROOT / 'pilot-v1/splits.json').read_text())['matter_inventory']['old_development_only'])
    from v3_candidates import CANDIDATES
    protected.update(case for key, case, split in CANDIDATES if split != 'train' and key not in REJECTED)
    ids, blocked, relationships, members = group_sources(sources, legacy + new + protected_rows,
        protected, [v for v in prior['group_members'].values()])
    combined = []
    exclusions = []
    for row in legacy + new:
        row['leakage_group'] = ids[row['matter_id']]
        if row['leakage_group'] in blocked:
            exclusions.append(dict(id=row['id'], reason='Full-source protected group overlap'))
        else:
            combined.append(row)
    from annotations.v3_strong_reuse_review import STRONG_REUSE
    strong = [row for row in combined if row['id'] in STRONG_REUSE or row.get('strongCurrentPositionReview')]
    report = dict(state='TARGETED_CANDIDATES_NOT_FROZEN_NO_GPU', legacy_recovery=prior['precision_recovery'],
                  combined_counts=counts(combined), new_candidates=len(new), quarantine=quarantine,
                  unsafe_unfrozen_holdout_reservations_quarantined=REJECTED,
                  protected_exclusions=exclusions, source_relationships=relationships, group_members=members,
                  certified_strong_current_position=dict(examples=len(strong), groups=len({r['leakage_group'] for r in strong})),
                  legacy_files_unchanged=verify_frozen() == frozen, decision='NO-GO')
    if not report['legacy_files_unchanged']:
        raise ValueError('Frozen legacy changed')
    write_jsonl(ROOT / 'pilot-v3/train-composite-candidate.jsonl', combined)
    write_json(ROOT / 'pilot-v3/targeted-gap-audit.json', report)
    print(json.dumps({k: report[k] for k in ('state', 'combined_counts', 'new_candidates', 'quarantine', 'protected_exclusions',
          'certified_strong_current_position', 'legacy_files_unchanged', 'decision')}, indent=2))


if __name__ == '__main__':
    main()
