# RBAC baseline proof and dynamic allocation proposal

Historical pre-implementation checkpoint at the mandatory base SHA. The approved
architecture corrections supersede the proposal below, including allocation
permission entries and strict officer visibility walls. See
`rbac-implementation-and-proof.md` and `anti-rbac-frontend-contract.md` for the
implemented model and current contracts. Test aliases here identify the original
baseline runs; several probes were subsequently converted to security regressions.

Review checkpoint: 7 October 2026 (Asia/Calcutta).

Branch: `codex/rbac-dynamic-allocation-audit`.

Exact base and current HEAD: `d44941636d54316350e8d6ee2dc03628281baace`.

The checkout was clean before `git switch -c codex/rbac-dynamic-allocation-audit <base>`.
No older RBAC/Dak branch was used or merged. This checkpoint adds evidence and a design;
production implementation, migration application, merge, and deployment have not occurred.

## 1. Current architecture

The existing relationships are:

```text
AppUser --DesignationId--> Designation                 (job title)
AppUser --UserRole[]-----> Role --RolePermission[]----> Permission + ScopeMode
AppUser --UserWorkstreamMembership[]-----------------> Workstream
AppUser --UserDeskMembership[]-----------------------> OfficeDesk
Business record --classification/assignment----------> Workstream / OfficeDesk / User
```

These are parallel relationships, not a designation-derived permission hierarchy.
Designation does not automatically confer a role or permission. Multiple roles and
workstreams are already supported in officer create/update API requests. Desks are
organizational custody/routing units; primary desk does not grant a permission.
An operational `WorkItem` is a task, not a reusable responsibility catalog entry.

Authoritative code:

| Concern | Existing implementation |
| --- | --- |
| Entities and scope vocabulary | `src/LAC.Domain/IdentityEntities.cs` |
| Resource context | `src/LAC.Domain/AccessControlModels.cs` |
| Login, account/role/desk administration, permission endpoint filters | `src/LAC.Api/RbacEndpoints.cs` |
| Global API authentication filter and cookies | `src/LAC.Api/Program.cs` |
| Live generic permission evaluation | `src/LAC.Infrastructure/AccessControlService.cs` |
| Cookie snapshots for identity/UI | `src/LAC.Infrastructure/HttpCurrentUserContext.cs` |
| Resource-specific decisions/list filtering | `DakAuthorizationService`, `MatterAuthorizationService`, `OutwardAuthorizationService`, `WorkItemAuthorizationService`, `ScheduleAuthorizationService`, `CourtAuthorizationService` |
| Account UI | `src/LAC.Web/src/admin/UsersAdmin.tsx` |
| Hashing and generic mutation audit | `RbacEndpoints.cs`, `src/LAC.Infrastructure/LacDbContext.cs` |

Login verifies an ASP.NET `IPasswordHasher<AppUser>` hash and issues an eight-hour
cookie containing identity, designation, roles, permissions, workstreams and desks.
Most authorization services query current database account/role/membership state,
so the cookie permission snapshot is not authoritative for those decisions.
`/api/auth/me` also refreshes response data from the database.

Scope modes mean `All`, `Workstream`, `Assigned`, or `Own`. Generic Workstream
checks compare the endpoint/resource classification to live workstream memberships.
Assigned checks require active membership in the assigned desk. Own needs explicit
owner context and is deliberately denied for Dak. Matter supports All/Workstream;
Assigned/Own fail closed there. Other workflow services have their own scope rules.
Collection access and individual record/document access must both be evaluated.

The Workstreams table is an initial responsibility grouping, but its admin API is
read-only. Ten groups are seeded, including Land Acquisition, Award, Land Records,
Possession, Accounts & Compensation, Court References, RTI and Record Room.
Accounts and Compensation are combined; NM/ENM have no separate catalog entries.
Creating roles changes permission bundles, not responsibilities.

## 2. Discovered gaps

