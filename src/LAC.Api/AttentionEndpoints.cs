namespace LAC.Api;

using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public static class AttentionEndpoints
{
    public static RouteGroupBuilder MapAttentionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/attention");

        // 1. My Attention
        group.MapGet("/my", async (
            string? bucket,
            string? sourceType,
            Guid? workstreamId,
            Guid? deskId,
            string? priority,
            string? q,
            int? page,
            int? pageSize,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();

            if (!string.IsNullOrWhiteSpace(priority) && !Enum.TryParse<ScheduledEventPriority>(priority, true, out _))
            {
                return Results.Json(new { error = $"Invalid priority: '{priority}'." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var query = new AttentionQuery(
                Bucket: bucket,
                SourceType: sourceType,
                WorkstreamId: workstreamId,
                DeskId: deskId,
                Priority: priority,
                Search: q,
                Page: page ?? 1,
                PageSize: pageSize ?? 25
            );

            try
            {
                var result = await projection.GetMyAttentionAsync(currentUser.UserId.Value, query, ct);
                return Results.Ok(result);
            }
            catch (ScheduleWorkflowException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // 2. Branch Attention
        group.MapGet("/branch", async (
            string? bucket,
            string? sourceType,
            Guid? workstreamId,
            Guid? deskId,
            string? priority,
            string? q,
            int? page,
            int? pageSize,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();

            if (!string.IsNullOrWhiteSpace(priority) && !Enum.TryParse<ScheduledEventPriority>(priority, true, out _))
            {
                return Results.Json(new { error = $"Invalid priority: '{priority}'." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var query = new AttentionQuery(
                Bucket: bucket,
                SourceType: sourceType,
                WorkstreamId: workstreamId,
                DeskId: deskId,
                Priority: priority,
                Search: q,
                Page: page ?? 1,
                PageSize: pageSize ?? 25
            );

            try
            {
                var result = await projection.GetBranchAttentionAsync(currentUser.UserId.Value, query, ct);
                return Results.Ok(result);
            }
            catch (ScheduleWorkflowException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        return api;
    }
}
