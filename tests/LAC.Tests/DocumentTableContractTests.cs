using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LAC.Tests;

public sealed class DocumentTableContractTests
{
    private static readonly Guid Id = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly string Sha = new('c', 64);
    private static readonly object Box = new { x = 10, y = 100, width = 300, height = 120 };
    private static readonly object HeadingBox = new { x = 10, y = 50, width = 200, height = 18 };
    private static readonly object LaterHeadingBox = new { x = 10, y = 240, width = 200, height = 18 };
    private static object Genre(string genre = "AWARD") => new
    {
        observationType = "DocumentGenre", genre, confidence = .9m, requiresHumanReview = true,
        page = 1, evidence = new[] { new { page = 1, rawText = "AWARD NO. 30/2002-03", sourceRegion = HeadingBox } },
        warnings = new[] { "Review genre." }, classifierVersion = "document-genre-rules/1.0"
    };
    private static object Section(object? headingRegion = null) => new
    {
        observationType = "DocumentSection", semantic = "LAND_SCHEDULE", presentation = "SCHEDULE",
        pageStart = 1, pageEnd = 1, rawHeading = "Land Awarded", confidence = .85m,
        requiresHumanReview = true,
        evidence = new[] { new { page = 1, rawText = "Land Awarded", sourceRegion = headingRegion ?? HeadingBox } },
        warnings = new[] { "Review section." }, classifierVersion = "document-section-rules/1.0"
    };
    private static object Table(string semantic = "AWARDED_LAND", int sectionIndex = 1,
        object? headingRegion = null) => new
    {
        observationType = "DocumentTableSemantic", tableId = "p1-t1", pageStart = 1, pageEnd = 1,
        sectionObservationIndex = sectionIndex, rawHeading = "Land Awarded",
        rawColumnLabels = new[] { new { column = 0, rawText = "Khasra", sourceRegion = Box },
                                  new { column = 1, rawText = "Area", sourceRegion = Box } },
        layout = "SIMPLE_TWO_COLUMN", semantic, confidence = .86m, requiresHumanReview = true,
        sourceRegion = Box,
        evidence = new[] { new { page = 1, rawText = "Land Awarded", sourceRegion = headingRegion ?? HeadingBox },
                           new { page = 1, rawText = "Khasra", sourceRegion = Box } },
        warnings = new[] { "Review table meaning." }, classifierVersion = "table-source-rules/1.0"
    };
    private static object Occurrence(string raw = "7//1 min", string? normalized = "7//1", string? qualifier = "min",
        string tableId = "p1-t1", int page = 1, int column = 0) => new
    {
        observationType = "KhasraOccurrence", occurrenceId = $"{tableId}-r1-c{column}-m1",
        tableId, page, sourceRow = 1, sourceColumn = column, mentionIndex = 1,
        rawKhasraText = raw, normalizedKhasraNumber = normalized, qualifier, explicitPart = false,
        semantic = "AWARDED_LAND", rawRowContext = $"{raw} | 4-16", sourceRegion = Box,
        areaFields = new[] { new { sourceColumn = 1, rawText = "4-16", role = "UNKNOWN", sourceRegion = Box } },
        confidence = .75m, requiresHumanReview = true,
        evidence = new[] { new { page, rawText = raw, sourceRegion = Box } },
        warnings = new[] { "Review source mention." }, classifierVersion = "table-source-rules/1.0"
    };
    private static LocalDocumentIntelligenceInput Input(bool enabled = true) =>
        new(2, Id, "unused.pdf", Guid.NewGuid(), null, PhysicalSha256: Sha, DocumentVersion: 2,
            PageCount: 1, GenreRouting: true, SectionObservations: true, TableSemantics: enabled);
    private static string Response(params object[] observations) => ResponseWithPages(1, observations);
    private static string ResponseWithPages(int pages, params object[] observations) => JsonSerializer.Serialize(new
    {
        contractVersion = 2, documentId = Id, status = "Completed", pagesProcessed = pages,
        candidates = Array.Empty<object>(), warnings = Array.Empty<string>(), metrics = new { khasraOccurrencesDetected = 1 },
        physicalSha256 = Sha, documentVersion = 2, pageCount = pages,
        processedAt = DateTimeOffset.Parse("2026-09-28T12:00:00Z"), extractorVersion = "local-award-worker/1.0",
        errors = Array.Empty<string>(), observations
    });
    private static LocalDocumentIntelligenceResult Parse(params object[] observations) =>
        LocalDocumentIntelligenceContract.ParseAndValidate(Response(observations), Input(), Sha, 1);

