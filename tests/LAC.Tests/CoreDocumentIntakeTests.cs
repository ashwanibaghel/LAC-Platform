using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Fluent;
using Npgsql;
using Xunit;

namespace LAC.Tests;

public sealed class CoreDocumentClassifierTests
{
    [Theory]
    [InlineData("Award No. 42/2010-11", "Award")]
    [InlineData("Award Number 42/2010-11", "Award")]
    [InlineData("Award Number 42/2010-11\nStatement A\nCompensation schedule", "Award")]
    [InlineData("NM of Award No. 42/2010-11", "NM")]
    [InlineData("Naksha Muntazamin\nAward No. 42/2010-11", "NM")]
    [InlineData("Naksha Muntazimin\nAward No. 42/2010-11", "NM")]
    [InlineData("Naqsha Mutabiq\nAward No. 42/2010-11", "NM")]
    [InlineData("N.M.\nAward Number 42/2010-11", "NM")]
    [InlineData("Statement A in Award No. 42/2010-11", "StatementA")]
    [InlineData("Possession Proceedings relating to Award No. 42/2010-11", "PossessionProceeding")]
    [InlineData("Possession Proceeding\nAward Number 42/2010-11", "PossessionProceeding")]
    [InlineData("A letter referring to Award No. 42/2010-11", "Unknown")]
    [InlineData("Unidentified scanned source", "Unknown")]
    public void Detects_opening_document_class_without_using_a_filename(string text, string role)
    {
        var result = Classify(text);
        Assert.Equal(role, result.DetectedRole);
        if (role != "Unknown") { Assert.Equal("42/2010-11", result.DetectedAwardNumber); Assert.False(result.RequiresReview); }
        else Assert.True(result.RequiresReview);
    }

    [Theory]
    [InlineData("Award No. 42/2010-11\nAward Number 43/2010-11")]
    [InlineData("Award Nos. 42/2010-11 and 43/2010-11")]
    [InlineData("Award No. 42/2010-11, 43/2010-11")]
    public void Multiple_references_require_review_and_preserve_each_candidate(string text)
    {
        var result = Classify(text);
        Assert.Null(result.DetectedAwardNumber); Assert.True(result.RequiresReview);
        Assert.Equal(2, result.Alternatives.Count(x => x.Kind == "awardNumber"));
        Assert.NotEmpty(result.Evidence);
    }

    [Fact]
    public void Only_explicit_award_date_and_village_are_extracted()
    {
        var result = Classify("Award No. 42 / 2010 – 11 dated 09 December 2010\nVillage: Fictional Estate\nNotification dated 01.01.2000");
        Assert.Equal("42/2010-11", result.DetectedAwardNumber);
        Assert.Equal(new DateOnly(2010, 12, 9), result.DetectedAwardDate);
        Assert.Equal("Fictional Estate", result.DetectedVillage);
        Assert.Contains(result.Evidence, x => x.Field == "awardDate" && x.SourceText.Contains("dated"));
        Assert.Null(Classify("Award No. 42/2010-11\nNotification dated 01.01.2000").DetectedAwardDate);
    }

    [Theory]
    [InlineData("Award No. 42/2010-11/1")]
    [InlineData("Award No. 42/2010-11A")]
    [InlineData("Award No. 4O/2010-11")]
    [InlineData("Award No. 42/201-11")]
    public void Unsafe_identity_is_not_partially_extracted_or_digit_corrected(string text)
    {
        Assert.Null(Classify(text).DetectedAwardNumber);
    }

    [Fact]
    public void Conflicting_dates_villages_or_role_headings_require_review()
    {
        Assert.True(Classify("Award No. 42/2010-11\nAward Date: 01.01.2010\nAward Date: 02.02.2010").RequiresReview);
        Assert.True(Classify("Award No. 42/2010-11\nVillage: Estate One\nVillage: Estate Two").RequiresReview);
        Assert.Equal("Unknown", Classify("NM\nStatement A\nAward No. 42/2010-11").DetectedRole);
    }

    private static CoreClassification Classify(string text) => new CoreDocumentClassifier().Classify([new(1, text)]);
}

