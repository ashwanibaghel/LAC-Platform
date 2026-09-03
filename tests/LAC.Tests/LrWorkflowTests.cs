using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LAC.Tests;

public sealed class LrWorkflowTests
{
    [Fact]
    public async Task Existing_khasra_is_reused_inside_the_same_village()
    {
        await using var db = Db(); var setup = await SetupAsync(db); var workflow = new LrWorkflowService(db);
        var reused = await workflow.CreateKhasraAsync(setup.Village.Id, "22 // 2", null, null, null, null, null, default);
        Assert.Equal(setup.Khasra.Id, reused.Id);
    }

    [Fact]
    public async Task Same_khasra_number_in_another_village_remains_separate()
    {
        await using var db = Db(); var setup = await SetupAsync(db); var other = new Village { SubDivision = setup.Village.SubDivision, Name = "Other Village" }; db.Add(other); await db.SaveChangesAsync();
        var created = await new LrWorkflowService(db).CreateKhasraAsync(other.Id, "22//2", null, null, null, null, null, default);
        Assert.NotEqual(setup.Khasra.Id, created.Id);
        Assert.Equal(2, await db.Khasras.CountAsync(x => x.NormalizedNumber == "22//2"));
    }

    [Fact]
    public async Task Commit_creates_award_khasra_once_and_recommit_does_not_duplicate()
    {
        await using var db = Db(); var setup = await SetupAsync(db); var workflow = new LrWorkflowService(db);
        var created = await workflow.CreateAsync(setup.Register.Id, Input(setup, VerificationStatus.Verified), default);
        await workflow.CommitAsync(created.Id, created.Revision, false, default);
        await Assert.ThrowsAsync<LrWorkflowException>(() => workflow.CommitAsync(created.Id, created.Revision + 1, false, default));
        Assert.Equal(1, await db.Set<AwardKhasra>().CountAsync(x => x.AwardId == setup.Award.Id && x.KhasraId == setup.Khasra.Id));
    }

    [Fact]
    public async Task Notification_khasra_links_are_not_duplicated_when_an_existing_link_is_reused()
    {
        await using var db = Db(); var setup = await SetupAsync(db); db.Add(new NotificationKhasra { NotificationId = setup.Section4.Id, KhasraId = setup.Khasra.Id }); await db.SaveChangesAsync();
        var workflow = new LrWorkflowService(db); var row = await workflow.CreateAsync(setup.Register.Id, Input(setup, VerificationStatus.Verified), default);
        await workflow.CommitAsync(row.Id, row.Revision, false, default);
        Assert.Equal(1, await db.Set<NotificationKhasra>().CountAsync(x => x.NotificationId == setup.Section4.Id && x.KhasraId == setup.Khasra.Id));
    }

    [Fact]
    public async Task Wrong_notification_section_type_is_rejected()
    {
        await using var db = Db(); var setup = await SetupAsync(db); var input = Input(setup, VerificationStatus.Draft) with { Section4NotificationId = setup.Section6.Id };
        var error = await Assert.ThrowsAsync<LrWorkflowException>(() => new LrWorkflowService(db).CreateAsync(setup.Register.Id, input, default));
        Assert.Contains("Section 4", error.Message);
    }

    [Fact]
    public async Task Raw_khasra_text_remains_preserved_after_structured_correction()
    {
        await using var db = Db(); var setup = await SetupAsync(db); var workflow = new LrWorkflowService(db);
        var created = await workflow.CreateAsync(setup.Register.Id, Input(setup, VerificationStatus.NeedsReview) with { KhasraId = null, RawKhasraText = "22//2 min" }, default);
        var updated = await workflow.UpdateAsync(created.Id, created.Revision, Input(setup, VerificationStatus.Verified) with { RawKhasraText = "22//2 min" }, default);
        var saved = await db.LREntries.SingleAsync(x => x.Id == updated.Id);
        Assert.Equal("22//2 min", saved.RawKhasraText);
        Assert.Equal(setup.Khasra.Id, saved.KhasraId);
    }

