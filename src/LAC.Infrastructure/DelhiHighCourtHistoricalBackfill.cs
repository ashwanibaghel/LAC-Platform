using System.Security.Cryptography;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LAC.Infrastructure;

public sealed record DhcHistoricalStatusDto(int EligibleCaseCount, int NoBaselineCount,
    int RealProceedingExclusionCount, DateOnly? EarliestBaseline, DateOnly? WindowEnd,
    CourtExternalSyncRun? LastAttempt, CourtExternalSyncRun? CompletedRun, bool CanStart);

// Deliberately no BackgroundService: this path is invoked only by the authorized
// manual endpoint. The live monitor keeps its existing today + ForwardDays window.
public sealed partial class DelhiHighCourtSyncService
{
    public const string OfficialArchive = "https://delhihighcourt.nic.in/web/cause-lists/archive-cause-list";
    private const int HistoricalMaxPdfBytes = 30 * 1024 * 1024;

    private sealed record Target(Guid CaseId, string Identity, DateOnly Baseline);
    private sealed record TargetAudit(List<Target> Targets, int NoBaseline, int RealProceeding);

    private async Task<TargetAudit> HistoricalTargetsAsync(DateOnly today, CancellationToken ct)
    {
        var cases = await db.CourtCases.AsNoTracking()
            .Include(x => x.Proceedings)
            .Where(x => x.RecordStatus == RecordStatus.Active && x.CourtName == "Delhi High Court")
            .ToListAsync(ct);
        var identities = cases.Select(x => CourtImportService.Identity(x.CourtName, x.CaseNumber))
            .Where(x => x != null).Select(x => x!).GroupBy(x => x)
            .ToDictionary(x => x.Key, x => x.Count());
        var targets = new List<Target>();
        var noBaseline = 0;
        var realProceeding = 0;
        foreach (var item in cases.Where(x => string.Equals(x.CurrentStatus?.Trim(), "Pending",
                     StringComparison.OrdinalIgnoreCase)))
        {
            var selected = item.Proceedings.Where(x => x.RecordStatus == RecordStatus.Active)
                .OrderByDescending(x => x.ProceedingDate.HasValue)
                .ThenByDescending(x => x.ProceedingDate)
                .ThenByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id).FirstOrDefault();
            if (selected?.ProceedingDate != null) { realProceeding++; continue; }
            if (selected?.SourceKind != "LegacyRegisterNDOH" || selected.NextDate == null)
            {
                noBaseline++;
                continue;
            }
            if (selected.NextDate >= today) continue;
            var identity = CourtImportService.Identity(item.CourtName, item.CaseNumber);
            if (identity != null && identities[identity] == 1)
                targets.Add(new Target(item.Id, identity, selected.NextDate.Value));
        }
        return new(targets, noBaseline, realProceeding);
    }

    public async Task<DhcHistoricalStatusDto> HistoricalStatusAsync(Guid userId, CancellationToken ct)
    {
        if (!await authorization.CanViewCourtReferencesAsync(userId, ct))
            throw new CourtWorkflowException("Global Court view permission is required.", 403);
        var today = clock.GetCurrentDate();
        var audit = await HistoricalTargetsAsync(today, ct);
        var runs = await db.CourtExternalSyncRuns.AsNoTracking()
            .Where(x => x.ProviderCode == Provider && x.Mode == CourtExternalSyncMode.HistoricalBackfill)
            .OrderByDescending(x => x.StartedAt).Take(50).ToListAsync(ct);
        var completed = await db.CourtExternalSyncRuns.AsNoTracking()
            .Where(x => x.ProviderCode == Provider && x.Mode == CourtExternalSyncMode.HistoricalBackfill &&
                x.Status == CourtExternalSyncRunStatus.Completed)
            .OrderByDescending(x => x.CompletedAt).FirstOrDefaultAsync(ct);
        return new(audit.Targets.Count, audit.NoBaseline, audit.RealProceeding,
            audit.Targets.Count == 0 ? null : audit.Targets.Min(x => x.Baseline),
            today.AddDays(-1), runs.FirstOrDefault(), completed,
            completed == null && gate.Semaphore.CurrentCount > 0 &&
            await authorization.CanEditCourtReferencesAsync(userId, ct));
    }

    public async Task<CourtExternalSyncRun> RunHistoricalAsync(Guid userId, CancellationToken ct)
    {
        if (!await authorization.CanViewCourtReferencesAsync(userId, ct) ||
            !await authorization.CanEditCourtReferencesAsync(userId, ct))
            throw new CourtWorkflowException("Global or Court-workstream view and edit access is required.", 403);
        if (!await gate.Semaphore.WaitAsync(0, ct))
            throw new CourtWorkflowException("Delhi High Court sync is already running.", 409);
        try
        {
            if (await db.CourtExternalSyncRuns.AnyAsync(x => x.ProviderCode == Provider &&
                x.Mode == CourtExternalSyncMode.HistoricalBackfill &&
                x.Status == CourtExternalSyncRunStatus.Completed, ct))
                throw new CourtWorkflowException("One-time historical backfill already completed.", 409);
            var interrupted = await db.CourtExternalSyncRuns.Where(x => x.ProviderCode == Provider &&
                x.Mode == CourtExternalSyncMode.HistoricalBackfill &&
                x.Status == CourtExternalSyncRunStatus.Running).ToListAsync(ct);
            foreach (var old in interrupted)
            {
                old.Status = CourtExternalSyncRunStatus.Failed;
                old.CompletedAt = clock.GetUtcNow();
                old.FailureMessage = "Interrupted before completion; resumed in a new idempotent attempt.";
            }
            if (interrupted.Count > 0) await db.SaveChangesAsync(ct);
            var today = clock.GetCurrentDate();
            var audit = await HistoricalTargetsAsync(today, ct);
            var run = new CourtExternalSyncRun
            {
                Mode = CourtExternalSyncMode.HistoricalBackfill, StartedAt = clock.GetUtcNow(),
                WindowStart = audit.Targets.Count == 0 ? null : audit.Targets.Min(x => x.Baseline),
                WindowEnd = today.AddDays(-1), EligibleCaseCount = audit.Targets.Count
            };
            db.CourtExternalSyncRuns.Add(run);
            await db.SaveChangesAsync(ct);
            try
            {
                if (audit.Targets.Count > 0)
                {
                    var targets = audit.Targets.ToDictionary(x => x.Identity);
                    using var http = clients.CreateClient("DelhiHighCourtCauseList");
                    var delay = Math.Max(750, configuration.GetValue<int?>("CourtSync:DelhiHighCourt:HistoricalDelayMs") ?? 750);
                    var lastRequest = DateTimeOffset.MinValue;
                    async Task<byte[]> PoliteGet(Uri uri, int maxBytes)
                    {
                        var wait = TimeSpan.FromMilliseconds(delay) - (clock.GetUtcNow() - lastRequest);
                        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                        lastRequest = clock.GetUtcNow();
                        return await ReadBytesAsync(http, uri, maxBytes, ct);
                    }
                    var publications = await DiscoverHistoricalAsync(run.WindowStart!.Value, run.WindowEnd!.Value,
                        run, PoliteGet, ct);
                    foreach (var publication in publications.OrderBy(x =>
                                 x.Kind == CourtExternalSourceKind.DeletionOrCorrigendum ? 1 : 0))
                    {
                        ct.ThrowIfCancellationRequested();
                        await ProcessHistoricalPublicationAsync(publication, targets, today, run, PoliteGet, ct);
                    }
                    var advanced = 0;
                    foreach (var target in audit.Targets)
                    {
                        if (!await IsStillEligibleAsync(target, today, ct)) continue;
                        if (await db.CourtExternalListingObservations.AsNoTracking().AnyAsync(x =>
                            x.CourtCaseId == target.CaseId && x.Mode == CourtExternalSyncMode.HistoricalBackfill &&
                            x.Status == CourtExternalListingStatus.Accepted && x.ListingDate > target.Baseline &&
                            x.ListingDate < today, ct)) advanced++;
                    }
                    run.CasesAdvanced = advanced;
                }
                run.Status = CourtExternalSyncRunStatus.Completed;
                run.CompletedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
                return run;
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                run = await db.CourtExternalSyncRuns.SingleAsync(x => x.Id == run.Id, CancellationToken.None);
                run.Status = CourtExternalSyncRunStatus.Failed;
                run.CompletedAt = clock.GetUtcNow();
                run.FailureMessage = ex.Message[..Math.Min(ex.Message.Length, 500)];
                await db.SaveChangesAsync(CancellationToken.None);
                if (ex is OperationCanceledException) throw;
                return run;
            }
        }
        finally { gate.Semaphore.Release(); }
    }

    private async Task<IReadOnlyList<DhcPublication>> DiscoverHistoricalAsync(DateOnly start, DateOnly end,
        CourtExternalSyncRun run, Func<Uri, int, Task<byte[]>> get, CancellationToken ct)
    {
        var publications = new Dictionary<string, DhcPublication>(StringComparer.Ordinal);
        var maxPages = Math.Clamp(configuration.GetValue<int?>("CourtSync:DelhiHighCourt:HistoricalMaxPages") ?? 500, 1, 1000);
        // The current public listing is paged back from today; the separately
        // published archive currently begins at June 2026. Both surfaces are
        // required to avoid silently omitting the intervening months.
        var roots = new List<Uri> { new(OfficialPage) };
        for (var year = end.Year; year >= start.Year; year--)
            roots.Add(new Uri(OfficialArchive + "?title=" + year));
        foreach (var root in roots)
        {
            var seenPages = new HashSet<string>(StringComparer.Ordinal);
            Uri? pageUrl = root;
            while (pageUrl != null)
            {
                ct.ThrowIfCancellationRequested();
                if (run.ArchivePagesDiscovered >= maxPages)
                    throw new InvalidDataException("Historical public archive page safety cap reached before baseline.");
                if (!seenPages.Add(pageUrl.AbsoluteUri))
                    throw new InvalidDataException("Public archive pagination cycle detected.");
                var html = System.Text.Encoding.UTF8.GetString(await get(pageUrl, 2 * 1024 * 1024));
                var page = DelhiHighCourtCauseListParser.DiscoverArchivePage(html, pageUrl);
                run.ArchivePagesDiscovered++;
                foreach (var item in page.Publications.Where(x => x.ListingDate >= start && x.ListingDate <= end))
                    publications.TryAdd(item.PdfUrl.AbsoluteUri, item);
                await db.SaveChangesAsync(ct);
                // Date order is descending on the official public pages. If
                // layout/order changes, fail closed rather than silently skip.
                var dates = page.Publications.Select(x => x.ListingDate!.Value).ToList();
                if (dates.Zip(dates.Skip(1)).Any(x => x.First < x.Second))
                    throw new InvalidDataException("Public archive date order changed.");
                if (dates.Max() < start || dates.Min() < start) break;
                pageUrl = page.NextPage;
            }
        }
        run.SourceDocumentsDiscovered = publications.Values.Count(x =>
            x.Kind != CourtExternalSourceKind.Unsupported && x.DateConflict == null);
        await db.SaveChangesAsync(ct);
        return publications.Values.ToList();
    }

    private async Task ProcessHistoricalPublicationAsync(DhcPublication publication,
        Dictionary<string, Target> targets, DateOnly today, CourtExternalSyncRun run,
        Func<Uri, int, Task<byte[]>> get, CancellationToken ct)
    {
        if (publication.Kind == CourtExternalSourceKind.Unsupported || publication.ListingDate == null ||
            publication.DateConflict != null) { if (publication.DateConflict != null) run.ReviewCount++; return; }
        var source = await db.CourtExternalSourceDocuments.SingleOrDefaultAsync(
            x => x.ProviderCode == Provider && x.SourceUrl == publication.PdfUrl.AbsoluteUri, ct);
        if (source == null)
        {
            source = new CourtExternalSourceDocument
            {
                SourceUrl = publication.PdfUrl.AbsoluteUri, SourceTitle = publication.Title,
                ListingDate = publication.ListingDate, Kind = publication.Kind, DiscoveredAt = clock.GetUtcNow()
            };
            db.CourtExternalSourceDocuments.Add(source);
            await db.SaveChangesAsync(ct);
        }
        if (source.ListingDate != publication.ListingDate || source.Kind != publication.Kind)
            throw new InvalidDataException("Previously stored official source metadata differs from archive.");
        byte[] bytes;
        if (source.DocumentId != null)
        {
            var document = await db.Documents.AsNoTracking().SingleAsync(x => x.Id == source.DocumentId, ct);
            await using var stream = await storage.OpenReadAsync(document.StoragePath, ct)
                ?? throw new InvalidDataException("Stored official PDF is missing.");
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }
        else bytes = await get(publication.PdfUrl, HistoricalMaxPdfBytes);
        if (bytes.Length < 4 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F')
            throw new InvalidDataException("Official download is not a PDF.");
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (source.Sha256Hash != null && source.Sha256Hash != sha)
            throw new InvalidDataException("Previously stored official PDF hash differs.");
        var peer = await db.CourtExternalSourceDocuments.AsNoTracking()
            .Where(x => x.ProviderCode == Provider && x.Sha256Hash == sha && x.DocumentId != null && x.Id != source.Id)
            .FirstOrDefaultAsync(ct);
        if (peer != null && (peer.ListingDate != source.ListingDate || peer.Kind != source.Kind))
        {
            source.DocumentId = peer.DocumentId;
            source.Sha256Hash = sha;
            source.DownloadedAt = clock.GetUtcNow();
            source.Status = CourtExternalSourceStatus.NeedsReview;
            source.FailureMessage = "Identical official PDF has conflicting publication date or source class.";
            var accepted = await db.CourtExternalListingObservations.Where(x =>
                x.SourceDocument.DocumentId == peer.DocumentId &&
                x.Status == CourtExternalListingStatus.Accepted).ToListAsync(ct);
            foreach (var item in accepted)
                ChangeStatus(item, CourtExternalListingStatus.NeedsReview,
                    "Identical official PDF has conflicting publication date or source class.", null);
            run.ReviewCount++;
            await db.SaveChangesAsync(ct);
            return;
        }
        if (source.DocumentId == null && peer != null)
        {
            source.DocumentId = peer.DocumentId;
            source.Sha256Hash = sha;
            source.DownloadedAt = clock.GetUtcNow();
        }
        if (source.DocumentId == null)
        {
            using var upload = new MemoryStream(bytes, writable: false);
            var stored = await storage.SaveAndHashAsync(upload, Path.GetFileName(publication.PdfUrl.LocalPath), ct);
            if (stored.Sha256Hash != sha) throw new InvalidDataException("Stored official PDF hash mismatch.");
            var document = new Document
            {
                DocumentType = "CourtCauseList", OriginalFileName = Path.GetFileName(publication.PdfUrl.LocalPath),
                StoragePath = stored.StoragePath, Sha256Hash = sha, FileSize = stored.FileSize,
                MimeType = "application/pdf", UploadedAt = clock.GetUtcNow(), UploadedBy = Provider
            };
            db.Documents.Add(document);
            source.DocumentId = document.Id;
            source.Sha256Hash = sha;
            source.DownloadedAt = document.UploadedAt;
        }
        await db.SaveChangesAsync(ct);
        IReadOnlyList<DhcCaseLine> lines;
        using (var pdf = new MemoryStream(bytes, writable: false))
            lines = DelhiHighCourtCauseListParser.ExtractCases(pdf);
        foreach (var line in lines.Where(x => targets.ContainsKey(x.Identity)))
        {
            var target = targets[line.Identity];
            if (publication.ListingDate < target.Baseline || publication.ListingDate >= today) continue;
            if (await db.CourtExternalListingObservations.AnyAsync(x =>
                x.SourceDocumentId == source.Id && x.NormalizedCaseIdentity == line.Identity &&
                x.ListingDate == publication.ListingDate && x.Mode == CourtExternalSyncMode.HistoricalBackfill, ct))
                continue;
            var observation = new CourtExternalListingObservation
            {
                Mode = CourtExternalSyncMode.HistoricalBackfill, SourceDocumentId = source.Id,
                CourtCaseId = target.CaseId, ListingDate = publication.ListingDate.Value,
                ObservedAt = clock.GetUtcNow(), SourcePageNumber = line.PageNumber,
                RawMatchedText = line.Text[..Math.Min(line.Text.Length, 1000)],
                NormalizedCaseIdentity = line.Identity
            };
            if (publication.Kind == CourtExternalSourceKind.DeletionOrCorrigendum)
            {
                ChangeStatus(observation, CourtExternalListingStatus.NeedsReview,
                    "Official historical deletion/corrigendum signal; no new date inferred.", null);
                var affected = await db.CourtExternalListingObservations.Where(x =>
                    x.NormalizedCaseIdentity == line.Identity && x.ListingDate == publication.ListingDate &&
                    x.Status == CourtExternalListingStatus.Accepted).ToListAsync(ct);
                foreach (var old in affected)
                    ChangeStatus(old, CourtExternalListingStatus.Superseded,
                        "Official deletion/corrigendum names this case and listing date.", null);
                run.ReviewCount++;
            }
            else
            {
                var deletion = await db.CourtExternalListingObservations.AnyAsync(x =>
                    x.NormalizedCaseIdentity == line.Identity && x.ListingDate == publication.ListingDate &&
                    x.SourceDocument.Kind == CourtExternalSourceKind.DeletionOrCorrigendum, ct);
                var eligible = !deletion && await IsStillEligibleAsync(target, today, ct);
                ChangeStatus(observation, eligible ? CourtExternalListingStatus.Accepted :
                    CourtExternalListingStatus.NeedsReview, eligible ?
                    "Exact past official DHC listing at or after stale register baseline." :
                    "Historical listing needs review: deletion signal or baseline changed.", null);
                if (eligible) run.ObservationsAccepted++; else run.ReviewCount++;
            }
            db.CourtExternalListingObservations.Add(observation);
            run.ObservationsCreated++;
            run.TargetCaseMatches++;
        }
        source.Status = CourtExternalSourceStatus.Processed;
        run.SourceDocumentsProcessed++;
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> IsStillEligibleAsync(Target target, DateOnly today, CancellationToken ct)
    {
        var item = await db.CourtCases.AsNoTracking().Include(x => x.Proceedings)
            .SingleOrDefaultAsync(x => x.Id == target.CaseId && x.RecordStatus == RecordStatus.Active &&
                x.CourtName == "Delhi High Court", ct);
        if (item == null || !string.Equals(item.CurrentStatus?.Trim(), "Pending", StringComparison.OrdinalIgnoreCase) ||
            CourtImportService.Identity(item.CourtName, item.CaseNumber) != target.Identity) return false;
        var active = await db.CourtCases.AsNoTracking().Where(x => x.RecordStatus == RecordStatus.Active &&
            x.CourtName == "Delhi High Court").Select(x => x.CaseNumber).ToListAsync(ct);
        if (active.Count(x => CourtImportService.Identity("Delhi High Court", x) == target.Identity) != 1)
            return false;
        var selected = item.Proceedings.Where(x => x.RecordStatus == RecordStatus.Active)
            .OrderByDescending(x => x.ProceedingDate.HasValue).ThenByDescending(x => x.ProceedingDate)
            .ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefault();
        return selected?.ProceedingDate == null && selected?.SourceKind == "LegacyRegisterNDOH" &&
            selected.NextDate == target.Baseline && selected.NextDate < today;
    }
}
