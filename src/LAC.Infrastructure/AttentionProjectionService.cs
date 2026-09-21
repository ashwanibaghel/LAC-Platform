namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed record AttentionSummaryDto(
    int Overdue,
    int Today,
    int Tomorrow,
    int Next7Days,
    int ReminderActive,
    int NeedsRouting = 0
);

public sealed record AttentionItemDto(
    Guid Id,
    string SourceType, // "ScheduledEvent" | "WorkItemDue" | "DakDue"
    string Title,
    string DueState, // "overdue" | "today" | "tomorrow" | "upcoming" | "none"
    DateOnly? ScheduledDate,
    TimeOnly? ScheduledTime,
    DateTimeOffset? DueAt,
    Guid? WorkstreamId,
    string? WorkstreamCode,
    string? WorkstreamName,
    Guid? ResponsibleDeskId,
    string? ResponsibleDeskCode,
    string? ResponsibleDeskName,
    Guid? AssignedUserId,
    string? AssignedUserDisplayName,
    string Priority,
    string Status,
    string? EventKind,
    IReadOnlyList<string> Buckets,
    bool IsReminderActive,
    bool NeedsRouting,
    DateTimeOffset LastActivityAt,
    AttentionContextDto? Context
);

public sealed record AttentionContextDto(
    string EntityType,
    Guid EntityId,
    string? Title,
    string? ReferenceNumber,
    bool CanOpen,
    string? NavigationUrl
);

public sealed record AttentionFeedResult(
    AttentionSummaryDto Summary,
    IReadOnlyList<AttentionItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<FilterOptionDto>? WorkstreamOptions = null,
    IReadOnlyList<FilterOptionDto>? DeskOptions = null
);

public sealed record AttentionQuery(
    string? Bucket = null,
    string? SourceType = null,
    Guid? WorkstreamId = null,
    Guid? DeskId = null,
    string? Priority = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25
);

public sealed record CalendarQuery(
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? SourceType = null,
    string? EventKind = null,
    Guid? WorkstreamId = null,
    Guid? DeskId = null,
    string? Status = null,
    string? Priority = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 100
);

public sealed record ScheduledEventDetailDto(
    Guid Id,
    Guid WorkstreamId,
    string WorkstreamCode,
    string WorkstreamName,
    Guid? ResponsibleOfficeDeskId,
    string? ResponsibleOfficeDeskCode,
    string? ResponsibleOfficeDeskName,
    Guid? AssignedUserId,
    string? AssignedUserDisplayName,
    string EventKind,
    string Title,
    string? Description,
    DateOnly ScheduledDate,
    TimeOnly? ScheduledTime,
    string Priority,
    string Status,
    int Revision,
    Guid CreatedByUserId,
    string? CreatedByDisplayName,
    string? CreatedByDesignation,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    string Origin,
    IReadOnlyList<ScheduledReminderDto> Reminders,
    IReadOnlyList<ScheduledEventEventDto> History,
    AttentionContextDto? MatterContext,
    AttentionContextDto? DakContext,
    AttentionContextDto? OutwardContext,
    AttentionContextDto? WorkItemContext,
    AttentionContextDto? CourtCaseContext,
    AttentionContextDto? CourtProceedingContext,
    ScheduledEventCapabilitiesDto Capabilities
);

public sealed record ScheduledEventCapabilitiesDto(
    bool CanReschedule,
    bool CanReassign,
    bool CanUpdate,
    bool CanComplete,
    bool CanCancel,
    bool CanManageReminders,
    bool CanLinkWorkItem
);

public sealed record ScheduledReminderDto(
    Guid Id,
    int DaysBefore,
    TimeOnly? ReminderTime,
    bool IsActive,
    Guid CreatedByUserId,
    string? CreatedByDisplayName
);

public sealed record ScheduledEventEventDto(
    Guid Id,
    int SequenceNumber,
    string Action,
    DateTimeOffset ActionAt,
    Guid ActorUserId,
    string ActorDisplayName,
    string? ActorDesignation,
    string? WorkstreamName,
    string? SourceDeskName,
    string? TargetDeskName,
    string? SourceUserDisplayName,
    string? TargetUserDisplayName,
    DateOnly? OldScheduledDate,
    TimeOnly? OldScheduledTime,
    DateOnly? NewScheduledDate,
    TimeOnly? NewScheduledTime,
    int? ReminderDaysBefore,
    Guid? LinkedWorkItemId,
    string? Reason,
    string? Notes
);

public interface IAttentionProjectionService
{
    Task<AttentionFeedResult> GetMyAttentionAsync(
        Guid userId,
        AttentionQuery query,
        CancellationToken ct = default);

    Task<AttentionFeedResult> GetBranchAttentionAsync(
        Guid userId,
        AttentionQuery query,
        CancellationToken ct = default);

    Task<AttentionFeedResult> GetCalendarEventsAsync(
        Guid userId,
        CalendarQuery query,
        CancellationToken ct = default);

    Task<ScheduledEventDetailDto?> GetScheduledEventDetailAsync(
        Guid eventId,
        Guid userId,
        CancellationToken ct = default);
}

