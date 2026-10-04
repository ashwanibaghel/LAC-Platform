using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed record MatterContextMetadata(Guid Id, string Title, string MatterType, string Status, Guid? WorkstreamId,
    string? WorkstreamName, string? ReferenceNumber, string? Remarks, string? KhasraReferenceText, int Revision);
public sealed record MatterVillageSummary(Guid VillageId, string Name);
public sealed record MatterAwardSummary(Guid AwardId, string AwardNumber, DateOnly? AwardDate, bool IsPrimary);
public sealed record MatterKhasraSummary(Guid KhasraId, Guid VillageId, string DisplayNumber);
public sealed record MatterCourtSummary(Guid CourtCaseId, string CaseNumber, string? CaseTitle, string CourtName,
    string? CurrentStatus, DateOnly? OperationalNdoh);
public sealed record MatterDakSummary(Guid DakId, string DiaryNumber, string Subject, string Status, DateOnly ReceivedDate);
public sealed record MatterDeskSummary(Guid DeskId, string Name);
public sealed record MatterUserSummary(Guid UserId, string DisplayName);
public sealed record MatterWorkItemSummary(Guid WorkItemId, string Title, string Status, string Priority, DateTimeOffset? DueAt,
    MatterDeskSummary? ResponsibleDesk, MatterUserSummary? AssignedUser);
public sealed record MatterOutwardSummary(Guid OutwardId, string OutwardNumber, string Subject, string Status, DateOnly OutwardDate);
public sealed record MatterContextDto(MatterContextMetadata Matter, MatterVillageSummary? Village,
    IReadOnlyList<MatterAwardSummary> Awards, IReadOnlyList<MatterKhasraSummary> Khasras,
    IReadOnlyList<MatterCourtSummary> CourtCases, string CourtContextState, IReadOnlyList<MatterDakSummary> Daks,
    IReadOnlyList<MatterWorkItemSummary> WorkItems, IReadOnlyList<MatterOutwardSummary> Outwards,
    int OutwardCount, int DocumentCount, int DraftCount);

