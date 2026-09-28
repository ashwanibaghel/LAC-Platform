using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Xunit;

namespace LAC.Tests;

public sealed class DocumentSectionContractTests
{
    private static readonly Guid Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly string Sha = new('b', 64);
    private static object Genre(string genre = "AWARD") => new
    {
        observationType = "DocumentGenre", genre, confidence = .9m, requiresHumanReview = true,
        page = 1, evidence = new[] { new { page = 1, rawText = "AWARD NO. 30/2002-03",
            sourceRegion = new { x = 1, y = 2, width = 100, height = 10 } } },
        warnings = new[] { "Review genre." }, classifierVersion = "document-genre-rules/1.0"
    };
    private static object Section(string semantic = "LAND_SCHEDULE", string heading = "Land Awarded",
        int start = 1, int end = 2, decimal confidence = .85m, string type = "DocumentSection") => new
    {
        observationType = type, semantic, presentation = "SCHEDULE", pageStart = start, pageEnd = end,
        rawHeading = heading, confidence, requiresHumanReview = true,
        evidence = new[] { new { page = start, rawText = heading,
            sourceRegion = new { x = 4, y = 5, width = 120, height = 12 } } },
        warnings = new[] { "Review source section." }, classifierVersion = "document-section-rules/1.0"
    };
    private static object Unknown(int page) => new
    {
        observationType = "DocumentSection", semantic = "UNKNOWN", presentation = "UNKNOWN",
        pageStart = page, pageEnd = page, rawHeading = (string?)null, confidence = 0m,
        requiresHumanReview = true, evidence = Array.Empty<object>(),
        warnings = new[] { "No clear heading." }, classifierVersion = "document-section-rules/1.0"
    };
    private static LocalDocumentIntelligenceInput Input(bool sections = true) => new(2, Id, "unused.pdf", Guid.NewGuid(), null,
        PhysicalSha256: Sha, DocumentVersion: 2, PageCount: 3, GenreRouting: true, SectionObservations: sections);
    private static string Response(params object[] observations) => JsonSerializer.Serialize(new
    {
        contractVersion = 2, documentId = Id, status = "Completed", pagesProcessed = 3,
        candidates = Array.Empty<object>(), warnings = Array.Empty<string>(), metrics = new { sectionObservations = 2 },
        physicalSha256 = Sha, documentVersion = 2, pageCount = 3,
        processedAt = DateTimeOffset.Parse("2026-09-28T12:00:00Z"), extractorVersion = "local-award-worker/1.0",
        errors = Array.Empty<string>(), observations
    });

    [Fact]
    public void Source_section_and_unknown_page_stage_review_only_with_identity()
    {
        var result = LocalDocumentIntelligenceContract.ParseAndValidate(Response(Genre(), Section(), Unknown(3)), Input(), Sha, 3);
        var mapped = LocalIntelligenceCandidateMapper.Map(result, true, true);
        Assert.Equal(3, mapped.Count);
        Assert.Equal(2, mapped.Count(x => x.CandidateType == AwardIngestionCandidateType.DocumentSection));
        using var payload = JsonDocument.Parse(mapped[1].PayloadJson);
        Assert.Equal("LAND_SCHEDULE", payload.RootElement.GetProperty("semantic").GetString());
        Assert.True(payload.RootElement.GetProperty("requiresHumanReview").GetBoolean());
        using var locator = JsonDocument.Parse(mapped[1].SourceLocatorJson!);
        Assert.Equal(2, locator.RootElement.GetProperty("pageEnd").GetInt32());
        Assert.Equal(Sha, locator.RootElement.GetProperty("processing").GetProperty("physicalSha256").GetString());
        Assert.Equal(2, new[] { new AwardIngestionCandidate { CandidateType = mapped[1].CandidateType },
            new AwardIngestionCandidate { CandidateType = mapped[2].CandidateType } }.AsQueryable().Reviewable().Count());
    }

    [Fact]
    public void Malformed_section_and_missing_page_fail_before_staging()
    {
        foreach (var bad in new[] { Section(type: "Wrong"), Section(start: 0), Section(end: 4),
                     Section(confidence: 1.2m) })
            Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(
                Response(Genre(), bad, Unknown(3)), Input(), Sha, 3));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(
            Response(Genre(), Section()), Input(), Sha, 3));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(
            Response(Genre(), Section(), Unknown(3)).Replace("\"rawHeading\":\"Land Awarded\"", "\"rawHeading\":\"Unproven heading\""),
            Input(), Sha, 3));
    }

    [Fact]
    public void Non_award_cannot_smuggle_sections_and_slice_b_flag_rejects_them()
    {
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(
            Response(Genre("OTHER"), Section(), Unknown(3)), Input(), Sha, 3));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(
            Response(Genre(), Section(), Unknown(3)), Input(false), Sha, 3));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.SerializeRequest(
            Input() with { GenreRouting = false }));
    }
}
