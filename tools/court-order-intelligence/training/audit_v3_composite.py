"""Offline composite TRAIN audit. Frozen V1/V2 inputs are strictly read-only.

Source evidence/labels are never repaired. Missing V3 lifecycle stays UNKNOWN;
incompatible records enter a separate review queue, not the runnable pool. No
model, acquisition, GPU, credentials, workbook or application/DB access.
"""
from collections import Counter, defaultdict
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent))
sys.path.insert(0, str(ROOT / 'kaggle'))
from foundation import canonical, digest, compile_example
from leakage import near_duplicate, normalized, shingles
from schema.contracts import TASKS, ANCHOR_SCHEMA, ANSWER_SCHEMA
from smoke_contract import parse_runtime_output
from v3_contract import context_for_v3, parse_v3_output
from audit_pilot_overlap import body
from annotations import pilot_review, v2_tasks
from v3_candidates import CANDIDATES


def load(path):
    return json.loads(path.read_text(encoding='utf-8'))


def rows(path):
    return [json.loads(line) for line in path.read_text(encoding='utf-8').splitlines() if line.strip()]


def verify_frozen(root=ROOT):
    verified = {}
    for version in ('pilot-v1', 'pilot-v2'):
        manifest = load(root / version / 'manifest.json')
        for relative, expected in manifest['files'].items():
            path = root / version / relative
            if not path.resolve().is_relative_to((root / version).resolve()):
                raise ValueError('Frozen manifest path escapes dataset')
            actual = hashlib.sha256(path.read_bytes()).hexdigest()
            if actual != expected:
                raise ValueError(f'Frozen file changed: {version}/{relative}')
            verified[f'{version}/{relative}'] = actual
        for relative, expected in manifest.get('review_files', {}).items():
            path = (root / relative).resolve()
            if not path.is_relative_to(root.resolve()):
                raise ValueError('Frozen review path escapes training root')
            actual = hashlib.sha256(path.read_bytes()).hexdigest()
            if actual != expected:
                raise ValueError('Frozen annotation changed: ' + relative)
            verified[relative] = actual
    return verified


def citation_keys(text):
    """Conservative links, including quoted/common cases; never infer independence.

    A citation is enough to group conservatively, not proof of legal connection.
    Common named authorities are also audited when no neutral citation is given.
    """
    keys = set()
    patterns = (
        ('W.P.(C)', r'W\.?\s*P\.?\s*\(\s*C\s*\)\s*(?:No\.?\s*)?(\d+)\s*/\s*(\d{4})'),
        ('CONT.CAS(C)', r'CONT\.?\s*CAS\.?\s*\(\s*C\s*\)\s*(\d+)\s*/\s*(\d{4})'),
        ('LPA', r'\bLPA\s*(?:No\.?\s*)?(\d+)\s*/\s*(\d{4})'),
        ('CS(OS)', r'CS\s*\(\s*OS\s*\)\s*(\d+)\s*/\s*(\d{4})'),
        ('CIVIL_APPEAL', r'Civil\s+Appeal\s+(?:No(?:s)?\.?\s*)?(\d+)\s*(?:/|of)\s*(\d{4})'),
    )
    for kind, pattern in patterns:
        keys.update(f'{kind} {number}/{year}' for number, year in re.findall(pattern, text, re.I))
    keys.update('JUDGMENT ' + normalized(s) for s in re.findall(
        r'\b\d{4}\s+(?:INSC|SCC\s+OnLine\s+(?:SC|Del))\s+\d+|\(\d{4}\)\s*\d+\s*SCC\s*\d+', text, re.I))
    for label, pattern in (
        ('BSK_REALTORS', r'B\.?\s*S\.?\s*K\.?\s+Realtors'),
        ('DDA_TEJPAL', r'\bTej\s*Pal\b'),
        ('OKAYA_INFOCOM', r'Okaya\s+Infocom'),
        ('JEEVANTIKA_ORGANIC', r'Jeevantika\s+Organic'),
    ):
        if re.search(pattern, text, re.I):
            keys.add('COMMON_AUTHORITY ' + label)
    return keys


