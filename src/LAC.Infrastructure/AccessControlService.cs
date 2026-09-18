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

        // Ensure user account is still active and valid in store
        var user = await db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, cancellationToken);
        if (user is null)
            return false;

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
        if (rolePermissions.Any(rp => rp.ScopeMode == ScopeMode.Own))
        {
            if (resourceContext?.OwnerUserId.HasValue == true && resourceContext.OwnerUserId.Value == userId)
                return true;
        }

        // 4. Assigned scope: Phase 1 intentionally fails closed because Task/Dak assignments are in a later phase.
        // Falls through to return false.

        return false;
    }

    public async Task<IReadOnlyDictionary<string, ScopeMode>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
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
