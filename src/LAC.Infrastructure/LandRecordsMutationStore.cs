using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LAC.Infrastructure;

internal sealed class LandRecordsMutationStore(LacDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Hash(object input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input, Json)))).ToLowerInvariant();

    public async Task<T> ExecuteAsync<T>(IReadOnlyList<string> locks, string? scope, Guid? mutationId, object input,
        Func<Task<T>> apply, CancellationToken ct, Func<Task>? rollback = null)
    {
        if (scope is not null && (!mutationId.HasValue || mutationId == Guid.Empty))
            throw new StaffWorkflowException("MutationIdRequired", "A non-empty mutationId is required.");
        var fingerprint = Hash(input);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
            try
            {
                if (db.Database.IsNpgsql())
                    foreach (var key in locks.Concat(scope is null ? [] : new[] { $"receipt:{scope}:{mutationId}" }).Distinct().Order(StringComparer.Ordinal))
                        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
                if (scope is not null)
                {
                    var previous = await db.Set<LandRecordMutationReceipt>().AsNoTracking().SingleOrDefaultAsync(x => x.Scope == scope && x.MutationId == mutationId, ct);
                    if (previous is not null)
                    {
                        if (previous.RequestHash != fingerprint) throw new StaffWorkflowException("IdempotencyConflict", "This mutationId was already used for different input.", 409);
                        return JsonSerializer.Deserialize<T>(previous.ResponseJson, Json)!;
                    }
                }
                var result = await apply();
                if (scope is not null)
                {
                    db.Add(new LandRecordMutationReceipt { Scope = scope, MutationId = mutationId!.Value, RequestHash = fingerprint, ResponseJson = JsonSerializer.Serialize(result, Json) });
                    await db.SaveChangesAsync(ct);
                }
                if (tx is not null) await tx.CommitAsync(ct);
                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                if (rollback is not null) await rollback();
                db.ChangeTracker.Clear();
                throw new StaffWorkflowException("RevisionConflict", "Record changed. Refresh before saving.", 409);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
            {
                if (rollback is not null) await rollback();
                db.ChangeTracker.Clear();
                throw new StaffWorkflowException("IdentityConflict", "A matching identity or attachment was committed concurrently. Refresh before retrying.", 409);
            }
            catch { if (rollback is not null) await rollback(); db.ChangeTracker.Clear(); throw; }
        });
    }
}
