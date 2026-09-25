using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LAC.Infrastructure;

public sealed class OnlyOfficeDraftService(
    LacDbContext db, IDocumentStorage storage, OnlyOfficeTokens tokens,
    IOptions<OnlyOfficeOptions> options, IHttpClientFactory clients, ILogger<OnlyOfficeDraftService> logger)
{
    private const long MaxBytes = 50 * 1024 * 1024;
    // Bounded, per-process serialization; Revision is also an EF concurrency token across processes.
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();
    public static string Key(MatterDraft draft) => $"lac-draft-{draft.Id:N}-g{draft.OfficeKeyGeneration}";

    public async Task MaterializeAsync(MatterDraft draft, CancellationToken ct)
    {
        if (draft.OfficeDocumentId.HasValue) return;
        using var docx = MatterDraftDocx.Create(draft);
        var saved = await storage.SaveAndHashAsync(docx, "draft.docx", ct);
        var document = new Document
        {
            OriginalFileName = draft.Title + ".docx", DocumentType = "MatterDraft",
            StoragePath = saved.StoragePath, MimeType = MatterDraftDocx.MimeType,
            Sha256Hash = saved.Sha256Hash, FileSize = saved.FileSize,
            CreatedBy = draft.CreatedBy, UploadedBy = draft.CreatedBy
        };
        db.Documents.Add(document);
        draft.OfficeDocument = document;
        draft.OfficeDocumentId = document.Id;
        draft.Revision++;
        draft.UpdatedAt = DateTimeOffset.UtcNow;
        // One SaveChanges transaction publishes the draft, document and audit metadata together.
        await db.SaveChangesAsync(ct);
    }

    public async Task<object> ConfigurationAsync(Guid id, Guid userId, string displayName, bool canEdit, CancellationToken ct)
    {
        var gate = Gates[(int)((uint)id.GetHashCode() % Gates.Length)];
        await gate.WaitAsync(ct);
        try
        {
            var draft = await db.MatterDrafts.Include(x => x.OfficeDocument).SingleAsync(x => x.Id == id, ct);
            await MaterializeAsync(draft, ct);
            var document = draft.OfficeDocument!;
            if (document.RecordStatus != RecordStatus.Active || document.Status != "Active")
                throw new InvalidOperationException("Office document is unavailable.");
            var root = options.Value.AppExternalUrl.TrimEnd('/') + $"/api/matter-drafts/{id}";
            var config = new Dictionary<string, object>
            {
                ["documentType"] = "word", ["type"] = "desktop", ["width"] = "100%", ["height"] = "100%",
                ["document"] = new
                {
                    fileType = "docx", key = Key(draft), title = draft.Title + ".docx",
                    url = root + "/office-file?token=" + tokens.DownloadToken(id, document.Id),
                    permissions = new { edit = canEdit, download = false, print = true, review = canEdit, comment = canEdit }
                },
                ["editorConfig"] = new
                {
                    callbackUrl = root + "/onlyoffice-callback", mode = canEdit ? "edit" : "view", lang = "en",
                    user = new { id = userId.ToString(), name = displayName },
                    customization = new { forcesave = true, autosave = true }
                }
            };
            config["token"] = tokens.Sign(config);
            return new { scriptUrl = options.Value.BrowserUrl.TrimEnd('/') + "/web-apps/apps/api/documents/api.js", config };
        }
        finally { gate.Release(); }
    }

    public async Task<int> CallbackAsync(Guid id, JsonElement signedPayload, CancellationToken ct)
    {
        // Header JWT wraps the body in payload; body JWT contains the body directly.
        var payload = signedPayload.TryGetProperty("payload", out var wrapped) ? wrapped : signedPayload;
        var callback = payload.Deserialize<OfficeCallback>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Missing callback payload.");
        if (string.IsNullOrEmpty(callback.Key)) throw new InvalidDataException("Missing callback key.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{callback.Key}\n{callback.Status}\n{callback.Url}")));
        var gate = Gates[(int)((uint)id.GetHashCode() % Gates.Length)];
        await gate.WaitAsync(ct);
        try
        {
            var draft = await db.MatterDrafts.Include(x => x.OfficeDocument).Include(x => x.Matter)
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            if (draft?.OfficeDocument is not { } document || draft.RecordStatus != RecordStatus.Active
                || draft.Matter.RecordStatus != RecordStatus.Active || document.RecordStatus != RecordStatus.Active
                || document.Status != "Active") return 1;
            // A successful final callback can be retried after the generation has advanced.
            if (callback.Status == 2 && draft.LastOfficeSaveTokenHash == fingerprint) return 0;
            if (callback.Key != Key(draft)) return 1;
            if (callback.Status is 1 or 4) return 0;
            if (callback.Status is 3 or 7)
            {
                logger.LogWarning("ONLYOFFICE reported status {Status} for draft {DraftId}, generation {Generation}",
                    callback.Status, id, draft.OfficeKeyGeneration);
                return 0;
            }
            if (callback.Status is not (2 or 6)) return 1;
            var url = ValidateSaveUrl(callback.Url);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var saveCt = timeout.Token;
            using var response = await clients.CreateClient("OnlyOffice").GetAsync(url, HttpCompletionOption.ResponseHeadersRead, saveCt);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException("Office file exceeds size limit.");
            // Stage to disk, cap streamed bytes, and inspect the package before publishing anything.
            var temp = Path.GetTempFileName();
            try
            {
                await using var stage = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None,
                    81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                await using var source = await response.Content.ReadAsStreamAsync(saveCt);
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, saveCt)) > 0)
                {
                    if (stage.Length + read > MaxBytes) throw new InvalidDataException("Office file exceeds size limit.");
                    await stage.WriteAsync(buffer.AsMemory(0, read), saveCt);
                }
                await stage.FlushAsync(saveCt);
                // The editor may change page setup. Restore the official Noting policy before hashing and publishing.
                if (draft.DraftType == MatterDraftType.Noting)
                {
                    MatterDraftDocx.EnforceNotingLayout(stage);
                    await stage.FlushAsync(saveCt);
                }
                ValidateDocx(stage);
                stage.Position = 0;
                // Force-save URLs may be reused for new bytes in the same session.
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stage, saveCt)).ToLowerInvariant();
                if (callback.Status == 6 && hash == document.Sha256Hash) return 0;
                stage.Position = 0;
                var saved = await storage.SaveAndHashAsync(stage, "draft.docx", saveCt);
                var previousPath = document.StoragePath;
                document.StoragePath = saved.StoragePath;
                document.FileSize = saved.FileSize;
                document.Sha256Hash = saved.Sha256Hash;
                document.Version++;
                document.UpdatedAt = DateTimeOffset.UtcNow;
                document.UpdatedBy = "ONLYOFFICE";
                draft.Revision++;
                draft.UpdatedAt = document.UpdatedAt;
                draft.UpdatedBy = "ONLYOFFICE";
                draft.LastOfficeSaveTokenHash = fingerprint;
                if (callback.Status == 2) draft.OfficeKeyGeneration++;
                // Atomic replacement through the existing Document's storage pointer. Readers see
                // either complete file; DB failures cannot overwrite the last good bytes/metadata.
                // Retain staged files on uncertain DB failures for recovery, never delete a possibly committed file.
                await db.SaveChangesAsync(saveCt);
                try { await storage.DeleteAsync(previousPath, CancellationToken.None); }
                catch (IOException) { logger.LogWarning("Old office file cleanup deferred for document {DocumentId}", document.Id); }
                return 0;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { gate.Release(); }
    }

    public Uri ValidateSaveUrl(string? value)
    {
        var expected = new Uri(options.Value.SaveOrigin);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !string.Equals(uri.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.IdnHost, expected.IdnHost, StringComparison.OrdinalIgnoreCase) || uri.Port != expected.Port)
            throw new InvalidDataException("Unexpected ONLYOFFICE save origin.");
        return uri;
    }

    public static void ValidateDocx(Stream stream)
    {
        if (stream.Length == 0) throw new InvalidDataException("Empty office file.");
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
        {
            if (zip.Entries.Count > 10000 || zip.Entries.Sum(x => x.Length) > 200 * 1024 * 1024L)
                throw new InvalidDataException("Office package is too large.");
        }
        stream.Position = 0;
        using var package = WordprocessingDocument.Open(stream, false);
        var main = package.MainDocumentPart;
        if (package.DocumentType != DocumentFormat.OpenXml.WordprocessingDocumentType.Document
            || main?.Document?.Body is null || main.VbaProjectPart is not null)
            throw new InvalidDataException("Expected a DOCX document.");
    }

    private sealed record OfficeCallback(string Key, int Status, string? Url);
}
