namespace LAC.Tests;

using LAC.Domain;
using LAC.Infrastructure;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

public sealed class DakPostgresFactAttribute : FactAttribute
{
    public DakPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LAC_TEST_POSTGRES")))
            Skip = "Set LAC_TEST_POSTGRES to a disposable local PostgreSQL admin connection; tests create isolated databases.";
    }
}

public sealed class DakPostgresTests
{
    private static RegisterDakCommand Command(string diary, Guid? key = null) => new(
        diary, new DateOnly(2026, 10, 6), "Incoming receipt", "Sender", null, null, null, null, null,
        "Physical", DakPriority.Routine, null, null, null, null, null, null, key);

    [DakPostgresFact]
    public async Task Concurrent_duplicate_registration_has_one_winner_and_database_uniqueness()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        var actorId = await database.CreateActorAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 12).Select(async i =>
        {
            await using var db = database.Context();
            await gate.Task;
            try
            {
                await new DakWorkflowService(db, new TestInMemoryDocumentStorage()).RegisterAsync(Command(i % 2 == 0 ? " Stamp/001 " : "STAMP/001"), actorId);
                return 201;
            }
            catch (DakWorkflowException ex) { return ex.StatusCode; }
        }).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(attempts);
        Assert.Equal(1, results.Count(r => r == 201));
        Assert.Equal(11, results.Count(r => r == 409));
        await using var verify = database.Context();
        Assert.Equal(1, await verify.Daks.CountAsync());
        Assert.Equal(1, await verify.DakMovements.CountAsync());
        Assert.Equal("STAMP/001", (await verify.Daks.SingleAsync()).DiaryNumberKey);
        verify.Daks.Add(new Dak { DiaryNumber = "stamp/001", Subject = "Bypass service", SenderName = "Sender", ReceivedDate = new(2026, 10, 6) });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => verify.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
    }

    [DakPostgresFact]
    public async Task Concurrent_same_request_replays_one_receipt_and_rejects_changed_payload()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        var actorId = await database.CreateActorAsync();
        var command = Command("Stamped/request", Guid.NewGuid());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storages = new System.Collections.Concurrent.ConcurrentBag<TestInMemoryDocumentStorage>();
        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = database.Context();
            var storage = new TestInMemoryDocumentStorage();
            storages.Add(storage);
            await using var scan = new MemoryStream("%PDF-1.4 identical source bytes"u8.ToArray());
            await gate.Task;
            return await new DakWorkflowService(db, storage).RegisterAsync(command with
            {
                DocumentStream = scan, DocumentFileName = "original.pdf", DocumentContentType = "application/pdf"
            }, actorId);
        }).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(attempts);
        Assert.Single(results.Select(r => r.Id).Distinct());
        await using var verify = database.Context();
        Assert.Equal(1, await verify.Daks.CountAsync());
        Assert.Equal(1, await verify.DakMovements.CountAsync());
        Assert.Equal(1, await verify.Documents.CountAsync());
        Assert.Equal(1, storages.Sum(s => s.Files.Count));
        var workflow = new DakWorkflowService(verify, new TestInMemoryDocumentStorage());
        var conflict = await Assert.ThrowsAsync<DakWorkflowException>(() => workflow.RegisterAsync(command with { Subject = "Changed" }, actorId));
        Assert.Equal(409, conflict.StatusCode);
    }

    [DakPostgresFact]
    public async Task Request_committed_between_replay_and_diary_lookup_returns_original()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        var actorId = await database.CreateActorAsync();
        var command = Command("REPLAY/LOOKUP-RACE", Guid.NewGuid());
        var pause = new PauseDiaryCheck();
        await using var pending = database.Context(pause);
        var replay = new DakWorkflowService(pending, new TestInMemoryDocumentStorage()).RegisterAsync(command, actorId);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await using var winner = database.Context();
        Dak committed;
        try { committed = await new DakWorkflowService(winner, new TestInMemoryDocumentStorage()).RegisterAsync(command, actorId); }
        finally { pause.Release.TrySetResult(); }
        Assert.Equal(committed.Id, (await replay).Id);
        Assert.Equal(1, await winner.Daks.CountAsync());
    }

    [DakPostgresFact]
    public async Task Blank_diary_fails_database_constraint_and_cancelled_active_number_stays_reserved()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        var actorId = await database.CreateActorAsync();
        await using var db = database.Context();
        db.Daks.Add(new Dak { DiaryNumber = " \t", Subject = "Blank", SenderName = "Sender", ReceivedDate = new(2026, 10, 6) });
        var blank = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(blank.InnerException).SqlState);
        db.ChangeTracker.Clear();
        var workflow = new DakWorkflowService(db, new TestInMemoryDocumentStorage());
        var dak = await workflow.RegisterAsync(Command("CANCELLED-STAMP"), actorId);
        await workflow.CancelAsync(dak.Id, new CancelDakCommand("Wrong receipt", 0), actorId);
        var duplicate = await Assert.ThrowsAsync<DakWorkflowException>(() => workflow.RegisterAsync(Command("cancelled-stamp"), actorId));
        Assert.Equal(409, duplicate.StatusCode);
    }

    [DakPostgresFact]
    public async Task Archived_legacy_numbers_do_not_block_new_active_receipt_but_cannot_be_reactivated_into_duplicate()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        var actorId = await database.CreateActorAsync();
        await using var db = database.Context();
        var workflow = new DakWorkflowService(db, new TestInMemoryDocumentStorage());
        var first = await workflow.RegisterAsync(Command("Legacy/9"), actorId);
        first.RecordStatus = RecordStatus.Archived;
        await db.SaveChangesAsync();
        var active = await workflow.RegisterAsync(Command("LEGACY/9"), actorId);
        Assert.NotEqual(first.Id, active.Id);
        db.ChangeTracker.Clear();
        var archived = await db.Daks.SingleAsync(d => d.Id == first.Id);
        archived.RecordStatus = RecordStatus.Active;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [DakPostgresFact]
    public async Task Migration_reports_duplicates_without_rewriting_or_partially_migrating()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(migrate: false);
        await using var db = database.Context();
        var previous = db.Database.GetMigrations().Last(m => !m.EndsWith("HardenDakIntakePhaseA"));
        await db.GetService<IMigrator>().MigrateAsync(previous);
        await InsertLegacyAsync(db, " stamp/2 ");
        await InsertLegacyAsync(db, "STAMP/2");
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
        Assert.Contains("duplicate canonical active diary", error.MessageText);
        Assert.Contains("audit-dak-diary.sql", error.Hint);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT array_agg(\"DiaryNumber\" ORDER BY \"DiaryNumber\") FROM \"Daks\"", connection);
        Assert.Equal(new[] { " stamp/2 ", "STAMP/2" }, (string[])(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT count(*) FROM information_schema.columns WHERE table_name = 'Daks' AND column_name = 'DiaryNumberKey'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [DakPostgresFact]
    public async Task Migration_reports_blank_legacy_diary_and_preserves_valid_display_text()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(migrate: false);
        await using var db = database.Context();
        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().Last(m => !m.EndsWith("HardenDakIntakePhaseA")));
        await InsertLegacyAsync(db, " \t ");
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
        Assert.Contains("blank diary", error.MessageText);
        // The test remediates only its synthetic fixture, never office data.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Daks\"");
        await InsertLegacyAsync(db, "  Manual/Ab  17  ");
        await db.Database.MigrateAsync();
        var legacy = await db.Daks.AsNoTracking().SingleAsync();
        Assert.Equal("  Manual/Ab  17  ", legacy.DiaryNumber);
        Assert.Equal("MANUAL/AB  17", legacy.DiaryNumberKey);
        Assert.Null(legacy.HasPhysicalOriginal);
    }

    [DakPostgresFact]
    public async Task Intake_mutation_waiting_on_closure_lock_fails_without_orphan_file()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        var actorId = await database.CreateActorAsync();
        Guid id;
        await using (var db = database.Context()) id = (await new DakWorkflowService(db, new TestInMemoryDocumentStorage()).RegisterAsync(Command("CLOSURE/RACE"), actorId)).Id;
        await using var closer = database.Context();
        await using var transaction = await closer.Database.BeginTransactionAsync();
        await closer.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Daks\" SET \"Status\" = 'Cancelled', \"Revision\" = 1 WHERE \"Id\" = {id}");
        await using var writer = database.Context();
        var storage = new SignallingStorage();
        var task = new DakWorkflowService(writer, storage).AddIntakeAttachmentAsync(id, 0, actorId,
            new MemoryStream("%PDF-1.4"u8.ToArray()), "scan.pdf", "application/pdf", null, null, CancellationToken.None);
        await storage.Stored.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.False(task.IsCompleted);
        await transaction.CommitAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<DakWorkflowException>(() => task)).StatusCode);
        Assert.Empty(storage.Inner.Files);
        Assert.Equal(0, await writer.DakAttachments.CountAsync());
    }

    private static Task InsertLegacyAsync(LacDbContext db, string diary) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO "Daks" ("Id", "DiaryNumber", "ReceivedDate", "Subject", "SenderName", "InwardMode", "Priority", "Status", "Revision", "CreatedAt", "UpdatedAt", "RecordStatus")
        VALUES ({Guid.NewGuid()}, {diary}, {new DateOnly(2026, 10, 6)}, 'Legacy receipt', 'Sender', 'Physical', 'Routine', 'Registered', 0, now(), now(), 'Active')
        """);

    private sealed class SignallingStorage : IDocumentStorage
    {
        public readonly TestInMemoryDocumentStorage Inner = new();
        public readonly TaskCompletionSource Stored = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DocumentStorageWriteResult> SaveAndHashAsync(Stream stream, string name, CancellationToken ct)
        {
            var result = await Inner.SaveAndHashAsync(stream, name, ct); Stored.TrySetResult(); return result;
        }
        public Task<string> SaveAsync(Stream stream, string name, CancellationToken ct) => Inner.SaveAsync(stream, name, ct);
        public Task DeleteAsync(string path, CancellationToken ct) => Inner.DeleteAsync(path, ct);
        public Task<Stream?> OpenReadAsync(string path, CancellationToken ct) => Inner.OpenReadAsync(path, ct);
        public StorageHealth GetHealth() => Inner.GetHealth();
    }

    private sealed class PauseDiaryCheck : DbCommandInterceptor
    {
        public readonly TaskCompletionSource Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool paused;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (!paused && command.CommandText.Contains("EXISTS") && command.CommandText.Contains("\"DiaryNumberKey\""))
            {
                paused = true;
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            }
            return result;
        }
    }
}

internal sealed class DisposableDakDatabase(string adminConnection, string name, string connectionString) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;
    public LacDbContext Context(DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(ConnectionString,
            provider => provider.EnableRetryOnFailure(2));
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new LacDbContext(options.Options);
    }

    public static async Task<DisposableDakDatabase> CreateAsync(bool migrate = true)
    {
        var admin = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LAC_TEST_POSTGRES"));
        if (admin.Host is not ("127.0.0.1" or "localhost" or "::1")) throw new InvalidOperationException("Dak tests require local PostgreSQL.");
        var name = $"lac_dak_test_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection)) await create.ExecuteNonQueryAsync();
        var test = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name, Pooling = false };
        var database = new DisposableDakDatabase(admin.ConnectionString, name, test.ConnectionString);
        try
        {
            if (migrate) { await using var db = database.Context(); await db.Database.MigrateAsync(); }
            return database;
        }
        catch { await database.DisposeAsync(); throw; }
    }

    public async Task<Guid> CreateActorAsync()
    {
        await using var db = Context();
        var actor = new AppUser { Username = $"actor-{Guid.NewGuid():N}", DisplayName = "Test registrar", IsActive = true };
        db.AppUsers.Add(actor); await db.SaveChangesAsync(); return actor.Id;
    }

    public async ValueTask DisposeAsync()
    {
        if (!name.StartsWith("lac_dak_test_", StringComparison.Ordinal) || !Guid.TryParseExact(name[13..], "N", out _))
            throw new InvalidOperationException("Refusing to drop a non-test database.");
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
