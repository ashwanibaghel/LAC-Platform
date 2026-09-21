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
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
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

public sealed record FilterOptionDto(Guid Id, string Name);

public sealed record TeamFilterOptionsDto(
    IReadOnlyList<FilterOptionDto> Workstreams,
    IReadOnlyList<FilterOptionDto> Desks,
    IReadOnlyList<FilterOptionDto> Actors
);

public interface IActivityProjectionService
{
    Task<ActivityFeedResult> GetMyHistoryAsync(Guid userId, ActivityQuery query, CancellationToken ct = default);
    Task<ActivityFeedResult> GetTeamActivityAsync(Guid userId, ActivityQuery query, CancellationToken ct = default);
    Task<TeamFilterOptionsDto> GetTeamFilterOptionsAsync(Guid userId, CancellationToken ct = default);
}

public sealed class ActivityProjectionService(
    LacDbContext db,
    IMatterAuthorizationService matterAuth,
    IDakAuthorizationService dakAuth,
    IOutwardAuthorizationService outwardAuth,
    IWorkItemAuthorizationService workItemAuth,
    IScheduleAuthorizationService scheduleAuth,
    ICourtAuthorizationService courtAuth) : IActivityProjectionService
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
    // 3. TEAM FILTER OPTIONS
    // ========================================================================
    public async Task<TeamFilterOptionsDto> GetTeamFilterOptionsAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null || !user.IsActive || user.RecordStatus != RecordStatus.Active)
            throw new ActivityAccessException("Inactive or unauthorized account.", 403);

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
            throw new ActivityAccessException("Team Activity requires All, Workstream, or Assigned scope for Audit.View. Own scope fails closed.", 403);
        }

        var hasAll = scopes.Contains(ScopeMode.All);
        var hasWorkstream = scopes.Contains(ScopeMode.Workstream);
        var hasAssigned = scopes.Contains(ScopeMode.Assigned);

        List<FilterOptionDto> workstreams = [];
        List<FilterOptionDto> desks = [];
        List<FilterOptionDto> actors = [];

        if (hasAll)
        {
            workstreams = await db.Workstreams.AsNoTracking()
                .Where(w => w.IsActive && w.RecordStatus == RecordStatus.Active)
                .OrderBy(w => w.Name)
                .Select(w => new FilterOptionDto(w.Id, w.Name))
                .ToListAsync(ct);

            desks = await db.OfficeDesks.AsNoTracking()
                .Where(d => d.IsActive && d.RecordStatus == RecordStatus.Active)
                .OrderBy(d => d.Name)
                .Select(d => new FilterOptionDto(d.Id, d.Name))
                .ToListAsync(ct);

            actors = await db.AppUsers.AsNoTracking()
                .Where(u => u.IsActive && u.RecordStatus == RecordStatus.Active)
                .OrderBy(u => u.DisplayName)
                .Select(u => new FilterOptionDto(u.Id, u.DisplayName))
                .ToListAsync(ct);
        }
        else
        {
            List<Guid> callerWsIds = [];
            if (hasWorkstream)
            {
                callerWsIds = await db.UserWorkstreamMemberships.AsNoTracking()
                    .Where(m => m.UserId == userId && m.IsActive && m.Workstream.IsActive && m.Workstream.RecordStatus == RecordStatus.Active)
                    .Select(m => m.WorkstreamId)
                    .ToListAsync(ct);
            }

            List<Guid> callerDeskIds = [];
            if (hasAssigned)
            {
                callerDeskIds = await db.UserDeskMemberships.AsNoTracking()
                    .Where(m => m.UserId == userId && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active && m.OfficeDesk.IsActive && m.OfficeDesk.RecordStatus == RecordStatus.Active)
                    .Select(m => m.OfficeDeskId)
                    .ToListAsync(ct);
            }

            var wsQuery = db.Workstreams.AsNoTracking()
                .Where(w => w.IsActive && w.RecordStatus == RecordStatus.Active
                         && (callerWsIds.Contains(w.Id) || db.OfficeDesks.Any(d => callerDeskIds.Contains(d.Id) && d.WorkstreamId == w.Id)));

            workstreams = await wsQuery
                .OrderBy(w => w.Name)
                .Select(w => new FilterOptionDto(w.Id, w.Name))
                .ToListAsync(ct);

            var deskQuery = db.OfficeDesks.AsNoTracking()
                .Where(d => d.IsActive && d.RecordStatus == RecordStatus.Active
                         && (callerDeskIds.Contains(d.Id) || (d.WorkstreamId.HasValue && callerWsIds.Contains(d.WorkstreamId.Value))));

            desks = await deskQuery
                .OrderBy(d => d.Name)
                .Select(d => new FilterOptionDto(d.Id, d.Name))
                .ToListAsync(ct);

            var actorQuery = db.AppUsers.AsNoTracking()
                .Where(u => u.IsActive && u.RecordStatus == RecordStatus.Active
                         && (db.UserWorkstreamMemberships.Any(wm => wm.UserId == u.Id && wm.IsActive && callerWsIds.Contains(wm.WorkstreamId))
                             || db.UserDeskMemberships.Any(dm => dm.UserId == u.Id && dm.IsActive && dm.RemovedAt == null && dm.RecordStatus == RecordStatus.Active && callerDeskIds.Contains(dm.OfficeDeskId))));

            actors = await actorQuery
                .OrderBy(u => u.DisplayName)
                .Select(u => new FilterOptionDto(u.Id, u.DisplayName))
                .ToListAsync(ct);
        }

        return new TeamFilterOptionsDto(workstreams, desks, actors);
    }

    // ========================================================================
    // 4. CORE UNIFIED PROJECTION ENGINE
    // ========================================================================
    private async Task<ActivityFeedResult> QueryFeedAsync(
        Guid currentUserId,
        ActivityQuery query,
        bool isTeamActivity,
        CancellationToken ct,
        List<ScopeMode>? auditScopes = null)
    {
        var page = Math.Clamp(query.Page, 1, 100);
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
                    .Where(m => m.UserId == currentUserId
                             && m.IsActive
                             && m.Workstream.IsActive
                             && m.Workstream.RecordStatus == RecordStatus.Active)
                    .Select(m => m.WorkstreamId)
                    .ToListAsync(ct);
            }

            if (hasAssignedScope)
            {
                callerDeskIds = await db.UserDeskMemberships.AsNoTracking()
                    .Where(m => m.UserId == currentUserId
                             && m.IsActive
                             && m.RemovedAt == null
                             && m.RecordStatus == RecordStatus.Active
                             && m.OfficeDesk.IsActive
                             && m.OfficeDesk.RecordStatus == RecordStatus.Active)
                    .Select(m => m.OfficeDeskId)
                    .ToListAsync(ct);
            }
        }

        var delhiOffset = TimeSpan.FromHours(5.5);
        DateTimeOffset? dateFrom = query.FromDate.HasValue
            ? new DateTimeOffset(query.FromDate.Value.ToDateTime(TimeOnly.MinValue), delhiOffset).ToUniversalTime()
            : null;
        DateTimeOffset? dateToExclusive = query.ToDate.HasValue
            ? new DateTimeOffset(query.ToDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), delhiOffset).ToUniversalTime()
            : null;

        var normalizedEntityType = query.EntityType?.Trim().ToLowerInvariant();
        var shouldIncludeDak = string.IsNullOrEmpty(normalizedEntityType) || normalizedEntityType == "dak";
        var shouldIncludeMatter = string.IsNullOrEmpty(normalizedEntityType) || normalizedEntityType == "matter";
        var shouldIncludeOutward = string.IsNullOrEmpty(normalizedEntityType) || normalizedEntityType == "outward";
        var shouldIncludeWorkItem = string.IsNullOrEmpty(normalizedEntityType) || normalizedEntityType == "workitem" || normalizedEntityType == "work";
        var shouldIncludeSchedule = string.IsNullOrEmpty(normalizedEntityType) || normalizedEntityType == "schedule" || normalizedEntityType == "event" || normalizedEntityType == "scheduled_event" || normalizedEntityType == "scheduledevent";
        var shouldIncludeCourt = string.IsNullOrEmpty(normalizedEntityType) || normalizedEntityType == "court" || normalizedEntityType == "courtcase" || normalizedEntityType == "litigation";
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
                    var wsMatch = hasWorkstreamScope && callerWsIds.Count > 0;
                    var deskMatch = hasAssignedScope && callerDeskIds.Count > 0;

                    if (wsMatch && deskMatch)
                    {
                        q = q.Where(m => (m.WorkstreamIdSnapshot.HasValue && callerWsIds.Contains(m.WorkstreamIdSnapshot.Value))
                                      || (m.FromDeskId.HasValue && callerDeskIds.Contains(m.FromDeskId.Value))
                                      || (m.ToDeskId.HasValue && callerDeskIds.Contains(m.ToDeskId.Value)));
                    }
                    else if (wsMatch)
                    {
                        q = q.Where(m => m.WorkstreamIdSnapshot.HasValue && callerWsIds.Contains(m.WorkstreamIdSnapshot.Value));
                    }
                    else if (deskMatch)
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

            if (dateFrom.HasValue) q = q.Where(m => m.ActionAt >= dateFrom.Value);
            if (dateToExclusive.HasValue) q = q.Where(m => m.ActionAt < dateToExclusive.Value);
            if (query.WorkstreamId.HasValue) q = q.Where(m => m.WorkstreamIdSnapshot == query.WorkstreamId.Value);
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
                    m.Dak.Subject,
                    m.WorkstreamIdSnapshot,
                    m.WorkstreamNameSnapshot
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
                    WorkstreamId: m.WorkstreamIdSnapshot,
                    WorkstreamName: m.WorkstreamNameSnapshot,
                    DeskId: m.ToDeskId ?? m.FromDeskId,
                    DeskName: m.ToDeskNameSnapshot ?? m.FromDeskNameSnapshot,
                    CanOpen: false,
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
            var q = db.MatterEvents.AsNoTracking().Include(e => e.Matter).AsQueryable();

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

            if (dateFrom.HasValue) q = q.Where(e => e.ActionAt >= dateFrom.Value);
            if (dateToExclusive.HasValue) q = q.Where(e => e.ActionAt < dateToExclusive.Value);
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
                    e.WorkstreamNameSnapshot,
                    e.SourceWorkstreamId,
                    e.SourceWorkstreamNameSnapshot,
                    e.TargetWorkstreamId,
                    e.TargetWorkstreamNameSnapshot,
                    e.DocumentId,
                    e.Matter.Title,
                    e.Matter.ReferenceNumber
                })
                .ToListAsync(ct);

            foreach (var e in matterEvents)
            {
                var summary = FormatMatterSummary(e.Action);
                var wsId = e.WorkstreamIdSnapshot ?? e.TargetWorkstreamId ?? e.SourceWorkstreamId;
                var wsName = e.WorkstreamNameSnapshot ?? e.TargetWorkstreamNameSnapshot ?? e.SourceWorkstreamNameSnapshot;

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
                    WorkstreamId: wsId,
                    WorkstreamName: wsName,
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
            var q = db.OutwardEvents.AsNoTracking().Include(e => e.Outward).AsQueryable();

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
                    var wsMatch = hasWorkstreamScope && callerWsIds.Count > 0;
                    var deskMatch = hasAssignedScope && callerDeskIds.Count > 0;

                    if (wsMatch && deskMatch)
                    {
                        q = q.Where(e => (e.IssuingDeskIdSnapshot.HasValue && callerDeskIds.Contains(e.IssuingDeskIdSnapshot.Value))
                                      || (e.WorkstreamIdSnapshot.HasValue && callerWsIds.Contains(e.WorkstreamIdSnapshot.Value)));
                    }
                    else if (deskMatch)
                    {
                        q = q.Where(e => e.IssuingDeskIdSnapshot.HasValue && callerDeskIds.Contains(e.IssuingDeskIdSnapshot.Value));
                    }
                    else if (wsMatch)
                    {
                        q = q.Where(e => e.WorkstreamIdSnapshot.HasValue && callerWsIds.Contains(e.WorkstreamIdSnapshot.Value));
                    }
                    else
                    {
                        q = q.Where(_ => false);
                    }
                }
            }

            if (dateFrom.HasValue) q = q.Where(e => e.ActionAt >= dateFrom.Value);
            if (dateToExclusive.HasValue) q = q.Where(e => e.ActionAt < dateToExclusive.Value);
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
                    e.IssuingDeskNameSnapshot,
                    e.WorkstreamIdSnapshot,
                    e.WorkstreamNameSnapshot,
                    e.DispatchMode,
                    e.CancellationReason,
                    e.Outward.OutwardNumber,
                    e.Outward.Subject
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
                    WorkstreamName: e.WorkstreamNameSnapshot,
                    DeskId: e.IssuingDeskIdSnapshot,
                    DeskName: e.IssuingDeskNameSnapshot,
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
                    var wsMatch = hasWorkstreamScope && callerWsIds.Count > 0;
                    var deskMatch = hasAssignedScope && callerDeskIds.Count > 0;

                    if (wsMatch && deskMatch)
                    {
                        q = q.Where(e => (callerWsIds.Contains(e.WorkItem.WorkstreamId))
                                      || (e.SourceDeskId.HasValue && callerDeskIds.Contains(e.SourceDeskId.Value))
                                      || (e.TargetDeskId.HasValue && callerDeskIds.Contains(e.TargetDeskId.Value))
                                      || e.WorkItem.Assignments.Any(a => a.RecordStatus == RecordStatus.Active
                                                                      && a.AssignedAt <= e.ActionAt
                                                                      && (a.ClosedAt == null || e.ActionAt < a.ClosedAt)
                                                                      && callerDeskIds.Contains(a.OfficeDeskId)));
                    }
                    else if (wsMatch)
                    {
                        q = q.Where(e => callerWsIds.Contains(e.WorkItem.WorkstreamId));
                    }
                    else if (deskMatch)
                    {
                        q = q.Where(e => (e.SourceDeskId.HasValue && callerDeskIds.Contains(e.SourceDeskId.Value))
                                      || (e.TargetDeskId.HasValue && callerDeskIds.Contains(e.TargetDeskId.Value))
                                      || e.WorkItem.Assignments.Any(a => a.RecordStatus == RecordStatus.Active
                                                                      && a.AssignedAt <= e.ActionAt
                                                                      && (a.ClosedAt == null || e.ActionAt < a.ClosedAt)
                                                                      && callerDeskIds.Contains(a.OfficeDeskId)));
                    }
                    else
                    {
                        q = q.Where(_ => false);
                    }
                }
            }

            if (dateFrom.HasValue) q = q.Where(e => e.ActionAt >= dateFrom.Value);
            if (dateToExclusive.HasValue) q = q.Where(e => e.ActionAt < dateToExclusive.Value);
            if (query.DeskId.HasValue)
            {
                q = q.Where(e => (e.SourceDeskId.HasValue && e.SourceDeskId.Value == query.DeskId.Value)
                              || (e.TargetDeskId.HasValue && e.TargetDeskId.Value == query.DeskId.Value)
                              || e.WorkItem.Assignments.Any(a => a.RecordStatus == RecordStatus.Active
                                                              && a.AssignedAt <= e.ActionAt
                                                              && (a.ClosedAt == null || e.ActionAt < a.ClosedAt)
                                                              && a.OfficeDeskId == query.DeskId.Value));
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
                    HistoricalDesk = e.WorkItem.Assignments
                        .Where(a => a.RecordStatus == RecordStatus.Active && a.AssignedAt <= e.ActionAt && (a.ClosedAt == null || e.ActionAt < a.ClosedAt))
                        .Select(a => new { a.OfficeDeskId })
                        .FirstOrDefault(),
                    e.FromStatus,
                    e.ToStatus,
                    e.RemarksSnapshot,
                    e.WorkItem.Title,
                    e.WorkItem.WorkstreamId
                })
                .ToListAsync(ct);

            foreach (var e in wiEvents)
            {
                var summary = FormatWorkItemSummary(e.Action, e.SourceDeskNameSnapshot, e.TargetDeskNameSnapshot, e.FromStatus, e.ToStatus);
                var deskId = e.TargetDeskId ?? e.SourceDeskId ?? e.HistoricalDesk?.OfficeDeskId;
                var deskName = e.TargetDeskNameSnapshot ?? e.SourceDeskNameSnapshot;

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
                    WorkstreamName: null,
                    DeskId: deskId,
                    DeskName: deskName,
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
            var q = db.RecordAccessEvents.AsNoTracking().Include(e => e.Document).AsQueryable();

            var canViewCourtReads = await courtAuth.CanViewCourtReferencesAsync(currentUserId, ct);
            if (!canViewCourtReads)
            {
                q = q.Where(e => e.ContextEntityType != "CourtCase");
            }

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
                    var wsMatch = hasWorkstreamScope && callerWsIds.Count > 0;
                    var deskMatch = hasAssignedScope && callerDeskIds.Count > 0;

                    if (wsMatch && deskMatch)
                    {
                        q = q.Where(e => (e.WorkstreamId.HasValue && callerWsIds.Contains(e.WorkstreamId.Value))
                                      || (e.OfficeDeskId.HasValue && callerDeskIds.Contains(e.OfficeDeskId.Value)));
                    }
                    else if (wsMatch)
                    {
                        q = q.Where(e => e.WorkstreamId.HasValue && callerWsIds.Contains(e.WorkstreamId.Value));
                    }
                    else if (deskMatch)
                    {
                        q = q.Where(e => e.OfficeDeskId.HasValue && callerDeskIds.Contains(e.OfficeDeskId.Value));
                    }
                    else
                    {
                        q = q.Where(_ => false);
                    }
                }
            }

            if (dateFrom.HasValue) q = q.Where(e => e.OccurredAt >= dateFrom.Value);
            if (dateToExclusive.HasValue) q = q.Where(e => e.OccurredAt < dateToExclusive.Value);
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
                    e.WorkstreamNameSnapshot,
                    e.OfficeDeskId,
                    e.OfficeDeskNameSnapshot
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
                    WorkstreamName: e.WorkstreamNameSnapshot,
                    DeskId: e.OfficeDeskId,
                    DeskName: e.OfficeDeskNameSnapshot,
                    CanOpen: false,
                    NavigationUrl: null,
                    IsReadEvent: true,
                    Metadata: new { e.ContextEntityType, e.ContextEntityId }
                ));
            }
        }

        // --------------------------------------------------------------------
        // F. SCHEDULED EVENT EVENTS
        // --------------------------------------------------------------------
        if (shouldIncludeSchedule)
        {
            var q = db.ScheduledEventEvents.AsNoTracking().Include(e => e.ScheduledEvent).AsQueryable();

            if (!isTeamActivity)
            {
                q = q.Where(e => e.ActorUserId == currentUserId);
            }
            else
            {
                q = q.Where(e => e.Action != ScheduledEventAction.ReminderAdded && e.Action != ScheduledEventAction.ReminderRemoved);

                var canViewCourt = await courtAuth.CanViewCourtReferencesAsync(currentUserId, ct);
                if (!canViewCourt)
                {
                    q = q.Where(e => e.ScheduledEvent.Origin != ScheduledEventOrigin.CourtProceeding
                                  && e.ScheduledEvent.CourtCaseId == null
                                  && e.ScheduledEvent.CourtProceedingId == null);
                }

                if (query.ActorUserId.HasValue)
                    q = q.Where(e => e.ActorUserId == query.ActorUserId.Value);

                if (!hasAllScope)
                {
                    var wsMatch = hasWorkstreamScope && callerWsIds.Count > 0;
                    var deskMatch = hasAssignedScope && callerDeskIds.Count > 0;

                    if (wsMatch && deskMatch)
                    {
                        q = q.Where(e => (e.WorkstreamIdSnapshot.HasValue && callerWsIds.Contains(e.WorkstreamIdSnapshot.Value))
                                      || (e.TargetDeskId.HasValue && callerDeskIds.Contains(e.TargetDeskId.Value))
                                      || (e.SourceDeskId.HasValue && callerDeskIds.Contains(e.SourceDeskId.Value)));
                    }
                    else if (wsMatch)
                    {
                        q = q.Where(e => e.WorkstreamIdSnapshot.HasValue && callerWsIds.Contains(e.WorkstreamIdSnapshot.Value));
                    }
                    else if (deskMatch)
                    {
                        q = q.Where(e => (e.TargetDeskId.HasValue && callerDeskIds.Contains(e.TargetDeskId.Value))
                                      || (e.SourceDeskId.HasValue && callerDeskIds.Contains(e.SourceDeskId.Value)));
                    }
                    else
                    {
                        q = q.Where(_ => false);
                    }
                }
            }

            if (dateFrom.HasValue) q = q.Where(e => e.ActionAt >= dateFrom.Value);
            if (dateToExclusive.HasValue) q = q.Where(e => e.ActionAt < dateToExclusive.Value);
            if (query.DeskId.HasValue) q = q.Where(e => e.TargetDeskId == query.DeskId.Value || e.SourceDeskId == query.DeskId.Value);
            if (query.WorkstreamId.HasValue) q = q.Where(e => e.WorkstreamIdSnapshot == query.WorkstreamId.Value);
            if (!string.IsNullOrWhiteSpace(query.Action))
            {
                var actionStr = query.Action.Trim();
                q = q.Where(e => e.Action.ToString().ToLower() == actionStr.ToLower());
            }
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var s = query.Search.Trim().ToLower();
                q = q.Where(e => e.ScheduledEvent.Title.ToLower().Contains(s)
                              || e.ActorDisplayNameSnapshot.ToLower().Contains(s)
                              || (e.Reason != null && e.Reason.ToLower().Contains(s))
                              || (e.Notes != null && e.Notes.ToLower().Contains(s)));
            }

            var scheduleCount = await q.CountAsync(ct);
            totalCount += scheduleCount;

            var scheduleEvents = await q.OrderByDescending(e => e.ActionAt)
                .ThenBy(e => e.Id)
                .Take(fetchCount)
                .Select(e => new
                {
                    e.Id,
                    e.ScheduledEventId,
                    e.Action,
                    e.ActorUserId,
                    e.ActorDisplayNameSnapshot,
                    e.ActionAt,
                    e.WorkstreamIdSnapshot,
                    e.WorkstreamNameSnapshot,
                    e.SourceDeskId,
                    e.SourceDeskNameSnapshot,
                    e.TargetDeskId,
                    e.TargetDeskNameSnapshot,
                    e.OldScheduledDate,
                    e.OldScheduledTime,
                    e.NewScheduledDate,
                    e.NewScheduledTime,
                    e.ReminderDaysBefore,
                    e.Reason,
                    e.Notes,
                    e.ScheduledEvent.Title,
                    e.ScheduledEvent.EventKind
                })
                .ToListAsync(ct);

            foreach (var e in scheduleEvents)
            {
                var summary = FormatScheduleSummary(e.Action, e.EventKind, e.OldScheduledDate, e.NewScheduledDate, e.SourceDeskNameSnapshot, e.TargetDeskNameSnapshot, e.ReminderDaysBefore, e.Reason);
                var deskId = e.TargetDeskId ?? e.SourceDeskId;
                var deskName = e.TargetDeskNameSnapshot ?? e.SourceDeskNameSnapshot;

                allItems.Add(new ActivityItemDto(
                    EventId: e.Id,
                    SourceType: "Schedule",
                    Action: e.Action.ToString(),
                    Summary: summary,
                    OccurredAt: e.ActionAt,
                    ActorUserId: e.ActorUserId,
                    ActorDisplayName: e.ActorDisplayNameSnapshot,
                    EntityType: "ScheduledEvent",
                    EntityId: e.ScheduledEventId,
                    EntityTitle: e.Title,
                    EntityReferenceNumber: null,
                    WorkstreamId: e.WorkstreamIdSnapshot,
                    WorkstreamName: e.WorkstreamNameSnapshot,
                    DeskId: deskId,
                    DeskName: deskName,
                    CanOpen: false,
                    NavigationUrl: null,
                    IsReadEvent: false,
                    Metadata: new { e.OldScheduledDate, e.NewScheduledDate, e.Reason }
                ));
            }
        }

        // --------------------------------------------------------------------
        // G. COURT CASE EVENTS
        // --------------------------------------------------------------------
        if (shouldIncludeCourt)
        {
            var canViewCourtFeed = await courtAuth.CanViewCourtReferencesAsync(currentUserId, ct);
            if (canViewCourtFeed)
            {
                var q = db.CourtCaseEvents.AsNoTracking().Include(e => e.CourtCase).AsQueryable();

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
                        var wsMatch = hasWorkstreamScope && callerWsIds.Count > 0;
                        var deskMatch = hasAssignedScope && callerDeskIds.Count > 0;

                        if (wsMatch && deskMatch)
                        {
                            q = q.Where(e => (e.WorkstreamIdSnapshot.HasValue && callerWsIds.Contains(e.WorkstreamIdSnapshot.Value))
                                          || (e.TargetDeskId.HasValue && callerDeskIds.Contains(e.TargetDeskId.Value))
                                          || (e.SourceDeskId.HasValue && callerDeskIds.Contains(e.SourceDeskId.Value)));
                        }
                        else if (wsMatch)
                        {
                            q = q.Where(e => e.WorkstreamIdSnapshot.HasValue && callerWsIds.Contains(e.WorkstreamIdSnapshot.Value));
                        }
                        else if (deskMatch)
                        {
                            q = q.Where(e => (e.TargetDeskId.HasValue && callerDeskIds.Contains(e.TargetDeskId.Value))
                                          || (e.SourceDeskId.HasValue && callerDeskIds.Contains(e.SourceDeskId.Value)));
                        }
                        else
                        {
                            q = q.Where(_ => false);
                        }
                    }
                }

                if (dateFrom.HasValue) q = q.Where(e => e.ActionAt >= dateFrom.Value);
                if (dateToExclusive.HasValue) q = q.Where(e => e.ActionAt < dateToExclusive.Value);
                if (query.DeskId.HasValue) q = q.Where(e => e.TargetDeskId == query.DeskId.Value || e.SourceDeskId == query.DeskId.Value);
                if (query.WorkstreamId.HasValue) q = q.Where(e => e.WorkstreamIdSnapshot == query.WorkstreamId.Value);
                if (!string.IsNullOrWhiteSpace(query.Action))
                {
                    var actionStr = query.Action.Trim();
                    q = q.Where(e => e.Action.ToString().ToLower() == actionStr.ToLower());
                }
                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    var s = query.Search.Trim().ToLower();
                    q = q.Where(e => e.CourtCase.CaseNumber.ToLower().Contains(s)
                                  || (e.CourtCase.CaseTitle != null && e.CourtCase.CaseTitle.ToLower().Contains(s))
                                  || e.ActorDisplayNameSnapshot.ToLower().Contains(s)
                                  || (e.Reason != null && e.Reason.ToLower().Contains(s))
                                  || (e.Notes != null && e.Notes.ToLower().Contains(s)));
                }

                var courtCount = await q.CountAsync(ct);
                totalCount += courtCount;

                var courtEvents = await q.OrderByDescending(e => e.ActionAt)
                    .ThenBy(e => e.Id)
                    .Take(fetchCount)
                    .Select(e => new
                    {
                        e.Id,
                        e.CourtCaseId,
                        e.Action,
                        e.ActorUserId,
                        e.ActorDisplayNameSnapshot,
                        e.ActionAt,
                        e.WorkstreamIdSnapshot,
                        e.WorkstreamNameSnapshot,
                        e.SourceDeskId,
                        e.SourceDeskNameSnapshot,
                        e.TargetDeskId,
                        e.TargetDeskNameSnapshot,
                        e.CaseNumberSnapshot,
                        e.CaseTitleSnapshot,
                        e.Reason,
                        e.Notes
                    })
                    .ToListAsync(ct);

                foreach (var e in courtEvents)
                {
                    var title = !string.IsNullOrWhiteSpace(e.CaseTitleSnapshot) ? e.CaseTitleSnapshot : e.CaseNumberSnapshot;
                    var summary = $"Court Case {e.Action}: {title}";
                    var deskId = e.TargetDeskId ?? e.SourceDeskId;
                    var deskName = e.TargetDeskNameSnapshot ?? e.SourceDeskNameSnapshot;

                    allItems.Add(new ActivityItemDto(
                        EventId: e.Id,
                        SourceType: "CourtCase",
                        Action: e.Action.ToString(),
                        Summary: summary,
                        OccurredAt: e.ActionAt,
                        ActorUserId: e.ActorUserId,
                        ActorDisplayName: e.ActorDisplayNameSnapshot,
                        EntityType: "CourtCase",
                        EntityId: e.CourtCaseId,
                        EntityTitle: title,
                        EntityReferenceNumber: e.CaseNumberSnapshot,
                        WorkstreamId: e.WorkstreamIdSnapshot,
                        WorkstreamName: e.WorkstreamNameSnapshot,
                        DeskId: deskId,
                        DeskName: deskName,
                        CanOpen: false,
                        NavigationUrl: null,
                        IsReadEvent: false,
                        Metadata: new { e.Reason, e.Notes }
                    ));
                }
            }
        }

        // --------------------------------------------------------------------
        // H. DETERMINISTIC MERGE-SORT & PAGE SLICING
        // --------------------------------------------------------------------
        var sortedItems = allItems
            .OrderByDescending(x => x.OccurredAt)
            .ThenBy(x => x.SourceType)
            .ThenBy(x => x.EventId)
            .Skip(skip)
            .Take(take)
            .ToList();

        // --------------------------------------------------------------------
        // H. NAVIGATION SECURITY EVALUATION (canOpen & navigationUrl)
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

                    case "Schedule":
                        canOpen = await scheduleAuth.CanAccessScheduledEventAsync(item.EntityId, PermissionCodes.ScheduleView, currentUserId, ct);
                        if (canOpen) navUrl = $"/calendar?eventId={item.EntityId}";
                        break;

                    case "CourtCase":
                        canOpen = await courtAuth.CanViewCourtCaseAsync(item.EntityId, currentUserId, ct);
                        if (canOpen) navUrl = $"/court-cases/{item.EntityId}";
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
                                    case "courtcase":
                                        canOpen = await courtAuth.CanViewCourtCaseAsync(cId.Value, currentUserId, ct);
                                        if (canOpen) navUrl = $"/court-cases/{cId.Value}";
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

    private static string FormatScheduleSummary(
        ScheduledEventAction action,
        ScheduledEventKind kind,
        DateOnly? oldDate,
        DateOnly? newDate,
        string? sourceDeskName,
        string? targetDeskName,
        int? reminderDaysBefore,
        string? reason)
    {
        return action switch
        {
            ScheduledEventAction.Created => newDate.HasValue
                ? (kind == ScheduledEventKind.CourtHearing
                    ? $"Court hearing scheduled for {newDate.Value:dd MMM yyyy}"
                    : $"{kind} scheduled for {newDate.Value:dd MMM yyyy}")
                : "Scheduled obligation created",

            ScheduledEventAction.Rescheduled => (oldDate.HasValue && newDate.HasValue)
                ? $"Hearing rescheduled from {oldDate.Value:dd MMM yyyy} to {newDate.Value:dd MMM yyyy}"
                : "Scheduled obligation rescheduled",

            ScheduledEventAction.ResponsibilityChanged => $"Responsibility moved from {sourceDeskName ?? "Unassigned"} to {targetDeskName ?? "Unassigned"}",

            ScheduledEventAction.ReminderAdded => $"Reminder added for {reminderDaysBefore ?? 0} days before",

            ScheduledEventAction.ReminderRemoved => $"Reminder removed for {reminderDaysBefore ?? 0} days before",

            ScheduledEventAction.WorkItemLinked => "Work item linked to scheduled event",

            ScheduledEventAction.WorkItemUnlinked => "Work item unlinked from scheduled event",

            ScheduledEventAction.Completed => "Scheduled obligation completed",

            ScheduledEventAction.Cancelled => !string.IsNullOrWhiteSpace(reason)
                ? $"Scheduled obligation cancelled: {reason}"
                : "Scheduled obligation cancelled",

            _ => "Schedule updated"
        };
    }
}
