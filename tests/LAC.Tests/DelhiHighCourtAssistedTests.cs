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
        <input type='hidden' name='randomid' value='TEST8'>
        <input type='hidden' name='officialOpaque' value='fixture-state'>
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

    private sealed class FakeHandler(bool imageChallenge = false) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public string? SubmittedHumanAnswer { get; private set; }
        public string? SubmittedOrderForm { get; private set; }
        public int StatusLookups;
        public int ImageRequests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");
            string body;
            if (request.Method == HttpMethod.Get && path.EndsWith("captcha-image"))
            {
                ImageRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent([137, 80, 78, 71])
                    { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") } } };
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("get-case-type-status") && request.RequestUri.Query == "")
                body = imageChallenge ? StatusForm.Replace("<span id='captcha-code'>TEST7</span>",
                    "<img id='captcha-image' src='/app/captcha-image'>") : StatusForm;
            else if (request.Method == HttpMethod.Get && path.EndsWith("case-number")) body = OrderForm;
            else if (request.Method == HttpMethod.Post && path.EndsWith("validateCaptcha"))
            {
                var submitted = await request.Content!.ReadAsStringAsync(ct);
                SubmittedHumanAnswer = Uri.UnescapeDataString(submitted.Split("captchaInput=")[1]);
                body = SubmittedHumanAnswer == "typed-by-officer" ? "{\"success\":true}" : "{\"success\":false}";
            }
            else if (request.Method == HttpMethod.Get && path.EndsWith("get-case-type-status"))
            {
                Assert.Equal("XMLHttpRequest", request.Headers.GetValues("X-Requested-With").Single());
                body = ++StatusLookups <= 2 ? StatusResult : "{\"error\":\"captcha required\"}";
            }
            else if (request.Method == HttpMethod.Post && path.EndsWith("case-number"))
            {
                SubmittedOrderForm = await request.Content!.ReadAsStringAsync(ct);
                body = OrderResult;
            }
            else return new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") };
        }
    }

    private sealed class TransientFormHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Attempts++;
            if (Attempts == 1) throw new HttpRequestException("Temporary official connection failure.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(StatusForm, Encoding.UTF8, "text/html") });
        }
    }

    [Fact]
    public async Task InitialOfficialFormGet_RetriesOnceWithoutSubmittingVerification()
    {
        var handler = new TransientFormHandler();
        await using var session = new DelhiHighCourtAssistedSession(new ConfigurationBuilder().Build(), _ => handler);
        var form = await session.LoadFormAsync(false, default);
        Assert.Equal(2, handler.Attempts);
        Assert.Equal("TEST7", form.VisibleChallenge);
        Assert.False(session.Verified);
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

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan span) => now += span;
    }

    private sealed class BatchHandler(string? orderReply = null) : HttpMessageHandler
    {
        public int StatusLookups { get; private set; }
        public int Validations { get; private set; }
        public int OrderForms { get; private set; }
        public int OrderPosts { get; private set; }
        public string? OrderPostBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (request.Method == HttpMethod.Get && path.EndsWith("get-case-type-status") &&
                request.RequestUri.Query.Length == 0) body = StatusForm;
            else if (request.Method == HttpMethod.Post && path.EndsWith("validateCaptcha"))
            {
                var submitted = await request.Content!.ReadAsStringAsync(ct);
                Assert.Contains("captchaInput=typed-by-officer", submitted);
                Validations++;
                body = "{\"success\":true}";
            }
            else if (request.Method == HttpMethod.Get && path.EndsWith("get-case-type-status"))
            {
                Assert.Equal("XMLHttpRequest", request.Headers.GetValues("X-Requested-With").Single());
                StatusLookups++;
                if (StatusLookups == 4 && Validations == 1) body = "{\"error\":\"captcha required\"}";
                else
                {
                    var number = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["case_number"]!;
                    body = StatusResult.Replace("7003", number);
                }
            }
            else if (request.Method == HttpMethod.Get && path.EndsWith("case-number"))
            {
                OrderForms++;
                body = OrderForm;
            }
            else if (request.Method == HttpMethod.Post && path.EndsWith("case-number"))
            {
                OrderPosts++;
                OrderPostBody = await request.Content!.ReadAsStringAsync(ct);
                body = orderReply ?? OrderResult;
            }
            else return new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "text/html") };
        }
    }

    private static async Task<(ServiceProvider Provider, Guid UserId, Guid[] CaseIds)> HarnessAsync(int count)
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<LacDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<ICourtAuthorizationService>(provider => new CourtAuthorizationService(
            provider.GetRequiredService<LacDbContext>(), null!, null!, provider));
        services.AddScoped<ICourtWorkflowService>(provider => new CourtWorkflowService(
            provider.GetRequiredService<LacDbContext>(),
            provider.GetRequiredService<ICourtAuthorizationService>(), null!));
        services.AddScoped<DelhiHighCourtAssistedService>();
        services.AddSingleton<IOfficeClock, Clock>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
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
        var cases = Enumerable.Range(0, count).Select(index => new CourtCase
        { CourtName = "Delhi High Court", CaseNumber = $"W.P.(C) {7003 + index}/2026", CurrentStatus = "Pending" }).ToArray();
        db.CourtCases.AddRange(cases);
        await db.SaveChangesAsync();
        return (provider, user.Id, cases.Select(x => x.Id).ToArray());
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
        Assert.Equal("TEST8", orders.HiddenFields["randomid"]);
        Assert.Equal("fixture-state", orders.HiddenFields["officialOpaque"]);
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
        var submitted = System.Web.HttpUtility.ParseQueryString(fake.SubmittedOrderForm!);
        Assert.Equal("typed-by-officer", submitted["captchaInput"]);
        Assert.NotEqual("TEST8", submitted["captchaInput"]);
        Assert.Equal("TEST8", submitted["randomid"]); // Browser's opaque hidden form state.
        Assert.Equal("fixture-state", submitted["officialOpaque"]);
        Assert.Equal("fixture-order-csrf", submitted["_token"]);
        Assert.Equal("CW", submitted["case_type"]);
        Assert.Equal("7003", submitted["case_number"]);
        Assert.Equal("2026", submitted["year"]);
        Assert.Equal(new[] { "_token", "captchaInput", "case_number", "case_type", "officialOpaque", "randomid", "year" },
            session.LastOrderPostFieldNames);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SearchOrdersAsync("CW", "7004", "2026", default));
    }

    [Fact]
    public void OrderResponse_SeparatesResultsExplicitCaptchaAndGenericForm()
    {
        var resultWithVisibleChallenge = OrderForm.Replace("</form>",
            "<script>const message = 'CAPTCHA is required';</script></form>") + OrderResult;
        var result = DelhiHighCourtAssistedForms.AssessOrderResponse(resultWithVisibleChallenge);
        Assert.Equal(DelhiHighCourtAssistedForms.OrderResponseKind.Result, result.Kind);
        Assert.True(result.HasResultTable);
        Assert.True(result.HasValidResultColumns);
        Assert.Equal(1, result.ResultDataRowCount);
        var initial = DelhiHighCourtAssistedForms.AssessOrderResponse(OrderForm);
        Assert.Equal(DelhiHighCourtAssistedForms.OrderResponseKind.UnconfirmedForm, initial.Kind);
        Assert.False(initial.HasResultTable);
        var missingField = DelhiHighCourtAssistedForms.AssessOrderResponse(
            OrderForm.Replace("</form>", "<p>Case number field is required</p></form>"));
        Assert.Equal(DelhiHighCourtAssistedForms.OrderResponseKind.FormValidationError, missingField.Kind);
        Assert.True(missingField.HasFormError);
        Assert.Equal(DelhiHighCourtAssistedForms.OrderResponseKind.CaptchaRequired,
            DelhiHighCourtAssistedForms.AssessOrderResponse("captcha required").Kind);
        Assert.Equal(DelhiHighCourtAssistedForms.OrderResponseKind.CaptchaRequired,
            DelhiHighCourtAssistedForms.AssessOrderResponse(
                OrderForm.Replace("</form>", "<p>CAPTCHA is incorrect</p></form>")).Kind);
    }

    [Fact]
    public void EmptyOrderTable_IsParsedSafely_ButDoesNotConfirmSearch()
    {
        var emptyTable = OrderResult.Replace(
            "<tbody><tr><td>1</td><td>W.P.(C) 7003/2026</td><td><a href='/app/showlogo/order.pdf'>25.09.2026</a></td><td>Parties</td><td></td><td>26.09.2026</td><td>Order</td></tr></tbody>",
            "<tbody><tr><td colspan='7'>No records found</td></tr></tbody>");
        Assert.Empty(DelhiHighCourtAssistedForms.ParseOrderRows(emptyTable));
        var assessment = DelhiHighCourtAssistedForms.AssessOrderResponse(OrderForm + emptyTable,
            "CW", "7003", "2026");
        Assert.Equal(DelhiHighCourtAssistedForms.OrderResponseKind.UnconfirmedForm, assessment.Kind);
        Assert.True(assessment.HasResultTable);
        Assert.True(assessment.HasValidResultColumns);
        Assert.Equal(0, assessment.ResultDataRowCount);
        Assert.False(assessment.EchoesCaseType);
        Assert.False(assessment.EchoesCaseNumber);
        Assert.False(assessment.EchoesYear);
    }

    [Fact]
    public void OrderResponseAssessment_ReportsOnlySafeControlNamesAndEchoBooleans()
    {
        var response = OrderForm.Replace("<select id='case_type'>", "<select id='case_type' name='case_type'>")
            .Replace("<option value='CW'>", "<option value='CW' selected>")
            .Replace("</form>", "<input name='case_number' value='7003'><input name='year' value='2026'>" +
                "<input name='unsafe secret=value' value='do-not-store'></form>");
        var assessment = DelhiHighCourtAssistedForms.AssessOrderResponse(response, "CW", "7003", "2026");
        Assert.True(assessment.EchoesCaseType);
        Assert.True(assessment.EchoesCaseNumber);
        Assert.True(assessment.EchoesYear);
        Assert.Contains("case_number", assessment.ReturnedControlNames);
        Assert.DoesNotContain("unsafe secret=value", assessment.ReturnedControlNames);
        Assert.DoesNotContain("fixture-order-csrf", string.Join(",", assessment.ReturnedControlNames));
        Assert.False(DelhiHighCourtAssistedForms.AssessOrderResponse(response, "CW", "7004", "2026")
            .EchoesCaseNumber);
    }

    private sealed class OrderPostTransportFailureHandler : HttpMessageHandler
    {
        public int OrderPosts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("case-number"))
            {
                OrderPosts++;
                throw new HttpRequestException("Simulated order POST transport failure");
            }
            var body = request.Method == HttpMethod.Get ? OrderForm : "{\"success\":true}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "text/html") });
        }
    }

    [Fact]
    public async Task OrderPostTransportFailure_IsNotReplayed()
    {
        var handler = new OrderPostTransportFailureHandler();
        await using var session = new DelhiHighCourtAssistedSession(new ConfigurationBuilder().Build(), _ => handler);
        await session.LoadFormAsync(true, default);
        Assert.True(await session.ValidateHumanAnswerAsync("typed-by-officer", true, default));
        await Assert.ThrowsAsync<HttpRequestException>(() => session.SearchOrdersAsync("CW", "7003", "2026", default));
        Assert.Equal(1, handler.OrderPosts);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SearchOrdersAsync("CW", "7003", "2026", default));
        Assert.Equal(1, handler.OrderPosts);
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
        var item = new DhcAssistedSyncItem { CourtCaseId = courtCase.Id,
            NormalizedCaseIdentity = "delhihighcourt|wpc|7003|2026", QueueOrder = 0, Status = DhcAssistedItemStatus.CheckingStatus };
        run.Items.Add(item); db.DhcAssistedSyncRuns.Add(run);
        await db.SaveChangesAsync();
        // LacDbContext stamps new official records with real UTC time. Make the
        // legacy fixture older than the fixed fake official observation clock.
        (await db.CourtProceedings.SingleAsync()).CreatedAt =
            new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
        await db.SaveChangesAsync();
        var auth = new CourtAuthorizationService(db, null!, null!, null!);
        var service = new DelhiHighCourtAssistedService(db, auth, new Clock());
        var before = await CourtOperationalNdohQuery.Resolve(db.CourtCases, db, new Clock().GetCurrentDate())
            .SingleAsync();
        Assert.Equal(new DateOnly(2026, 3, 15), before.OperationalNdoh);
        await service.ProcessStatusAsync(run, item, StatusResult, default);
        var statusEvidence = await db.CourtExternalCaseStatusObservations.SingleAsync();
        Assert.Equal(DhcAssistedEvidenceStatus.Accepted, statusEvidence.Status);
        Assert.Equal(new DateOnly(2026, 10, 7), statusEvidence.ListingDate);
        var proceedingCreatedAt = (await db.CourtProceedings.SingleAsync()).CreatedAt;
        Assert.True(statusEvidence.ObservedAt > proceedingCreatedAt,
            $"Observed {statusEvidence.ObservedAt:O}; proceeding {proceedingCreatedAt:O}");
        Assert.Equal(1, await db.CourtExternalCaseStatusObservations.CountAsync(x =>
            x.CourtCaseId == courtCase.Id && x.Status == DhcAssistedEvidenceStatus.Accepted &&
            x.ListingDate >= new Clock().GetCurrentDate()));
        var after = await CourtOperationalNdohQuery.Resolve(db.CourtCases, db, new Clock().GetCurrentDate())
            .SingleAsync();
        Assert.False(after.HasOfficialConflict);
        Assert.True(after.UsesAssistedStatus);
        Assert.Equal(new DateOnly(2026, 10, 7), after.OperationalNdoh);
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
        Assert.Equal("delhihighcourt|wpc|7003|2026", run.Items.Single().NormalizedCaseIdentity);
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
        { CourtCaseId = courtCase.Id, NormalizedCaseIdentity = "delhihighcourt|wpc|7003|2026",
            QueueOrder = 0, Status = DhcAssistedItemStatus.CheckingStatus };
        run.Items.Add(item);
        db.AppUsers.Add(user); db.CourtCases.Add(courtCase); db.DhcAssistedSyncRuns.Add(run);
        await db.SaveChangesAsync();
        var service = new DelhiHighCourtAssistedService(db,
            new CourtAuthorizationService(db, null!, null!, null!), new Clock());
        await service.ProcessStatusAsync(run, item, StatusResult.Replace("[Pending]", "[Disposed]"), default);
        Assert.Equal("StatusDifference", item.FailureCode);
        Assert.Equal(DhcAssistedItemStatus.StatusCaptured, item.Status);
        Assert.Equal(1, run.CompletedCases);
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
        await WaitForRunStatusAsync(provider, runId, DhcAssistedRunStatus.ReadyForOrders);
        await coordinator.StartOrdersAsync(runId, userId, default);
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
        { CourtCaseId = courtCase.Id, NormalizedCaseIdentity = "delhihighcourt|wpc|7003|2026",
            QueueOrder = 0, Status = DhcAssistedItemStatus.CheckingStatus };
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
        { CourtCaseId = courtCase.Id, NormalizedCaseIdentity = "delhihighcourt|wpc|7003|2026",
            QueueOrder = 0, Status = DhcAssistedItemStatus.CheckingStatus };
        var second = new DhcAssistedSyncItem
        { CourtCaseId = courtCase.Id, NormalizedCaseIdentity = "delhihighcourt|wpc|7003|2026",
            QueueOrder = 1, Status = DhcAssistedItemStatus.CheckingStatus };
        var third = new DhcAssistedSyncItem
        { CourtCaseId = courtCase.Id, NormalizedCaseIdentity = "delhihighcourt|wpc|7003|2026",
            QueueOrder = 2, Status = DhcAssistedItemStatus.CheckingStatus };
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

    [Fact]
    public async Task StatusBatch_ReusesOneVerificationForThreeCases_ThenPausesAtFourth_BeforeAnyOrder()
    {
        var (provider, userId, caseIds) = await HarnessAsync(5);
        await using (provider)
        {
            var config = new ConfigurationBuilder().Build();
            var fake = new BatchHandler();
            var coordinator = new DelhiHighCourtAssistedCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(), config,
                new Lifetime(), NullLogger<DelhiHighCourtAssistedCoordinator>.Instance,
                () => new DelhiHighCourtAssistedSession(config, _ => fake));
            var runId = await coordinator.StartAsync(userId,
                new DhcAssistedStartRequest("Selected", caseIds), default);
            Assert.True(await coordinator.SubmitHumanAnswerAsync(runId, userId, "typed-by-officer", default));
            await WaitForRunStatusAsync(provider, runId, DhcAssistedRunStatus.PausedForCaptcha);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
                var run = await db.DhcAssistedSyncRuns.AsNoTracking().SingleAsync(x => x.Id == runId);
                Assert.Equal(DhcAssistedPhase.StatusLookup, run.Phase);
                Assert.Equal(3, run.CompletedCases);
                Assert.Equal(3, await db.DhcAssistedSyncItems.CountAsync(x => x.Status == DhcAssistedItemStatus.StatusCaptured));
                Assert.Equal(1, await db.DhcAssistedSyncItems.CountAsync(x => x.Status == DhcAssistedItemStatus.CaptchaRequired));
            }
            Assert.Equal(0, fake.OrderForms);
            Assert.Equal("Case status", (await coordinator.ChallengeAsync(runId, userId, default)).Operation);
            Assert.True(await coordinator.SubmitHumanAnswerAsync(runId, userId, "typed-by-officer", default));
            await WaitForRunStatusAsync(provider, runId, DhcAssistedRunStatus.ReadyForOrders);
            Assert.Equal(6, fake.StatusLookups); // A/B/C, D challenge, D/E after human retry.
            Assert.Equal(0, fake.OrderForms);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
                Assert.Equal(5, await db.CourtExternalCaseStatusObservations.CountAsync());
                Assert.Equal(5, (await db.DhcAssistedSyncRuns.SingleAsync(x => x.Id == runId)).CompletedCases);
            }
            await coordinator.StartOrdersAsync(runId, userId, default);
            Assert.Equal(1, fake.OrderForms);
            Assert.Equal("Order search", (await coordinator.ChallengeAsync(runId, userId, default)).Operation);
            Assert.True(await coordinator.SubmitHumanAnswerAsync(runId, userId, "typed-by-officer", default));
            await WaitForRunStatusAsync(provider, runId, DhcAssistedRunStatus.PausedForCaptcha);
            Assert.Contains("case_type=CW", fake.OrderPostBody);
            await coordinator.CancelAsync(runId, userId, default);
        }
    }

    [Theory]
    [InlineData("result")]
    [InlineData("captcha")]
    [InlineData("form-rejected")]
    [InlineData("form-validation")]
    [InlineData("unrecognized")]
    [InlineData("invalid-result")]
    public async Task OrderPhase_DistinguishesResultCaptchaAndGenericReturnedForm(string scenario)
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            var config = new ConfigurationBuilder().Build();
            var fake = new BatchHandler(scenario switch
            {
                "captcha" => "captcha required",
                "form-rejected" => OrderForm,
                "form-validation" => OrderForm.Replace("</form>", "<p>Case number field is required</p></form>"),
                "unrecognized" => "<html><body>Unexpected official response</body></html>",
                "invalid-result" => OrderResult.Replace("/app/showlogo/order.pdf", "https://unapproved.example/order.pdf"),
                _ => OrderResult
            });
            var coordinator = new DelhiHighCourtAssistedCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(), config,
                new Lifetime(), NullLogger<DelhiHighCourtAssistedCoordinator>.Instance,
                () => new DelhiHighCourtAssistedSession(config, _ => fake));
            var runId = await coordinator.StartAsync(userId,
                new DhcAssistedStartRequest("Selected", caseIds), default);
            Assert.True(await coordinator.SubmitHumanAnswerAsync(runId, userId, "typed-by-officer", default));
            await WaitForRunStatusAsync(provider, runId, DhcAssistedRunStatus.ReadyForOrders);
            await coordinator.StartOrdersAsync(runId, userId, default);
            Assert.True(await coordinator.SubmitHumanAnswerAsync(runId, userId, "typed-by-officer", default));
            await WaitForRunStatusAsync(provider, runId, scenario switch
            {
                "captcha" => DhcAssistedRunStatus.PausedForCaptcha,
                "form-rejected" or "form-validation" or "unrecognized" or "invalid-result" => DhcAssistedRunStatus.Failed,
                _ => DhcAssistedRunStatus.Completed
            });
            Assert.Equal(1, fake.OrderPosts);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            Assert.Equal(scenario == "result" ? 1 : 0,
                await db.CourtExternalOrderObservations.CountAsync());
            if (scenario == "captcha") Assert.Equal(2, fake.OrderForms);
            if (scenario == "form-rejected") Assert.Equal(1, fake.OrderForms);
            if (scenario is "form-rejected" or "form-validation" or "unrecognized" or "invalid-result")
            {
                var item = await db.DhcAssistedSyncItems.SingleAsync(x => x.RunId == runId);
                var kind = scenario switch
                {
                    "form-rejected" => "UnconfirmedForm",
                    "form-validation" => "FormValidationError",
                    "invalid-result" => "Result",
                    _ => "Unrecognized"
                };
                Assert.Equal(scenario == "invalid-result" ? "OrderResultParseFailed" : "OrderResponse" + kind,
                    item.FailureCode);
                Assert.Contains("Category=" + kind, item.FailureMessage);
                Assert.Contains("HasResultTable=" + (scenario == "invalid-result" ? "True" : "False"), item.FailureMessage);
                Assert.Contains("ReturnedControls=[", item.FailureMessage);
                Assert.Contains("OutgoingFields=[", item.FailureMessage);
                Assert.True(item.FailureMessage!.Length <= 500);
                Assert.DoesNotContain("typed-by-officer", item.FailureMessage);
                Assert.DoesNotContain("TEST8", item.FailureMessage);
                Assert.DoesNotContain("fixture-order-csrf", item.FailureMessage);
                Assert.DoesNotContain("fixture-state", item.FailureMessage);
            }
            if (scenario == "captcha") await coordinator.CancelAsync(runId, userId, default);
        }
    }

    [Fact]
    public async Task IdleExpiry_ReleasesSlot_DisposesSession_AndResumeRequiresFreshChallenge()
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            var clock = new ManualTime();
            var config = new ConfigurationBuilder().Build();
            var sessions = new List<DelhiHighCourtAssistedSession>();
            var coordinator = new DelhiHighCourtAssistedCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(), config,
                new Lifetime(), NullLogger<DelhiHighCourtAssistedCoordinator>.Instance,
                () => { var session = new DelhiHighCourtAssistedSession(config, _ => new FakeHandler()); sessions.Add(session); return session; },
                clock);
            var runId = await coordinator.StartAsync(userId,
                new DhcAssistedStartRequest("Selected", caseIds), default);
            for (var i = 0; i < 3; i++)
            {
                clock.Advance(TimeSpan.FromMinutes(4));
                using var scope = provider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>()
                    .GetRunAsync(runId, userId, default);
                Assert.Equal("TEST7", (await coordinator.ChallengeAsync(runId, userId, default)).OfficialText);
            }
            clock.Advance(TimeSpan.FromMinutes(4));
            await coordinator.SweepExpiredAsync();
            Assert.Null(coordinator.ActiveRunId);
            Assert.True(sessions[0].IsDisposed);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
                Assert.Equal(DhcAssistedRunStatus.Interrupted,
                    (await db.DhcAssistedSyncRuns.SingleAsync(x => x.Id == runId)).Status);
            }
            await coordinator.ResumeAsync(runId, userId, default);
            Assert.Equal(2, sessions.Count);
            Assert.NotSame(sessions[0].Cookies, sessions[1].Cookies);
            Assert.Equal("Case status", (await coordinator.ChallengeAsync(runId, userId, default)).Operation);
            await coordinator.CancelAsync(runId, userId, default);
        }
    }

    [Fact]
    public async Task ImageChallengeRead_DoesNotExtendIdleLifetime()
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            var clock = new ManualTime();
            var config = new ConfigurationBuilder().Build();
            var fake = new FakeHandler(imageChallenge: true);
            var sessions = new List<DelhiHighCourtAssistedSession>();
            var coordinator = new DelhiHighCourtAssistedCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(), config,
                new Lifetime(), NullLogger<DelhiHighCourtAssistedCoordinator>.Instance,
                () => { var session = new DelhiHighCourtAssistedSession(config, _ => fake); sessions.Add(session); return session; }, clock);
            var runId = await coordinator.StartAsync(userId,
                new DhcAssistedStartRequest("Selected", caseIds), default);
            Assert.Equal("Image", (await coordinator.ChallengeAsync(runId, userId, default)).Kind);
            for (var i = 0; i < 3; i++)
            {
                clock.Advance(TimeSpan.FromMinutes(4));
                Assert.Equal("image/png", (await coordinator.ChallengeImageAsync(runId, userId, default)).ContentType);
            }
            clock.Advance(TimeSpan.FromMinutes(4));
            await coordinator.SweepExpiredAsync();
            Assert.Equal(3, fake.ImageRequests);
            Assert.Null(coordinator.ActiveRunId);
            Assert.True(sessions[0].IsDisposed);
        }
    }

    [Theory]
    [InlineData("number")]
    [InlineData("court")]
    [InlineData("status")]
    [InlineData("record")]
    public async Task ChangedQueuedCase_NeverIssuesStatusLookup(string change)
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            var config = new ConfigurationBuilder().Build();
            var fake = new FakeHandler();
            var coordinator = new DelhiHighCourtAssistedCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(), config,
                new Lifetime(), NullLogger<DelhiHighCourtAssistedCoordinator>.Instance,
                () => new DelhiHighCourtAssistedSession(config, _ => fake));
            var runId = await coordinator.StartAsync(userId,
                new DhcAssistedStartRequest("Selected", caseIds), default);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
                var item = await db.DhcAssistedSyncItems.SingleAsync(x => x.RunId == runId);
                Assert.Equal("delhihighcourt|wpc|7003|2026", item.NormalizedCaseIdentity);
                var courtCase = await db.CourtCases.SingleAsync(x => x.Id == caseIds[0]);
                switch (change)
                {
                    case "number": courtCase.CaseNumber = "W.P.(C) 7004/2026"; break;
                    case "court": courtCase.CourtName = "Supreme Court"; break;
                    case "status": courtCase.CurrentStatus = "Disposed"; break;
                    case "record": courtCase.RecordStatus = RecordStatus.Inactive; break;
                }
                await db.SaveChangesAsync();
            }
            Assert.True(await coordinator.SubmitHumanAnswerAsync(runId, userId, "typed-by-officer", default));
            await WaitForRunStatusAsync(provider, runId, DhcAssistedRunStatus.ReadyForOrders);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
                var item = await db.DhcAssistedSyncItems.SingleAsync(x => x.RunId == runId);
                Assert.Equal("CaseChangedSinceQueue", item.FailureCode);
                Assert.Equal(DhcAssistedItemStatus.NeedsReview, item.Status);
                Assert.Contains("changed after", item.FailureMessage);
                Assert.Empty(db.CourtExternalCaseStatusObservations);
            }
            Assert.Equal(0, fake.StatusLookups);
            await coordinator.FinishSessionAsync(runId, userId, default);
        }
    }

    [Fact]
    public async Task UnchangedQueuedCase_UsesSnapshotForExactStatusLookup()
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            var config = new ConfigurationBuilder().Build();
            var fake = new FakeHandler();
            var coordinator = new DelhiHighCourtAssistedCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(), config,
                new Lifetime(), NullLogger<DelhiHighCourtAssistedCoordinator>.Instance,
                () => new DelhiHighCourtAssistedSession(config, _ => fake));
            var runId = await coordinator.StartAsync(userId,
                new DhcAssistedStartRequest("Selected", caseIds), default);
            Assert.True(await coordinator.SubmitHumanAnswerAsync(runId, userId, "typed-by-officer", default));
            await WaitForRunStatusAsync(provider, runId, DhcAssistedRunStatus.ReadyForOrders);
            Assert.Equal(1, fake.StatusLookups);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var item = await db.DhcAssistedSyncItems.SingleAsync(x => x.RunId == runId);
            var observation = await db.CourtExternalCaseStatusObservations.SingleAsync();
            Assert.Equal(item.NormalizedCaseIdentity, observation.NormalizedCaseIdentity);
            await coordinator.FinishSessionAsync(runId, userId, default);
        }
    }

    [Fact]
    public async Task HardLifetime_ExpiresDespiteIntermittentOwnerActivity()
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            var clock = new ManualTime();
            var config = new ConfigurationBuilder().Build();
            var fake = new FakeHandler();
            var coordinator = new DelhiHighCourtAssistedCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(), config,
                new Lifetime(), NullLogger<DelhiHighCourtAssistedCoordinator>.Instance,
                () => new DelhiHighCourtAssistedSession(config, _ => fake), clock);
            var runId = await coordinator.StartAsync(userId,
                new DhcAssistedStartRequest("Selected", caseIds), default);
            for (var i = 0; i < 4; i++)
            {
                clock.Advance(TimeSpan.FromMinutes(14));
                await coordinator.RefreshAsync(runId, userId, default);
            }
            clock.Advance(TimeSpan.FromMinutes(5));
            await coordinator.SweepExpiredAsync();
            Assert.Null(coordinator.ActiveRunId);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            Assert.Equal(DhcAssistedRunStatus.Interrupted,
                (await db.DhcAssistedSyncRuns.SingleAsync(x => x.Id == runId)).Status);
        }
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public async Task AssistedQueue_HasStrictHundredCaseCap_NoSilentTruncation(int count, bool accepted)
    {
        var (provider, userId, caseIds) = await HarnessAsync(count);
        await using (provider)
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>();
            if (accepted)
            {
                var run = await service.CreateRunAsync(userId,
                    new DhcAssistedStartRequest("Recommended", null), default);
                Assert.Equal(100, run.TotalCases);
                Assert.Equal(100, run.Items.Count);
            }
            else
            {
                var error = await Assert.ThrowsAsync<CourtWorkflowException>(() => service.CreateRunAsync(userId,
                    new DhcAssistedStartRequest("Recommended", null), default));
                Assert.Contains("100 cases", error.Message);
                Assert.Empty(db.DhcAssistedSyncRuns);
                var selectedError = await Assert.ThrowsAsync<CourtWorkflowException>(() => service.CreateRunAsync(userId,
                    new DhcAssistedStartRequest("Selected", caseIds), default));
                Assert.Contains("100 cases", selectedError.Message);
                Assert.Empty(db.DhcAssistedSyncRuns);
            }
        }
    }

    [Fact]
    public async Task ExpiryAfterStatusBatch_PreservesCompletedOfficialEvidence()
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            var clock = new ManualTime();
            var config = new ConfigurationBuilder().Build();
            var fake = new FakeHandler();
            var coordinator = new DelhiHighCourtAssistedCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(), config,
                new Lifetime(), NullLogger<DelhiHighCourtAssistedCoordinator>.Instance,
                () => new DelhiHighCourtAssistedSession(config, _ => fake), clock);
            var runId = await coordinator.StartAsync(userId,
                new DhcAssistedStartRequest("Selected", caseIds), default);
            Assert.True(await coordinator.SubmitHumanAnswerAsync(runId, userId, "typed-by-officer", default));
            await WaitForRunStatusAsync(provider, runId, DhcAssistedRunStatus.ReadyForOrders);
            clock.Advance(TimeSpan.FromMinutes(16));
            await coordinator.SweepExpiredAsync();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            Assert.Equal(DhcAssistedRunStatus.Interrupted,
                (await db.DhcAssistedSyncRuns.SingleAsync(x => x.Id == runId)).Status);
            Assert.Single(await db.CourtExternalCaseStatusObservations.ToListAsync());
            Assert.Equal(DhcAssistedItemStatus.StatusCaptured,
                (await db.DhcAssistedSyncItems.SingleAsync(x => x.RunId == runId)).Status);
            var statusRequests = fake.StatusLookups;
            var allRequests = fake.Requests.Count;
            await coordinator.ResumeAsync(runId, userId, default);
            Assert.Null(coordinator.ActiveRunId);
            Assert.Equal(allRequests, fake.Requests.Count); // No new status form or CAPTCHA request.
            db.ChangeTracker.Clear();
            Assert.Equal(DhcAssistedRunStatus.ReadyForOrders,
                (await db.DhcAssistedSyncRuns.SingleAsync(x => x.Id == runId)).Status);
            Assert.Single(await db.CourtExternalCaseStatusObservations.ToListAsync());
            Assert.Equal(statusRequests, fake.StatusLookups);
            await coordinator.FinishSessionAsync(runId, userId, default);
        }
    }

    [Fact]
    public async Task OrderPhase_ChangedIdentityNeverIssuesOrderLookup()
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            var config = new ConfigurationBuilder().Build();
            var fake = new FakeHandler();
            var coordinator = new DelhiHighCourtAssistedCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(), config,
                new Lifetime(), NullLogger<DelhiHighCourtAssistedCoordinator>.Instance,
                () => new DelhiHighCourtAssistedSession(config, _ => fake));
            var runId = await coordinator.StartAsync(userId,
                new DhcAssistedStartRequest("Selected", caseIds), default);
            Assert.True(await coordinator.SubmitHumanAnswerAsync(runId, userId, "typed-by-officer", default));
            await WaitForRunStatusAsync(provider, runId, DhcAssistedRunStatus.ReadyForOrders);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
                (await db.CourtCases.SingleAsync(x => x.Id == caseIds[0])).CaseNumber = "W.P.(C) 7004/2026";
                await db.SaveChangesAsync();
            }
            await coordinator.StartOrdersAsync(runId, userId, default);
            Assert.True(await coordinator.SubmitHumanAnswerAsync(runId, userId, "typed-by-officer", default));
            await WaitForRunStatusAsync(provider, runId, DhcAssistedRunStatus.Completed);
            using var verifyScope = provider.CreateScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<LacDbContext>();
            Assert.Equal("CaseChangedSinceQueue",
                (await verify.DhcAssistedSyncItems.SingleAsync(x => x.RunId == runId)).FailureCode);
            Assert.Single(await verify.CourtExternalCaseStatusObservations.ToListAsync());
            Assert.Empty(verify.CourtExternalOrderObservations);
            Assert.Null(fake.SubmittedOrderForm);
        }
    }

    [Fact]
    public async Task ApplicationInterruption_AfterCompletedStatus_RestoresDecisionWithoutNewChallenge()
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            var config = new ConfigurationBuilder().Build();
            var fake = new FakeHandler();
            Guid runId;
            using (var setup = provider.CreateScope())
            {
                var service = setup.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>();
                var db = setup.ServiceProvider.GetRequiredService<LacDbContext>();
                var run = await service.CreateRunAsync(userId,
                    new DhcAssistedStartRequest("Selected", caseIds), default);
                var item = Assert.Single(run.Items);
                await service.ProcessStatusAsync(run, item, StatusResult, default);
                run.Status = DhcAssistedRunStatus.ReadyForOrders; // Durable state at app shutdown.
                await db.SaveChangesAsync();
                runId = run.Id;
            }
            var restarted = new DelhiHighCourtAssistedCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(), config,
                new Lifetime(), NullLogger<DelhiHighCourtAssistedCoordinator>.Instance,
                () => new DelhiHighCourtAssistedSession(config, _ => fake));
            await restarted.ResumeAsync(runId, userId, default);
            Assert.Null(restarted.ActiveRunId);
            Assert.Empty(fake.Requests);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
                Assert.Equal(DhcAssistedRunStatus.ReadyForOrders,
                    (await db.DhcAssistedSyncRuns.SingleAsync(x => x.Id == runId)).Status);
                Assert.Single(await db.CourtExternalCaseStatusObservations.ToListAsync());
                Assert.Equal(runId, await scope.ServiceProvider
                    .GetRequiredService<DelhiHighCourtAssistedService>()
                    .RecoverableRunIdAsync(userId, default));
            }
            await restarted.StartOrdersAsync(runId, userId, default);
            Assert.Equal("Order search", (await restarted.ChallengeAsync(runId, userId, default)).Operation);
            await restarted.CancelAsync(runId, userId, default);
        }
    }

    [Theory]
    [InlineData(false, "Case status")]
    [InlineData(true, "Order search")]
    public async Task InterruptedIncompletePhase_RequiresFreshCorrectChallenge(bool orders, string operation)
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            var config = new ConfigurationBuilder().Build();
            var fake = new FakeHandler();
            Guid runId;
            using (var setup = provider.CreateScope())
            {
                var service = setup.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>();
                var db = setup.ServiceProvider.GetRequiredService<LacDbContext>();
                var run = await service.CreateRunAsync(userId,
                    new DhcAssistedStartRequest("Selected", caseIds), default);
                if (orders)
                {
                    await service.ProcessStatusAsync(run, Assert.Single(run.Items), StatusResult, default);
                    run.Phase = DhcAssistedPhase.OrderLookup;
                }
                run.Status = DhcAssistedRunStatus.Interrupted;
                await db.SaveChangesAsync();
                runId = run.Id;
            }
            var restarted = new DelhiHighCourtAssistedCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(), config,
                new Lifetime(), NullLogger<DelhiHighCourtAssistedCoordinator>.Instance,
                () => new DelhiHighCourtAssistedSession(config, _ => fake));
            await restarted.ResumeAsync(runId, userId, default);
            Assert.Equal(operation, (await restarted.ChallengeAsync(runId, userId, default)).Operation);
            await restarted.CancelAsync(runId, userId, default);
        }
    }

    [Fact]
    public async Task ExactOfficialDisposed_AutomaticallyUpdatesWithAudit()
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>();
            var run = new DhcAssistedSyncRun { StartedByUserId = userId, StartedAt = new Clock().GetUtcNow() };
            var item = new DhcAssistedSyncItem
            { CourtCaseId = caseIds[0], NormalizedCaseIdentity = "delhihighcourt|wpc|7003|2026",
                QueueOrder = 0, Status = DhcAssistedItemStatus.CheckingStatus };
            run.Items.Add(item); db.DhcAssistedSyncRuns.Add(run);
            await db.SaveChangesAsync();
            await service.ProcessStatusAsync(run, item, StatusResult.Replace("[Pending]", "[Disposed]"), default);
            var observation = await db.CourtExternalCaseStatusObservations.SingleAsync();
            db.ChangeTracker.Clear();
            Assert.Equal("Disposed", (await db.CourtCases.SingleAsync(x => x.Id == caseIds[0])).CurrentStatus);
            var history = await db.CourtCaseEvents.SingleAsync(x => x.CourtCaseId == caseIds[0]);
            Assert.Equal(CourtCaseAction.StatusChanged, history.Action);
            Assert.Equal("Pending", history.OldStatus);
            Assert.Equal("Disposed", history.NewStatus);
            Assert.Equal(userId, history.ActorUserId);
            Assert.Contains(observation.Id.ToString(), history.Notes);
            Assert.Contains("Exact DHC case-status result", history.Notes);
            Assert.Single(await db.CourtExternalAssistedDecisions.Where(x => x.ObservationId == observation.Id).ToListAsync());
            Assert.Equal(DhcAssistedEvidenceStatus.Accepted, observation.Status);
        }
    }

    [Fact]
    public async Task MultipleOfficialRows_DoNotAutomaticallyDisposeEvenWithOneExactRow()
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>();
            var run = new DhcAssistedSyncRun { StartedByUserId = userId, StartedAt = new Clock().GetUtcNow() };
            var item = new DhcAssistedSyncItem { CourtCaseId = caseIds[0],
                NormalizedCaseIdentity = "delhihighcourt|wpc|7003|2026", QueueOrder = 0,
                Status = DhcAssistedItemStatus.CheckingStatus };
            run.Items.Add(item); db.DhcAssistedSyncRuns.Add(run);
            await db.SaveChangesAsync();
            var ambiguous = """
                {"data":[{"ctype":"W.P.(C) 7003/2026 [Disposed]","pet":"A v B","orderdate":"07.10.2026"},
                {"ctype":"W.P.(C) 8000/2026 [Pending]","pet":"C v D","orderdate":"07.10.2026"}]}
                """;
            await service.ProcessStatusAsync(run, item, ambiguous, default);
            db.ChangeTracker.Clear();
            Assert.Equal("Pending", (await db.CourtCases.SingleAsync(x => x.Id == caseIds[0])).CurrentStatus);
            Assert.Equal(DhcAssistedEvidenceStatus.NeedsReview,
                (await db.CourtExternalCaseStatusObservations.SingleAsync()).Status);
            Assert.Empty(db.CourtCaseEvents);
        }
    }

    [Fact]
    public async Task DuplicateLocalIdentity_DoesNotAutomaticallyDispose()
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>();
            db.CourtCases.Add(new CourtCase { CaseNumber = "W.P.(C) 7003/2026",
                CourtName = "Delhi High Court", CurrentStatus = "Pending" });
            var run = new DhcAssistedSyncRun { StartedByUserId = userId, StartedAt = new Clock().GetUtcNow() };
            var item = new DhcAssistedSyncItem { CourtCaseId = caseIds[0],
                NormalizedCaseIdentity = "delhihighcourt|wpc|7003|2026", QueueOrder = 0,
                Status = DhcAssistedItemStatus.CheckingStatus };
            run.Items.Add(item); db.DhcAssistedSyncRuns.Add(run);
            await db.SaveChangesAsync();
            await service.ProcessStatusAsync(run, item, StatusResult.Replace("[Pending]", "[Disposed]"), default);
            db.ChangeTracker.Clear();
            Assert.Equal("Pending", (await db.CourtCases.SingleAsync(x => x.Id == caseIds[0])).CurrentStatus);
            Assert.Equal(DhcAssistedEvidenceStatus.NeedsReview,
                (await db.CourtExternalCaseStatusObservations.SingleAsync()).Status);
        }
    }

    [Theory]
    [InlineData("Archived")]
    [InlineData("Stay")]
    public async Task UnknownOfficialStatus_CannotMutateCanonical(string rawStatus)
    {
        var (provider, userId, caseIds) = await HarnessAsync(1);
        await using (provider)
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>();
            var run = new DhcAssistedSyncRun { StartedByUserId = userId, StartedAt = new Clock().GetUtcNow() };
            var item = new DhcAssistedSyncItem
            { CourtCaseId = caseIds[0], NormalizedCaseIdentity = "delhihighcourt|wpc|7003|2026",
                QueueOrder = 0, Status = DhcAssistedItemStatus.CheckingStatus };
            run.Items.Add(item); db.DhcAssistedSyncRuns.Add(run);
            await db.SaveChangesAsync();
            await service.ProcessStatusAsync(run, item, StatusResult.Replace("[Pending]", $"[{rawStatus}]"), default);
            var observation = await db.CourtExternalCaseStatusObservations.SingleAsync();
            await Assert.ThrowsAsync<CourtWorkflowException>(() =>
                service.ConfirmCanonicalStatusAsync(observation.Id, "Attempt confirmation", userId, default));
            db.ChangeTracker.Clear();
            Assert.Equal("Pending", (await db.CourtCases.SingleAsync(x => x.Id == caseIds[0])).CurrentStatus);
            Assert.Empty(db.CourtCaseEvents);
        }
    }
}
