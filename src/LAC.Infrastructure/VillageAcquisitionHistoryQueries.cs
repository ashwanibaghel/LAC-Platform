using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public static class VillageAcquisitionHistoryQueries
{
    public static async Task<VillageAcquisitionHistory?> ReadAsync(
        LacDbContext db, Guid villageId, bool includePossessionEvents, CancellationToken ct, bool includeTemporary = false)
    {
        if (!await db.Villages.AsNoTracking().AnyAsync(x => x.Id == villageId && x.RecordStatus == RecordStatus.Active, ct)) return null;

        // Match the existing Village workspace's explicit membership paths. Neither dates nor project identity establish membership.
        var awards = await db.Awards.AsNoTracking().Where(x => x.RecordStatus == RecordStatus.Active &&
            (x.VillageLinks.Any(link => link.VillageId == villageId) ||
             x.KhasraLinks.Any(link => link.Khasra.VillageId == villageId && link.Khasra.RecordStatus == RecordStatus.Active)))
            .Select(x => new { x.Id, x.AwardNumber, x.AwardDate, x.Status }).ToListAsync(ct);
        var awardIds = awards.Select(x => x.Id).ToList();

        // A parcel/Village link establishes relevance, not attribution to any Award sharing that parcel/Village.
        var notifications = await db.Notifications.AsNoTracking().Where(x => x.RecordStatus == RecordStatus.Active &&
            (x.KhasraLinks.Any(link => link.Khasra.VillageId == villageId && link.Khasra.RecordStatus == RecordStatus.Active) ||
             db.AwardNotifications.Any(link => link.NotificationId == x.Id && awardIds.Contains(link.AwardId))))
            .Select(x => new { x.Id, x.NotificationNumber, x.SectionType, x.NotificationDate }).ToListAsync(ct);
        var notificationIds = notifications.Select(x => x.Id).ToList();
        // Include all explicit owners in sharing metadata, even owners outside this Village or archived owners.
        // Such a link also prevents falsely labelling a canonical record as unassigned.
        var links = await db.AwardNotifications.AsNoTracking().Where(x => notificationIds.Contains(x.NotificationId))
            .Select(x => new { x.NotificationId, x.AwardId }).ToListAsync(ct);
        var owners = links.GroupBy(x => x.NotificationId).ToDictionary(x => x.Key,
            x => (IReadOnlyList<Guid>)x.Select(link => link.AwardId).Distinct().Order().ToList());

        AcquisitionHistoryEvent NotificationEvent(Guid id, string number, string section, DateOnly? date)
        {
            var linkedAwards = owners.GetValueOrDefault(id) ?? [];
            return new(id, "Notification", date, section, id, null, number, null, null,
                linkedAwards.Count == 0 ? "NoExplicitAwardNotificationLink" : "ExplicitAwardNotificationLink",
                linkedAwards.Count > 1, linkedAwards);
        }
        var notificationEvents = notifications.ToDictionary(x => x.Id,
            x => NotificationEvent(x.Id, x.NotificationNumber, x.SectionType, x.NotificationDate));
        var notificationEventsByAward = links.Where(x => awardIds.Contains(x.AwardId)).GroupBy(x => x.AwardId)
            .ToDictionary(x => x.Key, x => x.Select(link => link.NotificationId).Distinct().Select(id => notificationEvents[id]).ToList());

        var possessionEventsByAward = new Dictionary<Guid, List<AcquisitionHistoryEvent>>();
        if (includePossessionEvents)
        {
            var possessionEvents = await db.PossessionEvents.AsNoTracking().Where(x => x.RecordStatus == RecordStatus.Active && awardIds.Contains(x.AwardId))
                .Select(x => new { x.Id, x.AwardId, x.PossessionDate, x.EventType, x.Status }).ToListAsync(ct);
            possessionEventsByAward = possessionEvents.GroupBy(x => x.AwardId).ToDictionary(x => x.Key,
                x => x.Select(p => new AcquisitionHistoryEvent(p.Id, "Possession", p.PossessionDate, null, null, p.Id,
                    null, p.EventType, p.Status, "ExplicitPossessionAwardLink", false, [p.AwardId])).ToList());
        }

        var sourceLinks = await db.DocumentAwards.AsNoTracking().Where(x => awardIds.Contains(x.AwardId) && !x.IsDetached &&
            x.Document.RecordStatus == RecordStatus.Active && x.Document.Status == "Active")
            .Select(x => new { x.AwardId, x.PossessionEventId, x.CoreDocumentRole, x.DocumentId }).ToListAsync(ct);
        var notificationSources = await db.DocumentNotifications.AsNoTracking().Where(x => notificationIds.Contains(x.NotificationId) &&
            x.Document.RecordStatus == RecordStatus.Active && x.Document.Status == "Active").Select(x => new { x.NotificationId, x.DocumentId }).ToListAsync(ct);
        AcquisitionHistoryEvent WithSource(AcquisitionHistoryEvent ev, DateOnly? awardDate = null)
        {
            var ids = ev.Type == "Notification" ? notificationSources.Where(x => x.NotificationId == ev.EventId).Select(x => x.DocumentId) :
                ev.Type == "Award" ? sourceLinks.Where(x => x.AwardId == ev.EventId && x.CoreDocumentRole is null or "Award").Select(x => x.DocumentId) :
                sourceLinks.Where(x => x.PossessionEventId == ev.EventId).Select(x => x.DocumentId);
            return ev with { SourcePath = ev.Type == "Notification" ? $"/notifications/{ev.EventId}" : ev.Type == "Award" ? $"/awards/{ev.EventId}" : $"/awards/{ev.LinkedAwardIds[0]}",
                Documents = ids.Distinct().Order().Select(id => new AcquisitionHistoryDocument(id, $"/api/documents/{id}/content")).ToList(),
                Warnings = ev.Date is null ? ["Legal date needs review."] : ev.Type == "Notification" && awardDate is not null && ev.Date > awardDate ? ["Notification is after the Award date; review the source context."] : [] };
        }
        var histories = awards.OrderBy(x => x.AwardDate is null).ThenBy(x => x.AwardDate)
            .ThenBy(x => x.AwardNumber, StringComparer.Ordinal).ThenBy(x => x.Id).Select(award =>
            {
                var events = new List<AcquisitionHistoryEvent>
                {
                    new(award.Id, "Award", award.AwardDate, null, null, null, award.AwardNumber, null,
                        award.Status, "AwardRecord", false, [award.Id])
                };
                if (notificationEventsByAward.TryGetValue(award.Id, out var awardNotifications)) events.AddRange(awardNotifications);
                if (possessionEventsByAward.TryGetValue(award.Id, out var possessionEvents)) events.AddRange(possessionEvents);
                return new AwardAcquisitionHistory(award.Id, award.AwardNumber, award.AwardDate, Chronological(events.Select(ev => WithSource(ev, award.AwardDate))));
            }).ToList();
        var temporaryEvents = new List<AcquisitionHistoryEvent>();
        if (includeTemporary)
        {
            var temporary = await db.Set<TemporaryAcquisitionProceeding>().AsNoTracking().Where(x => x.VillageId == villageId && x.RecordStatus == RecordStatus.Active).ToListAsync(ct);
            foreach (var item in temporary)
            {
                AcquisitionHistoryEvent Event(string type, DateOnly? date) => new(item.Id, type, date, item.LegalSection, null, null, item.ReferenceNumber, item.DocumentType, null,
                    "ExplicitTemporaryVillageLink", false, item.AwardId is Guid id ? [id] : [], $"/api/temporary-acquisition-proceedings/{item.Id}",
                    [new(item.DocumentId, $"/api/documents/{item.DocumentId}/content")], date is null ? ["Legal date needs review."] : []);
                temporaryEvents.Add(Event("TemporaryProceeding", item.LegalDate));
                if (item.CommencementDate is not null) temporaryEvents.Add(Event("TemporaryCommencement", item.CommencementDate));
                if (item.ExpiryDate is not null) temporaryEvents.Add(Event("TemporaryExpiry", item.ExpiryDate));
            }
        }
        return new(villageId, histories, Chronological(notificationEvents.Values.Where(x => x.LinkedAwardIds.Count == 0).Select(ev => WithSource(ev))), includePossessionEvents, Chronological(temporaryEvents));
    }

    private static IReadOnlyList<AcquisitionHistoryEvent> Chronological(IEnumerable<AcquisitionHistoryEvent> events) =>
        events.OrderBy(x => x.Date is null).ThenBy(x => x.Date).ThenBy(x => x.Type, StringComparer.Ordinal).ThenBy(x => x.EventId).ToList();
}
