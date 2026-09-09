using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace LAC.Tests;

public sealed class AwardPdfExtractionTests
{
    [Fact]
    public async Task Upload_requires_an_award_before_any_file_or_review_work_is_created()
    {
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 fictional no-context fixture");
        var storage = new MemoryStorage(bytes);
        var service = new AwardPdfExtractionService(db, storage, new AwardPdfJobQueue());

        var error = await Assert.ThrowsAsync<AwardIngestionException>(() => service.QueueUploadAsync(new MemoryStream(bytes), "fictional.pdf", "application/pdf", null, null, "Test officer", default));

        Assert.Equal("Choose an Award before uploading its PDF.", error.Message);
        Assert.Equal(0, storage.SaveCalls);
        Assert.Empty(await db.Documents.ToListAsync());
        Assert.Empty(await db.AwardDocumentExtractionJobs.ToListAsync());
        Assert.Empty(await db.AwardIngestionCandidates.ToListAsync());
        Assert.Empty(await db.Set<AwardKhasra>().ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Upload_links_document_before_background_work_and_duplicate_hash_reuses_identity()
    {
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var award = new Award { AwardNumber = "FICTIONAL-UPLOAD" };
        db.Add(award); await db.SaveChangesAsync();
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 fictional storage boundary fixture");
        var queue = new AwardPdfJobQueue();
        var service = new AwardPdfExtractionService(db, new MemoryStorage(bytes), queue);
        var first = await service.QueueUploadAsync(new MemoryStream(bytes), "fictional.pdf", "application/pdf", award.Id, null, "Test officer", default);
        var link = await db.DocumentAwards.SingleAsync();
        Assert.Equal(award.Id, link.AwardId); Assert.Equal(first.DocumentId, link.DocumentId);
        Assert.Empty(await db.AwardDocumentPageExtractions.ToListAsync());
        Assert.Empty(await db.AwardIngestionCandidates.ToListAsync());
        Assert.Null(first.JobId);
        Assert.Empty(await db.AwardDocumentExtractionJobs.ToListAsync());
        Assert.NotNull(await DocumentEvidenceQueries.AwardDocumentsAsync(db, award.Id, default));
        var second = await service.QueueUploadAsync(new MemoryStream(bytes), "same-content.pdf", "application/pdf", award.Id, null, "Test officer", default);
        Assert.Equal(first.DocumentId, second.DocumentId);
        Assert.Single(await db.Documents.ToListAsync()); Assert.Single(await db.DocumentAwards.ToListAsync());
        var analysis = await service.AnalyzeAsync(first.DocumentId, award.Id, null, default);
        Assert.NotNull(analysis.JobId);
        Assert.Equal(AwardDocumentExtractionJobStatus.Queued, (await service.GetAsync(analysis.JobId!.Value, default)).Status);
    }

    [Fact]
    public async Task Existing_unlinked_pdf_links_once_without_writing_or_copying_the_file()
    {
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var award = new Award { AwardNumber = "FICTIONAL-RECOVERY" };
        var document = new LAC.Domain.Document { DocumentType = "Award PDF", OriginalFileName = "already-stored.pdf", StoragePath = "stored-on-disk.pdf", Sha256Hash = "fixture" };
        db.AddRange(award, document); await db.SaveChangesAsync();
        var storage = new MemoryStorage(Array.Empty<byte>());
        var service = new AwardPdfExtractionService(db, storage, new AwardPdfJobQueue());

        var unlinked = await service.GetUnlinkedStoredDocumentsAsync(default);
        Assert.Contains(unlinked, x => x.DocumentId == document.Id);
        var first = await service.LinkStoredDocumentAsync(document.Id, award.Id, null, default);
        var second = await service.LinkStoredDocumentAsync(document.Id, award.Id, null, default);

        Assert.Equal(document.Id, first.DocumentId); Assert.Equal(document.Id, second.DocumentId);
        Assert.Equal(0, storage.SaveCalls); Assert.Equal(0, storage.DeleteCalls);
        Assert.Single(await db.DocumentAwards.ToListAsync());
        Assert.Empty(await db.AwardDocumentExtractionJobs.ToListAsync());
        Assert.Empty(await db.AwardIngestionCandidates.ToListAsync());
        Assert.Empty(await db.Set<AwardKhasra>().ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Award_document_review_state_excludes_final_rows_and_marks_reviewed_only_when_pending_is_empty()
    {
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var award=new Award { AwardNumber="REVIEW-STATE" };
        var document=new LAC.Domain.Document { OriginalFileName="review-state.pdf", StoragePath="review-state.pdf" };
        var session=new AwardIngestionSession { SourceType=AwardIngestionSourceType.Document, TargetAward=award, SourceDocument=document };
        var job=new AwardDocumentExtractionJob { Document=document, TargetAward=award, IngestionSession=session };
        db.AddRange(award,document,new DocumentAward { Award=award, Document=document },session,job,
            new AwardIngestionCandidate { Session=session, CandidateType=AwardIngestionCandidateType.UnmappedAwardFinding, StructuredPayloadJson="{}", Status=AwardIngestionCandidateStatus.Conflict });
        await db.SaveChangesAsync();
        static async Task<(bool reviewed,int attention)> Read(LacDbContext db,Guid awardId)
        {
            using var json=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(await DocumentEvidenceQueries.AwardDocumentsAsync(db,awardId,default)));
            var job=json.RootElement[0].GetProperty("Job");
            return (job.GetProperty("Reviewed").GetBoolean(),job.GetProperty("Attention").GetInt32());
        }
        var before=await Read(db,award.Id); Assert.False(before.reviewed); Assert.Equal(1,before.attention);
        var candidate=await db.AwardIngestionCandidates.SingleAsync();candidate.Status=AwardIngestionCandidateStatus.Skipped;await db.SaveChangesAsync();
        var after=await Read(db,award.Id); Assert.True(after.reviewed); Assert.Equal(0,after.attention);
    }

    [Theory]
    [InlineData(95.12345, 0.9512)]
    [InlineData(100, 1)]
    [InlineData(0, 0)]
    public void Ocr_confidence_fits_database_fraction_scale(decimal input, decimal expected)
    {
        Assert.Equal(expected, TesseractOcrEngine.NormalizeConfidence(input));
    }

    [Fact]
    public async Task Fictional_pdf_creates_staging_evidence_and_review_candidates_only()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var pdf = QuestPDF.Fluent.Document.Create(document => document.Page(page => { page.Margin(36); page.Content().Column(column => { column.Item().Text("Award No: FICTIONAL-2026/01"); column.Item().Text("Award Date: 01/01/2026"); column.Item().Text("Land Awarded"); column.Item().Text("Khasra No. 22//2 min"); column.Item().Text("Section 4 notification N-TEST/1"); }); })).GeneratePdf();
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var village = new Village { Name = "Fictional Village" }; var award = new Award { AwardNumber = "FICTIONAL-AWARD" }; var source = new LAC.Domain.Document { DocumentType = "Award PDF", OriginalFileName = "fictional-award.pdf", StoragePath = "fictional.pdf" }; var job = new AwardDocumentExtractionJob { Document = source, TargetAward = award, SelectedVillage = village };
        db.AddRange(village, award, new AwardVillage { Award = award, Village = village }, source, job); await db.SaveChangesAsync();
        var storage = new MemoryStorage(pdf); var runner = new AwardPdfJobRunner(db, storage, new AwardIngestionService(db, new AwardWorkflowService(db)), new UnavailableOcrEngine(), Rules(), NullLogger<AwardPdfJobRunner>.Instance);
        await runner.RunAsync(job.Id, default);
        var persisted = await db.AwardDocumentExtractionJobs.SingleAsync();
        Assert.Equal(AwardDocumentExtractionJobStatus.NeedsReview, persisted.Status); Assert.NotNull(persisted.IngestionSessionId); Assert.Single(await db.AwardDocumentPageExtractions.ToListAsync());
        Assert.Empty(await db.Khasras.ToListAsync()); Assert.Empty(await db.Set<AwardKhasra>().ToListAsync()); var candidates = await db.AwardIngestionCandidates.Where(x => x.SessionId == persisted.IngestionSessionId).ToListAsync(); Assert.NotEmpty(candidates);
        Assert.DoesNotContain(candidates, x => x.CandidateType == AwardIngestionCandidateType.AwardKhasra); // A labelled narrative line is not a verified Khasra table.
        var firstSession = persisted.IngestionSessionId; var rerun = await runner.ReanalyzeAsync(job.Id, default);
        Assert.Equal(AwardDocumentExtractionJobStatus.NeedsReview, rerun.Status); Assert.NotEqual(firstSession, rerun.IngestionSessionId); Assert.Single(await db.AwardDocumentPageExtractions.ToListAsync());
    }

    [Fact]
    public async Task Local_worker_contract_maps_only_to_review_staging_with_full_source_locator()
    {
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var village = new Village { Name = "Fictional Village" }; var award = new Award { AwardNumber = "FICTIONAL-LOCAL" }; var document = new LAC.Domain.Document { OriginalFileName = "fictional.pdf", StoragePath = "fictional.pdf" };
        db.AddRange(village, award, document, new AwardVillage { Award = award, Village = village }, new DocumentAward { Award = award, Document = document }); await db.SaveChangesAsync();
        static JsonElement Element(string value) => JsonDocument.Parse(value).RootElement.Clone();
        var result = new LocalDocumentIntelligenceResult(1, document.Id, "Completed", 2,
        [
            new("AwardKhasra", Element("{\"khasraNumber\":\"6//10\",\"qualifier\":null,\"recordedArea\":{\"rawOcr\":\"22 -- 3\",\"cellCropOcr\":\"22-3\",\"normalizedSuggestion\":\"22-3\",\"normalizationReason\":\"Normalized separator only\"},\"awardedArea\":{\"normalizedSuggestion\":\"22-3\"},\"rectangle\":{\"normalizedSuggestion\":\"6\"}}"), 1, Element("{\"x\":1,\"y\":2,\"width\":3,\"height\":4}"), "6//10", "6//10", "6//10", null, .99m, ["Geometry-backed OCR suggestion"]),
            new("LandClassification", Element("{\"khasraNumber\":\"6//10\",\"block\":{\"normalizedSuggestion\":\"A\"}}"), 2, Element("{\"x\":1,\"y\":2,\"width\":3,\"height\":4}"), null, null, null, null, null, []),
            new("UnmappedAwardFinding", Element("{\"category\":\"Local OCR narrative\"}"), 2, Element("{\"x\":0,\"y\":0,\"width\":100,\"height\":100}"), null, null, null, null, null, [])
        ], [], Element("{}"));

        var inputs = LocalIntelligenceCandidateMapper.Map(result);
        Assert.Equal([AwardIngestionCandidateType.AwardKhasra, AwardIngestionCandidateType.AwardLandClass, AwardIngestionCandidateType.UnmappedAwardFinding], inputs.Select(x => x.CandidateType));
        Assert.Contains("recordedArea", inputs[0].SourceLocatorJson!); Assert.Contains("cellCropOcr", inputs[0].SourceLocatorJson!);
        var staged = await new AwardIngestionService(db, new AwardWorkflowService(db)).CreatePreviewFromJsonAsync(AwardIngestionSourceType.Document, award.Id, village.Id, document.Id, "test", null, inputs, default);
        var rows = await db.AwardIngestionCandidates.Where(x => x.SessionId == staged.Id).ToListAsync();
        Assert.All(rows, row => { Assert.Equal(AwardIngestionCandidateStatus.NeedsReview, row.Status); Assert.False(row.SafeToConfirm); Assert.NotNull(row.SourceLocatorJson); });
        Assert.Empty(await db.Khasras.ToListAsync()); Assert.Empty(await db.Set<AwardKhasra>().ToListAsync()); Assert.Empty(await db.Set<SourceEvidence>().ToListAsync());
    }

    [Fact]
    public void Local_worker_unsupported_candidate_rejects_whole_mapping_before_staging()
    {
        static JsonElement Element(string value) => JsonDocument.Parse(value).RootElement.Clone();
        var result = new LocalDocumentIntelligenceResult(1, Guid.NewGuid(), "Completed", 1, [new("CourtCase", Element("{}"), 1, null, null, null, null, null, null, [])], [], Element("{}"));
        Assert.Throws<InvalidOperationException>(() => LocalIntelligenceCandidateMapper.Map(result));
    }

    private sealed class MemoryStorage(byte[] pdf) : IDocumentStorage
    {
        public int SaveCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct) { SaveCalls++; return Task.FromResult(fileName); }
        public Task<DocumentStorageWriteResult> SaveAndHashAsync(Stream content, string fileName, CancellationToken ct) { SaveCalls++; return Task.FromResult(new DocumentStorageWriteResult(fileName, "test", pdf.Length)); }
        public Task DeleteAsync(string storagePath, CancellationToken ct) { DeleteCalls++; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct) => Task.FromResult<Stream?>(new MemoryStream(pdf, writable: false));
        public StorageHealth GetHealth() => new("test", true, 1, 1);
    }

    private static AwardExtractionRuleEngine Rules() => new(new TextConceptMatcher(), new StrictKhasraParser(), new StrictDateParser(), new StrictAreaParser());
}
