"""Append-only bounded native-source acquisition; PDFs always temporary.

No model inference, CAPTCHA/search-form automation or assignment of gold.
"""
import argparse
import json
from pathlib import Path

from acquire_pilot_sources import acquire
from foundation import write_json
from v2_candidates import CANDIDATES, PREFIX, FOLLOWUPS


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--split', choices=['train', 'validation', 'blind', 'all'], default='train')
    parser.add_argument('--followups', action='store_true')
    args = parser.parse_args()
    root = Path(__file__).resolve().parent / 'local-private/pilot-v2-source-audit'
    sources = FOLLOWUPS if args.followups else CANDIDATES
    prefix = 'https://delhihighcourt.nic.in/app/downloadOrderbByDate/W.P.%28C%29/' if args.followups else PREFIX
    for identifier, case, split, suffix in sources:
        if args.split not in ('all', split):
            continue
        path = root / (identifier + '.json')
        if path.exists():
            existing = json.loads(path.read_text(encoding='utf-8'))
            if (existing['case'] != case or existing['url'] != prefix + suffix
                    or existing['reserved_split'] != split):
                raise ValueError('Existing V2 source identity mismatch; never overwrite')
            print(json.dumps({'id': identifier, 'result': 'already-acquired'}), flush=True)
            continue
        try:
            record = acquire(case, prefix + suffix)
        except Exception as error:
            print(json.dumps({'id': identifier, 'result': 'SOURCE_UNAVAILABLE',
                              'failure': type(error).__name__, 'message': str(error)[:180]}), flush=True)
            continue
        record['reserved_split'] = split
        write_json(path, record)
        print(json.dumps({'id': identifier, 'result': 'UNREVIEWED',
                          'pages': len(record['pages']), 'sha256': record['sha256']}), flush=True)


if __name__ == '__main__':
    main()
