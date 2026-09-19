namespace LAC.Api;

using System.Security.Claims;
using Claim = System.Security.Claims.Claim;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

public static class EndpointSecurityExtensions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permissionCode)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
            if (!currentUser.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            var accessControl = context.HttpContext.RequestServices.GetRequiredService<IAccessControlService>();
            var allowed = await accessControl.CanAsync(permissionCode);
            if (!allowed)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return await next(context);
        });
    }

    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder builder, string permissionCode)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
            if (!currentUser.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            var accessControl = context.HttpContext.RequestServices.GetRequiredService<IAccessControlService>();
            var allowed = await accessControl.CanAsync(permissionCode);
            if (!allowed)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return await next(context);
        });
    }

    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permissionCode, string workstreamCode)
    {
        var resourceContext = new AccessResourceContext(WorkstreamCode: workstreamCode);
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
            if (!currentUser.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            var accessControl = context.HttpContext.RequestServices.GetRequiredService<IAccessControlService>();
            var allowed = await accessControl.CanAsync(permissionCode, resourceContext);
            if (!allowed)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return await next(context);
        });
    }

    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder builder, string permissionCode, string workstreamCode)
    {
        var resourceContext = new AccessResourceContext(WorkstreamCode: workstreamCode);
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
            if (!currentUser.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            var accessControl = context.HttpContext.RequestServices.GetRequiredService<IAccessControlService>();
            var allowed = await accessControl.CanAsync(permissionCode, resourceContext);
            if (!allowed)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return await next(context);
        });
    }
}

public static class RbacEndpoints
{
    public static RouteGroupBuilder MapRbacEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapPost("/login", async (LoginRequest request, HttpContext httpContext, LacDbContext db, IPasswordHasher<AppUser> hasher, IAccessControlService accessControl, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return Results.Json(new { message = "Username and password are required." }, statusCode: StatusCodes.Status400BadRequest);

            var normalized = request.Username.Trim().ToUpperInvariant();
            var user = await db.AppUsers
                .Include(u => u.Designation)
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                        .ThenInclude(r => r.RolePermissions)
                            .ThenInclude(rp => rp.Permission)
                .Include(u => u.WorkstreamMemberships)
                    .ThenInclude(wm => wm.Workstream)
                .FirstOrDefaultAsync(u => u.NormalizedUsername == normalized && u.RecordStatus == RecordStatus.Active, ct);

            if (user is null || !user.IsActive)
                return Results.Json(new { message = "Invalid username or password." }, statusCode: StatusCodes.Status401Unauthorized);

            var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verify == PasswordVerificationResult.Failed)
                return Results.Json(new { message = "Invalid username or password." }, statusCode: StatusCodes.Status401Unauthorized);

            if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = hasher.HashPassword(user, request.Password);
            }

