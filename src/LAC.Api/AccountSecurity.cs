using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

public static class AccountSecurity
{
    public static async Task<bool> CanAssignRolesAsync(LacDbContext db, IAccessControlService access,
        IReadOnlyList<Guid> roleIds, CancellationToken ct)
    {
        if (await access.CanAsync(PermissionCodes.AccessManage, cancellationToken: ct)) return true;
        if (!await access.CanAsync(PermissionCodes.RolesAssign, cancellationToken: ct)) return false;
        var roles = await db.Roles.Include(r => r.RolePermissions).ThenInclude(p => p.Permission)
            .Where(r => roleIds.Contains(r.Id)).ToListAsync(ct);
        if (!await CoversScopesAsync(db, roles.SelectMany(r => r.RolePermissions), ct)) return false;
        // Assignment authority cannot bootstrap access administration or reserved system authority.
        foreach (var role in roles)
        {
            if (role.Code == "SYSTEM_ADMIN") return false;
            foreach (var grant in role.RolePermissions)
            {
                if (grant.Permission.Category == "Administration"
                    || !await access.CanAsync(grant.Permission.Code, cancellationToken: ct)) return false;
            }
        }
        return true;
    }

    public static async Task<bool> CanMaintainCredentialsAsync(LacDbContext db, IAccessControlService access,
        Guid targetId, Guid callerId, CancellationToken ct)
    {
        if (await access.CanAsync(PermissionCodes.AccessManage, cancellationToken: ct)) return true;
        if (targetId == callerId) return true;
        // Dormant privileged memberships also need protection from credential takeover.
        if (await db.UserRoles.AnyAsync(x => x.UserId == targetId && (x.Role.IsSystemRole
                || x.Role.RolePermissions.Any(p => p.Permission.Category == "Administration")), ct)) return false;
        var permissions = await db.UserRoles.Where(x => x.UserId == targetId && x.Role.IsActive
                && x.Role.RecordStatus == RecordStatus.Active)
            .SelectMany(x => x.Role.RolePermissions).Select(x => x.Permission.Code).Distinct().ToListAsync(ct);
        // Equal nominal role codes do not imply equal geographic/operational authority.
        if ((permissions.Count > 0 || await db.WorkAllocations.AnyAsync(x => x.UserId == targetId && x.RevokedAt == null, ct))
            && (!await access.CanAsync(PermissionCodes.RolesAssign, cancellationToken: ct)
                || !await access.CanAsync(PermissionCodes.AllocationsManage, cancellationToken: ct))) return false;
        var targetGrants = await db.UserRoles.Where(x => x.UserId == targetId && x.Role.IsActive && x.Role.RecordStatus == RecordStatus.Active)
            .SelectMany(x => x.Role.RolePermissions).ToListAsync(ct);
        if (!await CoversScopesAsync(db, targetGrants, ct)) return false;
        // Resetting a stronger account is another route to role escalation.
        foreach (var permission in permissions)
            if (!await access.CanAsync(permission, cancellationToken: ct)) return false;
        return true;
    }

    private static async Task<bool> CoversScopesAsync(LacDbContext db, IEnumerable<RolePermission> requested, CancellationToken ct)
    {
        var callerId = db.CurrentUser?.UserId;
        if (!callerId.HasValue) return false;
        var held = await db.UserRoles.Where(x => x.UserId == callerId && x.Role.IsActive && x.Role.RecordStatus == RecordStatus.Active)
            .SelectMany(x => x.Role.RolePermissions).AsNoTracking().ToListAsync(ct);
        // Collection-entry checks (notably Dak) are not evidence of All-scope authority.
        // Own, Assigned and Workstream are incomparable; only All or the same mode covers a grant.
        return requested.All(grant => held.Any(x => x.PermissionId == grant.PermissionId
            && (x.ScopeMode == ScopeMode.All || x.ScopeMode == grant.ScopeMode)));
    }
}
