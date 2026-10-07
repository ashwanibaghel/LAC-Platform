# RBAC implementation and proof

Branch: `codex/rbac-dynamic-allocation-audit`.
Mandatory base: `d44941636d54316350e8d6ee2dc03628281baace`.
Review date: 7 October 2026, Asia/Calcutta.
No older RBAC/Dak branch was used, merged or deployed. The office database was not
modified. This report supersedes the allocation-permission and strict visibility
proposal in the historical pre-implementation audit.

## Implemented authority model

```text
User -> Designation                    civil post, no authority implied
User -> Role[] -> RolePermission[]      sole positive permission source
User -> WorkAllocation[] -> WorkDefinition + Scope[] + validity + order
User -> Desk membership                existing custody rules

Officer reads: active role permission + existing resource/workflow scope rules
Officer actions: active role permission AND matching current allocation/scope
                 AND existing workflow/custody rules

Assistant: own role permission AND delegated permission ceiling
           AND supervisor's CURRENT matching permission
           AND child allocation intersected with CURRENT source allocation/scope
           AND existing workflow/custody rules
```

WorkAllocation contains no permissions. AssistantPermissionLimit is a restrictive
ceiling, not a positive grant. Removing a role permission defeats any allocation
and any assistant limit. Work is a reusable category, while WorkItem remains an
ad-hoc task. No officer names or current work-order assignments appear in production
allocation logic. Explicit senior powers are ordinary configured role permissions,
independent of ADM/Patwari/NT/AAO designation.

The API-wide narrowing gate covers generic and specialized operational routes,
uses bound DTOs/forms and canonical parent relationships to resolve geography, and
preserves the underlying workflow decisions. It handles Land Records, Award/NM,
Possession, Compensation, Land Acquisition, Court, Matter, Dak, Outward and Schedule
contexts. Reclassification checks both source and target workstreams. Unknown
geography requires explicit global responsibility. One-off WorkItem officer
operations continue to use their existing task/assignment rules.

Officer View is not bounded by allocation geography: a broad LR.View officer can
read Najafgarh while only Bijwasan/Dwarka actions are allocated. An active temporary
Najafgarh allocation enables its actions until expiry; designation and roles do not
change. Assistant record reads remain bounded by their explicit delegated scopes;
scope-blind collection routes fail closed for area-only delegated grants.

## Security fixes

1. Users.Manage-only accounts cannot change security roles. Role changes require
   Roles.Assign or Access.Manage. Roles.Assign cannot grant reserved system or
   administrative role bundles, and stays within the caller's existing permission
   and scope ceiling (same scope mode or All; Own/Assigned/Workstream are not ranked
   as interchangeable grants). Allocation/desk/workstream changes require Allocations.Manage. Password
   reset of another operational account requires Roles.Assign plus Allocations.Manage
   and the caller's permission ceiling, or Access.Manage. Equal role bundles with
   different geographic allocations cannot be taken over by Users.Manage alone.
   Dormant system/administration memberships are also protected from credential
   takeover before they are reactivated.
2. A SessionVersion UUID is included in every login cookie and checked against the
   current active account on every request. Password reset/replacement, recovery CLI
   reset and assistant revocation rotate it. Existing cookies are rejected with 401.
3. Both old/new generic audit values explicitly redact password, credential,
   security-stamp, session-version, token and secret fields. The additive migration
   removes historical AppUser credential/security copies. Role/allocation changes
   and scope edits are audited rather than excluded from history.
4. Generated credentials are cryptographically random, stored only as ASP.NET
   Identity hashes, shown once with no-store responses, expire after 24 hours and
   require replacement before operations. Reset rotates sessions. Existing profile
   getters never return passwords, hashes, session versions or temporary credentials.

## Exact schema changes

Migration: `20261006220754_AddDynamicWorkAllocationAndSessionSecurity`.
Generated migration, Designer and LacDbContextModelSnapshot are included.
Columns below are NOT NULL unless explicitly marked nullable. Enums use PostgreSQL
text; Revision values are application-managed concurrency tokens, not rowversion.

