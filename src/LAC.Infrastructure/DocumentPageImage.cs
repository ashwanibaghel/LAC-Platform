using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace LAC.Infrastructure;

/// <summary>Renders a single locally stored PDF page for an in-app review canvas.</summary>
public sealed class DocumentPageImageService(LocalStoragePaths paths, IOptions<DocumentIntelligenceOptions> options)
{
    public async Task<byte[]> RenderAsync(string storagePath, int page, bool rotateClockwise, CancellationToken ct)
    {
        if (page < 1) throw new AwardIngestionException("Source page must be positive.", 400);
        var name = Path.GetFileName(storagePath);
        if (!string.Equals(name, storagePath, StringComparison.Ordinal)) throw new AwardIngestionException("Stored document path is invalid.", 400);
        var source = Path.Combine(paths.DocumentRoot, name);
        if (!File.Exists(source)) throw new AwardIngestionException("The locally stored PDF is unavailable.", 404);
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.PythonExecutable) || string.IsNullOrWhiteSpace(config.WorkerScript) || !File.Exists(config.PythonExecutable) || !File.Exists(config.WorkerScript)) throw new AwardIngestionException("Local page rendering is not configured.", 503);
        var work = Path.Combine(Path.GetTempPath(), "lac-page-image", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var output = Path.Combine(work, "page.png");
        try
        {
            var start = new ProcessStartInfo(config.PythonExecutable) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = string.IsNullOrWhiteSpace(config.WorkingDirectory) ? Path.GetDirectoryName(Path.GetFullPath(config.WorkerScript))! : config.WorkingDirectory };
            start.ArgumentList.Add(config.WorkerScript); start.ArgumentList.Add("--render-page"); start.ArgumentList.Add("--pdf"); start.ArgumentList.Add(source); start.ArgumentList.Add("--page"); start.ArgumentList.Add(page.ToString());
            if (rotateClockwise) start.ArgumentList.Add("--rotate-clockwise");
            start.ArgumentList.Add("--output"); start.ArgumentList.Add(output);
            using var process = Process.Start(start) ?? throw new AwardIngestionException("Local page renderer could not start.", 503);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var diagnostic = (await error).Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (process.ExitCode != 0 || !File.Exists(output)) throw new AwardIngestionException($"Source page could not be rendered (exit {process.ExitCode}: {(diagnostic.Length > 120 ? diagnostic[..120] : diagnostic)}).", 503);
            return await File.ReadAllBytesAsync(output, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AwardIngestionException("Source page rendering timed out.", 503); }
        finally { try { Directory.Delete(work, true); } catch { } }
    }
}
