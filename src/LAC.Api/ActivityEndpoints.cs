namespace LAC.Api;

using System;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public static class ActivityEndpoints
{
    public static RouteGroupBuilder MapActivityEndpoints(this RouteGroupBuilder api)
    {
        var activity = api.MapGroup("/activity");

        // ====================================================================
        // 1. MY HISTORY (Actor GUID Match - No Audit.View required)
        // ====================================================================
        activity.MapGet("/my-history", async (
            DateTimeOffset? dateFrom,
            DateTimeOffset? dateTo,
            string? entityType,
            string? action,
            Guid? workstreamId,
            Guid? deskId,
            string? search,
            bool? includeReads,
            int? page,
            int? pageSize,
            IActivityProjectionService activityService,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue)
                return Results.Unauthorized();

            var query = new ActivityQuery(
                DateFrom: dateFrom,
                DateTo: dateTo,
                EntityType: entityType,
                Action: action,
                WorkstreamId: workstreamId,
                DeskId: deskId,
                Search: search,
                IncludeReads: includeReads ?? true,
                Page: page ?? 1,
                PageSize: pageSize ?? 20
            );

            try
            {
                var result = await activityService.GetMyHistoryAsync(currentUser.UserId.Value, query, ct);
                return Results.Ok(result);
            }
            catch (ActivityAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 2. TEAM ACTIVITY (Scoped by Audit.View)
        // ====================================================================
        activity.MapGet("/team", async (
            DateTimeOffset? dateFrom,
            DateTimeOffset? dateTo,
            string? entityType,
            string? action,
            Guid? workstreamId,
            Guid? deskId,
            Guid? actorUserId,
            string? search,
            bool? includeReads,
            int? page,
            int? pageSize,
            IActivityProjectionService activityService,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue)
                return Results.Unauthorized();

            var query = new ActivityQuery(
                DateFrom: dateFrom,
                DateTo: dateTo,
                EntityType: entityType,
                Action: action,
                WorkstreamId: workstreamId,
                DeskId: deskId,
                ActorUserId: actorUserId,
                Search: search,
                IncludeReads: includeReads ?? true,
                Page: page ?? 1,
                PageSize: pageSize ?? 20
            );

            try
            {
                var result = await activityService.GetTeamActivityAsync(currentUser.UserId.Value, query, ct);
                return Results.Ok(result);
            }
            catch (ActivityAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 3. TEAM FILTER OPTIONS
        // ====================================================================
        activity.MapGet("/team/filter-options", async (
            IActivityProjectionService activityService,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue)
                return Results.Unauthorized();

            try
            {
                var result = await activityService.GetTeamFilterOptionsAsync(currentUser.UserId.Value, ct);
                return Results.Ok(result);
            }
            catch (ActivityAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: ex.StatusCode);
            }
        });

        return activity;
    }
}
