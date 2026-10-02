# Real registered Court case integration checkpoint

Branch: `codex/court-intelligence-real-case-integration`.
Base SHA: `0cea02e9b3c1d0fa7623ffb1f69e9681fa71279e`.
The commit containing this report is the functional checkpoint; no main merge
or office deployment is included.

## What was completed

The existing workspace already used actual CourtCase.Id; it was not replaced
or duplicated. The missing part was an authorized registered-case generation
path and stricter read/answer boundaries.

Actual flow:

`Court Directory → /court-cases/{actual GUID} → CourtCaseWorkspace → CourtIntelligence`

`authorized API → read-only CourtExternalOrderObservations index → loopback 8097 → existing official-PDF/local-model processor → atomic GUID-scoped structured artifact → passive GET → same-case brief/Ask`

- Added explicit one-case Process known orders / Refresh intelligence.
- Preserved exact normalized DB identity, real case/observation IDs, order and
  upload dates, official/corrigendum URL metadata and actual PDF SHA where known.
  The captured HTML evidence hash is not misrepresented as a PDF hash.
- Added passive no-order and known-but-unprocessed views, partial coverage and
  processing/interrupted/unavailable states. GET does not create an artifact.
- Kept official-origin/path restrictions, native-text-only extraction,
  temporary-PDF cleanup, source/page/identity/attribution safeguards and local
  model independence. No broad search, OCR or cloud inference was introduced.
- Full refresh is sequential and filesystem locked. Structured current.json
  is replaced atomically only after the bounded snapshot is ready. Failed or
  oversized refresh leaves the prior readable snapshot intact. Individual
  failed sources contribute no new facts; prior verified evidence is retained
  with explicit failed-refresh disclosure.
- Added strict registered-case index pruning, artifact GUID/number/shape/source
  checks and Q&A case/order/page/verbatim-evidence checks. Party submissions
  cannot be relabeled as Court findings by the answer response.
- Added abort + request-generation + active-case guards for GET/Ask/Refresh;
  slow A responses and A→B→A races cannot replace another case's view/answer.
- No demo dependency needed removal: demo routing was already separate.
  Production now has explicit regression proof and no demo/static fallback.
- Restart of the question runtime marks interrupted local refresh status only;
  it does not restart PDF downloads or inference.

## Changed files

API:

- `src/LAC.Api/CourtEndpoints.cs`
- `src/LAC.Api/CourtIntelligenceCaseData.cs` (new)
- `src/LAC.Api/CourtIntelligenceQuestions.cs`

Frontend:

- `src/LAC.Web/src/court/CourtIntelligence.tsx`
- `src/LAC.Web/tests/court-intelligence.test.mjs`
- `src/LAC.Web/tests/court-intelligence-integration.test.mjs` (new)

Backend tests:

- `tests/LAC.Tests/CourtIntelligenceIntegrationTests.cs` (new)

Local runtime/tests:

- `tools/court-order-intelligence/order_index.py`
- `tools/court-order-intelligence/serve_questions.py`
- `tools/court-order-intelligence/real_case.py` (new)
- `tools/court-order-intelligence/test_real_case.py` (new)
- `tools/court-order-intelligence/test_real_case_http.py` (new)

Documentation:

- `docs/REAL-CASE-INTEGRATION-AUDIT.md` (new, written before implementation)
- `docs/REAL-CASE-INTEGRATION-RESULT.md` (new)
- `COURT-INTELLIGENCE-UI-HANDOFF.md` (new)

No domain, schema, migration, infrastructure business logic, Award, DHC
matching/sync/assisted semantics, premium CSS, workspace routing, model weights
or training dataset/bundle changes.

## Acceptance verification

- Focused .NET CourtIntelligence tests: **9 passed**.
- Full .NET suite: initial **768 passed**; final rebuilt-source rerun **768 passed**
  in 3 minutes 32 seconds. No failures, skips or hang.
- Python Court runtime/safety tests: **126 passed**.
- Frontend tests: **95 passed**.
- Direct `npx vite build`: **passed**, existing large-chunk advisory only.
- `npm run build`: TypeScript stage fails in files unchanged from the base
  (App, AppShell, CourtTimeline, editor, land and matter files). No error was
  emitted for the changed CourtIntelligence component. An in-memory compiler
  comparison using the exact base component versus the current component
  returned the same **39 diagnostics**, zero new diagnostics. These unrelated
  baseline errors were not repaired in this focused integration.
- `git diff --check`: passed.

Commands:

```text
dotnet test tests/LAC.Tests/LAC.Tests.csproj -c Release --filter FullyQualifiedName~CourtIntelligence --logger "console;verbosity=minimal"
dotnet test tests/LAC.Tests/LAC.Tests.csproj -c Release --no-build --logger "console;verbosity=minimal" --blame-hang --blame-hang-timeout 5m
python -m unittest discover -s tools/court-order-intelligence -p 'test_*.py'
node --test tests/*.test.mjs
npx vite build
```

Proof includes actual-ID authorized API requests, exact DB metadata forwarding,
registered-case access denial before runtime/file access, passive GET, missing/
deleted/malformed/wrong-ID artifacts, incorrect source/page/attribution answers,
real loopback HTTP Refresh→artifact→grounded Ask with synthetic sources,
single-case concurrency, restart interruption, partial/no-fake facts, model
failure and oversized-artifact preservation, navigation races and no canonical
Court/Award/Matter/WorkItem writes. All fixture storage is temporary; no office
DB or live DHC site was exercised.

## Remaining environment-only steps

Run the API/frontend and the non-demo loopback question service from this
product branch against the same configured absolute ExtractionRoot. Use an
approved local model; no V3 adapter was installed/promoted. Keep acceptance
bootstrap/background workers/DHC automatic traffic disabled. See the handoff
for the existing guarded startup script and real registered matter route.

A separate officer-authorized real-source acceptance run and 1366×768 visual
polish remain environment/UI follow-up; this checkpoint claims fixture-backed
functional integration, not live-model quality improvement or office rollout.

## Active V3 separation

Training worktree remained on `codex/court-intelligence-pilot-v3`, SHA
`41622a683c9888a175216291b259713d38badf5d`, clean. No active job/bundle, frozen
data, runtime/model or V1/V2/V3 artifact was altered. The earlier passive job
status was Running; the later authenticated CLI status request was denied
`kernels.get`, so completion/output download could not be verified. No restart,
resubmission or credential inspection was attempted.
