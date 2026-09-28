using LAC.Domain;

namespace LAC.Infrastructure;

public sealed class CourtOperationalNdohRow
{
    public CourtCase Case { get; init; } = null!;
    public DateOnly? OperationalNdoh { get; init; }
    public bool UsesExternalListing { get; init; }
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
                .Select(p => new { p.NextDate, p.CreatedAt })
                .FirstOrDefault(),
            Listing = db.CourtExternalListingObservations
                .Where(o => o.CourtCaseId == c.Id && o.Status == CourtExternalListingStatus.Accepted &&
                    o.ListingDate >= today &&
                    (c.CurrentStatus == null || c.CurrentStatus.Trim().ToLower() != "disposed"))
                .OrderByDescending(o => o.ObservedAt).ThenByDescending(o => o.Id)
                .Select(o => new { o.ListingDate, o.ObservedAt })
                .FirstOrDefault()
        });
        return evidence.Select(x => new CourtOperationalNdohRow
        {
            Case = x.Case,
            UsesExternalListing = x.Listing != null &&
                (x.Proceeding == null || x.Listing.ObservedAt > x.Proceeding.CreatedAt),
            OperationalNdoh = x.Listing != null &&
                (x.Proceeding == null || x.Listing.ObservedAt > x.Proceeding.CreatedAt)
                ? x.Listing.ListingDate
                : x.Proceeding != null ? x.Proceeding.NextDate : null
        });
    }
}
