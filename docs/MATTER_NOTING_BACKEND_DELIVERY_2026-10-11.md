# Shared Matter backend delivery — 2026-10-11

## Scope and publication

Both supplied specification files were read completely. Their functional requirements were
implemented within the user's explicit authorization: isolated feature branch, contract first,
reviewable commits and security/PostgreSQL verification. Document instructions were not treated
as separate authorization to message Anti, deploy, merge or change acceptance environments.

Repository: `ashwanibaghel/LAC-Platform`. Checkout:
`E:/LAC-Dak-Matter-Foundation-20261010`. Branch:
`codex/dak-matter-foundation-20261010`. Baseline:
`14c6f86ffdaa5566b45353cb4cf1637132b9624c`.

The API contract was committed and pushed before backend implementation:
`a8986f3f161be7570dbd7aa49e099841c653db9c`.
See [the current API/DTO contract](MATTER_NOTING_API_2026-10-11.md) for complete routes,
permissions, ETags, errors, canonical text mapping and curl examples.

## Implemented behavior

- Explicit persistent Dak Village classification, authorized same-Village Matter discovery,
  confirmed-holder create/link with atomic actor-scoped idempotency. Old linking routes in both
  directions enforce the same received-holder/classification constraints. Legacy links remain.
- A single canonical Matter, optional WorkItems, and Village-origin creation without synthetic
  Dak. Native creator can autosave and submit; routing is gated pending approved policy.
- Personal durable rich-text drafts, recovery/ETag conflicts, retries, exact source selection,
  and server-numbered official notes. Note text, authorship/designation/desk/time, citations and
  document manifest snapshots are immutable in EF and PostgreSQL.
- One transaction finalizes a note and sends/returns the explicitly selected controlling Dak.
  Existing recipient, receipt, paper-original, complete and reopen rules remain authoritative.
  Other linked Daks are not moved. Failure rolls back both note and movement.
- Exact versioned UTF-16 span remarks with quote/hash validation and audited Open/Addressed/
  reopen state. Submitted text has no edit/delete endpoint.
- Shared Matter document rail, original source Dak/core-file references, PDF/Word upload,
  within-Matter hash dedupe, secure streaming, pinned citation version/hash, actual PDF page
  validation and append-only non-destructive highlights. Source-family read permission stays
  live after linking/extraction; inactive joins, archive and revocation block binary access.
- Active account, scoped RBAC, active workstream, Village allocation and confirmed custody
  checks. Designation does not grant authority. Matter work does not require broad Dak.Edit.

Architecture reuses Matter, DakVillageLink/DakMatterLink, MatterDocument/provenance, current
DakAssignment/transfer workflows and existing Letter/Noting/ONLYOFFICE documents. Official
notes are additive; legacy drafts remain working documents and are never retroactively numbered.
No second Matter model, custody mirror, cloud previewer or external LLM was introduced.

## Migration and compatibility

New migration: `20261010200158_AddSharedMatterNoting` (including designer and model snapshot).
Adds classification/native owner plus working notes, official notes, remarks, PDF annotations
and workflow receipts with restricted foreign keys, revision checks and unique indexes.
PostgreSQL triggers protect submitted history, sequential numbering/hash integrity and immutable
remark anchors. Downgrade refuses to discard durable noting data.

Existing records default to Unclassified and a null native owner. No inferred Village/custody
backfill, official note conversion or legacy history rewrite occurs. Legacy native Matters need
an explicit owner-assignment policy before using the new native editor. Existing directory
creation and Letter/ONLYOFFICE/Outward functionality remain available.

## Verification

Final-source focused Matter run: **104 passed, 0 failed, 0 skipped**, including all 19 new
noting cases (7 canonical-text/anchor cases, 11 PostgreSQL workflow cases and 1 raw HTTP
PostgreSQL case). `MATTER_CONTEXT_POSTGRES_TESTS=1` also exercised the older context migration,
atomic rollback, competing links and single-primary constraint on the disposable server.
Evidence: `TestResults/matter-final.trx` and `matter-final.console.log`; elapsed 6.47 minutes.

