namespace LAC.Infrastructure;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed record CourtCaseFilterQuery(
    string? Search = null,
    string? CourtName = null,
    string? CurrentStatus = null,
    DateOnly? FiledFrom = null,
    DateOnly? FiledTo = null,
    Guid? DeskId = null,
    Guid? AssignedUserId = null,
    Guid? AwardId = null,
    Guid? VillageId = null,
    int Page = 1,
    int PageSize = 25
);

public sealed record CourtCaseSummaryDto(
    Guid Id,
    string CaseNumber,
    string CourtName,
    string? CaseTitle,
    string? CaseType,
    DateOnly? FiledDate,
    string? CurrentStatus,
    DateOnly? DisposedDate,
    Guid? ResponsibleOfficeDeskId,
    string? ResponsibleOfficeDeskName,
    Guid? AssignedUserId,
    string? AssignedUserDisplayName,
    DateOnly? AuthoritativeNextDate,
    DateOnly? ActiveScheduleNextDate,
    bool IsProjectedToCalendar,
    Guid? ActiveScheduledEventId,
    DateOnly? NextHearingDate,
    DateOnly? LastHearingDate,
    int? AwardsCount,
    int? KhasrasCount,
    int? MattersCount,
    int PartiesCount,
    int DocumentsCount,
    int ProceedingsCount,
    DateTimeOffset? LastActivityAt
);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public sealed record CourtCaseLinkedAwardDto(
    Guid AwardId,
    string AwardNumber,
    string? ProjectName,
    IReadOnlyList<string> VillageNames
);

public sealed record CourtCaseLinkedKhasraDto(
    Guid KhasraId,
    Guid VillageId,
    string VillageName,
    string NormalizedNumber,
    string? Qualifier,
    decimal? RecordedArea,
    string? AreaUnit
);

public sealed record CourtCaseLinkedMatterDto(
    Guid MatterId,
    string Title,
    string? ReferenceNumber,
    string Status,
    string? WorkstreamName,
    int DraftsCount,
    int WorkItemsCount
);

public sealed record CourtCaseCapabilitiesDto(
    bool CanEdit,
    bool CanAssign,
    bool CanManageProceedings,
    bool CanManageDocuments,
    bool CanPromoteToCalendar,
    bool CanLinkAward,
    bool CanLinkKhasra,
    bool CanLinkMatter
);

public sealed record CourtCaseDetailDto(
    Guid Id,
    string CaseNumber,
    string CourtName,
    string? CaseTitle,
    string? CaseType,
    DateOnly? FiledDate,
    string? CurrentStatus,
    DateOnly? DisposedDate,
    string? Remarks,
    int Revision,
    Guid? ResponsibleOfficeDeskId,
    string? ResponsibleOfficeDeskName,
    Guid? AssignedUserId,
    string? AssignedUserDisplayName,
    DateOnly? AuthoritativeNextDate,
    DateOnly? ActiveScheduleNextDate,
    bool IsProjectedToCalendar,
    Guid? ActiveScheduledEventId,
    DateOnly? NextHearingDate,
    DateOnly? LastHearingDate,
    string? LastOrderType,
    string? RestraintNature,
    string? LastSummary,
    IReadOnlyList<CourtCaseLinkedAwardDto> Awards,
    IReadOnlyList<CourtCaseLinkedKhasraDto> Khasras,
    IReadOnlyList<CourtCaseLinkedMatterDto> Matters,
    IReadOnlyList<CourtCasePartyDto> Parties,
    IReadOnlyList<CourtCaseRepresentativeDto> Representatives,
    int? AwardsCount,
    int? KhasrasCount,
    int? MattersCount,
    int PartiesCount,
    int DocumentsCount,
    int ProceedingsCount,
    int EventsCount,
    CourtCaseCapabilitiesDto Capabilities
);