    [Fact]
    public void Typed_table_and_occurrence_stage_with_physical_source_identity()
    {
        var mapped = LocalIntelligenceCandidateMapper.Map(Parse(Genre(), Section(), Table(), Occurrence()), true, true, true);
        Assert.Equal(4, mapped.Count);
        Assert.Equal(AwardIngestionCandidateType.DocumentTableSemantic, mapped[2].CandidateType);
        Assert.Equal(AwardIngestionCandidateType.KhasraOccurrence, mapped[3].CandidateType);
        using var table = JsonDocument.Parse(mapped[2].PayloadJson);
        Assert.Equal("AWARDED_LAND", table.RootElement.GetProperty("observation").GetProperty("semantic").GetString());
        using var occurrence = JsonDocument.Parse(mapped[3].PayloadJson);
        Assert.Equal("7//1 min", occurrence.RootElement.GetProperty("observation").GetProperty("rawKhasraText").GetString());
        using var locator = JsonDocument.Parse(mapped[3].SourceLocatorJson!);
        Assert.Equal(Sha, locator.RootElement.GetProperty("processing").GetProperty("physicalSha256").GetString());
    }

    [Fact]
    public void Invalid_table_type_semantic_reference_region_and_evidence_fail_closed()
    {
        var valid = Response(Genre(), Section(), Table(), Occurrence());
        foreach (var bad in new[]
        {
            valid.Replace("DocumentTableSemantic", "UnexpectedTableType"),
            valid.Replace("AWARDED_LAND", "UNSUPPORTED_LAND"),
            valid.Replace("\"sectionObservationIndex\":1", "\"sectionObservationIndex\":2"),
            valid.Replace("\"pageEnd\":1,\"sectionObservationIndex\"", "\"pageEnd\":2,\"sectionObservationIndex\""),
            valid.Replace("\"layout\":\"SIMPLE_TWO_COLUMN\"", "\"layout\":\"BROKEN\""),
            valid.Replace("\"rawHeading\":\"Land Awarded\"", "\"rawHeading\":\"No source heading\""),
            valid.Replace("Land Awarded", "TRUE AND CORRECT AREA"),
            valid.Replace("\"role\":\"UNKNOWN\"", "\"role\":\"AWARDED\""),
            valid.Replace("\"width\":300", "\"width\":0")
        })
            Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(bad, Input(), Sha, 1));
    }

    [Fact]
    public void Invalid_occurrence_page_table_cell_and_normalization_fail_closed()
    {
        Assert.Throws<InvalidOperationException>(() => Parse(Genre(), Section(), Table(), Occurrence(normalized: "7//2")));
        Assert.Throws<InvalidOperationException>(() => Parse(Genre(), Section(), Table(), Occurrence(tableId: "p1-t9")));
        Assert.Throws<InvalidOperationException>(() => Parse(Genre(), Section(), Table(), Occurrence(page: 2)));
        Assert.Throws<InvalidOperationException>(() => Parse(Genre(), Section(), Table(), Occurrence(column: 1)));
        var unresolved = Parse(Genre(), Section(), Table(), Occurrence("7?1", null, null));
        Assert.Null(DocumentTableContract.ReadAll(unresolved, DocumentGenreContract.Read(unresolved, true, true),
            DocumentSectionContract.ReadAll(unresolved, DocumentGenreContract.Read(unresolved, true, true), true, true), true)
            .Occurrences[0].NormalizedKhasraNumber);
    }

    [Fact]
    public void Non_unknown_table_requires_matching_heading_above_its_source_region()
    {
        Assert.NotNull(Parse(Genre(), Section(), Table()));
        Assert.Throws<InvalidOperationException>(() => Parse(Genre(), Section(LaterHeadingBox),
            Table(headingRegion: LaterHeadingBox)));
        Assert.Throws<InvalidOperationException>(() => Parse(Genre(), Section(LaterHeadingBox), Table()));
        Assert.Throws<InvalidOperationException>(() => Parse(Genre(), Section(),
            Table(headingRegion: LaterHeadingBox)));
    }

    [Fact]
    public void Genre_and_feature_flags_cannot_be_bypassed()
    {
        Assert.Throws<InvalidOperationException>(() => Parse(Genre("UNKNOWN"), Section(), Table(), Occurrence()));
        var unknown = Parse(Genre("UNKNOWN"));
        Assert.Single(LocalIntelligenceCandidateMapper.Map(unknown, true, true, true));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(
            Response(Genre(), Section(), Table(), Occurrence()), Input(false), Sha, 1));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.SerializeRequest(
            Input() with { SectionObservations = false }));
    }

    [Fact]
    public void A_continued_table_needs_a_current_page_source_cue()
    {
        var continuedSection = new
        {
            observationType = "DocumentSection", semantic = "LAND_SCHEDULE", presentation = "SCHEDULE",
            pageStart = 1, pageEnd = 2, rawHeading = "Land Awarded", confidence = .85m,
            requiresHumanReview = true,
            evidence = new[] { new { page = 1, rawText = "Land Awarded", sourceRegion = HeadingBox },
                               new { page = 2, rawText = "Land Awarded (continued)", sourceRegion = HeadingBox } },
            warnings = new[] { "Explicit continuation." }, classifierVersion = "document-section-rules/1.0"
        };
        var continuedTable = new
        {
            observationType = "DocumentTableSemantic", tableId = "p2-t1", pageStart = 2, pageEnd = 2,
            sectionObservationIndex = 1, rawHeading = "Land Awarded",
            rawColumnLabels = new[] { new { column = 0, rawText = "Khasra", sourceRegion = Box },
                                      new { column = 1, rawText = "Area", sourceRegion = Box } },
            layout = "SIMPLE_TWO_COLUMN", semantic = "AWARDED_LAND", confidence = .86m,
            requiresHumanReview = true, sourceRegion = Box,
            evidence = new[] { new { page = 1, rawText = "Land Awarded", sourceRegion = HeadingBox },
                               new { page = 2, rawText = "Land Awarded (continued)", sourceRegion = HeadingBox },
                               new { page = 2, rawText = "Khasra", sourceRegion = Box } },
            warnings = new[] { "Review continued table." }, classifierVersion = "table-source-rules/1.0"
        };
        var input = Input() with { PageCount = 2 };
        var valid = ResponseWithPages(2, Genre(), continuedSection, continuedTable);
        var result = LocalDocumentIntelligenceContract.ParseAndValidate(valid, input, Sha, 2);
        Assert.Equal(3, LocalIntelligenceCandidateMapper.Map(result, true, true, true).Count);
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(
            valid.Replace("Land Awarded (continued)", "No continuation cue"), input, Sha, 2));
        Assert.Throws<InvalidOperationException>(() => LocalDocumentIntelligenceContract.ParseAndValidate(
            valid.Replace("\"rawText\":\"Land Awarded (continued)\",\"sourceRegion\":{\"x\":10,\"y\":50",
                          "\"rawText\":\"Land Awarded (continued)\",\"sourceRegion\":{\"x\":10,\"y\":240"),
            input, Sha, 2));
    }

    [Fact]
    public async Task Generic_occurrence_cannot_verify_or_commit_an_award_khasra()
    {
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var award = new Award { AwardNumber = "TEST-30" };
        var village = new Village { Name = "Fictional village" };
        var document = new LAC.Domain.Document { Id = Id, OriginalFileName = "source.pdf", StoragePath = "source.pdf" };
        db.AddRange(award, village, document, new AwardVillage { Award = award, Village = village });
        await db.SaveChangesAsync();
        var service = new AwardIngestionService(db, new AwardWorkflowService(db));
        var mapped = LocalIntelligenceCandidateMapper.Map(Parse(Genre(), Section(), Table(), Occurrence()), true, true, true);
        var session = await service.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Document,
            award.Id, village.Id, document.Id, "test", null, mapped, default);
        var items = await service.GetCandidatesAsync(session.Id, null, null, 0, 25, default);
        var occurrence = Assert.Single(items.Items, x => x.CandidateType == AwardIngestionCandidateType.KhasraOccurrence);
        Assert.Equal(AwardIngestionCandidateStatus.NeedsReview, occurrence.Status);
        await Assert.ThrowsAsync<AwardIngestionException>(() => service.VerifyFactAsync(occurrence.Id, new("Officer", null), default));
        Assert.Equal(0, await db.Set<AwardKhasra>().CountAsync());
        Assert.Equal(0, await db.Khasras.CountAsync());
    }
}
