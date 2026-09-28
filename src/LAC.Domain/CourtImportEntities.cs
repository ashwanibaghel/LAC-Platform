namespace LAC.Domain;

public enum CourtImportBatchStatus { Parsing, Parsed, Failed }
public enum CourtImportRowStatus { NewCandidate, ExistingExact, PotentialDuplicate, IdentityConflict, NeedsReview, Invalid }
public enum CourtImportStatusClass { Pending, Disposed, Attention }
public enum CourtImportResolutionAction { ImportAsNewCase, LinkToExistingCase, Skip }
public enum CourtImportCommitStatus { NotCommitted, Committed, Failed }
public enum CourtImportNdohAction { UseImported, KeepExisting }

public sealed class CourtImportBatch : OfficialRecord
{
    public Guid SourceDocumentId { get; set; }
    public Document SourceDocument { get; set; } = null!;
    public string SourceSheetName { get; set; } = "Court case status pertains to L";
    public string SourceSha256 { get; set; } = "";
    public CourtImportBatchStatus Status { get; set; } = CourtImportBatchStatus.Parsing;
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int NeedsReviewRows { get; set; }
    public int ConflictRows { get; set; }
    public int InvalidRows { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string CreatedByDisplayNameSnapshot { get; set; } = "";
    public DateTimeOffset? ParsedAt { get; set; }
    public string? FailureMessage { get; set; }
    public string ParserVersion { get; set; } = "court-xlsx-v1";
    public ICollection<CourtImportRow> Rows { get; set; } = new List<CourtImportRow>();
}

public sealed class CourtImportRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }
    public CourtImportBatch Batch { get; set; } = null!;
    public int SourceRowNumber { get; set; }
    public string? SourceSerialNumberRaw { get; set; }
    public string RawRowJson { get; set; } = "{}";
    public string? RawNdoh { get; set; }
    public DateOnly? ParsedNdoh { get; set; }
    public string? RawStatus { get; set; }
    public CourtImportStatusClass? SuggestedStatusClass { get; set; }
    public string? RawAdvocate { get; set; }
    public string? RawCaseTitle { get; set; }
    public string? RawCaseNumber { get; set; }
    public string? SuggestedCaseType { get; set; }
    public string? SuggestedCaseNumber { get; set; }
    public int? SuggestedCaseYear { get; set; }
    public string? RawVillage { get; set; }
    public string? RawAwardNumber { get; set; }
    public string? RawDirections { get; set; }
    public string? RawCourt { get; set; }
    public string? SuggestedCourtName { get; set; }
    public string? RawLastOrderLink { get; set; }
    public string? LastOrderLinkState { get; set; }
    public string? RawBriefFacts { get; set; }
    public string ExtraCellsJson { get; set; } = "{}";
    public CourtImportRowStatus RowStatus { get; set; } = CourtImportRowStatus.NeedsReview;
    public string ValidationIssuesJson { get; set; } = "[]";
    public Guid? CandidateCourtCaseId { get; set; }
    public string? IdentityKey { get; set; }
    public string SourceRowHash { get; set; } = "";
    public CourtImportResolutionAction? ResolutionAction { get; set; }
    public Guid? ResolvedCourtCaseId { get; set; }
    public string? ApprovedCaseNumber { get; set; }
    public string? ApprovedCaseTitle { get; set; }
    public string? ApprovedCourtName { get; set; }
    public string? ApprovedStatus { get; set; }
    public bool ApplyStatusToExisting { get; set; }
    public CourtImportNdohAction? NdohAction { get; set; }
    public string? ReviewerNotes { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewedByDisplayNameSnapshot { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public CourtImportCommitStatus CommitStatus { get; set; } = CourtImportCommitStatus.NotCommitted;
    public Guid? CommittedCourtCaseId { get; set; }
    public Guid? CommittedProceedingId { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
    public string? CommitError { get; set; }
}
