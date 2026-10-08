using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

// Applied before all legacy administration endpoints as well as the simplified contract.
public static class OfficeHierarchyFilter
{
    public static async Task<bool> AllowedAsync(EndpointFilterInvocationContext ctx, LacDbContext db, Guid caller, CancellationToken ct)
    {
        var request = ctx.HttpContext.Request;
        var path = request.Path.Value!.ToLowerInvariant();
        var authority = await OfficeAuthorityService.GetAsync(db, caller, ct);
        var write = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method);
        if (path.StartsWith("/api/admin/roles") || path == "/api/admin/permissions")
            return authority == OfficeAuthority.SYSTEM_ADMIN;
        if (path.StartsWith("/api/admin/desks") && write)
            return authority >= OfficeAuthority.OFFICE_ADMIN;
        if (path.StartsWith("/api/admin/users"))
        {
            if (authority < OfficeAuthority.OFFICE_SUPERVISOR) return false;
            if (write)
            {
                var rawTarget = request.RouteValues["userId"] ?? request.RouteValues["id"];
                if (Guid.TryParse(rawTarget?.ToString(), out var target) && !await OfficeAuthorityService.CanManageAsync(db, caller, target, ct)) return false;
                if (Guid.TryParse(rawTarget?.ToString(), out target) && await db.AppUsers.AnyAsync(u => u.Id == target && u.SupervisingOfficerId != null, ct)
                    && !path.EndsWith("/reset-password") && !path.EndsWith("/toggle-status")) return false;
                var roleIds = ctx.Arguments.OfType<CreateUserRequest>().SelectMany(r => r.RoleIds ?? [])
                    .Concat(ctx.Arguments.OfType<UpdateUserRequest>().SelectMany(r => r.RoleIds ?? [])).ToList();
                foreach (var role in await db.Roles.Where(r => roleIds.Contains(r.Id)).ToListAsync(ct))
                {
                    if (role.Code == OfficeAccessPresets.Helper.Code) return false;
                    if (OfficeAuthorityService.Reserved(role.Code) && (!Enum.TryParse<OfficeAuthority>(role.Code, out var granted)
                        || !OfficeAuthorityService.CanGrant(authority, granted))) return false;
                }
            }
        }
        if (write && path.StartsWith("/api/admin/works") && authority < OfficeAuthority.OFFICE_ADMIN) return false;
        if (path.StartsWith("/api/officers/") && authority == OfficeAuthority.HELPER) return false;
        return true;
    }
}
