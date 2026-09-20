namespace LAC.Infrastructure;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class ActivityAccessException : Exception
{
    public int StatusCode { get; }
    public ActivityAccessException(string message, int statusCode = 403) : base(message)
    {
        StatusCode = statusCode;
    }
}

public sealed record ActivityQuery(
    DateTimeOffset? DateFrom = null,
    DateTimeOffset? DateTo = null,
    string? EntityType = null,
    string? Action = null,
    Guid? WorkstreamId = null,
    Guid? DeskId = null,
    Guid? ActorUserId = null,
    string? Search = null,
    bool IncludeReads = true,
    int Page = 1,
    int PageSize = 20
);

public sealed record ActivityItemDto(
    Guid EventId,
    string SourceType,
    string Action,
    string Summary,
    DateTimeOffset OccurredAt,
    Guid ActorUserId,
    string ActorDisplayName,
    string EntityType,
    Guid EntityId,
    string EntityTitle,
    string? EntityReferenceNumber,
    Guid? WorkstreamId,
    string? WorkstreamName,
    Guid? DeskId,
    string? DeskName,
    bool CanOpen,
    string? NavigationUrl,
    bool IsReadEvent,
    object? Metadata = null
);

public sealed record ActivityFeedResult(
    IReadOnlyList<ActivityItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasMore
);

public interface IActivityProjectionService
{
    Task<ActivityFeedResult> GetMyHistoryAsync(Guid userId, ActivityQuery query, CancellationToken ct = default);
    Task<ActivityFeedResult> GetTeamActivityAsync(Guid userId, ActivityQuery query, CancellationToken ct = default);
}

