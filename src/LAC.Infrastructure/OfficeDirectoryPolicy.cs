using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

// Directory visibility is deliberately narrower than technical provisioning/recovery authority.
// Officer-to-officer management retains the frozen office-wide authority tiers; only Helpers
// have a stored reporting parent. Audit CreatedBy is never treated as a reporting relationship.
public static class OfficeDirectoryPolicy
{
    public static async Task<IQueryable<AppUser>> ScopeAsync(LacDbContext db, Guid actor, CancellationToken ct = default)
    {
        var users = db.AppUsers.AsNoTracking().Where(u => u.RecordStatus == RecordStatus.Active);
        if (!await users.AnyAsync(u => u.Id == actor && u.IsActive, ct)) return users.Where(_ => false);
        var level = await OfficeAuthorityService.GetAsync(db, actor, ct);
        var technical = db.UserRoles.Where(r => r.Role.Code == "SYSTEM_ADMIN").Select(r => r.UserId);
        var protectedAccess = db.UserRoles.Where(r => r.Role.RolePermissions.Any(p => p.Permission.Code == PermissionCodes.AccessManage)).Select(r => r.UserId);
        if (level == OfficeAuthority.SYSTEM_ADMIN)
            return users.Where(u => u.SupervisingOfficerId == null && technical.Contains(u.Id));
        if (level == OfficeAuthority.HELPER) return users.Where(u => u.Id == actor);
        if (level == OfficeAuthority.STANDARD_OFFICER)
            return users.Where(u => u.Id == actor || u.SupervisingOfficerId == actor);
        var peersAndSuperiors = db.UserRoles.Where(r => r.Role.Code == "SYSTEM_ADMIN" || r.Role.Code == "OFFICE_ADMIN"
            || level == OfficeAuthority.OFFICE_SUPERVISOR && r.Role.Code == "OFFICE_SUPERVISOR").Select(r => r.UserId);
        var officers = await users.Where(u => u.SupervisingOfficerId == null
            && (u.Id == actor || !peersAndSuperiors.Contains(u.Id) && !protectedAccess.Contains(u.Id))).Select(u => u.Id).ToListAsync(ct);
        return users.Where(u => officers.Contains(u.Id) || u.SupervisingOfficerId.HasValue && officers.Contains(u.SupervisingOfficerId.Value));
    }

    public static async Task<bool> CanViewAsync(LacDbContext db, Guid actor, Guid target, CancellationToken ct = default) =>
        await (await ScopeAsync(db, actor, ct)).AnyAsync(u => u.Id == target, ct);

    public static async Task<bool> CanActAsync(LacDbContext db, Guid actor, Guid target, CancellationToken ct = default)
    {
        if (actor == target || !await OfficeAuthorityService.CanManageAsync(db, actor, target, ct)) return false;
        // The frozen hierarchy already excludes office peers, superiors and unrelated Helpers.
        // Technical directory actions have the additional technical-only visibility ceiling.
        return await OfficeAuthorityService.GetAsync(db, actor, ct) != OfficeAuthority.SYSTEM_ADMIN
            || await OfficeAuthorityService.GetAsync(db, target, ct, true) == OfficeAuthority.SYSTEM_ADMIN;
    }
}
