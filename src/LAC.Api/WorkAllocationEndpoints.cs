using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

public sealed record CreateWorkRequest(string Code, string Name, string? Description, [property: System.Text.Json.Serialization.JsonRequired] OperationalWorkKind Kind, Guid WorkstreamId);
public sealed record UpdateWorkRequest(string Name, string? Description, bool IsActive, int? ExpectedRevision);
public sealed record UpdateAllocationRequest(WorkAllocationInput Allocation, int? ExpectedRevision);
public sealed record RevisionRequest(int? ExpectedRevision);

public static class WorkAllocationEndpoints
{
    public static void MapWorkAllocationEndpoints(this RouteGroupBuilder api)
    {
        api.AddEndpointFilter(async (ctx, next) =>
        {
            try { return await next(ctx); }
            catch (AllocationException ex) { return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { message = "The allocation/account changed; reload and retry." }); }
        });
        var admin = api.MapGroup("/admin");
        admin.MapGet("/works", async (LacDbContext db, IAccessControlService access, CancellationToken ct) =>
        {
            if (!await CanReadOptions(access, ct)) return Results.Forbid();
            return Results.Ok(await db.WorkDefinitions.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Code, x.Name, x.Description, x.Kind, x.WorkstreamId, x.IsActive, x.Revision }).ToListAsync(ct));
        });
        admin.MapPost("/works", async (CreateWorkRequest request, LacDbContext db, CancellationToken ct) =>
        {
            var code = request.Code?.Trim().ToUpperInvariant() ?? "";
            if (code.Length is < 1 or > 64 || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200
                || !Enum.IsDefined(request.Kind) || !await db.Workstreams.AnyAsync(w => w.Id == request.WorkstreamId && w.IsActive && w.RecordStatus == RecordStatus.Active, ct))
                return Results.BadRequest(new { message = "Valid code, name, work kind and active workstream are required." });
            if (await db.WorkDefinitions.AnyAsync(x => x.Code == code, ct)) return Results.Conflict();
            var work = new WorkDefinition { Code = code, Name = request.Name.Trim(), Description = request.Description?.Trim(), Kind = request.Kind, WorkstreamId = request.WorkstreamId };
            db.WorkDefinitions.Add(work); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/works/{work.Id}", new { work.Id, work.Revision });
        }).RequirePermission(PermissionCodes.WorkCatalogManage);
        admin.MapPut("/works/{id:guid}", async (Guid id, UpdateWorkRequest request, LacDbContext db, CancellationToken ct) =>
        {
            var work = await db.WorkDefinitions.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (work is null) return Results.NotFound();
            if (work.Revision != request.ExpectedRevision) return Results.Conflict();
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200) return Results.BadRequest();
            work.Name = request.Name.Trim(); work.Description = request.Description?.Trim(); work.IsActive = request.IsActive; work.Revision++;
            await db.SaveChangesAsync(ct); return Results.Ok(new { work.Id, work.Revision });
        }).RequirePermission(PermissionCodes.WorkCatalogManage);

        admin.MapGet("/users/{userId:guid}/allocations", async (Guid userId, LacDbContext db, CancellationToken ct) =>
            Results.Ok(await ReadAllocations(db, userId, ct))).RequirePermission(PermissionCodes.AllocationsManage);
        admin.MapPost("/users/{userId:guid}/allocations", async (Guid userId, WorkAllocationInput input, LacDbContext db, WorkAllocationService service, CancellationToken ct) =>
        {
            var user = await db.AppUsers.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive && x.RecordStatus == RecordStatus.Active, ct);
            if (user is null) return Results.NotFound();
            var allocation = await service.StageAsync(userId, input, user.SupervisingOfficerId, ct);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/users/{userId}/allocations/{allocation.Id}", new { allocation.Id, allocation.Revision });
        }).RequirePermission(PermissionCodes.AllocationsManage);
        admin.MapPut("/users/{userId:guid}/allocations/{id:guid}", async (Guid userId, Guid id, UpdateAllocationRequest request,
            LacDbContext db, WorkAllocationService service, CancellationToken ct) =>
        {
            var user = await db.AppUsers.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive && x.RecordStatus == RecordStatus.Active, ct);
            var allocation = await db.WorkAllocations.Include(x => x.Scopes).SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            if (user is null || allocation is null) return Results.NotFound();
            if (allocation.Revision != request.ExpectedRevision || allocation.RevokedAt.HasValue) return Results.Conflict();
            if (allocation.WorkDefinitionId != request.Allocation.WorkDefinitionId) return Results.BadRequest(new { message = "Revoke and create a new allocation to change work." });
            var validated = await service.StageAsync(userId, request.Allocation, user.SupervisingOfficerId, ct, stage: false);
            db.WorkAllocationScopes.RemoveRange(allocation.Scopes);
            foreach (var scope in validated.Scopes) { scope.WorkAllocationId = allocation.Id; scope.WorkAllocation = allocation; }
            db.WorkAllocationScopes.AddRange(validated.Scopes);
            allocation.Scopes = validated.Scopes;
            allocation.ValidFrom = validated.ValidFrom; allocation.ValidTo = validated.ValidTo;
            allocation.WorkOrderReference = validated.WorkOrderReference; allocation.Reason = validated.Reason;
            allocation.DelegatedFromAllocationId = validated.DelegatedFromAllocationId; allocation.Revision++;
            await db.SaveChangesAsync(ct); return Results.Ok(new { allocation.Id, allocation.Revision });
        }).RequirePermission(PermissionCodes.AllocationsManage);
        admin.MapPost("/users/{userId:guid}/allocations/{id:guid}/revoke", async (Guid userId, Guid id, RevisionRequest request,
            LacDbContext db, WorkAllocationService service, ICurrentUserContext current, CancellationToken ct) =>
        {
            var allocation = await db.WorkAllocations.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            if (allocation is null) return Results.NotFound();
            if (allocation.Revision != request.ExpectedRevision) return Results.Conflict();
            service.Revoke(allocation, current.UserId!.Value); await db.SaveChangesAsync(ct);
            return Results.Ok(new { allocation.Id, allocation.Revision });
        }).RequirePermission(PermissionCodes.AllocationsManage);
        api.MapGet("/auth/allocations", async (LacDbContext db, ICurrentUserContext current, CancellationToken ct) =>
            Results.Ok(await ReadAllocations(db, current.UserId!.Value, ct)));
        admin.MapGet("/rbac-audit", async (Guid? actorUserId, Guid? onBehalfOfUserId, int? page, int? pageSize, LacDbContext db, CancellationToken ct) =>
        {
            var query = db.AuditLogs.AsNoTracking();
            if (actorUserId.HasValue) query = query.Where(x => x.ActorUserId == actorUserId);
            if (onBehalfOfUserId.HasValue) query = query.Where(x => x.OnBehalfOfUserId == onBehalfOfUserId);
            var size = Math.Clamp(pageSize ?? 50, 1, 100); var number = Math.Max(page ?? 0, 0);
            return Results.Ok(new { total = await query.CountAsync(ct), page = number, pageSize = size,
                items = await query.OrderByDescending(x => x.ChangedAt).ThenBy(x => x.Id).Skip(number * size).Take(size).ToListAsync(ct) });
        }).RequirePermission(PermissionCodes.AuditView);
        admin.MapGet("/account-options", async (LacDbContext db, IAccessControlService access, CancellationToken ct) =>
        {
            if (!await CanReadOptions(access, ct)) return Results.Forbid();
            var canAssign = await access.CanAsync(PermissionCodes.AccessManage, cancellationToken: ct) || await access.CanAsync(PermissionCodes.RolesAssign, cancellationToken: ct);
            var roles = new List<object>();
            if (canAssign)
                foreach (var role in await db.Roles.Where(x => x.IsActive && x.RecordStatus == RecordStatus.Active).ToListAsync(ct))
                    if (await AccountSecurity.CanAssignRolesAsync(db, access, [role.Id], ct)) roles.Add(new { role.Id, role.Code, role.Name });
            return Results.Ok(new { canAssignRoles = canAssign, canManageAllocations = await access.CanAsync(PermissionCodes.AllocationsManage, cancellationToken: ct),
                designations = await db.Designations.Where(x => x.IsActive).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct), roles,
                works = await db.WorkDefinitions.Where(x => x.IsActive).Select(x => new { x.Id, x.Code, x.Name, x.Kind, x.WorkstreamId }).ToListAsync(ct),
                workstreams = await db.Workstreams.AsNoTracking().Where(x => x.IsActive && x.RecordStatus == RecordStatus.Active)
                    .OrderBy(x => x.Name).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct),
                desks = await db.OfficeDesks.AsNoTracking().Where(x => x.IsActive && x.RecordStatus == RecordStatus.Active)
                    .OrderBy(x => x.Name).Select(x => new { x.Id, x.Code, x.Name, x.WorkstreamId }).ToListAsync(ct),
                districts = await db.Districts.Select(x => new { x.Id, x.Name }).ToListAsync(ct),
                subdivisions = await db.SubDivisions.Select(x => new { x.Id, x.Name, x.DistrictId }).ToListAsync(ct),
                villages = await db.Villages.Select(x => new { x.Id, x.Name, x.SubDivisionId }).ToListAsync(ct) });
        });
    }

    public static async Task<bool> CanReadOptions(IAccessControlService access, CancellationToken ct) =>
        await access.CanAsync(PermissionCodes.UsersManage, cancellationToken: ct)
        || await access.CanAsync(PermissionCodes.AllocationsManage, cancellationToken: ct)
        || await access.CanAsync(PermissionCodes.WorkCatalogManage, cancellationToken: ct)
        || await access.CanAsync(PermissionCodes.AssistantsManage, cancellationToken: ct)
        || await access.CanAsync(PermissionCodes.AccessManage, cancellationToken: ct);

    public static async Task<object> ReadAllocations(LacDbContext db, Guid userId, CancellationToken ct) =>
        await db.WorkAllocations.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.ValidFrom)
            .Select(x => new { x.Id, x.UserId, x.WorkDefinitionId, workCode = x.WorkDefinition.Code, workName = x.WorkDefinition.Name,
                x.ValidFrom, x.ValidTo, x.WorkOrderReference, x.Reason, x.RevokedAt, x.RevokedByUserId, x.DelegatedFromAllocationId, x.Revision,
                scopes = x.Scopes.Select(s => new { s.Kind, s.DistrictId, s.SubDivisionId, s.VillageId }).ToList() }).ToListAsync(ct);
}