public sealed class ActivityProjectionService(
    LacDbContext db,
    IMatterAuthorizationService matterAuth,
    IDakAuthorizationService dakAuth,
    IOutwardAuthorizationService outwardAuth,
    IWorkItemAuthorizationService workItemAuth) : IActivityProjectionService
{
    // ========================================================================
    // 1. MY HISTORY
    // ========================================================================
    public async Task<ActivityFeedResult> GetMyHistoryAsync(Guid userId, ActivityQuery query, CancellationToken ct = default)
    {
        var user = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null || !user.IsActive || user.RecordStatus != RecordStatus.Active)
            throw new ActivityAccessException("Inactive or unauthorized account.", 403);

        return await QueryFeedAsync(userId, query, isTeamActivity: false, ct);
    }

    // ========================================================================
    // 2. TEAM ACTIVITY
    // ========================================================================
    public async Task<ActivityFeedResult> GetTeamActivityAsync(Guid userId, ActivityQuery query, CancellationToken ct = default)
    {
        var user = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null || !user.IsActive || user.RecordStatus != RecordStatus.Active)
            throw new ActivityAccessException("Inactive or unauthorized account.", 403);

        // Audit.View authorization check
        var scopes = await (from ur in db.UserRoles
                            join r in db.Roles on ur.RoleId equals r.Id
                            join rp in db.RolePermissions on r.Id equals rp.RoleId
                            join p in db.Permissions on rp.PermissionId equals p.Id
                            where ur.UserId == userId && r.IsActive && r.RecordStatus == RecordStatus.Active && p.Code == PermissionCodes.AuditView
                            select rp.ScopeMode).Distinct().ToListAsync(ct);

        if (scopes.Count == 0)
            throw new ActivityAccessException("Caller lacks Audit.View permission.", 403);

        if (!scopes.Contains(ScopeMode.All) && !scopes.Contains(ScopeMode.Workstream) && !scopes.Contains(ScopeMode.Assigned))
        {
            // Own scope fails closed for Team Activity
            throw new ActivityAccessException("Team Activity requires All, Workstream, or Assigned scope for Audit.View. Own scope fails closed.", 403);
        }

        return await QueryFeedAsync(userId, query, isTeamActivity: true, ct, scopes);
    }

    // ========================================================================
    // 3. CORE UNIFIED PROJECTION ENGINE
    // ========================================================================
    private async Task<ActivityFeedResult> QueryFeedAsync(
        Guid currentUserId,
        ActivityQuery query,
        bool isTeamActivity,
        CancellationToken ct,
        List<ScopeMode>? auditScopes = null)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;
        var take = pageSize;
        var fetchCount = skip + take;

        var hasAllScope = !isTeamActivity || (auditScopes?.Contains(ScopeMode.All) ?? false);
        var hasWorkstreamScope = isTeamActivity && (auditScopes?.Contains(ScopeMode.Workstream) ?? false);
        var hasAssignedScope = isTeamActivity && (auditScopes?.Contains(ScopeMode.Assigned) ?? false);

        List<Guid> callerWsIds = [];
        List<Guid> callerDeskIds = [];

        if (isTeamActivity && !hasAllScope)
        {
            if (hasWorkstreamScope)
            {
                callerWsIds = await db.UserWorkstreamMemberships.AsNoTracking()
                    .Where(m => m.UserId == currentUserId && m.IsActive)
                    .Select(m => m.WorkstreamId)
                    .ToListAsync(ct);
            }

            if (hasAssignedScope)
            {
                callerDeskIds = await db.UserDeskMemberships.AsNoTracking()
                    .Where(m => m.UserId == currentUserId && m.IsActive && m.RecordStatus == RecordStatus.Active)
                    .Select(m => m.OfficeDeskId)
                    .ToListAsync(ct);
            }
        }

        var normalizedEntityType = query.EntityType?.Trim().ToLowerInvariant();
        var shouldIncludeDak = string.IsNullOrEmpty(normalizedEntityType) || normalizedEntityType == "dak";
        var shouldIncludeMatter = string.IsNullOrEmpty(normalizedEntityType) || normalizedEntityType == "matter";
        var shouldIncludeOutward = string.IsNullOrEmpty(normalizedEntityType) || normalizedEntityType == "outward";
        var shouldIncludeWorkItem = string.IsNullOrEmpty(normalizedEntityType) || normalizedEntityType == "workitem" || normalizedEntityType == "work";
        var shouldIncludeReads = query.IncludeReads && (string.IsNullOrEmpty(normalizedEntityType) || normalizedEntityType == "document" || normalizedEntityType == "read");

        var allItems = new List<ActivityItemDto>();
        var totalCount = 0;

        // --------------------------------------------------------------------
        // A. DAK MOVEMENTS
        // --------------------------------------------------------------------
        if (shouldIncludeDak)
        {
            var q = db.DakMovements.AsNoTracking().Include(m => m.Dak).AsQueryable();

            if (!isTeamActivity)
            {
                q = q.Where(m => m.ActionByUserId == currentUserId);
            }
            else
            {
                if (query.ActorUserId.HasValue)
                    q = q.Where(m => m.ActionByUserId == query.ActorUserId.Value);

                if (!hasAllScope)
                {
                    if (hasAssignedScope && callerDeskIds.Count > 0)
                    {
                        q = q.Where(m => (m.FromDeskId.HasValue && callerDeskIds.Contains(m.FromDeskId.Value))
                                      || (m.ToDeskId.HasValue && callerDeskIds.Contains(m.ToDeskId.Value)));
                    }
                    else
                    {
                        q = q.Where(_ => false);
                    }
                }
            }

            if (query.DateFrom.HasValue) q = q.Where(m => m.ActionAt >= query.DateFrom.Value);
            if (query.DateTo.HasValue) q = q.Where(m => m.ActionAt <= query.DateTo.Value);
            if (query.DeskId.HasValue)
            {
                q = q.Where(m => m.FromDeskId == query.DeskId.Value || m.ToDeskId == query.DeskId.Value);
            }
            if (!string.IsNullOrWhiteSpace(query.Action))
            {
                var actionStr = query.Action.Trim();
                q = q.Where(m => m.Action.ToString().ToLower() == actionStr.ToLower());
            }
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var s = query.Search.Trim().ToLower();
                q = q.Where(m => m.Dak.Subject.ToLower().Contains(s)
                              || m.Dak.DiaryNumber.ToLower().Contains(s)
                              || m.ActionByDisplayNameSnapshot.ToLower().Contains(s)
                              || (m.Remarks != null && m.Remarks.ToLower().Contains(s)));
            }

            var dakCount = await q.CountAsync(ct);
            totalCount += dakCount;

            var dakEvents = await q.OrderByDescending(m => m.ActionAt)
                .ThenBy(m => m.Id)
                .Take(fetchCount)
                .Select(m => new
                {
                    m.Id,
                    m.DakId,
                    m.Action,
                    m.ActionByUserId,
                    m.ActionByDisplayNameSnapshot,
                    m.ActionAt,
                    m.FromDeskId,
                    m.FromDeskNameSnapshot,
                    m.ToDeskId,
                    m.ToDeskNameSnapshot,
                    m.Remarks,
                    m.Dak.DiaryNumber,
                    m.Dak.Subject
                })
                .ToListAsync(ct);

            foreach (var m in dakEvents)
            {
                var summary = FormatDakSummary(m.Action, m.FromDeskNameSnapshot, m.ToDeskNameSnapshot);
                allItems.Add(new ActivityItemDto(
                    EventId: m.Id,
                    SourceType: "Dak",
                    Action: m.Action.ToString(),
                    Summary: summary,
                    OccurredAt: m.ActionAt,
                    ActorUserId: m.ActionByUserId,
                    ActorDisplayName: m.ActionByDisplayNameSnapshot,
                    EntityType: "Dak",
                    EntityId: m.DakId,
                    EntityTitle: m.Subject,
                    EntityReferenceNumber: m.DiaryNumber,
                    WorkstreamId: null,
                    WorkstreamName: null,
                    DeskId: m.ToDeskId ?? m.FromDeskId,
                    DeskName: m.ToDeskNameSnapshot ?? m.FromDeskNameSnapshot,
                    CanOpen: false, // will be evaluated for final page
                    NavigationUrl: null,
                    IsReadEvent: false,
                    Metadata: new { m.Remarks }
                ));
            }
        }

        // --------------------------------------------------------------------
        // B. MATTER EVENTS
        // --------------------------------------------------------------------
        if (shouldIncludeMatter)
        {
            var q = db.MatterEvents.AsNoTracking().Include(e => e.Matter).ThenInclude(m => m.Workstream).AsQueryable();

            if (!isTeamActivity)
            {
                q = q.Where(e => e.ActionByUserId == currentUserId);
            }
            else
            {
                if (query.ActorUserId.HasValue)
                    q = q.Where(e => e.ActionByUserId == query.ActorUserId.Value);

                if (!hasAllScope)
                {
                    if (hasWorkstreamScope && callerWsIds.Count > 0)
                    {
                        // Strict historical snapshot matching; fail closed if snapshot is null
                        q = q.Where(e => (e.WorkstreamIdSnapshot.HasValue && callerWsIds.Contains(e.WorkstreamIdSnapshot.Value))
                                      || (e.SourceWorkstreamId.HasValue && callerWsIds.Contains(e.SourceWorkstreamId.Value))
                                      || (e.TargetWorkstreamId.HasValue && callerWsIds.Contains(e.TargetWorkstreamId.Value)));
                    }
                    else
                    {
                        q = q.Where(_ => false);
                    }
                }
            }

            if (query.DateFrom.HasValue) q = q.Where(e => e.ActionAt >= query.DateFrom.Value);
            if (query.DateTo.HasValue) q = q.Where(e => e.ActionAt <= query.DateTo.Value);
            if (query.WorkstreamId.HasValue)
            {
                q = q.Where(e => e.WorkstreamIdSnapshot == query.WorkstreamId.Value
                              || e.SourceWorkstreamId == query.WorkstreamId.Value
                              || e.TargetWorkstreamId == query.WorkstreamId.Value);
            }
            if (!string.IsNullOrWhiteSpace(query.Action))
            {
                var actionStr = query.Action.Trim();
                q = q.Where(e => e.Action.ToString().ToLower() == actionStr.ToLower());
            }
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var s = query.Search.Trim().ToLower();
                q = q.Where(e => e.Matter.Title.ToLower().Contains(s)
                              || (e.Matter.ReferenceNumber != null && e.Matter.ReferenceNumber.ToLower().Contains(s))
                              || e.ActionByDisplayNameSnapshot.ToLower().Contains(s));
            }

            var matterCount = await q.CountAsync(ct);
            totalCount += matterCount;

            var matterEvents = await q.OrderByDescending(e => e.ActionAt)
                .ThenBy(e => e.Id)
                .Take(fetchCount)
                .Select(e => new
                {
                    e.Id,
                    e.MatterId,
                    e.Action,
                    e.ActionByUserId,
                    e.ActionByDisplayNameSnapshot,
                    e.ActionAt,
                    e.WorkstreamIdSnapshot,
                    e.SourceWorkstreamId,
                    e.TargetWorkstreamId,
                    e.DocumentId,
                    e.Matter.Title,
                    e.Matter.ReferenceNumber,
                    WorkstreamName = e.Matter.Workstream != null ? e.Matter.Workstream.Name : null
                })
                .ToListAsync(ct);

            foreach (var e in matterEvents)
            {
                var summary = FormatMatterSummary(e.Action);
                allItems.Add(new ActivityItemDto(
                    EventId: e.Id,
                    SourceType: "Matter",
                    Action: e.Action.ToString(),
                    Summary: summary,
                    OccurredAt: e.ActionAt,
                    ActorUserId: e.ActionByUserId,
                    ActorDisplayName: e.ActionByDisplayNameSnapshot,
                    EntityType: "Matter",
                    EntityId: e.MatterId,
                    EntityTitle: e.Title,
                    EntityReferenceNumber: e.ReferenceNumber,
                    WorkstreamId: e.WorkstreamIdSnapshot ?? e.TargetWorkstreamId ?? e.SourceWorkstreamId,
                    WorkstreamName: e.WorkstreamName,
                    DeskId: null,
                    DeskName: null,
                    CanOpen: false,
                    NavigationUrl: null,
                    IsReadEvent: false,
                    Metadata: new { e.DocumentId, e.SourceWorkstreamId, e.TargetWorkstreamId }
                ));
            }
        }

        // --------------------------------------------------------------------
        // C. OUTWARD EVENTS
        // --------------------------------------------------------------------
        if (shouldIncludeOutward)
        {
            var q = db.OutwardEvents.AsNoTracking().Include(e => e.Outward).ThenInclude(o => o.IssuingDesk).Include(e => e.Outward).ThenInclude(o => o.Workstream).AsQueryable();

            if (!isTeamActivity)
            {
                q = q.Where(e => e.ActionByUserId == currentUserId);
            }
            else
            {
                if (query.ActorUserId.HasValue)
                    q = q.Where(e => e.ActionByUserId == query.ActorUserId.Value);

                if (!hasAllScope)
                {
                    // Match against historical snapshots; fail closed if snapshot is null
                    q = q.Where(e => (hasAssignedScope && e.IssuingDeskIdSnapshot.HasValue && callerDeskIds.Contains(e.IssuingDeskIdSnapshot.Value))
                                  || (hasWorkstreamScope && e.WorkstreamIdSnapshot.HasValue && callerWsIds.Contains(e.WorkstreamIdSnapshot.Value)));
                }
            }

            if (query.DateFrom.HasValue) q = q.Where(e => e.ActionAt >= query.DateFrom.Value);
            if (query.DateTo.HasValue) q = q.Where(e => e.ActionAt <= query.DateTo.Value);
            if (query.DeskId.HasValue) q = q.Where(e => e.IssuingDeskIdSnapshot == query.DeskId.Value);
            if (query.WorkstreamId.HasValue) q = q.Where(e => e.WorkstreamIdSnapshot == query.WorkstreamId.Value);
            if (!string.IsNullOrWhiteSpace(query.Action))
            {
                var actionStr = query.Action.Trim();
                q = q.Where(e => e.Action.ToString().ToLower() == actionStr.ToLower());
            }
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var s = query.Search.Trim().ToLower();
                q = q.Where(e => e.Outward.Subject.ToLower().Contains(s)
                              || e.Outward.OutwardNumber.ToLower().Contains(s)
                              || e.ActionByDisplayNameSnapshot.ToLower().Contains(s)
                              || (e.CancellationReason != null && e.CancellationReason.ToLower().Contains(s)));
            }

            var outwardCount = await q.CountAsync(ct);
            totalCount += outwardCount;

            var outwardEvents = await q.OrderByDescending(e => e.ActionAt)
                .ThenBy(e => e.Id)
                .Take(fetchCount)
                .Select(e => new
                {
                    e.Id,
                    e.OutwardId,
                    e.Action,
                    e.ActionByUserId,
                    e.ActionByDisplayNameSnapshot,
                    e.ActionAt,
                    e.IssuingDeskIdSnapshot,
                    e.WorkstreamIdSnapshot,
                    e.DispatchMode,
                    e.CancellationReason,
                    e.Outward.OutwardNumber,
                    e.Outward.Subject,
                    IssuingDeskName = e.Outward.IssuingDesk != null ? e.Outward.IssuingDesk.Name : null,
                    WorkstreamName = e.Outward.Workstream != null ? e.Outward.Workstream.Name : null
                })
                .ToListAsync(ct);

            foreach (var e in outwardEvents)
            {
                var summary = FormatOutwardSummary(e.Action, e.DispatchMode);
                allItems.Add(new ActivityItemDto(
                    EventId: e.Id,
                    SourceType: "Outward",
                    Action: e.Action.ToString(),
                    Summary: summary,
                    OccurredAt: e.ActionAt,
                    ActorUserId: e.ActionByUserId,
                    ActorDisplayName: e.ActionByDisplayNameSnapshot,
                    EntityType: "Outward",
                    EntityId: e.OutwardId,
                    EntityTitle: e.Subject,
                    EntityReferenceNumber: e.OutwardNumber,
                    WorkstreamId: e.WorkstreamIdSnapshot,
                    WorkstreamName: e.WorkstreamName,
                    DeskId: e.IssuingDeskIdSnapshot,
                    DeskName: e.IssuingDeskName,
                    CanOpen: false,
                    NavigationUrl: null,
                    IsReadEvent: false,
                    Metadata: new { e.DispatchMode, e.CancellationReason }
                ));
            }
        }

        // --------------------------------------------------------------------
        // D. WORK ITEM EVENTS
        // --------------------------------------------------------------------
        if (shouldIncludeWorkItem)
        {
            var q = db.WorkItemEvents.AsNoTracking().Include(e => e.WorkItem).ThenInclude(w => w.Workstream).AsQueryable();

            if (!isTeamActivity)
            {
                q = q.Where(e => e.ActionByUserId == currentUserId);
            }
            else
            {
                if (query.ActorUserId.HasValue)
                    q = q.Where(e => e.ActionByUserId == query.ActorUserId.Value);

                if (!hasAllScope)
                {
                    q = q.Where(e => (hasWorkstreamScope && callerWsIds.Contains(e.WorkItem.WorkstreamId))
                                  || (hasAssignedScope && ((e.SourceDeskId.HasValue && callerDeskIds.Contains(e.SourceDeskId.Value))
                                                        || (e.TargetDeskId.HasValue && callerDeskIds.Contains(e.TargetDeskId.Value))
                                                        || e.WorkItem.Assignments.Any(a => a.IsActive && a.RecordStatus == RecordStatus.Active && callerDeskIds.Contains(a.OfficeDeskId)))));
                }
            }

            if (query.DateFrom.HasValue) q = q.Where(e => e.ActionAt >= query.DateFrom.Value);
            if (query.DateTo.HasValue) q = q.Where(e => e.ActionAt <= query.DateTo.Value);
            if (query.DeskId.HasValue)
            {
                q = q.Where(e => (e.SourceDeskId.HasValue && e.SourceDeskId.Value == query.DeskId.Value)
                              || (e.TargetDeskId.HasValue && e.TargetDeskId.Value == query.DeskId.Value)
                              || e.WorkItem.Assignments.Any(a => a.IsActive && a.RecordStatus == RecordStatus.Active && a.OfficeDeskId == query.DeskId.Value));
            }
            if (query.WorkstreamId.HasValue) q = q.Where(e => e.WorkItem.WorkstreamId == query.WorkstreamId.Value);
            if (!string.IsNullOrWhiteSpace(query.Action))
            {
                var actionStr = query.Action.Trim();
                q = q.Where(e => e.Action.ToString().ToLower() == actionStr.ToLower());
            }
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var s = query.Search.Trim().ToLower();
                q = q.Where(e => e.WorkItem.Title.ToLower().Contains(s)
                              || e.ActionByDisplayNameSnapshot.ToLower().Contains(s)
                              || (e.RemarksSnapshot != null && e.RemarksSnapshot.ToLower().Contains(s)));
            }

            var wiCount = await q.CountAsync(ct);
            totalCount += wiCount;

            var wiEvents = await q.OrderByDescending(e => e.ActionAt)
                .ThenBy(e => e.Id)
                .Take(fetchCount)
                .Select(e => new
                {
                    e.Id,
                    e.WorkItemId,
                    e.Action,
                    e.ActionByUserId,
                    e.ActionByDisplayNameSnapshot,
                    e.ActionAt,
                    e.SourceDeskId,
                    e.SourceDeskNameSnapshot,
                    e.TargetDeskId,
                    e.TargetDeskNameSnapshot,
                    e.FromStatus,
                    e.ToStatus,
                    e.RemarksSnapshot,
                    e.WorkItem.Title,
                    e.WorkItem.WorkstreamId,
                    WorkstreamName = e.WorkItem.Workstream != null ? e.WorkItem.Workstream.Name : null
                })
                .ToListAsync(ct);

            foreach (var e in wiEvents)
            {
                var summary = FormatWorkItemSummary(e.Action, e.SourceDeskNameSnapshot, e.TargetDeskNameSnapshot, e.FromStatus, e.ToStatus);
                allItems.Add(new ActivityItemDto(
                    EventId: e.Id,
                    SourceType: "WorkItem",
                    Action: e.Action.ToString(),
                    Summary: summary,
                    OccurredAt: e.ActionAt,
                    ActorUserId: e.ActionByUserId,
                    ActorDisplayName: e.ActionByDisplayNameSnapshot,
                    EntityType: "WorkItem",
                    EntityId: e.WorkItemId,
                    EntityTitle: e.Title,
                    EntityReferenceNumber: null,
                    WorkstreamId: e.WorkstreamId,
                    WorkstreamName: e.WorkstreamName,
                    DeskId: e.TargetDeskId ?? e.SourceDeskId,
                    DeskName: e.TargetDeskNameSnapshot ?? e.SourceDeskNameSnapshot,
                    CanOpen: false,
                    NavigationUrl: null,
                    IsReadEvent: false,
                    Metadata: new { e.FromStatus, e.ToStatus, e.RemarksSnapshot }
                ));
            }
        }

        // --------------------------------------------------------------------
        // E. RECORD ACCESS EVENTS (Meaningful document reads)
        // --------------------------------------------------------------------
        if (shouldIncludeReads)
        {
            var q = db.RecordAccessEvents.AsNoTracking().Include(e => e.Document).Include(e => e.Workstream).Include(e => e.OfficeDesk).AsQueryable();

            if (!isTeamActivity)
            {
                q = q.Where(e => e.ActorUserId == currentUserId);
            }
            else
            {
                if (query.ActorUserId.HasValue)
                    q = q.Where(e => e.ActorUserId == query.ActorUserId.Value);

                if (!hasAllScope)
                {
                    q = q.Where(e => (hasWorkstreamScope && e.WorkstreamId.HasValue && callerWsIds.Contains(e.WorkstreamId.Value))
                                  || (hasAssignedScope && e.OfficeDeskId.HasValue && callerDeskIds.Contains(e.OfficeDeskId.Value)));
                }
            }

            if (query.DateFrom.HasValue) q = q.Where(e => e.OccurredAt >= query.DateFrom.Value);
            if (query.DateTo.HasValue) q = q.Where(e => e.OccurredAt <= query.DateTo.Value);
            if (query.WorkstreamId.HasValue) q = q.Where(e => e.WorkstreamId == query.WorkstreamId.Value);
            if (query.DeskId.HasValue) q = q.Where(e => e.OfficeDeskId == query.DeskId.Value);
            if (!string.IsNullOrWhiteSpace(query.Action))
            {
                var actionStr = query.Action.Trim();
                q = q.Where(e => e.Action.ToString().ToLower() == actionStr.ToLower());
            }
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var s = query.Search.Trim().ToLower();
                q = q.Where(e => (e.DocumentTitleSnapshot != null && e.DocumentTitleSnapshot.ToLower().Contains(s))
                              || e.ActorDisplayNameSnapshot.ToLower().Contains(s));
            }

            var readCount = await q.CountAsync(ct);
            totalCount += readCount;

            var readEvents = await q.OrderByDescending(e => e.OccurredAt)
                .ThenBy(e => e.Id)
                .Take(fetchCount)
                .Select(e => new
                {
                    e.Id,
                    e.DocumentId,
                    e.Action,
                    e.ActorUserId,
                    e.ActorDisplayNameSnapshot,
                    e.OccurredAt,
                    e.DocumentTitleSnapshot,
                    e.ContextEntityType,
                    e.ContextEntityId,
                    e.WorkstreamId,
                    WorkstreamName = e.Workstream != null ? e.Workstream.Name : null,
                    e.OfficeDeskId,
                    OfficeDeskName = e.OfficeDesk != null ? e.OfficeDesk.Name : null
                })
                .ToListAsync(ct);

            foreach (var e in readEvents)
            {
                var title = e.DocumentTitleSnapshot ?? "Document";
                var summary = FormatReadSummary(e.Action, title);
                allItems.Add(new ActivityItemDto(
                    EventId: e.Id,
                    SourceType: "RecordAccess",
                    Action: e.Action.ToString(),
                    Summary: summary,
                    OccurredAt: e.OccurredAt,
                    ActorUserId: e.ActorUserId,
                    ActorDisplayName: e.ActorDisplayNameSnapshot,
                    EntityType: "Document",
                    EntityId: e.DocumentId,
                    EntityTitle: title,
                    EntityReferenceNumber: null,
                    WorkstreamId: e.WorkstreamId,
                    WorkstreamName: e.WorkstreamName,
                    DeskId: e.OfficeDeskId,
                    DeskName: e.OfficeDeskName,
                    CanOpen: false,
                    NavigationUrl: null,
                    IsReadEvent: true,
                    Metadata: new { e.ContextEntityType, e.ContextEntityId }
                ));
            }
        }

        // --------------------------------------------------------------------
        // F. DETERMINISTIC MERGE-SORT & PAGE SLICING
        // --------------------------------------------------------------------
        var sortedItems = allItems
            .OrderByDescending(x => x.OccurredAt)
            .ThenBy(x => x.SourceType)
            .ThenBy(x => x.EventId)
            .Skip(skip)
            .Take(take)
            .ToList();

        // --------------------------------------------------------------------
        // G. NAVIGATION SECURITY EVALUATION (canOpen & navigationUrl)
        // --------------------------------------------------------------------
        var finalItems = new List<ActivityItemDto>(sortedItems.Count);

        foreach (var item in sortedItems)
        {
            var canOpen = false;
            string? navUrl = null;

            try
            {
                switch (item.SourceType)
                {
                    case "Dak":
                        canOpen = await dakAuth.CanAccessDakAsync(item.EntityId, PermissionCodes.DakView, currentUserId, ct);
                        if (canOpen) navUrl = $"/dak/{item.EntityId}";
                        break;

                    case "Matter":
                        canOpen = await matterAuth.CanAccessMatterAsync(item.EntityId, PermissionCodes.MatterView, currentUserId, ct);
                        if (canOpen) navUrl = $"/matters/{item.EntityId}";
                        break;

                    case "Outward":
                        canOpen = await outwardAuth.CanAccessOutwardAsync(item.EntityId, PermissionCodes.OutwardView, currentUserId, ct);
                        if (canOpen) navUrl = $"/outward/{item.EntityId}";
                        break;

                    case "WorkItem":
                        canOpen = await workItemAuth.CanAccessWorkItemAsync(item.EntityId, PermissionCodes.WorkItemView, currentUserId, ct);
                        if (canOpen) navUrl = $"/work/{item.EntityId}";
                        break;

                    case "RecordAccess":
                        // For document reads, evaluate context entity permission if available
                        if (item.Metadata != null)
                        {
                            var meta = (dynamic)item.Metadata;
                            string? cType = meta.ContextEntityType;
                            Guid? cId = meta.ContextEntityId;

                            if (cId.HasValue && !string.IsNullOrWhiteSpace(cType))
                            {
                                switch (cType.ToLowerInvariant())
                                {
                                    case "matter":
                                        canOpen = await matterAuth.CanAccessMatterAsync(cId.Value, PermissionCodes.MatterView, currentUserId, ct);
                                        if (canOpen) navUrl = $"/matters/{cId.Value}";
                                        break;
                                    case "dak":
                                        canOpen = await dakAuth.CanAccessDakAsync(cId.Value, PermissionCodes.DakView, currentUserId, ct);
                                        if (canOpen) navUrl = $"/dak/{cId.Value}";
                                        break;
                                    case "outward":
                                        canOpen = await outwardAuth.CanAccessOutwardAsync(cId.Value, PermissionCodes.OutwardView, currentUserId, ct);
                                        if (canOpen) navUrl = $"/outward/{cId.Value}";
                                        break;
                                    case "workitem":
                                        canOpen = await workItemAuth.CanAccessWorkItemAsync(cId.Value, PermissionCodes.WorkItemView, currentUserId, ct);
                                        if (canOpen) navUrl = $"/work/{cId.Value}";
                                        break;
                                }
                            }
                        }
                        break;
                }
            }
            catch
            {
                canOpen = false;
                navUrl = null;
            }

            finalItems.Add(item with { CanOpen = canOpen, NavigationUrl = navUrl });
        }

        var hasMore = totalCount > (skip + finalItems.Count);

        return new ActivityFeedResult(
            Items: finalItems,
            TotalCount: totalCount,
            Page: page,
            PageSize: pageSize,
            HasMore: hasMore
        );
    }

    // ========================================================================
    // FORMATTERS
    // ========================================================================
    private static string FormatDakSummary(DakMovementAction action, string? fromDesk, string? toDesk)
    {
        return action switch
        {
            DakMovementAction.Registered => $"Registered and marked to {toDesk ?? "desk"}",
            DakMovementAction.Marked => $"Marked from {fromDesk ?? "desk"} to {toDesk ?? "desk"}",
            DakMovementAction.Forwarded => $"Forwarded from {fromDesk ?? "desk"} to {toDesk ?? "desk"}",
            DakMovementAction.Returned => $"Returned to {toDesk ?? "desk"}",
            DakMovementAction.Disposed => "Marked Dak as completed/disposed",
            DakMovementAction.Cancelled => "Cancelled Dak registration",
            _ => $"{action} at {toDesk ?? fromDesk ?? "desk"}"
        };
    }

    private static string FormatMatterSummary(MatterEventAction action)
    {
        return action switch
        {
            MatterEventAction.Created => "Created matter",
            MatterEventAction.MetadataUpdated => "Updated matter metadata",
            MatterEventAction.WorkstreamReclassified => "Reclassified workstream",
            MatterEventAction.DocumentUploaded => "Uploaded document to matter",
            MatterEventAction.DocumentLinked => "Linked document to matter",
            MatterEventAction.Archived => "Archived matter",
            _ => action.ToString()
        };
    }

    private static string FormatOutwardSummary(OutwardEventAction action, string? dispatchMode)
    {
        return action switch
        {
            OutwardEventAction.Registered => "Registered outward letter",
            OutwardEventAction.MetadataUpdated => "Updated outward metadata",
            OutwardEventAction.MainDocumentChanged => "Updated main outward document",
            OutwardEventAction.AttachmentAdded => "Added enclosure to outward letter",
            OutwardEventAction.AttachmentRemoved => "Removed enclosure from outward letter",
            OutwardEventAction.DakLinkAdded => "Linked Dak to outward letter",
            OutwardEventAction.DakLinkRemoved => "Unlinked Dak from outward letter",
            OutwardEventAction.Dispatched => !string.IsNullOrEmpty(dispatchMode)
                ? $"Dispatched outward letter via {dispatchMode}"
                : "Dispatched outward letter",
            OutwardEventAction.Cancelled => "Cancelled outward letter",
            _ => action.ToString()
        };
    }

    private static string FormatWorkItemSummary(
        WorkItemEventAction action,
        string? sourceDesk,
        string? targetDesk,
        WorkItemStatus? fromStatus,
        WorkItemStatus? toStatus)
    {
        return action switch
        {
            WorkItemEventAction.Created => "Created work item",
            WorkItemEventAction.Assigned => $"Assigned to {targetDesk ?? "desk"}",
            WorkItemEventAction.Reassigned => $"Reassigned from {sourceDesk ?? "desk"} to {targetDesk ?? "desk"}",
            WorkItemEventAction.FirstSeen => "Opened work item for review",
            WorkItemEventAction.Started => "Started work item",
            WorkItemEventAction.MetadataUpdated => "Updated work item details",
            WorkItemEventAction.UpdateAdded => "Added progress note",
            WorkItemEventAction.AttachmentAdded => "Attached document",
            WorkItemEventAction.AttachmentRemoved => "Removed attachment",
            WorkItemEventAction.ContextLinked => "Linked reference context",
            WorkItemEventAction.ContextUnlinked => "Unlinked reference context",
            WorkItemEventAction.ContributorAdded => "Added contributor",
            WorkItemEventAction.ContributorRemoved => "Removed contributor",
            WorkItemEventAction.ContributorSubmitted => "Submitted contribution for review",
            WorkItemEventAction.ContributorAccepted => "Accepted contribution",
            WorkItemEventAction.ContributorReturned => "Returned contribution for correction",
            WorkItemEventAction.SubmittedForReview => "Submitted work item for review",
            WorkItemEventAction.ReturnedForCorrection => "Returned work item for correction",
            WorkItemEventAction.Approved => "Approved work item",
            WorkItemEventAction.Completed => "Completed work item",
            WorkItemEventAction.Cancelled => "Cancelled work item",
            _ => action.ToString()
        };
    }

    private static string FormatReadSummary(RecordAccessAction action, string title)
    {
        return action switch
        {
            RecordAccessAction.Opened => $"Opened '{title}'",
            RecordAccessAction.Previewed => $"Previewed '{title}'",
            RecordAccessAction.Downloaded => $"Downloaded '{title}'",
            _ => $"Accessed '{title}'"
        };
    }
}
