using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

// Passive persistence of already published, source-validated local snapshots.
// No retrieval, model calls, canonical linking, or official workflow mutation.
public sealed class CourtScopeIngestionWorker(IServiceScopeFactory scopes,ILogger<CourtScopeIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try
            {
                using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<LacDbContext>();
                var cases=await db.CourtExternalOrderObservations.AsNoTracking().Select(o=>o.CourtCaseId).Distinct().ToListAsync(ct);
                foreach(var id in cases)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var index=await CourtIntelligenceCaseData.LoadAsync(db,id,ct);
                        if(index is not null)await scope.ServiceProvider.GetRequiredService<CourtStructuredIntelligenceService>().IngestAsync(index,ct);
                    }
                    catch(Exception e) when(e is InvalidDataException or IOException or System.Text.Json.JsonException or DbUpdateException)
                    { db.ChangeTracker.Clear();logger.LogWarning("Structured Court evidence awaiting revalidation ({ErrorType}).",e.GetType().Name); }
                }
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){return;}
            catch(Exception e){logger.LogWarning("Structured Court persistence temporarily unavailable ({ErrorType}).",e.GetType().Name);}
            await Task.Delay(TimeSpan.FromSeconds(30),ct);
        }
    }
}
