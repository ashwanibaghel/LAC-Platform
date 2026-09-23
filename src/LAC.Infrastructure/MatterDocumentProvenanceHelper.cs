namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public static class MatterDocumentProvenanceHelper
{
    public static async Task<Dictionary<Guid, string>> GetEligibleDocumentCandidateMapAsync(
        LacDbContext db,
        Matter matter,
        IAccessControlService accessControl,
        CancellationToken ct = default)
    {
        var candidateDocIds = new Dictionary<Guid, string>();

        var canAward = await accessControl.CanAsync(PermissionCodes.AwardView, new AccessResourceContext(WorkstreamCode: WorkstreamCodes.Award), ct);
        var canLr = await accessControl.CanAsync(PermissionCodes.LrView, new AccessResourceContext(WorkstreamCode: WorkstreamCodes.LandRecords), ct);

        if (canAward)
        {
            var linkedAwardIds = await db.MatterAwards.AsNoTracking()
                .Where(ma => ma.MatterId == matter.Id)
                .Select(ma => ma.AwardId)
                .ToListAsync(ct);

            if (linkedAwardIds.Count > 0)
            {
                var awardDocIds = await db.DocumentAwards.AsNoTracking()
                    .Where(da => linkedAwardIds.Contains(da.AwardId))
                    .Select(da => da.DocumentId)
                    .ToListAsync(ct);
                foreach (var did in awardDocIds) candidateDocIds[did] = "Selected Award";

                var nmDocIds = await db.NmDocuments.AsNoTracking()
                    .Where(nm => nm.AwardId.HasValue && linkedAwardIds.Contains(nm.AwardId.Value))
                    .Select(nm => nm.DocumentId)
                    .ToListAsync(ct);
                foreach (var did in nmDocIds) candidateDocIds[did] = "Selected Award NM";

                var notifDocIds = await db.AwardNotifications.AsNoTracking()
                    .Where(an => linkedAwardIds.Contains(an.AwardId))
                    .Join(db.DocumentNotifications.AsNoTracking(), an => an.NotificationId, dn => dn.NotificationId, (an, dn) => dn.DocumentId)
                    .ToListAsync(ct);
                foreach (var did in notifDocIds) candidateDocIds[did] = "Selected Award Notification";
            }
        }

        if (canLr)
        {
            // Only DocumentVillageLR and DocumentKhatauniRecord (NO bare DocumentVillage)
            var villageLrDocIds = await db.VillageLRs.AsNoTracking()
                .Where(vlr => vlr.VillageId == matter.VillageId && vlr.RecordStatus == RecordStatus.Active)
                .Join(db.DocumentVillageLRs.AsNoTracking(), vlr => vlr.Id, dvlr => dvlr.VillageLRId, (vlr, dvlr) => dvlr.DocumentId)
                .ToListAsync(ct);
            foreach (var did in villageLrDocIds) candidateDocIds.TryAdd(did, "Village LR");

            var khatauniDocIds = await db.KhatauniRecords.AsNoTracking()
                .Where(kr => kr.VillageId == matter.VillageId && kr.RecordStatus == RecordStatus.Active)
                .Join(db.DocumentKhatauniRecords.AsNoTracking(), kr => kr.Id, dkr => dkr.KhatauniRecordId, (kr, dkr) => dkr.DocumentId)
                .ToListAsync(ct);
            foreach (var did in khatauniDocIds) candidateDocIds.TryAdd(did, "Village Khatauni");
        }

        if (candidateDocIds.Count == 0)
            return candidateDocIds;

        // Filter: Document.RecordStatus == RecordStatus.Active AND Document.Status == "Active"
        var candidateKeys = candidateDocIds.Keys.ToList();
        var activeKeys = await db.Documents.AsNoTracking()
            .Where(d => candidateKeys.Contains(d.Id) && d.RecordStatus == RecordStatus.Active && (d.Status == "Active" || string.IsNullOrEmpty(d.Status)))
            .Select(d => d.Id)
            .ToListAsync(ct);

        var activeKeySet = activeKeys.ToHashSet();
        return candidateDocIds
            .Where(kvp => activeKeySet.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}