public sealed class AttentionProjectionService(
    LacDbContext db,
    IOfficeClock officeClock,
    IScheduleAuthorizationService scheduleAuth,
    IWorkItemAuthorizationService workItemAuth,
    IMatterAuthorizationService matterAuth,
    IDakAuthorizationService dakAuth,
    IOutwardAuthorizationService outwardAuth,
    ICourtAuthorizationService courtAuth) : IAttentionProjectionService
{
    private static readonly TimeZoneInfo DelhiZone = GetDelhiTimeZone();

    private static TimeZoneInfo GetDelhiTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }

    private DateOnly ToDelhiDate(DateTimeOffset utcDateTime)
    {
        var local = TimeZoneInfo.ConvertTime(utcDateTime, DelhiZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    // ========================================================================
    // 1. MY ATTENTION
    // ========================================================================
    public async Task<AttentionFeedResult> GetMyAttentionAsync(
        Guid userId,
        AttentionQuery query,
        CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);
        if (!isUserActive) throw new ScheduleWorkflowException("Inactive user account.", 403);

        var today = officeClock.GetCurrentDate();
        var nowUtc = officeClock.GetUtcNow();

        // Caller live desk memberships
        var activeDeskIds = await db.UserDeskMemberships.AsNoTracking()
            .Where(m => m.UserId == userId
                     && m.IsActive
                     && m.RemovedAt == null
                     && m.RecordStatus == RecordStatus.Active
                     && m.OfficeDesk.IsActive
                     && m.OfficeDesk.RecordStatus == RecordStatus.Active)
            .Select(m => m.OfficeDeskId)
            .ToListAsync(ct);

        // Caller live workstream memberships
        var activeWsIds = await db.UserWorkstreamMemberships.AsNoTracking()
            .Where(m => m.UserId == userId
                     && m.IsActive
                     && m.Workstream.IsActive
                     && m.Workstream.RecordStatus == RecordStatus.Active)
            .Select(m => m.WorkstreamId)
            .ToListAsync(ct);

        // Check Schedule.View permissions
        var scheduleViewScopes = await (from ur in db.UserRoles
                                        join r in db.Roles on ur.RoleId equals r.Id
                                        join rp in db.RolePermissions on r.Id equals rp.RoleId
                                        join p in db.Permissions on rp.PermissionId equals p.Id
                                        where ur.UserId == userId && r.IsActive && r.RecordStatus == RecordStatus.Active && p.Code == PermissionCodes.ScheduleView
                                        select rp.ScopeMode).Distinct().ToListAsync(ct);

        var hasScheduleAll = scheduleViewScopes.Contains(ScopeMode.All);
        var hasScheduleWs = scheduleViewScopes.Contains(ScopeMode.Workstream);
        var hasScheduleAssigned = scheduleViewScopes.Contains(ScopeMode.Assigned);
        var canViewSchedule = hasScheduleAll || hasScheduleWs || hasScheduleAssigned;

        // Check WorkItem.View permissions
        var workItemViewScopes = await (from ur in db.UserRoles
                                        join r in db.Roles on ur.RoleId equals r.Id
                                        join rp in db.RolePermissions on r.Id equals rp.RoleId
                                        join p in db.Permissions on rp.PermissionId equals p.Id
                                        where ur.UserId == userId && r.IsActive && r.RecordStatus == RecordStatus.Active && p.Code == PermissionCodes.WorkItemView
                                        select rp.ScopeMode).Distinct().ToListAsync(ct);

        // Check Dak.View permissions
        var dakViewScopes = await (from ur in db.UserRoles
                                   join r in db.Roles on ur.RoleId equals r.Id
                                   join rp in db.RolePermissions on r.Id equals rp.RoleId
                                   join p in db.Permissions on rp.PermissionId equals p.Id
                                   where ur.UserId == userId && r.IsActive && r.RecordStatus == RecordStatus.Active && p.Code == PermissionCodes.DakView
                                   select rp.ScopeMode).Distinct().ToListAsync(ct);

        // If caller has no Schedule.View, WorkItem.View, or Dak.View, fail closed with empty feed & 0 counts
        if (!canViewSchedule && workItemViewScopes.Count == 0 && dakViewScopes.Count == 0)
        {
            return new AttentionFeedResult(new AttentionSummaryDto(0, 0, 0, 0, 0, 0), [], 0, query.Page, query.PageSize);
        }

        var rawItems = new List<AttentionItemDto>();

        // 1. ScheduledEvents: Exact union-of-scopes under Schedule.View (ScopeMode.Own fails closed)
        if (canViewSchedule)
        {
            var eventQuery = db.ScheduledEvents.AsNoTracking()
                .Include(e => e.Workstream)
                .Include(e => e.ResponsibleOfficeDesk)
                .Include(e => e.AssignedUser)
                .Include(e => e.Reminders)
                .Where(e => e.RecordStatus == RecordStatus.Active
                         && e.Status != ScheduledEventStatus.Completed
                         && e.Status != ScheduledEventStatus.Cancelled);

            if (!hasScheduleAll)
            {
                if (hasScheduleWs && hasScheduleAssigned)
                {
                    eventQuery = eventQuery.Where(e => activeWsIds.Contains(e.WorkstreamId)
                        || (e.ResponsibleOfficeDeskId.HasValue && activeDeskIds.Contains(e.ResponsibleOfficeDeskId.Value)));
                }
                else if (hasScheduleWs)
                {
                    eventQuery = eventQuery.Where(e => activeWsIds.Contains(e.WorkstreamId));
                }
                else if (hasScheduleAssigned)
                {
                    eventQuery = eventQuery.Where(e => e.ResponsibleOfficeDeskId.HasValue && activeDeskIds.Contains(e.ResponsibleOfficeDeskId.Value));
                }
            }

            var canViewCourt = await courtAuth.CanViewCourtReferencesAsync(userId, ct);
            if (!canViewCourt)
            {
                eventQuery = eventQuery.Where(e => e.Origin != ScheduledEventOrigin.CourtProceeding
                                                && e.CourtCaseId == null
                                                && e.CourtProceedingId == null);
            }

            var events = await eventQuery.ToListAsync(ct);
            foreach (var e in events)
            {
                var buckets = ComputeBucketsForDate(e.ScheduledDate, today);
                var isReminderActive = e.Reminders.Any(r => r.CreatedByUserId == userId && r.IsActive && today >= e.ScheduledDate.AddDays(-r.DaysBefore) && today <= e.ScheduledDate);
                if (isReminderActive) buckets.Add("Reminder Active");

                var needsRouting = !e.ResponsibleOfficeDeskId.HasValue;
                if (needsRouting) buckets.Add("Needs Routing");

                var dueState = ComputeDueState(e.ScheduledDate, today);
                var context = await ResolveAttentionContextAsync(e, userId, ct);

                rawItems.Add(new AttentionItemDto(
                    e.Id,
                    "ScheduledEvent",
                    e.Title,
                    dueState,
                    e.ScheduledDate,
                    e.ScheduledTime,
                    null,
                    e.WorkstreamId,
                    e.Workstream.Code,
                    e.Workstream.Name,
                    e.ResponsibleOfficeDeskId,
                    e.ResponsibleOfficeDesk?.Code,
                    e.ResponsibleOfficeDesk?.Name,
                    e.AssignedUserId,
                    e.AssignedUser?.DisplayName,
                    e.Priority.ToString(),
                    e.Status.ToString(),
                    e.EventKind.ToString(),
                    buckets,
                    isReminderActive,
                    needsRouting,
                    e.LastActivityAt,
                    context
                ));
            }
        }

        // 2. WorkItemDue: Frozen My Work participation + exact WorkItem.View
        if (workItemViewScopes.Count > 0)
        {
            var reviewScopes = await (from ur in db.UserRoles
                                      join r in db.Roles on ur.RoleId equals r.Id
                                      join rp in db.RolePermissions on r.Id equals rp.RoleId
                                      join p in db.Permissions on rp.PermissionId equals p.Id
                                      where ur.UserId == userId && r.IsActive && r.RecordStatus == RecordStatus.Active && p.Code == PermissionCodes.WorkItemReview
                                      select rp.ScopeMode).Distinct().ToListAsync(ct);

            var hasAllReview = reviewScopes.Contains(ScopeMode.All);
            var hasWsReview = reviewScopes.Contains(ScopeMode.Workstream);
            var hasAssignedReview = reviewScopes.Contains(ScopeMode.Assigned);

            var workQuery = db.WorkItems.AsNoTracking()
                .Include(w => w.Workstream)
                .Include(w => w.Assignments).ThenInclude(a => a.OfficeDesk)
                .Include(w => w.Assignments).ThenInclude(a => a.AssignedUser)
                .Include(w => w.Contributors)
                .Where(w => w.RecordStatus == RecordStatus.Active
                         && w.Status != WorkItemStatus.Completed
                         && w.Status != WorkItemStatus.Cancelled
                         && w.DueAt.HasValue);

            // Operational participation
            workQuery = workQuery.Where(w =>
                (w.Assignments.Any(a => a.IsActive && a.RecordStatus == RecordStatus.Active && activeDeskIds.Contains(a.OfficeDeskId))) ||
                (w.RequestedByUserId == userId) ||
                (w.Contributors.Any(c => c.UserId == userId && c.IsActive && c.RecordStatus == RecordStatus.Active &&
                    (c.Status == WorkItemContributorStatus.Active || c.Status == WorkItemContributorStatus.Submitted || c.Status == WorkItemContributorStatus.Returned))) ||
                (w.Contributors.Any(c => c.IsActive && c.RecordStatus == RecordStatus.Active && c.Status == WorkItemContributorStatus.Submitted) &&
                    (hasAllReview || (hasWsReview && activeWsIds.Contains(w.WorkstreamId)) || (hasAssignedReview && w.Assignments.Any(a => a.IsActive && a.RecordStatus == RecordStatus.Active && activeDeskIds.Contains(a.OfficeDeskId)))))
            );

            // Intersect with WorkItem.View scopes
            if (!workItemViewScopes.Contains(ScopeMode.All))
            {
                var hasWs = workItemViewScopes.Contains(ScopeMode.Workstream);
                var hasAssigned = workItemViewScopes.Contains(ScopeMode.Assigned);

                workQuery = workQuery.Where(w =>
                    (hasWs && activeWsIds.Contains(w.WorkstreamId)) ||
                    (hasAssigned && (
                        w.Assignments.Any(a => a.IsActive && a.RecordStatus == RecordStatus.Active && activeDeskIds.Contains(a.OfficeDeskId)) ||
                        w.Contributors.Any(c => c.UserId == userId && c.IsActive && c.RecordStatus == RecordStatus.Active &&
                            (c.Status == WorkItemContributorStatus.Active || c.Status == WorkItemContributorStatus.Submitted || c.Status == WorkItemContributorStatus.Returned))
                    ))
                );
            }

            var workItems = await workQuery.ToListAsync(ct);
            foreach (var w in workItems)
            {
                var currentAssignment = w.Assignments.FirstOrDefault(a => a.IsActive && a.RecordStatus == RecordStatus.Active);
                var itemDelhiDate = ToDelhiDate(w.DueAt!.Value);
                var isOverdue = w.DueAt.Value < nowUtc;
                var buckets = ComputeBucketsForWorkItem(w.DueAt.Value, nowUtc, itemDelhiDate, today);

                var dueState = isOverdue ? "overdue" : itemDelhiDate == today ? "today" : itemDelhiDate == today.AddDays(1) ? "tomorrow" : "upcoming";
                var canOpen = await workItemAuth.CanAccessWorkItemAsync(w.Id, PermissionCodes.WorkItemView, userId, ct);

                rawItems.Add(new AttentionItemDto(
                    w.Id,
                    "WorkItemDue",
                    w.Title,
                    dueState,
                    itemDelhiDate,
                    null,
                    w.DueAt,
                    w.WorkstreamId,
                    w.Workstream.Code,
                    w.Workstream.Name,
                    currentAssignment?.OfficeDeskId,
                    currentAssignment?.OfficeDesk.Code,
                    currentAssignment?.OfficeDesk.Name,
                    currentAssignment?.AssignedUserId,
                    currentAssignment?.AssignedUser?.DisplayName,
                    w.Priority.ToString(),
                    w.Status.ToString(),
                    null,
                    buckets,
                    false,
                    currentAssignment is null,
                    w.UpdatedAt,
                    new AttentionContextDto("WorkItem", w.Id, w.Title, null, canOpen, canOpen ? $"/work/{w.Id}" : null)
                ));
            }
        }

        // 3. DakDue: Exact Dak.View and live membership in current responsible Dak desk
        if (dakViewScopes.Count > 0 && activeDeskIds.Count > 0)
        {
            var baseDakQuery = db.Daks.AsNoTracking()
                .Include(d => d.Workstream)
                .Include(d => d.CurrentAssignment).ThenInclude(a => a!.OfficeDesk)
                .Include(d => d.CurrentAssignment).ThenInclude(a => a!.AssignedUser)
                .Where(d => d.RecordStatus == RecordStatus.Active
                         && d.Status != DakStatus.Disposed
                         && d.Status != DakStatus.Cancelled
                         && d.DueDate.HasValue);

            var authDakResult = await dakAuth.AuthorizeListQueryAsync(baseDakQuery, PermissionCodes.DakView, userId, ct);
            if (authDakResult.HasPermission)
            {
                var dakQuery = authDakResult.Query.Where(d =>
                    d.CurrentAssignment != null
                    && d.CurrentAssignment.IsActive
                    && d.CurrentAssignment.RecordStatus == RecordStatus.Active
                    && activeDeskIds.Contains(d.CurrentAssignment.OfficeDeskId));

                var daks = await dakQuery.ToListAsync(ct);
                foreach (var d in daks)
                {
                    var buckets = ComputeBucketsForDate(d.DueDate!.Value, today);
                    var dueState = ComputeDueState(d.DueDate.Value, today);
                    var canOpen = await dakAuth.CanAccessDakAsync(d.Id, PermissionCodes.DakView, userId, ct);

                    rawItems.Add(new AttentionItemDto(
                        d.Id,
                        "DakDue",
                        d.Subject,
                        dueState,
                        d.DueDate.Value,
                        null,
                        null,
                        d.WorkstreamId,
                        d.Workstream?.Code,
                        d.Workstream?.Name,
                        d.CurrentAssignment?.OfficeDeskId,
                        d.CurrentAssignment?.OfficeDesk.Code,
                        d.CurrentAssignment?.OfficeDesk.Name,
                        d.CurrentAssignment?.AssignedUserId,
                        d.CurrentAssignment?.AssignedUser?.DisplayName,
                        d.Priority.ToString(),
                        d.Status.ToString(),
                        null,
                        buckets,
                        false,
                        false,
                        d.UpdatedAt,
                        new AttentionContextDto("Dak", d.Id, d.Subject, d.DiaryNumber, canOpen, canOpen ? $"/dak?selectedId={d.Id}" : null)
                    ));
                }
            }
        }

        return BuildFeedResult(rawItems, query);
    }

    // ========================================================================
    // 2. BRANCH ATTENTION
    // ========================================================================
    public async Task<AttentionFeedResult> GetBranchAttentionAsync(
        Guid userId,
        AttentionQuery query,
        CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);
        if (!isUserActive) throw new ScheduleWorkflowException("Inactive user account.", 403);

        var today = officeClock.GetCurrentDate();
        var nowUtc = officeClock.GetUtcNow();

        var activeDeskIds = await db.UserDeskMemberships.AsNoTracking()
            .Where(m => m.UserId == userId
                     && m.IsActive
                     && m.RemovedAt == null
                     && m.RecordStatus == RecordStatus.Active
                     && m.OfficeDesk.IsActive
                     && m.OfficeDesk.RecordStatus == RecordStatus.Active)
            .Select(m => m.OfficeDeskId)
            .ToListAsync(ct);

        var activeWsIds = await db.UserWorkstreamMemberships.AsNoTracking()
            .Where(m => m.UserId == userId
                     && m.IsActive
                     && m.Workstream.IsActive
                     && m.Workstream.RecordStatus == RecordStatus.Active)
            .Select(m => m.WorkstreamId)
            .ToListAsync(ct);

        // Schedule.View scopes
        var (hasAllSchedule, scheduleWsIds, scheduleDeskIds) = await scheduleAuth.GetAuthorizedScopesAsync(userId, PermissionCodes.ScheduleView, ct);

        // WorkItem.View scopes
        var workItemScopes = await (from ur in db.UserRoles
                                    join r in db.Roles on ur.RoleId equals r.Id
                                    join rp in db.RolePermissions on r.Id equals rp.RoleId
                                    join p in db.Permissions on rp.PermissionId equals p.Id
                                    where ur.UserId == userId && r.IsActive && r.RecordStatus == RecordStatus.Active && p.Code == PermissionCodes.WorkItemView
                                    select rp.ScopeMode).Distinct().ToListAsync(ct);

        // Dak.View scopes
        var dakScopes = await (from ur in db.UserRoles
                               join r in db.Roles on ur.RoleId equals r.Id
                               join rp in db.RolePermissions on r.Id equals rp.RoleId
                               join p in db.Permissions on rp.PermissionId equals p.Id
                               where ur.UserId == userId && r.IsActive && r.RecordStatus == RecordStatus.Active && p.Code == PermissionCodes.DakView
                               select rp.ScopeMode).Distinct().ToListAsync(ct);

        var rawItems = new List<AttentionItemDto>();

        // 1. ScheduledEvents under Schedule.View
        if (hasAllSchedule || scheduleWsIds.Count > 0 || scheduleDeskIds.Count > 0)
        {
            var eventQuery = db.ScheduledEvents.AsNoTracking()
                .Include(e => e.Workstream)
                .Include(e => e.ResponsibleOfficeDesk)
                .Include(e => e.AssignedUser)
                .Include(e => e.Reminders)
                .Where(e => e.RecordStatus == RecordStatus.Active && e.Status == ScheduledEventStatus.Scheduled);

            if (!hasAllSchedule)
            {
                eventQuery = eventQuery.Where(e =>
                    (scheduleWsIds.Contains(e.WorkstreamId)) ||
                    (e.ResponsibleOfficeDeskId.HasValue && scheduleDeskIds.Contains(e.ResponsibleOfficeDeskId.Value))
                );
            }

            var canViewCourt = await courtAuth.CanViewCourtReferencesAsync(userId, ct);
            if (!canViewCourt)
            {
                eventQuery = eventQuery.Where(e => e.Origin != ScheduledEventOrigin.CourtProceeding
                                                && e.CourtCaseId == null
                                                && e.CourtProceedingId == null);
            }

            var events = await eventQuery.ToListAsync(ct);
            foreach (var e in events)
            {
                var buckets = ComputeBucketsForDate(e.ScheduledDate, today);
                var isReminderActive = e.Reminders.Any(r => r.CreatedByUserId == userId && r.IsActive && today >= e.ScheduledDate.AddDays(-r.DaysBefore) && today <= e.ScheduledDate);
                if (isReminderActive) buckets.Add("Reminder Active");

                var needsRouting = !e.ResponsibleOfficeDeskId.HasValue;
                if (needsRouting) buckets.Add("Needs Routing");

                var dueState = ComputeDueState(e.ScheduledDate, today);
                var context = await ResolveAttentionContextAsync(e, userId, ct);

                rawItems.Add(new AttentionItemDto(
                    e.Id,
                    "ScheduledEvent",
                    e.Title,
                    dueState,
                    e.ScheduledDate,
                    e.ScheduledTime,
                    null,
                    e.WorkstreamId,
                    e.Workstream.Code,
                    e.Workstream.Name,
                    e.ResponsibleOfficeDeskId,
                    e.ResponsibleOfficeDesk?.Code,
                    e.ResponsibleOfficeDesk?.Name,
                    e.AssignedUserId,
                    e.AssignedUser?.DisplayName,
                    e.Priority.ToString(),
                    e.Status.ToString(),
                    e.EventKind.ToString(),
                    buckets,
                    isReminderActive,
                    needsRouting,
                    e.LastActivityAt,
                    context
                ));
            }
        }

        // 2. WorkItemDue under WorkItem.View
        if (workItemScopes.Count > 0)
        {
            var hasAllWork = workItemScopes.Contains(ScopeMode.All);
            var hasWsWork = workItemScopes.Contains(ScopeMode.Workstream);
            var hasAssignedWork = workItemScopes.Contains(ScopeMode.Assigned);

            var workQuery = db.WorkItems.AsNoTracking()
                .Include(w => w.Workstream)
                .Include(w => w.Assignments).ThenInclude(a => a.OfficeDesk)
                .Include(w => w.Assignments).ThenInclude(a => a.AssignedUser)
                .Where(w => w.RecordStatus == RecordStatus.Active
                         && w.Status != WorkItemStatus.Completed
                         && w.Status != WorkItemStatus.Cancelled
                         && w.DueAt.HasValue);

            if (!hasAllWork)
            {
                workQuery = workQuery.Where(w =>
                    (hasWsWork && activeWsIds.Contains(w.WorkstreamId)) ||
                    (hasAssignedWork && w.Assignments.Any(a => a.IsActive && a.RecordStatus == RecordStatus.Active && activeDeskIds.Contains(a.OfficeDeskId)))
                );
            }

            var workItems = await workQuery.ToListAsync(ct);
            foreach (var w in workItems)
            {
                var currentAssignment = w.Assignments.FirstOrDefault(a => a.IsActive && a.RecordStatus == RecordStatus.Active);
                var itemDelhiDate = ToDelhiDate(w.DueAt!.Value);
                var isOverdue = w.DueAt.Value < nowUtc;
                var buckets = ComputeBucketsForWorkItem(w.DueAt.Value, nowUtc, itemDelhiDate, today);

                var needsRouting = currentAssignment is null;
                if (needsRouting) buckets.Add("Needs Routing");

                var dueState = isOverdue ? "overdue" : itemDelhiDate == today ? "today" : itemDelhiDate == today.AddDays(1) ? "tomorrow" : "upcoming";
                var canOpen = await workItemAuth.CanAccessWorkItemAsync(w.Id, PermissionCodes.WorkItemView, userId, ct);

                rawItems.Add(new AttentionItemDto(
                    w.Id,
                    "WorkItemDue",
                    w.Title,
                    dueState,
                    itemDelhiDate,
                    null,
                    w.DueAt,
                    w.WorkstreamId,
                    w.Workstream.Code,
                    w.Workstream.Name,
                    currentAssignment?.OfficeDeskId,
                    currentAssignment?.OfficeDesk.Code,
                    currentAssignment?.OfficeDesk.Name,
                    currentAssignment?.AssignedUserId,
                    currentAssignment?.AssignedUser?.DisplayName,
                    w.Priority.ToString(),
                    w.Status.ToString(),
                    null,
                    buckets,
                    false,
                    needsRouting,
                    w.UpdatedAt,
                    new AttentionContextDto("WorkItem", w.Id, w.Title, null, canOpen, canOpen ? $"/work/{w.Id}" : null)
                ));
            }
        }

        // 3. DakDue under Dak.View
        if (dakScopes.Count > 0)
        {
            var hasAllDak = dakScopes.Contains(ScopeMode.All);
            var hasWsDak = dakScopes.Contains(ScopeMode.Workstream);
            var hasAssignedDak = dakScopes.Contains(ScopeMode.Assigned);

            var dakQuery = db.Daks.AsNoTracking()
                .Include(d => d.Workstream)
                .Include(d => d.CurrentAssignment).ThenInclude(a => a!.OfficeDesk)
                .Include(d => d.CurrentAssignment).ThenInclude(a => a!.AssignedUser)
                .Where(d => d.RecordStatus == RecordStatus.Active
                         && d.Status != DakStatus.Disposed
                         && d.Status != DakStatus.Cancelled
                         && d.DueDate.HasValue);

            if (!hasAllDak)
            {
                dakQuery = dakQuery.Where(d =>
                    (hasWsDak && d.WorkstreamId.HasValue && activeWsIds.Contains(d.WorkstreamId.Value)) ||
                    (hasAssignedDak && d.CurrentAssignment != null && d.CurrentAssignment.IsActive && activeDeskIds.Contains(d.CurrentAssignment.OfficeDeskId))
                );
            }

            var daks = await dakQuery.ToListAsync(ct);
            foreach (var d in daks)
            {
                var buckets = ComputeBucketsForDate(d.DueDate!.Value, today);
                var needsRouting = d.CurrentAssignment is null;
                if (needsRouting) buckets.Add("Needs Routing");

                var dueState = ComputeDueState(d.DueDate.Value, today);
                var canOpen = await dakAuth.CanAccessDakAsync(d.Id, PermissionCodes.DakView, userId, ct);

                rawItems.Add(new AttentionItemDto(
                    d.Id,
                    "DakDue",
                    d.Subject,
                    dueState,
                    d.DueDate.Value,
                    null,
                    null,
                    d.WorkstreamId,
                    d.Workstream?.Code,
                    d.Workstream?.Name,
                    d.CurrentAssignment?.OfficeDeskId,
                    d.CurrentAssignment?.OfficeDesk.Code,
                    d.CurrentAssignment?.OfficeDesk.Name,
                    d.CurrentAssignment?.AssignedUserId,
                    d.CurrentAssignment?.AssignedUser?.DisplayName,
                    d.Priority.ToString(),
                    d.Status.ToString(),
                    null,
                    buckets,
                    false,
                    needsRouting,
                    d.UpdatedAt,
                    new AttentionContextDto("Dak", d.Id, d.Subject, d.DiaryNumber, canOpen, canOpen ? $"/dak?selectedId={d.Id}" : null)
                ));
            }
        }

        // Bounded filter options
        var workstreamOptions = rawItems
            .Where(x => x.WorkstreamId.HasValue && !string.IsNullOrEmpty(x.WorkstreamName))
            .Select(x => new FilterOptionDto(x.WorkstreamId!.Value, x.WorkstreamName!))
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.Name)
            .ToList();

        var deskOptions = rawItems
            .Where(x => x.ResponsibleDeskId.HasValue && !string.IsNullOrEmpty(x.ResponsibleDeskName))
            .Select(x => new FilterOptionDto(x.ResponsibleDeskId!.Value, x.ResponsibleDeskName!))
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.Name)
            .ToList();

        return BuildFeedResult(rawItems, query, workstreamOptions, deskOptions);
    }

    // ========================================================================
    // 3. CALENDAR EVENTS
    // ========================================================================
    public async Task<AttentionFeedResult> GetCalendarEventsAsync(
        Guid userId,
        CalendarQuery query,
        CancellationToken ct = default)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);
        if (!isUserActive) throw new ScheduleWorkflowException("Inactive user account.", 403);

        var (hasAllSchedule, scheduleWsIds, scheduleDeskIds) = await scheduleAuth.GetAuthorizedScopesAsync(userId, PermissionCodes.ScheduleView, ct);

        if (!hasAllSchedule && scheduleWsIds.Count == 0 && scheduleDeskIds.Count == 0)
        {
            return new AttentionFeedResult(new AttentionSummaryDto(0, 0, 0, 0, 0), [], 0, query.Page, query.PageSize);
        }

        var eventQuery = db.ScheduledEvents.AsNoTracking()
            .Include(e => e.Workstream)
            .Include(e => e.ResponsibleOfficeDesk)
            .Include(e => e.AssignedUser)
            .Include(e => e.Reminders)
            .Where(e => e.RecordStatus == RecordStatus.Active);

        if (!hasAllSchedule)
        {
            eventQuery = eventQuery.Where(e =>
                (scheduleWsIds.Contains(e.WorkstreamId)) ||
                (e.ResponsibleOfficeDeskId.HasValue && scheduleDeskIds.Contains(e.ResponsibleOfficeDeskId.Value))
            );
        }

        var canViewCourt = await courtAuth.CanViewCourtReferencesAsync(userId, ct);
        if (!canViewCourt)
        {
            eventQuery = eventQuery.Where(e => e.Origin != ScheduledEventOrigin.CourtProceeding
                                            && e.CourtCaseId == null
                                            && e.CourtProceedingId == null);
        }

        // Bounded date range filters
        if (query.FromDate.HasValue) eventQuery = eventQuery.Where(e => e.ScheduledDate >= query.FromDate.Value);
        if (query.ToDate.HasValue) eventQuery = eventQuery.Where(e => e.ScheduledDate <= query.ToDate.Value);

        if (query.WorkstreamId.HasValue) eventQuery = eventQuery.Where(e => e.WorkstreamId == query.WorkstreamId.Value);
        if (query.DeskId.HasValue) eventQuery = eventQuery.Where(e => e.ResponsibleOfficeDeskId == query.DeskId.Value);

        if (!string.IsNullOrWhiteSpace(query.EventKind))
        {
            if (!Enum.TryParse<ScheduledEventKind>(query.EventKind, true, out var kind))
                throw new ScheduleWorkflowException($"Invalid eventKind: '{query.EventKind}'.", 400);
            eventQuery = eventQuery.Where(e => e.EventKind == kind);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<ScheduledEventStatus>(query.Status, true, out var status))
                throw new ScheduleWorkflowException($"Invalid status: '{query.Status}'.", 400);
            eventQuery = eventQuery.Where(e => e.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Priority))
        {
            if (!Enum.TryParse<ScheduledEventPriority>(query.Priority, true, out var priority))
                throw new ScheduleWorkflowException($"Invalid priority: '{query.Priority}'.", 400);
            eventQuery = eventQuery.Where(e => e.Priority == priority);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            eventQuery = eventQuery.Where(e => e.Title.ToLower().Contains(term) || (e.Description != null && e.Description.ToLower().Contains(term)));
        }

        var today = officeClock.GetCurrentDate();
        var totalCount = await eventQuery.CountAsync(ct);

        var summaryData = await eventQuery
            .Select(e => new {
                e.ScheduledDate,
                HasDesk = e.ResponsibleOfficeDeskId.HasValue,
                HasActiveReminder = e.Reminders.Any(r => r.CreatedByUserId == userId && r.IsActive && today >= e.ScheduledDate.AddDays(-r.DaysBefore) && today <= e.ScheduledDate)
            })
            .ToListAsync(ct);

        var summary = new AttentionSummaryDto(
            summaryData.Count(x => x.ScheduledDate < today),
            summaryData.Count(x => x.ScheduledDate == today),
            summaryData.Count(x => x.ScheduledDate == today.AddDays(1)),
            summaryData.Count(x => x.ScheduledDate >= today && x.ScheduledDate <= today.AddDays(7)),
            summaryData.Count(x => x.HasActiveReminder),
            summaryData.Count(x => !x.HasDesk)
        );

        var page = Math.Clamp(query.Page, 1, 100);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var events = await eventQuery
            .OrderBy(e => e.ScheduledDate)
            .ThenBy(e => e.ScheduledTime)
            .ThenByDescending(e => e.Priority)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = new List<AttentionItemDto>();

        foreach (var e in events)
        {
            var buckets = ComputeBucketsForDate(e.ScheduledDate, today);
            var isReminderActive = e.Reminders.Any(r => r.CreatedByUserId == userId && r.IsActive && today >= e.ScheduledDate.AddDays(-r.DaysBefore) && today <= e.ScheduledDate);
            if (isReminderActive) buckets.Add("Reminder Active");

            var dueState = ComputeDueState(e.ScheduledDate, today);
            var context = await ResolveAttentionContextAsync(e, userId, ct);

            items.Add(new AttentionItemDto(
                e.Id,
                "ScheduledEvent",
                e.Title,
                dueState,
                e.ScheduledDate,
                e.ScheduledTime,
                null,
                e.WorkstreamId,
                e.Workstream.Code,
                e.Workstream.Name,
                e.ResponsibleOfficeDeskId,
                e.ResponsibleOfficeDesk?.Code,
                e.ResponsibleOfficeDesk?.Name,
                e.AssignedUserId,
                e.AssignedUser?.DisplayName,
                e.Priority.ToString(),
                e.Status.ToString(),
                e.EventKind.ToString(),
                buckets,
                isReminderActive,
                !e.ResponsibleOfficeDeskId.HasValue,
                e.LastActivityAt,
                context
            ));
        }

        return new AttentionFeedResult(summary, items, totalCount, page, pageSize);
    }

    // ========================================================================
    // 4. SCHEDULED EVENT DETAIL
    // ========================================================================
    public async Task<ScheduledEventDetailDto?> GetScheduledEventDetailAsync(
        Guid eventId,
        Guid userId,
        CancellationToken ct = default)
    {
        var evt = await db.ScheduledEvents.AsNoTracking()
            .Include(e => e.Workstream)
            .Include(e => e.ResponsibleOfficeDesk)
            .Include(e => e.AssignedUser)
            .Include(e => e.Reminders.Where(r => r.IsActive))
            .Include(e => e.Events.OrderBy(h => h.SequenceNumber))
            .FirstOrDefaultAsync(e => e.Id == eventId && e.RecordStatus == RecordStatus.Active, ct);

        if (evt is null) return null;

        var canView = await scheduleAuth.CanAccessScheduledEventAsync(evt, PermissionCodes.ScheduleView, userId, ct);
        if (!canView) return null;

        var isCourtLinked = evt.Origin == ScheduledEventOrigin.CourtProceeding || evt.CourtCaseId.HasValue || evt.CourtProceedingId.HasValue;
        if (isCourtLinked && !await courtAuth.CanViewCourtReferencesAsync(userId, ct))
        {
            return null;
        }

        var isCourtProceeding = evt.Origin == ScheduledEventOrigin.CourtProceeding;
        var canUpdateScheduleAuth = await scheduleAuth.CanUpdateScheduledEventAsync(evt, userId, ct);
        var canReschedule = !isCourtProceeding && canUpdateScheduleAuth;
        var canReassign = await scheduleAuth.CanAccessScheduledEventAsync(evt, PermissionCodes.ScheduleAssign, userId, ct);
        var canUpdate = !isCourtProceeding && canUpdateScheduleAuth;
        var canComplete = await scheduleAuth.CanCompleteScheduledEventAsync(evt, userId, ct);
        var canCancel = !isCourtProceeding && await scheduleAuth.CanCancelScheduledEventAsync(evt, userId, ct);

        // Resolve context security
        AttentionContextDto? matterCtx = null;
        if (evt.MatterId.HasValue)
        {
            var matter = await db.Matters.AsNoTracking().FirstOrDefaultAsync(m => m.Id == evt.MatterId.Value, ct);
            var canViewMatter = matter != null && await matterAuth.CanAccessMatterAsync(evt.MatterId.Value, PermissionCodes.MatterView, userId, ct);
            matterCtx = new AttentionContextDto(
                "Matter",
                evt.MatterId.Value,
                canViewMatter ? matter?.Title : null,
                canViewMatter ? matter?.ReferenceNumber : null,
                canViewMatter,
                canViewMatter ? $"/matters/{evt.MatterId.Value}" : null
            );
        }

        AttentionContextDto? dakCtx = null;
        if (evt.DakId.HasValue)
        {
            var dak = await db.Daks.AsNoTracking().FirstOrDefaultAsync(d => d.Id == evt.DakId.Value, ct);
            var canViewDak = dak != null && await dakAuth.CanAccessDakAsync(evt.DakId.Value, PermissionCodes.DakView, userId, ct);
            dakCtx = new AttentionContextDto(
                "Dak",
                evt.DakId.Value,
                canViewDak ? dak?.Subject : null,
                canViewDak ? dak?.DiaryNumber : null,
                canViewDak,
                canViewDak ? $"/dak?selectedId={evt.DakId.Value}" : null
            );
        }

        AttentionContextDto? outwardCtx = null;
        if (evt.OutwardId.HasValue)
        {
            var outward = await db.Outwards.AsNoTracking().FirstOrDefaultAsync(o => o.Id == evt.OutwardId.Value, ct);
            var canViewOutward = outward != null && await outwardAuth.CanAccessOutwardAsync(evt.OutwardId.Value, PermissionCodes.OutwardView, userId, ct);
            outwardCtx = new AttentionContextDto(
                "Outward",
                evt.OutwardId.Value,
                canViewOutward ? outward?.Subject : null,
                canViewOutward ? outward?.OutwardNumber : null,
                canViewOutward,
                canViewOutward ? $"/outward?selectedId={evt.OutwardId.Value}" : null
            );
        }

        AttentionContextDto? workCtx = null;
        if (evt.WorkItemId.HasValue)
        {
            var work = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == evt.WorkItemId.Value, ct);
            var canViewWork = work != null && await workItemAuth.CanAccessWorkItemAsync(evt.WorkItemId.Value, PermissionCodes.WorkItemView, userId, ct);
            workCtx = new AttentionContextDto(
                "WorkItem",
                evt.WorkItemId.Value,
                canViewWork ? work?.Title : null,
                null,
                canViewWork,
                canViewWork ? $"/work/{evt.WorkItemId.Value}" : null
            );
        }

        AttentionContextDto? courtCaseCtx = null;
        if (evt.CourtCaseId.HasValue)
        {
            var canViewCourt = await courtAuth.CanViewCourtCaseAsync(evt.CourtCaseId.Value, userId, ct);
            var navUrl = canViewCourt ? await courtAuth.GetCourtCaseNavigationUrlAsync(evt.CourtCaseId.Value, userId, ct) : null;
            var ccase = canViewCourt ? await db.CourtCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == evt.CourtCaseId.Value, ct) : null;
            courtCaseCtx = new AttentionContextDto(
                "CourtCase",
                evt.CourtCaseId.Value,
                canViewCourt && ccase != null ? $"{ccase.CourtName} - {ccase.CaseNumber}" : null,
                canViewCourt ? ccase?.CaseNumber : null,
                canViewCourt && navUrl != null,
                navUrl
            );
        }

        AttentionContextDto? courtProcCtx = null;
        if (evt.CourtProceedingId.HasValue)
        {
            var canViewCourt = evt.CourtCaseId.HasValue && await courtAuth.CanViewCourtCaseAsync(evt.CourtCaseId.Value, userId, ct);
            var navUrl = canViewCourt && evt.CourtCaseId.HasValue ? await courtAuth.GetCourtCaseNavigationUrlAsync(evt.CourtCaseId.Value, userId, ct) : null;
            var proc = canViewCourt ? await db.CourtProceedings.Include(p => p.CourtCase).FirstOrDefaultAsync(p => p.Id == evt.CourtProceedingId.Value, ct) : null;
            courtProcCtx = new AttentionContextDto(
                "CourtProceeding",
                evt.CourtProceedingId.Value,
                canViewCourt && proc != null ? $"Proceeding: {proc.CourtCase.CaseNumber} ({proc.ProceedingDate:dd MMM yyyy})" : null,
                canViewCourt ? proc?.ProceedingDate?.ToString("yyyy-MM-dd") : null,
                canViewCourt && navUrl != null,
                navUrl
            );
        }

        var reminders = evt.Reminders
            .Where(r => r.CreatedByUserId == userId && r.IsActive)
            .Select(r => new ScheduledReminderDto(
                r.Id,
                r.DaysBefore,
                r.ReminderTime,
                r.IsActive,
                r.CreatedByUserId,
                r.CreatedByDisplayNameSnapshot
            )).ToList();

        var history = evt.Events
            .Where(h => (h.Action != ScheduledEventAction.ReminderAdded && h.Action != ScheduledEventAction.ReminderRemoved) || h.ActorUserId == userId)
            .Select(h => new ScheduledEventEventDto(
            h.Id,
            h.SequenceNumber,
            h.Action.ToString(),
            h.ActionAt,
            h.ActorUserId,
            h.ActorDisplayNameSnapshot,
            h.ActorDesignationSnapshot,
            h.WorkstreamNameSnapshot,
            h.SourceDeskNameSnapshot,
            h.TargetDeskNameSnapshot,
            h.SourceUserDisplayNameSnapshot,
            h.TargetUserDisplayNameSnapshot,
            h.OldScheduledDate,
            h.OldScheduledTime,
            h.NewScheduledDate,
            h.NewScheduledTime,
            h.ReminderDaysBefore,
            h.LinkedWorkItemId,
            h.Reason,
            h.Notes
        )).ToList();

        return new ScheduledEventDetailDto(
            evt.Id,
            evt.WorkstreamId,
            evt.Workstream.Code,
            evt.Workstream.Name,
            evt.ResponsibleOfficeDeskId,
            evt.ResponsibleOfficeDesk?.Code,
            evt.ResponsibleOfficeDesk?.Name,
            evt.AssignedUserId,
            evt.AssignedUser?.DisplayName,
            evt.EventKind.ToString(),
            evt.Title,
            evt.Description,
            evt.ScheduledDate,
            evt.ScheduledTime,
            evt.Priority.ToString(),
            evt.Status.ToString(),
            evt.Revision,
            evt.CreatedByUserId,
            evt.CreatedByDisplayNameSnapshot,
            evt.CreatedByDesignationSnapshot,
            evt.CreatedAt,
            evt.LastActivityAt,
            evt.CompletedAt,
            evt.CancelledAt,
            evt.CancellationReason,
            evt.Origin.ToString(),
            reminders,
            history,
            matterCtx,
            dakCtx,
            outwardCtx,
            workCtx,
            courtCaseCtx,
            courtProcCtx,
            new ScheduledEventCapabilitiesDto(
                canReschedule,
                canReassign,
                canUpdate,
                canComplete,
                canCancel,
                canUpdateScheduleAuth,
                canUpdateScheduleAuth
            )
        );
    }

    // ========================================================================
    // HELPER METHODS
    // ========================================================================
    private static List<string> ComputeBucketsForDate(DateOnly targetDate, DateOnly today)
    {
        var buckets = new List<string>();
        if (targetDate < today) buckets.Add("Overdue");
        if (targetDate == today) buckets.Add("Today");
        if (targetDate == today.AddDays(1)) buckets.Add("Tomorrow");
        if (targetDate >= today && targetDate <= today.AddDays(7)) buckets.Add("Next 7 Days");
        return buckets;
    }

    private static List<string> ComputeBucketsForWorkItem(DateTimeOffset dueAt, DateTimeOffset nowUtc, DateOnly itemDelhiDate, DateOnly today)
    {
        var buckets = new List<string>();
        if (dueAt < nowUtc) buckets.Add("Overdue");
        if (itemDelhiDate == today) buckets.Add("Today");
        if (itemDelhiDate == today.AddDays(1)) buckets.Add("Tomorrow");
        if (itemDelhiDate >= today && itemDelhiDate <= today.AddDays(7)) buckets.Add("Next 7 Days");
        return buckets;
    }

    private static string ComputeDueState(DateOnly targetDate, DateOnly today)
    {
        if (targetDate < today) return "overdue";
        if (targetDate == today) return "today";
        if (targetDate == today.AddDays(1)) return "tomorrow";
        return "upcoming";
    }

    private async Task<AttentionContextDto?> ResolveAttentionContextAsync(ScheduledEvent e, Guid userId, CancellationToken ct)
    {
        if (e.CourtCaseId.HasValue)
        {
            var canViewCourt = await courtAuth.CanViewCourtCaseAsync(e.CourtCaseId.Value, userId, ct);
            if (!canViewCourt) return null;

            var courtCase = await db.CourtCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == e.CourtCaseId.Value, ct);
            var navUrl = await courtAuth.GetCourtCaseNavigationUrlAsync(e.CourtCaseId.Value, userId, ct);
            return new AttentionContextDto("CourtCase", e.CourtCaseId.Value, courtCase?.CaseNumber, courtCase?.CourtName, navUrl != null, navUrl);
        }

        if (e.WorkItemId.HasValue)
        {
            var workItem = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == e.WorkItemId.Value, ct);
            var canViewWork = workItem != null && await workItemAuth.CanAccessWorkItemAsync(e.WorkItemId.Value, PermissionCodes.WorkItemView, userId, ct);
            return new AttentionContextDto("WorkItem", e.WorkItemId.Value, canViewWork ? workItem?.Title : null, null, canViewWork, canViewWork ? $"/work/{e.WorkItemId.Value}" : null);
        }

        if (e.MatterId.HasValue)
        {
            var matter = await db.Matters.AsNoTracking().FirstOrDefaultAsync(m => m.Id == e.MatterId.Value, ct);
            var canViewMatter = matter != null && await matterAuth.CanAccessMatterAsync(e.MatterId.Value, PermissionCodes.MatterView, userId, ct);
            return new AttentionContextDto("Matter", e.MatterId.Value, canViewMatter ? matter?.Title : null, canViewMatter ? matter?.ReferenceNumber : null, canViewMatter, canViewMatter ? $"/matters/{e.MatterId.Value}" : null);
        }

        if (e.DakId.HasValue)
        {
            var dak = await db.Daks.AsNoTracking().FirstOrDefaultAsync(d => d.Id == e.DakId.Value, ct);
            var canViewDak = dak != null && await dakAuth.CanAccessDakAsync(e.DakId.Value, PermissionCodes.DakView, userId, ct);
            return new AttentionContextDto("Dak", e.DakId.Value, canViewDak ? dak?.Subject : null, canViewDak ? dak?.DiaryNumber : null, canViewDak, canViewDak ? $"/dak?selectedId={e.DakId.Value}" : null);
        }

        if (e.OutwardId.HasValue)
        {
            var outward = await db.Outwards.AsNoTracking().FirstOrDefaultAsync(o => o.Id == e.OutwardId.Value, ct);
            var canViewOutward = outward != null && await outwardAuth.CanAccessOutwardAsync(e.OutwardId.Value, PermissionCodes.OutwardView, userId, ct);
            return new AttentionContextDto("Outward", e.OutwardId.Value, canViewOutward ? outward?.Subject : null, canViewOutward ? outward?.OutwardNumber : null, canViewOutward, canViewOutward ? $"/outward?selectedId={e.OutwardId.Value}" : null);
        }

        return null;
    }

    private static AttentionFeedResult BuildFeedResult(
        List<AttentionItemDto> rawItems,
        AttentionQuery query,
        IReadOnlyList<FilterOptionDto>? workstreams = null,
        IReadOnlyList<FilterOptionDto>? desks = null)
    {
        IEnumerable<AttentionItemDto> baseFiltered = rawItems;

        if (!string.IsNullOrWhiteSpace(query.SourceType))
        {
            var st = query.SourceType.Trim().ToLowerInvariant();
            baseFiltered = st switch
            {
                "schedule" or "event" or "scheduled-event" or "scheduledevent" => baseFiltered.Where(x => x.SourceType == "ScheduledEvent"),
                "work" or "workitem" or "work-item" => baseFiltered.Where(x => x.SourceType == "WorkItemDue"),
                "dak" => baseFiltered.Where(x => x.SourceType == "DakDue"),
                _ => baseFiltered
            };
        }

        if (query.WorkstreamId.HasValue)
        {
            baseFiltered = baseFiltered.Where(x => x.WorkstreamId == query.WorkstreamId.Value);
        }

        if (query.DeskId.HasValue)
        {
            baseFiltered = baseFiltered.Where(x => x.ResponsibleDeskId == query.DeskId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Priority))
        {
            if (!Enum.TryParse<ScheduledEventPriority>(query.Priority, true, out _))
                throw new ScheduleWorkflowException($"Invalid priority: '{query.Priority}'.", 400);

            baseFiltered = baseFiltered.Where(x => string.Equals(x.Priority, query.Priority, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            baseFiltered = baseFiltered.Where(x => x.Title.ToLowerInvariant().Contains(term));
        }

        var baseList = baseFiltered.ToList();

        var summary = new AttentionSummaryDto(
            baseList.Count(x => x.Buckets.Contains("Overdue")),
            baseList.Count(x => x.Buckets.Contains("Today")),
            baseList.Count(x => x.Buckets.Contains("Tomorrow")),
            baseList.Count(x => x.Buckets.Contains("Next 7 Days")),
            baseList.Count(x => x.IsReminderActive),
            baseList.Count(x => x.NeedsRouting)
        );

        IEnumerable<AttentionItemDto> bucketFiltered = baseList;

        if (!string.IsNullOrWhiteSpace(query.Bucket))
        {
            var b = query.Bucket.Trim().ToLowerInvariant();
            bucketFiltered = b switch
            {
                "overdue" => bucketFiltered.Where(x => x.Buckets.Contains("Overdue")),
                "today" => bucketFiltered.Where(x => x.Buckets.Contains("Today")),
                "tomorrow" => bucketFiltered.Where(x => x.Buckets.Contains("Tomorrow")),
                "next-7-days" or "next7days" or "next_7_days" or "this-week" => bucketFiltered.Where(x => x.Buckets.Contains("Next 7 Days")),
                "reminder-active" or "reminderactive" or "reminder" => bucketFiltered.Where(x => x.IsReminderActive),
                "needs-routing" or "needsrouting" or "unassigned" => bucketFiltered.Where(x => x.NeedsRouting),
                _ => bucketFiltered
            };
        }

        var list = bucketFiltered.ToList();
        var totalCount = list.Count;
        var page = Math.Clamp(query.Page, 1, 100);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var pagedItems = list
            .OrderBy(x => x.ScheduledDate ?? (x.DueAt.HasValue ? DateOnly.FromDateTime(x.DueAt.Value.DateTime) : DateOnly.MaxValue))
            .ThenBy(x => x.ScheduledTime ?? TimeOnly.MinValue)
            .ThenByDescending(x => x.Priority switch { "Immediate" => 3, "Urgent" => 2, _ => 1 })
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new AttentionFeedResult(summary, pagedItems, totalCount, page, pageSize, workstreams, desks);
    }
}
