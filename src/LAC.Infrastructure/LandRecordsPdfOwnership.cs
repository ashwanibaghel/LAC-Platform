using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace LAC.Infrastructure;

public sealed partial class LandRecordsStaffWorkflowService
{
    internal async Task EnsureIntakeSourceReadAsync(Guid documentId, CancellationToken ct)
    {
        if (await db.DocumentAwards.AnyAsync(x => x.DocumentId == documentId, ct))
        {
            try { await AuthorizeDocumentReadAsync(documentId, ct); }
            catch (StaffWorkflowException ex) when (ex.StatusCode == 403) { throw await OwnershipConflictAsync(documentId, Guid.Empty, ct); }
        }
        else if (await db.DocumentNotifications.AnyAsync(x => x.DocumentId == documentId, ct) ||
                 await db.Set<TemporaryAcquisitionProceeding>().AnyAsync(x => x.DocumentId == documentId, ct))
            await AuthorizeDocumentReadAsync(documentId, ct);
        else
        {
            var document = await db.Documents.FindAsync([documentId], ct) ?? throw new StaffWorkflowException("DocumentNotFound", "Source was not found.", 404);
            if (db.Entry(document).State == EntityState.Added) return;
            var owner = document.CreatedBy ?? document.UploadedBy;
            Guid? ownerId = Guid.TryParse(owner, out var id) ? id : await db.AppUsers.Where(x => x.Username == owner).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            if (!await access.CanAsync(PermissionCodes.AwardView, new(WorkstreamCode: WorkstreamCodes.Award, OwnerUserId: ownerId), ct))
                throw new StaffWorkflowException("Forbidden", "You do not have live access to this staged source.", 403);
        }
    }
    internal async Task<StaffWorkflowException> OwnershipConflictAsync(Guid documentId, Guid selectedAwardId, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().SingleAsync(x => x.Id == documentId, ct);
        var hash = document.Sha256Hash?.ToLowerInvariant();
        var owners = await db.DocumentAwards.AsNoTracking().Include(x => x.Document).Include(x => x.Award).ThenInclude(x => x.VillageLinks).ThenInclude(x => x.Village)
            .Where(x => (x.DocumentId == documentId || hash != null && x.Document.Sha256Hash != null && x.Document.Sha256Hash.ToLower() == hash) && !x.IsDetached && x.AwardId != selectedAwardId).OrderBy(x => x.AwardId).ToListAsync(ct);
        var details = new List<PdfOwnershipLink>();
        foreach (var link in owners)
        {
            if (!await access.CanAsync(PermissionCodes.AwardView, Context(link.Award.CreatedBy), ct) ||
                link.Award.RecordStatus != RecordStatus.Active && !await access.CanAsync(PermissionCodes.AuditView, Context(link.Award.CreatedBy), ct)) continue;
            var canCorrect = await access.CanAsync(PermissionCodes.AwardEdit, Context(link.Award.CreatedBy), ct) &&
                await access.CanAsync(PermissionCodes.AwardCoreDocumentUpload, Context(link.Award.CreatedBy), ct) &&
                await access.CanAsync(PermissionCodes.AuditView, Context(link.Award.CreatedBy), ct);
            details.Add(new(link.AwardId, link.Award.AwardNumber, link.Award.Revision, link.Id, link.Revision, link.VillageId,
                link.Award.VillageLinks.Select(x => new PdfOwnershipVillage(x.VillageId, x.Village.Name)).OrderBy(x => x.VillageId).ToList(), canCorrect, link.DocumentId, link.Document.OriginalFileName));
        }
        var visible = details.FirstOrDefault();
        return new("PDF_OWNERSHIP_CONFLICT", "This PDF is attached to another Award. Open the existing Award, or request a reviewed link correction.", 409,
            new PdfOwnershipConflict(visible?.DocumentId, visible?.DocumentReference,
                visible is not null ? $"/api/documents/{visible.DocumentId}/content" : null, document.Sha256Hash ?? "", selectedAwardId, details, details.Count != owners.Count));
    }