| Gap | Consequence / implementation priority |
| --- | --- |
| `Users.Manage` alone authorizes replacing any user's role IDs, including SYSTEM_ADMIN | Account-management privilege can become access-management privilege; add distinct role-assignment authority and prevent unauthorized escalation, including self-promotion and indirect privileged-account resets |
| No geographic scope entity or allocation-to-village/subdivision/district relationship | Land Records Workstream access applies across villages; Bijwasan/Dwarka versus Najafgarh cannot be expressed |
| Membership has AssignedAt but no effective-from/to, work-order reference or revocation history | Temporary/cross-area responsibility cannot expire automatically or be reconstructed reliably |
| Workstream membership is independent of every role | Adding another workstream activates all Workstream-scoped permissions across all assigned workstreams; a future design must bind each work/permission/scope grant together |
| No editable Work Catalog | Work-order changes require seed/code changes or coarse membership reassignment |
| No supervising officer, bounded delegation, or on-behalf-of actor fields | DEO is currently a designation only; assistant account lifecycle and subset enforcement are absent |
| Existing `/assistant/conversations` is the local AI assistant | It does not implement human assistant/DEO delegation and should not be repurposed |
| Password reset changes hash but has no session/security version validation | Previously issued cookies remain usable; no temporary expiry/mandatory replacement flow |
| Generic audit serializes all AppUser properties, including PasswordHash | Plaintext is not stored, but credential hashes are unnecessarily duplicated into audit JSON; redact credentials/security material |
| UserRole, RolePermission and UserWorkstreamMembership do not derive from OfficialRecord | Their changes lack the generic per-entity allocation/permission audit history |
| User UI supports create, status, reset and desks, but no officer account edit form | The update API exists without equivalent editable role/work UI |
| User UI loads Access.Manage-only catalogs as part of Users.Manage account administration | Users.Manage-only accounts cannot load all form data, although direct update can change roles |
| Effective permission response collapses multiple ScopeModes into one ranked value | Workstream, Assigned and Own are overlapping predicates, not a total breadth order; expose grants/scopes without losing information |
| Authorization is spread across generic and six workflow services | A new allocation table alone would not enforce access; every list/detail/write/download path needs integration |
| Several endpoints provide only a constant Workstream context | Geographic and NM/ENM boundaries require resource IDs and server-resolved context, including create requests |

The Patwari/DEO/ADM designations are not security roles. No officer-name routing
table or automatic designation-to-role mapping is proposed.

## 3. Proof method and limits

The new direct-API probes use `WebApplicationFactory<Program>`, the actual API
routing/filter/service pipeline, real password hashing and real cookie login,
with isolated EF InMemory databases and fictional seed/test users. They send HTTP
requests without the UI. The existing suite additionally includes service-level
tests and selected API tests using a test authentication handler; those are not
cookie-login end-to-end proofs.
No production/office account, password or database is read or modified.

Existing tests cover scope denial, assignment/desk eligibility, document isolation,
collection scope unions, immutable history, concurrency and admin invariants.
`tests/LAC.Tests/RbacBaselineProofTests.cs` adds targeted direct-API baseline probes.
Tests named `Known_gap_*` assert the observed existing behavior to make it reproducible;
their passing test result does **not** mean the security requirement passes. Convert
them to denial/expiry acceptance tests when the corresponding fix is implemented.

This proves the tested server pipeline. It does not certify every route, the deployed
office installation, browser behavior, PostgreSQL constraints, or a production upgrade.
The geographic probe uses two fictional villages; there are no actual Bijwasan,
Dwarka or Najafgarh assignments to prove in the current schema.

### Reproduction

Run sequentially, because Windows locks the shared test assembly while executing:

```powershell
dotnet test tests/LAC.Tests/LAC.Tests.csproj --filter 'FullyQualifiedName~RbacTests|FullyQualifiedName~MatterAuthorizationAndWorkspaceTests|FullyQualifiedName~DakTests|FullyQualifiedName~OutwardTests|FullyQualifiedName~WorkItemTests|FullyQualifiedName~CourtOperationalDirectoryTests|FullyQualifiedName~OfficeAuthMaintenanceTests' --logger 'trx;LogFileName=rbac-baseline.trx' --results-directory tmp/rbac-proof --verbosity quiet
dotnet test tests/LAC.Tests/LAC.Tests.csproj --no-restore --filter FullyQualifiedName~RbacBaselineProofTests --logger 'trx;LogFileName=rbac-direct-api-proof.trx' --results-directory tmp/rbac-proof --verbosity quiet
```

## 4. Proposed schema

Retain designation, roles, existing permission codes, organizational workstreams and
desks. Add a distinct allocation foundation, without renaming/reworking workflow modules.

| Entity/change | Proposed fields and constraints |
| --- | --- |
| WorkDefinition | Stable unique Code, Name, Description, IsActive, optional ParentWorkId, WorkstreamId; audited/archivable; no officer identity or implicit permission grant |
| WorkPermissionApplicability | WorkDefinitionId + existing PermissionId; catalog applicability ceiling, not a grant; edits require access administration and cannot invent API permissions |
| WorkAllocation | UserId, WorkDefinitionId, ValidFromUtc, ValidToUtc, WorkOrderReference, Reason, GrantedByUserId, RevokedAt/By, Revision; nonempty interval and explicit scopes required; historical records retained |
| AllocationPermission | AllocationId + PermissionId + ScopeMode; references/grant validation against the user's active role permissions and catalog applicability; no authority from WorkDefinition alone |
| AllocationScope | AllocationId, typed Kind (`Global`, `District`, `Subdivision`, `Village`, `Desk`, or explicitly supported record scope), typed FK target; exactly one target matching Kind, explicit Global, validated hierarchy |
| OfficerDelegation | AssistantUserId, SupervisorUserId, validity/revocation/revision; one current supervisor per assistant, many assistants per officer, distinct active users, no cycles or nested re-delegation |
| DelegationGrant | DelegationId, source OfficerAllocationId, allowed PermissionIds, delegated scopes and validity; delegated work must be the same source work; enforce set containment at write and every authorization decision |
| AppUser credential lifecycle | SessionVersion/security stamp, MustChangePassword, TemporaryCredentialExpiresAt; never add a plaintext credential field |
| Structured audit identity | ActorUserId, OnBehalfOfUserId, DelegationId, allocation/grant ID where relevant, ActorDisplayNameSnapshot, OfficerDisplayNameSnapshot; apply to generic audit, workflow events and record/download access logs |

Seed configurable catalog entries for Land Acquisition, Accounts, Compensation,
Court, RTI, Award, NM, ENM, LR and Possession. Map Accounts/Compensation to the
existing combined workstream; NM/ENM to Award where appropriate. Existing NM routes
must resolve NM context specifically. ENM applicability needs an existing operation
to enforce; creating a work cannot create a new module or make unimplemented work
operational. Catalog creation and Role creation remain separate screens and APIs.

### Authorization contract

```text
active user
AND matching active role permission
AND matching currently valid work allocation + allocation permission
AND server-resolved resource work + geographic/record/desk scope
AND existing workflow custody/ownership constraints
```

Senior access administration retains an explicit, audited global administration
grant; designation and display name never grant that override. Data-wide All scope
must not silently bypass allocations for ordinary officers. Administrative endpoints
use administrative permission policy; operational endpoints use allocation policy.

Within one allocation, geographic targets form a union; distinct restriction
dimensions (geography, work, custody, permission, validity) form an intersection.
Across allocations, complete matching grants form a union. Do not independently
union works, scopes and permissions and create unintended combinations.
Where a role grant itself restricts to Workstream/Assigned/Own, the allocation
restriction narrows it and never widens it.