public sealed class CoreDocumentIntakeTests
{
    [Fact]
    public async Task Stage_proposes_new_award_without_creating_or_linking_canonical_records_and_retains_source()
    {
        await using var f = await Fixture.Create();
        var intake = await f.Stage("Award No. 42/2010-11\nAward Date: 09.12.2010\nVillage: Fictional Estate");
        Assert.Equal("ProposedNewAward", intake.MatchState); Assert.Equal("NeedsConfirmation", intake.Status);
        Assert.Empty(await f.Db.Awards.ToListAsync()); Assert.Empty(await f.Db.DocumentAwards.ToListAsync());
        Assert.Empty(await f.Db.DocumentVillages.ToListAsync());
        var staged = await f.Db.CoreDocumentIntakes.SingleAsync();
        Assert.Contains("42/2010-11", staged.SourcePagesJson); Assert.Contains("awardNumber", staged.ProposalJson);
        var source = await f.Db.Documents.SingleAsync(); Assert.Equal("Staged", source.Status); Assert.Equal("Unclassified Core PDF", source.DocumentType);
        Assert.Equal(intake.DocumentId, staged.DocumentId);
        Assert.Equal(f.LastBytes, f.Storage.Get(source.StoragePath));
    }

    [Fact]
    public async Task Exact_existing_award_and_safe_year_variants_match_only_in_selected_village()
    {
        await using var f = await Fixture.Create(); var award = await f.Award("42 / 2010-2011");
        var result = await f.Stage("NM of Award No. 42/2010–11");
        Assert.Equal("MatchedExistingAward", result.MatchState); Assert.Equal(award.Id, result.MatchedAwardId);
        var confirmed = await f.Service.ConfirmAsync(result.IntakeId, new("NM", award.Id), default);
        Assert.Equal(award.Id, confirmed.ConfirmedAwardId); Assert.Equal("NM", (await f.Db.DocumentAwards.SingleAsync()).CoreDocumentRole);
        Assert.Single(await f.Db.Awards.ToListAsync());
    }

    [Fact]
    public async Task Multiple_existing_normalized_identities_are_never_merged_or_created_again()
    {
        await using var f = await Fixture.Create(); await f.Award("42/2010-11"); await f.Award("42/2010-2011");
        var result = await f.Stage("Award No. 42/2010-11");
        Assert.Equal("NeedsOfficerReview", result.MatchState); Assert.Null(result.MatchedAwardId);
        Assert.Equal(2, result.Alternatives.Count(x => x.Kind == "existingAward"));
        var error = await Assert.ThrowsAsync<CoreIntakeException>(() => f.Service.ConfirmAsync(result.IntakeId, new("Award", CreateAward: new("42/2010-11", Confirmed: true)), default));
        Assert.Equal(409, error.StatusCode); Assert.Equal(2, await f.Db.Awards.CountAsync()); Assert.Empty(await f.Db.DocumentAwards.ToListAsync());
    }

    [Fact]
    public async Task Officer_can_confirm_creation_and_retry_without_duplicates()
    {
        await using var f = await Fixture.Create(); var intake = await f.Stage("Award No. 42/2010-11");
        var decision = new CoreIntakeConfirmation("Award", CreateAward: new("42/2010-11", new(2010, 12, 9), "Main", true));
        var confirmed = await f.Service.ConfirmAsync(intake.IntakeId, decision, default);
        var retry = await f.Service.ConfirmAsync(intake.IntakeId, decision, default);
        Assert.Equal(confirmed.ConfirmedAwardId, retry.ConfirmedAwardId); Assert.Equal("Confirmed", retry.Status);
        Assert.Single(await f.Db.Awards.ToListAsync()); Assert.Single(await f.Db.DocumentAwards.ToListAsync());
        Assert.Single(await f.Db.AwardVillages.ToListAsync()); Assert.Single(await f.Db.DocumentVillages.ToListAsync());
        Assert.Contains(await f.Db.AuditLogs.ToListAsync(), x => x.Action == "CoreDocumentIntakeConfirmed");
        Assert.Contains("42/2010-11", (await f.Db.CoreDocumentIntakes.SingleAsync()).SourcePagesJson);
        Assert.Equal(new DateOnly(2010, 12, 9), (await f.Db.Awards.SingleAsync()).AwardDate);
    }