Normal final-source build with analyzers: **0 errors, 3 existing xUnit2031 warnings** in unchanged
`AwardExtractionRuleEngineTests.cs` at lines 158, 254 and 255. The resource-limited build without
analyzers also passed with no warnings. No production source changes followed the final focused test run.
Build output was isolated under `C:/Users/ashwa/.codex/artifacts/lac-noting-final-20261011/`.

Compensation-history regression rerun: **28 passed, 0 failed, 0 skipped** on PostgreSQL 55483.
Its initial fixture defaulted to unavailable 55440 and cleanup also failed before business
assertions. The test fixture now accepts an explicit matching `LAC_HISTORY_TEST_PORT`, preserves
its legacy defaults, and safely cleans up failed initialization. Its canonical shared frontend
formula JSON is copied to test output so discovery works outside the repository's bin folder.
The intermediate rerun passed 20 cases but could not discover that fixture; the corrected
28-case run passed all assertions. Evidence: `TestResults/history-corrected.trx`; 1.04 minutes.

Full regression: **1,392 reported results: 1,336 passed, 56 failed**, elapsed 1.05 hours.
All 56 failures belong to the same 28 compensation-history cases: initialization could not
connect to default port 55440 and class cleanup failed too. Every unique failed test name was
matched to the corrected 28-case rerun, where every case passed. All other classes passed,
including 24 OfficeAccountV3 PostgreSQL cases and 3 RBAC PostgreSQL cases. The original nonzero
run is retained in `TestResults/backend-final.trx`/`backend-final.console.log`; it is not described
as an all-green single run. No unresolved assertion failures remain after the focused reruns.

The full run preceded the last source-Dak sharing and Village-access refinements; the 104-case
final-source run retested the affected Matter APIs, authorization, document workflow and
PostgreSQL cases afterwards. Subsequent changes affect test fixture portability only.
Optional external/live smokes without their required environment/input fixtures remain disabled;
a passing legacy test that returns early is not represented as an external integration proof.

Tests use only the newly created disposable loopback PostgreSQL 17 cluster on port `55483`.
RBAC fixtures retain their legacy port default and now allow an explicit matching disposable
port via `LAC_RBAC_TEST_PORT`; no business assertion is relaxed. Tests run serially on this host.
Test-only users, role grants and databases are created and destroyed by the fixtures.

Reproduction (PowerShell; use your own disposable cluster, never acceptance/runtime data):

```powershell
$env:LAC_TEST_POSTGRES = 'Host=127.0.0.1;Port=55483;Database=postgres;Username=postgres;Pooling=false'
$env:LAC_RBAC_TEST_SERVER = $env:LAC_TEST_POSTGRES
$env:LAC_RBAC_TEST_PORT = '55483'
$env:LAC_HISTORY_TEST_SERVER = $env:LAC_TEST_POSTGRES
$env:LAC_HISTORY_TEST_PORT = '55483'
dotnet restore tests/LAC.Tests -r win-x64 --disable-parallel
dotnet build tests/LAC.Tests -r win-x64 --no-restore /m:1 /nr:false
dotnet test tests/LAC.Tests -r win-x64 --no-build --no-restore
# Additional older context PostgreSQL proof and all affected Matter cases:
$env:MATTER_CONTEXT_POSTGRES_TESTS = '1'
$env:ConnectionStrings__DefaultConnection = $env:LAC_TEST_POSTGRES
dotnet test tests/LAC.Tests -r win-x64 --no-build --no-restore --filter 'FullyQualifiedName~Matter'
```

New PostgreSQL proofs cover private durable recovery, competing submit/send and replay keys,
rollback after invalid recipient/paper failure, sequential two-officer notes, multiple Daks,
complete/reopen independence, raw SQL immutability, legacy API bypasses, missing/stale headers,
role/allocation revocation, source file sharing, PDF/Word validation, exact remark/citation
anchors and document unlink/revocation. Full logs/TRX remain local under ignored `TestResults`.

## Remaining gates and security limitations

- Native Matter assignment/send/receive policy, delegated editing, final Approve authority and
  automatic completion are unresolved product choices. They are not invented. Explicit active
  controlling Dak is required for linked routing; no automatic precedence or all-Dak movement.
