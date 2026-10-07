using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LAC.Api;
using LAC.Domain;
using LAC.Domain.Calculators;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LAC.Tests;

public sealed class CompensationCalculatorApiTests : IClassFixture<CompensationCalculatorApiFactory>
{
    private const string Endpoint = "/api/calculators/compensation/compute";
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    private readonly CompensationCalculatorApiFactory factory;
    public CompensationCalculatorApiTests(CompensationCalculatorApiFactory factory) => this.factory = factory;
    private async Task<HttpClient> Client()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        if (!await db.AppUsers.AnyAsync(x => x.Username == "calculator-user"))
        {
            // A local user with no roles, permissions, village/workstream allocations or award links.
            var user = new AppUser { Username = "calculator-user", NormalizedUsername = "CALCULATOR-USER", DisplayName = "Calculator User", IsActive = true };
            user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>().HashPassword(user, TestCredentials.SharedPassword);
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();
        }
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "calculator-user", password = TestCredentials.SharedPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }
    [Fact]
    public async Task AuthenticationRequired_IncludingMalformedRequests()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Endpoint, CompensationCalculatorTests.Sample(), WireOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(Endpoint, new StringContent("broken", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(Endpoint, new StringContent("broken"))).StatusCode);
    }
    [Fact]
    public async Task OrdinaryAuthenticatedUser_CanCompute_ExactResponse_NoPersistence()
    {
        using var client = await Client();
        var saves = factory.Writes.Count;
        var response = await client.PostAsJsonAsync(Endpoint, CompensationCalculatorTests.Sample(), WireOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var result = await response.Content.ReadFromJsonAsync<CompensationResponse>();
        Assert.Equal("79568513.75", result!.FinalCompensation.Display);
        Assert.Equal("195713.75", result.AdditionalAmount.Amount.Display);
        Assert.True(result.Area.UsesExplicitEquivalentArea);
        Assert.Equal(saves, factory.Writes.Count);
        // Identical requests are reproducible and do not create a history record.
        var repeat = await client.PostAsJsonAsync(Endpoint, CompensationCalculatorTests.Sample(), WireOptions);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await repeat.Content.ReadAsStringAsync());
        Assert.Equal(saves, factory.Writes.Count);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("finalCompensation").GetProperty("precise").ValueKind);
    }
    private const string ValidJson = """
        {"land":{"area":"3.744","unit":"acre"},"marketRate":{"amount":"5300000","perUnit":"acre"},"multiplicationFactor":2,"assets":{"treesAndStructures":0},"solatium":{"percent":100},"additionalAmount":{"annualRatePercent":12,"duration":{"mode":"Days","value":30}}}
        """;
    [Fact]
    public async Task DecimalStringsAndDefaultInterestAndBasis_AreSupported()
    {
        using var client = await Client();
        var response = await client.PostAsync(Endpoint, new StringContent(ValidJson, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CompensationResponse>();
        Assert.Equal("Interest", result!.AdditionalAmount.Type);
        Assert.Equal("MarketValue", result.AdditionalAmount.Basis);
        Assert.Equal("79568513.75", result.FinalCompensation.Display);
    }
    [Theory]
    [InlineData("\"3.744\"", "\"NaN\"", "land.area")]
    [InlineData("\"3.744\"", "\"Infinity\"", "land.area")]
    [InlineData("\"3.744\"", "\"unparseable\"", "land.area")]
    [InlineData("\"3.744\"", "0.1234567890123", "land.area")]
    [InlineData("\"3.744\"", "0.123456789012345678901234567890", "land.area")]
    [InlineData("\"3.744\"", "1e1", "land.area")]
    [InlineData("\"3.744\"", "1000000000001", "land.area")]
    [InlineData("\"3.744\"", "-1", "land.area")]
    [InlineData("\"3.744\"", "null", "land.area")]
    [InlineData("\"acre\"", "\"unsupported\"", "land.unit")]
    [InlineData("\"Days\"", "\"unsupported\"", "additionalAmount.duration.mode")]
    [InlineData("\"Days\"", "999", "additionalAmount.duration.mode")]
    [InlineData("\"Days\"", "0", "additionalAmount.duration.mode")]
    [InlineData("\"value\":30", "\"value\":-30", "additionalAmount.duration.value")]
    [InlineData("\"value\":30", "\"value\":30,\"value\":31", "additionalAmount.duration.value")]
    [InlineData("\"annualRatePercent\":12", "\"annualRatePercent\":12,\"basis\":\"bad\"", "additionalAmount.basis")]
    [InlineData("\"annualRatePercent\":12", "\"annualRatePercent\":12,\"awardId\":\"x\"", "additionalAmount")]
    public async Task InvalidJsonValues_Return400FieldErrors_WithoutWrites(string oldValue, string newValue, string field)
    {
        using var client = await Client();
        var saves = factory.Writes.Count;
        var response = await client.PostAsync(Endpoint, new StringContent(ValidJson.Replace(oldValue, newValue), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(json.RootElement.GetProperty("errors").EnumerateObject(), x => x.Name.StartsWith(field, StringComparison.Ordinal));
        Assert.Equal(saves, factory.Writes.Count);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task MissingOrMalformedRequests_Return400(string body)
    {
        using var client = await Client();
        var response = await client.PostAsync(Endpoint, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task OtherAndDateRange_WorkThroughJsonContract()
    {
        using var client = await Client();
        var request = CompensationCalculatorTests.Sample() with { AdditionalAmount = new(AdditionalAmountType.Other,
            Duration: new(DurationMode.DateRange, StartDate: new(2026, 1, 1), EndDate: new(2026, 1, 31)), Formula: "MARKET_VALUE * 12 / 100 * (DAYS / 365)") };
        var response = await client.PostAsJsonAsync(Endpoint, request, WireOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("79568513.75", (await response.Content.ReadFromJsonAsync<CompensationResponse>())!.FinalCompensation.Display);
        var invalid = await client.PostAsJsonAsync(Endpoint, request with { AdditionalAmount = request.AdditionalAmount with { Formula = "1 / 0" } }, WireOptions);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BodySize_BoundedEvenWithoutContentLength(bool unknownLength)
    {
        using var client = await Client();
        HttpContent content = unknownLength ? new UnboundedLengthContent(new string(' ', 16385)) : new StringContent(new string(' ', 16385), Encoding.UTF8, "application/json");
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var response = await client.PostAsync(Endpoint, content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
    [Fact]
    public async Task NonJsonContentType_Returns415()
    {
        using var client = await Client();
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync(Endpoint, new StringContent(ValidJson))).StatusCode);
    }
    [Fact]
    public async Task RevokedLocalUserCookie_IsRejected()
    {
        using var client = await Client();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var user = await db.AppUsers.SingleAsync(x => x.Username == "calculator-user");
        user.SessionVersion = Guid.NewGuid();
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Endpoint, CompensationCalculatorTests.Sample(), WireOptions)).StatusCode);
    }
    private sealed class UnboundedLengthContent(string body) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(Encoding.UTF8.GetBytes(body)).AsTask();
    }
}

public sealed class CompensationCalculatorApiFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"compensation-{Guid.NewGuid()}";
    public CalculatorWriteCounter Writes { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BootstrapAdmin:Username"] = "testadmin", ["BootstrapAdmin:Password"] = TestCredentials.SharedPassword
        }));
        builder.ConfigureServices(services => services.AddDbContext<LacDbContext>(options => options.UseInMemoryDatabase(databaseName).AddInterceptors(Writes)));
    }
}
public sealed class CalculatorWriteCounter : SaveChangesInterceptor
{
    public int Count;
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) { Interlocked.Increment(ref Count); return result; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Interlocked.Increment(ref Count); return ValueTask.FromResult(result); }
}
