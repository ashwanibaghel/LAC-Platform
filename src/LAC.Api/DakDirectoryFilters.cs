namespace LAC.Api;

using LAC.Domain;

// Apply only to an already authorized query, before count and pagination.
public sealed record DakDirectoryFilters(DateOnly? ReceivedFrom = null, DateOnly? ReceivedTo = null,
    string? InwardMode = null, Guid? CategoryId = null, Guid? WorkstreamId = null,
    Guid? HandlerId = null, string? Sender = null, bool? HasDocument = null)
{
    public IQueryable<Dak> Apply(IQueryable<Dak> query)
    {
        if (ReceivedFrom.HasValue) query = query.Where(d => d.ReceivedDate >= ReceivedFrom.Value);
        if (ReceivedTo.HasValue) query = query.Where(d => d.ReceivedDate <= ReceivedTo.Value);
        if (!string.IsNullOrWhiteSpace(InwardMode))
        {
            var mode = InwardMode.Trim().ToLowerInvariant();
            query = query.Where(d => d.InwardMode.ToLower() == mode);
        }
        if (CategoryId.HasValue) query = query.Where(d => d.CategoryId == CategoryId);
        if (WorkstreamId.HasValue) query = query.Where(d => d.WorkstreamId == WorkstreamId);
        if (HandlerId.HasValue) query = query.Where(d => d.CurrentAssignment != null && d.CurrentAssignment.IsActive && d.CurrentAssignment.AssignedUserId == HandlerId);
        if (!string.IsNullOrWhiteSpace(Sender))
        {
            var term = Sender.Trim().ToLowerInvariant();
            query = query.Where(d => d.SenderName.ToLower().Contains(term) || d.SenderDepartment != null && d.SenderDepartment.ToLower().Contains(term));
        }
        if (HasDocument.HasValue)
            query = query.Where(d => (d.MainDocumentId != null || d.Attachments.Any(a => a.RecordStatus == RecordStatus.Active)) == HasDocument.Value);
        return query;
    }
}
