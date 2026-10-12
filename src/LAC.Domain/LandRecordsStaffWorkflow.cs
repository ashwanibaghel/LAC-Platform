namespace LAC.Domain;

public sealed partial class Award
{
    public int Revision { get; set; }
    public string? ArchiveKind { get; set; }
}

public sealed partial class Notification
{
    public int Revision { get; set; }
    public bool IsFinalized { get; set; }
    public string? NumberAbsentReason { get; set; }
}

public sealed partial class DocumentAward
{
    public Guid? VillageId { get; set; }
    public Village? Village { get; set; }
    public Guid? PossessionEventId { get; set; }
    public PossessionEvent? PossessionEvent { get; set; }
    public DateOnly? LegalDate { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? PartNumber { get; set; }
    public string? Title { get; set; }
    public string? Remarks { get; set; }
    public bool IsDetached { get; set; }
    public bool IsSharedSource { get; set; }
    public int Revision { get; set; }
}

// Evidence of temporary occupation is not a final Award. Dates are source facts,
// never calculated from duration; an actual Award can be explicitly referenced.
public sealed class TemporaryAcquisitionProceeding : OfficialRecord
{
    public Guid VillageId { get; set; }
    public Village Village { get; set; } = null!;
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public Guid? AcquisitionProjectId { get; set; }
    public AcquisitionProject? AcquisitionProject { get; set; }
    public Guid? AwardId { get; set; }
    public Award? Award { get; set; }
    public string Nature { get; set; } = "Temporary";
    public string DocumentType { get; set; } = "TemporaryProceedings";
    public string ActRegime { get; set; } = "";
    public string LegalSection { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string? ReferenceNumber { get; set; }
    public int? DurationMonths { get; set; }
    public DateOnly? LegalDate { get; set; }
    public DateOnly? CommencementDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? CompensationReference { get; set; }
    public string? Remarks { get; set; }
}

// Atomic response receipts are operational retry state, not a second record model.
public sealed class LandRecordMutationReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Scope { get; set; } = "";
    public Guid MutationId { get; set; }
    public string RequestHash { get; set; } = "";
    public string ResponseJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
