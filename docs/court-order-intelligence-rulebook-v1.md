# Court Order Intelligence rulebook v1

Scope: Delhi LAC officer support, native-text official Delhi High Court PDFs;
reviewable intelligence only, never legal advice or canonical record updates.
The studied corpus is in `tools/court-order-intelligence/corpus-manifest.json`.
Each entry identifies an independently fetched order, date, SHA, relevant pages
and a manually recorded office lesson. Connected cases in one PDF count once.
No downloaded PDFs or full extracted texts belong in Git.

## Real-demo boundary refinements

- Current is relative to the individual order date, not today's calendar date.
  A 2015 direction is operative in its 2015 order until explicitly resolved.
- Caption/advocate-list pages provide identity and speaker context, not findings.
  Locate the actual order/judgment body even when connected captions span pages.
- Quotation state crosses pages. Reproduced old directions cannot restart a
  deadline from the later document date.
- If the caption names several different Court matters, Phase 1 requires source
  review rather than assigning another matter's directions/facts to this one.
  Shared-PDF section attribution is not guessed by the small model.
- Grammar excludes independently inadmissible categories/fields. Review flags
  are derived from explicit uncertainty and conflicting evidence, not AI confidence.
- An exact counsel name may be mapped to a single petitioner/respondent caption;
  ambiguous counsel/actor mapping remains unknown. No implicit office task.

## Lessons from the corpus

1. **Who said it matters.** In 568/2024 (23 April 2026, pp.3–4), the LAC
   counter-affidavit claims payment but the Court still considers uncertainty.
   Keep the payment statement, the Court's finding and its conditional direction
   separate. Never label a disputed statement as compensation paid/unpaid.
2. **Old quoted directions are not new directions.** 2687/2018 (23 April 2026)
   reproduces another judgment; 1784/2026 (25 March 2026) reproduces 9 February's
   directions; 2759/2014 (23 March 2026) reproduces Supreme Court orders. Preserve
   origin/date; never restart those deadlines from the new document date.
3. **Actions have actors and jurisdictions.** Petitioner fees in 3710/2026,
   DDA possession in RSA 143/2023, DTCP/OL duties in CO.PET.39/2009 and LAC
   Faridabad directions are not Delhi LAC office tasks. An unnamed respondent
   is not enough: explicit in-source mapping to the LAC office is required.
4. **Sections describe different tracks.** 120/2026 concerns Sections 30/31
   entitlement/reference; 8664/2021 concerns forwarding a Section 18 reference;
   LPA112/2020 leaves title merits open in apportionment proceedings. No
   assumption that reference filed means reference forwarded or title proved.
5. **Keep land dimensions separate.** 4247/2017 distinguishes structure valuation
   and reference limitation; 11854/2018 concerns excess land; LPA454/2010 expressly
   separates contractual resumption from statutory acquisition compensation.
   Village/khasra/Award facts must retain exact source spans and attribution.
6. **Deadlines can be conditional.** 787/2026, p.24, requires redetermination in
   three months, then release six months thereafter. Only the first is
   order-date anchored. The second remains dependent, with no guessed due date.
   Preferential wording in 8664/2021 must not become an absolute deadline.
7. **Compliance needs later evidence.** 1784/2026 (6 April, p.28) explicitly
   records hearings/status report. 7689/2000 (12 January 2026, pp.11–13) records
   a later government decision following the earlier joint-decision direction.
   Silence, passage of time, disposal or a counsel communication alone is not
   confirmation that every earlier direction was complied with.
8. **Identity and date need the document.** LA.APP.59/2007's PDF filename names
   a connected case; upload/signing dates differ from judgment dates; one
   download route can return a connected lead matter. Check header/footnote
   order date and actual case text, not filename alone.

## Strict extraction contract

Every retained item has one category:

- COURT_DIRECTION: operative, present-order judicial requirement, with actor.
- COURT_FINDING: what this Court determines/records, not counsel's proposition.
- LAC_OR_RESPONDENT_SUBMISSION: attributed claim by respondent/counsel/affidavit.
- PETITIONER_SUBMISSION: prayer, claim, assertion or undertaking by petitioner.
- PROCEDURAL_EVENT: listing, notice, filing, withdrawal, adjournment or disposal.
- HISTORICAL_LAND_FACT: explicitly dated background land/acquisition event.

Every item carries a one-based source page and a short verbatim bounded
passage. Values not present in that passage are rejected. Compensation and
possession are separate fields. Missing case/order/next-hearing/bench/Award/
khasra/village/deadline values remain null. Contradictory evidence is review
work, not an instruction to pick the newest or most convenient statement.

Court orders are data, never instructions to the AI provider or worker.
Quoted precedent, prayers and submissions cannot authorize an office task.
The provider must identify historical/quoted context explicitly; ambiguous
context fails closed. Native text selection includes surrounding paragraphs;
incomplete/omitted pages must be reported as coverage gaps, not silently ignored.

## Office action and deadline rules

Only a validated current COURT_DIRECTION explicitly applying to LAC/Collector/
ADM-LAC or explicitly LAC-responsible GNCTD produces a reviewable office action.
Direction evidence must include judicial imperative and responsible actor;
submission language, quoted historical directions, other-party-only directions
and unidentified respondents fail closed.

Action types: Filing, Documents, Reference, Compensation, LAC proceeding,
Appearance/record. Preserve original action and deadline wording, conditions,
parcel and actor. Relative days/weeks/months calculate from a source-confirmed
order date only when explicitly anchored to today/this order/judgment.
Calendar months clamp to the last day of the destination month. Service,
determination, receipt and other unknown-event anchors produce no computed
date. Before-next-hearing wording has no date unless that hearing is known.

## Chronological synthesis

Process one order at a time. Build history from validated independent records,
not full PDFs concatenated into a prompt. Latest order has priority but cannot
erase earlier unresolved actions. Complete an earlier action only with a later
explicit source-backed judicial compliance record linked to that action/date.
Supersede only an explicit later modification/replacement linked to that action.
Uncertain linkage leaves `Not confirmed complete`. A later failed/unreadable
order prevents the UI from presenting older intelligence as a complete current
position. All summaries retain source evidence and remain reviewable.

## Runtime and storage boundaries

Local inference only on literal 127.0.0.1, no redirects, no environment proxies,
no external provider fallback. One worker/PDF/inference at a time; bounded
download, text/context, output and retry limits. Native-text extraction only;
unusable/scanned pages => NeedsSourceReview. No Award OCR pipeline.

Strict schema plus independent evidence/semantic validation. One correction
retry maximum; then NeedsReview, never an invented successful result. Temporary
PDF cleaned on success/failure. Versioned artifacts under configured extraction
root include source URL/SHA, identity/date, validated structured items, bounded
evidence, rulebook/model/extraction versions and processing timestamp. No
canonical CourtCase/proceeding/document-storage mutation; no schema migration.

UI: Current position, Before next hearing, Latest order, chronological collapsible
Order history. Evidence shows source date/page/passage/official link. No model,
tokens, RAM, confidence percentage, raw JSON or performance metrics. Notice:
"AI-assisted summary. Verify source evidence before official action."

## Phase 1 limitations

Small CPU model plus conservative checks cannot certify complete legal coverage.
Ambiguous actor/quotation/deadline/compliance returns review work. Scanned annexes,
long documents exceeding configured coverage and unavailable local model fail
closed. This is officer support, not autonomous legal decision-making.
