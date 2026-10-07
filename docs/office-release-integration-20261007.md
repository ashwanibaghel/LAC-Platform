# Office release integration verification — 2026-10-07

Integration branch: `integration/office-release-20261007-dak-rbac`.
Starting base: `d44941636d54316350e8d6ee2dc03628281baace`.
The final integration SHA is supplied with the release handover and can be read
with `git rev-parse HEAD` on this branch. No main merge or office deployment was performed.

## Frozen inputs and ancestry

| Input | Exact frozen SHA |
| --- | --- |
| Base | `d44941636d54316350e8d6ee2dc03628281baace` |
| Dak backend | `ec39e83b28b96b34a7d2d2f090ffec4b42d074ad` |
| RBAC backend | `cd899af13690502f316d743e1af56c34e8b9283a` |
| Dak frontend | `244f35a1fea6a608f5e83d82aa422905571ae32a` |
| RBAC frontend | `fa9cc2af053ad82858dfcc301af55a29d84c7390` |

Created the branch at the exact base and used four non-fast-forward merges, in
the order above. `git merge-base --is-ancestor` passed for all five inputs.
Frozen feature branches and commits were not changed.

## Conflict review and integration corrections

There were **no textual merge conflicts**. Git automatically merged these shared
files when integrating the RBAC backend with the Dak backend:

- `src/LAC.Api/RbacEndpoints.cs`
- `src/LAC.Domain/IdentityEntities.cs`
- `src/LAC.Domain/PermissionCodes.cs`
- `src/LAC.Infrastructure/DakAuthorizationService.cs`
- `src/LAC.Infrastructure/LacDbContext.cs`
- `src/LAC.Infrastructure/Migrations/LacDbContextModelSnapshot.cs`
- `tests/LAC.Tests/DakTests.cs`

Reviewed the automatic merges as a union:

- Account credential/session/role/allocation gates coexist with the desk-purpose
  DTO fields and validation used by Dak Record Room destinations.
- AppUser session/delegation fields coexist with OfficeDesk.Purpose.
- Roles.Assign, Allocations.Manage, WorkCatalog.Manage and Assistants.Manage coexist
  with Dak.Mark, Dak.Receive, Dak.PullBack, Dak.Resolve and Dak.Reopen. Dak.Move
  compatibility remains. Dak.Assign is absent from the permission catalog.
- Assistant resource checks coexist with the custody-aware Dak read/action scopes.
- WorkAllocation DbSets/configuration, redacted dual-actor audit and event delegation
  fields coexist with Dak transfer/receipt models and immutable custody safeguards.
- The combined snapshot matches the runtime model: EF reports no pending model changes.
- Existing Dak role/custody fixtures retain their explicit RBAC responsibility grants.

Manual integration-only corrections:

1. `tests/LAC.Tests/DakCustodyApiTests.cs`: explicitly allocate work to the synthetic
   receiver. The combined RBAC action gate initially returned 403 because this new
   Dak fixture had role/desk authority without work responsibility. The correction
   preserves both production gates and the original custody assertions.
2. `tests/LAC.Tests/DakPostgresTests.cs` and `DakCustodyPostgresTests.cs`: select
   predecessors relative to the exact HardenDakIntakePhaseA and AddDakAcknowledgedCustody
   migration IDs, with a missing-target assertion. Later RBAC migrations cannot move
   the intended test target. Production migrations were not edited.
3. `src/LAC.Web/src/admin/dateUtils.ts`: remove inherited comment trailing whitespace.
4. `src/LAC.Web/src/dak/dak.css`: remove an inherited extra blank line at EOF.
   Both frontend corrections are formatting only; frozen frontend comparisons pass
   with whitespace/blank-line differences ignored.

## Verification results

| Final run | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Full non-PostgreSQL backend | 967 | 0 | 0 |
| Dak PostgreSQL (10 intake + 9 custody) | 19 | 0 | 0 |
| RBAC PostgreSQL | 3 | 0 | 0 |
| Frontend: all 18 `tests/*.test.mjs` files | 180 | 0 | 0 |

Backend total: **989 passed, 0 failed, 0 skipped**. Frontend cancellations and
todo counts are also zero. No PostgreSQL test was skipped; both test environment
variables pointed to the dedicated loopback cluster on port 55438, database
`postgres`, with disposable per-test databases created and removed by the suites.

`npm run build`: **PASS**. The final build after formatting cleanup produced the
same JS/CSS output hashes as the browser-tested build.
`git diff --check` and the full base-to-release whitespace check: **PASS**.

