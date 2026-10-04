using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

public static class MatterContextEndpoints
{
    public static void MapCanonicalContextEndpoints(this RouteGroupBuilder matters)
    {
        matters.MapGet("/{id:guid}/context", async (Guid id, MatterContextQuery query, ICurrentUserContext currentUser, CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            try { return Results.Ok(await query.GetAsync(id, currentUser.UserId.Value, ct)); }
            catch (MatterWorkflowException ex) { return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode); }
        });

        foreach (var (segment, kind) in new[] { ("awards", MatterContextKind.Award), ("khasras", MatterContextKind.Khasra),
                     ("court-cases", MatterContextKind.CourtCase), ("daks", MatterContextKind.Dak) })
        {
            matters.MapGet($"/{{id:guid}}/{segment}", async (Guid id, MatterContextQuery query, ICurrentUserContext currentUser, CancellationToken ct) =>
            {
                if (!currentUser.UserId.HasValue) return Results.Unauthorized();
                try
                {
                    var context = await query.GetAsync(id, currentUser.UserId.Value, ct);
                    object items = kind switch
                    {
                        MatterContextKind.Award => context.Awards, MatterContextKind.Khasra => context.Khasras,
                        MatterContextKind.CourtCase => context.CourtCases, _ => context.Daks
                    };
                    return Results.Ok(new { items, revision = context.Matter.Revision });
                }
                catch (MatterWorkflowException ex) { return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode); }
            });
            matters.MapPut($"/{{id:guid}}/{segment}/{{targetId:guid}}", async (Guid id, Guid targetId, MatterContextLinkCommand request,
                MatterWorkflowService workflow, ICurrentUserContext currentUser, CancellationToken ct) =>
            {
                if (!currentUser.UserId.HasValue) return Results.Unauthorized();
                try { return Results.Ok(await workflow.ChangeContextLinkAsync(id, kind, targetId, true, request, currentUser.UserId.Value, ct)); }
                catch (MatterWorkflowException ex) { return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode); }
            });
            matters.MapDelete($"/{{id:guid}}/{segment}/{{targetId:guid}}", async (Guid id, Guid targetId, int expectedRevision,
                MatterWorkflowService workflow, ICurrentUserContext currentUser, CancellationToken ct) =>
            {
                if (!currentUser.UserId.HasValue) return Results.Unauthorized();
                try { return Results.Ok(await workflow.ChangeContextLinkAsync(id, kind, targetId, false,
                    new MatterContextLinkCommand(expectedRevision), currentUser.UserId.Value, ct)); }
                catch (MatterWorkflowException ex) { return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode); }
            });
        }

        matters.MapGet("/{id:guid}/events", async (Guid id, LacDbContext db, IMatterAuthorizationService matterAuth,
            ICourtAuthorizationService courtAuth, IDakAuthorizationService dakAuth, ICurrentUserContext currentUser, CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;
            if (!await matterAuth.CanAccessMatterAsync(id, PermissionCodes.MatterView, userId, ct)) return Results.Forbid();
            var visible = new List<object>();
            foreach (var ev in await db.MatterEvents.AsNoTracking().Where(x => x.MatterId == id).OrderBy(x => x.SequenceNumber).ToListAsync(ct))
            {
                // Do not expose hidden target IDs through the activity journal either.
                var allowed = ev.ContextEntityId is not Guid target || ev.ContextEntityType switch
                {
                    "Award" => await courtAuth.CanAccessAwardAsync(target, userId, ct),
                    "Khasra" => await courtAuth.CanAccessKhasraAsync(target, userId, ct),
                    "CourtCase" => await courtAuth.CanViewCourtCaseAsync(target, userId, ct),
                    "Dak" => await dakAuth.CanAccessDakAsync(target, PermissionCodes.DakView, userId, ct),
                    _ => true
                };
                if (allowed) visible.Add(new { ev.Id, ev.SequenceNumber, action = ev.Action.ToString(), ev.ActionAt,
                    ev.ActionByDisplayNameSnapshot, ev.ContextEntityType, ev.ContextEntityId });
            }
            return Results.Ok(new { items = visible });
        });
    }
}