public sealed record CourtCaseTimelineEventDto(
    Guid Id,
    int SequenceNumber,
    string Action,
    DateTimeOffset ActionAt,
    Guid ActorUserId,
    string ActorDisplayName,
    string? ActorDesignation,
    string? SourceDeskName,
    string? TargetDeskName,
    string? SourceUserName,
    string? TargetUserName,
    string? OldStatus,
    string? NewStatus,
    string? Reason,
    string? Notes
);

public sealed record CourtFilterOptionDto(
    Guid Id,
    string Name,
    string? WorkstreamName = null
)
{
    public string DisplayName => Name;
}

public sealed record CourtFilterOptionsDto(
    IReadOnlyList<string> CourtNames,
    IReadOnlyList<string> Statuses,
    IReadOnlyList<CourtFilterOptionDto> Desks,
    IReadOnlyList<CourtFilterOptionDto> Officers
)
{
    public IReadOnlyList<CourtFilterOptionDto> AssignedUsers => Officers;
}

public interface ICourtProjectionService
{
    Task<PagedResult<CourtCaseSummaryDto>> GetCourtCasesAsync(CourtCaseFilterQuery query, Guid callerUserId, CancellationToken ct = default);
    Task<CourtCaseDetailDto?> GetCourtCaseDetailAsync(Guid courtCaseId, Guid callerUserId, CancellationToken ct = default);
    Task<IReadOnlyList<CourtProceedingDto>> GetProceedingsAsync(Guid courtCaseId, Guid callerUserId, CancellationToken ct = default);
    Task<IReadOnlyList<CourtCaseDocumentDto>> GetDocumentsAsync(Guid courtCaseId, Guid callerUserId, CancellationToken ct = default);
    Task<IReadOnlyList<CourtCaseTimelineEventDto>> GetTimelineAsync(Guid courtCaseId, Guid callerUserId, CancellationToken ct = default);
    Task<IReadOnlyList<CourtCaseLinkedMatterDto>> GetLinkedWorkAsync(Guid courtCaseId, Guid callerUserId, CancellationToken ct = default);
    Task<CourtFilterOptionsDto> GetFilterOptionsAsync(Guid callerUserId, CancellationToken ct = default);
}