Commands (from repository root unless noted):

```text
dotnet test tests/LAC.Tests/LAC.Tests.csproj --no-restore --filter "FullyQualifiedName!~Postgres"
dotnet test tests/LAC.Tests/LAC.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~DakPostgresTests|FullyQualifiedName~DakCustodyPostgresTests"
dotnet test tests/LAC.Tests/LAC.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~RbacPostgreSqlTests"
dotnet ef migrations list --project src/LAC.Infrastructure --startup-project src/LAC.Api --no-build --no-connect
dotnet ef migrations has-pending-model-changes --project src/LAC.Infrastructure --startup-project src/LAC.Api --no-build
npm run build                                      (src/LAC.Web)
node --test tests/*.test.mjs                       (src/LAC.Web)
```

The frontend run includes every requested RBAC, Dak, Matter and Court test file,
plus calculator, bulk revenue, ONLYOFFICE, Court import/officer workflow/error and
DHC suites. Visual capture utilities and ad-hoc live-server smoke scripts are not
included in the automated test totals.

Warnings/initial findings:

- Three pre-existing xUnit2031 warnings in unchanged AwardExtractionRuleEngineTests.
- Vite warns about its main minified chunk exceeding 500 kB; the build succeeds.
- The local API-only Production-environment health process warned that its source
  working directory has no published `wwwroot`. API health and the independent
  production frontend build pass; office static-file staging/deployment was not run.
- Initial backend run: 966 passed, 1 failed, 0 skipped. The only failure was the
  synthetic receiver's missing allocation; after correction the complete 967-test
  non-PostgreSQL suite passed.
- Frontend dependencies initially needed `npm ci`. An initial browser run overlapped
  Vite rebuilding `dist` and encountered four navigation timeouts. Running the
  complete suite after the build finished passed all 180 tests.

Raw TRX/build/browser logs remain under ignored `tmp/rbac-proof`; they are not
committed. There are no unresolved test failures or skips.

## Runtime, migrations and preservation

Production-environment API startup against the disposable loopback PostgreSQL
server at `127.0.0.1:55438` completed successfully. `/api/health` returned HTTP 200,
status Healthy, database Reachable and document storage writable. All 51 migrations
were applied in order. The health process was stopped and its database removed.
No office connection/database was used. No office migration was run.

Both frozen production migrations and their designers match their source SHAs
byte-for-byte in Git. There are 51 distinct migration IDs; the last three are:

1. `20261005194104_HardenDakIntakePhaseA`
2. `20261006214619_AddDakAcknowledgedCustody`
3. `20261006220754_AddDynamicWorkAllocationAndSessionSecurity`

EF pending-model check: **No changes have been made to the model since the last migration.**

Preservation audit:

- Court runtime scripts `install-office-court-ai.ps1`,
  `start-office-court-services.ps1` and `verify-office-court-ai.ps1` match the base.
- Frontend changes are confined to the accepted Dak/RBAC areas, their App routes,
  auth contracts and AppShell technical identity display. Court, Matter, Land,
  Home, Assistant and ONLYOFFICE/editor implementations retain the accepted ancestry.
- Village Matters remains the contextual projection of the central Matter system.
- Dak frontend/backend contracts are exercised by the browser custody suite and
  direct backend custody API/service tests: Send does not immediately transfer
  holding, Receive does, Pull Back/physical return retain sender and evidence rules,
  Resolve requires personal received holding and attestation, and Reopen excludes
  historical Disposed records. Transfer load failures block mutations.
- RBAC frontend/backend contract tests preserve account-options metadata, assignment
  ceilings, temporary credential replacement, parent/child scope/time ceilings and
  server-provided audit identities. Technical SYSTEM_ADMIN has no ADM inference.
- Bootstrap normalization remains exactly guarded by BootstrapAdminId, SYSTEM_ADMIN
  membership and canonical ADM designation. Other officer identities remain intact.
- No screenshots/capture scripts, runtime databases, test logs or credentials were
  added by integration. Changed-path secret-marker scan found no private-key,
  provider-token or AWS access-key markers; integration edits contain no credentials.

## Exact base-to-release name-status

