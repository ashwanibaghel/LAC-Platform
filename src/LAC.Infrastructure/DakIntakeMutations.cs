namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

public sealed partial class DakWorkflowService
{
    /// <summary>Serialize intake edits with routing changes and reuse the existing audit log as a commit receipt.</summary>
    public async Task<TResult> MutateIntakeAsync<TResult>(Guid id, int? expectedRevision, Guid userId,
        string action, Func<Dak, CancellationToken, Task<TResult>> mutate, CancellationToken ct = default)
    {
        var operationId = Guid.NewGuid();
        try
        {
            return await ExecuteWorkflowTransactionAsync(async c =>
            {
                db.ChangeTracker.Clear();
                var dak = db.Database.IsRelational()
                    ? await db.Daks.FromSqlInterpolated($"SELECT * FROM \"Daks\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(c)
                    : await db.Daks.SingleOrDefaultAsync(d => d.Id == id, c);
                if (dak is null) throw new DakWorkflowException("Dak record not found.", 404);
                EnsureMutable(dak);
                if (expectedRevision.HasValue && dak.Revision != expectedRevision)
                    throw new DakWorkflowException("This Dak was modified elsewhere. Refresh before saving.", 409);
                var result = await mutate(dak, c);
                dak.Revision++;
                db.AuditLogs.Add(new AuditLog
                {
                    Id = operationId, EntityType = "Dak", EntityId = id, Action = action,
                    ChangedBy = userId.ToString(), ChangedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync(c);
                return result;
            }, async c =>
            {
                db.ChangeTracker.Clear();
                return await db.AuditLogs.AsNoTracking().AnyAsync(a => a.Id == operationId, c);
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DakWorkflowException("This Dak was modified elsewhere. Refresh before saving.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new DakWorkflowException("This record is already actively linked. Refresh before saving.", 409);
        }
    }

    private static void EnsureMutable(Dak dak)
    {
        if (dak.RecordStatus != RecordStatus.Active)
            throw new DakWorkflowException("Archived Dak is read-only.", 409);
        if (dak.Status is DakStatus.Disposed or DakStatus.Cancelled or DakStatus.Resolved)
            throw new DakWorkflowException("Disposed or cancelled Dak is read-only.", 409);
    }

    public async Task<DakAttachment> AddIntakeAttachmentAsync(Guid id, int? expectedRevision, Guid userId,
        Stream stream, string fileName, string mime, string? title, string? type, CancellationToken ct)
    {
        var dak = await db.Daks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new DakWorkflowException("Dak record not found.", 404);
        EnsureMutable(dak);
        var actor = await db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        var stored = await storage.SaveAndHashAsync(stream, fileName, ct);
        var documentId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        try
        {
            return await MutateIntakeAsync(id, expectedRevision, userId, "AttachmentAdded", async (_, c) =>
            {
                var document = new Document
                {
                    Id = documentId, OriginalFileName = fileName, StoragePath = stored.StoragePath,
                    Sha256Hash = stored.Sha256Hash, FileSize = stored.FileSize, MimeType = mime,
                    DocumentType = "DakAttachment", UploadedBy = actor.DisplayName
                };
                var sequence = await db.DakAttachments.Where(a => a.DakId == id).MaxAsync(a => (int?)a.SequenceOrder, c);
                var attachment = new DakAttachment
                {
                    Id = attachmentId, DakId = id, Document = document,
                    Title = string.IsNullOrWhiteSpace(title) ? fileName : title.Trim(),
                    AttachmentType = string.IsNullOrWhiteSpace(type) ? "Annexure" : type.Trim(),
                    SequenceOrder = (sequence ?? 0) + 1
                };
                db.Documents.Add(document);
                db.DakAttachments.Add(attachment);
                return attachment;
            }, ct);
        }
        catch
        {
            try { await storage.DeleteAsync(stored.StoragePath, CancellationToken.None); } catch { /* best effort compensation */ }
            throw;
        }
    }
}
