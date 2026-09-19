namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed record OutwardListAuthResult(bool HasPermission, IQueryable<Outward> Query);

public interface IOutwardAuthorizationService
{
    Task<OutwardListAuthResult> AuthorizeListQueryAsync(IQueryable<Outward> query, Guid userId, CancellationToken ct = default);
    Task<bool> CanCreateAsync(Guid issuingDeskId, Guid? workstreamId, Guid userId, CancellationToken ct = default);
    Task<bool> CanAccessOutwardAsync(Guid outwardId, string permissionCode, Guid userId, CancellationToken ct = default);
    Task<bool> CanAccessDocumentAsync(Guid outwardId, Guid documentId, Guid userId, CancellationToken ct = default);
}

public sealed class OutwardAuthorizationService(LacDbContext db) : IOutwardAuthorizationService
{
    public async Task<OutwardListAuthResult> AuthorizeListQueryAsync(IQueryable<Outward> query, Guid userId, CancellationToken ct = default)
    {
        // 1. Verify user exists, is active, and has active record status
        var isUserActive = await db.AppUsers
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive)
            return new OutwardListAuthResult(false, query.Where(_ => false));

        // 2. Fetch distinct ScopeModes across active roles for Outward.View
        var scopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.OutwardView
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (scopes.Count == 0)
            return new OutwardListAuthResult(false, query.Where(_ => false));

        // ScopeMode.All grants unrestricted access across all active records
        if (scopes.Contains(ScopeMode.All))
            return new OutwardListAuthResult(true, query);

        var hasWorkstream = scopes.Contains(ScopeMode.Workstream);
        var hasAssigned = scopes.Contains(ScopeMode.Assigned);

        // Retrieve active workstream memberships
        var userWorkstreamIds = hasWorkstream
            ? await db.UserWorkstreamMemberships
                .AsNoTracking()
                .Where(m => m.UserId == userId && m.IsActive && m.Workstream.IsActive && m.Workstream.RecordStatus == RecordStatus.Active)
                .Select(m => m.WorkstreamId)
                .ToListAsync(ct)
            : new List<Guid>();

        // Retrieve active desk memberships (Assigned for Outward means Issuing Desk authority)
        var userDeskIds = hasAssigned
            ? await db.UserDeskMemberships
                .AsNoTracking()
                .Where(m => m.UserId == userId
                         && m.IsActive
                         && m.RemovedAt == null
                         && m.RecordStatus == RecordStatus.Active
                         && m.OfficeDesk.IsActive
                         && m.OfficeDesk.RecordStatus == RecordStatus.Active)
                .Select(m => m.OfficeDeskId)
                .ToListAsync(ct)
            : new List<Guid>();

        // ScopeMode.Own fails closed: official correspondence belongs to the office, not individuals
        var filtered = query.Where(o =>
            (hasWorkstream && o.WorkstreamId.HasValue && userWorkstreamIds.Contains(o.WorkstreamId.Value))
            || (hasAssigned && userDeskIds.Contains(o.IssuingDeskId))
        );

        return new OutwardListAuthResult(true, filtered);
    }

    public async Task<bool> CanCreateAsync(Guid issuingDeskId, Guid? workstreamId, Guid userId, CancellationToken ct = default)
    {
        // 1. Verify active user
        var isUserActive = await db.AppUsers
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        // 2. Strict Institutional Rule: EVERY Outward creation (including ScopeMode.All)
        // requires the caller to hold a LIVE active membership in the selected IssuingDeskId.
        var isDeskMember = await db.UserDeskMemberships
            .AsNoTracking()
            .AnyAsync(m => m.UserId == userId
                        && m.OfficeDeskId == issuingDeskId
                        && m.IsActive
                        && m.RemovedAt == null
                        && m.RecordStatus == RecordStatus.Active
                        && m.OfficeDesk.IsActive
                        && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);

        if (!isDeskMember) return false;

        // 3. Fetch distinct ScopeModes for Outward.Create
        var scopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.OutwardCreate
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (scopes.Count == 0) return false;

        // ScopeMode.All and ScopeMode.Assigned are satisfied since caller is a verified active desk member
        if (scopes.Contains(ScopeMode.All) || scopes.Contains(ScopeMode.Assigned))
            return true;

        // ScopeMode.Workstream requires selected WorkstreamId to be in caller's active workstreams
        if (scopes.Contains(ScopeMode.Workstream) && workstreamId.HasValue)
        {
            var isWorkstreamMember = await db.UserWorkstreamMemberships
                .AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.WorkstreamId == workstreamId.Value
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);

            if (isWorkstreamMember) return true;
        }

        // ScopeMode.Own fails closed
        return false;
    }

    public async Task<bool> CanAccessOutwardAsync(Guid outwardId, string permissionCode, Guid userId, CancellationToken ct = default)
    {
        // 1. Verify active user
        var isUserActive = await db.AppUsers
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        // 2. Fetch outward record (must have RecordStatus == Active)
        var outward = await db.Outwards
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == outwardId && o.RecordStatus == RecordStatus.Active, ct);

        if (outward is null) return false;

        // 3. Fetch distinct ScopeModes for permissionCode
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

        // Check Assigned scope (Issuing Desk membership)
        if (scopes.Contains(ScopeMode.Assigned))
        {
            var isDeskMember = await db.UserDeskMemberships
                .AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.OfficeDeskId == outward.IssuingDeskId
                            && m.IsActive
                            && m.RemovedAt == null
                            && m.RecordStatus == RecordStatus.Active
                            && m.OfficeDesk.IsActive
                            && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);

            if (isDeskMember) return true;
        }

        // Check Workstream scope
        if (scopes.Contains(ScopeMode.Workstream) && outward.WorkstreamId.HasValue)
        {
            var isWorkstreamMember = await db.UserWorkstreamMemberships
                .AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.WorkstreamId == outward.WorkstreamId.Value
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);

            if (isWorkstreamMember) return true;
        }

        // ScopeMode.Own fails closed
        return false;
    }

    public async Task<bool> CanAccessDocumentAsync(Guid outwardId, Guid documentId, Guid userId, CancellationToken ct = default)
    {
        if (!await CanAccessOutwardAsync(outwardId, PermissionCodes.OutwardView, userId, ct))
            return false;

        var isMain = await db.Outwards
            .AsNoTracking()
            .AnyAsync(o => o.Id == outwardId && o.MainDocumentId == documentId && o.RecordStatus == RecordStatus.Active, ct);

        if (isMain) return true;

        var isAttachment = await db.OutwardAttachments
            .AsNoTracking()
            .AnyAsync(a => a.OutwardId == outwardId && a.DocumentId == documentId && a.RecordStatus == RecordStatus.Active, ct);

        return isAttachment;
    }
}