public sealed class MatterContextQuery(LacDbContext db, IMatterAuthorizationService matterAuth,
    ICourtAuthorizationService courtAuth, IDakAuthorizationService dakAuth,
    IWorkItemAuthorizationService workItemAuth, IOutwardAuthorizationService outwardAuth)
{
    public async Task<MatterContextDto> GetAsync(Guid matterId, Guid userId, CancellationToken ct = default)
    {
        if (!await matterAuth.CanAccessMatterAsync(matterId, PermissionCodes.MatterView, userId, ct))
            throw new MatterWorkflowException("Matter is unavailable or unauthorized.", 403);
        var matter = await db.Matters.AsNoTracking().Include(x => x.Workstream).SingleAsync(x => x.Id == matterId, ct);
        MatterVillageSummary? village = null;
        if (await courtAuth.CanAccessVillageAsync(matter.VillageId, userId, ct))
            village = await db.Villages.AsNoTracking().Where(x => x.Id == matter.VillageId)
                .Select(x => new MatterVillageSummary(x.Id, x.Name)).SingleOrDefaultAsync(ct);

        var awards = new List<MatterAwardSummary>();
        foreach (var link in await db.MatterAwards.AsNoTracking().Where(x => x.MatterId == matterId).OrderBy(x => x.AwardId).ToListAsync(ct))
            if (await courtAuth.CanAccessAwardAsync(link.AwardId, userId, ct))
            {
                var row = await db.Awards.AsNoTracking().Where(x => x.Id == link.AwardId)
                    .Select(x => new MatterAwardSummary(x.Id, x.AwardNumber, x.AwardDate, link.IsPrimary)).SingleAsync(ct);
                awards.Add(row);
            }
        var khasras = new List<MatterKhasraSummary>();
        foreach (var id in await db.MatterKhasras.AsNoTracking().Where(x => x.MatterId == matterId).OrderBy(x => x.KhasraId).Select(x => x.KhasraId).ToListAsync(ct))
            if (await courtAuth.CanAccessKhasraAsync(id, userId, ct))
                khasras.Add(await db.Khasras.AsNoTracking().Where(x => x.Id == id)
                    .Select(x => new MatterKhasraSummary(x.Id, x.VillageId, x.DisplayNumber)).SingleAsync(ct));

        var courtIds = new List<Guid>();
        foreach (var id in await db.CourtCaseMatters.AsNoTracking().Where(x => x.MatterId == matterId && x.RecordStatus == RecordStatus.Active)
                     .OrderBy(x => x.CourtCaseId).Select(x => x.CourtCaseId).Distinct().ToListAsync(ct))
            if (await courtAuth.CanViewCourtCaseAsync(id, userId, ct)) courtIds.Add(id);
        // Authorization is applied before resolving Court evidence, and the canonical resolver is shared with Court detail.
        var courts = courtIds.Count == 0 ? new List<MatterCourtSummary>() : await CourtOperationalNdohQuery.Resolve(
                db.CourtCases.AsNoTracking().Where(x => courtIds.Contains(x.Id)), db, DateOnly.FromDateTime(DateTime.UtcNow))
            .OrderBy(x => x.Case.CaseNumber).ThenBy(x => x.Case.Id)
            .Select(x => new MatterCourtSummary(x.Case.Id, x.Case.CaseNumber, x.Case.CaseTitle, x.Case.CourtName,
                x.Case.CurrentStatus, x.OperationalNdoh)).ToListAsync(ct);

        var daks = new List<MatterDakSummary>();
        foreach (var id in await db.DakMatterLinks.AsNoTracking().Where(x => x.MatterId == matterId && x.RecordStatus == RecordStatus.Active)
                     .OrderBy(x => x.DakId).Select(x => x.DakId).Distinct().ToListAsync(ct))
            if (await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakView, userId, ct))
                daks.Add(await db.Daks.AsNoTracking().Where(x => x.Id == id)
                    .Select(x => new MatterDakSummary(x.Id, x.DiaryNumber, x.Subject, x.Status.ToString(), x.ReceivedDate)).SingleAsync(ct));

        var workItems = new List<MatterWorkItemSummary>();
        foreach (var id in await db.WorkItemMatterLinks.AsNoTracking().Where(x => x.MatterId == matterId && x.RecordStatus == RecordStatus.Active
                     && x.WorkItem.RecordStatus == RecordStatus.Active && x.WorkItem.CompletedAt == null
                     && x.WorkItem.Status != WorkItemStatus.Completed && x.WorkItem.Status != WorkItemStatus.Cancelled)
                     .OrderBy(x => x.WorkItemId).Select(x => x.WorkItemId).Distinct().ToListAsync(ct))
            if (await workItemAuth.CanAccessWorkItemAsync(id, PermissionCodes.WorkItemView, userId, ct))
            {
                var item = await db.WorkItems.AsNoTracking().SingleAsync(x => x.Id == id, ct);
                var assignment = await db.WorkItemAssignments.AsNoTracking().Include(x => x.OfficeDesk).Include(x => x.AssignedUser)
                    .FirstOrDefaultAsync(x => x.WorkItemId == id && x.IsActive && x.RecordStatus == RecordStatus.Active, ct);
                var desk = assignment?.OfficeDesk is { IsActive: true, RecordStatus: RecordStatus.Active } d ? new MatterDeskSummary(d.Id, d.Name) : null;
                // WorkItem.View authorizes its responsibility metadata, matching the canonical WorkItem workspace.
                var user = assignment?.AssignedUser is { IsActive: true, RecordStatus: RecordStatus.Active } u ? new MatterUserSummary(u.Id, u.DisplayName) : null;
                workItems.Add(new MatterWorkItemSummary(item.Id, item.Title, item.Status.ToString(), item.Priority.ToString(), item.DueAt, desk, user));
            }

        var outwards = new List<MatterOutwardSummary>();
        var outwardList = await outwardAuth.AuthorizeListQueryAsync(db.Outwards.AsNoTracking()
            .Where(x => x.MatterId == matterId && x.RecordStatus == RecordStatus.Active), userId, ct);
        if (outwardList.HasPermission)
            outwards = await outwardList.Query.OrderByDescending(x => x.OutwardDate).ThenBy(x => x.Id)
                .Select(x => new MatterOutwardSummary(x.Id, x.OutwardNumber, x.Subject, x.Status.ToString(), x.OutwardDate)).ToListAsync(ct);

        var documentCount = await db.MatterDocuments.AsNoTracking().CountAsync(x => x.MatterId == matterId
            && x.RecordStatus == RecordStatus.Active && x.Document.RecordStatus == RecordStatus.Active && x.Document.Status == "Active", ct);
        var draftCount = 0;
        foreach (var id in await db.MatterDrafts.AsNoTracking().Where(x => x.MatterId == matterId && x.RecordStatus == RecordStatus.Active).Select(x => x.Id).ToListAsync(ct))
            if (await matterAuth.CanAccessDraftAsync(id, PermissionCodes.DraftView, userId, ct)) draftCount++;

        return new MatterContextDto(new MatterContextMetadata(matter.Id, matter.Title, matter.MatterType, matter.Status,
            matter.WorkstreamId, matter.Workstream?.Name, matter.ReferenceNumber, matter.Remarks, matter.KhasraReferenceText, matter.Revision),
            village, awards, khasras, courts, courts.Count == 0 ? "Court context not linked" : "Linked", daks, workItems,
            outwards, outwards.Count, documentCount, draftCount);
    }
}
