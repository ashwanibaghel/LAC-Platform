"""Three targeted rich primary-source candidates, reserved BEFORE gold review.

These judgments contain explicit earlier operative/filing context and are
candidates for missing action/compliance/chain or replacement holdout coverage.
No random search, form POST, CAPTCHA, automatic gold or production API.
"""
import json
import re
from pathlib import Path

from acquire_pilot_sources import acquire
from foundation import write_json
from audit_pilot_overlap import body

ROOT = Path(__file__).resolve().parent
CANDIDATES = (
 ('wpc15198-2025', 'W.P.(C) 15198/2025', 'train', '2026-02-13',
  'https://delhihighcourt.nic.in/app/showFileJudgment/75513022026CW151982025_172020.pdf'),
 ('wpc787-2026', 'W.P.(C) 787/2026', 'validation', '2026-02-02',
  'https://delhihighcourt.nic.in/app/showFileJudgment/PMS02022026CW7872026_113422.pdf'),
 ('wpc1784-2026', 'W.P.(C) 1784/2026', 'blind', '2026-04-06',
  'https://delhihighcourt.nic.in/app/showFileJudgment/PMS06042026CW17842026_210203.pdf'),
)


def main():
 for key, case, split, day, url in CANDIDATES:
  path = ROOT / 'local-private/pilot-v3-source-audit' / (key + '.json')
  if path.exists():
   print(json.dumps(dict(id=key, state='EXISTS_NOT_OVERWRITTEN')), flush=True)
   continue
  try:
   record = acquire(case, url)
   text = ' '.join(record['pages'].values())
   number, year = re.search(r'(\d+)/(\d{4})$', case).groups()
   if not re.search(r'W\.?\s*P\.?\s*\(\s*C\s*\)\s*' + number + r'\s*/\s*' + year + r'(?!\d)', text, re.I):
    raise ValueError('Exact native identity absent')
   body(record)
   record.update(reserved_split=split, order_date=day, training_eligible=False,
                 audit_state='UNREVIEWED_DATE_AND_LEAKAGE_CHECK_REQUIRED_NOT_GOLD')
   write_json(path, record)
   print(json.dumps(dict(id=key, reserved_split=split, pages=len(record['pages']), sha256=record['sha256'])), flush=True)
  except Exception as error:
   print(json.dumps(dict(id=key, state='SOURCE_UNCONFIRMED_NOT_GOLD', failure_type=type(error).__name__)), flush=True)


if __name__ == '__main__':
 main()