    [Fact]
    public async Task Failed_commit_does_not_partially_create_canonical_relationships()
    {
        await using var db = Db(); var setup = await SetupAsync(db); var workflow = new LrWorkflowService(db);
        var row = await workflow.CreateAsync(setup.Register.Id, Input(setup, VerificationStatus.Verified) with { Section6NotificationId = null }, default);
        var invalid = await db.LREntries.SingleAsync(x => x.Id == row.Id);
        invalid.Section6NotificationId = setup.Section4.Id;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LrWorkflowException>(() => workflow.CommitAsync(row.Id, row.Revision, false, default));
        Assert.Equal(0, await db.Set<NotificationKhasra>().CountAsync());
        Assert.Equal(0, await db.Set<AwardKhasra>().CountAsync());
        Assert.Equal(VerificationStatus.Verified, (await db.LREntries.SingleAsync(x => x.Id == row.Id)).VerificationStatus);
    }

    [Fact]
    public async Task Needs_review_row_cannot_auto_commit()
    {
        await using var db = Db(); var setup = await SetupAsync(db); var workflow = new LrWorkflowService(db);
        var row = await workflow.CreateAsync(setup.Register.Id, Input(setup, VerificationStatus.NeedsReview), default);
        var error = await Assert.ThrowsAsync<LrWorkflowException>(() => workflow.CommitAsync(row.Id, row.Revision, false, default));
        Assert.Contains("Verified", error.Message);
    }

    [Fact]
    public async Task Duplicate_warning_is_reported_without_blocking_a_historical_repeat()
    {
        await using var db = Db(); var setup = await SetupAsync(db); var workflow = new LrWorkflowService(db);
        await workflow.CreateAsync(setup.Register.Id, Input(setup, VerificationStatus.Draft), default);
        var repeated = await workflow.CreateAsync(setup.Register.Id, Input(setup, VerificationStatus.Draft), default);
        Assert.True(repeated.PossibleDuplicate);
        Assert.Contains("Possible duplicate", repeated.DuplicateWarning);
    }

    [Fact]
    public async Task Parsed_area_must_be_positive_when_provided()
    {
        await using var db = Db(); var setup = await SetupAsync(db); var input = Input(setup, VerificationStatus.Draft) with { ParsedArea = 0 };
        await Assert.ThrowsAsync<LrWorkflowException>(() => new LrWorkflowService(db).CreateAsync(setup.Register.Id, input, default));
    }

    private static LrRowInput Input(Setup setup, VerificationStatus status) => new(1, "22//2 min", setup.Khasra.Id, "2 bigha", 2m, "Bigha", setup.Section4.Id, setup.Section6.Id, setup.Award.Id, "training row", status);
    private static async Task<Setup> SetupAsync(LacDbContext db)
    {
        var district = new District { Name = "D" }; var subdivision = new SubDivision { District = district, Name = "S" }; var village = new Village { SubDivision = subdivision, Name = "V" }; var khasra = new Khasra { Village = village, DisplayNumber = "22//2", NormalizedNumber = "22//2" }; var project = new AcquisitionProject { Name = "P" }; var section4 = new Notification { AcquisitionProject = project, SectionType = "4", NotificationNumber = "S4" }; var section6 = new Notification { AcquisitionProject = project, SectionType = "6", NotificationNumber = "S6" }; var award = new Award { AwardNumber = "A-1" }; var register = new VillageLR { Village = village, RegisterReference = "LR-1" };
        db.AddRange(district, subdivision, village, khasra, project, section4, section6, award, register); await db.SaveChangesAsync(); return new Setup(village, khasra, section4, section6, award, register);
    }
    private static LacDbContext Db() => new(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed record Setup(Village Village, Khasra Khasra, Notification Section4, Notification Section6, Award Award, VillageLR Register);
}
