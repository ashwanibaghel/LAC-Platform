using System.Diagnostics;
using System.Net;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace LAC.Tests;

public sealed class StaffBrowserFactAttribute : FactAttribute
{
    public StaffBrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LAC_STAFF_BROWSER_TEST") != "1" || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LAC_STAFF_TEST_POSTGRES")))
            Skip = "Requires the isolated PostgreSQL fixture, installed browser and LAC_STAFF_BROWSER_TEST=1.";
    }
}
public sealed class LandRecordsStaffBrowserTests
{
    [StaffBrowserFact]
    public async Task Real_browser_shows_safe_ownership_resolution_without_JSON_or_type_confidence_and_renders_canonical_timeline()
    {
        await using var factory = new StaffPostgresFactory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var client = factory.CreateClient();
        Assert.Equal("127.0.0.1", client.BaseAddress!.Host);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fixture = Path.Combine(factory.StorageRoot, "synthetic-browser-source.pdf");
        Directory.CreateDirectory(factory.StorageRoot);
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Fluent.Document.Create(c => c.Page(p => p.Content().Text("Synthetic source includes two Award references for office workflow browser verification. " + Guid.NewGuid()))).GeneratePdf(fixture);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("LAC_STAFF_NODE") ?? "node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.Combine(root, "src/LAC.Web") };
        start.ArgumentList.Add(Path.Combine(root, "src/LAC.Web/tests/land-records-staff-browser.mjs"));
        start.Environment["LAC_STAFF_BROWSER_API"] = client.BaseAddress.ToString(); start.Environment["LAC_STAFF_BROWSER_PDF"] = fixture;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var report = await output + "\n" + await error;
        Directory.CreateDirectory(Path.Combine(root, "out/browser-checks")); await File.WriteAllTextAsync(Path.Combine(root, "out/browser-checks/result.txt"), report);
        Assert.True(process.ExitCode == 0, report);
    }
}