The two new OfficialRecord tables (WorkDefinitions and WorkAllocations) include:
`Id uuid PK`, `CreatedAt/UpdatedAt timestamptz NOT NULL`, `CreatedBy/UpdatedBy text
NULL`, `RecordStatus text NOT NULL`. CreatedBy/UpdatedBy identify the actual actor.

| Table | Columns beyond OfficialRecord | Keys, constraints and behavior |
| --- | --- | --- |
| WorkDefinitions | Code varchar(64), Name varchar(200), Description text nullable, Kind text, WorkstreamId uuid, IsActive bool, Revision int | Unique Code; Workstream FK restrict; Revision concurrency token; Code/Kind/Workstream immutable through update API |
| WorkAllocations | UserId uuid, WorkDefinitionId uuid, ValidFrom timestamptz, ValidTo nullable timestamptz, WorkOrderReference text, Reason nullable text, RevokedAt nullable timestamptz, RevokedByUserId nullable uuid, DelegatedFromAllocationId nullable uuid, Revision int | User/WorkDefinition/source allocation FKs restrict; index (UserId,WorkDefinitionId,ValidFrom), individual FK indexes; CHECK ValidTo is null or greater than ValidFrom; Revision concurrency token |
| WorkAllocationScopes (not OfficialRecord) | Id uuid PK, WorkAllocationId uuid, Kind text, DistrictId/SubDivisionId/VillageId nullable uuid | Allocation and typed geographic FKs restrict; FK indexes; CHECK exactly the target matching Global/District/Subdivision/Village; scope edits preserved through audit rows |
| AssistantPermissionLimits (not OfficialRecord) | UserId uuid, PermissionId uuid | Composite PK (UserId,PermissionId); user and existing permission FKs restrict; PermissionId index; only narrows roles |

AppUsers adds `SessionVersion uuid NOT NULL`, `MustChangePassword bool NOT NULL`,
`TemporaryCredentialExpiresAt timestamptz NULL`, `SupervisingOfficerId uuid NULL`,
`AssistantRevision int NOT NULL`. Supervisor FK references AppUsers with restrict;
indexed; CHECK supervisor is not self. SessionVersion and AssistantRevision are
concurrency tokens. The application additionally forbids assistant supervisors and
re-parenting, so nested delegation/cycles cannot be created through its APIs.

AuditLogs adds `ActorUserId uuid NULL`, `OnBehalfOfUserId uuid NULL`,
`ActorDisplayNameSnapshot text NULL`, `OnBehalfOfDisplayNameSnapshot text NULL`, and
`ActorLabel text NULL`. These are immutable identity snapshots rather than
designation-derived authority. ChangedBy retains its prior actual-user GUID string.

DakMovements, OutwardEvents, MatterEvents, WorkItemEvents, ScheduledEventEvents,
CourtCaseEvents and RecordAccessEvents each add nullable UUID OnBehalfOfUserId and
nullable text OnBehalfOfDisplayNameSnapshot. Existing actor IDs remain the assistant,
never the officer. These two fields are EF shadow properties populated centrally
when saving. The RBAC audit endpoint exposes the structured identity and label;
existing timeline layouts are unchanged.

There is no Role-to-Designation grant, WorkAllocationPermission table, Work-owned
permission bundle, name-based officer routing or second positive permission catalog.
A per-request RolePermission query ceiling prevents unselected/removed parent codes
from leaking through specialized context/capability queries; it is not stored.
Secondary resource authorization also applies live assistant parent/work bounds.

Seed catalog: Land Acquisition, Award, LR, NM, ENM, Possession, Accounts,
Compensation, Statement-A, Court Cases, RTI, Correspondence, General Administration.
Existing Workstreams/Desks remain intact. The bootstrap account receives explicit
global bootstrap responsibilities once; ordinary existing users receive no guessed
geographic assignments. Revoked bootstrap responsibilities are not restored by seeding.

## APIs and permissions

The exact requests, responses, permission gates, error statuses and TypeScript shapes
are documented in [Anti RBAC frontend contract](anti-rbac-frontend-contract.md).

