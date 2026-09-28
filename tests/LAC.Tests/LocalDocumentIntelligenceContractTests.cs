using System.Text.Json;
using LAC.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace LAC.Tests;

public sealed class LocalDocumentIntelligenceContractTests
{
    private static readonly Guid DocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly string Sha = new('a', 64);

    private static LocalDocumentIntelligenceInput Input(int version = 2) =>
        new(version, DocumentId, "unused.pdf", Guid.NewGuid(), null,
            PhysicalSha256: Sha, DocumentVersion: 3, PageCount: 5);

    private static string Response(int version = 2, string status = "Completed", string? sha = null,
        int documentVersion = 3, int pageCount = 5, int pagesProcessed = 5) =>
        JsonSerializer.Serialize(new
        {
            contractVersion = version,
            documentId = DocumentId,
            status,
            pagesProcessed,
            candidates = Array.Empty<object>(),
            warnings = Array.Empty<string>(),
            metrics = new { },
            physicalSha256 = sha ?? Sha,
            documentVersion,
            pageCount,
            processedAt = DateTimeOffset.Parse("2026-09-28T12:00:00Z"),
            extractorVersion = "local-award-worker/1.0",
            errors = Array.Empty<string>(),
            observations = Array.Empty<object>()
        });

    [Fact]
    public void Legacy_v1_result_remains_supported_without_v2_metadata()
    {
        const string json = """{"contractVersion":1,"documentId":"11111111-1111-1111-1111-111111111111","status":"Completed","pagesProcessed":1,"candidates":[],"warnings":[],"metrics":{}}""";
        var result = LocalDocumentIntelligenceContract.ParseAndValidate(json, Input(1));
        Assert.Equal(1, result.ContractVersion);
        Assert.Null(result.PhysicalSha256);
        Assert.Empty(LocalIntelligenceCandidateMapper.Map(result));
    }

    [Fact]
    public void V1_request_keeps_original_shape_and_v2_request_carries_physical_identity()
    {
        using var v1 = JsonDocument.Parse(LocalDocumentIntelligenceContract.SerializeRequest(Input(1)));
        Assert.Equal(1, v1.RootElement.GetProperty("contractVersion").GetInt32());
        Assert.False(v1.RootElement.TryGetProperty("physicalSha256", out _));
        Assert.False(v1.RootElement.TryGetProperty("documentVersion", out _));
        Assert.False(v1.RootElement.TryGetProperty("pageCount", out _));

        using var v2 = JsonDocument.Parse(LocalDocumentIntelligenceContract.SerializeRequest(Input(), Sha, 5));
        Assert.Equal(DocumentId.ToString(), v2.RootElement.GetProperty("documentId").GetString());
        Assert.Equal(Sha, v2.RootElement.GetProperty("physicalSha256").GetString());
        Assert.Equal(3, v2.RootElement.GetProperty("documentVersion").GetInt32());
        Assert.Equal(5, v2.RootElement.GetProperty("pageCount").GetInt32());
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.SerializeRequest(Input(), null, 5));
    }

    [Fact]
    public void V2_identity_page_count_and_processing_metadata_round_trip()
    {
        var result = LocalDocumentIntelligenceContract.ParseAndValidate(Response(), Input(), Sha, 5);
        Assert.Equal(DocumentId, result.DocumentId);
        Assert.Equal(Sha, result.PhysicalSha256);
        Assert.Equal(3, result.DocumentVersion);
        Assert.Equal(5, result.PageCount);
        Assert.Equal(5, result.PagesProcessed);
        Assert.Equal("local-award-worker/1.0", result.ExtractorVersion);
        Assert.Equal(DateTimeOffset.Parse("2026-09-28T12:00:00Z"), result.ProcessedAt);
        Assert.Empty(LocalIntelligenceCandidateMapper.Map(result));
    }

    [Fact]
    public void Unsupported_contract_versions_fail_closed()
    {
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(Response(3), Input(3)));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(Response(1), Input(), Sha, 5));
    }

    [Fact]
    public void Malformed_json_fails_with_explicit_error()
    {
        var error = Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate("{broken", Input(), Sha, 5));
        Assert.Contains("malformed JSON", error.Message);
    }

    [Fact]
    public void Unavailable_worker_is_reported_before_processing()
    {
        var client = new LocalDocumentIntelligenceClient(Options.Create(new DocumentIntelligenceOptions
        {
            Enabled = true,
            PythonExecutable = "missing-python-for-contract-test.exe",
            WorkerScript = "missing-worker-for-contract-test.py"
        }));
        var preflight = client.GetPreflight();
        Assert.False(preflight.Ready);
        Assert.Equal("Misconfigured", preflight.Status);
        Assert.False(preflight.PythonExists);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Partial")]
    public void Worker_error_or_partial_result_does_not_stage_candidates(string status)
    {
        var error = Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(Response(status: status), Input(), Sha, 5));
        Assert.Contains(status, error.Message);
    }

    [Fact]
    public void V2_sha_version_and_page_count_mismatches_fail_closed()
    {
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(Response(sha: new string('b', 64)), Input(), Sha, 5));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(Response(documentVersion: 4), Input(), Sha, 5));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(Response(pageCount: 4), Input(), Sha, 5));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(Response(pagesProcessed: 6), Input(), Sha, 5));
    }

    [Fact]
    public void V2_candidate_locator_carries_processing_identity_without_changing_fact_payload()
    {
        var candidate = new LocalDocumentIntelligenceCandidate(
            "UnmappedAwardFinding", JsonSerializer.SerializeToElement(new { category = "Source finding" }),
            1, null, "raw source", null, null, null, null, []);
        var result = new LocalDocumentIntelligenceResult(2, DocumentId, "Completed", 1, [candidate], [],
            JsonSerializer.SerializeToElement(new { }), Sha, 3, 5,
            DateTimeOffset.Parse("2026-09-28T12:00:00Z"), "local-award-worker/1.0", [],
            JsonSerializer.SerializeToElement(Array.Empty<object>()));
        var mapped = Assert.Single(LocalIntelligenceCandidateMapper.Map(result));
        using var locator = JsonDocument.Parse(mapped.SourceLocatorJson!);
        var processing = locator.RootElement.GetProperty("processing");
        Assert.Equal(Sha, processing.GetProperty("physicalSha256").GetString());
        Assert.Equal(3, processing.GetProperty("documentVersion").GetInt32());
        Assert.Equal(5, processing.GetProperty("pageCount").GetInt32());
        Assert.Equal(2, processing.GetProperty("contractVersion").GetInt32());
        Assert.Contains("Local OCR narrative", mapped.PayloadJson);
    }
}