public sealed class CourtProjectionService(
    LacDbContext db,
    ICourtAuthorizationService courtAuth) : ICourtProjectionService
{
    private async Task<HashSet<string>> GetUserPermissionsAsync(Guid userId, CancellationToken ct)
    {
        var isUserActive = await db.AppUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);
        if (!isUserActive) return [];

        var codes = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
               && r.IsActive && r.RecordStatus == RecordStatus.Active
            select p.Code
        ).Distinct().ToListAsync(ct);

        return codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<PagedResult<CourtCaseSummaryDto>> GetCourtCasesAsync(CourtCaseFilterQuery query, Guid callerUserId, CancellationToken ct = default)
    {
        var rawQuery = db.CourtCases.AsNoTracking()
            .Where(c => c.RecordStatus == RecordStatus.Active);

        var authorizedQuery = await courtAuth.AuthorizeListQueryAsync(rawQuery, callerUserId, ct);

        // Apply filters
        if (!string.IsNullOrWhiteSpace(query.CourtName))
            authorizedQuery = authorizedQuery.Where(c => c.CourtName == query.CourtName.Trim());

        if (!string.IsNullOrWhiteSpace(query.CurrentStatus))
            authorizedQuery = authorizedQuery.Where(c => c.CurrentStatus == query.CurrentStatus.Trim());

        if (query.FiledFrom.HasValue)
            authorizedQuery = authorizedQuery.Where(c => c.FiledDate >= query.FiledFrom.Value);

        if (query.FiledTo.HasValue)
            authorizedQuery = authorizedQuery.Where(c => c.FiledDate <= query.FiledTo.Value);

        if (query.DeskId.HasValue)
            authorizedQuery = authorizedQuery.Where(c => c.ResponsibleOfficeDeskId == query.DeskId.Value);

        if (query.AssignedUserId.HasValue)
            authorizedQuery = authorizedQuery.Where(c => c.AssignedUserId == query.AssignedUserId.Value);

        if (query.AwardId.HasValue)
            authorizedQuery = authorizedQuery.Where(c => c.Awards.Any(a => a.AwardId == query.AwardId.Value));

        if (query.VillageId.HasValue)
            authorizedQuery = authorizedQuery.Where(c => c.Khasras.Any(k => k.Khasra.VillageId == query.VillageId.Value));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            authorizedQuery = authorizedQuery.Where(c =>
                c.CaseNumber.ToLower().Contains(s)
                || (c.CaseTitle != null && c.CaseTitle.ToLower().Contains(s))
                || c.CourtName.ToLower().Contains(s)
                || c.Parties.Any(p => p.DisplayName.ToLower().Contains(s) && p.RecordStatus == RecordStatus.Active)
            );
        }

        var totalCount = await authorizedQuery.CountAsync(ct);

        var page = Math.Clamp(query.Page, 1, 1000);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var perms = await GetUserPermissionsAsync(callerUserId, ct);
        var hasAwardView = perms.Contains(PermissionCodes.AwardView);
        var hasKhasraView = perms.Contains(PermissionCodes.KhasraView);
        var hasMatterView = perms.Contains(PermissionCodes.MatterView);
        var hasScheduleView = perms.Contains(PermissionCodes.ScheduleView);

        var items = await authorizedQuery
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.CaseNumber,
                c.CourtName,
                c.CaseTitle,
                c.CaseType,
                c.FiledDate,
                c.CurrentStatus,
                c.DisposedDate,
                c.ResponsibleOfficeDeskId,
                ResponsibleOfficeDeskName = c.ResponsibleOfficeDesk != null ? c.ResponsibleOfficeDesk.Name : null,
                c.AssignedUserId,
                AssignedUserDisplayName = c.AssignedUser != null ? c.AssignedUser.DisplayName : null,
                AwardsCount = c.Awards.Count,
                KhasrasCount = c.Khasras.Count,
                MattersCount = c.Matters.Count(m => m.RecordStatus == RecordStatus.Active),
                PartiesCount = c.Parties.Count(p => p.RecordStatus == RecordStatus.Active),
                DocumentsCount = c.Documents.Count(d => d.RecordStatus == RecordStatus.Active),
                ProceedingsCount = c.Proceedings.Count(p => p.RecordStatus == RecordStatus.Active),
                LastActivityAt = (DateTimeOffset?)c.UpdatedAt,
                ActiveSchedule = db.ScheduledEvents
                    .Where(se => se.CourtCaseId == c.Id && se.Origin == ScheduledEventOrigin.CourtProceeding && se.Status == ScheduledEventStatus.Scheduled && se.RecordStatus == RecordStatus.Active)
                    .Select(se => new { se.Id, se.ScheduledDate })
                    .FirstOrDefault(),
                LatestProceeding = c.Proceedings
                    .Where(p => p.RecordStatus == RecordStatus.Active)
                    .OrderByDescending(p => p.ProceedingDate.HasValue)
                    .ThenByDescending(p => p.ProceedingDate)
                    .ThenByDescending(p => p.CreatedAt)
                    .ThenByDescending(p => p.Id)
                    .Select(p => new { p.ProceedingDate, p.NextDate })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var resultList = items.Select(x =>
        {
            var authNextDate = x.LatestProceeding?.NextDate;
            var activeScheduleNextDate = hasScheduleView ? x.ActiveSchedule?.ScheduledDate : null;
            var activeScheduledEventId = hasScheduleView ? (Guid?)x.ActiveSchedule?.Id : null;
            var isProjected = activeScheduledEventId.HasValue;
            var nextHearingDate = activeScheduleNextDate ?? authNextDate;

            return new CourtCaseSummaryDto(
                x.Id,
                x.CaseNumber,
                x.CourtName,
                x.CaseTitle,
                x.CaseType,
                x.FiledDate,
                x.CurrentStatus,
                x.DisposedDate,
                x.ResponsibleOfficeDeskId,
                x.ResponsibleOfficeDeskName,
                x.AssignedUserId,
                x.AssignedUserDisplayName,
                authNextDate,
                activeScheduleNextDate,
                isProjected,
                activeScheduledEventId,
                nextHearingDate,
                x.LatestProceeding?.ProceedingDate,
                hasAwardView ? x.AwardsCount : null,
                hasKhasraView ? x.KhasrasCount : null,
                hasMatterView ? x.MattersCount : null,
                x.PartiesCount,
                x.DocumentsCount,
                x.ProceedingsCount,
                x.LastActivityAt
            );
        }).ToList();

        return new PagedResult<CourtCaseSummaryDto>(resultList, totalCount, page, pageSize);
    }

    public async Task<CourtCaseDetailDto?> GetCourtCaseDetailAsync(Guid courtCaseId, Guid callerUserId, CancellationToken ct = default)
    {
        var canView = await courtAuth.CanViewCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canView) return null;

        var courtCase = await db.CourtCases.AsNoTracking()
            .Include(c => c.ResponsibleOfficeDesk)
            .Include(c => c.AssignedUser)
            .Include(c => c.Awards).ThenInclude(a => a.Award).ThenInclude(aw => aw.VillageLinks).ThenInclude(v => v.Village)
            .Include(c => c.Khasras).ThenInclude(k => k.Khasra).ThenInclude(kh => kh.Village)
            .Include(c => c.Matters.Where(m => m.RecordStatus == RecordStatus.Active)).ThenInclude(m => m.Matter).ThenInclude(mat => mat.Workstream)
            .Include(c => c.Parties.Where(p => p.RecordStatus == RecordStatus.Active))
            .Include(c => c.Representatives.Where(r => r.RecordStatus == RecordStatus.Active))
            .FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct);

        if (courtCase is null) return null;

        var perms = await GetUserPermissionsAsync(callerUserId, ct);
        var hasAwardView = perms.Contains(PermissionCodes.AwardView);
        var hasKhasraView = perms.Contains(PermissionCodes.KhasraView);
        var hasMatterView = perms.Contains(PermissionCodes.MatterView);
        var hasScheduleView = perms.Contains(PermissionCodes.ScheduleView);
        var hasDraftView = perms.Contains(PermissionCodes.DraftView);
        var hasWorkItemView = perms.Contains(PermissionCodes.WorkItemView);
        var hasScheduleCreate = perms.Contains(PermissionCodes.ScheduleCreate);

        var activeSchedule = hasScheduleView
            ? await db.ScheduledEvents.AsNoTracking()
                .FirstOrDefaultAsync(se => se.CourtCaseId == courtCaseId
                                        && se.Origin == ScheduledEventOrigin.CourtProceeding
                                        && se.Status == ScheduledEventStatus.Scheduled
                                        && se.RecordStatus == RecordStatus.Active, ct)
            : null;

        var latestProceeding = await db.CourtProceedings.AsNoTracking()
            .Where(p => p.CourtCaseId == courtCaseId && p.RecordStatus == RecordStatus.Active)
            .OrderByDescending(p => p.ProceedingDate.HasValue)
            .ThenByDescending(p => p.ProceedingDate)
            .ThenByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync(ct);

        var docCount = await db.CourtCaseDocuments.AsNoTracking()
            .CountAsync(d => d.CourtCaseId == courtCaseId && d.RecordStatus == RecordStatus.Active, ct);

        var procCount = await db.CourtProceedings.AsNoTracking()
            .CountAsync(p => p.CourtCaseId == courtCaseId && p.RecordStatus == RecordStatus.Active, ct);

        var eventCount = await db.CourtCaseEvents.AsNoTracking()
            .CountAsync(e => e.CourtCaseId == courtCaseId, ct);

        IReadOnlyList<CourtCaseLinkedAwardDto> awards = [];
        int? awardsCount = null;
        if (hasAwardView)
        {
            awards = courtCase.Awards.Select(a => new CourtCaseLinkedAwardDto(
                a.AwardId,
                a.Award.AwardNumber,
                null,
                a.Award.VillageLinks.Select(v => v.Village.Name).Distinct().ToList()
            )).ToList();
            awardsCount = awards.Count;
        }

        IReadOnlyList<CourtCaseLinkedKhasraDto> khasras = [];
        int? khasrasCount = null;
        if (hasKhasraView)
        {
            khasras = courtCase.Khasras.Select(k => new CourtCaseLinkedKhasraDto(
                k.KhasraId,
                k.Khasra.VillageId,
                k.Khasra.Village?.Name ?? "",
                k.Khasra.NormalizedNumber,
                k.Khasra.Qualifier,
                k.Khasra.TotalArea,
                k.Khasra.AreaUnit
            )).ToList();
            khasrasCount = khasras.Count;
        }

        IReadOnlyList<CourtCaseLinkedMatterDto> matters = [];
        int? mattersCount = null;
        if (hasMatterView)
        {
            var linkedMatterIds = courtCase.Matters.Select(m => m.MatterId).ToList();

            var draftsCountByMatter = hasDraftView
                ? await db.MatterDrafts.AsNoTracking()
                    .Where(d => linkedMatterIds.Contains(d.MatterId) && d.RecordStatus == RecordStatus.Active)
                    .GroupBy(d => d.MatterId)
                    .Select(g => new { MatterId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.MatterId, g => g.Count, ct)
                : new Dictionary<Guid, int>();

            var workItemsCountByMatter = hasWorkItemView
                ? await db.Set<WorkItemMatterLink>().AsNoTracking()
                    .Where(w => linkedMatterIds.Contains(w.MatterId) && w.RecordStatus == RecordStatus.Active)
                    .GroupBy(w => w.MatterId)
                    .Select(g => new { MatterId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.MatterId, g => g.Count, ct)
                : new Dictionary<Guid, int>();

            matters = courtCase.Matters.Select(m => new CourtCaseLinkedMatterDto(
                m.MatterId,
                m.Matter.Title,
                m.Matter.ReferenceNumber,
                m.Matter.Status,
                m.Matter.Workstream?.Name,
                draftsCountByMatter.GetValueOrDefault(m.MatterId, 0),
                workItemsCountByMatter.GetValueOrDefault(m.MatterId, 0)
            )).ToList();
            mattersCount = matters.Count;
        }

        var parties = courtCase.Parties
            .OrderBy(p => p.Sequence)
            .ThenBy(p => p.CreatedAt)
            .Select(p => new CourtCasePartyDto(
                p.Id,
                p.CourtCaseId,
                p.PartyId,
                p.DisplayName,
                p.Role,
                p.FatherOrSpouseName,
                p.AddressText,
                p.Remarks,
                p.Sequence
            )).ToList();

        var reps = courtCase.Representatives
            .OrderBy(r => r.CreatedAt)
            .Select(r => new CourtCaseRepresentativeDto(
                r.Id,
                r.CourtCaseId,
                r.CourtCasePartyId,
                r.DisplayName,
                r.RepresentativeType,
                r.RepresentsRole,
                r.ContactText,
                r.Remarks
            )).ToList();

        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        var canAssign = await courtAuth.CanAssignCourtCaseAsync(courtCaseId, callerUserId, ct);
        var canManageProc = await courtAuth.CanManageProceedingsAsync(courtCaseId, callerUserId, ct);
        var canManageDoc = await courtAuth.CanManageDocumentsAsync(courtCaseId, callerUserId, ct);

        var authNextDate = latestProceeding?.NextDate;
        var isProjected = activeSchedule != null;
        var canPromote = authNextDate.HasValue && !isProjected && hasScheduleCreate && (canEdit || canManageProc);
        var canLinkAward = canEdit && hasAwardView;
        var canLinkKhasra = canEdit && hasKhasraView;
        var canLinkMatter = canEdit && hasMatterView;

        var capabilities = new CourtCaseCapabilitiesDto(
            CanEdit: canEdit,
            CanAssign: canAssign,
            CanManageProceedings: canManageProc,
            CanManageDocuments: canManageDoc,
            CanPromoteToCalendar: canPromote,
            CanLinkAward: canLinkAward,
            CanLinkKhasra: canLinkKhasra,
            CanLinkMatter: canLinkMatter
        );

        return new CourtCaseDetailDto(
            courtCase.Id,
            courtCase.CaseNumber,
            courtCase.CourtName,
            courtCase.CaseTitle,
            courtCase.CaseType,
            courtCase.FiledDate,
            courtCase.CurrentStatus,
            courtCase.DisposedDate,
            courtCase.Remarks,
            courtCase.Revision,
            courtCase.ResponsibleOfficeDeskId,
            courtCase.ResponsibleOfficeDesk?.Name,
            courtCase.AssignedUserId,
            courtCase.AssignedUser?.DisplayName,
            authNextDate,
            activeSchedule?.ScheduledDate,
            isProjected,
            activeSchedule?.Id,
            activeSchedule?.ScheduledDate ?? authNextDate,
            latestProceeding?.ProceedingDate,
            latestProceeding?.OrderType,
            latestProceeding?.RestraintNature,
            latestProceeding?.Summary,
            awards,
            khasras,
            matters,
            parties,
            reps,
            awardsCount,
            khasrasCount,
            mattersCount,
            parties.Count,
            docCount,
            procCount,
            eventCount,
            capabilities
        );
    }

    public async Task<IReadOnlyList<CourtProceedingDto>> GetProceedingsAsync(Guid courtCaseId, Guid callerUserId, CancellationToken ct = default)
    {
        var canView = await courtAuth.CanViewCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canView) throw new UnauthorizedAccessException("Forbidden");

        return await db.CourtProceedings.AsNoTracking()
            .Where(p => p.CourtCaseId == courtCaseId && p.RecordStatus == RecordStatus.Active)
            .OrderByDescending(p => p.ProceedingDate.HasValue)
            .ThenByDescending(p => p.ProceedingDate)
            .ThenByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Select(p => new CourtProceedingDto(
                p.Id,
                p.CourtCaseId,
                p.ProceedingDate,
                p.OrderType,
                p.RestraintNature,
                p.Summary,
                p.NextDate,
                p.CreatedAt
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CourtCaseDocumentDto>> GetDocumentsAsync(Guid courtCaseId, Guid callerUserId, CancellationToken ct = default)
    {
        var canView = await courtAuth.CanViewCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canView) throw new UnauthorizedAccessException("Forbidden");

        return await db.CourtCaseDocuments.AsNoTracking()
            .Include(cd => cd.Document)
            .Where(cd => cd.CourtCaseId == courtCaseId && cd.RecordStatus == RecordStatus.Active)
            .OrderByDescending(cd => cd.CreatedAt)
            .Select(cd => new CourtCaseDocumentDto(
                cd.Id,
                cd.CourtCaseId,
                cd.DocumentId,
                cd.Document.OriginalFileName,
                cd.Document.DocumentType,
                cd.DocumentRole,
                cd.DisplayName,
                cd.CourtProceedingId,
                cd.Document.FileSize,
                cd.Document.MimeType,
                cd.Document.UploadedAt
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CourtCaseTimelineEventDto>> GetTimelineAsync(Guid courtCaseId, Guid callerUserId, CancellationToken ct = default)
    {
        var canView = await courtAuth.CanViewCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canView) throw new UnauthorizedAccessException("Forbidden");

        return await db.CourtCaseEvents.AsNoTracking()
            .Where(e => e.CourtCaseId == courtCaseId)
            .OrderByDescending(e => e.SequenceNumber)
            .Select(e => new CourtCaseTimelineEventDto(
                e.Id,
                e.SequenceNumber,
                e.Action.ToString(),
                e.ActionAt,
                e.ActorUserId,
                e.ActorDisplayNameSnapshot,
                e.ActorDesignationSnapshot,
                e.SourceDeskNameSnapshot,
                e.TargetDeskNameSnapshot,
                e.SourceUserDisplayNameSnapshot,
                e.TargetUserDisplayNameSnapshot,
                e.OldStatus,
                e.NewStatus,
                e.Reason,
                e.Notes
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CourtCaseLinkedMatterDto>> GetLinkedWorkAsync(Guid courtCaseId, Guid callerUserId, CancellationToken ct = default)
    {
        var canView = await courtAuth.CanViewCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canView) throw new UnauthorizedAccessException("Forbidden");

        var perms = await GetUserPermissionsAsync(callerUserId, ct);
        if (!perms.Contains(PermissionCodes.MatterView))
            return [];

        var hasDraftView = perms.Contains(PermissionCodes.DraftView);
        var hasWorkItemView = perms.Contains(PermissionCodes.WorkItemView);

        var linkedMatters = await db.CourtCaseMatters.AsNoTracking()
            .Include(m => m.Matter).ThenInclude(mat => mat.Workstream)
            .Where(m => m.CourtCaseId == courtCaseId && m.RecordStatus == RecordStatus.Active)
            .ToListAsync(ct);

        var matterIds = linkedMatters.Select(m => m.MatterId).ToList();

        var draftsCount = hasDraftView
            ? await db.MatterDrafts.AsNoTracking()
                .Where(d => matterIds.Contains(d.MatterId) && d.RecordStatus == RecordStatus.Active)
                .GroupBy(d => d.MatterId)
                .Select(g => new { MatterId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.MatterId, g => g.Count, ct)
            : new Dictionary<Guid, int>();

        var workItemsCount = hasWorkItemView
            ? await db.Set<WorkItemMatterLink>().AsNoTracking()
                .Where(w => matterIds.Contains(w.MatterId) && w.RecordStatus == RecordStatus.Active)
                .GroupBy(w => w.MatterId)
                .Select(g => new { MatterId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.MatterId, g => g.Count, ct)
            : new Dictionary<Guid, int>();

        return linkedMatters.Select(m => new CourtCaseLinkedMatterDto(
            m.MatterId,
            m.Matter.Title,
            m.Matter.ReferenceNumber,
            m.Matter.Status,
            m.Matter.Workstream?.Name,
            draftsCount.GetValueOrDefault(m.MatterId, 0),
            workItemsCount.GetValueOrDefault(m.MatterId, 0)
        )).ToList();
    }

    public async Task<CourtFilterOptionsDto> GetFilterOptionsAsync(Guid callerUserId, CancellationToken ct = default)
    {
        var rawQuery = db.CourtCases.AsNoTracking().Where(c => c.RecordStatus == RecordStatus.Active);
        var authorized = await courtAuth.AuthorizeListQueryAsync(rawQuery, callerUserId, ct);

        var courtNames = await authorized
            .Select(c => c.CourtName)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);

        var statuses = await authorized
            .Where(c => c.CurrentStatus != null)
            .Select(c => c.CurrentStatus!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);

        var courtDesks = await db.OfficeDesks.AsNoTracking()
            .Include(d => d.Workstream)
            .Where(d => d.IsActive && d.RecordStatus == RecordStatus.Active
                     && d.Workstream != null && d.Workstream.Code == WorkstreamCodes.CourtReferences)
            .OrderBy(d => d.Name)
            .Select(d => new CourtFilterOptionDto(d.Id, d.Name, d.Workstream != null ? d.Workstream.Name : null))
            .ToListAsync(ct);

        var courtDeskIds = courtDesks.Select(d => d.Id).ToList();

        var officers = await db.UserDeskMemberships.AsNoTracking()
            .Where(m => courtDeskIds.Contains(m.OfficeDeskId)
                     && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active
                     && m.User.IsActive && m.User.RecordStatus == RecordStatus.Active)
            .Select(m => m.User)
            .Distinct()
            .OrderBy(u => u.DisplayName)
            .Select(u => new CourtFilterOptionDto(u.Id, u.DisplayName, null))
            .ToListAsync(ct);

        return new CourtFilterOptionsDto(courtNames, statuses, courtDesks, officers);
    }
}
