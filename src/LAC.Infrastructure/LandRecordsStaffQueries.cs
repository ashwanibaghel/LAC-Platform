using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed partial class LandRecordsStaffWorkflowService
{
    public async Task AuthorizeDocumentReadAsync(Guid documentId, CancellationToken ct)
    {
        _ = Actor;
        var links = await db.DocumentAwards.Include(x => x.Award).Where(x => x.DocumentId == documentId).ToListAsync(ct);
        foreach (var link in links)
        {
            var context = Context(link.Award.CreatedBy, link.CoreDocumentRole == "PossessionProceeding" ? WorkstreamCodes.Possession : WorkstreamCodes.Award);
            if (await access.CanAsync(PermissionCodes.AwardView, context, ct) &&
                (!link.IsDetached && link.Award.RecordStatus == RecordStatus.Active || await access.CanAsync(PermissionCodes.AuditView, context, ct))) return;
        }
        var notifications = await db.DocumentNotifications.Include(x => x.Notification).Where(x => x.DocumentId == documentId).ToListAsync(ct);
        foreach (var link in notifications)
            if (await access.CanAsync(PermissionCodes.AwardView, Context(link.Notification.CreatedBy), ct) &&
                (link.Notification.RecordStatus == RecordStatus.Active || await access.CanAsync(PermissionCodes.AuditView, Context(link.Notification.CreatedBy), ct))) return;
        var occupations = await db.Set<TemporaryAcquisitionProceeding>().Where(x => x.DocumentId == documentId).ToListAsync(ct);
        foreach (var item in occupations) if (await access.CanAsync(PermissionCodes.AwardView, Context(item.CreatedBy, WorkstreamCodes.LandRecords), ct)) return;
        var nms = await db.NmDocuments.Where(x => x.DocumentId == documentId && x.AwardId == null).ToListAsync(ct);
        foreach (var nm in nms) if (await access.CanAsync(PermissionCodes.AwardView, Context(nm.CreatedBy), ct)) return;
        throw new StaffWorkflowException("Forbidden", "You do not have live access to this source PDF.", 403);
    }
    public async Task<object> WorkspaceAsync(Guid id, CancellationToken ct)
    {
        var award = await AwardAsync(id, PermissionCodes.AwardView, false, ct);
        if (award.RecordStatus != RecordStatus.Active) await AuthorizeAsync(PermissionCodes.AuditView, award.CreatedBy, ct: ct);
        var attachments = await AttachmentsAsync(id, includeDetached: true, ct);
        var notifications = await db.AwardNotifications.AsNoTracking().Where(x => x.AwardId == id && x.Notification.RecordStatus == RecordStatus.Active)
            .Select(x => new { x.Notification.Id, x.Notification.SectionType, x.Notification.NotificationNumber, x.Notification.NotificationDate,
                x.Notification.IsFinalized, x.Notification.NumberAbsentReason, x.Notification.Revision,
                needsReview = !x.Notification.IsFinalized || x.Notification.NotificationDate == null,
                documents = x.Notification.DocumentRelationships.Select(d => new { d.DocumentId, d.Document.OriginalFileName, d.Document.Sha256Hash }).ToList() }).ToListAsync(ct);
        var sourceFacts = await db.SourceEvidence.AsNoTracking().Where(x => x.AwardId == id)
            .Select(x => new { x.FactName, x.VerifiedBy, x.VerifiedAt, x.DocumentId, x.PageNumber, x.ConfirmedValueJson }).ToListAsync(ct);
        return new { award.Id, award.AwardNumber, award.AwardDate, award.AwardType, award.ActRegime, award.Purpose, award.AcquisitionProjectId,
            award.Remarks, award.Status, award.Revision, award.RecordStatus, award.ArchiveKind,
            metadataProvenance = sourceFacts.Any(x => new[] { "awardNumber", "awardDate", "awardType", "purpose" }.Contains(x.FactName)) ? "SourceReviewed" : "Manual",
            sourceFacts, attachments, notifications, villageIds = await db.AwardVillages.Where(x => x.AwardId == id).Select(x => x.VillageId).ToListAsync(ct),
            villages = await db.AwardVillages.Where(x => x.AwardId == id).Select(x => new { x.VillageId, x.Village.Name }).ToListAsync(ct) };
    }
    public async Task<object> AttachmentsAsync(Guid id, bool includeDetached, CancellationToken ct)
    {
        var award = await AwardAsync(id, PermissionCodes.AwardView, false, ct);
        if (award.RecordStatus != RecordStatus.Active) await AuthorizeAsync(PermissionCodes.AuditView, award.CreatedBy, ct: ct);
        var canPossession = await access.CanAsync(PermissionCodes.AwardView, Context(award.CreatedBy, WorkstreamCodes.Possession), ct);
        return await db.DocumentAwards.AsNoTracking().Where(x => x.AwardId == id && x.CoreDocumentRole != null &&
            (includeDetached || !x.IsDetached) && (canPossession || x.CoreDocumentRole != "PossessionProceeding"))
            .OrderBy(x => x.LegalDate == null).ThenBy(x => x.LegalDate).ThenBy(x => x.Document.UploadedAt).ThenBy(x => x.Id)
            .Select(x => new { attachmentId = x.Id, x.DocumentId, role = x.CoreDocumentRole, x.Document.OriginalFileName, x.Document.MimeType,
                x.Document.UploadedAt, sourceHash = x.Document.Sha256Hash, x.VillageId, x.LegalDate, x.ReferenceNumber, x.PartNumber, x.Title,
                x.Remarks, x.Revision, x.IsDetached, x.IsSharedSource, x.PossessionEventId, needsReview = x.CoreDocumentRole == "PossessionProceeding" && (x.LegalDate == null || x.PossessionEventId == null) }).ToListAsync(ct);
    }
    public async Task<object> TemporaryProceedingAsync(Guid id, CancellationToken ct)
    {
        var item = await db.Set<TemporaryAcquisitionProceeding>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new StaffWorkflowException("ProceedingNotFound", "Temporary proceeding was not found.", 404);
        await AuthorizeAsync(PermissionCodes.AwardView, item.CreatedBy, WorkstreamCodes.LandRecords, ct);
        return new { item.Id, item.VillageId, item.DocumentId, item.Nature, item.DocumentType, item.ActRegime, item.LegalSection, item.Purpose, item.ReferenceNumber,
            item.DurationMonths, item.LegalDate, item.CommencementDate, item.ExpiryDate, item.CompensationReference, item.Remarks, item.AcquisitionProjectId, item.AwardId,
            contentPath = $"/api/documents/{item.DocumentId}/content" };
    }
    public async Task<IReadOnlyList<AcquisitionTimelineEvent>> TimelineAsync(Guid villageId, CancellationToken ct)
    {
        await AuthorizeAsync(PermissionCodes.AwardView, workstream: WorkstreamCodes.LandRecords, ct: ct);
        if (!await db.Villages.AnyAsync(x => x.Id == villageId, ct)) throw new StaffWorkflowException("VillageNotFound", "Village was not found.", 404);
        var allAwards = await db.Awards.AsNoTracking().Where(x => x.RecordStatus == RecordStatus.Active &&
            (x.VillageLinks.Any(v => v.VillageId == villageId) || x.KhasraLinks.Any(k => k.Khasra.VillageId == villageId))).ToListAsync(ct);
        var awards = new List<Award>();
        foreach (var award in allAwards) if (await access.CanAsync(PermissionCodes.AwardView, Context(award.CreatedBy), ct)) awards.Add(award);
        var awardIds = awards.Select(x => x.Id).ToArray();
        var result = new List<AcquisitionTimelineEvent>();
        var coreSources = await db.DocumentAwards.AsNoTracking().Where(x => awardIds.Contains(x.AwardId) && !x.IsDetached && x.Document.RecordStatus == RecordStatus.Active &&
            (x.VillageId == null || x.VillageId == villageId)).Select(x => new { x.AwardId, x.DocumentId, x.CoreDocumentRole, x.PossessionEventId }).ToListAsync(ct);
        foreach (var award in awards)
            result.Add(Event("Award", award.Id, [award.Id], award.AwardDate, $"Award {award.AwardNumber}", $"/awards/{award.Id}", coreSources.Where(x => x.AwardId == award.Id && x.CoreDocumentRole == "Award").Select(x => x.DocumentId), []));
        var notifications = await db.Notifications.AsNoTracking().Where(x => x.RecordStatus == RecordStatus.Active &&
            (x.KhasraLinks.Any(k => k.Khasra.VillageId == villageId) || db.AwardNotifications.Any(a => awardIds.Contains(a.AwardId) && a.NotificationId == x.Id)))
            .Include(x => x.DocumentRelationships).Include(x => x.KhasraLinks).ToListAsync(ct);
        var notificationIds = notifications.Select(x => x.Id).ToArray();
        var notificationLinks = await db.AwardNotifications.AsNoTracking().Where(x => awardIds.Contains(x.AwardId) && notificationIds.Contains(x.NotificationId)).ToListAsync(ct);
        foreach (var notification in notifications)
        {
            if (!await access.CanAsync(PermissionCodes.AwardView, Context(notification.CreatedBy), ct)) continue;
            var linkedIds = notificationLinks.Where(x => x.NotificationId == notification.Id).Select(x => x.AwardId).Distinct().Order().ToArray();
            var warnings = new List<string>();
            if (!notification.IsFinalized) warnings.Add("Notification is incomplete or awaiting source review.");
            if (awards.Any(x => linkedIds.Contains(x.Id) && notification.NotificationDate > x.AwardDate)) warnings.Add("Notification follows Award date; review chronology against the source.");
            result.Add(Event("Notification", notification.Id, linkedIds, notification.NotificationDate, $"Section {notification.SectionType} · {notification.NotificationNumber}", $"/notifications/{notification.Id}", notification.DocumentRelationships.Select(x => x.DocumentId), warnings));
        }
        var ppAwards = new List<Guid>();
        foreach (var award in awards) if (await access.CanAsync(PermissionCodes.AwardView, Context(award.CreatedBy, WorkstreamCodes.Possession), ct)) ppAwards.Add(award.Id);
        var possessions = await db.PossessionEvents.AsNoTracking().Where(x => ppAwards.Contains(x.AwardId) && x.RecordStatus == RecordStatus.Active &&
            (!db.DocumentAwards.Any(d => d.PossessionEventId == x.Id) || db.DocumentAwards.Any(d => d.PossessionEventId == x.Id && !d.IsDetached && (d.VillageId == villageId || d.VillageId == null))))
            .ToListAsync(ct);
        var possessionIds = possessions.Select(x => x.Id).ToArray();
        var ppSources = await db.SourceEvidence.Where(x => x.PossessionEventId != null && possessionIds.Contains(x.PossessionEventId.Value)).Select(x => new { x.PossessionEventId, x.DocumentId }).ToListAsync(ct);
        foreach (var pp in possessions)
            result.Add(Event("PossessionProceedings", pp.Id, [pp.AwardId], pp.PossessionDate, "Possession Proceedings", $"/awards/{pp.AwardId}?possessionEventId={pp.Id}",
                coreSources.Where(x => x.PossessionEventId == pp.Id).Select(x => x.DocumentId).Concat(ppSources.Where(x => x.PossessionEventId == pp.Id).Select(x => x.DocumentId)), []));
        var temporary = await db.Set<TemporaryAcquisitionProceeding>().AsNoTracking().Where(x => x.VillageId == villageId && x.RecordStatus == RecordStatus.Active).ToListAsync(ct);
        foreach (var item in temporary)
        {
            if (!await access.CanAsync(PermissionCodes.AwardView, Context(item.CreatedBy, WorkstreamCodes.LandRecords), ct)) continue;
            var ids = item.AwardId is Guid id ? new[] { id } : [];
            result.Add(Event("TemporaryProceeding", item.Id, ids, item.LegalDate, $"{item.DocumentType} · Section {item.LegalSection} · {item.Purpose}", $"/api/temporary-acquisition-proceedings/{item.Id}", [item.DocumentId], []));
            if (item.CommencementDate is not null) result.Add(Event("TemporaryCommencement", item.Id, ids, item.CommencementDate, "Temporary occupation commencement", $"/api/temporary-acquisition-proceedings/{item.Id}", [item.DocumentId], []));
            if (item.ExpiryDate is not null) result.Add(Event("TemporaryExpiry", item.Id, ids, item.ExpiryDate, "Temporary occupation expiry", $"/api/temporary-acquisition-proceedings/{item.Id}", [item.DocumentId], []));
        }
        return result.OrderBy(x => x.LegalDate is null).ThenBy(x => x.LegalDate).ThenBy(x => x.EventId, StringComparer.Ordinal).ToList();
    }
    private static AcquisitionTimelineEvent Event(string type, Guid id, IReadOnlyList<Guid> awards, DateOnly? date, string title, string path, IEnumerable<Guid> documents, IReadOnlyList<string> warnings)
    {
        var issues = warnings.ToList(); if (date is null) issues.Add("Actual legal date is missing; legacy source needs review.");
        return new($"{type}:{id}", type, id, awards, date, title, path, documents.Distinct().Order().Select(x => new TimelineDocument(x, $"/api/documents/{x}/content")).ToList(), issues.Count > 0, issues);
    }
}
