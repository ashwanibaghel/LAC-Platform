using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Xunit;

namespace LAC.Tests;

public sealed class AwardPdfExtractionTests
{
    [Fact]
    public async Task Fictional_pdf_creates_staging_evidence_and_review_candidates_only()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var pdf = QuestPDF.Fluent.Document.Create(document => document.Page(page => { page.Margin(36); page.Content().Column(column => { column.Item().Text("Award No: FICTIONAL-2026/01"); column.Item().Text("Award Date: 01/01/2026"); column.Item().Text("Land Awarded"); column.Item().Text("Khasra No. 22//2 min"); column.Item().Text("Section 4 notification N-TEST/1"); }); })).GeneratePdf();
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var village = new Village { Name = "Fictional Village" }; var award = new Award { AwardNumber = "FICTIONAL-AWARD" }; var source = new LAC.Domain.Document { DocumentType = "Award PDF", OriginalFileName = "fictional-award.pdf", StoragePath = "fictional.pdf" }; var job = new AwardDocumentExtractionJob { Document = source, TargetAward = award, SelectedVillage = village };
        db.AddRange(village, award, new AwardVillage { Award = award, Village = village }, source, job); await db.SaveChangesAsync();
        var storage = new MemoryStorage(pdf); var runner = new AwardPdfJobRunner(db, storage, new AwardIngestionService(db, new AwardWorkflowService(db)), new UnavailableOcrEngine(), new AwardSectionClassifier());
        await runner.RunAsync(job.Id, default);
        var persisted = await db.AwardDocumentExtractionJobs.SingleAsync();
        Assert.Equal(AwardDocumentExtractionJobStatus.NeedsReview, persisted.Status); Assert.NotNull(persisted.IngestionSessionId); Assert.Single(await db.AwardDocumentPageExtractions.ToListAsync());
        Assert.Empty(await db.Khasras.ToListAsync()); Assert.Empty(await db.Set<AwardKhasra>().ToListAsync()); Assert.NotEmpty(await db.AwardIngestionCandidates.Where(x => x.SessionId == persisted.IngestionSessionId).ToListAsync());
    }

    private sealed class MemoryStorage(byte[] pdf) : IDocumentStorage
    {
        public Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct) => Task.FromResult(fileName);
        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct) => Task.FromResult<Stream?>(new MemoryStream(pdf, writable: false));
    }
}