            user.LastLoginAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new("username", user.Username),
                new("display_name", user.DisplayName)
            };

            if (user.Designation is not null)
            {
                claims.Add(new("designation_id", user.Designation.Id.ToString()));
                claims.Add(new("designation_code", user.Designation.Code));
                claims.Add(new("designation_name", user.Designation.Name));
            }

            var activeRoles = user.UserRoles
                .Where(ur => ur.Role.IsActive && ur.Role.RecordStatus == RecordStatus.Active)
                .Select(ur => ur.Role)
                .ToList();

            foreach (var r in activeRoles)
            {
                claims.Add(new(ClaimTypes.Role, r.Code));
            }

            var activeWorkstreams = user.WorkstreamMemberships
                .Where(m => m.IsActive && m.Workstream.IsActive && m.Workstream.RecordStatus == RecordStatus.Active)
                .ToList();

            foreach (var ws in activeWorkstreams)
            {
                claims.Add(new("workstream_id", ws.Workstream.Id.ToString()));
                claims.Add(new("workstream_code", ws.Workstream.Code));
            }

            var effectivePermissions = await accessControl.GetEffectivePermissionsAsync(user.Id, ct);
            foreach (var (code, _) in effectivePermissions)
            {
                claims.Add(new("permission", code));
            }

            var activeDesks = await db.UserDeskMemberships
                .AsNoTracking()
                .Where(m => m.UserId == user.Id && m.IsActive && m.OfficeDesk.IsActive && m.OfficeDesk.RecordStatus == RecordStatus.Active)
                .Include(m => m.OfficeDesk)
                .OrderByDescending(m => m.IsPrimary).ThenBy(m => m.OfficeDesk.Name)
                .Select(m => new UserDeskDto(m.OfficeDesk.Id, m.OfficeDesk.Code, m.OfficeDesk.Name, m.IsPrimary))
                .ToListAsync(ct);

            foreach (var d in activeDesks)
            {
                claims.Add(new("desk_id", d.Id.ToString()));
                claims.Add(new("desk_code", d.Code));
                if (d.IsPrimary)
                {
                    claims.Add(new("primary_desk_id", d.Id.ToString()));
                }
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            var authProps = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            };

            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProps);

            var response = new CurrentUserResponse(
                user.Id,
                user.Username,
                user.DisplayName,
                user.Designation is not null ? new DesignationDto(user.Designation.Id, user.Designation.Code, user.Designation.Name) : null,
                activeRoles.Select(r => r.Code).ToList(),
                effectivePermissions.Select(kvp => new PermissionScopeDto(kvp.Key, kvp.Value.ToString())).ToList(),
                activeWorkstreams.Select(w => new WorkstreamDto(w.Workstream.Id, w.Workstream.Code, w.Workstream.Name, w.IsPrimary)).ToList(),
                activeDesks
            );

            return Results.Ok(response);
        });

        auth.MapPost("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { message = "Logged out successfully" });
        });

        auth.MapGet("/me", async (HttpContext httpContext, LacDbContext db, ICurrentUserContext currentUser, IAccessControlService accessControl, CancellationToken ct) =>
        {
            if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
                return Results.Unauthorized();

            var user = await db.AppUsers
                .Include(u => u.Designation)
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Include(u => u.WorkstreamMemberships)
                    .ThenInclude(wm => wm.Workstream)
                .FirstOrDefaultAsync(u => u.Id == currentUser.UserId.Value && u.RecordStatus == RecordStatus.Active, ct);

            if (user is null || !user.IsActive)
            {
                await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Unauthorized();
            }

            var activeRoles = user.UserRoles
                .Where(ur => ur.Role.IsActive && ur.Role.RecordStatus == RecordStatus.Active)
                .Select(ur => ur.Role)
                .ToList();

            var activeWorkstreams = user.WorkstreamMemberships
                .Where(m => m.IsActive && m.Workstream.IsActive && m.Workstream.RecordStatus == RecordStatus.Active)
                .ToList();

            var effectivePermissions = await accessControl.GetEffectivePermissionsAsync(user.Id, ct);

            var activeDesks = await db.UserDeskMemberships
                .AsNoTracking()
                .Where(m => m.UserId == user.Id && m.IsActive && m.OfficeDesk.IsActive && m.OfficeDesk.RecordStatus == RecordStatus.Active)
                .Include(m => m.OfficeDesk)
                .OrderByDescending(m => m.IsPrimary).ThenBy(m => m.OfficeDesk.Name)
                .Select(m => new UserDeskDto(m.OfficeDesk.Id, m.OfficeDesk.Code, m.OfficeDesk.Name, m.IsPrimary))
                .ToListAsync(ct);

            var response = new CurrentUserResponse(
                user.Id,
                user.Username,
                user.DisplayName,
                user.Designation is not null ? new DesignationDto(user.Designation.Id, user.Designation.Code, user.Designation.Name) : null,
                activeRoles.Select(r => r.Code).ToList(),
                effectivePermissions.Select(kvp => new PermissionScopeDto(kvp.Key, kvp.Value.ToString())).ToList(),
                activeWorkstreams.Select(w => new WorkstreamDto(w.Workstream.Id, w.Workstream.Code, w.Workstream.Name, w.IsPrimary)).ToList(),
                activeDesks
            );

            return Results.Ok(response);
        });

        var admin = api.MapGroup("/admin");

        admin.MapGet("/users", async (LacDbContext db, CancellationToken ct) =>
        {
            var users = await db.AppUsers
                .AsNoTracking()
                .Where(u => u.RecordStatus == RecordStatus.Active)
                .Include(u => u.Designation)
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .Include(u => u.WorkstreamMemberships).ThenInclude(wm => wm.Workstream)
                .Include(u => u.DeskMemberships).ThenInclude(dm => dm.OfficeDesk)
                .OrderBy(u => u.Username)
                .Select(u => new UserListItem(
                    u.Id,
                    u.Username,
                    u.DisplayName,
                    u.Designation == null ? null : new DesignationDto(u.Designation.Id, u.Designation.Code, u.Designation.Name),
                    u.IsActive,
                    u.LastLoginAt,
                    u.CreatedAt,
                    u.UserRoles.Where(r => r.Role.IsActive && r.Role.RecordStatus == RecordStatus.Active).Select(r => r.Role.Code).ToList(),
                    u.WorkstreamMemberships.Where(m => m.IsActive && m.Workstream.IsActive && m.Workstream.RecordStatus == RecordStatus.Active).Select(m => m.Workstream.Code).ToList(),
                    u.DeskMemberships.Where(m => m.IsActive && m.IsPrimary && m.OfficeDesk.IsActive && m.RecordStatus == RecordStatus.Active).Select(m => m.OfficeDesk.Name).FirstOrDefault(),
                    u.DeskMemberships.Count(m => m.IsActive && m.OfficeDesk.IsActive && m.RecordStatus == RecordStatus.Active)
                ))
                .ToListAsync(ct);
            return Results.Ok(users);
        }).RequirePermission(PermissionCodes.UsersManage);

        admin.MapGet("/users/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
        {
            var u = await db.AppUsers
                .AsNoTracking()
                .Where(x => x.Id == id && x.RecordStatus == RecordStatus.Active)
                .Include(x => x.Designation)
                .Include(x => x.UserRoles).ThenInclude(ur => ur.Role)
                .Include(x => x.WorkstreamMemberships).ThenInclude(wm => wm.Workstream)
                .Include(x => x.DeskMemberships).ThenInclude(dm => dm.OfficeDesk).ThenInclude(d => d.Workstream)
                .FirstOrDefaultAsync(ct);

            if (u is null) return Results.NotFound();

            var roleIds = u.UserRoles.Select(r => r.RoleId).ToList();
            var roles = u.UserRoles
                .Where(r => r.Role.RecordStatus == RecordStatus.Active)
                .Select(r => new RoleSummaryDto(r.Role.Id, r.Role.Code, r.Role.Name, r.Role.IsSystemRole))
                .ToList();

            var workstreams = u.WorkstreamMemberships
                .Where(m => m.Workstream.RecordStatus == RecordStatus.Active)
                .Select(m => new UserWorkstreamDto(m.Workstream.Id, m.Workstream.Code, m.Workstream.Name, m.IsPrimary))
                .ToList();

            var desks = u.DeskMemberships
                .Where(m => m.RecordStatus == RecordStatus.Active)
                .OrderByDescending(m => m.IsActive).ThenByDescending(m => m.IsPrimary).ThenBy(m => m.OfficeDesk.Name)
                .Select(m => new UserDeskMembershipDto(
                    m.Id,
                    m.OfficeDeskId,
                    m.OfficeDesk.Code,
                    m.OfficeDesk.Name,
                    m.OfficeDesk.Workstream != null ? m.OfficeDesk.Workstream.Name : null,
                    m.IsPrimary,
                    m.IsActive,
                    m.AssignedAt,
                    m.RemovedAt
                ))
                .ToList();

            var response = new UserDetailResponse(
                u.Id,
                u.Username,
                u.DisplayName,
                u.DesignationId,
                u.Designation == null ? null : new DesignationDto(u.Designation.Id, u.Designation.Code, u.Designation.Name),
                u.IsActive,
                u.LastLoginAt,
                u.PasswordChangedAt,
                u.CreatedAt,
                roleIds,
                roles,
                workstreams,
                desks
            );

            return Results.Ok(response);
        }).RequirePermission(PermissionCodes.UsersManage);

        admin.MapPost("/users", async (CreateUserRequest request, LacDbContext db, IPasswordHasher<AppUser> hasher, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest(new { message = "Username, display name, and password are required." });

            var normalized = request.Username.Trim().ToUpperInvariant();
            if (await db.AppUsers.AnyAsync(u => u.NormalizedUsername == normalized && u.RecordStatus == RecordStatus.Active, ct))
                return Results.Conflict(new { message = $"User with username '{request.Username}' already exists." });

            // Validate DesignationId
            if (request.DesignationId.HasValue && request.DesignationId.Value != Guid.Empty)
            {
                var desigExists = await db.Designations.AnyAsync(d => d.Id == request.DesignationId.Value && d.IsActive && d.RecordStatus == RecordStatus.Active, ct);
                if (!desigExists)
                    return Results.BadRequest(new { message = $"Designation with ID '{request.DesignationId.Value}' does not exist or is inactive." });
            }

            // Validate RoleIds
            var distinctRoleIds = request.RoleIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
            if (distinctRoleIds.Count > 0)
            {
                var validRoleCount = await db.Roles.CountAsync(r => distinctRoleIds.Contains(r.Id) && r.IsActive && r.RecordStatus == RecordStatus.Active, ct);
                if (validRoleCount != distinctRoleIds.Count)
                    return Results.BadRequest(new { message = "One or more specified role IDs do not exist or are inactive." });
            }

            // Validate WorkstreamIds & PrimaryWorkstreamId
            var distinctWsIds = request.WorkstreamIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
            if (distinctWsIds.Count > 0)
            {
                var validWsCount = await db.Workstreams.CountAsync(w => distinctWsIds.Contains(w.Id) && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);
                if (validWsCount != distinctWsIds.Count)
                    return Results.BadRequest(new { message = "One or more specified workstream IDs do not exist or are inactive." });
            }

            if (request.PrimaryWorkstreamId.HasValue && request.PrimaryWorkstreamId.Value != Guid.Empty)
            {
                if (!distinctWsIds.Contains(request.PrimaryWorkstreamId.Value))
                    return Results.BadRequest(new { message = "PrimaryWorkstreamId must be included in the assigned WorkstreamIds." });
            }

            var user = new AppUser
            {
                Username = request.Username.Trim(),
                NormalizedUsername = normalized,
                DisplayName = request.DisplayName.Trim(),
                DesignationId = request.DesignationId.HasValue && request.DesignationId.Value != Guid.Empty ? request.DesignationId : null,
                IsActive = true,
                PasswordChangedAt = DateTimeOffset.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            db.AppUsers.Add(user);

            foreach (var roleId in distinctRoleIds)
            {
                db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
            }

            foreach (var wsId in distinctWsIds)
            {
                db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership
                {
                    UserId = user.Id,
                    WorkstreamId = wsId,
                    IsPrimary = wsId == request.PrimaryWorkstreamId,
                    IsActive = true
                });
            }

            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/users/{user.Id}", new IdResponse(user.Id));
        }).RequirePermission(PermissionCodes.UsersManage);

        admin.MapPut("/users/{id:guid}", async (Guid id, UpdateUserRequest request, LacDbContext db, CancellationToken ct) =>
        {
            var user = await db.AppUsers
                .Include(u => u.UserRoles)
                .Include(u => u.WorkstreamMemberships)
                .FirstOrDefaultAsync(u => u.Id == id && u.RecordStatus == RecordStatus.Active, ct);

            if (user is null) return Results.NotFound();

            if (string.IsNullOrWhiteSpace(request.DisplayName))
                return Results.BadRequest(new { message = "Display name is required." });

            // Validate DesignationId
            if (request.DesignationId.HasValue && request.DesignationId.Value != Guid.Empty)
            {
                var desigExists = await db.Designations.AnyAsync(d => d.Id == request.DesignationId.Value && d.IsActive && d.RecordStatus == RecordStatus.Active, ct);
                if (!desigExists)
                    return Results.BadRequest(new { message = $"Designation with ID '{request.DesignationId.Value}' does not exist or is inactive." });
            }

            // Validate RoleIds
            var distinctRoleIds = request.RoleIds?.Where(rId => rId != Guid.Empty).Distinct().ToList();
            if (distinctRoleIds is not null && distinctRoleIds.Count > 0)
            {
                var validRoleCount = await db.Roles.CountAsync(r => distinctRoleIds.Contains(r.Id) && r.IsActive && r.RecordStatus == RecordStatus.Active, ct);
                if (validRoleCount != distinctRoleIds.Count)
                    return Results.BadRequest(new { message = "One or more specified role IDs do not exist or are inactive." });
            }

            // Validate WorkstreamIds & PrimaryWorkstreamId
            var distinctWsIds = request.WorkstreamIds?.Where(wsId => wsId != Guid.Empty).Distinct().ToList();
            if (distinctWsIds is not null && distinctWsIds.Count > 0)
            {
                var validWsCount = await db.Workstreams.CountAsync(w => distinctWsIds.Contains(w.Id) && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);
                if (validWsCount != distinctWsIds.Count)
                    return Results.BadRequest(new { message = "One or more specified workstream IDs do not exist or are inactive." });
            }

            if (request.PrimaryWorkstreamId.HasValue && request.PrimaryWorkstreamId.Value != Guid.Empty)
            {
                if (distinctWsIds is null || !distinctWsIds.Contains(request.PrimaryWorkstreamId.Value))
                    return Results.BadRequest(new { message = "PrimaryWorkstreamId must be included in the assigned WorkstreamIds." });
            }

            // Invariant: Do not remove SYSTEM_ADMIN role from the last active admin
            if (distinctRoleIds is not null)
            {
                var sysAdminRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "SYSTEM_ADMIN", ct);
                if (sysAdminRole is not null && user.UserRoles.Any(ur => ur.RoleId == sysAdminRole.Id) && !distinctRoleIds.Contains(sysAdminRole.Id))
                {
                    var otherActiveAdmins = await db.UserRoles.AnyAsync(ur => ur.RoleId == sysAdminRole.Id && ur.UserId != user.Id && ur.User.IsActive && ur.User.RecordStatus == RecordStatus.Active, ct);
                    if (!otherActiveAdmins)
                        return Results.BadRequest(new { message = "Cannot remove SYSTEM_ADMIN role from the last active System Administrator." });
                }
            }

            user.DisplayName = request.DisplayName.Trim();
            user.DesignationId = request.DesignationId.HasValue && request.DesignationId.Value != Guid.Empty ? request.DesignationId : null;

            if (distinctRoleIds is not null)
            {
                db.UserRoles.RemoveRange(user.UserRoles);
                foreach (var rId in distinctRoleIds)
                {
                    db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = rId });
                }
            }

            if (distinctWsIds is not null)
            {
                db.UserWorkstreamMemberships.RemoveRange(user.WorkstreamMemberships);
                foreach (var wsId in distinctWsIds)
                {
                    db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership
                    {
                        UserId = user.Id,
                        WorkstreamId = wsId,
                        IsPrimary = wsId == request.PrimaryWorkstreamId,
                        IsActive = true
                    });
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new IdResponse(user.Id));
        }).RequirePermission(PermissionCodes.UsersManage);

        admin.MapPost("/users/{id:guid}/toggle-status", async (Guid id, LacDbContext db, ICurrentUserContext currentUser, CancellationToken ct) =>
        {
            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == id && u.RecordStatus == RecordStatus.Active, ct);
            if (user is null) return Results.NotFound();

            if (currentUser.UserId == user.Id)
                return Results.BadRequest(new { message = "Cannot deactivate your own account." });

            if (user.IsActive)
            {
                // Invariant: Do not deactivate the last active SYSTEM_ADMIN
                var sysAdminRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "SYSTEM_ADMIN", ct);
                if (sysAdminRole is not null && await db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == sysAdminRole.Id, ct))
                {
                    var otherActiveAdmins = await db.UserRoles.AnyAsync(ur => ur.RoleId == sysAdminRole.Id && ur.UserId != user.Id && ur.User.IsActive && ur.User.RecordStatus == RecordStatus.Active, ct);
                    if (!otherActiveAdmins)
                        return Results.BadRequest(new { message = "Cannot deactivate the last active System Administrator." });
                }
            }

            user.IsActive = !user.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = user.Id, isActive = user.IsActive });
        }).RequirePermission(PermissionCodes.UsersManage);

        admin.MapPost("/users/{id:guid}/reset-password", async (Guid id, ResetPasswordRequest request, LacDbContext db, IPasswordHasher<AppUser> hasher, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.NewPassword))
                return Results.BadRequest(new { message = "New password cannot be empty." });

            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == id && u.RecordStatus == RecordStatus.Active, ct);
            if (user is null) return Results.NotFound();

            user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
            user.PasswordChangedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "Password reset successfully." });
        }).RequirePermission(PermissionCodes.UsersManage);

        admin.MapGet("/designations", async (LacDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Designations.AsNoTracking().Where(d => d.IsActive && d.RecordStatus == RecordStatus.Active).OrderBy(d => d.DisplayOrder).Select(d => new DesignationDto(d.Id, d.Code, d.Name)).ToListAsync(ct)))
            .RequirePermission(PermissionCodes.AccessManage);

        admin.MapGet("/workstreams", async (LacDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Workstreams.AsNoTracking().Where(w => w.IsActive && w.RecordStatus == RecordStatus.Active).OrderBy(w => w.Name).Select(w => new { w.Id, w.Code, w.Name, w.Description, w.IsActive }).ToListAsync(ct)))
            .RequirePermission(PermissionCodes.AccessManage);

        admin.MapGet("/permissions", async (LacDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Permissions.AsNoTracking().OrderBy(p => p.Category).ThenBy(p => p.Code).ToListAsync(ct)))
            .RequirePermission(PermissionCodes.AccessManage);

        admin.MapGet("/roles", async (LacDbContext db, CancellationToken ct) =>
        {
            var roles = await db.Roles
                .AsNoTracking()
                .Where(r => r.RecordStatus == RecordStatus.Active)
                .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
                .OrderBy(r => r.Name)
                .Select(r => new RoleDetailResponse(
                    r.Id,
                    r.Code,
                    r.Name,
                    r.Description,
                    r.IsSystemRole,
                    r.IsActive,
                    r.RolePermissions.Select(rp => new RolePermissionDto(rp.PermissionId, rp.Permission.Code, rp.Permission.Name, rp.Permission.Category, rp.ScopeMode)).ToList()
                ))
                .ToListAsync(ct);
            return Results.Ok(roles);
        }).RequirePermission(PermissionCodes.AccessManage);

        admin.MapGet("/roles/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
        {
            var r = await db.Roles
                .AsNoTracking()
                .Where(x => x.Id == id && x.RecordStatus == RecordStatus.Active)
                .Include(x => x.RolePermissions).ThenInclude(rp => rp.Permission)
                .FirstOrDefaultAsync(ct);

            if (r is null) return Results.NotFound();

            var detail = new RoleDetailResponse(
                r.Id,
                r.Code,
                r.Name,
                r.Description,
                r.IsSystemRole,
                r.IsActive,
                r.RolePermissions.Select(rp => new RolePermissionDto(rp.PermissionId, rp.Permission.Code, rp.Permission.Name, rp.Permission.Category, rp.ScopeMode)).ToList()
            );
            return Results.Ok(detail);
        }).RequirePermission(PermissionCodes.AccessManage);

        admin.MapPost("/roles", async (CreateRoleRequest request, LacDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { message = "Role code and name are required." });

            var code = request.Code.Trim().ToUpperInvariant();
            if (string.Equals(code, "SYSTEM_ADMIN", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "Role code 'SYSTEM_ADMIN' is reserved." });

            if (await db.Roles.AnyAsync(r => r.Code == code && r.RecordStatus == RecordStatus.Active, ct))
                return Results.Conflict(new { message = $"Role with code '{code}' already exists." });

            var role = new Role
            {
                Code = code,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                IsSystemRole = false,
                IsActive = true
            };
            db.Roles.Add(role);

            if (request.Permissions is not null && request.Permissions.Count > 0)
            {
                var distinctInputs = request.Permissions.GroupBy(p => p.PermissionCode, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                var requestedCodes = distinctInputs.Select(p => p.PermissionCode).ToList();
                var knownPerms = await db.Permissions.Where(p => requestedCodes.Contains(p.Code)).ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, ct);

                foreach (var pInput in distinctInputs)
                {
                    if (!knownPerms.TryGetValue(pInput.PermissionCode, out var p))
                        return Results.BadRequest(new { message = $"Unknown permission code: '{pInput.PermissionCode}'." });

                    db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.Id,
                        PermissionId = p.Id,
                        ScopeMode = pInput.ScopeMode
                    });
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/roles/{role.Id}", new IdResponse(role.Id));
        }).RequirePermission(PermissionCodes.AccessManage);

        admin.MapPut("/roles/{id:guid}", async (Guid id, UpdateRoleRequest request, LacDbContext db, CancellationToken ct) =>
        {
            var role = await db.Roles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Id == id && r.RecordStatus == RecordStatus.Active, ct);

            if (role is null) return Results.NotFound();

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { message = "Role name is required." });

            // Invariant: SYSTEM_ADMIN cannot have permissions emptied
            if (role.IsSystemRole)
            {
                if (request.Permissions is null || request.Permissions.Count == 0)
                    return Results.BadRequest(new { message = "System roles cannot have their permissions emptied." });

                if (request.Permissions.Any(p => p.ScopeMode != ScopeMode.All))
                    return Results.BadRequest(new { message = "SYSTEM_ADMIN permissions must have ScopeMode.All." });
            }

            role.Name = request.Name.Trim();
            role.Description = request.Description?.Trim();

            if (request.Permissions is not null)
            {
                var distinctInputs = request.Permissions.GroupBy(p => p.PermissionCode, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                var requestedCodes = distinctInputs.Select(p => p.PermissionCode).ToList();
                var knownPerms = await db.Permissions.Where(p => requestedCodes.Contains(p.Code)).ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, ct);

                foreach (var pInput in distinctInputs)
                {
                    if (!knownPerms.ContainsKey(pInput.PermissionCode))
                        return Results.BadRequest(new { message = $"Unknown permission code: '{pInput.PermissionCode}'." });
                }

                if (role.IsSystemRole)
                {
                    var allPermCodes = await db.Permissions.Select(p => p.Code).ToListAsync(ct);
                    if (distinctInputs.Count < allPermCodes.Count || allPermCodes.Any(c => !knownPerms.ContainsKey(c)))
                    {
                        return Results.BadRequest(new { message = "SYSTEM_ADMIN must retain all platform permissions." });
                    }
                }

                db.RolePermissions.RemoveRange(role.RolePermissions);
                foreach (var pInput in distinctInputs)
                {
                    var p = knownPerms[pInput.PermissionCode];
                    db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.Id,
                        PermissionId = p.Id,
                        ScopeMode = role.IsSystemRole ? ScopeMode.All : pInput.ScopeMode
                    });
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new IdResponse(role.Id));
        }).RequirePermission(PermissionCodes.AccessManage);

        admin.MapGet("/audit-logs", async (string? entityType, Guid? entityId, int page, int pageSize, LacDbContext db, CancellationToken ct) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            var query = db.AuditLogs.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(entityType))
                query = query.Where(a => a.EntityType == entityType);
            if (entityId.HasValue && entityId.Value != Guid.Empty)
                query = query.Where(a => a.EntityId == entityId.Value);

            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(a => a.ChangedAt)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Select(a => new AuditLogListItem(a.Id, a.EntityType, a.EntityId, a.Action, a.ChangedAt, a.ChangedBy, a.OldValues, a.NewValues))
                .ToListAsync(ct);

            return Results.Ok(new PageResponse<AuditLogListItem>(items, total, page, pageSize));
        }).RequirePermission(PermissionCodes.AuditView);

        api.MapGet("/audit-logs", async (string? entityType, Guid? entityId, int page, int pageSize, LacDbContext db, CancellationToken ct) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            var query = db.AuditLogs.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(entityType))
                query = query.Where(a => a.EntityType == entityType);
            if (entityId.HasValue && entityId.Value != Guid.Empty)
                query = query.Where(a => a.EntityId == entityId.Value);

            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(a => a.ChangedAt)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Select(a => new AuditLogListItem(a.Id, a.EntityType, a.EntityId, a.Action, a.ChangedAt, a.ChangedBy, a.OldValues, a.NewValues))
                .ToListAsync(ct);

            return Results.Ok(new PageResponse<AuditLogListItem>(items, total, page, pageSize));
        }).RequirePermission(PermissionCodes.AuditView);

        // --- Office Desks Administration ---
        admin.MapGet("/desks", async (LacDbContext db, CancellationToken ct) =>
        {
            var desks = await db.OfficeDesks
                .AsNoTracking()
                .Where(d => d.RecordStatus == RecordStatus.Active)
                .Include(d => d.Workstream)
                .OrderBy(d => d.Name)
                .Select(d => new DeskListItemDto(
                    d.Id,
                    d.Code,
                    d.Name,
                    d.Description,
                    d.WorkstreamId,
                    d.Workstream != null ? d.Workstream.Code : null,
                    d.Workstream != null ? d.Workstream.Name : null,
                    d.IsActive,
                    d.UserMemberships.Count(m => m.IsActive && m.RecordStatus == RecordStatus.Active),
                    d.CreatedAt
                ))
                .ToListAsync(ct);
            return Results.Ok(desks);
        }).RequirePermission(PermissionCodes.AccessManage);

        admin.MapPost("/desks", async (CreateDeskRequest request, LacDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { message = "Desk code and name are required." });

            var code = request.Code.Trim().ToUpperInvariant();
            if (await db.OfficeDesks.AnyAsync(d => d.Code == code && d.RecordStatus == RecordStatus.Active, ct))
                return Results.Conflict(new { message = $"Desk with code '{code}' already exists." });

            Guid? wsId = null;
            if (request.WorkstreamId.HasValue && request.WorkstreamId.Value != Guid.Empty)
            {
                var wsExists = await db.Workstreams.AnyAsync(w => w.Id == request.WorkstreamId.Value && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);
                if (!wsExists)
                    return Results.BadRequest(new { message = "Invalid or inactive workstream." });
                wsId = request.WorkstreamId.Value;
            }

            var desk = new OfficeDesk
            {
                Code = code,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                WorkstreamId = wsId,
                IsActive = true
            };

            db.OfficeDesks.Add(desk);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/desks/{desk.Id}", new IdResponse(desk.Id));
        }).RequirePermission(PermissionCodes.AccessManage);

        admin.MapPut("/desks/{id:guid}", async (Guid id, UpdateDeskRequest request, LacDbContext db, CancellationToken ct) =>
        {
            var desk = await db.OfficeDesks.FirstOrDefaultAsync(d => d.Id == id && d.RecordStatus == RecordStatus.Active, ct);
            if (desk is null) return Results.NotFound();

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { message = "Desk name is required." });

            Guid? wsId = null;
            if (request.WorkstreamId.HasValue && request.WorkstreamId.Value != Guid.Empty)
            {
                var wsExists = await db.Workstreams.AnyAsync(w => w.Id == request.WorkstreamId.Value && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);
                if (!wsExists)
                    return Results.BadRequest(new { message = "Invalid or inactive workstream." });
                wsId = request.WorkstreamId.Value;
            }

            desk.Name = request.Name.Trim();
            desk.Description = request.Description?.Trim();
            desk.WorkstreamId = wsId;

            await db.SaveChangesAsync(ct);
            return Results.Ok(new IdResponse(desk.Id));
        }).RequirePermission(PermissionCodes.AccessManage);

        admin.MapPost("/desks/{id:guid}/toggle-status", async (Guid id, LacDbContext db, CancellationToken ct) =>
        {
            var desk = await db.OfficeDesks.FirstOrDefaultAsync(d => d.Id == id && d.RecordStatus == RecordStatus.Active, ct);
            if (desk is null) return Results.NotFound();

            desk.IsActive = !desk.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = desk.Id, isActive = desk.IsActive });
        }).RequirePermission(PermissionCodes.AccessManage);

        // --- User Desk Memberships Administration ---
        admin.MapGet("/users/{userId:guid}/desks", async (Guid userId, LacDbContext db, CancellationToken ct) =>
        {
            var userExists = await db.AppUsers.AnyAsync(u => u.Id == userId && u.RecordStatus == RecordStatus.Active, ct);
            if (!userExists) return Results.NotFound();

            var memberships = await db.UserDeskMemberships
                .AsNoTracking()
                .Where(m => m.UserId == userId && m.RecordStatus == RecordStatus.Active)
                .Include(m => m.OfficeDesk).ThenInclude(d => d.Workstream)
                .OrderByDescending(m => m.IsActive).ThenByDescending(m => m.IsPrimary).ThenBy(m => m.OfficeDesk.Name)
                .Select(m => new UserDeskMembershipDto(
                    m.Id,
                    m.OfficeDeskId,
                    m.OfficeDesk.Code,
                    m.OfficeDesk.Name,
                    m.OfficeDesk.Workstream != null ? m.OfficeDesk.Workstream.Name : null,
                    m.IsPrimary,
                    m.IsActive,
                    m.AssignedAt,
                    m.RemovedAt
                ))
                .ToListAsync(ct);
            return Results.Ok(memberships);
        }).RequirePermission(PermissionCodes.UsersManage);

        admin.MapPost("/users/{userId:guid}/desks", async (Guid userId, AssignDeskRequest request, LacDbContext db, CancellationToken ct) =>
        {
            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId && u.RecordStatus == RecordStatus.Active, ct);
            if (user is null || !user.IsActive)
                return Results.BadRequest(new { message = "User not found or is inactive." });

            var desk = await db.OfficeDesks.FirstOrDefaultAsync(d => d.Id == request.OfficeDeskId && d.RecordStatus == RecordStatus.Active, ct);
            if (desk is null || !desk.IsActive)
                return Results.BadRequest(new { message = "Office desk not found or is inactive." });

            var alreadyActive = await db.UserDeskMemberships.AnyAsync(m => m.UserId == userId && m.OfficeDeskId == request.OfficeDeskId && m.IsActive && m.RecordStatus == RecordStatus.Active, ct);
            if (alreadyActive)
                return Results.BadRequest(new { message = "User already has an active membership for this desk." });

            if (request.IsPrimary)
            {
                var activeMemberships = await db.UserDeskMemberships
                    .Where(m => m.UserId == userId && m.IsActive && m.IsPrimary && m.RecordStatus == RecordStatus.Active)
                    .ToListAsync(ct);
                foreach (var active in activeMemberships)
                {
                    active.IsPrimary = false;
                }
            }

            var membership = new UserDeskMembership
            {
                UserId = userId,
                OfficeDeskId = request.OfficeDeskId,
                IsPrimary = request.IsPrimary,
                IsActive = true,
                AssignedAt = DateTimeOffset.UtcNow,
                RemovedAt = null
            };

            db.UserDeskMemberships.Add(membership);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/users/{userId}/desks/{membership.Id}", new IdResponse(membership.Id));
        }).RequirePermission(PermissionCodes.UsersManage);

        admin.MapPost("/users/{userId:guid}/desks/{membershipId:guid}/remove", async (Guid userId, Guid membershipId, LacDbContext db, CancellationToken ct) =>
        {
            var membership = await db.UserDeskMemberships.FirstOrDefaultAsync(m => m.Id == membershipId && m.UserId == userId && m.RecordStatus == RecordStatus.Active, ct);
            if (membership is null) return Results.NotFound();

            if (!membership.IsActive)
                return Results.Ok(new { message = "Membership already inactive." });

            membership.IsActive = false;
            membership.IsPrimary = false;
            membership.RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "Desk membership removed successfully." });
        }).RequirePermission(PermissionCodes.UsersManage);

        admin.MapPost("/users/{userId:guid}/desks/{membershipId:guid}/set-primary", async (Guid userId, Guid membershipId, LacDbContext db, CancellationToken ct) =>
        {
            var membership = await db.UserDeskMemberships
                .Include(m => m.OfficeDesk)
                .FirstOrDefaultAsync(m => m.Id == membershipId && m.UserId == userId && m.RecordStatus == RecordStatus.Active, ct);
            if (membership is null) return Results.NotFound();

            if (!membership.IsActive)
                return Results.BadRequest(new { message = "Cannot set an inactive desk membership as primary." });

            if (membership.OfficeDesk is null || !membership.OfficeDesk.IsActive || membership.OfficeDesk.RecordStatus != RecordStatus.Active)
                return Results.BadRequest(new { message = "Cannot set an inactive or unavailable office desk as primary." });

            var otherPrimaries = await db.UserDeskMemberships
                .Where(m => m.UserId == userId && m.Id != membershipId && m.IsActive && m.IsPrimary && m.RecordStatus == RecordStatus.Active)
                .ToListAsync(ct);
            foreach (var op in otherPrimaries)
            {
                op.IsPrimary = false;
            }
            membership.IsPrimary = true;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "Primary desk set successfully." });
        }).RequirePermission(PermissionCodes.UsersManage);

        return api;
    }
}