def semantic_key(example):
    """Conservative semantic dedupe, ignoring IDs, translations and query wording.

    Same task, ordered dated/page-bound evidence, reviewed labels/selection and
    outcome has one canonical training copy. Different tasks/chronology survive.
    Multilingual paraphrases with identical supervision do NOT pad counts.
    """
    evidence = [(p['order_date'], p['page'], normalized(p['text'])) for p in example['provenance']]
    labels = [(e.get('category'), e.get('scope')) for e in example['input'].get('availableEvidence', [])]
    return digest(canonical([example['task'], evidence, labels, example['target'], example['outcome']]))


def deduplicate(examples):
    seen, retained, duplicates = {}, [], []
    for example in examples:
        key = semantic_key(example)
        if key in seen:
            if canonical(seen[key]['target']) != canonical(example['target']):
                raise ValueError('Conflicting duplicate supervision')
            duplicates.append({'id': example['id'], 'origin': example['reuseOrigin'],
                               'canonical_id': seen[key]['id'],
                               'canonical_origin': seen[key]['reuseOrigin'], 'semantic_sha256': key})
        else:
            seen[key] = example
            retained.append(example)
    return retained, duplicates


def counts(examples):
    def selected(task, positive=None):
        return [e for e in examples if e['task'] == task and
                (positive is None or bool(e['target'].get('claims', e['target'].get('facts'))) == positive)]

    def describe(values):
        return {'examples': len(values), 'groups': len({e['leakage_group'] for e in values})}

    return {
        'total_examples': len(examples), 'independent_leakage_groups': len({e['leakage_group'] for e in examples}),
        'matters': len({e['matter_id'] for e in examples}),
        'reused_by_origin': dict(Counter(e['reuseOrigin'] for e in examples)),
        'tasks': dict(Counter(e['task'] for e in examples)),
        'attribution': describe(selected('attribution_classification')),
        'multi_order_current_position': describe(selected('multi_order_current_position')),
        'positive_lac_action': describe(selected('office_action_detection', True)),
        'empty_lac_action': describe(selected('office_action_detection', False)),
        'compliance': describe(selected('compliance_state')),
        'positive_compliance': describe(selected('compliance_state', True)),
        'positive_date_qa': describe(selected('date_specific_retrieval_or_QA', True)),
        'empty_date_qa': describe(selected('date_specific_retrieval_or_QA', False)),
        'languages': dict(Counter(e['language'] for e in examples)),
    }


