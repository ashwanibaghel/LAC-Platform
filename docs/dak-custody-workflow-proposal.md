# Dak acknowledged custody workflow — audit and proposal

Review date: 7 October 2026 (Asia/Kolkata). This records the source audit and approved design. See dak-custody-workflow-implementation.md for delivered behavior and verification.

- Mandatory branch base: `d44941636d54316350e8d6ee2dc03628281baace`.
- Fresh branch: `codex/dak-custody-workflow`, created directly from that SHA.
- Checkout: `C:/Users/ashwa/.codex/worktrees/dak-custody-workflow/LAC-Platform`.
- Approved scope corrections: no formal required-work entity/API; no supervisory on-behalf forwarding; resolution requires attestation/remarks and settled delivery/physical return. No merge or office deployment is authorized.

## Current backend audit

| Area | Verified behavior at the mandatory base | Consequence for the new rules |
|---|---|---|
| Diary identity | Mandatory manual number, GUID PK, stored canonical key, global unique `IX_Daks_DiaryNumberKey`, global `CK_Daks_DiaryNumber`. | Preserve exactly, including all historical/lifecycle reservations and request replay. |
| Lifecycle | `Registered`, `InProcess`, `Disposed`, `Cancelled`. | No explicit pending transfer, receive, pull-back, resolve or reopen operation exists. |
| Assignment | Exactly one `DakAssignment` projection row per Dak; its assigned user is nullable. | One row does not establish that one particular officer actually received the Dak. Desk-only legacy rows stay unconfirmed until explicit initial nomination and personal acceptance. |
| Movement | `MoveAsync` immediately overwrites the assignment destination and sets `InProcess` on Marked/Forwarded/Returned. | The current implementation transfers custody before acknowledgment. This must change under the newly confirmed rule. |
| Authorization | `Dak.View/Register/Edit/Move/Dispose/Cancel`; scopes include All, Workstream and Assigned. Assigned scope checks desk membership. | Permission/scope alone is insufficient for holder-only actions; a colleague at the same desk must not receive, forward or resolve another officer's Dak. No SO-only rule exists in this service. |
| Concurrency | PostgreSQL aggregate row locks, expected revision, unique movement sequence and stable operation IDs with transaction success verification. | Reuse these mechanisms for send/receive/pull-back races and lost-response retries. |
| History | Actor, source/destination, UTC timestamps, identity snapshots, remarks/instructions and sequence are recorded. PostgreSQL already has `trg_dak_movement_immutable`, rejecting UPDATE/DELETE. | Preserve this trigger and old events. Extend the event vocabulary and snapshots; do not manufacture old receipt events. |
| EF immutability | The Dak movement guard is in `SaveChangesAsync(CancellationToken)`, but not in the synchronous/bool overloads. | PostgreSQL protects persisted movements; overload coverage still needs hardening, especially for InMemory checks. |
| Generic audit | OfficialRecord changes produce before/after AuditLogs; intake mutations add operation audit receipts. No Dak-specific AuditLogs immutability trigger was found. | Protect Dak audit entries as append-only without changing unrelated modules' policies. |
| Physical original | Nullable existence and explicit observed Desk/User/location/provenance fields; routing does not infer paper custody. PUT can currently change observed user/desk directly. | Preserve unknown/no/yes and observation provenance. Add dispatch/acceptance tracking, and prevent the observation endpoint bypassing a tracked transfer. |
| Record Room | A RECORD_ROOM workstream and configurable desks/memberships already exist; no Dak caretaker acknowledgment workflow exists. | Reuse identities and memberships with an explicit destination purpose; do not hard-code a caretaker account. |
| Required work | Typed `WorkItemDakLink` exists, but links do not specify which work is required for resolution. | An unrelated context link must not silently become a mandatory task. Formal completion obligations are deferred by approved scope correction; this phase uses completion attestation and remarks. |
| Read models | My Desk reads active assignments; timeline/activity assume immediate routing. | Add separate received/incoming/sent queues and accurate dispatch/receipt wording. Preserve target-level privacy. |

Primary evidence: `src/LAC.Domain/DakEntities.cs`; `src/LAC.Infrastructure/Configurations/DakConfiguration.cs`; `DakWorkflowService.cs`; `DakAuthorizationService.cs`; `DakIntakeMutations.cs`; `LacDbContext.cs`; migration `20260919071625_AddDakWorkflowFoundation.cs`; `src/LAC.Api/DakEndpoints.cs`; `src/LAC.Domain/IdentityEntities.cs`; `WorkItemEntities.cs`; `src/LAC.Infrastructure/SeedData.cs`; existing Dak/Intake/Postgres tests.

