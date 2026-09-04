using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LAC.Tests;

public sealed class AwardIngestionTests
{
    [Fact]
    public async Task Preview_is_durable_and_never_creates_canonical_khasras()
    {
        await using var db = Db(); var village = new Village { Name = "Fictional Village" }; var award = new Award { AwardNumber = "FICTIONAL-AWARD" }; db.AddRange(village, award, new AwardVillage { Award = award, Village = village }); await db.SaveChangesAsync();
        var auditBeforePreview = await db.AuditLogs.CountAsync(); var service = Service(db); var session = await service.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Excel, award.Id, village.Id, null, "tester", null, [Candidate("4//12", "min", 2m)], default);
        Assert.Equal(0, await db.Khasras.CountAsync()); Assert.Equal(auditBeforePreview, await db.AuditLogs.CountAsync());
        var loaded = await service.GetSummaryAsync(session.Id, default); Assert.Equal(AwardIngestionSessionStatus.ReadyToCommit, loaded.Status); Assert.Equal(1, loaded.Counts["Ready"]);
    }

    [Fact]
    public async Task Commit_creates_one_village_khasra_review_flag_and_award_link()
    {
        await using var db = Db(); var village = new Village { Name = "Fictional Village" }; var award = new Award { AwardNumber = "FICTIONAL-AWARD" }; db.AddRange(village, award, new AwardVillage { Award = award, Village = village }); await db.SaveChangesAsync();
        var service = Service(db); var session = await service.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Manual, award.Id, village.Id, null, "tester", null, [Candidate("4//12", "min", 2m)], default);
        var candidate = await db.AwardIngestionCandidates.SingleAsync(); var result = await service.CommitAsync(session.Id, [candidate.Id], "reviewer", default);
        var khasra = await db.Khasras.SingleAsync(); Assert.Equal("4//12", khasra.NormalizedNumber); Assert.Equal("min", khasra.Qualifier); Assert.Single(await db.Set<AwardKhasra>().Where(x => x.AwardId == award.Id && x.KhasraId == khasra.Id).ToListAsync()); Assert.Single(await db.KhasraReviewFlags.Where(x => x.KhasraId == khasra.Id).ToListAsync()); Assert.Equal(1, result.Created);
    }

    [Fact]
    public async Task Existing_khasra_area_conflict_is_not_committed_without_resolution()
    {
        await using var db = Db(); var village = new Village { Name = "Fictional Village" }; var award = new Award { AwardNumber = "FICTIONAL-AWARD" }; var khasra = new Khasra { Village = village, DisplayNumber = "4//12", NormalizedNumber = "4//12", AreaBigha = 2m }; db.AddRange(village, award, khasra, new AwardVillage { Award = award, Village = village }); await db.SaveChangesAsync();
        var service = Service(db); var session = await service.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Excel, award.Id, village.Id, null, null, null, [Candidate("4//12", null, 3m)], default);
        var candidate = await db.AwardIngestionCandidates.SingleAsync(); Assert.Equal(AwardIngestionCandidateStatus.Conflict, candidate.Status); await Assert.ThrowsAsync<AwardIngestionException>(() => service.CommitAsync(session.Id, [candidate.Id], null, default)); Assert.Empty(await db.Set<AwardKhasra>().ToListAsync());
    }

    [Fact]
    public async Task Repeated_ingestion_reuses_qualifier_aware_canonical_identity()
    {
        await using var db = Db(); var village = new Village { Name = "Fictional Village" }; var award = new Award { AwardNumber = "FICTIONAL-AWARD" }; db.AddRange(village, award, new AwardVillage { Award = award, Village = village }); await db.SaveChangesAsync(); var service = Service(db);
        var first = await service.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Excel, award.Id, village.Id, null, null, null, [Candidate("4//12", null, 2m), Candidate("4//12", "min", 2m)], default); var candidates = await db.AwardIngestionCandidates.Where(x => x.SessionId == first.Id).ToListAsync(); await service.CommitAsync(first.Id, candidates.Select(x => x.Id).ToList(), null, default);
        var second = await service.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Excel, award.Id, village.Id, null, null, null, [Candidate("4//12", "min", 2m)], default); var secondCandidate = await db.AwardIngestionCandidates.SingleAsync(x => x.SessionId == second.Id); await service.CommitAsync(second.Id, [secondCandidate.Id], null, default);
        Assert.Equal(2, await db.Khasras.CountAsync()); Assert.Equal(2, await db.Set<AwardKhasra>().CountAsync());
    }

    [Fact]
    public async Task Partial_commit_leaves_unresolved_candidates_in_durable_review()
    {
        await using var db = Db(); var village = new Village { Name = "Fictional Village" }; var award = new Award { AwardNumber = "FICTIONAL-AWARD" }; db.AddRange(village, award, new AwardVillage { Award = award, Village = village }); await db.SaveChangesAsync(); var service = Service(db);
        var session = await service.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Excel, award.Id, village.Id, null, null, null, [Candidate("1//1", null, 1m), Candidate("1//2", null, 1m), Candidate("1//2", null, 2m)], default);
        var ready = await db.AwardIngestionCandidates.SingleAsync(x => x.Sequence == 1); await service.CommitAsync(session.Id, [ready.Id], null, default);
        var summary = await service.GetSummaryAsync(session.Id, default); Assert.Equal(AwardIngestionSessionStatus.PartiallyCommitted, summary.Status); Assert.Equal(1, summary.Counts["Committed"]); Assert.True(summary.Counts.ContainsKey("Conflict"));
    }

    [Fact]
    public async Task Exact_notification_is_reused_and_linked_without_duplication()
    {
        await using var db = Db(); var village = new Village { Name = "Fictional Village" }; var award = new Award { AwardNumber = "FICTIONAL-AWARD" }; var notification = new Notification { SectionType = "Section 4", NotificationNumber = "N-1", NotificationDate = new DateOnly(2026, 1, 1) }; db.AddRange(village, award, notification, new AwardVillage { Award = award, Village = village }); await db.SaveChangesAsync(); var service = Service(db);
        var payload = new IngestionCandidateInput(AwardIngestionCandidateType.Notification, JsonSerializer.Serialize(new NotificationCandidate("Section 4", "N-1", new DateOnly(2026, 1, 1)))); var session = await service.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Excel, award.Id, village.Id, null, null, null, [payload], default); var candidate = await db.AwardIngestionCandidates.SingleAsync();
        await service.CommitAsync(session.Id, [candidate.Id], null, default); Assert.Single(await db.Notifications.ToListAsync()); Assert.Single(await db.AwardNotifications.Where(x => x.AwardId == award.Id && x.NotificationId == notification.Id).ToListAsync());
    }

    private static IngestionCandidateInput Candidate(string number, string? qualifier, decimal? canonicalArea) => new(AwardIngestionCandidateType.AwardKhasra, JsonSerializer.Serialize(new AwardKhasraCandidate(number, qualifier, canonicalArea, null, null, null, null, null, null, null, null)));
    private static AwardIngestionService Service(LacDbContext db) => new(db, new AwardWorkflowService(db));
    private static LacDbContext Db() => new(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
