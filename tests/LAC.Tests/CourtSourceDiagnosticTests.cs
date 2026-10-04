using System.Text.Json.Nodes;
using LAC.Api;
using Xunit;

namespace LAC.Tests;

public sealed class CourtSourceDiagnosticTests
{
    private static readonly Guid CaseId = Guid.NewGuid();
    private static CourtIntelligenceKnownOrder Source => new(CaseId, "delhihighcourt|wpc|6328|2026",
        new DateOnly(2026, 5, 8), "https://delhihighcourt.nic.in/app/showlogo/order.pdf/2026", null, null, Guid.NewGuid(),
        RawCaseNumber: "W.P.(C) 6328/2026", RawOrderDate: "08/05/2026");
    private static CourtIntelligenceCaseIndex Index(string number = "WPC NO. 6328/2026") => new(CaseId, number, new[] { Source });

    [Theory]
    [InlineData("WPC NO. 6328/2026")]
    [InlineData("W.P.(C) - 6328 / 2026")]
    [InlineData("W.P.(C) 6328/2026")]
    public void Display_number_label_does_not_reject_exact_registered_identity(string number)
    {
        Assert.True(CourtIntelligenceCaseData.Eligible(Index(number), Source));
        Assert.Equal("Eligible", CourtIntelligenceCaseData.EligibilityReason(Index(number), Source));
        Assert.False(CourtIntelligenceCaseData.Eligible(Index(number), Source with { NormalizedCaseIdentity = "delhihighcourt|wpc|6329|2026" }));
        Assert.False(CourtIntelligenceCaseData.Eligible(Index(number), Source with { CourtCaseId = Guid.NewGuid() }));
    }

    [Fact]
    public void Eligibility_exposes_precise_date_identity_and_route_failures()
    {
        Assert.Equal("UnparseableOfficialDate", CourtIntelligenceCaseData.EligibilityReason(Index(), Source with { OrderDate = null }));
        Assert.Equal("MissingOrderDate", CourtIntelligenceCaseData.EligibilityReason(Index(), Source with { OrderDate = null, RawOrderDate = null }));
        Assert.Equal("UnsafeOfficialUrl", CourtIntelligenceCaseData.EligibilityReason(Index(), Source with { OfficialUrl = "https://example.com/order.pdf" }));
        Assert.Equal("UnsupportedOfficialPdfRoute", CourtIntelligenceCaseData.EligibilityReason(Index(), Source with { OfficialUrl = "https://delhihighcourt.nic.in/app/unknown/order.pdf" }));
        Assert.Equal("CaseIdentityMismatch", CourtIntelligenceCaseData.EligibilityReason(Index(), Source with { NormalizedCaseIdentity = "supremecourt|wpc|6328|2026" }));
    }

    [Theory]
    [InlineData("NeedsSourceReview: connected-case PDF needs case-specific attribution; no cross-case facts inferred", "ConnectedCasePdf")]
    [InlineData("Known official source bytes changed; source-version review required", "SourceBytesChanged")]
    public void Legacy_worker_sources_have_typed_diagnostics_and_zero_usable_facts(string failure, string reason)
    {
        var node = new JsonObject { ["status"] = "NeedsSourceReview", ["failureMessage"] = failure, ["facts"] = new JsonArray() };
        var result = CourtSourceDiagnostics.Evaluate(Index(), Source, node);
        Assert.Equal(reason, result.ReasonCode);
        Assert.Equal("BlockedBeforeAI", result.AiState);
        Assert.Equal(0, result.UsableFactCount);
        Assert.Contains("withheld", result.OfficerMessage);
    }

    [Fact]
    public void Official_download_timeout_is_explicitly_before_ai_in_source_diagnostics()
    {
        var node = JsonNode.Parse("""{"status":"NeedsSourceReview","sourceReasonCode":"OfficialPdfDownloadTimeout","failureMessage":"NeedsSourceReview: official PDF download timed out"}""");
        var result = CourtSourceDiagnostics.Evaluate(Index(), Source, node);
        Assert.Equal("OfficialPdfDownloadTimeout", result.ReasonCode);
        Assert.Equal("BlockedBeforeAI", result.AiState);
        Assert.Equal("Blocked", result.SourceState);
        Assert.Equal(0, result.UsableFactCount);
        Assert.Contains("before AI processing", result.OfficerMessage);
    }

    [Fact]
    public void Completed_review_brief_is_distinct_from_partial_failed_extraction()
    {
        var node = JsonNode.Parse("""{"status":"NeedsReview","summaryFacts":[{}],"coverage":{"allSelectedChunksProcessed":true}}""");
        var completed = CourtSourceDiagnostics.Evaluate(Index(), Source, node);
        Assert.Equal("Verified", completed.SourceState);
        Assert.Equal("ProcessedWithReview", completed.AiState);
        Assert.Equal(1, completed.UsableFactCount);
        node!["failureMessage"] = "Failed chunk";
        node["summaryFacts"] = new JsonArray();
        var failed = CourtSourceDiagnostics.Evaluate(Index(), Source, node);
        Assert.Equal("AiExtractionIncomplete", failed.ReasonCode);
        Assert.Equal("Incomplete", failed.AiState);
        Assert.Equal(0, failed.UsableFactCount);
    }
}
