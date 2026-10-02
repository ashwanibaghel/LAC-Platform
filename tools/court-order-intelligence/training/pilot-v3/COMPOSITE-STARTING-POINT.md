# V3 composite TRAIN starting point - 3 October 2026

This is the requested reuse audit checkpoint, **not a completed V3 dataset or
permission to launch GPU training**. V3 does not start from zero. Existing
V1/V2 TRAIN gold is reusable subject to leakage, dedupe, source binding and the
V3 contract. Fresh-only counts in the previous progress checkpoint were not
the combined training starting point.

Previous source SHA: `9b28780536f87fe5010fc0018ee526ba064538f2`.
Branch: `codex/court-intelligence-pilot-v3`. Main not merged.

## Existing gold inventory and reuse result

| Stage | V1 TRAIN | V2 TRAIN | Total | Conservative leakage groups |
|---|---:|---:|---:|---:|
| Frozen reviewed TRAIN inputs | 113 | 132 | 245 | 21 |
| After semantic dedupe / protected-group audit | 87 | 132 | 219 | 21 |
| V3 contract-compatible reuse candidates | 76 | 110 | 186 | 21 |
| Additional V3 contract review queue | 11 | 22 | 33 | Not training-eligible |

There are 23 original train matter identities, not 23 independent groups.
The 26 deduped rows are V1 multilingual variants with identical dated/page-bound
evidence and supervision. Cross-version V1/V2 duplicate count is **0**.
Different tasks and different actual order dates are not duplicates merely
because they reuse an evidence passage. Canonical records retain their original
language, truth, bounded source evidence, SHA, URL, order date and page.

| Coverage | Eligible deduped legacy gold | Passing current V3 contract |
|---|---:|---:|
| Attribution | 12 / 10 groups | 11 / 9 groups |
| Multi-order current-position task | 13 / 4 groups | 11 / 3 groups |
| Positive LAC action | 11 / 3 groups | 2 / 1 group |
| Empty LAC action | 27 | 27 |
| Compliance | 36, including 4 positive | 32, all empty |
| Positive date QA | 35 | 28 |
| Empty date QA | 34 | 34 |

These are task counts, **not certification that all current-position questions
meet the final strong synthesis gate**. In particular, listing-only and focused
lifecycle questions must not be misreported as full dispute/action synthesis.
The minimum additional legacy-pool coverage is 18 attribution examples,
12 current-position examples plus 4 independent groups, and 14 positive LAC
actions. This lower-bound assumes the existing reviewed examples are genuinely
compatible and strong; the executable V3 candidate pool has larger shortfalls
until its review queue is resolved or definitively excluded. Do not collect a
fresh corpus from scratch or count source/question paraphrases as new gold.

Languages after dedupe: English 132, Hinglish 36, Hindi 25, RomanHindi 26.
Languages in V3-compatible candidates: English 119, Hinglish 26, Hindi 20,
RomanHindi 21.

## Audit boundaries and source-only context

- Both original manifests and V2 review-file hashes verified unchanged before
  and after the audit. Original TRAIN records also match independent annotation
  compilation. V1/V2 validation/blind targets are not imported into new gold.
- Conservative grouping includes frozen groups, documented connections, common
  cited judgments, exact source SHA/URL, exact/near passages and full native
  order-body overlap. No protected-group TRAIN exclusion was found in this pool.
- Existing full native records were used locally. No live DHC requests, model
  inference, CAPTCHA, workbook upload, PDF upload or database access occurred.
- Added field/actor metadata comes from the original reviewed passages, not
  target selections. Explicit reviewed lifecycle additions are in
  `annotations/v3_reuse_context.py`; no later silence/expired deadline is used
  to manufacture completion. Scope is the bounded supplied-order snapshot,
  never current canonical office status.
- Adjacent actor/condition context is an exact contiguous same-page native
  excerpt. Original provenance and selected target text remain unchanged.
- Existing parser and semantic gate are **unchanged**. No label was altered to
  make a gate pass. Thirty-three incompatible candidates remain quarantined:
  20 judicial/party-assertion gate rejections, 4 completion wording rejections,
  8 active-action gate rejections, and 1 anchor expansion rejection. This is
  contract incompatibility, **not a finding that all old reviewed truth is wrong**.
  Legitimate filing/recorded-performance wording needs careful compatibility
  review; do not substitute easier labels or silently accept the queue.

