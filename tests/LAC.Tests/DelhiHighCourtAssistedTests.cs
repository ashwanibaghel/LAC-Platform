using System.Diagnostics;
using System.Net;
using System.Text;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LAC.Tests;

public sealed class DelhiHighCourtAssistedTests
{
    private const string StatusForm = """
        <html><select id='case_type'><option value='W.P.(C)'>W.P.(C)</option><option value='LPA'>LPA</option></select>
        <span id='captcha-code'>TEST7</span><input id='captchaInput' name='captchaInput'>
        <script>data: { "_token": "fixture-csrf", "captchaInput": captchaInput }</script></html>
        """;
    private const string OrderForm = """
        <html><form id='search1' method='post' action='https://delhihighcourt.nic.in/app/case-number'>
        <input type='hidden' name='_token' value='fixture-order-csrf'>
        <select id='case_type'><option value='CW'>W.P.(C)</option><option value='LPA'>LPA</option></select>
        <span id='captcha-code'>TEST8</span><input id='captchaInput' name='captchaInput'></form></html>
        """;
    private const string StatusResult = """
        {"data":[{"ctype":"<b>W.P.(C) 7003/2026 [Pending]</b>","pet":"Petitioner Vs Respondent","orderdate":"07.10.2026 / Court No. 23"}]}
        """;
    private const string OrderResult = """
        <table id='s_judgeTable'><thead><tr><th>S.No.</th><th>Case No.</th><th>Date of Judgment/Order</th><th>Party</th><th>Corrigendum</th><th>Date of Uploading</th><th>Remark</th></tr></thead>
        <tbody><tr><td>1</td><td>W.P.(C) 7003/2026</td><td><a href='/app/showlogo/order.pdf'>25.09.2026</a></td><td>Parties</td><td></td><td>26.09.2026</td><td>Order</td></tr></tbody></table>
        """;

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public string? SubmittedHumanAnswer { get; private set; }
        public string? SubmittedOrderForm { get; private set; }
        public int StatusLookups;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");
            string body;
            if (request.Method == HttpMethod.Get && path.EndsWith("get-case-type-status") && request.RequestUri.Query == "")
                body = StatusForm;
            else if (request.Method == HttpMethod.Get && path.EndsWith("case-number")) body = OrderForm;
            else if (request.Method == HttpMethod.Post && path.EndsWith("validateCaptcha"))
            {
                var submitted = await request.Content!.ReadAsStringAsync(ct);
                SubmittedHumanAnswer = Uri.UnescapeDataString(submitted.Split("captchaInput=")[1]);
                body = SubmittedHumanAnswer == "typed-by-officer" ? "{\"success\":true}" : "{\"success\":false}";
            }
            else if (request.Method == HttpMethod.Get && path.EndsWith("get-case-type-status"))
                body = ++StatusLookups <= 2 ? StatusResult : "{\"error\":\"captcha required\"}";
            else if (request.Method == HttpMethod.Post && path.EndsWith("case-number"))
            {
                SubmittedOrderForm = await request.Content!.ReadAsStringAsync(ct);
                body = OrderResult;
            }
            else return new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") };
        }
    }

    private sealed class Clock : IOfficeClock
    {
        public DateOnly GetCurrentDate() => new(2026, 9, 29);
        public DateTimeOffset GetUtcNow() => new(2026, 9, 29, 11, 0, 0, TimeSpan.Zero);
    }

    private sealed class Lifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    [Fact]
    public void CurrentOfficialForms_RequireTwoIndependentExactOptionMaps()
    {
        var status = DelhiHighCourtAssistedForms.ParseForm(StatusForm, false);
        var orders = DelhiHighCourtAssistedForms.ParseForm(OrderForm, true);
        const string identity = "delhihighcourt|wpc|7003|2026";
        Assert.Equal("W.P.(C)", DelhiHighCourtAssistedForms.ExactCaseTypeValue(identity, status, false));
        Assert.Equal("CW", DelhiHighCourtAssistedForms.ExactCaseTypeValue(identity, orders, true));
        Assert.Null(DelhiHighCourtAssistedForms.ExactCaseTypeValue("delhihighcourt|unknown|1|2026", status, false));
        Assert.Equal("TEST7", status.VisibleChallenge);
        Assert.Equal("TEST8", orders.VisibleChallenge);
        Assert.NotEqual(status.Csrf, orders.Csrf);
    }

    [Fact]
    public async Task HumanOnlySameSession_SequentialResultsThenCaptchaRequired()
    {
        var fake = new FakeHandler();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["CourtSync:DelhiHighCourt:AssistedDelayMs"] = "1" }).Build();
        await using var session = new DelhiHighCourtAssistedSession(config, _ => fake);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SearchStatusAsync("W.P.(C)", "7003", "2026", default));
        await session.LoadFormAsync(false, default);
        Assert.False(await session.ValidateHumanAnswerAsync("wrong-human-entry", false, default));
        Assert.True(await session.ValidateHumanAnswerAsync("typed-by-officer", false, default));
        Assert.Equal("typed-by-officer", fake.SubmittedHumanAnswer);
        Assert.Single(DelhiHighCourtAssistedForms.ParseStatusRows(await session.SearchStatusAsync("W.P.(C)", "7003", "2026", default)));
        Assert.Single(DelhiHighCourtAssistedForms.ParseStatusRows(await session.SearchStatusAsync("W.P.(C)", "7004", "2026", default)));
        Assert.True(DelhiHighCourtAssistedForms.CaptchaRequired(
            await session.SearchStatusAsync("W.P.(C)", "7005", "2026", default)));
        Assert.Equal(3, fake.StatusLookups);
        Assert.Equal(2, fake.Requests.Count(x => x == "POST /app/validateCaptcha"));
        Assert.DoesNotContain(fake.Requests, x => x.Contains("typed-by-officer", StringComparison.Ordinal));
    }

    [Fact]
    public void ParsedOfficialRows_RetainRawFields_RejectExternalOrderLink()
    {
        var row = Assert.Single(DelhiHighCourtAssistedForms.ParseStatusRows(StatusResult));
        Assert.Equal("Pending", row.RawStatus);
        Assert.Equal(new DateOnly(2026, 10, 7), row.ListingDate);
        Assert.Equal("23", row.RawCourtNumber);
        var order = Assert.Single(DelhiHighCourtAssistedForms.ParseOrderRows(OrderResult));
        Assert.Equal(new DateOnly(2026, 9, 25), order.OrderDate);
        Assert.Equal("https://delhihighcourt.nic.in/app/showlogo/order.pdf", order.OfficialUrl);
        Assert.Throws<InvalidDataException>(() => DelhiHighCourtAssistedForms.ParseOrderRows(
            OrderResult.Replace("/app/showlogo/order.pdf", "https://evil.example/order.pdf")));
        Assert.False(DelhiHighCourtAssistedForms.IsApprovedOfficialUri(new Uri("https://evil.example/app/order.pdf")));
    }

    [Fact]
    public async Task EveryRunHasFreshCookieJar_AndMinimumDelayCannotBeDisabled()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["CourtSync:DelhiHighCourt:AssistedDelayMs"] = "1" }).Build();
        await using var first = new DelhiHighCourtAssistedSession(config, _ => new FakeHandler());
        await using var second = new DelhiHighCourtAssistedSession(config, _ => new FakeHandler());
        Assert.NotSame(first.Cookies, second.Cookies);
        var watch = Stopwatch.StartNew();
        await first.LoadFormAsync(false, default);
        await first.LoadFormAsync(false, default);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(950));
    }

    [Fact]
    public async Task OrderForm_UsesOnlyHumanAnswerOnce_AndNeverDisplayedChallenge()
    {
        var fake = new FakeHandler();
        var config = new ConfigurationBuilder().Build();
        await using var session = new DelhiHighCourtAssistedSession(config, _ => fake);
        await session.LoadFormAsync(true, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SearchOrdersAsync("CW", "7003", "2026", default));
        Assert.True(await session.ValidateHumanAnswerAsync("typed-by-officer", true, default));
        Assert.Single(DelhiHighCourtAssistedForms.ParseOrderRows(
            await session.SearchOrdersAsync("CW", "7003", "2026", default)));
        Assert.Contains("captchaInput=typed-by-officer", fake.SubmittedOrderForm);
        Assert.DoesNotContain("TEST8", fake.SubmittedOrderForm);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SearchOrdersAsync("CW", "7004", "2026", default));
    }

    [Fact]
    public async Task ExactAssistedFutureDateBecomesOperational_NoCanonicalMutation_ConflictFallsBack()
    {
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new AppUser { Username = "officer", NormalizedUsername = "OFFICER", DisplayName = "Officer" };
        var role = new Role { Code = "OFFICER", Name = "Officer" };
        db.AppUsers.Add(user); db.Roles.Add(role);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        foreach (var code in new[] { PermissionCodes.CourtView, PermissionCodes.CourtEdit })
        {
            var permission = new Permission { Code = code, Name = code, Category = "Court" };
            db.Permissions.Add(permission);
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = ScopeMode.All });
        }
        var courtCase = new CourtCase { CourtName = "Delhi High Court", CaseNumber = "W.P.(C) 7003/2026", CurrentStatus = "Pending" };
        db.CourtCases.Add(courtCase);
        db.CourtProceedings.Add(new CourtProceeding { CourtCaseId = courtCase.Id,
            SourceKind = "LegacyRegisterNDOH", NextDate = new DateOnly(2026, 3, 15) });
        var run = new DhcAssistedSyncRun { StartedByUserId = user.Id, StartedAt = new Clock().GetUtcNow() };
        var item = new DhcAssistedSyncItem { CourtCaseId = courtCase.Id, QueueOrder = 0, Status = DhcAssistedItemStatus.CheckingStatus };
        run.Items.Add(item); db.DhcAssistedSyncRuns.Add(run);
        await db.SaveChangesAsync();
        var auth = new CourtAuthorizationService(db, null!, null!, null!);
        var service = new DelhiHighCourtAssistedService(db, auth, new Clock());
        var before = await CourtOperationalNdohQuery.Resolve(db.CourtCases, db, new Clock().GetCurrentDate())
            .SingleAsync();
        Assert.Equal(new DateOnly(2026, 3, 15), before.OperationalNdoh);
        await service.ProcessStatusAsync(run, item, StatusResult, default);
        var after = await CourtOperationalNdohQuery.Resolve(db.CourtCases, db, new Clock().GetCurrentDate())
            .SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 7), after.OperationalNdoh);
        Assert.True(after.UsesAssistedStatus);
        Assert.Equal("Pending", courtCase.CurrentStatus);
        Assert.Single(db.CourtProceedings);
        Assert.Empty(db.ScheduledEvents);
        var corroboratingSource = new CourtExternalSourceDocument
        {
            SourceUrl = "https://delhihighcourt.nic.in/app/corroboration.pdf",
            SourceTitle = "Official matching listing", ListingDate = new DateOnly(2026, 10, 7),
            Kind = CourtExternalSourceKind.OrdinaryListing
        };
        db.CourtExternalSourceDocuments.Add(corroboratingSource);
        db.CourtExternalListingObservations.Add(new CourtExternalListingObservation
        {
            CourtCaseId = courtCase.Id, SourceDocumentId = corroboratingSource.Id,
            ListingDate = new DateOnly(2026, 10, 7), Mode = CourtExternalSyncMode.LiveWindow,
            Status = CourtExternalListingStatus.Accepted
        });
        await db.SaveChangesAsync();
        var corroborated = await CourtOperationalNdohQuery.Resolve(db.CourtCases, db,
            new Clock().GetCurrentDate()).SingleAsync();
        Assert.False(corroborated.HasOfficialConflict);
        Assert.Equal(new DateOnly(2026, 10, 7), corroborated.OperationalNdoh);
        var source = new CourtExternalSourceDocument { SourceUrl = "https://delhihighcourt.nic.in/app/list.pdf",
            SourceTitle = "Official listing", ListingDate = new DateOnly(2026, 10, 9), Kind = CourtExternalSourceKind.OrdinaryListing };
        db.CourtExternalSourceDocuments.Add(source);
        db.CourtExternalListingObservations.Add(new CourtExternalListingObservation
        { CourtCaseId = courtCase.Id, SourceDocumentId = source.Id, ListingDate = new DateOnly(2026, 10, 9),
            Mode = CourtExternalSyncMode.LiveWindow, Status = CourtExternalListingStatus.Accepted });
        await db.SaveChangesAsync();
        var conflict = await CourtOperationalNdohQuery.Resolve(db.CourtCases, db, new Clock().GetCurrentDate())
            .SingleAsync();
        Assert.True(conflict.HasOfficialConflict);
        Assert.Equal(new DateOnly(2026, 3, 15), conflict.OperationalNdoh);
        db.CourtProceedings.Add(new CourtProceeding
        {
            CourtCaseId = courtCase.Id, ProceedingDate = new DateOnly(2026, 9, 29),
            NextDate = new DateOnly(2026, 10, 12)
        });
        await db.SaveChangesAsync();
        var laterProceeding = await CourtOperationalNdohQuery.Resolve(db.CourtCases, db,
            new Clock().GetCurrentDate()).SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 12), laterProceeding.OperationalNdoh);
    }

    [Fact]
    public async Task ExplicitSelection_AlsoPermitsHealthyPendingDhcCase_ButNotOtherCourt()
    {
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new AppUser { Username = "officer", NormalizedUsername = "OFFICER", DisplayName = "Officer" };
        var role = new Role { Code = "OFFICER", Name = "Officer" };
        db.AppUsers.Add(user); db.Roles.Add(role);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        foreach (var code in new[] { PermissionCodes.CourtView, PermissionCodes.CourtEdit })
        {
            var permission = new Permission { Code = code, Name = code, Category = "Court" };
            db.Permissions.Add(permission);
            db.RolePermissions.Add(new RolePermission
            { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = ScopeMode.All });
        }
        var healthy = new CourtCase
        { CourtName = "Delhi High Court", CaseNumber = "W.P.(C) 7003/2026", CurrentStatus = "Pending" };
        var otherCourt = new CourtCase
        { CourtName = "Supreme Court", CaseNumber = "W.P.(C) 7004/2026", CurrentStatus = "Pending" };
        db.CourtCases.AddRange(healthy, otherCourt);
        db.CourtProceedings.Add(new CourtProceeding
        { CourtCaseId = healthy.Id, SourceKind = "LegacyRegisterNDOH", NextDate = new DateOnly(2026, 10, 20) });
        await db.SaveChangesAsync();
        var service = new DelhiHighCourtAssistedService(db,
            new CourtAuthorizationService(db, null!, null!, null!), new Clock());
        var preview = await service.PreviewAsync(user.Id, default);
        Assert.Equal(0, preview.RecommendedCount);
        Assert.Contains(preview.Cases, x => x.CourtCaseId == healthy.Id && x.Reason == "");
        var run = await service.CreateRunAsync(user.Id,
            new DhcAssistedStartRequest("Selected", [healthy.Id]), default);
        Assert.Single(run.Items);
        Assert.Equal(healthy.Id, run.Items.Single().CourtCaseId);
        await Assert.ThrowsAsync<CourtWorkflowException>(() => service.CreateRunAsync(user.Id,
            new DhcAssistedStartRequest("Selected", [otherCourt.Id]), default));
    }

    [Fact]
    public async Task StatusDifference_IsReviewOnly_AndOrderCanStillBeCaptured()
    {
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new AppUser { Username = "officer", NormalizedUsername = "OFFICER", DisplayName = "Officer" };
        var courtCase = new CourtCase
        { CourtName = "Delhi High Court", CaseNumber = "W.P.(C) 7003/2026", CurrentStatus = "Pending" };
        var run = new DhcAssistedSyncRun { StartedByUserId = user.Id, StartedAt = new Clock().GetUtcNow() };
        var item = new DhcAssistedSyncItem
        { CourtCaseId = courtCase.Id, QueueOrder = 0, Status = DhcAssistedItemStatus.CheckingStatus };
        run.Items.Add(item);
        db.AppUsers.Add(user); db.CourtCases.Add(courtCase); db.DhcAssistedSyncRuns.Add(run);
        await db.SaveChangesAsync();
        var service = new DelhiHighCourtAssistedService(db,
            new CourtAuthorizationService(db, null!, null!, null!), new Clock());
        await service.ProcessStatusAsync(run, item, StatusResult.Replace("[Pending]", "[Disposed]"), default);
        Assert.Equal("StatusDifference", item.FailureCode);
        Assert.Equal(DhcAssistedItemStatus.CheckingStatus, item.Status);
        Assert.Equal(0, run.CompletedCases);
        Assert.Equal("Pending", courtCase.CurrentStatus);
        Assert.Equal(DhcAssistedEvidenceStatus.NeedsReview,
            Assert.Single(db.CourtExternalCaseStatusObservations).Status);
        item.Status = DhcAssistedItemStatus.CheckingOrders;
        await service.ProcessOrdersAsync(run, item, OrderResult, default);
        Assert.Equal(DhcAssistedItemStatus.NeedsReview, item.Status);
        Assert.Equal(1, run.CompletedCases);
        Assert.Equal("https://delhihighcourt.nic.in/app/showlogo/order.pdf",
            Assert.Single(db.CourtExternalOrderObservations).OfficialUrl);
    }

    [Fact]
    public async Task UnprivilegedOfficer_CannotPreviewOrCreateRun()
    {
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new AppUser { Username = "viewer", NormalizedUsername = "VIEWER", DisplayName = "Viewer" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var service = new DelhiHighCourtAssistedService(db,
            new CourtAuthorizationService(db, null!, null!, null!), new Clock());
        var previewError = await Assert.ThrowsAsync<CourtWorkflowException>(() =>
            service.PreviewAsync(user.Id, default));
        Assert.Equal(403, previewError.StatusCode);
        var startError = await Assert.ThrowsAsync<CourtWorkflowException>(() =>
            service.CreateRunAsync(user.Id, new DhcAssistedStartRequest("Recommended", null), default));
        Assert.Equal(403, startError.StatusCode);
        Assert.Empty(db.DhcAssistedSyncRuns);
    }

    [Fact]
    public async Task AssignedOnlyCourtPermissions_CannotStartOfficeWideAssistedRun()
    {
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new AppUser { Username = "assigned", NormalizedUsername = "ASSIGNED", DisplayName = "Assigned" };
        var role = new Role { Code = "ASSIGNED", Name = "Assigned" };
        db.AppUsers.Add(user); db.Roles.Add(role);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        foreach (var code in new[] { PermissionCodes.CourtView, PermissionCodes.CourtEdit })
        {
            var permission = new Permission { Code = code, Name = code, Category = "Court" };
            db.Permissions.Add(permission);
            db.RolePermissions.Add(new RolePermission
            { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = ScopeMode.Assigned });
        }
        await db.SaveChangesAsync();
        var service = new DelhiHighCourtAssistedService(db,
            new CourtAuthorizationService(db, null!, null!, null!), new Clock());
        var error = await Assert.ThrowsAsync<CourtWorkflowException>(() =>
            service.CreateRunAsync(user.Id, new DhcAssistedStartRequest("Recommended", null), default));
        Assert.Equal(403, error.StatusCode);
    }

    [Fact]
    public async Task Coordinator_InvalidAnswerDoesNotAdvance_OnlyOneRun_PausesAndResumesExactItem()
    {
        var configuration = new ConfigurationBuilder().Build();
        var fake = new FakeHandler();
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<LacDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<ICourtAuthorizationService>(provider => new CourtAuthorizationService(
            provider.GetRequiredService<LacDbContext>(), null!, null!, provider));
        services.AddScoped<DelhiHighCourtAssistedService>();
        services.AddSingleton<IOfficeClock, Clock>();
        await using var provider = services.BuildServiceProvider();
        Guid userId;
        Guid caseId;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var user = new AppUser { Username = "operator", NormalizedUsername = "OPERATOR", DisplayName = "Operator" };
            var role = new Role { Code = "OPERATOR", Name = "Operator" };
            db.AppUsers.Add(user); db.Roles.Add(role);
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            foreach (var code in new[] { PermissionCodes.CourtView, PermissionCodes.CourtEdit })
            {
                var permission = new Permission { Code = code, Name = code, Category = "Court" };
                db.Permissions.Add(permission);
                db.RolePermissions.Add(new RolePermission
                { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = ScopeMode.All });
            }
            var courtCase = new CourtCase
            { CourtName = "Delhi High Court", CaseNumber = "W.P.(C) 7003/2026", CurrentStatus = "Pending" };
            db.CourtCases.Add(courtCase);
            await db.SaveChangesAsync();
            userId = user.Id;
            caseId = courtCase.Id;
        }
        var coordinator = new DelhiHighCourtAssistedCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(), configuration,
            new Lifetime(), NullLogger<DelhiHighCourtAssistedCoordinator>.Instance,
            () => new DelhiHighCourtAssistedSession(configuration, _ => fake));
        var runId = await coordinator.StartAsync(userId,
            new DhcAssistedStartRequest("Selected", [caseId]), default);
        Assert.Equal("TEST7", (await coordinator.ChallengeAsync(runId, userId, default)).OfficialText);
        await Assert.ThrowsAsync<CourtWorkflowException>(() => coordinator.StartAsync(userId,
            new DhcAssistedStartRequest("Selected", [caseId]), default));
        Assert.False(await coordinator.SubmitHumanAnswerAsync(runId, userId, "wrong-human-entry", default));
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            Assert.Equal(DhcAssistedItemStatus.Queued,
                (await db.DhcAssistedSyncItems.SingleAsync()).Status);
            Assert.Empty(db.CourtExternalCaseStatusObservations);
        }
        Assert.True(await coordinator.SubmitHumanAnswerAsync(runId, userId, "typed-by-officer", default));
        await WaitForRunStatusAsync(provider, runId, DhcAssistedRunStatus.PausedForCaptcha);
        Assert.Equal("TEST8", (await coordinator.ChallengeAsync(runId, userId, default)).OfficialText);
        Assert.True(await coordinator.SubmitHumanAnswerAsync(runId, userId, "typed-by-officer", default));
        await WaitForRunStatusAsync(provider, runId, DhcAssistedRunStatus.Completed);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            Assert.Single(await db.CourtExternalCaseStatusObservations.ToListAsync());
            Assert.Single(await db.CourtExternalOrderObservations.ToListAsync());
            Assert.Equal(1, (await db.DhcAssistedSyncItems.SingleAsync()).AttemptCount);
            var storedText = string.Join(" | ",
                (await db.CourtExternalCaseStatusObservations.ToListAsync()).Select(x => x.RawEvidenceText)
                .Concat((await db.CourtExternalOrderObservations.ToListAsync()).Select(x => x.RawEvidenceText)));
            Assert.DoesNotContain("typed-by-officer", storedText);
            Assert.DoesNotContain("TEST7", storedText);
            Assert.DoesNotContain("TEST8", storedText);
        }
        Assert.Null(coordinator.ActiveRunId);
    }

    private static async Task WaitForRunStatusAsync(ServiceProvider provider, Guid runId,
        DhcAssistedRunStatus status)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var run = await db.DhcAssistedSyncRuns.AsNoTracking().SingleAsync(x => x.Id == runId);
            if (run.Status == status) return;
            if (run.Status == DhcAssistedRunStatus.Failed)
                throw new Xunit.Sdk.XunitException($"Assisted fixture run failed: {run.FailureMessage}");
            await Task.Delay(100);
        }
        throw new TimeoutException($"Assisted run did not reach {status}.");
    }

    [Theory]
    [InlineData("not-found")]
    [InlineData("mismatch")]
    [InlineData("multiple")]
    public async Task StatusRows_FailClosedForMissingWrongOrAmbiguousIdentity(string scenario)
    {
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new AppUser { Username = "officer", NormalizedUsername = "OFFICER", DisplayName = "Officer" };
        var courtCase = new CourtCase
        { CourtName = "Delhi High Court", CaseNumber = "W.P.(C) 7003/2026", CurrentStatus = "Pending" };
        var run = new DhcAssistedSyncRun { StartedByUserId = user.Id, StartedAt = new Clock().GetUtcNow() };
        var item = new DhcAssistedSyncItem
        { CourtCaseId = courtCase.Id, QueueOrder = 0, Status = DhcAssistedItemStatus.CheckingStatus };
        run.Items.Add(item);
        db.AppUsers.Add(user); db.CourtCases.Add(courtCase); db.DhcAssistedSyncRuns.Add(run);
        await db.SaveChangesAsync();
        var response = scenario switch
        {
            "not-found" => "{\"data\":[]}",
            "mismatch" => StatusResult.Replace("7003", "7004"),
            _ => StatusResult.Replace("}]}", "},{\"ctype\":\"<b>W.P.(C) 7003/2026 [Pending]</b>\",\"pet\":\"Petitioner Vs Respondent\",\"orderdate\":\"07.10.2026 / Court No. 23\"}]}")
        };
        var service = new DelhiHighCourtAssistedService(db,
            new CourtAuthorizationService(db, null!, null!, null!), new Clock());
        await service.ProcessStatusAsync(run, item, response, default);
        Assert.Equal("Pending", courtCase.CurrentStatus);
        Assert.Null((await CourtOperationalNdohQuery.Resolve(db.CourtCases, db,
            new Clock().GetCurrentDate()).SingleAsync()).OperationalNdoh);
        Assert.Equal(scenario == "not-found" ? DhcAssistedItemStatus.NotFound :
            DhcAssistedItemStatus.NeedsReview, item.Status);
        Assert.Equal(scenario == "not-found" ? 0 : 1,
            await db.CourtExternalCaseStatusObservations.CountAsync());
    }

    [Fact]
    public async Task DifferentCurrentOfficialDates_StayInReviewUntilAuditedOfficerDecision()
    {
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new AppUser { Username = "officer", NormalizedUsername = "OFFICER", DisplayName = "Officer" };
        var role = new Role { Code = "OFFICER", Name = "Officer" };
        db.AppUsers.Add(user); db.Roles.Add(role);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        foreach (var code in new[] { PermissionCodes.CourtView, PermissionCodes.CourtEdit })
        {
            var permission = new Permission { Code = code, Name = code, Category = "Court" };
            db.Permissions.Add(permission);
            db.RolePermissions.Add(new RolePermission
            { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = ScopeMode.All });
        }
        var courtCase = new CourtCase
        { CourtName = "Delhi High Court", CaseNumber = "W.P.(C) 7003/2026", CurrentStatus = "Pending" };
        db.CourtCases.Add(courtCase);
        var run = new DhcAssistedSyncRun { StartedByUserId = user.Id, StartedAt = new Clock().GetUtcNow() };
        var first = new DhcAssistedSyncItem
        { CourtCaseId = courtCase.Id, QueueOrder = 0, Status = DhcAssistedItemStatus.CheckingStatus };
        var second = new DhcAssistedSyncItem
        { CourtCaseId = courtCase.Id, QueueOrder = 1, Status = DhcAssistedItemStatus.CheckingStatus };
        var third = new DhcAssistedSyncItem
        { CourtCaseId = courtCase.Id, QueueOrder = 2, Status = DhcAssistedItemStatus.CheckingStatus };
        run.Items.Add(first); run.Items.Add(second); run.Items.Add(third);
        db.DhcAssistedSyncRuns.Add(run);
        await db.SaveChangesAsync();
        var service = new DelhiHighCourtAssistedService(db,
            new CourtAuthorizationService(db, null!, null!, null!), new Clock());
        await service.ProcessStatusAsync(run, first, StatusResult, default);
        await service.ProcessStatusAsync(run, second, StatusResult.Replace("07.10.2026", "09.10.2026"), default);
        await service.ProcessStatusAsync(run, third, StatusResult, default);
        var conflict = await CourtOperationalNdohQuery.Resolve(db.CourtCases, db,
            new Clock().GetCurrentDate()).SingleAsync();
        Assert.True(conflict.HasOfficialConflict);
        Assert.Null(conflict.OperationalNdoh);
        Assert.Equal(2, await db.CourtExternalCaseStatusObservations.CountAsync(x =>
            x.ReviewReason == "DateConflict"));
        var chosen = await db.CourtExternalCaseStatusObservations.SingleAsync(x => x.RunItemId == second.Id);
        await service.ReviewAsync(chosen.Id, true, "Verified exact current DHC result", user.Id, default);
        var resolved = await CourtOperationalNdohQuery.Resolve(db.CourtCases, db,
            new Clock().GetCurrentDate()).SingleAsync();
        Assert.False(resolved.HasOfficialConflict);
        Assert.Equal(new DateOnly(2026, 10, 9), resolved.OperationalNdoh);
        Assert.Equal(3, await db.CourtExternalAssistedDecisions.CountAsync());
        Assert.Equal("Pending", courtCase.CurrentStatus);
    }
}
