using LAC.Domain;

namespace LAC.Infrastructure;

public sealed class StaffWorkflowException(string code, string message, int statusCode = 400, object? conflicts = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public object? Conflicts { get; } = conflicts;
}
public sealed record AwardMetadataInput(Guid MutationId, int ExpectedRevision, string AwardNumber, DateOnly? AwardDate,
    string? AwardType, string? ActRegime, string? Purpose, Guid? AcquisitionProjectId, string? Remarks, string Reason);
public sealed record AwardLifecycleInput(Guid MutationId, int ExpectedRevision, string Action, string Reason);
public sealed record AwardMutationResult(Guid AwardId, int Revision, RecordStatus RecordStatus, string? ArchiveKind);
public sealed record AwardIdentityConflict(Guid AwardId, string AwardNumber, DateOnly? AwardDate, string? ActRegime);
public sealed record CoreUploadInput(string Role, Guid? VillageId = null, DateOnly? LegalDate = null,
    string? ReferenceNumber = null, string? PartNumber = null, string? Title = null, string? Remarks = null,
    IReadOnlyList<Guid>? KhasraIds = null, string Mode = "Direct");
public sealed record CoreUploadResult(Guid DocumentId, string Role, Guid AttachmentId, Guid? VillageId,
    Guid? PossessionEventId, Guid? NmDocumentId, bool Duplicate, string? Warning, int Revision, Guid? JobId = null)
{
    public string AttachmentStatus => Duplicate ? "AlreadyAttached" : "Attached";
}
public sealed record AttachmentCorrectionInput(Guid MutationId, int ExpectedRevision, string Action, DateOnly? LegalDate,
    string? ReferenceNumber, string? PartNumber, string? Title, string? Remarks, string Reason, bool OfficerConfirmed = false);
public sealed record AttachmentCorrectionResult(Guid AttachmentId, int Revision, bool IsDetached);
public sealed record NotificationUploadInput(Guid MutationId, Guid VillageId, Guid? NotificationId,
    string? SectionType, string? NotificationNumber, string? NumberAbsentReason, DateOnly? NotificationDate, string? Remarks);
public sealed record NotificationUploadResult(Guid NotificationId, Guid DocumentId, bool Finalized, bool NeedsReview, int Revision, string? Warning);
public sealed record FinalizeNotificationInput(Guid MutationId, int ExpectedRevision, string Reason);
public sealed record NotificationMetadataInput(Guid MutationId, int ExpectedRevision, string SectionType, string? NotificationNumber,
    DateOnly? NotificationDate, string? NumberAbsentReason, string? Remarks, string Reason);
public sealed record TemporaryProceedingInput(Guid MutationId, string DocumentType, string ActRegime, string LegalSection,
    string Purpose, string? ReferenceNumber, int? DurationMonths, DateOnly? LegalDate, DateOnly? CommencementDate,
    DateOnly? ExpiryDate, string? CompensationReference, string? Remarks, Guid? AcquisitionProjectId, Guid? AwardId);
public sealed record TemporaryProceedingResult(Guid ProceedingId, Guid DocumentId, bool Duplicate);
public sealed record TimelineDocument(Guid DocumentId, string ContentPath);
public sealed record AcquisitionTimelineEvent(string EventId, string EventType, Guid RecordId, IReadOnlyList<Guid> AwardIds,
    DateOnly? LegalDate, string Title, string SourcePath, IReadOnlyList<TimelineDocument> Documents, bool NeedsReview, IReadOnlyList<string> Warnings);
public sealed record PdfOwnershipVillage(Guid VillageId, string Name);
public sealed record PdfOwnershipLink(Guid AwardId, string AwardNumber, int AwardRevision, Guid AttachmentId,
    int AttachmentRevision, Guid? VillageId, IReadOnlyList<PdfOwnershipVillage> Villages, bool CanCorrect, Guid DocumentId, string DocumentReference);
public sealed record PdfOwnershipConflict(Guid? DocumentId, string? DocumentReference, string? ContentPath, string Sha256Hash,
    Guid SelectedAwardId, IReadOnlyList<PdfOwnershipLink> ExistingLinks, bool HasRestrictedLinks);
public sealed record AwardPdfLinkResolutionInput(Guid MutationId, string Action, Guid SourceAwardId, Guid TargetAwardId,
    Guid VillageId, int ExpectedSourceAwardRevision, int ExpectedTargetAwardRevision, int ExpectedSourceAttachmentRevision,
    string ExpectedSha256Hash, bool OfficerConfirmed, string Reason, IReadOnlyList<int>? SourcePages, string? CoverageDescription);
public sealed record AwardPdfLinkResolutionResult(Guid DocumentId, Guid AwardId, Guid AttachmentId, int AttachmentRevision,
    string Resolution, string SourcePath);