For an assistant, additionally require an active supervisor and delegation and
intersect each delegated grant with the supervisor's **current** authorized source
allocation, role permission, geographic/desk scope and delegation ceiling.
Parent revocation, expiry, role removal, account disablement or work disablement
must take effect on the next request. No admin/reset/role-management privilege
may arise through ordinary operational delegation. A valid assistant login is
insufficient without an effective delegation; independent roles/memberships must
not bypass the subset bound. Officer X cannot edit Officer Y's assistants.

Resolve villages, awards, matters, documents, work items and court links on the
server. Client resource IDs are inputs, not proof. A multi-village resource needs
an explicit policy: initially require access to every linked area for complete
record/document reads and mutation; list/projection behavior must not expose hidden
areas through counts or links. Records with no trustworthy geographic context fail
closed for area-scoped grants; explicit global allocation can access them.
Apply query predicates before paging/counting/export, then recheck detail/download
and writes. Keep shared geographic caches from returning another user's results.

## 5. Proposed API/UI changes

| API | Purpose / authority |
| --- | --- |
| Existing `/api/admin/roles` | Permission-bundle CRUD remains Access.Manage; validate scope enums and sensitive grant ceilings |
| `/api/admin/works` GET/POST/PUT/status | Work Catalog CRUD; WorkCatalog.Manage (or explicit Access.Manage policy); deactivate without deleting history |
| `/api/admin/account-options` GET | Return only designations, roles, works/scopes that caller may actually assign; avoid requiring unrestricted catalog access for account editing |
| Existing `/api/admin/users` POST/PUT | Add versioned allocation inputs; create/update account, roles and allocations atomically; Users.Manage plus distinct Roles.Assign/Allocations.Manage for those sections |
| `/api/admin/users/{id}/allocations` GET/POST/PUT/revoke | Manage multiple work/scope/time/order grants; require Allocations.Manage and target/grant ceiling checks; use revisions and explicit revoke |
| `/api/officers/me/assistants` GET/POST | Supervising officer creates several individual assistant logins under Assistants.Manage and delegable-grant ceiling |
| `/api/officers/me/assistants/{id}` PUT/revoke/reset-credential | Edit owned assistant and bounded work/permission/scope grants; ownership check before action; revoke server-side |
| `/api/auth/change-password` | Replace temporary credential; expiry checked during login, operational access blocked until replacement |
| Existing admin reset, plus owned-assistant reset | Generate/issue temporary credential once over authenticated response; store only hash/expiry, rotate SessionVersion, never return any current reusable password |
| `/api/auth/me` | Return structured effective allocation grants, supervisor identity and capabilities; snapshot is for UI, not server authorization |

Keep human assistants under `/officers/...`, distinct from the AI conversation API.
Account create/edit forms have separate designation, roles, works, scopes, work-order
and effective-date fields. Multiple allocations can be added without changing the
designation. Show ongoing and temporary grants and explicit revocation. Role creation
edits permission bundles; Work creation edits catalog responsibility metadata.
An officer assistant panel exposes only delegable parent grants and editable owned
accounts. Audit UI renders `DEO X on behalf of Officer Y` using stored structured
actor IDs/name snapshots, not a client-supplied actor label.

## 6. Migration and implementation plan

1. Finish/report the baseline proof matrix before production changes. Preserve
   regressions as tests; separate observed gaps from acceptance successes.
2. Close administrative escalation and credential/session gaps. Separate role
   assignment, allocation management and account maintenance; guard privileged
   target resets/updates, preserve last-admin invariants, redact security fields
   from audits, and implement temporary expiry/change plus session invalidation.
3. Add additive EF schema/migration, catalog seed and allocation/delegation services.
   Use typed FKs, interval checks, restricted deletes, unique codes, live-parent
   validation, transactional updates, revision conflict handling and explicit audit events.
4. Build one allocation evaluator/query policy and integrate all existing generic
   and specialized authorization entry points. Preserve institutional custody,
   task contributor rules, context redaction and linked-document restrictions.
   Do not rely only on AccessControlService: specialized services also need the policy.
5. Implement catalog/account allocation APIs and officer account edit/create UI.
   Prove multiple roles/works and assignment ceiling checks through direct API tests.
