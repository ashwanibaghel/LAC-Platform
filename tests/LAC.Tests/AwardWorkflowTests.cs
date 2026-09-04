using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LAC.Tests;

public sealed class AwardWorkflowTests
{
    [Fact]
    public async Task Missing_award_khasra_becomes_canonical_village_khasra_with_review_flag_and_is_reused()
    {
        await using var db = Db(); var village = new Village { Name = "Fictional Village" }; db.Add(village); await db.SaveChangesAsync();
        var service = new AwardWorkflowService(db); var award = await service.CreateAsync(new("TEST-AWARD-01", village.Id, null, null, null, null, null, null), default);
        var first = await service.LinkKhasraAsync(award.Id, new(village.Id, "2//22/1", null, 1m, 0, 0, 1m, 0, 0, "Recorded", null), default);
        var second = await service.LinkKhasraAsync(award.Id, new(village.Id, "2//22/1", null, null, null, null, null, null, null, null, null), default);
        Assert.True(first.CreatedKhasra); Assert.True(first.CreatedReviewFlag); Assert.True(first.CreatedAwardLink); Assert.False(second.CreatedKhasra); Assert.False(second.CreatedAwardLink);
        Assert.Single(await db.Khasras.Where(x => x.VillageId == village.Id).ToListAsync()); Assert.Single(await db.KhasraReviewFlags.Where(x => x.KhasraId == first.KhasraId && x.Status == "Open").ToListAsync()); Assert.Single(await db.Set<AwardKhasra>().ToListAsync());
        var importer = new KhasraWorkspaceService(db); var imported = await importer.ImportAsync(village.Id, [new("2//22/1", null, null, null, null, null)], default);
        Assert.Equal(0, imported.CreatedKhasras); Assert.Equal(1, imported.ReusedKhasras);
    }

    [Fact]
    public async Task Qualifier_is_part_of_award_khasra_identity_and_award_area_never_overwrites_master_area()
    {
        await using var db = Db(); var village = new Village { Name = "Fictional Village" }; var master = new Khasra { Village = village, DisplayNumber = "2//22/1", NormalizedNumber = "2//22/1", AreaBigha = 4m, AreaBiswa = 3 }; db.Add(master); await db.SaveChangesAsync();
        var service = new AwardWorkflowService(db); var award = await service.CreateAsync(new("TEST-AWARD-02", village.Id, null, null, null, null, null, null), default);
        await service.LinkKhasraAsync(award.Id, new(village.Id, "2//22/1", "min", null, null, null, 1m, 2, null, null, null), default);
        var link = await db.Set<AwardKhasra>().SingleAsync();
        Assert.Equal(4m, (await db.Khasras.SingleAsync(x => x.Id == master.Id)).AreaBigha); Assert.Equal(1m, link.AwardedAreaBigha); Assert.Equal(2, await db.Khasras.CountAsync());
    }

    [Fact]
    public async Task Review_can_resolve_without_recreating_khasra()
    {
        await using var db = Db(); var village = new Village { Name = "Fictional Village" }; db.Add(village); await db.SaveChangesAsync(); var service = new AwardWorkflowService(db); var award = await service.CreateAsync(new("TEST-AWARD-03", village.Id, null, null, null, null, null, null), default);
        var result = await service.LinkKhasraAsync(award.Id, new(village.Id, "4//1", null, null, null, null, null, null, null, null, null), default); var flag = await db.KhasraReviewFlags.SingleAsync();
        await service.ResolveReviewFlagAsync(flag.Id, "tester", default);
        Assert.Equal(result.KhasraId, (await db.Khasras.SingleAsync()).Id); Assert.Equal("Resolved", (await db.KhasraReviewFlags.SingleAsync()).Status); Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "KhasraMasterReviewResolved");
    }

    [Fact]
    public async Task Live_match_returns_existing_master_area_and_link_does_not_overwrite_it()
    {
        await using var db = Db(); var village = new Village { Name = "Fictional Village" }; var master = new Khasra { Village = village, DisplayNumber = "8//2", NormalizedNumber = "8//2", AreaBigha = 4m, AreaBiswa = 16, AreaBiswansi = 2 }; db.AddRange(village, master); await db.SaveChangesAsync(); var service = new AwardWorkflowService(db); var award = await service.CreateAsync(new("TEST-AWARD-AREA", village.Id, null, null, null, null, null, null), default);
        var match = await service.MatchKhasraAsync(award.Id, village.Id, "8//2", null, default);
        Assert.True(match.IsExisting); Assert.Equal(4m, match.CanonicalAreaBigha); Assert.Equal(16, match.CanonicalAreaBiswa);
        await service.LinkKhasraAsync(award.Id, new(village.Id, "8//2", null, 3m, 12, 1, 2m, 10, 0, "Recorded", null, 9m, 1, 1), default);
        var savedMaster = await db.Khasras.SingleAsync(x => x.Id == master.Id); var link = await db.Set<AwardKhasra>().SingleAsync(); Assert.Equal(4m, savedMaster.AreaBigha); Assert.Equal(16, savedMaster.AreaBiswa); Assert.Equal(3m, link.RecordedTotalAreaBigha); Assert.Equal(2m, link.AwardedAreaBigha);
    }

    [Fact]
    public async Task New_award_khasra_accepts_optional_master_area_and_creates_review_flag()
    {
        await using var db = Db(); var village = new Village { Name = "Fictional Village" }; db.Add(village); await db.SaveChangesAsync(); var service = new AwardWorkflowService(db); var award = await service.CreateAsync(new("TEST-AWARD-NEW-AREA", village.Id, null, null, null, null, null, null), default);
        var result = await service.LinkKhasraAsync(award.Id, new(village.Id, "9//4", "min", 3m, 8, 1, 2m, 5, 0, "Recorded", null, 4m, 16, 2), default);
        var master = await db.Khasras.SingleAsync(); Assert.True(result.CreatedKhasra); Assert.True(result.CreatedReviewFlag); Assert.Equal("min", master.Qualifier); Assert.Equal(4m, master.AreaBigha); Assert.Equal(16, master.AreaBiswa); Assert.Single(await db.KhasraReviewFlags.ToListAsync());
    }

    [Fact]
    public async Task Possession_is_partial_evidence_and_can_only_link_award_khasras()
    {
        await using var db = Db(); var village = new Village { Name = "Fictional Village" }; var khasra = new Khasra { Village = village, DisplayNumber = "7//1", NormalizedNumber = "7//1" }; db.AddRange(village, khasra); await db.SaveChangesAsync();
        var service = new AwardWorkflowService(db); var award = await service.CreateAsync(new("TEST-AWARD-04", village.Id, null, null, null, null, null, null), default); await service.LinkKhasraAsync(award.Id, new(village.Id, "7//1", null, null, null, null, 1m, 0, null, null, null), default);
        var possession = await service.AddPossessionAsync(award.Id, new DateOnly(2026, 1, 1), "Memo", "Partial", null, [khasra.Id], default);
        Assert.Equal("Partial", possession.Status); Assert.Single(await db.Set<PossessionKhasra>().Where(x => x.PossessionEventId == possession.Id).ToListAsync()); Assert.NotEqual("Possessed", (await db.Awards.SingleAsync(x => x.Id == award.Id)).Status);
    }

    private static LacDbContext Db() => new(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
