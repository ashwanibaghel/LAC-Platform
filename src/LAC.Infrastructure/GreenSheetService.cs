namespace LAC.Infrastructure;

using System.Security.Cryptography;
using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed record CreateGreenSheetCommand(Guid? PreviewId);

public sealed class GreenSheetService(LacDbContext db, MatterNotingWorkflow workflow, OnlyOfficeNotingService editor,
    IDocumentStorage storage, OnlyOfficeTokens tokens, NotingOfficeClient office, IGreenSheetPdfVerifier verifier, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<object> CreateAsync(Guid matter, CreateGreenSheetCommand cmd, Guid actor, Guid key, CancellationToken ct)
    {
        office.Enabled();
        Guid? controllingDak = null;
        if (cmd.PreviewId is { } previewId)
            controllingDak = (await db.MatterOfficePreviews.AsNoTracking().SingleOrDefaultAsync(x => x.Id == previewId && x.MatterId == matter && x.ActorUserId == actor, ct)
                ?? throw new MatterWorkflowException("Working preview is private or unavailable.", 403)).SourceDakId;
        var payload = new { action = "GreenSheetSource", matter, cmd };
        Task<Guid> Capture(Func<CancellationToken, Task<Guid>> apply) => cmd.PreviewId.HasValue
            ? workflow.OfficeCommandAsync(matter, controllingDak, actor, key, payload, apply, ct)
            : workflow.OfficeReadCommandAsync(matter, actor, key, payload, apply, ct);
        var sourceId = await Capture(async c =>
        {
            var m = await db.Matters.AsNoTracking().SingleAsync(x => x.Id == matter, c);
            var notes = await db.MatterOfficialNotes.AsNoTracking().Where(x => x.MatterId == matter).OrderBy(x => x.Number).ToListAsync(c);
            if (notes.Count > 500 || notes.Sum(n => n.CanonicalText.Length) > 1_000_000) throw new MatterWorkflowException("This chain exceeds the verified synchronous renderer limit; no partial output was produced.", 422);
            foreach (var n in notes)
                if (NotingText.Canonicalize(n.ContentJson) != n.CanonicalText || NotingText.Hash(n.CanonicalText) != n.TextHash)
                    throw new MatterWorkflowException("Historical note content/hash integrity failed; no history was rewritten.", 409);
            var input = notes.Select(n => new GreenNoteInput(n.Number, n.AuthorName, n.Designation, n.SubmittedAt, n.ContentJson)).ToList();
            MatterOfficePreview? preview = null;
            if (cmd.PreviewId is { } pid)
            {
                preview = await db.MatterOfficePreviews.AsNoTracking().SingleOrDefaultAsync(x => x.Id == pid && x.MatterId == matter && x.ActorUserId == actor, c)
                    ?? throw new MatterWorkflowException("Working preview is private or unavailable.", 403);
                var lease = await editor.AuthorizeAsync(preview.SessionId, actor, false, c);
                if (lease.State is not ("Frozen" or "Reviewed") || lease.LatestSnapshotId != preview.SnapshotId) throw new MatterWorkflowException("Select the current frozen committed preview.", 409);
                var user = await db.AppUsers.AsNoTracking().Include(x => x.Designation).SingleAsync(x => x.Id == actor, c);
                input.Add(new((notes.LastOrDefault()?.Number ?? 0) + 1, user.DisplayName, user.CustomDesignation ?? user.Designation?.Name ?? "", clock.GetUtcNow(), preview.ContentJson, true));
            }
            if (input.Count == 0) throw new MatterWorkflowException("There are no submitted notes or committed reviewed working preview to print.", 409);
            using var docx = OfficeNoteDocx.Compose(input);
            var saved = await storage.SaveAndHashAsync(docx, "green-sheet-source.docx", c);
            var source = new MatterGreenSheetSource { MatterId = matter, ActorUserId = actor, WorkingPreviewId = cmd.PreviewId,
                ManifestJson = JsonSerializer.Serialize(new { policy = "GreenLegalV2", composerVersion = "GreenSheetComposerV2.1", fontPolicy = OfficeNoteDocx.FontPolicy,
                    matterRevision = m.Revision, notes = notes.Select(n => new { n.Id, n.Number, n.TextHash, n.AuthorUserId, n.SubmittedAt }),
                    working = preview is null ? null : new { preview.Id, preview.CanonicalHash, preview.SnapshotId, label = "UNSUBMITTED WORKING PREVIEW" } }, Json),
                StoragePath = saved.StoragePath, Sha256 = saved.Sha256Hash.ToLowerInvariant(), CreatedAt = clock.GetUtcNow() };
            db.MatterGreenSheetSources.Add(source);
            return source.Id;
        });
        var existing = await db.MatterGreenSheets.AsNoTracking().SingleOrDefaultAsync(x => x.SourceId == sourceId, ct);
        if (existing is not null) return await MetadataAsync(existing.Id, actor, ct);
        var sourceRow = await SourceAsync(sourceId, actor, ct);
        var fontHash = await office.VerifyRendererAsync(ct);
        var downloadToken = tokens.Sign(new { purpose = "green-sheet-source", sourceId, actor, exp = clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds() });
        var url = office.Options.AppExternalUrl.TrimEnd('/') + $"/api/noting-green-sources/{sourceId}/file?token=" + Uri.EscapeDataString(downloadToken);
        var pdf = await office.ConvertPdfAsync(url, "lac-green-" + sourceId.ToString("N"), ct);
        await using var sourceBytes = await storage.OpenReadAsync(sourceRow.StoragePath, ct) ?? throw new MatterWorkflowException("Composed source is unavailable.", 409);
        using var sourcePackage = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(sourceBytes, false);
        var pageCount = await verifier.ValidateAsync(pdf, sourcePackage.MainDocumentPart!.Document!.Body!.InnerText, ct);
        await office.VerifyRendererAsync(ct); // Refuse publication if version/font provision changed during conversion.
        await SourceAsync(sourceId, actor, ct);
        using var bytes = new MemoryStream(pdf);
        var savedPdf = await storage.SaveAndHashAsync(bytes, "green-sheet.pdf", ct);
        var sheetId = await workflow.OfficeReadCommandAsync(sourceRow.MatterId, actor, Guid.NewGuid(), new { action = "GreenSheetPublished", sourceId }, async c =>
        {
            await SourceAsync(sourceId, actor, c);
            var winner = await db.MatterGreenSheets.SingleOrDefaultAsync(x => x.SourceId == sourceId, c);
            if (winner is not null) return winner.Id;
            var sheet = new MatterGreenSheet { SourceId = sourceId, PdfStoragePath = savedPdf.StoragePath, PdfSha256 = savedPdf.Sha256Hash.ToLowerInvariant(),
                RendererVersion = office.NotingOptions.RendererVersion, FontManifestSha256 = fontHash, PageCount = pageCount, CreatedAt = clock.GetUtcNow() };
            db.MatterGreenSheets.Add(sheet);
            return sheet.Id;
        }, ct);
        return await MetadataAsync(sheetId, actor, ct);
    }

    private async Task<MatterGreenSheetSource> SourceAsync(Guid sourceId, Guid actor, CancellationToken ct)
    {
        var source = await db.MatterGreenSheetSources.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId, ct)
            ?? throw new MatterWorkflowException("Green Sheet source unavailable.", 404);
        await workflow.AuthorizeOfficeReaderAsync(source.MatterId, actor, ct);
        if (source.WorkingPreviewId.HasValue)
        {
            if (source.ActorUserId != actor) throw new MatterWorkflowException("Working-inclusive sheets are private.", 403);
            // Existing private draft read policy, independently from editing/custody.
            await workflow.DraftAsync(source.MatterId, actor, ct);
        }
        return source;
    }

    public async Task<Stream> SourceFileAsync(Guid id, string token, CancellationToken ct)
    {
        var p = tokens.Verify(token);
        if (p.GetProperty("purpose").GetString() != "green-sheet-source" || p.GetProperty("sourceId").GetGuid() != id) throw new UnauthorizedAccessException();
        var source = await SourceAsync(id, p.GetProperty("actor").GetGuid(), ct);
        await using var check = await storage.OpenReadAsync(source.StoragePath, ct) ?? throw new MatterWorkflowException("Source unavailable.", 409);
        if (Convert.ToHexString(await SHA256.HashDataAsync(check, ct)).ToLowerInvariant() != source.Sha256) throw new MatterWorkflowException("Green Sheet source integrity failed.", 409);
        return await storage.OpenReadAsync(source.StoragePath, ct) ?? throw new MatterWorkflowException("Source unavailable.", 409);
    }
    public async Task<object> MetadataAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var sheet = await db.MatterGreenSheets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new MatterWorkflowException("Green Sheet unavailable.", 404);
        var source = await SourceAsync(sheet.SourceId, actor, ct);
        return new { sheet.Id, source.MatterId, policy = "GreenLegalV2", source.ManifestJson, sheet.PdfSha256, sheet.PageCount, sheet.RendererVersion,
            sheet.FontManifestSha256, sheet.CreatedAt, pdfUrl = $"/api/noting-green-sheets/{id}/pdf", actualSizePrintingRequired = true,
            screenOnlyGuides = Enumerable.Range(1, sheet.PageCount).Select(page => new { page, widthMm = 215.9m, heightMm = 355.6m, xMm = page % 2 == 1 ? 45m : 170.9m, color = "red", print = false }) };
    }
    public async Task<Stream> PdfAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var sheet = await db.MatterGreenSheets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new MatterWorkflowException("Green Sheet unavailable.", 404);
        await SourceAsync(sheet.SourceId, actor, ct);
        await using var check = await storage.OpenReadAsync(sheet.PdfStoragePath, ct) ?? throw new MatterWorkflowException("PDF unavailable.", 409);
        if (Convert.ToHexString(await SHA256.HashDataAsync(check, ct)).ToLowerInvariant() != sheet.PdfSha256) throw new MatterWorkflowException("Green Sheet PDF integrity failed.", 409);
        return await storage.OpenReadAsync(sheet.PdfStoragePath, ct) ?? throw new MatterWorkflowException("PDF unavailable.", 409);
    }
}