    [Fact]
    public async Task New_proposal_rechecks_existing_awards_at_confirmation_and_links_instead_of_duplicating()
    {
        await using var f = await Fixture.Create(); var intake = await f.Stage("Award No. 42/2010-11");
        var existing = await f.Award("42/2010-2011");
        var result = await f.Service.ConfirmAsync(intake.IntakeId, new("Award", CreateAward: new("42/2010-11", Confirmed: true)), default);
        Assert.Equal(existing.Id, result.ConfirmedAwardId); Assert.Single(await f.Db.Awards.ToListAsync());
    }

    [Fact]
    public async Task Conflicting_creation_date_does_not_silently_reuse_existing_award()
    {
        await using var f = await Fixture.Create(); var award = await f.Award("42/2010-11", date: new(2010, 12, 9));
        var intake = await f.Stage("Award No. 42/2010-11\nAward Date: 10.12.2010");
        Assert.Equal("NeedsOfficerReview", intake.MatchState);
        var error = await Assert.ThrowsAsync<CoreIntakeException>(() => f.Service.ConfirmAsync(intake.IntakeId,
            new("Award", CreateAward: new("42/2010-11", new(2010, 12, 10), Confirmed: true)), default));
        Assert.Equal(409, error.StatusCode); Assert.Empty(await f.Db.DocumentAwards.ToListAsync());
        Assert.Equal(new DateOnly(2010, 12, 9), (await f.Db.Awards.SingleAsync()).AwardDate);
    }

    [Fact]
    public async Task Ambiguous_or_unknown_file_requires_manual_officer_selection()
    {
        await using var f = await Fixture.Create(); var award = await f.Award("42/2010-11");
        var intake = await f.Stage("Unreadable material"); Assert.Equal("NeedsOfficerReview", intake.MatchState);
        await Assert.ThrowsAsync<CoreIntakeException>(() => f.Service.ConfirmAsync(intake.IntakeId, new("Unknown", award.Id), default));
        await Assert.ThrowsAsync<CoreIntakeException>(() => f.Service.ConfirmAsync(intake.IntakeId, new("NM"), default));
        var confirmed = await f.Service.ConfirmAsync(intake.IntakeId, new("StatementA", award.Id), default);
        Assert.Equal("StatementA", confirmed.ConfirmedRole); Assert.Equal("Unknown", confirmed.DetectedRole);
    }

    [Fact]
    public async Task Creation_needs_explicit_confirmation_and_award_role()
    {
        await using var f = await Fixture.Create(); var intake = await f.Stage("NM of Award No. 42/2010-11");
        await Assert.ThrowsAsync<CoreIntakeException>(() => f.Service.ConfirmAsync(intake.IntakeId, new("Award", CreateAward: new("42/2010-11")), default));
        await Assert.ThrowsAsync<CoreIntakeException>(() => f.Service.ConfirmAsync(intake.IntakeId, new("NM", CreateAward: new("42/2010-11", Confirmed: true)), default));
        Assert.Empty(await f.Db.Awards.ToListAsync());
    }

    [Fact]
    public async Task Duplicate_upload_reuses_intake_and_original_but_multiple_distinct_files_per_role_are_supported()
    {
        await using var f = await Fixture.Create(); var award = await f.Award("42/2010-11");
        var first = await f.Stage("NM of Award No. 42/2010-11");
        var duplicate = await f.Stage("NM of Award No. 42/2010-11", "other-name.pdf");
        Assert.Equal(first.IntakeId, duplicate.IntakeId); Assert.True(duplicate.IsDuplicate); Assert.Single(await f.Db.Documents.ToListAsync());
        await f.Service.ConfirmAsync(first.IntakeId, new("NM", award.Id), default);
        var confirmedDuplicate = await f.Stage("NM of Award No. 42/2010-11"); Assert.Equal("Confirmed", confirmedDuplicate.Status);
        var second = await f.Stage("NM of Award No. 42/2010-11\nAdditional schedule");
        await f.Service.ConfirmAsync(second.IntakeId, new("NM", award.Id), default);
        var inventory = Assert.Single(await CoreDocumentInventory.VillageAsync(f.Db, f.Village.Id, default));
        var nm = Assert.Single(inventory.Roles, x => x.Role == "NM"); Assert.Equal(2, nm.Count); Assert.Equal(2, nm.Documents.Count);
        Assert.All(nm.Documents, x => { Assert.Equal("Active", x.Status); Assert.Contains("/content", x.ViewRoute); Assert.EndsWith("?download=true", x.DownloadRoute); });
        Assert.Empty(Assert.Single(inventory.Roles, x => x.Role == "Award").Documents);
    }

