using LAC.Infrastructure;

namespace LAC.Api;

// Defaults preserve normal office startup. Acceptance can opt out of database
// bootstrap and automatic workers without disabling manual HTTP workflows.
public static class ApiStartupPolicy
{
    public static bool RunDatabaseBootstrap(IConfiguration configuration) =>
        configuration.GetValue<bool?>("Startup:RunDatabaseBootstrap") ?? true;

    public static bool BackgroundWorkersEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool?>("BackgroundWorkers:Enabled") ?? true;

    public static async Task BootstrapDatabaseAsync(IConfiguration configuration, bool relational,
        Func<Task> migrate, Func<Task> ensureCreated, Func<Task> seed)
    {
        if (!RunDatabaseBootstrap(configuration)) return;
        if (relational) await migrate(); else await ensureCreated();
        await seed();
    }

    public static void RegisterBackgroundWorkers(IServiceCollection services,
        IConfiguration configuration, bool testingEnvironment)
    {
        if (!BackgroundWorkersEnabled(configuration)) return;
        services.AddHostedService<AwardPdfExtractionWorker>();
        if (!testingEnvironment) services.AddHostedService<DelhiHighCourtSyncWorker>();
    }
}
