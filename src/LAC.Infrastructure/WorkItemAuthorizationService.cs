namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed record WorkItemDeskOptionDto(
    Guid Id,
    string Code,
    string Name,
    Guid? WorkstreamId,
    IReadOnlyList<WorkItemDeskMemberOptionDto> Members
);

public sealed record WorkItemDeskMemberOptionDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string? Designation,
    bool IsPrimaryDesk
);

public interface IWorkItemAuthorizationService
{
    Task<bool> CanAccessWorkItemAsync(
        Guid workItemId,
        string permissionCode,
        Guid userId,
        CancellationToken ct = default);

    Task<bool> CanCreateWorkItemAsync(
        Guid targetWorkstreamId,
        Guid targetDeskId,
        Guid? targetUserId,
        Guid callerUserId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Workstream>> GetCreateContextWorkstreamsAsync(
        Guid userId,
        CancellationToken ct = default);

    Task<IReadOnlyList<WorkItemDeskOptionDto>> GetAssignmentOptionsAsync(
        Guid? workstreamId,
        Guid userId,
        CancellationToken ct = default);

    Task<(bool Allowed, int StatusCode, string? ErrorMessage, IReadOnlyList<WorkItemDeskOptionDto> Desks)> GetAssignmentOptionsForWorkstreamAsync(
        Guid workstreamId,
        Guid userId,
        CancellationToken ct = default);

    Task<bool> CanAccessAttachmentContentAsync(
        Guid workItemId,
        Guid attachmentId,
        Guid userId,
        CancellationToken ct = default);
}

public sealed class WorkItemAuthorizationService(LacDbContext db) : IWorkItemAuthorizationService
{
    public async Task<bool> CanAccessWorkItemAsync(
        Guid workItemId,
        string permissionCode,
        Guid userId,
        CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isUserActive) return false;

        var workItem = await db.WorkItems.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workItemId, ct);

