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

## Final-mission runtime proof checkpoint — 3 October 2026 (India)

Actual Transformers 4.56.2 / Accelerate 1.10.1 CPU Trainer now consumes an
immutable, finite Curriculum-selected map dataset through a SequentialSampler.
Trainer alone skips consumed microbatches on resume; no generator cursor or
double-skipping. Curriculum state saves at completed accumulation boundaries.
Checkpoint state is verified against the original dataset fingerprint and index.

Connected-case diversity uses leakage_group, not separate case numbers.
Two actual CPU proof reports are retained; the v3.2 report is authoritative for
the new group-aware curriculum. Both are SOFTWARE fixtures, not Court training.
Continuous 80 steps/320 consumed samples match step-10 checkpoint/resume exactly.
Simulated skipped update: 80 attempts/79 applied, accounting preserved on resume.
Real CUDA GradScaler behavior and QLoRA fitting are NOT proven by this CPU test.
Torch CPU 2.10.0 differs from Kaggle's pinned CUDA Torch 2.6.0; report this openly.
No base model weights, network inference, GPU or real gold used in this proof.

Proof exposure (320 software samples, NOT final V3 training budget):
attribution 61, current-position 62, action 60, compliance 32, extraction 30,
important-facts 33, date-QA 22, digest 20. Action positive/empty 45/15;
date-QA 17/5. Three fixture leakage groups, zero consecutive group repeats.
Final V3 exposure and budget remain unavailable until trustworthy gold is frozen.

Semantic gate also rejects party assertions mislabeled judicial facts across
digest/current-position/important-fact claims. Existing source/structural checks
remain first; no gate weakening and no office runtime deployment.

Acquired records now: 19 (9 train, 5 validation, 5 blind reservations), not 19
independent verified groups. No acquired record is automatically gold.
Holdout source review found 12473/2025 and 17094/2025 share petitioners and
Award 02/2024/SW; possible related acquisition chain requires explicit review.
Do not freeze them across validation/blind without resolving that relationship.
Neither source may be moved to training. A new unrelated replacement may be
reserved before freeze if necessary. Existing V1/V2 protected sets remain intact.

**Pre-GPU decision: NO-GO.** Actual VERIFIED_GOLD counts remain zero:
attribution shortfall 30 examples/10 groups; current-position shortfall 25/8;
positive LAC-action shortfall 25. Fresh holdouts not frozen, positive blind
coverage not yet proven, source-derived runtime lifecycle not bound, context
audit and final budget not complete. No synthetic padding and no GPU launch.
The new launch checklist refuses missing/under-threshold evidence, including a
blind set with zero positive office actions. It is not by itself an independent
gold audit or a claim that the future Kaggle launcher has already been wired.

Remaining work: richer independent source chains; source-bound gold, lifecycle
and attribution review; production-compatible context and semantic-gate proof;
full protected native overlap; holdout positive coverage/freeze; token audit;
final exposure/budget; allowlisted V3 Kaggle runner using CurriculumTrainer and
launch gate. Then ONE run, four-system split-separated and Level-2 evaluation.
Product integration waits until the authorized GPU fit is actually launched.