    [Fact]
    public async Task Wrong_village_award_selection_is_rejected_and_cannot_match_or_leak_through_duplicate_hash()
    {
        await using var f = await Fixture.Create(); var other = new Village { Name = "Other Estate" }; f.Db.Add(other); await f.Db.SaveChangesAsync();
        var otherAward = await f.Award("42/2010-11", other.Id);
        var intake = await f.Stage("NM of Award No. 42/2010-11");
        Assert.Equal("NeedsOfficerReview", intake.MatchState); Assert.Null(intake.MatchedAwardId);
        await Assert.ThrowsAsync<CoreIntakeException>(() => f.Service.ConfirmAsync(intake.IntakeId, new("NM", otherAward.Id), default));
        var otherIntake = await f.Service.StageAsync(other.Id, new MemoryStream(f.LastBytes), "other.pdf", default);
        Assert.NotEqual(intake.IntakeId, otherIntake.IntakeId); Assert.NotEqual(intake.DocumentId, otherIntake.DocumentId);
        Assert.Empty(await f.Db.DocumentAwards.ToListAsync());
    }

    [Fact]
    public async Task Explicit_different_village_blocks_an_automatic_match()
    {
        await using var f = await Fixture.Create(); await f.Award("42/2010-11");
        var intake = await f.Stage("Award No. 42/2010-11\nVillage: Other Estate");
        Assert.Equal("NeedsOfficerReview", intake.MatchState); Assert.Contains(intake.Alternatives, x => x.Kind == "villageConflict");
    }

    [Fact]
    public async Task Per_award_inventory_does_not_treat_one_awards_nm_as_village_wide_coverage()
    {
        await using var f = await Fixture.Create(); var first = await f.Award("42/2010-11"); var second = await f.Award("43/2010-11");
        var intake = await f.Stage("NM of Award No. 42/2010-11"); await f.Service.ConfirmAsync(intake.IntakeId, new("NM", first.Id), default);
        var inventory = await CoreDocumentInventory.VillageAsync(f.Db, f.Village.Id, default);
        Assert.Single(inventory.Single(x => x.Id == first.Id).Roles.Single(x => x.Role == "NM").Documents);
        Assert.Empty(inventory.Single(x => x.Id == second.Id).Roles.Single(x => x.Role == "NM").Documents);
    }

    [Fact]
    public async Task Existing_manual_duplicate_is_linked_once_and_original_intake_evidence_is_kept()
    {
        await using var f = await Fixture.Create(); var award = await f.Award("42/2010-11"); var intake = await f.Stage("NM of Award No. 42/2010-11");
        var original = await f.Db.Documents.SingleAsync();
        var canonical = new LAC.Domain.Document { OriginalFileName = "manual.pdf", StoragePath = "existing", Sha256Hash = original.Sha256Hash };
        f.Db.Add(new DocumentAward { AwardId = award.Id, Document = canonical, CoreDocumentRole = "NM" }); await f.Db.SaveChangesAsync();
        var confirmed = await f.Service.ConfirmAsync(intake.IntakeId, new("NM", award.Id), default);
        Assert.Equal(canonical.Id, confirmed.ConfirmedDocumentId); Assert.Equal(original.Id, confirmed.DocumentId);
        Assert.Single(await f.Db.DocumentAwards.ToListAsync()); Assert.Equal(f.LastBytes, f.Storage.Get(original.StoragePath));
    }