Added permissions: `Roles.Assign`, `Allocations.Manage`, `WorkCatalog.Manage`,
`Assistants.Manage`. Users.Manage and Access.Manage remain distinct.

New endpoints:

- GET/POST `/api/admin/works`; PUT `/api/admin/works/{id}`.
- GET `/api/admin/account-options`.
- GET/POST `/api/admin/users/{userId}/allocations`;
  PUT `.../{id}`; POST `.../{id}/revoke`.
- GET `/api/auth/allocations`; POST `/api/auth/change-password`.
- GET/POST `/api/officers/me/assistants`; GET `.../delegation-options`;
  GET/PUT `.../{id}`; POST `.../{id}/reset-credential`; POST `.../{id}/revoke`.
- GET `/api/admin/rbac-audit` with bounded paging and actor/officer filters.

Existing officer create/edit accepts optional allocation arrays atomically. Existing
password reset issues a temporary credential (or accepts a legacy supplied temporary
password) and invalidates sessions. Existing login/me adds credential-change and
supervisor state. Assistant metadata/ownership is a separate account relationship,
not a DEO designation rule. Another officer cannot edit/reset/revoke an owned assistant.

## Proof method and results

Direct API tests use real cookie authentication, real password hashing, the production
endpoint/filter/service pipeline and isolated fictional data. No hidden UI button is
used as evidence. Existing tests also include service-level/fake-auth fixtures; those
are regression evidence, not real-cookie end-to-end certification.

Phase 0 ran before allocation implementation: **28 passed, 0 failed, 0 skipped**.
Evidence: `tmp/rbac-proof/phase0-security.trx`.

The initial combined foundation run passed **38/38**, including **3/3 PostgreSQL**
tests without skips. Evidence: `tmp/rbac-proof/rbac-foundation-and-postgresql.trx`.
Final regression totals and subsequent additions are recorded below after execution.

### Direct API proof matrix

All identities are fictional. “LR bundle” means configured role permissions, not
the Patwari designation. Expected/actual results are the server HTTP/status or stored
audit assertion. Historical baseline FAIL rows remain in the pre-implementation audit.

