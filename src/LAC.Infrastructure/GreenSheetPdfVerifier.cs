namespace LAC.Infrastructure;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

public interface IGreenSheetPdfVerifier { Task<int> ValidateAsync(byte[] pdf, string expectedText, CancellationToken ct); }

public sealed class GreenSheetPdfVerifier(IOptions<OnlyOfficeNotingOptions> options, LocalStoragePaths paths) : IGreenSheetPdfVerifier
{
    private static readonly SemaphoreSlim Gate = new(1);
    public async Task<int> ValidateAsync(byte[] pdf, string expectedText, CancellationToken ct)
    {
        var o = options.Value;
        if (!File.Exists(o.PdfVerifierPython) || !File.Exists(o.PdfVerifierScript) ||
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(o.PdfVerifierScript, ct))).ToLowerInvariant() != o.PdfVerifierSha256.ToLowerInvariant())
            throw new MatterWorkflowException("Pinned physical PDF verifier is unavailable; printable output is blocked.", 503);
        await Gate.WaitAsync(ct);
        var id = Guid.NewGuid().ToString("N");
        var input = Path.Combine(paths.ExtractionRoot, "green-verify-" + id + ".pdf");
        var output = Path.Combine(paths.ExtractionRoot, "green-verify-" + id + ".json");
        var text = Path.Combine(paths.ExtractionRoot, "green-verify-" + id + ".txt");
        try
        {
            await File.WriteAllBytesAsync(input, pdf, ct);
            await File.WriteAllTextAsync(text, expectedText, ct);
            var start = new ProcessStartInfo { FileName = o.PdfVerifierPython, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add("-B"); start.ArgumentList.Add(o.PdfVerifierScript); start.ArgumentList.Add(input);
            start.ArgumentList.Add("--output"); start.ArgumentList.Add(output);
            start.ArgumentList.Add("--expected-text"); start.ArgumentList.Add(text);
            foreach (var name in start.Environment.Keys.Where(k => k.Contains("Secret", StringComparison.OrdinalIgnoreCase) || k.Contains("Password", StringComparison.OrdinalIgnoreCase) || k.StartsWith("ConnectionStrings", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(name);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(2));
            using var process = Process.Start(start) ?? throw new MatterWorkflowException("Physical verifier could not start.", 503);
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(true); throw; }
            await Task.WhenAll(stdout, stderr); // Consume both pipes; never expose paths/text or inherited secrets to clients/logs.
            if (process.ExitCode != 0 || !File.Exists(output)) throw new MatterWorkflowException("Physical PDF geometry or clean-print verification failed; no output was published.", 422);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(output, ct));
            if (!report.RootElement.GetProperty("passed").GetBoolean()) throw new MatterWorkflowException("Physical PDF verification failed.", 422);
            return report.RootElement.GetProperty("pages").GetArrayLength();
        }
        finally
        {
            // Only this invocation's exact owned staging files, never source/snapshot bytes.
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
            if (File.Exists(text)) File.Delete(text);
            Gate.Release();
        }
    }
}