    [Fact]
    public async Task Service_enforces_upload_view_and_creation_permissions_independently()
    {
        await using var f = await Fixture.Create(); f.Access.Denied.Add(PermissionCodes.AwardCoreDocumentUpload);
        Assert.Equal(403, (await Assert.ThrowsAsync<CoreIntakeException>(() => f.Stage("Award No. 42/2010-11"))).StatusCode);
        f.Access.Denied.Clear(); var intake = await f.Stage("Award No. 42/2010-11");
        f.Access.Denied.Add(PermissionCodes.AwardCreate);
        Assert.Equal(403, (await Assert.ThrowsAsync<CoreIntakeException>(() => f.Service.ConfirmAsync(intake.IntakeId, new("Award", CreateAward: new("42/2010-11", Confirmed: true)), default))).StatusCode);
        f.Access.Denied.Add(PermissionCodes.AwardView);
        Assert.Equal(403, (await Assert.ThrowsAsync<CoreIntakeException>(() => f.Service.GetAsync(intake.IntakeId, default))).StatusCode);
        Assert.Empty(await f.Db.Awards.ToListAsync());
    }

    [Fact]
    public async Task Native_reader_classifies_a_real_pdf_and_bounds_pages_and_retains_readable_evidence()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var pdf = QuestPDF.Fluent.Document.Create(document =>
        {
            for (var i = 0; i < 8; i++) document.Page(page => { page.Content().Column(column =>
            {
                column.Item().Text("Award Number 42/2010-11"); column.Item().Text("Award Date: 09.12.2010"); column.Item().Text("Village: Fictional Estate");
            }); });
        }).GeneratePdf();
        var pages = await new CoreDocumentNativeTextReader().ReadAsync(new MemoryStream(pdf), default);
        Assert.Equal(CoreDocumentNativeTextReader.MaxPages, pages.Count);
        Assert.All(pages, page => Assert.True(page.Text.Length <= CoreDocumentNativeTextReader.MaxCharactersPerPage));
        var parsed = new CoreDocumentClassifier().Classify(pages); Assert.Equal("Award", parsed.DetectedRole);
        Assert.Equal("42/2010-11", parsed.DetectedAwardNumber); Assert.Equal(new DateOnly(2010, 12, 9), parsed.DetectedAwardDate);
        Assert.Contains(parsed.Evidence, x => x.PageNumber == 1 && x.SourceText.Contains("42/2010-11"));
    }

    [Fact]
    public async Task Corrupt_or_scanned_pdf_remains_staged_for_manual_review()
    {
        await using var f = await Fixture.Create(reader: new CoreDocumentNativeTextReader());
        var intake = await f.Stage("not a real PDF structure");
        Assert.Equal("Unknown", intake.DetectedRole); Assert.Equal("NeedsOfficerReview", intake.MatchState);
        Assert.NotEmpty(intake.Evidence); Assert.Single(await f.Db.Documents.ToListAsync()); Assert.Empty(await f.Db.Awards.ToListAsync());
    }

    [Fact]
    public async Task Changed_confirmation_is_rejected_after_success()
    {
        await using var f = await Fixture.Create(); var award = await f.Award("42/2010-11"); var intake = await f.Stage("NM of Award No. 42/2010-11");
        await f.Service.ConfirmAsync(intake.IntakeId, new("NM", award.Id), default);
        Assert.Equal(409, (await Assert.ThrowsAsync<CoreIntakeException>(() => f.Service.ConfirmAsync(intake.IntakeId, new("Award", award.Id), default))).StatusCode);
        Assert.Equal("NM", (await f.Db.DocumentAwards.SingleAsync()).CoreDocumentRole);
    }

    [Fact]
    public async Task Duplicate_pdf_already_linked_to_another_award_requires_review()
    {
        await using var f = await Fixture.Create(); var chosen = await f.Award("42/2010-11"); var other = await f.Award("43/2010-11");
        var intake = await f.Stage("NM of Award No. 42/2010-11"); var source = await f.Db.Documents.SingleAsync();
        f.Db.Add(new DocumentAward { AwardId = other.Id, Document = new() { Sha256Hash = source.Sha256Hash, OriginalFileName = "prior.pdf", StoragePath = "prior" }, CoreDocumentRole = "NM" });
        await f.Db.SaveChangesAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<CoreIntakeException>(() => f.Service.ConfirmAsync(intake.IntakeId, new("NM", chosen.Id), default))).StatusCode);
        Assert.Single(await f.Db.DocumentAwards.ToListAsync()); Assert.Equal("NeedsConfirmation", (await f.Service.GetAsync(intake.IntakeId, default)).Status);
    }

    [Fact]
    public async Task Source_evidence_is_readable_after_confirmation_and_keeps_source_document_identity()
    {
        await using var f = await Fixture.Create(); var intake = await f.Stage("Award No. 42/2010-11");
        await f.Service.ConfirmAsync(intake.IntakeId, new("Award", CreateAward: new("42/2010-11", Confirmed: true)), default);
        var evidence = await f.Service.SourceEvidenceAsync(intake.IntakeId, default);
        Assert.Equal(intake.DocumentId, evidence.DocumentId); Assert.Equal("core-native-v1", evidence.ClassifierVersion);
        Assert.Equal("Award No. 42/2010-11", Assert.Single(evidence.Pages).Text); Assert.Equal(64, evidence.Sha256Hash.Length);
    }

    [Fact]
    public async Task Isolated_postgres_migration_and_concurrent_confirmations_when_explicitly_enabled()
    {
        if (Environment.GetEnvironmentVariable("CORE_INTAKE_POSTGRES_SMOKE") != "1") return;
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(configured));
        var connection = new NpgsqlConnectionStringBuilder(configured);
        Assert.Contains(connection.Host, new[] { "127.0.0.1", "localhost", "::1" });
        var schema = "core_intake_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            connection.SearchPath = schema;
            var options = new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection.ConnectionString, postgres =>
            {
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", schema); postgres.EnableRetryOnFailure(2);
            }).Options;
            await using var setup = new LacDbContext(options); await setup.Database.MigrateAsync();
            Assert.Contains("20261004181412_AddSmartCoreDocumentIntake", await setup.Database.GetAppliedMigrationsAsync());
            var village = new Village { Name = "Concurrent Test Estate", SubDivision = new() { Name = "Intake Test Subdivision", District = new() { Name = "Intake Test District" } } };
            setup.Add(village); await setup.SaveChangesAsync();
            var storage = new MemoryStorage(); var access = new Access(); var user = new User();
            CoreDocumentIntakeService Service(LacDbContext db) => new(db, storage, new TextReader(), new CoreDocumentClassifier(), new CoreIntakeGate(), access, user);
            var staging = Service(setup);
            var first = await staging.StageAsync(village.Id, new MemoryStream(Encoding.UTF8.GetBytes("%PDF-Award No. 42/2010-11")), "first.pdf", default);
            var second = await staging.StageAsync(village.Id, new MemoryStream(Encoding.UTF8.GetBytes("%PDF-Award No. 42/2010-11\nSecond copy")), "second.pdf", default);
            await using var context1 = new LacDbContext(options); await using var context2 = new LacDbContext(options);
            var decision = new CoreIntakeConfirmation("Award", CreateAward: new("42/2010-11", Confirmed: true));
            var confirmations = await Task.WhenAll(Service(context1).ConfirmAsync(first.IntakeId, decision, default), Service(context2).ConfirmAsync(second.IntakeId, decision, default));
            Assert.Equal(confirmations[0].ConfirmedAwardId, confirmations[1].ConfirmedAwardId);
            setup.ChangeTracker.Clear(); Assert.Single(await setup.Awards.ToListAsync()); Assert.Equal(2, await setup.DocumentAwards.CountAsync());
            Assert.Equal(2, (await CoreDocumentInventory.VillageAsync(setup, village.Id, default)).Single().Roles.Single(x => x.Role == "Award").Documents.Count);
            var replay = await Service(context1).ConfirmAsync(first.IntakeId, decision, default); Assert.Equal("Confirmed", replay.Status);
            // Separate gates also exercise the database lock for a simultaneous duplicate upload.
            await using var context3 = new LacDbContext(options); await using var context4 = new LacDbContext(options);
            var duplicateBytes = Encoding.UTF8.GetBytes("%PDF-NM of Award No. 42/2010-11");
            var uploads = await Task.WhenAll(Service(context3).StageAsync(village.Id, new MemoryStream(duplicateBytes), "nm.pdf", default),
                Service(context4).StageAsync(village.Id, new MemoryStream(duplicateBytes), "nm-again.pdf", default));
            Assert.Equal(uploads[0].IntakeId, uploads[1].IntakeId); Assert.Contains(uploads, x => x.IsDuplicate);
            Assert.Equal(3, await setup.CoreDocumentIntakes.CountAsync());
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin); await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public LacDbContext Db { get; } = new(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Village Village { get; } = new() { Name = "Fictional Estate" };
        public MemoryStorage Storage { get; } = new();
        public Access Access { get; } = new();
        public byte[] LastBytes { get; private set; } = [];
        public CoreDocumentIntakeService Service { get; private set; } = null!;
        public static async Task<Fixture> Create(ICoreDocumentTextReader? reader = null)
        {
            var f = new Fixture(); f.Db.Add(f.Village); await f.Db.SaveChangesAsync();
            f.Service = new(f.Db, f.Storage, reader ?? new TextReader(), new CoreDocumentClassifier(), new CoreIntakeGate(), f.Access, new User()); return f;
        }
        public Task<CoreIntakeView> Stage(string text, string fileName = "source.pdf")
        {
            LastBytes = Encoding.UTF8.GetBytes("%PDF-" + text);
            return Service.StageAsync(Village.Id, new MemoryStream(LastBytes), fileName, default);
        }
        public async Task<Award> Award(string number, Guid? villageId = null, DateOnly? date = null)
        {
            var award = new Award { AwardNumber = number, AwardDate = date }; Db.Add(new AwardVillage { VillageId = villageId ?? Village.Id, Award = award }); await Db.SaveChangesAsync(); return award;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class TextReader : ICoreDocumentTextReader
    {
        public async Task<IReadOnlyList<CoreSourcePage>> ReadAsync(Stream source, CancellationToken ct)
        {
            using var reader = new StreamReader(source); return [new(1, (await reader.ReadToEndAsync(ct))[5..])];
        }
    }
    private sealed class MemoryStorage : IDocumentStorage
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> files = new();
        public Task<string> SaveAsync(Stream source, string name, CancellationToken ct) => throw new NotSupportedException();
        public async Task<DocumentStorageWriteResult> SaveAndHashAsync(Stream source, string name, CancellationToken ct)
        {
            using var copy = new MemoryStream(); await source.CopyToAsync(copy, ct); var bytes = copy.ToArray(); var path = Guid.NewGuid().ToString(); files[path] = bytes;
            return new(path, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
        }
        public Task DeleteAsync(string path, CancellationToken ct) { files.TryRemove(path, out _); return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string path, CancellationToken ct) => Task.FromResult<Stream?>(files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes) : null);
        public StorageHealth GetHealth() => new("memory", true, null, null);
        public byte[] Get(string path) => files[path];
    }
    private sealed class Access : IAccessControlService
    {
        public HashSet<string> Denied { get; } = [];
        public Task<bool> CanAsync(string permissionCode, AccessResourceContext? resourceContext = null, CancellationToken cancellationToken = default) => Task.FromResult(!Denied.Contains(permissionCode));
        public Task<IReadOnlyDictionary<string, ScopeMode>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class User : ICurrentUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? Username => "test-officer";
        public string? DisplayName => Username;
        public Guid? DesignationId => null;
        public string? DesignationCode => null;
        public string? DesignationName => null;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public IReadOnlyList<Guid> WorkstreamIds => [];
        public IReadOnlyList<string> WorkstreamCodes => [];
        public IReadOnlyList<Guid> DeskIds => [];
        public IReadOnlyList<string> DeskCodes => [];
        public Guid? PrimaryDeskId => null;
    }
}

