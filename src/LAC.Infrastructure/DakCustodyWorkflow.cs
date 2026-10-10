namespace LAC.Infrastructure;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

public sealed record SendDakCommand(DakMovementAction Action, Guid ToDeskId, Guid ToUserId,
    DakDestinationKind DestinationKind, bool IncludesPhysicalOriginal, string? Remarks, string? Instructions,
    int ExpectedRevision, Guid RequestId, string RemarksKind = "Text");
public sealed record ReceiveDakCommand(int ExpectedRevision, Guid RequestId, bool PhysicalReceiptConfirmed = false);
public sealed record PullBackDakCommand(string Reason, int ExpectedRevision, Guid RequestId);
public sealed record ConfirmReturnDakCommand(string Provenance, int ExpectedRevision, Guid RequestId);
public sealed record ResolveDakCommand(bool CompletionAttested, string Remarks, int ExpectedRevision, Guid RequestId);
public sealed record ReopenDakCommand(string Reason, int ExpectedRevision, Guid RequestId);
public sealed record ConfirmDakCustodyCommand(int ExpectedRevision, Guid RequestId);
public sealed record DakCommandResult(Guid CommandId, Guid DakId, Guid? TransferId, int Revision, string Status,
    string RoutingState, string PhysicalState, Guid? ConfirmedHolderUserId, int ProcessingCycle, Guid? PhysicalCustodianUserId, Guid? PhysicalDeskId, string? PhysicalLocationNote);

public sealed partial class DakWorkflowService
{
    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string RequiredText(string? value, string label)
    {
        if (Text(value) is not { } text || text.Length > 1000) throw new DakWorkflowException($"{label} is required and must not exceed 1000 characters.");
        return text;
    }

