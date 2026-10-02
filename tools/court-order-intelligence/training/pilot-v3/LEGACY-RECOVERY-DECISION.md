# V3 legacy recovery: source review and required decision

Reviewed 2026-10-03 against the existing native source pages and the frozen
independent TRAIN annotations. Starting commit:
`8030dbac601b59da876a2563cbbe23f082d4f9ef`.

**NO-GO. No Kaggle upload, GPU job or live Court request was made.**
This is an actual contract/source conflict requiring a decision, not a claim
that another generic framework or more random PDF collection is needed.

## Outcome of all 33 queued examples

- One example recovered with exact adjacent native context, unchanged target,
  unchanged selected wording, unchanged role/field/scope and unchanged gate.
- One current-position selection has a genuine current-state support gap and
  remains quarantined; a historical requirement is not proven still active.
- Thirty-one source-supported candidates remain quarantined because the
  current contract/parser rejects valid source wording or lacks the required
  source-context mechanism. They are NOT declared incorrect gold.

The requested A/B distinction does not cover this third condition. Calling a
lexical false rejection a true source failure would be dishonest; silently
changing the gate would violate the instruction not to relax it. The proposal
is a narrowly scoped **V3-only generic precision correction**, preserving all
source/page, actor, attribution, scope, negation and lifecycle safety checks.
No correction has been implemented without that decision.

Legend: B = recovered source context; A = insufficient current-state support;
C = source-supported but blocked by the current parser/semantic contract.
The legacy target is unchanged in every row. C does not promise that one
single regex edit will recover the entire example: all selected claims must
still satisfy the same reviewed semantics.

| Example | Decision | Source-grounded finding / remaining boundary |
|---|---|---|
| wpc7003-2026-important | C | 20 May 2026 p6: Court denies interim relief; `prayer` is mistaken for party speech. NHAI compensation assertion remains separately attributed. |
| wpc7003-2026-digest | C | Same Court disposition and separately attributed NHAI assertion; no allegation is promoted to a finding. |
| wpc7003-2026-date-en | C | Same dated order; the selected denial of interim relief hits the `prayer` false rejection. |
| wpc10308-2024-important | C | 13 Aug 2026 p5: operative final-reconciliation requirement refers to the earlier `counter affidavit`; the noun triggers party-speech rejection. |
| wpc10308-2024-compliance | C | 13 Aug 2026 p3: Court records GNCTD `has now filed` the affidavit. PERFORMANCE does not accept the intervening `now`. No restored-land completion is asserted. |
| wpc10308-2024-digest | C | Actual judicial reconciliation direction is blocked by its counter-affidavit reference; GNCTD's excess-land assertion retains party attribution. |
| wpc10308-2024-date-en | C | Same dated judicial reconciliation direction; not a claim that reconciliation has finished. |
| wpc10308-2024-feb2026-important | C | 6 Feb 2026 p1: Court says it has perused the record and pleadings; `counter affidavits` alone triggers rejection of the Court observation. |
| wpc10308-2024-feb2026-digest | C | Same explicit Court observation; the later GNCTD counsel statement remains a statement, not a finding. |
| wpc10308-2024-feb2026-date-en | C | Same dated observation, short-affidavit requirement and actual listing. |
| 10308-position | C | Latest Aug reconciliation direction is blocked by the noun heuristic; any restored eligibility must also bind its fresh direction lifecycle to the supplied Aug order, not infer completion from disposal. |
| cont418-2025-attribute-0 | C | 13 Jul 2026 p3 paragraph 7 expressly starts the Court's referral to Chief Secretary. The old bounded parser reconstructs pages from selected anchors only, omits paragraph 7, and treats paragraph 8 as continuing DDA speech. Changing precedingContext alone cannot fix that parser. |
| wpc6108-2015-nov2025-action | C | 11 Nov 2025 p39 expressly says LAC counsel `shall submit` Master List. `submit` is wrongly treated as speech; `filing` field and dependent actor in the second sentence also need truthful context support. |
| wpc6108-2015-nov2025-digest | C | Same fresh judicial Master List requirement; separate petitioner/DDA undertaking is not completed LAC performance. |
| wpc6108-2015-nov2025-date | C | Same dated requirement and listing; `shall submit` is not counsel claiming it has been submitted. |
| wpc6108-2015-feb25-action | C | 25 Feb 2026 p37: separate filled-template duty names LAC expressly. Original field is `filing`; office-action gate admits only `direction`. |
| wpc6108-2015-feb25-compliance | C | 25 Feb 2026 p37: Court says both counsel `have placed before the Court` blank templates. PERFORMANCE excludes `placed`. This is not populated LAC-template completion. |
| wpc13932-2025-feb03-compliance | B | 3 Feb 2026 p2 paragraphs 5-7: exact contiguous native service-affidavit/publication-service context supports the Court's original `stood served` conclusion. Recovered without changing selected text or gate. |
| wpc4806-2014-feb2020-action | C | 4 Feb 2020 p1: `Let the same be filed` depends on the preceding replacement-affidavit statement and named LAC respondent caption. Exact actor is absent from the bounded selected sentence; field remains `filing`. No generic respondent-to-LAC inference is permitted. |
| wpc4806-2014-apr2022-action | C | 11 Apr 2022 p1: last opportunity expressly granted to respondent/LAC to file affidavit. Field `filing` and passive grant wording are not admitted by the action predicate. |
| wpc4806-2014-action | C | 7 Feb 2026 p2: office of LAC expressly ordered to file additional affidavit; original `filing` field rejected. Conditional costs remain conditional, not an unconditional immediate payment. |
| wpc6108-2015-action | C | 20 Mar 2026 p1: LAC named expressly in renewed filled-template requirement. Field `filing` and passive `be completed` wording rejected. R3 filing does not prove LAC template completion. |
| wpc4255-2016-action | C | 6 Apr 2026 p1: same expressly named renewed template requirement. Same source family as 6108, not independent breadth. |
| wpc9093-2022-feb09-digest | C | 9 Feb 2026 p2: CGSC `shall seek instructions and file` on behalf of DPIIT. `seek` is mistaken for party speech. This is not a LAC duty. |
| wpc9093-2022-feb09-date | C | Same DPIIT requirement in dated digest; SDM report and petitioner prayer remain independently attributed. |
| wpc14785-2023-digest | C | 1 Sep 2026 p1: Court gives petitioner a final opportunity to bring rejoinder on record and `argue` next date. `argue` is mistaken for party speech; no LAC action is invented. |
| wpc14785-2023-date | C | Same Court opportunity; conditional non-prosecution consequence is not a dismissal already made. |
| wpc4644-2024-digest | C | 23 Feb 2026 p1: actual Court condonation/bring-on-record decision mentions `counter affidavit`, triggering rejection. Counsel's filing claim remains a separate submission. |
| wpc4644-2024-date | C | Same Court decision and source listing; no unqualified performance is invented from counsel's statement. |
| 4806-active-action | C | Same 7 Feb 2026 fresh `filing` requirement and exact conditional consequence. Earlier opportunities are not revived. |
| 6108-position | A | Three-order snapshot selects Nov Master List direction as part of current position. Later blank/populated-template progress does not establish that separate Master List duty remains active. UNKNOWN cannot become OPEN from silence. Keep quarantined even if lexical issues are corrected. |
| 6108-active-action | C | Same 20 Mar 2026 renewed populated-template `filing` duty; fresh current direction is supported, completion is not. |
| 6108-compliance | C | Same Court-confirmed Feb blank-template presentation as the single-order positive. No proof that LAC's separate populated-template duty was completed. |