## State model

Keep lifecycle, delivery and paper condition separate. Otherwise resolving work would erase the paper location, or sending paper would falsely complete officer receipt.

- Keep existing `Dak.Status` values and meanings. Add `Resolved`; never rename historical `Disposed` records into it. Registered remains unassigned intake; InProcess represents an open processing cycle.
- Initial dispatch changes Registered to InProcess with InTransit routing. Pull-back of that unreceived initial mark restores Registered/Unassigned, subject to truthful paper-recovery state. Later sends/pull-backs remain InProcess. These routing changes intentionally replace the old immediate-assignment behavior; the frozen Phase A intake/identity/privacy protections remain intact.
- Add routing state: `Unassigned`, `WithHolder`, `InTransit`, `LegacyUnconfirmed`. A confirmed assignment has exactly one non-null officer and desk. Registration still creates no received holder.
- Add physical state: `Unknown`, `NotPresent`, `AtRecordedLocation`, `Held`, `InTransit`, `ReturnPending`. Unknown and an observed legacy location are not proof of acknowledged custody. During transit expose last confirmed custodian and pending destination separately.
- `Reopened` is an immutable event starting another open cycle, not a permanent competing state. A reopened Dak returns to WithHolder if its retained holder remains valid; otherwise it is flagged for explicit custody reconciliation.
- User-facing workflow state is derived from these axes: Unassigned, WithHolder, InTransit, Resolved, or NeedsAttention. RecordStatus remains independent; Archived/Inactive records do not become mutable through reopening.

| Starting condition | Operation | Result and holder |
|---|---|---|
| Registered / Unassigned | Initial MARK by authorized user | Pending transfer, InTransit; destination has not received. No holder is invented for the registration operator. |
| Open / WithHolder A | SEND/FORWARD/RETURN to B | InTransit; A remains the sole last received/booked holder. B is only the pending recipient. No second transfer or resolution is allowed. |
| InTransit to B | RECEIVE by B | Pending transfer closes as Received; assignment changes once to B; WithHolder B. |
| InTransit to B | PULL BACK by sender, mandatory reason | Pending transfer closes as PulledBack. B cannot accept it afterward. Return to A, or to unassigned intake for an initial mark. Paper recovery is handled separately below. |
| WithHolder A, completion attested and remarks provided | RESOLVE by A | Resolved; retain last confirmed custody/location. Remove from active work queues without pretending the paper disappeared. |
| Resolved | REOPEN by separately authorized supervisor, mandatory reason | Open processing cycle plus Reopened event; supervisor does not become holder. |
| LegacyUnconfirmed | Explicit present-day custody confirmation | Known assignee confirms actual holding now; record a new confirmation event, not an invented historical receive date. Desk-only legacy allocation has no confirmed officer and can be explicitly initially marked to a nominated receiver; only RECEIVE confirms the new holder. |

RETURN to an earlier officer is a new send requiring that officer's RECEIVE. It is different from cancelling one's own unreceived send through PULL BACK. A receiver cannot act on pending work as a current holder. No on-behalf RECEIVE is allowed, even with All scope.

## Physical transfer and Record Room

`includesPhysicalOriginal=true` requires explicitly confirmed original existence and verified sender custody. An unknown original, scan, or current routing assignment is insufficient evidence. Electronic-only transfer leaves physical-original fields untouched.

At dispatch, physical state becomes InTransit. The last confirmed custodian remains A; the current location is explicitly shown as transit to B, not as physically sitting with A or already received by B. Store who dispatched, who initiated the action, destination, sent time and remarks.

For a physically sent transfer, B's RECEIVE must explicitly confirm the actual paper is present. Only then set received time and confirmed physical custodian/location to B. Reading the scan or opening the detail page never accepts custody. No partial paper acceptance or silent delivery assumption is introduced.

Pull-back withdraws B's right to receive immediately, but must not claim travelling paper has returned. If paper is already physically recovered, sender confirms that fact with provenance. Otherwise record ReturnPending, retain last confirmed custody, and require a separate sender return-confirmation event. Block another dispatch, resolution and closure until recovery is confirmed. This is a proposed safeguard for the otherwise unspecified paper-return case.

