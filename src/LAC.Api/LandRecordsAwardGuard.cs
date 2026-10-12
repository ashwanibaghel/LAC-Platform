using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

internal static class LandRecordsAwardGuard
{
    public static RouteHandlerBuilder RequireAwardRecordPermission(this RouteHandlerBuilder builder, string permission, string workstream = WorkstreamCodes.Award)
    {
        return builder.RequireAuthorization().AddEndpointFilter(async (context, next) =>
        {
            var user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
            if (!user.UserId.HasValue) return Results.Unauthorized();
            var values = context.HttpContext.Request.RouteValues;
            if (!Guid.TryParse((values.GetValueOrDefault("id") ?? values.GetValueOrDefault("awardId"))?.ToString(), out var id)) return Results.BadRequest();
            var db = context.HttpContext.RequestServices.GetRequiredService<LacDbContext>();
            var award = await db.Awards.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, context.HttpContext.RequestAborted);
            var access = context.HttpContext.RequestServices.GetRequiredService<IAccessControlService>();
            var resource = new AccessResourceContext(WorkstreamCode: workstream, OwnerUserId: Guid.TryParse(award?.CreatedBy, out var owner) ? owner : null);
            if (!await access.CanAsync(permission, resource, context.HttpContext.RequestAborted)) return Results.StatusCode(403);
            return await next(context);
        });
    }
    public static RouteHandlerBuilder RequireLandRecordDocumentAccess(this RouteHandlerBuilder builder)
    {
        return builder.RequireAuthorization().AddEndpointFilter(async (context, next) =>
        {
            var user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
            if (!user.UserId.HasValue) return Results.Unauthorized();
            var access = context.HttpContext.RequestServices.GetRequiredService<IAccessControlService>();
            foreach (var workstream in new[] { WorkstreamCodes.Award, WorkstreamCodes.LandRecords, WorkstreamCodes.Possession })
                if (await access.CanAsync(PermissionCodes.AwardView, new(WorkstreamCode: workstream, OwnerUserId: user.UserId), context.HttpContext.RequestAborted)) return await next(context);
            return Results.StatusCode(403);
        });
    }
    public static void GuardArchivedAwardWrites(this RouteGroupBuilder api)
    {
        api.AddEndpointFilter(async (context, next) =>
        {
            var request = context.HttpContext.Request;
            var path = request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
            if (path.Length < 3 || path[1] != "awards" || !Guid.TryParse(path[2], out var id)) return await next(context);
            var db = context.HttpContext.RequestServices.GetRequiredService<LacDbContext>();
            var award = await db.Awards.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.RecordStatus, x.CreatedBy }).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
            if (award is null || award.RecordStatus == RecordStatus.Active) return await next(context);
            var user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
            if (!user.IsAuthenticated) return Results.Unauthorized();
            if (request.Method != "GET" && path.LastOrDefault() != "lifecycle") return Results.Json(new { code = "AwardArchived", message = "Restore the Award before changing official records." }, statusCode: 409);
            var access = context.HttpContext.RequestServices.GetRequiredService<IAccessControlService>();
            var resource = new AccessResourceContext(WorkstreamCode: WorkstreamCodes.Award, OwnerUserId: Guid.TryParse(award.CreatedBy, out var owner) ? owner : null);
            if (!await access.CanAsync(PermissionCodes.AuditView, resource, context.HttpContext.RequestAborted)) return Results.StatusCode(403);
            return await next(context);
        });
    }
}
