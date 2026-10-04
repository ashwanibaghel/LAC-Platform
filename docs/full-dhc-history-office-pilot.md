# Full DHC history office pilot

Checkpoint: `0e1f17146d68bc3ad7b877c9ef57a8ff8038a571`, pushed to `codex/court-intelligence-v1-office-pilot`. This preserved all accepted uncommitted V3 and Q1–Q8 work from `900eef1`. Work continues in the accepted `court-intelligence-v1-office-pilot` checkout. No main merge or IIS deployment.

## Official workflow

The registered case page posts to `/api/court-cases/{id}/dhc-history`. The existing assisted coordinator creates exactly one Selected item, after checking Court view/edit authorization and the registered DHC identity. Full-history scope is independent of Excel provenance, operational NDOH and office Pending/Disposed status.

The officer answers the official case-status CAPTCHA. The exact status result supplies both the status/listing observation and its **Click here for Orders** link. The opaque link is retained as official source provenance, never constructed from a case number. Only HTTPS on `delhihighcourt.nic.in`, the audited `case-type-status-details` route and its three opaque path components are admitted.

This Orders route is distinct from the site's `case-number` judgment-search form. Real Kamal Singh acceptance exposed that distinction: judgment search returned a blank table while the status-result Orders link returned three orders. Full history follows the latter. The existing global assisted workflow retains its separate behavior.

The Orders endpoint uses server-side DataTables pagination. The session requests pages of 50 with the site's AJAX header, verifies draw, unfiltered total, each expected page length and each global row index, and accumulates all rows before publishing observations. Totals above the bounded 1,000-source limit, changing totals, repeated/missing pages, unsafe links and changed columns fail closed. It never treats a first page or an unconfirmed blank judgment form as complete history.

Every exact row retains case identity, order date, PDF/corrigendum URLs, optional upload metadata when supplied by the source, observation identity, evidence text/hash and observation time. A separate official Hindi publication becomes its own dated PDF observation and enters the same visible source index and guarded processing path; it does not change the table's pagination row count. Stable row identities deduplicate across runs; new rows append without dropping older observations. An exact HTML row may link to a connected-case PDF: the link remains visible, but PDF attribution must pass the unchanged extraction guard before any facts can be used.

Full history does not update canonical CourtCase status/NDOH, proceedings or calendar. Official DHC and office-register values are displayed separately. A failed Orders GET can resume from the same run's accepted, exact status observation within one hour: its evidence hash and approved opaque URL are checked again, along with current case authorization. Missing, stale, ambiguous or altered provenance requires fresh human status verification. No CAPTCHA answer is persisted or replayed.

## Automatic local processing

`DhcHistoryIntelligenceBridge` consumes only completed, human-started full-history order runs. It rechecks current-case access, loads the durable order index and queues the existing loopback Python service. Atomic run receipts prevent repeated handoffs; interrupted Python work resumes from persisted order artifacts without another official search.

The worker uses one case lock, one temporary PDF and one inference request at a time. Each order is independently bounded, validated and persisted before the next. Exact source identity/hash, pinned model version and extraction/semantic versions determine reuse. Failed/review sources remain explicit coverage gaps; partial failed chunks contribute no verified facts. Temporary PDFs are deleted after processing. Combined synthesis is rebuilt after each order and preserves earlier facts and historical/quoted scope.

Retrying a failed attempt with no usable facts validates a newly downloaded PDF from scratch and records the previous attempt's byte hash. This accommodates DHC's regenerated PDF wrappers without asserting byte equivalence. Any prior usable facts retain the source-byte change guard. A completed extraction can still require review; the UI counts completed AI extraction separately from review needs and pending work.

The evaluated Pilot V3 adapter, Qwen3-4B-Instruct-2507 pinned Q4_K_M base, context 3072, threads 6, batch 2048, ubatch 128, parallel 1, f16 KV, cache-ram 0, seed and temperature remain unchanged. Inference remains loopback only. No retraining, weight changes, OCR or cloud model calls.

Whole-case artifacts are bounded at 8 MiB; individual order artifacts at 2 MiB. These bounds accommodate incremental histories while keeping independent case, source, page, evidence, schema and attribution checks. Court Q&A does not redownload PDFs on ordinary requests.

## Officer workspace and questions

The 1366×768 workspace prioritizes official/office status, latest order, source/processing/review counts, current position, mandatory actions, conditional directions, latest digest, chat and all discovered PDF links. Pending processing and source review have separate labels. Complete history expands each order into attributed facts and exact evidence controls.

Court questions retrieve only the current case's usable facts across roles, topics, dates, year ranges and requested last-order counts. All-history replies can compose complete exact persisted evidence without exceeding the pinned model context. Oversized replies explicitly request a narrower range rather than claiming a truncated timeline is complete. Conditional directions retain their conditions, including conditional reference adjudication deadlines.

The model selects evidence IDs. The runtime supplies exact text and attribution and the authenticated API independently validates every citation against the registered source, date, page and passage. Selection cannot omit verified conditional directions from an explicit direction question; this also supports accepted legacy artifacts whose conditional propositions were classified as compensation/procedural facts. No artifact mutation or new factual inference is involved.

