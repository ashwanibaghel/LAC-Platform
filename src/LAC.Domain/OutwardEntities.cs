namespace LAC.Domain;

public enum OutwardStatus
{
    Registered = 0,
    Dispatched = 1,
    Cancelled = 2
}

public enum OutwardEventAction
{
    Registered = 0,
    MetadataUpdated = 1,
    MainDocumentChanged = 2,
    AttachmentAdded = 3,
    AttachmentRemoved = 4,
    DakLinkAdded = 5,
    DakLinkRemoved = 6,
    Dispatched = 7,
    Cancelled = 8
}

public sealed class Outward : OfficialRecord
{
    public string OutwardNumber { get; set; } = "";
    public string NormalizedOutwardNumber { get; set; } = "";
    public DateOnly OutwardDate { get; set; }

    public string Subject { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string? RecipientDesignation { get; set; }
    public string? RecipientDepartment { get; set; }
    public string? RecipientAddress { get; set; }
    public string? RecipientEmail { get; set; }
    public string? RecipientPhone { get; set; }

    public Guid IssuingDeskId { get; set; }
    public OfficeDesk IssuingDesk { get; set; } = null!;

    public Guid? WorkstreamId { get; set; }
    public Workstream? Workstream { get; set; }

    public string? OfficeReferenceNumber { get; set; }
    public string? Remarks { get; set; }

    public OutwardStatus Status { get; set; } = OutwardStatus.Registered;
    public int Revision { get; set; }

    public Guid? MainDocumentId { get; set; }
    public Document? MainDocument { get; set; }

    public DateOnly? DispatchDate { get; set; }
    public string? DispatchMode { get; set; }
    public string? DispatchReferenceNumber { get; set; }
    public Guid? DispatchedByUserId { get; set; }
    public AppUser? DispatchedByUser { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }

    public string? CancellationReason { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public AppUser? CancelledByUser { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? MatterId { get; set; }
    public Matter? Matter { get; set; }

    public ICollection<OutwardEvent> Events { get; set; } = new List<OutwardEvent>();
    public ICollection<OutwardAttachment> Attachments { get; set; } = new List<OutwardAttachment>();
    public ICollection<OutwardDakLink> DakLinks { get; set; } = new List<OutwardDakLink>();
}

public sealed class OutwardEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OutwardId { get; set; }
    public Outward Outward { get; set; } = null!;

    public int SequenceNumber { get; set; }
    public OutwardEventAction Action { get; set; }

    public Guid ActionByUserId { get; set; }
    public AppUser ActionByUser { get; set; } = null!;
    public string ActionByDisplayNameSnapshot { get; set; } = "";
    public DateTimeOffset ActionAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? DocumentId { get; set; }
    public Guid? AttachmentId { get; set; }
    public Guid? DakId { get; set; }
    public DateOnly? DispatchDate { get; set; }
    public string? DispatchMode { get; set; }
    public string? DispatchReferenceNumber { get; set; }
    public string? CancellationReason { get; set; }
}

public sealed class OutwardAttachment : OfficialRecord
{
    public Guid OutwardId { get; set; }
    public Outward Outward { get; set; } = null!;

    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public string Title { get; set; } = "";
    public string AttachmentType { get; set; } = "Enclosure";
    public int SequenceOrder { get; set; } = 1;
}

public sealed class OutwardDakLink : OfficialRecord
{
    public Guid OutwardId { get; set; }
    public Outward Outward { get; set; } = null!;

    public Guid DakId { get; set; }
    public Dak Dak { get; set; } = null!;

    public bool IsPrimary { get; set; } = false;
    public string RelationshipType { get; set; } = "RelatedPetition";
}
