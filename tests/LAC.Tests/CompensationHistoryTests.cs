using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using Npgsql;
using Xunit;
namespace LAC.Tests;

public sealed class CompensationHistoryTests : IClassFixture<HistoryPostgresFixture>
{
    private readonly HistoryPostgresFixture fixture;
    public CompensationHistoryTests(HistoryPostgresFixture fixture) => this.fixture=fixture;
    public static CompensationHistoryInputs Inputs(string area="18",string unit="acre",bool official=true) => new(area,"bigha","5300000",unit,official,"3.744","2","0","100","interest","12","days","30","","","MarketValue","","None","","","");
    private async Task<HttpClient> Client(string user="history-a") {
        var client=fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies=true });
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/auth/login",new {username=user,password=TestCredentials.SharedPassword})).StatusCode); return client;
    }
    private static async Task<JsonElement> Save(HttpClient client,CompensationHistoryInputs inputs,Guid? key=null) {
        var reply=await client.PostAsJsonAsync("/api/calculators/compensation/history",new {idempotencyKey=key ?? Guid.NewGuid(),inputs});
        Assert.Equal(HttpStatusCode.OK,reply.StatusCode);var json=await reply.Content.ReadFromJsonAsync<JsonElement>();Assert.True(json.GetProperty("saved").GetBoolean());return json;
    }
    private static readonly JsonSerializerOptions ContractJson = new(JsonSerializerDefaults.Web) {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    public static IEnumerable<object[]> FormulaContracts() {
        // The frontend regression reads this same fixture; both must satisfy its canonical expressions.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent) {
            var path = Path.Combine(directory.FullName, "src", "LAC.Web", "tests", "fixtures", "compensation-formula-contracts.json");
            if (!File.Exists(path)) continue;
            using var cases = JsonDocument.Parse(File.ReadAllText(path));
            return cases.RootElement.EnumerateArray().Select(c => new object[] {
                c.GetProperty("readable").GetString()!, c.GetProperty("normalized").GetString()!,
                c.GetProperty("durationMode").GetString()!, c.GetProperty("durationValue").GetString()!,
                c.GetProperty("valid").GetBoolean(), c.GetProperty("finalDisplay").GetString()!
            }).ToArray();
        }
        throw new FileNotFoundException("Shared frontend/history formula contract fixture was not found.");
    }
    [Theory]
    [InlineData("MarketValue")]
    [InlineData("FactorAdjustedValue")]
    [InlineData("BaseCompensation")]
    [InlineData("AmountAfterSolatium")]
    public async Task Switching_interest_basis_to_other_saves_original_inputs_and_exact_result(string basis) {
        var interest = Inputs() with { CalculatedOn = basis };
        Assert.Equal(Enum.Parse<InterestBasis>(basis), interest.Normalize().AdditionalAmount!.Basis);
        using var client = await Client();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/calculators/compensation/compute", interest.Normalize(), ContractJson)).StatusCode);
        var inputs = interest with { AdditionalAmountType = "other", FormulaReadable = "After Solatium × 12 / 100" };
        var additional = inputs.Normalize().AdditionalAmount!;
        Assert.Equal(InterestBasis.MarketValue, additional.Basis);
        Assert.Null(additional.AnnualRatePercent);
        Assert.Null(additional.Duration);
        var record = await Save(client, inputs);
        using var fresh = await Client();
        var reopened = await fresh.GetFromJsonAsync<JsonElement>($"/api/calculators/compensation/history/{record.GetProperty("id").GetGuid()}");
        Assert.Equal("88897536.00", reopened.GetProperty("response").GetProperty("finalCompensation").GetProperty("display").GetString());
        foreach (var field in new[] { "inputs", "request", "response" }) Assert.Equal(record.GetProperty(field).ToString(), reopened.GetProperty(field).ToString());
        Assert.Equal(inputs, reopened.GetProperty("inputs").Deserialize<CompensationHistoryInputs>(ContractJson));
        Assert.Equal("MarketValue", reopened.GetProperty("request").GetProperty("additionalAmount").GetProperty("basis").GetString());
    }
    [Theory]
    [MemberData(nameof(FormulaContracts))]
    public async Task Shared_frontend_formula_contract_matches_saved_backend_and_stateless_compute(
        string readable, string normalized, string durationMode, string durationValue, bool valid, string finalDisplay) {
        var inputs = Inputs() with { AdditionalAmountType = "other", CalculatedOn = "AmountAfterSolatium",
            FormulaReadable = readable, OtherDurationMode = durationMode, OtherDurationValue = durationValue };
        var request = inputs.Normalize();
        Assert.Equal(normalized, request.AdditionalAmount!.Formula);
        // Build the canonical frontend request independently from the shared expected expression.
        var frontendRequest = Inputs().Normalize() with { AdditionalAmount = new(
            LAC.Domain.Calculators.AdditionalAmountType.Other, Basis: InterestBasis.MarketValue, Formula: normalized,
            Duration: durationMode == "None" ? null : new(DurationMode.Days, decimal.Parse(durationValue, System.Globalization.CultureInfo.InvariantCulture))) };
        Assert.Equal(frontendRequest, request);
        using var client = await Client();
        var computed = await client.PostAsJsonAsync("/api/calculators/compensation/compute", frontendRequest, ContractJson);
        var key = Guid.NewGuid();
        if (!valid) {
            Assert.Equal(HttpStatusCode.BadRequest, computed.StatusCode);
            var rejected = await client.PostAsJsonAsync("/api/calculators/compensation/history", new { idempotencyKey = key, inputs });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            await using var db = fixture.Db();
            Assert.False(await db.CompensationHistory.AnyAsync(x => x.IdempotencyKey == key));
            return;
        }
        Assert.Equal(HttpStatusCode.OK, computed.StatusCode);
        var expectedResponse = await computed.Content.ReadFromJsonAsync<JsonElement>();
        var record = await Save(client, inputs, key);
        Assert.True(JsonElement.DeepEquals(expectedResponse, record.GetProperty("response")));
        Assert.Equal(finalDisplay, record.GetProperty("response").GetProperty("finalCompensation").GetProperty("display").GetString());
        var retry = await Save(client, inputs, key);
        Assert.Equal(record.GetProperty("id").GetGuid(), retry.GetProperty("id").GetGuid());
        using var fresh = await Client();
        var reopened = await fresh.GetFromJsonAsync<JsonElement>($"/api/calculators/compensation/history/{record.GetProperty("id").GetGuid()}");
        Assert.True(JsonElement.DeepEquals(expectedResponse, reopened.GetProperty("response")));
        Assert.Equal(inputs, reopened.GetProperty("inputs").Deserialize<CompensationHistoryInputs>(ContractJson));
        Assert.Equal(frontendRequest, reopened.GetProperty("request").Deserialize<CompensationRequest>(ContractJson));
    }
    [Fact] public void Other_ignores_invalid_interest_only_fields_but_interest_still_validates_basis() {
        var inputs = Inputs() with { AdditionalAmountType = "other", FormulaReadable = "Solatium × 12 / 100",
            CalculatedOn = "obsolete", AnnualRate = "invalid", DurationType = "date_range", StartDate = "invalid", EndDate = "invalid" };
        var normalized = inputs.Normalize().AdditionalAmount!;
        Assert.Equal(InterestBasis.MarketValue, normalized.Basis);
        Assert.Null(normalized.AnnualRatePercent);
        Assert.Null(normalized.Duration);
        Assert.Throws<CalculatorValidationException>(() => (Inputs() with { CalculatedOn = "obsolete" }).Normalize());
    }
    [Fact] public async Task Durable_full_snapshot_survives_new_login_and_reopen() {
        using var client=await Client();var record=await Save(client,Inputs());var id=record.GetProperty("id").GetGuid();
        Assert.Equal("79568513.75",record.GetProperty("response").GetProperty("finalCompensation").GetProperty("display").GetString());
        using var fresh=await Client();var reopened=await fresh.GetFromJsonAsync<JsonElement>($"/api/calculators/compensation/history/{id}");
        Assert.Equal(record.GetProperty("response").ToString(),reopened.GetProperty("response").ToString());
        Assert.Equal("18",reopened.GetProperty("inputs").GetProperty("landArea").GetString());Assert.Equal("lac-delhi-v1",reopened.GetProperty("conversionVersion").GetString());
        Assert.NotEmpty(reopened.GetProperty("response").GetProperty("trace").EnumerateArray());
        await using var db=fixture.Db();Assert.True(await db.CompensationHistory.AnyAsync(x=>x.Id==id));
    }
    [Fact] public async Task Guessing_ids_cannot_read_or_rename_another_users_history() {
        using var first=await Client();using var second=await Client("history-b");var record=await Save(first,Inputs());var id=record.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,(await second.GetAsync($"/api/calculators/compensation/history/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await second.PatchAsJsonAsync($"/api/calculators/compensation/history/{id}",new {title="stolen"})).StatusCode);
        var list=await second.GetFromJsonAsync<JsonElement>("/api/calculators/compensation/history");Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==id);
    }
    [Fact] public async Task Rename_and_new_calculation_do_not_change_original_evidence() {
        using var client=await Client();var record=await Save(client,Inputs());var id=record.GetProperty("id").GetGuid();
        var edit=await client.PatchAsJsonAsync($"/api/calculators/compensation/history/{id}",new {title="South West reference"});Assert.Equal(HttpStatusCode.OK,edit.StatusCode);
        var newer=await Save(client,Inputs("4-16",official:false));Assert.NotEqual(id,newer.GetProperty("id").GetGuid());
        var original=await client.GetFromJsonAsync<JsonElement>($"/api/calculators/compensation/history/{id}");
        foreach(var field in new[]{"inputs","request","response"}) Assert.Equal(record.GetProperty(field).ToString(),original.GetProperty(field).ToString());
        var list=await client.GetFromJsonAsync<JsonElement>("/api/calculators/compensation/history?search=west&page=1&pageSize=1");Assert.True(list.GetProperty("total").GetInt32()>=1);Assert.Single(list.GetProperty("items").EnumerateArray());
    }
    [Fact] public async Task Concurrent_idempotent_retries_create_one_row_and_reused_payload_conflicts() {
        using var client=await Client();var key=Guid.NewGuid();var responses=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Save(client,Inputs(),key)));
        Assert.Single(responses.Select(x=>x.GetProperty("id").GetGuid()).Distinct());
        await using var db=fixture.Db();Assert.Equal(1,await db.CompensationHistory.CountAsync(x=>x.IdempotencyKey==key));
        var conflict=await client.PostAsJsonAsync("/api/calculators/compensation/history",new {idempotencyKey=key,inputs=Inputs("4-16")});Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
    }
    [Theory] [InlineData("4-20")] [InlineData("2-9-20")] [InlineData("-1")] [InlineData("4--16")] [InlineData("0.1234567890123")]
    public async Task Invalid_calculation_is_never_saved(string area) {
        using var client=await Client();var key=Guid.NewGuid();var invalid=await client.PostAsJsonAsync("/api/calculators/compensation/history",new {idempotencyKey=key,inputs=Inputs(area)});
        Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);await using var db=fixture.Db();Assert.False(await db.CompensationHistory.AnyAsync(x=>x.IdempotencyKey==key));
    }
    [Fact] public async Task Failed_database_commit_never_returns_saved() {
        using var client=await Client();fixture.Factory.Failure.Enabled=true;var key=Guid.NewGuid();
        try {var failed=await client.PostAsJsonAsync("/api/calculators/compensation/history",new {idempotencyKey=key,inputs=Inputs()});Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.False((await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("saved").GetBoolean());}
        finally {fixture.Factory.Failure.Enabled=false;}
        await using var db=fixture.Db();Assert.False(await db.CompensationHistory.AnyAsync(x=>x.IdempotencyKey==key));
        await Save(client,Inputs(),key);
    }
    [Fact] public async Task Entity_and_database_reject_snapshot_mutation() {
        using var client=await Client();var record=await Save(client,Inputs());var id=record.GetProperty("id").GetGuid();
        await using(var db=fixture.Db()){var row=await db.CompensationHistory.SingleAsync(x=>x.Id==id);row.ResponseJson="{}";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());}
        await using var connection=new NpgsqlConnection(fixture.Connection);await connection.OpenAsync();
        await using var command=new NpgsqlCommand("UPDATE \"CompensationHistory\" SET \"ResponseJson\"='{}' WHERE \"Id\"=@id",connection);command.Parameters.AddWithValue("id",id);
        await Assert.ThrowsAsync<PostgresException>(()=>command.ExecuteNonQueryAsync());
    }
    [Fact] public async Task Stateless_compute_preserved_and_malicious_client_results_rejected() {
        using var client=await Client();await using var db=fixture.Db();var before=await db.CompensationHistory.CountAsync();
        var reply=await client.PostAsJsonAsync("/api/calculators/compensation/compute",Inputs().Normalize(),new JsonSerializerOptions(JsonSerializerDefaults.Web){Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}});
        Assert.Equal(HttpStatusCode.OK,reply.StatusCode);Assert.Equal(before,await db.CompensationHistory.CountAsync());
        var invalid=await client.PostAsJsonAsync("/api/calculators/compensation/history",new {idempotencyKey=Guid.NewGuid(),inputs=Inputs(),response=new {finalCompensation=1}});Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using var anonymous=fixture.Factory.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/calculators/compensation/history")).StatusCode);
    }
    [Theory] [InlineData("acre")] [InlineData("bigha")] [InlineData("sqm")]
    public async Task Shorthand_decimal_parity_is_exact_across_rate_units(string unit) {
        using var client=await Client();var a=await Save(client,Inputs("4-16",unit,false));var b=await Save(client,Inputs("4.8",unit,false));
        Assert.Equal(a.GetProperty("response").ToString(),b.GetProperty("response").ToString());
        Assert.Equal("4-16 bigha",a.GetProperty("originalAreaNotation").GetString());
    }
}
public sealed class HistoryPostgresFixture : IAsyncLifetime
{
    private static string Server => Environment.GetEnvironmentVariable("LAC_HISTORY_TEST_SERVER")
        ?? "Host=127.0.0.1;Port=55440;Database=postgres;Username=postgres;Pooling=false";
    private readonly string name=$"lac_comp_history_test_{Guid.NewGuid():N}";
    public string Connection {get;private set;}="";
    public HistoryFactory Factory {get;private set;}=null!;
    public LacDbContext Db()=>new(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(Connection).Options);
    public async Task InitializeAsync(){
        var destination=new NpgsqlConnectionStringBuilder(Server);
        if(destination.Host!="127.0.0.1" || destination.Database!="postgres" || destination.Port is not (55440 or 55442))
            throw new InvalidOperationException("History tests require a disposable loopback admin server on 55440 or 55442.");
        await using var admin=new NpgsqlConnection(Server);await admin.OpenAsync();await using(var create=new NpgsqlCommand($"CREATE DATABASE \"{name}\"",admin))await create.ExecuteNonQueryAsync();
        var builder=new NpgsqlConnectionStringBuilder(Server){Database=name};Connection=builder.ConnectionString;
        await using var db=Db();await db.Database.MigrateAsync();
        foreach(var username in new[]{"history-a","history-b"}){var user=new AppUser{Username=username,NormalizedUsername=username.ToUpperInvariant(),DisplayName=username,IsActive=true};user.PasswordHash=new PasswordHasher<AppUser>().HashPassword(user,TestCredentials.SharedPassword);db.AppUsers.Add(user);}await db.SaveChangesAsync();Factory=new HistoryFactory(Connection);
    }
    public async Task DisposeAsync(){await Factory.DisposeAsync();await using var admin=new NpgsqlConnection(Server);await admin.OpenAsync();await using var drop=new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)",admin);await drop.ExecuteNonQueryAsync();}
}
public sealed class HistoryFactory(string connection) : WebApplicationFactory<Program>
{
    public HistoryFailure Failure {get;}=new();
    protected override void ConfigureWebHost(IWebHostBuilder builder){builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Startup:RunDatabaseBootstrap"]="false",["BackgroundWorkers:Enabled"]="false"}));builder.ConfigureServices(s=>s.AddDbContext<LacDbContext>(o=>o.UseNpgsql(connection).AddInterceptors(Failure)));}
}
public sealed class HistoryFailure : SaveChangesInterceptor
{
    public bool Enabled;
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default){if(Enabled && e.Context!.ChangeTracker.Entries<CompensationHistory>().Any(x=>x.State==EntityState.Added))throw new DbUpdateException("Synthetic database commit failure");return ValueTask.FromResult(result);}
}
