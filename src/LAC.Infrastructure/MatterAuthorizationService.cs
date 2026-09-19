namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed record MatterListAuthorizationResult(bool HasPermission, IQueryable<Matter> Query);

public interface IMatterAuthorizationService
{
    Task<MatterListAuthorizationResult> AuthorizeListQueryAsync(
        IQueryable<Matter> query,
        string permissionCode,
        Guid userId,
        bool includeArchived = false,
        CancellationToken ct = default);

    Task<bool> CanAccessMatterAsync(
        Guid matterId,
        string permissionCode,
        Guid userId,
        CancellationToken ct = default);

    Task<bool> CanCreateMatterInWorkstreamAsync(
        Guid workstreamId,
        Guid userId,
        CancellationToken ct = default);

    Task<bool> CanReclassifyMatterAsync(
        Guid matterId,
        Guid targetWorkstreamId,
        Guid userId,
        CancellationToken ct = default);

    Task<bool> CanAccessDraftAsync(
        Guid draftId,
        string draftPermissionCode,
        Guid userId,
        CancellationToken ct = default);

    Task<bool> CanAccessMatterDraftCapabilityAsync(
        Guid matterId,
        string draftPermissionCode,
        Guid userId,
        CancellationToken ct = default);

    Task<bool> CanAccessMatterDocumentAsync(
        Guid matterId,
        Guid documentId,
        Guid userId,
        CancellationToken ct = default);
}

public sealed class MatterAuthorizationService(LacDbContext db) : IMatterAuthorizationService
{
    public async Task<MatterListAuthorizationResult> AuthorizeListQueryAsync(
        IQueryable<Matter> query,
        string permissionCode,
        Guid userId,
        bool includeArchived = false,
        CancellationToken ct = default)
    {
        if (!includeArchived)
        {
            query = query.Where(m => m.RecordStatus == RecordStatus.Active);
        }

        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive)
            return new MatterListAuthorizationResult(false, query.Where(_ => false));

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
            return new MatterListAuthorizationResult(false, query.Where(_ => false));

        if (scopes.Contains(ScopeMode.All))
            return new MatterListAuthorizationResult(true, query);

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var userWorkstreamIds = await db.UserWorkstreamMemberships.AsNoTracking()
                .Where(m => m.UserId == userId
                         && m.IsActive
                         && m.Workstream.IsActive
                         && m.Workstream.RecordStatus == RecordStatus.Active)
                .Select(m => m.WorkstreamId)
                .ToListAsync(ct);

            var filtered = query.Where(m => m.WorkstreamId.HasValue && userWorkstreamIds.Contains(m.WorkstreamId.Value));
            return new MatterListAuthorizationResult(true, filtered);
        }

        return new MatterListAuthorizationResult(false, query.Where(_ => false));
    }

    public async Task<bool> CanAccessMatterAsync(
        Guid matterId,
        string permissionCode,
        Guid userId,
        CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var matter = await db.Matters.AsNoTracking().SingleOrDefaultAsync(m => m.Id == matterId, ct);
        if (matter is null) return false;

        if (matter.RecordStatus != RecordStatus.Active)
            return false;

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

        if (scopes.Contains(ScopeMode.Workstream))
        {
            if (!matter.WorkstreamId.HasValue) return false;

            return await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.WorkstreamId == matter.WorkstreamId.Value
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);
        }

        return false;
    }

    public async Task<bool> CanCreateMatterInWorkstreamAsync(
        Guid workstreamId,
        Guid userId,
        CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var isWsActive = await db.Workstreams.AsNoTracking()
            .AnyAsync(w => w.Id == workstreamId && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);

        if (!isWsActive) return false;

        var scopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.MatterCreate
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (scopes.Count == 0) return false;

        if (scopes.Contains(ScopeMode.All)) return true;

        if (scopes.Contains(ScopeMode.Workstream))
        {
            return await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.WorkstreamId == workstreamId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);
        }

        return false;
    }

    public async Task<bool> CanReclassifyMatterAsync(
        Guid matterId,
        Guid targetWorkstreamId,
        Guid userId,
        CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var targetWsActive = await db.Workstreams.AsNoTracking()
            .AnyAsync(w => w.Id == targetWorkstreamId && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);

        if (!targetWsActive) return false;

        var matter = await db.Matters.AsNoTracking().SingleOrDefaultAsync(m => m.Id == matterId, ct);
        if (matter is null || matter.RecordStatus != RecordStatus.Active) return false;

        var scopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.MatterEdit
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (scopes.Count == 0) return false;

        if (scopes.Contains(ScopeMode.All)) return true;

        // Legacy null WorkstreamId can ONLY be classified by ScopeMode.All
        if (!matter.WorkstreamId.HasValue) return false;

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var userWorkstreamIds = await db.UserWorkstreamMemberships.AsNoTracking()
                .Where(m => m.UserId == userId
                         && m.IsActive
                         && m.Workstream.IsActive
                         && m.Workstream.RecordStatus == RecordStatus.Active)
                .Select(m => m.WorkstreamId)
                .ToListAsync(ct);

            return userWorkstreamIds.Contains(matter.WorkstreamId.Value) && userWorkstreamIds.Contains(targetWorkstreamId);
        }

        return false;
    }

    public async Task<bool> CanAccessMatterDraftCapabilityAsync(
        Guid matterId,
        string draftPermissionCode,
        Guid userId,
        CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        // 1. Caller must have Matter.View on exact parent active Matter (CanAccessMatterAsync enforces RecordStatus == Active)
        var canViewMatter = await CanAccessMatterAsync(matterId, PermissionCodes.MatterView, userId, ct);
        if (!canViewMatter) return false;

        var matter = await db.Matters.AsNoTracking().SingleOrDefaultAsync(m => m.Id == matterId, ct);
        if (matter is null || matter.RecordStatus != RecordStatus.Active) return false;

        // 2. Draft capability on parent Workstream
        var scopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == draftPermissionCode
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (scopes.Count == 0) return false;

        if (scopes.Contains(ScopeMode.All)) return true;

        if (scopes.Contains(ScopeMode.Workstream))
        {
            if (!matter.WorkstreamId.HasValue) return false;

            return await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.WorkstreamId == matter.WorkstreamId.Value
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);
        }

        return false;
    }

    public async Task<bool> CanAccessDraftAsync(
        Guid draftId,
        string draftPermissionCode,
        Guid userId,
        CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var draft = await db.MatterDrafts.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == draftId, ct);

        if (draft is null || draft.RecordStatus != RecordStatus.Active) return false;

        return await CanAccessMatterDraftCapabilityAsync(draft.MatterId, draftPermissionCode, userId, ct);
    }

    public async Task<bool> CanAccessMatterDocumentAsync(
        Guid matterId,
        Guid documentId,
        Guid userId,
        CancellationToken ct = default)
    {
        var canViewMatter = await CanAccessMatterAsync(matterId, PermissionCodes.MatterView, userId, ct);
        if (!canViewMatter) return false;

        var joinExists = await db.MatterDocuments.AsNoTracking()
            .AnyAsync(md => md.MatterId == matterId && md.DocumentId == documentId, ct);

        if (!joinExists) return false;

        return await db.Documents.AsNoTracking()
            .AnyAsync(d => d.Id == documentId && d.RecordStatus == RecordStatus.Active && d.Status == "Active", ct);
    }
}
