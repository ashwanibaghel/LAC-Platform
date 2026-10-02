# Court Intelligence training foundation (T1)

Branch: `codex/court-intelligence-training-v1`.
Frozen product baseline: `0cea02e9b3c1d0fa7623ffb1f69e9681fa71279e`.
No UI, extraction, inference service, API, domain, schema, database or Award changes.
T1 produces a small source-audited **development seed**, not a trained model or
proof of generalization. No GPU/Kaggle training or model-weight download ran.

## Model pin

`Qwen/Qwen3-4B-Instruct-2507` at immutable revision
`cdbee75f17c01a7cc42f958dc650907174af0554`.
Official config/card/license/tokenizer metadata verified 2 October 2026:
Apache-2.0, `Qwen3ForCausalLM`, 4.0B, non-thinking-only instructions,
`Qwen2Tokenizer`, role framing with `im_start`/`im_end` and assistant generation
prompt. Use the **pinned tokenizer's** `apply_chat_template` for eventual T2;
do not hand-insert thinking tokens. Metadata hashes and baseline are in
`model_metadata.json`. No model weights are needed by these tools.

Official repository: https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507
Pinned files: https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507/tree/cdbee75f17c01a7cc42f958dc650907174af0554

## Review and provenance

`annotations/seed_review.py` contains manually authored decisions after Codex
independently read **all native-text pages** of four exact PDF downloads. It
does not import existing checklists, artifacts, regression outputs or model
prose. This review is not a human legal certification; T1 still needs user
acceptance before pilot training. The explicit `VERIFIED_IDS` ledger approves
only enumerated reviewed examples. A new annotation without ledger approval
is UNREVIEWED. QUARANTINED/UNREVIEWED/REJECTED cannot enter gold JSONL.

Each bounded passage binds an exact official URL, binary SHA-256, order date,
page, text and text SHA. Text is verbatim with whitespace normalization only;
source numbering and qualifiers remain. Multi-order examples bind **each**
passage to its own source version, never combine one PDF's text with another
PDF's hash. No full PDF or full page is committed. Temporary PDFs were cleaned.

`verify_download_version` rejects even identical-looking text if binary SHA
changes. A changed official download must become a new version and be
quarantined until the relevant passages and review are explicitly reconciled.
The exporter does not redownload silently or replace a source. Reproduction
requires the exact audited bytes; mutable official URLs alone are insufficient.

Seed: W.P.(C) 14604/2025 (7 May and 29 July 2026), 8664/2021 (30 January 2025),
940/2015 (21 May 2026). The uncertain 2015 connected-stay candidate is excluded
and QUARANTINED. A party's reported notice-service assertion is labelled a
submission, while the Court's rejection of that assertion is a separate finding.
The LAC reference-forwarding direction retains **preferably** verbatim. Disposal
does not establish compliance. A DDA direction does not become a LAC action.

## Runtime contracts

The model returns IDs and strict JSON, not legal essays. Dataset `input` equals
the frozen runtime prompt object shape; `target` equals its output schema.
Dataset provenance/review/outcome are audit metadata, not additional output keys.
`schema/contracts.py` imports the actual frozen `ANCHOR_SCHEMA` and
`ANSWER_SCHEMA`; generated schema snapshots are checksummed in the manifest.

| Training task | Input | Target | Runtime consumer / status | Validator |
| --- | --- | --- | --- | --- |
| semantic_proposition_extraction | documentOrderDate, caseNumber, sourceRoleContext, numbered anchors | facts: anchorId/category/field/scope; needsReview | worker extraction → anchors.expand | ANCHOR_SCHEMA + exact reviewed selection |
| attribution_classification | same numbered-anchor object, source speaker context | same facts JSON | same extraction contract; no separate new endpoint | ANCHOR_SCHEMA + reviewed role/scope |
| important_fact_selection | currentCase, question, availableEvidence with factId | claims: factId | questions.answer selection contract | ANSWER_SCHEMA + reviewed selected IDs |
| office_action_detection | same evidence selection prompt, specific LAC question | claims: factId; empty for NONE | auxiliary selection supervision; production semantics.office_action stays deterministic | ANSWER_SCHEMA + exact Current Court direction to LAC |
| compliance_state | same prompt containing actual earlier/later evidence | claims: factId; empty for NOT_CONFIRMED_COMPLETE | auxiliary selection supervision; production lifecycle safety stays deterministic | ANSWER_SCHEMA + recorded completion only |
| date_specific_retrieval_or_QA | same prompt with date-bound query/evidence | claims: factId; empty for INSUFFICIENT_EVIDENCE | questions.answer + citation-bound compose | ANSWER_SCHEMA + exact source IDs |
| order_digest | same evidence prompt for one order | claims: factId | auxiliary selection task; production digest remains deterministic, not newly wired to ML | ANSWER_SCHEMA + reviewed digest IDs |
| multi_order_current_position | same evidence prompt with one matter's ordered source chain | claims: factId | auxiliary selection task; production synthesis remains deterministic | ANSWER_SCHEMA + source/version/current-order linkage |