The router sends case-related and ambiguous follow-up questions through evidence checks. Ordinary conversation uses the same local model with only bounded authenticated display name/designation and filtered general-chat history. Name questions use exact authenticated context. General answers that introduce unsupported Court claims are withheld. Case changes abort stale requests and clear artifact/chat state; history is bounded and case-scoped.

General chat also guards against adopting the authenticated user's identity and repeated-phrase degeneration. A repetitive reply gets one bounded correction request to the same pinned local model; repetitive history is excluded. An unsuccessful correction produces an honest request to rephrase rather than displaying a loop of generated text.

## Completed real acceptance — 4 October 2026

Acceptance used the actual authenticated registered-case screens and human-read official CAPTCHA answers. Unit tests supplement these observations.

| Check | Observed result |
|---|---|
| Prem accepted artifact preserved | SHA-256 `CF4C893725F00B4C57D6958C4F0BBF2CFEFE7503F782EFED4B7A69F9E808BDDC`, unchanged |
| Prem history/PDF | Existing 25 September 2026 source visible, NeedsReview disclosed |
| Prem Q1/Q2/Q3/Q5/Q7/Q8 | Actual UI evidence verified; Q8 truthfully has no supported next hearing |
| Prem Q6 | Actual UI shows all three conditional propositions with correct page/source and non-mandatory labels |
| Prem Q4 | Actual UI shows three conditional propositions plus petition/application disposition, all with exact source evidence |
| Authenticated name | Actual UI: “Aapka naam Ashwani Baghel hai.”, no Court citations |
| Kamal official status | PENDING; official listing 5 October 2026; office Pending/NDOH unchanged, revision 2 |
| Kamal discovery | Correct official Orders route returned and persisted all 3 rows: 6 April, 18 May, 28 September 2026 |
| Kamal PDF links | All 3 visible on actual case screen |
| Kamal processing | Automatic handoff verified; all 3 sources checked. Two connected-case PDFs explicitly NeedsSourceReview; latest extraction completed with 18 facts, 11 usable after evidence/scope checks, coverage review disclosed |
| A→B→A isolation | Actual case navigation cleared chat and restored the unchanged Prem artifact |
| Normal conversational model reply | Actual greeting and Hinglish proverb-meaning recheck passed; exact authenticated name passed. Initial repetitive meaning reply exposed and prompted the bounded correction guard |
| Kamal grounded questions | Actual whole-case, latest directions, last-three history and next-hearing questions passed exact citation checks; all displayed PDF citations use Kamal's 28 September source. Reviewed April/May sources contribute no invented facts |
| Additional case | Dayanand W.P.(C) 940/2015: same-run verified Orders GET resume completed without CAPTCHA replay; all 19 unique official PDFs visible; PENDING/listing 29 January 2027 visible. All 19 sources checked, 15 AI extractions complete, 5 require review (one overlaps the processed count). Latest order, current position and dated last-three-order Q&A verified in the actual UI |

Final verification: full Python **230/230**, frontend **109/109**, full .NET **793/793**, all passed with zero skips. The final .NET binary includes Hindi publication handling; the focused DHC suite also passed 64/64. Vite build passed; TypeScript baseline/current each contain **39 existing diagnostics**, with **zero normalized diagnostic delta**. This is not a clean TypeScript build. `git diff --check` passed. Existing bundle-size and three xUnit2031 warnings remain.

**MONDAY DEMO READY: YES for the verified local office pilot, with the source-review limitations below visible in the product.** This is not a declaration that every historical proposition has been extracted or every official Court order is available. IIS deployment has not been performed. The final implementation SHA is reported after commit/push rather than embedding a self-referential commit hash here.

The final A→B→A check used full URL navigation, verified an empty Kamal chat, returned to an empty Prem chat and restored Prem's same status/source/artifact. Frontend integration tests separately exercise case changes on the mounted component and stale-response cancellation. The preserved Prem artifact hash was checked again after Dayanand processing. No temporary PDFs remained after the completed job.

## Required implementation report

