using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LAC.Infrastructure;

public sealed partial class LandRecordsStaffWorkflowService
{
    internal long MaxPdfBytes => Math.Clamp(configuration.GetValue<int?>("PdfImport:MaxFileSizeMb") ?? 250, 1, 1024) * 1024L * 1024L;
    private static readonly string[] CoreRoles = ["Award", "NM", "StatementA", "PossessionProceeding", "TemporaryOffer", "TemporaryProceedings"];

    public async Task<CoreUploadResult> UploadCoreAsync(Guid awardId, CoreUploadInput input, Stream source, string name, CancellationToken ct)
    {
        var authorizedAward = await AwardAsync(awardId, PermissionCodes.AwardCoreDocumentUpload, true, ct);
        if (!CoreRoles.Contains(input.Role)) throw new StaffWorkflowException("InvalidRole", "Choose an explicit core document role.");
        if (input.Mode is not ("Direct" or "UploadAndAnalyze") || input.Mode == "UploadAndAnalyze" && input.Role != "Award") throw new StaffWorkflowException("InvalidMode", "Choose Direct, or UploadAndAnalyze for Award PDF.");
        if (input.Mode == "UploadAndAnalyze") await AuthorizeAsync(PermissionCodes.AwardEdit, authorizedAward.CreatedBy, ct: ct);
        if (input.Role == "PossessionProceeding")
        {
            await AuthorizeAsync(PermissionCodes.AwardCoreDocumentUpload, authorizedAward.CreatedBy, WorkstreamCodes.Possession, ct);
            if (input.LegalDate is null) throw new StaffWorkflowException("PossessionDateRequired", "Actual Possession Proceedings date is required.");
        }
        await using var pdf = await PreparedLandRecordPdf.ReadAsync(source, name, MaxPdfBytes, ct);
        var files = new LandRecordPdfStore(db, storage);
        // Intake may resolve an existing Award only after taking the identity lock.
        // Take that lock first on every attachment path to avoid late-Award lock cycles.
        return await mutations.ExecuteAsync(["award-metadata-identities", $"pdf:{pdf.Hash}", $"award:{awardId}"], null, null, input, async () =>
        {
            var award = await AwardAsync(awardId, PermissionCodes.AwardCoreDocumentUpload, true, ct);
            await db.Entry(award).ReloadAsync(ct);
            if (award.RecordStatus != RecordStatus.Active) throw new StaffWorkflowException("AwardArchived", "Restore the Award before attaching evidence.", 409);
            var villages = await db.AwardVillages.Where(x => x.AwardId == awardId).Select(x => x.VillageId).ToListAsync(ct);
            var villageId = input.VillageId ?? (villages.Count == 1 ? villages[0] : (Guid?)null);
            if (villageId is null || !villages.Contains(villageId.Value)) throw new StaffWorkflowException("VillageContextRequired", "Select a Village directly linked to this Award.");
            var khasras = (input.KhasraIds ?? []).Distinct().Order().ToArray();
            if (input.Role != "PossessionProceeding" && khasras.Length > 0) throw new StaffWorkflowException("InvalidKhasraContext", "Khasra evidence selections are supported for PP uploads.");
            var validCount = await db.Set<AwardKhasra>().CountAsync(x => x.AwardId == awardId && khasras.Contains(x.KhasraId) && x.Khasra.VillageId == villageId, ct);
            if (validCount != khasras.Length) throw new StaffWorkflowException("InvalidKhasraContext", "Every selected Khasra must belong to the selected Award and Village.");
            var document = await files.GetOrStoreAsync(pdf, name, input.Role == "Award" ? "Award PDF" : input.Role, Actor, ct, awardId);
            await ValidatePreviousOwnersAsync(document.Id, awardId, ct);
            if (document.Status == "Staged" && document.DocumentType == "Unclassified Core PDF")
            { document.Status = "Active"; document.DocumentType = input.Role == "Award" ? "Award PDF" : input.Role; }
            // Notification/temporary sources must not become final Award evidence by hash reuse.
            if (input.Role == "Award" && (await db.DocumentNotifications.AnyAsync(x => x.DocumentId == document.Id, ct) ||
                await db.Set<TemporaryAcquisitionProceeding>().AnyAsync(x => x.DocumentId == document.Id, ct)))
                throw new StaffWorkflowException("SourceRoleConflict", "Notification or temporary proceedings evidence cannot be uploaded as a final Award.", 409);
            var link = await db.DocumentAwards.Include(x => x.PossessionEvent).SingleOrDefaultAsync(x => x.DocumentId == document.Id && x.AwardId == awardId, ct);
            var duplicate = link is not null;
            if (link is not null)
            {
                if (link.IsDetached) throw new StaffWorkflowException("AttachmentDetached", "Use an explicit Restore correction for this attachment.", 409);
                if (link.CoreDocumentRole != input.Role || link.VillageId != villageId || link.LegalDate != input.LegalDate ||
                    link.ReferenceNumber != Clean(input.ReferenceNumber) || link.PartNumber != Clean(input.PartNumber) || link.Title != Clean(input.Title) || link.Remarks != Clean(input.Remarks))
                    throw new StaffWorkflowException("DuplicatePdfContextConflict", "This source PDF already has an attachment with different role or metadata. Use its correction action.", 409);
                if (link.PossessionEventId is Guid possessionId)
                {
                    var linkedKhasras = await db.Set<PossessionKhasra>().Where(x => x.PossessionEventId == possessionId).Select(x => x.KhasraId).ToListAsync(ct);
                    if (!linkedKhasras.Order().SequenceEqual(khasras)) throw new StaffWorkflowException("DuplicatePdfContextConflict", "This PP source already has different Khasra evidence selections.", 409);
                }
            }
            else
            {
                link = new DocumentAward { AwardId = awardId, Document = document, CoreDocumentRole = input.Role, VillageId = villageId,
                    LegalDate = input.LegalDate, ReferenceNumber = Clean(input.ReferenceNumber), PartNumber = Clean(input.PartNumber), Title = Clean(input.Title), Remarks = Clean(input.Remarks) };
                if (input.Role == "PossessionProceeding")
                {
                    var possession = new PossessionEvent { AwardId = awardId, PossessionDate = input.LegalDate, EventType = "PossessionProceedings", Status = "EvidenceUploaded", Remarks = Clean(input.Remarks) };
                    link.PossessionEvent = possession; db.Add(possession);
                    foreach (var khasra in khasras) db.Add(new PossessionKhasra { PossessionEvent = possession, KhasraId = khasra });
                }
                db.Add(link);
                Audit(nameof(DocumentAward), link.Id, "StaffCoreDocumentAttached", null, LinkMetadata(link));
            }
            if (!await db.DocumentVillages.AnyAsync(x => x.DocumentId == document.Id && x.VillageId == villageId, ct)) db.Add(new DocumentVillage { Document = document, VillageId = villageId.Value });
            Guid? nmId = null;
            if (input.Role == "NM")
            {
                var nm = await db.NmDocuments.SingleOrDefaultAsync(x => x.DocumentId == document.Id, ct);
                if (nm is not null && (nm.AwardId != awardId || nm.VillageId != villageId)) throw new StaffWorkflowException("NmContextConflict", "This source has an NM review in a different context; preserve it and register reviewed replacement evidence.", 409);
                if (nm is null) { nm = new NmDocument { Document = document, AwardId = awardId, VillageId = villageId.Value, ReferenceNumber = Clean(input.ReferenceNumber), RecordDate = input.LegalDate }; db.Add(nm); }
                nmId = nm.Id;
            }
            await db.SaveChangesAsync(ct);
            return new CoreUploadResult(document.Id, input.Role, link.Id, villageId, link.PossessionEventId, nmId, duplicate,
                duplicate ? "Same PDF already attached; existing evidence reused." : null, link.Revision);
        }, ct, files.CleanupAsync);
    }

