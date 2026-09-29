using LAC.Domain;

namespace LAC.Infrastructure;

public sealed class CourtOperationalNdohRow
{
    public CourtCase Case { get; init; } = null!;
    public DateOnly? OperationalNdoh { get; init; }
    public bool UsesExternalListing { get; init; }
    public bool UsesHistoricalListing { get; init; }
    public bool UsesAssistedStatus { get; init; }
    public bool HasOfficialConflict { get; init; }
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
                .Select(o => (DateOnly?)o.ListingDate).FirstOrDefault(),
            Assisted = db.CourtExternalCaseStatusObservations
                .Where(o => o.CourtCaseId == c.Id && o.Status == DhcAssistedEvidenceStatus.Accepted &&
                    o.ListingDate >= today && c.RecordStatus == RecordStatus.Active &&
                    c.CourtName == "Delhi High Court" && c.CurrentStatus != null &&
                    c.CurrentStatus.Trim().ToLower() == "pending")
                .OrderByDescending(o => o.ObservedAt).ThenByDescending(o => o.Id)
                .Select(o => new { o.ListingDate, o.ObservedAt }).FirstOrDefault(),
            OfficialConflict = db.CourtExternalCaseStatusObservations.Any(o =>
                o.CourtCaseId == c.Id && o.ListingDate >= today && o.ReviewReason == "DateConflict") ||
                db.CourtExternalCaseStatusObservations.Any(o =>
                    o.CourtCaseId == c.Id && o.ListingDate >= today &&
                    o.Status == DhcAssistedEvidenceStatus.Accepted &&
                    db.CourtExternalListingObservations.Any(l => l.CourtCaseId == c.Id &&
                        l.Mode == CourtExternalSyncMode.LiveWindow &&
                        l.Status == CourtExternalListingStatus.Accepted && l.ListingDate >= today &&
                        l.ListingDate != o.ListingDate))
        });
        var choice = evidence.Select(x => new
        {
            x.Case, x.Proceeding, x.Listing, x.Assisted, x.Historical, x.OfficialConflict,
            AssistedWins = !x.OfficialConflict && x.Assisted != null &&
                (x.Listing == null || x.Assisted.ObservedAt > x.Listing.ObservedAt) &&
                (x.Proceeding == null || x.Assisted.ObservedAt > x.Proceeding.CreatedAt),
            ListingWins = !x.OfficialConflict && x.Listing != null &&
                (x.Assisted == null || x.Listing.ObservedAt >= x.Assisted.ObservedAt) &&
                (x.Proceeding == null || x.Listing.ObservedAt > x.Proceeding.CreatedAt),
            HistoricalWins = x.Proceeding != null && x.Proceeding.ProceedingDate == null &&
                x.Proceeding.SourceKind == "LegacyRegisterNDOH" && x.Proceeding.NextDate != null &&
                x.Historical > x.Proceeding.NextDate && x.Case.CourtName == "Delhi High Court" &&
                x.Case.CurrentStatus != null && x.Case.CurrentStatus.Trim().ToLower() == "pending"
        });
        return choice.Select(x => new CourtOperationalNdohRow
        {
            Case = x.Case,
            UsesExternalListing = x.ListingWins || (!x.AssistedWins && !x.ListingWins && x.HistoricalWins),
            UsesHistoricalListing = !x.AssistedWins && !x.ListingWins && x.HistoricalWins,
            UsesAssistedStatus = x.AssistedWins,
            HasOfficialConflict = x.OfficialConflict,
            OperationalNdoh = x.AssistedWins ? x.Assisted!.ListingDate :
                x.ListingWins ? x.Listing!.ListingDate :
                x.HistoricalWins ? x.Historical :
                x.Proceeding != null ? x.Proceeding.NextDate : null
        });
    }
}
