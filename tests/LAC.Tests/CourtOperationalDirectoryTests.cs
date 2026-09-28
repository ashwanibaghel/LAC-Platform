using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LAC.Tests;

public sealed class CourtOperationalDirectoryTests
{
    private sealed class FixedClock(DateOnly today) : IOfficeClock
    {
        public DateOnly GetCurrentDate() => today;
        public DateTimeOffset GetUtcNow() => new(2026, 9, 28, 4, 0, 0, TimeSpan.Zero);
    }

    private static async Task<(LacDbContext Db, AppUser User, CourtProjectionService Projection)> SetupAsync(
        ScopeMode courtScope = ScopeMode.All, bool landAccess = false)
    {
        var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new AppUser { Username = "queue-officer", NormalizedUsername = "QUEUE-OFFICER", DisplayName = "Queue Officer" };
        var role = new Role { Code = "COURT_QUEUE", Name = "Court queue" };
        db.AppUsers.Add(user);
        db.Roles.Add(role);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        AddPermission(PermissionCodes.CourtView, courtScope);
        AddPermission(PermissionCodes.ScheduleView, ScopeMode.All);
        if (landAccess)
        {
            AddPermission(PermissionCodes.AwardView, ScopeMode.All);
            AddPermission(PermissionCodes.VillageView, ScopeMode.All);
            AddPermission(PermissionCodes.KhasraView, ScopeMode.All);
        }
        await db.SaveChangesAsync();
        var auth = new CourtAuthorizationService(db, null!, null!, null!);
        return (db, user, new CourtProjectionService(db, auth, new ScheduleAuthorizationService(db, auth), new FixedClock(new DateOnly(2026, 9, 28))));

        void AddPermission(string code, ScopeMode scope)
        {
            var permission = new Permission { Code = code, Name = code, Category = "Court" };
            db.Permissions.Add(permission);
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = scope });
        }
    }

    private static CourtCase AddCase(LacDbContext db, string number, string? status = "Pending",
        DateOnly? nextDate = null, string court = "Delhi High Court", string? caseType = "WP(C)")
    {
        var item = new CourtCase
        {
            CaseNumber = number, CaseTitle = "Case " + number, CourtName = court,
            CurrentStatus = status, CaseType = caseType,
            UpdatedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)
        };
        db.CourtCases.Add(item);
        if (nextDate.HasValue)
            db.CourtProceedings.Add(new CourtProceeding
            {
                CourtCaseId = item.Id, NextDate = nextDate, SourceKind = "LegacyRegisterNDOH",
                CreatedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)
            });
        return item;
    }

    [Fact]
    public async Task DefaultQueue_ScopesDateSortKeysToPendingBuckets_BeforePagination()
    {
        var (db, user, projection) = await SetupAsync();
        using (db)
        {
            var older = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
            var newer = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

            AddCase(db, "UP-LATER", nextDate: new DateOnly(2026, 10, 2));
            AddCase(db, "UP-EARLIER", nextDate: new DateOnly(2026, 9, 29));
            AddCase(db, "OVER-OLDER", nextDate: new DateOnly(2026, 9, 26));
            AddCase(db, "OVER-NEWER", nextDate: new DateOnly(2026, 9, 27));
            AddCase(db, "NO-OLDER").UpdatedAt = older;
            AddCase(db, "NO-NEWER").UpdatedAt = newer;
            AddCase(db, "ATT-OLDER", "Stay Granted", new DateOnly(2026, 10, 1)).UpdatedAt = older;
            AddCase(db, "ATT-NEWER", "Stay Granted", new DateOnly(2026, 10, 10)).UpdatedAt = newer;
            AddCase(db, "DIS-OLDER", "Disposed", new DateOnly(2026, 9, 27)).UpdatedAt = older;
            AddCase(db, "DIS-NEWER", "Disposed", new DateOnly(2026, 9, 26)).UpdatedAt = newer;
            await db.SaveChangesAsync();

            var expected = new[] { "UP-EARLIER", "UP-LATER", "OVER-NEWER", "OVER-OLDER",
                "NO-NEWER", "NO-OLDER", "ATT-NEWER", "ATT-OLDER", "DIS-NEWER", "DIS-OLDER" };
            var all = await projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), user.Id);
            Assert.Equal(expected, all.Items.Select(x => x.CaseNumber));
            var pages = new List<string>();
            for (var page = 1; page <= 5; page++)
                pages.AddRange((await projection.GetCourtCasesAsync(new CourtCaseFilterQuery(Page: page, PageSize: 2), user.Id))
                    .Items.Select(x => x.CaseNumber));
            Assert.Equal(expected, pages);
        }
    }

    [Fact]
    public async Task DefaultQueue_UsesCourtNameCaseNumberAndIdToBreakActivityTies()
    {
        var (db, user, projection) = await SetupAsync();
        using (db)
        {
            var firstId = AddCase(db, "A", court: "A Court");
            var secondId = AddCase(db, "A", court: "A Court");
            AddCase(db, "B", court: "A Court");
            AddCase(db, "A", court: "B Court");
            await db.SaveChangesAsync();

            var expectedIds = new[] { firstId.Id, secondId.Id }.Order().ToArray();
            var all = await projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), user.Id);
            Assert.Equal(new[] { "A Court", "A Court", "A Court", "B Court" }, all.Items.Select(x => x.CourtName));
            Assert.Equal(new[] { "A", "A", "B", "A" }, all.Items.Select(x => x.CaseNumber));
            Assert.Equal(expectedIds, all.Items.Take(2).Select(x => x.Id));
            var pages = new List<Guid>();
            for (var page = 1; page <= 4; page++)
                pages.AddRange((await projection.GetCourtCasesAsync(new CourtCaseFilterQuery(Page: page, PageSize: 1), user.Id))
                    .Items.Select(x => x.Id));
            Assert.Equal(all.Items.Select(x => x.Id), pages);
        }
    }

    [Fact]
    public async Task DefaultQueue_UsesAuthoritativeNdohBeforeStablePagination_NotCalendar()
    {
        var (db, user, projection) = await SetupAsync();
        using (db)
        {
            var tomorrowB = AddCase(db, "B-2026", nextDate: new DateOnly(2026, 9, 29));
            var tomorrowA = AddCase(db, "A-2026", nextDate: new DateOnly(2026, 9, 29));
            AddCase(db, "C-2026", " pending ", new DateOnly(2026, 9, 29));
            var today = AddCase(db, "TODAY", nextDate: new DateOnly(2026, 9, 28));
            AddCase(db, "SEP30", nextDate: new DateOnly(2026, 9, 30));
            AddCase(db, "SEP27", nextDate: new DateOnly(2026, 9, 27));
            AddCase(db, "SEP26", nextDate: new DateOnly(2026, 9, 26));
            AddCase(db, "NO-NDOH");
            AddCase(db, "ATTENTION", "Stay Granted", new DateOnly(2026, 9, 28));
            AddCase(db, "DISPOSED", "Disposed", new DateOnly(2026, 10, 1));
            db.CourtProceedings.Add(new CourtProceeding
            {
                CourtCaseId = tomorrowB.Id, ProceedingDate = new DateOnly(2026, 9, 20),
                NextDate = new DateOnly(2026, 10, 5), CreatedAt = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero)
            });
            var workstream = new Workstream { Code = WorkstreamCodes.CourtReferences, Name = "Court References" };
            db.Workstreams.Add(workstream);
            db.ScheduledEvents.Add(new ScheduledEvent
            {
                CourtCaseId = today.Id, WorkstreamId = workstream.Id, CreatedByUserId = user.Id,
                Origin = ScheduledEventOrigin.CourtProceeding, ScheduledDate = new DateOnly(2026, 9, 1), Title = "Calendar projection"
            });
            await db.SaveChangesAsync();

            var all = await projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), user.Id);
            Assert.Equal(new[] { "TODAY", "A-2026", "C-2026", "SEP30", "B-2026", "SEP27", "SEP26", "NO-NDOH", "ATTENTION", "DISPOSED" },
                all.Items.Select(x => x.CaseNumber));
            Assert.Equal(new[] { "Today", "Upcoming", "Upcoming", "Upcoming", "Upcoming", "Overdue", "Overdue", "NoNdoh", "Attention", "Disposed" },
                all.Items.Select(x => x.QueueState));
            Assert.Equal(new DateOnly(2026, 9, 28), all.Items[0].OperationalNdoh);
            Assert.Equal(new DateOnly(2026, 9, 1), all.Items[0].ActiveScheduleNextDate);
            Assert.Equal(new DateOnly(2026, 10, 5), all.Items[4].OperationalNdoh);
            Assert.Equal(new DateOnly(2026, 9, 1), (await db.ScheduledEvents.SingleAsync()).ScheduledDate);
            var pages = new List<string>();
            for (var page = 1; page <= 5; page++)
                pages.AddRange((await projection.GetCourtCasesAsync(new CourtCaseFilterQuery(Page: page, PageSize: 2), user.Id))
                    .Items.Select(x => x.CaseNumber));
            Assert.Equal(all.Items.Select(x => x.CaseNumber), pages);
        }
    }

    [Fact]
    public async Task QuickFilters_UseOfficeDateAndPendingOnly_WithBoundaryDates()
    {
        var (db, user, projection) = await SetupAsync();
        using (db)
        {
            foreach (var day in new[] { 26, 27, 28, 29, 30 }) AddCase(db, $"SEP{day}", nextDate: new DateOnly(2026, 9, day));
            AddCase(db, "OCT1", nextDate: new DateOnly(2026, 10, 1));
            AddCase(db, "OCT4", nextDate: new DateOnly(2026, 10, 4));
            AddCase(db, "OCT5", nextDate: new DateOnly(2026, 10, 5));
            AddCase(db, "NONE");
            AddCase(db, "CLOSED", "Disposed", new DateOnly(2026, 9, 28));
            await db.SaveChangesAsync();
            async Task<string[]> Find(string filter) => (await projection.GetCourtCasesAsync(new CourtCaseFilterQuery(NdohFilter: filter), user.Id))
                .Items.Select(x => x.CaseNumber).ToArray();
            Assert.Equal(["SEP28"], await Find("Today"));
            Assert.Equal(["SEP29"], await Find("Tomorrow"));
            Assert.Equal(["SEP28", "SEP29", "SEP30", "OCT1", "OCT4"], await Find("Next7Days"));
            Assert.Equal(["SEP28", "SEP29", "SEP30", "OCT1", "OCT4"], await Find("ThisWeek"));
            Assert.Equal(["SEP28", "SEP29", "SEP30", "SEP27", "SEP26"], await Find("ThisMonth"));
            Assert.Equal(["SEP28", "SEP29", "SEP30", "OCT1", "OCT4", "OCT5"], await Find("Upcoming"));
            Assert.Equal(["SEP27", "SEP26"], await Find("Overdue"));
            Assert.Equal(["NONE"], await Find("NoNdoh"));
            Assert.Equal(["SEP29", "SEP30"], (await projection.GetCourtCasesAsync(new CourtCaseFilterQuery(
                NdohFilter: "CustomRange", NdohFrom: new DateOnly(2026, 9, 29), NdohTo: new DateOnly(2026, 9, 30)), user.Id))
                .Items.Select(x => x.CaseNumber));
            var auth = new CourtAuthorizationService(db, null!, null!, null!);
            var sundayProjection = new CourtProjectionService(db, auth, new ScheduleAuthorizationService(db, auth),
                new FixedClock(new DateOnly(2026, 10, 4)));
            Assert.Equal(["OCT4", "OCT1", "SEP30", "SEP29", "SEP28"],
                (await sundayProjection.GetCourtCasesAsync(new CourtCaseFilterQuery(NdohFilter: "ThisWeek"), user.Id))
                .Items.Select(x => x.CaseNumber));
            Assert.Equal(["OCT4", "OCT5", "OCT1"],
                (await sundayProjection.GetCourtCasesAsync(new CourtCaseFilterQuery(NdohFilter: "ThisMonth"), user.Id))
                .Items.Select(x => x.CaseNumber));
            await Assert.ThrowsAsync<CourtWorkflowException>(() => projection.GetCourtCasesAsync(new CourtCaseFilterQuery(
                NdohFilter: "CustomRange", NdohFrom: new DateOnly(2026, 10, 1), NdohTo: new DateOnly(2026, 9, 1)), user.Id));
        }
    }

    [Fact]
    public async Task SearchAndFilters_UseCommittedSourceOnly_AndAuthorizedLinkedLand()
    {
        var (db, user, projection) = await SetupAsync(landAccess: true);
        using (db)
        {
            var imported = AddCase(db, "WP(C) 3352/2024", court: "Dwarka Court", caseType: "WP(C)");
            var linked = AddCase(db, "LINKED-1", court: "Supreme Court", caseType: "SLP");
            db.CourtCaseRepresentatives.Add(new CourtCaseRepresentative { CourtCaseId = imported.Id, DisplayName = "Advocate Kumar" });
            var batch = new CourtImportBatch { SourceDocument = new Document { OriginalFileName = "source.xlsx", StoragePath = "memory" }, CreatedByUserId = user.Id, Status = CourtImportBatchStatus.Parsed };
            db.CourtImportBatches.Add(batch);
            db.CourtImportRows.Add(new CourtImportRow
            {
                BatchId = batch.Id, SourceRowNumber = 3, CommittedCourtCaseId = imported.Id,
                CommitStatus = CourtImportCommitStatus.Committed, RawVillage = "Pochanpur", RawAwardNumber = "AW-77",
                RawDirections = "File counter affidavit", RawBriefFacts = "Acquisition challenge",
                LastOrderLinkState = "ValidHttpUrl"
            });
            db.CourtImportRows.Add(new CourtImportRow
            {
                BatchId = batch.Id, SourceRowNumber = 4, CommitStatus = CourtImportCommitStatus.NotCommitted,
                RawVillage = "Secret staging", RawDirections = "Uncommitted secret", LastOrderLinkState = "NeedsReview"
            });
            var village = new Village { Name = "Bamnoli" };
            var khasra = new Khasra { VillageId = village.Id, DisplayNumber = "12", NormalizedNumber = "12" };
            var award = new Award { AwardNumber = "LINK-AWARD" };
            db.AddRange(village, khasra, award);
            db.Set<CourtCaseKhasra>().Add(new CourtCaseKhasra { CourtCaseId = linked.Id, KhasraId = khasra.Id });
            db.Set<CourtCaseAward>().Add(new CourtCaseAward { CourtCaseId = linked.Id, AwardId = award.Id });
            await db.SaveChangesAsync();
            async Task<string[]> Find(CourtCaseFilterQuery query) => (await projection.GetCourtCasesAsync(query, user.Id)).Items.Select(x => x.CaseNumber).ToArray();
            Assert.Equal([imported.CaseNumber], await Find(new(Advocate: "KUMAR")));
            Assert.Equal([imported.CaseNumber], await Find(new(Village: "poch")));
            Assert.Equal([imported.CaseNumber], await Find(new(Award: "aw-77")));
            Assert.Equal([imported.CaseNumber], await Find(new(Directions: "counter affidavit")));
            Assert.Equal([imported.CaseNumber], await Find(new(BriefFacts: "challenge")));
            Assert.Equal([imported.CaseNumber], await Find(new(SourceOrderLinkState: "ValidHttpUrl")));
            Assert.Empty(await Find(new(SourceOrderLinkState: "NeedsReview")));
            Assert.Empty(await Find(new(Search: "Uncommitted secret")));
            Assert.Equal([linked.CaseNumber], await Find(new(Village: "Bamnoli")));
            Assert.Equal([linked.CaseNumber], await Find(new(Award: "LINK-AWARD")));
            Assert.Equal([imported.CaseNumber], await Find(new(CaseNumber: "3352/2024")));
            Assert.Equal([linked.CaseNumber], await Find(new(CaseType: "SLP")));
            Assert.Equal([imported.CaseNumber, linked.CaseNumber], await Find(new(CourtNames: ["Dwarka Court", "Supreme Court"])));
            Assert.Equal(2, (await projection.GetFilterOptionsAsync(user.Id)).CaseTypes!.Count);
        }
    }

    [Fact]
    public async Task MultiStatusMultiCourt_AndAllSourceLinkStates_AreServerFiltered()
    {
        var (db, user, projection) = await SetupAsync();
        using (db)
        {
            var pending = AddCase(db, "PENDING", " pending ", court: "Delhi High Court");
            var disposed = AddCase(db, "DISPOSED", "Disposed", court: "Dwarka Court");
            var attention = AddCase(db, "ATTENTION", "Stay Granted", court: "Supreme Court");
            var batch = new CourtImportBatch { SourceDocument = new Document { OriginalFileName = "links.xlsx", StoragePath = "memory" }, CreatedByUserId = user.Id, Status = CourtImportBatchStatus.Parsed };
            db.CourtImportBatches.Add(batch);
            db.CourtImportRows.AddRange(
                new CourtImportRow { BatchId = batch.Id, SourceRowNumber = 3, CommittedCourtCaseId = pending.Id, CommitStatus = CourtImportCommitStatus.Committed, LastOrderLinkState = "ValidHttpUrl" },
                new CourtImportRow { BatchId = batch.Id, SourceRowNumber = 4, CommittedCourtCaseId = disposed.Id, CommitStatus = CourtImportCommitStatus.Committed, LastOrderLinkState = "Missing" },
                new CourtImportRow { BatchId = batch.Id, SourceRowNumber = 5, CommittedCourtCaseId = attention.Id, CommitStatus = CourtImportCommitStatus.Committed, LastOrderLinkState = "NeedsReview" });
            await db.SaveChangesAsync();
            Assert.Equal(["PENDING", "DISPOSED"], (await projection.GetCourtCasesAsync(
                new CourtCaseFilterQuery(Statuses: ["Pending", "Disposed"]), user.Id)).Items.Select(x => x.CaseNumber));
            Assert.Equal(["PENDING", "ATTENTION"], (await projection.GetCourtCasesAsync(
                new CourtCaseFilterQuery(CourtNames: ["Delhi High Court", "Supreme Court"]), user.Id)).Items.Select(x => x.CaseNumber));
            foreach (var (state, number) in new[] { ("ValidHttpUrl", "PENDING"), ("Missing", "DISPOSED"), ("NeedsReview", "ATTENTION") })
                Assert.Equal(number, Assert.Single((await projection.GetCourtCasesAsync(
                    new CourtCaseFilterQuery(SourceOrderLinkState: state), user.Id)).Items).CaseNumber);
            Assert.Equal(3, (await projection.GetCourtCasesAsync(new CourtCaseFilterQuery(SourceOrderLinkState: "Any"), user.Id)).TotalCount);
        }
    }

    [Fact]
    public async Task AssignedScopeAndLinkedLandPermission_FailClosedForSearchAndOptions()
    {
        var (db, user, projection) = await SetupAsync(ScopeMode.Assigned);
        using (db)
        {
            var workstream = new Workstream { Code = WorkstreamCodes.CourtReferences, Name = "Court References" };
            var mine = new OfficeDesk { WorkstreamId = workstream.Id, Name = "My desk" };
            var other = new OfficeDesk { WorkstreamId = workstream.Id, Name = "Other desk" };
            db.AddRange(workstream, mine, other);
            db.UserDeskMemberships.Add(new UserDeskMembership { UserId = user.Id, OfficeDeskId = mine.Id, IsActive = true });
            var visible = AddCase(db, "VISIBLE", court: "Delhi High Court", caseType: "WP(C)");
            visible.ResponsibleOfficeDeskId = mine.Id;
            visible.AssignedUserId = user.Id;
            var hidden = AddCase(db, "HIDDEN", court: "Supreme Court", caseType: "SLP");
            hidden.ResponsibleOfficeDeskId = other.Id;
            var hiddenOfficer = new AppUser { Username = "hidden-officer", NormalizedUsername = "HIDDEN-OFFICER", DisplayName = "Hidden Officer" };
            db.AppUsers.Add(hiddenOfficer);
            hidden.AssignedUserId = hiddenOfficer.Id;
            var village = new Village { Name = "Restricted Village" };
            var khasra = new Khasra { VillageId = village.Id, DisplayNumber = "1", NormalizedNumber = "1" };
            var award = new Award { AwardNumber = "RESTRICTED AWARD" };
            db.AddRange(village, khasra, award);
            db.Set<CourtCaseKhasra>().Add(new CourtCaseKhasra { CourtCaseId = visible.Id, KhasraId = khasra.Id });
            db.Set<CourtCaseAward>().Add(new CourtCaseAward { CourtCaseId = visible.Id, AwardId = award.Id });
            await db.SaveChangesAsync();
            Assert.Equal(["VISIBLE"], (await projection.GetCourtCasesAsync(new(), user.Id)).Items.Select(x => x.CaseNumber));
            Assert.Empty((await projection.GetCourtCasesAsync(new(Village: "Restricted Village"), user.Id)).Items);
            Assert.Empty((await projection.GetCourtCasesAsync(new(Award: "RESTRICTED AWARD"), user.Id)).Items);
            Assert.Empty((await projection.GetCourtCasesAsync(new(Search: "HIDDEN"), user.Id)).Items);
            var options = await projection.GetFilterOptionsAsync(user.Id);
            Assert.Equal(["Delhi High Court"], options.CourtNames);
            Assert.Equal(["WP(C)"], options.CaseTypes);
            Assert.Equal([user.Id], options.DirectoryOfficers!.Select(x => x.Id));
        }
    }

    [Fact]
    public async Task IsolatedPostgres_QueueQueryTranslates_WhenConfigured()
    {
        var connection = Environment.GetEnvironmentVariable("COURT_DIRECTORY_POSTGRES_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection).Options);
        await db.Database.MigrateAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new AppUser { Username = "pg_queue_" + suffix, NormalizedUsername = "PG_QUEUE_" + suffix.ToUpperInvariant(), DisplayName = "PG Queue" };
        var role = new Role { Code = "PG_COURT_QUEUE_" + suffix, Name = "PG Court queue" };
        db.AddRange(user, role);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        var permission = await db.Permissions.SingleAsync(x => x.Code == PermissionCodes.CourtView);
        db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = ScopeMode.All });
        var todayCase = AddCase(db, "PG-TODAY-" + suffix, nextDate: new DateOnly(2026, 9, 28));
        AddCase(db, "PG-OVERDUE-" + suffix, nextDate: new DateOnly(2026, 9, 27));
        db.CourtCaseRepresentatives.Add(new CourtCaseRepresentative { CourtCaseId = todayCase.Id, DisplayName = "PG Advocate " + suffix });
        var batch = new CourtImportBatch { SourceDocument = new Document { OriginalFileName = "query-smoke.xlsx", StoragePath = "temp" }, CreatedByUserId = user.Id, Status = CourtImportBatchStatus.Parsed };
        db.CourtImportBatches.Add(batch);
        db.CourtImportRows.Add(new CourtImportRow
        {
            BatchId = batch.Id, SourceRowNumber = 3, CommittedCourtCaseId = todayCase.Id,
            CommitStatus = CourtImportCommitStatus.Committed, RawVillage = "PG Village " + suffix,
            RawAwardNumber = "PG Award " + suffix, RawDirections = "PG Direction " + suffix,
            RawBriefFacts = "PG Facts " + suffix, LastOrderLinkState = "ValidHttpUrl"
        });
        await db.SaveChangesAsync();
        var auth = new CourtAuthorizationService(db, null!, null!, null!);
        var projection = new CourtProjectionService(db, auth, null!, new FixedClock(new DateOnly(2026, 9, 28)));
        var page = await projection.GetCourtCasesAsync(new CourtCaseFilterQuery(CaseNumber: suffix, PageSize: 1), user.Id);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(todayCase.CaseNumber, Assert.Single(page.Items).CaseNumber);
        Assert.Equal("PG-OVERDUE-" + suffix, Assert.Single((await projection.GetCourtCasesAsync(
            new CourtCaseFilterQuery(CaseNumber: suffix, NdohFilter: "Overdue"), user.Id)).Items).CaseNumber);
        Assert.Equal(todayCase.CaseNumber, Assert.Single((await projection.GetCourtCasesAsync(
            new CourtCaseFilterQuery(CaseNumber: suffix, NdohFilter: "Today"), user.Id)).Items).CaseNumber);
        foreach (var query in new[]
        {
            new CourtCaseFilterQuery(Advocate: suffix), new CourtCaseFilterQuery(Village: suffix),
            new CourtCaseFilterQuery(Award: suffix), new CourtCaseFilterQuery(Directions: suffix),
            new CourtCaseFilterQuery(BriefFacts: suffix), new CourtCaseFilterQuery(SourceOrderLinkState: "ValidHttpUrl", CaseNumber: suffix),
            new CourtCaseFilterQuery(Search: "PG Facts " + suffix)
        })
            Assert.Equal(todayCase.CaseNumber, Assert.Single((await projection.GetCourtCasesAsync(query, user.Id)).Items).CaseNumber);
        var options = await projection.GetFilterOptionsAsync(user.Id);
        Assert.Contains("Delhi High Court", options.CourtNames);
        Assert.Contains("WP(C)", options.CaseTypes!);
        Assert.Empty(options.DirectoryOfficers!);
    }
}
