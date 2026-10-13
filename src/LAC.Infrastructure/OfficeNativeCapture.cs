namespace LAC.Infrastructure;

using System.Security.Cryptography;
using System.Text.Json;
using LAC.Domain;

public sealed record OfficeCaptureEvidence(int Version, string StoragePath, string Sha256, long Size, string CanonicalHash, string ContentHash);

public static class OfficeNativeCapture
{
    public static async Task VerifyAsync(IDocumentStorage storage, MatterOfficeSaveRequest request, GreenEnvelopeManifest manifest, CancellationToken ct)
    {
        if (request.NativeCaptureStoragePath is null || request.NativeCaptureSha256 is null || request.NativeCaptureSize is null ||
            request.NativeCanonicalHash is null || request.NativeContentHash is null) throw new MatterWorkflowException("Missing native synchronization evidence.", 409);
        await VerifyAsync(storage, new OfficeCaptureEvidence(1,request.NativeCaptureStoragePath,request.NativeCaptureSha256,request.NativeCaptureSize.Value,request.NativeCanonicalHash,request.NativeContentHash),manifest,ct);
    }
    public static OfficeCaptureEvidence Read(string json)
    {
        try { return JsonSerializer.Deserialize<OfficeCaptureEvidence>(json) ?? throw new MatterWorkflowException("Missing native preview evidence.",409); }
        catch (JsonException) { throw new MatterWorkflowException("Invalid native preview evidence.",409); }
    }
    public static async Task VerifyAsync(IDocumentStorage storage, OfficeCaptureEvidence evidence, GreenEnvelopeManifest manifest, CancellationToken ct)
    {
        if(evidence.Version!=1 || evidence.Size is <=0 or >OfficeNoteDocx.MaxBytes || string.IsNullOrWhiteSpace(evidence.StoragePath))
            throw new MatterWorkflowException("Invalid native synchronization evidence.",409);
        await using var bytes = await storage.OpenReadAsync(evidence.StoragePath, ct) ?? throw new MatterWorkflowException("Native synchronization evidence unavailable.", 409);
        if (bytes.Length != evidence.Size || bytes.Length > OfficeNoteDocx.MaxBytes ||
            Convert.ToHexString(await SHA256.HashDataAsync(bytes, ct)).ToLowerInvariant() != evidence.Sha256)
            throw new MatterWorkflowException("Native synchronization capture integrity failed.", 409);
        bytes.Position = 0; var target = ContinuousGreenEnvelope.Convert(bytes, manifest);
        if (target.CanonicalHash != evidence.CanonicalHash || ContinuousGreenEnvelope.ContentFingerprint(target) != evidence.ContentHash)
            throw new MatterWorkflowException("Native synchronization capture canonical evidence differs.", 409);
    }
}