def group_sources(sources, examples, protected_matters, known_groups=()):
    """Union frozen groups, identities, SHA, URL, passages and full native body.

    Only provenance from protected rows is needed: targets/model outputs are
    neither inputs to this audit nor imported into new gold.
    """
    parent, reasons = {}, []

    def find(m):
        parent.setdefault(m, m)
        if parent[m] != m:
            parent[m] = find(parent[m])
        return parent[m]

    def union(a, b, reason):
        a, b = find(a), find(b)
        if a != b:
            parent[max(a, b)] = min(a, b)
            reasons.append(reason)

    for matter in protected_matters | {e['matter_id'] for e in examples}:
        find(matter)
    for group in known_groups:
        for member in group[1:]:
            union(group[0], member, {'kind': 'frozen_or_reviewed_group', 'matters': [group[0], member]})
    owners = {}
    source_entries = []
    for key, record in sorted(sources.items()):
        matter = record['case']
        find(matter)
        try:
            source_body = body(record)
        except ValueError:
            # Can't establish a safe full-source boundary: block its owner.
            protected_matters.add(matter)
            source_body = ' '.join(record['pages'].values())
            reasons.append({'kind': 'unconfirmed_native_boundary', 'source': key, 'matter': matter})
        links = citation_keys(' '.join(record['pages'].values())) | {matter}
        for token in [*links, 'SHA ' + record['sha256'], 'URL ' + record['url']]:
            if token in owners:
                union(matter, owners[token], {'kind': 'identity_common_judgment_sha_or_url',
                                            'link': token, 'matters': [matter, owners[token]]})
            else:
                owners[token] = matter
        for related in links:
            if not related.startswith(('COMMON_AUTHORITY ', 'JUDGMENT ')):
                union(matter, related, {'kind': 'conservative_cited_identity', 'matters': [matter, related]})
        source_entries.append((key, matter, source_body))
    passages = sorted({(e['matter_id'], p['text']) for e in examples for p in e['provenance']})
    for i, (matter, passage) in enumerate(passages):
        for other, text in passages[i + 1:]:
            if find(matter) != find(other) and near_duplicate(passage, text):
                union(matter, other, {'kind': 'exact_or_near_passage', 'matters': [matter, other],
                                     'passage_sha256': digest(passage)})
    # Full native comparisons use an inexpensive upper bound before the existing
    # unchanged near-duplicate criterion. No reduction in audit sensitivity.
    from difflib import SequenceMatcher
    prepared = [(k, m, t, normalized(t), shingles(t)) for k, m, t in source_entries]
    for i, (key, matter, text, norm, sh) in enumerate(prepared):
        for other_key, other, content, other_norm, other_sh in prepared[i + 1:]:
            if find(matter) == find(other):
                continue
            overlap = len(sh & other_sh) / max(1, len(sh | other_sh))
            if norm == other_norm or (len(sh) >= 8 and len(other_sh) >= 8 and
                    (overlap >= .65 or SequenceMatcher(None, norm, other_norm, autojunk=False).quick_ratio() >= .9)):
                if near_duplicate(text, content):
                    union(matter, other, {'kind': 'full_native_body_overlap',
                                          'sources': [key, other_key], 'matters': [matter, other]})
    members = defaultdict(list)
    for matter in sorted(parent):
        members[find(matter)].append(matter)
    ids = {m: 'composite-' + digest('\n'.join(group))[:16] for group in members.values() for m in group}
    excluded_groups = {ids[m] for m in protected_matters}
    return ids, excluded_groups, reasons, dict(members)


def bind_reviewed_context(example, passage_ledger, source_records):
    """Copy source-reviewed metadata only; no lookup into selected target IDs.

    Lifecycle cannot safely be inferred from a role or a missing later order.
    UNKNOWN deliberately quarantines old active-action claims pending review.
    """
    result = deepcopy(example)
    for index, p in enumerate(result['provenance']):
        annotation = passage_ledger[p['passage_id']]
        version, page, text, role, field, scope, actor = annotation
        record = source_records[version]
        if (record['sha256'] != p['sha256'] or record['url'] != p['url'] or
                p['matter_id'] != record['case'] or page != p['page'] or text != p['text'] or
                digest(text) != p['text_sha256'] or
                re.sub(r'\s+', ' ', text).strip() not in
                re.sub(r'\s+', ' ', record['pages'].get(str(page), '')).strip()):
            raise ValueError('Exact reviewed source/page/version binding failed')
        from datetime import datetime
        from anchors import source_header
        _, bodies = source_header({int(k): v for k, v in record['pages'].items()})
        header = re.search(r'%\s*(\d{2}\.\d{2}\.\d{4})', next(iter(bodies.values()))) if bodies else None
        if not header or datetime.strptime(header[1], '%d.%m.%Y').date().isoformat() != p['order_date']:
            raise ValueError('Actual order header date differs from provenance')
        if 'availableEvidence' in result['input']:
            entry = result['input']['availableEvidence'][index]
            if (entry['text'], entry['category'], entry['scope']) != (text, role, scope):
                raise ValueError('Original reviewed input/role/scope mismatch')
            entry.update(field=field, actor=actor)
    result['contract'] = TASKS[result['task']]
    from annotations.v3_reuse_context import enrich
    result = enrich(result, passage_ledger, source_records)
    result['input'] = context_for_v3(result['input'])
    if result['target'] != example['target'] or result['provenance'] != example['provenance']:
        raise ValueError('Reuse changed original target or provenance')
    parse_runtime_output(canonical(result['target']), result,
                         {'anchors': ANCHOR_SCHEMA, 'claims': ANSWER_SCHEMA}, ROOT.parent)
    return result


