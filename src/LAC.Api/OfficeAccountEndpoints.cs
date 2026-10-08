using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

public static class OfficeAccountEndpoints
{
    public static void MapOfficeAccountEndpoints(this RouteGroupBuilder api)
    {
        var office = api.MapGroup("/office");
        office.MapGet("/me", async (OfficeAccountService service, ICurrentUserContext current, CancellationToken ct) =>
            Results.Ok(await service.Detail(current.UserId!.Value, ct)));
        var accounts = office.MapGroup("/accounts").AddEndpointFilter(async (ctx, next) =>
        {
            var db = ctx.HttpContext.RequestServices.GetRequiredService<LacDbContext>();
            var actor = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>().UserId!.Value;
            return await OfficeAuthorityService.GetAsync(db, actor, ctx.HttpContext.RequestAborted) >= OfficeAuthority.OFFICE_SUPERVISOR
                ? await next(ctx) : Results.Forbid();
        });
        accounts.MapGet("/options", async (LacDbContext db, ICurrentUserContext actor, CancellationToken ct) =>
        {
            var authority = await OfficeAuthorityService.GetAsync(db, actor.UserId!.Value, ct);
            return Results.Ok(new { authority, canAssignOfficeSupervisor = authority >= OfficeAuthority.OFFICE_ADMIN,
                canAssignOfficeAdmin = authority == OfficeAuthority.SYSTEM_ADMIN, canAssignSystemAdmin = authority == OfficeAuthority.SYSTEM_ADMIN,
                authorities = Enum.GetValues<OfficeAuthority>().Where(a => a != OfficeAuthority.HELPER && OfficeAuthorityService.CanGrant(authority, a)),
                modules = Enum.GetValues<OfficeModule>(), landAccess = Enum.GetValues<LandAccessLevel>(),
                designations = await db.Designations.Where(d => d.IsActive && d.RecordStatus == RecordStatus.Active).OrderBy(d => d.DisplayOrder).Select(d => new { d.Id, d.Code, d.Name }).ToListAsync(ct),
                desks = await db.OfficeDesks.Where(d => d.IsActive && d.RecordStatus == RecordStatus.Active).Select(d => new { d.Id, d.Code, d.Name, d.WorkstreamId }).ToListAsync(ct) });
        });
        accounts.MapGet("", async (LacDbContext db, OfficeAccountService service, CancellationToken ct) =>
        {
            var ids = await db.AppUsers.Where(u => u.RecordStatus == RecordStatus.Active).OrderBy(u => u.Username).Select(u => u.Id).ToListAsync(ct);
            var result = new List<object>();
            foreach (var id in ids) result.Add(await service.Detail(id, ct));
            return Results.Ok(result);
        });
        accounts.MapGet("/{id:guid}", async (Guid id, LacDbContext db, OfficeAccountService service, CancellationToken ct) =>
            await db.AppUsers.AnyAsync(u => u.Id == id && u.RecordStatus == RecordStatus.Active, ct) ? Results.Ok(await service.Detail(id, ct)) : Results.NotFound());
        accounts.MapPost("", async (CreateOfficeAccountRequest request, OfficeAccountService service, LacDbContext db, ICurrentUserContext actor, CancellationToken ct) =>
        {
            var created = await service.Create(actor.UserId!.Value, request, ct);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/office/accounts/{created.User.Id}", new { account = await service.Detail(created.User.Id, ct),
                temporaryCredential = created.Credential, credentialExpiresAt = created.User.TemporaryCredentialExpiresAt });
        });
        accounts.MapPut("/{id:guid}", async (Guid id, UpdateOfficeAccountRequest request, OfficeAccountService service, LacDbContext db, ICurrentUserContext actor, CancellationToken ct) =>
        {
            var target = await db.AppUsers.SingleOrDefaultAsync(u => u.Id == id && u.RecordStatus == RecordStatus.Active, ct);
            if (target is null) return Results.NotFound();
            if (!await OfficeAuthorityService.CanManageAsync(db, actor.UserId!.Value, id, ct)) return Results.Forbid();
            if (target.OfficeRevision != request.ExpectedRevision) return Results.Conflict();
            await service.Apply(actor.UserId.Value, target, request.Account, false, ct);
            await db.SaveChangesAsync(ct); return Results.Ok(await service.Detail(id, ct));
        });
        accounts.MapPost("/{id:guid}/helpers", async (Guid id, CreateOfficeHelperRequest request, OfficeAccountService service, LacDbContext db, ICurrentUserContext actor, CancellationToken ct) =>
            await CreateHelper(service, db, actor.UserId!.Value, id, request, ct));
        MapHelperRoutes(office.MapGroup("/me/helpers"));
        accounts.MapPut("/helpers/{id:guid}", async (Guid id, UpdateOfficeHelperRequest request, OfficeAccountService service, LacDbContext db, ICurrentUserContext actor, CancellationToken ct) =>
            await EditHelper(service, db, actor.UserId!.Value, id, request, false, ct));
        foreach (var action in new[] { "reset-credential", "toggle-status" })
        {
            var operation = action;
            accounts.MapPost("/{id:guid}/" + operation, async (Guid id, RevisionRequest request, LacDbContext db, ICurrentUserContext actor,
                IPasswordHasher<AppUser> hasher, OfficeAccountService service, CancellationToken ct) =>
                await Maintain(db, actor.UserId!.Value, id, request, operation, hasher, service, false, ct));
        }
    }
    private static void MapHelperRoutes(RouteGroupBuilder helpers)
    {
        helpers.AddEndpointFilter(async (ctx, next) =>
        {
            var db = ctx.HttpContext.RequestServices.GetRequiredService<LacDbContext>();
            var actor = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>().UserId!.Value;
            return await OfficeAuthorityService.GetAsync(db, actor, ctx.HttpContext.RequestAborted) != OfficeAuthority.HELPER ? await next(ctx) : Results.Forbid();
        });
        helpers.MapGet("", async (LacDbContext db, OfficeAccountService service, ICurrentUserContext actor, CancellationToken ct) =>
        {
            var result = new List<object>();
            foreach (var id in await db.AppUsers.Where(u => u.SupervisingOfficerId == actor.UserId && u.RecordStatus == RecordStatus.Active).Select(u => u.Id).ToListAsync(ct))
                result.Add(await service.Detail(id, ct));
            return Results.Ok(result);
        });
        helpers.MapGet("/options", async (LacDbContext db, ICurrentUserContext actor, CancellationToken ct) => Results.Ok(new {
            designation = await db.Designations.Where(d => d.Code == "DEO" && d.IsActive && d.RecordStatus == RecordStatus.Active).Select(d => new { d.Id, d.Name }).SingleAsync(ct),
            desks = await db.UserDeskMemberships.Where(m => m.UserId == actor.UserId && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active
                && m.OfficeDesk.IsActive && m.OfficeDesk.RecordStatus == RecordStatus.Active).Select(m => new { m.OfficeDesk.Id, m.OfficeDesk.Name }).ToListAsync(ct),
            access = Enum.GetValues<HelperAccessLevel>() }));
        helpers.MapPost("", async (CreateOfficeHelperRequest request, OfficeAccountService service, LacDbContext db, ICurrentUserContext actor, CancellationToken ct) =>
            await CreateHelper(service, db, actor.UserId!.Value, actor.UserId.Value, request, ct));
        helpers.MapPut("/{id:guid}", async (Guid id, UpdateOfficeHelperRequest request, OfficeAccountService service, LacDbContext db, ICurrentUserContext actor, CancellationToken ct) =>
            await EditHelper(service, db, actor.UserId!.Value, id, request, true, ct));
        foreach (var action in new[] { "reset-credential", "toggle-status" })
        {
            var operation = action;
            helpers.MapPost("/{id:guid}/" + operation, async (Guid id, RevisionRequest request, LacDbContext db, ICurrentUserContext actor,
                IPasswordHasher<AppUser> hasher, OfficeAccountService service, CancellationToken ct) =>
                await Maintain(db, actor.UserId!.Value, id, request, operation, hasher, service, true, ct));
        }
    }
    private static async Task<IResult> CreateHelper(OfficeAccountService service, LacDbContext db, Guid actor, Guid parent, CreateOfficeHelperRequest request, CancellationToken ct)
    {
        var created = await service.CreateHelper(actor, parent, request, ct);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/office/accounts/{created.User.Id}", new { account = await service.Detail(created.User.Id, ct),
            temporaryCredential = created.Credential, credentialExpiresAt = created.User.TemporaryCredentialExpiresAt });
    }
    private static async Task<IResult> EditHelper(OfficeAccountService service, LacDbContext db, Guid actor, Guid id, UpdateOfficeHelperRequest request, bool own, CancellationToken ct)
    {
        var target = await db.AppUsers.SingleOrDefaultAsync(u => u.Id == id && u.SupervisingOfficerId != null && u.RecordStatus == RecordStatus.Active, ct);
        if (target is null || own && target.SupervisingOfficerId != actor) return Results.NotFound();
        if (!await OfficeAuthorityService.CanManageAsync(db, actor, id, ct)) return Results.Forbid();
        if (target.AssistantRevision != request.ExpectedRevision || !target.IsActive) return Results.Conflict();
        await service.ApplyHelper(actor, target, request.Helper, ct); await db.SaveChangesAsync(ct);
        return Results.Ok(await service.Detail(id, ct));
    }
    private static async Task<IResult> Maintain(LacDbContext db, Guid actor, Guid id, RevisionRequest request, string operation,
        IPasswordHasher<AppUser> hasher, OfficeAccountService service, bool own, CancellationToken ct)
    {
        var user = await db.AppUsers.SingleOrDefaultAsync(u => u.Id == id && u.RecordStatus == RecordStatus.Active, ct);
        if (user is null || own && user.SupervisingOfficerId != actor) return Results.NotFound();
        if (!await OfficeAuthorityService.CanManageAsync(db, actor, id, ct)) return Results.Forbid();
        if (user.OfficeRevision != request.ExpectedRevision) return Results.Conflict();
        if (operation == "toggle-status" && user.IsActive && await OfficeAuthorityService.GetAsync(db, id, ct, true) == OfficeAuthority.SYSTEM_ADMIN
            && !await db.UserRoles.AnyAsync(r => r.UserId != id && r.Role.Code == "SYSTEM_ADMIN" && r.Role.IsActive
                && r.Role.RecordStatus == RecordStatus.Active && r.User.IsActive && r.User.RecordStatus == RecordStatus.Active, ct))
            return Results.BadRequest(new { message = "Cannot deactivate the last active System Administrator." });
        string? credential = null;
        if (operation == "toggle-status") user.IsActive = !user.IsActive;
        else
        {
            if (!user.IsActive) return Results.BadRequest();
            credential = TemporaryCredentials.Issue(user, hasher);
        }
        await OfficeSessionSecurity.InvalidateAsync(db, user, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { account = await service.Detail(id, ct), temporaryCredential = credential, credentialExpiresAt = user.TemporaryCredentialExpiresAt });
    }
}
