namespace LAC.Domain;

public enum DakPriority
{
    Routine = 0,
    Urgent = 1,
    Immediate = 2
}

public enum DakStatus
{
    Registered = 0,
    InProcess = 1,
    Disposed = 2,
    Cancelled = 3,
    Resolved = 4
}

public enum DakMovementAction
{
    Registered = 0,
    Marked = 1,
    Forwarded = 2,
    Returned = 3,
    Disposed = 4,
    Cancelled = 5,
    Received = 6,
    PulledBack = 7,
    PhysicalReturnConfirmed = 8,
    Resolved = 9,
    Reopened = 10,
    CustodyConfirmed = 11
}

public sealed class DakCategory : OfficialRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DakPriority DefaultPriority { get; set; } = DakPriority.Routine;
    public Guid? DefaultWorkstreamId { get; set; }
    public Workstream? DefaultWorkstream { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Dak : OfficialRecord
{
    public string DiaryNumber { get; set; } = "";
    public string DiaryNumberKey { get; set; } = "";
    public Guid? RegistrationRequestId { get; set; }
    public string? RegistrationRequestHash { get; set; }
    public Guid? RegisteredByUserId { get; set; }
    public AppUser? RegisteredByUser { get; set; }

    // Unknown is distinct from no original. Paper custody is never inferred from routing.
    public bool? HasPhysicalOriginal { get; set; }
    public Guid? PhysicalOriginalDeskId { get; set; }
    public OfficeDesk? PhysicalOriginalDesk { get; set; }
    public Guid? PhysicalOriginalUserId { get; set; }
    public AppUser? PhysicalOriginalUser { get; set; }
    public string? PhysicalOriginalLocationNote { get; set; }
    public string? PhysicalOriginalProvenanceNote { get; set; }
    public DateTimeOffset? PhysicalOriginalUpdatedAt { get; set; }
    public Guid? PhysicalOriginalUpdatedByUserId { get; set; }
    public AppUser? PhysicalOriginalUpdatedByUser { get; set; }
    public DateOnly ReceivedDate { get; set; }

    public string Subject { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string? SenderDesignation { get; set; }
    public string? SenderDepartment { get; set; }
    public string? SenderAddress { get; set; }
    public string? SenderReferenceNumber { get; set; }
    public DateOnly? SenderLetterDate { get; set; }
    public string InwardMode { get; set; } = "Physical";

    public DakPriority Priority { get; set; } = DakPriority.Routine;
    public DateOnly? DueDate { get; set; }
    public Guid? CategoryId { get; set; }
    public DakCategory? Category { get; set; }
    public Guid? WorkstreamId { get; set; }
    public Workstream? Workstream { get; set; }

    public DakStatus Status { get; set; } = DakStatus.Registered;

    public Guid? MainDocumentId { get; set; }
    public Document? MainDocument { get; set; }

    public int Revision { get; set; }
    public DakRoutingState RoutingState { get; set; } = DakRoutingState.Unassigned;
    public DakPhysicalState PhysicalState { get; set; } = DakPhysicalState.Unknown;
    public int ProcessingCycle { get; set; } = 1;
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionRemarks { get; set; }
    public ICollection<DakTransfer> Transfers { get; set; } = new List<DakTransfer>();

    public DakAssignment? CurrentAssignment { get; set; }
    public ICollection<DakMovement> Movements { get; set; } = new List<DakMovement>();
    public ICollection<DakAttachment> Attachments { get; set; } = new List<DakAttachment>();
    public ICollection<DakVillageLink> VillageLinks { get; set; } = new List<DakVillageLink>();
    public ICollection<DakAwardLink> AwardLinks { get; set; } = new List<DakAwardLink>();
    public ICollection<DakMatterLink> MatterLinks { get; set; } = new List<DakMatterLink>();
    public ICollection<DakKhasraLink> KhasraLinks { get; set; } = new List<DakKhasraLink>();
}

public static class DakDiaryNumber
{
    // Preserve punctuation, internal whitespace, Unicode and historical display text.
    // This deliberately does not infer a register, year or government numbering format.
    public static string Normalize(string value) => string.Concat(
        value.Trim(' ', '\t', '\r', '\n').Select(c => c is >= 'a' and <= 'z' ? (char)(c - 32) : c));

    public const string SqlKey = "translate(btrim(\"DiaryNumber\", E' \\t\\r\\n'), 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ')";
}

public sealed class DakAssignment : OfficialRecord
{
    public Guid DakId { get; set; }
    public Dak Dak { get; set; } = null!;

    public Guid OfficeDeskId { get; set; }
    public OfficeDesk OfficeDesk { get; set; } = null!;

    public Guid? AssignedUserId { get; set; }
    public AppUser? AssignedUser { get; set; }

    public Guid AssignedByUserId { get; set; }
    public AppUser AssignedByUser { get; set; } = null!;

    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReceivedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ClosedAt { get; set; }
    public string? Instructions { get; set; }
}

public sealed class DakMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DakId { get; set; }
    public Dak Dak { get; set; } = null!;

    public int SequenceNumber { get; set; }
    public DakMovementAction Action { get; set; }

    public Guid? FromDeskId { get; set; }
    public OfficeDesk? FromDesk { get; set; }
    public Guid? FromUserId { get; set; }
    public AppUser? FromUser { get; set; }

    public Guid? ToDeskId { get; set; }
    public OfficeDesk? ToDesk { get; set; }
    public Guid? ToUserId { get; set; }
    public AppUser? ToUser { get; set; }

    public Guid ActionByUserId { get; set; }
    public AppUser ActionByUser { get; set; } = null!;
    public DateTimeOffset ActionAt { get; set; } = DateTimeOffset.UtcNow;

    public string? FromDeskCodeSnapshot { get; set; }
    public string? FromDeskNameSnapshot { get; set; }
    public string? FromUserDisplayNameSnapshot { get; set; }

    public string? ToDeskCodeSnapshot { get; set; }
    public string? ToDeskNameSnapshot { get; set; }
    public string? ToUserDisplayNameSnapshot { get; set; }

    public string ActionByDisplayNameSnapshot { get; set; } = "";

    public string? Remarks { get; set; }
    public Guid? TransferId { get; set; }
    public int EventVersion { get; set; }
    public string? StateSnapshotJson { get; set; }
    public string? InstructionsSnapshot { get; set; }
    public Guid? DocumentId { get; set; }
    public Document? Document { get; set; }
    public Guid? WorkstreamIdSnapshot { get; set; }
    public string? WorkstreamNameSnapshot { get; set; }
}

public sealed class DakAttachment : OfficialRecord
{
    public Guid DakId { get; set; }
    public Dak Dak { get; set; } = null!;

    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public string Title { get; set; } = "";
    public string AttachmentType { get; set; } = "Annexure";
    public int SequenceOrder { get; set; } = 1;
}

public sealed class DakVillageLink : OfficialRecord
{
    public Guid DakId { get; set; }
    public Dak Dak { get; set; } = null!;

    public Guid VillageId { get; set; }
    public Village Village { get; set; } = null!;
}

public sealed class DakAwardLink : OfficialRecord
{
    public Guid DakId { get; set; }
    public Dak Dak { get; set; } = null!;

    public Guid AwardId { get; set; }
    public Award Award { get; set; } = null!;
}

public sealed class DakMatterLink : OfficialRecord
{
    public Guid DakId { get; set; }
    public Dak Dak { get; set; } = null!;

    public Guid MatterId { get; set; }
    public Matter Matter { get; set; } = null!;
}

public sealed class DakKhasraLink : OfficialRecord
{
    public Guid DakId { get; set; }
    public Dak Dak { get; set; } = null!;

    public Guid KhasraId { get; set; }
    public Khasra Khasra { get; set; } = null!;
}
