using LAC.Infrastructure;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

public static class CourtStructuredEndpoints
{
    public static void MapCourtStructuredEndpoints(this RouteGroupBuilder api)
    {
        foreach(var (route,type) in new[]{("villages","Village"),("awards","Award"),("khasras","Khasra")})
        {
            var entityType=type;
            api.MapGet($"/{route}/{{id:guid}}/court-orders",async(Guid id,CourtConfirmedOrderProjection projection,ICurrentUserContext user,CancellationToken ct)=>
            {
                if(user.UserId is not Guid actor)return Results.Unauthorized();
                try{return Results.Ok(await projection.ReadAsync(entityType,id,actor,ct));}
                catch(CourtWorkflowException e){return Results.Json(new {error=e.Message},statusCode:e.StatusCode);}
            });
        }
        api.MapPost("/court-cases/{id:guid}/intelligence/persist",async(Guid id,CourtStructuredIntelligenceService service,
            CourtOrderLinkReview review,LacDbContext db,ICourtAuthorizationService auth,ICurrentUserContext user,CancellationToken ct)=>
        {
            if(user.UserId is not Guid actor)return Results.Unauthorized();
            if(!await auth.CanEditCourtCaseAsync(id,actor,ct))return Results.Forbid();
            var index=await CourtIntelligenceCaseData.LoadAsync(db,id,ct);if(index is null)return Results.NotFound();
            try
            {
                var count=await service.IngestAsync(index,ct);
                var revisions=await db.CourtOrderIntelligenceRevisions.Where(r=>r.Order.CourtCaseId==id).Select(r=>r.Id).ToListAsync(ct);
                foreach(var revision in revisions)await review.MatchAsync(revision,actor,ct);
                return Results.Ok(new {ordersPersisted=count});
            }
            catch(Exception e) when(e is InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {return Results.Problem("Structured order evidence needs review.",statusCode:422);}
            catch(DbUpdateException){return Results.Conflict(new {error="Order evidence changed concurrently. Reload and retry."});}
        });
        api.MapPut("/court-order-links/{linkId:guid}/review",async(Guid linkId,CourtLinkReviewRequest request,
            CourtOrderLinkReview review,ICurrentUserContext user,CancellationToken ct)=>
        {
            if(user.UserId is not Guid actor)return Results.Unauthorized();
            try{await review.ReviewAsync(linkId,request,actor,ct);return Results.NoContent();}
            catch(CourtWorkflowException e){return Results.Json(new {error=e.Message},statusCode:e.StatusCode);}
            catch(DbUpdateException){return Results.Conflict(new {error="Another review changed this link. Reload."});}
        });
    }
}
