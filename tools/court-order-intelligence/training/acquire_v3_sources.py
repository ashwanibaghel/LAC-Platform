"""Bounded append-only acquisition from existing PRIVATE discovery inventory.

Public native text only retained locally; no workbook upload, CAPTCHA, form POST,
model inference, application endpoints or gold assignment. PDFs are temporary.
Refuses all previously used V1/V2 matters and known protected connections.
"""
import argparse
import json
import re
from pathlib import Path

from acquire_pilot_sources import acquire
from foundation import write_json
from semantics import identity
from v3_candidates import CANDIDATES

ROOT = Path(__file__).resolve().parent


def excluded():
    old = set()
    for dataset in ('pilot-v1', 'pilot-v2'):
        for split in ('train', 'validation', 'blind', 'development'):
            path = ROOT / dataset / (split + '.jsonl')
            if path.exists():
                old.update(identity(json.loads(line)['matter_id']) for line in path.read_text(encoding='utf-8').splitlines())
    old.update(identity(case) for case in (
        'W.P.(C) 940/2015', 'W.P.(C) 14604/2025', 'W.P.(C) 8664/2021',
        'W.P.(C) 2686/2018', 'W.P.(C) 7817/2017', 'W.P.(C) 5796/2026'))
    return old


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--split', choices=('train', 'validation', 'blind'), default='train')
    parser.add_argument('--limit', type=int, default=4)
    args = parser.parse_args()
    if not 1 <= args.limit <= 8:
        raise ValueError('Bounded acquisition batch required')
    rows = json.loads((ROOT / 'local-private/workbook-inventory.json').read_text(encoding='utf-8'))['rows']
    root = ROOT / 'local-private/pilot-v3-source-audit'
    protected = excluded()
    attempted = 0
    for identifier, case, split in CANDIDATES:
        if split != args.split:
            continue
        path = root / (identifier + '.json')
        if path.exists():
            saved = json.loads(path.read_text(encoding='utf-8'))
            if saved['case'] != case or saved['reserved_split'] != split:
                raise ValueError('Existing reservation changed; never overwrite')
            continue
        if identity(case) in protected:
            raise ValueError('Previously used/protected matter in V3 reservation')
        number_year = re.search(r'(\d+)/(\d{4})$', case)
        matches = [row for row in rows if re.search(
            r'(?<!\d)' + number_year[1] + r'\s*/\s*' + number_year[2] + r'(?!\d)', row['raw_case_number'])]
        urls = {row.get('candidate_order_url') for row in matches if row.get('candidate_order_url', '').startswith('https://delhihighcourt.nic.in/')}
        if len(urls) != 1:
            print(json.dumps({'id': identifier, 'state': 'DISCOVERY_LINK_AMBIGUOUS_OR_MISSING'}), flush=True)
            continue
        if attempted >= args.limit:
            break
        attempted += 1
        try:
            record = acquire(case, next(iter(urls)))
        except Exception as error:
            print(json.dumps({'id': identifier, 'state': 'SOURCE_UNAVAILABLE', 'error_type': type(error).__name__}), flush=True)
            continue
        record['reserved_split'] = split
        record['training_eligible'] = False
        record['audit_state'] = 'UNREVIEWED_NOT_GOLD'
        write_json(path, record)
        print(json.dumps({'id': identifier, 'state': record['audit_state'],
                          'pages': len(record['pages']), 'sha256': record['sha256']}), flush=True)


if __name__ == '__main__':
    main()