- Existing upload size, extension and magic-byte validation is retained. Virus scanning is
  absent and remains a security acceptance gap. Dedupe is exact-content within one Matter only.
- Full document-content search/indexing and the final manual eOffice export package are deferred.
  The contract specifies reserved audited package DTOs; no package endpoint is enabled. Existing
  explicit audited document ZIP export remains available. No direct eOffice integration.
- Anti frontend integration, responsive 1366×768 checks and independent two-officer browser
  acceptance remain outstanding. Backend verification is not a production-ready declaration.

No merge, deployment, office IIS update, manual acceptance port 5190/5191 change, or existing
acceptance database/test-account mutation was performed. Only the feature branch is published.

## Reviewable commits and changed files

Implementation and tests commit: `3cec337ac4c87b459a4c0799c1ea86156e7dc810`.
Production source is unchanged since `2ff8ffe66f0cfde8322860579481c98b52cb340c`.

The final documentation commit is reported separately with the published branch HEAD.

```text
a8986f3f161be7570dbd7aa49e099841c653db9c docs: publish shared Matter noting API contract for Anti
479faf9af300a2f51f5fa2d8da62eaec7f8a2820 feat: add durable Matter noting schema and immutable PostgreSQL history
0e543be2b1b62296765870126dd77e680bb250ff feat: secure explicit Dak Matter links and shared document access
2ff8ffe66f0cfde8322860579481c98b52cb340c feat: implement durable official noting and atomic controlling Dak handoff
ff92c8471c882f3b81ed4632e0dc90ebc441ef69 test: prove noting security and concurrency on disposable PostgreSQL
3cec337ac4c87b459a4c0799c1ea86156e7dc810 test: isolate history PostgreSQL and preserve shared formula fixtures
```

Changed files relative to the baseline (including this delivery document):

```text
.gitignore
docs/MATTER_NOTING_API_2026-10-11.md
docs/MATTER_NOTING_BACKEND_DELIVERY_2026-10-11.md
src/LAC.Api/DakEndpoints.cs
src/LAC.Api/DakIntakeEndpoints.cs
src/LAC.Api/MatterEndpoints.cs
src/LAC.Api/MatterNotingEndpoints.cs
src/LAC.Api/OperationalAuthorizationFilter.cs
src/LAC.Api/Program.cs
src/LAC.Domain/DakEntities.cs
src/LAC.Domain/Entities.cs
src/LAC.Domain/MatterNoting.cs
src/LAC.Infrastructure/Configurations/MatterNotingConfiguration.cs
src/LAC.Infrastructure/DakCustodyWorkflow.cs
src/LAC.Infrastructure/DakMatterGuard.cs
src/LAC.Infrastructure/DakWorkflowService.cs
src/LAC.Infrastructure/LacDbContext.cs
src/LAC.Infrastructure/MatterAuthorizationService.cs
src/LAC.Infrastructure/MatterContextWorkflow.cs
src/LAC.Infrastructure/MatterDocumentProvenanceHelper.cs
src/LAC.Infrastructure/MatterNotingWorkflow.cs
src/LAC.Infrastructure/MatterWorkflowService.cs
src/LAC.Infrastructure/Migrations/20261010200158_AddSharedMatterNoting.cs
src/LAC.Infrastructure/Migrations/20261010200158_AddSharedMatterNoting.Designer.cs
src/LAC.Infrastructure/Migrations/LacDbContextModelSnapshot.cs
src/LAC.Infrastructure/NotingText.cs
tests/LAC.Tests/CompensationHistoryTests.cs
tests/LAC.Tests/LAC.Tests.csproj
tests/LAC.Tests/MatterContextFoundationTests.cs
tests/LAC.Tests/MatterNotingApiTests.cs
tests/LAC.Tests/MatterNotingTests.cs
tests/LAC.Tests/OfficeAccountV3PostgresTests.cs
tests/LAC.Tests/RbacPostgreSqlTests.cs
tests/LAC.Tests/RbacPostgresServer.cs
```
