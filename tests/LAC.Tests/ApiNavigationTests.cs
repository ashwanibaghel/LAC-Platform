using System.Net.Http.Json;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LAC.Tests;

public sealed class ApiNavigationTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public ApiNavigationTests(ApiFactory factory) => _client = factory.CreateClient();

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
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LacDbContext>>();
            services.RemoveAll<LacDbContext>();
            services.AddDbContext<LacDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}
