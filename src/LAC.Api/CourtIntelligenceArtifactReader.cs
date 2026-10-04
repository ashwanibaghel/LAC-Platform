using System.Text.Json;

namespace LAC.Api;

// Read-only filesystem boundary. No provider/network/database dependency.
public static class CourtIntelligenceArtifactReader
{
    // Bounded complete histories (20+ orders); individual PDFs/orders stay
    // sequential and evidence validation remains independent of this limit.
    public const int MaxArtifactBytes = 8 * 1024 * 1024;
    public static async Task<JsonElement?> ReadAsync(string extractionRoot, Guid caseId, CancellationToken ct)
    {
        var path = Path.Combine(extractionRoot, "court-intelligence", "v1", caseId.ToString(), "current.json");
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        if (stream.Length > MaxArtifactBytes) throw new InvalidDataException("Court intelligence artifact exceeds safety limit.");
        using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 32 }, ct);
        var root = document.RootElement;
        if (root.GetProperty("version").GetInt32() != 1
            || !Guid.TryParse(root.GetProperty("caseId").GetString(), out var storedId) || storedId != caseId)
            throw new InvalidDataException("Court intelligence artifact identity/version mismatch.");
        return root.Clone();
    }
}
