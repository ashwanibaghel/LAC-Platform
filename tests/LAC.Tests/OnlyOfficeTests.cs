using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
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
using Claim = System.Security.Claims.Claim;

namespace LAC.Tests;

public sealed class OnlyOfficeTests : IDisposable
{
    private readonly OfficeFactory factory = new();
    private HttpClient Client => factory.CreateClient();
    private OnlyOfficeTokens Tokens => factory.Services.GetRequiredService<OnlyOfficeTokens>();
    public void Dispose() => factory.Dispose();

    private async Task<Guid> DraftAsync(bool legacy = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var matter = new Matter { Title = "Office test", VillageId = await db.Villages.Select(x => x.Id).FirstAsync(),
            WorkstreamId = await db.Workstreams.Select(x => x.Id).FirstAsync() };
        db.Matters.Add(matter);
        await db.SaveChangesAsync();
        if (legacy)
        {
            var draft = new MatterDraft { MatterId = matter.Id, Title = "Legacy", ContentJson = Content("Original & unchanged text") };
            db.MatterDrafts.Add(draft);
            await db.SaveChangesAsync();
            return draft.Id;
        }
        using var response = await Client.PostAsJsonAsync($"/api/matters/{matter.Id}/drafts", new { title = "Letter", draftType = "Letter" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static string Content(string text) => JsonSerializer.Serialize(new { type = "doc", content = new[] {
        new { type = "paragraph", content = new[] { new { type = "text", text } } } } });
    private async Task<JsonElement> Config(Guid id) => await Client.GetFromJsonAsync<JsonElement>($"/api/matter-drafts/{id}/office-config");
    private async Task<MatterDraft> Read(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LacDbContext>().MatterDrafts.AsNoTracking()
            .Include(x => x.OfficeDocument).SingleAsync(x => x.Id == id);
    }
    private async Task<int> Callback(Guid id, string key, int status, string? url = "http://localhost:8082/cache/output.docx")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/matter-drafts/{id}/onlyoffice-callback");
        request.Headers.Authorization = new("Bearer", Tokens.Sign(new { payload = new { key, status, url } }));
        // Deliberately untrusted outer fields: the endpoint must use only the signed payload.
        request.Content = JsonContent.Create(new { key = "wrong", status = 2, url = "http://untrusted.invalid/" });
        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetInt32();
    }

    [Fact]
    public async Task Creation_and_lazy_migration_produce_valid_docx_and_preserve_json()
    {
        var id = await DraftAsync();
        Assert.NotNull((await Read(id)).OfficeDocumentId);
        var legacy = await DraftAsync(true);
        Assert.Null((await Read(legacy)).OfficeDocumentId);
        var config = await Config(legacy);
        var draft = await Read(legacy);
        Assert.Equal(Content("Original & unchanged text"), draft.ContentJson);
        var url = new Uri(config.GetProperty("config").GetProperty("document").GetProperty("url").GetString()!);
        using var response = await Client.GetAsync(url.PathAndQuery);
        response.EnsureSuccessStatusCode();
        Assert.Equal(MatterDraftDocx.MimeType, response.Content.Headers.ContentType!.MediaType);
        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var package = WordprocessingDocument.Open(bytes, false);
        Assert.Empty(new OpenXmlValidator().Validate(package));
        Assert.Equal("Original & unchanged text", package.MainDocumentPart!.Document!.Body!.InnerText);
        var same = await Config(legacy);
        Assert.Equal(config.GetProperty("config").GetProperty("document").GetProperty("key").GetString(),
            same.GetProperty("config").GetProperty("document").GetProperty("key").GetString());
        Assert.Equal(draft.OfficeDocumentId, (await Read(legacy)).OfficeDocumentId);
    }

    [Fact]
    public async Task Force_save_preserves_key_final_save_rotates_and_retry_is_idempotent()
    {
        var id = await DraftAsync();
        var before = await Read(id);
        var key = OnlyOfficeDraftService.Key(before);
        factory.Download.Bytes = MakeDocx("Persisted office text");
        Assert.Equal(0, await Callback(id, key, 6));
        var forced = await Read(id);
        Assert.Equal(key, OnlyOfficeDraftService.Key(forced));
        Assert.Equal(before.Revision + 1, forced.Revision);
        Assert.Equal(before.OfficeDocument!.Version + 1, forced.OfficeDocument!.Version);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(factory.Download.Bytes)).ToLowerInvariant(), forced.OfficeDocument.Sha256Hash);
        using (var scope = factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            await using var file = await storage.OpenReadAsync(forced.OfficeDocument.StoragePath, default);
            using var output = new MemoryStream();
            await file!.CopyToAsync(output);
            Assert.Equal(factory.Download.Bytes, output.ToArray());
            Assert.Null(await storage.OpenReadAsync(before.OfficeDocument.StoragePath, default));
        }
        Assert.Equal(0, await Callback(id, key, 6));
        Assert.Equal(forced.Revision, (await Read(id)).Revision);
        factory.Download.Bytes = MakeDocx("A second change at the same force-save URL");
        Assert.Equal(0, await Callback(id, key, 6));
        var secondForce = await Read(id);
        Assert.Equal(forced.Revision + 1, secondForce.Revision);
        Assert.Equal(key, OnlyOfficeDraftService.Key(secondForce));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(factory.Download.Bytes)).ToLowerInvariant(), secondForce.OfficeDocument!.Sha256Hash);
        Assert.Equal(0, await Callback(id, key, 2));
        var final = await Read(id);
        Assert.Equal(before.OfficeKeyGeneration + 1, final.OfficeKeyGeneration);
        Assert.Equal(0, await Callback(id, key, 2));
        Assert.Equal(final.Revision, (await Read(id)).Revision);
        Assert.Equal(1, await Callback(id, key, 6));
        var reopened = await Config(id);
        Assert.Equal(OnlyOfficeDraftService.Key(final), reopened.GetProperty("config").GetProperty("document").GetProperty("key").GetString());
    }

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(4)] [InlineData(7)]
    public async Task Non_save_statuses_leave_last_good_document(int status)
    {
        var id = await DraftAsync();
        var before = await Read(id);
        Assert.Equal(0, await Callback(id, OnlyOfficeDraftService.Key(before), status));
        Assert.Equal(before.Revision, (await Read(id)).Revision);
        Assert.Equal(0, factory.Download.Requests);
    }

    [Theory]
    [InlineData("http://evil.invalid/file.docx")]
    [InlineData("http://localhost:8083/file.docx")]
    [InlineData("http://localhost:8082@evil.invalid/file.docx")]
    [InlineData("file:///etc/passwd")]
    public async Task Untrusted_callback_origins_are_rejected_before_network_access(string url)
    {
        var id = await DraftAsync();
        var before = await Read(id);
        Assert.Equal(1, await Callback(id, OnlyOfficeDraftService.Key(before), 2, url));
        Assert.Equal(0, factory.Download.Requests);
        Assert.Equal(before.OfficeDocument!.StoragePath, (await Read(id)).OfficeDocument!.StoragePath);
    }

    [Theory]
    [InlineData(200, false)] [InlineData(200, true)] [InlineData(500, false)] [InlineData(302, false)]
    public async Task Empty_invalid_failed_and_redirect_downloads_do_not_replace_file(int status, bool garbage)
    {
        var id = await DraftAsync();
        var before = await Read(id);
        factory.Download.Status = (HttpStatusCode)status;
        factory.Download.Bytes = garbage ? "not a docx"u8.ToArray() : [];
        Assert.Equal(1, await Callback(id, OnlyOfficeDraftService.Key(before), 2));
        var after = await Read(id);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.OfficeDocument!.StoragePath, after.OfficeDocument!.StoragePath);
        Assert.Equal(before.OfficeDocument.Sha256Hash, after.OfficeDocument.Sha256Hash);
    }

    [Fact]
    public async Task File_tokens_are_bound_to_purpose_document_draft_and_expiry()
    {
        var id = await DraftAsync();
        var draft = await Read(id);
        var token = Tokens.DownloadToken(id, draft.OfficeDocumentId!.Value);
        Assert.True(Tokens.CanDownload(token, id, draft.OfficeDocumentId.Value));
        Assert.False(Tokens.CanDownload(token, Guid.NewGuid(), draft.OfficeDocumentId.Value));
        Assert.False(Tokens.CanDownload(token, id, Guid.NewGuid()));
        Assert.False(Tokens.CanDownload(token + "x", id, draft.OfficeDocumentId.Value));
        var expired = Tokens.Sign(new { purpose = "onlyoffice-download", draftId = id, documentId = draft.OfficeDocumentId, exp = 1 });
        Assert.False(Tokens.CanDownload(expired, id, draft.OfficeDocumentId.Value));
        using var denied = await Client.GetAsync($"/api/matter-drafts/{id}/office-file?token={expired}");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var unsigned = await Client.PostAsJsonAsync($"/api/matter-drafts/{id}/onlyoffice-callback", new { key = OnlyOfficeDraftService.Key(draft), status = 2 });
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
    }

    [Fact]
    public async Task Config_requires_view_and_reflects_live_edit_permission_and_blocks_legacy_writes()
    {
        var id = await DraftAsync();
        var edit = (await Config(id)).GetProperty("config");
        Assert.Equal("edit", edit.GetProperty("editorConfig").GetProperty("mode").GetString());
        Assert.True(edit.GetProperty("document").GetProperty("permissions").GetProperty("edit").GetBoolean());
        var signed = Tokens.Verify(edit.GetProperty("token").GetString()!);
        Assert.Equal(edit.GetProperty("document").GetRawText(), signed.GetProperty("document").GetRawText());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var editPermission = await db.Permissions.SingleAsync(x => x.Code == PermissionCodes.DraftEdit);
        db.RolePermissions.RemoveRange(db.RolePermissions.Where(x => x.PermissionId == editPermission.Id));
        await db.SaveChangesAsync();
        var view = (await Config(id)).GetProperty("config");
        Assert.Equal("view", view.GetProperty("editorConfig").GetProperty("mode").GetString());
        Assert.False(view.GetProperty("document").GetProperty("permissions").GetProperty("edit").GetBoolean());
        var viewPermission = await db.Permissions.SingleAsync(x => x.Code == PermissionCodes.DraftView);
        db.RolePermissions.RemoveRange(db.RolePermissions.Where(x => x.PermissionId == viewPermission.Id));
        await db.SaveChangesAsync();
        using var denied = await Client.GetAsync($"/api/matter-drafts/{id}/office-config");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var anonymous = new HttpRequestMessage(HttpMethod.Get, $"/api/matter-drafts/{id}/office-config");
        anonymous.Headers.Add("X-Office-Test-Anonymous", "true");
        using var unauthenticated = await Client.SendAsync(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task Docx_authority_rejects_legacy_content_updates()
    {
        var id = await DraftAsync();
        var draft = await Read(id);
        using var response = await Client.PutAsJsonAsync($"/api/matter-drafts/{id}", new {
            title = "Overwrite", contentJson = Content("stale JSON"), pageSize = "A4", orientation = "Portrait",
            marginTopMm = 20, marginRightMm = 20, marginBottomMm = 20, marginLeftMm = 20, expectedRevision = draft.Revision });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(draft.ContentJson, (await Read(id)).ContentJson);
    }

    [Theory]
    [InlineData(MatterDraftType.Letter, 1134, 1134)]
    [InlineData(MatterDraftType.Noting, 1417, 1417)]
    public void Generated_document_has_valid_page_profile(MatterDraftType type, int top, int left)
    {
        using var bytes = MatterDraftDocx.Create(new MatterDraft { DraftType = type, ContentJson = Content("Text") });
        using var package = WordprocessingDocument.Open(bytes, false);
        Assert.Empty(new OpenXmlValidator().Validate(package));
        var section = package.MainDocumentPart!.Document!.Body!.GetFirstChild<SectionProperties>()!;
        Assert.Equal(11906U, section.GetFirstChild<PageSize>()!.Width!.Value);
        Assert.Equal(top, section.GetFirstChild<PageMargin>()!.Top!.Value);
        Assert.Equal((uint)left, section.GetFirstChild<PageMargin>()!.Left!.Value);
    }

    [Fact]
    public void Enabled_options_require_origins_and_secret()
    {
        Assert.True(new OnlyOfficeOptions().IsValid());
        Assert.False(new OnlyOfficeOptions { Enabled = true }.IsValid());
        Assert.False(new OnlyOfficeOptions { Enabled = true, BrowserUrl = "javascript:bad", AppExternalUrl = "http://localhost:5088", JwtSecret = new string('x', 32) }.IsValid());
    }

    private static byte[] MakeDocx(string text)
    {
        using var bytes = MatterDraftDocx.Create(new MatterDraft { ContentJson = Content(text) });
        return bytes.ToArray();
    }
}

public sealed class OfficeFactory : WebApplicationFactory<Program>
{
    private readonly string database = "office-tests-" + Guid.NewGuid();
    private readonly string root = Path.Combine(Path.GetTempPath(), "lac-office-tests-" + Guid.NewGuid());
    public OfficeDownloadHandler Download { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
            ["BootstrapAdmin:Username"] = Guid.NewGuid().ToString("N"),
            ["BootstrapAdmin:Password"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)) + "aA1!",
            ["BootstrapAdmin:DisplayName"] = "Office Test User",
            ["OnlyOffice:Enabled"] = "true", ["OnlyOffice:BrowserUrl"] = "http://localhost:8082",
            ["OnlyOffice:AppExternalUrl"] = "http://host.docker.internal:5088",
            ["OnlyOffice:JwtSecret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Storage:DocumentRoot"] = Path.Combine(root, "documents"),
            ["Storage:ExtractionRoot"] = Path.Combine(root, "extraction"), ["Storage:BackupRoot"] = Path.Combine(root, "backups")
        }));
        builder.ConfigureServices(services => {
            services.RemoveAll<DbContextOptions<LacDbContext>>();
            services.RemoveAll<LacDbContext>();
            services.AddDbContext<LacDbContext>(options => options.UseInMemoryDatabase(database));
            services.AddAuthentication(options => {
                options.DefaultAuthenticateScheme = "OfficeTest"; options.DefaultChallengeScheme = "OfficeTest";
                options.DefaultForbidScheme = "OfficeTest";
            }).AddScheme<AuthenticationSchemeOptions, OfficeTestAuth>("OfficeTest", _ => { });
            services.AddHttpClient("OnlyOffice").ConfigurePrimaryHttpMessageHandler(() => Download);
        });
    }
}

public sealed class OfficeDownloadHandler : HttpMessageHandler
{
    public byte[] Bytes { get; set; } = [];
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests++;
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(Bytes) });
    }
}

public sealed class OfficeTestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey("X-Office-Test-Anonymous")) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, SeedData.BootstrapAdminId.ToString()),
            new Claim("display_name", "Office Test User") }, "OfficeTest");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "OfficeTest")));
    }
}
