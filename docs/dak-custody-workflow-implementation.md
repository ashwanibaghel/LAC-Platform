# Dak acknowledged custody — backend implementation

Branch: `codex/dak-custody-workflow`. Exact base: `d44941636d54316350e8d6ee2dc03628281baace`.

This delivery implements the approved backend workflow. It does not merge or deploy, add audio, add DakRequiredWork/formal WorkItem obligations, or permit supervisory forwarding on behalf of a received holder. Existing WorkItem assignments and context links are unchanged. Diary numbers remain manual, mandatory and globally reserved through every lifecycle and archival state; registration replay, GUID identity, linked-target privacy, Delhi date handling and intake file compensation remain intact.

## Workflow and physical custody

- Initial MARK nominates one active receiving officer at an active desk. Registration itself still creates no received assignment. Desk-only legacy allocation may be explicitly nominated using Mark permission because it has no confirmed officer; a known legacy assignee must personally confirm present-day holding.
- SEND/FORWARD/RETURN of received Dak requires the current confirmed holder, regardless of All scope or designation. There is no Dak.Assign permission or on-behalf forwarding path.
- Dispatch creates an immutable-origin DakTransfer and sets InTransit. The prior received holder remains booked until the destination user personally RECEIVEs. Shared desk membership does not authorize receiving for another officer.
- Physical inclusion is explicit. Dispatch requires confirmed original existence and a recorded sender custodian/desk with current membership. During transit, location fields describe the last recorded/confirmed location, not delivery at the destination. Receive of a physically included dispatch requires `physicalReceiptConfirmed=true`; only then are physical custodian and location updated.
- Sender-only PULL BACK requires a reason and a still-pending transfer. It restores the actual pre-dispatch lifecycle/routing state, including legacy desk-only allocation. Physically dispatched paper becomes ReturnPending until the sender records actual recovery with mandatory provenance. No dispatch, resolution or closure can bypass outstanding delivery/recovery.
- RESOLVE requires current confirmed holder, `completionAttested=true`, nonblank resolution remarks, no pending transfer and no physical return awaiting recovery. It preserves paper/holder facts and removes the Dak from outstanding-work attention.
- REOPEN requires separately granted Dak.Reopen and a reason. Only active Resolved Dak can reopen; archived/inactive, Disposed and Cancelled records cannot. It starts another processing cycle and clears current resolution metadata while keeping the original immutable resolution evidence. It never makes the supervisor the holder.
- Record Room is an explicitly configured OfficeDesk purpose with one nominated active caretaker, using exactly the same send/receive protocol in both directions. Shelf inventory, retention and separate paper-only movement of resolved records remain future scope.

## Endpoint contracts

All new workflow mutations and the `/move` dispatch adapter require one non-empty UUID `Idempotency-Key`. `expectedRevision` is mandatory and nonnegative. Keep the whole body, including revision, unchanged when retrying a lost response. A successful identical actor/key retry returns the original immutable command result even after later events; changed payload under that key returns 409. Registration keeps its previous optional-key behavior.

| Endpoint under `/api/dak` | Body / behavior |
|---|---|
| `POST /{id}/transfers` | `action`: Marked/Forwarded/Returned; `toDeskId`; `toUserId`; `destinationKind`: Officer/RecordRoom; `includesPhysicalOriginal`; `expectedRevision`; optional `remarks`, `instructions`; `remarksKind` defaults to Text. Unsupported remark kinds are rejected. |
| `POST /{id}/transfers/{transferId}/receive` | `expectedRevision`, `physicalReceiptConfirmed` (mandatory true for paper-inclusive dispatch). Only nominated receiver. |
| `POST /{id}/transfers/{transferId}/pull-back` | `reason`, `expectedRevision`. Only sender, before acceptance. |
| `POST /{id}/transfers/{transferId}/confirm-return` | `provenance`, `expectedRevision`. Only sender, after pull-back of paper-inclusive dispatch; actual recovery confirmation. |
| `POST /{id}/resolve` | `completionAttested`, `remarks`, `expectedRevision`. No formal required-work entity or endpoint. |
| `POST /{id}/reopen` | `reason`, `expectedRevision`. Separately authorized supervisor. |
| `POST /{id}/custody/confirm` | `expectedRevision`. Known active legacy assignee personally confirms holding now; never fabricates an old receive date. |
| `GET /{id}/transfers` | Dispatch/receipt/pull-back/recovery facts, nominated identities, remarks and timestamps. |
| `GET /delivery-queue` | `bucket`: incoming/sent/with-me/resolved/attention; optional zero-based `page`, `pageSize` capped at 100. Returns items, totalCount and pagination metadata. |
| `GET /{id}` | Adds routing/physical states, processing cycle, current resolution metadata, pending receiver/transfer and needsAttention. Assignment exposes receivedAt/isConfirmed. |
| `GET /{id}/timeline` | Adds transfer correlation, event version and public before/after custody/location changes plus completion attestation. Internal command payload/request key is not exposed. |
| `GET /{id}/physical-original` | Keeps Phase A fields and adds physical state/recorded custodian context. Observations cannot overwrite acknowledged custodian or clear transit/recovery. |
| `POST /{id}/move` | Existing body retained, but nominated user is mandatory. Dispatch adapter, never implicit RECEIVE. Stable original result on same-key replay, including when initial marker's scope no longer covers the new holder. Room destinations use `/transfers` with explicit kind. |

