# Court structured LAC validation

Validated locally on 8 October 2026, on the implementation branch from accepted base `8a02466df4ef96ba585cb2cd1e700f0a55f11a50`.

| Check | Result |
| --- | --- |
| Full backend regression | 1,020 passed, 3 skipped, 0 failed; 1,023 total |
| Final Court backend selection | 71 passed, 0 skipped, 0 failed |
| Actual isolated PostgreSQL integrations | 23 passed: 4 new Court integrations and 19 existing Dak regressions |
| Python Court suite | 315 passed, 0 failed |
| Frontend suite | 188 passed, 0 skipped, 0 failed |
| Browser visual checks | 5 passed at 1366 × 768; 0 page errors; 0 render-time mutations |
| Production frontend build | TypeScript and Vite passed |
| EF pending model check | No changes have been made to the model since the last migration |
| Git whitespace check | Passed |

The full backend run preceded the last confirmed-link read projections, legacy office-action answer guard and malformed-contract exception normalization. The final 71-test Court selection was rebuilt after those changes. These runs overlap and must not be added together as unique tests. The full run's three skips are the existing RBAC PostgreSQL tests, which require their separate connection setting. Existing optional Court/Matter/Core PostgreSQL smoke tests that return without their own opt-in settings are not counted as actual integrations here. A SQL-only PostgreSQL translation test is also excluded from the integration count.

PostgreSQL tests used a separate disposable PostgreSQL 17 cluster on localhost port 55439, with each test creating and dropping an isolated database. This did not use the office database. The four new Court integrations exercise the complete migration chain, stable order uniqueness/JSONB/reload, canonical shared projections and audit, review concurrency rollback, and database confirmation constraints.

Commands:

```powershell
$env:LAC_TEST_POSTGRES='Host=127.0.0.1;Port=55439;Database=postgres;Username=court_scope_tests;Pooling=false'
dotnet test tests/LAC.Tests --logger 'trx;LogFileName=backend-final.trx' --results-directory C:\Users\ashwa\.codex\court-scope-validation
dotnet test tests/LAC.Tests --artifacts-path C:\Users\ashwa\.codex\court-scope-final-build --filter 'FullyQualifiedName~CourtScopePersistenceTests|FullyQualifiedName~CourtStructuredReviewTests|FullyQualifiedName~CourtIntelligenceIntegrationTests|FullyQualifiedName~CourtOperationalDirectoryTests|FullyQualifiedName~CourtQuestionConversationTests|FullyQualifiedName~CourtIntelligenceQuestionMetadataTests' --logger 'trx;LogFileName=court-feature-final.trx' --results-directory C:\Users\ashwa\.codex\court-scope-validation
$env:PYTHONPATH='C:\Users\ashwa\.codex\court-scope-python-deps'
# In tools/court-order-intelligence:
python -m unittest discover
# In src/LAC.Web:
npm test
npm run build
node tests/court-structured-visual.mjs
# At repository root:
dotnet ef migrations has-pending-model-changes --project src/LAC.Infrastructure --startup-project src/LAC.Api
git diff --cached --check
```

The Python run used the existing pilot test dependency `gguf` in a task-local dependency folder. The frontend build reports the existing large-chunk advisory; it completes successfully. Backend compilation reports existing xUnit analyzer advisories in Award extraction tests.

Local evidence files, retained outside the repository:

- `C:\Users\ashwa\.codex\court-scope-validation\backend-final.trx`
- `C:\Users\ashwa\.codex\court-scope-validation\court-feature-final.trx`
- `C:\Users\ashwa\.codex\court-backend-final.log`
- `C:\Users\ashwa\.codex\court-feature-final.log`
- `C:\Users\ashwa\.codex\court-python-final.log`
- `C:\Users\ashwa\.codex\court-frontend-final.log`
- `C:\Users\ashwa\.codex\court-npm-build.log`
- `C:\Users\ashwa\.codex\court-ef-model-check.log`

Screenshots and `visual-checks.json` are in this receipt folder. The screenshots use synthetic data, including a fabricated jurisdiction, to demonstrate configurable office resolution without live office information.

Scope preservation: no changes to existing authorization/RBAC services, Dak services, canonical land parsers, DHC retrieval/history services, ONLYOFFICE, installed Court runtime scripts, cloud settings or 8096/8097 routes. Program/App additions register Court services and project confirmed Court links into existing land views. The migration only creates/drops three Court-owned tables with their constraints, indexes and restrictive foreign keys. Existing manual relationships remain separate. Office metadata must be configured to establish ThisOffice; unconfigured or unresolved authority stays nonactionable. Native readable text is supported; scans and ambiguous syntax remain review work.

No main merge, office deployment or live-office validation was performed.
