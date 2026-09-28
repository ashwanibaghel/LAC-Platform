using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

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
    // Version 1 remains the default until the version 2 intake envelope is enabled.
    public int ContractVersion { get; set; } = 1;
    public bool GenreRoutingEnabled { get; set; }
    public bool SectionObservationsEnabled { get; set; }
    public bool TableSemanticsEnabled { get; set; }
}

public sealed record LocalDocumentIntelligenceInput(int ContractVersion, Guid DocumentId, string FilePath, Guid TargetAwardId, Guid? SelectedVillageId, IReadOnlyList<int>? SelectedPages = null, bool NmPilot = false, bool NmSemantic = false, string? PhysicalSha256 = null, int? DocumentVersion = null, int? PageCount = null, bool GenreRouting = false, bool SectionObservations = false, bool TableSemantics = false);

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
    IReadOnlyList<string>? InterpretationWarnings,
    bool? RequiresIndividualReview = null);

public sealed record LocalDocumentIntelligenceResult(
    int ContractVersion,
    Guid DocumentId,
    string Status,
    int PagesProcessed,
    IReadOnlyList<LocalDocumentIntelligenceCandidate> Candidates,
    IReadOnlyList<string> Warnings,
    JsonElement Metrics,
    string? PhysicalSha256 = null,
    int? DocumentVersion = null,
    int? PageCount = null,
    DateTimeOffset? ProcessedAt = null,
    string? ExtractorVersion = null,
    IReadOnlyList<string>? Errors = null,
    JsonElement? Observations = null);

public static class LocalDocumentIntelligenceContract
{
    public static string SerializeRequest(LocalDocumentIntelligenceInput input, string? physicalSha256 = null, int? pageCount = null)
    {
        if (input.GenreRouting && input.ContractVersion != 2)
            throw new InvalidOperationException("Document genre routing requires worker contract version 2.");
        if (input.SectionObservations && (input.ContractVersion != 2 || !input.GenreRouting))
            throw new InvalidOperationException("Section observations require version 2 genre routing.");
        if (input.TableSemantics && !input.SectionObservations)
            throw new InvalidOperationException("Table semantics require version 2 section observations.");
        if (input.ContractVersion == 1)
            return JsonSerializer.Serialize(new
            {
                contractVersion = 1,
                documentId = input.DocumentId,
                filePath = input.FilePath,
                targetAwardId = input.TargetAwardId,
                selectedVillageId = input.SelectedVillageId,
                options = new { processTables = true, nmPilot = input.NmPilot, nmSemantic = input.NmSemantic },
                selectedPages = input.SelectedPages
            });
        if (input.ContractVersion != 2)
            throw new InvalidOperationException("Unsupported local worker contract version.");
        if (input.DocumentId == Guid.Empty || input.DocumentVersion is null or < 1 ||
            string.IsNullOrWhiteSpace(physicalSha256) || pageCount is null or < 1)
            throw new InvalidOperationException("Version 2 document identity metadata is missing.");
        return JsonSerializer.Serialize(new
        {
            contractVersion = 2,
            documentId = input.DocumentId,
            filePath = input.FilePath,
            targetAwardId = input.TargetAwardId,
            selectedVillageId = input.SelectedVillageId,
            options = new { processTables = true, nmPilot = input.NmPilot, nmSemantic = input.NmSemantic, genreRouting = input.GenreRouting, sectionObservations = input.SectionObservations, tableSemantics = input.TableSemantics },
            selectedPages = input.SelectedPages,
            physicalSha256,
            documentVersion = input.DocumentVersion,
            pageCount
        });
    }

