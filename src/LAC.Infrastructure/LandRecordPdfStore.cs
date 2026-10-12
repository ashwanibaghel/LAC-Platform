using System.Security.Cryptography;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UglyToad.PdfPig;

namespace LAC.Infrastructure;

internal sealed class PreparedLandRecordPdf : IAsyncDisposable
{
    public FileStream Content { get; }
    public string Hash { get; }
    public long Size => Content.Length;
    private PreparedLandRecordPdf(FileStream content, string hash) { Content = content; Hash = hash; }
    public static async Task<PreparedLandRecordPdf> ReadAsync(Stream source, string name, long maxBytes, CancellationToken ct)
    {
        if (!string.Equals(Path.GetExtension(name), ".pdf", StringComparison.OrdinalIgnoreCase)) throw new StaffWorkflowException("PdfRequired", "Choose a PDF file.");
        var path = Path.Combine(Path.GetTempPath(), $"lac-staff-pdf-{Guid.NewGuid():N}.pdf");
        var buffer = new byte[131072];
        var spool = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int count;
            while ((count = await source.ReadAsync(buffer, ct)) > 0)
            {
                if (spool.Length + count > maxBytes) throw new StaffWorkflowException("PdfTooLarge", "PDF exceeds the configured file size limit.", 413);
                sha.AppendData(buffer, 0, count); await spool.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            await spool.FlushAsync(ct); spool.Position = 0;
            var signature = new byte[5];
            if (await spool.ReadAsync(signature, ct) != 5 || !signature.AsSpan().SequenceEqual("%PDF-"u8)) throw new StaffWorkflowException("InvalidPdf", "The file does not have a PDF signature.");
            spool.Position = 0;
            try
            {
                using var pdf = PdfDocument.Open(spool);
                if (pdf.NumberOfPages < 1) throw new InvalidOperationException("No pages");
                // Opening each page rejects broken object/page trees without OCR or classification.
                foreach (var page in pdf.GetPages()) _ = page.Width;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { throw new StaffWorkflowException("InvalidPdf", "PDF content is unreadable, encrypted or malformed."); }
            spool.Position = 0;
            return new(spool, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
        }
        catch { await spool.DisposeAsync(); throw; }
    }
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

internal sealed class LandRecordPdfStore(LacDbContext db, IDocumentStorage storage)
{
    public string? NewStoragePath { get; private set; }
    public async Task CleanupAsync()
    {
        if (NewStoragePath is not { } path) return;
        // A connection failure during COMMIT can have an unknown outcome. Never
        // remove source bytes that a successfully committed Document references.
        try
        {
            bool referenced;
            if (db.Database.IsNpgsql())
            {
                await using var probe = new NpgsqlConnection(db.Database.GetConnectionString());
                await probe.OpenAsync(CancellationToken.None);
                await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM \"Documents\" WHERE \"StoragePath\" = @path)", probe);
                command.Parameters.AddWithValue("path", path);
                referenced = (bool)(await command.ExecuteScalarAsync(CancellationToken.None))!;
            }
            else referenced = await db.Documents.AsNoTracking().AnyAsync(x => x.StoragePath == path, CancellationToken.None);
            if (!referenced) await storage.DeleteAsync(path, CancellationToken.None);
        }
        catch { /* Database/storage unavailable: retain uncertain bytes for recovery. */ }
        NewStoragePath = null;
    }
    public async Task<Document> GetOrStoreAsync(PreparedLandRecordPdf pdf, string name, string type, string? actor, CancellationToken ct, Guid? preferredAwardId = null)
    {
        // Older duplicate rows remain historical; select deterministically, never Single().
        var document = await db.Documents.Where(x => x.Sha256Hash != null && x.Sha256Hash.ToLower() == pdf.Hash && x.RecordStatus == RecordStatus.Active && x.Status == "Active")
            .OrderByDescending(x => preferredAwardId != null && x.AwardLinks.Any(a => a.AwardId == preferredAwardId && !a.IsDetached))
            .ThenBy(x => x.UploadedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (document is not null)
        {
            await VerifyAsync(document.StoragePath, pdf.Hash, pdf.Size, ct); return document;
        }
        if (await db.Documents.AnyAsync(x => x.Sha256Hash != null && x.Sha256Hash.ToLower() == pdf.Hash, ct))
            throw new StaffWorkflowException("SourceArchived", "This PDF already exists as inactive or archived evidence. Request an authorized source review instead of creating another identity.", 409);
        pdf.Content.Position = 0;
        var stored = await storage.SaveAndHashAsync(pdf.Content, Path.GetFileName(name), ct);
        NewStoragePath = stored.StoragePath;
        if (stored.Sha256Hash != pdf.Hash || stored.FileSize != pdf.Size) throw new StaffWorkflowException("StorageIntegrityFailure", "Stored size/hash differs from the uploaded source.", 500);
        await VerifyAsync(stored.StoragePath, pdf.Hash, pdf.Size, ct);
        document = new Document { DocumentType = type, OriginalFileName = Path.GetFileName(name), StoragePath = stored.StoragePath,
            Sha256Hash = pdf.Hash, MimeType = "application/pdf", FileSize = pdf.Size, UploadedBy = actor };
        db.Add(document); return document;
    }
    public async Task VerifyAsync(string path, string hash, long? size, CancellationToken ct)
    {
        await using var bytes = await storage.OpenReadAsync(path, ct) ?? throw new StaffWorkflowException("SourceUnavailable", "Stored source PDF is unavailable.", 409);
        if (size is not null && bytes.CanSeek && bytes.Length != size) throw new StaffWorkflowException("StorageIntegrityFailure", "Stored PDF size mismatch.", 409);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(bytes, ct)).ToLowerInvariant();
        if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase)) throw new StaffWorkflowException("StorageIntegrityFailure", "Stored PDF hash mismatch.", 409);
    }
}