public sealed record LoginRequest(string Username, string Password);
public sealed record CurrentUserResponse(
    Guid Id,
    string Username,
    string DisplayName,
    DesignationDto? Designation,
    IReadOnlyList<string> Roles,
    IReadOnlyList<PermissionScopeDto> Permissions,
    IReadOnlyList<WorkstreamDto> Workstreams,
    IReadOnlyList<UserDeskDto> Desks
);
public sealed record DesignationDto(Guid Id, string Code, string Name);
public sealed record WorkstreamDto(Guid Id, string Code, string Name, bool IsPrimary);
public sealed record PermissionScopeDto(string Code, string Scope);
public sealed record UserDeskDto(Guid Id, string Code, string Name, bool IsPrimary);

public sealed record UserListItem(
    Guid Id,
    string Username,
    string DisplayName,
    DesignationDto? Designation,
    bool IsActive,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Workstreams,
    string? PrimaryDeskName,
    int ActiveDesksCount
);

public sealed record UserDetailResponse(
    Guid Id,
    string Username,
    string DisplayName,
    Guid? DesignationId,
    DesignationDto? Designation,
    bool IsActive,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? PasswordChangedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<RoleSummaryDto> Roles,
    IReadOnlyList<UserWorkstreamDto> Workstreams,
    IReadOnlyList<UserDeskMembershipDto> Desks
);

