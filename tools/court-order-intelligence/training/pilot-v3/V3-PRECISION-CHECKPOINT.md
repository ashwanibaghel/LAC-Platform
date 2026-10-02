# V3 precision correction checkpoint — 3 October 2026

Previous source HEAD: `cc5e22d62a6da2282829974222a7806b98276b6d`.

## Compatibility recovery, before acquiring additional sources

32 baseline quarantine records were rechecked against native source-bound
context. **31 recovered; one remains quarantined: `6108-position`.**
Its earlier Master List requirement has UNKNOWN later lifecycle; later silence
cannot certify that it remains a current requirement. No legacy target or
selected passage was rewritten.

The recovery removes false positives for judicial modal/permission wording,
recognizes source-confirmed filing duties without changing the filing field,
recognizes explicit recorded performance with the same actor/task and obligation
context, and binds native adjacency rather than concatenating chosen anchors.
`compatibility-baseline-cc5e22d.json` and the `precision_recovery` section of
`composite-reuse-audit.json` list all 32 IDs, original rejection and result.

218 deduplicated legacy TRAIN examples pass. Recalculated coverage **before new
PDF acquisition**: attribution 12 examples / 10 groups; positive LAC actions 11 /
3; positive compliance 4 / 2; certified complete current position 1 / 1.
Twelve rows have a multi-order task name, but focused/listing questions and
incomplete action/condition snapshots are not counted as full-chain synthesis.

## Only targeted additions after recalculation

Seven actual official PDFs were read; one non-PDF download was rejected. Most
were procedural, not treated as rich synthesis. Nine separately reviewed task
examples were added, including the 5202 referral/timeliness/disposal chain and
the expressly recorded DDA affidavit filing in 10308. Sources remain ignored
locally; there are no private workbook files, raw PDFs or weights in this commit.

Current candidate pool: **227** examples / 21 conservative groups / 24 matters.
Attribution **17 / 11**; positive LAC actions **12 / 4**; positive compliance
**5 / 2**; certified complete current position **2 / 2**. These are candidates,
not a frozen training release. Four unsafe, unfrozen holdout reservations are
explicitly quarantined and are not moved into TRAIN.

## Context and identical comparison contract

Uncompacted legacy prompts reach 5,351 tokens. The tested V3 input presentation
deduplicates repeated metadata/text while restoring the original complete input
exactly. All 218 reused records fit 4,096 with a 512-token output reserve;
maximum compact prompt is 2,780. No evidence truncation or dropped example.
CUDA forward/backward headroom is **not yet proven**.

Stock/V1/V2/V3 evaluation uses the same V3 gate before gold scoring. This does
not rewrite frozen legacy evaluations or claim any new model-quality result.

## Verification

- Full .NET: 762 passed; no hang (4.49 minutes).
- Frontend: 87 passed, after restoring dependencies with `npm ci` (no lockfile change).
- Direct Vite production build: passed; existing large-chunk warning only.
- Runtime Python: 116 passed.
- Training Python: 134 passed.
- Kaggle Python: 34 passed.
- All 18 frozen V1/V2 dataset/review files match their original manifests.

No production runtime, backend/domain/schema/migration or main merge change.
**GPU NO-GO:** genuine positive-action and full-chain coverage still below the
approved thresholds; fresh holdout freeze and T4 proof remain unfinished.
Next work must fill those real gaps, not add generic infrastructure or inflate
counts with paraphrases, routine listings or shared judgment families.
