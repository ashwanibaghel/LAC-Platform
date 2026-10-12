using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed partial class LandRecordsStaffWorkflowService
{
    public async Task<NotificationUploadResult> UploadNotificationAsync(Guid awardId, NotificationUploadInput input, Stream source, string name, CancellationToken ct)
    {
        var award = await AwardAsync(awardId, PermissionCodes.AwardCoreDocumentUpload, true, ct);
        await AuthorizeAsync(PermissionCodes.AwardEdit, award.CreatedBy, ct: ct);
        if (input.NotificationId is null) await AuthorizeAsync(PermissionCodes.AwardCreate, award.CreatedBy, ct: ct);
        await using var pdf = await PreparedLandRecordPdf.ReadAsync(source, name, MaxPdfBytes, ct);
        var files = new LandRecordPdfStore(db, storage);
        return await mutations.ExecuteAsync([$"pdf:{pdf.Hash}", $"award:{awardId}", "notification-identities"], $"notification-upload:{awardId}", input.MutationId,
            new { input, sourceHash = pdf.Hash }, async () =>
        {
            await AwardAsync(awardId, PermissionCodes.AwardEdit, true, ct);
            if (!await db.AwardVillages.AnyAsync(x => x.AwardId == awardId && x.VillageId == input.VillageId, ct)) throw new StaffWorkflowException("VillageContextRequired", "Notification Village must be linked to this Award.");
            Notification notification;
            if (input.NotificationId is Guid selected)
            {
                notification = await db.Notifications.SingleOrDefaultAsync(x => x.Id == selected && x.RecordStatus == RecordStatus.Active, ct) ?? throw new StaffWorkflowException("NotificationNotFound", "Notification was not found.", 404);
                await AuthorizeAsync(PermissionCodes.AwardEdit, notification.CreatedBy, ct: ct);
                if (input.SectionType is not null || input.NotificationNumber is not null || input.NotificationDate is not null || input.NumberAbsentReason is not null)
                    throw new StaffWorkflowException("ReuseMetadataConflict", "Explicit reuse accepts only notificationId; correct canonical metadata separately.");
            }
            else
            {
                var section = Clean(input.SectionType) ?? throw new StaffWorkflowException("SectionRequired", "Notification section/subsection is required.");
                var number = Clean(input.NotificationNumber) ?? "";
                if (section.Length > 200 || number.Length > 500) throw new StaffWorkflowException("MetadataTooLong", "Notification reference or section is too long.");
                if (input.NotificationDate is null) throw new StaffWorkflowException("NotificationDateRequired", "Actual notification issue date is required for source registration.");
                if (number.Length == 0 && Clean(input.NumberAbsentReason) is null) throw new StaffWorkflowException("NumberAbsenceReasonRequired", "Record why the source notification number is absent.");
                var existing = await db.Notifications.AsNoTracking().Where(x => x.RecordStatus == RecordStatus.Active && x.SectionType == section && x.NotificationNumber == number && x.NotificationDate == input.NotificationDate).Select(x => x.Id).ToListAsync(ct);
                if (number.Length > 0 && existing.Count > 0) throw new StaffWorkflowException("NotificationIdentityConflict", "Select the existing canonical Notification explicitly.", 409, existing);
                notification = new Notification { SectionType = section, NotificationNumber = number, NotificationDate = input.NotificationDate,
                    NumberAbsentReason = Clean(input.NumberAbsentReason), Remarks = Clean(input.Remarks), IsFinalized = true };
                db.Add(notification);
            }
            var document = await files.GetOrStoreAsync(pdf, name, "Notification", Actor, ct);
            var otherNotifications = await db.DocumentNotifications.Where(x => x.DocumentId == document.Id && x.NotificationId != notification.Id).Select(x => x.NotificationId).ToListAsync(ct);
            if (otherNotifications.Count > 0) throw new StaffWorkflowException("NotificationSourceConflict", "This PDF already belongs to another canonical Notification. Select that Notification explicitly.", 409, otherNotifications);
            if (await db.DocumentAwards.AnyAsync(x => x.DocumentId == document.Id && !x.IsDetached, ct) || await db.Set<TemporaryAcquisitionProceeding>().AnyAsync(x => x.DocumentId == document.Id, ct))
                throw new StaffWorkflowException("SourceRoleConflict", "This PDF is already registered as Award/possession/temporary evidence.", 409);
            if (!await db.DocumentNotifications.AnyAsync(x => x.NotificationId == notification.Id && x.DocumentId == document.Id, ct)) db.Add(new DocumentNotification { Notification = notification, Document = document });
            if (!await db.AwardNotifications.AnyAsync(x => x.NotificationId == notification.Id && x.AwardId == awardId, ct)) db.Add(new AwardNotification { Notification = notification, AwardId = awardId });
            if (!await db.DocumentVillages.AnyAsync(x => x.DocumentId == document.Id && x.VillageId == input.VillageId, ct)) db.Add(new DocumentVillage { Document = document, VillageId = input.VillageId });
            Audit(nameof(Notification), notification.Id, "StaffNotificationSourceLinked", null, new { awardId, input.VillageId, documentId = document.Id, notification.IsFinalized });
            await db.SaveChangesAsync(ct);
            return new NotificationUploadResult(notification.Id, document.Id, notification.IsFinalized, !notification.IsFinalized || notification.NotificationDate is null,
                notification.Revision, notification.NotificationDate > award.AwardDate ? "Notification follows Award date; review chronology against the source." : null);
        }, ct, files.CleanupAsync);
    }

    public async Task<NotificationUploadResult> FinalizeNotificationAsync(Guid id, FinalizeNotificationInput input, CancellationToken ct)
    {
        var authorized = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new StaffWorkflowException("NotificationNotFound", "Notification was not found.", 404);
        await AuthorizeAsync(PermissionCodes.AwardEdit, authorized.CreatedBy, ct: ct);
        return await mutations.ExecuteAsync([$"notification:{id}"], $"notification-finalize:{id}", input.MutationId, input, async () =>
        {
            var notification = await db.Notifications.SingleAsync(x => x.Id == id, ct); Revision(notification.Revision, input.ExpectedRevision); Reason(input.Reason);
            if (notification.RecordStatus != RecordStatus.Active || notification.NotificationDate is null || Clean(notification.SectionType) is null ||
                Clean(notification.NotificationNumber) is null && Clean(notification.NumberAbsentReason) is null)
                throw new StaffWorkflowException("NotificationIncomplete", "Finalization requires actual date, section and number or documented absence.");
            var document = await db.DocumentNotifications.Where(x => x.NotificationId == id && x.Document.RecordStatus == RecordStatus.Active && x.Document.Status == "Active" && x.Document.Sha256Hash != null)
                .Select(x => x.Document).FirstOrDefaultAsync(ct) ?? throw new StaffWorkflowException("SourceRequired", "A linked source PDF is required.");
            if (!await db.AwardNotifications.AnyAsync(x => x.NotificationId == id && x.Award.RecordStatus == RecordStatus.Active && x.Award.VillageLinks.Any(), ct)) throw new StaffWorkflowException("AwardContextRequired", "An active Award/Village context is required.");
            await new LandRecordPdfStore(db, storage).VerifyAsync(document.StoragePath, document.Sha256Hash!, document.FileSize, ct);
            Audit(nameof(Notification), id, "StaffNotificationFinalized", new { notification.IsFinalized }, new { IsFinalized = true }, input.Reason);
            notification.IsFinalized = true; db.Entry(notification).Property(x => x.Revision).IsModified = true;
            await db.SaveChangesAsync(ct); return new NotificationUploadResult(id, document.Id, true, false, notification.Revision, null);
        }, ct);
    }

    public async Task<object> CorrectDraftNotificationAsync(Guid id, NotificationMetadataInput input, CancellationToken ct)
    {
        var authorized = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new StaffWorkflowException("NotificationNotFound", "Notification was not found.", 404);
        await AuthorizeAsync(PermissionCodes.AwardEdit, authorized.CreatedBy, ct: ct);
        return await mutations.ExecuteAsync([$"notification:{id}", "notification-identities"], $"notification-metadata:{id}", input.MutationId, input, async () =>
        {
            var item = await db.Notifications.SingleAsync(x => x.Id == id, ct); await db.Entry(item).ReloadAsync(ct);
            Revision(item.Revision, input.ExpectedRevision); Reason(input.Reason);
            if (item.RecordStatus != RecordStatus.Active || item.IsFinalized || await db.SourceEvidence.AnyAsync(x => x.NotificationId == id, ct))
                throw new StaffWorkflowException("VerifiedMetadataProtected", "Only incomplete, unverified Notification metadata permits generic corrections.", 409);
            var section = Clean(input.SectionType) ?? throw new StaffWorkflowException("SectionRequired", "Section/subsection is required.");
            var number = Clean(input.NotificationNumber) ?? "";
            if (section.Length > 200 || number.Length > 500) throw new StaffWorkflowException("MetadataTooLong", "Notification reference or section is too long.");
            if (number.Length == 0 && Clean(input.NumberAbsentReason) is null) throw new StaffWorkflowException("NumberAbsenceReasonRequired", "Document why the number is absent.");
            if (number.Length > 0 && await db.Notifications.AnyAsync(x => x.Id != id && x.SectionType == section && x.NotificationNumber == number && x.NotificationDate == input.NotificationDate, ct))
                throw new StaffWorkflowException("NotificationIdentityConflict", "Another canonical Notification has this identity.", 409);
            var before = new { item.SectionType, item.NotificationNumber, item.NotificationDate, item.NumberAbsentReason, item.Remarks };
            item.SectionType = section; item.NotificationNumber = number; item.NotificationDate = input.NotificationDate; item.NumberAbsentReason = Clean(input.NumberAbsentReason); item.Remarks = Clean(input.Remarks);
            db.Entry(item).Property(x => x.Revision).IsModified = true;
            Audit(nameof(Notification), id, "StaffDraftNotificationCorrected", before, input, input.Reason);
            await db.SaveChangesAsync(ct);
            return new { notificationId = id, item.Revision, item.IsFinalized, needsReview = true };
        }, ct);
    }

    public async Task<TemporaryProceedingResult> UploadTemporaryAsync(Guid villageId, TemporaryProceedingInput input, Stream source, string name, CancellationToken ct)
    {
        await AuthorizeAsync(PermissionCodes.AwardCreate, workstream: WorkstreamCodes.LandRecords, ct: ct);
        await AuthorizeAsync(PermissionCodes.AwardCoreDocumentUpload, workstream: WorkstreamCodes.LandRecords, ct: ct);
        if (input.DocumentType is not ("TemporaryOffer" or "TemporaryOrder" or "TemporaryProceedings")) throw new StaffWorkflowException("InvalidRole", "Choose TemporaryOffer, TemporaryOrder or TemporaryProceedings.");
        if (Clean(input.ActRegime) is null || Clean(input.LegalSection) is null || Clean(input.Purpose) is null) throw new StaffWorkflowException("TemporaryContextRequired", "Act, section/subsection and purpose are required.");
        if (input.DurationMonths is <= 0 || input.ExpiryDate < input.CommencementDate) throw new StaffWorkflowException("InvalidOccupationPeriod", "Duration must be positive and expiry cannot precede commencement.");
        await using var pdf = await PreparedLandRecordPdf.ReadAsync(source, name, MaxPdfBytes, ct);
        var files = new LandRecordPdfStore(db, storage);
        return await mutations.ExecuteAsync([$"pdf:{pdf.Hash}"], $"temporary-upload:{villageId}", input.MutationId, new { input, sourceHash = pdf.Hash }, async () =>
        {
            if (!await db.Villages.AnyAsync(x => x.Id == villageId && x.RecordStatus == RecordStatus.Active, ct)) throw new StaffWorkflowException("VillageNotFound", "Village was not found.", 404);
            if (input.AcquisitionProjectId is Guid project && !await db.AcquisitionProjects.AnyAsync(x => x.Id == project && x.RecordStatus == RecordStatus.Active, ct)) throw new StaffWorkflowException("ProjectNotFound", "Project was not found.", 404);
            if (input.AwardId is Guid awardId)
            {
                await AwardAsync(awardId, PermissionCodes.AwardEdit, true, ct);
                if (!await db.AwardVillages.AnyAsync(x => x.AwardId == awardId && x.VillageId == villageId, ct)) throw new StaffWorkflowException("WrongAwardLink", "Actual Award must be linked to this Village.");
            }
            var document = await files.GetOrStoreAsync(pdf, name, input.DocumentType, Actor, ct);
            if (await db.DocumentAwards.AnyAsync(x => x.DocumentId == document.Id && !x.IsDetached && x.CoreDocumentRole == "Award", ct) || await db.DocumentNotifications.AnyAsync(x => x.DocumentId == document.Id, ct)) throw new StaffWorkflowException("SourceRoleConflict", "Source is already final Award or Notification evidence.", 409);
            var existing = await db.Set<TemporaryAcquisitionProceeding>().SingleOrDefaultAsync(x => x.DocumentId == document.Id && x.VillageId == villageId, ct);
            if (existing is not null) throw new StaffWorkflowException("TemporarySourceConflict", "This source already has a temporary proceeding in this Village. Retry with the original mutationId.", 409, new { existing.Id });
            var proceeding = new TemporaryAcquisitionProceeding { VillageId = villageId, Document = document, DocumentType = input.DocumentType,
                ActRegime = input.ActRegime.Trim(), LegalSection = input.LegalSection.Trim(), Purpose = input.Purpose.Trim(), ReferenceNumber = Clean(input.ReferenceNumber),
                DurationMonths = input.DurationMonths, LegalDate = input.LegalDate, CommencementDate = input.CommencementDate, ExpiryDate = input.ExpiryDate,
                CompensationReference = Clean(input.CompensationReference), Remarks = Clean(input.Remarks), AcquisitionProjectId = input.AcquisitionProjectId, AwardId = input.AwardId };
            db.Add(proceeding);
            if (!await db.DocumentVillages.AnyAsync(x => x.DocumentId == document.Id && x.VillageId == villageId, ct)) db.Add(new DocumentVillage { Document = document, VillageId = villageId });
            Audit(nameof(TemporaryAcquisitionProceeding), proceeding.Id, "StaffTemporaryProceedingRegistered", null, input);
            await db.SaveChangesAsync(ct); return new TemporaryProceedingResult(proceeding.Id, document.Id, false);
        }, ct, files.CleanupAsync);
    }
}