New command results include commandId, dakId, transferId where relevant, revision, lifecycle/routing/physical state, confirmed holder, processing cycle and recorded physical custodian/location. They are operation receipts; GET detail/queues provide the latest aggregate state.

400: invalid input/key, missing attestation/reason or unsupported kind. 403: denied permission/scope or wrong holder/receiver/sender. 409: stale revision, ended transfer, pending delivery/recovery or invalid transition. Text remarks/reasons/provenance are limited to 1000 characters.

Legacy `/dispose` remains a distinct irreversible Disposed outcome with its existing disposal grant/mandatory justification; now it also requires confirmed holder and settled delivery. It does not produce Resolved or permit reopening. Cancellation retains its existing reason/permission contract and cannot close a pending transfer/recovery. Resolved records reject ordinary intake mutations until supervised reopen.

`POST /api/admin/desks` adds optional purpose General/RecordRoom. `PUT /api/admin/desks/{id}` accepts optional purpose and preserves it when omitted. Existing Access.Manage authorization controls configuration; movement targets expose purpose. No named officers/designations are hard-coded.

## Permissions and migration

Added: `Dak.Mark`, `Dak.Receive`, `Dak.PullBack`, `Dak.Resolve`, `Dak.Reopen`. Existing Dak.Move handles holder-only forward/return. Existing catalog seeding adds definitions and retains configured SYSTEM_ADMIN All grants; operational grants must be configured by the office. Permissions and scopes never substitute for identity/custody checks.

New additive migration: **`20261006214619_AddDakAcknowledgedCustody`**. No older migration is edited.

- Adds DakTransfer and append-only DakWorkflowCommandReceipt, routing/physical states, processing cycle, resolution metadata, assignment receive time, versioned correlated movement snapshots and explicit desk purpose.
- Locks and structurally audits existing Dak/assignment references before schema work. Errors include conflicting Dak/assignment IDs and raw diary display text. No renumber, merge, archive or delete is performed.
- Backfills only deterministic classifications: existing active allocations become LegacyUnconfirmed; original unknown/no/observed paper facts remain unknown/no/observed. No old assignment becomes an invented RECEIVE, and no sent/received timestamp is manufactured.
- Keeps `IX_Daks_DiaryNumberKey` globally unique and `CK_Daks_DiaryNumber` global. Keeps unique assignment per Dak and movement sequence. Adds unique pending transfer per Dak and global actor/request uniqueness plus dispatch, outcome, physical acknowledgment and resolution checks.
- Preserves the existing movement UPDATE/DELETE trigger. Adds receipt/audit/dispatch-fact/outcome protections and history TRUNCATE guards. EF guards cover every save overload.
- Deferred constraints verify transfer outcomes have matching immutable actor/event/receipt evidence, projections agree with pending delivery/recovery, sender remains booked during transit, confirmed assignment matches the latest personal acceptance and resolution has settled custody.
- Unused schema can roll back/reapply. Once acknowledged workflow history exists, downgrade fails safely; correction requires a forward migration instead of deleting evidence.

`scripts/audit-dak-custody.sql` provides read-only pre-migration legacy review; it intentionally uses only the old schema. Inactive or ambiguous records need explicit review. Known assignee confirmation and desk-only initial nomination provide acceptance without guessing receipt.

## Concurrency and verification

Every new command locks its Dak first, rechecks active actor/permission/scope and live eligibility, then writes projection, ordered event, audit and durable command receipt in one transaction. Relevant identity/membership/grant rows are shared-locked against concurrent revocation. Stable command/event/transfer IDs are allocated outside transaction retries. Immutable operation receipts provide commit verification and later replay without requiring the aggregate to remain at the old state.