def audit(root=ROOT):
    frozen_before = verify_frozen(root)
    train, protected_rows, protected = [], [], set()
    known = defaultdict(set)
    for version in ('pilot-v1', 'pilot-v2'):
        manifest = load(root / version / ('splits.json' if version == 'pilot-v1' else 'manifest.json'))
        for split in ('train', 'validation', 'blind'):
            for original in rows(root / version / (split + '.jsonl')):
                e = deepcopy(original)
                group = (manifest['leakage_groups'][e['id']] if version == 'pilot-v1' else e['leakage_group'])
                known[version + ':' + group].add(e['matter_id'])
                if split == 'train':
                    if version == 'pilot-v1' and manifest['assignments'][e['id']] != 'train':
                        raise ValueError('V1 train assignment mismatch')
                    if version == 'pilot-v2' and e['split'] != 'train':
                        raise ValueError('V2 train assignment mismatch')
                    if e['state'] != 'VERIFIED_GOLD':
                        raise ValueError('Non-gold in frozen TRAIN')
                    e['reuseOrigin'] = version
                    train.append(e)
                else:
                    # Retain provenance only; no holdout answers in the new pool.
                    protected.add(e['matter_id'])
                    protected_rows.append({'id': version + ':' + e['id'], 'matter_id': e['matter_id'],
                                           'provenance': e['provenance']})
    protected.update(load(root / 'pilot-v1/splits.json')['matter_inventory']['old_development_only'])
    for matter, metadata in pilot_review.MATTERS.items():
        if metadata['connections']:
            known['review:' + matter] |= {matter, *metadata['connections']}
    sources, ledger = {}, {}
    for version, folder, annotations in (
        ('pilot-v1', 'pilot-source-audit', pilot_review.PASSAGES),
        ('pilot-v2', 'pilot-v2-source-audit', v2_tasks.PASSAGES),
    ):
        ledger.update(annotations)
        for path in (root / 'local-private' / folder).glob('*.json'):
            sources[version + ':' + path.stem] = load(path)
    # Frozen SHA establishes immutability, not correctness. Recheck source gold
    # against its independent annotation ledger, before any V3 context enrichment.
    from types import SimpleNamespace
    # Source dates come from the review ledger, not a filename/listing date.
    from annotations.v2_review import REVIEWED
    v2_sources = {k.split(':', 1)[1]: {'matter_id': s['case'],
        'order_date': REVIEWED[k.split(':', 1)[1]][0], 'sha256': s['sha256'], 'url': s['url']}
        for k, s in sources.items() if k.startswith('pilot-v2:') and k.split(':', 1)[1] in REVIEWED}
    v2_corpus = SimpleNamespace(SOURCES=v2_sources, PASSAGES=v2_tasks.PASSAGES,
                               REVIEW=v2_tasks.REVIEW, VERIFIED_IDS=v2_tasks.VERIFIED_IDS)
    gold_annotations = {'pilot-v1': {a[0]: a for a in pilot_review.EXAMPLES},
                        'pilot-v2': {a[0]: a for a in v2_tasks.EXAMPLES}}
    for e in train:
        corpus = pilot_review if e['reuseOrigin'] == 'pilot-v1' else v2_corpus
        expected = compile_example(gold_annotations[e['reuseOrigin']][e['id']], corpus)
        for field in ('id', 'task', 'state', 'language', 'matter_id', 'outcome', 'provenance', 'input', 'target', 'review'):
            if expected[field] != e[field]:
                raise ValueError(f"Frozen train differs from reviewed annotation: {e['id']} / {field}")
    ids, excluded_groups, relationships, members = group_sources(
        sources, train + protected_rows, protected, [sorted(g) for g in known.values()])
    for e in train:
        e['leakage_group'] = ids[e['matter_id']]
    exclusion = [{'id': e['id'], 'origin': e['reuseOrigin'], 'group': e['leakage_group'],
                  'reason': 'Connected to protected matter/source/common judgment or unconfirmed source boundary'}
                 for e in train if e['leakage_group'] in excluded_groups]
    eligible = [e for e in train if e['leakage_group'] not in excluded_groups]
    canonical_rows, duplicates = deduplicate(eligible)
    compatible, quarantine = [], []
    for e in canonical_rows:
        source_map = {k.split(':', 1)[1]: v for k, v in sources.items() if k.startswith(e['reuseOrigin'] + ':')}
        try:
            enriched = bind_reviewed_context(e, ledger, source_map)
            parse_v3_output(canonical(enriched['target']), enriched,
                            {'anchors': ANCHOR_SCHEMA, 'claims': ANSWER_SCHEMA}, ROOT.parent)
            compatible.append(enriched)
        except (ValueError, KeyError) as error:
            quarantine.append({'id': e['id'], 'origin': e['reuseOrigin'], 'reason': str(error),
                               'truthful_labels_changed': False, 'training_eligible': False})
    candidate_conflicts = []
    used_groups = {e['leakage_group'] for e in train}
    for key, case, split in CANDIDATES:
        if split == 'train':
            continue
        links = {case}
        path = root / 'local-private/pilot-v3-source-audit' / (key + '.json')
        if path.exists():
            record = load(path)
            links |= citation_keys(' '.join(record['pages'].values()))
        crossed = sorted({ids[x] for x in links if x in ids and
                           (ids[x] in used_groups or ids[x] in excluded_groups)})
        # Also compare common-authority/citation tokens against full prior sources.
        shared = sorted(set().union(*(citation_keys(' '.join(s['pages'].values())) for s in sources.values())) & links)
        if crossed or shared:
            candidate_conflicts.append({'candidate': key, 'case': case, 'reserved_split': split,
                                       'used_or_protected_groups': crossed, 'shared_prior_citations': shared,
                                       'action': 'Replace or quarantine before fresh holdout freeze; never move holdout to train'})
    if verify_frozen(root) != frozen_before:
        raise ValueError('Audit modified frozen source files')
    report = {
        'state': 'COMPOSITE_TRAIN_AUDITED_NOT_GPU_READY', 'training_started': False,
        'raw_train_candidates': counts(train), 'protected_group_exclusions': exclusion,
        'deduplicated_overlap_count': len(duplicates), 'duplicates': duplicates,
        'cross_version_duplicate_count': sum(d['origin'] != d['canonical_origin'] for d in duplicates),
        'dedupe_policy': 'Same task, ordered dated/page-bound source text, target and outcome; query/language variants canonicalized',
        'eligible_deduplicated_legacy_gold': counts(canonical_rows),
        'v3_contract_compatible_train': counts(compatible),
        'reused_contract_quarantine': quarantine,
        'source_relationships': relationships, 'group_members': members,
        'fresh_holdout_candidate_conflicts': candidate_conflicts,
        'fresh_holdout_cross_split_review': [{
            'cases': ['W.P.(C) 12473/2025', 'W.P.(C) 17094/2025'],
            'basis': 'Same petitioner caption HARENDER & ORS and Award 02/2024/SW dated 18 November 2024',
            'action': 'Treat as one conservative group; replace one candidate before freeze; neither enters training',
        }],
        'new_v3_gold_examples': 0, 'validation_frozen': False, 'blind_frozen': False,
        'context_token_audit_complete': False, 'final_exposure_and_budget': None,
        'protected_files_verified_unchanged': frozen_before,
        'source_record_sha256': {k: digest(canonical(v)) for k, v in sources.items()},
        'truthful_labels_changed': False, 'no_live_requests': True,
        'decision': 'NO-GO',
    }
    return compatible, canonical_rows, report


