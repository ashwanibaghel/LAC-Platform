namespace LAC.Domain;

public enum CourtExternalSyncRunStatus { Running, Completed, Failed }
public enum CourtExternalSyncMode { LiveWindow, HistoricalBackfill }
public enum CourtExternalSourceKind { OrdinaryListing, DeletionOrCorrigendum, Unsupported }
public enum CourtExternalSourceStatus { Discovered, Processed, NeedsReview }
public enum CourtExternalListingStatus { Observed, Accepted, NeedsReview, Rejected, Superseded }

public sealed class CourtExternalSyncRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProviderCode { get; set; } = "DELHI_HIGH_COURT_CAUSE_LIST";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public CourtExternalSyncRunStatus Status { get; set; } = CourtExternalSyncRunStatus.Running;
    public CourtExternalSyncMode Mode { get; set; } = CourtExternalSyncMode.LiveWindow;
    public DateOnly? WindowStart { get; set; }
    public DateOnly? WindowEnd { get; set; }
    public int EligibleCaseCount { get; set; }
    public int ArchivePagesDiscovered { get; set; }
    public int TargetCaseMatches { get; set; }
    public int CasesAdvanced { get; set; }
    public int SourceDocumentsDiscovered { get; set; }
    public int SourceDocumentsProcessed { get; set; }
    public int ObservationsCreated { get; set; }
    public int ObservationsAccepted { get; set; }
    public int ReviewCount { get; set; }
    public string? FailureMessage { get; set; }
}

// Publication identity and immutable downloaded evidence are kept separately from
// a case observation. Unknown publications can be logged without being applied.
public sealed class CourtExternalSourceDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProviderCode { get; set; } = "DELHI_HIGH_COURT_CAUSE_LIST";
    public string SourceUrl { get; set; } = "";
    public string SourceTitle { get; set; } = "";
    public DateOnly? ListingDate { get; set; }
    public CourtExternalSourceKind Kind { get; set; }
    public CourtExternalSourceStatus Status { get; set; } = CourtExternalSourceStatus.Discovered;
    public DateTimeOffset DiscoveredAt { get; set; }
    public DateTimeOffset? DownloadedAt { get; set; }
    public Guid? DocumentId { get; set; }
    public Document? Document { get; set; }
    public string? Sha256Hash { get; set; }
    public string? LiveTargetSetFingerprint { get; set; }
    public string? FailureMessage { get; set; }
}

// A listing is evidence of a future appearance, never a CourtProceeding.
public sealed class CourtExternalListingObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceDocumentId { get; set; }
    public CourtExternalSourceDocument SourceDocument { get; set; } = null!;
    public Guid? CourtCaseId { get; set; }
    public CourtCase? CourtCase { get; set; }
    public string ProviderCode { get; set; } = "DELHI_HIGH_COURT_CAUSE_LIST";
    public DateOnly ListingDate { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public int SourcePageNumber { get; set; }
    public string RawMatchedText { get; set; } = "";
    public string NormalizedCaseIdentity { get; set; } = "";
    public CourtExternalListingStatus Status { get; set; } = CourtExternalListingStatus.Observed;
    public CourtExternalSyncMode Mode { get; set; } = CourtExternalSyncMode.LiveWindow;
    public string? ConflictReason { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
}

// Decisions are append-only even when the observation's current status changes.
public sealed class CourtExternalListingDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ObservationId { get; set; }
    public CourtExternalListingObservation Observation { get; set; } = null!;
    public CourtExternalListingStatus FromStatus { get; set; }
    public CourtExternalListingStatus ToStatus { get; set; }
    public Guid? ActorUserId { get; set; }
    public AppUser? ActorUser { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    public string Reason { get; set; } = "";
}