Record Room is a typed destination backed by a configured OfficeDesk and one nominated, active caretaker user. The same send/receive protocol applies in both directions. Until caretaker receipt, the file is InTransit; afterward the caretaker is the single received holder and physical custodian. Later sending it to an officer creates another pending transfer. No accession register, shelf inventory, retention policy, multi-recipient routing or full Record Room module is included.

Proposed scope boundary: these operations transfer an open Dak and optionally its original together. Moving only the paper of an already Resolved Dak without reopening its work needs a separate storage-operation policy; do not implicitly turn it into processing or silently reopen it.

Retain the Phase A physical observation endpoint. For legacy/untracked observations it retains its existing purpose. Once acknowledged custody tracking applies, it cannot overwrite the custodian, clear transit or put paper at the pending destination; those changes require transfer/receipt/recovery events. Metadata and provenance edits must not become an alternate custody-transfer route.

## Entities and database constraints

| Entity | Proposed change |
|---|---|
| Dak | Routing state, resolution metadata and processing cycle number; continue using the existing Revision for every aggregate mutation. Keep diary, identity, physical existence and registration replay fields. |
| DakAssignment | Preserve the unique DakId projection. Add receipt/confirmation metadata and confirmed-custody flag. Require user/desk for confirmed assignments; preserve nullable legacy rows as unconfirmed. Update destination only on RECEIVE. Resolved retains confirmed custody; work queues filter lifecycle separately. |
| DakTransfer (new) | DakId; purpose Marked/Forwarded/Returned; sender user and source desk/holder; initiated-by user; one destination user/desk/kind; physical-inclusion flag; sent time; text remarks/instructions; mutable Pending/Received/PulledBack projection; acknowledgment/pull-back actor/time/reason; physical recovery projection; originating movement ID. No soft deletion releasing pending uniqueness. |
| DakMovement | Keep existing actions/numeric values and immutable historical rows. Append Received, PulledBack, Resolved, Reopened, CustodyConfirmed, PhysicalReturnConfirmed events. Add nullable transfer correlation and lifecycle/routing/physical before/after snapshots with an event schema version. Old events remain old facts. |
| DakWorkflowCommand (new) | Actor + request UUID, DakId, action, canonical payload hash, result receipt/event ID. Persistent idempotency for new workflow commands; separate from frozen registration replay. |
| OfficeDesk | Explicit configurable purpose General/RecordRoom; no inference from desk names, designations or today's officers. |

Planned constraints: retain global diary index/check and global unique assignment DakId; unique pending transfer per Dak using `State = 'Pending'` (never RecordStatus); unique actor/request UUID; existing unique DakId/movement-sequence; required destination user/desk; valid state/timestamp combinations; receive and pull-back outcomes mutually exclusive; received physical timestamp only after explicit physical confirmation; same-Dak transfer/event references. Received/pulled-back facts cannot be rewritten. Protect transfer dispatch facts against UPDATE, protect transfer rows against DELETE, and keep terminal projections consistent with their immutable events.

Extend the existing save guards to all overloads. Keep the existing movement UPDATE/DELETE trigger. Add scoped protection for Dak-related audit rows; guard TRUNCATE or deny it to the application role on protected history tables. The mutable assignment/transfer projection is not the authoritative historical evidence: every successful state change appends its immutable event and audit receipt in the same transaction.

Voice extensibility: text remarks use a versioned `kind: Text` contract, retaining existing remarks/instructions. Future kinds can be added without replacing event identity. This phase rejects unsupported kinds and creates no audio upload, storage, playback or transcription implementation.

## Permissions and read authorization

| Permission | Additional invariant |
|---|---|
| Existing Dak.View | Scoped reads, monitoring, documents/history. Exact pending recipient gets appropriate scoped pending access, with existing linked-target redaction. |
| Existing Dak.Register / Dak.Edit / Dak.Cancel | Frozen intake rules remain; no mutation of historical/terminal records or bypass of a pending custody transfer. |
| New Dak.Mark | Initial dispatch from unassigned intake. SO and NT can receive this permission through configured roles. |
| Existing Dak.Move | Forward/return by the current confirmed holder. |
| New Dak.Receive | Only the nominated recipient, with current valid account/desk membership; also narrowly controls explicit legacy self-confirmation. |
| New Dak.PullBack | The recorded sender, while the transfer remains Pending; reason mandatory. No supervising initiator may send on behalf of a received holder in this phase. |
| New Dak.Resolve | Current received holder, no pending transfer/recovery and completion attested and remarks provided; completion attestation/remarks required. |
| New Dak.Reopen | Separately granted supervisory authority in scope, only for Resolved, reason mandatory. It is not implied by Edit/Move/Resolve. |


