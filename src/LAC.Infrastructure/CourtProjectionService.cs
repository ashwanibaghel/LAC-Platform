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
    DateOnly? NextHearingDate,
    DateOnly? LastHearingDate,
    int AwardsCount,
    int KhasrasCount,
    int MattersCount,
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
    bool CanManageDocuments
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

public sealed record CourtFilterOptionsDto(
    IReadOnlyList<string> CourtNames,
    IReadOnlyList<string> Statuses,
    IReadOnlyList<FilterOptionDto> Desks,
    IReadOnlyList<FilterOptionDto> Officers
);

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
                ActiveScheduleNextDate = db.ScheduledEvents
                    .Where(se => se.CourtCaseId == c.Id && se.Origin == ScheduledEventOrigin.CourtProceeding && se.Status == ScheduledEventStatus.Scheduled && se.RecordStatus == RecordStatus.Active)
                    .Select(se => (DateOnly?)se.ScheduledDate)
                    .FirstOrDefault(),
                LatestProceedingDate = c.Proceedings
                    .Where(p => p.RecordStatus == RecordStatus.Active && p.ProceedingDate.HasValue)
                    .OrderByDescending(p => p.ProceedingDate)
                    .ThenByDescending(p => p.CreatedAt)
                    .Select(p => p.ProceedingDate)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var resultList = items.Select(x => new CourtCaseSummaryDto(
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
            x.ActiveScheduleNextDate,
            x.LatestProceedingDate,
            x.AwardsCount,
            x.KhasrasCount,
            x.MattersCount,
            x.PartiesCount,
            x.DocumentsCount,
            x.ProceedingsCount,
            x.LastActivityAt
        )).ToList();

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

        var activeSchedule = await db.ScheduledEvents.AsNoTracking()
            .FirstOrDefaultAsync(se => se.CourtCaseId == courtCaseId
                                    && se.Origin == ScheduledEventOrigin.CourtProceeding
                                    && se.Status == ScheduledEventStatus.Scheduled
                                    && se.RecordStatus == RecordStatus.Active, ct);

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

        var awards = courtCase.Awards.Select(a => new CourtCaseLinkedAwardDto(
            a.AwardId,
            a.Award.AwardNumber,
            null,
            a.Award.VillageLinks.Select(v => v.Village.Name).Distinct().ToList()
        )).ToList();

        var khasras = courtCase.Khasras.Select(k => new CourtCaseLinkedKhasraDto(
            k.KhasraId,
            k.Khasra.VillageId,
            k.Khasra.Village?.Name ?? "",
            k.Khasra.NormalizedNumber,
            k.Khasra.Qualifier,
            k.Khasra.TotalArea,
            k.Khasra.AreaUnit
        )).ToList();

        var linkedMatterIds = courtCase.Matters.Select(m => m.MatterId).ToList();
        var draftsCountByMatter = await db.MatterDrafts.AsNoTracking()
            .Where(d => linkedMatterIds.Contains(d.MatterId) && d.RecordStatus == RecordStatus.Active)
            .GroupBy(d => d.MatterId)
            .Select(g => new { MatterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.MatterId, g => g.Count, ct);

        var workItemsCountByMatter = await db.Set<WorkItemMatterLink>().AsNoTracking()
            .Where(w => linkedMatterIds.Contains(w.MatterId) && w.RecordStatus == RecordStatus.Active)
            .GroupBy(w => w.MatterId)
            .Select(g => new { MatterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.MatterId, g => g.Count, ct);

        var matters = courtCase.Matters.Select(m => new CourtCaseLinkedMatterDto(
            m.MatterId,
            m.Matter.Title,
            m.Matter.ReferenceNumber,
            m.Matter.Status,
            m.Matter.Workstream?.Name,
            draftsCountByMatter.GetValueOrDefault(m.MatterId, 0),
            workItemsCountByMatter.GetValueOrDefault(m.MatterId, 0)
        )).ToList();

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

        var capabilities = new CourtCaseCapabilitiesDto(
            CanEdit: canEdit,
            CanAssign: canAssign,
            CanManageProceedings: canManageProc,
            CanManageDocuments: canManageDoc
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
            activeSchedule?.ScheduledDate ?? latestProceeding?.NextDate,
            latestProceeding?.ProceedingDate,
            latestProceeding?.OrderType,
            latestProceeding?.RestraintNature,
            latestProceeding?.Summary,
            awards,
            khasras,
            matters,
            parties,
            reps,
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

        var linkedMatters = await db.CourtCaseMatters.AsNoTracking()
            .Include(m => m.Matter).ThenInclude(mat => mat.Workstream)
            .Where(m => m.CourtCaseId == courtCaseId && m.RecordStatus == RecordStatus.Active)
            .ToListAsync(ct);

        var matterIds = linkedMatters.Select(m => m.MatterId).ToList();

        var draftsCount = await db.MatterDrafts.AsNoTracking()
            .Where(d => matterIds.Contains(d.MatterId) && d.RecordStatus == RecordStatus.Active)
            .GroupBy(d => d.MatterId)
            .Select(g => new { MatterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.MatterId, g => g.Count, ct);

        var workItemsCount = await db.Set<WorkItemMatterLink>().AsNoTracking()
            .Where(w => matterIds.Contains(w.MatterId) && w.RecordStatus == RecordStatus.Active)
            .GroupBy(w => w.MatterId)
            .Select(g => new { MatterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.MatterId, g => g.Count, ct);

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

        var desks = await db.OfficeDesks.AsNoTracking()
            .Where(d => d.IsActive && d.RecordStatus == RecordStatus.Active)
            .OrderBy(d => d.Name)
            .Select(d => new FilterOptionDto(d.Id, d.Name))
            .ToListAsync(ct);

        var officers = await db.AppUsers.AsNoTracking()
            .Where(u => u.IsActive && u.RecordStatus == RecordStatus.Active)
            .OrderBy(u => u.DisplayName)
            .Select(u => new FilterOptionDto(u.Id, u.DisplayName))
            .ToListAsync(ct);

        return new CourtFilterOptionsDto(courtNames, statuses, desks, officers);
    }
}
