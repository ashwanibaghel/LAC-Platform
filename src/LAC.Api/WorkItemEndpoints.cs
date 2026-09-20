namespace LAC.Api;

using System.IO;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

public sealed record CreateWorkItemApiRequest(
    string Title,
    string? Instructions,
    Guid WorkstreamId,
    Guid OfficeDeskId,
    Guid? AssignedUserId,
    string? Priority,
    DateTimeOffset? DueAt,
    Guid? MatterId = null,
    Guid? DakId = null
);

public sealed record StartWorkApiRequest(int? ExpectedRevision);
public sealed record AddUpdateApiRequest(string Message, int? ExpectedRevision);
public sealed record RemoveAttachmentApiRequest(int? ExpectedRevision);

public sealed record MyWorkSummaryDto(
    int TotalOpen,
    int AssignedToMe,
    int RequestedByMe,
    int Overdue,
    int DueToday,
    int DueThisWeek,
    int NeedsReview
);

public sealed record MyWorkItemDto(
    Guid Id,
    string Title,
    string Priority,
    string Status,
    DateTimeOffset? DueAt,
    string DueState, // "overdue" | "today" | "upcoming" | "none"
    Guid WorkstreamId,
    string WorkstreamName,
    Guid RequestedByUserId,
    string RequestedByDisplayName,
    string? RequestedByDesignation,
    Guid OfficeDeskId,
    string OfficeDeskName,
    Guid? AssignedUserId,
    string? AssignedUserDisplayName,
    string? AssignedUserDesignation,
    int Revision,
    DateTimeOffset LastActivityAt,
    DateTimeOffset CreatedAt,
    bool HasLinkedMatter,
    string? LinkedMatterTitle,
    bool HasLinkedDak,
    string? LinkedDakSubject
);

