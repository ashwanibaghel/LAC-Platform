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
    Cancelled = 3
}

public enum DakMovementAction
{
    Registered = 0,
    Marked = 1,
    Forwarded = 2,
    Returned = 3,
    Disposed = 4,
    Cancelled = 5
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

    public DakAssignment? CurrentAssignment { get; set; }
    public ICollection<DakMovement> Movements { get; set; } = new List<DakMovement>();
    public ICollection<DakAttachment> Attachments { get; set; } = new List<DakAttachment>();
    public ICollection<DakVillageLink> VillageLinks { get; set; } = new List<DakVillageLink>();
    public ICollection<DakAwardLink> AwardLinks { get; set; } = new List<DakAwardLink>();
    public ICollection<DakMatterLink> MatterLinks { get; set; } = new List<DakMatterLink>();
    public ICollection<DakKhasraLink> KhasraLinks { get; set; } = new List<DakKhasraLink>();
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
