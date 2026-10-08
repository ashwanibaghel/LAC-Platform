# Office account model V3

Branch: `codex/rbac-office-access-v3`.
Exact base: `c17a9ae5694257fee80959e9650ce122f63950fc`.
This is a backend account/authorization change. The existing frontend remains;
its next account workflow should use the contracts below. No Dak, Matter, Court,
Land ingestion, Compensation or ONLYOFFICE workflow is redesigned.

## Persisted authority and hierarchy

Designation != Authority != Module Access != Desk != Delegated Work.
Authority is resolved from current active reserved role memberships, never from
a designation, display name, username, client claim or module checkbox.

| Authority | Grants and management ceiling |
| --- | --- |
| SYSTEM_ADMIN | Existing reserved role, all current granular permissions; manages every level and advanced raw security configuration; designation may be null |
| OFFICE_ADMIN | All operational permission families; Users.Manage, Roles.Assign, Allocations.Manage, Assistants.Manage, Audit.View, WorkCatalog.Manage, OfficeConfiguration.Manage; manages supervisors, ordinary officers and their helpers |
| OFFICE_SUPERVISOR | All operational permission families; Users.Manage, Roles.Assign, Allocations.Manage, Assistants.Manage, Audit.View; manages ordinary officers and their helpers |
| STANDARD_OFFICER | Server-owned module roles plus Assistants.Manage; own account read/password change and bounded own helpers |
| HELPER | Exactly one supervising officer; operational role source intersected with AssistantPermissionLimits and the live parent ceiling; own account read/password change only |

Neither office authority has Access.Manage. OfficeConfiguration.Manage is a new,
separate permission for operational desk configuration. OFFICE_ADMIN can use desk
create/update/toggle routes; OFFICE_SUPERVISOR cannot change desk configuration.
Both can assign existing desks to eligible lower accounts.

Only SYSTEM_ADMIN grants SYSTEM_ADMIN or OFFICE_ADMIN. SYSTEM_ADMIN and OFFICE_ADMIN
grant/revoke OFFICE_SUPERVISOR. OFFICE_ADMIN cannot manage another OFFICE_ADMIN,
itself through administration routes, SYSTEM_ADMIN, or their protected helpers.
OFFICE_SUPERVISOR cannot manage peer supervisors or higher identities.
Use self-service for password changes, not an administrative self-edit.

Dormant reserved memberships still protect targets against credential takeover.
Legacy Access.Manage-bearing accounts also remain protected against takeover by
office administrators. Their granular grants do not make them SYSTEM_ADMIN.

The hierarchy gate covers legacy `/api/admin/users` writes, role assignment,
credential resets, activation, desk memberships and allocations. Crafted RoleIds
cannot bypass it. Raw `/api/admin/roles` and `/api/admin/permissions` require actual
SYSTEM_ADMIN authority as well as their existing permission gate. Office authority
and preset role codes/bundles are server-owned and cannot be forged or edited.
SYSTEM_ADMIN retains its existing protected full-bundle configuration contract.

PostgreSQL account mutations use an advisory transaction lock with checks and saves
in the same transaction. Concurrent changes cannot both pass a stale last-active-
SYSTEM_ADMIN or hierarchy check. Business workflow locking is unchanged.

## Controlled ADM creation

When SYSTEM_ADMIN posts a new simplified account with the canonical ADM designation,
the transaction explicitly assigns OFFICE_ADMIN and returns that authority. A
technical SYSTEM_ADMIN request with ADM is rejected: these are separate identities.
Changing an existing account's designation to ADM never promotes it. Existing ADM
officers receive no automatic authority during seed/upgrade.
The narrowly guarded legacy BootstrapAdminId normalization remains unchanged.

## Simplified account API

JSON enum values are strings; GUIDs are UUIDs. Unsupported properties in the new
write DTOs return 400, including raw RoleIds, WorkstreamIds or passwords.