1. **Checkpoint SHA:** `0e1f17146d68bc3ad7b877c9ef57a8ff8038a571`, pushed before product changes.
2. **Final SHA:** supplied in the final chat and the external acceptance report after commit/push.
3. **Exact changed files:** the 30 paths listed below, relative to the accepted checkout.
4. **Architecture:** authenticated selected-case sync → human CAPTCHA → exact status-result Orders link → checked paginated durable discovery → local sequential resumable V3 worker → guarded combined evidence/brief → case-scoped multilingual Q&A and bounded general chat. The preceding sections describe the failure and identity checks.
5. **Official orders:** Prem 1 preserved supplied source (not a new full-history discovery); Kamal 3; Dayanand 19 unique sources spanning 2 February 2015–28 September 2026.
6. **V3 processing:** Prem 1 existing accepted extraction retained without reprocessing; Kamal 3 checked / 1 completed extraction / 3 review; Dayanand 19 checked / 15 completed extractions (14 Validated + 1 NeedsReview) / 5 review. Processing and review counts overlap. Sources withheld at attribution/source-version checks contribute no facts.
7. **Every discovered official PDF visible:** YES, actual history rows verified: Prem 1, Kamal 3, Dayanand 19.
8. **Latest DHC status visible:** YES; Prem DISPOSED, Kamal and Dayanand PENDING. Office-register dates/status remain separately labelled.
9. **Latest order visible:** YES; Prem 25 September 2026; Kamal and Dayanand 28 September 2026.
10. **Complete discovered history visible:** YES. Completeness beyond the official sources returned is explicitly unestablished; Prem's accepted single-source coverage remains disclosed.
11. **Current-position synthesis visible:** YES, from each case's available usable evidence; gaps and incomplete office-action checks remain visible.
12. **Grounded whole-case Q&A:** PASS within usable evidence. Actual Prem Q1–Q8, Kamal whole-case/latest direction/last-three history/next-hearing, and Dayanand last-three history checked. Reviewed sources do not supply invented facts. Kamal citations use only its September PDF; Dayanand's answer uses its May/July/September 2026 PDFs.
13. **Normal local chat:** PASS, actual greeting, authenticated name and Hinglish meaning. Uses the unchanged local V3 model; persona/repetition guards passed actual rechecks.
14. **A/B isolation:** PASS, actual A→B→A navigation plus mounted-component/stale-request frontend tests.
15. **Tests/builds:** Python 230, frontend 109, .NET 793 passed; Vite passed; TypeScript 39 baseline/39 current, zero added diagnostic delta; diff whitespace check passed.
16. **Warnings/limitations:** Kamal April/May connected-case PDFs require attribution review; September extraction retains coverage review. Dayanand 2015-10-05 source bytes changed, and three connected-case PDFs (2025-04-01, 2025-08-07, 2025-11-18) require source review; 2026-07-28 extraction retains review. Prem's accepted coverage review remains. No OCR was introduced. Initial CPU processing took about 11 minutes for Kamal and 44 minutes for Dayanand during concurrent validation; incremental progress and persisted results remained usable. Existing TypeScript diagnostics and build/test warnings remain. No unresolved implementation/acceptance blocker was observed for this local pilot.
17. **MONDAY DEMO READY:** YES, scoped to the verified local pilot with these review gaps disclosed; no main merge or IIS deployment.

Exact changed files:

```text
docs/full-dhc-history-office-pilot.md
src/LAC.Api/CourtEndpoints.cs
src/LAC.Api/CourtIntelligenceArtifactReader.cs
src/LAC.Api/CourtIntelligenceCaseData.cs
src/LAC.Api/CourtIntelligenceQuestions.cs
src/LAC.Api/DhcAssistedEndpoints.cs
src/LAC.Api/DhcHistoryIntelligenceBridge.cs
src/LAC.Api/Program.cs
src/LAC.Infrastructure/CourtImportService.cs
src/LAC.Infrastructure/DelhiHighCourtAssistedCoordinator.cs
src/LAC.Infrastructure/DelhiHighCourtAssistedForms.cs
src/LAC.Infrastructure/DelhiHighCourtAssistedService.cs
src/LAC.Infrastructure/DelhiHighCourtAssistedSession.cs
src/LAC.Web/src/court/CourtIntelligence.tsx
src/LAC.Web/src/court/court-intelligence.css
src/LAC.Web/tests/court-intelligence-integration.test.mjs
src/LAC.Web/tests/court-intelligence.test.mjs
tests/LAC.Tests/CourtIntelligenceIntegrationTests.cs
tests/LAC.Tests/CourtIntelligenceQuestionMetadataTests.cs
tests/LAC.Tests/DelhiHighCourtAssistedTests.cs
tools/court-order-intelligence/chat_router.py
tools/court-order-intelligence/order_index.py
tools/court-order-intelligence/provider.py
tools/court-order-intelligence/query_intents.py
tools/court-order-intelligence/questions.py
tools/court-order-intelligence/real_case.py
tools/court-order-intelligence/semantics.py
tools/court-order-intelligence/serve_questions.py
tools/court-order-intelligence/test_full_history.py
tools/court-order-intelligence/test_real_case_http.py
```

Local acceptance evidence is retained outside Git at `E:\LAC-DHC-History-Acceptance-20261004`: public official-list provenance, exact Dayanand row/link JSON, actual UI question results and test logs. Final viewport screenshots are in the current task's visualization directory. No CAPTCHA answers or credentials are committed.

Kamal's official PDF links:

- [28 September 2026](https://delhihighcourt.nic.in/app/showlogo/1790764485_12b79b03260e9149_prj_44562026.pdf/2026)
- [18 May 2026](https://delhihighcourt.nic.in/app/showlogo/1779181375_3b327a940a2c82bc_svn_43762026.pdf/2026)
- [6 April 2026](https://delhihighcourt.nic.in/app/showlogo/1775486596_9fdb9f184646a185_svn_43762026.pdf/2026)

Prem's existing official PDF: [25 September 2026](https://delhihighcourt.nic.in/app/showFileJudgment/PMS25092026CW24932023_130944.pdf).
