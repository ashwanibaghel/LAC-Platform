namespace LAC.Api;

using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

public sealed record CreateCourtProceedingApiRequest(
    DateOnly? ProceedingDate,
    string? OrderType,
    string? RestraintNature,
    string? Summary,
    DateOnly? NextDate
);

public static class CourtEndpoints
{
    public static RouteGroupBuilder MapCourtEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/court-cases");

        // 1. Get Proceedings for a Court Case
        group.MapGet("/{id:guid}/proceedings", async (
            Guid id,
            ICourtAuthorizationService courtAuth,
            LacDbContext db,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            if (!await courtAuth.CanViewCourtReferencesAsync(currentUser.UserId.Value, ct))
                return Results.Forbid();

            var courtCase = await db.CourtCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (courtCase is null) return Results.NotFound(new { error = "Court case not found." });

            var proceedings = await db.Set<CourtProceeding>().AsNoTracking()
                .Where(p => p.CourtCaseId == id && p.RecordStatus == RecordStatus.Active)
                .OrderByDescending(p => p.ProceedingDate)
                .ThenByDescending(p => p.CreatedAt)
                .Select(p => new CourtProceedingDto(
                    p.Id,
                    p.CourtCaseId,
                    p.ProceedingDate,
                    p.OrderType,
                    p.RestraintNature,
                    p.Summary,
                    p.NextDate,
                    p.CreatedAt
                ))
                .ToListAsync(ct);

            return Results.Ok(proceedings);
        });

        // 2. Create Proceeding for a Court Case
        group.MapPost("/{id:guid}/proceedings", async (
            Guid id,
            CreateCourtProceedingApiRequest req,
            ICourtWorkflowService courtWorkflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();

            try
            {
                var command = new RecordCourtProceedingCommand(
                    req.ProceedingDate,
                    req.OrderType,
                    req.RestraintNature,
                    req.Summary,
                    req.NextDate
                );

                var result = await courtWorkflow.RecordProceedingAsync(id, command, currentUser.UserId.Value, ct);
                return Results.Created($"/api/court-cases/{id}/proceedings/{result.Id}", result);
            }
            catch (CourtWorkflowException ex)
            {
                return ex.StatusCode switch
                {
                    400 => Results.BadRequest(new { error = ex.Message }),
                    401 => Results.Unauthorized(),
                    403 => Results.Forbid(),
                    404 => Results.NotFound(new { error = ex.Message }),
                    409 => Results.Conflict(new { error = ex.Message }),
                    _ => Results.Problem(ex.Message, statusCode: ex.StatusCode)
                };
            }
        });

        return group;
    }
}
