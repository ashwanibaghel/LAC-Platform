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
    int PageSize = 25,
    IReadOnlyList<string>? CourtNames = null,
    IReadOnlyList<string>? Statuses = null,
    string? NdohFilter = null,
    DateOnly? NdohFrom = null,
    DateOnly? NdohTo = null,
    string? CaseNumber = null,
    string? CaseType = null,
    string? Advocate = null,
    string? Village = null,
    string? Award = null,
    string? Directions = null,
    string? BriefFacts = null,
    string? SourceOrderLinkState = null
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
    DateTimeOffset? LastActivityAt,
    DateOnly? OperationalNdoh = null,
    string QueueState = "NoNdoh",
    int? DaysFromToday = null,
    IReadOnlyList<string>? Advocates = null,
    string? SourceVillage = null,
    string? SourceAwardNumber = null,
    string? OperationalNdohSource = null
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
    int? DraftsCount,
    int? WorkItemsCount
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
    CourtCaseCapabilitiesDto Capabilities,
    DateOnly? OperationalNdoh = null,
    string? OperationalNdohSource = null
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
    IReadOnlyList<CourtFilterOptionDto> Officers,
    IReadOnlyList<CourtFilterOptionDto>? ViewDesks = null,
    IReadOnlyList<CourtFilterOptionDto>? CreateDesks = null,
    IReadOnlyList<CourtFilterOptionDto>? AssignTargetDesks = null,
    IReadOnlyList<string>? CaseTypes = null,
    IReadOnlyList<CourtFilterOptionDto>? DirectoryOfficers = null
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
    ICourtAuthorizationService courtAuth,
    IScheduleAuthorizationService scheduleAuth,
    IOfficeClock? officeClock = null) : ICourtProjectionService
{
    private readonly IOfficeClock _officeClock = officeClock ?? new OfficeClock();
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
        var today = _officeClock.GetCurrentDate();
        var rawQuery = db.CourtCases.AsNoTracking()
            .Where(c => c.RecordStatus == RecordStatus.Active);

        var authorizedQuery = await courtAuth.AuthorizeListQueryAsync(rawQuery, callerUserId, ct);

        // The CourtCase ACL is the root of every correlated search below. Source-register
        // fields are only searchable after commit to an authorized CourtCase.
        if (!string.IsNullOrWhiteSpace(query.CourtName))
            authorizedQuery = authorizedQuery.Where(c => c.CourtName == query.CourtName.Trim());

        var courts = (query.CourtNames ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToArray();
        if (courts.Length > 0)
            authorizedQuery = authorizedQuery.Where(c => courts.Contains(c.CourtName));

        if (!string.IsNullOrWhiteSpace(query.CurrentStatus))
        {
            var status = query.CurrentStatus.Trim().ToLower();
            authorizedQuery = authorizedQuery.Where(c => c.CurrentStatus != null && c.CurrentStatus.Trim().ToLower() == status);
        }

        var statuses = (query.Statuses ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLower()).Distinct().ToArray();
        if (statuses.Length > 0)
            authorizedQuery = authorizedQuery.Where(c => c.CurrentStatus != null && statuses.Contains(c.CurrentStatus.Trim().ToLower()));

        if (query.FiledFrom.HasValue)
            authorizedQuery = authorizedQuery.Where(c => c.FiledDate >= query.FiledFrom.Value);

        if (query.FiledTo.HasValue)
            authorizedQuery = authorizedQuery.Where(c => c.FiledDate <= query.FiledTo.Value);

        if (query.DeskId.HasValue)
            authorizedQuery = authorizedQuery.Where(c => c.ResponsibleOfficeDeskId == query.DeskId.Value);

        if (query.AssignedUserId.HasValue)
            authorizedQuery = authorizedQuery.Where(c => c.AssignedUserId == query.AssignedUserId.Value);

        if (query.AwardId.HasValue)
        {
            if (!await courtAuth.CanAccessAwardAsync(query.AwardId.Value, callerUserId, ct))
                return new PagedResult<CourtCaseSummaryDto>([], 0, query.Page, query.PageSize);

            authorizedQuery = authorizedQuery.Where(c => c.Awards.Any(a => a.AwardId == query.AwardId.Value) || db.CourtOrderRecordLinks.Any(l => l.MatchState == CourtRecordMatchState.Confirmed && l.AwardId == query.AwardId.Value && l.Revision.Order.CourtCaseId == c.Id));
        }

        if (query.VillageId.HasValue)
        {
            if (!await courtAuth.CanAccessVillageAsync(query.VillageId.Value, callerUserId, ct))
                return new PagedResult<CourtCaseSummaryDto>([], 0, query.Page, query.PageSize);

            authorizedQuery = authorizedQuery.Where(c => c.Khasras.Any(k => k.Khasra.VillageId == query.VillageId.Value) || db.CourtOrderRecordLinks.Any(l => l.MatchState == CourtRecordMatchState.Confirmed && (l.VillageId == query.VillageId.Value || l.Khasra != null && l.Khasra.VillageId == query.VillageId.Value) && l.Revision.Order.CourtCaseId == c.Id));
        }

        if (!string.IsNullOrWhiteSpace(query.CaseNumber))
        {
            var term = query.CaseNumber.Trim().ToLower();
            authorizedQuery = authorizedQuery.Where(c => c.CaseNumber.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(query.CaseType))
        {
            var term = query.CaseType.Trim().ToLower();
            authorizedQuery = authorizedQuery.Where(c => c.CaseType != null && c.CaseType.Trim().ToLower() == term);
        }
        if (!string.IsNullOrWhiteSpace(query.Advocate))
        {
            var term = query.Advocate.Trim().ToLower();
            authorizedQuery = authorizedQuery.Where(c => c.Representatives.Any(r => r.RecordStatus == RecordStatus.Active && r.DisplayName.ToLower().Contains(term)));
        }

        var canReadAwards = !string.IsNullOrWhiteSpace(query.Award) &&
            await CanReadLinkedLandAsync(callerUserId, PermissionCodes.AwardView, [WorkstreamCodes.Award], ct);
        var canReadVillages = !string.IsNullOrWhiteSpace(query.Village) &&
            await CanReadLinkedLandAsync(callerUserId, PermissionCodes.VillageView,
            [WorkstreamCodes.LandRecords, WorkstreamCodes.Award, WorkstreamCodes.CourtReferences], ct)
            && await CanReadLinkedLandAsync(callerUserId, PermissionCodes.KhasraView,
                [WorkstreamCodes.LandRecords, WorkstreamCodes.Award], ct);
        if (!string.IsNullOrWhiteSpace(query.Village))
        {
            var term = query.Village.Trim().ToLower();
            authorizedQuery = authorizedQuery.Where(c =>
                db.CourtImportRows.Any(r => r.CommittedCourtCaseId == c.Id && r.CommitStatus == CourtImportCommitStatus.Committed &&
                    r.RawVillage != null && r.RawVillage.ToLower().Contains(term)) ||
                (canReadVillages && c.Khasras.Any(k => k.Khasra.RecordStatus == RecordStatus.Active &&
                    k.Khasra.Village.RecordStatus == RecordStatus.Active && k.Khasra.Village.Name.ToLower().Contains(term))));
        }
        if (!string.IsNullOrWhiteSpace(query.Award))
        {
            var term = query.Award.Trim().ToLower();
            authorizedQuery = authorizedQuery.Where(c =>
                db.CourtImportRows.Any(r => r.CommittedCourtCaseId == c.Id && r.CommitStatus == CourtImportCommitStatus.Committed &&
                    r.RawAwardNumber != null && r.RawAwardNumber.ToLower().Contains(term)) ||
                (canReadAwards && c.Awards.Any(a => a.Award.RecordStatus == RecordStatus.Active && a.Award.AwardNumber.ToLower().Contains(term))));
        }
        if (!string.IsNullOrWhiteSpace(query.Directions))
        {
            var term = query.Directions.Trim().ToLower();
            authorizedQuery = authorizedQuery.Where(c => db.CourtImportRows.Any(r =>
                r.CommittedCourtCaseId == c.Id && r.CommitStatus == CourtImportCommitStatus.Committed &&
                r.RawDirections != null && r.RawDirections.ToLower().Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(query.BriefFacts))
        {
            var term = query.BriefFacts.Trim().ToLower();
            authorizedQuery = authorizedQuery.Where(c => db.CourtImportRows.Any(r =>
                r.CommittedCourtCaseId == c.Id && r.CommitStatus == CourtImportCommitStatus.Committed &&
                r.RawBriefFacts != null && r.RawBriefFacts.ToLower().Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(query.SourceOrderLinkState) && !query.SourceOrderLinkState.Equals("Any", StringComparison.OrdinalIgnoreCase))
        {
            var state = query.SourceOrderLinkState.Trim().ToLower();
            if (state is not ("validhttpurl" or "missing" or "needsreview"))
                throw new CourtWorkflowException("Unknown source order-link filter.", 400);
            authorizedQuery = authorizedQuery.Where(c => db.CourtImportRows.Any(r =>
                r.CommittedCourtCaseId == c.Id && r.CommitStatus == CourtImportCommitStatus.Committed &&
                r.LastOrderLinkState != null && r.LastOrderLinkState.ToLower() == state));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            authorizedQuery = authorizedQuery.Where(c =>
                c.CaseNumber.ToLower().Contains(s)
                || (c.CaseTitle != null && c.CaseTitle.ToLower().Contains(s))
                || c.CourtName.ToLower().Contains(s)
                || (c.CaseType != null && c.CaseType.ToLower().Contains(s))
                || c.Representatives.Any(r => r.RecordStatus == RecordStatus.Active && r.DisplayName.ToLower().Contains(s))
                || c.Parties.Any(p => p.DisplayName.ToLower().Contains(s) && p.RecordStatus == RecordStatus.Active)
                || db.CourtImportRows.Any(r => r.CommittedCourtCaseId == c.Id && r.CommitStatus == CourtImportCommitStatus.Committed &&
                    ((r.RawVillage != null && r.RawVillage.ToLower().Contains(s)) ||
                     (r.RawAwardNumber != null && r.RawAwardNumber.ToLower().Contains(s)) ||
                     (r.RawDirections != null && r.RawDirections.ToLower().Contains(s)) ||
                     (r.RawBriefFacts != null && r.RawBriefFacts.ToLower().Contains(s))))
            );
        }

        var queue = CourtOperationalNdohQuery.Resolve(authorizedQuery, db, today);

        var ndohFilter = query.NdohFilter?.Trim().ToLowerInvariant() ?? "all";
        if (ndohFilter != "all")
        {
            if (ndohFilter != "customrange")
                queue = queue.Where(x => x.Case.CurrentStatus != null && x.Case.CurrentStatus.Trim().ToLower() == "pending");
            switch (ndohFilter)
            {
                case "today": queue = queue.Where(x => x.OperationalNdoh == today); break;
                case "tomorrow": var tomorrow = today.AddDays(1); queue = queue.Where(x => x.OperationalNdoh == tomorrow); break;
                case "next7days": var seventh = today.AddDays(6); queue = queue.Where(x => x.OperationalNdoh >= today && x.OperationalNdoh <= seventh); break;
                case "thisweek":
                    var monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
                    var sunday = monday.AddDays(6);
                    queue = queue.Where(x => x.OperationalNdoh >= monday && x.OperationalNdoh <= sunday); break;
                case "thismonth":
                    var first = new DateOnly(today.Year, today.Month, 1);
                    var last = first.AddMonths(1).AddDays(-1);
                    queue = queue.Where(x => x.OperationalNdoh >= first && x.OperationalNdoh <= last); break;
                case "upcoming": queue = queue.Where(x => x.OperationalNdoh >= today); break;
                case "overdue": queue = queue.Where(x => x.OperationalNdoh < today); break;
                case "nondoh": queue = queue.Where(x => x.OperationalNdoh == null); break;
                case "customrange":
                    if (!query.NdohFrom.HasValue || !query.NdohTo.HasValue || query.NdohFrom > query.NdohTo)
                        throw new CourtWorkflowException("Custom NDOH range requires From <= To.", 400);
                    queue = queue.Where(x => x.OperationalNdoh >= query.NdohFrom.Value && x.OperationalNdoh <= query.NdohTo.Value); break;
                default: throw new CourtWorkflowException("Unknown NDOH filter.", 400);
            }
        }

        var totalCount = await queue.CountAsync(ct);

        var page = Math.Clamp(query.Page, 1, 1000);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;
        var hasScheduleViewCap = (await GetScopesForPermissionAsync(callerUserId, PermissionCodes.ScheduleView, ct)).Count > 0;

        var rawItems = await queue
            .OrderBy(x => x.Case.CurrentStatus != null && x.Case.CurrentStatus.Trim().ToLower() == "pending"
                ? (x.OperationalNdoh >= today ? 0 : x.OperationalNdoh != null ? 1 : 2)
                : x.Case.CurrentStatus != null && x.Case.CurrentStatus.Trim().ToLower() == "disposed" ? 4 : 3)
            .ThenBy(x => x.Case.CurrentStatus != null && x.Case.CurrentStatus.Trim().ToLower() == "pending" && x.OperationalNdoh >= today
                ? x.OperationalNdoh : null)
            .ThenByDescending(x => x.Case.CurrentStatus != null && x.Case.CurrentStatus.Trim().ToLower() == "pending" && x.OperationalNdoh < today
                ? x.OperationalNdoh : null)
            .ThenByDescending(x => x.Case.CurrentStatus == null || x.Case.CurrentStatus.Trim().ToLower() != "pending" || x.OperationalNdoh == null
                ? (DateTimeOffset?)x.Case.UpdatedAt : null)
            .ThenBy(x => x.Case.CourtName)
            .ThenBy(x => x.Case.CaseNumber)
            .ThenBy(x => x.Case.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(x => new
            {
                x.OperationalNdoh,
                x.UsesExternalListing,
                x.UsesHistoricalListing,
                x.UsesAssistedStatus,
                x.Case.Id,
                x.Case.CaseNumber,
                x.Case.CourtName,
                x.Case.CaseTitle,
                x.Case.CaseType,
                x.Case.FiledDate,
                x.Case.CurrentStatus,
                x.Case.DisposedDate,
                x.Case.ResponsibleOfficeDeskId,
                ResponsibleOfficeDeskName = x.Case.ResponsibleOfficeDesk != null ? x.Case.ResponsibleOfficeDesk.Name : null,
                x.Case.AssignedUserId,
                AssignedUserDisplayName = x.Case.AssignedUser != null ? x.Case.AssignedUser.DisplayName : null,
                AwardIds = x.Case.Awards.Select(a => a.AwardId).ToList(),
                KhasraIds = x.Case.Khasras.Select(k => k.KhasraId).ToList(),
                MatterIds = x.Case.Matters.Where(m => m.RecordStatus == RecordStatus.Active).Select(m => m.MatterId).ToList(),
                PartiesCount = x.Case.Parties.Count(p => p.RecordStatus == RecordStatus.Active),
                DocumentsCount = x.Case.Documents.Count(d => d.RecordStatus == RecordStatus.Active),
                ProceedingsCount = x.Case.Proceedings.Count(p => p.RecordStatus == RecordStatus.Active),
                LastActivityAt = (DateTimeOffset?)x.Case.UpdatedAt,
                Advocates = x.Case.Representatives.Where(r => r.RecordStatus == RecordStatus.Active).OrderBy(r => r.DisplayName).Select(r => r.DisplayName).ToList(),
                Source = db.CourtImportRows.Where(r => r.CommittedCourtCaseId == x.Case.Id && r.CommitStatus == CourtImportCommitStatus.Committed)
                    .OrderByDescending(r => r.CommittedAt).ThenByDescending(r => r.Id)
                    .Select(r => new { r.RawVillage, r.RawAwardNumber }).FirstOrDefault(),
                ActiveSchedule = db.ScheduledEvents
                    .Where(se => se.CourtCaseId == x.Case.Id && se.Origin == ScheduledEventOrigin.CourtProceeding && se.Status == ScheduledEventStatus.Scheduled && se.RecordStatus == RecordStatus.Active)
                    .Select(se => new { se.Id, se.ScheduledDate })
                    .FirstOrDefault(),
                LatestProceeding = x.Case.Proceedings
                    .Where(p => p.RecordStatus == RecordStatus.Active)
                    .OrderByDescending(p => p.ProceedingDate.HasValue)
                    .ThenByDescending(p => p.ProceedingDate)
                    .ThenByDescending(p => p.CreatedAt)
                    .ThenByDescending(p => p.Id)
                    .Select(p => new { p.ProceedingDate, p.NextDate })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var resultList = new List<CourtCaseSummaryDto>();
        foreach (var x in rawItems)
        {
            var authNextDate = x.LatestProceeding?.NextDate;
            var status = x.CurrentStatus?.Trim();
            var queueState = status?.Equals("Disposed", StringComparison.OrdinalIgnoreCase) == true ? "Disposed"
                : status?.Equals("Pending", StringComparison.OrdinalIgnoreCase) != true ? "Attention"
                : !x.OperationalNdoh.HasValue ? "NoNdoh"
                : x.OperationalNdoh.Value == today ? "Today"
                : x.OperationalNdoh.Value > today ? "Upcoming" : "Overdue";

            DateOnly? activeScheduleNextDate = null;
            Guid? activeScheduledEventId = null;
            if (hasScheduleViewCap && x.ActiveSchedule != null)
            {
                if (await scheduleAuth.CanAccessScheduledEventAsync(x.ActiveSchedule.Id, PermissionCodes.ScheduleView, callerUserId, ct))
                {
                    activeScheduleNextDate = x.ActiveSchedule.ScheduledDate;
                    activeScheduledEventId = x.ActiveSchedule.Id;
                }
            }

            var isProjected = activeScheduledEventId.HasValue;
            var nextHearingDate = activeScheduleNextDate ?? authNextDate;

            int? awardsCount = await GetEffectiveAwardsCountAsync(x.AwardIds, callerUserId, ct);
            int? khasrasCount = await GetEffectiveKhasrasCountAsync(x.KhasraIds, callerUserId, ct);
            int? mattersCount = await GetEffectiveMattersCountAsync(x.MatterIds, callerUserId, ct);

            resultList.Add(new CourtCaseSummaryDto(
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
                awardsCount,
                khasrasCount,
                mattersCount,
                x.PartiesCount,
                x.DocumentsCount,
                x.ProceedingsCount,
                x.LastActivityAt,
                x.OperationalNdoh,
                queueState,
                x.OperationalNdoh.HasValue ? x.OperationalNdoh.Value.DayNumber - today.DayNumber : null,
                x.Advocates,
                x.Source?.RawVillage,
                x.Source?.RawAwardNumber,
                x.UsesAssistedStatus ? "DHC assisted case status" : x.UsesHistoricalListing ? "DHC historical cause list" : x.UsesExternalListing ? "DHC Cause List" : x.OperationalNdoh.HasValue ?
                    x.LatestProceeding?.ProceedingDate.HasValue == true ? "Court proceeding" : "Office register" : null
            ));
        }

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


        var candidateSchedule = await db.ScheduledEvents.AsNoTracking()
            .FirstOrDefaultAsync(se => se.CourtCaseId == courtCaseId
                                    && se.Origin == ScheduledEventOrigin.CourtProceeding
                                    && se.Status == ScheduledEventStatus.Scheduled
                                    && se.RecordStatus == RecordStatus.Active, ct);

        DateOnly? activeScheduleNextDate = null;
        Guid? activeScheduledEventId = null;
        bool isProjectedToCalendar = false;

        if (candidateSchedule != null)
        {
            if (await scheduleAuth.CanAccessScheduledEventAsync(candidateSchedule.Id, PermissionCodes.ScheduleView, callerUserId, ct))
            {
                activeScheduleNextDate = candidateSchedule.ScheduledDate;
                activeScheduledEventId = candidateSchedule.Id;
                isProjectedToCalendar = true;
            }
        }

        var latestProceeding = await db.CourtProceedings.AsNoTracking()
            .Where(p => p.CourtCaseId == courtCaseId && p.RecordStatus == RecordStatus.Active)
            .OrderByDescending(p => p.ProceedingDate.HasValue)
            .ThenByDescending(p => p.ProceedingDate)
            .ThenByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync(ct);

        var operational = await CourtOperationalNdohQuery.Resolve(
                db.CourtCases.AsNoTracking().Where(c => c.Id == courtCaseId), db, _officeClock.GetCurrentDate())
            .Select(x => new { x.OperationalNdoh, x.UsesExternalListing, x.UsesHistoricalListing, x.UsesAssistedStatus }).SingleAsync(ct);

        var docCount = await db.CourtCaseDocuments.AsNoTracking()
            .CountAsync(d => d.CourtCaseId == courtCaseId && d.RecordStatus == RecordStatus.Active, ct);

        var procCount = await db.CourtProceedings.AsNoTracking()
            .CountAsync(p => p.CourtCaseId == courtCaseId && p.RecordStatus == RecordStatus.Active, ct);

        var eventCount = await db.CourtCaseEvents.AsNoTracking()
            .CountAsync(e => e.CourtCaseId == courtCaseId, ct);

        IReadOnlyList<CourtCaseLinkedAwardDto> awards = [];
        int? awardsCount = await GetEffectiveAwardsCountAsync(courtCase.Awards.Select(a => a.AwardId).ToList(), callerUserId, ct);
        if (awardsCount.HasValue)
        {
            var authAwards = new List<CourtCaseLinkedAwardDto>();
            foreach (var a in courtCase.Awards)
            {
                if (await courtAuth.CanAccessAwardAsync(a.AwardId, callerUserId, ct))
                {
                    authAwards.Add(new CourtCaseLinkedAwardDto(
                        a.AwardId,
                        a.Award.AwardNumber,
                        null,
                        a.Award.VillageLinks.Select(v => v.Village.Name).Distinct().ToList()
                    ));
                }
            }
            awards = authAwards;
        }

        IReadOnlyList<CourtCaseLinkedKhasraDto> khasras = [];
        int? khasrasCount = await GetEffectiveKhasrasCountAsync(courtCase.Khasras.Select(k => k.KhasraId).ToList(), callerUserId, ct);
        if (khasrasCount.HasValue)
        {
            var authKhasras = new List<CourtCaseLinkedKhasraDto>();
            foreach (var k in courtCase.Khasras)
            {
                if (await courtAuth.CanAccessKhasraAsync(k.KhasraId, callerUserId, ct))
                {
                    authKhasras.Add(new CourtCaseLinkedKhasraDto(
                        k.KhasraId,
                        k.Khasra.VillageId,
                        k.Khasra.Village?.Name ?? "",
                        k.Khasra.NormalizedNumber,
                        k.Khasra.Qualifier,
                        k.Khasra.TotalArea,
                        k.Khasra.AreaUnit
                    ));
                }
            }
            khasras = authKhasras;
        }

        IReadOnlyList<CourtCaseLinkedMatterDto> matters = [];
        int? mattersCount = await GetEffectiveMattersCountAsync(courtCase.Matters.Select(m => m.MatterId).ToList(), callerUserId, ct);
        if (mattersCount.HasValue)
        {
            matters = await GetLinkedWorkAsync(courtCaseId, callerUserId, ct);
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

        var courtWs = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Code == WorkstreamCodes.CourtReferences, ct);
        var canScheduleCreate = courtWs != null && await scheduleAuth.CanCreateScheduledEventAsync(
            courtWs.Id,
            courtCase.ResponsibleOfficeDeskId,
            courtCase.AssignedUserId,
            callerUserId,
            ct);

        var canPromote = authNextDate.HasValue && candidateSchedule == null && (canEdit || canManageProc) && canScheduleCreate;
        var canLinkAward = canEdit && awardsCount.HasValue;
        var canLinkKhasra = canEdit && khasrasCount.HasValue;
        var canLinkMatter = canEdit && mattersCount.HasValue;

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
            activeScheduleNextDate,
            isProjectedToCalendar,
            activeScheduledEventId,
            activeScheduleNextDate ?? authNextDate,
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
            capabilities,
            operational.OperationalNdoh,
            operational.UsesAssistedStatus ? "DHC assisted case status" : operational.UsesHistoricalListing ? "DHC historical cause list" : operational.UsesExternalListing ? "DHC Cause List" : operational.OperationalNdoh.HasValue ?
                latestProceeding?.ProceedingDate.HasValue == true ? "Court proceeding" : "Office register" : null
        );
    }

    public async Task<IReadOnlyList<CourtProceedingDto>> GetProceedingsAsync(Guid courtCaseId, Guid callerUserId, CancellationToken ct = default)
    {
        var canView = await courtAuth.CanViewCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canView) throw new UnauthorizedAccessException("Forbidden");

        var rawProceedings = await db.CourtProceedings.AsNoTracking()
            .Where(p => p.CourtCaseId == courtCaseId && p.RecordStatus == RecordStatus.Active)
            .OrderByDescending(p => p.ProceedingDate.HasValue)
            .ThenByDescending(p => p.ProceedingDate)
            .ThenByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .ToListAsync(ct);

        var firstId = rawProceedings.FirstOrDefault()?.Id;

        return rawProceedings.Select(p => new CourtProceedingDto(
            p.Id,
            p.CourtCaseId,
            p.ProceedingDate,
            p.OrderType,
            p.RestraintNature,
            p.Summary,
            p.NextDate,
            p.CreatedAt,
            p.Id == firstId,
            p.CreatedBy,
            p.SourceKind
        )).ToList();
    }

    public async Task<IReadOnlyList<CourtCaseDocumentDto>> GetDocumentsAsync(Guid courtCaseId, Guid callerUserId, CancellationToken ct = default)
    {
        var canView = await courtAuth.CanViewCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canView) throw new UnauthorizedAccessException("Forbidden");

        return await db.CourtCaseDocuments.AsNoTracking()
            .Where(cd => cd.CourtCaseId == courtCaseId && cd.RecordStatus == RecordStatus.Active)
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
                cd.CreatedAt
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

        var linkedMatters = await db.CourtCaseMatters.AsNoTracking()
            .Include(m => m.Matter).ThenInclude(mat => mat.Workstream)
            .Where(m => m.CourtCaseId == courtCaseId && m.RecordStatus == RecordStatus.Active)
            .ToListAsync(ct);

        var matterScopes = await GetScopesForPermissionAsync(callerUserId, PermissionCodes.MatterView, ct);
        if (matterScopes.Count == 0) return [];

        var draftScopes = await GetScopesForPermissionAsync(callerUserId, PermissionCodes.DraftView, ct);
        var workItemScopes = await GetScopesForPermissionAsync(callerUserId, PermissionCodes.WorkItemView, ct);

        var authMatters = new List<CourtCaseLinkedMatterDto>();
        foreach (var m in linkedMatters)
        {
            if (!await courtAuth.CanAccessMatterAsync(m.MatterId, callerUserId, ct))
                continue;

            int? draftsCount = null;
            if (draftScopes.Count > 0)
            {
                var matterDraftIds = await db.MatterDrafts.AsNoTracking()
                    .Where(d => d.MatterId == m.MatterId && d.RecordStatus == RecordStatus.Active)
                    .Select(d => d.Id).ToListAsync(ct);

                var authDraftCount = 0;
                var hasEffectiveDraftAccess = false;

                if (draftScopes.Contains(ScopeMode.All))
                {
                    hasEffectiveDraftAccess = true;
                }
                else if (draftScopes.Contains(ScopeMode.Workstream))
                {
                    var isWsMember = m.Matter.WorkstreamId.HasValue && await db.UserWorkstreamMemberships.AsNoTracking()
                        .AnyAsync(w => w.UserId == callerUserId && w.WorkstreamId == m.Matter.WorkstreamId.Value && w.IsActive && w.Workstream.IsActive && w.Workstream.RecordStatus == RecordStatus.Active, ct);
                    if (isWsMember) hasEffectiveDraftAccess = true;
                }

                foreach (var did in matterDraftIds)
                {
                    if (await courtAuth.CanAccessDraftAsync(did, callerUserId, ct))
                    {
                        authDraftCount++;
                        hasEffectiveDraftAccess = true;
                    }
                }

                if (hasEffectiveDraftAccess)
                {
                    draftsCount = authDraftCount;
                }
            }

            int? workItemsCount = null;
            if (workItemScopes.Count > 0)
            {
                var matterWorkItemIds = await db.Set<WorkItemMatterLink>().AsNoTracking()
                    .Where(w => w.MatterId == m.MatterId && w.RecordStatus == RecordStatus.Active)
                    .Select(w => w.WorkItemId).ToListAsync(ct);

                var authWorkItemCount = 0;
                var hasEffectiveWorkItemAccess = false;

                if (workItemScopes.Contains(ScopeMode.All))
                {
                    hasEffectiveWorkItemAccess = true;
                }
                else if (workItemScopes.Contains(ScopeMode.Workstream))
                {
                    var isWsMember = m.Matter.WorkstreamId.HasValue && await db.UserWorkstreamMemberships.AsNoTracking()
                        .AnyAsync(w => w.UserId == callerUserId && w.WorkstreamId == m.Matter.WorkstreamId.Value && w.IsActive && w.Workstream.IsActive && w.Workstream.RecordStatus == RecordStatus.Active, ct);
                    if (isWsMember) hasEffectiveWorkItemAccess = true;
                }

                foreach (var wid in matterWorkItemIds)
                {
                    if (await courtAuth.CanAccessWorkItemAsync(wid, callerUserId, ct))
                    {
                        authWorkItemCount++;
                        hasEffectiveWorkItemAccess = true;
                    }
                }

                if (hasEffectiveWorkItemAccess)
                {
                    workItemsCount = authWorkItemCount;
                }
            }

            authMatters.Add(new CourtCaseLinkedMatterDto(
                m.MatterId,
                m.Matter.Title,
                m.Matter.ReferenceNumber,
                m.Matter.Status,
                m.Matter.Workstream?.Name,
                draftsCount,
                workItemsCount
            ));
        }

        return authMatters;
    }

    private async Task<int?> GetEffectiveAwardsCountAsync(IReadOnlyList<Guid> awardIds, Guid callerUserId, CancellationToken ct)
    {
        var scopes = await GetScopesForPermissionAsync(callerUserId, PermissionCodes.AwardView, ct);
        if (scopes.Count == 0) return null;

        if (scopes.Contains(ScopeMode.All))
        {
            var cnt = 0;
            foreach (var aid in awardIds)
            {
                if (await courtAuth.CanAccessAwardAsync(aid, callerUserId, ct)) cnt++;
            }
            return cnt;
        }

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var isMember = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == callerUserId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active
                            && m.Workstream.Code == WorkstreamCodes.Award, ct);
            if (!isMember) return null;

            var cnt = 0;
            foreach (var aid in awardIds)
            {
                if (await courtAuth.CanAccessAwardAsync(aid, callerUserId, ct)) cnt++;
            }
            return cnt;
        }

        var authCount = 0;
        foreach (var aid in awardIds)
        {
            if (await courtAuth.CanAccessAwardAsync(aid, callerUserId, ct)) authCount++;
        }
        return authCount > 0 ? authCount : null;
    }

    private async Task<int?> GetEffectiveKhasrasCountAsync(IReadOnlyList<Guid> khasraIds, Guid callerUserId, CancellationToken ct)
    {
        var scopes = await GetScopesForPermissionAsync(callerUserId, PermissionCodes.KhasraView, ct);
        if (scopes.Count == 0) return null;

        if (scopes.Contains(ScopeMode.All))
        {
            var cnt = 0;
            foreach (var kid in khasraIds)
            {
                if (await courtAuth.CanAccessKhasraAsync(kid, callerUserId, ct)) cnt++;
            }
            return cnt;
        }

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var isMember = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == callerUserId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active
                            && (m.Workstream.Code == WorkstreamCodes.LandRecords || m.Workstream.Code == WorkstreamCodes.Award), ct);
            if (!isMember) return null;

            var cnt = 0;
            foreach (var kid in khasraIds)
            {
                if (await courtAuth.CanAccessKhasraAsync(kid, callerUserId, ct)) cnt++;
            }
            return cnt;
        }

        var authCount = 0;
        foreach (var kid in khasraIds)
        {
            if (await courtAuth.CanAccessKhasraAsync(kid, callerUserId, ct)) authCount++;
        }
        return authCount > 0 ? authCount : null;
    }

    private async Task<int?> GetEffectiveMattersCountAsync(IReadOnlyList<Guid> matterIds, Guid callerUserId, CancellationToken ct)
    {
        var scopes = await GetScopesForPermissionAsync(callerUserId, PermissionCodes.MatterView, ct);
        if (scopes.Count == 0) return null;

        if (scopes.Contains(ScopeMode.All))
        {
            var cnt = 0;
            foreach (var mid in matterIds)
            {
                if (await courtAuth.CanAccessMatterAsync(mid, callerUserId, ct)) cnt++;
            }
            return cnt;
        }

        if (scopes.Contains(ScopeMode.Workstream))
        {
            var hasAnyMatterWsMembership = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == callerUserId
                            && m.IsActive
                            && m.Workstream.IsActive
                            && m.Workstream.RecordStatus == RecordStatus.Active, ct);
            if (!hasAnyMatterWsMembership) return null;

            var cnt = 0;
            foreach (var mid in matterIds)
            {
                if (await courtAuth.CanAccessMatterAsync(mid, callerUserId, ct)) cnt++;
            }
            return cnt;
        }

        var authCount = 0;
        foreach (var mid in matterIds)
        {
            if (await courtAuth.CanAccessMatterAsync(mid, callerUserId, ct)) authCount++;
        }
        return authCount > 0 ? authCount : null;
    }

    private async Task<List<ScopeMode>> GetScopesForPermissionAsync(Guid userId, string permissionCode, CancellationToken ct)
    {
        return await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join rp in db.RolePermissions on r.Id equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId && r.IsActive && r.RecordStatus == RecordStatus.Active
               && p.Code == permissionCode
            select rp.ScopeMode
        ).Distinct().ToListAsync(ct);
    }

    private async Task<bool> CanReadLinkedLandAsync(Guid userId, string permissionCode,
        string[] workstreamCodes, CancellationToken ct)
    {
        var scopes = await GetScopesForPermissionAsync(userId, permissionCode, ct);
        if (scopes.Contains(ScopeMode.All)) return true;
        if (!scopes.Contains(ScopeMode.Workstream)) return false;
        return await db.UserWorkstreamMemberships.AsNoTracking().AnyAsync(m =>
            m.UserId == userId && m.IsActive && m.Workstream.IsActive &&
            m.Workstream.RecordStatus == RecordStatus.Active && workstreamCodes.Contains(m.Workstream.Code), ct);
    }

    private async Task<List<CourtFilterOptionDto>> GetAuthorizedDesksForScopesAsync(List<ScopeMode> scopeModes, Guid callerUserId, CancellationToken ct)
    {
        if (scopeModes.Count == 0) return [];

        var query = db.OfficeDesks.AsNoTracking()
            .Include(d => d.Workstream)
            .Where(d => d.IsActive && d.RecordStatus == RecordStatus.Active
                     && d.Workstream != null && d.Workstream.Code == WorkstreamCodes.CourtReferences);

        if (scopeModes.Contains(ScopeMode.All))
        {
            // All court desks
        }
        else if (scopeModes.Contains(ScopeMode.Workstream))
        {
            var isCourtWsMember = await db.UserWorkstreamMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == callerUserId && m.IsActive && m.Workstream.IsActive && m.Workstream.RecordStatus == RecordStatus.Active && m.Workstream.Code == WorkstreamCodes.CourtReferences, ct);

            if (!isCourtWsMember) return [];
        }
        else if (scopeModes.Contains(ScopeMode.Assigned))
        {
            var userDeskIds = await db.UserDeskMemberships.AsNoTracking()
                .Where(m => m.UserId == callerUserId && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active)
                .Select(m => m.OfficeDeskId)
                .ToListAsync(ct);

            query = query.Where(d => userDeskIds.Contains(d.Id));
        }
        else
        {
            return [];
        }

        return await query
            .OrderBy(d => d.Name)
            .Select(d => new CourtFilterOptionDto(d.Id, d.Name, d.Workstream != null ? d.Workstream.Name : null))
            .ToListAsync(ct);
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

        var caseTypes = await authorized
            .Where(c => c.CaseType != null)
            .Select(c => c.CaseType!)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(ct);

        var directoryOfficers = await authorized
            .Where(c => c.AssignedUserId != null && c.AssignedUser != null &&
                        c.AssignedUser.IsActive && c.AssignedUser.RecordStatus == RecordStatus.Active)
            .Select(c => new { Id = c.AssignedUserId!.Value, Name = c.AssignedUser!.DisplayName })
            .Distinct()
            .OrderBy(x => x.Name)
            .Select(x => new CourtFilterOptionDto(x.Id, x.Name))
            .ToListAsync(ct);

        var viewScopes = await GetScopesForPermissionAsync(callerUserId, PermissionCodes.CourtView, ct);
        var createScopes = await GetScopesForPermissionAsync(callerUserId, PermissionCodes.CourtCreate, ct);
        var assignScopes = await GetScopesForPermissionAsync(callerUserId, PermissionCodes.CourtAssign, ct);

        var viewDesks = await GetAuthorizedDesksForScopesAsync(viewScopes, callerUserId, ct);
        var createDesks = await GetAuthorizedDesksForScopesAsync(createScopes, callerUserId, ct);
        var assignTargetDesks = await GetAuthorizedDesksForScopesAsync(assignScopes, callerUserId, ct);

        var allDeskIds = viewDesks.Concat(createDesks).Concat(assignTargetDesks).Select(d => d.Id).Distinct().ToList();

        var officers = await db.UserDeskMemberships.AsNoTracking()
            .Where(m => allDeskIds.Contains(m.OfficeDeskId)
                     && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active
                     && m.User.IsActive && m.User.RecordStatus == RecordStatus.Active)
            .Select(m => m.User)
            .Distinct()
            .OrderBy(u => u.DisplayName)
            .Select(u => new CourtFilterOptionDto(u.Id, u.DisplayName, null))
            .ToListAsync(ct);

        return new CourtFilterOptionsDto(courtNames, statuses, viewDesks, officers, viewDesks, createDesks, assignTargetDesks, caseTypes, directoryOfficers);
    }
}
