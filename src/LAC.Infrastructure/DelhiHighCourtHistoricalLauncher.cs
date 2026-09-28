using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LAC.Infrastructure;

// A manual one-shot launcher, not a hosted recurring worker. The HTTP request
// authorizes and reserves the shared gate; a fresh scope owns the long job.
public sealed class DelhiHighCourtHistoricalLauncher(
    IServiceScopeFactory scopes, IHostApplicationLifetime lifetime,
    DelhiHighCourtSyncGate gate, ILogger<DelhiHighCourtHistoricalLauncher> logger)
{
    public async Task StartAsync(Guid userId, CancellationToken requestCt)
    {
        using var preflight = scopes.CreateScope();
        var service = preflight.ServiceProvider.GetRequiredService<DelhiHighCourtSyncService>();
        await service.EnsureHistoricalStartAllowedAsync(userId, requestCt);
        if (!await gate.Semaphore.WaitAsync(0, requestCt))
            throw new CourtWorkflowException("Delhi High Court sync is already running.", 409);
        try
        {
            // Recheck after reserving the gate, so an already completed one-time
            // run cannot be accepted between preflight and launch.
            await service.EnsureHistoricalStartAllowedAsync(userId, requestCt);
            _ = Task.Run(async () =>
            {
                var handedOff = false;
                try
                {
                    using var jobScope = scopes.CreateScope();
                    var job = jobScope.ServiceProvider.GetRequiredService<DelhiHighCourtSyncService>();
                    handedOff = true;
                    await job.RunHistoricalWithReservedGateAsync(userId, lifetime.ApplicationStopping);
                }
                catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
                {
                    logger.LogInformation("One-time DHC historical backfill interrupted by application shutdown.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "One-time DHC historical backfill failed.");
                }
                finally
                {
                    if (!handedOff) gate.Semaphore.Release();
                }
            }, CancellationToken.None);
        }
        catch
        {
            gate.Semaphore.Release();
            throw;
        }
    }
}