def context_audit(examples):
    """Reuse the proven offline token audit; never load weights or drop examples."""
    from audit_v2_context import check, MODEL, REVISION
    from transformers import AutoTokenizer
    import transformers
    if transformers.__version__ != '4.56.2':
        raise ValueError('Use pinned tokenizer package 4.56.2')
    frozen = ROOT / 'local-private/pilot-v1-frozen/lac-court-pilot-v1'
    manifest = load(frozen / 'dataset-manifest.json')
    if (manifest['model'], manifest['revision']) != (MODEL, REVISION):
        raise ValueError('Frozen tokenizer model/revision mismatch')
    checksums = load(frozen / 'artifact-checksums.json')
    verified = {}
    for relative, expected in checksums.items():
        if relative.startswith('adapter/') and not relative.endswith('.safetensors'):
            path = (frozen / relative).resolve()
            if not path.is_relative_to(frozen.resolve()):
                raise ValueError('Tokenizer path escape')
            actual = hashlib.sha256(path.read_bytes()).hexdigest()
            if actual != expected:
                raise ValueError('Frozen tokenizer changed')
            verified[relative] = actual
    tokenizer = AutoTokenizer.from_pretrained(frozen / 'adapter', local_files_only=True, trust_remote_code=False)
    schemas = load(ROOT / 'pilot-v1/target.schemas.json')
    results = [check(tokenizer, e, schemas) for e in examples]
    return {'state': 'REUSE_CANDIDATE_ONLY_NOT_FINAL_FREEZE', 'model': MODEL, 'revision': REVISION,
            'transformers': transformers.__version__, 'tokenizer_artifacts': verified,
            'candidate_count': len(examples), 'sequence_cap': 2048, 'output_reserve': 512,
            'training_fit': sum(r['training_fits'] for r in results),
            'inference_prompt_fit': sum(r['existing_pilot_inference_prompt_fits'] for r in results),
            'inference_with_output_reserve_fit': sum(r['inference_with_output_reserve_fits'] for r in results),
            'max_training_tokens': max(r['training_tokens'] for r in results),
            'max_inference_prompt_tokens': max(r['inference_prompt_tokens'] for r in results),
            'input_sha256': digest(canonical(examples)), 'results': results,
            'weights_loaded': False, 'evidence_truncated': False, 'examples_dropped': False,
            'gpu_launch_allowed': False}


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', action='store_true', help='Write new V3 audit artifacts only; never touch V1/V2')
    parser.add_argument('--context', action='store_true', help='Offline pinned-tokenizer check of reused candidates')
    args = parser.parse_args()
    compatible, legacy, report = audit()
    context = context_audit(compatible) if args.context else None
    # This is a diagnosed CURRENT candidate-pool probe, never the final V3
    # training budget. It makes tiny-positive-pool oversampling visible now.
    from v3_sampler import calculate_budget, exposure_report
    provisional_budget = calculate_budget(len(compatible), 4, 2)
    probe = {'state': 'PROVISIONAL_REUSE_POOL_DIAGNOSTIC_NOT_LAUNCH_BUDGET',
             'gpu_launch_allowed': False, 'budget': provisional_budget,
             'exposure': exposure_report(compatible, 20261003, provisional_budget['logical_exposures'])}
    if args.output:
        from foundation import write_json, write_jsonl
        out = ROOT / 'pilot-v3'
        write_json(out / 'composite-reuse-audit.json', report)
        write_jsonl(out / 'train-reuse-candidate.jsonl', compatible)
        write_json(out / 'reuse-quarantine.json', report['reused_contract_quarantine'])
        write_json(out / 'reuse-exposure-probe.json', probe)
        if context:
            write_json(out / 'reuse-context-audit.json', context)
    print(json.dumps({k: report[k] for k in ('state', 'raw_train_candidates',
                    'deduplicated_overlap_count', 'eligible_deduplicated_legacy_gold',
                    'v3_contract_compatible_train', 'fresh_holdout_candidate_conflicts', 'decision')}, indent=2))
    print('Protected excluded:', len(report['protected_group_exclusions']))
    print('V3 contract quarantine:', len(report['reused_contract_quarantine']))


if __name__ == '__main__':
    main()
