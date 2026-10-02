"""Targeted TRAIN chain gaps, never random PDFs or protected holdout content.

Discovery dates below are earlier directions or listings in already acquired
orders / official cause lists. A listing does NOT establish an order. Only an
actual native PDF with exact case identity and order date can be retained as
an UNREVIEWED candidate. No form POST, CAPTCHA, inference or gold generation.
"""
import json
import re
from pathlib import Path

from acquire_pilot_sources import acquire
from foundation import write_json
from audit_pilot_overlap import body

ROOT = Path(__file__).resolve().parent
DISCOVERY = (
    ('wpc5202-2024-apr2025', 'W.P.(C) 5202/2024', '15-04-2025', 'Official 15 April 2025 cause list; verify actual PDF, not assumed hearing'),
    ('wpc5202-2024-nov2025', 'W.P.(C) 5202/2024', '04-11-2025', 'Official 4 November 2025 cause list; verify actual PDF'),
    ('wpc4806-2014-sep2020', 'W.P.(C) 4806/2014', '24-09-2020', '4 February 2020 supplied order listing'),
    ('wpc4806-2014-apr2026', 'W.P.(C) 4806/2014', '20-04-2026', '7 February 2026 supplied order listing'),
    ('wpc10308-2024-aug2025', 'W.P.(C) 10308/2024', '11-08-2025', '23 September 2025 supplied order expressly reproduces 11 August order'),
    ('wpc10308-2024-nov2025', 'W.P.(C) 10308/2024', '19-11-2025', '23 September 2025 supplied order listing'),
    ('wpc5084-2018-jan2021', 'W.P.(C) 5084/2018', '29-01-2021', '24 November 2020 supplied order listing'),
    ('wpc5997-2024-aug2024', 'W.P.(C) 5997/2024', '20-08-2024', '29 April 2024 supplied order Registrar listing'),
    ('wpc4806-2014-jul2026', 'W.P.(C) 4806/2014', '13-07-2026', '20 April 2026 actual supplied order listing; verify actual PDF'),
    ('wpc10308-2024-jul2025', 'W.P.(C) 10308/2024', '02-07-2025', '11 August 2025 expressly records affidavit in compliance with this earlier order'),
    ('wpc10308-2024-apr2026', 'W.P.(C) 10308/2024', '14-04-2026', '19 November 2025 actual supplied order listing'),
    ('wpc5084-2018-feb2021', 'W.P.(C) 5084/2018', '12-02-2021', '29 January 2021 actual supplied order listing before roster bench'),
    ('wpc5997-2024-dec2024', 'W.P.(C) 5997/2024', '09-12-2024', '20 August 2024 actual supplied Registrar listing'),
    ('wpc13932-2025-oct2025', 'W.P.(C) 13932/2025', '17-10-2025', '10 September 2025 supplied order listing; verify actual order'),
)


def main():
    directory = ROOT / 'local-private/pilot-v3-source-audit'
    for key, case, day, reason in DISCOVERY:
        path = directory / (key + '.json')
        if path.exists():
            print(json.dumps({'id': key, 'state': 'ALREADY_ACQUIRED_NOT_OVERWRITTEN'}), flush=True)
            continue
        number, year = re.search(r'(\d+)/(\d{4})$', case).groups()
        url = f'https://delhihighcourt.nic.in/app/downloadOrderbByDate/W.P.%28C%29/{number}/{year}/{day}'
        try:
            record = acquire(case, url)
            text = ' '.join(record['pages'].values())
            if not re.search(r'W\.?\s*P\.?\s*\(\s*C\s*\)\s*' + number + r'\s*/\s*' + year + r'(?!\d)', text, re.I):
                raise ValueError('Exact native case identity absent')
            dd, mm, yyyy = day.split('-')
            body(record)  # Require the native caption/body boundary as well.
            if not re.search(r'O\s*R\s*D\s*E\s*R\s*%?\s*' + dd + r'\.' + mm + r'\.' + yyyy, text, re.I):
                raise ValueError('Exact native judicial order date not established')
        except Exception as error:
            print(json.dumps({'id': key, 'state': 'SOURCE_UNCONFIRMED_NOT_GOLD', 'failure_type': type(error).__name__,
                              'failure': str(error)}), flush=True)
            continue
        record.update(reserved_split='train', training_eligible=False, audit_state='UNREVIEWED_NOT_GOLD',
                      discovery_basis=reason, order_date=f'{yyyy}-{mm}-{dd}')
        write_json(path, record)
        print(json.dumps({'id': key, 'state': 'UNREVIEWED_NOT_GOLD', 'pages': len(record['pages']),
                          'sha256': record['sha256']}), flush=True)


if __name__ == '__main__':
    main()