Keep role/workstream/desk configuration data-driven. ADM/senior roles can be configured primarily for View/monitoring with optional Mark/Reopen grants. Existing SYSTEM_ADMIN catalog seeding grants All permissions by its configured policy; All scope still cannot bypass nominated receiver/current-holder rules. No new operational grants based on designation names, reporting titles or named officers.

Pending recipients can view their incoming Dak and permitted documents without becoming current assignment holders. They do not obtain holder Move/Resolve privileges before RECEIVE. Exact pending senders also retain scoped read visibility, including the initial marker whose Dak has no received assignment. Other members of the destination desk do not gain acceptance rights. My Desk distinguishes With Me, Incoming Awaiting Receipt, Sent Awaiting Receipt, Resolved and NeedsAttention; authorized monitoring can aggregate these without granting custody actions.

## API proposal

All new mutation commands require a UUID Idempotency-Key and expected aggregate revision. Server assigns UTC operation timestamps; existing ReceivedDate remains the inward registration date. Responses include command receipt/event ID, transfer ID where applicable, revision, lifecycle, routing, current confirmed holder, pending destination and physical state.

| API under /api/dak | Purpose |
|---|---|
| POST /{id}/transfers | Initial mark or subsequent send/forward/return. Body includes destination kind/desk/user, text remarks/instructions, physical-inclusion flag and expectedRevision. |
| POST /{id}/transfers/{transferId}/receive | Nominated recipient acceptance, with explicit paper confirmation when included. |
| POST /{id}/transfers/{transferId}/pull-back | Sender withdrawal with mandatory reason; explicit physical-recovery evidence if already returned. |
| POST /{id}/transfers/{transferId}/confirm-return | Original sender confirms actual paper recovery after pull-back. |
| POST /{id}/custody/confirm | Explicit present-day confirmation by a known legacy assignee; no retroactive receipt fabrication. Desk-only reconciliation uses initial Mark permission and a normal accepted transfer. |
| POST /{id}/resolve | Holder completion attestation and mandatory resolution remarks. |
| POST /{id}/reopen | Separately authorized supervisor and mandatory reason. |
| GET /{id}; GET /{id}/timeline; GET /{id}/transfers | Current projections, correlated immutable events and transfer summaries. |
| GET /my-desk; GET /{id}/movement-targets | New queue buckets and eligible named destination users, including configured caretaker destinations. |

Retain `/move` as an adapter to dispatch, using action-specific permission checks (Mark versus Move) and the same invariants. It must never retain an auto-receive path. Desk-only destinations now fail validation rather than guessing an officer. Existing callers must consume the pending-transfer result and require a RECEIVE action; route presence alone is not compatibility with the old immediate-custody semantics.

Keep `/dispose` distinct and irreversible for legacy terminal semantics, but apply holder/completion/no-transit guards so it is not a bypass around RESOLVE. Cancel still requires its existing permission and reason; a pending transfer or paper recovery must be settled before closure. Archived/Inactive, Disposed and Cancelled cannot be reopened. New Resolved records reject ordinary intake edits until authorized reopening. Registration and its optional-key compatibility are untouched.

Use 400 for invalid input, 403 for denied actor/permission, and 409 for stale revision, already-ended transfer, competing command or invalid workflow state. Preserve existing privacy behavior for unauthorized reads; new errors do not expose restricted linked records.

## Concurrency and transaction rules

1. Reauthorize the live actor and scoped operation inside the transaction, not only before waiting for the Dak lock. Lock the Dak first, then the relevant transfer/projection; validate recipient membership and holder against that locked state. Serialize relevant eligibility changes or lock membership/account rows during acceptance.
2. Allocate stable command, transfer and event IDs once outside execution-strategy retries. Persist request hash and immutable result receipt; valid replay returns the original operation result even if later events advanced the Dak. Changed payload under the same key conflicts. Registration's existing mechanism is not replaced.
3. RECEIVE versus PULL BACK: both lock the same Dak and pending transfer. Exactly one succeeds. A receive winner blocks pull-back; a pull-back winner blocks receive. No contradictory receipt/pull-back events or mixed paper/assignment state.
4. Two sends, two receives, resolve versus send, and resolve versus reopen are governed by the same row lock, revision and database uniqueness. No background job or timeout auto-accepts a transfer.
5. A successful operation atomically updates projections, increments revision and appends movement/audit/command receipt. Sequence allocation remains under the aggregate lock. Retry verification uses immutable operation evidence, not a demand that the current projection still equal the operation's old result.
6. Resolution uses mandatory holder completion attestation and remarks in this phase. No WorkItem completion obligation, entity, endpoint or workflow change is included.
7. Account deactivation, removed membership or inactive destination produces NeedsAttention; it never retargets the transfer to a replacement officer automatically. Emergency/supervisory reassignment is deferred; monitoring never replaces custody. Paper state and custody are never inferred from role changes.

