# Bounded pending-first quality work — 2 October 2026

This is an audited development checkpoint, not a claim that pre-training quality
is complete. No fine-tuning or office deployment was performed.

## Branch and checkpoints

Branch: `codex/court-order-intelligence-phase1`.

- Starting SHA: `b45a5582691bd2f92a7aa3a205fba9daa32e997a`.
- A: `daa8f1a5212f1651ad4684f2562da60cb363944c` — six-order chronology,
  full-source gold audit, attributed facts, page evidence and officer presentation.
- B: `5073cc9646714d6c18b54847847af591b2e80466` — case-scoped known order
  index, explicit-date lazy processing, grounded date/full-chain retrieval.
- C: follow-up commit containing this report — second Pending register benchmark,
  quotation-boundary correction and substantive full-story evidence selection.
  Obtain its exact SHA with `git log -1 --format=%H -- tools/court-order-intelligence/gold/FINAL-QUALITY-REPORT.md`.

## 14604/2025: six actual orders, nine pages

19 September 2025; 15 October 2025; 16 January 2026; 16 April 2026;
7 May 2026; 29 July 2026. All supplied sources were read in full.

The source-backed 1 October 2026 listing is NOT a seventh order/completed hearing.
No PDF for that date was found. Exemption-application disposal is not disposal of
the writ petition. Filing/rejoinder directions do not prove an affidavit was filed.

Before: renewed directions were obscured as history, LAC counsel's utterance was
missed, routine passovers dominated the brief, and timeline retrieval could omit
middle dates. No saved numeric baseline exists; no before-score is fabricated.
After: 22/22 manually selected key checklist facts represented. April still has
additional unrepresented anchors and remains NeedsReview. This is key-fact recall,
not proof of complete legal coverage or a confidence percentage.

## General rules and chronology

Exact caption identity/date checks; source-speaker statements retained as
submissions; Court directions after requests kept separate; bounded adjacent-page
evidence retained with both pages; quoted precedents not current office duties;
caption/advocate appearances dated rather than flattened into current advocates.
Routine orders are compact but never removed; earlier substantive developments
remain accessible. Mentioned factual dates stay separate from actual Court orders.
An old disposition followed by later orders produces a continuity warning, not
an automatic current disposal.

Actions remain Not confirmed complete unless explicit source-backed completion
or supersession identifies the original obligation. Expired time, counsel promise,
renewal or silence never means compliance. Generic respondent/DDA duties are not
silently reassigned to LAC. Preferred periods remain qualified, not hard deadlines.

## Grounded Q&A acceptance

- Six-date chronology: warm local-model/browser acceptance returned all six actual
  dates with original order/page evidence; screenshot captured at Checkpoint A.
- 29 July 2026: only July's last/final counter-affidavit opportunity, conditional
  rejoinder and already-fixed listing; local HTTP 200.
- 16 April 2026: only April's short-affidavit reasons, similar matters, conditional
  periods and LAC counsel's relied-on orders; local HTTP 200.
- 1 October 2026: zero claims and `I could not find an official order for that listed date.`
- Full story: initial answer selected procedural context instead of petition origin.
  General retrieval now reserves verified originating dispute, substantive party
  stand and operative direction rather than routine adjournment/listing noise.
  Final real local-model rerun returned April's refusal-letter/quashing context,
  Mr. Pathak's similar-pending-matters submission and July's last/final counter-
  affidavit opportunity, with page/date evidence. A petitioner procedural request
  remains labeled a submission, not a substantive finding or allegation proven.
- Unsupported actual filing/status-report facts return insufficient evidence.
  No model memory, cloud inference or cross-case evidence supplies answers.

## Index, storage and lazy sources

The authorized API reads existing `CourtExternalOrderObservation` metadata with
AsNoTracking and sends only case identity/date/official URL/corrigendum/upload/
observation identity to loopback. No HTML, cookies, CAPTCHA values or HTML hash
becomes PDF evidence. Case-scoped artifacts retain metadata, PDF SHA, processing
state, versions and timestamps. No duplicate order database/schema is introduced.