```typescript
type Authority = "STANDARD_OFFICER" | "OFFICE_SUPERVISOR" | "OFFICE_ADMIN" | "SYSTEM_ADMIN";
type Module = "DakMatters" | "Court" | "Rti" | "Accounts" | "RecordRoom";
type LandAccess = "None" | "ViewOnly" | "ViewWrite";
type Account = {
  fullName: string;
  designationId: string | null;
  customDesignation: string | null;
  authority: Authority;
  modules: Module[];
  canRegisterInwardDak: boolean;
  landAccess: LandAccess;
  deskIds: string[];
};
```

| Route | Contract |
| --- | --- |
| GET `/api/office/accounts/options` | Manager-only options: current authority, grantable authority names, canAssignOfficeSupervisor/OfficeAdmin/SystemAdmin flags, modules, Land choices, active standard designations and desks |
| GET `/api/office/accounts` | Manager-only account directory |
| GET `/api/office/accounts/{id}` | Manager-only account detail |
| POST `/api/office/accounts` | `{username, account: Account}`; returns 201 `{account, temporaryCredential, credentialExpiresAt}` |
| PUT `/api/office/accounts/{id}` | `{account: Account, expectedRevision}`; authorized full replacement of account access; returns current detail; stale revision is 409 |
| POST `/api/office/accounts/{id}/toggle-status` | `{expectedRevision}`; retains identity/history; hierarchy and last-admin protection apply |
| POST `/api/office/accounts/{id}/reset-credential` | `{expectedRevision}`; fresh generated temporary credential, 24-hour expiry; inactive targets rejected |
| GET `/api/office/me` | Own official account/profile; available to officers and helpers after mandatory password replacement |
| POST `/api/auth/change-password` | Existing `{currentPassword,newPassword}` contract; requires another login after success |

Detail contains id, username, fullName, designationId, customDesignation,
effectiveDesignation, authority, officeAccessManaged, isActive, landAccess,
canRegisterInwardDak, supervisingOfficerId, assistantRevision, revision, module
names, deskIds, and helperPermissionCodes where applicable. It never returns a
password hash, reusable password or session/security stamp. Revision is a normal
concurrency marker. There is no self-service identity/access PUT and no user delete.

Selecting a supervisor/head/system authority automatically supplies its operational
bundle and responsibilities; module choices are not needed for those accounts.
For ordinary accounts, the server maps presets to roles, internal workstreams and
explicit responsibility allocations. No officer names or today's work order are
hard-coded. Preset-generated responsibility orders are identified as OFFICE-ACCESS-V3
or OFFICE-AUTHORITY-V3; work allocation remains distinct from role permission.
Custody and business rules still govern the operation after these gates.

## Exact module permission mapping

Every normal officer also receives the server-owned STAFF role containing only
Assistants.Manage. Permission codes below are internal implementation details;
the normal account form selects module names.

**DakMatters — Assigned scope for every grant:**

```text
Dak.View
Dak.Edit
Dak.Move
Dak.Receive
Dak.PullBack
Dak.Resolve
Matter.View
Matter.Create
Matter.Edit
Matter.Document.Manage
Matter.Archive
Draft.View
Draft.Create
Draft.Edit
Outward.View
Outward.Create
Outward.Edit
Outward.Dispatch
Outward.Cancel
WorkItem.View
WorkItem.Create
WorkItem.Update
WorkItem.Contribute
WorkItem.Complete
Schedule.View
Schedule.Create
Schedule.Update
Schedule.Complete
```

Internal workstreams: DAK_CORRESPONDENCE and DRAFTING_NOTING. Existing Assigned
checks remain authoritative; this does not implement future automatic Matter
access for the currently marked Dak officer. No Register/Mark, Reopen, Dispose,
Cancel, WorkItem.Assign/Review/Cancel or Schedule.Assign/Cancel is implicitly added.

**Court — Workstream scope:** Court.View, Court.Create, Court.Edit, Court.Assign,
Court.Proceeding.Manage and Court.Document.Manage. Internal workstream:
COURT_REFERENCES. No Court business logic is changed.

