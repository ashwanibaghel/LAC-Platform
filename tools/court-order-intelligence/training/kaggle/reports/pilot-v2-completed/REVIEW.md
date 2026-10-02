# Court Intelligence Pilot V2 — completed-run review

## Decision

**Training infrastructure succeeded. The targeted V2 quality/safety acceptance gate did not pass. Do not promote or deploy this adapter.**

The completed Kaggle run is `ashwanibaghel9027/lac-court-qwen4b-pilot-v2`, version 1. No retraining, source changes, protected-label edits, deployment or main merge was performed during this review.

## Evidence and verification

- Kaggle reports COMPLETE. This means the job finished, not that all product gates passed.
- Downloaded `pilot-v2-evaluation.json`, all 435 saved evaluation outputs, training metadata, representative parser outputs, adapter/tokenizer files and final checkpoint-80 files.
- 26 downloaded files listed in the final artifact checksum manifest were independently SHA-256 verified: zero mismatches. The manifest lists 45 files total; earlier checkpoint files were not downloaded in this review. This is not a claim that all 45 were checked.
- Independently recomputed every aggregate from the 435 saved outputs and the original frozen upload records. All nine model/split aggregates match the saved report.
- 145 held-out examples were evaluated for each of stock, V1 and V2: 81 V1 regression, 27 fresh V2 validation, 37 fresh V2 blind. These are examples, not 145 independent matters. The 81 V1 regression examples are 44 validation plus 37 blind examples, not 81 blind matters.
- V2 training used all 132 frozen examples; no oversized example was dropped. Validation/blind remained separate from training. No protected dataset was changed after results were available.

## Training execution

| Check | Observed result |
|---|---|
| Base | Qwen/Qwen3-4B-Instruct-2507 |
| Immutable revision | cdbee75f17c01a7cc42f958dc650907174af0554, verified |
| GPU | Tesla T4, 14.56 GiB VRAM; CUDA 12.4; FP16, native BF16 unavailable |
| Peak allocation | 8.20 GiB |
| Trainer steps | 80/80 |
| Actual optimizer updates | 79; one scaler-skipped update |
| Training gate | PASS |
| OOMs | 0 |
| Checkpoint/resume | step 10 to step 80, passed |
| Adapter save/reload | passed |
| Training duration | 17.74 minutes |
| Three-model evaluation duration | 65.45 minutes |
| Representative parser gate | 5/6 accepted; INCOMPLETE_PARSER_GATE |

The isolated nonfinite gradient did not prevent the run from completing; the durable optimizer counter shows one skipped update. It must not be described as 80 successful weight updates. Low training loss does not establish held-out model quality.

## Same-run exact-target results

| Frozen set | Stock 4B | Pilot V1 | Pilot V2 |
|---|---:|---:|---:|
| V1 regression | 44/81 (54.3%) | 62/81 (76.5%) | 62/81 (76.5%) |
| V2 validation | 16/27 (59.3%) | 19/27 (70.4%) | 17/27 (63.0%) |
| V2 blind | 21/37 (56.8%) | 26/37 (70.3%) | 25/37 (67.6%) |

V2 improves on stock overall, but does not improve on V1: unchanged on the old regression and lower on both fresh sets. The fresh combined result is V1 45/64 versus V2 42/64. These small, clustered samples do not support a statistical or deployment-quality claim. Exact target is a strict parser-plus-target match, not a broad natural-language quality score.

## Targeted acceptance gates

| Metric / set | V1 | V2 | Assessment |
|---|---:|---:|---|
| Positive office-action fact recall, old regression | 4/6 | 3/6 | Regressed; mandatory gate fails |
| Office-action precision, old regression | 4/8 | 3/4 | Improved precision, but insufficient recall |
| Positive office-action fact recall, fresh validation | 1/3 | 0/3 | Regressed; misses all three gold facts |
| Old multi-order current-position exact | 0/3 | 0/3 | No recovery |
| Fresh multi-order exact, validation plus blind | 1/5 | 1/5 | No improvement; one validation chain and one blind chain |
| Attribution exact, old regression | 0/3 | 0/3 | No recovery |
| Attribution exact, fresh validation | 0/2 | 0/2 | No recovery |
| Attribution exact, fresh blind | 1/6 | 1/6 | No improvement |
| Wrong category / old regression | 18 | 18 | Unchanged fact-level count |
| Wrong scope / old regression | 6 | 6 | Unchanged fact-level count |
| Wrong field / old regression | 18 | 14 | Partial improvement, not attribution recovery |
| Compliance exact / old regression | 8/9 | 7/9 | Regressed |
| Compliance exact / fresh validation plus blind | 13/13 | 13/13 | Maintained; these are negative completion tests |
| Date QA exact / old regression | 33/36 | 34/36 | Improved |
| Date QA exact / fresh validation | 11/12 | 8/12 | Regressed |
| Date QA exact / fresh blind | 12/14 | 10/14 | Regressed |

