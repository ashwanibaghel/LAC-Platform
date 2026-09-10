namespace LAC.Domain;

// Permanent verified fact snapshots. No ingestion-session FK: evidence outlives staging.
public sealed class SourceEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public int PageNumber { get; set; }
    public int? PageEnd { get; set; }
    public string? SourceRegionJson { get; set; }
    public string? ExtractedSnippet { get; set; }
    public string FactName { get; set; } = "";
    public string ConfirmedValueJson { get; set; } = "null";
    public DateTimeOffset VerifiedAt { get; set; }
    public string VerifiedBy { get; set; } = "";
    public Guid? AwardId { get; set; }
    public Award? Award { get; set; }
    public Guid? AwardKhasraId { get; set; }
    public AwardKhasra? AwardKhasra { get; set; }
    public Guid? NotificationId { get; set; }
    public Notification? Notification { get; set; }
    public Guid? PossessionEventId { get; set; }
    public PossessionEvent? PossessionEvent { get; set; }
    public Guid? CourtCaseId { get; set; }
    public CourtCase? CourtCase { get; set; }
    public Guid? ClaimId { get; set; }
    public Claim? Claim { get; set; }
    public Guid? AwardAreaIssueId { get; set; }
    public AwardAreaIssue? AwardAreaIssue { get; set; }
    public Guid? AwardValuationRuleId { get; set; }
    public AwardValuationRule? AwardValuationRule { get; set; }
    public Guid? AwardCompensationRuleId { get; set; }
    public AwardCompensationRule? AwardCompensationRule { get; set; }
    public Guid? AwardLandClassId { get; set; }
    public AwardLandClass? AwardLandClass { get; set; }
    public Guid? AwardSupplementaryMatterId { get; set; }
    public AwardSupplementaryMatter? AwardSupplementaryMatter { get; set; }
    public Guid? NmRecordedPersonId { get; set; }
    public NmRecordedPerson? NmRecordedPerson { get; set; }
    public Guid? NmEntitlementId { get; set; }
    public NmEntitlement? NmEntitlement { get; set; }
    public Guid? NmEntitlementKhasraId { get; set; }
    public NmEntitlementKhasra? NmEntitlementKhasra { get; set; }
}

public sealed partial class AwardIngestionCandidate
{
    public bool SafeToConfirm { get; set; }
    public int? SourcePage { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public string? VerifiedPayloadJson { get; set; }
    // A document row can be reviewed one source cell at a time.  This is
    // staging state only; it is deliberately separate from canonical facts.
    public string? FieldReviewJson { get; set; }
}

// Local-only human labels for future evaluation/adaptation.  The crop is
// reproducible from Document + page + region and is never stored as bytes.
public sealed class DocumentTrainingExample
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public int PageNumber { get; set; }
    public string SourceRegionJson { get; set; } = "{}";
    public string CellRole { get; set; } = "";
    public string? RawOcr { get; set; }
    public string? NormalizedSuggestion { get; set; }
    public string HumanFinalValue { get; set; } = "";
    public bool WasCorrected { get; set; }
    public string ReviewDecision { get; set; } = "Confirm";
    public DateTimeOffset VerifiedAt { get; set; }
    public string VerifiedBy { get; set; } = "";
    public Guid? SourceCandidateId { get; set; }
    public AwardIngestionCandidate? SourceCandidate { get; set; }
    public int VerificationRevision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