public sealed class CoreDocumentIntakeApiTests : IClassFixture<RbacFactory>
{
    private readonly RbacFactory factory;
    public CoreDocumentIntakeApiTests(RbacFactory factory) => this.factory = factory;

    [Fact]
    public async Task Real_pdf_upload_confirm_inventory_and_duplicate_retry_follow_the_public_contract()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass))).StatusCode);
        Guid villageId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var village = new Village { Name = "API Intake Estate" };
            db.Add(village); await db.SaveChangesAsync(); villageId = village.Id;
        }
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var bytes = QuestPDF.Fluent.Document.Create(document => document.Page(page => page.Content().Column(column =>
        {
            column.Item().Text("Main Award Number 71/2014-15"); column.Item().Text("Award Date: 09.12.2014"); column.Item().Text("Village: API Intake Estate");
        }))).GeneratePdf();
        async Task<CoreIntakeView> Upload()
        {
            using var body = new MultipartFormDataContent(); body.Add(new ByteArrayContent(bytes), "file", "award.pdf");
            var response = await client.PostAsync($"/api/villages/{villageId}/core-document-intake", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<CoreIntakeView>())!;
        }
        var intake = await Upload(); Assert.Equal("ProposedNewAward", intake.MatchState); Assert.Equal("Main", intake.DetectedAwardType);
        var response = await client.PostAsJsonAsync($"/api/core-document-intakes/{intake.IntakeId}/confirm",
            new CoreIntakeConfirmation("Award", CreateAward: new("71/2014-15", new(2014, 12, 9), "Main", true)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var confirmed = (await response.Content.ReadFromJsonAsync<CoreIntakeView>())!;
        Assert.Equal("Confirmed", confirmed.Status); Assert.NotNull(confirmed.ConfirmedAwardId);
        var inventoryResponse = await client.GetAsync($"/api/villages/{villageId}/core-records"); Assert.Equal(HttpStatusCode.OK, inventoryResponse.StatusCode);
        var inventory = Assert.Single((await inventoryResponse.Content.ReadFromJsonAsync<List<CoreAwardInventory>>())!);
        var document = Assert.Single(inventory.Roles.Single(x => x.Role == "Award").Documents);
        Assert.Equal(confirmed.ConfirmedDocumentId, document.DocumentId);
        var file = await client.GetAsync(document.ViewRoute); Assert.Equal(HttpStatusCode.OK, file.StatusCode); Assert.Equal(bytes, await file.Content.ReadAsByteArrayAsync());
        var evidence = await client.GetAsync($"/api/core-document-intakes/{intake.IntakeId}/source-evidence"); Assert.Equal(HttpStatusCode.OK, evidence.StatusCode);
        var source = (await evidence.Content.ReadFromJsonAsync<CoreIntakeSourceEvidence>())!; Assert.Contains("71/2014-15", Assert.Single(source.Pages).Text);
        var duplicate = await Upload(); Assert.Equal(intake.IntakeId, duplicate.IntakeId); Assert.True(duplicate.IsDuplicate); Assert.Equal("Confirmed", duplicate.Status);
        var retry = await client.PostAsJsonAsync($"/api/core-document-intakes/{intake.IntakeId}/confirm", new CoreIntakeConfirmation("Award", confirmed.ConfirmedAwardId));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task Intake_endpoints_reject_anonymous_requests()
    {
        using var client = factory.CreateClient(); var id = Guid.NewGuid();
        using var upload = new MultipartFormDataContent(); upload.Add(new ByteArrayContent("%PDF-test"u8.ToArray()), "file", "source.pdf");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/villages/{id}/core-document-intake", upload)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/core-document-intakes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/core-document-intakes/{id}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/villages/{id}/core-document-intakes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/core-document-intakes/{id}/confirm", new CoreIntakeConfirmation("NM", id))).StatusCode);
    }

    [Fact]
    public async Task Batch_handles_invalid_and_review_files_independently_and_original_can_be_downloaded()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass))).StatusCode);
        Guid villageId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var village = new Village { Name = "Batch Test Estate" }; db.Add(village); await db.SaveChangesAsync(); villageId = village.Id;
        }
        using var upload = new MultipartFormDataContent();
        var bytes = "%PDF-unreadable original"u8.ToArray();
        upload.Add(new ByteArrayContent(bytes), "files", "review.pdf");
        upload.Add(new ByteArrayContent("invalid"u8.ToArray()), "files", "invalid.pdf");
        var response = await client.PostAsync($"/api/villages/{villageId}/core-document-intake", upload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = payload.RootElement.GetProperty("items"); Assert.Equal(2, items.GetArrayLength());
        var intake = items[0].GetProperty("intake"); Assert.Equal("NeedsOfficerReview", intake.GetProperty("matchState").GetString());
        Assert.Equal(400, items[1].GetProperty("errorStatus").GetInt32());
        var file = await client.GetAsync(intake.GetProperty("downloadRoute").GetString()); Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(bytes, await file.Content.ReadAsByteArrayAsync()); Assert.Contains("review.pdf", file.Content.Headers.ContentDisposition!.ToString());
        var list = await client.GetAsync($"/api/villages/{villageId}/core-document-intakes"); Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }
}
