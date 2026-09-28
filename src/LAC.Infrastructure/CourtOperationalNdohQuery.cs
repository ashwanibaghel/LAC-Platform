using LAC.Domain;

namespace LAC.Infrastructure;

public sealed class CourtOperationalNdohRow
{
    public CourtCase Case { get; init; } = null!;
    public DateOnly? OperationalNdoh { get; init; }
    public bool UsesExternalListing { get; init; }
    public bool UsesHistoricalListing { get; init; }
}

// Shared SQL-translatable resolver for directory filters/sort and case detail.
// The established proceeding selection order remains unchanged. Calendar dates
// never participate. Listing evidence only wins after the selected proceeding
// was recorded and while its official list date is still future/current.
public static class CourtOperationalNdohQuery
{
    public static IQueryable<CourtOperationalNdohRow> Resolve(IQueryable<CourtCase> cases,
        LacDbContext db, DateOnly today)
    {
        var evidence = cases.Select(c => new
        {
            Case = c,
            Proceeding = c.Proceedings.Where(p => p.RecordStatus == RecordStatus.Active)
                .OrderByDescending(p => p.ProceedingDate.HasValue)
                .ThenByDescending(p => p.ProceedingDate)
                .ThenByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .Select(p => new { p.NextDate, p.CreatedAt, p.ProceedingDate, p.SourceKind })
                .FirstOrDefault(),
            Listing = db.CourtExternalListingObservations
                .Where(o => o.CourtCaseId == c.Id && o.Status == CourtExternalListingStatus.Accepted &&
                    o.Mode == CourtExternalSyncMode.LiveWindow &&
                    o.ListingDate >= today &&
                    (c.CurrentStatus == null || c.CurrentStatus.Trim().ToLower() != "disposed"))
                .OrderByDescending(o => o.ObservedAt).ThenByDescending(o => o.Id)
                .Select(o => new { o.ListingDate, o.ObservedAt })
                .FirstOrDefault(),
            Historical = db.CourtExternalListingObservations
                .Where(o => o.CourtCaseId == c.Id && o.Status == CourtExternalListingStatus.Accepted &&
                    o.Mode == CourtExternalSyncMode.HistoricalBackfill && o.ListingDate < today)
                .OrderByDescending(o => o.ListingDate).ThenByDescending(o => o.Id)
                .Select(o => (DateOnly?)o.ListingDate).FirstOrDefault()
        });
        return evidence.Select(x => new CourtOperationalNdohRow
        {
            Case = x.Case,
            UsesExternalListing = x.Listing != null &&
                (x.Proceeding == null || x.Listing.ObservedAt > x.Proceeding.CreatedAt) ||
                (x.Proceeding != null && x.Proceeding.ProceedingDate == null &&
                 x.Proceeding.SourceKind == "LegacyRegisterNDOH" && x.Proceeding.NextDate != null &&
                 x.Historical > x.Proceeding.NextDate && x.Case.CourtName == "Delhi High Court" &&
                 x.Case.CurrentStatus != null && x.Case.CurrentStatus.Trim().ToLower() == "pending"),
            UsesHistoricalListing = !(x.Listing != null &&
                (x.Proceeding == null || x.Listing.ObservedAt > x.Proceeding.CreatedAt)) &&
                x.Proceeding != null && x.Proceeding.ProceedingDate == null &&
                x.Proceeding.SourceKind == "LegacyRegisterNDOH" && x.Proceeding.NextDate != null &&
                x.Historical > x.Proceeding.NextDate && x.Case.CourtName == "Delhi High Court" &&
                x.Case.CurrentStatus != null && x.Case.CurrentStatus.Trim().ToLower() == "pending",
            OperationalNdoh = x.Listing != null &&
                (x.Proceeding == null || x.Listing.ObservedAt > x.Proceeding.CreatedAt)
                ? x.Listing.ListingDate
                : x.Proceeding != null && x.Proceeding.ProceedingDate == null &&
                  x.Proceeding.SourceKind == "LegacyRegisterNDOH" && x.Proceeding.NextDate != null &&
                  x.Historical > x.Proceeding.NextDate && x.Case.CourtName == "Delhi High Court" &&
                  x.Case.CurrentStatus != null && x.Case.CurrentStatus.Trim().ToLower() == "pending"
                ? x.Historical
                : x.Proceeding != null ? x.Proceeding.NextDate : null
        });
    }
}
