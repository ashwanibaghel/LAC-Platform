using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public static class OfficeAuthorityService
{
    public static bool Reserved(string code) => code is "SYSTEM_ADMIN" or "OFFICE_ADMIN" or "OFFICE_SUPERVISOR";
    public static async Task<OfficeAuthority> GetAsync(LacDbContext db, Guid id, CancellationToken ct = default, bool protectDormant = false)
    {
        var user = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user?.SupervisingOfficerId is not null) return OfficeAuthority.HELPER;
        var codes = await db.UserRoles.Where(r => r.UserId == id && (protectDormant || r.Role.IsActive && r.Role.RecordStatus == RecordStatus.Active))
            .Select(r => r.Role.Code).ToListAsync(ct);
        return codes.Contains("SYSTEM_ADMIN") ? OfficeAuthority.SYSTEM_ADMIN : codes.Contains("OFFICE_ADMIN") ? OfficeAuthority.OFFICE_ADMIN
            : codes.Contains("OFFICE_SUPERVISOR") ? OfficeAuthority.OFFICE_SUPERVISOR : OfficeAuthority.STANDARD_OFFICER;
    }

    public static bool CanGrant(OfficeAuthority actor, OfficeAuthority grant) => actor == OfficeAuthority.SYSTEM_ADMIN
        || actor == OfficeAuthority.OFFICE_ADMIN && grant <= OfficeAuthority.OFFICE_SUPERVISOR
        || actor == OfficeAuthority.OFFICE_SUPERVISOR && grant <= OfficeAuthority.STANDARD_OFFICER;

    public static async Task<bool> CanManageAsync(LacDbContext db, Guid actorId, Guid targetId, CancellationToken ct = default)
    {
        var actor = await GetAsync(db, actorId, ct);
        if (actor == OfficeAuthority.SYSTEM_ADMIN) return true;
        var parentId = await db.AppUsers.Where(u => u.Id == targetId).Select(u => u.SupervisingOfficerId).SingleOrDefaultAsync(ct);
        if (parentId.HasValue)
        {
            if (parentId == actorId) return actor != OfficeAuthority.HELPER;
            return actor >= OfficeAuthority.OFFICE_SUPERVISOR && await CanManageAsync(db, actorId, parentId.Value, ct);
        }
        var target = await GetAsync(db, targetId, ct, protectDormant: true);
        if (target >= actor) return false;
        if (await db.UserRoles.AnyAsync(r => r.UserId == targetId && r.Role.RolePermissions.Any(p => p.Permission.Code == PermissionCodes.AccessManage), ct))
            return false; // Legacy technical access remains protected without elevating the user's authority level.
        if (actor >= OfficeAuthority.OFFICE_SUPERVISOR) return true;
        return actor == OfficeAuthority.STANDARD_OFFICER && await db.AppUsers.AnyAsync(u => u.Id == targetId
            && u.SupervisingOfficerId == actorId, ct);
    }
}
