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

public sealed record WorkItemContributorOptionDto(
    Guid UserId,
    string DisplayName,
    string? Designation,
    IReadOnlyList<string> DeskNames
);

public sealed record WorkItemCapabilitiesDto(
    bool CanAddContributor,
    bool CanRemoveContributor,
    bool CanContribute,
    bool CanSubmitContribution,
    bool CanReviewContributions,
    bool CanAddUpdate,
    bool CanUploadAttachment,
    bool CanStartWork
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

    Task<bool> IsUserEligibleContributorAsync(
        Guid workItemId,
        Guid targetUserId,
        CancellationToken ct = default);

    Task<(bool Allowed, int StatusCode, string? ErrorMessage, IReadOnlyList<WorkItemContributorOptionDto> Options)> GetContributorOptionsForWorkItemAsync(
        Guid workItemId,
        string? query,
        Guid callerUserId,
        CancellationToken ct = default);

    Task<WorkItemCapabilitiesDto> ComputeCapabilitiesAsync(
        WorkItem item,
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

            // Workstream membership is an independent positive authority.
            // Contributor state (Submitted, Accepted, Removed) is NOT a deny override
            // against independently-held Workstream authority.
            if (hasWorkstreamMembership) return true;
        }

        if (scopes.Contains(ScopeMode.Assigned))
        {
            var assignment = await db.WorkItemAssignments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.WorkItemId == workItemId && a.IsActive && a.RecordStatus == RecordStatus.Active, ct);

            if (assignment is not null)
            {
                // OfficeDesk is institutional responsibility.
                // A caller qualifies under ScopeMode.Assigned if:
                // - assignment is active
                // - assigned OfficeDesk is active + RecordStatus.Active
                // - caller is active (already verified)
                // - caller still has a live active UserDeskMembership in that exact assigned OfficeDesk
                // AssignedUserId is an optional named handler metadata/hint, NOT a private ACL.
                var isDeskMember = await db.UserDeskMemberships.AsNoTracking()
                    .AnyAsync(m => m.UserId == userId
                                && m.OfficeDeskId == assignment.OfficeDeskId
                                && m.IsActive
                                && m.RemovedAt == null
                                && m.RecordStatus == RecordStatus.Active
                                && m.OfficeDesk.IsActive
                                && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);

                if (isDeskMember)
                {
                    return true;
                }
            }

            // Contributor authority strictly permitted only for View, Update, Contribute
            if (permissionCode == PermissionCodes.WorkItemView)
            {
                var isViewContributor = await db.WorkItemContributors.AsNoTracking()
                    .AnyAsync(c => c.WorkItemId == workItemId
                                && c.UserId == userId
                                && c.IsActive
                                && c.RecordStatus == RecordStatus.Active
                                && (c.Status == WorkItemContributorStatus.Active
                                 || c.Status == WorkItemContributorStatus.Submitted
                                 || c.Status == WorkItemContributorStatus.Returned), ct);

                if (isViewContributor) return true;
            }
            else if (permissionCode is PermissionCodes.WorkItemUpdate or PermissionCodes.WorkItemContribute)
            {
                var isEditContributor = await db.WorkItemContributors.AsNoTracking()
                    .AnyAsync(c => c.WorkItemId == workItemId
                                && c.UserId == userId
                                && c.IsActive
                                && c.RecordStatus == RecordStatus.Active
                                && (c.Status == WorkItemContributorStatus.Active
                                 || c.Status == WorkItemContributorStatus.Returned), ct);

                if (isEditContributor) return true;
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

    public async Task<bool> IsUserEligibleContributorAsync(
        Guid workItemId,
        Guid targetUserId,
        CancellationToken ct = default)
    {
        var targetUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == targetUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (targetUser is null) return false;

        var workItem = await db.WorkItems.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workItemId && w.RecordStatus == RecordStatus.Active, ct);

        if (workItem is null) return false;

        // Check WorkItem.View scopes
        var viewScopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == targetUserId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.WorkItemView
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (viewScopes.Count == 0) return false;

        var hasViewCapability = false;
        if (viewScopes.Contains(ScopeMode.All) || viewScopes.Contains(ScopeMode.Assigned))
        {
            hasViewCapability = true;
        }
        else if (viewScopes.Contains(ScopeMode.Workstream))
        {
            hasViewCapability = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == targetUserId
                            && m.WorkstreamId == workItem.WorkstreamId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);
        }

        if (!hasViewCapability) return false;

        // Check WorkItem.Contribute scope — WorkItem.Update alone is NOT sufficient.
        // SubmitContributionAsync requires WorkItem.Contribute; a contributor candidate
        // who only has Update cannot submit and is therefore not eligible.
        var contributeScopes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == targetUserId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == PermissionCodes.WorkItemContribute
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);

        if (contributeScopes.Count == 0) return false;

        var hasContributeCapability = false;
        if (contributeScopes.Contains(ScopeMode.All) || contributeScopes.Contains(ScopeMode.Assigned))
        {
            hasContributeCapability = true;
        }
        else if (contributeScopes.Contains(ScopeMode.Workstream))
        {
            hasContributeCapability = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == targetUserId
                            && m.WorkstreamId == workItem.WorkstreamId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);
        }

        return hasContributeCapability;
    }

    public async Task<(bool Allowed, int StatusCode, string? ErrorMessage, IReadOnlyList<WorkItemContributorOptionDto> Options)> GetContributorOptionsForWorkItemAsync(
        Guid workItemId,
        string? query,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var isCallerActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == callerUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

        if (!isCallerActive) return (false, 401, "Caller user not active.", []);

        var workItem = await db.WorkItems.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workItemId && w.RecordStatus == RecordStatus.Active, ct);

        if (workItem is null) return (false, 404, "Work item not found.", []);

        // Caller requires exact WorkItem.Assign authorization on this WorkItem
        var canAssign = await CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemAssign, callerUserId, ct);
        if (!canAssign) return (false, 403, "Forbidden: Caller requires WorkItem.Assign permission on this work item.", []);

        // Exclude users who already have an active contributor relationship on this work item
        var activeContributorUserIds = await db.WorkItemContributors.AsNoTracking()
            .Where(c => c.WorkItemId == workItemId
                     && c.IsActive
                     && c.RecordStatus == RecordStatus.Active
                     && (c.Status == WorkItemContributorStatus.Active
                      || c.Status == WorkItemContributorStatus.Submitted
                      || c.Status == WorkItemContributorStatus.Returned))
            .Select(c => c.UserId)
            .ToListAsync(ct);

        var usersQuery = db.AppUsers.AsNoTracking()
            .Include(u => u.Designation)
            .Where(u => u.IsActive && u.RecordStatus == RecordStatus.Active && !activeContributorUserIds.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLower();
            usersQuery = usersQuery.Where(u => u.DisplayName.ToLower().Contains(q) || u.Username.ToLower().Contains(q));
        }

        var candidateUsers = await usersQuery.OrderBy(u => u.DisplayName).ToListAsync(ct);

        var eligibleOptions = new List<WorkItemContributorOptionDto>();

        // Load desk memberships for candidate users in batch
        var candidateIds = candidateUsers.Select(u => u.Id).ToList();
        var userDeskMap = await db.UserDeskMemberships.AsNoTracking()
            .Where(m => candidateIds.Contains(m.UserId)
                     && m.IsActive
                     && m.RemovedAt == null
                     && m.RecordStatus == RecordStatus.Active
                     && m.OfficeDesk.IsActive
                     && m.OfficeDesk.RecordStatus == RecordStatus.Active)
            .Select(m => new { m.UserId, DeskName = m.OfficeDesk.Name })
            .ToListAsync(ct);

        var deskLookup = userDeskMap.GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DeskName).Distinct().ToList());

        foreach (var user in candidateUsers)
        {
            if (await IsUserEligibleContributorAsync(workItemId, user.Id, ct))
            {
                deskLookup.TryGetValue(user.Id, out var deskNames);
                eligibleOptions.Add(new WorkItemContributorOptionDto(
                    user.Id,
                    user.DisplayName,
                    user.Designation?.Name,
                    deskNames ?? []
                ));
            }
        }

        return (true, 200, null, eligibleOptions);
    }

    public async Task<WorkItemCapabilitiesDto> ComputeCapabilitiesAsync(
        WorkItem item,
        Guid userId,
        CancellationToken ct = default)
    {
        var isTerminal = item.Status is WorkItemStatus.Completed or WorkItemStatus.Cancelled;
        if (isTerminal)
        {
            return new WorkItemCapabilitiesDto(
                CanAddContributor: false,
                CanRemoveContributor: false,
                CanContribute: false,
                CanSubmitContribution: false,
                CanReviewContributions: false,
                CanAddUpdate: false,
                CanUploadAttachment: false,
                CanStartWork: false
            );
        }

        var canAssign = await CanAccessWorkItemAsync(item.Id, PermissionCodes.WorkItemAssign, userId, ct);
        var canContribute = await CanAccessWorkItemAsync(item.Id, PermissionCodes.WorkItemContribute, userId, ct);
        var canUpdate = await CanAccessWorkItemAsync(item.Id, PermissionCodes.WorkItemUpdate, userId, ct);
        var canReview = await CanAccessWorkItemAsync(item.Id, PermissionCodes.WorkItemReview, userId, ct);

        var canSubmitContribution = item.Contributors.Any(c => c.UserId == userId
                                                            && c.IsActive
                                                            && c.RecordStatus == RecordStatus.Active
                                                            && (c.Status == WorkItemContributorStatus.Active || c.Status == WorkItemContributorStatus.Returned))
                                    && canContribute;

        var canReviewContributions = canReview && item.Contributors.Any(c => c.IsActive
                                                                          && c.RecordStatus == RecordStatus.Active
                                                                          && c.Status == WorkItemContributorStatus.Submitted);

        var canAddUpdate = canUpdate || canContribute;
        var canUploadAttachment = canUpdate || canContribute;

        var canStartWork = false;
        if (item.Status == WorkItemStatus.Assigned && item.CurrentAssignment != null)
        {
            canStartWork = await db.UserDeskMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                            && m.OfficeDeskId == item.CurrentAssignment.OfficeDeskId
                            && m.IsActive
                            && m.RemovedAt == null
                            && m.RecordStatus == RecordStatus.Active
                            && m.OfficeDesk.IsActive
                            && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);
        }

        return new WorkItemCapabilitiesDto(
            CanAddContributor: canAssign,
            CanRemoveContributor: canAssign,
            CanContribute: canContribute,
            CanSubmitContribution: canSubmitContribution,
            CanReviewContributions: canReviewContributions,
            CanAddUpdate: canAddUpdate,
            CanUploadAttachment: canUploadAttachment,
            CanStartWork: canStartWork
        );
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