**Rti:** persisted membership and RTI workstream; no fabricated operational grants.

**Accounts:** persisted membership and ACCOUNTS_COMPENSATION workstream; no
fabricated operational grants. Existing land/Award APIs keep their existing gates.

**RecordRoom — Assigned scope:** Dak.View, Dak.Receive, Dak.Move, Dak.PullBack,
Dak.Resolve. Workstreams: RECORD_ROOM and DAK_CORRESPONDENCE. Receipt still requires
the nominated receiver, appropriate desk membership and the physical/custody rules.

**Can Register Inward Dak — separate Workstream-scoped role:** Dak.View,
Dak.Register, Dak.Mark. Workstream: DAK_CORRESPONDENCE. DakMatters alone never
supplies registry authority. Initial mark remains the existing custody transaction.

## Exact Land mapping

None: no land role or land permissions.

ViewOnly, all scoped All:

```text
Village.View
Khasra.View
LR.View
Award.View
```

ViewWrite adds these All-scoped mutations to all four read grants:

```text
Khasra.Edit
LR.Edit
LR.Verify
LR.Commit
Award.Create
Award.Edit
Award.CoreDocument.Upload
```

Inventory: existing village/khasra/LR and ownership reads; notifications/projects;
Award official-record/candidate/core-document, NM, LR migration/verification/commit,
khatauni/share and reviewed core-intake boundaries use these same granular codes.
Award's existing claims/apportionment paths use Award.View/Award.Edit; their original
validation/workflow remains. Internal memberships/responsibilities cover LAND_RECORDS,
AWARD, LAND_ACQUISITION, POSSESSION and ACCOUNTS_COMPENSATION so permitted mutations
can reach the existing validators. Geographic View stays independent of allocation
walls; operational allocation and workflow checks remain in place.
Land access is independent of the main module. Land/Award grants do not imply Court
grants: legacy Award-to-Court permission backfill skips the new server-owned presets.

## Helper contract and ceiling

```typescript
type Helper = {
  fullName: string;
  designationId: string | null;  // canonical DEO only
  customDesignation: string | null;
  deskId: string | null;        // one of the parent's current active desks
  access: "None" | "ReadOnly" | "ReadWrite";
  permissionCodes?: string[];   // optional further restriction; outside parent = 403
  allocations?: AllocationInput[]; // optional narrower scopes/time; parent IDs required
};
```

| Route | Contract |
| --- | --- |
| GET `/api/office/me/helpers/options` | Canonical DEO, own active desks and simple access choices |
| GET `/api/office/me/helpers` | Own attached helpers |
| POST `/api/office/me/helpers` | `{username,helper: Helper}`; creation response includes one-time temporary credential |
| PUT `/api/office/me/helpers/{id}` | `{helper: Helper,expectedRevision}` using assistantRevision |
| POST `/api/office/me/helpers/{id}/reset-credential` | `{expectedRevision}` using account revision |
| POST `/api/office/me/helpers/{id}/toggle-status` | `{expectedRevision}` using account revision |
| POST `/api/office/accounts/{officerId}/helpers` | Same creation shape, for authorized senior management of a lower officer |
| PUT `/api/office/accounts/helpers/{helperId}` | Same bounded edit, for authorized senior management |

Helpers cannot use any of the administration/helper-management routes. One active
officer is required. A helper cannot be reassigned to another parent through these
contracts. None grants no operational permission. ReadOnly selects the parent's
current operational `.View` codes. ReadWrite selects current operational codes;
optional permissionCodes can only narrow them. Administration codes are excluded.
The persisted limits do not auto-expand if a parent later gains new permissions.

