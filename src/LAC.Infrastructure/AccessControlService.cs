namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class AccessControlService(LacDbContext db, ICurrentUserContext currentUser) : IAccessControlService
{
    public async Task<bool> CanAsync(string permissionCode, AccessResourceContext? resourceContext = null, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
            return false;

        var userId = currentUser.UserId.Value;
        return await CanForUserAsync(userId, permissionCode, resourceContext, cancellationToken);
    }

    public async Task<bool> CanForUserAsync(Guid userId, string permissionCode, AccessResourceContext? resourceContext,
        CancellationToken cancellationToken, bool allowDelegation = true)
    {

        // Ensure user account is still active and valid in store
        var user = await db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, cancellationToken);
        if (user is null)
            return false;
        if (user.SupervisingOfficerId.HasValue)
        {
            if (!allowDelegation || await db.Permissions.AnyAsync(p => p.Code == permissionCode && p.Category == "Administration", cancellationToken)
                || !await db.AssistantPermissionLimits.AnyAsync(x => x.UserId == userId && x.Permission.Code == permissionCode, cancellationToken)
                || !await CanForUserAsync(user.SupervisingOfficerId.Value, permissionCode, resourceContext, cancellationToken, false)) return false;
        }

        // Retrieve user's active roles
        var activeRoleIds = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.Role.IsActive && ur.Role.RecordStatus == RecordStatus.Active)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);

        if (activeRoleIds.Count == 0)
            return false;

        // Retrieve permissions assigned to these roles for the specified permission code
        var rolePermissions = await db.RolePermissions
            .AsNoTracking()
            .Where(rp => activeRoleIds.Contains(rp.RoleId) && rp.Permission.Code == permissionCode)
            .ToListAsync(cancellationToken);

        if (rolePermissions.Count == 0)
            return false;

        // 1. All scope: grants access across the entire system
        if (rolePermissions.Any(rp => rp.ScopeMode == ScopeMode.All))
            return true;

        // 2. Workstream scope: grants access if resource belongs to one of user's active workstreams
        if (rolePermissions.Any(rp => rp.ScopeMode == ScopeMode.Workstream))
        {
            if (resourceContext is not null)
            {
                var userWorkstreams = await db.UserWorkstreamMemberships
                    .AsNoTracking()
                    .Where(m => m.UserId == userId && m.IsActive && m.Workstream.IsActive && m.Workstream.RecordStatus == RecordStatus.Active)
                    .Select(m => new { m.WorkstreamId, m.Workstream.Code })
                    .ToListAsync(cancellationToken);

                if (resourceContext.WorkstreamId.HasValue && userWorkstreams.Any(w => w.WorkstreamId == resourceContext.WorkstreamId.Value))
                    return true;

                if (!string.IsNullOrWhiteSpace(resourceContext.WorkstreamCode) && userWorkstreams.Any(w => string.Equals(w.Code, resourceContext.WorkstreamCode, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }

        // 3. Own scope: grants access if resource was created / owned by the current user
        // Note: Dak permissions fail closed for ScopeMode.Own because official correspondence belongs to the office.
        if (rolePermissions.Any(rp => rp.ScopeMode == ScopeMode.Own))
        {
            if (!permissionCode.StartsWith("Dak.", StringComparison.OrdinalIgnoreCase))
            {
                if (resourceContext?.OwnerUserId.HasValue == true && resourceContext.OwnerUserId.Value == userId)
                    return true;
            }
        }

        // 4. Assigned scope: grants access if resource is currently assigned to an active desk of which the user is an active member
        if (rolePermissions.Any(rp => rp.ScopeMode == ScopeMode.Assigned))
        {
            if (resourceContext?.AssignedDeskId.HasValue == true)
            {
                var deskId = resourceContext.AssignedDeskId.Value;
                var isEligibleDeskMember = await db.UserDeskMemberships
                    .AsNoTracking()
                    .AnyAsync(m => m.UserId == userId
                                && m.OfficeDeskId == deskId
                                && m.IsActive
                                && m.RemovedAt == null
                                && m.OfficeDesk.IsActive
                                && m.OfficeDesk.RecordStatus == RecordStatus.Active, cancellationToken);

                if (isEligibleDeskMember)
                    return true;
            }
        }

        // 5. Coarse-grained check for Dak collection-level queries:
        // When no specific resource is evaluated (resourceContext is null), holding Dak permissions
        // in ScopeMode.Workstream or ScopeMode.Assigned permits entry to the endpoint, where
        // collection-level union-of-scopes filtering is enforced by IDakAuthorizationService.
        if (resourceContext is null && permissionCode.StartsWith("Dak.", StringComparison.OrdinalIgnoreCase))
        {
            if (rolePermissions.Any(rp => rp.ScopeMode == ScopeMode.Workstream || rp.ScopeMode == ScopeMode.Assigned))
                return true;
        }

        return false;
    }

    public async Task<IReadOnlyDictionary<string, ScopeMode>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, cancellationToken);
        if (account is null) return new Dictionary<string, ScopeMode>();
        var activeRoleIds = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.Role.IsActive && ur.Role.RecordStatus == RecordStatus.Active)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);

        if (activeRoleIds.Count == 0)
            return new Dictionary<string, ScopeMode>();

        var permissionsWithScope = await db.RolePermissions
            .AsNoTracking()
            .Where(rp => activeRoleIds.Contains(rp.RoleId))
            .Select(rp => new { rp.Permission.Code, rp.ScopeMode })
            .ToListAsync(cancellationToken);

        if (account.SupervisingOfficerId.HasValue)
        {
            var parent = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(u => u.Id == account.SupervisingOfficerId
                && u.IsActive && u.RecordStatus == RecordStatus.Active && u.SupervisingOfficerId == null, cancellationToken);
            if (parent is null) return new Dictionary<string, ScopeMode>();
            var limits = await db.AssistantPermissionLimits.Where(x => x.UserId == userId && x.Permission.Category != "Administration")
                .Select(x => x.Permission.Code).ToListAsync(cancellationToken);
            var parentGrants = await db.UserRoles.Where(x => x.UserId == parent.Id && x.Role.IsActive && x.Role.RecordStatus == RecordStatus.Active)
                .SelectMany(x => x.Role.RolePermissions).Select(x => new { x.Permission.Code, x.ScopeMode }).ToListAsync(cancellationToken);
            // The helper bundle is only a role source. Report the scope intersection, not a fictitious All ceiling.
            permissionsWithScope = permissionsWithScope.Where(x => limits.Contains(x.Code)).SelectMany(child =>
                parentGrants.Where(p => p.Code == child.Code && (child.ScopeMode == ScopeMode.All || p.ScopeMode == ScopeMode.All || p.ScopeMode == child.ScopeMode))
                    .Select(p => new { child.Code, ScopeMode = child.ScopeMode == ScopeMode.All ? p.ScopeMode : child.ScopeMode })).ToList();
        }

        var result = new Dictionary<string, ScopeMode>(StringComparer.OrdinalIgnoreCase);

        // Precedence: All (broadest) > Workstream > Own > Assigned (narrowest/closed)
        static int ScopeRank(ScopeMode m) => m switch
        {
            ScopeMode.All => 3,
            ScopeMode.Workstream => 2,
            ScopeMode.Own => 1,
            ScopeMode.Assigned => 0,
            _ => -1
        };

        foreach (var item in permissionsWithScope)
        {
            if (!result.TryGetValue(item.Code, out var current) || ScopeRank(item.ScopeMode) > ScopeRank(current))
            {
                result[item.Code] = item.ScopeMode;
            }
        }

        return result;
    }
}
