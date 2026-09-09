using System.Diagnostics;
using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LAC.Infrastructure;

/// <summary>Produces one ephemeral, locally-rendered evidence crop.  Callers
/// never provide a storage path, document id, page or bounds independently.</summary>
public sealed class DocumentSourceCropService(LacDbContext db, LocalStoragePaths paths, IOptions<DocumentIntelligenceOptions> options)
{
    public async Task<byte[]> CreateAsync(Guid candidateId, string? fieldRole, CancellationToken ct)
    {
        var candidate = await db.AwardIngestionCandidates.AsNoTracking()
            .Include(x => x.Session).ThenInclude(x => x.SourceDocument)
            .SingleOrDefaultAsync(x => x.Id == candidateId, ct)
            ?? throw new AwardIngestionException("Review item not found.", 404);
        var document = candidate.Session.SourceDocument ?? throw new AwardIngestionException("This review has no source document.");
        var (page, region) = ReadEvidence(candidate.SourceLocatorJson, fieldRole);
        var totalPages = await db.AwardDocumentExtractionJobs.Where(x => x.DocumentId == document.Id)
            .Select(x => x.TotalPages).OrderByDescending(x => x).FirstOrDefaultAsync(ct);
        if (page < 1 || totalPages is null || page > totalPages) throw new AwardIngestionException("The requested source page is outside this document.");
        var name = Path.GetFileName(document.StoragePath);
        if (!string.Equals(name, document.StoragePath, StringComparison.Ordinal)) throw new AwardIngestionException("Stored document path is invalid.");
        var source = Path.Combine(paths.DocumentRoot, name);
        if (!File.Exists(source)) throw new AwardIngestionException("The locally stored PDF is unavailable.", 404);
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.PythonExecutable) || string.IsNullOrWhiteSpace(config.WorkerScript) || !File.Exists(config.PythonExecutable) || !File.Exists(config.WorkerScript)) throw new AwardIngestionException("Local evidence crop runtime is not configured.", 503);
        var work = Path.Combine(Path.GetTempPath(), "lac-source-crop", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var output = Path.Combine(work, "crop.png");
        try
        {
            var start = new ProcessStartInfo(config.PythonExecutable) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = string.IsNullOrWhiteSpace(config.WorkingDirectory) ? Path.GetDirectoryName(Path.GetFullPath(config.WorkerScript))! : config.WorkingDirectory };
            start.ArgumentList.Add(config.WorkerScript); start.ArgumentList.Add("--crop"); start.ArgumentList.Add("--pdf"); start.ArgumentList.Add(source); start.ArgumentList.Add("--page"); start.ArgumentList.Add(page.ToString()); start.ArgumentList.Add("--region"); start.ArgumentList.Add(JsonSerializer.Serialize(region)); start.ArgumentList.Add("--output"); start.ArgumentList.Add(output);
            using var process = Process.Start(start) ?? throw new AwardIngestionException("Local evidence crop runtime could not start.", 503);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var diagnostic = (await error).Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (process.ExitCode != 0 || !File.Exists(output)) throw new AwardIngestionException($"Local evidence crop could not be rendered (exit {process.ExitCode}: {(diagnostic.Length > 120 ? diagnostic[..120] : diagnostic)}).", 503);
            return await File.ReadAllBytesAsync(output, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AwardIngestionException("Local evidence crop timed out.", 503); }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    private static (int Page, CropRegion Region) ReadEvidence(string? locatorJson, string? fieldRole)
    {
        try
        {
            using var locator = JsonDocument.Parse(locatorJson ?? "{}"); var root = locator.RootElement;
            var page = root.TryGetProperty("Page", out var p) ? p.GetInt32() : root.TryGetProperty("page", out p) ? p.GetInt32() : 0;
            var region = root.TryGetProperty("SourceRegion", out var r) ? r : root.TryGetProperty("sourceRegion", out r) ? r : default;
            if (!string.IsNullOrWhiteSpace(fieldRole) && root.TryGetProperty("StructuredPayload", out var payload) && payload.TryGetProperty("sourceCells", out var cells) && cells.TryGetProperty(fieldRole, out var cell) && cell.TryGetProperty("sourceRegion", out var cellRegion)) region = cellRegion;
            var parsed = JsonSerializer.Deserialize<CropRegion>(region.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new JsonException();
            if (parsed.X < 0 || parsed.Y < 0 || parsed.Width <= 0 || parsed.Height <= 0 || parsed.Width > 20000 || parsed.Height > 20000 || parsed.X + parsed.Width > 20000 || parsed.Y + parsed.Height > 20000) throw new AwardIngestionException("Source region is outside supported local bounds.");
            return (page, parsed);
        }
        catch (JsonException) { throw new AwardIngestionException("Source region evidence is invalid."); }
    }
    private sealed record CropRegion(float X, float Y, float Width, float Height);
}
