# Pilot V2: targeted gold foundation, unchanged Qwen 4B

Status: FOUNDATION / NOT TRAINED. No new GPU training launched by this plan.

Keep Qwen/Qwen3-4B-Instruct-2507 at
`cdbee75f17c01a7cc42f958dc650907174af0554`. No architecture or product changes.
V1 uploaded training source: `90d3be08ff023ce237a256e8287297db3f358301`.

## V1 freeze and first-result interpretation

Preserve the original kernel output, adapter, checkpoints, optimizer, scheduler,
scaler, RNG, configuration, manifest, raw predictions, and original checksums in
the ignored `local-private/pilot-v1-frozen` directory. Verify every checksum.
Never overwrite V1 files or retrospectively edit its scores. Weights stay out of
Git. Kaggle V1 kernel output remains the independent original copy.

81 examples are **not 81 blind matters**: 44 validation examples on 2 matters,
37 blind examples on 2 different matters. V1 trained on 11 other matters.
V1 exact-target correctness: stock 44/81, adapter 62/81. Its first safety gate
failed. Raw tuple mismatches were originally labeled wrong_role_or_scope but
include field mismatches; retain that metric and add disaggregated diagnostics.

V1 adapter has 19 failed examples (11 validation / 8 blind): overlapping buckets
are attribution/extraction 6, multi-order 3, office-action 4 (2 misses, 3 extras),
date-QA 3, important-fact selection 1, order digest 1, compliance 1. Eleven
examples fail the runtime parser. All three remaining date-QA misses emit an
unretrieved fact ID, rather than merely choosing a wrong hearing date.
These are example counts, not independent matter counts; extraction and
attribution tasks can use the same passage. Do not exaggerate sample size.

At shared expected/predicted anchor IDs, stock has category/field/scope mismatch
counts 20/16/6; V1 has 18/18/6. Thus the original composite regression is **not**
evidence that category attribution alone worsened. Both still fail all 6
anchor examples. Unchanged safety parser rejects those outputs.

Compliance 8/9 is encouraging bounded evidence, not proof of generalized
understanding or end-to-end officer readiness: claims tasks receive manually
audited structured roles. V1 training has zero positive office-action targets;
this imbalance must be corrected from fresh sources, not evaluation answers.

## Protected evaluation policy

Do not train on any V1 validation or blind matter, answer, near-duplicate order,
connected matter, shared judgment or source SHA. Validation errors can guide
pattern selection, but new training targets must come from different matters.
V1 blind is now observed: preserve it as an exposed regression benchmark, not
an untouched final V2 blind. Freeze at least two **fresh** untouched V2 blind
matter groups before training; no outputs inspected until training is frozen.
Freeze a separate V2 validation set. Three models must run the same unchanged
protocol on exposed regression and fresh protected evaluation, reported apart.

## New gold targets before GPU

Planning targets, never quotas to fill with synthetic facts:

- 8-12 new trustworthy training matters, excluding all protected leakage groups.
- At least 8 independent 3-6-order chains (24-48 actual official orders).
  If fewer are available, report the shortfall; never invent dates/hearings.
- At least 40 independent source passages for positive LAC actions across at
  least 6 matters, including passive directions, conditional obligations,
  qualified periods, and actor attribution confirmed from caption/context.
- At least 40 independently reviewed contrast examples for submission vs
  finding, respondent stand vs observation, and quoted/history vs current.
- At least 24 chain questions (current position, active action, latest meaningful
  development, unresolved dispute). Multiple questions from a chain are not
  independent chains and must not dominate sampling.
- Balance difficult NONE/insufficient-evidence/completion-not-confirmed cases
  with positives. Keep realistic Hindi/Hinglish/English questions, but avoid
  duplicating paraphrases as if they were new evidence.

Each gold fact requires source SHA, exact case/date/page, verbatim passage,
reviewed category/scope/actor, qualifier, and a human-readable decision reason.
Only explicit later compliance/modification closes or supersedes a direction;
silence, disposal, or a party allegation does not establish completion.
No private workbook upload. Official PDFs temporary only; no CAPTCHA automation.

## Chain contract and context gate

Use the existing claims/anchors contracts. A training record supplies source-
dated bounded evidence from 3-6 real orders; model selects exact IDs, with all
conditions retained in the source passages. Teach each chain facet through
separate task questions; do not invent a new free-prose structured output schema.
Validate every prompt plus target against the exact pinned tokenizer's context
before upload. Never truncate evidence to fit. If 2048 is insufficient, quarantine
the oversized example or make a separately measured context decision before
training, not a silent architecture/runtime change.

## Fair comparison and acceptance (freeze before V2 output)

Stock, immutable V1 adapter, and V2 adapter use identical pinned base,
quantization, prompts, context, grammar, max output length and safety parser.
Keep raw model mistakes distinct from safe delivered/fail-closed outputs.
Record actual training updates, skipped updates, memory and checkpoint resume.
Do not substitute parser acceptance for factual correctness.

For the fixed 81-example regression suite:

- exact targets >62/81, with per-task and per-matter scores;
- office-action recall at least V1 4/6, preferably recovering stock 5/6 or better;
  action precision must not regress (V1 4/8), no newly invented obligations;
- all 3 multi-order position targets correct is the desired small-suite gate;
- category and scope errors each materially reduce; define relative >=50%
  reduction from V1 18 category / 6 scope errors, not the composite tuple score;
- insufficient-evidence unsupported answers stay 0; no guessed next hearing,
  party statement promoted to finding, lost condition, or invented compliance.

Fresh V2 protected sets must independently show safe attribution, positive
action recall and multi-order usefulness, with denominators and manual source-
chain review. Scores from different test populations must not be compared as
if 76.5% were a transferable universal baseline. Any critical unsafe delivered
claim rejects deployment candidacy regardless of average improvement.

Next executable step: acquire/audit new sources and freeze V2 groups, then
validate gold/contracts/context/task balance. Only then one bounded V2 training
run and a three-way comparison. No model switch, speculative sweep, deployment,
main merge, canonical Court changes, or Award changes.