| User | Role | Designation | Permission | Scope | Expected API result | Actual result | PASS/FAIL |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Anonymous | None | — | None | None | Admin read/write 401 | 401 / 401 | PASS |
| Unprivileged | None | ADM | None | LR membership only | Admin/LR operations 403 | 403 | PASS |
| Reader | LR.View bundle | PATWARI / DEO / ADM | LR.View, no LR.Edit | All read | Read 200; direct write 403 for each post | 200 / 403 | PASS |
| Account manager | Users.Manage only | — | No role assignment | All account maintenance | Promote self/other/create SYSTEM_ADMIN denied | 403 / 403 / 403 | PASS |
| Same account manager | Users.Manage only | — | No stronger-target reset | Administrative | Reset bootstrap admin denied | 403 | PASS |
| Reset officer, two sessions | Account management role | — | Users.Manage | Existing cookies | Both old sessions rejected after reset | 401 / 401 | PASS |
| Reset officer | Same | — | Login | Credentials | Old password 401; new temporary password login 200 | 401 / 200 | PASS |
| Created/reset user | Any | — | Credentials | N/A | Hash verifies; account responses contain no credential/hash | Stored/response assertions passed | PASS |
| Admin | Explicit administration | Any | Audit credential secrecy | Administrative | No credential fields in old/new audit JSON | Assertions passed | PASS |
| LR officer | Broad LR.View + LR.Edit | PATWARI | View | No geographic allocation | Read Najafgarh 200 | 200 | PASS |
| Same officer | Same | PATWARI | Edit | No allocation | Direct official creation 403 | 403 | PASS |
| Same officer | Same | PATWARI | Edit | Bijwasan + Dwarka | Create in each home area 201; Najafgarh 403 | 201 / 201 / 403 | PASS |
| Same officer | Same, unchanged | PATWARI unchanged | Edit | Temporary Najafgarh | Before expiry 201; after expiry 403 | 201 / 403 | PASS |
| Same officer after expiry | Same | PATWARI | View | Broad role read | Najafgarh remains readable | 200 | PASS |
| Monitoring officer | LR.View only | PATWARI | No Edit | Home allocation present | Allocation cannot grant edit | 403 | PASS |
| Catalog administrator | Explicit catalog role | Any | WorkCatalog.Manage | Administrative | Create Work 201; role count unchanged | 201; unchanged count | PASS |
| Scoped officer | LR.Edit role | PATWARI | Edit | Future grant then active | Before start 403; after start 201 | 403 / 201 | PASS |
| Allocation administrator | Allocations.Manage | Any | Allocation revoke | Revision-bound | Stale 409; correct revoke 200; subsequent action 403 | 409 / 200 / 403 | PASS |
| Account administrator | Explicit admin powers | Any | Create/edit account + allocations | Atomic | Invalid allocation 400 leaves no user | 400; no persisted account | PASS |
| Edited officer | Same role | PATWARI unchanged | Edit | Replace home with cross-area allocation | Home action 403; new area action 201 | 403 / 201 | PASS |
| Account maintainer | Users.Manage + same LR role as target | PATWARI | Password reset | Maintainer home; target cross-area | Cannot take over target despite equal role codes | 403; target still acts 201 | PASS |
| Role assigner | Users.Manage/Roles.Assign/Allocations.Manage + Dak.View Assigned | — | Assign / Reset | Requested Dak.View All | Scope cannot expand via collection-entry permission or takeover; same Assigned grant allowed | 403 / 403; same-scope 200 | PASS |
| LR officer | LR.Verify/LR.Commit | PATWARI | Verify / Commit | Source record outside home; forged home body | Canonical target overrides forged geographic claim | 403 / 403 | PASS |
| Matter officer | Matter.Edit | PATWARI | Reclassify | Source allocated, target not allocated | Reject until both workstreams allocated | 403 then 200 | PASS |
| Reader DEO | LR role + selected View ceiling | Optional | View only | One delegated home village | Own area read 200; other area/read and write denied | 200 / 403 / 403 | PASS |
| Writer DEO | LR role + selected View/Edit ceiling | Optional | Edit | Delegated second home village | Creation 201 with actual actor and supervisor | 201; both IDs/label stored | PASS |
| Same DEO | Same role | Optional | Assistant creation | No re-delegation | Create own assistant denied | 403 | PASS |
| Supervising officer | Assistants.Manage | PATWARI | Delegation | Outside current parent scope | Wider child allocation denied | 403 | PASS |
| Existing writer DEO | Same child role | Optional | Edit | Parent Edit removed | Next action denied without re-login | 403 | PASS |
| Existing DEO | View ceiling retained | Optional | View | Parent scope narrowed | Formerly covered area denied immediately | 403 | PASS |
| Existing DEO | LR role + ceiling | Optional | Edit | Source parent allocation revoked | Next action denied without re-login | 403 | PASS |
| Temporary DEO session | Same | Optional | Credential replacement / login | Credential expired after login | Existing and new authentication denied | 401 / 401 | PASS |
| Other officer | Assistants.Manage | PATWARI | Reset owned assistant | Not its supervisor | Another officer's assistant reset hidden | 404 | PASS |
| Owned DEO after edit | Same raw bundle, narrower ceiling | Optional | View only | Current grant | Edit removed immediately | 403 | PASS |
| Owned DEO after reset | Same | Optional | Credentials | Old session | Session invalidated; temporary operations blocked | 401 / 403 | PASS |
| Owned DEO after revoke | Same | Optional | Any access | Account + allocations revoked | Existing session rejected | 401 | PASS |
| LR workstream reader | Workstream-scoped bundle | — | Award.View | LR only | Award/Possession direct reads denied | 403 / 403 | PASS |
| Dak wrong-desk member | Assigned Dak.View | — | Download | Other live desk | Direct document stream denied | 403 | PASS (existing suite) |

