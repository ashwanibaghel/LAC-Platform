using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Channels;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace LAC.Infrastructure;

public sealed record DocumentToken(string Text, decimal X, decimal Y, decimal Width, decimal Height, decimal? Confidence = null);
public sealed record NormalizedDocumentPage(int PageNumber, decimal Width, decimal Height, AwardDocumentExtractionMethod Method, string Text, IReadOnlyList<DocumentToken> Tokens, decimal? Confidence = null, string? Warning = null);
public interface IOcrEngine { Task<NormalizedDocumentPage?> ExtractAsync(Stream pdf, int pageNumber, CancellationToken ct); }
public sealed class UnavailableOcrEngine : IOcrEngine { public Task<NormalizedDocumentPage?> ExtractAsync(Stream pdf, int pageNumber, CancellationToken ct) => Task.FromResult<NormalizedDocumentPage?>(null); }

/// <summary>Runs only locally installed Poppler and Tesseract executables. No document bytes leave this machine.</summary>
public sealed class TesseractOcrEngine(LocalStoragePaths paths, IConfiguration configuration, ILogger<TesseractOcrEngine> logger) : IOcrEngine, IAsyncDisposable
{
    private string? _workingDirectory;
    private string? _stagedPdf;

    public async Task<NormalizedDocumentPage?> ExtractAsync(Stream pdf, int pageNumber, CancellationToken ct)
    {
        var tesseract = configuration["Ocr:TesseractExecutable"];
        var renderer = configuration["Ocr:PdfToImageExecutable"];
        if (!Enabled() || string.IsNullOrWhiteSpace(tesseract) || string.IsNullOrWhiteSpace(renderer) || !File.Exists(tesseract) || !File.Exists(renderer))
        {
            logger.LogWarning("Local OCR is enabled but its executable configuration is incomplete.");
            return null;
        }

        if (_stagedPdf is null)
        {
            _workingDirectory = Path.Combine(paths.ExtractionRoot, "ocr", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workingDirectory);
            _stagedPdf = Path.Combine(_workingDirectory, "source.pdf");
            if (pdf.CanSeek) pdf.Position = 0;
            await using var output = new FileStream(_stagedPdf, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await pdf.CopyToAsync(output, 131072, ct);
        }

        var pagePrefix = Path.Combine(_workingDirectory!, $"page-{pageNumber}");
        var rendered = await RunAsync(renderer, ["-f", pageNumber.ToString(CultureInfo.InvariantCulture), "-l", pageNumber.ToString(CultureInfo.InvariantCulture), "-r", "300", "-png", _stagedPdf, pagePrefix], ct);
        if (rendered.ExitCode != 0) { logger.LogWarning("Local PDF rendering failed for page {Page}: {Error}", pageNumber, rendered.Error); return null; }
        var image = Directory.GetFiles(_workingDirectory!, $"page-{pageNumber}-*.png").SingleOrDefault();
        if (image is null) { logger.LogWarning("Local PDF renderer produced no image for page {Page}.", pageNumber); return null; }

        var outputBase = Path.Combine(_workingDirectory!, $"ocr-{pageNumber}");
        var language = string.IsNullOrWhiteSpace(configuration["Ocr:Language"]) ? "eng" : configuration["Ocr:Language"]!;
        var ocr = await RunAsync(tesseract, [image, outputBase, "-l", language, "--psm", "6", "tsv"], ct);
        var tsvPath = outputBase + ".tsv";
        if (ocr.ExitCode != 0 || !File.Exists(tsvPath)) { logger.LogWarning("Tesseract failed for page {Page}: {Error}", pageNumber, ocr.Error); return null; }

        var primary = await ReadTsvAsync(tsvPath, pageNumber, ct);
        if (primary is null || primary.Confidence < .75m)
        {
            // Automatic layout is a second independent pass for dense/multi-column pages.
            // Select one complete result, never assemble identifiers from disagreeing OCR passes.
            var alternateBase = outputBase + "-auto";
            var alternateRun = await RunAsync(tesseract, [image, alternateBase, "-l", language, "--psm", "3", "-c", "thresholding_method=2", "tsv"], ct);
            if (alternateRun.ExitCode == 0 && File.Exists(alternateBase + ".tsv"))
            {
                var alternate = await ReadTsvAsync(alternateBase + ".tsv", pageNumber, ct);
                if (alternate is not null && (primary is null || (alternate.Confidence ?? 0) > (primary.Confidence ?? 0))) primary = alternate;
            }
        }
        return primary is not null && primary.Confidence < .75m ? primary with { Warning = "Low OCR confidence: verify identifiers and numeric cells against the PDF; no character substitutions were made." } : primary;
    }

    private static async Task<NormalizedDocumentPage?> ReadTsvAsync(string tsvPath, int pageNumber, CancellationToken ct)
    {
        var words = new List<DocumentToken>();
        decimal pageWidth = 0, pageHeight = 0;
        foreach (var line in await File.ReadAllLinesAsync(tsvPath, ct))
        {
            var fields = line.Split('\t');
            if (fields.Length >= 10 && fields[0] == "1")
            {
                decimal.TryParse(fields[8], NumberStyles.Number, CultureInfo.InvariantCulture, out pageWidth);
                decimal.TryParse(fields[9], NumberStyles.Number, CultureInfo.InvariantCulture, out pageHeight);
            }
            if (fields.Length < 12 || fields[0] != "5" || string.IsNullOrWhiteSpace(fields[11])) continue;
            if (!decimal.TryParse(fields[6], NumberStyles.Number, CultureInfo.InvariantCulture, out var x) || !decimal.TryParse(fields[7], NumberStyles.Number, CultureInfo.InvariantCulture, out var y) || !decimal.TryParse(fields[8], NumberStyles.Number, CultureInfo.InvariantCulture, out var width) || !decimal.TryParse(fields[9], NumberStyles.Number, CultureInfo.InvariantCulture, out var height)) continue;
            decimal? confidence = decimal.TryParse(fields[10], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedConfidence) ? NormalizeConfidence(parsedConfidence) : null;
            words.Add(new DocumentToken(fields[11].Trim(), x, y, width, height, confidence));
        }
        var text = string.Join(' ', words.Select(word => word.Text));
        if (text.Length == 0) return null;
        var confidences = words.Where(word => word.Confidence is not null).Select(word => word.Confidence!.Value).ToList();
        return new NormalizedDocumentPage(pageNumber, pageWidth, pageHeight, AwardDocumentExtractionMethod.LocalOcr, text, words, confidences.Count == 0 ? null : confidences.Average());
    }

    public static decimal? NormalizeConfidence(decimal percentage) => percentage is >= 0 and <= 100 ? decimal.Round(percentage / 100m, 4) : null;

    public ValueTask DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_workingDirectory) && Directory.Exists(_workingDirectory))
        {
            try { Directory.Delete(_workingDirectory, recursive: true); } catch (Exception ex) { logger.LogWarning(ex, "Could not clean local OCR working directory."); }
        }
        return ValueTask.CompletedTask;
    }

    private bool Enabled() => bool.TryParse(configuration["Ocr:Enabled"], out var enabled) && enabled;
    private static async Task<(int ExitCode, string Error)> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start local OCR process.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var error = process.StandardError.ReadToEndAsync(timeout.Token); var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException("Local OCR/rendering exceeded the two-minute per-process limit.");
        }
        return (process.ExitCode, string.Join(Environment.NewLine, await error, await output));
    }
}
public interface IAwardSectionClassifier { IReadOnlyList<string> Classify(string text); }
public sealed class AwardSectionClassifier : IAwardSectionClassifier
{
    private static readonly IReadOnlyDictionary<string, string[]> Vocabulary = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["AwardIdentity"] = ["award no", "award number", "award date"], ["Notifications"] = ["section 4", "u/s 4", "section 6", "notification"],
        ["LandAwarded"] = ["khasra", "land awarded", "area awarded"], ["Possession"] = ["possession"], ["CourtCases"] = ["case no", "cwp", "w.p."],
        ["Claims"] = ["claimant", "claim"], ["MarketValue"] = ["market value"], ["CompensationRules"] = ["solatium", "interest"], ["SupplementaryMatters"] = ["supplementary"]
    };
    public IReadOnlyList<string> Classify(string text) => Vocabulary.Where(pair => pair.Value.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase))).Select(pair => pair.Key).ToList();
}
public interface IAwardPdfJobQueue { ValueTask EnqueueAsync(Guid jobId, CancellationToken ct); IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct); }
public sealed class AwardPdfJobQueue : IAwardPdfJobQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = false });
    public ValueTask EnqueueAsync(Guid jobId, CancellationToken ct) => _channel.Writer.WriteAsync(jobId, ct);
    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