    public Task<DakCommandResult> SendAsync(Guid id, SendDakCommand command, Guid actor, CancellationToken ct = default)
    {
        if (command.Action is not (DakMovementAction.Marked or DakMovementAction.Forwarded or DakMovementAction.Returned))
            throw new DakWorkflowException("Only Marked, Forwarded and Returned dispatches are allowed.");
        if (!Enum.IsDefined(command.DestinationKind) || command.ToUserId == Guid.Empty || command.ToDeskId == Guid.Empty)
            throw new DakWorkflowException("A valid destination kind, desk and nominated receiver are required.");
        if (command.RemarksKind != "Text") throw new DakWorkflowException("Only text remarks are supported.");
        if (command.Remarks?.Length > 1000 || command.Instructions?.Length > 1000) throw new DakWorkflowException("Remarks and instructions must not exceed 1000 characters.");
        command = command with { Remarks = Text(command.Remarks), Instructions = Text(command.Instructions) };
        return CustodyCommandAsync(id, actor, command.RequestId, command.ExpectedRevision, command.Action.ToString(),
            command.Action == DakMovementAction.Marked ? PermissionCodes.DakMark : PermissionCodes.DakMove, command,
            async (dak, movement, now, c) =>
            {
                EnsureMutable(dak);
                await EnsureNoDeliveryAsync(dak, c);
                if (command.Action == DakMovementAction.Marked)
                {
                    var freshIntake = dak.Status == DakStatus.Registered && dak.RoutingState == DakRoutingState.Unassigned && dak.CurrentAssignment?.IsActive != true;
                    var deskOnlyLegacy = dak.Status == DakStatus.InProcess && dak.RoutingState == DakRoutingState.LegacyUnconfirmed &&
                        dak.CurrentAssignment is { IsActive: true, AssignedUserId: null, ReceivedAt: null };
                    if (!freshIntake && !deskOnlyLegacy)
                        throw new DakWorkflowException("Initial marking requires unassigned intake or desk-only legacy allocation without a received officer.", 409);
                }
                else await EnsureConfirmedHolderAsync(dak, actor, c);

                await LockCustodyEligibilityAsync(actor, command.ToUserId, c);
                var desk = await db.OfficeDesks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == command.ToDeskId && d.IsActive && d.RecordStatus == RecordStatus.Active, c)
                    ?? throw new DakWorkflowException("Destination desk is missing or inactive.");
                if ((desk.Purpose == OfficeDeskPurpose.RecordRoom) != (command.DestinationKind == DakDestinationKind.RecordRoom))
                    throw new DakWorkflowException("Destination kind does not match the configured desk purpose.");
                await RequireDeskMemberAsync(command.ToUserId, command.ToDeskId, c, 400);
                if (!await DakRecipientEligibility.CanReceiveAsync(db, dak, command.ToUserId, command.ToDeskId, c))
                    throw new DakWorkflowException("The nominated desk member is not authorized to receive this Dak. Choose an eligible recipient.", 400);
                if (command.IncludesPhysicalOriginal && (dak.HasPhysicalOriginal != true || dak.PhysicalOriginalUserId != actor ||
                    dak.PhysicalOriginalDeskId is null || dak.PhysicalState is DakPhysicalState.InTransit or DakPhysicalState.ReturnPending))
                    throw new DakWorkflowException("Confirm original existence and sender's physical custody before dispatch.", 409);
                if (command.IncludesPhysicalOriginal) await RequireDeskMemberAsync(actor, dak.PhysicalOriginalDeskId!.Value, c);

                var transfer = new DakTransfer
                {
                    Id = movement.Id, DakId = id, Purpose = command.Action, FromStatus = dak.Status, FromRoutingState = dak.RoutingState, SenderUserId = actor,
                    FromDeskId = dak.CurrentAssignment?.IsActive == true ? dak.CurrentAssignment.OfficeDeskId : dak.PhysicalOriginalDeskId,
                    FromHolderUserId = dak.CurrentAssignment?.IsActive == true ? dak.CurrentAssignment.AssignedUserId : null,
                    ToDeskId = command.ToDeskId, ToUserId = command.ToUserId, DestinationKind = command.DestinationKind,
                    IncludesPhysicalOriginal = command.IncludesPhysicalOriginal, SentAt = now,
                    Remarks = command.Remarks, Instructions = command.Instructions, State = DakTransferState.Pending
                };
                db.DakTransfers.Add(transfer);
                movement.TransferId = transfer.Id;
                movement.ToDeskId = desk.Id; movement.ToUserId = command.ToUserId;
                movement.ToDeskCodeSnapshot = desk.Code; movement.ToDeskNameSnapshot = desk.Name;
                movement.ToUserDisplayNameSnapshot = (await db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == command.ToUserId, c)).DisplayName;
                movement.Remarks = command.Remarks; movement.InstructionsSnapshot = command.Instructions;
                dak.Status = DakStatus.InProcess; dak.RoutingState = DakRoutingState.InTransit;
                if (command.IncludesPhysicalOriginal) dak.PhysicalState = DakPhysicalState.InTransit;
                return transfer.Id;
            }, ct);
    }

    public Task<DakCommandResult> ReceiveAsync(Guid id, Guid transferId, ReceiveDakCommand command, Guid actor, CancellationToken ct = default) =>
        CustodyCommandAsync(id, actor, command.RequestId, command.ExpectedRevision, "Received", PermissionCodes.DakReceive,
            new { transferId, command }, async (dak, movement, now, c) =>
            {
                EnsureMutable(dak);
                var transfer = await PendingTransferAsync(dak, transferId, c);
                if (transfer.ToUserId != actor) throw new DakWorkflowException("Only the nominated receiver can receive this Dak.", 403);
                await RequireDeskMemberAsync(actor, transfer.ToDeskId, c);
                var desk = await db.OfficeDesks.AsNoTracking().SingleAsync(d => d.Id == transfer.ToDeskId, c);
                if ((desk.Purpose == OfficeDeskPurpose.RecordRoom) != (transfer.DestinationKind == DakDestinationKind.RecordRoom))
                    throw new DakWorkflowException("Destination purpose changed; review this delivery.", 409);
                if (transfer.IncludesPhysicalOriginal && !command.PhysicalReceiptConfirmed)
                    throw new DakWorkflowException("Confirm actual physical original receipt before accepting this dispatch.", 409);
                if (!transfer.IncludesPhysicalOriginal && command.PhysicalReceiptConfirmed)
                    throw new DakWorkflowException("This dispatch did not include the physical original.");
                transfer.State = DakTransferState.Received; transfer.ReceivedAt = now;
                if (transfer.IncludesPhysicalOriginal)
                {
                    transfer.PhysicalReceivedAt = now;
                    SetPhysicalReceipt(dak, actor, transfer.ToDeskId, now, "Physical original received with acknowledged Dak transfer.");
                }
                var assignment = dak.CurrentAssignment;
                if (assignment is null)
                {
                    assignment = new DakAssignment { DakId = id };
                    db.DakAssignments.Add(assignment); dak.CurrentAssignment = assignment;
                }
                assignment.OfficeDeskId = transfer.ToDeskId; assignment.AssignedUserId = actor;
                assignment.AssignedByUserId = transfer.SenderUserId; assignment.AssignedAt = now; assignment.ReceivedAt = now;
                assignment.Instructions = transfer.Instructions; assignment.RecordStatus = RecordStatus.Active; assignment.IsActive = true; assignment.ClosedAt = null;
                dak.RoutingState = DakRoutingState.WithHolder;
                movement.TransferId = transfer.Id; movement.ToDeskId = transfer.ToDeskId; movement.ToUserId = actor;
                movement.ToDeskCodeSnapshot = desk.Code; movement.ToDeskNameSnapshot = desk.Name;
                movement.ToUserDisplayNameSnapshot = (await db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == actor, c)).DisplayName;
                return transfer.Id;
            }, ct);

    public Task<DakCommandResult> PullBackAsync(Guid id, Guid transferId, PullBackDakCommand command, Guid actor, CancellationToken ct = default)
    {
        command = command with { Reason = RequiredText(command.Reason, "Pull-back reason") };
        return CustodyCommandAsync(id, actor, command.RequestId, command.ExpectedRevision, "PulledBack", PermissionCodes.DakPullBack,
            new { transferId, command }, async (dak, movement, now, c) =>
            {
                EnsureMutable(dak);
                var transfer = await PendingTransferAsync(dak, transferId, c);
                if (transfer.SenderUserId != actor) throw new DakWorkflowException("Only this dispatch's sender may pull it back.", 403);
                transfer.State = DakTransferState.PulledBack; transfer.PulledBackAt = now; transfer.PullBackReason = command.Reason;
                dak.RoutingState = transfer.FromRoutingState;
                dak.Status = transfer.FromStatus;
                if (transfer.IncludesPhysicalOriginal) dak.PhysicalState = DakPhysicalState.ReturnPending;
                movement.TransferId = transfer.Id; movement.Remarks = command.Reason;
                return transfer.Id;
            }, ct);
    }

    public Task<DakCommandResult> ConfirmReturnAsync(Guid id, Guid transferId, ConfirmReturnDakCommand command, Guid actor, CancellationToken ct = default)
    {
        command = command with { Provenance = RequiredText(command.Provenance, "Physical recovery provenance") };
        return CustodyCommandAsync(id, actor, command.RequestId, command.ExpectedRevision, "PhysicalReturnConfirmed", PermissionCodes.DakPullBack,
            new { transferId, command }, async (dak, movement, now, c) =>
            {
                EnsureMutable(dak);
                var transfer = await db.DakTransfers.SingleOrDefaultAsync(t => t.Id == transferId && t.DakId == id, c)
                    ?? throw new DakWorkflowException("Transfer not found.", 404);
                if (transfer.SenderUserId != actor) throw new DakWorkflowException("Only the sender may confirm actual paper recovery.", 403);
                if (transfer.State != DakTransferState.PulledBack || !transfer.IncludesPhysicalOriginal || transfer.PhysicalReturnedAt.HasValue || dak.PhysicalState != DakPhysicalState.ReturnPending)
                    throw new DakWorkflowException("This transfer has no outstanding physical return.", 409);
                if (dak.PhysicalOriginalDeskId is not { } deskId) throw new DakWorkflowException("Last confirmed sender location is missing.", 409);
                await RequireDeskMemberAsync(actor, deskId, c);
                transfer.PhysicalReturnedAt = now; transfer.PhysicalReturnProvenance = command.Provenance;
                SetPhysicalReceipt(dak, actor, deskId, now, command.Provenance);
                movement.TransferId = transfer.Id; movement.Remarks = command.Provenance;
                return transfer.Id;
            }, ct);
    }

    public Task<DakCommandResult> ResolveAsync(Guid id, ResolveDakCommand command, Guid actor, CancellationToken ct = default)
    {
        if (!command.CompletionAttested) throw new DakWorkflowException("Completion attestation is mandatory.");
        command = command with { Remarks = RequiredText(command.Remarks, "Resolution remarks") };
        return CustodyCommandAsync(id, actor, command.RequestId, command.ExpectedRevision, "Resolved", PermissionCodes.DakResolve, command,
            async (dak, movement, now, c) =>
            {
                EnsureMutable(dak); await EnsureNoDeliveryAsync(dak, c); await EnsureConfirmedHolderAsync(dak, actor, c);
                dak.Status = DakStatus.Resolved; dak.ResolvedAt = now; dak.ResolvedByUserId = actor; dak.ResolutionRemarks = command.Remarks;
                movement.Remarks = command.Remarks;
                return null;
            }, ct);
    }

    public Task<DakCommandResult> ReopenAsync(Guid id, ReopenDakCommand command, Guid actor, CancellationToken ct = default)
    {
        command = command with { Reason = RequiredText(command.Reason, "Reopen reason") };
        return CustodyCommandAsync(id, actor, command.RequestId, command.ExpectedRevision, "Reopened", PermissionCodes.DakReopen, command,
            async (dak, movement, _, c) =>
            {
                if (dak.RecordStatus != RecordStatus.Active || dak.Status != DakStatus.Resolved)
                    throw new DakWorkflowException("Only active Resolved Dak may be reopened.", 409);
                await EnsureNoDeliveryAsync(dak, c);
                dak.Status = DakStatus.InProcess; dak.ProcessingCycle++;
                dak.ResolvedAt = null; dak.ResolvedByUserId = null; dak.ResolutionRemarks = null;
                movement.Remarks = command.Reason;
                return null;
            }, ct);
    }

    public Task<DakCommandResult> ConfirmCustodyAsync(Guid id, ConfirmDakCustodyCommand command, Guid actor, CancellationToken ct = default) =>
        CustodyCommandAsync(id, actor, command.RequestId, command.ExpectedRevision, "CustodyConfirmed", PermissionCodes.DakReceive, command,
            async (dak, _, now, c) =>
            {
                EnsureMutable(dak); await EnsureNoDeliveryAsync(dak, c);
                if (dak.RoutingState != DakRoutingState.LegacyUnconfirmed || dak.CurrentAssignment is not { IsActive: true, RecordStatus: RecordStatus.Active } assignment || assignment.AssignedUserId != actor)
                    throw new DakWorkflowException("Only the known legacy assignee may confirm present-day holding. Desk-only custody requires reviewed nomination.", 409);
                await RequireDeskMemberAsync(actor, assignment.OfficeDeskId, c);
                assignment.ReceivedAt = now; dak.RoutingState = DakRoutingState.WithHolder;
                return null;
            }, ct);

    private void SetPhysicalReceipt(Dak dak, Guid actor, Guid deskId, DateTimeOffset now, string provenance)
    {
        dak.PhysicalState = DakPhysicalState.Held; dak.PhysicalOriginalUserId = actor; dak.PhysicalOriginalDeskId = deskId;
        dak.PhysicalOriginalLocationNote = null; dak.PhysicalOriginalProvenanceNote = provenance;
        dak.PhysicalOriginalUpdatedAt = now; dak.PhysicalOriginalUpdatedByUserId = actor;
    }

    private async Task EnsureNoDeliveryAsync(Dak dak, CancellationToken ct)
    {
        if (dak.RoutingState == DakRoutingState.InTransit || dak.PhysicalState is DakPhysicalState.InTransit or DakPhysicalState.ReturnPending ||
            await db.DakTransfers.AnyAsync(t => t.DakId == dak.Id && (t.State == DakTransferState.Pending ||
                t.State == DakTransferState.PulledBack && t.IncludesPhysicalOriginal && t.PhysicalReturnedAt == null), ct))
            throw new DakWorkflowException("Settle the pending delivery or confirm physical return first.", 409);
    }

    private async Task EnsureConfirmedHolderAsync(Dak dak, Guid actor, CancellationToken ct)
    {
        if (dak.RoutingState != DakRoutingState.WithHolder || dak.CurrentAssignment is not { IsActive: true, RecordStatus: RecordStatus.Active, ReceivedAt: not null } assignment || assignment.AssignedUserId != actor)
            throw new DakWorkflowException("Only the current confirmed received holder may perform this action.", 403);
        await RequireDeskMemberAsync(actor, assignment.OfficeDeskId, ct);
    }

    private async Task RequireDeskMemberAsync(Guid userId, Guid deskId, CancellationToken ct, int statusCode = 409)
    {
        if (!await db.UserDeskMemberships.AnyAsync(m => m.UserId == userId && m.OfficeDeskId == deskId && m.IsActive && m.RemovedAt == null &&
            m.RecordStatus == RecordStatus.Active && m.User.IsActive && m.User.RecordStatus == RecordStatus.Active &&
            m.OfficeDesk.IsActive && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct))
            throw new DakWorkflowException("Officer is not an active member of the nominated desk.", statusCode);
    }

    private async Task<DakTransfer> PendingTransferAsync(Dak dak, Guid transferId, CancellationToken ct)
    {
        var transfer = await db.DakTransfers.SingleOrDefaultAsync(t => t.Id == transferId && t.DakId == dak.Id, ct)
            ?? throw new DakWorkflowException("Transfer not found.", 404);
        if (transfer.State != DakTransferState.Pending || dak.RoutingState != DakRoutingState.InTransit)
            throw new DakWorkflowException("This delivery has already ended.", 409);
        return transfer;
    }

    private async Task LockCustodyEligibilityAsync(Guid actor, Guid? receiver, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return;
        var eligibleReceiver = receiver ?? actor;
        // The Dak lock precedes these shared locks. Eligibility revocations wait until the command commits.
        await db.AppUsers.FromSqlInterpolated($"SELECT * FROM \"AppUsers\" WHERE \"Id\" = {actor} OR \"Id\" = {eligibleReceiver} ORDER BY \"Id\" FOR SHARE").LoadAsync(ct);
        await db.UserRoles.FromSqlInterpolated($"SELECT * FROM \"UserRoles\" WHERE \"UserId\" = {actor} FOR SHARE").LoadAsync(ct);
        await db.Roles.FromSqlInterpolated($"SELECT r.* FROM \"Roles\" r JOIN \"UserRoles\" u ON r.\"Id\" = u.\"RoleId\" WHERE u.\"UserId\" = {actor} FOR SHARE OF r").LoadAsync(ct);
        await db.RolePermissions.FromSqlInterpolated($"SELECT p.* FROM \"RolePermissions\" p JOIN \"UserRoles\" u ON p.\"RoleId\" = u.\"RoleId\" WHERE u.\"UserId\" = {actor} FOR SHARE OF p").LoadAsync(ct);
        if (receiver.HasValue && receiver.Value != actor)
        {
            await db.UserRoles.FromSqlInterpolated($"SELECT * FROM \"UserRoles\" WHERE \"UserId\" = {eligibleReceiver} FOR SHARE").LoadAsync(ct);
            await db.Roles.FromSqlInterpolated($"SELECT r.* FROM \"Roles\" r JOIN \"UserRoles\" u ON r.\"Id\" = u.\"RoleId\" WHERE u.\"UserId\" = {eligibleReceiver} FOR SHARE OF r").LoadAsync(ct);
            await db.RolePermissions.FromSqlInterpolated($"SELECT p.* FROM \"RolePermissions\" p JOIN \"UserRoles\" u ON p.\"RoleId\" = u.\"RoleId\" WHERE u.\"UserId\" = {eligibleReceiver} FOR SHARE OF p").LoadAsync(ct);
            await db.WorkAllocations.FromSqlInterpolated($"SELECT * FROM \"WorkAllocations\" WHERE \"UserId\" = {eligibleReceiver} FOR SHARE").LoadAsync(ct);
            await db.WorkAllocationScopes.FromSqlInterpolated($"SELECT s.* FROM \"WorkAllocationScopes\" s JOIN \"WorkAllocations\" a ON a.\"Id\" = s.\"WorkAllocationId\" WHERE a.\"UserId\" = {eligibleReceiver} FOR SHARE OF s").LoadAsync(ct);
            await db.WorkDefinitions.FromSqlInterpolated($"SELECT w.* FROM \"WorkDefinitions\" w JOIN \"WorkAllocations\" a ON a.\"WorkDefinitionId\" = w.\"Id\" WHERE a.\"UserId\" = {eligibleReceiver} FOR SHARE OF w").LoadAsync(ct);
        }
        await db.UserDeskMemberships.FromSqlInterpolated($"SELECT * FROM \"UserDeskMemberships\" WHERE \"UserId\" = {actor} OR \"UserId\" = {eligibleReceiver} FOR SHARE").LoadAsync(ct);
        await db.OfficeDesks.FromSqlInterpolated($"SELECT d.* FROM \"OfficeDesks\" d JOIN \"UserDeskMemberships\" m ON d.\"Id\" = m.\"OfficeDeskId\" WHERE m.\"UserId\" = {actor} OR m.\"UserId\" = {eligibleReceiver} FOR SHARE OF d").LoadAsync(ct);
        await db.UserWorkstreamMemberships.FromSqlInterpolated($"SELECT * FROM \"UserWorkstreamMemberships\" WHERE \"UserId\" = {actor} FOR SHARE").LoadAsync(ct);
        await db.Workstreams.FromSqlInterpolated($"SELECT w.* FROM \"Workstreams\" w JOIN \"UserWorkstreamMemberships\" m ON w.\"Id\" = m.\"WorkstreamId\" WHERE m.\"UserId\" = {actor} FOR SHARE OF w").LoadAsync(ct);
    }

    private async Task<DakCommandResult> CustodyCommandAsync(Guid id, Guid actor, Guid requestId, int expectedRevision, string action,
        string permission, object payload, Func<Dak, DakMovement, DateTimeOffset, CancellationToken, Task<Guid?>> apply, CancellationToken ct)
    {
        if (requestId == Guid.Empty || expectedRevision < 0) throw new DakWorkflowException("A non-empty Idempotency-Key and valid expectedRevision are required.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, action, payload }))));
        var preserveChanges = db.Database.CurrentTransaction is not null;
        var eventId = Guid.NewGuid();
        DakCommandResult? result = null;
        async Task<DakCommandResult?> Replay(CancellationToken c)
        {
            var receipt = await db.DakWorkflowCommandReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.ActorUserId == actor && r.RequestId == requestId, c);
            if (receipt is null) return null;
            if (receipt.PayloadHash != hash) throw new DakWorkflowException("Idempotency-Key was already used for a different command.", 409);
            return JsonSerializer.Deserialize<DakCommandResult>(receipt.ResultJson)!;
        }
        try
        {
            return await ExecuteWorkflowTransactionAsync(async c =>
            {
                if (!preserveChanges) db.ChangeTracker.Clear();
                var query = db.Database.IsRelational()
                    ? db.Daks.FromSqlInterpolated($"SELECT * FROM \"Daks\" WHERE \"Id\" = {id} FOR UPDATE") : db.Daks.Where(d => d.Id == id);
                var dak = await query.Include(d => d.CurrentAssignment).ThenInclude(a => a!.OfficeDesk)
                    .Include(d => d.CurrentAssignment).ThenInclude(a => a!.AssignedUser).Include(d => d.Workstream).SingleOrDefaultAsync(c)
                    ?? throw new DakWorkflowException("Dak not found.", 404);
                await LockCustodyEligibilityAsync(actor, null, c);
                if (!await db.AppUsers.AnyAsync(u => u.Id == actor && u.IsActive && u.RecordStatus == RecordStatus.Active, c) ||
                    !await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                        join rp in db.RolePermissions on r.Id equals rp.RoleId join p in db.Permissions on rp.PermissionId equals p.Id
                        where ur.UserId == actor && r.IsActive && r.RecordStatus == RecordStatus.Active && p.Code == permission select rp).AnyAsync(c))
                    throw new DakWorkflowException("This workflow permission is not granted to the active actor.", 403);
                var replay = await Replay(c);
                if (replay is not null) return result = replay;
                if (!await new DakAuthorizationService(db).CanAccessDakAsync(id, permission, actor, c))
                    throw new DakWorkflowException("Workflow action is outside your authorized scope.", 403);
                if (dak.Revision != expectedRevision) throw new DakWorkflowException("Dak changed elsewhere; refresh before acting.", 409);
                var now = DateTimeOffset.UtcNow;
                var movement = new DakMovement
                {
                    Id = eventId, DakId = id, Action = Enum.Parse<DakMovementAction>(action), EventVersion = 1,
                    ActionByUserId = actor, ActionByDisplayNameSnapshot = (await db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == actor, c)).DisplayName,
                    ActionAt = now, FromDeskId = dak.CurrentAssignment?.OfficeDeskId, FromUserId = dak.CurrentAssignment?.AssignedUserId,
                    FromDeskCodeSnapshot = dak.CurrentAssignment?.OfficeDesk.Code, FromDeskNameSnapshot = dak.CurrentAssignment?.OfficeDesk.Name,
                    FromUserDisplayNameSnapshot = dak.CurrentAssignment?.AssignedUser?.DisplayName,
                    WorkstreamIdSnapshot = dak.WorkstreamId, WorkstreamNameSnapshot = dak.Workstream?.Name,
                    SequenceNumber = (await db.DakMovements.Where(m => m.DakId == id).MaxAsync(m => (int?)m.SequenceNumber, c) ?? 0) + 1
                };
                var before = new { Status = dak.Status.ToString(), RoutingState = dak.RoutingState.ToString(), PhysicalState = dak.PhysicalState.ToString(), dak.Revision, dak.ProcessingCycle, dak.HasPhysicalOriginal, dak.PhysicalOriginalUserId, dak.PhysicalOriginalDeskId, dak.PhysicalOriginalLocationNote, dak.PhysicalOriginalProvenanceNote, dak.ResolvedAt, dak.ResolvedByUserId, dak.ResolutionRemarks };
                var transferId = await apply(dak, movement, now, c);
                dak.Revision++;
                result = new DakCommandResult(eventId, id, transferId, dak.Revision, dak.Status.ToString(), dak.RoutingState.ToString(),
                    dak.PhysicalState.ToString(), dak.CurrentAssignment is { IsActive: true, RecordStatus: RecordStatus.Active, ReceivedAt: not null } ? dak.CurrentAssignment.AssignedUserId : null, dak.ProcessingCycle, dak.PhysicalOriginalUserId, dak.PhysicalOriginalDeskId, dak.PhysicalOriginalLocationNote);
                movement.StateSnapshotJson = JsonSerializer.Serialize(new { before, after = result, command = payload }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                db.DakMovements.Add(movement);
                db.AuditLogs.Add(new AuditLog { Id = eventId, EntityType = "Dak", EntityId = id, Action = action,
                    ChangedAt = now, ChangedBy = actor.ToString(), OldValues = JsonSerializer.Serialize(before), NewValues = JsonSerializer.Serialize(result) });
                db.DakWorkflowCommandReceipts.Add(new DakWorkflowCommandReceipt { Id = eventId, DakId = id, ActorUserId = actor,
                    RequestId = requestId, Action = action, PayloadHash = hash, ResultJson = JsonSerializer.Serialize(result), CreatedAt = now });
                await db.SaveChangesAsync(c);
                return result;
            }, async c => { db.ChangeTracker.Clear(); result = await Replay(c); return result is not null; }, ct);
        }
        catch (DbUpdateConcurrencyException) { throw new DakWorkflowException("Dak changed elsewhere; refresh before acting.", 409); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return await Replay(ct) ?? throw new DakWorkflowException("A competing workflow command already committed.", 409);
        }
    }
}
