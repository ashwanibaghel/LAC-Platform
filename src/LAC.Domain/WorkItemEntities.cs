namespace LAC.Domain;


public enum WorkItemPriority
{
    Routine = 0,
    Urgent = 1,
    Immediate = 2
}

public enum WorkItemStatus
{
    Assigned = 0,
    InProgress = 1,
    SubmittedForReview = 2,
    ReturnedForCorrection = 3,
    Completed = 4,
    Cancelled = 5
}

public enum WorkItemOrigin
{
    Manual = 0,
    SystemGenerated = 1
}

public enum WorkItemContributorStatus
{
    Active = 0,
    Submitted = 1,
    Returned = 2,
    Accepted = 3,
    Removed = 4
}

public enum WorkItemEventAction
{
    Created = 0,
    Assigned = 1,
    FirstSeen = 2,
    Started = 3,
    MetadataUpdated = 4,
    UpdateAdded = 5,
    AttachmentAdded = 6,
    AttachmentRemoved = 7,
    ContextLinked = 8,
    ContextUnlinked = 9,
    ContributorAdded = 10,
    ContributorSubmitted = 11,
    ContributorReturned = 12,
    ContributorAccepted = 13,
    ContributorRemoved = 14,
    SubmittedForReview = 15,
    ReturnedForCorrection = 16,
    Approved = 17,
    Reassigned = 18,
    Completed = 19,
    Cancelled = 20
}

public sealed class WorkItem : OfficialRecord
{
    public Guid WorkstreamId { get; set; }
    public Workstream Workstream { get; set; } = null!;

    public string Title { get; set; } = "";
    public string? Instructions { get; set; }

    public WorkItemPriority Priority { get; set; } = WorkItemPriority.Routine;
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Assigned;
    public WorkItemOrigin Origin { get; set; } = WorkItemOrigin.Manual;

    public DateTimeOffset? DueAt { get; set; }
    public int Revision { get; set; }

    public Guid RequestedByUserId { get; set; }
    public AppUser RequestedByUser { get; set; } = null!;
    public string RequestedByDisplayNameSnapshot { get; set; } = "";
    public string? RequestedByDesignationSnapshot { get; set; }

    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Official responsibility cycles. The current responsibility is the unique active row.</summary>
    public ICollection<WorkItemAssignment> Assignments { get; set; } = new List<WorkItemAssignment>();
    public ICollection<WorkItemContributor> Contributors { get; set; } = new List<WorkItemContributor>();
    public ICollection<WorkItemUpdate> Updates { get; set; } = new List<WorkItemUpdate>();
    public ICollection<WorkItemAttachment> Attachments { get; set; } = new List<WorkItemAttachment>();
    public ICollection<WorkItemEvent> Events { get; set; } = new List<WorkItemEvent>();
    public ICollection<WorkItemMatterLink> MatterLinks { get; set; } = new List<WorkItemMatterLink>();
    public ICollection<WorkItemDakLink> DakLinks { get; set; } = new List<WorkItemDakLink>();
}

public sealed class WorkItemAssignment : OfficialRecord
{
    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public Guid OfficeDeskId { get; set; }
    public OfficeDesk OfficeDesk { get; set; } = null!;

    public Guid? AssignedUserId { get; set; }
    public AppUser? AssignedUser { get; set; }

    public Guid AssignedByUserId { get; set; }
    public AppUser AssignedByUser { get; set; } = null!;
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? FirstSeenAt { get; set; }
    public Guid? FirstSeenByUserId { get; set; }
    public AppUser? FirstSeenByUser { get; set; }

    public DateTimeOffset? FirstActionAt { get; set; }
    public Guid? FirstActionByUserId { get; set; }
    public AppUser? FirstActionByUser { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ClosedAt { get; set; }
}

public sealed class WorkItemContributor : OfficialRecord
{
    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public Guid AddedByUserId { get; set; }
    public AppUser AddedByUser { get; set; } = null!;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? Instructions { get; set; }
    public WorkItemContributorStatus Status { get; set; } = WorkItemContributorStatus.Active;
    public bool IsActive { get; set; } = true;

    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public AppUser? ReviewedByUser { get; set; }
}

public sealed class WorkItemUpdate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public string Message { get; set; } = "";

    public Guid AddedByUserId { get; set; }
    public AppUser AddedByUser { get; set; } = null!;
    public string AddedByDisplayNameSnapshot { get; set; } = "";
    public string? AddedByDesignationSnapshot { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WorkItemAttachment : OfficialRecord
{
    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public string? Title { get; set; }
    public string? AttachmentType { get; set; }
    public Guid? WorkItemUpdateId { get; set; }
    public WorkItemUpdate? WorkItemUpdate { get; set; }
}

public sealed class WorkItemMatterLink : OfficialRecord
{
    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public Guid MatterId { get; set; }
    public Matter Matter { get; set; } = null!;
}

public sealed class WorkItemDakLink : OfficialRecord
{
    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public Guid DakId { get; set; }
    public Dak Dak { get; set; } = null!;
}

public sealed class WorkItemEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public int SequenceNumber { get; set; }
    public WorkItemEventAction Action { get; set; }

    public Guid ActionByUserId { get; set; }
    public AppUser ActionByUser { get; set; } = null!;
    public string ActionByDisplayNameSnapshot { get; set; } = "";
    public string? ActionByDesignationSnapshot { get; set; }
    public DateTimeOffset ActionAt { get; set; } = DateTimeOffset.UtcNow;

    public WorkItemStatus? FromStatus { get; set; }
    public WorkItemStatus? ToStatus { get; set; }

    public Guid? TargetUserId { get; set; }
    public Guid? TargetDeskId { get; set; }
    public Guid? SourceAssignmentId { get; set; }
    public Guid? TargetAssignmentId { get; set; }
    public Guid? SourceDeskId { get; set; }
    public Guid? SourceUserId { get; set; }
    public string? SourceDeskNameSnapshot { get; set; }
    public string? SourceUserDisplayNameSnapshot { get; set; }
    public string? TargetDeskNameSnapshot { get; set; }
    public string? TargetUserDisplayNameSnapshot { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? WorkItemUpdateId { get; set; }
    public Guid? ContributorId { get; set; }

    public string? ContextType { get; set; }
    public Guid? ContextEntityId { get; set; }

    public string? RemarksSnapshot { get; set; }
}
