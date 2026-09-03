using LAC.Domain;
using LAC.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LAC.Tests;

public sealed class KhasraWorkspaceTests
{
    [Fact]
    public async Task Import_creates_multiple_khasras_reuses_award_and_is_idempotent()
    {
        await using var db = Db(); var village = new Village { Name = "Test Village" }; db.Add(village); await db.SaveChangesAsync(); var service = new KhasraWorkspaceService(db);
        var rows = new[] { new KhasraWorkspaceRow("10//1", 2m, 5, 0, "AWD-100", new DateOnly(2026, 1, 1)), new KhasraWorkspaceRow("10//2", 1m, 10, 5, "AWD-100", null) };
        var first = await service.ImportAsync(village.Id, rows, default); var second = await service.ImportAsync(village.Id, rows, default);
        Assert.Equal(2, first.CreatedKhasras); Assert.Equal(1, first.CreatedAwards); Assert.Equal(2, first.CreatedAwardLinks);
        Assert.Equal(2, second.ReusedKhasras); Assert.Equal(0, second.CreatedAwardLinks);
        Assert.Equal(2, await db.Khasras.CountAsync()); Assert.Single(await db.Awards.ToListAsync()); Assert.Equal(2, await db.Set<AwardKhasra>().CountAsync());
        var saved = await db.Khasras.SingleAsync(x => x.DisplayNumber == "10//2"); Assert.Equal(1m, saved.AreaBigha); Assert.Equal(10, saved.AreaBiswa); Assert.Equal(5, saved.AreaBiswansi);
    }

    [Fact]
    public async Task Duplicate_in_one_import_is_rejected_without_corrupting_valid_rows()
    {
        await using var db = Db(); var village = new Village { Name = "Test Village" }; db.Add(village); await db.SaveChangesAsync(); var service = new KhasraWorkspaceService(db);
        var rows = new[] { new KhasraWorkspaceRow("11//1", 1m, null, null, null, null), new KhasraWorkspaceRow("11 // 1", 2m, null, null, null, null) };
        var preview = await service.PreviewRowsAsync(village.Id, rows, default);
        Assert.Single(preview.Problems); var result = await service.ImportAsync(village.Id, rows, default); Assert.Equal(1, result.CreatedKhasras); Assert.Equal(1, result.FailedRows); Assert.Single(await db.Khasras.ToListAsync());
    }

    [Fact]
    public async Task Excel_preview_identifies_valid_and_problem_rows_without_writing()
    {
        await using var db = Db(); var village = new Village { Name = "Test Village" }; db.Add(village); await db.SaveChangesAsync(); var service = new KhasraWorkspaceService(db);
        using var source = new MemoryStream(); using (var workbook = new XLWorkbook()) { var sheet = workbook.AddWorksheet("Khasras"); foreach (var (header, column) in new[] { ("Khasra No.", 1), ("Bigha", 2), ("Biswa", 3), ("Biswansi", 4), ("Award No.", 5), ("Award Date", 6) }) sheet.Cell(1, column).Value = header; sheet.Cell(2, 1).Value = "13//1"; sheet.Cell(2, 2).Value = 1; sheet.Cell(2, 5).Value = "AWD-X"; sheet.Cell(2, 6).Value = "19/09/1986"; sheet.Cell(3, 1).Value = "13//2"; sheet.Cell(3, 2).Value = -1; workbook.SaveAs(source); }
        source.Position = 0; var preview = await service.PreviewAsync(village.Id, source, default);
        Assert.Equal(2, preview.TotalRows); Assert.Equal(1, preview.ValidRows); Assert.Single(preview.Problems); Assert.Empty(await db.Khasras.ToListAsync());
    }

    [Fact]
    public async Task Same_number_in_another_village_is_separate_and_edit_is_audited()
    {
        await using var db = Db(); var a = new Village { Name = "A" }; var b = new Village { Name = "B" }; db.AddRange(a, b); await db.SaveChangesAsync(); var service = new KhasraWorkspaceService(db);
        await service.ImportAsync(a.Id, [new("12//1", 1m, 0, 0, null, null)], default); await service.ImportAsync(b.Id, [new("12//1", 2m, 0, 0, null, null)], default);
        var khasra = await db.Khasras.SingleAsync(x => x.VillageId == a.Id); await service.UpdateAsync(khasra.Id, new("12//1", 3m, 1, 2, null, null), default);
        Assert.Equal(2, await db.Khasras.CountAsync()); Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.EntityId == khasra.Id && x.Action == "KhasraWorkspaceEdited");
        db.Remove(khasra); await db.SaveChangesAsync(); Assert.Equal(RecordStatus.Archived, (await db.Khasras.SingleAsync(x => x.Id == khasra.Id)).RecordStatus);
    }

    [Fact]
    public async Task Multi_khasra_cell_and_negative_or_missing_khasra_are_blocked_while_blank_award_is_allowed()
    {
        await using var db = Db(); var village = new Village { Name = "Test Village" }; db.Add(village); await db.SaveChangesAsync(); var service = new KhasraWorkspaceService(db);
        var preview = await service.PreviewRowsAsync(village.Id, [new("3//19/1, 3//19/2", 0m, 16, 0, null, null), new("", 0m, 1, 0, "AWD", null), new("3//19/3", 0m, 15, 0, null, null)], default);
        Assert.Equal(1, preview.ValidRows); Assert.Equal(2, preview.InvalidRows); Assert.Contains(preview.Problems, x => x.Message.Contains("one Khasra", StringComparison.OrdinalIgnoreCase));
        var result = await service.ImportAsync(village.Id, preview.ImportableRows, default); Assert.Equal(1, result.CreatedKhasras); Assert.Empty(await db.Awards.ToListAsync());
    }

    [Fact]
    public async Task Area_conflict_is_blocked_and_never_silently_overwrites()
    {
        await using var db = Db(); var village = new Village { Name = "Test Village" }; var existing = new Khasra { Village = village, DisplayNumber = "3//19/1", NormalizedNumber = "3//19/1", AreaBigha = 0m, AreaBiswa = 16, AreaBiswansi = 0 }; db.Add(existing); await db.SaveChangesAsync(); var service = new KhasraWorkspaceService(db);
        var preview = await service.PreviewRowsAsync(village.Id, [new("3//19/1", 0m, 17, 0, null, null)], default);
        Assert.Equal("AREA CONFLICT", preview.Rows.Single().AreaStatus); Assert.False(preview.Rows.Single().CanImport); await service.ImportAsync(village.Id, preview.ImportableRows, default);
        Assert.Equal(16, (await db.Khasras.SingleAsync()).AreaBiswa);
    }

    [Fact]
    public void Template_has_exact_headers_and_instructions_sheet()
    {
        using var stream = new MemoryStream(KhasraWorkspaceService.ImportTemplate()); using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Khasra Import"); Assert.Equal(new[] { "Khasra No.", "Bigha", "Biswa", "Biswansi", "Award No.", "Award Date" }, Enumerable.Range(1, 6).Select(i => sheet.Cell(1, i).GetString()));
        Assert.Equal("Instructions", workbook.Worksheets.Last().Name); Assert.Contains("One row = one Khasra", workbook.Worksheet("Instructions").Cell(1, 1).GetString());
    }

    private static LacDbContext Db() => new(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
