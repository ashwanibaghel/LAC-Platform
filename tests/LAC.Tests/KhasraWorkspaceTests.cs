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
        Assert.Single(preview.Problems); await Assert.ThrowsAsync<KhasraWorkspaceException>(() => service.ImportAsync(village.Id, rows, default)); Assert.Empty(await db.Khasras.ToListAsync());
    }

    [Fact]
    public async Task Excel_preview_identifies_valid_and_problem_rows_without_writing()
    {
        await using var db = Db(); var village = new Village { Name = "Test Village" }; db.Add(village); await db.SaveChangesAsync(); var service = new KhasraWorkspaceService(db);
        using var source = new MemoryStream(); using (var workbook = new XLWorkbook()) { var sheet = workbook.AddWorksheet("Khasras"); sheet.Cell(1, 1).Value = "Khasra Number"; sheet.Cell(1, 2).Value = "Bigha"; sheet.Cell(1, 3).Value = "Biswa"; sheet.Cell(1, 4).Value = "Biswansi"; sheet.Cell(1, 5).Value = "Award Number"; sheet.Cell(2, 1).Value = "13//1"; sheet.Cell(2, 2).Value = 1; sheet.Cell(2, 5).Value = "AWD-X"; sheet.Cell(3, 1).Value = "13//2"; sheet.Cell(3, 2).Value = -1; workbook.SaveAs(source); }
        source.Position = 0; var preview = await service.PreviewAsync(village.Id, source, default);
        Assert.Equal(2, preview.TotalRows); Assert.Single(preview.ValidRows); Assert.Single(preview.Problems); Assert.Empty(await db.Khasras.ToListAsync());
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

    private static LacDbContext Db() => new(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
