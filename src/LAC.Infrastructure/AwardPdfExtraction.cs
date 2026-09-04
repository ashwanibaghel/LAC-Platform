using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UglyToad.PdfPig;

namespace LAC.Infrastructure;

public sealed record DocumentToken(string Text, decimal X, decimal Y, decimal Width, decimal Height, decimal? Confidence = null);
public sealed record NormalizedDocumentPage(int PageNumber, decimal Width, decimal Height, AwardDocumentExtractionMethod Method, string Text, IReadOnlyList<DocumentToken> Tokens, decimal? Confidence = null, string? Warning = null);
public interface IOcrEngine { Task<NormalizedDocumentPage?> ExtractAsync(Stream pdf, int pageNumber, CancellationToken ct); }
public sealed class UnavailableOcrEngine : IOcrEngine { public Task<NormalizedDocumentPage?> ExtractAsync(Stream pdf, int pageNumber, CancellationToken ct) => Task.FromResult<NormalizedDocumentPage?>(null); }
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
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    public ValueTask EnqueueAsync(Guid jobId, CancellationToken ct) => _channel.Writer.WriteAsync(jobId, ct);
    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
public sealed record AwardPdfUploadResult(Guid JobId, Guid DocumentId);
public sealed record AwardPdfJobSummary(Guid Id, AwardDocumentExtractionJobStatus Status, Guid DocumentId, Guid? IngestionSessionId, Guid? TargetAwardId, int? TotalPages, int ProcessedPages, string? CurrentStage, string? ErrorMessage, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed class AwardPdfExtractionService(LacDbContext db, IDocumentStorage storage, IAwardPdfJobQueue queue)
{
    public async Task<AwardPdfUploadResult> QueueUploadAsync(Stream content, string fileName, string? contentType, Guid? targetAwardId, Guid? selectedVillageId, string? uploadedBy, CancellationToken ct)
    {
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new AwardIngestionException("Choose a PDF file.");
        await using var validationCopy = new MemoryStream(); await content.CopyToAsync(validationCopy, ct);
        var bytes = validationCopy.ToArray();
        if (bytes.Length < 5 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F') throw new AwardIngestionException("The uploaded file is not a valid PDF.");
        if (targetAwardId is not null && !await db.Awards.AnyAsync(x => x.Id == targetAwardId, ct)) throw new AwardIngestionException("Target Award was not found.", 404);
        if (selectedVillageId is not null && !await db.Villages.AnyAsync(x => x.Id == selectedVillageId, ct)) throw new AwardIngestionException("Selected Village was not found.", 404);
        if (targetAwardId is not null && selectedVillageId is not null && !await db.AwardVillages.AnyAsync(x => x.AwardId == targetAwardId && x.VillageId == selectedVillageId, ct)) throw new AwardIngestionException("The selected Village must be directly linked to the target Award.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Sha256Hash == hash && x.Status == "Active", ct);
        if (document is null)
        {
            await using var saveStream = new MemoryStream(bytes, writable: false);
            document = new Document { DocumentType = "Award PDF", OriginalFileName = Path.GetFileName(fileName), StoragePath = await storage.SaveAsync(saveStream, fileName, ct), Sha256Hash = hash, MimeType = string.IsNullOrWhiteSpace(contentType) ? "application/pdf" : contentType, FileSize = bytes.Length, UploadedBy = Clean(uploadedBy) };
            db.Documents.Add(document);
        }
        var job = new AwardDocumentExtractionJob { Document = document, TargetAwardId = targetAwardId, SelectedVillageId = selectedVillageId, CurrentStage = "Reading document" };
        db.AwardDocumentExtractionJobs.Add(job); await db.SaveChangesAsync(ct); await queue.EnqueueAsync(job.Id, ct); return new(job.Id, document.Id);
    }
    public async Task<AwardPdfJobSummary> GetAsync(Guid jobId, CancellationToken ct) => await db.AwardDocumentExtractionJobs.AsNoTracking().Where(x => x.Id == jobId).Select(x => new AwardPdfJobSummary(x.Id, x.Status, x.DocumentId, x.IngestionSessionId, x.TargetAwardId, x.TotalPages, x.ProcessedPages, x.CurrentStage, x.ErrorMessage, x.CreatedAt, x.CompletedAt)).SingleOrDefaultAsync(ct) ?? throw new AwardIngestionException("PDF extraction job was not found.", 404);
    public async Task<IReadOnlyList<AwardPdfJobSummary>> GetForAwardAsync(Guid awardId, CancellationToken ct) => await db.AwardDocumentExtractionJobs.AsNoTracking().Where(x => x.TargetAwardId == awardId).OrderByDescending(x => x.CreatedAt).Take(20).Select(x => new AwardPdfJobSummary(x.Id, x.Status, x.DocumentId, x.IngestionSessionId, x.TargetAwardId, x.TotalPages, x.ProcessedPages, x.CurrentStage, x.ErrorMessage, x.CreatedAt, x.CompletedAt)).ToListAsync(ct);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class AwardPdfExtractionWorker(IServiceScopeFactory scopes, IAwardPdfJobQueue queue) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var startupScope = scopes.CreateAsyncScope(); var startupDb = startupScope.ServiceProvider.GetRequiredService<LacDbContext>();
        foreach (var id in await startupDb.AwardDocumentExtractionJobs.Where(x => x.Status == AwardDocumentExtractionJobStatus.Queued).Select(x => x.Id).ToListAsync(stoppingToken)) await queue.EnqueueAsync(id, stoppingToken);
        await foreach (var id in queue.DequeueAllAsync(stoppingToken)) { try { await using var scope = scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<AwardPdfJobRunner>().RunAsync(id, stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { } }
    }
}

public sealed class AwardPdfJobRunner(LacDbContext db, IDocumentStorage storage, AwardIngestionService ingestion, IOcrEngine ocr, IAwardSectionClassifier classifier)
{
    private static readonly Regex AwardRegex = new(@"award\s*(?:no\.?|number)?\s*[:#-]?\s*([A-Za-z0-9/.-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KhasraRegex = new(@"(?<!\d)(\d+//\d+(?:/\d+)?)(?:\s+(min))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NotificationRegex = new(@"(?:section|u/s)\s*(4|6|17)\D{0,30}?([A-Za-z0-9/.-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public async Task RunAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.AwardDocumentExtractionJobs.Include(x => x.Document).SingleOrDefaultAsync(x => x.Id == jobId, ct); if (job is null || job.Status is AwardDocumentExtractionJobStatus.Completed or AwardDocumentExtractionJobStatus.NeedsReview) return;
        try
        {
            job.Status = AwardDocumentExtractionJobStatus.Extracting; job.StartedAt ??= DateTimeOffset.UtcNow; job.CurrentStage = "Reading document"; job.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
            await using var source = await storage.OpenReadAsync(job.Document.StoragePath, ct) ?? throw new AwardIngestionException("The stored PDF could not be opened.", 404);
            using var memory = new MemoryStream(); await source.CopyToAsync(memory, ct); var bytes = memory.ToArray();
            using var pdf = PdfDocument.Open(new MemoryStream(bytes, writable: false)); job.TotalPages = pdf.NumberOfPages; await db.SaveChangesAsync(ct);
            var pages = new List<NormalizedDocumentPage>();
            for (var pageNo = 1; pageNo <= pdf.NumberOfPages; pageNo++)
            {
                var page = pdf.GetPage(pageNo); var text = page.Text?.Trim() ?? "";
                var tokens = page.Letters.Select(l => new DocumentToken(l.Value, (decimal)l.GlyphRectangle.Left, (decimal)l.GlyphRectangle.Bottom, (decimal)l.GlyphRectangle.Width, (decimal)l.GlyphRectangle.Height)).ToList();
                var normalized = new NormalizedDocumentPage(pageNo, (decimal)page.Width, (decimal)page.Height, AwardDocumentExtractionMethod.EmbeddedText, text, tokens);
                if (text.Length < 20 || tokens.Count < 5) { await using var copy = new MemoryStream(bytes, writable: false); normalized = await ocr.ExtractAsync(copy, pageNo, ct) ?? normalized with { Method = AwardDocumentExtractionMethod.Unavailable, Warning = "No usable embedded text; local OCR is not configured." }; }
                pages.Add(normalized); db.AwardDocumentPageExtractions.Add(new AwardDocumentPageExtraction { JobId = job.Id, PageNumber = pageNo, Width = normalized.Width, Height = normalized.Height, ExtractionMethod = normalized.Method, NormalizedText = normalized.Text, StructuredLayoutJson = JsonSerializer.Serialize(normalized.Tokens), OcrConfidence = normalized.Confidence, Status = normalized.Method == AwardDocumentExtractionMethod.Unavailable ? "NeedsReview" : "Extracted", WarningMessage = normalized.Warning });
                job.ProcessedPages = pageNo; job.CurrentStage = $"Reading document ({pageNo} of {pdf.NumberOfPages} pages)"; job.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
            }
            job.Status = AwardDocumentExtractionJobStatus.Analyzing; job.CurrentStage = "Analyzing Award"; await db.SaveChangesAsync(ct);
            var inputs = BuildCandidates(pages); job.Status = AwardDocumentExtractionJobStatus.BuildingCandidates; job.CurrentStage = "Preparing review"; await db.SaveChangesAsync(ct);
            var session = await ingestion.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Document, job.TargetAwardId, job.SelectedVillageId, job.DocumentId, "PDF extraction", "Automatically extracted from a PDF; review every candidate before commit.", inputs, ct);
            job.IngestionSessionId = session.Id; job.Status = AwardDocumentExtractionJobStatus.NeedsReview; job.CurrentStage = "Review ready"; job.CompletedAt = DateTimeOffset.UtcNow; job.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            var failed = await db.AwardDocumentExtractionJobs.SingleAsync(x => x.Id == jobId, CancellationToken.None); failed.Status = AwardDocumentExtractionJobStatus.Failed; failed.ErrorMessage = ex.Message; failed.FailedAt = DateTimeOffset.UtcNow; failed.CurrentStage = "Processing could not be completed"; failed.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(CancellationToken.None);
        }
    }
    private List<IngestionCandidateInput> BuildCandidates(IReadOnlyList<NormalizedDocumentPage> pages)
    {
        var result = new List<IngestionCandidateInput>(); var seenKhasras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            var locator = JsonSerializer.Serialize(new { page = page.PageNumber, sections = classifier.Classify(page.Text) });
            var snippet = page.Text.Length > 1200 ? page.Text[..1200] : page.Text;
            var award = AwardRegex.Match(page.Text); if (award.Success) result.Add(Input(new AwardCoreCandidate(award.Groups[1].Value, FindDate(page.Text), null, null), locator, snippet));
            foreach (Match match in KhasraRegex.Matches(page.Text)) { var number = match.Groups[1].Value; var qualifier = match.Groups[2].Success ? match.Groups[2].Value : null; if (seenKhasras.Add($"{number}|{qualifier}")) result.Add(Input(new AwardKhasraCandidate(number, qualifier, null, null, null, null, null, null, null, null, null), locator, match.Value)); }
            foreach (Match match in NotificationRegex.Matches(page.Text)) result.Add(Input(new NotificationCandidate($"Section {match.Groups[1].Value}", match.Groups[2].Value, FindDate(match.Value)), locator, match.Value));
            if (page.Method == AwardDocumentExtractionMethod.Unavailable || (result.Count == 0 && page.Text.Length > 0)) result.Add(Input(new UnmappedAwardFindingCandidate(page.Method == AwardDocumentExtractionMethod.Unavailable ? "Unreadable page" : "Unmapped text", "Review this extracted content; no safe canonical mapping was made.", page.Text), locator, snippet));
        }
        return result.Count == 0 ? [Input(new UnmappedAwardFindingCandidate("No extractable content", "No safe structured candidates were found.", null), "[]", null)] : result;
    }
    private static IngestionCandidateInput Input(IAwardIngestionCandidatePayload payload, string locator, string? raw) => new(payload.CandidateType, JsonSerializer.Serialize(payload, payload.GetType()), locator, raw, null);
    private static DateOnly? FindDate(string text) { var match = Regex.Match(text, @"\b(\d{1,2})[/-](\d{1,2})[/-](\d{2,4})\b"); return match.Success && DateOnly.TryParse($"{match.Groups[1].Value}/{match.Groups[2].Value}/{match.Groups[3].Value}", out var date) ? date : null; }
}

public sealed record UnmappedAwardFindingCandidate(string Category, string Summary, string? ExtractedText) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.UnmappedAwardFinding; }
