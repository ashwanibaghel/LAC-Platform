using LAC.Infrastructure;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace LAC.Tests;

public sealed class LocalDocumentIntelligenceSmokeTests
{
    private readonly ITestOutputHelper output;

    public LocalDocumentIntelligenceSmokeTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Fictional_camel_case_worker_contract_deserializes()
    {
        const string payload = "{\"contractVersion\":1,\"documentId\":\"11111111-1111-1111-1111-111111111111\",\"status\":\"Completed\",\"pagesProcessed\":1,\"candidates\":[],\"warnings\":[],\"metrics\":{}}";

        var result = JsonSerializer.Deserialize<LocalDocumentIntelligenceResult>(payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(result);
        Assert.Equal(1, result.ContractVersion);
        Assert.Equal("Completed", result.Status);
    }

    [Fact]
    public async Task Real_local_worker_smoke_test_when_explicitly_configured()
    {
        var pdf = Environment.GetEnvironmentVariable("LAC_REAL_AWARD_PDF");
        var python = Environment.GetEnvironmentVariable("LAC_DOCUMENT_INTELLIGENCE_PYTHON");
        var worker = Environment.GetEnvironmentVariable("LAC_DOCUMENT_INTELLIGENCE_WORKER");
        if (string.IsNullOrWhiteSpace(pdf) || string.IsNullOrWhiteSpace(python) || string.IsNullOrWhiteSpace(worker)) return;
        var client = new LocalDocumentIntelligenceClient(Options.Create(new DocumentIntelligenceOptions { Enabled = true, PythonExecutable = python, WorkerScript = worker, TimeoutMinutes = 15 }));
        var documentId = Guid.NewGuid(); var result = await client.RunAsync(new(1, documentId, pdf, Guid.NewGuid(), null), CancellationToken.None);
        Assert.Equal(1, result.ContractVersion); Assert.Equal(documentId, result.DocumentId); Assert.Equal("Completed", result.Status); Assert.True(result.PagesProcessed > 0); Assert.NotEmpty(result.Candidates);
        Assert.NotEmpty(result.Warnings);
        Assert.Equal(JsonValueKind.Object, result.Metrics.ValueKind);
        Assert.All(result.Candidates, candidate => Assert.True(candidate.Page > 0));
        Assert.Contains(result.Candidates, candidate => candidate.SourceRegion is not null);
        Assert.Contains(result.Candidates, candidate =>
            !string.IsNullOrWhiteSpace(candidate.CandidateType)
            && candidate.SourceRegion is not null
            && !string.IsNullOrWhiteSpace(candidate.RawSourceText)
            && !string.IsNullOrWhiteSpace(candidate.RawOcr)
            && candidate.Confidence is not null
            && !string.IsNullOrWhiteSpace(candidate.NormalizedSuggestion));

        var typeCounts = result.Candidates
            .GroupBy(candidate => candidate.CandidateType)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key}={group.Count()}");
        var sourceRegionCount = result.Candidates.Count(candidate => candidate.SourceRegion is not null);
        var runtime = result.Metrics.TryGetProperty("runtimeSeconds", out var runtimeSeconds)
            ? runtimeSeconds.GetRawText()
            : "unknown";
        output.WriteLine($"Sanitized worker metrics: pages={result.PagesProcessed}; candidates={result.Candidates.Count}; types={string.Join(',', typeCounts)}; sourceRegions={sourceRegionCount}; runtimeSeconds={runtime}");
    }
}
