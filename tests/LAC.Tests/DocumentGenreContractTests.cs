using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LAC.Tests;

public sealed class DocumentGenreContractTests
{
    private static readonly Guid DocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly string Sha = new('a', 64);

    private static LocalDocumentIntelligenceInput Input(bool route = true, int version = 2) =>
        new(version, DocumentId, "unused.pdf", Guid.NewGuid(), null,
            PhysicalSha256: Sha, DocumentVersion: 3, PageCount: 5, GenreRouting: route);

    private static object Observation(string genre = "AWARD", string type = "DocumentGenre", object? evidence = null,
        int page = 1, decimal confidence = .85m) => new
        {
            observationType = type,
            genre,
            confidence,
            requiresHumanReview = true,
            page,
            evidence = evidence ?? new[] { new { page = 1, rawText = "AWARD NO. 30/2002-03",
                sourceRegion = new { x = 10, y = 12, width = 240, height = 18 } } },
            warnings = new[] { "Genre requires human review." },
            classifierVersion = "document-genre-rules/1.0"
        };

    private static string Response(object? observation = null, object[]? candidates = null) => JsonSerializer.Serialize(new
    {
        contractVersion = 2,
        documentId = DocumentId,
        status = "Completed",
        pagesProcessed = 5,
        candidates = candidates ?? [],
        warnings = Array.Empty<string>(),
        metrics = new { genreClassified = 1 },
        physicalSha256 = Sha,
        documentVersion = 3,
        pageCount = 5,
        processedAt = DateTimeOffset.Parse("2026-09-28T12:00:00Z"),
        extractorVersion = "local-award-worker/1.0",
        errors = Array.Empty<string>(),
        observations = new[] { observation ?? Observation() }
    });

    [Fact]
    public void V2_genre_evidence_is_reviewable_and_source_located()
    {
        var result = LocalDocumentIntelligenceContract.ParseAndValidate(Response(), Input(), Sha, 5);
        var mapped = Assert.Single(LocalIntelligenceCandidateMapper.Map(result, true));
        Assert.Equal(AwardIngestionCandidateType.DocumentGenre, mapped.CandidateType);
        using var payload = JsonDocument.Parse(mapped.PayloadJson);
        Assert.Equal("AWARD", payload.RootElement.GetProperty("genre").GetString());
        Assert.True(payload.RootElement.GetProperty("requiresHumanReview").GetBoolean());
        using var locator = JsonDocument.Parse(mapped.SourceLocatorJson!);
        Assert.Equal(1, locator.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(Sha, locator.RootElement.GetProperty("processing").GetProperty("physicalSha256").GetString());
        Assert.Equal("AWARD NO. 30/2002-03", locator.RootElement.GetProperty("genreEvidence")[0].GetProperty("rawText").GetString());
        Assert.Single(new[] { new AwardIngestionCandidate { CandidateType = mapped.CandidateType } }.AsQueryable().Reviewable());
    }

    [Theory]
    [InlineData("POSSESSION_PROCEEDINGS")]
    [InlineData("RENTAL_OR_REQUISITION_OFFER")]
    [InlineData("CLAIMANT_REGISTER_OR_CONTINUATION")]
    [InlineData("UNKNOWN")]
    public void Non_award_and_unknown_route_only_genre_review(string genre)
    {
        var evidence = genre == "UNKNOWN" ? Array.Empty<object>() : null;
        var result = LocalDocumentIntelligenceContract.ParseAndValidate(
            Response(Observation(genre, evidence: evidence)), Input(), Sha, 5);
        var mapped = Assert.Single(LocalIntelligenceCandidateMapper.Map(result, true));
        Assert.Equal(AwardIngestionCandidateType.DocumentGenre, mapped.CandidateType);
    }

    [Fact]
    public void Non_award_result_cannot_smuggle_award_candidates()
    {
        var award = new { candidateType = "AwardCore", structuredPayload = new { awardNumber = "30/2002-03" },
            page = 1, sourceRegion = (object?)null, rawSourceText = "Award", rawOcr = (string?)null,
            normalizedSuggestion = (string?)null, normalizationReason = (string?)null,
            confidence = (decimal?)null, interpretationWarnings = Array.Empty<string>() };
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(
            Response(Observation("POSSESSION_PROCEEDINGS"), [award]), Input(), Sha, 5));
    }

    [Fact]
    public void Malformed_or_unsupported_genre_observation_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(
            Response(Observation(type: "UnknownTableSemantic")), Input(), Sha, 5));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(
            Response(Observation(page: 6)), Input(), Sha, 5));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(
            Response(Observation(confidence: 1.2m)), Input(), Sha, 5));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(
            Response(Observation(evidence: new[] { new { page = 0, rawText = "AWARD NO. 30/2002-03" } })), Input(), Sha, 5));
    }

    [Fact]
    public void Genre_requires_explicit_v2_flag_and_v1_request_is_unchanged()
    {
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.SerializeRequest(Input(version: 1)));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(Response(), Input(route: false), Sha, 5));
        using var request = JsonDocument.Parse(LocalDocumentIntelligenceContract.SerializeRequest(Input(), Sha, 5));
        Assert.True(request.RootElement.GetProperty("options").GetProperty("genreRouting").GetBoolean());
    }

    [Fact]
    public async Task Unknown_genre_is_staged_in_existing_attention_queue_without_a_canonical_fact()
    {
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var award = new Award { AwardNumber = "TEST-30" };
        var village = new Village { Name = "Fictional village" };
        var document = new LAC.Domain.Document { Id = DocumentId, OriginalFileName = "award-named-file.pdf",
            StoragePath = "source.pdf" };
        db.AddRange(award, village, document, new AwardVillage { Award = award, Village = village });
        await db.SaveChangesAsync();
        var result = LocalDocumentIntelligenceContract.ParseAndValidate(
            Response(Observation("UNKNOWN", evidence: Array.Empty<object>())), Input(), Sha, 5);
        var service = new AwardIngestionService(db, new AwardWorkflowService(db));
        var session = await service.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Document,
            award.Id, village.Id, document.Id, "test", null,
            LocalIntelligenceCandidateMapper.Map(result, true), default);
        var attention = await service.GetCandidatesAsync(session.Id, null, null, 0, 25, default, "attention");
        var item = Assert.Single(attention.Items);
        Assert.Equal(AwardIngestionCandidateType.DocumentGenre, item.CandidateType);
        Assert.Equal(1, item.SourcePage);
        Assert.Equal(AwardIngestionCandidateStatus.NeedsReview, item.Status);
        Assert.Equal(0, await db.Set<AwardKhasra>().CountAsync());
        Assert.Equal(0, await db.PossessionEvents.CountAsync());
    }
}
