namespace LAC.Tests;

using System.Data.Common;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

public sealed class DakCustodyPostgresTests
{
    private static DakWorkflowService Workflow(LacDbContext db) => new(db, new TestInMemoryDocumentStorage());

    [DakPostgresFact]
    public async Task Concurrent_receive_vs_pullback_has_one_winner_and_no_split_custody()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        CustodyActors a; Guid id; DakCommandResult sent;
        await using (var db = database.Context()) { a = await CustodyActors.CreateAsync(db); id = await a.RegisterAsync(db, true); sent = await Workflow(db).SendAsync(id, a.Mark(true), a.Sender); }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> Receive()
        {
            await using var db = database.Context(); await gate.Task;
            try { await Workflow(db).ReceiveAsync(id, sent.TransferId!.Value, new(1, Guid.NewGuid(), true), a.Receiver); return 200; }
            catch (DakWorkflowException e) { return e.StatusCode; }
        }
        async Task<int> Pull()
        {
            await using var db = database.Context(); await gate.Task;
            try { await Workflow(db).PullBackAsync(id, sent.TransferId!.Value, new("Wrong dispatch", 1, Guid.NewGuid()), a.Sender); return 200; }
            catch (DakWorkflowException e) { return e.StatusCode; }
        }
        var tasks = new[] { Receive(), Pull() }; gate.SetResult(); var results = await Task.WhenAll(tasks);
        Assert.Equal(new[] { 200, 409 }, results.Order().ToArray());
        await using var verify = database.Context(); var dak = await verify.Daks.Include(d => d.CurrentAssignment).SingleAsync(); var t = await verify.DakTransfers.SingleAsync();
        Assert.Equal(2, dak.Revision); Assert.Equal(3, await verify.DakMovements.CountAsync()); Assert.Equal(2, await verify.DakWorkflowCommandReceipts.CountAsync());
        if (t.State == DakTransferState.Received)
        {
            Assert.Null(t.PulledBackAt); Assert.Equal(a.Receiver, dak.CurrentAssignment!.AssignedUserId);
            Assert.Equal(a.Receiver, dak.PhysicalOriginalUserId); Assert.Equal(DakPhysicalState.Held, dak.PhysicalState);
        }
        else
        {
            Assert.Null(t.ReceivedAt); Assert.Null(dak.CurrentAssignment); Assert.Equal(a.Sender, dak.PhysicalOriginalUserId);
            Assert.Equal(DakPhysicalState.ReturnPending, dak.PhysicalState);
        }
    }

    [DakPostgresFact]
    public async Task Receive_and_pullback_each_win_when_the_other_waits_on_the_same_lock()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        foreach (var receiveWins in new[] { true, false })
        {
            CustodyActors a; Guid id; DakCommandResult sent;
            await using (var setup = database.Context()) { a = await CustodyActors.CreateAsync(setup); id = await a.RegisterAsync(setup, true); sent = await Workflow(setup).SendAsync(id, a.Mark(true), a.Sender); }
            var pause = new PauseLockedDak(); await using var winnerDb = database.Context(pause);
            var winner = receiveWins ? Workflow(winnerDb).ReceiveAsync(id, sent.TransferId!.Value, new(1, Guid.NewGuid(), true), a.Receiver)
                : Workflow(winnerDb).PullBackAsync(id, sent.TransferId!.Value, new("Withdraw dispatch", 1, Guid.NewGuid()), a.Sender);
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await using var loserDb = database.Context();
            var loser = receiveWins ? Workflow(loserDb).PullBackAsync(id, sent.TransferId!.Value, new("Withdraw dispatch", 1, Guid.NewGuid()), a.Sender)
                : Workflow(loserDb).ReceiveAsync(id, sent.TransferId!.Value, new(1, Guid.NewGuid(), true), a.Receiver);
            try { Assert.False(loser.IsCompleted); } finally { pause.Release.TrySetResult(); }
            await winner;
            Assert.Equal(409, (await Assert.ThrowsAsync<DakWorkflowException>(() => loser)).StatusCode);
        }
    }

    [DakPostgresFact]
    public async Task Concurrent_identical_send_and_receive_replay_single_immutable_results_after_resolution()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); CustodyActors a; Guid id;
        await using (var setup = database.Context()) { a = await CustodyActors.CreateAsync(setup); id = await a.RegisterAsync(setup); }
        var command = a.Mark();
        var sends = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Context(); return await Workflow(db).SendAsync(id, command, a.Sender); }));
        Assert.All(sends, s => Assert.Equal(sends[0], s));
        var receiveCommand = new ReceiveDakCommand(1, Guid.NewGuid());
        var receives = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = database.Context(); return await Workflow(db).ReceiveAsync(id, sends[0].TransferId!.Value, receiveCommand, a.Receiver); }));
        Assert.All(receives, r => Assert.Equal(receives[0], r));
        await using var verify = database.Context(); var w = Workflow(verify);
        await w.ResolveAsync(id, new(true, "All work completed", 2, Guid.NewGuid()), a.Receiver);
        Assert.Equal(sends[0], await w.SendAsync(id, command, a.Sender));
        Assert.Equal(receives[0], await w.ReceiveAsync(id, sends[0].TransferId!.Value, receiveCommand, a.Receiver));
        Assert.Equal(409, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.SendAsync(id, command with { Remarks = "Changed" }, a.Sender))).StatusCode);
        Assert.Single(await verify.DakTransfers.ToListAsync()); Assert.Single(await verify.DakAssignments.ToListAsync());
        Assert.Equal(4, await verify.DakMovements.CountAsync()); Assert.Equal(3, await verify.DakWorkflowCommandReceipts.CountAsync());
    }

    [DakPostgresFact]
    public async Task Membership_revoked_while_receiver_waits_prevents_acceptance()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); CustodyActors a; Guid id; DakCommandResult sent;
        await using (var setup = database.Context()) { a = await CustodyActors.CreateAsync(setup); id = await a.RegisterAsync(setup); sent = await Workflow(setup).SendAsync(id, a.Mark(), a.Sender); }
        await using var holder = database.Context(); await using var transaction = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlInterpolatedAsync($"SELECT * FROM \"Daks\" WHERE \"Id\" = {id} FOR UPDATE");
        await using var receiver = database.Context();
        var pending = Workflow(receiver).ReceiveAsync(id, sent.TransferId!.Value, new(1, Guid.NewGuid()), a.Receiver);
        await holder.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserDeskMemberships\" SET \"IsActive\" = false WHERE \"UserId\" = {a.Receiver}");
        await transaction.CommitAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<DakWorkflowException>(() => pending)).StatusCode);
        Assert.Equal(DakTransferState.Pending, (await receiver.DakTransfers.SingleAsync()).State);
        Assert.Empty(await receiver.DakAssignments.ToListAsync());
    }

    [DakPostgresFact]
    public async Task Physical_roundtrip_return_recovery_and_reopen_preserve_history_and_constraints()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); await using var db = database.Context(); var a = await CustodyActors.CreateAsync(db);
        var id = await a.RegisterAsync(db, true); var w = Workflow(db); var sent = await w.SendAsync(id, a.Mark(true), a.Sender);
        await w.ReceiveAsync(id, sent.TransferId!.Value, new(1, Guid.NewGuid(), true), a.Receiver);
        var room = await w.SendAsync(id, new(DakMovementAction.Forwarded, a.Room, a.Caretaker, DakDestinationKind.RecordRoom, true, "Preserve", null, 2, Guid.NewGuid()), a.Receiver);
        await w.ReceiveAsync(id, room.TransferId!.Value, new(3, Guid.NewGuid(), true), a.Caretaker);
        var returned = await w.SendAsync(id, new(DakMovementAction.Returned, a.ReceiverDesk, a.Receiver, DakDestinationKind.Officer, true, "Review", null, 4, Guid.NewGuid()), a.Caretaker);
        await w.PullBackAsync(id, returned.TransferId!.Value, new("File needs checking", 5, Guid.NewGuid()), a.Caretaker);
        Assert.Equal(409, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.ResolveAsync(id, new(true, "Done", 6, Guid.NewGuid()), a.Caretaker))).StatusCode);
        await w.ConfirmReturnAsync(id, returned.TransferId.Value, new("Actual paper recovered", 6, Guid.NewGuid()), a.Caretaker);
        await w.ResolveAsync(id, new(true, "All work done", 7, Guid.NewGuid()), a.Caretaker);
        await w.ReopenAsync(id, new("New review", 8, Guid.NewGuid()), a.Supervisor);
        Assert.Equal(a.Caretaker, (await db.Daks.SingleAsync()).PhysicalOriginalUserId);
        Assert.Equal(10, await db.DakMovements.CountAsync()); Assert.Single(await db.DakAssignments.ToListAsync());
        await using var connection = new NpgsqlConnection(database.ConnectionString); await connection.OpenAsync();
        foreach (var sql in new[] {
            "UPDATE \"DakMovements\" SET \"Remarks\" = 'tampered'", "DELETE FROM \"DakMovements\"",
            "UPDATE \"AuditLogs\" SET \"EntityType\" = 'Other' WHERE \"EntityType\" = 'Dak'",
            "DELETE FROM \"DakWorkflowCommandReceipts\"", "UPDATE \"DakTransfers\" SET \"Remarks\" = 'tampered'",
            "UPDATE \"DakTransfers\" SET \"State\" = 'Pending' WHERE \"State\" = 'Received'", "DELETE FROM \"DakTransfers\"",
            $"UPDATE \"DakAssignments\" SET \"AssignedUserId\" = '{a.Supervisor}' WHERE \"DakId\" = '{id}'",
            "TRUNCATE \"DakMovements\", \"DakWorkflowCommandReceipts\", \"DakTransfers\" CASCADE" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }
        var previous = db.Database.GetMigrations().TakeWhile(m => !m.EndsWith("AddDakAcknowledgedCustody")).Last();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous));
        Assert.Contains("downgrade blocked", error.MessageText); Assert.Equal(10, await db.DakMovements.CountAsync());
    }

    [DakPostgresFact]
    public async Task Different_send_keys_and_resolve_vs_send_have_only_one_committed_transition()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); CustodyActors a; Guid id;
        await using (var setup = database.Context()) { a = await CustodyActors.CreateAsync(setup); id = await a.RegisterAsync(setup); }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var db = database.Context(); await gate.Task;
            try { await Workflow(db).SendAsync(id, a.Mark(), a.Sender); return 200; } catch (DakWorkflowException e) { return e.StatusCode; }
        }).ToArray(); gate.SetResult(); var sends = await Task.WhenAll(attempts);
        Assert.Equal(1, sends.Count(x => x == 200)); Assert.Equal(4, sends.Count(x => x == 409));
        await using (var receive = database.Context())
        {
            var transfer = await receive.DakTransfers.SingleAsync();
            await Workflow(receive).ReceiveAsync(id, transfer.Id, new(1, Guid.NewGuid()), a.Receiver);
        }
        var nextGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> Resolve()
        {
            await using var db = database.Context(); await nextGate.Task;
            try { await Workflow(db).ResolveAsync(id, new(true, "All work complete", 2, Guid.NewGuid()), a.Receiver); return 200; } catch (DakWorkflowException e) { return e.StatusCode; }
        }
        async Task<int> Forward()
        {
            await using var db = database.Context(); await nextGate.Task;
            try { await Workflow(db).SendAsync(id, new(DakMovementAction.Forwarded, a.Room, a.Caretaker, DakDestinationKind.RecordRoom, false, "Review", null, 2, Guid.NewGuid()), a.Receiver); return 200; } catch (DakWorkflowException e) { return e.StatusCode; }
        }
        var next = new[] { Resolve(), Forward() }; nextGate.SetResult();
        Assert.Equal(new[] { 200, 409 }, (await Task.WhenAll(next)).Order().ToArray());
        await using var verify = database.Context(); Assert.Equal(3, (await verify.Daks.SingleAsync()).Revision);
        Assert.Equal(4, await verify.DakMovements.CountAsync()); Assert.Equal(3, await verify.DakWorkflowCommandReceipts.CountAsync());
    }

    [DakPostgresFact]
    public async Task Permission_revocation_while_receiver_waits_is_reauthorized_inside_transaction()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); CustodyActors a; Guid id; DakCommandResult sent;
        await using (var setup = database.Context()) { a = await CustodyActors.CreateAsync(setup); id = await a.RegisterAsync(setup); sent = await Workflow(setup).SendAsync(id, a.Mark(), a.Sender); }
        await using var blocker = database.Context(); await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT * FROM \"Daks\" WHERE \"Id\" = {id} FOR UPDATE");
        await using var receiver = database.Context(); var task = Workflow(receiver).ReceiveAsync(id, sent.TransferId!.Value, new(1, Guid.NewGuid()), a.Receiver);
        await blocker.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"RolePermissions\" SET \"PermissionId\" = (SELECT \"Id\" FROM \"Permissions\" WHERE \"Code\" = 'Dak.Reopen') WHERE \"RoleId\" IN (SELECT \"RoleId\" FROM \"UserRoles\" WHERE \"UserId\" = {a.Receiver}) AND \"PermissionId\" IN (SELECT \"Id\" FROM \"Permissions\" WHERE \"Code\" = 'Dak.Receive')");
        await transaction.CommitAsync(); Assert.Equal(403, (await Assert.ThrowsAsync<DakWorkflowException>(() => task)).StatusCode);
        Assert.Equal(1, await receiver.DakWorkflowCommandReceipts.CountAsync()); Assert.Empty(await receiver.DakAssignments.ToListAsync());
    }

    [DakPostgresFact]
    public async Task Structural_preflight_fails_before_schema_changes_and_reports_raw_legacy_identity()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(migrate: false); await using var db = database.Context();
        var previous = db.Database.GetMigrations().TakeWhile(m => !m.EndsWith("AddDakAcknowledgedCustody")).Last();
        await db.GetService<IMigrator>().MigrateAsync(previous);
        var id = Guid.NewGuid(); var badDesk = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Daks\" (\"Id\",\"DiaryNumber\",\"ReceivedDate\",\"Subject\",\"SenderName\",\"InwardMode\",\"Priority\",\"Status\",\"Revision\",\"RecordStatus\",\"CreatedAt\",\"UpdatedAt\") VALUES ({id}, ' Raw/Stamp ', '2026-10-01', 'Legacy', 'Sender', 'Physical', 'Routine', 'InProcess', 5, 'Active', now(), now())");
        // Corruption fixture only in this disposable database. Reset to normal enforcement before migration.
        await db.Database.ExecuteSqlInterpolatedAsync($"SET session_replication_role = replica; INSERT INTO \"DakAssignments\" (\"Id\",\"DakId\",\"OfficeDeskId\",\"AssignedByUserId\",\"AssignedAt\",\"IsActive\",\"RecordStatus\",\"CreatedAt\",\"UpdatedAt\") VALUES ({Guid.NewGuid()}, {id}, {badDesk}, {Guid.NewGuid()}, now(), true, 'Active', now(), now()); SET session_replication_role = origin;");
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
        Assert.Contains("structurally invalid", error.MessageText); Assert.Contains(id.ToString(), error.Detail); Assert.Contains(" Raw/Stamp ", error.Detail);
        await using var connection = new NpgsqlConnection(database.ConnectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM information_schema.columns WHERE table_name = 'Daks' AND column_name = 'RoutingState'", connection);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT \"DiaryNumber\" FROM \"Daks\""; Assert.Equal(" Raw/Stamp ", await command.ExecuteScalarAsync());
    }

    [DakPostgresFact]
    public async Task Additive_migration_preserves_legacy_facts_without_inventing_receipt_and_unused_schema_rolls_back()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(migrate: false); await using var db = database.Context();
        var previous = db.Database.GetMigrations().TakeWhile(m => !m.EndsWith("AddDakAcknowledgedCustody")).Last();
        await db.GetService<IMigrator>().MigrateAsync(previous);
        var deskId = Guid.NewGuid(); var userId = Guid.NewGuid(); var dakId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AppUsers\" (\"Id\", \"Username\", \"DisplayName\", \"IsActive\", \"RecordStatus\", \"CreatedAt\", \"UpdatedAt\", \"PasswordHash\", \"NormalizedUsername\") VALUES ({userId}, 'legacy', 'Legacy officer', true, 'Active', now(), now(), '', 'LEGACY')");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"OfficeDesks\" (\"Id\", \"Code\", \"Name\", \"IsActive\", \"RecordStatus\", \"CreatedAt\", \"UpdatedAt\") VALUES ({deskId}, 'LEGACY', 'Legacy desk', true, 'Active', now(), now())");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Daks\" (\"Id\", \"DiaryNumber\", \"ReceivedDate\", \"Subject\", \"SenderName\", \"InwardMode\", \"Priority\", \"Status\", \"Revision\", \"RecordStatus\", \"CreatedAt\", \"UpdatedAt\") VALUES ({dakId}, '  Manual/Legacy  ', '2026-10-01', 'Legacy', 'Sender', 'Physical', 'Routine', 'InProcess', 5, 'Active', now(), now())");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"DakAssignments\" (\"Id\", \"DakId\", \"OfficeDeskId\", \"AssignedUserId\", \"AssignedByUserId\", \"AssignedAt\", \"IsActive\", \"RecordStatus\", \"CreatedAt\", \"UpdatedAt\") VALUES ({Guid.NewGuid()}, {dakId}, {deskId}, {userId}, {userId}, now(), true, 'Active', now(), now())");
        await db.Database.MigrateAsync();
        var legacy = await db.Daks.SingleAsync(); Assert.Equal("  Manual/Legacy  ", legacy.DiaryNumber); Assert.Equal(5, legacy.Revision);
        Assert.Equal(DakRoutingState.LegacyUnconfirmed, legacy.RoutingState); Assert.Null((await db.DakAssignments.SingleAsync()).ReceivedAt);
        Assert.Equal(DakPhysicalState.Unknown, legacy.PhysicalState); Assert.Empty(await db.DakTransfers.ToListAsync());
        await db.GetService<IMigrator>().MigrateAsync(previous); await db.Database.MigrateAsync();
        db.ChangeTracker.Clear(); Assert.Equal("  Manual/Legacy  ", (await db.Daks.SingleAsync()).DiaryNumber);
    }

    private sealed class PauseLockedDak : DbCommandInterceptor
    {
        public readonly TaskCompletionSource Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool paused;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default)
        {
            if (!paused && command.CommandText.Contains("FOR UPDATE")) { paused = true; Reached.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct); }
            return result;
        }
    }
}
