using System.Text.Json.Nodes;

namespace LAC.Api;

public sealed record CourtProgressSummary(int OfficialSources, int UsableBriefs, int BlockedSources,
    int PendingSources, bool LatestBriefReady, string? LatestOrderDate, string? ProcessingCurrentOrderDate,
    int ProcessingChecked, int ProcessingTotal, bool BackgroundProcessing, bool CoverageComplete)
{
    public string ActionStatus => UsableBriefs > 0 ? "VerifiedEvidenceAvailable" : "UnavailableUntilVerifiedIntelligenceReady";
    public string ActionStatusMessage => UsableBriefs > 0 ? "See verified current-action evidence." : "Action status unavailable until verified intelligence is ready.";
    public static CourtProgressSummary Build(IReadOnlyList<CourtSourceDiagnostic> sources,
        IEnumerable<JsonNode?> orders, JsonNode? refresh)
    {
        var orderList = orders.ToList();
        var latest = sources.Where(s => s.OrderDate is not null).Select(s => s.OrderDate).Max();
        var enriching = sources.Count(s => orderList.Any(o => o?["officialUrl"]?.GetValue<string>() == s.OfficialUrl
            && o?["orderDate"]?.GetValue<string>() == s.OrderDate && o?["deepProcessingComplete"]?.GetValue<bool>() == false));
        var pending = sources.Count(s => s.AiState is "Waiting" or "Processing") + enriching;
        var running = refresh?["status"]?.GetValue<string>() == "Running";
        var current = running ? refresh?["processingCurrentOrderDate"]?.GetValue<string>() : null;
        if (current is not null && (!DateOnly.TryParseExact(current, "yyyy-MM-dd", out _) || !sources.Any(s => s.OrderDate == current)))
            throw new InvalidDataException("Processing date does not match the registered source index.");
        return new(sources.Count, sources.Count(s => s.UsableFactCount > 0),
            sources.Count(s => s.AiState == "BlockedBeforeAI"), pending,
            latest is not null && sources.Any(s => s.OrderDate == latest && s.UsableFactCount > 0), latest, current,
            running ? Math.Min(refresh?["checked"]?.GetValue<int>() ?? 0, sources.Count) : Math.Max(0, sources.Count - pending),
            sources.Count, running, sources.Count > 0 && pending == 0 && !running && sources.All(s => !s.ReviewRequired && s.AiState == "Processed"));
    }
}
