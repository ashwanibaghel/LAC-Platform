# Pilot V2 — frozen targeted pilot data

The 2 October pre-GPU gates below are complete. This is not an adapter or a
quality-improvement claim. Pilot V1 remains immutable. The older checkpoint
notes below document the state before freeze, not current readiness.

## Final pre-GPU freeze (2 October 2026)

`manifest.json` freezes 132 training, 27 validation and 37 blind examples,
their reviewed-source bindings, annotation versions and matter/leakage groups.
No matter/group crosses splits. Full native-source and bounded-passage overlap
checks exclude V1 protected data from V2 training and all V1 data from V2
evaluation. Protected gold was authored before any model outputs. It must not
be used for another tuning loop after evaluation.

Training includes 12 different current-position decisions over **four real
three-order matter chains**, not 12 independent chains. These ask different
chronology, active-action, unresolved-dispute and completion questions using
existing training sources; no synthetic order or extra source collection.
Positive LAC-action questions remain 11. Only eight raw attribution examples
survive the strict full-source runtime gate, a real limitation of this pilot.
The pre-freeze source review corrected the 9093 training position target to
select the actual SDM-report passage rather than the petitioner's prayer.
No protected prediction or gold was used to make that correction.

All 196 prompt/target records fit the unchanged 2048 training-token cap.
All 196 constrained-inference **prompts** fit the existing Pilot cap of 2048;
generation remains at most 512 tokens. The eight earlier flags came from an
additional conservative *prompt plus full output reservation* check, which
the existing `pilot_train.py` never imposed. The pinned base config has
262144 max-position embeddings. No cap, evidence, schema or output policy was
changed to resolve these flags. `all-splits-context-audit.json` retains both
measurements and records the actual existing cap transparently.

Fifteen training extraction chunks remain quarantined. Additional protected
runtime-incompatible chunks and cross-page qualified propositions are listed
in `pre-freeze-audit.json`; no labels were weakened to make them fit. Claims
tasks are oracle reviewed-evidence selection, not end-to-end PDF extraction.
Gold is a Codex source-review pass, **not independent human legal certification**.

The one-run pipeline uses the exact pinned base and unchanged safety parser,
80 optimizer steps with checkpoint/resume and adapter reload proof. No
shorter-context fallback or silent example dropping is allowed. Stock, V1 and
V2 receive identical separate frozen V1 regression / V2 validation / V2 blind
sets, constrained decoding and output bounds. V1 regression is 44 validation
plus 37 blind examples, **not 81 truly blind matters**. A checksummed existing
V1 kernel artifact mount supplies the comparison adapter; it is never modified.
Weights, workbook, PDFs, complete native pages and secrets are excluded from Git
and the public-passage upload bundle. No deployment or main merge is authorized.

Base: `Qwen/Qwen3-4B-Instruct-2507`, revision
`cdbee75f17c01a7cc42f958dc650907174af0554`.

## Source coverage

The latest `foundation-audit.json` records acquired official source versions,
the subset fully read and passage-reviewed, exact PDF SHA/page/date/URL bindings,
and relationships that cannot cross reserved splits. Acquisition is not gold
approval. A source listing date is never treated as proof of a subsequent order.
Coverage remains supplied/publicly discovered orders, not complete Court history.

Actual training-candidate three-order chains include 4806/2014, 13932/2025,
and the common-source 6108/2015 family. 4255/2016 shares that family and is not
an independent leakage group. Positive LAC instructions, conditional costs,
party assertions, recorded service, quoted earlier instructions and a closed
right to file are independently distinguished in the source review.

A reserved blind four-order chain for 10105/2023 was found through public
indexes and acquired from official DHC PDFs: 6 December 2024, 15 May 2025,
11 July 2025 and 31 August 2026. It must never be moved to training.
3282/2024 now has two acquired orders. No exhaustive-history claim is made.

Private workbook inventory is only an acquisition aid. Workbook data, private
email download/session URLs, native full-page dumps, PDFs and model weights are
not part of this directory or a Kaggle bundle. PDF files are temporary and
removed after native extraction; no OCR or CAPTCHA automation is used.

## Outstanding release gates

1. Complete additional substantive chains and independently reviewed task targets.
2. Complete fresh validation/blind gold and freeze leakage groups before training.
3. Test truthful gold against the unchanged runtime, quarantining incompatibility
   rather than relabeling Court directions or relaxing safety checks.
4. Audit every input with the pinned tokenizer; never truncate evidence to fit.
5. Release the actual V2-DATA checkpoint, then one bounded Kaggle training run.
6. Compare stock/V1/V2 on separate frozen sets and perform officer Q&A acceptance.

No application/domain/database/deployment code is changed by this foundation.

## Offline task foundation checkpoint (2 October 2026)

`train-foundation.jsonl` currently contains 124 source-reviewed, contract-valid
task examples from 21 order versions / 12 training matters / 94 passages.
This includes 11 positive LAC-action questions and four three-order
current-position questions. Questions are not independent orders or matters.
There are only 13 independent positive LAC passages; the larger planning target
is not met. Source reservations cover 28 acquired matters / 48 versions.

The 9093/2022 chain now includes actual April 2024, July 2024, February 2026
and August 2026 orders. Its selected three-order task does not let a trivial
intermediate adjournment erase the original demarcation/possession/compensation
dispute. Petitioner's claims and the SDM report remain attributed; LAC's
joint-meeting/records obligation is not proof of handover or compensation.
Reserved validation 6504/2023 now has four actual source versions; its labels
and questions are not yet released or frozen and never enter training here.

Fifteen full extraction chunks are quarantined by unchanged runtime expansion.
Their independently reviewed labels remain unchanged; no offending fact is
silently dropped to make an extraction target pass. Claims tasks receive
reviewed structured roles and are explicitly **oracle-evidence selection**,
not end-to-end raw-PDF extraction. Attribution has only eight passing examples;
do not claim that this foundation adequately repairs attribution yet.

The read-only `v1-native-overlap-audit.json` compares full native source bodies,
source identities, common related identities and hashes with all 22 frozen V1
orders. A V2 evaluation collision with V1 training is also prohibited because
the V1 adapter has already seen that data. No source/split is moved on collision.

Training/runtime/schema safety suites: 69 foundation tests, 116 unchanged
Court-intelligence tests and 23 Kaggle-contract tests passed. No GPU run,
dataset upload, model-quality improvement, final V2-DATA checkpoint, deployment
or main merge has occurred. `context-audit.json`, when generated, is an offline
tokenizer measurement only; it does not authorize training or freeze splits.

The pinned tokenizer was loaded offline from checksummed immutable V1 adapter
tokenizer files using Transformers 4.56.2; model weights were not loaded.
All 124 training prompt+target records fit 2048 tokens without truncation.
With the unchanged constrained-inference template and a conservative 512-token
output reservation inside that same budget, only 116 fit. Eight remain context
review items, including three current-position examples. This is a local
acceptance-budget check, not a claim that Qwen's original model window is 2048.
Do not silently discard those chains or waive the output reserve to inflate
readiness; any final bounded retrieval/context decision must be explicit,
measured and identical across stock/V1/V2 comparison.

Local tokenizer dependencies are installed only in ignored
`local-private/t2-tokenizer-env`. Downloads initially timed out and encountered
low C: disk space; the successful retry used task-specific D: temporary storage.
No old artifacts, user files or dirty worktrees were deleted or cleaned.