## Fresh holdouts are not yet frozen

- Validation candidate 17105/2025 shares Okaya/Jeevantika authorities with prior
  training: replace/quarantine before freeze.
- Blind candidate 10546/2023 is cited by prior TRAIN CONT.CAS(C) 1094/2026:
  cannot be a fresh independent holdout.
- Blind CONT.CAS(C) 724/2021 shares BSK judgment context with prior sources:
  conservative independence review/replacement required.
- 12473/2025 (validation) and 17094/2025 (blind) share HARENDER & ORS and Award
  02/2024/SW dated 18 November 2024. Treat as one conservative group; replace one
  reservation before freeze. Neither source may move to training.

No new final V3 TRAIN gold additions or frozen V3 validation/blind examples have
been exported in this checkpoint. Candidate reservations are not frozen groups.

## Context and exposure diagnostics

Offline checksum-verified pinned Qwen tokenizer, Transformers 4.56.2; no weights
loaded. Exact V3 enriched contexts were checked without truncation or dropping:

- Training fit at existing 2048 cap: **173/186**.
- Constrained inference prompt fit at 2048: **173/186**.
- Prompt plus 512 output reserve fit at 2048: **136/186**.
- Maximum training tokens: **2663**; maximum inference prompt tokens: **2685**.

Thirteen candidates exceed the existing cap. They remain present. Context
representation/budget must be resolved explicitly before final freeze; no
shorter-context fallback or silent evidence removal is permitted.

Provisional CPU-only sampler probe: seed 20261003, accumulation 4, two nominal
equivalent passes => 93 Trainer steps / 372 exposures. **Not a launch budget.**
Attribution 72; current-position 72; office-action 72; compliance 36;
extraction 36; important facts 36; date QA 24; digest 24. Only 123/186 distinct
records exposed under this probe. Office-action positive/empty exposure 54/18,
but those 54 positives repeat just **two** source-reviewed examples. Compliance
exposure is 0 positive / 36 empty. Date QA exposure is 18 positive / 6 empty.
Task weighting does not cure insufficient positive breadth or manufacture gold.

## Requested pre-GPU report - current checkpoint

1. Reused V1 TRAIN: 87 eligible after dedupe; 76 pass current V3 contract.
2. Reused V2 TRAIN: 132 eligible; 110 pass current V3 contract.
3. Deduped overlaps: 26 language variants; 0 cross-version duplicates.
4. Protected exclusions: 0; V3 contract quarantine: 33.
5. NEW V3 TRAIN examples: 0 in this checkpoint.
6. FINAL composite train: **not frozen**; current passing candidate pool 186.
7. Candidate independent TRAIN groups: 21 (23 matter identities).
8. Attribution: candidate 11/9 groups; eligible legacy 12/10.
9. Multi-order: candidate 11/3 groups; eligible legacy 13/4; strong synthesis
   certification still required.
10. Positive action: candidate 2/1 group; eligible legacy 11/3.
11. Positive/empty distributions: reported above, including compliance shortfall.
12. Fresh V3 validation gold/groups: not frozen; 0 final exported examples.
13. Fresh V3 blind gold/groups: not frozen; 0 final exported examples.
14. Blind positive-action coverage: not yet established as VERIFIED_GOLD.
15. Sampler exposure: provisional candidate-pool diagnostic, not final exposure.
16. Context audit: performed on all 186 candidates; 13 exceed current 2048 cap.
17. Exact FINAL step budget: **not determined**; 93 is only a diagnostic probe.
18. Decision: **NO-GO**. Do not launch until coverage, source/context compatibility,
    fresh holdout freeze, final token/exposure checks and actual Kaggle integration
    pass. Preserve the same pinned Qwen 4B/rank-8 plan.

## Verification

273 software tests passed: 116 existing runtime, 123 training (14 new composite
audit tests), 34 Kaggle contract tests. These are software safety results, not
model accuracy or a V3 training result. No backend/domain/schema/migration,
Award Intelligence, office deployment or main merge change.
