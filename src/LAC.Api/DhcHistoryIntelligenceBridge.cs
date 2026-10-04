using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

// Only human-started, case-scoped completed assisted runs enter this queue.
// No official requests, CAPTCHA tokens, canonical writes or model launch here.
public sealed class DhcHistoryIntelligenceBridge(IServiceScopeFactory scopes,
    LocalStoragePaths paths, IHttpClientFactory clients, ILogger<DhcHistoryIntelligenceBridge> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PumpAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("Full DHC history processing queue temporarily unavailable ({ErrorType}).", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public async Task PumpAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var items = await db.DhcAssistedSyncItems.AsNoTracking()
            .Where(x => x.Reason == DelhiHighCourtAssistedService.FullHistoryReason &&
                x.Run.Status == DhcAssistedRunStatus.Completed && x.Run.Phase == DhcAssistedPhase.OrderLookup)
            .OrderBy(x => x.Run.CompletedAt)
            .Select(x => new { x.CourtCaseId, x.RunId, x.Run.StartedByUserId }).ToListAsync(ct);
        foreach (var item in items)
        {
            var folder = Path.Combine(paths.ExtractionRoot, "court-intelligence", "v1", item.CourtCaseId.ToString());
            var receipt = Path.Combine(folder, $"sync-{item.RunId}.json");
            if (File.Exists(receipt))
            {
                // A restart of Python marks running jobs Interrupted. Resume
                // from per-order artifacts, without rechecking official forms.
                var statePath = Path.Combine(folder, "refresh.json");
                if (!File.Exists(statePath)) continue;
                var info = new FileInfo(statePath);
                if (info.Length > 8192) continue;
                await using var stream = new FileStream(statePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var state = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (state.RootElement.GetProperty("caseId").GetGuid() != item.CourtCaseId ||
                    state.RootElement.GetProperty("status").GetString() != "Interrupted") continue;
            }
            var auth = scope.ServiceProvider.GetRequiredService<ICourtAuthorizationService>();
            if (!await auth.CanViewCourtCaseAsync(item.CourtCaseId, item.StartedByUserId, ct)) continue;
            var index = await CourtIntelligenceCaseData.LoadAsync(db, item.CourtCaseId, ct);
            if (index == null) continue;
            var usable = index.Orders.Any(o => CourtIntelligenceCaseData.Eligible(index, o));
            if (usable)
            {
                var result = await CourtIntelligenceQuestions.RefreshAsync(index, clients, ct);
                if (result is not IStatusCodeHttpResult { StatusCode: 202 }) return;
            }
            Directory.CreateDirectory(folder);
            var temporary = receipt + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
                { caseId = item.CourtCaseId, runId = item.RunId, state = usable ? "ProcessingQueued" : "NoOfficialPdfs" }), ct);
                File.Move(temporary, receipt, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            if (usable) return; // one local case at a time, next pump after completion
        }
    }
}
