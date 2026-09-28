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
    private sealed class SmokeClock : IOfficeClock
    {
        public DateOnly GetCurrentDate() => new(2026, 9, 28);
        public DateTimeOffset GetUtcNow() => new(2026, 9, 28, 4, 0, 0, TimeSpan.Zero);
    }
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

    private static void Row(IXLWorksheet sheet, int number, string caseNo, string title, string status = "Pending", string? ndoh = "28.09.2026", string? link = "https://delhihighcourt.nic.in/order.pdf", string court = "Delhi High Court")
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
        sheet.Cell(number, 10).Value = court;
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
    public async Task AuditedCourtAliases_KeepRawTextAndShareDeterministicIdentity()
    {
        using var db = Db(); var storage = new MemoryStorage(); var actor = new AppUser { DisplayName = "Importer" };
        db.AppUsers.Add(actor); await db.SaveChangesAsync();
        var bytes = Workbook(sheet =>
        {
            Row(sheet, 3, "WP(C) 100/2026", "Same title", court: "High Court");
            Row(sheet, 4, "WP(C) 100/2026", "Same title", court: "Delhi High Court");
            Row(sheet, 5, "WP(C) 101/2026", "Dwarka", court: "District Court Dwarka");
            Row(sheet, 6, "WP(C) 102/2026", "Supreme", court: "Suprem Court");
            Row(sheet, 7, "WP(C) 103/2026", "Tis Hazari", court: "Tis Hazari Court");
            Row(sheet, 8, "WP(C) 104/2026", "Unknown", court: "High  Court");
            Row(sheet, 9, "WP(C) 105/2026", "Unknown 2", court: "District Court Rohini");
            Row(sheet, 10, "WP(C) 106/2026", "Casing", court: " high court ");
        });
        var batch = await new CourtImportService(db, storage).StageAsync(new MemoryStream(bytes), "aliases.xlsx", null, actor.Id);
        Assert.Equal("Parsed", batch.Status);
        var rows = await db.CourtImportRows.OrderBy(x => x.SourceRowNumber).ToListAsync();
        Assert.Equal("High Court", rows[0].RawCourt);
        Assert.Equal("Delhi High Court", rows[0].SuggestedCourtName);
        Assert.Equal(rows[0].IdentityKey, rows[1].IdentityKey);
        Assert.Equal(CourtImportRowStatus.PotentialDuplicate, rows[0].RowStatus);
        Assert.Equal("Dwarka Court", rows[2].SuggestedCourtName);
        Assert.Equal("Supreme Court", rows[3].SuggestedCourtName);
        Assert.Equal("Tis Hazari Court", rows[4].SuggestedCourtName);
        Assert.All(rows.Skip(5).Take(2), row =>
        {
            Assert.Null(row.SuggestedCourtName);
            Assert.Null(row.IdentityKey);
            Assert.Equal(CourtImportRowStatus.NeedsReview, row.RowStatus);
            Assert.Contains("approved alias list", row.ValidationIssuesJson);
        });
        Assert.Equal(" high court ", rows[7].RawCourt);
        Assert.Equal("Delhi High Court", rows[7].SuggestedCourtName);
        Assert.Equal("delhihighcourt|wpc|106|2026", rows[7].IdentityKey);
    }

    [Fact]
    public async Task HighCourtAlias_MatchesExistingCaseWithoutChangingItsName()
    {
        using var db = Db(); var storage = new MemoryStorage(); var actor = new AppUser { DisplayName = "Importer" };
        var existing = new CourtCase { CourtName = "Delhi High Court", CaseNumber = "WP(C) 940/2015", CaseTitle = "Same title" };
        db.AddRange(actor, existing); await db.SaveChangesAsync();
        var bytes = Workbook(sheet => Row(sheet, 3, "WP(C) 940/2015", "Same title", court: "High Court"));
        var batch = await new CourtImportService(db, storage).StageAsync(new MemoryStream(bytes), "alias.xlsx", null, actor.Id);
        Assert.Equal("Parsed", batch.Status);
        var row = await db.CourtImportRows.SingleAsync();
        Assert.Equal(CourtImportRowStatus.ExistingExact, row.RowStatus);
        Assert.Equal(existing.Id, row.CandidateCourtCaseId);
        Assert.Equal("High Court", row.RawCourt);
        Assert.Equal("Delhi High Court", (await db.CourtCases.SingleAsync()).CourtName);
    }

    [Fact]
    public async Task SafeAliasImport_WritesCanonicalCourtAndPreservesRawProvenance()
    {
        using var db = Db(); var storage = new MemoryStorage(); var actor = new AppUser { DisplayName = "Importer" };
        db.AppUsers.Add(actor); await db.SaveChangesAsync();
        var bytes = Workbook(sheet =>
        {
            Row(sheet, 3, "WP(C) 201/2026", "Canonical import", court: "High Court");
            Row(sheet, 4, "WP(C) 202/2026", "Needs officer", court: "District Court Rohini");
        });
        var batch = await new CourtImportService(db, storage).StageAsync(new MemoryStream(bytes), "alias.xlsx", null, actor.Id);
        var role = new Role { Code = "ALIAS_IMPORTER", Name = "Alias importer" };
        db.Roles.Add(role); db.UserRoles.Add(new UserRole { UserId = actor.Id, RoleId = role.Id });
        foreach (var code in new[] { PermissionCodes.CourtView, PermissionCodes.CourtCreate, PermissionCodes.CourtEdit })
        {
            var permission = new Permission { Code = code, Name = code, Category = "Court" };
            db.Permissions.Add(permission);
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = ScopeMode.All });
        }
        await db.SaveChangesAsync();
        var auth = new CourtAuthorizationService(db, null!, null!, null!);
        var review = new CourtImportReviewService(db, auth, new CourtWorkflowService(db, auth, storage));
        var summary = await review.ApproveSafeAsync(batch.Id, actor.Id);
        Assert.Equal(1, summary.Ready);
        Assert.Equal(1, summary.Unresolved);
        Assert.Equal(1, (await review.CommitAsync(batch.Id, actor.Id)).CommittedThisRun);
        Assert.Equal("Delhi High Court", (await db.CourtCases.SingleAsync()).CourtName);
        var rows = await db.CourtImportRows.OrderBy(x => x.SourceRowNumber).ToListAsync();
        Assert.Equal("High Court", rows[0].RawCourt);
        Assert.Equal("Delhi High Court", rows[0].ApprovedCourtName);
        Assert.Equal(CourtImportCommitStatus.Committed, rows[0].CommitStatus);
        Assert.Equal("District Court Rohini", rows[1].RawCourt);
        Assert.Null(rows[1].ResolutionAction);
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
        foreach (var group in rows.GroupBy(x => x.RawCourt ?? "<blank>").OrderBy(x => x.Key))
            _output.WriteLine($"RawCourt {group.Key}: {group.Count()}");
        foreach (var group in rows.GroupBy(x => x.SuggestedCourtName ?? "<unresolved>").OrderBy(x => x.Key))
            _output.WriteLine($"SuggestedCourtName {group.Key}: {group.Count()}");
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

        var safe = rows.Where(x => x.RowStatus == CourtImportRowStatus.NewCandidate &&
            x.IdentityKey != null && x.CandidateCourtCaseId == null &&
            x.SuggestedStatusClass is CourtImportStatusClass.Pending or CourtImportStatusClass.Disposed &&
            x.ValidationIssuesJson == "[]" &&
            x.SuggestedCourtName is "Delhi High Court" or "Dwarka Court" or "Supreme Court" or "Tis Hazari Court").ToList();
        _output.WriteLine($"Safe bulk candidates: {safe.Count}");
        _output.WriteLine($"Blocked unknown court: {rows.Count(x => !string.IsNullOrWhiteSpace(x.RawCourt) && x.SuggestedCourtName is null)}");
        foreach (var group in safe.GroupBy(x => x.SuggestedCourtName!).OrderBy(x => x.Key))
            _output.WriteLine($"Safe canonical CourtName {group.Key}: {group.Count()}");
        _output.WriteLine($"Blocked PotentialDuplicate: {rows.Count(x => x.RowStatus == CourtImportRowStatus.PotentialDuplicate)}");
        _output.WriteLine($"Blocked IdentityConflict: {rows.Count(x => x.RowStatus == CourtImportRowStatus.IdentityConflict)}");
        _output.WriteLine($"Blocked NeedsReview: {rows.Count(x => x.RowStatus == CourtImportRowStatus.NeedsReview)}");
        _output.WriteLine($"Blocked Invalid: {rows.Count(x => x.RowStatus == CourtImportRowStatus.Invalid)}");
        _output.WriteLine($"Safe with parsed NDOH: {safe.Count(x => x.ParsedNdoh.HasValue)}");
        _output.WriteLine($"Safe with advocate: {safe.Count(x => !string.IsNullOrWhiteSpace(x.RawAdvocate))}");
        _output.WriteLine($"Safe with valid last-order URL: {safe.Count(x => x.LastOrderLinkState == "ValidHttpUrl")}");
        var role = new Role { Code = "SMOKE_IMPORTER", Name = "Smoke importer" };
        db.Roles.Add(role); db.UserRoles.Add(new UserRole { UserId = actor.Id, RoleId = role.Id });
        foreach (var code in new[] { PermissionCodes.CourtView, PermissionCodes.CourtCreate, PermissionCodes.CourtEdit })
        {
            var permission = new Permission { Code = code, Name = code, Category = "Court" };
            db.Permissions.Add(permission);
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = ScopeMode.All });
        }
        await db.SaveChangesAsync();
        var auth = new CourtAuthorizationService(db, null!, null!, null!);
        var review = new CourtImportReviewService(db, auth, new CourtWorkflowService(db, auth, storage));
        var approved = await review.ApproveSafeAsync(result.Id, actor.Id);
        Assert.Equal(safe.Count, approved.Ready);
        Assert.Equal(310 - safe.Count, approved.Unresolved);
        // In-memory database is unique to this test; no production or application DB is touched.
        var committed = await review.CommitAsync(result.Id, actor.Id);
        _output.WriteLine($"Isolated canonical commit: {committed.CommittedThisRun}; failures: {committed.Failures.Count}");
        foreach (var failure in committed.Failures.Take(5)) _output.WriteLine(failure);
        Assert.Equal(safe.Count, committed.CommittedThisRun);
        Assert.Empty(committed.Failures);
        Assert.Equal(safe.Count, await db.CourtCases.CountAsync());
        Assert.Equal(0, (await review.CommitAsync(result.Id, actor.Id)).CommittedThisRun);
        Assert.Equal(safe.Count, await db.CourtCases.CountAsync());
        var directory = new CourtProjectionService(db, auth, null!, new SmokeClock());
        var defaultPage = await directory.GetCourtCasesAsync(new CourtCaseFilterQuery(PageSize: 20), actor.Id);
        var pendingCount = await db.CourtCases.CountAsync(c => c.CurrentStatus == "Pending");
        var disposedCount = await db.CourtCases.CountAsync(c => c.CurrentStatus == "Disposed");
        _output.WriteLine($"Operational smoke total canonical: {defaultPage.TotalCount}; Pending: {pendingCount}; Disposed: {disposedCount}");
        foreach (var filter in new[] { "Today", "Upcoming", "Overdue", "NoNdoh" })
            _output.WriteLine($"Operational smoke {filter}: {(await directory.GetCourtCasesAsync(new CourtCaseFilterQuery(NdohFilter: filter), actor.Id)).TotalCount}");
        foreach (var item in defaultPage.Items)
            _output.WriteLine($"Queue row: {item.CaseNumber} | {item.CurrentStatus} | {item.OperationalNdoh?.ToString("yyyy-MM-dd") ?? "—"} | {item.QueueState}");
        Assert.Equal(214, defaultPage.TotalCount);
        Assert.Equal(20, defaultPage.Items.Count);
    }
}
