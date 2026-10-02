# Court Intelligence: functional UI handoff

Branch: `codex/court-intelligence-real-case-integration`.
Product base: `0cea02e9b3c1d0fa7623ffb1f69e9681fa71279e`.
Acceptance viewport: **1366 × 768**. Visual redesign is deliberately deferred.

## Actual registered matter route

Open `/court-cases`, choose a registered matter, then
`/court-cases/{actual CourtCase.Id}`. `CourtCaseWorkspace.tsx` renders
`<CourtIntelligence key={courtCase.id} caseId={courtCase.id} />` in Overview.
Do not link production navigation to the development demo HTML or fake GUIDs.

## Files and responsibilities

- `src/LAC.Web/src/court/CourtCaseWorkspace.tsx`: existing real workspace and ID binding (unchanged).
- `src/LAC.Web/src/court/CourtIntelligence.tsx`: brief, explicit processing/refresh,
  grounded questions, navigation isolation, evidence and coverage states.
- `src/LAC.Web/src/court/court-intelligence.css`: existing presentation (unchanged).
- `src/LAC.Api/CourtEndpoints.cs`: authorized GET, Ask and explicit Refresh routes.
- `src/LAC.Api/CourtIntelligenceCaseData.cs`: read-only DB index, artifact identity,
  source/page validation and passive known-but-unprocessed view.
- `src/LAC.Api/CourtIntelligenceQuestions.cs`: fixed loopback requests and answer citation checks.
- `tools/court-order-intelligence/serve_questions.py`: local runtime HTTP boundary.
- `tools/court-order-intelligence/real_case.py`: explicit one-case sequential refresh,
  atomic structured snapshot and case-scoped runtime status.
- `tools/court-order-intelligence/order_index.py`: exact known-source merge and
  bounded explicit-date lazy processing; not a search/discovery service.
- Existing `worker.py`, `semantics.py`, `questions.py`, `chronology.py`: extraction,
  attribution/scope/lifecycle safeguards, local Q&A and synthesis (unchanged).

## Real data, not demo data

The registered case ID/number and official order index come from the existing DB.
`CourtExternalOrderObservations` supplies identity, dates, official/corrigendum
URLs, upload date and observation ID. Cause-list appearances are not orders.
The observation's `EvidenceSha256` hashes captured HTML, so it is **not** forwarded
as a PDF hash. The processor retains the actual downloaded PDF SHA in structured
evidence. PDFs are temporary; structured JSON is durable under
`Storage:ExtractionRoot/court-intelligence/v1/{actual GUID}/current.json`.

No sample artifact is copied under a real ID. GET only reads the DB/artifact;
it never runs a model, downloads a PDF or starts work. Model failure does not
prevent a previously verified structured brief from being read.

## Information hierarchy to preserve

1. Case identity / source-confirmed hearing: distinguish office register dates
   from source-listed dates; an expired date is not the next hearing.
2. Needs your attention: only source-supported LAC obligations. Without a
   confirmed next hearing use **Outstanding LAC action**.
3. Current position: attributed Court findings/directions, party submissions,
   historical references and procedural events remain distinct voices.
4. Latest/final meaningful order: newest available source, substantive prior
   developments and any continuity gaps. A source outcome does not dispose the
   canonical office matter automatically.
5. Ask Intelligence: current matter only; questions require explicit submission.
6. Order history: every indexed date remains visible, including unavailable and
   unprocessed orders, with separate evidence/coverage gaps.
7. Evidence on demand: exact order date, page(s), verbatim passage and strictly
   allowlisted official source link. Officer summaries may omit paragraph
   numbering; expandable evidence must retain the original text/qualifiers.

## States that must remain visible

- No official indexed order: no inferred current position or obligation.
- Known order, not processed: **Intelligence not processed yet** and explicit
  **Process known orders**. Do not substitute a demo brief.
- Ready: verified brief, grounded Ask and explicit **Refresh intelligence**.
- Running: same-case checked count; old verified evidence remains readable.
  Passive polling is GET only, never automatic Refresh/Ask.
- Partial/unusable source: verified material only, clear source coverage gap.
  Scanned/native-text-deficient PDFs fail closed, without OCR or guessed facts.
- Failed/interrupted refresh: prior verified evidence retained and disclosed;
  retry is explicit. Restart never resumes PDF/model work automatically.
- Malformed/wrong-case/unavailable artifact: calm unavailable state; the rest
  of Court Matter remains usable.
- Q&A model unavailable: question answering unavailable, brief still available.
- Insufficient answer: **I could not confirm this from the orders processed for
  this matter.** A requested hearing date is not proof that an order exists.

## Safety language/actions

Keep the meaning of these warnings even if their presentation changes:

- Actions/summaries shown use only verified evidence.
- Party submissions are not established Court facts.
- No direct LAC action identified does not establish all duties are complete.
- Supplied sources do not claim to contain every Court order.
- Source judicial outcome does not change the office register automatically.
- A preferred period is not an unconditional statutory deadline.

Do not invent automatic case status/NDOH/compliance changes, a close-action
button, new proceedings, assignment/reassignment, Award/Matter/WorkItem writes,
cross-case chat, broad web search, cloud inference, CAPTCHA automation or DHC
sync/order-search controls within this intelligence component. Canonical
promotion remains a separate future explicit officer workflow.

## Environment-only acceptance steps (not performed by this checkpoint)

Run the product API/frontend from this branch, preserving the existing DB and
absolute storage configuration. For a safe local acceptance API keep database
bootstrap/background workers/DHC auto-sync disabled. No schema change is needed.

Run `scripts/start-court-intelligence.ps1 -Mode Questions` with the existing
absolute ExtractionRoot, Python environment, separate runtime-log directory and
approved local ModelVersion. **Do not use `-Demo`.** The script guards occupied
port 8097. Local model service remains on loopback 8096; do not switch to the
actively training V3 adapter. Starting the question runtime makes no DHC request.

After authorization, use a real registered case with exact known official order
metadata. An officer may explicitly process its sources and ask a question.
This checkpoint's acceptance proof uses synthetic fixtures and local test HTTP,
not live DHC or the office database. Visual acceptance should cover available,
unprocessed, unavailable and partial states at the target viewport. Antigravity
has freedom over presentation, not the above data/safety boundaries.
