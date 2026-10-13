namespace LAC.Infrastructure;

using System.Security.Cryptography;
using LAC.Domain;

public static class OfficeNativeCapture
{
    public static async Task VerifyAsync(IDocumentStorage storage, MatterOfficeSaveRequest request, GreenEnvelopeManifest manifest, CancellationToken ct)
    {
        if (request.NativeCaptureStoragePath is null || request.NativeCaptureSha256 is null || request.NativeCaptureSize is null ||
            request.NativeCanonicalHash is null || request.NativeContentHash is null) throw new MatterWorkflowException("Missing native synchronization evidence.", 409);
        await using var bytes = await storage.OpenReadAsync(request.NativeCaptureStoragePath, ct) ?? throw new MatterWorkflowException("Native synchronization evidence unavailable.", 409);
        if (bytes.Length != request.NativeCaptureSize || bytes.Length > OfficeNoteDocx.MaxBytes ||
            Convert.ToHexString(await SHA256.HashDataAsync(bytes, ct)).ToLowerInvariant() != request.NativeCaptureSha256)
            throw new MatterWorkflowException("Native synchronization capture integrity failed.", 409);
        bytes.Position = 0; var target = ContinuousGreenEnvelope.Convert(bytes, manifest);
        if (target.CanonicalHash != request.NativeCanonicalHash || ContinuousGreenEnvelope.ContentFingerprint(target) != request.NativeContentHash)
            throw new MatterWorkflowException("Native synchronization capture canonical evidence differs.", 409);
    }
}
