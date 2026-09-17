using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LAC.Tests;

public sealed class MatterDraftTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    private readonly ApiFactory _factory;

    public MatterDraftTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(Guid VillageId, Guid MatterId)> CreateTestMatterAsync()
    {
        var village = await _client.GetFromJsonAsync<PageResponse<VillageListItem>>("/api/villages?page=0&pageSize=1");
        var villageId = Assert.Single(village!.Items).Id;

        using var response = await _client.PostAsJsonAsync($"/api/villages/{villageId}/matters", new
        {
            title = $"Test Matter {Guid.NewGuid()}",
            matterType = "Court Case",
            status = "Open"
        });
        response.EnsureSuccessStatusCode();
        var matterId = (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        return (villageId, matterId);
    }

    [Fact]
    public async Task Creating_noting_draft_applies_locked_delhi_lac_noting_v1_profile()
    {
        var (_, matterId) = await CreateTestMatterAsync();

        using var createResponse = await _client.PostAsJsonAsync($"/api/matters/{matterId}/drafts", new
        {
            title = "Office Noting on Khasra 22//2",
            draftType = "Noting"
        });
        createResponse.EnsureSuccessStatusCode();
        var draftId = (await createResponse.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var draft = await _client.GetFromJsonAsync<JsonElement>($"/api/matter-drafts/{draftId}");
        Assert.Equal("Noting", draft.GetProperty("draftType").GetString());
        Assert.Equal("Legal", draft.GetProperty("pageSize").GetString());
        Assert.Equal("Portrait", draft.GetProperty("orientation").GetString());
        Assert.Equal(25m, draft.GetProperty("marginTopMm").GetDecimal());
        Assert.Equal(0m, draft.GetProperty("marginRightMm").GetDecimal());
        Assert.Equal(25m, draft.GetProperty("marginBottomMm").GetDecimal());
        Assert.Equal(0m, draft.GetProperty("marginLeftMm").GetDecimal());
    }

    [Fact]
    public async Task Noting_draft_rejects_custom_layout_changes()
    {
        var (_, matterId) = await CreateTestMatterAsync();

        using var createResponse = await _client.PostAsJsonAsync($"/api/matters/{matterId}/drafts", new
        {
            title = "Noting Layout Enforcement",
            draftType = "Noting"
        });
        createResponse.EnsureSuccessStatusCode();
        var draftId = (await createResponse.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var draft = await _client.GetFromJsonAsync<JsonElement>($"/api/matter-drafts/{draftId}");

        using var putResponse = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new
        {
            title = "Attempted Mutation",
            contentJson = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Test\"}]}]}",
            pageSize = "Legal",
            orientation = "Landscape",
            marginTopMm = 10m,
            marginRightMm = 10m,
            marginBottomMm = 10m,
            marginLeftMm = 10m,
            expectedRevision = draft.GetProperty("revision").GetInt32()
        });

        Assert.Equal(HttpStatusCode.BadRequest, putResponse.StatusCode);
        var body = await putResponse.Content.ReadAsStringAsync();
        Assert.Contains("Noting Sheet layout is fixed by the office profile", body);
    }

    [Fact]
    public async Task Letter_draft_supports_a4_and_legal_and_custom_margins()
    {
        var (_, matterId) = await CreateTestMatterAsync();

        using var createResponse = await _client.PostAsJsonAsync($"/api/matters/{matterId}/drafts", new
        {
            title = "Official Letter to ADM",
            draftType = "Letter"
        });
        createResponse.EnsureSuccessStatusCode();
        var draftId = (await createResponse.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var initial = await _client.GetFromJsonAsync<JsonElement>($"/api/matter-drafts/{draftId}");

        using var putResponse = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new
        {
            title = "Official Letter to ADM (Legal Landscape)",
            contentJson = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Respected Sir,\"}]}]}",
            pageSize = "Legal",
            orientation = "Landscape",
            marginTopMm = 30m,
            marginRightMm = 15m,
            marginBottomMm = 25m,
            marginLeftMm = 35m,
            expectedRevision = initial.GetProperty("revision").GetInt32()
        });
        putResponse.EnsureSuccessStatusCode();

        var updated = await _client.GetFromJsonAsync<JsonElement>($"/api/matter-drafts/{draftId}");
        Assert.Equal("Legal", updated.GetProperty("pageSize").GetString());
        Assert.Equal("Landscape", updated.GetProperty("orientation").GetString());
        Assert.Equal(30m, updated.GetProperty("marginTopMm").GetDecimal());
        Assert.Equal(15m, updated.GetProperty("marginRightMm").GetDecimal());
        Assert.Equal(25m, updated.GetProperty("marginBottomMm").GetDecimal());
        Assert.Equal(35m, updated.GetProperty("marginLeftMm").GetDecimal());
        Assert.Equal(initial.GetProperty("revision").GetInt32() + 1, updated.GetProperty("revision").GetInt32());
    }

    [Fact]
    public async Task Backend_accepts_both_flat_doc_and_legacy_draftpage_content()
    {
        var (_, matterId) = await CreateTestMatterAsync();

        using var createResponse = await _client.PostAsJsonAsync($"/api/matters/{matterId}/drafts", new
        {
            title = "Compatibility Draft",
            draftType = "Letter"
        });
        createResponse.EnsureSuccessStatusCode();
        var draftId = (await createResponse.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var initial = await _client.GetFromJsonAsync<JsonElement>($"/api/matter-drafts/{draftId}");

        var legacyContent = "{\"type\":\"doc\",\"content\":[{\"type\":\"draftPage\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Legacy Page 1\"}]}]}]}";
        using var putLegacy = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new
        {
            title = "Compatibility Draft",
            contentJson = legacyContent,
            pageSize = "A4",
            orientation = "Portrait",
            marginTopMm = 25m,
            marginRightMm = 20m,
            marginBottomMm = 20m,
            marginLeftMm = 25m,
            expectedRevision = initial.GetProperty("revision").GetInt32()
        });
        putLegacy.EnsureSuccessStatusCode();

        var afterLegacy = await _client.GetFromJsonAsync<JsonElement>($"/api/matter-drafts/{draftId}");

        var flatContent = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Flat Line 1\"}]},{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Flat Line 2\"}]}]}";
        using var putFlat = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new
        {
            title = "Compatibility Draft (Normalized Flat)",
            contentJson = flatContent,
            pageSize = "A4",
            orientation = "Portrait",
            marginTopMm = 25m,
            marginRightMm = 20m,
            marginBottomMm = 20m,
            marginLeftMm = 25m,
            expectedRevision = afterLegacy.GetProperty("revision").GetInt32()
        });
        putFlat.EnsureSuccessStatusCode();

        var finalDraft = await _client.GetFromJsonAsync<JsonElement>($"/api/matter-drafts/{draftId}");
        Assert.Equal(flatContent, finalDraft.GetProperty("contentJson").GetString());
    }
}
