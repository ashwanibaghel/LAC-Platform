using System.Text.Json.Nodes;
using LAC.Api;
using Xunit;

namespace LAC.Tests;

public sealed class CourtProgressSummaryTests
{
    private static CourtSourceDiagnostic Source(string date, string ai = "Processed", int facts = 1, bool review = false) =>
        new(date, date, $"https://delhihighcourt.nic.in/app/showlogo/{date}.pdf/2026", Guid.NewGuid(),
            ai == "BlockedBeforeAI" ? "Blocked" : "Verified", "Eligible", "Source checked", ai, facts, review, "Verified source", ai, new { });

    [Fact]
    public void Verified_fast_latest_is_ready_while_history_and_enrichment_remain_pending()
    {
        var latest = Source("2026-09-23", "ProcessedWithReview", 4, true);
        var sources = new[] { Source("2026-05-06", "Waiting", 0), latest };
        var orders = new[] { JsonNode.Parse($$"""{"officialUrl":"{{latest.OfficialUrl}}","orderDate":"2026-09-23","deepProcessingComplete":false}""") };
        var state = JsonNode.Parse("""{"status":"Running","processingCurrentOrderDate":"2026-09-23","checked":1,"total":2}""");
        var result = CourtProgressSummary.Build(sources, orders, state);
        Assert.True(result.LatestBriefReady);
        Assert.True(result.BackgroundProcessing);
        Assert.False(result.CoverageComplete);
        Assert.Equal(2, result.PendingSources);
        Assert.Equal(1, result.UsableBriefs);
        Assert.Equal(1, result.ProcessingChecked);
        Assert.Equal(2, result.ProcessingTotal);
        Assert.Equal("2026-09-23", result.ProcessingCurrentOrderDate);
    }

    [Fact]
    public void Older_usable_brief_does_not_claim_new_latest_is_ready()
    {
        var result = CourtProgressSummary.Build(new[] { Source("2026-05-06"), Source("2026-09-23", "BlockedBeforeAI", 0, true) }, [], null);
        Assert.False(result.LatestBriefReady);
        Assert.Equal("2026-09-23", result.LatestOrderDate);
        Assert.Equal(1, result.BlockedSources);
        Assert.False(result.CoverageComplete);
    }

    [Fact]
    public void Cached_complete_sources_and_empty_index_require_no_runtime_state()
    {
        var complete = CourtProgressSummary.Build(new[] { Source("2026-09-23") }, [], null);
        Assert.True(complete.LatestBriefReady);
        Assert.True(complete.CoverageComplete);
        Assert.False(complete.BackgroundProcessing);
        Assert.Null(complete.ProcessingCurrentOrderDate);
        var empty = CourtProgressSummary.Build([], [], null);
        Assert.Null(empty.LatestOrderDate);
        Assert.False(empty.LatestBriefReady);
        Assert.False(empty.CoverageComplete);
    }

    [Fact]
    public void Runtime_processing_date_must_belong_to_current_registered_index()
    {
        Assert.Throws<InvalidDataException>(() => CourtProgressSummary.Build(new[] { Source("2026-09-23") }, [],
            JsonNode.Parse("""{"status":"Running","processingCurrentOrderDate":"2026-09-99"}""")));
    }
}