public sealed record MyWorkResponseDto(
    MyWorkSummaryDto Summary,
    IReadOnlyList<MyWorkItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public sealed record WorkItemDetailDto(
    Guid Id,
    string Title,
    string? Instructions,
    string Priority,
    string Status,
    string Origin,
    DateTimeOffset? DueAt,
    int Revision,
    Guid WorkstreamId,
    string WorkstreamName,
    Guid RequestedByUserId,
    string RequestedByDisplayName,
    string? RequestedByDesignation,
    DateTimeOffset LastActivityAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    WorkItemAssignmentDetailDto? CurrentAssignment,
    IReadOnlyList<WorkItemUpdateDetailDto> Updates,
    IReadOnlyList<WorkItemAttachmentDetailDto> Attachments,
    IReadOnlyList<WorkItemMatterLinkDetailDto> MatterLinks,
    IReadOnlyList<WorkItemDakLinkDetailDto> DakLinks
);

public sealed record WorkItemAssignmentDetailDto(
    Guid Id,
    Guid OfficeDeskId,
    string OfficeDeskCode,
    string OfficeDeskName,
    Guid? AssignedUserId,
    string? AssignedUserDisplayName,
    string? AssignedUserDesignation,
    Guid AssignedByUserId,
    string AssignedByDisplayName,
    DateTimeOffset AssignedAt,
    DateTimeOffset? FirstSeenAt,
    DateTimeOffset? FirstActionAt,
    bool IsActive
);

public sealed record WorkItemUpdateDetailDto(
    Guid Id,
    string Message,
    Guid AddedByUserId,
    string AddedByDisplayName,
    string? AddedByDesignation,
    DateTimeOffset AddedAt
);

public sealed record WorkItemAttachmentDetailDto(
    Guid Id,
    Guid DocumentId,
    string FileName,
    string? Title,
    string? AttachmentType,
    string ContentType,
    long FileSizeBytes,
    DateTimeOffset CreatedAt
);

public sealed record WorkItemMatterLinkDetailDto(
    Guid MatterId,
    bool IsAuthorized,
    string? Title,
    string? ReferenceNumber,
    string? Status,
    string? MatterType
);

public sealed record WorkItemDakLinkDetailDto(
    Guid DakId,
    bool IsAuthorized,
    string? DiaryNumber,
    string? Subject,
    string? Status,
    string? Priority
);

public sealed record WorkItemEventDto(
    Guid Id,
    int SequenceNumber,
    string Action,
    string ActionText,
    Guid ActionByUserId,
    string ActionByDisplayName,
    string? ActionByDesignation,
    DateTimeOffset ActionAt,
    string? FromStatus,
    string? ToStatus,
    string? Remarks
);

public static class WorkItemEndpoints
{
    private static TimeZoneInfo GetDelhiTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }

    private static string FormatActionText(WorkItemEventAction action, string actorName, string? remarks, string? contextType)
    {
        return action switch
        {
            WorkItemEventAction.Created => $"Work assigned by {actorName}",
            WorkItemEventAction.Assigned => $"Assigned by {actorName}",
            WorkItemEventAction.FirstSeen => $"First seen by {actorName}",
            WorkItemEventAction.Started => $"Work started by {actorName}",
            WorkItemEventAction.MetadataUpdated => $"Details updated by {actorName}",
            WorkItemEventAction.UpdateAdded => $"Progress update added by {actorName}",
            WorkItemEventAction.AttachmentAdded => $"{remarks ?? "Document"} attached by {actorName}",
            WorkItemEventAction.AttachmentRemoved => $"{remarks ?? "Document"} removed by {actorName}",
            WorkItemEventAction.ContextLinked => $"{contextType ?? "Context"} linked by {actorName}",
            WorkItemEventAction.ContextUnlinked => $"{contextType ?? "Context"} unlinked by {actorName}",
            WorkItemEventAction.ContributorAdded => $"Contributor added by {actorName}",
            WorkItemEventAction.ContributorSubmitted => $"Contribution submitted by {actorName}",
            WorkItemEventAction.ContributorReturned => $"Contribution returned by {actorName}",
            WorkItemEventAction.ContributorAccepted => $"Contribution accepted by {actorName}",
            WorkItemEventAction.ContributorRemoved => $"Contributor removed by {actorName}",
            WorkItemEventAction.SubmittedForReview => $"Submitted for review by {actorName}",
            WorkItemEventAction.ReturnedForCorrection => $"Returned for correction by {actorName}",
            WorkItemEventAction.Approved => $"Approved by {actorName}",
            WorkItemEventAction.Reassigned => $"Reassigned by {actorName}",
            WorkItemEventAction.Completed => $"Completed by {actorName}",
            WorkItemEventAction.Cancelled => $"Cancelled by {actorName}",
            _ => $"{action} by {actorName}"
        };
    }

    public static RouteGroupBuilder MapWorkItemEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/work-items");

        // ====================================================================
        // 1. CREATE WORK ITEM
        // ====================================================================
        group.MapPost("/", async (
            CreateWorkItemApiRequest request,
            WorkItemWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            var priority = WorkItemPriority.Routine;
            if (!string.IsNullOrWhiteSpace(request.Priority) &&
                Enum.TryParse<WorkItemPriority>(request.Priority, true, out var parsedPriority))
            {
                priority = parsedPriority;
            }

            try
            {
                var command = new CreateWorkItemCommand(
                    request.Title,
                    request.Instructions,
                    request.WorkstreamId,
                    request.OfficeDeskId,
                    request.AssignedUserId,
                    priority,
                    request.DueAt,
                    request.MatterId,
                    request.DakId,
                    null
                );

                var result = await workflow.CreateWorkItemAsync(command, userId, ct);
                return Results.Created($"/api/work-items/{result.WorkItemId}", result);
            }
            catch (WorkItemWorkflowException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 2. MY WORK PROJECTION
        // ====================================================================
        group.MapGet("/my-work", async (
            string? q,
            Guid? workstreamId,
            string? status,
            string? due,
            string? relationship,
            int? page,
            int? pageSize,
            LacDbContext db,
            IWorkItemAuthorizationService workItemAuth,
            IMatterAuthorizationService matterAuth,
            IDakAuthorizationService dakAuth,
            IOfficeClock officeClock,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            var isUserActive = await db.AppUsers.AsNoTracking()
                .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);

            if (!isUserActive) return Results.Forbid();

            // Check caller's permissions for WorkItem.View
            var viewScopes = await (
                from ur in db.UserRoles
                join r in db.Roles on ur.RoleId equals r.Id
                join rp in db.RolePermissions on r.Id equals rp.RoleId
                join p in db.Permissions on rp.PermissionId equals p.Id
                where ur.UserId == userId
                   && r.IsActive && r.RecordStatus == RecordStatus.Active
                   && p.Code == PermissionCodes.WorkItemView
                select rp.ScopeMode
            ).Distinct().ToListAsync(ct);

            if (viewScopes.Count == 0) return Results.Forbid();

            // Resolve caller's live active desks
            var activeDeskIds = await db.UserDeskMemberships.AsNoTracking()
                .Where(m => m.UserId == userId
                         && m.IsActive
                         && m.RemovedAt == null
                         && m.RecordStatus == RecordStatus.Active
                         && m.OfficeDesk.IsActive
                         && m.OfficeDesk.RecordStatus == RecordStatus.Active)
                .Select(m => m.OfficeDeskId)
                .ToListAsync(ct);

            // Step 1: Base query representing operational participation
            var baseQuery = db.WorkItems.AsNoTracking()
                .Where(w => w.RecordStatus == RecordStatus.Active);

            // Intersect with operational participation criteria:
            // - Desk responsibility (user must have live active membership in the assigned desk, regardless of optional named handler)
            // - Requested by caller and not completed/cancelled
            // - Contributor participation
            baseQuery = baseQuery.Where(w =>
                (w.CurrentAssignment != null && w.CurrentAssignment.IsActive && w.CurrentAssignment.RecordStatus == RecordStatus.Active &&
                    activeDeskIds.Contains(w.CurrentAssignment.OfficeDeskId)) ||
                (w.RequestedByUserId == userId && w.Status != WorkItemStatus.Completed && w.Status != WorkItemStatus.Cancelled) ||
                w.Contributors.Any(c => c.UserId == userId && c.IsActive && c.RecordStatus == RecordStatus.Active && c.Status == WorkItemContributorStatus.Active)
            );

            // Step 2: Intersect with WorkItem.View authorization scopes
            if (!viewScopes.Contains(ScopeMode.All))
            {
                if (viewScopes.Contains(ScopeMode.Workstream))
                {
                    var userWorkstreamIds = await db.UserWorkstreamMemberships.AsNoTracking()
                        .Where(m => m.UserId == userId
                                 && m.IsActive
                                 && m.Workstream.IsActive
                                 && m.Workstream.RecordStatus == RecordStatus.Active)
                        .Select(m => m.WorkstreamId)
                        .ToListAsync(ct);

                    baseQuery = baseQuery.Where(w => userWorkstreamIds.Contains(w.WorkstreamId));
                }
                else if (viewScopes.Contains(ScopeMode.Assigned))
                {
                    // ScopeMode.Assigned allows only items where caller is a live desk member of the assigned desk or active contributor
                    baseQuery = baseQuery.Where(w =>
                        (w.CurrentAssignment != null && w.CurrentAssignment.IsActive && w.CurrentAssignment.RecordStatus == RecordStatus.Active &&
                            activeDeskIds.Contains(w.CurrentAssignment.OfficeDeskId)) ||
                        w.Contributors.Any(c => c.UserId == userId && c.IsActive && c.RecordStatus == RecordStatus.Active && c.Status == WorkItemContributorStatus.Active)
                    );
                }
                else
                {
                    return Results.Forbid();
                }
            }

            // Step 3: Compute Delhi office date boundaries
            var officeToday = officeClock.GetCurrentDate();
            var delhiTz = GetDelhiTimeZone();
            var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(officeToday.ToDateTime(TimeOnly.MinValue), delhiTz);
            var tomorrowStartUtc = TimeZoneInfo.ConvertTimeToUtc(officeToday.AddDays(1).ToDateTime(TimeOnly.MinValue), delhiTz);
            var weekEndUtc = TimeZoneInfo.ConvertTimeToUtc(officeToday.AddDays(7).ToDateTime(TimeOnly.MinValue), delhiTz);
            var nowUtc = officeClock.GetUtcNow();

            // Step 4: Summary calculated BEFORE pagination and before tab filter
            var summaryTotalOpen = await baseQuery.CountAsync(w => w.Status != WorkItemStatus.Completed && w.Status != WorkItemStatus.Cancelled, ct);
            var summaryAssignedToMe = await baseQuery.CountAsync(w =>
                w.Status != WorkItemStatus.Completed &&
                w.Status != WorkItemStatus.Cancelled &&
                w.CurrentAssignment != null &&
                activeDeskIds.Contains(w.CurrentAssignment.OfficeDeskId), ct);
            var summaryRequestedByMe = await baseQuery.CountAsync(w =>
                w.Status != WorkItemStatus.Completed &&
                w.Status != WorkItemStatus.Cancelled &&
                w.RequestedByUserId == userId, ct);
            var summaryOverdue = await baseQuery.CountAsync(w =>
                w.Status != WorkItemStatus.Completed &&
                w.Status != WorkItemStatus.Cancelled &&
                w.DueAt != null && w.DueAt.Value < nowUtc, ct);
            var summaryDueToday = await baseQuery.CountAsync(w =>
                w.Status != WorkItemStatus.Completed &&
                w.Status != WorkItemStatus.Cancelled &&
                w.DueAt != null && w.DueAt.Value >= todayStartUtc && w.DueAt.Value < tomorrowStartUtc, ct);
            var summaryDueThisWeek = await baseQuery.CountAsync(w =>
                w.Status != WorkItemStatus.Completed &&
                w.Status != WorkItemStatus.Cancelled &&
                w.DueAt != null && w.DueAt.Value >= todayStartUtc && w.DueAt.Value < weekEndUtc, ct);
            var summaryNeedsReview = await baseQuery.CountAsync(w =>
                w.Status == WorkItemStatus.SubmittedForReview, ct);

            var summary = new MyWorkSummaryDto(
                summaryTotalOpen,
                summaryAssignedToMe,
                summaryRequestedByMe,
                summaryOverdue,
                summaryDueToday,
                summaryDueThisWeek,
                summaryNeedsReview
            );

            // Step 5: Filter application
            var filteredQuery = baseQuery;

            // Status filter: by default exclude terminal Completed / Cancelled unless explicit
            if (string.IsNullOrWhiteSpace(status))
            {
                filteredQuery = filteredQuery.Where(w => w.Status != WorkItemStatus.Completed && w.Status != WorkItemStatus.Cancelled);
            }
            else if (!string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse<WorkItemStatus>(status, true, out var targetStatus))
                {
                    filteredQuery = filteredQuery.Where(w => w.Status == targetStatus);
                }
            }

            if (workstreamId.HasValue)
            {
                filteredQuery = filteredQuery.Where(w => w.WorkstreamId == workstreamId.Value);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                filteredQuery = filteredQuery.Where(w =>
                    w.Title.ToLower().Contains(term) ||
                    (w.Instructions != null && w.Instructions.ToLower().Contains(term)) ||
                    w.RequestedByDisplayNameSnapshot.ToLower().Contains(term));
            }

            var dueFilter = (due ?? "all").Trim().ToLowerInvariant();
            if (dueFilter == "overdue")
            {
                filteredQuery = filteredQuery.Where(w => w.DueAt != null && w.DueAt.Value < nowUtc);
            }
            else if (dueFilter == "today")
            {
                filteredQuery = filteredQuery.Where(w => w.DueAt != null && w.DueAt.Value >= todayStartUtc && w.DueAt.Value < tomorrowStartUtc);
            }
            else if (dueFilter == "week")
            {
                filteredQuery = filteredQuery.Where(w => w.DueAt != null && w.DueAt.Value >= todayStartUtc && w.DueAt.Value < weekEndUtc);
            }
            else if (dueFilter == "upcoming")
            {
                filteredQuery = filteredQuery.Where(w => w.DueAt != null && w.DueAt.Value >= tomorrowStartUtc);
            }
            else if (dueFilter == "none")
            {
                filteredQuery = filteredQuery.Where(w => w.DueAt == null);
            }

            var relFilter = (relationship ?? "all").Trim().ToLowerInvariant();
            if (relFilter == "assigned")
            {
                filteredQuery = filteredQuery.Where(w =>
                    w.CurrentAssignment != null &&
                    activeDeskIds.Contains(w.CurrentAssignment.OfficeDeskId));
            }
            else if (relFilter == "requested")
            {
                filteredQuery = filteredQuery.Where(w => w.RequestedByUserId == userId);
            }
            else if (relFilter == "review")
            {
                filteredQuery = filteredQuery.Where(w => w.Status == WorkItemStatus.SubmittedForReview);
            }

            var totalCount = await filteredQuery.CountAsync(ct);

            // Step 6: Deterministic sorting
            // 1: Overdue, 2: Due Today, 3: Nearest future due, 4: No due date
            var pIndex = Math.Max(0, page ?? 0);
            var pSize = Math.Clamp(pageSize ?? 25, 1, 100);

            var rawItems = await filteredQuery
                .OrderBy(w => w.DueAt != null && w.DueAt.Value < nowUtc ? 0 :
                              w.DueAt != null && w.DueAt.Value >= todayStartUtc && w.DueAt.Value < tomorrowStartUtc ? 1 :
                              w.DueAt != null && w.DueAt.Value >= tomorrowStartUtc ? 2 : 3)
                .ThenByDescending(w => w.Priority)
                .ThenBy(w => w.DueAt)
                .ThenByDescending(w => w.LastActivityAt)
                .ThenByDescending(w => w.CreatedAt)
                .ThenByDescending(w => w.Id)
                .Skip(pIndex * pSize)
                .Take(pSize)
                .Select(w => new
                {
                    w.Id,
                    w.Title,
                    Priority = w.Priority.ToString(),
                    Status = w.Status.ToString(),
                    w.DueAt,
                    w.WorkstreamId,
                    WorkstreamName = w.Workstream.Name,
                    w.RequestedByUserId,
                    w.RequestedByDisplayNameSnapshot,
                    w.RequestedByDesignationSnapshot,
                    OfficeDeskId = w.CurrentAssignment != null ? w.CurrentAssignment.OfficeDeskId : Guid.Empty,
                    OfficeDeskName = w.CurrentAssignment != null ? w.CurrentAssignment.OfficeDesk.Name : "",
                    AssignedUserId = w.CurrentAssignment != null ? w.CurrentAssignment.AssignedUserId : null,
                    AssignedUserDisplayName = w.CurrentAssignment != null && w.CurrentAssignment.AssignedUser != null ? w.CurrentAssignment.AssignedUser.DisplayName : null,
                    AssignedUserDesignation = w.CurrentAssignment != null && w.CurrentAssignment.AssignedUser != null && w.CurrentAssignment.AssignedUser.Designation != null ? w.CurrentAssignment.AssignedUser.Designation.Name : null,
                    w.Revision,
                    w.LastActivityAt,
                    w.CreatedAt,
                    MatterId = w.MatterLinks.Where(m => m.RecordStatus == RecordStatus.Active).Select(m => (Guid?)m.MatterId).FirstOrDefault(),
                    DakId = w.DakLinks.Where(d => d.RecordStatus == RecordStatus.Active).Select(d => (Guid?)d.DakId).FirstOrDefault()
                })
                .ToListAsync(ct);

            // Step 7: Map context hints with isolated authorization checks
            var items = new List<MyWorkItemDto>();
            foreach (var item in rawItems)
            {
                string dueState = "none";
                if (item.DueAt.HasValue)
                {
                    if (item.DueAt.Value < nowUtc) dueState = "overdue";
                    else if (item.DueAt.Value >= todayStartUtc && item.DueAt.Value < tomorrowStartUtc) dueState = "today";
                    else dueState = "upcoming";
                }

                bool hasLinkedMatter = item.MatterId.HasValue;
                string? linkedMatterTitle = null;
                if (item.MatterId.HasValue)
                {
                    var canAccessMatter = await matterAuth.CanAccessMatterAsync(item.MatterId.Value, PermissionCodes.MatterView, userId, ct);
                    if (canAccessMatter)
                    {
                        linkedMatterTitle = await db.Matters.AsNoTracking()
                            .Where(m => m.Id == item.MatterId.Value)
                            .Select(m => m.Title)
                            .FirstOrDefaultAsync(ct);
                    }
                }

                bool hasLinkedDak = item.DakId.HasValue;
                string? linkedDakSubject = null;
                if (item.DakId.HasValue)
                {
                    var canAccessDak = await dakAuth.CanAccessDakAsync(item.DakId.Value, PermissionCodes.DakView, userId, ct);
                    if (canAccessDak)
                    {
                        linkedDakSubject = await db.Daks.AsNoTracking()
                            .Where(d => d.Id == item.DakId.Value)
                            .Select(d => d.Subject)
                            .FirstOrDefaultAsync(ct);
                    }
                }

                items.Add(new MyWorkItemDto(
                    item.Id,
                    item.Title,
                    item.Priority,
                    item.Status,
                    item.DueAt,
                    dueState,
                    item.WorkstreamId,
                    item.WorkstreamName,
                    item.RequestedByUserId,
                    item.RequestedByDisplayNameSnapshot,
                    item.RequestedByDesignationSnapshot,
                    item.OfficeDeskId,
                    item.OfficeDeskName,
                    item.AssignedUserId,
                    item.AssignedUserDisplayName,
                    item.AssignedUserDesignation,
                    item.Revision,
                    item.LastActivityAt,
                    item.CreatedAt,
                    hasLinkedMatter,
                    linkedMatterTitle,
                    hasLinkedDak,
                    linkedDakSubject
                ));
            }

            return Results.Ok(new MyWorkResponseDto(summary, items, totalCount, pIndex, pSize));
        });

        // ====================================================================
        // 3. GET WORK ITEM DETAIL
        // ====================================================================
        group.MapGet("/{id:guid}", async (
            Guid id,
            LacDbContext db,
            IWorkItemAuthorizationService workItemAuth,
            IMatterAuthorizationService matterAuth,
            IDakAuthorizationService dakAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            var canView = await workItemAuth.CanAccessWorkItemAsync(id, PermissionCodes.WorkItemView, userId, ct);
            if (!canView) return Results.Forbid();

            var item = await db.WorkItems.AsNoTracking()
                .Include(w => w.Workstream)
                .Include(w => w.CurrentAssignment)
                    .ThenInclude(a => a!.OfficeDesk)
                .Include(w => w.CurrentAssignment)
                    .ThenInclude(a => a!.AssignedUser)
                        .ThenInclude(u => u!.Designation)
                .Include(w => w.CurrentAssignment)
                    .ThenInclude(a => a!.AssignedByUser)
                .Include(w => w.Updates)
                    .ThenInclude(u => u.AddedByUser)
                        .ThenInclude(u => u.Designation)
                .Include(w => w.Attachments.Where(a => a.RecordStatus == RecordStatus.Active))
                    .ThenInclude(a => a.Document)
                .Include(w => w.MatterLinks.Where(m => m.RecordStatus == RecordStatus.Active))
                    .ThenInclude(m => m.Matter)
                .Include(w => w.DakLinks.Where(d => d.RecordStatus == RecordStatus.Active))
                    .ThenInclude(d => d.Dak)
                .FirstOrDefaultAsync(w => w.Id == id && w.RecordStatus == RecordStatus.Active, ct);

            if (item is null) return Results.NotFound();

            WorkItemAssignmentDetailDto? assignmentDto = null;
            if (item.CurrentAssignment != null)
            {
                assignmentDto = new WorkItemAssignmentDetailDto(
                    item.CurrentAssignment.Id,
                    item.CurrentAssignment.OfficeDeskId,
                    item.CurrentAssignment.OfficeDesk.Code,
                    item.CurrentAssignment.OfficeDesk.Name,
                    item.CurrentAssignment.AssignedUserId,
                    item.CurrentAssignment.AssignedUser?.DisplayName,
                    item.CurrentAssignment.AssignedUser?.Designation?.Name,
                    item.CurrentAssignment.AssignedByUserId,
                    item.CurrentAssignment.AssignedByUser.DisplayName,
                    item.CurrentAssignment.AssignedAt,
                    item.CurrentAssignment.FirstSeenAt,
                    item.CurrentAssignment.FirstActionAt,
                    item.CurrentAssignment.IsActive
                );
            }

            var updatesDto = item.Updates
                .OrderBy(u => u.AddedAt)
                .Select(u => new WorkItemUpdateDetailDto(
                    u.Id,
                    u.Message,
                    u.AddedByUserId,
                    u.AddedByDisplayNameSnapshot,
                    u.AddedByDesignationSnapshot,
                    u.AddedAt
                ))
                .ToList();

            var attachmentsDto = item.Attachments
                .Where(a => a.Document != null && a.Document.RecordStatus == RecordStatus.Active && a.Document.Status == "Active")
                .OrderBy(a => a.CreatedAt)
                .Select(a => new WorkItemAttachmentDetailDto(
                    a.Id,
                    a.DocumentId,
                    a.Document.OriginalFileName,
                    a.Title,
                    a.AttachmentType,
                    a.Document.MimeType ?? "application/octet-stream",
                    a.Document.FileSize ?? 0,
                    a.CreatedAt
                ))
                .ToList();

            var matterLinksDto = new List<WorkItemMatterLinkDetailDto>();
            foreach (var link in item.MatterLinks)
            {
                var canAccessMatter = await matterAuth.CanAccessMatterAsync(link.MatterId, PermissionCodes.MatterView, userId, ct);
                if (canAccessMatter && link.Matter != null)
                {
                    matterLinksDto.Add(new WorkItemMatterLinkDetailDto(
                        link.MatterId,
                        true,
                        link.Matter.Title,
                        link.Matter.ReferenceNumber,
                        link.Matter.Status,
                        link.Matter.MatterType
                    ));
                }
                else
                {
                    matterLinksDto.Add(new WorkItemMatterLinkDetailDto(
                        link.MatterId,
                        false,
                        null,
                        null,
                        null,
                        null
                    ));
                }
            }

            var dakLinksDto = new List<WorkItemDakLinkDetailDto>();
            foreach (var link in item.DakLinks)
            {
                var canAccessDak = await dakAuth.CanAccessDakAsync(link.DakId, PermissionCodes.DakView, userId, ct);
                if (canAccessDak && link.Dak != null)
                {
                    dakLinksDto.Add(new WorkItemDakLinkDetailDto(
                        link.DakId,
                        true,
                        link.Dak.DiaryNumber,
                        link.Dak.Subject,
                        link.Dak.Status.ToString(),
                        link.Dak.Priority.ToString()
                    ));
                }
                else
                {
                    dakLinksDto.Add(new WorkItemDakLinkDetailDto(
                        link.DakId,
                        false,
                        null,
                        null,
                        null,
                        null
                    ));
                }
            }

            return Results.Ok(new WorkItemDetailDto(
                item.Id,
                item.Title,
                item.Instructions,
                item.Priority.ToString(),
                item.Status.ToString(),
                item.Origin.ToString(),
                item.DueAt,
                item.Revision,
                item.WorkstreamId,
                item.Workstream.Name,
                item.RequestedByUserId,
                item.RequestedByDisplayNameSnapshot,
                item.RequestedByDesignationSnapshot,
                item.LastActivityAt,
                item.CompletedAt,
                item.CreatedAt,
                assignmentDto,
                updatesDto,
                attachmentsDto,
                matterLinksDto,
                dakLinksDto
            ));
        });

        // ====================================================================
        // 4. TIMELINE
        // ====================================================================
        group.MapGet("/{id:guid}/timeline", async (
            Guid id,
            LacDbContext db,
            IWorkItemAuthorizationService workItemAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            var canView = await workItemAuth.CanAccessWorkItemAsync(id, PermissionCodes.WorkItemView, userId, ct);
            if (!canView) return Results.Forbid();

            var events = await db.WorkItemEvents.AsNoTracking()
                .Where(e => e.WorkItemId == id)
                .OrderBy(e => e.SequenceNumber)
                .ToListAsync(ct);

            var dtos = events.Select(e => new WorkItemEventDto(
                e.Id,
                e.SequenceNumber,
                e.Action.ToString(),
                FormatActionText(e.Action, e.ActionByDisplayNameSnapshot, e.RemarksSnapshot, e.ContextType),
                e.ActionByUserId,
                e.ActionByDisplayNameSnapshot,
                e.ActionByDesignationSnapshot,
                e.ActionAt,
                e.FromStatus?.ToString(),
                e.ToStatus?.ToString(),
                e.RemarksSnapshot
            )).ToList();

            return Results.Ok(dtos);
        });

        // ====================================================================
        // 5. MARK SEEN (IDEMPOTENT)
        // ====================================================================
        group.MapPost("/{id:guid}/seen", async (
            Guid id,
            WorkItemWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            try
            {
                var marked = await workflow.MarkSeenAsync(id, userId, ct);
                return Results.Ok(new { seen = marked });
            }
            catch (WorkItemWorkflowException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 6. START WORK
        // ====================================================================
        group.MapPost("/{id:guid}/start", async (
            Guid id,
            StartWorkApiRequest request,
            WorkItemWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!request.ExpectedRevision.HasValue)
                return Results.BadRequest(new { message = "expectedRevision is required." });

            try
            {
                var newRevision = await workflow.StartWorkAsync(id, new StartWorkCommand(request.ExpectedRevision.Value), userId, ct);
                return Results.Ok(new { revision = newRevision, status = "InProgress" });
            }
            catch (WorkItemWorkflowException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 7. ADD PROGRESS UPDATE
        // ====================================================================
        group.MapPost("/{id:guid}/updates", async (
            Guid id,
            AddUpdateApiRequest request,
            WorkItemWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!request.ExpectedRevision.HasValue)
                return Results.BadRequest(new { message = "expectedRevision is required." });

            try
            {
                var newRevision = await workflow.AddUpdateAsync(id, new AddWorkItemUpdateCommand(request.Message, request.ExpectedRevision.Value), userId, ct);
                return Results.Ok(new { revision = newRevision });
            }
            catch (WorkItemWorkflowException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 8. UPLOAD ATTACHMENT
        // ====================================================================
        group.MapPost("/{id:guid}/attachments", async (
            Guid id,
            HttpRequest request,
            WorkItemWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!request.HasFormContentType)
                return Results.BadRequest(new { message = "Multipart form data required for attachment upload." });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { message = "File must be provided." });

            var title = form["title"].ToString();
            var attachmentType = form["attachmentType"].ToString();
            if (!form.ContainsKey("expectedRevision") || !int.TryParse(form["expectedRevision"].ToString(), out var expectedRevision))
                return Results.BadRequest(new { message = "expectedRevision is required." });

            try
            {
                await using var stream = file.OpenReadStream();
                var cmd = new UploadWorkItemAttachmentCommand(
                    stream,
                    file.FileName,
                    file.ContentType,
                    string.IsNullOrWhiteSpace(title) ? null : title,
                    string.IsNullOrWhiteSpace(attachmentType) ? null : attachmentType,
                    expectedRevision
                );

                var attachmentId = await workflow.UploadAttachmentAsync(id, cmd, userId, ct);
                return Results.Created($"/api/work-items/{id}/attachments/{attachmentId}", new { attachmentId });
            }
            catch (WorkItemWorkflowException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 9. STREAM ATTACHMENT CONTENT
        // ====================================================================
        group.MapGet("/{id:guid}/attachments/{attachmentId:guid}/content", async (
            Guid id,
            Guid attachmentId,
            LacDbContext db,
            IDocumentStorage storage,
            IWorkItemAuthorizationService workItemAuth,
            ICurrentUserContext currentUser,
            HttpResponse response,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            var canAccess = await workItemAuth.CanAccessAttachmentContentAsync(id, attachmentId, userId, ct);
            if (!canAccess) return Results.Forbid();

            var attachment = await db.WorkItemAttachments.AsNoTracking()
                .Include(a => a.Document)
                .FirstOrDefaultAsync(a => a.Id == attachmentId && a.WorkItemId == id && a.RecordStatus == RecordStatus.Active, ct);

            if (attachment is null || attachment.Document is null)
                return Results.NotFound();

            var doc = attachment.Document;
            if (attachment.RecordStatus != RecordStatus.Active || doc.RecordStatus != RecordStatus.Active || doc.Status != "Active")
                return Results.NotFound(new { message = "Document is not in an active state." });

            var stream = await storage.OpenReadAsync(doc.StoragePath, ct);
            if (stream is null)
                return Results.NotFound(new { message = "Document file not found on storage." });

            response.Headers.Append("X-Content-Type-Options", "nosniff");
            return Results.File(stream, doc.MimeType ?? "application/octet-stream", doc.OriginalFileName);
        });

        // ====================================================================
        // 10. REMOVE ATTACHMENT
        // ====================================================================
        group.MapDelete("/{id:guid}/attachments/{attachmentId:guid}", async (
            Guid id,
            Guid attachmentId,
            [Microsoft.AspNetCore.Mvc.FromQuery] int? expectedRevision,
            HttpContext httpContext,
            WorkItemWorkflowService workflow,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            int? rev = expectedRevision;
            if (!rev.HasValue && httpContext.Request.HasJsonContentType())
            {
                try
                {
                    var body = await httpContext.Request.ReadFromJsonAsync<RemoveAttachmentApiRequest>(ct);
                    if (body != null) rev = body.ExpectedRevision;
                }
                catch { }
            }

            if (!rev.HasValue)
                return Results.BadRequest(new { message = "expectedRevision is required." });

            try
            {
                var newRevision = await workflow.RemoveAttachmentAsync(id, attachmentId, new RemoveWorkItemAttachmentCommand(rev.Value), userId, ct);
                return Results.Ok(new { revision = newRevision });
            }
            catch (WorkItemWorkflowException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        });

        // ====================================================================
        // 11. CREATE CONTEXT
        // ====================================================================
        group.MapGet("/create-context", async (
            IWorkItemAuthorizationService workItemAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            var workstreams = await workItemAuth.GetCreateContextWorkstreamsAsync(userId, ct);
            var dtos = workstreams.Select(w => new { w.Id, w.Code, w.Name, w.Description }).ToList();
            return Results.Ok(new { workstreams = dtos });
        });

        // ====================================================================
        // 12. ASSIGNMENT OPTIONS
        // ====================================================================
        group.MapGet("/assignment-options", async (
            [Microsoft.AspNetCore.Mvc.FromQuery] Guid? workstreamId,
            IWorkItemAuthorizationService workItemAuth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var userId = currentUser.UserId.Value;

            if (!workstreamId.HasValue || workstreamId.Value == Guid.Empty)
                return Results.BadRequest(new { message = "workstreamId is required." });

            var (allowed, statusCode, errorMessage, desks) = await workItemAuth.GetAssignmentOptionsForWorkstreamAsync(workstreamId.Value, userId, ct);
            if (!allowed)
            {
                if (statusCode == 400) return Results.BadRequest(new { message = errorMessage });
                return Results.Forbid();
            }

            return Results.Ok(new { desks });
        });

        return group;
    }
}