The initial PostgreSQL real-cookie test additionally proves officer broad View,
home-only actions, individual assistant creation/replacement/edit, parent scope
change, dual-actor persistence and password-reset session rejection on a relational
database. Tests named for security gaps were converted from unsafe-baseline
characterization to denial/regression assertions before implementation.

### PostgreSQL migration/integration results (separate)

Dedicated PostgreSQL 17 cluster: loopback port **55438**, data only under
`tmp/rbac-postgresql`. Tests require LAC_RBAC_TEST_SERVER and reject the office
runtime destination. Each test creates a random `lac_rbac_test_<uuid>` database,
then removes only that validated test database. No office connection/credential is used.

| Integration check | Initial actual result |
| --- | --- |
| Full migration from empty database, no pending model/migrations | PASS |
| Production cookie/role/allocation/assistant/audit API flow on PostgreSQL | PASS |
| Upgrade from previous migration with historical credential-bearing audit | PASS; credentials removed, other audit fields retained; session versions generated |
| Development downgrade/re-upgrade of this migration | PASS |
| Invalid geographic scope shape | PostgreSQL 23514 CHECK rejection |
| Nonexistent geographic FK | PostgreSQL 23503 FK rejection |
| Reversed allocation interval | PostgreSQL 23514 CHECK rejection |
| Duplicate work code | PostgreSQL 23505 unique rejection |
| Concurrent stale allocation revision | DbUpdateConcurrencyException |

Downgrade is a schema-test operation on disposable data; it drops allocation tables
and does not restore erased audit credentials. It is not a production rollback plan.

## Deployment/handoff limitations

- Office migration, merge, push and deployment were not performed.
- Existing officers need explicit work/geographic allocations. Their action access
  fails closed until configured; no names/designations/current order are guessed.
- ENM/RTI/Statement-A and additional categories do not create missing application
  modules or permission codes. Existing supported routes have explicit action bindings.
- Assistant area-scoped aggregate directories fail closed; a future filtered directory
  UI/API can build on record-level allocation checks. Broad officer viewing is retained.
- Legacy role Workstream scopes still need explicit matching legacy memberships.
  Generic Assigned/Own scopes retain their existing fail-closed/resource-context rules.
- Unresolved/bulk action contexts require explicit global responsibility rather than
  inferring geography from a desk. Multi-area resources require coverage of every
  resolved affected/linked area. In-flight requests are authorized at their checks;
  revocations are re-evaluated on subsequent requests without a permission cache.
- Existing timeline projections and the frontend were not redesigned. Anti RBAC
  receives the exact contract; the new audit API exposes the dual-actor fields.
- Local direct/relational tests do not certify the deployed office installation.

## Final verification and changed files

Full regression run before final reset/scope hardening: **950 passed, 0 failed,
10 skipped (960 total)**. The skips are existing DakPostgresTests requiring their
separate LAC_TEST_POSTGRES opt-in; none of the new RBAC PostgreSQL tests skipped.

Final focused run on final source: **42 passed, 0 failed, 0 skipped**:
16 existing RBAC tests, 11 direct baseline/security proofs, 10 dynamic-allocation
proofs, 2 recovery/bootstrap tests, and **3 PostgreSQL migration/integration tests**.
Security suites therefore pass 29/29 in this final run; Phase 0 passed 28/28 before
dynamic allocation implementation. The added test covers scope escalation.

Commands:

```powershell
$env:LAC_RBAC_TEST_SERVER='Host=127.0.0.1;Port=55438;Database=postgres;Username=lac_rbac_test;Pooling=false'
dotnet test tests/LAC.Tests/LAC.Tests.csproj --no-restore --logger 'trx;LogFileName=rbac-final-full.trx' --results-directory tmp/rbac-proof
dotnet build tests/LAC.Tests/LAC.Tests.csproj --no-restore --no-incremental
dotnet test tests/LAC.Tests/LAC.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RbacTests|FullyQualifiedName~RbacBaselineProofTests|FullyQualifiedName~DynamicAllocationApiTests|FullyQualifiedName~RbacPostgreSqlTests|FullyQualifiedName~OfficeAuthMaintenanceTests' --logger 'trx;LogFileName=rbac-final-verified.trx' --results-directory tmp/rbac-proof
```