public sealed record DeskListItemDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? WorkstreamId,
    string? WorkstreamCode,
    string? WorkstreamName,
    bool IsActive,
    int ActiveMembersCount,
    DateTimeOffset CreatedAt
);

public sealed record CreateDeskRequest(string Code, string Name, string? Description, Guid? WorkstreamId);
public sealed record UpdateDeskRequest(string Name, string? Description, Guid? WorkstreamId);

public sealed record UserDeskMembershipDto(
    Guid Id,
    Guid OfficeDeskId,
    string DeskCode,
    string DeskName,
    string? WorkstreamName,
    bool IsPrimary,
    bool IsActive,
    DateTimeOffset AssignedAt,
    DateTimeOffset? RemovedAt
);

public sealed record AssignDeskRequest(Guid OfficeDeskId, bool IsPrimary);

public sealed record RoleSummaryDto(Guid Id, string Code, string Name, bool IsSystemRole);
public sealed record UserWorkstreamDto(Guid WorkstreamId, string Code, string Name, bool IsPrimary);

public sealed record CreateUserRequest(
    string Username,
    string DisplayName,
    string Password,
    Guid? DesignationId,
    IReadOnlyList<Guid>? RoleIds,
    IReadOnlyList<Guid>? WorkstreamIds,
    Guid? PrimaryWorkstreamId
);

public sealed record UpdateUserRequest(
    string DisplayName,
    Guid? DesignationId,
    IReadOnlyList<Guid>? RoleIds,
    IReadOnlyList<Guid>? WorkstreamIds,
    Guid? PrimaryWorkstreamId
);

public sealed record ResetPasswordRequest(string NewPassword);

public sealed record RoleDetailResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystemRole,
    bool IsActive,
    IReadOnlyList<RolePermissionDto> Permissions
);

public sealed record RolePermissionDto(Guid PermissionId, string Code, string Name, string Category, ScopeMode ScopeMode);
public sealed record RolePermissionInput(string PermissionCode, ScopeMode ScopeMode);

public sealed record CreateRoleRequest(
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<RolePermissionInput>? Permissions
);

public sealed record UpdateRoleRequest(
    string Name,
    string? Description,
    IReadOnlyList<RolePermissionInput>? Permissions
);

public sealed record AuditLogListItem(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Action,
    DateTimeOffset ChangedAt,
    string? ChangedBy,
    string? OldValues,
    string? NewValues
);