6. Implement assistant account/delegation APIs, UI and structured audit propagation.
   Prove subset enforcement, own-account lifecycle, parent revocation and concurrent
   parent/grant edits. Keep actor identity as the assistant's login, never impersonation.
7. Add deterministic TimeProvider tests: future, active, expired, revoked grants;
   Patwari home allocation for Bijwasan/Dwarka plus temporary Najafgarh allocation;
   designation unchanged; unauthorized direct writes/downloads/exports denied.
8. Validate migrations/constraints and transactions against an isolated PostgreSQL
   database, web build and relevant regression tests. No office migration or deployment.

Migration policy: backfill legacy active workstream grants into explicit historical
catalog allocations without widening authority; existing All non-admin roles need
explicit global operational allocations, and catalog mappings need review. Do not
invent village assignments from officer names or today's work order. Unsupported or
ambiguous mappings fail closed and appear in an administrative remediation report.
Enforcement rollout must be explicit and versioned; no indefinite OR fallback to
legacy workstream access after allocation revocation. Keep original membership data
for custody compatibility/history, but define exactly which policy consumes it.

Acceptance must include list/detail/create/edit/download/export, supervisor/assistant
revocation while logged in, role removal, privilege escalation attempts, scope/permission
cross-product attacks, temporary credential expiry/one-time replacement, audit secrecy,
and both actor IDs on real delegated writes. Preserve unrelated module behavior.

## 7. Proof matrix and run results

The existing selected suite passed **289/289** tests, with no skips. The new direct
API probes passed **10/10**, with no skips, recorded separately in
`tmp/rbac-proof/rbac-direct-api-proof.trx`. All **299** selected checks passed;
baseline gap-characterization successes still represent FAIL rows below.
The baseline suite evidence is `tmp/rbac-proof/rbac-baseline.trx`. Build warnings
in unrelated AwardExtractionRuleEngineTests predate this audit.

Test aliases below refer to method names in `RbacBaselineProofTests.cs`:

- P1: `Anonymous_direct_admin_read_and_write_are_401`
- P2: `Designation_and_workstream_without_role_grant_no_authority`
- P3: `Same_permission_bundle_works_independently_of_designation`
- P4: `Workstream_and_role_removal_revoke_existing_cookie_access`
- P5: `Deactivated_account_cannot_use_existing_cookie_or_login`
- P6: `Known_gap_UsersManage_alone_can_promote_self_to_SYSTEM_ADMIN`
- P7: `Known_gap_password_reset_keeps_existing_cookie_valid`
- P8: `Password_is_hashed_and_user_responses_do_not_expose_credentials`
- P9: `Known_gap_LR_workstream_has_no_geographic_boundary`
- P10: `Admin_can_create_and_edit_multiple_roles_and_workstreams_independently_of_designation`

All accounts are test identities; generated `PROOF_*` role codes are represented by
their bundles. A dash means no designation assigned. PASS/FAIL evaluates the stated
requirement, not whether a baseline-characterization test passes.

