namespace LAC.Tests;

using LAC.Domain;
using LAC.Infrastructure;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using System.Text.Json;
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
    public async Task Blank_diary_fails_global_database_constraint_and_cancelled_number_stays_reserved()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        var actorId = await database.CreateActorAsync();
        await using var db = database.Context();
        foreach (var recordStatus in Enum.GetValues<RecordStatus>())
        foreach (var status in Enum.GetValues<DakStatus>())
        {
            db.Daks.Add(new Dak { DiaryNumber = " \t\r\n", Subject = "Blank", SenderName = "Sender", ReceivedDate = new(2026, 10, 6), RecordStatus = recordStatus, Status = status });
            var blank = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var error = Assert.IsType<PostgresException>(blank.InnerException);
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            Assert.Equal("CK_Daks_DiaryNumber", error.ConstraintName);
            db.ChangeTracker.Clear();
        }
        var workflow = new DakWorkflowService(db, new TestInMemoryDocumentStorage());
        var dak = await workflow.RegisterAsync(Command("CANCELLED-STAMP"), actorId);
        await workflow.CancelAsync(dak.Id, new CancelDakCommand("Wrong receipt", 0), actorId);
        var duplicate = await Assert.ThrowsAsync<DakWorkflowException>(() => workflow.RegisterAsync(Command("cancelled-stamp"), actorId));
        Assert.Equal(409, duplicate.StatusCode);
    }

    [DakPostgresFact]
    public async Task Diary_identity_remains_globally_reserved_after_every_lifecycle_and_record_status()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        var actorId = await database.CreateActorAsync();
        await using var db = database.Context();
        var workflow = new DakWorkflowService(db, new TestInMemoryDocumentStorage());
        foreach (var recordStatus in Enum.GetValues<RecordStatus>())
        foreach (var status in Enum.GetValues<DakStatus>())
        {
            var command = Command($"Legacy/{recordStatus}/{status}", Guid.NewGuid());
            var first = await workflow.RegisterAsync(command, actorId);
            // Synthetic lifecycle fixtures isolate identity reservation from routing policy.
            first.RecordStatus = recordStatus;
            first.Status = status;
            await db.SaveChangesAsync();
            var duplicate = await Assert.ThrowsAsync<DakWorkflowException>(() => workflow.RegisterAsync(
                command with { DiaryNumber = $" \t{command.DiaryNumber.ToUpperInvariant()}\r\n", RequestId = Guid.NewGuid() }, actorId));
            Assert.Equal(409, duplicate.StatusCode);
            Assert.Equal(first.Id, (await workflow.RegisterAsync(command, actorId)).Id);
            db.Daks.Add(new Dak { DiaryNumber = command.DiaryNumber.ToUpperInvariant(), Subject = "Bypass service", SenderName = "Sender", ReceivedDate = new(2026, 10, 6) });
            var error = Assert.IsType<PostgresException>((await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
            Assert.Equal("IX_Daks_DiaryNumberKey", error.ConstraintName);
            db.ChangeTracker.Clear();
        }
        Assert.Equal(12, await db.Daks.CountAsync());
        Assert.Equal(12, await db.DakMovements.CountAsync());
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var index = new NpgsqlCommand("SELECT indisunique AND indpred IS NULL FROM pg_index WHERE indexrelid = '\"IX_Daks_DiaryNumberKey\"'::regclass", connection);
        Assert.Equal(true, await index.ExecuteScalarAsync());
    }

    [DakPostgresFact]
    public async Task Migration_reports_duplicates_without_rewriting_or_partially_migrating()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(migrate: false);
        await using var db = database.Context();
        var previous = db.Database.GetMigrations().Last(m => !m.EndsWith("HardenDakIntakePhaseA"));
        await db.GetService<IMigrator>().MigrateAsync(previous);
        var activeId = Guid.NewGuid();
        var archivedId = Guid.NewGuid();
        var disposedId = Guid.NewGuid();
        var cancelledId = Guid.NewGuid();
        await InsertLegacyAsync(db, " stamp/2 ", id: activeId);
        await InsertLegacyAsync(db, "STAMP/2", RecordStatus.Archived, id: archivedId);
        // A second duplicate group has no Active records at all.
        await InsertLegacyAsync(db, " closed/3 ", RecordStatus.Archived, DakStatus.Disposed, disposedId);
        await InsertLegacyAsync(db, "CLOSED/3", RecordStatus.Inactive, DakStatus.Cancelled, cancelledId);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
        Assert.Contains("duplicate canonical diary", error.MessageText);
        Assert.Contains("audit-dak-diary.sql", error.Hint);
        using var detail = JsonDocument.Parse(error.Detail!);
        var conflicts = detail.RootElement.EnumerateArray().ToArray();
        Assert.Equal(4, conflicts.Length);
        foreach (var id in new[] { activeId, archivedId, disposedId, cancelledId })
            Assert.Contains(conflicts, row => row.GetProperty("id").GetGuid() == id);
        Assert.Contains(conflicts, row => row.GetProperty("diaryNumber").GetString() == " stamp/2 ");
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT array_agg(\"DiaryNumber\" ORDER BY \"DiaryNumber\") FROM \"Daks\"", connection);
        Assert.Equal(new[] { " closed/3 ", " stamp/2 ", "CLOSED/3", "STAMP/2" }, (string[])(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT count(*) FROM information_schema.columns WHERE table_name = 'Daks' AND column_name = 'DiaryNumberKey'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261005194104_HardenDakIntakePhaseA'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [DakPostgresFact]
    public async Task Migration_reports_blank_legacy_diary_and_preserves_valid_display_text()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(migrate: false);
        await using var db = database.Context();
        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().Last(m => !m.EndsWith("HardenDakIntakePhaseA")));
        var blankId = Guid.NewGuid();
        await InsertLegacyAsync(db, " \t ", RecordStatus.Archived, DakStatus.Cancelled, blankId);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
        Assert.Contains("blank diary", error.MessageText);
        using var detail = JsonDocument.Parse(error.Detail!);
        var blank = Assert.Single(detail.RootElement.EnumerateArray());
        Assert.Equal(blankId, blank.GetProperty("id").GetGuid());
        Assert.Equal(" \t ", blank.GetProperty("diaryNumber").GetString());
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var preserved = new NpgsqlCommand("SELECT \"DiaryNumber\" FROM \"Daks\"", connection);
            Assert.Equal(" \t ", await preserved.ExecuteScalarAsync());
            preserved.CommandText = "SELECT count(*) FROM information_schema.columns WHERE table_name = 'Daks' AND column_name = 'DiaryNumberKey'";
            Assert.Equal(0L, await preserved.ExecuteScalarAsync());
        }
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
    public async Task Audit_script_reports_global_blockers_and_status_only_as_diagnostics()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(migrate: false);
        await using var db = database.Context();
        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().Last(m => !m.EndsWith("HardenDakIntakePhaseA")));
        var blankId = Guid.NewGuid();
        var disposedId = Guid.NewGuid();
        var cancelledId = Guid.NewGuid();
        await InsertLegacyAsync(db, " \t ", RecordStatus.Archived, id: blankId);
        await InsertLegacyAsync(db, " stamp/4 ", RecordStatus.Archived, DakStatus.Disposed, disposedId);
        await InsertLegacyAsync(db, "STAMP/4", RecordStatus.Inactive, DakStatus.Cancelled, cancelledId);
        await InsertLegacyAsync(db, "Unique", status: DakStatus.Cancelled);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "scripts", "audit-dak-diary.sql"))) root = root.Parent;
        Assert.NotNull(root);
        var script = await File.ReadAllTextAsync(Path.Combine(root.FullName, "scripts", "audit-dak-diary.sql"));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(script, connection);
        var blockers = new List<(string Reason, Guid Id, string Raw, string Status, string RecordStatus)>();
        var diagnostics = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            do
            {
                if (reader.FieldCount == 0) continue;
                if (reader.GetName(0) == "blocking_reason")
                {
                    while (await reader.ReadAsync())
                        blockers.Add((reader.GetString(0), reader.GetGuid(3), reader.GetString(4), reader.GetString(6), reader.GetString(7)));
                }
                else
                {
                    Assert.Equal("diagnostic", reader.GetName(0));
                    while (await reader.ReadAsync()) diagnostics.Add(reader.GetString(0));
                }
            } while (await reader.NextResultAsync());
        }
        Assert.Equal(3, blockers.Count);
        Assert.Contains(blockers, r => r == ("BLOCKING_GLOBAL_BLANK", blankId, " \t ", "Registered", "Archived"));
        Assert.Contains(blockers, r => r == ("BLOCKING_GLOBAL_DUPLICATE", disposedId, " stamp/4 ", "Disposed", "Archived"));
        Assert.Contains(blockers, r => r == ("BLOCKING_GLOBAL_DUPLICATE", cancelledId, "STAMP/4", "Cancelled", "Inactive"));
        Assert.Equal(4, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal("DIAGNOSTIC_STATUS_COUNTS", d));
        command.CommandText = "SELECT count(*) FROM \"Daks\"";
        Assert.Equal(4L, await command.ExecuteScalarAsync());
    }

    [DakPostgresFact]
    public async Task Phase_a_can_roll_back_and_reapply_without_changing_permanent_diary_identity()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        var actorId = await database.CreateActorAsync();
        await using var db = database.Context();
        var workflow = new DakWorkflowService(db, new TestInMemoryDocumentStorage());
        var dak = await workflow.RegisterAsync(Command("Manual/Rollback"), actorId);
        dak.RecordStatus = RecordStatus.Archived;
        dak.Status = DakStatus.Disposed;
        await db.SaveChangesAsync();
        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().Last(m => !m.EndsWith("HardenDakIntakePhaseA")));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM information_schema.columns WHERE table_name = 'Daks' AND column_name = 'DiaryNumberKey'", connection);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT count(*) FROM pg_indexes WHERE indexname = 'IX_Daks_DiaryNumberKey'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT count(*) FROM pg_constraint WHERE conname = 'CK_Daks_DiaryNumber'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var restored = await db.Daks.SingleAsync();
        Assert.Equal(dak.Id, restored.Id);
        Assert.Equal("Manual/Rollback", restored.DiaryNumber);
        Assert.Equal("MANUAL/ROLLBACK", restored.DiaryNumberKey);
        Assert.Equal(RecordStatus.Archived, restored.RecordStatus);
        Assert.Equal(DakStatus.Disposed, restored.Status);
        var duplicate = await Assert.ThrowsAsync<DakWorkflowException>(() => workflow.RegisterAsync(Command(" manual/rollback "), actorId));
        Assert.Equal(409, duplicate.StatusCode);
        Assert.Equal(1, await db.DakMovements.CountAsync());
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

    private static Task InsertLegacyAsync(LacDbContext db, string diary, RecordStatus recordStatus = RecordStatus.Active,
        DakStatus status = DakStatus.Registered, Guid? id = null) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO "Daks" ("Id", "DiaryNumber", "ReceivedDate", "Subject", "SenderName", "InwardMode", "Priority", "Status", "Revision", "CreatedAt", "UpdatedAt", "RecordStatus")
        VALUES ({id ?? Guid.NewGuid()}, {diary}, {new DateOnly(2026, 10, 6)}, 'Legacy receipt', 'Sender', 'Physical', 'Routine', {status.ToString()}, 0, now(), now(), {recordStatus.ToString()})
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
        var test = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name, Pooling = false, IncludeErrorDetail = true };
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
