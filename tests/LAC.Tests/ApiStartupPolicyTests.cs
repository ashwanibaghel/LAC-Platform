using LAC.Api;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LAC.Tests;

[Collection("Startup environment")]
public sealed class ApiStartupPolicyTests
{
    private static IConfiguration Configuration(bool? bootstrap = null, bool? workers = null)
    {
        var values = new Dictionary<string, string?>();
        if (bootstrap.HasValue) values["Startup:RunDatabaseBootstrap"] = bootstrap.Value.ToString();
        if (workers.HasValue) values["BackgroundWorkers:Enabled"] = workers.Value.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public async Task DefaultStartup_PreservesRelationalMigrateAndSeed()
    {
        var config = Configuration();
        Assert.True(ApiStartupPolicy.RunDatabaseBootstrap(config));
        Assert.True(ApiStartupPolicy.BackgroundWorkersEnabled(config));
        var calls = new List<string>();
        await ApiStartupPolicy.BootstrapDatabaseAsync(config, true,
            () => { calls.Add("migrate"); return Task.CompletedTask; },
            () => { calls.Add("ensure-created"); return Task.CompletedTask; },
            () => { calls.Add("seed"); return Task.CompletedTask; });
        Assert.Equal(["migrate", "seed"], calls);
    }

    [Fact]
    public async Task DefaultStartup_PreservesNonRelationalEnsureCreatedAndSeed()
    {
        var calls = new List<string>();
        await ApiStartupPolicy.BootstrapDatabaseAsync(Configuration(), false,
            () => { calls.Add("migrate"); return Task.CompletedTask; },
            () => { calls.Add("ensure-created"); return Task.CompletedTask; },
            () => { calls.Add("seed"); return Task.CompletedTask; });
        Assert.Equal(["ensure-created", "seed"], calls);
    }

    [Fact]
    public async Task AcceptanceStartup_NeverInvokesMigrationCreationOrSeed()
    {
        var config = Configuration(bootstrap: false, workers: false);
        Assert.False(ApiStartupPolicy.RunDatabaseBootstrap(config));
        await ApiStartupPolicy.BootstrapDatabaseAsync(config, true,
            () => throw new Xunit.Sdk.XunitException("Migration was invoked"),
            () => throw new Xunit.Sdk.XunitException("Database creation was invoked"),
            () => throw new Xunit.Sdk.XunitException("Seed was invoked"));
        await ApiStartupPolicy.BootstrapDatabaseAsync(config, false,
            () => throw new Xunit.Sdk.XunitException("Migration was invoked"),
            () => throw new Xunit.Sdk.XunitException("Database creation was invoked"),
            () => throw new Xunit.Sdk.XunitException("Seed was invoked"));
    }

    [Fact]
    public void AcceptanceStartup_RegistersNeitherAutomaticWorker()
    {
        var services = new ServiceCollection();
        ApiStartupPolicy.RegisterBackgroundWorkers(services, Configuration(workers: false), false);
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void NormalStartup_RegistersBothWorkers_AndPreservesTestingException()
    {
        var services = new ServiceCollection();
        ApiStartupPolicy.RegisterBackgroundWorkers(services, Configuration(), false);
        Assert.Contains(services, x => x.ServiceType == typeof(IHostedService) &&
            x.ImplementationType == typeof(AwardPdfExtractionWorker));
        Assert.Contains(services, x => x.ServiceType == typeof(IHostedService) &&
            x.ImplementationType == typeof(DelhiHighCourtSyncWorker));
        Assert.Contains(services, x => x.ImplementationType == typeof(DhcHistoryIntelligenceBridge));
        Assert.Contains(services, x => x.ImplementationType == typeof(CourtScopeIngestionWorker));

        var testingServices = new ServiceCollection();
        ApiStartupPolicy.RegisterBackgroundWorkers(testingServices, Configuration(), true);
        Assert.Contains(testingServices, x => x.ImplementationType == typeof(AwardPdfExtractionWorker));
        Assert.DoesNotContain(testingServices, x => x.ImplementationType == typeof(DelhiHighCourtSyncWorker));
        Assert.DoesNotContain(testingServices, x => x.ImplementationType == typeof(DhcHistoryIntelligenceBridge));
        Assert.DoesNotContain(testingServices, x => x.ImplementationType == typeof(CourtScopeIngestionWorker));
    }

    [Fact]
    public void AcceptancePolicy_NeitherBootstrapsNorRegistersDhcWorker()
    {
        var config = Configuration(bootstrap: false, workers: false);
        var services = new ServiceCollection();
        ApiStartupPolicy.RegisterBackgroundWorkers(services, config, false);
        Assert.False(ApiStartupPolicy.RunDatabaseBootstrap(config));
        Assert.DoesNotContain(services, x => x.ImplementationType == typeof(DelhiHighCourtSyncWorker));
        // Manual assisted services are registered separately in Program.cs,
        // not as background workers, so the switch does not disable endpoints.
    }

    [Fact]
    public void AcceptanceHost_StillResolvesManualAssistedServices()
    {
        using var factory = new ApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Startup:RunDatabaseBootstrap"] = "false",
                    ["BackgroundWorkers:Enabled"] = "false"
                })));
        using var client = factory.CreateClient(); // Startup only; no DHC endpoint is called.
        using var scope = factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedCoordinator>());
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Development")]
    public void Disabled_workers_are_absent_from_the_actual_host(string environment)
    {
        // Program's registrations read configuration before WAF's late callbacks.
        // Use process startup inputs, serialize this collection, and restore them.
        var values = new Dictionary<string, string?>
        {
            ["Startup__RunDatabaseBootstrap"] = "false", ["BackgroundWorkers__Enabled"] = "false",
            ["ConnectionStrings__DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=never_connect;Username=never_connect;Timeout=1"
        };
        var previous = values.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var pair in values) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            using var factory = new ApiFactory().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IDbContextOptionsConfiguration<LacDbContext>>();
                    services.RemoveAll<DbContextOptions<LacDbContext>>();
                    services.RemoveAll<LacDbContext>();
                    services.AddDbContext<LacDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
                });
            });
            using var client = factory.CreateClient();
            Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), x =>
                x is DhcHistoryIntelligenceBridge or CourtScopeIngestionWorker or DelhiHighCourtSyncWorker or AwardPdfExtractionWorker);
        }
        finally { foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
    }
}

[CollectionDefinition("Startup environment", DisableParallelization = true)]
public sealed class StartupEnvironmentCollection { }
