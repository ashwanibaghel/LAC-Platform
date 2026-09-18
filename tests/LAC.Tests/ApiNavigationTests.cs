using System.Net.Http.Json;
using System.Security.Claims;
using Claim = System.Security.Claims.Claim;
using System.Text.Encodings.Web;
using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace LAC.Tests;

public sealed class ApiNavigationTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    private readonly ApiFactory _factory;

    public ApiNavigationTests(ApiFactory factory) { _factory = factory; _client = factory.CreateClient(); }

    [Fact]
    public async Task Unknown_api_route_is_not_served_as_the_spa()
    {
        using var response = await _client.GetAsync("/api/does-not-exist");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Document_intelligence_health_is_read_only_and_reports_configuration()
    {
        using var response = await _client.GetAsync("/api/health/document-intelligence");
        Assert.True(response.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("enabled", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pythonExists", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Matters_inherit_award_core_documents_without_creating_document_copies()
    {
        var village = await _client.GetFromJsonAsync<PageResponse<VillageListItem>>("/api/villages?page=0&pageSize=1");
        var villageId = Assert.Single(village!.Items).Id;
        var award = new Award { AwardNumber = "CORE-TEST" };
        var document = new Document { DocumentType = "NM", OriginalFileName = "nm.pdf", StoragePath = "nm.pdf" };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Add(award); db.Add(new AwardVillage { Award = award, VillageId = villageId });
            db.Add(document); db.Add(new DocumentAward { Award = award, Document = document, CoreDocumentRole = "NM" });
            await db.SaveChangesAsync();
        }
        async Task<Guid> Create(string title)
        {
            using var response = await _client.PostAsJsonAsync($"/api/villages/{villageId}/matters", new { title, matterType = "Court Case", status = "Open", awardId = award.Id });
            response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        }
        var first = await Create("Dharambir"); var second = await Create("Second matter");
        var firstDetail = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{first}");
        var secondDetail = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{second}");
        Assert.Equal(document.Id.ToString(), firstDetail.GetProperty("award").GetProperty("documents")[0].GetProperty("documentId").GetString());
        Assert.Equal(document.Id.ToString(), secondDetail.GetProperty("award").GetProperty("documents")[0].GetProperty("documentId").GetString());
        using var verify = _factory.Services.CreateScope(); Assert.Equal(1, await verify.ServiceProvider.GetRequiredService<LacDbContext>().Documents.CountAsync(x => x.Id == document.Id));
    }

    [Fact]
    public async Task Core_document_upload_links_the_single_document_to_every_award_village()
    {
        Guid awardId; Guid firstVillageId; Guid secondVillageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var first = await db.Villages.OrderBy(x => x.Name).FirstAsync();
            var second = new Village { Name = "Core document second village", SubDivisionId = first.SubDivisionId };
            var award = new Award { AwardNumber = "MULTI-VILLAGE-CORE" };
            db.Add(second); db.Add(award); db.AddRange(new AwardVillage { Award = award, VillageId = first.Id }, new AwardVillage { Award = award, Village = second });
            await db.SaveChangesAsync(); awardId = award.Id; firstVillageId = first.Id; secondVillageId = second.Id;
        }
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(new MemoryStream("%PDF-test"u8.ToArray())), "file", "source.pdf");
        using var response = await _client.PostAsync($"/api/awards/{awardId}/core-documents?role=NM", content);
        response.EnsureSuccessStatusCode();
        var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("documentId").GetGuid();
        using var verify = _factory.Services.CreateScope(); var check = verify.ServiceProvider.GetRequiredService<LacDbContext>();
        var links = await check.DocumentVillages.Where(x => x.DocumentId == created).Select(x => x.VillageId).ToListAsync();
        Assert.Equal(new[] { firstVillageId, secondVillageId }.Order(), links.Order());
        Assert.Equal(1, await check.Documents.CountAsync(x => x.Id == created));
        await verify.ServiceProvider.GetRequiredService<IDocumentStorage>().DeleteAsync((await check.Documents.SingleAsync(x => x.Id == created)).StoragePath, CancellationToken.None);
    }

    [Fact]
    public async Task Matter_can_link_existing_village_document_but_rejects_unrelated_document()
    {
        Guid matterId; Guid allowedId; Guid unrelatedId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var village = await db.Villages.FirstAsync();
            var matter = new Matter { VillageId = village.Id, Title = "Matter document test" }; var allowed = new Document { OriginalFileName = "allowed.pdf", StoragePath = "allowed.pdf" }; var unrelated = new Document { OriginalFileName = "other.pdf", StoragePath = "other.pdf" };
            db.AddRange(matter, allowed, unrelated); db.Add(new DocumentVillage { Document = allowed, VillageId = village.Id }); await db.SaveChangesAsync(); matterId = matter.Id; allowedId = allowed.Id; unrelatedId = unrelated.Id;
        }
        using var allowedResponse = await _client.PostAsJsonAsync($"/api/matters/{matterId}/documents/link", new { documentId = allowedId, role = "Application" });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, allowedResponse.StatusCode);
        using var rejectedResponse = await _client.PostAsJsonAsync($"/api/matters/{matterId}/documents/link", new { documentId = unrelatedId, role = "Other" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, rejectedResponse.StatusCode);
        var linked = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{matterId}/documents");
        Assert.Equal(allowedId, linked[0].GetProperty("documentId").GetGuid());
    }

    [Fact]
    public async Task Matter_upload_rejects_unsupported_files_before_storage()
    {
        Guid matterId; using (var scope = _factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var village = await db.Villages.FirstAsync(); var matter = new Matter { VillageId = village.Id, Title = "Upload validation" }; db.Add(matter); await db.SaveChangesAsync(); matterId = matter.Id; }
        using var content = new MultipartFormDataContent(); content.Add(new StreamContent(new MemoryStream([1, 2, 3])), "file", "unsafe.exe");
        using var response = await _client.PostAsync($"/api/matters/{matterId}/documents?role=Other", content);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        using var verify = _factory.Services.CreateScope(); Assert.Empty(await verify.ServiceProvider.GetRequiredService<LacDbContext>().MatterDocuments.Where(x => x.MatterId == matterId).ToListAsync());
    }

    [Fact]
    public async Task Matter_drafts_are_isolated_structured_and_revision_safe()
    {
        Guid villageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            villageId = (await db.Villages.FirstAsync()).Id;
        }
        async Task<Guid> CreateMatter(string title)
        {
            using var response = await _client.PostAsJsonAsync($"/api/villages/{villageId}/matters", new { title, matterType = "Court Case", status = "Open" });
            response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        }
        var firstMatter = await CreateMatter("Draft matter A"); var secondMatter = await CreateMatter("Draft matter B");
        using var invalidType = await _client.PostAsJsonAsync($"/api/matters/{firstMatter}/drafts", new { title = "Bad", draftType = "Memo" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalidType.StatusCode);
        using var created = await _client.PostAsJsonAsync($"/api/matters/{firstMatter}/drafts", new { title = "Reply to Court", draftType = "Letter" });
        created.EnsureSuccessStatusCode(); var draftId = (await created.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var firstList = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{firstMatter}/drafts"); var secondList = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{secondMatter}/drafts");
        Assert.Single(firstList.EnumerateArray()); Assert.Empty(secondList.EnumerateArray());
        const string content = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Reply to Court\",\"marks\":[{\"type\":\"bold\"}]}]}]}";
        var update = new { title = "Updated reply", contentJson = content, pageSize = "A4", orientation = "Landscape", marginTopMm = 18, marginRightMm = 19, marginBottomMm = 20, marginLeftMm = 21, expectedRevision = 0 };
        using var saved = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", update); saved.EnsureSuccessStatusCode();
        var savedBody = await saved.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(1, savedBody.GetProperty("revision").GetInt32());
        var reloaded = await _client.GetFromJsonAsync<JsonElement>($"/api/matter-drafts/{draftId}"); Assert.Equal(content, reloaded.GetProperty("contentJson").GetString()); Assert.Equal("Updated reply", reloaded.GetProperty("title").GetString());
        using var stale = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", update); Assert.Equal(System.Net.HttpStatusCode.Conflict, stale.StatusCode);
        using var unsafeContent = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new { title = "Unsafe", contentJson = "{\"type\":\"doc\",\"content\":[{\"type\":\"image\",\"attrs\":{\"src\":\"data:image/png;base64,x\"}}]}", pageSize = "A4", orientation = "Portrait", marginTopMm = 20, marginRightMm = 20, marginBottomMm = 20, marginLeftMm = 20, expectedRevision = 1 });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, unsafeContent.StatusCode);
        const string literalText = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Literal <script> and data:image text are not executable.\"}]}]}";
        using var ordinaryText = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new { title = "Plain text", contentJson = literalText, pageSize = "A4", orientation = "Portrait", marginTopMm = 20, marginRightMm = 20, marginBottomMm = 20, marginLeftMm = 20, expectedRevision = 1 });
        ordinaryText.EnsureSuccessStatusCode();
        using var unknownNode = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new { title = "Unknown", contentJson = "{\"type\":\"doc\",\"content\":[{\"type\":\"customWidget\"}]}", pageSize = "A4", orientation = "Portrait", marginTopMm = 20, marginRightMm = 20, marginBottomMm = 20, marginLeftMm = 20, expectedRevision = 2 });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, unknownNode.StatusCode);
        using var oversized = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new { title = "Too large", contentJson = new string('x', 1_000_001), pageSize = "A4", orientation = "Portrait", marginTopMm = 20, marginRightMm = 20, marginBottomMm = 20, marginLeftMm = 20, expectedRevision = 2 });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, oversized.StatusCode);
        using var legal = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new { title = "Legal letter", contentJson = literalText, pageSize = "Legal", orientation = "Landscape", marginTopMm = 15, marginRightMm = 16, marginBottomMm = 17, marginLeftMm = 18, expectedRevision = 2 });
        legal.EnsureSuccessStatusCode();
        const string pagedContent = "{\"type\":\"doc\",\"content\":[{\"type\":\"draftPage\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Paged editor content\"}]}]}]}";
        using var paged = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new { title = "Paged letter", contentJson = pagedContent, pageSize = "Legal", orientation = "Landscape", marginTopMm = 15, marginRightMm = 16, marginBottomMm = 17, marginLeftMm = 18, expectedRevision = 3 });
        paged.EnsureSuccessStatusCode();
        using var notingCreated = await _client.PostAsJsonAsync($"/api/matters/{firstMatter}/drafts", new { title = "Office noting", draftType = "Noting" });
        notingCreated.EnsureSuccessStatusCode(); var notingId = (await notingCreated.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var noting = await _client.GetFromJsonAsync<JsonElement>($"/api/matter-drafts/{notingId}"); Assert.Equal(25m, noting.GetProperty("marginTopMm").GetDecimal()); Assert.Equal(25m, noting.GetProperty("marginLeftMm").GetDecimal());
        using var alteredNoting = await _client.PutAsJsonAsync($"/api/matter-drafts/{notingId}", new { title = "Office noting", contentJson = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\"}]}", pageSize = "Legal", orientation = "Landscape", marginTopMm = 1, marginRightMm = 1, marginBottomMm = 1, marginLeftMm = 1, expectedRevision = 0 });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, alteredNoting.StatusCode);
    }

    [Fact]
    public async Task Nm_owner_review_projects_summary_money_states_and_keeps_running_total_separate()
    {
        var document = new Document { DocumentType = "NM", OriginalFileName = "review.pdf", StoragePath = "review.pdf" };
        var village = new Village { Name = "Review village" };
        var nm = new NmDocument { Document = document, Village = village, Status = NmReviewStatus.NeedsReview };
        var session = new NmSemanticAnalysisSession { NmDocument = nm, ParserVersion = "test", SourcePagesJson = "[1]", Status = NmSemanticSessionStatus.Completed };
        var owner = new NmSemanticOwnerBlock { AnalysisSession = session, SourceSequence = 1, PageStart = 1, PageEnd = 1, RecordedNameRaw = "Review owner", ShareRaw = "1/3" };
        var group = new NmSemanticParcelGroup { OwnerBlock = owner, SourceSequence = 1, ParcelCountAsRecorded = "3", TotalAreaAsRecorded = "6-3" };
        var parcel = new NmSemanticParcelEntry { ParcelGroup = group, SourceSequence = 1, RawKhasraText = "26/21/3", RawAreaText = "6-3", LandClassRaw = "A", SourcePage = 1 };
        var land = new NmSemanticCompensationComponent { OwnerBlock = owner, ComponentType = "land_compensation", RawAmountText = "Rs590,229.17", Amount = 590229.17m, SourcePage = 1, SemanticState = "MappedByColumn" };
        var structure = new NmSemanticCompensationComponent { OwnerBlock = owner, ComponentType = "structure_compensation", RawAmountText = "Rs0.00", Amount = 0m, SourcePage = 1, SemanticState = "MappedByColumn" };
        var running = new NmSemanticCompensationComponent { OwnerBlock = owner, ComponentType = "running_total", RawAmountText = "Rs999.00", Amount = 999m, SourcePage = 1, SemanticState = "RunningTotal" };
        using (var scope = _factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); db.AddRange(session, owner, group, parcel, land, structure, running); await db.SaveChangesAsync(); }

        using var response = await _client.GetAsync($"/api/nm-semantic-sessions/{session.Id}/review-workspace");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var card = json.RootElement.GetProperty("owners")[0];
        Assert.Equal("3", card.GetProperty("summary").GetProperty("parcelCountAsRecorded").GetString());
        Assert.Equal("6-3", card.GetProperty("summary").GetProperty("totalAreaAsRecorded").GetString());
        var compensation = card.GetProperty("compensation");
        var projectedLand = compensation.EnumerateArray().Single(item => item.GetProperty("componentType").GetString() == "land_compensation");
        var projectedStructure = compensation.EnumerateArray().Single(item => item.GetProperty("componentType").GetString() == "structure_compensation");
        var projectedFinal = compensation.EnumerateArray().Single(item => item.GetProperty("componentType").GetString() == "grand_total");
        Assert.Equal("Rs590,229.17", projectedLand.GetProperty("rawAmountText").GetString()); Assert.Equal(590229.17m, projectedLand.GetProperty("amount").GetDecimal());
        Assert.Equal(0m, projectedStructure.GetProperty("amount").GetDecimal()); Assert.True(projectedStructure.GetProperty("isResolved").GetBoolean());
        Assert.Equal("NeedsReview", projectedFinal.GetProperty("semanticState").GetString()); Assert.Equal(JsonValueKind.Null, projectedFinal.GetProperty("amount").ValueKind);
        Assert.DoesNotContain(compensation.EnumerateArray(), item => item.GetProperty("componentType").GetString() == "running_total");
        Assert.Equal("Rs999.00", card.GetProperty("runningTotals")[0].GetProperty("rawAmountText").GetString());
    }

    [Fact]
    public async Task Village_khasra_award_and_back_to_khasra_use_canonical_detail_endpoints()
    {
        var districts = await _client.GetFromJsonAsync<List<DistrictListItem>>("/api/districts");
        var district = Assert.Single(districts!);
        var districtDetail = await _client.GetFromJsonAsync<DistrictDetail>($"/api/districts/{district.Id}");
        var subdivision = Assert.Single(districtDetail!.SubDivisions, item => item.Name == "Matiala");
        var subdivisionDetail = await _client.GetFromJsonAsync<SubDivisionDetail>($"/api/subdivisions/{subdivision.Id}?page=0&pageSize=25");
        var village = Assert.Single(subdivisionDetail!.Villages.Items, item => item.Name == "GALIB PUR");

        var khasras = await _client.GetFromJsonAsync<PageResponse<KhasraListItem>>($"/api/villages/{village.Id}/khasras?page=0&pageSize=25&q=22%2F%2F2");
        var khasra = Assert.Single(khasras!.Items);
        var khasraDetail = await _client.GetFromJsonAsync<KhasraDetail>($"/api/khasras/{khasra.Id}");
        var awardLink = Assert.Single(khasraDetail!.Awards);
        var awardDetail = await _client.GetFromJsonAsync<AwardDetail>($"/api/awards/{awardLink.Id}");

        Assert.Contains(awardDetail!.Khasras, item => item.Id == khasra.Id);
        Assert.Equal("22//2", khasraDetail.DisplayNumber);
    }

    [Fact]
    public async Task Khasra_search_includes_village_context_and_award_filter_is_paged()
    {
        var search = await _client.GetFromJsonAsync<List<SearchResultItem>>("/api/search?q=22%2F%2F2");
        var khasra = Assert.Single(search!, item => item.Type == "Khasra");
        Assert.Equal("GALIB PUR", khasra.Context);
        Assert.StartsWith("/khasras/", khasra.Route, StringComparison.Ordinal);

        var awards = await _client.GetFromJsonAsync<PageResponse<AwardListItem>>("/api/awards?page=0&pageSize=1&q=DEMO-AWARD");
        Assert.NotNull(awards);
        Assert.Equal(1, awards!.PageSize);
        Assert.Equal(1, awards.TotalCount);
        Assert.Single(awards.Items);
        Assert.Equal("DEMO-AWARD-01", awards.Items[0].AwardNumber);
    }

    [Fact]
    public async Task Lr_register_exposes_preserved_source_rows_progress_and_review_queue()
    {
        var villages = await _client.GetFromJsonAsync<PageResponse<VillageListItem>>("/api/villages?page=0&pageSize=100&q=GALIB");
        var village = Assert.Single(villages!.Items, item => item.Name == "GALIB PUR");
        var registers = await _client.GetFromJsonAsync<List<VillageLrListItem>>($"/api/villages/{village.Id}/lrs");
        var register = Assert.Single(registers!);

        var detail = await _client.GetFromJsonAsync<VillageLrDetail>($"/api/village-lrs/{register.Id}");
        var rows = await _client.GetFromJsonAsync<PageResponse<LrEntryDetailItem>>($"/api/village-lrs/{register.Id}/entries?page=0&pageSize=25");
        var progress = await _client.GetFromJsonAsync<LrProgress>($"/api/villages/{village.Id}/lr-progress");
        var review = await _client.GetFromJsonAsync<PageResponse<LrReviewItem>>("/api/lr-review?status=Verified&page=0&pageSize=25");

        Assert.Equal(1, detail!.TotalRows);
        Assert.Equal("22//2 min", Assert.Single(rows!.Items).RawKhasraText);
        Assert.Equal(1, progress!.TotalRows);
        Assert.Single(review!.Items);
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"api-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = "testadmin",
                ["BootstrapAdmin:Password"] = "TestAdminPass!789",
                ["BootstrapAdmin:DisplayName"] = "Test Administrator"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LacDbContext>>();
            services.RemoveAll<LacDbContext>();
            services.AddDbContext<LacDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        });
    }
}

public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, SeedData.BootstrapAdminId.ToString()),
            new(ClaimTypes.Name, "admin"),
            new("username", "admin"),
            new("display_name", "System Administrator"),
            new(ClaimTypes.Role, "SYSTEM_ADMIN"),
            new("designation_code", "ADM"),
            new("workstream_code", "LAND_ACQUISITION"),
            new("workstream_code", "LAND_RECORDS")
        };
        foreach (var perm in PermissionCodes.All)
        {
            claims.Add(new Claim("permission", perm.Code));
        }
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
