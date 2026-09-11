using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LAC.Infrastructure;

public sealed class DocumentIntelligenceOptions
{
    public bool Enabled { get; set; }
    public string? PythonExecutable { get; set; }
    public string? WorkerScript { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? ModelCachePath { get; set; }
    // Full local layout/OCR has measured around fourteen minutes on the pilot
    // document.  Keep explicit operational headroom while still allowing a
    // deployment to tighten this through configuration.
    public int TimeoutMinutes { get; set; } = 30;
    public int MaxConcurrentJobs { get; set; } = 1;
}

public sealed record LocalDocumentIntelligenceInput(int ContractVersion, Guid DocumentId, string FilePath, Guid TargetAwardId, Guid? SelectedVillageId, IReadOnlyList<int>? SelectedPages = null, bool NmPilot = false, bool NmSemantic = false);

public sealed record LocalDocumentIntelligenceCandidate(
    string CandidateType,
    JsonElement StructuredPayload,
    int Page,
    JsonElement? SourceRegion,
    string? RawSourceText,
    string? RawOcr,
    string? NormalizedSuggestion,
    string? NormalizationReason,
    decimal? Confidence,
    IReadOnlyList<string>? InterpretationWarnings);

public sealed record LocalDocumentIntelligenceResult(
    int ContractVersion,
    Guid DocumentId,
    string Status,
    int PagesProcessed,
    IReadOnlyList<LocalDocumentIntelligenceCandidate> Candidates,
    IReadOnlyList<string> Warnings,
    JsonElement Metrics);

public interface ILocalDocumentIntelligenceClient
{
    Task<LocalDocumentIntelligenceResult> RunAsync(LocalDocumentIntelligenceInput input, CancellationToken ct);
}

public sealed class LocalDocumentIntelligenceClient(IOptions<DocumentIntelligenceOptions> configured) : ILocalDocumentIntelligenceClient
{
    public async Task<LocalDocumentIntelligenceResult> RunAsync(LocalDocumentIntelligenceInput input, CancellationToken ct)
    {
        var options = configured.Value;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.PythonExecutable) || string.IsNullOrWhiteSpace(options.WorkerScript))
            throw new InvalidOperationException("Local document intelligence is disabled or not configured.");

        var tempDirectory = Path.Combine(Path.GetTempPath(), "lac-document-intelligence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var inputPath = Path.Combine(tempDirectory, "input.json");
        var outputPath = Path.Combine(tempDirectory, "output.json");

        try
        {
            await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(new
            {
                contractVersion = 1,
                documentId = input.DocumentId,
                filePath = input.FilePath,
                targetAwardId = input.TargetAwardId,
                selectedVillageId = input.SelectedVillageId,
                options = new { processTables = true, nmPilot = input.NmPilot, nmSemantic = input.NmSemantic },
                selectedPages = input.SelectedPages
            }), ct);

            var processStart = new ProcessStartInfo(options.PythonExecutable)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
                    ? Path.GetDirectoryName(Path.GetFullPath(options.WorkerScript))!
                    : options.WorkingDirectory
            };
            if (!string.IsNullOrWhiteSpace(options.ModelCachePath))
                processStart.Environment["HF_HOME"] = options.ModelCachePath;

            processStart.ArgumentList.Add(options.WorkerScript);
            processStart.ArgumentList.Add("--input");
            processStart.ArgumentList.Add(inputPath);
            processStart.ArgumentList.Add("--output");
            processStart.ArgumentList.Add(outputPath);

            using var process = Process.Start(processStart)
                ?? throw new InvalidOperationException("Local worker could not start.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(Math.Clamp(options.TimeoutMinutes, 1, 60)));
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                throw new TimeoutException("Local document intelligence worker timed out.");
            }

            var stderr = await stderrTask;
            await stdoutTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Local worker failed: {SanitizeDiagnostic(stderr)}");
            if (!File.Exists(outputPath))
                throw new InvalidOperationException("Local worker returned no result.");

            var result = JsonSerializer.Deserialize<LocalDocumentIntelligenceResult>(
                await File.ReadAllTextAsync(outputPath, timeout.Token),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Local worker result is malformed.");
            if (result.ContractVersion != 1 || result.DocumentId != input.DocumentId)
                throw new InvalidOperationException("Local worker contract validation failed.");

            return result;
        }
        finally
        {
            try { Directory.Delete(tempDirectory, recursive: true); } catch { }
        }
    }

    private static string SanitizeDiagnostic(string value)
    {
        var singleLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= 400 ? singleLine : singleLine[^400..];
    }
}