public sealed record AwardPdfUploadResult(Guid? JobId, Guid DocumentId);
public sealed record AwardPdfJobSummary(Guid Id, AwardDocumentExtractionJobStatus Status, Guid DocumentId, Guid? IngestionSessionId, Guid? TargetAwardId, int? TotalPages, int ProcessedPages, string? CurrentStage, string? ErrorMessage, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed class AwardPdfExtractionService(LacDbContext db, IDocumentStorage storage, IAwardPdfJobQueue queue)
{
    public async Task<AwardPdfUploadResult> QueueUploadAsync(Stream content, string fileName, string? contentType, Guid? targetAwardId, Guid? selectedVillageId, string? uploadedBy, CancellationToken ct)
    {
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new AwardIngestionException("Choose a PDF file.");
        var signature = new byte[5]; var signatureRead = await content.ReadAsync(signature.AsMemory(), ct);
        if (signatureRead < 4 || signature[0] != '%' || signature[1] != 'P' || signature[2] != 'D' || signature[3] != 'F') throw new AwardIngestionException("The uploaded file is not a valid PDF.");
        if (!content.CanSeek) throw new AwardIngestionException("The uploaded PDF stream cannot be safely stored."); content.Position = 0;
        if (targetAwardId is not null && !await db.Awards.AnyAsync(x => x.Id == targetAwardId, ct)) throw new AwardIngestionException("Target Award was not found.", 404);
        if (selectedVillageId is not null && !await db.Villages.AnyAsync(x => x.Id == selectedVillageId, ct)) throw new AwardIngestionException("Selected Village was not found.", 404);
        if (targetAwardId is not null && selectedVillageId is not null && !await db.AwardVillages.AnyAsync(x => x.AwardId == targetAwardId && x.VillageId == selectedVillageId, ct)) throw new AwardIngestionException("The selected Village must be directly linked to the target Award.");
        var saved = await storage.SaveAndHashAsync(content, fileName, ct);
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Sha256Hash == saved.Sha256Hash && x.Status == "Active", ct);
        if (document is null)
        {
            document = new Document { DocumentType = "Award PDF", OriginalFileName = Path.GetFileName(fileName), StoragePath = saved.StoragePath, Sha256Hash = saved.Sha256Hash, MimeType = string.IsNullOrWhiteSpace(contentType) ? "application/pdf" : contentType, FileSize = saved.FileSize, UploadedBy = Clean(uploadedBy) };
            db.Documents.Add(document);
        }
        else await storage.DeleteAsync(saved.StoragePath, ct);
        if (targetAwardId is Guid owner && !await db.DocumentAwards.AnyAsync(x => x.DocumentId == document.Id && x.AwardId == owner, ct))
            db.DocumentAwards.Add(new DocumentAward { Document = document, AwardId = owner });
        await db.SaveChangesAsync(ct);
        return new(null, document.Id);
    }
    public async Task<AwardPdfUploadResult> AnalyzeAsync(Guid documentId, Guid targetAwardId, Guid? selectedVillageId, CancellationToken ct)
    {
        if (!await db.DocumentAwards.AnyAsync(x=>x.DocumentId==documentId && x.AwardId==targetAwardId,ct)) throw new AwardIngestionException("This document is not linked to the selected Award.",404);
        if (selectedVillageId is not null && !await db.AwardVillages.AnyAsync(x=>x.AwardId==targetAwardId && x.VillageId==selectedVillageId,ct)) throw new AwardIngestionException("Choose a Village linked to this Award.");
        var active=await db.AwardDocumentExtractionJobs.Where(x=>x.DocumentId==documentId && x.TargetAwardId==targetAwardId && (x.Status==AwardDocumentExtractionJobStatus.Queued || x.Status==AwardDocumentExtractionJobStatus.Extracting || x.Status==AwardDocumentExtractionJobStatus.Analyzing || x.Status==AwardDocumentExtractionJobStatus.BuildingCandidates)).Select(x=>(Guid?)x.Id).FirstOrDefaultAsync(ct);
        if(active is Guid existing) return new(existing,documentId);
        var job=new AwardDocumentExtractionJob{DocumentId=documentId,TargetAwardId=targetAwardId,SelectedVillageId=selectedVillageId,CurrentStage="Waiting to analyze"};
        db.AwardDocumentExtractionJobs.Add(job);await db.SaveChangesAsync(ct);await queue.EnqueueAsync(job.Id,ct);return new(job.Id,documentId);
    }
    public async Task<AwardPdfJobSummary> GetAsync(Guid jobId, CancellationToken ct) => await db.AwardDocumentExtractionJobs.AsNoTracking().Where(x => x.Id == jobId).Select(x => new AwardPdfJobSummary(x.Id, x.Status, x.DocumentId, x.IngestionSessionId, x.TargetAwardId, x.TotalPages, x.ProcessedPages, x.CurrentStage, x.ErrorMessage, x.CreatedAt, x.CompletedAt)).SingleOrDefaultAsync(ct) ?? throw new AwardIngestionException("PDF extraction job was not found.", 404);
    public async Task<IReadOnlyList<AwardPdfJobSummary>> GetForAwardAsync(Guid awardId, CancellationToken ct) => await db.AwardDocumentExtractionJobs.AsNoTracking().Where(x => x.TargetAwardId == awardId).OrderByDescending(x => x.CreatedAt).Take(20).Select(x => new AwardPdfJobSummary(x.Id, x.Status, x.DocumentId, x.IngestionSessionId, x.TargetAwardId, x.TotalPages, x.ProcessedPages, x.CurrentStage, x.ErrorMessage, x.CreatedAt, x.CompletedAt)).ToListAsync(ct);
    public async Task<IReadOnlyList<AwardPdfJobSummary>> GetRecentUnassignedAsync(CancellationToken ct) => await db.AwardDocumentExtractionJobs.AsNoTracking().Where(x => x.TargetAwardId == null).OrderByDescending(x => x.CreatedAt).Take(20).Select(x => new AwardPdfJobSummary(x.Id, x.Status, x.DocumentId, x.IngestionSessionId, x.TargetAwardId, x.TotalPages, x.ProcessedPages, x.CurrentStage, x.ErrorMessage, x.CreatedAt, x.CompletedAt)).ToListAsync(ct);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class AwardPdfExtractionWorker(IServiceScopeFactory scopes, IAwardPdfJobQueue queue, Microsoft.Extensions.Configuration.IConfiguration configuration, IOptions<DocumentIntelligenceOptions> intelligence, ILogger<AwardPdfExtractionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var startupScope = scopes.CreateAsyncScope(); var startupDb = startupScope.ServiceProvider.GetRequiredService<LacDbContext>();
        foreach (var id in await startupDb.AwardDocumentExtractionJobs.Where(x => x.Status == AwardDocumentExtractionJobStatus.Queued || x.Status == AwardDocumentExtractionJobStatus.Extracting || x.Status == AwardDocumentExtractionJobStatus.Analyzing || x.Status == AwardDocumentExtractionJobStatus.BuildingCandidates).Select(x => x.Id).ToListAsync(stoppingToken)) await queue.EnqueueAsync(id, stoppingToken);
        var configuredConcurrency = intelligence.Value.Enabled
            ? intelligence.Value.MaxConcurrentJobs
            : (int.TryParse(configuration["PdfImport:MaxConcurrentJobs"], out var legacyConcurrency) ? legacyConcurrency : 1);
        var concurrency = Math.Clamp(configuredConcurrency, 1, 2);
        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => ProcessQueueAsync(stoppingToken)));
    }
    private async Task ProcessQueueAsync(CancellationToken stoppingToken) { await foreach (var id in queue.DequeueAllAsync(stoppingToken)) { try { await using var scope = scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<AwardPdfJobRunner>().RunAsync(id, stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { } catch (Exception ex) { logger.LogError(ex, "PDF job {JobId} failed; the API and queue remain available", id); } } }
}

public sealed class AwardPdfJobRunner(LacDbContext db, IDocumentStorage storage, AwardIngestionService ingestion, IOcrEngine ocr, AwardExtractionRuleEngine rules, ILogger<AwardPdfJobRunner> logger, ILocalDocumentIntelligenceClient? intelligence = null, IOptions<DocumentIntelligenceOptions>? intelligenceOptions = null, LocalStoragePaths? localPaths = null, IAwardPdfJobQueue? queue = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<AwardPdfJobSummary> ReanalyzeAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.AwardDocumentExtractionJobs.Include(x => x.Pages).Include(x => x.SelectedVillage).SingleOrDefaultAsync(x => x.Id == jobId, ct) ?? throw new AwardIngestionException("PDF extraction job was not found.", 404);
        if(job.Status is AwardDocumentExtractionJobStatus.Queued or AwardDocumentExtractionJobStatus.Extracting or AwardDocumentExtractionJobStatus.Analyzing or AwardDocumentExtractionJobStatus.BuildingCandidates)
            throw new AwardIngestionException("This document is still processing. Saved-page re-analysis is available after processing finishes.",409);
        if (UseLocalIntelligence())
        {
            if (queue is null) throw new InvalidOperationException("Local document-intelligence queue is not available.");
            job.Status = AwardDocumentExtractionJobStatus.Queued; job.CurrentStage = "Waiting to re-analyze locally"; job.ErrorMessage = null; job.StartedAt = null; job.CompletedAt = null; job.FailedAt = null; job.ProcessedPages = 0; job.TotalPages = null; job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct); await queue.EnqueueAsync(job.Id, ct);
            return new AwardPdfJobSummary(job.Id, job.Status, job.DocumentId, job.IngestionSessionId, job.TargetAwardId, job.TotalPages, job.ProcessedPages, job.CurrentStage, job.ErrorMessage, job.CreatedAt, job.CompletedAt);
        }
        if (job.Pages.Count == 0) throw new AwardIngestionException("This document has no saved page evidence to re-analyze.");
        job.Status = AwardDocumentExtractionJobStatus.Analyzing; job.CurrentStage = "Re-analyzing saved page evidence"; job.ErrorMessage = null; job.ExtractorVersion = AwardExtractionRuleSet.Version; job.StartedAt = DateTimeOffset.UtcNow; job.ProcessedPages = 0; job.TotalPages = job.Pages.Count; job.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
        try
        {
            var pages = job.Pages.OrderBy(page => page.PageNumber).Select(page => new NormalizedDocumentPage(page.PageNumber, page.Width, page.Height, page.ExtractionMethod, page.NormalizedText, JsonSerializer.Deserialize<List<DocumentToken>>(page.StructuredLayoutJson) ?? [], page.OcrConfidence, page.WarningMessage)).ToList();
            var inputs = await BuildCandidatesAsync(pages, job, ct);
            job.ProcessedPages = pages.Count; job.CurrentStage = "Preparing review"; job.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
            var session = await ingestion.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Document, job.TargetAwardId, job.SelectedVillageId, job.DocumentId, "PDF extraction", $"Re-analyzed using {AwardExtractionRuleSet.Version}; review every candidate before commit.", inputs, ct);
            job.IngestionSessionId = session.Id; job.Status = AwardDocumentExtractionJobStatus.NeedsReview; job.CurrentStage = "Review ready"; job.CompletedAt = DateTimeOffset.UtcNow; job.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            job.Status = AwardDocumentExtractionJobStatus.Failed; job.ErrorMessage = ex.Message; job.FailedAt = DateTimeOffset.UtcNow; job.CurrentStage = "Re-analysis could not be completed"; job.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); throw;
        }
        return new AwardPdfJobSummary(job.Id, job.Status, job.DocumentId, job.IngestionSessionId, job.TargetAwardId, job.TotalPages, job.ProcessedPages, job.CurrentStage, job.ErrorMessage, job.CreatedAt, job.CompletedAt);
    }

    public async Task RunAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.AwardDocumentExtractionJobs.Include(x => x.Document).SingleOrDefaultAsync(x => x.Id == jobId, ct); if (job is null || job.Status is AwardDocumentExtractionJobStatus.Completed or AwardDocumentExtractionJobStatus.NeedsReview) return;
        try
        {
            job.Status = AwardDocumentExtractionJobStatus.Extracting; job.ExtractorVersion = AwardExtractionRuleSet.Version; job.StartedAt ??= DateTimeOffset.UtcNow; job.CurrentStage = "Reading document"; job.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
            if (UseLocalIntelligence())
            {
                await RunLocalIntelligenceAsync(job, ct);
                return;
            }
            await using var source = await storage.OpenReadAsync(job.Document.StoragePath, ct) ?? throw new AwardIngestionException("The stored PDF could not be opened.", 404);
            using var pdf = PdfDocument.Open(source); job.TotalPages = pdf.NumberOfPages; await db.SaveChangesAsync(ct);
            var pages = new List<NormalizedDocumentPage>();
            var savedPages = await db.AwardDocumentPageExtractions.AsNoTracking().Where(x => x.JobId == jobId).ToDictionaryAsync(x => x.PageNumber, ct);
            for (var pageNo = 1; pageNo <= pdf.NumberOfPages; pageNo++)
            {
                if (savedPages.TryGetValue(pageNo, out var savedPage))
                {
                    pages.Add(new(savedPage.PageNumber, savedPage.Width, savedPage.Height, savedPage.ExtractionMethod, savedPage.NormalizedText, JsonSerializer.Deserialize<List<DocumentToken>>(savedPage.StructuredLayoutJson) ?? [], savedPage.OcrConfidence, savedPage.WarningMessage));
                    job.ProcessedPages = pageNo; job.CurrentStage = $"Reading document ({pageNo} of {pdf.NumberOfPages} pages)"; job.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
                    continue;
                }
                var page = pdf.GetPage(pageNo); var text = page.Text?.Trim() ?? "";
                var tokens = BuildEmbeddedWordTokens(page.Letters);
                var normalized = new NormalizedDocumentPage(pageNo, (decimal)page.Width, (decimal)page.Height, AwardDocumentExtractionMethod.EmbeddedText, text, tokens);
                if (NeedsLocalOcr(page, text, tokens.Count)) normalized = await ocr.ExtractAsync(source, pageNo, ct) ?? normalized with { Method = AwardDocumentExtractionMethod.Unavailable, Warning = "This page requires local OCR; no usable OCR result was returned. Embedded text is retained for manual review only." };
                pages.Add(normalized); db.AwardDocumentPageExtractions.Add(new AwardDocumentPageExtraction { JobId = job.Id, PageNumber = pageNo, Width = normalized.Width, Height = normalized.Height, ExtractionMethod = normalized.Method, NormalizedText = normalized.Text, StructuredLayoutJson = JsonSerializer.Serialize(normalized.Tokens), OcrConfidence = normalized.Confidence, Status = normalized.Method == AwardDocumentExtractionMethod.Unavailable ? "NeedsReview" : "Extracted", WarningMessage = normalized.Warning });
                job.ProcessedPages = pageNo; job.CurrentStage = $"Reading document ({pageNo} of {pdf.NumberOfPages} pages)"; job.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
            }
            job.Status = AwardDocumentExtractionJobStatus.Analyzing; job.CurrentStage = "Analyzing Award"; await db.SaveChangesAsync(ct);
            var inputs = await BuildCandidatesAsync(pages, job, ct); job.Status = AwardDocumentExtractionJobStatus.BuildingCandidates; job.CurrentStage = "Preparing review"; await db.SaveChangesAsync(ct);
            var session = await ingestion.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Document, job.TargetAwardId, job.SelectedVillageId, job.DocumentId, "PDF extraction", $"Automatically extracted using {AwardExtractionRuleSet.Version}; review every candidate before commit.", inputs, ct);
            job.IngestionSessionId = session.Id; job.Status = AwardDocumentExtractionJobStatus.NeedsReview; job.CurrentStage = "Review ready"; job.CompletedAt = DateTimeOffset.UtcNow; job.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PDF extraction job {JobId} failed", jobId);
            // Discard the invalid pending page before recording the job failure.
            db.ChangeTracker.Clear();
            var failed = await db.AwardDocumentExtractionJobs.SingleAsync(x => x.Id == jobId, CancellationToken.None); failed.Status = AwardDocumentExtractionJobStatus.Failed; failed.ErrorMessage = ex.Message; failed.FailedAt = DateTimeOffset.UtcNow; failed.CurrentStage = "Processing could not be completed"; failed.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private bool UseLocalIntelligence() => intelligenceOptions?.Value.Enabled == true && intelligence is not null;

    private async Task RunLocalIntelligenceAsync(AwardDocumentExtractionJob job, CancellationToken ct)
    {
        if (localPaths is null) throw new InvalidOperationException("Local document storage is not available for local document intelligence.");
        var safeName = Path.GetFileName(job.Document.StoragePath);
        if (!string.Equals(safeName, job.Document.StoragePath, StringComparison.Ordinal)) throw new InvalidOperationException("Stored document path is invalid.");
        var filePath = Path.Combine(localPaths.DocumentRoot, safeName);
        if (!File.Exists(filePath)) throw new AwardIngestionException("The stored PDF could not be opened.", 404);

        job.Status = AwardDocumentExtractionJobStatus.Analyzing;
        job.CurrentStage = "Analyzing Award locally";
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var result = await intelligence!.RunAsync(new(1, job.DocumentId, filePath, job.TargetAwardId ?? throw new InvalidOperationException("Target Award is required."), job.SelectedVillageId), ct);
        var inputs = LocalIntelligenceCandidateMapper.Map(result);

        // Mapping validates the complete worker response before any staging
        // write.  The existing ingestion service remains the authority for
        // candidate validation and no canonical table is touched here.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
            job.TotalPages = result.PagesProcessed;
            job.ProcessedPages = result.PagesProcessed;
            job.Status = AwardDocumentExtractionJobStatus.BuildingCandidates;
            job.CurrentStage = "Preparing review";
            job.UpdatedAt = DateTimeOffset.UtcNow;
            var session = await ingestion.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Document, job.TargetAwardId, job.SelectedVillageId, job.DocumentId, "Local document intelligence", "Local OCR/layout suggestions require human verification before commit.", inputs, ct);
            job.IngestionSessionId = session.Id;
            job.Status = AwardDocumentExtractionJobStatus.NeedsReview;
            job.CurrentStage = "Review ready";
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        });
        logger.LogInformation("Local document intelligence staged job {JobId}; pages {Pages}; worker candidates {WorkerCandidates}; staged candidates {StagedCandidates}", job.Id, result.PagesProcessed, result.Candidates.Count, inputs.Count);
    }
    private async Task<List<IngestionCandidateInput>> BuildCandidatesAsync(IReadOnlyList<NormalizedDocumentPage> pages, AwardDocumentExtractionJob job, CancellationToken ct)
    {
        var masters = job.SelectedVillageId is null ? [] : await db.Khasras.AsNoTracking().Where(x => x.VillageId == job.SelectedVillageId).Select(x => new ExtractionCanonicalKhasra(x.NormalizedNumber, x.Qualifier, x.DisplayNumber)).ToListAsync(ct);
        var villages = await db.Villages.AsNoTracking().OrderBy(x => x.Name).Select(x => new ExtractionCanonicalVillage(x.Name)).ToListAsync(ct);
        var context = new AwardExtractionContext(job.TargetAwardId, job.SelectedVillageId, job.SelectedVillage?.Name, masters, villages);
        var candidates = rules.Extract(pages, context).Select(x => x.Input).ToList();
        logger.LogInformation("Award extraction rule analysis completed. Job {JobId}; rule {RuleVersion}; pages {PageCount}; candidate types {CandidateTypes}", job.Id, AwardExtractionRuleSet.Version, pages.Count, string.Join(",", candidates.GroupBy(x => x.CandidateType).OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Count()}")));
        return candidates.Count == 0 ? [new IngestionCandidateInput(AwardIngestionCandidateType.UnmappedAwardFinding, JsonSerializer.Serialize(new UnmappedAwardFindingCandidate("No extractable content", "No safe structured candidates were found.", null)), "{}", null)] : candidates;
    }

    public static bool NeedsLocalOcr(UglyToad.PdfPig.Content.Page page, string text, int wordCount)
    {
        if (text.Length < 20 || wordCount < 5) return true;
        // Scanners often include an old, inaccurate hidden text layer. Its length is not a quality signal.
        var pageArea = page.Width * page.Height;
        return pageArea > 0 && page.GetImages().Sum(image => Math.Abs(image.Bounds.Width * image.Bounds.Height)) >= pageArea * .6;
    }

    public static List<DocumentToken> BuildEmbeddedWordTokens(IEnumerable<UglyToad.PdfPig.Content.Letter> letters)
    {
        var words = new List<DocumentToken>();
        var lines = new List<List<UglyToad.PdfPig.Content.Letter>>();
        foreach (var letter in letters.Where(letter => !string.IsNullOrEmpty(letter.Value)).OrderByDescending(letter => letter.GlyphRectangle.Bottom).ThenBy(letter => letter.GlyphRectangle.Left))
        {
            var line = lines.FirstOrDefault(existing => Math.Abs((decimal)existing.Average(item => item.GlyphRectangle.Bottom) - (decimal)letter.GlyphRectangle.Bottom) <= Math.Max(2m, (decimal)letter.GlyphRectangle.Height * .6m));
            if (line is null) lines.Add([letter]); else line.Add(letter);
        }
        foreach (var line in lines)
        {
            var ordered = line.OrderBy(letter => letter.GlyphRectangle.Left).ToList();
            var text = new StringBuilder(); decimal start = 0, right = 0, bottom = 0, height = 0;
            void Flush()
            {
                if (text.Length == 0) return;
                words.Add(new DocumentToken(text.ToString(), start, bottom, right - start, height)); text.Clear();
            }
            foreach (var letter in ordered)
            {
                var left = (decimal)letter.GlyphRectangle.Left; var glyphRight = (decimal)letter.GlyphRectangle.Right; var value = letter.Value;
                var gap = text.Length == 0 ? 0 : left - right;
                if (text.Length > 0 && (char.IsWhiteSpace(value[0]) || gap > Math.Max(2m, height * .45m))) Flush();
                if (char.IsWhiteSpace(value[0])) continue;
                if (text.Length == 0) { start = left; bottom = (decimal)letter.GlyphRectangle.Bottom; height = (decimal)letter.GlyphRectangle.Height; }
                text.Append(value); right = glyphRight;
            }
            Flush();
        }
        return words;
    }
}

public static class LocalIntelligenceCandidateMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<IngestionCandidateInput> Map(LocalDocumentIntelligenceResult result)
    {
        if (result.ContractVersion != 1 || !string.Equals(result.Status, "Completed", StringComparison.OrdinalIgnoreCase) || result.PagesProcessed < 1)
            throw new InvalidOperationException("Local document intelligence returned an unsupported or incomplete result.");

        var mapped = new List<IngestionCandidateInput>(result.Candidates.Count);
        foreach (var candidate in result.Candidates)
        {
            if (candidate.Page < 1) throw new InvalidOperationException("Local document intelligence returned an invalid source page.");
            var locator = JsonSerializer.Serialize(new
            {
                candidate.Page,
                candidate.SourceRegion,
                candidate.StructuredPayload,
                candidate.RawOcr,
                candidate.NormalizedSuggestion,
                candidate.NormalizationReason,
                OcrSource = "RapidOCR + Table Transformer",
                Warnings = (candidate.InterpretationWarnings ?? []).Append("Local document-intelligence suggestion requires human verification.").ToArray()
            }, Json);

            switch (candidate.CandidateType)
            {
                case "AwardKhasra":
                    mapped.Add(MapAwardKhasra(candidate, locator));
                    break;
                case "LandClassification":
                    mapped.Add(MapLandClassification(candidate, locator));
                    break;
                case "UnmappedAwardFinding":
                    mapped.Add(new(AwardIngestionCandidateType.UnmappedAwardFinding,
                        JsonSerializer.Serialize(new UnmappedAwardFindingCandidate("Local OCR narrative", "Page-level OCR evidence retained for review.", null), Json),
                        locator, candidate.RawSourceText, candidate.Confidence));
                    break;
                default:
                    throw new InvalidOperationException($"Local document intelligence candidate type '{candidate.CandidateType}' is not supported for staging.");
            }
        }
        return mapped;
    }

    private static IngestionCandidateInput MapAwardKhasra(LocalDocumentIntelligenceCandidate candidate, string locator)
    {
        var payload = candidate.StructuredPayload;
        var number = Value(payload, "khasraNumber") ?? "";
        var qualifier = Value(payload, "qualifier");
        var recorded = Area(payload, "recordedArea");
        var awarded = Area(payload, "awardedArea");
        // Deliberately no canonical-area/master substitution: the worker OCR
        // identifier and its source locator are staged exactly as received.
        var typed = new AwardKhasraCandidate(number, qualifier, null, null, null,
            recorded.Bigha, recorded.Biswa, recorded.Biswansi,
            awarded.Bigha, awarded.Biswa, awarded.Biswansi);
        return new(AwardIngestionCandidateType.AwardKhasra, JsonSerializer.Serialize(typed, Json), locator, candidate.RawSourceText, candidate.Confidence);
    }

    private static IngestionCandidateInput MapLandClassification(LocalDocumentIntelligenceCandidate candidate, string locator)
    {
        var block = Value(candidate.StructuredPayload, "block") ?? "Unclassified";
        var typed = new LandClassCandidate(block, "Geometry-backed land-classification suggestion; verify the linked Khasra and area from source evidence.");
        return new(AwardIngestionCandidateType.AwardLandClass, JsonSerializer.Serialize(typed, Json), locator, candidate.RawSourceText, candidate.Confidence);
    }

    private static ParsedArea Area(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out var node)) return new(null, null, null);
        var normalized = Value(node, "normalizedSuggestion");
        return normalized is not null && new StrictAreaParser().TryParse(normalized, out var parsed) ? parsed : new(null, null, null);
    }

    private static string? Value(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out var node)) return null;
        return node.ValueKind == JsonValueKind.String ? node.GetString() :
            node.ValueKind == JsonValueKind.Object && node.TryGetProperty("normalizedSuggestion", out var normalized) && normalized.ValueKind == JsonValueKind.String ? normalized.GetString() : null;
    }
}

public sealed record UnmappedAwardFindingCandidate(string Category, string Summary, string? ExtractedText) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.UnmappedAwardFinding; }