Sanitized test names/outcomes, evidence hashes and exact changed-file inventory:
[rbac-verification-results.json](rbac-verification-results.json). Raw local TRX
output and PostgreSQL data are ignored by Git. The evidence JSON includes no
HTTP bodies, credential values or test diagnostic logs. Git diff whitespace checks
pass. Existing AwardExtractionRuleEngineTests xUnit2031 warnings are unrelated.

Commit identity is the local commit containing this report (git rev-parse HEAD);
its full SHA is returned in the handoff message. Its sole parent is the mandatory
base SHA above. No merge, push, deployment or office migration was performed.

48 changed files:

- `.gitignore`
- `docs/anti-rbac-frontend-contract.md`
- `docs/rbac-audit-and-dynamic-allocation-plan.md`
- `docs/rbac-implementation-and-proof.md`
- `docs/rbac-verification-results.json`
- `src/LAC.Api/AccountSecurity.cs`
- `src/LAC.Api/OfficeAuthMaintenance.cs`
- `src/LAC.Api/OfficerAssistantEndpoints.cs`
- `src/LAC.Api/OperationalAuthorizationFilter.cs`
- `src/LAC.Api/Program.cs`
- `src/LAC.Api/RbacEndpoints.cs`
- `src/LAC.Api/TemporaryCredentials.cs`
- `src/LAC.Api/WorkAllocationEndpoints.cs`
- `src/LAC.Domain/Entities.cs`
- `src/LAC.Domain/ICurrentUserContext.cs`
- `src/LAC.Domain/IdentityEntities.cs`
- `src/LAC.Domain/PermissionCodes.cs`
- `src/LAC.Domain/WorkAllocationEntities.cs`
- `src/LAC.Infrastructure/AccessControlService.cs`
- `src/LAC.Infrastructure/AssistantResourceAuthorization.cs`
- `src/LAC.Infrastructure/AuditRedaction.cs`
- `src/LAC.Infrastructure/Configurations/WorkAllocationConfiguration.cs`
- `src/LAC.Infrastructure/CourtAuthorizationService.cs`
- `src/LAC.Infrastructure/DakAuthorizationService.cs`
- `src/LAC.Infrastructure/HttpCurrentUserContext.cs`
- `src/LAC.Infrastructure/LacDbContext.cs`
- `src/LAC.Infrastructure/MatterAuthorizationService.cs`
- `src/LAC.Infrastructure/Migrations/20261006220754_AddDynamicWorkAllocationAndSessionSecurity.cs`
- `src/LAC.Infrastructure/Migrations/20261006220754_AddDynamicWorkAllocationAndSessionSecurity.Designer.cs`
- `src/LAC.Infrastructure/Migrations/LacDbContextModelSnapshot.cs`
- `src/LAC.Infrastructure/OutwardAuthorizationService.cs`
- `src/LAC.Infrastructure/ScheduleAuthorizationService.cs`
- `src/LAC.Infrastructure/SeedData.cs`
- `src/LAC.Infrastructure/WorkAllocationService.cs`
- `src/LAC.Infrastructure/WorkCatalogSeed.cs`
- `src/LAC.Infrastructure/WorkItemAuthorizationService.cs`
- `tests/LAC.Tests/DakTests.cs`
- `tests/LAC.Tests/DynamicAllocationApiTests.cs`
- `tests/LAC.Tests/MatterAuthorizationAndWorkspaceTests.cs`
- `tests/LAC.Tests/MatterContextFoundationTests.cs`
- `tests/LAC.Tests/OutwardTests.cs`
- `tests/LAC.Tests/Phase2GTests.cs`
- `tests/LAC.Tests/Phase2HTests.cs`
- `tests/LAC.Tests/Phase2ITests.cs`
- `tests/LAC.Tests/RbacBaselineProofTests.cs`
- `tests/LAC.Tests/RbacPostgreSqlTests.cs`
- `tests/LAC.Tests/TestWorkAllocations.cs`
- `tests/LAC.Tests/WorkItemTests.cs`
