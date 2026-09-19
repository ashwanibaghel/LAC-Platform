namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed record DakListAuthorizationResult(bool HasPermission, IQueryable<Dak> Query);

public interface IDakAuthorizationService
{
    Task<DakListAuthorizationResult> AuthorizeListQueryAsync(IQueryable<Dak> query, string permissionCode, Guid userId, CancellationToken ct = default);
    Task<bool> CanAccessDakAsync(Guid dakId, string permissionCode, Guid userId, CancellationToken ct = default);
    Task<bool> CanAccessDocumentAsync(Guid dakId, Guid documentId, Guid userId, CancellationToken ct = default);
}

public sealed class DakAuthorizationService(LacDbContext db) : IDakAuthorizationService
{
    public async Task<DakListAuthorizationResult> AuthorizeListQueryAsync(IQueryable<Dak> query, string permissionCode, Guid userId, CancellationToken ct = default)
    {
        // 1. Verify user exists and is active
        var isUserActive = await db.AppUsers
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive)
            return new DakListAuthorizationResult(false, query.Where(_ => false));

        // 2. Fetch all ScopeModes across active roles for this permission code
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

        if (scopes.Count == 0)
            return new DakListAuthorizationResult(false, query.Where(_ => false));

        // 3. All scope grants unrestricted access
        if (scopes.Contains(ScopeMode.All))
            return new DakListAuthorizationResult(true, query);

        var hasWorkstream = scopes.Contains(ScopeMode.Workstream);
        var hasAssigned = scopes.Contains(ScopeMode.Assigned);

        // Retrieve active workstreams
        var userWorkstreamIds = hasWorkstream
            ? await db.UserWorkstreamMemberships
                .AsNoTracking()
                .Where(m => m.UserId == userId && m.IsActive && m.Workstream.IsActive && m.Workstream.RecordStatus == RecordStatus.Active)
                .Select(m => m.WorkstreamId)
                .ToListAsync(ct)
            : new List<Guid>();

        // Check active membership in DAK_CORRESPONDENCE for intake queue access
        var isInDakCorrespondence = hasWorkstream && await db.UserWorkstreamMemberships
            .AsNoTracking()
            .AnyAsync(m => m.UserId == userId 
                        && m.IsActive 
                        && m.Workstream.Code == WorkstreamCodes.DakCorrespondence 
                        && m.Workstream.IsActive 
                        && m.Workstream.RecordStatus == RecordStatus.Active, ct);

        // Retrieve active desks
        var userDeskIds = hasAssigned
            ? await db.UserDeskMemberships
                .AsNoTracking()
                .Where(m => m.UserId == userId 
                         && m.IsActive 
                         && m.RemovedAt == null 
                         && m.OfficeDesk.IsActive 
                         && m.OfficeDesk.RecordStatus == RecordStatus.Active)
                .Select(m => m.OfficeDeskId)
                .ToListAsync(ct)
            : new List<Guid>();

        // 4. Build UNION filter:
        // - Functional Workstream match
        // - Active Assigned Desk match
        // - Intake Queue match (Registered + unassigned + user has DAK_CORRESPONDENCE)
        var filtered = query.Where(d =>
            (hasWorkstream && d.WorkstreamId.HasValue && userWorkstreamIds.Contains(d.WorkstreamId.Value))
            || (hasAssigned && d.CurrentAssignment != null && d.CurrentAssignment.IsActive && userDeskIds.Contains(d.CurrentAssignment.OfficeDeskId))
            || (hasWorkstream && isInDakCorrespondence && d.Status == DakStatus.Registered && (d.CurrentAssignment == null || !d.CurrentAssignment.IsActive))
        );

        return new DakListAuthorizationResult(true, filtered);
    }

    public async Task<bool> CanAccessDakAsync(Guid dakId, string permissionCode, Guid userId, CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var dak = await db.Daks
            .AsNoTracking()
            .Include(d => d.CurrentAssignment)
            .FirstOrDefaultAsync(d => d.Id == dakId, ct);

        if (dak is null) return false;

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

        var hasWorkstream = scopes.Contains(ScopeMode.Workstream);
        var hasAssigned = scopes.Contains(ScopeMode.Assigned);

        // Check Workstream scope
        if (hasWorkstream)
        {
            // Functional workstream classification match
            if (dak.WorkstreamId.HasValue)
            {
                var hasWs = await db.UserWorkstreamMemberships
                    .AsNoTracking()
                    .AnyAsync(m => m.UserId == userId 
                                && m.WorkstreamId == dak.WorkstreamId.Value 
                                && m.IsActive 
                                && m.Workstream.IsActive 
                                && m.Workstream.RecordStatus == RecordStatus.Active, ct);
                if (hasWs) return true;
            }

            // Intake queue match: Registered + unassigned
            var isUnassignedRegistered = dak.Status == DakStatus.Registered && (dak.CurrentAssignment == null || !dak.CurrentAssignment.IsActive);
            if (isUnassignedRegistered)
            {
                var hasDakCorr = await db.UserWorkstreamMemberships
                    .AsNoTracking()
                    .AnyAsync(m => m.UserId == userId 
                                && m.Workstream.Code == WorkstreamCodes.DakCorrespondence 
                                && m.IsActive 
                                && m.Workstream.IsActive 
                                && m.Workstream.RecordStatus == RecordStatus.Active, ct);
                if (hasDakCorr) return true;
            }
        }

        // Check Assigned scope (Active assignment ONLY)
        if (hasAssigned && dak.CurrentAssignment is { IsActive: true })
        {
            var hasDesk = await db.UserDeskMemberships
                .AsNoTracking()
                .AnyAsync(m => m.UserId == userId 
                            && m.OfficeDeskId == dak.CurrentAssignment.OfficeDeskId 
                            && m.IsActive 
                            && m.RemovedAt == null 
                            && m.OfficeDesk.IsActive 
                            && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);
            if (hasDesk) return true;
        }

        return false;
    }

    public async Task<bool> CanAccessDocumentAsync(Guid dakId, Guid documentId, Guid userId, CancellationToken ct = default)
    {
        if (!await CanAccessDakAsync(dakId, PermissionCodes.DakView, userId, ct))
            return false;

        var isMain = await db.Daks
            .AsNoTracking()
            .AnyAsync(d => d.Id == dakId && d.MainDocumentId == documentId, ct);
        if (isMain) return true;

        var isAttachment = await db.DakAttachments
            .AsNoTracking()
            .AnyAsync(a => a.DakId == dakId && a.DocumentId == documentId && a.RecordStatus == RecordStatus.Active, ct);

        return isAttachment;
    }
}