| User | Role | Designation | Permission | Scope | Expected API result | Actual result | PASS/FAIL | Evidence |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Anonymous | None | — | None | None | GET `/api/admin/users`: 401 | 401 | PASS | P1 |
| Anonymous | None | — | None | None | POST `/api/admin/users`: 401 | 401 | PASS | P1 |
| Unprivileged officer | None | ADM | None | LAND_RECORDS membership | GET `/api/admin/users`: 403 | 403 | PASS | P2 |
| Unprivileged officer | None | ADM | None | LAND_RECORDS membership | GET village khatauni: 403 | 403 | PASS | P2 |
| Unprivileged officer | None | ADM | None | LAND_RECORDS membership | POST `/api/admin/roles`: 403 | 403 | PASS | P2 |
| LR reader | LR read bundle | PATWARI | LR.View | All | GET village khatauni: 200 | 200 | PASS | P3 |
| LR reader | LR read bundle | DEO | LR.View | All | GET village khatauni: 200 | 200 | PASS | P3 |
| LR reader | LR read bundle | ADM | LR.View | All | GET village khatauni: 200 | 200 | PASS | P3 |
| LR readers | LR read bundle | PATWARI / DEO / ADM | No LR.Edit | All read only | Direct POST `/api/khatauni`: 403 | 403 for all three | PASS | P3 |
| LR officer | Scoped LR read bundle | — | LR.View | Workstream, live LAND_RECORDS | GET village khatauni: 200 | 200 | PASS | P4 |
| Same LR officer, existing cookie | Scoped LR read bundle | — | LR.View | Workstream membership removed | GET village khatauni: 403 | 403 | PASS | P4 |
| Same LR officer, existing cookie | Role removed | — | None | LAND_RECORDS restored | GET village khatauni: 403 | 403 | PASS | P4 |
| Multi-work officer | LR + Award scoped bundles | PATWARI | LR.View + Award.View | LAND_RECORDS + AWARD | Create with two roles/two workstreams; GET both work resources 200 | 201 create; both GETs 200; `/auth/me` reports both | PASS | P10 |
| Same multi-work officer after edit | LR bundle only | PATWARI unchanged | LR.View | LAND_RECORDS only | PUT removes Award role/work; LR GET 200, Award GET 403 | 200 / 200 / 403; designation unchanged | PASS | P10 |
| Account manager | Account management bundle | — | Users.Manage | All | GET `/api/admin/users`: 200 | 200 | PASS | P5 |
| Disabled account manager, existing cookie | Account management bundle | — | Users.Manage | All | GET `/api/admin/users`: denied | 403 | PASS | P5 |
| Disabled account manager | Account management bundle | — | Users.Manage | All | POST login: 401 | 401 | PASS | P5 |
| Account manager | Users.Manage only | — | No Access.Manage | All | GET `/api/admin/roles`: 403 | 403 | PASS | P6 |
| Same account manager | Users.Manage only | — | Users.Manage, no role-assignment authority | All | PUT own account assigning SYSTEM_ADMIN: deny | 200; subsequent GET roles 200 | **FAIL** | P6; privilege separation missing |
| Reset account, old cookie | Account management bundle | — | Users.Manage | All | GET admin users after reset: stale session denied | 200 | **FAIL** | P7; session invalidation missing |
| Reset account, fresh login | Account management bundle | — | Login | N/A | Old password login 401 / new password 200 | 401 / 200 | PASS | P7 |
| New officer | None | — | Login | N/A | Password stored as verifiable hash; no plaintext | Hash verification succeeds | PASS | P8; database assertion |
| New officer/admin caller | None / SYSTEM_ADMIN | — / ADM | Self identity / Users.Manage | Own / All | `/auth/me`, user list/detail contain no reusable credential/hash | No password or hash exposed | PASS | P8 |
| Admin creating officer | SYSTEM_ADMIN | ADM | Users.Manage | All | User audit excludes credential material | PasswordHash copied into creation audit JSON; no plaintext | **FAIL** | P8; credential audit redaction target |
| Area-scoped LR officer (desired model) | Scoped LR read bundle | — | LR.View | Existing LAND_RECORDS, geography unavailable | Express home-village restriction and deny unallocated area | Both fictional villages return 200; no area scope input exists | **FAIL** | P9; missing allocation model, not an existing configured-area bypass |
| LR scoped officer | WS_ROLE_* | — | LR.View | Workstream LAND_RECORDS | GET village khatauni: 200 | 200 | PASS | Existing RbacTests `Scoped_workstream_user_access_is_enforced_by_business_function` |
| Same LR scoped officer | WS_ROLE_* | — | Award.View | Workstream LAND_RECORDS | GET Award-classified award: 403 | 403 | PASS | Same existing test |
| Same LR scoped officer | WS_ROLE_* | — | Award.View | Workstream LAND_RECORDS | GET Possession-classified award events: 403 | 403 | PASS | Same existing test |
| Same LR scoped officer | WS_ROLE_* | — | Matter.View | LAND_RECORDS, unavailable record | GET nonexistent matter: fail closed | 403 | PASS | Same existing test; not proof of matching-matter success |
| Assigned/Own LR readers | ASSIGNED_ROLE_* / OWN_ROLE_* | — | LR.View | Assigned / Own; no resource context | GET village khatauni: 403 | 403 / 403 | PASS | Existing RbacTests `ScopeMode_Assigned_and_Own_fail_closed` |
| Dak desk A member | ROLE_AUTH13 | — | Dak.View | Assigned desk A | GET desk A Dak content: 200 | 200 | PASS | Existing DakTests Group13 |
| Dak desk B member | ROLE_UNAUTH13 | — | Dak.View | Assigned desk B | Direct GET desk A Dak content: 403 | 403 | PASS | Existing DakTests Group13 |
| Outward desk A member | R_DESK | — | Outward.View | Assigned desk A | GET own desk outward 200 / other desk 403 | 200 / 403 | PASS | Existing OutwardTests Test02 |
| Outward workstream 1 member | R_WS | — | Outward.View | Workstream 1 | GET workstream 1 outward 200 / workstream 2 403 | 200 / 403 | PASS | Existing OutwardTests Test03 |
| User A | ROLE_WS_A | — | WorkItem.View | Workstream A | GET item A 200 / item B 403 | 200 / 403 | PASS | Existing WorkItemTests `WorkItem_Scope_All_vs_Workstream_vs_Assigned_AccessControl` |
| User B | ROLE_ASSIGNED_B | — | WorkItem.View | Assigned desk B | GET item B 200 / item A 403 | 200 / 403 | PASS | Same WorkItemTests test |
| Matter view-only user | View Only | — | Matter.View, no document management | All | GET eligible-documents: 403 | 403 | PASS | Existing MatterAuthorizationAndWorkspaceTests `EligibleDocuments_Requires_MatterDocumentManage_ApiRegression`; test authentication handler |
| Matter document manager | Doc Manage | — | Matter.View + Matter.Document.Manage | All | GET eligible-documents: 200 | 200 | PASS | Same existing test; test authentication handler |