    public async Task<AwardPdfLinkResolutionResult> ResolvePdfOwnershipAsync(Guid documentId, AwardPdfLinkResolutionInput input, CancellationToken ct)
    {
        var sourceAward = await AwardAsync(input.SourceAwardId, PermissionCodes.AwardEdit, false, ct);
        var targetAward = await AwardAsync(input.TargetAwardId, PermissionCodes.AwardEdit, true, ct);
        foreach (var award in new[] { sourceAward, targetAward })
        {
            await AuthorizeAsync(PermissionCodes.AwardCoreDocumentUpload, award.CreatedBy, ct: ct);
            await AuthorizeAsync(PermissionCodes.AuditView, award.CreatedBy, ct: ct);
        }
        return await mutations.ExecuteAsync([$"pdf:{input.ExpectedSha256Hash?.ToLowerInvariant()}", $"document:{documentId}", $"award:{input.SourceAwardId}", $"award:{input.TargetAwardId}", "award-metadata-identities"],
            $"pdf-resolution:{documentId}", input.MutationId, input, async () =>
        {
            if (!input.OfficerConfirmed) throw new StaffWorkflowException("OfficerConfirmationRequired", "Explicit officer confirmation is required for changing source ownership.");
            Reason(input.Reason);
            if (input.Action is not ("RelinkIncorrectAttachment" or "VoidDuplicateAndRelink" or "ShareSource")) throw new StaffWorkflowException("InvalidResolution", "Choose a reviewed correction, duplicate correction, or explicit shared-source association.");
            if (input.SourceAwardId == input.TargetAwardId) throw new StaffWorkflowException("InvalidResolution", "Source and target Awards must be distinct.");
            await db.Entry(sourceAward).ReloadAsync(ct); await db.Entry(targetAward).ReloadAsync(ct);
            Revision(sourceAward.Revision, input.ExpectedSourceAwardRevision); Revision(targetAward.Revision, input.ExpectedTargetAwardRevision);
            if (targetAward.RecordStatus != RecordStatus.Active) throw new StaffWorkflowException("AwardArchived", "Target Award must be active.", 409);
            var sourceLink = await db.DocumentAwards.Include(x => x.Document).SingleOrDefaultAsync(x => x.DocumentId == documentId && x.AwardId == input.SourceAwardId && !x.IsDetached, ct)
                ?? throw new StaffWorkflowException("AttachmentNotFound", "Active source ownership link was not found.", 404);
            Revision(sourceLink.Revision, input.ExpectedSourceAttachmentRevision);
            if (sourceLink.CoreDocumentRole is not (null or "Award") || sourceLink.Document.DocumentType is not ("Award" or "Award PDF")) throw new StaffWorkflowException("InvalidResolutionRole", "This correction workflow is for Award PDF evidence only.");
            if (!string.Equals(sourceLink.Document.Sha256Hash, input.ExpectedSha256Hash, StringComparison.OrdinalIgnoreCase) || input.ExpectedSha256Hash?.Length != 64) throw new StaffWorkflowException("SourceHashConflict", "Source hash differs from the confirmed PDF.", 409);
            await new LandRecordPdfStore(db, storage).VerifyAsync(sourceLink.Document.StoragePath, input.ExpectedSha256Hash, sourceLink.Document.FileSize, ct);
            if (!await db.AwardVillages.AnyAsync(x => x.AwardId == input.TargetAwardId && x.VillageId == input.VillageId, ct)) throw new StaffWorkflowException("VillageContextRequired", "Target Village must be directly linked to the target Award.");
            var sourceHash = input.ExpectedSha256Hash.ToLowerInvariant();
            var hashOwners = db.DocumentAwards.Where(x => !x.IsDetached &&
                (x.DocumentId == documentId || x.Document.Sha256Hash != null && x.Document.Sha256Hash.ToLower() == sourceHash));
            if (await hashOwners.AnyAsync(x => x.AwardId == input.TargetAwardId, ct))
                throw new StaffWorkflowException("AlreadyAttached", "PDF is already attached to the selected target Award. Refresh before resolving.", 409);
            if (await hashOwners.AnyAsync(x => x.AwardId == input.SourceAwardId && x.DocumentId != documentId, ct))
                throw new StaffWorkflowException("UnreviewedSharedOwnership", "Multiple historical source links require review before changing ownership.", 409);
            var otherOwners = await db.DocumentAwards.Include(x => x.Award).Where(x =>
                (x.DocumentId == documentId || x.Document.Sha256Hash != null && x.Document.Sha256Hash.ToLower() == sourceHash) &&
                !x.IsDetached && x.AwardId != input.SourceAwardId && x.AwardId != input.TargetAwardId).ToListAsync(ct);
            if (otherOwners.Count > 0 && input.Action != "ShareSource") throw await OwnershipConflictAsync(documentId, input.TargetAwardId, ct);
            foreach (var owner in otherOwners)
            {
                await AuthorizeAsync(PermissionCodes.AwardEdit, owner.Award.CreatedBy, ct: ct);
                await AuthorizeAsync(PermissionCodes.AwardCoreDocumentUpload, owner.Award.CreatedBy, ct: ct);
                await AuthorizeAsync(PermissionCodes.AuditView, owner.Award.CreatedBy, ct: ct);
                if (!owner.IsSharedSource) throw new StaffWorkflowException("UnreviewedSharedOwnership", "Existing additional ownership requires source coverage review first.", 409);
            }
            if (input.Action == "ShareSource")
            {
                if (sourceAward.RecordStatus != RecordStatus.Active || Clean(input.CoverageDescription) is null || input.SourcePages is not { Count: > 0 } || input.SourcePages.Any(x => x < 1))
                    throw new StaffWorkflowException("SourceCoverageRequired", "Shared source requires active Awards, source pages and a description of how this PDF covers both Awards.");
                await using var source = await storage.OpenReadAsync(sourceLink.Document.StoragePath, ct) ?? throw new StaffWorkflowException("SourceUnavailable", "Stored source is unavailable.", 409);
                using var pdf = PdfDocument.Open(source);
                if (input.SourcePages.Any(x => x > pdf.NumberOfPages)) throw new StaffWorkflowException("InvalidSourcePages", "Coverage page exceeds the stored PDF page count.");
            }
            if (input.Action == "VoidDuplicateAndRelink")
            {
                var sourceVillages = await db.AwardVillages.Where(x => x.AwardId == sourceAward.Id).Select(x => x.VillageId).ToListAsync(ct);
                if (!sourceVillages.Contains(input.VillageId) || !AwardNumberIdentity.Equivalent(sourceAward.AwardNumber, targetAward.AwardNumber) ||
                    !string.Equals(Clean(sourceAward.ActRegime), Clean(targetAward.ActRegime), StringComparison.OrdinalIgnoreCase) ||
                    sourceAward.AcquisitionProjectId is not null && targetAward.AcquisitionProjectId is not null && sourceAward.AcquisitionProjectId != targetAward.AcquisitionProjectId)
                    throw new StaffWorkflowException("NotDuplicateAwards", "Duplicate correction requires equivalent Award identity within the same Village/acquisition context.");
                Audit(nameof(Award), sourceAward.Id, "StaffDuplicateAwardVoided", Result(sourceAward), new { targetAwardId = targetAward.Id }, input.Reason);
                sourceAward.RecordStatus = RecordStatus.Archived; sourceAward.ArchiveKind = "Void";
            }
            var beforeSource = LinkMetadata(sourceLink);
            sourceLink.IsDetached = input.Action != "ShareSource"; sourceLink.IsSharedSource = input.Action == "ShareSource";
            db.Entry(sourceLink).Property(x => x.Revision).IsModified = true;
            var targetLink = await db.DocumentAwards.SingleOrDefaultAsync(x => x.DocumentId == documentId && x.AwardId == targetAward.Id, ct);
            if (targetLink is not null && !targetLink.IsDetached) throw new StaffWorkflowException("AlreadyAttached", "PDF is already attached to the selected target Award. Refresh before resolving.", 409);
            if (targetLink is not null && targetLink.CoreDocumentRole is not (null or "Award")) throw new StaffWorkflowException("SourceRoleConflict", "Target historical attachment has a different role.", 409);
            var beforeTarget = targetLink is null ? null : LinkMetadata(targetLink);
            if (targetLink is null) { targetLink = new DocumentAward { DocumentId = documentId, AwardId = targetAward.Id }; db.Add(targetLink); }
            targetLink.CoreDocumentRole = "Award"; targetLink.VillageId = input.VillageId; targetLink.IsDetached = false; targetLink.IsSharedSource = input.Action == "ShareSource";
            if (!await db.DocumentVillages.AnyAsync(x => x.DocumentId == documentId && x.VillageId == input.VillageId, ct)) db.Add(new DocumentVillage { DocumentId = documentId, VillageId = input.VillageId });
            // Advance both Award tokens; stale independent edits cannot race the officer decision.
            db.Entry(sourceAward).Property(x => x.Revision).IsModified = true; db.Entry(targetAward).Property(x => x.Revision).IsModified = true;
            Audit(nameof(DocumentAward), sourceLink.Id, "StaffPdfOwnershipResolved", beforeSource, new { source = LinkMetadata(sourceLink), input.Action, input.SourcePages, input.CoverageDescription }, input.Reason);
            Audit(nameof(DocumentAward), targetLink.Id, "StaffPdfOwnershipTargetLinked", beforeTarget, LinkMetadata(targetLink), input.Reason);
            await db.SaveChangesAsync(ct);
            return new AwardPdfLinkResolutionResult(documentId, targetAward.Id, targetLink.Id, targetLink.Revision, input.Action, $"/awards/{targetAward.Id}");
        }, ct);
    }
}