## Migration and compatibility strategy

Create a new additive migration after the mandatory base. Do not edit Phase A or any earlier migration, diary constraint, history row, registration request or existing recorded physical location.

Preflight reports malformed assignments, missing/inactive users/desks, ambiguous desk-only custody and inconsistent physical observations with IDs. Structural conflicts fail transactionally before new strict constraints; historical ambiguity is explicitly LegacyUnconfirmed. Do not backfill every old destination as Received just because it appears in an assignment.

Registered/unassigned records remain Unassigned. Existing assigned records lack acknowledgment evidence, so start LegacyUnconfirmed with their old assignment/history retained for reference. Physical unknown/observed locations remain unknown/observed, not retroactively acknowledged Held. There is no creation of fake sent/received times. Existing Disposed/Cancelled remain terminal. No required-work table or API is introduced; old WorkItem links and responsibilities are untouched.

Backfill only deterministic classification needed for the new columns. New strict invariants apply to confirmed/new workflow rows, while ambiguous legacy records require explicit reviewed confirmation or nomination. Seed permission definitions and configure grants without officer-specific rules. Adapt all old mutation endpoints and read projections before enabling acknowledged operations, so there is no alternate instant-transfer writer.

Downgrade is permitted on an unused schema. Once new custody/transfer/resolution history exists, Down must fail safely rather than drop evidence; use a forward corrective migration. Test both cases on disposable PostgreSQL. EF model validation and generated SQL review are implementation gates, not a reason to apply migrations to the office database.

## Planned tests and delivery gates

- Frozen Phase A: permanent diary identity in every lifecycle/status, mandatory number, normalization, original registration replay, scan compensation, target privacy, terminal mutation guards, historical reads, Delhi date, unknown physical existence and physical/routing independence.
- Initial mark, send and receive: one nominated receiver; A remains confirmed holder during transit; B cannot forward/resolve before receive; explicit paper receipt; exactly one projection and complete event sequence.
- Pull-back: mandatory reason; sender-only; successful before receipt, forbidden after; initial marking returns to intake; travelling paper enters ReturnPending until recovery, with no fictional returned location.
- Record Room round trip: officer dispatch, caretaker receipt, caretaker dispatch, officer receipt; inactive/replaced caretaker and desk-only target reject safely.
- Resolve/reopen: holder-only; missing completion attestation or remarks block completion; mandatory attestations/reasons; separately granted Reopen; no designation-based authorization; no reopen for archived/Disposed/Cancelled; reopening never assigns supervisor custody.
- Authorization: same-desk colleague and All-scope supervisor cannot receive for B; pending recipient can read incoming context with existing target redaction; sender and monitor queues remain accurate.
- Real PostgreSQL races: receive/pull-back barrier race in both winning orders, concurrent sends/receives, permission/membership changes while waiting, stale revisions, rollback and lost-response idempotent replay after subsequent state advancement.
- History: UPDATE/DELETE via every EF save overload, bulk/raw SQL and protected application role; TRUNCATE protection; unchanged legacy event content; atomic event/projection/audit receipts.
- Migration: empty and populated legacy fixtures, desk-only and inactive assignees, physical unknowns, retained diary constraints, failure rollback, no fake receipt backfill, safe downgrade before use and guarded downgrade after use; EF pending-model validation.
- Client compatibility when implemented: pending state/Receive controls, no old move or physical-edit bypass, stale command/route navigation handling; targeted TypeScript, Dak frontend tests and Vite build alongside focused backend suites.

Audit performed by source, migration and test inspection at the exact base. No tests were executed for this proposal and no new workflow behavior is claimed to be implemented. The previous Phase A report's execution results remain historical evidence, not a fresh run of this branch.
