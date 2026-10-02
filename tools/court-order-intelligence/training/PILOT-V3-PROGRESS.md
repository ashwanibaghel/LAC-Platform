# Pilot V3 — preparation checkpoints (not a training/quality result)

Preserved V2 source: `fc954b68d94784ee722d846591b1efeda1102962`.
V2 review/metadata are frozen separately. V2 quality gate FAILED; no deployment.
V3 branch: `codex/court-intelligence-pilot-v3`. No main merge.

## V3-A: opt-in semantic safety foundation

- Unchanged legacy structural/source parser first, then deterministic task gate.
- Completion requires recorded performance, not a future direction, permission,
  promise, expiry, silence, negation or party assertion.
- Office action requires source-supported LAC actor, operative Court direction,
  current source scope and explicitly reviewed OPEN/PARTIAL lifecycle.
- Next-hearing selection requires explicit source listing/date and latest-confirmed
  date metadata. A mentioned historical date is not a next hearing.
- Current-position selection cannot promote quoted or stale/unknown directions.
- New chronology context exposes actor, role, field, source date, scope, reviewed
  lifecycle/links and latest supplied order. Missing knowledge stays UNKNOWN.
  Latest supplied order does NOT automatically mean latest meaningful development.
- Original verbatim evidence, qualifiers and conditions remain untouched.
- Eight case-neutral regression tests pass. These are software tests, NOT new gold.
- This is an opt-in V3 module, not an office runtime deployment. The existing
  production chunk/extraction and source validation must precede it in V3 Level 2.

## Required before a GPU launch

## V3-B: deterministic curriculum foundation

- SHA-256 counter-seeded task shuffle; task weights 6 attribution/current-position/
  office-action, 3 compliance/extraction/important-facts, 2 date-QA/digest.
- Positive/empty pools separate; office-action/date-QA exposure 3:1 when both
  pools exist. Original records are never duplicated or rewritten.
- Matter and example shuffled decks; avoid consecutive same matter when another
  matter exists in the selected bucket. Actual exposure report records repeats.
- Persist version, seed, immutable dataset fingerprint, logical consumed index,
  shuffle counter and task/polarity/matter/example decks. Resume replay verifies
  all state rather than trusting a changed curriculum.
- Eight sampler tests pass, including continuous 80 steps vs resumes at 1/10/37/79
  with accumulation 4, changed dataset/state rejection and prefetch separation.
- This proves logical CPU sample continuity, NOT actual Kaggle Trainer integration.
  GPU runner must checkpoint consumed microbatches at accumulation boundaries,
  avoid double-skipping on resume and prove identical Trainer sample IDs.
- Budget calculator derives updates from final count, requested equivalent passes
  and accumulation; ceiling violations fail rather than silently truncate.
  No final V3 exposure/budget is claimed before V3-C/D data exists.

## Required before a GPU launch (continued)

Fresh source-reviewed lifecycle/actor relationships must be bound to exact pages;
never infer them from target labels, silence or a generated model assertion.
The current claims contract selects evidence IDs, not unrestricted generated
compliance states. OPEN/COMPLETED/SUPERSEDED/NOT_CONFIRMED_COMPLETE examples need
an explicitly reviewed state-selection representation before dataset freeze.

Frozen legacy comparisons must remain separate from common-gate V3 comparisons.
All four systems receive identical new context and gate for the latter. A guard
catch is NOT an adapter improvement. Do not use legacy missing metadata as proof
that V1/V2 weights failed a task under the new contract.

V3-C/D are NOT complete: no fresh V3 train/validation/blind is frozen yet.
No V3 GPU training or evaluation has been launched. Capacity remains rank 8,
alpha 16, q/k/v/o, pinned Qwen3-4B-Instruct-2507 revision unchanged.
No Court database, backend/domain/schema/migration, Award or canonical action change.

## V3-C discovery in progress — not VERIFIED_GOLD

Candidate reservations: 13 train identities, 5 validation, 5 blind, all absent
from previously used V1/V2 gold and known protected connections. These are
candidates, NOT proven independent matter counts or a frozen evaluation set.
Private workbook stays local, used only for discovery of public official URLs.
Acquisition is append-only, bounded GET of direct public PDFs, temporary PDF
cleanup, no CAPTCHA/search POST or model. Acquired native text stays ignored.

First train batch acquired four source records, not four independent matters:

- 10338/2015 and 10341/2015 return the same joint 13 July 2026 order, with
  10337/10338/10339/10340/10341 captions. Dynamic download footer changes bytes;
  different SHA does NOT establish independent evidence. Treat as one group.
  This source contains only no-time-left adjournment/listing, no positive action.
- 1069/2025: 18 February 2026 routine adjournment/listing; no positive LAC action.
- 5997/2024: 29 April 2024 contains quoted petitioner prayers, exemption application
  disposal while the writ continues, notice, affidavit/service timelines and an
  interim direction. A filing requirement is NOT completed filing. ADM caption
  is not automatically a source-supported LAC identity. Needs actor/chain audit.
- 10340/2015 discovery URL is missing/ambiguous; no guessed download or gold.

No fresh example has yet been promoted to VERIFIED_GOLD. Full page/relationship,
native overlap, source-only actor/lifecycle and production-extractor checks remain.
No V3 training launch; positive action and multi-order coverage gates not met yet.
