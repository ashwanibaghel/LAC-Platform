using System.Security.Cryptography;
using ClosedXML.Excel;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace LAC.Tests;

public sealed class CourtImportTests
{
    private readonly ITestOutputHelper _output;
    public CourtImportTests(ITestOutputHelper output) => _output = output;

    private sealed class MemoryStorage : IDocumentStorage
    {
        private readonly Dictionary<string, byte[]> _files = new();
        public Task<string> SaveAsync(Stream content, string name, CancellationToken ct) => throw new NotSupportedException();
        public async Task<DocumentStorageWriteResult> SaveAndHashAsync(Stream content, string name, CancellationToken ct)
        {
            using var copy = new MemoryStream(); await content.CopyToAsync(copy, ct);
            var bytes = copy.ToArray(); var path = Guid.NewGuid().ToString("N"); _files[path] = bytes;
            return new(path, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.LongLength);
        }
        public Task DeleteAsync(string path, CancellationToken ct) { _files.Remove(path); return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string path, CancellationToken ct) => Task.FromResult<Stream?>(_files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes, false) : null);
        public StorageHealth GetHealth() => new("memory", true, null, null);
        public byte[] Get(string path) => _files[path];
    }

    private static LacDbContext Db()
    {
        var options = new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new LacDbContext(options);
    }

    private static byte[] Workbook(Action<IXLWorksheet> fill, string sheetName = CourtImportService.PrimarySheet)
    {
        using var book = new XLWorkbook(); var sheet = book.AddWorksheet(sheetName);
        var headers = new[] { "Sr. No.", "NDOH", "Case Satus", "Recived from which advocate", "Case title", "Case No.", "Village", "Award No.", "Directions", "Which Court Pertains to", "Last order link", "Brief facts of the case" };
        for (var i = 0; i < headers.Length; i++) sheet.Cell(2, i + 1).Value = headers[i];
        fill(sheet); using var output = new MemoryStream(); book.SaveAs(output); return output.ToArray();
    }

    private static void Row(IXLWorksheet sheet, int number, string caseNo, string title, string status = "Pending", string? ndoh = "28.09.2026", string? link = "https://delhihighcourt.nic.in/order.pdf")
    {
        sheet.Cell(number, 1).Value = number - 2;
        if (ndoh != null) sheet.Cell(number, 2).Value = ndoh;
        sheet.Cell(number, 3).Value = status;
        sheet.Cell(number, 4).Value = "Advocate A";
        sheet.Cell(number, 5).Value = title;
        sheet.Cell(number, 6).Value = caseNo;
        sheet.Cell(number, 7).Value = "Village A";
        sheet.Cell(number, 8).Value = "01/2024/SW";
        sheet.Cell(number, 9).Value = "Direction A";
        sheet.Cell(number, 10).Value = "Delhi High Court";
        if (link != null) sheet.Cell(number, 11).Value = link;
        sheet.Cell(number, 12).Value = "Fact A";
    }

    [Fact]
    public async Task PrimarySheet_ExactTyposAndSourceValues_AreStagedWithoutCanonicalWrites()
    {
        using var db = Db(); var storage = new MemoryStorage(); var actor = new AppUser { DisplayName = "Importer" }; db.AppUsers.Add(actor); await db.SaveChangesAsync();
        var bytes = Workbook(sheet =>
        {
            Row(sheet, 3, "WP(C) 100/2026", "Title A");
            sheet.Cell(3, 13).Value = "attend"; sheet.Cell(3, 14).Value = new DateTime(2026, 5, 18);
            Row(sheet, 4, "WP(C) 101/2026", "Title B", "PENDING", "Disposed with directions", "chrome-extension://viewer/file.pdf");
            Row(sheet, 5, "WP(C) 102/2026", "Title C", "pending", null, "delhihighcourt.nic.in/order.pdf");
            Row(sheet, 6, "WP(C) 103/2026", "Title D", "Disposed off", "25.09.2026", "https://delhihighcourt.nic.in/order.pdf");
            Row(sheet, 7, "WP(C) 104/2026", "Title E", "disposed off", "25.09.2026", "bad://url");
            Row(sheet, 8, "WP(C) 105/2026", "Title F", "SINE-DIE");
            Row(sheet, 9, "WP(C) 106/2026", "Title G", "");
            Row(sheet, 10, "WP(C) 107/2026", "Title H"); sheet.Cell(10, 2).Value = new DateTime(2027, 2, 17);
        });
        var result = await new CourtImportService(db, storage).StageAsync(new MemoryStream(bytes), "office.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", actor.Id);
        Assert.True(result.Status == "Parsed", result.FailureMessage); Assert.Equal(8, result.TotalRows);
        var rows = await db.CourtImportRows.OrderBy(x => x.SourceRowNumber).ToListAsync();
        Assert.Equal("Advocate A", rows[0].RawAdvocate);
        Assert.Equal(new DateOnly(2026, 9, 28), rows[0].ParsedNdoh);
        Assert.Contains("attend", rows[0].ExtraCellsJson); Assert.Contains("N", rows[0].ExtraCellsJson);
        Assert.Equal("Direction A", rows[0].RawDirections); Assert.Equal("Fact A", rows[0].RawBriefFacts);
        Assert.Equal("ValidHttpUrl", rows[0].LastOrderLinkState);
        Assert.Equal("PENDING", rows[1].RawStatus); Assert.Equal(CourtImportStatusClass.Pending, rows[1].SuggestedStatusClass);
        Assert.Equal("Disposed with directions", rows[1].RawNdoh); Assert.Null(rows[1].ParsedNdoh);
        Assert.Equal("NeedsReview", rows[1].LastOrderLinkState);
        Assert.Null(rows[2].RawNdoh); Assert.Null(rows[2].ParsedNdoh); Assert.Equal("NeedsReview", rows[2].LastOrderLinkState);
        Assert.All(rows.Skip(3).Take(2), x => Assert.Equal(CourtImportStatusClass.Disposed, x.SuggestedStatusClass));
        Assert.Equal(CourtImportStatusClass.Attention, rows[5].SuggestedStatusClass); Assert.Equal(CourtImportRowStatus.NeedsReview, rows[5].RowStatus);
        Assert.Null(rows[6].SuggestedStatusClass); Assert.Equal(CourtImportRowStatus.NeedsReview, rows[6].RowStatus);
        Assert.Equal(new DateOnly(2027, 2, 17), rows[7].ParsedNdoh);
        var doc = await db.Documents.SingleAsync();
        Assert.Equal("office.xlsx", doc.OriginalFileName); Assert.Equal(bytes.Length, doc.FileSize);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), doc.Sha256Hash);
        Assert.Equal(bytes, storage.Get(doc.StoragePath));
        Assert.Empty(db.CourtCases); Assert.Empty(db.CourtProceedings); Assert.Empty(db.CourtCaseRepresentatives);
        Assert.Empty(db.Set<CourtCaseAward>()); Assert.Empty(db.Set<CourtCaseKhasra>()); Assert.Empty(db.CourtCaseMatters);
    }

    [Fact]
    public async Task MissingPrimarySheet_FailsVisibly()
    {
        using var db = Db(); var storage = new MemoryStorage(); var actor = new AppUser { DisplayName = "Importer" }; db.AppUsers.Add(actor); await db.SaveChangesAsync();
        var bytes = Workbook(sheet => Row(sheet, 3, "WP(C) 100/2026", "Title"), "Wrong sheet");
        var result = await new CourtImportService(db, storage).StageAsync(new MemoryStream(bytes), "office.xlsx", null, actor.Id);
        Assert.Equal("Failed", result.Status); Assert.Contains(CourtImportService.PrimarySheet, result.FailureMessage);
        Assert.Empty(db.CourtImportRows);
    }

    [Fact]
    public async Task DuplicateFallback_ConflictAndExactExistingMatch_AreReviewable()
    {
        using var db = Db(); var storage = new MemoryStorage(); var actor = new AppUser { DisplayName = "Importer" };
        var existing = new CourtCase { CourtName = "Delhi High Court", CaseNumber = "W.P.(C) - 940 / 2015", CaseTitle = "Existing Title" };
        db.AddRange(actor, existing); await db.SaveChangesAsync();
        var bytes = Workbook(sheet =>
        {
            Row(sheet, 3, "WP (C) 3352/2024 & CM APPL. 13812/2024", "Same title");
            Row(sheet, 4, "WP (C) 3352/2024 & CM APPL. 13812/2024", "Same title");
            Row(sheet, 5, "WP(C) No.15528/2025", "Title One");
            Row(sheet, 6, "WP(C) No.15528/2025", "Title Two");
            Row(sheet, 7, "WP(C) 940/2015", "Existing Title");
            Row(sheet, 8, "WP(C) 200/2026 & SLP 3/2026", "Unresolved");
        });
        var result = await new CourtImportService(db, storage).StageAsync(new MemoryStream(bytes), "office.xlsx", null, actor.Id);
        Assert.True(result.Status == "Parsed", result.FailureMessage);
        var rows = await db.CourtImportRows.OrderBy(x => x.SourceRowNumber).ToListAsync();
        Assert.All(rows.Take(2), x => { Assert.Equal(CourtImportRowStatus.PotentialDuplicate, x.RowStatus); Assert.Null(x.IdentityKey); });
        Assert.All(rows.Skip(2).Take(2), x => Assert.Equal(CourtImportRowStatus.IdentityConflict, x.RowStatus));
        Assert.Equal(CourtImportRowStatus.ExistingExact, rows[4].RowStatus); Assert.Equal(existing.Id, rows[4].CandidateCourtCaseId);
        Assert.Equal(CourtImportRowStatus.NeedsReview, rows[5].RowStatus); Assert.Null(rows[5].CandidateCourtCaseId);
        Assert.Single(db.CourtCases); Assert.Equal("Existing Title", (await db.CourtCases.SingleAsync()).CaseTitle);
    }

    [Fact]
    public async Task RealWorkbook_ReadOnlySmoke_WhenPathProvided()
    {
        var path = Environment.GetEnvironmentVariable("COURT_IMPORT_SMOKE_FILE");
        if (string.IsNullOrWhiteSpace(path)) return;
        using var db = Db(); var storage = new MemoryStorage(); var actor = new AppUser { DisplayName = "Smoke importer" };
        db.AppUsers.Add(actor); await db.SaveChangesAsync();
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var result = await new CourtImportService(db, storage).StageAsync(source, Path.GetFileName(path), null, actor.Id);
        Assert.True(result.Status == "Parsed", result.FailureMessage);
        var rows = await db.CourtImportRows.ToListAsync();
        _output.WriteLine($"Total staged: {rows.Count}");
        foreach (var group in rows.GroupBy(x => x.RowStatus).OrderBy(x => x.Key)) _output.WriteLine($"Classification {group.Key}: {group.Count()}");
        _output.WriteLine($"NDOH parsed: {rows.Count(x => x.ParsedNdoh.HasValue)}");
        _output.WriteLine($"NDOH nonblank unparsed: {rows.Count(x => !string.IsNullOrWhiteSpace(x.RawNdoh) && !x.ParsedNdoh.HasValue)}");
        _output.WriteLine($"NDOH blank: {rows.Count(x => string.IsNullOrWhiteSpace(x.RawNdoh))}");
        foreach (var group in rows.GroupBy(x => x.SuggestedStatusClass?.ToString() ?? "Blank").OrderBy(x => x.Key)) _output.WriteLine($"Status {group.Key}: {group.Count()}");
        foreach (var group in rows.GroupBy(x => x.LastOrderLinkState ?? "Blank").OrderBy(x => x.Key)) _output.WriteLine($"URL {group.Key}: {group.Count()}");
        _output.WriteLine($"Identity parsed: {rows.Count(x => x.IdentityKey is not null)}; unresolved: {rows.Count(x => x.IdentityKey is null)}");
        var known = rows.Where(x => x.RawCaseNumber == "WP (C) 3352/2024 & CM APPL. 13812/2024").ToList();
        _output.WriteLine($"Known duplicate rows: {known.Count}; classifications: {string.Join(",", known.Select(x => x.RowStatus))}");
        Assert.Equal(310, rows.Count);
        Assert.Equal(2, known.Count);
        Assert.All(known, x => Assert.True(x.RowStatus is CourtImportRowStatus.PotentialDuplicate or CourtImportRowStatus.IdentityConflict));
        Assert.Empty(db.CourtCases); Assert.Empty(db.CourtProceedings); Assert.Empty(db.CourtCaseRepresentatives);
        Assert.Empty(db.Set<CourtCaseAward>()); Assert.Empty(db.Set<CourtCaseKhasra>()); Assert.Empty(db.CourtCaseMatters);
        var pageCount = (int)Math.Ceiling(rows.Count / 25.0);
        var service = new CourtImportService(db, storage);
        var browsed = 0;
        for (var page = 1; page <= pageCount; page++)
        {
            var (items, total) = await service.RowsAsync(result.Id, null, null, null, page, 25);
            Assert.Equal(310, total); browsed += items.Count;
        }
        Assert.Equal(310, browsed);
    }
}
