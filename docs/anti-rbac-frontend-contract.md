# Anti RBAC frontend contract

Backend foundation for branch `codex/rbac-dynamic-allocation-audit`, based exclusively
on `d44941636d54316350e8d6ee2dc03628281baace`. No frontend redesign is included.
The following contracts are implemented; UI permissions are hints and the server
remains authoritative on every request.

## Concepts and authorization

Designation is a civil post. Role is a permission bundle. WorkDefinition is a
reusable responsibility category. WorkAllocation assigns that responsibility to a
user with geographic scopes, dates and a work-order reference. Desk remains a
custody/operational seat. One-off tasks use `/api/work-items`, not Create Work.

Officer View follows existing role permission/scope rules and can remain broad
without a geographic allocation. Operational mutations require matching current
allocations plus the existing workflow/custody rules. Creating an allocation never
grants an absent role permission. Assistant access also intersects its selected
permission ceiling and allocations with the supervising officer's current state.
Assistants cannot administer security or create additional assistants.

New permissions:

| Code | Meaning |
| --- | --- |
| `Roles.Assign` | Assign non-administrative role bundles within the caller's permission ceiling; reserved SYSTEM_ADMIN is excluded |
| `Allocations.Manage` | Assign/revoke work/geographic allocations and desk/workstream memberships |
| `WorkCatalog.Manage` | Create/update reusable responsibility categories |
| `Assistants.Manage` | Create/edit/reset/revoke the caller's own supervised assistants |

Existing `Users.Manage` maintains accounts and profiles. Supplying changed RoleIds
also requires Roles.Assign or Access.Manage. Access.Manage is full security-role
administration. Resetting another operational account additionally requires
Roles.Assign plus Allocations.Manage and a matching permission ceiling, or
Access.Manage. Administration accounts, including dormant privileged memberships,
require Access.Manage for another caller's reset. Equal role codes with different geographic allocations do not authorize
Users.Manage-only takeover. Self-reset and an officer's own assistant reset use
their respective authorized endpoints. Users.Manage alone does not grant allocation
or desk assignment. New powers are not automatically given to existing ordinary
roles; configure senior administration explicitly. No civil designation grants power.

## Shared shapes

JSON enums use the names below; GUIDs use canonical UUID strings. Dates are ISO
8601 offsets, stored as UTC. Render dates in the office/client timezone; do not send
a local date without its offset. `ValidFrom` is inclusive and `ValidTo` exclusive.

```typescript
type ScopeKind = "Global" | "District" | "Subdivision" | "Village";
type WorkKind = "LandAcquisition" | "Award" | "LandRecords" | "Nm" | "Enm"
  | "Possession" | "Accounts" | "Compensation" | "StatementA" | "Court"
  | "Rti" | "Correspondence" | "General";
type AllocationScope = {
  kind: ScopeKind;                 // required: no implicit Global
  districtId?: string | null;
  subDivisionId?: string | null;
  villageId?: string | null;
};
type AllocationInput = {
  workDefinitionId: string;
  validFrom: string;
  validTo: string | null;
  workOrderReference: string;
  reason: string | null;
  scopes: AllocationScope[];      // 1..100 explicit targets, union within allocation
  delegatedFromAllocationId?: string | null; // required for assistant grants
};
type Allocation = AllocationInput & {
  id: string; userId: string; workCode: string; workName: string;
  revision: number; revokedAt: string | null; revokedByUserId: string | null;
};
type IdRevision = { id: string; revision: number };
```

Global has no target IDs. District has only DistrictId; Subdivision only
SubDivisionId; Village only VillageId. Each target must exist and be active.
Different allocations form a union of responsibilities. A complete matching
allocation must cover the action's work and geography. Role permissions and custody
remain additional constraints; independent works/scopes do not invent permissions.

WorkKind binds a responsibility to an existing operational family. It does not
create a permission or a new application module. ENM, RTI, Accounts and Statement-A
catalog entries are available for allocation even where a dedicated action API is
not yet present. Code, Kind and WorkstreamId are immutable after creation; edit
labels/status or create a replacement category. Allocations can be edited freely
within their validation/revision rules. Revocation retains history.

## Catalog and assignment options

