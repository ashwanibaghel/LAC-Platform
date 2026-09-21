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

public sealed record CourtProceedingDto(
    Guid Id,
    Guid CourtCaseId,
    DateOnly? ProceedingDate,
    string? OrderType,
    string? RestraintNature,
    string? Summary,
    DateOnly? NextDate,
    DateTimeOffset CreatedAt
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
            ICourtAuthorizationService courtAuth,
            LacDbContext db,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            if (!await courtAuth.CanEditCourtReferencesAsync(currentUser.UserId.Value, ct))
                return Results.Forbid();

            var courtCase = await db.CourtCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (courtCase is null) return Results.NotFound(new { error = "Court case not found." });

            if (req.NextDate.HasValue && req.ProceedingDate.HasValue && req.NextDate.Value < req.ProceedingDate.Value)
            {
                return Results.BadRequest(new { error = "NextDate cannot be earlier than ProceedingDate." });
            }

            var proceeding = new CourtProceeding
            {
                CourtCaseId = id,
                ProceedingDate = req.ProceedingDate,
                OrderType = req.OrderType,
                RestraintNature = req.RestraintNature,
                Summary = req.Summary,
                NextDate = req.NextDate,
                CreatedBy = currentUser.Username ?? "System",
                RecordStatus = RecordStatus.Active
            };

            db.Set<CourtProceeding>().Add(proceeding);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/court-cases/{id}/proceedings/{proceeding.Id}", new CourtProceedingDto(
                proceeding.Id,
                proceeding.CourtCaseId,
                proceeding.ProceedingDate,
                proceeding.OrderType,
                proceeding.RestraintNature,
                proceeding.Summary,
                proceeding.NextDate,
                proceeding.CreatedAt
            ));
        });

        return group;
    }
}