## Recalculated pool and real coverage gap

245 original TRAIN rows -> 219 after unchanged semantic dedupe -> **187**
compatible candidates, 21 conservative groups / 23 matter identities.
V1 reused 76; V2 reused 111. Quarantine 32. NEW V3 gold 0.

| Task | Passing candidates | Groups | Launch requirement |
|---|---:|---:|---|
| Attribution | 11 | 9 | >=30 / >=10 groups |
| All multi-order task rows | 11 | 3 | Not equivalent to full current-position coverage |
| Broad full-position candidates | 3 | 3 | >=25 strong / >=8 groups; not final-certified/frozen |
| Positive LAC action | 2 | 1 | >=25 with real breadth |
| Positive compliance | 1 | 1 | Genuine positive breadth, not repeated single gold |

The three broad full-position candidates are 9093-position, 4806-position and
13932-position. They still need the final full-chain completeness check before
being counted as certified strong gold (including outstanding memo/action
coverage in 13932). The other eight task rows are focused lifecycle/dispute/
latest-development/date subtasks, not extra full-position examples.
Listing-only 6108-latest-listing and 9093-meeting-vs-hearing do not count toward
the strong-synthesis target. No paraphrases or common judgments create groups.

## Raw positive/empty counts and provisional sampler exposure

| Task | Candidate positive / empty | CPU probe positive / empty |
|---|---:|---:|
| Office action | 2 / 27 | 54 / 18 |
| Date QA | 28 / 34 | 18 / 6 |
| Compliance | 1 / 32 | 18 / 19 |
| Multi-order task (all, not certified strong) | 11 / 0 | 73 / 0 |

Seed 20261003, 376 logical exposures, 94 proposed Trainer steps at accumulation
4. **Diagnostic probe only, NOT final training budget.** It reaches 119/187
distinct records; repeating the tiny positive pools does not meet the launch
gate. No actual Kaggle curriculum integration is claimed.

## Context and downstream work not falsely declared complete

Offline pinned-tokenizer recheck: 187 rows present, 174 fit training cap 2048,
13 remain over cap, max training 2663 / inference prompt 2685 tokens.
No evidence truncation, example dropping or weights loaded. Larger T4
forward/backward preflight remains authorized but has NOT been run.

Fresh holdout replacement/freeze, rich-chain acquisition to fill actual gaps,
actual Kaggle integration proof, final immutable freeze and launch remain
uncompleted. Do not collect extra random sources while unresolved contract
rejections distort the gap. Do not launch a training job under these counts.

## Decision needed to unblock

Allow a generic **V3-only parser/gate precision bugfix** that:

- distinguishes judicial `shall submit`/`shall seek`/permission to argue from
  a counsel submission, without admitting genuine party assertions;
- admits source-confirmed filing requirements without changing their original
  `filing` field to `direction` just to fit the gate;
- recognizes recorded `has now filed` and `have placed before the Court`
  performance, preserving actor/task linkage, scope and negation safeguards;
- preserves bounded native judicial-reset context for anchors rather than
  creating misleading adjacency by concatenating selected passages;
- keeps unknown lifecycle unknown and the 6108 current-position gap quarantined;
- applies the same corrected V3 contract to stock/V1/V2/V3 comparisons,
  leaving frozen legacy/regression artifacts and office production untouched.

Do not substitute case IDs, expected answers or training targets into a guard.
Tests must prove both source-confirmed positives and their unsafe lookalikes.

## Verification

275 offline software tests pass: runtime 116, training 125 (16 composite
tests), Kaggle contracts 34. The composite audit reverified all 18 frozen
V1/V2/review files. Existing semantic gate, runtime parser, model/revision,
protected splits and targets unchanged. No application/backend/domain/schema/
migration/Award changes, no main merge, no credentials/private workbook/PDF
upload, no external Court traffic. PDF skill used for read-only source review.