    public static LocalDocumentIntelligenceResult ParseAndValidate(string json, LocalDocumentIntelligenceInput input, string? physicalSha256 = null, int? pageCount = null)
    {
        LocalDocumentIntelligenceResult result;
        try
        {
            result = JsonSerializer.Deserialize<LocalDocumentIntelligenceResult>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Local worker result is malformed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Local worker returned malformed JSON.", ex);
        }

        if (input.ContractVersion is not (1 or 2) || result.ContractVersion != input.ContractVersion || result.DocumentId != input.DocumentId)
            throw new InvalidOperationException("Local worker contract version or document identity mismatch.");
        if (!string.Equals(result.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Local worker reported {SafeStatus(result.Status)} processing.");
        if (result.PagesProcessed < 1 || result.Candidates is null || result.Warnings is null || result.Metrics.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Local worker result is incomplete.");

        if (input.ContractVersion == 2)
        {
            if (input.DocumentVersion is null or < 1 || string.IsNullOrWhiteSpace(physicalSha256) || pageCount is null or < 1)
                throw new InvalidOperationException("Version 2 document identity metadata is missing.");
            if (!string.Equals(result.PhysicalSha256, physicalSha256, StringComparison.OrdinalIgnoreCase) ||
                result.DocumentVersion != input.DocumentVersion || result.PageCount != pageCount ||
                result.PagesProcessed > result.PageCount || result.ProcessedAt is null ||
                string.IsNullOrWhiteSpace(result.ExtractorVersion) || result.Errors is null || result.Errors.Count != 0 ||
                result.Observations is null || result.Observations.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Version 2 worker metadata is incomplete or inconsistent.");
            var genre = DocumentGenreContract.Read(result, input.GenreRouting, input.SectionObservations);
            var sections = DocumentSectionContract.ReadAll(result, genre, input.SectionObservations, input.TableSemantics);
            DocumentTableContract.ReadAll(result, genre, sections, input.TableSemantics);
        }
        return result;
    }

    private static string SafeStatus(string? status) => status is "Failed" or "Partial" or "Cancelled" ? status : "incomplete";
}

public sealed record DocumentIntelligencePreflight(
    string Status,
    string? Message,
    bool Enabled,
    bool PythonConfigured,
    bool PythonExists,
    bool WorkerScriptConfigured,
    bool WorkerScriptExists,
    bool WorkingDirectoryExists,
    bool WorkingDirectoryAccessible,
    bool ModelCacheConfigured,
    bool? ModelCacheExists,
    bool? InputDocumentExists)
{
    public bool Ready => Status == "Ready";
}

public interface ILocalDocumentIntelligenceClient
{
    DocumentIntelligencePreflight GetPreflight(string? inputDocumentPath = null) => new("Disabled", "OCR worker is disabled.", false, false, false, false, false, false, false, false, null, inputDocumentPath is null ? null : false);
    Task<LocalDocumentIntelligenceResult> RunAsync(LocalDocumentIntelligenceInput input, CancellationToken ct);
}

public sealed class LocalDocumentIntelligenceClient(IOptions<DocumentIntelligenceOptions> configured) : ILocalDocumentIntelligenceClient
{
    public DocumentIntelligencePreflight GetPreflight(string? inputDocumentPath = null)
    {
        var options = configured.Value;
        var pythonConfigured = !string.IsNullOrWhiteSpace(options.PythonExecutable);
        var pythonExists = pythonConfigured && File.Exists(options.PythonExecutable);
        var workerConfigured = !string.IsNullOrWhiteSpace(options.WorkerScript);
        var workerExists = workerConfigured && File.Exists(options.WorkerScript);
        var workingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
            ? (workerConfigured ? Path.GetDirectoryName(Path.GetFullPath(options.WorkerScript!)) : null)
            : options.WorkingDirectory;
        var workingDirectoryExists = !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory);
        var workingDirectoryAccessible = workingDirectoryExists && CanEnumerateDirectory(workingDirectory!);
        var modelCacheConfigured = !string.IsNullOrWhiteSpace(options.ModelCachePath);
        bool? inputExists = inputDocumentPath is null ? null : File.Exists(inputDocumentPath);
        var (status, message) = !options.Enabled ? ("Disabled", "OCR worker is disabled.")
            : !pythonConfigured ? ("Misconfigured", "OCR worker Python runtime is not configured.")
            : !pythonExists ? ("Misconfigured", "Python runtime not found.")
            : !workerConfigured ? ("Misconfigured", "OCR worker script is not configured.")
            : !workerExists ? ("Misconfigured", "Worker script not found.")
            : !workingDirectoryExists ? ("Misconfigured", "Worker working directory not found.")
            : !workingDirectoryAccessible ? ("Misconfigured", "Worker working directory is not accessible.")
            : inputExists == false ? ("InvalidInput", "Source document could not be found.")
            : ("Ready", (string?)null);
        return new(status, message, options.Enabled, pythonConfigured, pythonExists, workerConfigured, workerExists,
            workingDirectoryExists, workingDirectoryAccessible, modelCacheConfigured,
            modelCacheConfigured ? Directory.Exists(options.ModelCachePath!) : (bool?)null, inputExists);
    }

    public async Task<LocalDocumentIntelligenceResult> RunAsync(LocalDocumentIntelligenceInput input, CancellationToken ct)
    {
        if (input.ContractVersion is not (1 or 2))
            throw new InvalidOperationException("Unsupported local worker contract version.");
        if (input.GenreRouting && input.ContractVersion != 2)
            throw new InvalidOperationException("Document genre routing requires worker contract version 2.");
        var options = configured.Value;
        var preflight = GetPreflight(input.FilePath);
        if (!preflight.Ready)
            throw new InvalidOperationException(preflight.Message ?? "OCR worker is not configured.");

        string? physicalSha256 = null;
        int? pageCount = null;
        if (input.ContractVersion == 2)
        {
            if (input.DocumentId == Guid.Empty || input.DocumentVersion is null or < 1)
                throw new InvalidOperationException("Version 2 document identity metadata is missing.");
            physicalSha256 = await ComputeSha256Async(input.FilePath, ct);
            if (!string.IsNullOrWhiteSpace(input.PhysicalSha256) &&
                !string.Equals(input.PhysicalSha256, physicalSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Stored document SHA-256 does not match the physical PDF.");
            using var pdf = PdfDocument.Open(input.FilePath);
            pageCount = pdf.NumberOfPages;
            if (pageCount < 1 || input.PageCount is not null && input.PageCount != pageCount)
                throw new InvalidOperationException("Document page count is invalid or has changed.");
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "lac-document-intelligence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var inputPath = Path.Combine(tempDirectory, "input.json");
        var outputPath = Path.Combine(tempDirectory, "output.json");

        try
        {
            await File.WriteAllTextAsync(inputPath,
                LocalDocumentIntelligenceContract.SerializeRequest(input, physicalSha256, pageCount), ct);

            var processStart = new ProcessStartInfo(options.PythonExecutable!)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
                    ? Path.GetDirectoryName(Path.GetFullPath(options.WorkerScript!))!
                    : options.WorkingDirectory
            };
            if (!string.IsNullOrWhiteSpace(options.ModelCachePath))
                processStart.Environment["HF_HOME"] = options.ModelCachePath;

            processStart.ArgumentList.Add(options.WorkerScript!);
            processStart.ArgumentList.Add("--input");
            processStart.ArgumentList.Add(inputPath);
            processStart.ArgumentList.Add("--output");
            processStart.ArgumentList.Add(outputPath);

            Process? started;
            try { started = Process.Start(processStart); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            { throw new InvalidOperationException("Worker could not start.", ex); }
            using var process = started ?? throw new InvalidOperationException("Worker could not start.");
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
                throw new TimeoutException("Worker timed out.");
            }

            var stderr = await stderrTask;
            await stdoutTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Local worker failed: {SanitizeDiagnostic(stderr)}");
            if (!File.Exists(outputPath))
                throw new InvalidOperationException("Local worker returned no result.");

            var result = LocalDocumentIntelligenceContract.ParseAndValidate(
                await File.ReadAllTextAsync(outputPath, timeout.Token), input, physicalSha256, pageCount);
            if (input.ContractVersion == 2 && !string.Equals(await ComputeSha256Async(input.FilePath, ct), physicalSha256, StringComparison.Ordinal))
                throw new InvalidOperationException("Physical document changed while the worker was processing it.");

            return result;
        }
        finally
        {
            try { Directory.Delete(tempDirectory, recursive: true); } catch { }
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var source = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(source, ct)).ToLowerInvariant();
    }

    private static string SanitizeDiagnostic(string value)
    {
        var singleLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= 400 ? singleLine : singleLine[^400..];
    }

    private static bool CanEnumerateDirectory(string path)
    {
        try { using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator(); _ = entries.MoveNext(); return true; }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }
}