| Method/path | Request | Response | Authority |
| --- | --- | --- | --- |
| GET `/api/admin/account-options` | None | `{canAssignRoles, canManageAllocations, designations:[{id,code,name}], roles:[{id,code,name}], works:[{id,code,name,kind,workstreamId}], districts:[{id,name}], subdivisions:[{id,name,districtId}], villages:[{id,name,subDivisionId}]}` | Account/assignment/catalog/assistant administration; roles restricted to assignable bundles |
| GET `/api/admin/works` | None | `[{id,code,name,description,kind,workstreamId,isActive,revision}]` | Same option-read authority |
| POST `/api/admin/works` | `{code,name,description,kind,workstreamId}` | 201 `IdRevision` | WorkCatalog.Manage |
| PUT `/api/admin/works/{id}` | `{name,description,isActive,expectedRevision}` | 200 `IdRevision` | WorkCatalog.Manage |

Create Role continues to use `/api/admin/roles` and Access.Manage. Role permission
inputs remain `{permissionCode, scopeMode}` with `All`, `Workstream`, `Assigned`,
`Own`. Workstream is existing functional classification, distinct from the catalog.
When retaining a legacy Workstream-scoped role, assign its matching WorkstreamIds
as well; new allocations do not silently change legacy role/workstream membership.
Normal broad View bundles may use All while allocations narrow their action authority.

## Officer account create/edit

Existing endpoints retain their routes and add optional `allocations`:

```typescript
type CreateOfficer = {
  username: string; displayName: string; password?: string | null;
  designationId: string | null; roleIds?: string[] | null;
  workstreamIds?: string[] | null; primaryWorkstreamId?: string | null;
  allocations?: AllocationInput[] | null;
};
type EditOfficer = Omit<CreateOfficer, "username" | "password">;
```

POST `/api/admin/users` requires Users.Manage. Nonempty RoleIds additionally require
role assignment authority, including the same scope mode or All in the assigner's
existing grants (Own, Assigned and Workstream are incomparable). Nonempty WorkstreamIds and any allocations input require
Allocations.Manage. Create is atomic: invalid allocation/role/scope leaves no account.
The response is `{id, temporaryCredential, credentialExpiresAt}` (201).
Omit password to receive a generated 24-hour temporary credential once. The legacy
supplied-password create flow remains supported and stores only its hash.

PUT `/api/admin/users/{id}` requires Users.Manage and returns `{id}` (200).
Omitted/null RoleIds or WorkstreamIds preserve their sets; empty arrays remove them.
An omitted/null `allocations` preserves allocations. A supplied array revokes all
current allocations and creates the new set atomically; `[]` revokes all. For
single allocation edits, prefer the allocation endpoints below to preserve its ID
and delegated children. Replacing an officer allocation revokes its child grants
through the live parent relationship; it does not copy children to the new allocation.
Designation remains independent of every allocation. Preserving/changing a primary
desk does not grant permissions or invent workflow custody.

## Allocation endpoints

| Method/path | Request | Response |
| --- | --- | --- |
| GET `/api/admin/users/{userId}/allocations` | None | `Allocation[]`, including history |
| POST `/api/admin/users/{userId}/allocations` | `AllocationInput` | 201 `IdRevision` |
| PUT `/api/admin/users/{userId}/allocations/{id}` | `{allocation: AllocationInput, expectedRevision}` | 200 `IdRevision` |
| POST `/api/admin/users/{userId}/allocations/{id}/revoke` | `{expectedRevision}` | 200 `IdRevision` |
| GET `/api/auth/allocations` | None | Own declared allocation/history list |

Admin allocation endpoints require Allocations.Manage. Revisions are mandatory for
updates/revocations; omitted/null/stale revisions fail with 409. A revoked allocation
cannot be edited to resurrect it. Changing WorkDefinitionId requires revoke/create.
Scopes and dates remain editable. Lists describe declared history, not an authorization
guarantee; source work, parent validity, role and custody are checked on the server.

## Assistant / DEO endpoints

All management endpoints require Assistants.Manage and operate only on the logged-in
officer's owned assistants. Another officer receives 404. One assistant has one
supervising officer; many assistants may belong to the same officer. Re-parenting is
not exposed. Assistant logins cannot access admin or assistant-management endpoints.

```typescript
type AssistantInput = {
  displayName: string; designationId: string | null;
  roleIds: string[]; permissionCodes: string[];
  allocations: AllocationInput[]; deskIds: string[];
};
// permissionCodes is a restrictive delegation ceiling, not another grant source.
// Every code must exist in the chosen roles AND current supervisor roles.
// Administrative permissions and reserved system roles cannot be delegated.
```