The helper's role is only a permission source. AccessControlService also checks
the live parent's permission at the actual resource/scope. WorkAllocation requires
matching, active parent responsibility, geography and dates; desk actions require
current parent desk authority too. Explicit broader permissions, foreign desks,
Global over bounded geography, or longer intervals fail with 403. Without explicit
child allocations, the current parent's allocations are copied as bounded delegated
records referencing their parents. Parent revocation still narrows effective access
immediately. Legacy assistant writes also enforce role-scope and desk ceilings.
Audit remains `DEO X on behalf of Officer Y`, with both IDs and snapshots.
Login/current-user responses report the parent/child scope intersection rather
than advertising All from the helper's underlying bundle. When specifying child
dates, use the parent's returned saved timestamps (PostgreSQL stores microseconds).

## Designation and migration

The existing seven civil designations remain; added canonical codes are TEHSILDAR,
ASO, SENIOR_ASSISTANT, JUNIOR_ASSISTANT and PA_ADM. Custom text is stored only on
AppUser, normalized with FormKC and trimming, maximum 200 characters, with control
characters rejected. Standard and custom designation are mutually exclusive.
Custom titles never create a global Designation row or grant authority.
Legacy auth/directory/detail DTOs render a custom title as the display designation
with code CUSTOM and empty UUID; actual designationId stays null. CurrentUser also
returns customDesignation and authority. The existing header/profile can render
the returned name; technical SYSTEM_ADMIN nullable-designation behavior remains.

Migration `20261008194507_AddOfficeAccountAuthorityV3` adds:

- AppUsers.CustomDesignation: nullable varchar(200).
- AppUsers.OfficeAccessManaged: boolean, existing rows false.
- AppUsers.OfficeRevision: integer concurrency token, existing rows zero.
- AppUsers.LandAccess: text enum, existing rows None.
- AppUsers.CanRegisterInwardDak: boolean, existing rows false.
- OfficeModuleMemberships: UUID PK, UserId FK (Restrict), Module text, existing
  OfficialRecord status/timestamps/actor fields; unique active UserId + Module.
- CK_AppUser_DesignationChoice: custom text requires null DesignationId and nonblank
  length <= 200. Old rows remain valid. Existing roles/assignments are not deleted.

New roles/presets are seeded idempotently without assigning them to legacy users.
Reserved code collision with an existing custom role fails closed. Granular roles
and Workstreams remain. Legacy account access continues until an authorized explicit
V3 update replaces its role/workstream selections; unmanaged legacy assignments and
custody history are preserved. Legacy managers need an explicit office authority
grant: designation and Users.Manage alone no longer permit account mutations.
An advanced raw role/workstream replacement marks the simple selections unmanaged
and archives their module-selection metadata, so the profile cannot claim those
checkboxes still describe custom granular grants.

## Session and audit policy

Authority, module, Land, role, workstream, allocation and desk-membership changes
rotate the target's SessionVersion. Parent changes rotate attached helper stamps
too. Helper ceilings, deactivation and credential resets invalidate existing
cookies. Role configuration changes invalidate role members; desk configuration
changes invalidate members. A stale cookie returns 401 and the client must log in.
Underlying authorization also continues to read the current database state.

The generated credential lasts 24 hours, is stored only as a secure password hash,
and is returned only by create/reset. Mandatory first-login replacement and expiry
rejection remain. Responses carrying credentials are no-store. Authority/module/
designation/desk changes produce actor/target before-after audit records; password
hashes, credentials and security stamps are explicitly redacted.

## Verification

The final source build passed with 0 errors and 3 existing xUnit2031 warnings in
unchanged AwardExtractionRuleEngineTests. Full backend verification on that build
completed in 28.69 minutes: 1,293 passed, 0 failed, 0 skipped. Source/test SHA-256
checksums captured at run start still matched after completion (33 changed files).

| Final-build group | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Full backend, including PostgreSQL | 1,293 | 0 | 0 |
| Normal backend | 1,253 | 0 | 0 |
| Dak PostgreSQL | 20 | 0 | 0 |
| Existing RBAC PostgreSQL | 3 | 0 | 0 |
| V3 PostgreSQL | 17 | 0 | 0 |
| Focused RBAC/account groups, subset of full run | 69 | 0 | 0 |
| Assistant foundation, subset of full run | 15 | 0 | 0 |

