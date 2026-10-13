namespace LAC.Infrastructure;

using System.Security.Cryptography;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public static class OfficeNotingSubmission
{
    public static async Task VerifyBytesAsync(IDocumentStorage storage, MatterOfficeSnapshot snapshot, CancellationToken ct)
    {
        await using var stream = await storage.OpenReadAsync(snapshot.StoragePath, ct) ?? throw new MatterWorkflowException("Committed Office snapshot is unavailable; submission is blocked.", 409);
        if (stream.Length != snapshot.FileSize || stream.Length > OfficeNoteDocx.MaxBytes ||
            Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant() != snapshot.Sha256)
            throw new MatterWorkflowException("Committed Office snapshot integrity check failed; submission is blocked.", 409);
    }

    public static async Task<MatterOfficeNoteEvidence?> ValidateAsync(LacDbContext db, IDocumentStorage storage,
        MatterWorkingNote draft, Guid? previewId, string? hash, CancellationToken ct)
    {
        if (draft.OfficeSessionId is null)
        {
            if (previewId.HasValue || hash is not null) throw new MatterWorkflowException("Office preview does not belong to this working note.", 409);
            return null;
        }
        if (previewId is null || previewId != draft.OfficePreviewId) throw new MatterWorkflowException("Officer-reviewed Office preview and exact canonical hash are required.", 409);
        var preview = await db.MatterOfficePreviews.SingleOrDefaultAsync(x => x.Id == previewId && x.MatterId == draft.MatterId && x.ActorUserId == draft.AuthorUserId, ct);
        var session = await db.MatterOfficeSessions.SingleAsync(x => x.Id == draft.OfficeSessionId, ct);
        var actor = await db.AppUsers.AsNoTracking().SingleAsync(x => x.Id == draft.AuthorUserId, ct);
        if (preview is null || preview.SessionId != session.Id || session.State != "Reviewed" || session.ExpiresAt <= DateTimeOffset.UtcNow ||
            actor.SessionVersion != session.ActorSessionVersion || session.WorkingRevision != draft.Revision || session.SourceDakId != draft.SourceDakId ||
            preview.SourceDakId != draft.SourceDakId || preview.SnapshotId != session.LatestSnapshotId ||
            preview.ConverterVersion != session.CompositionVersion || session.CompositionVersion is not (OfficeNoteDocx.ConverterVersion or ContinuousGreenEnvelope.Version) || preview.CanonicalHash != hash || preview.CanonicalText != draft.CanonicalText ||
            preview.ContentJson != draft.ContentJson || NotingText.Canonicalize(draft.ContentJson) != draft.CanonicalText)
            throw new MatterWorkflowException("Office review is stale, expired or differs from the saved canonical note.", 409);
        var snapshot = await db.MatterOfficeSnapshots.SingleAsync(x => x.Id == preview.SnapshotId && x.SessionId == session.Id, ct);
        await VerifyBytesAsync(storage, snapshot, ct);
        if (session.CompositionVersion == ContinuousGreenEnvelope.Version)
        {
            var manifest = await ContinuousGreenEnvelope.AuthoritativeAsync(db, session, ct);
            await using var bytes = await storage.OpenReadAsync(snapshot.StoragePath, ct) ?? throw new MatterWorkflowException("Snapshot unavailable.", 409);
            var canonical = ContinuousGreenEnvelope.Convert(bytes, manifest);
            if (canonical.ContentJson != preview.ContentJson || canonical.CanonicalText != preview.CanonicalText || canonical.CanonicalHash != preview.CanonicalHash)
                throw new MatterWorkflowException("Envelope differs from officer-confirmed canonical content.", 409);
            if(snapshot.SaveRequestId is { } save)
            {
                var request = await db.MatterOfficeSaveRequests.SingleAsync(x => x.Id == save && x.SessionId == session.Id && x.SnapshotId == snapshot.Id && x.State == "Committed", ct);
                await OfficeNativeCapture.VerifyAsync(storage, request, manifest, ct);
                if (canonical.CanonicalHash != request.NativeCanonicalHash || ContinuousGreenEnvelope.ContentFingerprint(canonical) != request.NativeContentHash)
                    throw new MatterWorkflowException("Submission differs from quiesced native capture.", 409);
            }
            else if(snapshot.Version!=0 || preview.NativeCaptureJson is null)
                throw new MatterWorkflowException("Unchanged seed review requires retained native capture evidence.",409);
            if(preview.NativeCaptureJson is not null)
            {
                var proof=OfficeNativeCapture.Read(preview.NativeCaptureJson);
                await OfficeNativeCapture.VerifyAsync(storage,proof,manifest,ct);
                if(canonical.CanonicalHash!=proof.CanonicalHash || ContinuousGreenEnvelope.ContentFingerprint(canonical)!=proof.ContentHash)
                    throw new MatterWorkflowException("Submission differs from frozen native review capture.",409);
            }
        }
        session.State = "Submitted"; session.Revision++;
        return new MatterOfficeNoteEvidence { PreviewId = preview.Id, SnapshotId = snapshot.Id, Sha256 = snapshot.Sha256, ConverterVersion = preview.ConverterVersion };
    }
}