GET/render never downloads. A question about one unique known unprocessed actual
date may fetch that exact official URL under the sequential worker lock. Exact
PDF identity/date are checked again, structured evidence is atomically saved,
temporary PDF is deleted. Processed sources reuse intelligence; review sources
require explicit retry. Changed binary SHA fails closed for source-version review.
Dynamic DHC download stamps are not silently normalized or considered identical.

## Additional Pending benchmark: 940/2015

Read-only register inventory found at most two known PDFs per Pending matter;
no extra dates were invented or hours spent crawling. Selected 940/2015 has two
known sources: full five-page 5 October 2015 judgment and one-page 21 May 2026 order.
Its 2015 petition-allowed judgment and later 2026 procedural order do not establish
intervening restoration/appeal history. Canonical register status remains untouched.

First download attempt failed safely on transport errors. Explicit bounded retry
processed both PDFs. Before quotation correction: 4/12 key checks represented
(1/9 old judgment, 3/3 later order). A low opening/high reversed closing quote
around a defined statute name incorrectly leaked quote scope through the judgment.
General glyph and explicit predecessor-return boundaries were corrected with tests.
The 2026 DDA reply direction is not a LAC obligation; Registrar/Court dates differ.
After a fresh local-model/full-PDF rerun: **11/12 key checks** (8/9 old judgment,
3/3 later order). The respondent stay explanation is still withheld as uncertain;
no unsupported Court fact is substituted. The old judgment remains NeedsReview.
Both extracted PDFs carry their own hashes. Fresh binary hashes differ from the
first audit copies, so `sourceShaMatches: false` remains honestly reported, not
an integrity-equivalent pass. No temporary `lac-court-order-*` directory remained
after the worker completed.

Internal scorecard: exact caption/date identity checked independently for both
sources; later DDA duty and Court/Registrar dates correct; disposition/land facts
recovered after quote fix; party-position recall incomplete for the stay submission;
current canonical status deliberately unmodified; old/later continuity unresolved.
This benchmark demonstrates the general quote correction, not universal readiness.

## Remaining limitations and validation

7003/2026 retains some Court findings as historical land context and stays review
work. 940's stay explanation is withheld; its missing intervening history is not
invented. A broader Pending Section 18 benchmark is still open. Scanned/unusable native
text and ambiguous connected captions fail closed. Known-source completeness is
not website-wide completeness. Old caption formats and ambiguous submissions can
remain review work. Long lazy inference may time out calmly without affecting
the rest of LAC Platform.

Python safety suite: 116 passed. Frontend: 86 passed. Direct Vite build passed,
with the existing large-bundle warning. Release .NET build passed; focused API
artifact/metadata suite 3 passed. Full .NET suite stalled after test-file discovery;
allowed beyond the configured 5-minute blame-hang timeout, then the owned process
was stopped after roughly 6 minutes. No blame dump or full pass was obtained.

No domain/schema/migration changes. Main not merged. Office database/canonical
Court data and Award Intelligence untouched. Only explicit public PDF GETs were
used; no CAPTCHA, DHC search POST, assisted action, sync or review decision occurred.
All inference is loopback-only. PDFs/full runtime caches are not committed.
Both unrelated dirty reference checkouts were not edited/reset/stashed/cleaned.

Officer-screen evidence: Checkpoint-A screenshot at
`C:\Users\ashwa\AppData\Local\Temp\lac-14604-checkpoint-a.png` was visually
inspected with the six-order history and grounded answer. A later browser rebind
was blocked by browser URL policy; it was not bypassed and no fresh screenshot
claim is made. The final routine/history/continuity presentation has rendering
regression coverage. Isolated preview is `http://127.0.0.1:5176/court-intelligence-demo.html`
and uses loopback Q&A on 8097, not the office/API database.