        if (workItem is null || workItem.RecordStatus != RecordStatus.Active)
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
            var hasWorkstreamMembership = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.WorkstreamId == workItem.WorkstreamId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);

            if (hasWorkstreamMembership) return true;
        }

        if (scopes.Contains(ScopeMode.Assigned))
        {
            var assignment = await db.WorkItemAssignments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.WorkItemId == workItemId && a.IsActive && a.RecordStatus == RecordStatus.Active, ct);

            if (assignment is not null)
            {
                // A direct named handler qualifies only while:
                // - assignment is active
                // - assigned OfficeDesk is active + RecordStatus.Active
                // - caller is active (already verified)
                // - caller still has a live active UserDeskMembership in that exact assigned OfficeDesk
                // Desk-based unnamed responsibility remains available to any live active member of that desk.
                var isDeskMember = await db.UserDeskMemberships.AsNoTracking()
                    .AnyAsync(m => m.UserId == userId
                                && m.OfficeDeskId == assignment.OfficeDeskId
                                && m.IsActive
                                && m.RemovedAt == null
                                && m.RecordStatus == RecordStatus.Active
                                && m.OfficeDesk.IsActive
                                && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);

                if (isDeskMember && (!assignment.AssignedUserId.HasValue || assignment.AssignedUserId.Value == userId))
                {
                    return true;
                }
            }

            // Contributor authority strictly permitted only for View, Update, Contribute
            if (permissionCode is PermissionCodes.WorkItemView
                               or PermissionCodes.WorkItemUpdate
                               or PermissionCodes.WorkItemContribute)
            {
                var isContributor = await db.WorkItemContributors.AsNoTracking()
                    .AnyAsync(c => c.WorkItemId == workItemId
                                && c.UserId == userId
                                && c.IsActive
                                && c.RecordStatus == RecordStatus.Active
                                && c.Status == WorkItemContributorStatus.Active, ct);

                if (isContributor) return true;
            }
        }

        // ScopeMode.Own fails closed for official work items
        return false;
    }

    public async Task<bool> CanCreateWorkItemAsync(
        Guid targetWorkstreamId,
        Guid targetDeskId,
        Guid? targetUserId,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var isCallerActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == callerUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isCallerActive) return false;

        var isWsActive = await db.Workstreams.AsNoTracking()
            .AnyAsync(w => w.Id == targetWorkstreamId && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);

        if (!isWsActive) return false;

        var isDeskActive = await db.OfficeDesks.AsNoTracking()
            .AnyAsync(d => d.Id == targetDeskId && d.IsActive && d.RecordStatus == RecordStatus.Active, ct);

        if (!isDeskActive) return false;

        if (targetUserId.HasValue)
        {
            var isTargetUserValid = await db.UserDeskMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == targetUserId.Value
                            && m.OfficeDeskId == targetDeskId
                            && m.IsActive
                            && m.RemovedAt == null
                            && m.RecordStatus == RecordStatus.Active
                            && m.User.IsActive
                            && m.User.RecordStatus == RecordStatus.Active, ct);

            if (!isTargetUserValid) return false;
        }

        // Caller must hold both WorkItem.Create and WorkItem.Assign
        var createScopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == callerUserId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.WorkItemCreate
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        var assignScopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == callerUserId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.WorkItemAssign
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        // Assigned and Own fail closed for creation
        var canCreate = createScopes.Contains(ScopeMode.All);
        if (!canCreate && createScopes.Contains(ScopeMode.Workstream))
        {
            canCreate = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == callerUserId
                            && m.WorkstreamId == targetWorkstreamId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);
        }

        if (!canCreate) return false;

        var canAssign = assignScopes.Contains(ScopeMode.All);
        if (!canAssign && assignScopes.Contains(ScopeMode.Workstream))
        {
            canAssign = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == callerUserId
                            && m.WorkstreamId == targetWorkstreamId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);
        }

        return canAssign;
    }

    public async Task<IReadOnlyList<Workstream>> GetCreateContextWorkstreamsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var isCallerActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isCallerActive) return [];

        var createScopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.WorkItemCreate
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        var assignScopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.WorkItemAssign
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        var hasAll = createScopes.Contains(ScopeMode.All) && assignScopes.Contains(ScopeMode.All);
        if (hasAll)
        {
            return await db.Workstreams.AsNoTracking()
                .Where(w => w.IsActive && w.RecordStatus == RecordStatus.Active)
                .OrderBy(w => w.Name)
                .ToListAsync(ct);
        }

        var hasCreateWs = createScopes.Contains(ScopeMode.All) || createScopes.Contains(ScopeMode.Workstream);
        var hasAssignWs = assignScopes.Contains(ScopeMode.All) || assignScopes.Contains(ScopeMode.Workstream);

        if (!hasCreateWs || !hasAssignWs) return [];

        return await db.UserWorkstreamMemberships.AsNoTracking()
            .Where(m => m.UserId == userId
                     && m.IsActive
                     && m.Workstream.IsActive
                     && m.Workstream.RecordStatus == RecordStatus.Active)
            .Select(m => m.Workstream)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);
    }

    public async Task<(bool Allowed, int StatusCode, string? ErrorMessage, IReadOnlyList<WorkItemDeskOptionDto> Desks)> GetAssignmentOptionsForWorkstreamAsync(
        Guid workstreamId,
        Guid userId,
        CancellationToken ct = default)
    {
        var isCallerActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isCallerActive)
            return (false, 403, "Caller is not active.", []);

        var targetWs = await db.Workstreams.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workstreamId, ct);

        if (targetWs is null || !targetWs.IsActive || targetWs.RecordStatus != RecordStatus.Active)
            return (false, 400, "Target workstream is inactive or does not exist.", []);

        var assignScopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.WorkItemAssign
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (assignScopes.Count == 0)
            return (false, 403, "Caller lacks WorkItem.Assign permission.", []);

        if (!assignScopes.Contains(ScopeMode.All))
        {
            if (assignScopes.Contains(ScopeMode.Workstream))
            {
                var isMember = await db.UserWorkstreamMemberships.AsNoTracking()
                    .AnyAsync(m => m.UserId == userId
                                && m.WorkstreamId == workstreamId
                                && m.IsActive
                                && m.Workstream.IsActive
                                && m.Workstream.RecordStatus == RecordStatus.Active, ct);

                if (!isMember)
                    return (false, 403, "Caller is not an active member of target workstream.", []);
            }
            else
            {
                return (false, 403, "Insufficient scope for assignment options.", []);
            }
        }

        // OfficeDesk.WorkstreamId is optional classification metadata only - NOT routing or security authority.
        // Once target workstream authorization is validated, all active desks are eligible routing targets.
        var desks = await db.OfficeDesks.AsNoTracking()
            .Where(d => d.IsActive && d.RecordStatus == RecordStatus.Active)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

        var deskIds = desks.Select(d => d.Id).ToList();

        var memberships = await db.UserDeskMemberships.AsNoTracking()
            .Where(m => deskIds.Contains(m.OfficeDeskId)
                     && m.IsActive
                     && m.RemovedAt == null
                     && m.RecordStatus == RecordStatus.Active
                     && m.User.IsActive
                     && m.User.RecordStatus == RecordStatus.Active)
            .Select(m => new
            {
                m.OfficeDeskId,
                m.UserId,
                m.User.Username,
                m.User.DisplayName,
                Designation = m.User.Designation != null ? m.User.Designation.Name : null,
                m.IsPrimary
            })
            .ToListAsync(ct);

        var result = new List<WorkItemDeskOptionDto>();
        foreach (var desk in desks)
        {
            var deskMembers = memberships
                .Where(m => m.OfficeDeskId == desk.Id)
                .OrderByDescending(m => m.IsPrimary)
                .ThenBy(m => m.DisplayName)
                .Select(m => new WorkItemDeskMemberOptionDto(
                    m.UserId,
                    m.Username,
                    m.DisplayName,
                    m.Designation,
                    m.IsPrimary
                ))
                .ToList();

            result.Add(new WorkItemDeskOptionDto(
                desk.Id,
                desk.Code,
                desk.Name,
                desk.WorkstreamId,
                deskMembers
            ));
        }

        return (true, 200, null, result);
    }

    public async Task<IReadOnlyList<WorkItemDeskOptionDto>> GetAssignmentOptionsAsync(
        Guid? workstreamId,
        Guid userId,
        CancellationToken ct = default)
    {
        if (!workstreamId.HasValue) return [];
        var res = await GetAssignmentOptionsForWorkstreamAsync(workstreamId.Value, userId, ct);
        return res.Allowed ? res.Desks : [];
    }

    public async Task<bool> CanAccessAttachmentContentAsync(
        Guid workItemId,
        Guid attachmentId,
        Guid userId,
        CancellationToken ct = default)
    {
        return await CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemView, userId, ct);
    }
}