NONE / INSUFFICIENT_EVIDENCE / NOT_CONFIRMED_COMPLETE are audit outcome labels.
They map to the runtime-compatible **empty claims array**, not invented string
outputs or unsupported prose. No positive compliance examples are claimed.
Hindi/Hinglish/Roman Hindi queries reuse identical English source evidence and
answer IDs. These are four queries for one underlying reviewed answer, not four
independent matters. A recorded listing date is not proof another hearing occurred.

For T2, select only the proper task's existing instruction prompt and schema,
encode `input` as user JSON and `target` as assistant JSON. Auxiliary tasks are
not a claim that those inference consumers exist today. No runtime adapter or
training configuration is activated by T1. Adding a T2 trainer is deferred.

## Leakage and protected splits

Grouping is deterministic union-find over matter, explicit connected-matter
relationships, common-judgment IDs, identical binary SHA, normalized duplicate
passages and near-duplicates (>=8 four-word shingles plus Jaccard >= 0.65 or
normalized-text sequence similarity >= 0.90).
Conservative overgrouping is intentional. All examples in a group must stay in
one split. Known relationships require reviewer-supplied metadata; grouping is
not a claim to discover every unknown connected case. Future larger corpora need
a manual connected/common-judgment audit before protected splits are frozen.

These three previously inspected matters are DEVELOPMENT, never pristine
validation/blind. All 22 examples are in `development.jsonl`; required train,
validation and blind JSONL files are **honestly empty**. Future untouched matters
require explicit split inventory and frozen validation/blind groups before T2.
No random sentence splitting. An inspected protected matter requires explicit
demotion and a new reviewed split version, not silent overwrite. Export checks
the prior manifest to prevent protected group/assignment drift.

## Private workbook inventory is NOT gold

`inventory_workbook.py` is a separate read-only local acquisition aid. It selects
all explicitly labelled Delhi High Court Pending/Disposed rows, includes the
observed `Diposed off` typo, and separately preserves generic `High Court` rows
awaiting court confirmation. Blank statuses and incomplete/duplicate identities
are retained as exceptions/issues. Counts are **rows, not unique verified cases**.
It never infers Court status from a register note, downloads PDFs, modifies
canonical cases or imports register narratives into training.

Output: ignored `local-private/workbook-inventory.json`. No officer directions,
brief facts, advocates, titles, villages, awards or other office sheets are
exported. Even minimized case/URL/status inventory remains private and is not
committed or uploaded. The workbook's SHA and Excel row provide local provenance.
Workbook candidates cannot become public gold until independently verified from
official public Court evidence. No workbook edit is performed.

## Privacy and validation

Only required bounded public operative passages were retained. Addresses,
phone/email, irrelevant personal IDs, advocate lists and full pages are omitted.
`redact_public_dataset.py` rejects/quarantines suspect passages; it never rewrites
an evidence string while pretending it remains verbatim. Automated patterns are
not exhaustive; independent source review is mandatory. Kaggle defaults PRIVATE.
No office data, credentials or tokens belong in public exports.

The validator rejects missing/mismatched SHA, URL, page, date, exact text/version,
unknown reviewed IDs, invalid JSON/schema, altered roles/scopes, arbitrary prose,
unapproved states, cross-case inputs, invented actors/filing/compliance/hearings,
PII and any target differing from the independent reviewed annotation. It does
not claim regex can independently establish legal truth. The annotation ledger
is the trusted reviewed input; changing it requires new source review/version.

## Run locally

Use an existing Python with the frozen worker's `jsonschema>=4.23,<5` dependency.
Only the separate workbook inventory needs `openpyxl`; no training/GPU library.

```powershell
python -m unittest discover -s tools/court-order-intelligence/training/tests -v
python tools/court-order-intelligence/training/validate_gold.py
python tools/court-order-intelligence/training/export_gold.py
python tools/court-order-intelligence/training/validate_gold.py tools/court-order-intelligence/training/generated/development.jsonl
```

Exporter runs fully offline. Deterministic UTF-8 JSONL, schemas, source versions,
quarantine list, splits, stats and content SHA manifest go to `generated/`.
Tests deliberately corrupt source/target data, check split leakage/freeze/privacy,
and export twice for byte equality. Synthetic invalid fixtures are not gold.

## Remaining gates before training

- Independently annotate more official orders from private acquisition candidates.
- Resolve generic High Court court identities and incomplete/multi-case rows.
- Source-verify connected-case/common-judgment groups before split freeze.
- Add untouched validation/blind matters; protected evaluation is not ready.
- Add actual Court-recorded compliance positives and compensation/possession chains.
- Retain the uncertain 940 stay quarantine until its exact attribution is resolved.
- Human acceptance of T1, then explicitly authorized T2 smoke/QLoRA work.

Dataset size is not a reliability claim. This checkpoint stops at T1.