```text
M	.gitignore
A	docs/anti-rbac-frontend-contract.md
A	docs/dak-custody-workflow-implementation.md
A	docs/dak-custody-workflow-proposal.md
A	docs/office-release-integration-20261007.md
A	docs/rbac-audit-and-dynamic-allocation-plan.md
A	docs/rbac-implementation-and-proof.md
A	docs/rbac-verification-results.json
A	scripts/audit-dak-custody.sql
A	src/LAC.Api/AccountSecurity.cs
A	src/LAC.Api/DakCustodyEndpoints.cs
M	src/LAC.Api/DakEndpoints.cs
M	src/LAC.Api/OfficeAuthMaintenance.cs
A	src/LAC.Api/OfficerAssistantEndpoints.cs
A	src/LAC.Api/OperationalAuthorizationFilter.cs
M	src/LAC.Api/Program.cs
M	src/LAC.Api/RbacEndpoints.cs
A	src/LAC.Api/TemporaryCredentials.cs
A	src/LAC.Api/WorkAllocationEndpoints.cs
A	src/LAC.Domain/DakCustodyEntities.cs
M	src/LAC.Domain/DakEntities.cs
M	src/LAC.Domain/Entities.cs
M	src/LAC.Domain/ICurrentUserContext.cs
M	src/LAC.Domain/IdentityEntities.cs
M	src/LAC.Domain/PermissionCodes.cs
A	src/LAC.Domain/WorkAllocationEntities.cs
M	src/LAC.Infrastructure/AccessControlService.cs
M	src/LAC.Infrastructure/ActivityProjectionService.cs
A	src/LAC.Infrastructure/AssistantResourceAuthorization.cs
M	src/LAC.Infrastructure/AttentionProjectionService.cs
A	src/LAC.Infrastructure/AuditRedaction.cs
M	src/LAC.Infrastructure/Configurations/DakConfiguration.cs
A	src/LAC.Infrastructure/Configurations/WorkAllocationConfiguration.cs
M	src/LAC.Infrastructure/CourtAuthorizationService.cs
M	src/LAC.Infrastructure/DakAuthorizationService.cs
A	src/LAC.Infrastructure/DakCustodyWorkflow.cs
M	src/LAC.Infrastructure/DakIntakeMutations.cs
M	src/LAC.Infrastructure/DakWorkflowService.cs
M	src/LAC.Infrastructure/HttpCurrentUserContext.cs
M	src/LAC.Infrastructure/LacDbContext.cs
M	src/LAC.Infrastructure/MatterAuthorizationService.cs
A	src/LAC.Infrastructure/Migrations/20261006214619_AddDakAcknowledgedCustody.Designer.cs
A	src/LAC.Infrastructure/Migrations/20261006214619_AddDakAcknowledgedCustody.cs
A	src/LAC.Infrastructure/Migrations/20261006220754_AddDynamicWorkAllocationAndSessionSecurity.Designer.cs
A	src/LAC.Infrastructure/Migrations/20261006220754_AddDynamicWorkAllocationAndSessionSecurity.cs
M	src/LAC.Infrastructure/Migrations/LacDbContextModelSnapshot.cs
M	src/LAC.Infrastructure/OutwardAuthorizationService.cs
M	src/LAC.Infrastructure/ScheduleAuthorizationService.cs
M	src/LAC.Infrastructure/SeedData.cs
A	src/LAC.Infrastructure/WorkAllocationService.cs
A	src/LAC.Infrastructure/WorkCatalogSeed.cs
M	src/LAC.Infrastructure/WorkItemAuthorizationService.cs
M	src/LAC.Web/src/App.tsx
M	src/LAC.Web/src/admin/AccessAdmin.tsx
M	src/LAC.Web/src/admin/AuditLogsAdmin.tsx
A	src/LAC.Web/src/admin/OfficerAssistantAdmin.tsx
M	src/LAC.Web/src/admin/UsersAdmin.tsx
A	src/LAC.Web/src/admin/WorkCatalogAdmin.tsx
A	src/LAC.Web/src/admin/admin.css
A	src/LAC.Web/src/admin/assistantUtils.ts
A	src/LAC.Web/src/admin/dateUtils.ts
A	src/LAC.Web/src/admin/types.ts
A	src/LAC.Web/src/auth/ChangePasswordView.tsx
M	src/LAC.Web/src/auth/types.ts
M	src/LAC.Web/src/components/AppShell.tsx
M	src/LAC.Web/src/dak/DakDetailWorkspace.tsx
M	src/LAC.Web/src/dak/DakDirectory.tsx
M	src/LAC.Web/src/dak/DakMovementModal.tsx
M	src/LAC.Web/src/dak/DakTimeline.tsx
M	src/LAC.Web/src/dak/dak.css
A	src/LAC.Web/src/dak/dakConfig.ts
M	src/LAC.Web/src/dak/types.ts
M	src/LAC.Web/tests/dak-v2-intake-workspace.test.mjs
A	src/LAC.Web/tests/rbac-contract-regression.test.mjs
A	tests/LAC.Tests/AccountOptionsContractTests.cs
A	tests/LAC.Tests/BootstrapAdminIdentityTests.cs
A	tests/LAC.Tests/DakCustodyApiTests.cs
A	tests/LAC.Tests/DakCustodyPostgresTests.cs
A	tests/LAC.Tests/DakCustodyTests.cs
M	tests/LAC.Tests/DakIntakeTests.cs
M	tests/LAC.Tests/DakPostgresTests.cs
A	tests/LAC.Tests/DakTestCustodyFixtures.cs
M	tests/LAC.Tests/DakTests.cs
A	tests/LAC.Tests/DynamicAllocationApiTests.cs
M	tests/LAC.Tests/MatterAuthorizationAndWorkspaceTests.cs
M	tests/LAC.Tests/MatterContextFoundationTests.cs
M	tests/LAC.Tests/MyDeskTests.cs
M	tests/LAC.Tests/OutwardTests.cs
M	tests/LAC.Tests/Phase2GTests.cs
M	tests/LAC.Tests/Phase2HTests.cs
M	tests/LAC.Tests/Phase2ITests.cs
A	tests/LAC.Tests/RbacBaselineProofTests.cs
A	tests/LAC.Tests/RbacPostgreSqlTests.cs
A	tests/LAC.Tests/TestWorkAllocations.cs
M	tests/LAC.Tests/WorkItemTests.cs
```

