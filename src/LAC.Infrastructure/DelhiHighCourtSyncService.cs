using System.Net;
using System.Security.Cryptography;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LAC.Infrastructure;

public sealed class DelhiHighCourtSyncGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}

public sealed record DhcSyncStatusDto(CourtExternalSyncRun? LastAttempt, CourtExternalSyncRun? LastSuccess,
    bool CanSyncNow);
public sealed record DhcObservationDto(Guid Id, Guid? CourtCaseId, string Identity, DateOnly ListingDate,
    DateTimeOffset ObservedAt, string SourceTitle, string SourceUrl, Guid? DocumentId,
    int PageNumber, string RawMatchedText, string Status, string? ConflictReason, string SourceKind,
    string Mode);
public sealed record DhcSourceReviewDto(Guid Id, string SourceTitle, string SourceUrl,
    DateOnly? ListingDate, string Kind, string? FailureMessage);
public sealed record DhcReviewDecisionRequest(bool Accept, Guid? CourtCaseId, DateOnly? ListingDate, string Reason);

public sealed partial class DelhiHighCourtSyncService(
    LacDbContext db, IDocumentStorage storage, IOfficeClock clock,
    IHttpClientFactory clients, IConfiguration configuration,
    ICourtAuthorizationService authorization, DelhiHighCourtSyncGate gate)
{
    public const string Provider = "DELHI_HIGH_COURT_CAUSE_LIST";
    public const string OfficialPage = "https://delhihighcourt.nic.in/web/cause-lists/cause-list";
    private const int MaxPdfBytes = 20 * 1024 * 1024;

    public async Task<DhcSyncStatusDto> StatusAsync(Guid userId, CancellationToken ct)
    {
        if (!await authorization.CanViewCourtReferencesAsync(userId, ct))
            throw new CourtWorkflowException("Global Court view permission is required.", 403);
        var runs = await db.CourtExternalSyncRuns.AsNoTracking().Where(x => x.ProviderCode == Provider &&
            x.Mode == CourtExternalSyncMode.LiveWindow)
            .OrderByDescending(x => x.StartedAt).Take(50).ToListAsync(ct);
        return new(runs.FirstOrDefault(), runs.FirstOrDefault(x => x.Status == CourtExternalSyncRunStatus.Completed),
            await authorization.CanEditCourtReferencesAsync(userId, ct));
    }

    public async Task<IReadOnlyList<DhcObservationDto>> ObservationsAsync(Guid? caseId, bool reviewOnly,
        Guid userId, CancellationToken ct)
    {
        if (caseId.HasValue)
        {
            if (!await authorization.CanViewCourtCaseAsync(caseId.Value, userId, ct))
                throw new CourtWorkflowException("Court case access is required.", 403);
        }
        else if (!await authorization.CanViewCourtReferencesAsync(userId, ct))
            throw new CourtWorkflowException("Global Court view permission is required.", 403);
        var query = db.CourtExternalListingObservations.AsNoTracking().Include(x => x.SourceDocument)
            .Where(x => x.ProviderCode == Provider);
        if (caseId.HasValue) query = query.Where(x => x.CourtCaseId == caseId.Value);
        if (reviewOnly) query = query.Where(x => x.Status == CourtExternalListingStatus.NeedsReview);
        var rows = await query.OrderByDescending(x => x.ObservedAt).ThenBy(x => x.Id).Take(200).ToListAsync(ct);
        return rows.Select(x => new DhcObservationDto(x.Id, x.CourtCaseId, x.NormalizedCaseIdentity,
            x.ListingDate, x.ObservedAt, x.SourceDocument.SourceTitle, x.SourceDocument.SourceUrl,
            x.SourceDocument.DocumentId, x.SourcePageNumber, x.RawMatchedText,
            x.Status.ToString(), x.ConflictReason, x.SourceDocument.Kind.ToString(), x.Mode.ToString())).ToList();
    }

    public async Task<IReadOnlyList<DhcSourceReviewDto>> SourceReviewsAsync(Guid userId, CancellationToken ct)
    {
        if (!await authorization.CanViewCourtReferencesAsync(userId, ct))
            throw new CourtWorkflowException("Global Court view permission is required.", 403);
        var sources = await db.CourtExternalSourceDocuments.AsNoTracking()
            .Where(x => x.Status == CourtExternalSourceStatus.NeedsReview)
            .OrderByDescending(x => x.DiscoveredAt).Take(100).ToListAsync(ct);
        return sources.Select(x => new DhcSourceReviewDto(x.Id, x.SourceTitle, x.SourceUrl,
            x.ListingDate, x.Kind.ToString(), x.FailureMessage)).ToList();
    }

    public async Task<CourtExternalSyncRun> RunAsync(Guid? requestedByUserId, CancellationToken ct)
    {
        if (requestedByUserId.HasValue &&
            (!await authorization.CanViewCourtReferencesAsync(requestedByUserId.Value, ct) ||
             !await authorization.CanEditCourtReferencesAsync(requestedByUserId.Value, ct)))
            throw new CourtWorkflowException("Global or Court-workstream view and edit access is required.", 403);
        if (!await gate.Semaphore.WaitAsync(0, ct))
            throw new CourtWorkflowException("Delhi High Court sync is already running.", 409);
        try
        {
            var now = clock.GetUtcNow();
            var run = new CourtExternalSyncRun { StartedAt = now, Mode = CourtExternalSyncMode.LiveWindow };
            db.CourtExternalSyncRuns.Add(run);
            await db.SaveChangesAsync(ct);
            try
            {
                var configured = configuration["CourtSync:DelhiHighCourt:BaseUrl"] ?? OfficialPage;
                if (!Uri.TryCreate(configured, UriKind.Absolute, out var pageUrl) ||
                    !DelhiHighCourtCauseListParser.IsApprovedUri(pageUrl) ||
                    pageUrl.AbsolutePath != "/web/cause-lists/cause-list")
                    throw new InvalidOperationException("DHC discovery page must be the approved public cause-list URL.");
                var today = clock.GetCurrentDate();
                var end = today.AddDays(Math.Clamp(configuration.GetValue<int?>("CourtSync:DelhiHighCourt:ForwardDays") ?? 7, 1, 30));
                using var http = clients.CreateClient("DelhiHighCourtCauseList");
                var html = await ReadTextAsync(http, pageUrl, 2 * 1024 * 1024, ct);
                var discovered = DelhiHighCourtCauseListParser.Discover(html, pageUrl);
                if (discovered.Count == 0)
                    throw new InvalidDataException("No approved DHC publication rows found; public page layout may have changed.");
                var entries = discovered
                    .Where(x => x.ListingDate >= today && x.ListingDate <= end)
                    // Apply ordinary lists first even if a deletion note appears earlier on the page.
                    .OrderBy(x => x.Kind == CourtExternalSourceKind.DeletionOrCorrigendum ? 1 : 0)
                    .ToList();
                run.SourceDocumentsDiscovered = entries.Count;
                await db.SaveChangesAsync(ct);
                foreach (var publication in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var url = publication.PdfUrl.AbsoluteUri;
                    var source = await db.CourtExternalSourceDocuments.SingleOrDefaultAsync(
                        x => x.ProviderCode == Provider && x.SourceUrl == url, ct);
                    if (source?.Status == CourtExternalSourceStatus.Processed ||
                        source is { Kind: CourtExternalSourceKind.Unsupported, Status: CourtExternalSourceStatus.NeedsReview }) continue;
                    if (source == null)
                    {
                        source = new CourtExternalSourceDocument { SourceUrl = url, SourceTitle = publication.Title,
                            ListingDate = publication.ListingDate, Kind = publication.Kind, DiscoveredAt = clock.GetUtcNow() };
                        db.CourtExternalSourceDocuments.Add(source);
                        await db.SaveChangesAsync(ct);
                    }
                    if (publication.Kind == CourtExternalSourceKind.Unsupported || publication.DateConflict != null ||
                        publication.ListingDate == null)
                    {
                        source.Status = CourtExternalSourceStatus.NeedsReview;
                        source.FailureMessage = publication.DateConflict ?? "Unsupported or undated source title.";
                        run.ReviewCount++;
                        await db.SaveChangesAsync(ct);
                        continue;
                    }
                    try
                    {
                        var bytes = await ReadBytesAsync(http, publication.PdfUrl, MaxPdfBytes, ct);
                        if (bytes.Length < 4 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F')
                            throw new InvalidDataException("Official download is not a PDF.");
                        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                        // If a publication URL changes its content, preserve the old evidence and
                        // require human review instead of silently rewriting an accepted source.
                        if (source.Sha256Hash != null && source.Sha256Hash != sha)
                            throw new InvalidDataException("A previously observed publication URL changed content.");
                        var sameHashSources = await db.CourtExternalSourceDocuments
                            .Where(x => x.ProviderCode == Provider && x.Sha256Hash == sha &&
                                x.DocumentId != null && x.Id != source.Id)
                            .ToListAsync(ct);
                        var storedPeer = sameHashSources.FirstOrDefault();
                        if (source.DocumentId == null && storedPeer != null)
                        {
                            source.DocumentId = storedPeer.DocumentId;
                            source.DownloadedAt = clock.GetUtcNow();
                            source.Sha256Hash = sha;
                            await db.SaveChangesAsync(ct);
                        }
                        if (sameHashSources.Any(x => x.Kind != publication.Kind || x.ListingDate != publication.ListingDate))
                        {
                            // The bytes cannot establish which conflicting publication metadata is correct.
                            // Withdraw any previously accepted date from this PDF until an officer resolves it.
                            var accepted = await db.CourtExternalListingObservations
                                .Where(x => x.SourceDocument.DocumentId == source.DocumentId &&
                                    x.Status == CourtExternalListingStatus.Accepted)
                                .ToListAsync(ct);
                            foreach (var item in accepted)
                                ChangeStatus(item, CourtExternalListingStatus.NeedsReview,
                                    "Identical official PDF has conflicting publication date or source class.", null);
                            source.Status = CourtExternalSourceStatus.NeedsReview;
                            source.FailureMessage = "Identical official PDF has conflicting publication date or source class.";
                            run.ReviewCount++;
                            await db.SaveChangesAsync(ct);
                            continue;
                        }
                        IReadOnlyList<DhcCaseLine> caseLines;
                        using (var pdf = new MemoryStream(bytes, writable: false))
                            caseLines = DelhiHighCourtCauseListParser.ExtractCases(pdf);
                        if (source.DocumentId == null)
                        {
                            using var upload = new MemoryStream(bytes, writable: false);
                            var stored = await storage.SaveAndHashAsync(upload, Path.GetFileName(publication.PdfUrl.LocalPath), ct);
                            if (stored.Sha256Hash != sha) throw new InvalidDataException("Stored official PDF hash mismatch.");
                            var document = new Document { DocumentType = "CourtCauseList", OriginalFileName = Path.GetFileName(publication.PdfUrl.LocalPath),
                                StoragePath = stored.StoragePath, Sha256Hash = sha, FileSize = stored.FileSize, MimeType = "application/pdf",
                                UploadedAt = clock.GetUtcNow(), UploadedBy = Provider };
                            db.Documents.Add(document);
                            source.DocumentId = document.Id;
                            source.DownloadedAt = document.UploadedAt;
                            source.Sha256Hash = sha;
                            await db.SaveChangesAsync(ct);
                        }
                        if (publication.Kind == CourtExternalSourceKind.DeletionOrCorrigendum)
                            await ApplyDeletionSignalsAsync(source, caseLines, run, ct);
                        else
                            await ApplyPositiveListingsAsync(source, caseLines, today, run, ct);
                        source.Status = CourtExternalSourceStatus.Processed;
                        source.FailureMessage = null;
                        run.SourceDocumentsProcessed++;
                        await db.SaveChangesAsync(ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        db.ChangeTracker.Clear();
                        source = await db.CourtExternalSourceDocuments.SingleAsync(x => x.ProviderCode == Provider && x.SourceUrl == url, ct);
                        run = await db.CourtExternalSyncRuns.SingleAsync(x => x.Id == run.Id, ct);
                        source.Status = CourtExternalSourceStatus.NeedsReview;
                        source.FailureMessage = ex.Message[..Math.Min(ex.Message.Length, 500)];
                        run.ReviewCount++;
                        await db.SaveChangesAsync(ct);
                    }
                }
                run.Status = CourtExternalSyncRunStatus.Completed;
                run.CompletedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
                return run;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                run = await db.CourtExternalSyncRuns.SingleAsync(x => x.Id == run.Id, CancellationToken.None);
                run.Status = CourtExternalSyncRunStatus.Failed;
                run.CompletedAt = clock.GetUtcNow();
                run.FailureMessage = ex.Message[..Math.Min(ex.Message.Length, 500)];
                await db.SaveChangesAsync(CancellationToken.None);
                return run;
            }
        }
        finally { gate.Semaphore.Release(); }
    }

    private async Task ApplyPositiveListingsAsync(CourtExternalSourceDocument source, IReadOnlyList<DhcCaseLine> lines,
        DateOnly today, CourtExternalSyncRun run, CancellationToken ct)
    {
        var local = await db.CourtCases.AsNoTracking().Where(x => x.RecordStatus == RecordStatus.Active &&
            x.CourtName == "Delhi High Court").Select(x => new { x.Id, x.CourtName, x.CaseNumber, x.CurrentStatus }).ToListAsync(ct);
        var identities = local.GroupBy(x => CourtImportService.Identity(x.CourtName, x.CaseNumber))
            .Where(x => x.Key != null).ToDictionary(x => x.Key!, x => x.ToList());
        var alreadySeen = (await db.CourtExternalListingObservations.AsNoTracking()
            .Where(x => x.SourceDocument.DocumentId == source.DocumentId &&
                x.ListingDate == source.ListingDate && x.SourceDocument.Kind == source.Kind)
            .Select(x => x.NormalizedCaseIdentity).ToListAsync(ct)).ToHashSet();
        foreach (var line in lines)
        {
            if (!alreadySeen.Add(line.Identity)) continue;
            identities.TryGetValue(line.Identity, out var matches);
            var match = matches?.Count == 1 ? matches[0] : null;
            var observation = new CourtExternalListingObservation { SourceDocumentId = source.Id,
                CourtCaseId = match?.Id, ListingDate = source.ListingDate!.Value,
                ObservedAt = clock.GetUtcNow(), SourcePageNumber = line.PageNumber,
                RawMatchedText = line.Text[..Math.Min(line.Text.Length, 1000)], NormalizedCaseIdentity = line.Identity };
            var reason = match == null ? matches == null ? "No canonical DHC case matches." : "Multiple canonical DHC cases match."
                : string.Equals(match.CurrentStatus?.Trim(), "Disposed", StringComparison.OrdinalIgnoreCase) ? "Canonical case is Disposed."
                : source.ListingDate < today ? "Listing date is in the past."
                : null;
            if (reason == null && match != null)
            {
                var deletionSignal = await db.CourtExternalListingObservations.AnyAsync(x =>
                    x.NormalizedCaseIdentity == line.Identity && x.ListingDate == source.ListingDate &&
                    x.SourceDocument.Kind == CourtExternalSourceKind.DeletionOrCorrigendum, ct);
                if (deletionSignal) reason = "Official deletion/corrigendum signal requires review.";
            }
            if (reason == null && match != null)
            {
                var competing = await db.CourtExternalListingObservations
                    .Where(x => x.ProviderCode == Provider && x.NormalizedCaseIdentity == line.Identity &&
                        x.SourceDocument.Kind == CourtExternalSourceKind.OrdinaryListing &&
                        (x.Status == CourtExternalListingStatus.Accepted ||
                         x.Status == CourtExternalListingStatus.NeedsReview ||
                         x.Status == CourtExternalListingStatus.Observed) &&
                        x.ListingDate >= today && x.ListingDate != source.ListingDate)
                    .ToListAsync(ct);
                if (competing.Count > 0)
                {
                    reason = "Conflicting future official DHC listing dates.";
                    foreach (var old in competing.Where(x => x.Status == CourtExternalListingStatus.Accepted))
                        ChangeStatus(old, CourtExternalListingStatus.NeedsReview, reason, null);
                }
            }
            ChangeStatus(observation, reason == null ? CourtExternalListingStatus.Accepted : CourtExternalListingStatus.NeedsReview,
                reason ?? "Exact future official DHC listing.", null);
            db.CourtExternalListingObservations.Add(observation);
            run.ObservationsCreated++;
            if (reason == null) run.ObservationsAccepted++; else run.ReviewCount++;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyDeletionSignalsAsync(CourtExternalSourceDocument source, IReadOnlyList<DhcCaseLine> lines,
        CourtExternalSyncRun run, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            if (await db.CourtExternalListingObservations.AnyAsync(x =>
                x.SourceDocument.DocumentId == source.DocumentId && x.SourceDocument.Kind == source.Kind &&
                x.NormalizedCaseIdentity == line.Identity && x.ListingDate == source.ListingDate, ct)) continue;
            // Only an exact identity AND list date can invalidate an observation.
            // If a correction proposes an unclear new date, no date is guessed.
            var affected = await db.CourtExternalListingObservations.Where(x =>
                x.NormalizedCaseIdentity == line.Identity && x.ListingDate == source.ListingDate &&
                x.Status == CourtExternalListingStatus.Accepted).ToListAsync(ct);
            var signal = new CourtExternalListingObservation { SourceDocumentId = source.Id,
                CourtCaseId = affected.Count == 1 ? affected[0].CourtCaseId : null,
                ListingDate = source.ListingDate!.Value, ObservedAt = clock.GetUtcNow(),
                SourcePageNumber = line.PageNumber, RawMatchedText = line.Text[..Math.Min(line.Text.Length, 1000)],
                NormalizedCaseIdentity = line.Identity };
            ChangeStatus(signal, CourtExternalListingStatus.NeedsReview,
                "Official deletion/corrigendum signal; no new listing date inferred.", null);
            db.CourtExternalListingObservations.Add(signal);
            run.ObservationsCreated++;
            run.ReviewCount++;
            foreach (var old in affected)
                ChangeStatus(old, CourtExternalListingStatus.Superseded, "Official deletion/corrigendum names this case and listing date.", null);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<DhcObservationDto> ReviewAsync(Guid id, DhcReviewDecisionRequest decision,
        Guid userId, CancellationToken ct)
    {
        if (!await authorization.CanViewCourtReferencesAsync(userId, ct) ||
            !await authorization.CanEditCourtReferencesAsync(userId, ct))
            throw new CourtWorkflowException("Global Court view and edit permission is required.", 403);
        var observation = await db.CourtExternalListingObservations.Include(x => x.SourceDocument)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CourtWorkflowException("Observation not found.", 404);
        if (observation.Status != CourtExternalListingStatus.NeedsReview)
            throw new CourtWorkflowException("Only review observations may be decided.", 409);
        if (string.IsNullOrWhiteSpace(decision.Reason))
            throw new CourtWorkflowException("Review reason is required.", 400);
        if (decision.Accept)
        {
            if (!decision.CourtCaseId.HasValue || !decision.ListingDate.HasValue ||
                decision.ListingDate != observation.ListingDate || observation.SourceDocument.DocumentId == null)
                throw new CourtWorkflowException("Select a canonical case and the evidenced listing date.", 400);
            var courtCase = await db.CourtCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == decision.CourtCaseId &&
                x.RecordStatus == RecordStatus.Active && x.CourtName == "Delhi High Court", ct);
            if (courtCase == null || string.Equals(courtCase.CurrentStatus?.Trim(), "Disposed", StringComparison.OrdinalIgnoreCase) ||
                CourtImportService.Identity(courtCase.CourtName, courtCase.CaseNumber) != observation.NormalizedCaseIdentity ||
                observation.ListingDate < clock.GetCurrentDate() || observation.SourceDocument.Kind != CourtExternalSourceKind.OrdinaryListing)
                throw new CourtWorkflowException("Evidence, identity, future date and active DHC status must match.", 409);
            var other = await db.CourtExternalListingObservations.AnyAsync(x =>
                x.ProviderCode == Provider && x.NormalizedCaseIdentity == observation.NormalizedCaseIdentity &&
                x.SourceDocument.Kind == CourtExternalSourceKind.OrdinaryListing &&
                (x.Status == CourtExternalListingStatus.Accepted ||
                 x.Status == CourtExternalListingStatus.NeedsReview ||
                 x.Status == CourtExternalListingStatus.Observed) &&
                x.ListingDate >= clock.GetCurrentDate() && x.ListingDate != observation.ListingDate, ct);
            if (other) throw new CourtWorkflowException("Resolve different-date official listing evidence first.", 409);
            var deletionSignal = await db.CourtExternalListingObservations.AnyAsync(x =>
                x.NormalizedCaseIdentity == observation.NormalizedCaseIdentity &&
                x.ListingDate == observation.ListingDate &&
                x.SourceDocument.Kind == CourtExternalSourceKind.DeletionOrCorrigendum, ct);
            if (deletionSignal) throw new CourtWorkflowException("Official deletion/corrigendum signal blocks accepting this listing.", 409);
            observation.CourtCaseId = courtCase.Id;
            ChangeStatus(observation, CourtExternalListingStatus.Accepted, decision.Reason, userId);
        }
        else ChangeStatus(observation, CourtExternalListingStatus.Rejected, decision.Reason, userId);
        await db.SaveChangesAsync(ct);
        return new(observation.Id, observation.CourtCaseId, observation.NormalizedCaseIdentity,
            observation.ListingDate, observation.ObservedAt, observation.SourceDocument.SourceTitle,
            observation.SourceDocument.SourceUrl, observation.SourceDocument.DocumentId,
            observation.SourcePageNumber, observation.RawMatchedText, observation.Status.ToString(),
            observation.ConflictReason, observation.SourceDocument.Kind.ToString(), observation.Mode.ToString());
    }

    private void ChangeStatus(CourtExternalListingObservation item, CourtExternalListingStatus status, string reason, Guid? actor)
    {
        var from = item.Status;
        item.Status = status;
        item.ConflictReason = status == CourtExternalListingStatus.Accepted ? null : reason;
        if (status == CourtExternalListingStatus.Accepted) item.AppliedAt = clock.GetUtcNow();
        if (status == CourtExternalListingStatus.Superseded) item.SupersededAt = clock.GetUtcNow();
        db.CourtExternalListingDecisions.Add(new CourtExternalListingDecision { Observation = item,
            FromStatus = from, ToStatus = status, ActorUserId = actor,
            DecidedAt = clock.GetUtcNow(), Reason = reason });
    }

    private static async Task<string> ReadTextAsync(HttpClient http, Uri uri, int maxBytes, CancellationToken ct) =>
        System.Text.Encoding.UTF8.GetString(await ReadBytesAsync(http, uri, maxBytes, ct));

    private static async Task<byte[]> ReadBytesAsync(HttpClient http, Uri uri, int maxBytes, CancellationToken ct)
    {
        if (!DelhiHighCourtCauseListParser.IsApprovedUri(uri)) throw new InvalidOperationException("Unapproved DHC URL.");
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest)
            throw new InvalidOperationException("DHC redirects are not followed.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("DHC source exceeds size limit.");
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > maxBytes) throw new InvalidDataException("DHC source exceeds size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}

public sealed class DelhiHighCourtSyncWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<DelhiHighCourtSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("CourtSync:DelhiHighCourt:Enabled", true)) return;
        var minutes = Math.Clamp(configuration.GetValue<int?>("CourtSync:DelhiHighCourt:IntervalMinutes") ?? 120, 60, 1440);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DelhiHighCourtSyncService>().RunAsync(null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Public DHC cause-list sync failed; existing Court data is retained."); }
            try { await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
