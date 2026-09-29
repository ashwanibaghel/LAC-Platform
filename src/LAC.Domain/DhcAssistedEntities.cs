namespace LAC.Domain;

public enum DhcAssistedRunStatus
{
    WaitingForCaptcha, Running, PausedForCaptcha, ReadyForOrders, Completed, Failed, Cancelled, Interrupted
}

public enum DhcAssistedPhase { StatusLookup, OrderLookup }

public enum DhcAssistedItemStatus
{
    Queued, CheckingStatus, StatusCaptured, CheckingOrders, Completed, NeedsReview, NotFound,
    CaptchaRequired, Failed, Skipped, Cancelled
}

public enum DhcAssistedEvidenceStatus { Accepted, NeedsReview, Rejected }

// Only workflow and evidence are durable. Official cookies, CSRF values, the
// visible challenge and the officer's answer belong exclusively to memory.
public sealed class DhcAssistedSyncRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StartedByUserId { get; set; }
    public AppUser StartedByUser { get; set; } = null!;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DhcAssistedRunStatus Status { get; set; } = DhcAssistedRunStatus.WaitingForCaptcha;
    public DhcAssistedPhase Phase { get; set; } = DhcAssistedPhase.StatusLookup;
    public int TotalCases { get; set; }
    public int CompletedCases { get; set; }
    public int UpdatedCases { get; set; }
    public int NoChangeCases { get; set; }
    public int NeedsReviewCases { get; set; }
    public int FailedCases { get; set; }
    public int CaptchaChallenges { get; set; }
    public string? FailureMessage { get; set; }
    public ICollection<DhcAssistedSyncItem> Items { get; set; } = [];
}

public sealed class DhcAssistedSyncItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public DhcAssistedSyncRun Run { get; set; } = null!;
    public Guid CourtCaseId { get; set; }
    public CourtCase CourtCase { get; set; } = null!;
    public string NormalizedCaseIdentity { get; set; } = "";
    public int QueueOrder { get; set; }
    public string Reason { get; set; } = "";
    public DhcAssistedItemStatus Status { get; set; } = DhcAssistedItemStatus.Queued;
    public int AttemptCount { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}

// Append-only official result fragments. This never mutates canonical status,
// proceedings, calendar or the imported raw last-order URL.
public sealed class CourtExternalCaseStatusObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourtCaseId { get; set; }
    public CourtCase CourtCase { get; set; } = null!;
    public Guid RunItemId { get; set; }
    public DhcAssistedSyncItem RunItem { get; set; } = null!;
    public string ProviderCode { get; set; } = "DELHI_HIGH_COURT_ASSISTED";
    public DateTimeOffset ObservedAt { get; set; }
    public string NormalizedCaseIdentity { get; set; } = "";
    public string RawCaseNumber { get; set; } = "";
    public string? RawDiaryNumber { get; set; }
    public string? RawStatus { get; set; }
    public string? RawParties { get; set; }
    public string? RawListingDate { get; set; }
    public DateOnly? ListingDate { get; set; }
    public string? RawCourtNumber { get; set; }
    public string SourceUrl { get; set; } = "";
    public string RawEvidenceText { get; set; } = "";
    public string EvidenceSha256 { get; set; } = "";
    public string ParserVersion { get; set; } = "dhc-assisted-2026-09-v1";
    public DhcAssistedEvidenceStatus Status { get; set; }
    public string? ReviewReason { get; set; }
}

public sealed class CourtExternalOrderObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourtCaseId { get; set; }
    public CourtCase CourtCase { get; set; } = null!;
    public Guid RunItemId { get; set; }
    public DhcAssistedSyncItem RunItem { get; set; } = null!;
    public DateTimeOffset ObservedAt { get; set; }
    public string NormalizedCaseIdentity { get; set; } = "";
    public string RawCaseNumber { get; set; } = "";
    public DateOnly? OrderDate { get; set; }
    public string? RawOrderDate { get; set; }
    public string? OfficialUrl { get; set; }
    public string? CorrigendumUrl { get; set; }
    public DateOnly? UploadDate { get; set; }
    public string? RawUploadDate { get; set; }
    public string? RawRemark { get; set; }
    public string SourceUrl { get; set; } = "";
    public string EvidenceSha256 { get; set; } = "";
    public string RawEvidenceText { get; set; } = "";
    public string ParserVersion { get; set; } = "dhc-assisted-2026-09-v1";
}

public sealed class CourtExternalAssistedDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ObservationId { get; set; }
    public CourtExternalCaseStatusObservation Observation { get; set; } = null!;
    public DhcAssistedEvidenceStatus FromStatus { get; set; }
    public DhcAssistedEvidenceStatus ToStatus { get; set; }
    public Guid ActorUserId { get; set; }
    public AppUser ActorUser { get; set; } = null!;
    public DateTimeOffset DecidedAt { get; set; }
    public string Reason { get; set; } = "";
}