The fresh blind office-action tests contain **zero positive LAC-action gold facts**. Thus a reported recall denominator of 0/0 is not 100% recall and cannot demonstrate positive-action learning. V2's 6/7 exact action answers there mostly measure correct abstention; one false-positive answer remains.

Across negative claims tests, unsupported selections were:

| Set (negative-example denominator) | Stock | V1 | V2 |
|---|---:|---:|---:|
| V1 regression (21) | 11 | 0 | 1 |
| Fresh validation (15) | 4 | 1 | 0 |
| Fresh blind (21) | 5 | 2 | 1 |

V2 is much safer than stock on these counts and improves fresh negative selection versus V1, but loses V1's zero-error old-regression achievement. Across all evaluated splits, unretrieved fact IDs decreased from stock 7 / V1 6 to V2 1. Invalid IDs are rejected by the parser; they are not released as valid cited evidence.

## Concrete failure review

### Representative parser rejection

`wpc9093-2022-apr2024-attribute-0` returned valid JSON but mislabeled anchor 2 as PETITIONER_SUBMISSION rather than CASE_CONTEXT. It also labeled the next-hearing anchor PROCEDURAL_EVENT rather than the reviewed COURT_DIRECTION. Replaying the stored output through the unchanged parser reproduces:

> Party attribution is not independently confirmed in source context

The fail-closed guard worked. This is not a JSON syntax failure, transport failure or missing model file. Five other representative outputs were parser-accepted, but parser acceptance alone does not establish correct answer selection; the representative current-position output was not the reviewed complete target.

### Missed real office work

- W.P.(C) 6384/2024: selected conditional personal appearance, but omitted the renewed costs-deposit direction.
- W.P.(C) 6203/2026: missed a last-opportunity affidavit filing direction whose actor is carried by the preceding sentence. This is a difficult contextual/passive direction, not absent evidence.
- Fresh validation W.P.(C) 6504/2023: selected observations about a missing report/unclear status rather than the explicit LAC digitization-status report direction. Another action output cited an out-of-range fact ID and was blocked.

### Completion confused with future direction

W.P.(C) 8404/2024 compliance question: V2 selected the direction to send a reference within one month as its completion answer. The reviewed target is empty: a requirement to do something is not proof that it was done. Its citation is real, but its use as a compliance answer is semantically wrong. The ID-validity parser does not catch this type of task-relevance error.

### Multi-order synthesis still weak

All three old current-position tests fail. In fresh blind W.P.(C) 10105/2023, the broad current-position answer selected every supplied fact, including older filing events, instead of the reviewed current-position subset. A narrower compensation question did correctly retrieve the separate petitioner stand, respondent explanation and Court's unresolved issue. That is a useful success, but all three models passed that narrower question; it is not a V2-specific gain.

### Over-abstention on available facts

V2 returned empty answers on eight fresh positive date-QA examples, versus three for V1. This helps some negative-answer counts but loses useful source-backed answers. It is not sufficient-evidence discipline when the requested evidence is actually supplied.

## Interpretation and limitations

The results support a **selectivity/recall trade-off**, not reliable chronology improvement. The training set had 55 empty-target questions among 124 claims tasks, only eight passing raw attribution examples, and 12 current-position questions over four independent training chains. These are plausible contributors to over-abstention and limited generalization, not a proven causal attribution from one experiment. No claim that the model architecture is inherently unsuitable follows from this run.

Claims tasks already receive reviewed structured roles and page/source evidence. This evaluation does not prove end-to-end PDF extraction, free-form officer answer quality, completeness of Court order history, or production semantic citation safety. Some category differences can also be label-taxonomy distinctions rather than party-to-Court misattribution. The metrics must not all be described as hallucinations. No submitted party stand was promoted to a Court category in the directly measured submission-as-Court counter, but this small, partially quarantined benchmark is not proof that such failures are impossible.

The same-run V1 regression total reproduces 62/81. The current stock date-QA subtotal is 26/36; an older V1 report recorded 28/36. All comparisons above use this completed run's internally verified outputs rather than mixing baseline runs.

## Recommended next decision

1. Preserve V1 and V2 separately as immutable experimental artifacts. Do not deploy V2 or overwrite V1.
2. Keep these frozen evaluation sets unchanged as regression sets. Do not train on their exact passages, questions, targets or connected-source groups.
3. Before another GPU run, review the training-only attribution/actor-context and lifecycle representation, plus the mismatch between syntactic citation validity and semantic office-action/compliance relevance. Do not weaken the parser to turn rejected outputs into apparent successes.
4. Any follow-up pilot needs independently sourced equivalent patterns and a fresh untouched final blind set. Improving this already-reviewed blind set through tuning cannot be reported as fresh-blind improvement.
5. No architecture switch or open-ended data collection is justified automatically. Decide a bounded next experiment only after reviewing these failures.

Source worktree remains clean; no application, backend/domain/schema/migration, Award Intelligence or canonical Court data was changed. No DHC request, CAPTCHA, live Court action or office deployment occurred during this review.
