"""Audit the initial V2 review; never exports training records or starts GPU.

V1/V2 protected matters and connected identities are excluded. Initial reviewed
passages are not automatically a final dataset: gold questions, chain targets,
runtime checks, token-length checks and split freeze are separate requirements.
"""
import json
import re
from pathlib import Path
from collections import Counter
from urllib.parse import urlparse
from datetime import datetime

from foundation import write_json, digest, canonical
from semantics import identity
from annotations.v2_review import REVIEWED, QUARANTINED
from v2_candidates import CANDIDATES, FOLLOWUPS
from audit_pilot_overlap import body
from leakage import near_duplicate
from anchors import source_header

ROOT = Path(__file__).resolve().parent


def related_identities(record):
    text = ' '.join(record['pages'].values())
    return {'W.P.(C) ' + number + '/' + year for number, year in
            re.findall(r'W\.?\s*P\.?\s*\(C\)\s*(\d+)\s*/\s*(\d{4})', text)}


def audit_relationships(records):
    """Do not let common captions, judgments, bytes or bodies cross splits."""
    relations = []
    entries = [(version, record, related_identities(record),
                body({**record, 'pages': {k: record['pages'][k] for k in sorted(record['pages'], key=int)}}))
               for version, record in sorted(records.items())]
    for index, (version, record, cited, text) in enumerate(entries):
        for other, candidate, other_cited, other_text in entries[index + 1:]:
            same_matter = record['case'] == candidate['case']
            common = sorted(cited & other_cited)
            same_sha = record['sha256'] == candidate['sha256']
            # An already-proven relationship needs no expensive approximate
            # body comparison. None means not checked, not "different".
            same_body = None if same_matter or common or same_sha else near_duplicate(text, other_text)
            if same_matter or common or same_sha or same_body:
                if record['reserved_split'] != candidate['reserved_split']:
                    raise ValueError(f'Protected split relationship: {version} / {other}; never move evaluation sources to train')
                relations.append({'sources': [version, other], 'split': record['reserved_split'],
                                  'same_matter': same_matter, 'common_identities': common,
                                  'same_sha': same_sha, 'near_duplicate_body': same_body})
    return relations


def protected_matters():
    splits = json.loads((ROOT / 'pilot-v1/splits.json').read_text())
    inventory = splits['matter_inventory']['matters']
    protected = {m for m, v in inventory.items() if v['split'] != 'train'}
    protected.update(splits['matter_inventory']['old_development_only'])
    protected.update(case for _, case, split, _ in CANDIDATES if split != 'train')
    # V1-related matters are protected even when absent from its selected inventory.
    protected.update({'W.P.(C) 2686/2018', 'W.P.(C) 7817/2017', 'W.P.(C) 5796/2026'})
    return protected


def audit_record(record, expected_case, date, passages, protected, required_split='train'):
    """Pure fail-closed source binding, usable with synthetic records in CI."""
    protected_ids = {identity(m) for m in protected}
    if required_split not in {'train', 'validation', 'blind'}:
        raise ValueError('Unknown reserved split')
    if record['case'] != expected_case or record['reserved_split'] != required_split:
        raise ValueError('Source identity/reserved split mismatch')
    if identity(record['case']) in protected_ids:
        raise ValueError('Protected source cannot enter training')
    parsed = urlparse(record['url'])
    if parsed.scheme != 'https' or parsed.hostname != 'delhihighcourt.nic.in':
        raise ValueError('Official HTTPS provenance required')
    if not re.fullmatch(r'[0-9a-f]{64}', record['sha256']):
        raise ValueError('Binary source SHA-256 required')
    text = ' '.join(record['pages'][k] for k in sorted(record['pages'], key=int))
    header = re.search(r'O R D E R\s*%\s*(\d{2}\.\d{2}\.\d{4})', text)
    if not header or datetime.strptime(header[1], '%d.%m.%Y').date().isoformat() != date:
        raise ValueError('Order header date mismatch; cited/listing date is not an order date')
    # The exact header date was verified above. Parsing every incidental date
    # across concatenated pages would incorrectly treat download-footer digits
    # before a continued month name as a historical date.
    headers, bodies = source_header({int(k): record['pages'][k] for k in sorted(record['pages'], key=int)})
    if bodies is None or identity(record['case']) not in identity(' '.join(headers.values())):
        raise ValueError('Exact case absent from source caption; a quoted case is not this matter')
    cited = related_identities(record)
    if any(identity(case) in protected_ids for case in cited):
        raise ValueError('Protected related identity in training source')
    for page, passage, role, field, scope, actor, reason in passages:
        if passage not in record['pages'].get(str(page), '') or not reason:
            raise ValueError(f'Unbound reviewed passage or missing decision reason: page {page}, {passage[:80]!r}')
    return cited


def audit():
    protected = protected_matters()
    source_root = ROOT / 'local-private/pilot-v2-source-audit'
    records = {p.stem: json.loads(p.read_text(encoding='utf-8')) for p in source_root.glob('*.json')}
    relationships = audit_relationships(records)
    candidates = {key: case for key, case, _, _ in CANDIDATES + FOLLOWUPS}
    ledger, roles, scopes = {}, Counter(), Counter()
    for version, (date, passages) in REVIEWED.items():
        record = json.loads((source_root / (version + '.json')).read_text(encoding='utf-8'))
        cited = audit_record(record, candidates[version], date, passages, protected)
        for page, passage, role, field, scope, actor, reason in passages:
            roles[role] += 1
            scopes[scope] += 1
        ledger[version] = {'case': record['case'], 'order_date': date,
                           'url': record['url'], 'sha256': record['sha256'],
                           'native_pages_sha256': digest(canonical(record['pages'])),
                           'pages': len(record['pages']), 'split': 'train',
                           'state': 'SOURCE_PASSAGES_REVIEWED_NOT_FINAL_TRAINING_GOLD',
                           'all_pages_read': True, 'pdf_retained': False,
                           'related_identities': sorted(cited)}
    report = {'phase': 'V2_SOURCE_FOUNDATION_IN_PROGRESS', 'training_started': False,
              'model': 'Qwen/Qwen3-4B-Instruct-2507',
              'revision': 'cdbee75f17c01a7cc42f958dc650907174af0554',
              'reviewed_sources': len(ledger),
              'acquired_sources': len(records),
              'acquired_matters': len({r['case'] for r in records.values()}),
              'source_relationships': relationships,
              'reviewed_matters': len({x['case'] for x in ledger.values()}),
              'reviewed_passages': sum(map(len, (x[1] for x in REVIEWED.values()))),
              'roles': roles, 'scopes': scopes, 'protected_matters': sorted(protected),
              'quarantined': QUARANTINED,
              'positive_lac_passages': sum(role == 'COURT_DIRECTION' and scope == 'Current' and actor == 'LAC'
                                           for _, ps in REVIEWED.values() for _, _, role, _, scope, actor, _ in ps),
              'final_training_examples': 0,
              'gates_remaining': ['independent question/target annotation', 'additional substantive order chains',
                                  'complete fresh validation/blind source review', 'full leakage-group freeze',
                                  'runtime-gold compatibility', 'pinned tokenizer context audit'],
              'ledger': ledger}
    write_json(ROOT / 'pilot-v2/foundation-audit.json', report)
    print(json.dumps({k: v for k, v in report.items() if k not in {'ledger', 'source_relationships'}}, indent=2))
    return report


if __name__ == '__main__':
    audit()
