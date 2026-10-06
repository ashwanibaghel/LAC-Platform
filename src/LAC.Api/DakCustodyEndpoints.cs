namespace LAC.Api;

using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

public static partial class DakEndpoints
{
    private static JsonElement PublicCustodyStateChanges(string snapshot)
    {
        using var document = JsonDocument.Parse(snapshot);
        var root = document.RootElement;
        bool? completion = root.GetProperty("command").TryGetProperty("completionAttested", out var attestation) ? attestation.GetBoolean() : null;
        // Expose custody/location transitions and completion evidence, not request keys or internal payloads.
        return JsonSerializer.SerializeToElement(new { before = root.GetProperty("before"), after = root.GetProperty("after"), completionAttested = completion });
    }

    private static Guid CustodyRequestKey(HttpRequest request)
    {
        var values = request.Headers["Idempotency-Key"];
        if (values.Count != 1 || !Guid.TryParse(values[0], out var key) || key == Guid.Empty)
            throw new DakWorkflowException("One non-empty UUID Idempotency-Key is mandatory for workflow commands.");
        return key;
    }

    private static void MapCustodyEndpoints(RouteGroupBuilder dak)
    {
        dak.MapPost("/{id:guid}/transfers", async (Guid id, SendDakRequest body, HttpRequest request, DakWorkflowService workflow,
            ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } actor) return Results.Unauthorized();
            if (!Enum.TryParse<DakMovementAction>(body.Action, true, out var action) || !Enum.IsDefined(action) ||
                !Enum.TryParse<DakDestinationKind>(body.DestinationKind, true, out var kind) || !Enum.IsDefined(kind))
                return Results.BadRequest(new { message = "Invalid dispatch action or destination kind." });
            return Results.Ok(await workflow.SendAsync(id, new SendDakCommand(action, body.ToDeskId, body.ToUserId, kind,
                body.IncludesPhysicalOriginal, body.Remarks, body.Instructions, body.ExpectedRevision, CustodyRequestKey(request), body.RemarksKind), actor, ct));
        }); // Action-specific permission and scope are rechecked under the aggregate lock.
        dak.MapPost("/{id:guid}/transfers/{transferId:guid}/receive", async (Guid id, Guid transferId, ReceiveDakRequest body,
            HttpRequest request, DakWorkflowService workflow, ICurrentUserContext user, CancellationToken ct) =>
            user.UserId is not { } actor ? Results.Unauthorized() : Results.Ok(await workflow.ReceiveAsync(id, transferId,
                new ReceiveDakCommand(body.ExpectedRevision, CustodyRequestKey(request), body.PhysicalReceiptConfirmed), actor, ct)));
        dak.MapPost("/{id:guid}/transfers/{transferId:guid}/pull-back", async (Guid id, Guid transferId, ReasonDakRequest body,
            HttpRequest request, DakWorkflowService workflow, ICurrentUserContext user, CancellationToken ct) =>
            user.UserId is not { } actor ? Results.Unauthorized() : Results.Ok(await workflow.PullBackAsync(id, transferId,
                new PullBackDakCommand(body.Reason, body.ExpectedRevision, CustodyRequestKey(request)), actor, ct)));
        dak.MapPost("/{id:guid}/transfers/{transferId:guid}/confirm-return", async (Guid id, Guid transferId, PhysicalReturnDakRequest body,
            HttpRequest request, DakWorkflowService workflow, ICurrentUserContext user, CancellationToken ct) =>
            user.UserId is not { } actor ? Results.Unauthorized() : Results.Ok(await workflow.ConfirmReturnAsync(id, transferId,
                new ConfirmReturnDakCommand(body.Provenance, body.ExpectedRevision, CustodyRequestKey(request)), actor, ct)));
        dak.MapPost("/{id:guid}/resolve", async (Guid id, ResolveDakRequest body, HttpRequest request,
            DakWorkflowService workflow, ICurrentUserContext user, CancellationToken ct) =>
            user.UserId is not { } actor ? Results.Unauthorized() : Results.Ok(await workflow.ResolveAsync(id,
                new ResolveDakCommand(body.CompletionAttested, body.Remarks, body.ExpectedRevision, CustodyRequestKey(request)), actor, ct)));
        dak.MapPost("/{id:guid}/reopen", async (Guid id, ReasonDakRequest body, HttpRequest request,
            DakWorkflowService workflow, ICurrentUserContext user, CancellationToken ct) =>
            user.UserId is not { } actor ? Results.Unauthorized() : Results.Ok(await workflow.ReopenAsync(id,
                new ReopenDakCommand(body.Reason, body.ExpectedRevision, CustodyRequestKey(request)), actor, ct)));
        dak.MapPost("/{id:guid}/custody/confirm", async (Guid id, RevisionDakRequest body, HttpRequest request,
            DakWorkflowService workflow, ICurrentUserContext user, CancellationToken ct) =>
            user.UserId is not { } actor ? Results.Unauthorized() : Results.Ok(await workflow.ConfirmCustodyAsync(id,
                new ConfirmDakCustodyCommand(body.ExpectedRevision, CustodyRequestKey(request)), actor, ct)));
        dak.MapGet("/{id:guid}/transfers", async (Guid id, LacDbContext db, IDakAuthorizationService auth,
            ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } actor) return Results.Unauthorized();
            if (!await auth.CanAccessDakAsync(id, PermissionCodes.DakView, actor, ct)) return Results.Forbid();
            var items = await db.DakTransfers.AsNoTracking().Where(t => t.DakId == id).OrderByDescending(t => t.SentAt)
                .Select(t => new { t.Id, t.SenderUserId, t.FromHolderUserId, t.FromDeskId, t.ToDeskId, t.ToUserId,
                    destinationKind = t.DestinationKind.ToString(), purpose = t.Purpose.ToString(), state = t.State.ToString(),
                    t.IncludesPhysicalOriginal, t.SentAt, t.ReceivedAt, t.PhysicalReceivedAt, t.PulledBackAt, t.PullBackReason,
                    t.PhysicalReturnedAt, t.PhysicalReturnProvenance, t.Remarks, t.Instructions }).ToListAsync(ct);
            return Results.Ok(new { items });
        }).RequirePermission(PermissionCodes.DakView);
        dak.MapGet("/delivery-queue", async (string? bucket, int? page, int? pageSize, LacDbContext db, IDakAuthorizationService auth,
            ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } actor) return Results.Unauthorized();
            var allowed = await auth.AuthorizeListQueryAsync(db.Daks.AsNoTracking().Where(d => d.RecordStatus == RecordStatus.Active), PermissionCodes.DakView, actor, ct);
            if (!allowed.HasPermission) return Results.Forbid();
            var query = allowed.Query;
            switch (bucket?.ToLowerInvariant() ?? "incoming")
            {
                case "incoming": query = query.Where(d => db.DakTransfers.Any(t => t.DakId == d.Id && t.State == DakTransferState.Pending && t.ToUserId == actor)); break;
                case "sent": query = query.Where(d => db.DakTransfers.Any(t => t.DakId == d.Id && t.State == DakTransferState.Pending && t.SenderUserId == actor)); break;
                case "with-me": query = query.Where(d => d.Status != DakStatus.Resolved && d.RoutingState == DakRoutingState.WithHolder && d.CurrentAssignment != null && d.CurrentAssignment.IsActive && d.CurrentAssignment.AssignedUserId == actor); break;
                case "resolved": query = query.Where(d => d.Status == DakStatus.Resolved); break;
                case "attention":
                    query = query.Where(d => d.RoutingState == DakRoutingState.LegacyUnconfirmed || d.PhysicalState == DakPhysicalState.ReturnPending ||
                        d.CurrentAssignment != null && d.CurrentAssignment.IsActive &&
                            (d.CurrentAssignment.RecordStatus != RecordStatus.Active || d.CurrentAssignment.AssignedUserId != null &&
                                !db.UserDeskMemberships.Any(m => m.UserId == d.CurrentAssignment.AssignedUserId && m.OfficeDeskId == d.CurrentAssignment.OfficeDeskId && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active && m.User.IsActive && m.User.RecordStatus == RecordStatus.Active && m.OfficeDesk.IsActive && m.OfficeDesk.RecordStatus == RecordStatus.Active)) ||
                        d.Transfers.Any(t => t.State == DakTransferState.Pending &&
                            (!db.UserDeskMemberships.Any(m => m.UserId == t.ToUserId && m.OfficeDeskId == t.ToDeskId && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active && m.User.IsActive && m.User.RecordStatus == RecordStatus.Active && m.OfficeDesk.IsActive && m.OfficeDesk.RecordStatus == RecordStatus.Active && m.OfficeDesk.Purpose == (t.DestinationKind == DakDestinationKind.RecordRoom ? OfficeDeskPurpose.RecordRoom : OfficeDeskPurpose.General)) ||
                             !db.UserRoles.Any(u => u.UserId == t.ToUserId && u.Role.IsActive && u.Role.RecordStatus == RecordStatus.Active && u.Role.RolePermissions.Any(p => p.Permission.Code == PermissionCodes.DakReceive && (p.ScopeMode == ScopeMode.All || p.ScopeMode == ScopeMode.Workstream || p.ScopeMode == ScopeMode.Assigned))))));
                    break;
                default: return Results.BadRequest(new { message = "Unknown delivery queue bucket." });
            }
            var p = Math.Max(0, page ?? 0); var size = Math.Clamp(pageSize ?? 25, 1, 100);
            var totalCount = await query.CountAsync(ct);
            var items = await query.OrderByDescending(d => d.ReceivedDate).ThenBy(d => d.Id).Skip(p * size).Take(size).Select(d => new { d.Id, d.DiaryNumber,
                status = d.Status.ToString(), routingState = d.RoutingState.ToString(), physicalState = d.PhysicalState.ToString(), d.Revision }).ToListAsync(ct);
            return Results.Ok(new { items, totalCount, page = p, pageSize = size });
        }).RequirePermission(PermissionCodes.DakView);
    }
}

public sealed record SendDakRequest(string Action, Guid ToDeskId, Guid ToUserId, string DestinationKind,
    bool IncludesPhysicalOriginal, int ExpectedRevision, string? Remarks = null, string? Instructions = null, string RemarksKind = "Text");
public sealed record ReceiveDakRequest(int ExpectedRevision, bool PhysicalReceiptConfirmed = false);
public sealed record ReasonDakRequest(string Reason, int ExpectedRevision);
public sealed record PhysicalReturnDakRequest(string Provenance, int ExpectedRevision);
public sealed record ResolveDakRequest(bool CompletionAttested, string Remarks, int ExpectedRevision);
public sealed record RevisionDakRequest(int ExpectedRevision);
