using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LAC.Infrastructure;

// Caller first validates the artifact against the registered case index. No model or canonical writes here.
public sealed class CourtIntelligencePersistence(LacDbContext db, IOptions<CourtIntelligenceOfficeOptions>? options = null)
{
    public async Task<CourtOrderIntelligenceRevision> PersistAsync(Guid caseId, Guid observationId,
        DateOnly date, string officialUrl, JsonObject scope, string sourceKind = "Order", CancellationToken ct = default)
    {
        var observation = await db.CourtExternalOrderObservations.AsNoTracking().SingleAsync(x => x.Id == observationId, ct);
        if (observation.CourtCaseId != caseId || observation.OrderDate != date
            || officialUrl != observation.OfficialUrl && officialUrl != observation.CorrigendumUrl
            || observation.NormalizedCaseIdentity != CourtImportService.Identity("Delhi High Court", observation.RawCaseNumber))
            throw new InvalidDataException("Order intelligence does not match its registered observation.");
        var pdfHash = scope["source"]!["pdfSha256"]?.GetValue<string>() ?? "";
        CourtScopeContract.Validate(scope, pdfHash);
        if (scope["source"]!["officialUrl"]?.GetValue<string>() != officialUrl || scope["source"]!["orderDate"]?.GetValue<string>() != date.ToString("yyyy-MM-dd"))
            throw new InvalidDataException("Structured source identity mismatch.");
        scope = await new CourtOfficeAuthority(db, options ?? Options.Create(new CourtIntelligenceOfficeOptions())).ResolveAsync(scope, ct);
        var json = scope.ToJsonString();
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var order = await db.CourtOrderIntelligence.Include(x => x.Revisions)
            .SingleOrDefaultAsync(x => x.CourtCaseId == caseId && x.OrderDate == date && x.OfficialUrl == officialUrl, ct);
        if (order is null)
        {
            order = new CourtOrderIntelligence { CourtCaseId = caseId, OrderDate = date, OfficialUrl = officialUrl, SourceKind = sourceKind };
            db.CourtOrderIntelligence.Add(order);
        }
        if (officialUrl == observation.CorrigendumUrl && officialUrl != observation.OfficialUrl)
        {
            order.SourceKind = "Corrigendum";
            order.CorrectsOrderId = await db.CourtOrderIntelligence.Where(x => x.CourtCaseId == caseId && x.OrderDate == date && x.OfficialUrl == observation.OfficialUrl).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        }
        var prior = order.Revisions.SingleOrDefault(x => x.SourceObservationId == observationId && x.PayloadSha256 == payloadHash);
        if (prior is not null)
        {
            // The original may be registered after its independently retrieved correction.
            if(db.Entry(order).State==EntityState.Modified)await db.SaveChangesAsync(ct);
            return prior;
        }
        var revision = new CourtOrderIntelligenceRevision
        {
            Order = order, CourtOrderIntelligenceId = order.Id, SourceObservationId = observationId,
            PdfSha256 = pdfHash, PayloadSha256 = payloadHash, StructuredFactsJson = json,
            ExtractionState = scope["extraction"]!["state"]!.GetValue<string>(),
            LacRelevant = scope["lacRelevant"]!.GetValue<bool>(), LacRelevanceState = scope["lacRelevanceState"]!.GetValue<string>(),
            LacAuthorityScope = scope["lacAuthorityScope"]!.GetValue<string>(), LacActionable = scope["lacActionable"]!.GetValue<bool>()
        };
        db.CourtOrderIntelligenceRevisions.Add(revision);
        await db.SaveChangesAsync(ct);
        return revision;
    }

    public static void GuardImmutableRevisions(LacDbContext db)
    {
        if (db.ChangeTracker.Entries<CourtOrderIntelligenceRevision>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Court intelligence revisions are immutable. Append a new revision.");
    }
}
