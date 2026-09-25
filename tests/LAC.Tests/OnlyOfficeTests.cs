using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using DocumentFormat.OpenXml;
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

    private async Task<Guid> DraftAsync(MatterDraftType draftType = MatterDraftType.Letter, bool legacy = false)
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
        using var response = await Client.PostAsJsonAsync($"/api/matters/{matter.Id}/drafts", new { title = draftType.ToString(), draftType = draftType.ToString() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static string Content(string text) => JsonSerializer.Serialize(new { type = "doc", content = new[] {
        new { type = "paragraph", content = new[] { new { type = "text", text } } } } });
    private async Task<JsonElement> Config(Guid id, int? viewportHeight = null) => await Client.GetFromJsonAsync<JsonElement>(
        $"/api/matter-drafts/{id}/office-config" + (viewportHeight.HasValue ? $"?viewportHeight={viewportHeight}" : ""));
    private async Task<MatterDraft> Read(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LacDbContext>().MatterDrafts.AsNoTracking()
            .Include(x => x.OfficeDocument).SingleAsync(x => x.Id == id);
    }
    private async Task<int> Callback(Guid id, string key, int status, string? url = "http://localhost:8082/cache/output.docx", bool anonymous = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/matter-drafts/{id}/onlyoffice-callback");
        if (anonymous) request.Headers.Add("X-Office-Test-Anonymous", "true");
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
        var legacy = await DraftAsync(legacy: true);
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
        Assert.Equal(before.OfficeDocumentId, forced.OfficeDocumentId);
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
        Assert.Equal(before.OfficeDocumentId, secondForce.OfficeDocumentId);
        Assert.Equal(forced.Revision + 1, secondForce.Revision);
        Assert.Equal(key, OnlyOfficeDraftService.Key(secondForce));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(factory.Download.Bytes)).ToLowerInvariant(), secondForce.OfficeDocument!.Sha256Hash);
        Assert.Equal(0, await Callback(id, key, 2));
        var final = await Read(id);
        Assert.Equal(before.OfficeDocumentId, final.OfficeDocumentId);
        Assert.Equal(before.OfficeKeyGeneration + 1, final.OfficeKeyGeneration);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(factory.Download.Bytes)).ToLowerInvariant(), final.OfficeDocument!.Sha256Hash);
        Assert.Equal(factory.Download.Bytes, await StoredBytes(final));
        Assert.Equal(0, await Callback(id, key, 2));
        Assert.Equal(final.Revision, (await Read(id)).Revision);
        Assert.Equal(1, await Callback(id, key, 6));
        var reopened = await Config(id);
        Assert.Equal(OnlyOfficeDraftService.Key(final), reopened.GetProperty("config").GetProperty("document").GetProperty("key").GetString());
        using var reopenedBytes = new MemoryStream(await StoredBytes(await Read(id)));
        using var reopenedPackage = WordprocessingDocument.Open(reopenedBytes, false);
        Assert.Contains("A second change at the same force-save URL", reopenedPackage.MainDocumentPart!.Document!.Body!.InnerText);
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
        var initial = await Read(id);
        factory.Download.Bytes = MakeDocx("Last known good content");
        Assert.Equal(0, await Callback(id, OnlyOfficeDraftService.Key(initial), 6));
        var before = await Read(id);
        var goodBytes = await StoredBytes(before);
        factory.Download.Status = (HttpStatusCode)status;
        factory.Download.Bytes = garbage ? "not a docx"u8.ToArray() : [];
        Assert.Equal(1, await Callback(id, OnlyOfficeDraftService.Key(before), 2));
        var after = await Read(id);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.OfficeDocument!.StoragePath, after.OfficeDocument!.StoragePath);
        Assert.Equal(before.OfficeDocument.Sha256Hash, after.OfficeDocument.Sha256Hash);
        Assert.Equal(goodBytes, await StoredBytes(after));
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
    public async Task Anonymous_onlyoffice_endpoints_bypass_global_cookie_filter_but_retain_their_own_security()
    {
        var id = await DraftAsync();
        var draft = await Read(id);
        var validToken = Tokens.DownloadToken(id, draft.OfficeDocumentId!.Value);

        using (var signedFile = new HttpRequestMessage(HttpMethod.Get,
                   $"/api/matter-drafts/{id}/office-file?token={Uri.EscapeDataString(validToken)}"))
        {
            signedFile.Headers.Add("X-Office-Test-Anonymous", "true");
            using var response = await Client.SendAsync(signedFile);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(MatterDraftDocx.MimeType, response.Content.Headers.ContentType!.MediaType);
        }

        var expiredToken = Tokens.Sign(new
        {
            purpose = "onlyoffice-download", draftId = id, documentId = draft.OfficeDocumentId, exp = 1
        });
        using (var expiredFile = new HttpRequestMessage(HttpMethod.Get,
                   $"/api/matter-drafts/{id}/office-file?token={Uri.EscapeDataString(expiredToken)}"))
        {
            expiredFile.Headers.Add("X-Office-Test-Anonymous", "true");
            using var response = await Client.SendAsync(expiredFile);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        factory.Download.Bytes = MakeDocx("Callback without browser cookie");
        Assert.Equal(0, await Callback(id, OnlyOfficeDraftService.Key(draft), 6, anonymous: true));

        using (var unsignedCallback = new HttpRequestMessage(HttpMethod.Post, $"/api/matter-drafts/{id}/onlyoffice-callback")
        {
            Content = JsonContent.Create(new { key = OnlyOfficeDraftService.Key(draft), status = 6 })
        })
        {
            unsignedCallback.Headers.Add("X-Office-Test-Anonymous", "true");
            using var response = await Client.SendAsync(unsignedCallback);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var config = new HttpRequestMessage(HttpMethod.Get, $"/api/matter-drafts/{id}/office-config"))
        {
            config.Headers.Add("X-Office-Test-Anonymous", "true");
            using var response = await Client.SendAsync(config);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var protectedEndpoint = new HttpRequestMessage(HttpMethod.Get, "/api/home"))
        {
            protectedEndpoint.Headers.Add("X-Office-Test-Anonymous", "true");
            using var response = await Client.SendAsync(protectedEndpoint);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Config_requires_view_and_reflects_live_edit_permission_and_blocks_legacy_writes()
    {
        var id = await DraftAsync();
        var edit = (await Config(id, 768)).GetProperty("config");
        var customization = edit.GetProperty("editorConfig").GetProperty("customization");
        Assert.True(customization.GetProperty("compactHeader").GetBoolean());
        Assert.True(customization.GetProperty("compactToolbar").GetBoolean());
        Assert.True(customization.GetProperty("toolbarHideFileName").GetBoolean());
        Assert.True(customization.GetProperty("hideRightMenu").GetBoolean());
        Assert.False(customization.GetProperty("hideRulers").GetBoolean());
        Assert.Equal(85, customization.GetProperty("zoom").GetInt32());
        Assert.Equal("http://localhost:5173/matters/" + (await Read(id)).MatterId,
            customization.GetProperty("goback").GetProperty("url").GetString());
        Assert.False(customization.GetProperty("goback").GetProperty("blank").GetBoolean());
        Assert.Equal("Back to Matter", customization.GetProperty("goback").GetProperty("text").GetString());
        Assert.Equal("line", customization.GetProperty("features").GetProperty("tabStyle").GetProperty("mode").GetString());
        Assert.False(customization.GetProperty("features").GetProperty("tabStyle").GetProperty("change").GetBoolean());
        Assert.Equal("toolbar", customization.GetProperty("features").GetProperty("tabBackground").GetProperty("mode").GetString());
        Assert.False(customization.GetProperty("features").GetProperty("tabBackground").GetProperty("change").GetBoolean());
        Assert.True(customization.GetProperty("forcesave").GetBoolean());
        Assert.True(customization.GetProperty("autosave").GetBoolean());
        Assert.False(edit.GetProperty("document").GetProperty("permissions").GetProperty("download").GetBoolean());
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

    [Fact]
    public void New_noting_document_has_the_delhi_lac_noting_v1_wordprocessing_profile()
    {
        using var bytes = MatterDraftDocx.Create(new MatterDraft { DraftType = MatterDraftType.Noting, ContentJson = Content("Text") });
        using var package = WordprocessingDocument.Open(bytes, false);
        Assert.Empty(new OpenXmlValidator().Validate(package));
        var section = package.MainDocumentPart!.Document!.Body!.GetFirstChild<SectionProperties>()!;
        var size = section.GetFirstChild<PageSize>()!;
        var margins = section.GetFirstChild<PageMargin>()!;
        Assert.Equal(12240U, size.Width!.Value);
        Assert.Equal(20160U, size.Height!.Value);
        Assert.Equal(PageOrientationValues.Portrait, size.Orient!.Value);
        Assert.Equal(1417, margins.Top!.Value);
        Assert.Equal(1134U, margins.Right!.Value);
        Assert.Equal(1134, margins.Bottom!.Value);
        Assert.Equal(2551U, margins.Left!.Value);
        Assert.Equal(0U, margins.Gutter!.Value);
        Assert.NotNull(package.MainDocumentPart.DocumentSettingsPart!.Settings!.GetFirstChild<MirrorMargins>());
    }

    [Fact]
    public void Letter_documents_retain_a4_legal_landscape_and_custom_margins_without_mirror_margins()
    {
        using var a4 = MatterDraftDocx.Create(new MatterDraft { DraftType = MatterDraftType.Letter, ContentJson = Content("A4") });
        using var legal = MatterDraftDocx.Create(new MatterDraft
        {
            DraftType = MatterDraftType.Letter, PageSize = "Legal", Orientation = "Landscape",
            MarginTopMm = 30m, MarginRightMm = 15m, MarginBottomMm = 25m, MarginLeftMm = 35m, ContentJson = Content("Legal")
        });
        Assert.Equal(11906U, Section(a4).GetFirstChild<PageSize>()!.Width!.Value);
        var size = Section(legal).GetFirstChild<PageSize>()!;
        var margins = Section(legal).GetFirstChild<PageMargin>()!;
        Assert.Equal(20160U, size.Width!.Value);
        Assert.Equal(12240U, size.Height!.Value);
        Assert.Equal(PageOrientationValues.Landscape, size.Orient!.Value);
        Assert.Equal(1701, margins.Top!.Value);
        Assert.Equal(850U, margins.Right!.Value);
        Assert.Equal(1417, margins.Bottom!.Value);
        Assert.Equal(1984U, margins.Left!.Value);
        Assert.Null(FindMirrorMargins(legal));
    }

    [Fact]
    public async Task Noting_callback_restores_layout_preserves_content_and_keeps_save_semantics()
    {
        var id = await DraftAsync(MatterDraftType.Noting);
        var before = await Read(id);
        var key = OnlyOfficeDraftService.Key(before);
        factory.Download.Bytes = MakeMalformattedNotingDocx();

        Assert.Equal(0, await Callback(id, key, 6));
        var forced = await Read(id);
        Assert.Equal(before.Revision + 1, forced.Revision);
        Assert.Equal(before.OfficeDocument!.Version + 1, forced.OfficeDocument!.Version);
        Assert.Equal(before.OfficeKeyGeneration, forced.OfficeKeyGeneration);
        var forcedBytes = await StoredBytes(forced);
        AssertNotingLayoutAndContent(forcedBytes);

        Assert.Equal(0, await Callback(id, key, 2));
        var final = await Read(id);
        Assert.Equal(forced.Revision + 1, final.Revision);
        Assert.Equal(forced.OfficeDocument!.Version + 1, final.OfficeDocument!.Version);
        Assert.Equal(before.OfficeKeyGeneration + 1, final.OfficeKeyGeneration);
        AssertNotingLayoutAndContent(await StoredBytes(final));
    }

    [Fact]
    public async Task Letter_callback_preserves_user_selected_layout_without_mirror_margins()
    {
        var id = await DraftAsync();
        var before = await Read(id);
        factory.Download.Bytes = MakeLetterLayoutDocx();
        Assert.Equal(0, await Callback(id, OnlyOfficeDraftService.Key(before), 6));
        using var bytes = new MemoryStream(await StoredBytes(await Read(id)));
        using var package = WordprocessingDocument.Open(bytes, false);
        var section = package.MainDocumentPart!.Document!.Body!.GetFirstChild<SectionProperties>()!;
        var size = section.GetFirstChild<PageSize>()!;
        var margins = section.GetFirstChild<PageMargin>()!;
        Assert.Equal(20160U, size.Width!.Value);
        Assert.Equal(12240U, size.Height!.Value);
        Assert.Equal(PageOrientationValues.Landscape, size.Orient!.Value);
        Assert.Equal(1701, margins.Top!.Value);
        Assert.Equal(850U, margins.Right!.Value);
        Assert.Equal(1417, margins.Bottom!.Value);
        Assert.Equal(1984U, margins.Left!.Value);
        Assert.Null(package.MainDocumentPart.DocumentSettingsPart?.Settings?.GetFirstChild<MirrorMargins>());
    }

    [Fact]
    public void Enabled_options_require_origins_and_secret()
    {
        Assert.True(new OnlyOfficeOptions().IsValid());
        Assert.False(new OnlyOfficeOptions { Enabled = true }.IsValid());
        Assert.False(new OnlyOfficeOptions { Enabled = true, BrowserUrl = "javascript:bad", AppExternalUrl = "http://localhost:5088", JwtSecret = new string('x', 32) }.IsValid());
        Assert.False(new OnlyOfficeOptions { Enabled = true, BrowserUrl = "http://localhost:8082", AppExternalUrl = "http://localhost:5088", AppBrowserUrl = "javascript:bad", JwtSecret = new string('x', 32) }.IsValid());
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(0, 100)]
    [InlineData(768, 85)]
    [InlineData(800, 85)]
    [InlineData(801, 90)]
    [InlineData(900, 90)]
    [InlineData(950, 90)]
    [InlineData(951, 100)]
    [InlineData(1080, 100)]
    public void Responsive_zoom_uses_browser_viewport_height(int? height, int expected)
        => Assert.Equal(expected, OnlyOfficeDraftService.ZoomForViewportHeight(height));

    [Theory]
    [InlineData(900, 90)]
    [InlineData(1080, 100)]
    public async Task Generated_config_signs_responsive_zoom(int height, int expected)
    {
        var id = await DraftAsync();
        var config = (await Config(id, height)).GetProperty("config");
        Assert.Equal(expected, config.GetProperty("editorConfig").GetProperty("customization").GetProperty("zoom").GetInt32());
        var signed = Tokens.Verify(config.GetProperty("token").GetString()!);
        Assert.Equal(config.GetProperty("editorConfig").GetRawText(), signed.GetProperty("editorConfig").GetRawText());
    }

    private static byte[] MakeDocx(string text)
    {
        using var bytes = MatterDraftDocx.Create(new MatterDraft { ContentJson = Content(text) });
        return bytes.ToArray();
    }

    private async Task<byte[]> StoredBytes(MatterDraft draft)
    {
        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
        await using var file = await storage.OpenReadAsync(draft.OfficeDocument!.StoragePath, default);
        using var bytes = new MemoryStream();
        await file!.CopyToAsync(bytes);
        return bytes.ToArray();
    }

    private static SectionProperties Section(Stream stream)
    {
        stream.Position = 0;
        using var package = WordprocessingDocument.Open(stream, false);
        return (SectionProperties)package.MainDocumentPart!.Document!.Body!.GetFirstChild<SectionProperties>()!.CloneNode(true);
    }

    private static MirrorMargins? FindMirrorMargins(Stream stream)
    {
        stream.Position = 0;
        using var package = WordprocessingDocument.Open(stream, false);
        return package.MainDocumentPart!.DocumentSettingsPart?.Settings?.GetFirstChild<MirrorMargins>();
    }

    private static void AssertNotingLayoutAndContent(byte[] source)
    {
        using var bytes = new MemoryStream(source);
        using var package = WordprocessingDocument.Open(bytes, false);
        var body = package.MainDocumentPart!.Document!.Body!;
        var section = body.GetFirstChild<SectionProperties>()!;
        var size = section.GetFirstChild<PageSize>()!;
        var margins = section.GetFirstChild<PageMargin>()!;
        Assert.Equal(12240U, size.Width!.Value); Assert.Equal(20160U, size.Height!.Value);
        Assert.Equal(PageOrientationValues.Portrait, size.Orient!.Value);
        Assert.Equal(1417, margins.Top!.Value); Assert.Equal(1134U, margins.Right!.Value);
        Assert.Equal(1134, margins.Bottom!.Value); Assert.Equal(2551U, margins.Left!.Value); Assert.Equal(0U, margins.Gutter!.Value);
        Assert.NotNull(package.MainDocumentPart.DocumentSettingsPart!.Settings!.GetFirstChild<MirrorMargins>());
        Assert.Contains("Preserved bold text", body.InnerText);
        Assert.Contains("Preserved table cell", body.InnerText);
        Assert.Contains(body.Descendants<Table>(), _ => true);
        Assert.Contains(body.Descendants<Break>(), x => x.Type?.Value == BreakValues.Page);
        Assert.Contains(body.Descendants<Bold>(), _ => true);
    }

    private static byte[] MakeMalformattedNotingDocx() => MakeLayoutDocx(
        width: 16838U, height: 11906U, orientation: PageOrientationValues.Landscape,
        top: 567, right: 567U, bottom: 567, left: 567U, text: "Preserved bold text", includeTable: true, includePageBreak: true);

    private static byte[] MakeLetterLayoutDocx() => MakeLayoutDocx(
        width: 20160U, height: 12240U, orientation: PageOrientationValues.Landscape,
        top: 1701, right: 850U, bottom: 1417, left: 1984U, text: "Letter custom layout", includeTable: false, includePageBreak: false);

    private static byte[] MakeLayoutDocx(uint width, uint height, PageOrientationValues orientation, int top, uint right, int bottom, uint left, string text, bool includeTable, bool includePageBreak)
    {
        using var output = new MemoryStream();
        using (var package = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document, true))
        {
            var main = package.AddMainDocumentPart();
            var body = new Body(new Paragraph(new Run(new RunProperties(new Bold()), new Text(text))));
            if (includePageBreak) body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
            if (includeTable) body.Append(new Table(new TableRow(new TableCell(new Paragraph(new Run(new Text("Preserved table cell")))))));
            body.Append(new SectionProperties(new PageSize { Width = width, Height = height, Orient = orientation },
                new PageMargin { Top = top, Right = right, Bottom = bottom, Left = left, Gutter = 0U, Header = 720U, Footer = 720U }));
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body);
            main.Document.Save();
        }
        return output.ToArray();
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
            ["OnlyOffice:AppBrowserUrl"] = "http://localhost:5173",
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
