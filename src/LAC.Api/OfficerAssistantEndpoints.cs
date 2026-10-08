using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

public sealed record AssistantInput(string DisplayName, Guid? DesignationId, IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> PermissionCodes, IReadOnlyList<WorkAllocationInput> Allocations, IReadOnlyList<Guid> DeskIds);
public sealed record CreateAssistantRequest(string Username, AssistantInput Assistant);
public sealed record UpdateAssistantRequest(AssistantInput Assistant, int? ExpectedRevision);

public static class OfficerAssistantEndpoints
{
    public static void MapOfficerAssistantEndpoints(this RouteGroupBuilder api)
    {
        var assistants = api.MapGroup("/officers/me/assistants").RequirePermission(PermissionCodes.AssistantsManage);
        assistants.MapGet("", async (LacDbContext db, ICurrentUserContext current, CancellationToken ct) =>
            Results.Ok(await db.AppUsers.AsNoTracking().Where(u => u.SupervisingOfficerId == current.UserId)
                .Select(u => new { u.Id, u.Username, u.DisplayName, u.DesignationId, u.IsActive, u.SupervisingOfficerId, u.AssistantRevision, u.MustChangePassword }).ToListAsync(ct)));
        assistants.MapGet("/delegation-options", async (LacDbContext db, ICurrentUserContext current, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            return Results.Ok(new
            {
                allocations = await WorkAllocationEndpoints.ReadAllocations(db, current.UserId!.Value, ct),
                roles = await db.Roles.Where(x => x.IsActive && !x.IsSystemRole && x.RecordStatus == RecordStatus.Active)
                    .Select(x => new { x.Id, x.Code, x.Name, permissions = x.RolePermissions.Where(p => p.Permission.Category != "Administration")
                        .Select(p => new { p.Permission.Code, p.ScopeMode }).ToList() }).ToListAsync(ct),
                permissions = await db.UserRoles.Where(x => x.UserId == current.UserId && x.Role.IsActive && x.Role.RecordStatus == RecordStatus.Active)
                    .SelectMany(x => x.Role.RolePermissions).Where(p => p.Permission.Category != "Administration")
                    .Select(p => new { p.Permission.Code, p.ScopeMode }).Distinct().ToListAsync(ct),
                desks = await db.UserDeskMemberships.Where(x => x.UserId == current.UserId && x.IsActive && x.RemovedAt == null && x.OfficeDesk.IsActive)
                    .Select(x => new { x.OfficeDesk.Id, x.OfficeDesk.Code, x.OfficeDesk.Name }).ToListAsync(ct), serverTime = now
            });
        });
        assistants.MapGet("/{id:guid}", async (Guid id, LacDbContext db, ICurrentUserContext current, CancellationToken ct) =>
        {
            var user = await Owned(db, id, current.UserId!.Value, ct);
            if (user is null) return Results.NotFound();
            return Results.Ok(new { user.Id, user.Username, user.DisplayName, user.DesignationId, user.IsActive, user.SupervisingOfficerId,
                user.AssistantRevision, roleIds = await db.UserRoles.Where(x => x.UserId == id).Select(x => x.RoleId).ToListAsync(ct),
                permissionCodes = await db.AssistantPermissionLimits.Where(x => x.UserId == id).Select(x => x.Permission.Code).ToListAsync(ct),
                allocations = await WorkAllocationEndpoints.ReadAllocations(db, id, ct),
                deskIds = await db.UserDeskMemberships.Where(x => x.UserId == id && x.IsActive).Select(x => x.OfficeDeskId).ToListAsync(ct) });
        });
        assistants.MapPost("", async (CreateAssistantRequest request, LacDbContext db, ICurrentUserContext current,
            IPasswordHasher<AppUser> hasher, WorkAllocationService allocations, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username)) return Results.BadRequest();
            var normalized = request.Username.Trim().ToUpperInvariant();
            if (await db.AppUsers.AnyAsync(x => x.NormalizedUsername == normalized, ct)) return Results.Conflict();
            var user = new AppUser { Username = request.Username.Trim(), NormalizedUsername = normalized, SupervisingOfficerId = current.UserId };
            db.AppUsers.Add(user);
            await Stage(db, allocations, user, request.Assistant, current.UserId!.Value, ct);
            var credential = TemporaryCredentials.Issue(user, hasher);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/officers/me/assistants/{user.Id}", new { user.Id, user.AssistantRevision,
                temporaryCredential = credential, credentialExpiresAt = user.TemporaryCredentialExpiresAt });
        });
        assistants.MapPut("/{id:guid}", async (Guid id, UpdateAssistantRequest request, LacDbContext db, ICurrentUserContext current,
            WorkAllocationService allocations, CancellationToken ct) =>
        {
            var user = await Owned(db, id, current.UserId!.Value, ct);
            if (user is null) return Results.NotFound();
            if (user.AssistantRevision != request.ExpectedRevision) return Results.Conflict();
            if (!user.IsActive) return Results.BadRequest(new { message = "Revoked assistant accounts cannot be reactivated through delegation edits." });
            await Stage(db, allocations, user, request.Assistant, current.UserId!.Value, ct);
            user.AssistantRevision++; await OfficeSessionSecurity.InvalidateAsync(db, user, ct); await db.SaveChangesAsync(ct);
            return Results.Ok(new { user.Id, user.AssistantRevision });
        });
        assistants.MapPost("/{id:guid}/revoke", async (Guid id, RevisionRequest request, LacDbContext db, ICurrentUserContext current, WorkAllocationService allocations, CancellationToken ct) =>
        {
            var user = await Owned(db, id, current.UserId!.Value, ct);
            if (user is null) return Results.NotFound();
            if (user.AssistantRevision != request.ExpectedRevision) return Results.Conflict();
            user.IsActive = false; user.SessionVersion = Guid.NewGuid(); user.AssistantRevision++;
            foreach (var allocation in await db.WorkAllocations.Where(x => x.UserId == id && x.RevokedAt == null).ToListAsync(ct)) allocations.Revoke(allocation, current.UserId!.Value);
            await db.SaveChangesAsync(ct); return Results.Ok(new { user.Id, user.AssistantRevision });
        });
        assistants.MapPost("/{id:guid}/reset-credential", async (Guid id, RevisionRequest request, LacDbContext db, ICurrentUserContext current,
            IPasswordHasher<AppUser> hasher, CancellationToken ct) =>
        {
            var user = await Owned(db, id, current.UserId!.Value, ct);
            if (user is null) return Results.NotFound();
            if (!user.IsActive || user.AssistantRevision != request.ExpectedRevision) return Results.Conflict();
            var credential = TemporaryCredentials.Issue(user, hasher); user.AssistantRevision++;
            await db.SaveChangesAsync(ct); return Results.Ok(new { user.Id, user.AssistantRevision,
                temporaryCredential = credential, credentialExpiresAt = user.TemporaryCredentialExpiresAt });
        });
    }

    private static Task<AppUser?> Owned(LacDbContext db, Guid id, Guid officerId, CancellationToken ct) =>
        db.AppUsers.SingleOrDefaultAsync(x => x.Id == id && x.SupervisingOfficerId == officerId && x.RecordStatus == RecordStatus.Active, ct);

    private static async Task Stage(LacDbContext db, WorkAllocationService allocations, AppUser user, AssistantInput input, Guid officerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.DisplayName) || input.Allocations is null || input.RoleIds is null || input.PermissionCodes is null || input.DeskIds is null || input.Allocations.Count is < 1 or > 100
            || !await db.AppUsers.AnyAsync(x => x.Id == officerId && x.IsActive && x.SupervisingOfficerId == null, ct))
            throw new AllocationException(400, "An officer, display name and at least one bounded allocation are required.");
        if (input.DesignationId.HasValue && !await db.Designations.AnyAsync(x => x.Id == input.DesignationId && x.Code == "DEO" && x.IsActive && x.RecordStatus == RecordStatus.Active, ct)) throw new AllocationException(400, "Helpers may use only canonical DEO or the simplified custom designation contract.");
        var roleIds = input.RoleIds.Distinct().ToList();
        if (await db.Roles.AnyAsync(x => roleIds.Contains(x.Id) && x.IsSystemRole, ct)) throw new AllocationException(403, "Assistants cannot hold reserved system roles.");
        if (await db.Roles.CountAsync(x => roleIds.Contains(x.Id) && x.IsActive && x.RecordStatus == RecordStatus.Active, ct) != roleIds.Count)
            throw new AllocationException(400, "One or more roles are missing or inactive.");
        var permissionCodes = input.PermissionCodes.Distinct(StringComparer.Ordinal).ToList();
        var childCodes = await db.RolePermissions.Where(x => roleIds.Contains(x.RoleId)).Select(x => x.Permission.Code).Distinct().ToListAsync(ct);
        var parentCodes = await db.UserRoles.Where(x => x.UserId == officerId && x.Role.IsActive && x.Role.RecordStatus == RecordStatus.Active)
            .SelectMany(x => x.Role.RolePermissions).Select(x => x.Permission.Code).Distinct().ToListAsync(ct);
        var permissions = await db.Permissions.Where(x => permissionCodes.Contains(x.Code) && x.Category != "Administration").ToListAsync(ct);
        if (permissionCodes.Count == 0 || permissions.Count != permissionCodes.Count || permissionCodes.Any(x => !childCodes.Contains(x) || !parentCodes.Contains(x)))
            throw new AllocationException(403, "Assistant permissions must be operational permissions in both the selected roles and the officer's current roles.");
        var parentScopes = await db.UserRoles.Where(x => x.UserId == officerId && x.Role.IsActive && x.Role.RecordStatus == RecordStatus.Active)
            .SelectMany(x => x.Role.RolePermissions).ToListAsync(ct);
        var childScopes = await db.RolePermissions.Where(x => roleIds.Contains(x.RoleId) && permissionCodes.Contains(x.Permission.Code)).ToListAsync(ct);
        if (childScopes.Any(p => !parentScopes.Any(g => g.PermissionId == p.PermissionId && (g.ScopeMode == ScopeMode.All || g.ScopeMode == p.ScopeMode))))
            throw new AllocationException(403, "Requested role scopes exceed the supervising officer's ceiling.");
        foreach (var desk in input.DeskIds.Distinct())
            if (!await db.UserDeskMemberships.AnyAsync(x => x.UserId == officerId && x.OfficeDeskId == desk && x.IsActive && x.RemovedAt == null
                && x.RecordStatus == RecordStatus.Active && x.OfficeDesk.IsActive && x.OfficeDesk.RecordStatus == RecordStatus.Active, ct))
                throw new AllocationException(403, "Assistant desk membership must be a subset of the officer's current desks.");
        user.DisplayName = input.DisplayName.Trim();
        user.DesignationId = input.DesignationId ?? await db.Designations.Where(d => d.Code == "DEO" && d.IsActive && d.RecordStatus == RecordStatus.Active).Select(d => d.Id).SingleAsync(ct);
        user.CustomDesignation = null;
        db.UserRoles.RemoveRange(await db.UserRoles.Where(x => x.UserId == user.Id).ToListAsync(ct));
        foreach (var role in roleIds) db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role });
        var oldLimits = await db.AssistantPermissionLimits.Where(x => x.UserId == user.Id).ToListAsync(ct);
        db.AssistantPermissionLimits.RemoveRange(oldLimits.Where(x => !permissions.Any(p => p.Id == x.PermissionId)));
        foreach (var permission in permissions.Where(p => !oldLimits.Any(x => x.PermissionId == p.Id))) db.AssistantPermissionLimits.Add(new AssistantPermissionLimit { UserId = user.Id, PermissionId = permission.Id });
        foreach (var old in await db.WorkAllocations.Where(x => x.UserId == user.Id && x.RevokedAt == null).ToListAsync(ct)) allocations.Revoke(old, officerId);
        foreach (var inputAllocation in input.Allocations) await allocations.StageAsync(user.Id, inputAllocation, officerId, ct);
        var workIds = input.Allocations.Select(x => x.WorkDefinitionId).ToList();
        var streamIds = await db.WorkDefinitions.Where(x => workIds.Contains(x.Id)).Select(x => x.WorkstreamId).Distinct().ToListAsync(ct);
        streamIds = await db.UserWorkstreamMemberships.Where(x => x.UserId == officerId && x.IsActive && streamIds.Contains(x.WorkstreamId)
            && x.Workstream.IsActive).Select(x => x.WorkstreamId).ToListAsync(ct);
        db.UserWorkstreamMemberships.RemoveRange(await db.UserWorkstreamMemberships.Where(x => x.UserId == user.Id).ToListAsync(ct));
        foreach (var stream in streamIds) db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership { UserId = user.Id, WorkstreamId = stream });
        var oldDesks = await db.UserDeskMemberships.Where(x => x.UserId == user.Id && x.IsActive).ToListAsync(ct);
        foreach (var old in oldDesks.Where(x => !input.DeskIds.Contains(x.OfficeDeskId))) { old.IsActive = false; old.RemovedAt = DateTimeOffset.UtcNow; }
        foreach (var desk in input.DeskIds.Distinct().Where(id => !oldDesks.Any(x => x.OfficeDeskId == id))) db.UserDeskMemberships.Add(new UserDeskMembership { UserId = user.Id, OfficeDeskId = desk });
    }
}
