namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

// Receipt participants can use All/Assigned/Workstream scopes. Own alone does not
// authorize receipt of a pending delivery in DakAuthorizationService.
public static class DakRecipientEligibility
{
    public static IQueryable<UserDeskMembership> Memberships(LacDbContext db) => db.UserDeskMemberships
        .Where(m => m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active
            && m.User.IsActive && m.User.RecordStatus == RecordStatus.Active
            && m.OfficeDesk.IsActive && m.OfficeDesk.RecordStatus == RecordStatus.Active
            && db.UserRoles.Any(ur => ur.UserId == m.UserId && ur.Role.IsActive && ur.Role.RecordStatus == RecordStatus.Active
                && ur.Role.RolePermissions.Any(p => p.Permission.Code == PermissionCodes.DakReceive
                    && (p.ScopeMode == ScopeMode.All || p.ScopeMode == ScopeMode.Assigned || p.ScopeMode == ScopeMode.Workstream))));

    public static async Task<bool> WithinReceiptBoundsAsync(LacDbContext db, Dak dak, Guid userId, CancellationToken ct)
    {
        var villages = await db.DakVillageLinks.Where(l => l.DakId == dak.Id && l.RecordStatus == RecordStatus.Active)
            .Select(l => l.VillageId).ToListAsync(ct);
        var kind = dak.WorkstreamId.HasValue ? (OperationalWorkKind?)null : OperationalWorkKind.Correspondence;
        if (await AssistantResourceAuthorization.IsAssistantAsync(db, userId, ct))
            return await AssistantResourceAuthorization.CheckAsync(db, userId, PermissionCodes.DakReceive,
                kind, dak.WorkstreamId, villages, dak.CurrentAssignment?.OfficeDeskId, ct);
        // The API already requires live work responsibility on RECEIVE. Check the
        // same bound before dispatch to avoid nominating someone who cannot accept.
        return await new WorkAllocationService(db, db.AuthorizationClock).CanWorkAsync(userId, kind, dak.WorkstreamId, villages, ct);
    }

    public static async Task<bool> CanReceiveAsync(LacDbContext db, Dak dak, Guid userId, Guid deskId, CancellationToken ct) =>
        await Memberships(db).AnyAsync(m => m.UserId == userId && m.OfficeDeskId == deskId, ct)
            && await WithinReceiptBoundsAsync(db, dak, userId, ct);
}
