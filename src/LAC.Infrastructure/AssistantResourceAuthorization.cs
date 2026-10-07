using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

// Additional live bounds for secondary record/context checks inside workflow services.
// Returning true for an officer does not grant access: the existing service still checks its policy.
public static class AssistantResourceAuthorization
{
    public static Task<bool> IsAssistantAsync(LacDbContext db, Guid userId, CancellationToken ct) =>
        db.RequestOfficerUserId == userId ? Task.FromResult(false)
            : db.AppUsers.AnyAsync(x => x.Id == userId && x.SupervisingOfficerId != null, ct);
    public static async Task<bool> CheckAsync(LacDbContext db, Guid userId, string permission,
        OperationalWorkKind? kind, Guid? workstreamId, IReadOnlyList<Guid> villages, Guid? deskId, CancellationToken ct)
    {
        if (db.RequestOfficerUserId == userId) return true;
        var user = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user?.SupervisingOfficerId is null) return true;
        var access = new AccessControlService(db, db.CurrentUser!);
        var code = kind switch { OperationalWorkKind.Court => WorkstreamCodes.CourtReferences, OperationalWorkKind.Award => WorkstreamCodes.Award,
            OperationalWorkKind.LandRecords => WorkstreamCodes.LandRecords, OperationalWorkKind.Correspondence => WorkstreamCodes.DakCorrespondence, _ => null };
        if (!await access.CanForUserAsync(userId, permission, new AccessResourceContext(WorkstreamId: workstreamId, WorkstreamCode: code, AssignedDeskId: deskId), ct)) return false;
        return await new WorkAllocationService(db, db.AuthorizationClock).CanWorkAsync(userId, kind, workstreamId, villages, ct);
    }
}