## EF migration list

```text
20260902192711_InitialCreate
20260903072138_AddLrMigrationWorkflow
20260903081724_AddKhatauniOwnershipFoundation
20260903195544_AddKhasraStructuredAreaWorkspace
20260903212943_AddKhasraQualifierIntegrity
20260903220941_EnforceKhasraQualifierIdentity
20260904100609_ExpandAwardDomainFoundation
20260904112549_AddAwardIngestionFoundation
20260904155533_CorrectNotificationIdentityForIngestion
20260904162946_RepairAwardVillageLegacyLinks
20260904182949_AddAwardPdfExtractionStaging
20260906071903_AddPermanentSourceEvidence
20260908145823_AddDocumentTrainingExamples
20260909115247_AddSupplementaryAwardParentReference
20260910195054_AddNmEntitlementWorkflow
20260911065519_AddNmReviewFragments
20260911130145_AddNmSemanticStaging
20260911165035_AddNmSemanticAreaRecovery
20260911180454_AddNmSemanticAreaReview
20260913101318_AddNmOwnerReviewOverrides
20260914203355_ReconcileVillageCoreRecordsModel
20260914204918_AddMatterDocuments
20260914211911_AddMatterDrafts
20260918213523_AddIdentityAndRbacFoundation
20260919051325_AddOfficeDeskFoundation
20260919071625_AddDakWorkflowFoundation
20260919170758_AddOutwardWorkflowFoundation
20260919202534_AddMatterAuthorizationFoundation
20260920084853_AddWorkItemFoundation
20260920144429_EnableWorkItemAssignmentHistoryAndRoutingSnapshots
20260920184346_AddRecordAccessEventAndHistoricalContextSnapshots
20260920201642_AddUnifiedTimeAndAttentionEngine
20260921125030_AddCourtLitigationWorkspace
20260923201900_AddMatterDocumentExtractProvenance
20260924214843_AddMatterDraftOfficeDocument
20260927105739_AddAwardClaimSourceFields
20260928131017_AddCourtImportStaging
20260928142254_AddReviewedCourtImport
20260928165240_AddDelhiHighCourtCauseListSync
20260928185101_AllowSharedDhcCauseListDocument
20260928193923_AddDhcHistoricalBackfillMode
20260929060336_AddDhcAssistedSync
20260929071709_AddDhcAssistedPhases
20260929080244_SnapshotDhcAssistedQueueIdentity
20260929140616_AddDhcLiveTargetSetFingerprint
20261004155206_AddAssistantConversations
20261004181412_AddSmartCoreDocumentIntake
20261004222119_AddMatterCanonicalContext
20261005194104_HardenDakIntakePhaseA
20261006214619_AddDakAcknowledgedCustody
20261006220754_AddDynamicWorkAllocationAndSessionSecurity
```
