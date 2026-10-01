# Pending-first quality checkpoint — 2 October 2026

This is an evaluation checkpoint, **not completion of the pre-training quality bar**.
No fine-tuning, canonical Court update, CAPTCHA/search submission, migration,
background DHC sync or database mutation was performed. Inference is loopback-only.

## Full-source benchmarks

| Matter | Actually read sources | Finding / outcome |
| --- | --- | --- |
| W.P.(C) 14604/2025 | Six orders, nine pages: 19 Sep 2025, 15 Oct 2025, 16 Jan 2026, 16 Apr 2026, 7 May 2026, 29 Jul 2026 | Six-date history retained; the manually selected 22 key checks are represented after rerun. One order still has additional unrepresented anchors and remains NeedsReview. |
| W.P.(C) 8664/2021 | Complete six-page judgment, 30 Jan 2025 | Latest model rerun temporarily marked three explicit LAC utterances Uncertain. General source-speaker expansion was corrected and revalidated against the exact same SHA/full PDF: 21/21 key facts represented; no claim promoted to Court fact. |
| W.P.(C) 7003/2026 | Complete seven-page order, 20 May 2026 | NHAI's compensation statement is now an Other party submission; quoted precedent is not a current direction. Cross-page respondent direction and Court/Registrar dates retained. Some Court findings still classify as historical land context; benchmark remains review work, not a pass. |

14604 is the real pending six-order benchmark supplied by the user. Its petition
is not treated as finally disposed merely because an exemption application was
disposed. The latest supplied order gives a last/final counter-affidavit opportunity,
a conditional rejoinder period, and the already-fixed Court date of 1 Oct 2026.
That date is not proof that a hearing occurred. A PDF for that date was unavailable;
no seventh Court event was invented. Filing/compliance is not confirmed by these
six orders. Generic respondent directions are not silently assigned to LAC alone.

## Misses and general corrections

- Caption parties/bench/advocates were missing: independently retain exact caption
  passages, with source page and date. Mixed respondent advocate blocks remain mixed.
- LAC counsel's explicit statement could be omitted by model selection: retain
  deterministically mapped source speech **as a submission**, never as a Court finding.
- Dates inside a judgment were confused with Court events: separate mentioned
  factual dates, retain original multi-date passage, never infer a hearing.
- Prior orders could be obscured by the latest outcome: preserve every supplied
  order, with attributed compact facts and an expandable history.
- Cross-page directions lost their ending: validate two exact adjacent-page
  passages independently; preserve both page references in officer evidence.
- Damaged quotation glyphs leaked Supreme Court extracts into current directions:
  retain quote context until a provable outer-paragraph boundary.
- Registrar and Court dates conflicted: prefer the explicit Court listing date;
  do not claim a past listed date is a currently upcoming hearing.
- Six-date questions lost middle dates: reserve evidence across requested dates;
  invalid generated selections fall back to exact retrieved passages across up to
  eight dates, not invented summaries.
- Old passovers led Current position: preserve them in history, not the current brief.
- A direction to file was mistaken for actual filing: past-filing questions require
  explicit recorded filing evidence; an obligation is not compliance.

## Audit dimensions and limitations

The JSON gold checklists are human-reviewed source checklists, not model-generated
training labels, confidence percentages, or proof of complete legal coverage.
Recall, attribution and exact page evidence are separate from officer usefulness.
8664's original misses included notice-service assertion/admission, limitation
reasoning, factual dates and final disposition. 14604's first iterations missed
renewed filing directions and the LAC-counsel statement; later key recall is 22/22.
No numeric before-score is fabricated where no saved baseline score exists.

The date-download endpoint dynamically adds a download timestamp. Consequently
fresh PDF binary SHA values differ from the first manually audited copies. The
evaluator deliberately reports `sourceShaMatches: false`; this is **not** an
integrity-equivalent pass. Each extraction retains its own downloaded SHA and
validates its own page evidence. Gold hashes must not replace evidence hashes.

Pending compensation/possession/filing is exercised by 7003; pending six-order
filing/denotification is exercised by 14604. A fully audited pending Section 18
reference benchmark and broader repeated real-model Q&A acceptance remain open.
Scanned/unusable sources and ambiguous connected captions fail closed. The system
does not claim that supplied known URLs exhaust all orders on the Court website.

## Reproducible offline checklist evaluation

```powershell
python tools/court-order-intelligence/evaluate_gold.py --gold tools/court-order-intelligence/gold/wpc-14604-2025.json --artifact <absolute-demo-current.json>
python -m unittest discover -s tools/court-order-intelligence -p 'test_*.py' -v
```

Checklist evaluation is read-only: no network, database or model request. The
optional `benchmark_inventory.py` uses a read-only PostgreSQL session to inventory
registered known sources; it needs psycopg separately, not in the worker runtime.
Synthetic demo GUIDs must never be copied onto real registered Court IDs.