### Requirements not implementable in the current baseline

These are source/schema findings, **not fabricated HTTP test results**.

| User | Role | Designation | Permission | Scope | Expected result | Actual baseline capability | PASS/FAIL |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Authorized senior administration | Explicit access administration | Any | Catalog administration | Administrative | Create/edit Work separately from Role | Roles CRUD exists; Workstreams GET only | **FAIL** |
| Authorized senior administration | Assignment administration | Any | Account + bounded role/allocation assignment | Multiple works/areas | Create/edit roles, works and scopes | Multiple role/workstream API inputs exist; no allocation scopes; UI edit absent | **FAIL** (partial foundation exists) |
| Patwari | Appropriate permission bundle | PATWARI unchanged | Delegated LR/work actions | Bijwasan/Dwarka + temporary Najafgarh | Grant, expire and revoke temporary cross-area work | No typed geographic/time-bound allocation schema/API | **FAIL** |
| Officer managing own DEOs | Assistant management authority | Any officer designation | Bounded assistant creation/edit/revoke | Own supervised accounts | Multiple individual DEO logins under officer | Ordinary users only; no supervisor relationship | **FAIL** |
| DEO X | Bounded delegated grants | DEO | Subset of supervisor Y | Subset of current parent grants | Parent removal/expiry immediately denies child access | No delegation-grant evaluator | **FAIL** |
| DEO X acting for Y | Bounded delegated grants | DEO | Authorized operation | Parent allocation scope | Audit both actor and officer, render on-behalf-of label | Audit identifies logged-in user only | **FAIL** |
| New/reset officer or DEO | Any | Any | Credential replacement | Temporary validity | Expire temporary credential and require replacement | Hash/reset exists; no temporary expiry/mandatory change | **FAIL** |
