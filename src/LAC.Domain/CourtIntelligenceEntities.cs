namespace LAC.Domain;

// Intelligence sidecars reference the existing register; they never create proceedings or land records.
public sealed class CourtOrderIntelligence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourtCaseId { get; set; }
    public CourtCase CourtCase { get; set; } = null!;
    public DateOnly OrderDate { get; set; }
    public string OfficialUrl { get; set; } = "";
    public string SourceKind { get; set; } = "Order";
    public Guid? CorrectsOrderId { get; set; }
    public CourtOrderIntelligence? CorrectsOrder { get; set; }
    public ICollection<CourtOrderIntelligenceRevision> Revisions { get; set; } = [];
}

public sealed class CourtOrderIntelligenceRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourtOrderIntelligenceId { get; set; }
    public CourtOrderIntelligence Order { get; set; } = null!;
    public Guid SourceObservationId { get; set; }
    public CourtExternalOrderObservation SourceObservation { get; set; } = null!;
    public string PdfSha256 { get; set; } = "";
    public string PayloadSha256 { get; set; } = "";
    public string Contract { get; set; } = "court-lac-order-scope/v1";
    public string StructuredFactsJson { get; set; } = "{}";
    public string ExtractionState { get; set; } = "NeedsReview";
    public bool LacRelevant { get; set; }
    public string LacRelevanceState { get; set; } = "NeedsReview";
    public string LacAuthorityScope { get; set; } = "UnknownLAC";
    public bool LacActionable { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<CourtOrderRecordLink> Links { get; set; } = [];
}

public enum CourtRecordMatchState { Candidate, Ambiguous, Conflict, NotMatched, NeedsReview, Confirmed }

public sealed class CourtOrderRecordLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RevisionId { get; set; }
    public CourtOrderIntelligenceRevision Revision { get; set; } = null!;
    public string ExtractedEntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? VillageId { get; set; }
    public Village? Village { get; set; }
    public Guid? AwardId { get; set; }
    public Award? Award { get; set; }
    public Guid? KhasraId { get; set; }
    public Khasra? Khasra { get; set; }
    public CourtRecordMatchState MatchState { get; set; } = CourtRecordMatchState.Candidate;
    public string Origin { get; set; } = "CourtOrderExtraction";
    public string MatchReason { get; set; } = "";
    public int Version { get; set; } = 1;
    public Guid? ReviewedByUserId { get; set; }
    public AppUser? ReviewedByUser { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewReason { get; set; }
}
