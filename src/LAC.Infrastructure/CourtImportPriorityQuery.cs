using LAC.Domain;

namespace LAC.Infrastructure;

// A working queue only; source classification and canonical hearing dates stay unchanged.
public static class CourtImportPriorityQuery
{
    public static IQueryable<CourtImportRow> Urgent(IQueryable<CourtImportRow> rows, DateOnly today)
    {
        var end = today.AddDays(7);
        return rows.Where(x => x.Batch.RecordStatus == RecordStatus.Active &&
            x.Batch.Status == CourtImportBatchStatus.Parsed &&
            x.CommitStatus != CourtImportCommitStatus.Committed &&
            x.ResolutionAction != CourtImportResolutionAction.Skip && x.ParsedNdoh != null &&
            ((x.ParsedNdoh >= today && x.ParsedNdoh <= end) ||
             (x.ParsedNdoh < today && x.SuggestedStatusClass == CourtImportStatusClass.Pending)));
    }

    // Upcoming ascending, then nearest overdue descending; source row and ID break ties.
    public static IOrderedQueryable<CourtImportRow> Order(IQueryable<CourtImportRow> rows, DateOnly today) => rows
        .OrderBy(x => x.ParsedNdoh < today ? 1 : 0)
        .ThenBy(x => x.ParsedNdoh >= today ? x.ParsedNdoh : null)
        .ThenByDescending(x => x.ParsedNdoh < today ? x.ParsedNdoh : null)
        .ThenBy(x => x.SourceRowNumber).ThenBy(x => x.Id);
}