    private async Task ValidatePreviousOwnersAsync(Guid documentId, Guid targetAwardId, CancellationToken ct)
    {
        var document = await db.Documents.FindAsync([documentId], ct);
        var hash = document?.Sha256Hash?.ToLowerInvariant();
        var owners = await db.DocumentAwards.Include(x => x.Award).Where(x =>
            (x.DocumentId == documentId || hash != null && x.Document.Sha256Hash != null && x.Document.Sha256Hash.ToLower() == hash) && x.AwardId != targetAwardId).ToListAsync(ct);
        var target = await db.DocumentAwards.AsNoTracking().SingleOrDefaultAsync(x => x.DocumentId == documentId && x.AwardId == targetAwardId && !x.IsDetached, ct);
        if (target is not null) return; // Exact same-Award retry introduces no new ownership.
        if (owners.Any(x => !x.IsDetached))
            throw await OwnershipConflictAsync(documentId, targetAwardId, ct, hash);
        foreach (var owner in owners) await AuthorizeAsync(PermissionCodes.AwardEdit, owner.Award.CreatedBy, ct: ct);
    }

    public async Task<CoreUploadResult> LinkStoredAwardAsync(Guid awardId, Guid documentId, Guid? villageId, CancellationToken ct)
    {
        var authorizedAward = await AwardAsync(awardId, PermissionCodes.AwardEdit, true, ct);
        await AuthorizeAsync(PermissionCodes.AwardCoreDocumentUpload, authorizedAward.CreatedBy, ct: ct);
        var existingHash = await ReadSourceHashAsync(documentId, ct);
        return await mutations.ExecuteAsync(["award-metadata-identities", $"award:{awardId}", $"document:{documentId}", $"pdf:{existingHash}"], null, null, new { documentId, villageId }, async () =>
        {
            await AwardAsync(awardId, PermissionCodes.AwardEdit, true, ct);
            await AwardAsync(awardId, PermissionCodes.AwardCoreDocumentUpload, true, ct);
            var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == documentId && x.Status == "Active" && x.RecordStatus == RecordStatus.Active)
                ?? throw new StaffWorkflowException("DocumentNotFound", "Stored Award PDF was not found.", 404);
            if (document.DocumentType is not ("Award PDF" or "Award")) throw new StaffWorkflowException("InvalidRole", "Only a stored Award PDF can be recovered through this action.");
            if (document.Sha256Hash is null) document.Sha256Hash = existingHash;
            await ValidatePreviousOwnersAsync(documentId, awardId, ct);
            var villages = await db.AwardVillages.Where(x => x.AwardId == awardId).Select(x => x.VillageId).ToListAsync(ct);
            var selected = villageId ?? (villages.Count == 1 ? villages[0] : (Guid?)null);
            if (selected is null || !villages.Contains(selected.Value)) throw new StaffWorkflowException("VillageContextRequired", "Select a directly linked Village.");
            await using (var source = await storage.OpenReadAsync(document.StoragePath, ct) ?? throw new StaffWorkflowException("SourceUnavailable", "Stored PDF is unavailable.", 409))
            {
                await using var pdf = await PreparedLandRecordPdf.ReadAsync(source, document.OriginalFileName, MaxPdfBytes, ct);
                if (document.Sha256Hash is null) document.Sha256Hash = pdf.Hash;
                if (!string.Equals(document.Sha256Hash, pdf.Hash, StringComparison.OrdinalIgnoreCase)) throw new StaffWorkflowException("StorageIntegrityFailure", "Stored PDF hash mismatch.", 409);
            }
            var link = await db.DocumentAwards.SingleOrDefaultAsync(x => x.DocumentId == documentId && x.AwardId == awardId, ct);
            var duplicate = link is not null;
            if (link?.IsDetached == true) throw new StaffWorkflowException("AttachmentDetached", "Use explicit Restore for detached evidence.", 409);
            if (link is not null && link.CoreDocumentRole is not (null or "Award")) throw new StaffWorkflowException("SourceRoleConflict", "Stored source has a different role.", 409);
            if (link is not null)
            {
                if (link.VillageId is not null && link.VillageId != selected) throw new StaffWorkflowException("VillageContextConflict", "Existing source Village differs; use a reviewed correction.", 409);
                return new CoreUploadResult(documentId, "Award", link.Id, link.VillageId, null, null, true, "Already attached to this Award.", link.Revision);
            }
            if (link is null) { link = new DocumentAward { DocumentId = documentId, AwardId = awardId }; db.Add(link); }
            link.CoreDocumentRole = "Award"; link.VillageId = selected;
            if (!await db.DocumentVillages.AnyAsync(x => x.DocumentId == documentId && x.VillageId == selected, ct)) db.Add(new DocumentVillage { DocumentId = documentId, VillageId = selected.Value });
            Audit(nameof(DocumentAward), link.Id, "StaffStoredAwardPdfLinked", null, LinkMetadata(link));
            await db.SaveChangesAsync(ct);
            return new CoreUploadResult(documentId, "Award", link.Id, selected, null, null, duplicate, null, link.Revision);
        }, ct);
    }

    public async Task<AwardPdfUploadResult> AnalyzeAwardAsync(Guid documentId, Guid awardId, Guid? villageId, IAwardPdfJobQueue queue, CancellationToken ct)
    {
        await AwardAsync(awardId, PermissionCodes.AwardEdit, true, ct);
        var createdJob = false;
        var result = await mutations.ExecuteAsync([$"award:{awardId}", $"document:{documentId}"], null, null, new { documentId, villageId }, async () =>
        {
            await AwardAsync(awardId, PermissionCodes.AwardEdit, true, ct);
            var link = await db.DocumentAwards.Include(x => x.Document).SingleOrDefaultAsync(x => x.DocumentId == documentId && x.AwardId == awardId && !x.IsDetached, ct)
                ?? throw new StaffWorkflowException("WrongAwardLink", "This source is not actively attached to the selected Award.", 404);
            if (link.CoreDocumentRole != "Award" && !(link.CoreDocumentRole is null && link.Document.DocumentType is "Award" or "Award PDF")) throw new StaffWorkflowException("InvalidAnalysisRole", "Only explicit Award PDF evidence uses Award analysis.");
            var villages = await db.AwardVillages.Where(x => x.AwardId == awardId).Select(x => x.VillageId).ToListAsync(ct);
            var selected = villageId ?? link.VillageId ?? (villages.Count == 1 ? villages[0] : (Guid?)null);
            if (selected is not null && !villages.Contains(selected.Value)) throw new StaffWorkflowException("VillageContextRequired", "Choose a Village linked to this Award.");
            if (link.VillageId is not null && selected != link.VillageId) throw new StaffWorkflowException("VillageContextConflict", "Analysis Village must match the attachment evidence context.", 409);
            if (link.Document.Sha256Hash is { Length: 64 } hash) await new LandRecordPdfStore(db, storage).VerifyAsync(link.Document.StoragePath, hash, link.Document.FileSize, ct);
            var active = await db.AwardDocumentExtractionJobs.Where(x => x.DocumentId == documentId && x.TargetAwardId == awardId &&
                (x.Status == AwardDocumentExtractionJobStatus.Queued || x.Status == AwardDocumentExtractionJobStatus.Extracting || x.Status == AwardDocumentExtractionJobStatus.Analyzing || x.Status == AwardDocumentExtractionJobStatus.BuildingCandidates))
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            if (active is Guid existing) return new AwardPdfUploadResult(existing, documentId);
            var job = new AwardDocumentExtractionJob { DocumentId = documentId, TargetAwardId = awardId, SelectedVillageId = selected, CurrentStage = "Waiting to analyze" };
            createdJob = true;
            db.Add(job); Audit(nameof(AwardDocumentExtractionJob), job.Id, "StaffExplicitAnalysisRequested", null, new { documentId, awardId, selected });
            await db.SaveChangesAsync(ct); return new AwardPdfUploadResult(job.Id, documentId);
        }, ct);
        // Durable queued job is committed first; worker startup/polling can recover
        // it if the in-process signal is lost. Repeated signals do not create jobs.
        if (createdJob && result.JobId is Guid jobId) await queue.EnqueueAsync(jobId, ct);
        return result;
    }

    public async Task<AttachmentCorrectionResult> CorrectAttachmentAsync(Guid awardId, Guid linkId, AttachmentCorrectionInput input, CancellationToken ct)
    {
        await AwardAsync(awardId, PermissionCodes.AwardEdit, true, ct);
        var source = await db.DocumentAwards.AsNoTracking().Where(x => x.Id == linkId && x.AwardId == awardId).Select(x => new { x.DocumentId, x.Document.Sha256Hash }).SingleOrDefaultAsync(ct)
            ?? throw new StaffWorkflowException("AttachmentNotFound", "Attachment does not belong to this Award.", 404);
        var hash = input.Action == "Restore" ? await ReadSourceHashAsync(source.DocumentId, ct) : source.Sha256Hash?.ToLowerInvariant();
        return await mutations.ExecuteAsync(["award-metadata-identities", $"award:{awardId}", $"document:{source.DocumentId}", $"pdf:{hash}"], $"attachment:{linkId}", input.MutationId, input, async () =>
        {
            var award = await AwardAsync(awardId, PermissionCodes.AwardEdit, true, ct);
            var link = await db.DocumentAwards.Include(x => x.Document).Include(x => x.PossessionEvent).SingleOrDefaultAsync(x => x.Id == linkId && x.AwardId == awardId, ct)
                ?? throw new StaffWorkflowException("AttachmentNotFound", "Attachment does not belong to this Award.", 404);
            Revision(link.Revision, input.ExpectedRevision); Reason(input.Reason);
            if (link.CoreDocumentRole == "Award" && input.Action is "Detach" or "Restore")
            {
                if (!input.OfficerConfirmed) throw new StaffWorkflowException("OfficerConfirmationRequired", "Confirm the reviewed Award PDF correction explicitly.");
                await AuthorizeAsync(PermissionCodes.AuditView, award.CreatedBy, ct: ct);
                await AuthorizeAsync(PermissionCodes.AwardCoreDocumentUpload, award.CreatedBy, ct: ct);
            }
            if (link.CoreDocumentRole == "PossessionProceeding") await AuthorizeAsync(PermissionCodes.AwardEdit, award.CreatedBy, WorkstreamCodes.Possession, ct);
            if (input.Action is not ("Update" or "Detach" or "Restore")) throw new StaffWorkflowException("InvalidAction", "Choose Update, Detach or Restore.");
            if (input.Action == "Restore")
            {
                if (!link.IsDetached) throw new StaffWorkflowException("InvalidTransition", "Only detached evidence can be restored.", 409);
                if (link.Document.RecordStatus != RecordStatus.Active || link.Document.Status != "Active") throw new StaffWorkflowException("SourceArchived", "Review inactive source evidence before restoring an attachment.", 409);
                if (link.Document.Sha256Hash is null) link.Document.Sha256Hash = hash;
                await new LandRecordPdfStore(db, storage).VerifyAsync(link.Document.StoragePath, hash!, link.Document.FileSize, ct);
                await ValidatePreviousOwnersAsync(link.DocumentId, awardId, ct);
            }
            if (input.Action == "Update" && link.IsDetached) throw new StaffWorkflowException("AttachmentDetached", "Restore evidence before editing it.", 409);
            var before = LinkMetadata(link);
            if (input.Action == "Update")
            {
                if (link.CoreDocumentRole == "PossessionProceeding" && input.LegalDate is null) throw new StaffWorkflowException("PossessionDateRequired", "Actual PP date is required.");
                link.LegalDate = input.LegalDate; link.ReferenceNumber = Clean(input.ReferenceNumber); link.PartNumber = Clean(input.PartNumber);
                link.Title = Clean(input.Title); link.Remarks = Clean(input.Remarks);
                if (link.CoreDocumentRole == "PossessionProceeding" && link.PossessionEvent is null)
                {
                    // Legacy evidence is explicitly dated during correction, never automatically matched.
                    link.PossessionEvent = new PossessionEvent { AwardId = awardId, PossessionDate = input.LegalDate, EventType = "PossessionProceedings", Status = "EvidenceUploaded", Remarks = link.Remarks };
                    db.Add(link.PossessionEvent);
                }
                if (link.PossessionEvent is not null) { link.PossessionEvent.PossessionDate = input.LegalDate; link.PossessionEvent.Remarks = link.Remarks; }
            }
            else
            {
                link.IsDetached = input.Action == "Detach";
                if (link.PossessionEvent is not null) link.PossessionEvent.RecordStatus = link.IsDetached ? RecordStatus.Archived : RecordStatus.Active;
            }
            db.Entry(link).Property(x => x.Revision).IsModified = true;
            Audit(nameof(DocumentAward), link.Id, "StaffAttachment" + input.Action, before, LinkMetadata(link), input.Reason);
            await db.SaveChangesAsync(ct); return new AttachmentCorrectionResult(link.Id, link.Revision, link.IsDetached);
        }, ct);
    }

    private async Task<string> ReadSourceHashAsync(Guid documentId, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId, ct)
            ?? throw new StaffWorkflowException("DocumentNotFound", "Source document was not found.", 404);
        await using var source = await storage.OpenReadAsync(document.StoragePath, ct) ?? throw new StaffWorkflowException("SourceUnavailable", "Stored source PDF is unavailable.", 409);
        await using var pdf = await PreparedLandRecordPdf.ReadAsync(source, document.OriginalFileName, MaxPdfBytes, ct);
        if (document.Sha256Hash is not null && !string.Equals(document.Sha256Hash, pdf.Hash, StringComparison.OrdinalIgnoreCase))
            throw new StaffWorkflowException("StorageIntegrityFailure", "Stored PDF hash mismatch.", 409);
        return pdf.Hash;
    }
    private static object LinkMetadata(DocumentAward link) => new { link.DocumentId, link.AwardId, link.CoreDocumentRole, link.VillageId,
        link.PossessionEventId, link.LegalDate, link.ReferenceNumber, link.PartNumber, link.Title, link.Remarks, link.IsDetached, link.IsSharedSource, link.Revision };
}