Final verification: **348 backend tests passed, 0 failed, 0 skipped** in 7m14s, covering Dak, intake, custody/API, MyDesk, WorkItem, Outward, Matter authorization/workspace, attention and activity suites. **329 are non-PostgreSQL tests; 19 are real PostgreSQL tests**.

PostgreSQL results separately: **19/19 passed**, comprising 10 frozen intake/diary/preflight tests and 9 new custody integration/race tests. New coverage includes simultaneous receive/pull-back, deterministic receive-winning and pull-back-winning lock order, concurrent identical send/receive replay after later resolution, competing send keys, resolve-versus-send, membership and permission revocation while waiting, physical Record Room round trip/recovery/reopen, raw SQL history/projection tamper rejection, structural preflight rollback, legacy preservation and unused-schema rollback/reapplication. Downgrade after new history is safely rejected. PostgreSQL 17 used only uniquely named disposable databases on a dedicated loopback test cluster; final cleanup found zero test databases and stopped the cluster. No office/runtime migration was executed.

Existing frontend regression checks: **13 passed**, including Delhi-date boundaries and 11 mocked browser component cases for registration replay/error preservation, physical-original observations, privacy, terminal controls and SPA race isolation. The first attempt could not load Playwright in the fresh checkout; an existing dependency cache was linked and the complete rerun passed. Browser tests use an isolated headless browser and mocked APIs, not the office backend. Targeted strict Dak TypeScript checking and Vite production bundling passed; the existing bundle-size advisory remains. No frontend source was changed and these checks do not implement the new acceptance UI.

EF `has-pending-model-changes`: **no pending model changes**. Backend build succeeded; `git diff --check` clean. Three existing xUnit2031 analyzer warnings in AwardExtractionRuleEngineTests remain.

## Delivery boundaries

Frontend RECEIVE/return/reopen controls are outside this backend delivery and must be adapted before deployment; old desk-only/no-key movement submissions now fail safely. Inactive confirmed holders require reviewed restoration or a future explicit emergency handover policy; monitoring cannot silently replace custody. Audio, formal completion obligations, full Record Room inventory and physical-only storage movement of resolved records are deferred as agreed.

## Changed files

Exact inventory: **29 files**. The final response supplies the commit SHA.

- `docs/dak-custody-workflow-implementation.md`
- `docs/dak-custody-workflow-proposal.md`
- `scripts/audit-dak-custody.sql`
- `src/LAC.Api/DakCustodyEndpoints.cs`
- `src/LAC.Api/DakEndpoints.cs`
- `src/LAC.Api/RbacEndpoints.cs`
- `src/LAC.Domain/DakCustodyEntities.cs`
- `src/LAC.Domain/DakEntities.cs`
- `src/LAC.Domain/IdentityEntities.cs`
- `src/LAC.Domain/PermissionCodes.cs`
- `src/LAC.Infrastructure/ActivityProjectionService.cs`
- `src/LAC.Infrastructure/AttentionProjectionService.cs`
- `src/LAC.Infrastructure/Configurations/DakConfiguration.cs`
- `src/LAC.Infrastructure/DakAuthorizationService.cs`
- `src/LAC.Infrastructure/DakCustodyWorkflow.cs`
- `src/LAC.Infrastructure/DakIntakeMutations.cs`
- `src/LAC.Infrastructure/DakWorkflowService.cs`
- `src/LAC.Infrastructure/LacDbContext.cs`
- `src/LAC.Infrastructure/Migrations/20261006214619_AddDakAcknowledgedCustody.Designer.cs`
- `src/LAC.Infrastructure/Migrations/20261006214619_AddDakAcknowledgedCustody.cs`
- `src/LAC.Infrastructure/Migrations/LacDbContextModelSnapshot.cs`
- `tests/LAC.Tests/DakCustodyApiTests.cs`
- `tests/LAC.Tests/DakCustodyPostgresTests.cs`
- `tests/LAC.Tests/DakCustodyTests.cs`
- `tests/LAC.Tests/DakIntakeTests.cs`
- `tests/LAC.Tests/DakPostgresTests.cs`
- `tests/LAC.Tests/DakTestCustodyFixtures.cs`
- `tests/LAC.Tests/DakTests.cs`
- `tests/LAC.Tests/MyDeskTests.cs`