The earlier standalone focused run passed 67/67; the final full run includes those
groups plus the two subsequent account/scope proofs. PostgreSQL RBAC totals are
20/20 (3 existing + 17 V3). Every PostgreSQL proof used the dedicated disposable
loopback cluster on port 55438, with isolated databases. All test databases and the
schema-only template were removed by the fixtures. No office database was used.

Final backend command (both disposable PostgreSQL environment variables set):

```text
dotnet test tests/LAC.Tests/LAC.Tests.csproj --no-build --no-restore
  --logger trx;LogFileName=office-v3-complete-verified.trx
  --logger console;verbosity=normal --results-directory tmp/rbac-proof
```

Frontend build passed; all 231 frontend tests passed with 0 failures/skips.
Frontend source is unchanged. Existing Vite large-chunk/plugin timing notices
remain. EF reports no pending model changes; all 53 migrations were validated by
the PostgreSQL schema fixture, including upgrade from the immediate predecessor
while preserving existing users, password hashes and non-elevated ADM authority.
New designation and active-module constraints, reserved-role seeding, concurrent
last-admin protection and helper/parent revocation were proved on PostgreSQL.

Intermediate failures from session-invalidated test cookies and synthetic
sub-microsecond allocation timestamps were corrected in the affected test fixtures;
security rules were retained. There are no remaining failures requiring reproduction on the base. No unrelated business implementation was changed.
`git diff --check` passed. No merge or deployment was performed.

## Changed-file inventory

```text
M	docs/anti-rbac-frontend-contract.md
A	docs/rbac-office-access-v3-contract.md
M	src/LAC.Api/AccountSecurity.cs
A	src/LAC.Api/OfficeAccountEndpoints.cs
A	src/LAC.Api/OfficeAccountService.cs
A	src/LAC.Api/OfficeHierarchyFilter.cs
A	src/LAC.Api/OfficeSecurityTransaction.cs
M	src/LAC.Api/OfficerAssistantEndpoints.cs
M	src/LAC.Api/OperationalAuthorizationFilter.cs
M	src/LAC.Api/Program.cs
M	src/LAC.Api/RbacEndpoints.cs
M	src/LAC.Api/WorkAllocationEndpoints.cs
M	src/LAC.Domain/IdentityEntities.cs
A	src/LAC.Domain/OfficeAccountModel.cs
M	src/LAC.Domain/PermissionCodes.cs
M	src/LAC.Infrastructure/AccessControlService.cs
M	src/LAC.Infrastructure/LacDbContext.cs
A	src/LAC.Infrastructure/Migrations/20261008194507_AddOfficeAccountAuthorityV3.Designer.cs
A	src/LAC.Infrastructure/Migrations/20261008194507_AddOfficeAccountAuthorityV3.cs
M	src/LAC.Infrastructure/Migrations/LacDbContextModelSnapshot.cs
A	src/LAC.Infrastructure/OfficeAccessPresets.cs
A	src/LAC.Infrastructure/OfficeAccountConfiguration.cs
A	src/LAC.Infrastructure/OfficeAuthorityResponsibilities.cs
A	src/LAC.Infrastructure/OfficeAuthorityService.cs
A	src/LAC.Infrastructure/OfficeSessionSecurity.cs
M	src/LAC.Infrastructure/SeedData.cs
M	tests/LAC.Tests/AccountOptionsContractTests.cs
M	tests/LAC.Tests/BootstrapAdminIdentityTests.cs
M	tests/LAC.Tests/DakCustodyApiTests.cs
M	tests/LAC.Tests/DynamicAllocationApiTests.cs
M	tests/LAC.Tests/MyDeskTests.cs
A	tests/LAC.Tests/OfficeAccountV3PostgresTests.cs
A	tests/LAC.Tests/OfficeAccountV3Tests.cs
M	tests/LAC.Tests/RbacBaselineProofTests.cs
M	tests/LAC.Tests/RbacPostgreSqlTests.cs
```
