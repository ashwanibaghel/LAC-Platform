using LAC.Api;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LAC.Tests;

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

        var testingServices = new ServiceCollection();
        ApiStartupPolicy.RegisterBackgroundWorkers(testingServices, Configuration(), true);
        Assert.Contains(testingServices, x => x.ImplementationType == typeof(AwardPdfExtractionWorker));
        Assert.DoesNotContain(testingServices, x => x.ImplementationType == typeof(DelhiHighCourtSyncWorker));
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
}
