namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public interface ICourtAuthorizationService
{
    Task<bool> CanViewCourtReferencesAsync(Guid userId, CancellationToken ct = default);
    Task<bool> CanEditCourtReferencesAsync(Guid userId, CancellationToken ct = default);
    Task<bool> CanViewCourtCaseAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default);
    Task<string?> GetCourtCaseNavigationUrlAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default);
}

public sealed class CourtAuthorizationService(LacDbContext db) : ICourtAuthorizationService
{
    public async Task<bool> CanViewCourtReferencesAsync(Guid userId, CancellationToken ct = default)
    {
        return await CheckCourtPermissionAsync(userId, PermissionCodes.AwardView, ct);
    }

    public async Task<bool> CanEditCourtReferencesAsync(Guid userId, CancellationToken ct = default)
    {
        return await CheckCourtPermissionAsync(userId, PermissionCodes.AwardEdit, ct);
    }

    public async Task<bool> CanViewCourtCaseAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default)
    {
        var caseExists = await db.CourtCases.AsNoTracking().AnyAsync(c => c.Id == courtCaseId, ct);
        if (!caseExists) return false;

        return await CanViewCourtReferencesAsync(userId, ct);
    }

    public async Task<string?> GetCourtCaseNavigationUrlAsync(Guid courtCaseId, Guid userId, CancellationToken ct = default)
    {
        var canView = await CanViewCourtCaseAsync(courtCaseId, userId, ct);
        if (!canView) return null;

        // Check if court case is linked to an Award
        var linkedAward = await db.Set<CourtCaseAward>().AsNoTracking()
            .Where(x => x.CourtCaseId == courtCaseId)
            .Select(x => (Guid?)x.AwardId)
            .FirstOrDefaultAsync(ct);

        if (linkedAward.HasValue)
        {
            return $"/awards/{linkedAward.Value}";
        }

        // No standalone /court-cases/{id} frontend route exists; return null target
        return null;
    }

    private async Task<bool> CheckCourtPermissionAsync(Guid userId, string permissionCode, CancellationToken ct)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var scopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == permissionCode
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (scopes.Count == 0) return false;

        if (scopes.Contains(ScopeMode.All)) return true;

        // Check workstream membership in COURT_REFERENCES or AWARD
        if (scopes.Contains(ScopeMode.Workstream))
        {
            var isMember = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active
                            && (m.Workstream.Code == WorkstreamCodes.CourtReferences || m.Workstream.Code == WorkstreamCodes.Award), ct);

            if (isMember) return true;
        }

        // Check assigned desk membership in desks under COURT_REFERENCES or AWARD
        if (scopes.Contains(ScopeMode.Assigned))
        {
            var isDeskMember = await db.UserDeskMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.IsActive
                            && m.RemovedAt == null
                            && m.RecordStatus == RecordStatus.Active
                            && m.OfficeDesk.IsActive
                            && m.OfficeDesk.RecordStatus == RecordStatus.Active
                            && m.OfficeDesk.Workstream != null
                            && (m.OfficeDesk.Workstream.Code == WorkstreamCodes.CourtReferences || m.OfficeDesk.Workstream.Code == WorkstreamCodes.Award), ct);

            if (isDeskMember) return true;
        }

        // ScopeMode.Own fails closed for official Court references
        return false;
    }
}