| Method/path | Request | Response |
| --- | --- | --- |
| GET `/api/officers/me/assistants` | None | `[{id,username,displayName,designationId,isActive,supervisingOfficerId,assistantRevision,mustChangePassword}]` |
| GET `/api/officers/me/assistants/delegation-options` | None | `{allocations,roles:[{id,code,name,permissions:[{code,scopeMode}]}],permissions:[{code,scopeMode}],desks:[{id,code,name}],serverTime}` |
| GET `/api/officers/me/assistants/{id}` | None | `{id,username,displayName,designationId,isActive,supervisingOfficerId,assistantRevision,roleIds,permissionCodes,allocations,deskIds}` |
| POST `/api/officers/me/assistants` | `{username,assistant:AssistantInput}` | 201 `{id,assistantRevision,temporaryCredential,credentialExpiresAt}` |
| PUT `/api/officers/me/assistants/{id}` | `{assistant:AssistantInput,expectedRevision}` | 200 `{id,assistantRevision}` |
| POST `/api/officers/me/assistants/{id}/reset-credential` | `{expectedRevision}` | 200 `{id,assistantRevision,temporaryCredential,credentialExpiresAt}` |
| POST `/api/officers/me/assistants/{id}/revoke` | `{expectedRevision}` | 200 `{id,assistantRevision}` |

Each assistant allocation must reference a CURRENT officer allocation with the
same WorkDefinitionId, a contained interval and contained geographic scope.
DeskIds must be a subset of the officer's live desk memberships. Future/unbounded
child dates cannot outlive a bounded parent. Parent work/role/scope revocation or
disablement is effective on the next request, without assistant re-login. Reset
invalidates every old assistant session. Revocation disables the account and revokes
its allocations; an account status toggle does not restore those allocations.
Assistant edit replaces its bounded grant sets and retains history; revocation is
separate from editing. Revoked assistants cannot be reactivated through this edit API.

## Credentials, authentication and audit

| Method/path | Request | Response |
| --- | --- | --- |
| POST `/api/auth/login` | `{username,password}` | Existing CurrentUserResponse plus `mustChangePassword`, `temporaryCredentialExpiresAt`, `supervisingOfficerId`; session cookie |
| GET `/api/auth/me` | None | Same current profile/role summary |
| POST `/api/auth/change-password` | `{currentPassword,newPassword}` | 200 `{requiresLogin:true}`, session cleared; new password must differ and be at least 12 chars |
| POST `/api/admin/users/{id}/reset-password` | `{}` to generate; legacy `{newPassword}` accepted | 200 `{message,temporaryCredential,credentialExpiresAt}`; supplied temporary password is not echoed |
| GET `/api/admin/rbac-audit` | Optional `actorUserId,onBehalfOfUserId,page,pageSize` (page starts at 0; size 1..100) | `{total,page,pageSize,items:AuditLog[]}`; Audit.View required |

Credential responses use no-store headers. Show a generated temporary credential
once and discard it from form state after acknowledgement; do not persist it in
localStorage, logs, screenshots or application history. All later account APIs
omit credentials/hashes. Temporary login cannot access operations until password
replacement. Expired temporary credentials fail authentication. Replacement requires
fresh login and invalidates all old sessions. Bootstrap/recovery CLI hash changes
also rotate SessionVersion.

AuditLog now includes `actorUserId`, `onBehalfOfUserId`, `actorDisplayNameSnapshot`,
`onBehalfOfDisplayNameSnapshot`, and `actorLabel` in addition to its prior fields.
Render the server's label, e.g. `DEO X on behalf of Officer Y`. CreatedBy/UpdatedBy
and workflow actor IDs remain the actual assistant's identity. Existing workflow
event tables also store supervisor ID/name columns; existing timeline projections
are unchanged, so use the RBAC audit endpoint for the dual-actor display. Legacy
audit entries may have null snapshots. OldValues/NewValues never contain password,
credential, session-version, security-stamp, secret or token fields.

## Status handling and handoff boundaries

401 means no valid session, including reset/disabled/expired credentials. 403 means
authenticated but missing role, allocation, parent delegation or custody authority.
404 covers unknown records and another officer's assistant. 400 covers invalid
catalog/allocation shapes and workflow validation. 409 covers stale revisions or
duplicate unique identifiers. Preserve the server error message and refresh current
profile/grants after authorization failures. Do not infer action permission from a
visible record or a desk membership.

The legacy `/auth/me.permissions` scope field is a nominal role/UI summary, not a
complete effective action decision. Do not use its rank to flatten custody and
geographic constraints. Assistant area-scoped collection queries without a specific
record context fail closed; direct authorized record routes work. Unknown/ungrouped
action contexts require an explicit global allocation. Additional ENM/RTI/Statement-A
modules and a filtered assistant directory UI are outside this backend foundation.
