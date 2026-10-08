using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

public static class OfficeSecurityTransaction
{
    private static readonly SemaphoreSlim InMemoryGate = new(1, 1);
    public static async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var path = request.Path.Value ?? "";
        var db = context.HttpContext.RequestServices.GetRequiredService<LacDbContext>();
        var accountMutation = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)
            && (path.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/office/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/officers/", StringComparison.OrdinalIgnoreCase));
        if (!accountMutation) return await OperationalAuthorizationFilter.InvokeAsync(context, next);
        var ct = request.HttpContext.RequestAborted;
        if (!db.Database.IsRelational())
        {
            await InMemoryGate.WaitAsync(ct);
            try { return await OperationalAuthorizationFilter.InvokeAsync(context, next); }
            finally { InMemoryGate.Release(); }
        }
        // Serialize hierarchy checks and changes, including last-admin checks, within the same transaction.
        // This affects account administration only; no business workflow/custody locking is replaced.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(734812036271::bigint)", ct);
            var result = await OperationalAuthorizationFilter.InvokeAsync(context, next);
            if (result is IStatusCodeHttpResult { StatusCode: >= 400 }) await transaction.RollbackAsync(ct);
            else await transaction.CommitAsync(ct);
            return result;
        });
    }
}
