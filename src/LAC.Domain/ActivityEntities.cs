namespace LAC.Domain;

public enum RecordAccessAction
{
    Opened = 1,
    Previewed = 2,
    Downloaded = 3
}

public sealed class RecordAccessEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ActorUserId { get; set; }
    public AppUser ActorUser { get; set; } = null!;
    public string ActorDisplayNameSnapshot { get; set; } = "";

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public RecordAccessAction Action { get; set; }

    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public string? ContextEntityType { get; set; }
    public Guid? ContextEntityId { get; set; }

    public Guid? WorkstreamId { get; set; }
    public Workstream? Workstream { get; set; }

    public Guid? OfficeDeskId { get; set; }
    public OfficeDesk? OfficeDesk { get; set; }

    public string? DocumentTitleSnapshot { get; set; }
    public string? WorkstreamNameSnapshot { get; set; }
    public string? OfficeDeskNameSnapshot { get; set; }

    /// <summary>
    /// Deduplication key for Open and Preview events within a 5-minute bucket:
    /// {ActorUserId}:{DocumentId}:{ContextEntityType}:{ContextEntityId}:{Bucket}.
    /// Null for Download events so downloads are never deduplicated.
    /// </summary>
    public string? DeduplicationKey { get; set; }
}
