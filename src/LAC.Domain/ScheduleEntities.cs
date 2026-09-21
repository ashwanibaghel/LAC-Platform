namespace LAC.Domain;

public enum ScheduledEventKind
{
    CourtHearing = 0,
    Deadline = 1,
    FollowUp = 2,
    Compliance = 3,
    Review = 4,
    Meeting = 5,
    Other = 6,
    ComplianceDeadline = 7,
    SiteInspection = 8,
    OrderDelivery = 9,
    CompensationDisbursement = 10,
    ReportSubmission = 11,
    NoticeExpiry = 12,
    DakCompliance = 13
}

public enum ScheduledEventStatus
{
    Scheduled = 0,
    Completed = 1,
    Cancelled = 2
}

public enum ScheduledEventPriority
{
    Routine = 0,
    Urgent = 1,
    Immediate = 2
}

public enum ScheduledEventOrigin
{
    Manual = 0,
    CourtProceeding = 1
}

public enum ScheduledEventAction
{
    Created = 0,
    Rescheduled = 1,
    ResponsibilityChanged = 2,
    ReminderAdded = 3,
    ReminderRemoved = 4,
    WorkItemLinked = 5,
    WorkItemUnlinked = 6,
    Completed = 7,
    Cancelled = 8
}

public sealed class ScheduledEvent : OfficialRecord
{
    public Guid WorkstreamId { get; set; }
    public Workstream Workstream { get; set; } = null!;

    public Guid? ResponsibleOfficeDeskId { get; set; }
    public OfficeDesk? ResponsibleOfficeDesk { get; set; }

    /// <summary>
    /// Optional named handler. Routing metadata / hint only; does NOT act as a private ACL.
    /// </summary>
    public Guid? AssignedUserId { get; set; }
    public AppUser? AssignedUser { get; set; }

    public ScheduledEventKind EventKind { get; set; } = ScheduledEventKind.Other;
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public DateOnly ScheduledDate { get; set; }
    public TimeOnly? ScheduledTime { get; set; }

    public ScheduledEventPriority Priority { get; set; } = ScheduledEventPriority.Routine;
    public ScheduledEventStatus Status { get; set; } = ScheduledEventStatus.Scheduled;
    public int Revision { get; set; } = 1;

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public string? CreatedByDisplayNameSnapshot { get; set; }
    public string? CreatedByDesignationSnapshot { get; set; }

    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    // Optional domain context references
    public Guid? MatterId { get; set; }
    public Matter? Matter { get; set; }

    public Guid? DakId { get; set; }
    public Dak? Dak { get; set; }

    public Guid? OutwardId { get; set; }
    public Outward? Outward { get; set; }

    public Guid? WorkItemId { get; set; }
    public WorkItem? WorkItem { get; set; }

    public Guid? CourtCaseId { get; set; }
    public CourtCase? CourtCase { get; set; }

    public Guid? CourtProceedingId { get; set; }
    public CourtProceeding? CourtProceeding { get; set; }

    public ScheduledEventOrigin Origin { get; set; } = ScheduledEventOrigin.Manual;

    public ICollection<ScheduledReminder> Reminders { get; set; } = new List<ScheduledReminder>();
    public ICollection<ScheduledEventEvent> Events { get; set; } = new List<ScheduledEventEvent>();
}

public sealed class ScheduledReminder : OfficialRecord
{
    public Guid ScheduledEventId { get; set; }
    public ScheduledEvent ScheduledEvent { get; set; } = null!;

    public int DaysBefore { get; set; }
    public TimeOnly? ReminderTime { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public string? CreatedByDisplayNameSnapshot { get; set; }
}

public sealed class ScheduledEventEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ScheduledEventId { get; set; }
    public ScheduledEvent ScheduledEvent { get; set; } = null!;

    public int SequenceNumber { get; set; }
    public ScheduledEventAction Action { get; set; }
    public DateTimeOffset ActionAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid ActorUserId { get; set; }
    public AppUser ActorUser { get; set; } = null!;
    public string ActorDisplayNameSnapshot { get; set; } = "";
    public string? ActorDesignationSnapshot { get; set; }

    public Guid? WorkstreamIdSnapshot { get; set; }
    public string? WorkstreamNameSnapshot { get; set; }

    public Guid? SourceDeskId { get; set; }
    public string? SourceDeskNameSnapshot { get; set; }

    public Guid? TargetDeskId { get; set; }
    public string? TargetDeskNameSnapshot { get; set; }

    public Guid? SourceUserId { get; set; }
    public string? SourceUserDisplayNameSnapshot { get; set; }

    public Guid? TargetUserId { get; set; }
    public string? TargetUserDisplayNameSnapshot { get; set; }

    public DateOnly? OldScheduledDate { get; set; }
    public TimeOnly? OldScheduledTime { get; set; }
    public DateOnly? NewScheduledDate { get; set; }
    public TimeOnly? NewScheduledTime { get; set; }

    public Guid? ReminderId { get; set; }
    public int? ReminderDaysBefore { get; set; }

    public Guid? LinkedWorkItemId { get; set; }

    public string? Reason { get; set; }
    public string? Notes { get; set; }
}
