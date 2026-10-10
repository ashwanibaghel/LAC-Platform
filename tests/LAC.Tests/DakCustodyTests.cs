namespace LAC.Tests;

using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

internal sealed record CustodyActors(Guid Sender, Guid Receiver, Guid Caretaker, Guid Supervisor, Guid SenderDesk, Guid ReceiverDesk, Guid Room)
{
    public static async Task<CustodyActors> CreateAsync(LacDbContext db)
    {
        foreach (var def in PermissionCodes.All)
            if (!await db.Permissions.AnyAsync(p => p.Code == def.Code)) db.Permissions.Add(new Permission { Code = def.Code, Name = def.Name, Description = def.Description, Category = def.Category });
        await db.SaveChangesAsync();
        var holderRole = new Role { Code = $"holder-{Guid.NewGuid():N}", Name = "Test holders" };
        var supervisorRole = new Role { Code = $"supervisor-{Guid.NewGuid():N}", Name = "Test supervisor" };
        db.Roles.AddRange(holderRole, supervisorRole);
        foreach (var p in await db.Permissions.Where(p => p.Code.StartsWith("Dak.")).ToListAsync())
        {
            if (p.Code != PermissionCodes.DakReopen) db.RolePermissions.Add(new RolePermission { Role = holderRole, PermissionId = p.Id, ScopeMode = ScopeMode.All });
            // Giving Move and Resolve to a monitor must still never give it holder custody.
            db.RolePermissions.Add(new RolePermission { Role = supervisorRole, PermissionId = p.Id, ScopeMode = ScopeMode.All });
        }
        AppUser Officer(string label, Role role)
        {
            var u = new AppUser { Username = $"{label}-{Guid.NewGuid():N}", DisplayName = label, IsActive = true };
            u.NormalizedUsername = u.Username.ToUpperInvariant();
            db.AppUsers.Add(u); db.UserRoles.Add(new UserRole { User = u, Role = role }); return u;
        }
        var a = Officer("Sender", holderRole); var b = Officer("Receiver", holderRole);
        var r = Officer("Caretaker", holderRole); var s = Officer("Monitor", supervisorRole);
        var ad = new OfficeDesk { Code = $"A-{Guid.NewGuid():N}", Name = "Sender desk" };
        var bd = new OfficeDesk { Code = $"B-{Guid.NewGuid():N}", Name = "Receiver desk" };
        var rd = new OfficeDesk { Code = $"R-{Guid.NewGuid():N}", Name = "Configured room", Purpose = OfficeDeskPurpose.RecordRoom };
        db.OfficeDesks.AddRange(ad, bd, rd);
        db.UserDeskMemberships.AddRange(new UserDeskMembership { User = a, OfficeDesk = ad }, new UserDeskMembership { User = b, OfficeDesk = bd }, new UserDeskMembership { User = r, OfficeDesk = rd });
        await db.SaveChangesAsync();
        if (!await db.WorkDefinitions.AnyAsync(w => w.Kind == OperationalWorkKind.Correspondence))
        {
            var stream = await db.Workstreams.SingleOrDefaultAsync(w => w.Code == WorkstreamCodes.DakCorrespondence);
            if (stream is null) { stream = new Workstream { Code = WorkstreamCodes.DakCorrespondence, Name = "Synthetic correspondence" }; db.Workstreams.Add(stream); }
            db.WorkDefinitions.Add(new WorkDefinition { Code = $"TEST_DAK_{Guid.NewGuid():N}", Name = "Synthetic receipt responsibility", Kind = OperationalWorkKind.Correspondence, Workstream = stream });
            await db.SaveChangesAsync();
        }
        foreach (var officer in new[] { a, b, r, s }) await TestWorkAllocations.GrantGlobalAsync(db, officer.Id);
        return new(a.Id, b.Id, r.Id, s.Id, ad.Id, bd.Id, rd.Id);
    }

    public async Task<Guid> RegisterAsync(LacDbContext db, bool paper = false)
    {
        var dak = await new DakWorkflowService(db, new TestInMemoryDocumentStorage()).RegisterAsync(
            new RegisterDakCommand($"STAMP/{Guid.NewGuid():N}", new(2026, 10, 7), "Incoming", "External sender", null, null, null, null, null,
                "Physical", DakPriority.Routine, null, null, null, null, null, null), Sender);
        if (paper)
        {
            var tracked = await db.Daks.SingleAsync(d => d.Id == dak.Id);
            tracked.HasPhysicalOriginal = true; tracked.PhysicalOriginalUserId = Sender; tracked.PhysicalOriginalDeskId = SenderDesk;
            tracked.PhysicalState = DakPhysicalState.AtRecordedLocation; tracked.PhysicalOriginalProvenanceNote = "Observed original in hand before dispatch";
            await db.SaveChangesAsync();
        }
        return dak.Id;
    }

    public SendDakCommand Mark(bool paper = false) => new(DakMovementAction.Marked, ReceiverDesk, Receiver, DakDestinationKind.Officer,
        paper, "Check and reply", "Required action", 0, Guid.NewGuid());
}

public sealed class DakCustodyTests
{
    private static LacDbContext Context() => new(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase($"custody-{Guid.NewGuid()}").Options);
    private static DakWorkflowService Workflow(LacDbContext db) => new(db, new TestInMemoryDocumentStorage());

    [Fact]
    public async Task Send_requires_personal_receipt_and_monitor_cannot_forward_for_holder()
    {
        await using var db = Context(); var actors = await CustodyActors.CreateAsync(db); var id = await actors.RegisterAsync(db);
        var w = Workflow(db); var sent = await w.SendAsync(id, actors.Mark(), actors.Sender);
        Assert.Equal("InTransit", sent.RoutingState); Assert.Null(sent.ConfirmedHolderUserId); Assert.Empty(await db.DakAssignments.ToListAsync());
        var receiver = await w.ReceiveAsync(id, sent.TransferId!.Value, new(1, Guid.NewGuid()), actors.Receiver);
        Assert.Equal(actors.Receiver, receiver.ConfirmedHolderUserId); Assert.Equal("WithHolder", receiver.RoutingState);
        var forward = new SendDakCommand(DakMovementAction.Forwarded, actors.Room, actors.Caretaker, DakDestinationKind.RecordRoom, false, "Preserve", null, 2, Guid.NewGuid());
        Assert.Equal(403, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.SendAsync(id, forward, actors.Supervisor))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.SendAsync(id, forward, actors.Sender))).StatusCode);
        var roomSent = await w.SendAsync(id, forward, actors.Receiver);
        Assert.Equal(actors.Receiver, roomSent.ConfirmedHolderUserId);
        var roomReceived = await w.ReceiveAsync(id, roomSent.TransferId!.Value, new(3, Guid.NewGuid()), actors.Caretaker);
        Assert.Equal(actors.Caretaker, roomReceived.ConfirmedHolderUserId);
        var back = await w.SendAsync(id, new(DakMovementAction.Returned, actors.ReceiverDesk, actors.Receiver, DakDestinationKind.Officer, false, null, null, 4, Guid.NewGuid()), actors.Caretaker);
        await w.ReceiveAsync(id, back.TransferId!.Value, new(5, Guid.NewGuid()), actors.Receiver);
        Assert.Single(await db.DakAssignments.ToListAsync());
        Assert.Equal(new[] { DakMovementAction.Registered, DakMovementAction.Marked, DakMovementAction.Received, DakMovementAction.Forwarded,
            DakMovementAction.Received, DakMovementAction.Returned, DakMovementAction.Received }, await db.DakMovements.OrderBy(m => m.SequenceNumber).Select(m => m.Action).ToArrayAsync());
    }

    [Fact]
    public async Task Paper_acknowledgment_pullback_recovery_resolution_and_supervisory_reopen()
    {
        await using var db = Context(); var a = await CustodyActors.CreateAsync(db); var id = await a.RegisterAsync(db, true); var w = Workflow(db);
        var sent = await w.SendAsync(id, a.Mark(true), a.Sender);
        Assert.Equal(a.Sender, sent.PhysicalCustodianUserId); Assert.Equal("InTransit", sent.PhysicalState);
        Assert.Equal(409, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.ReceiveAsync(id, sent.TransferId!.Value, new(1, Guid.NewGuid()), a.Receiver))).StatusCode);
        var received = await w.ReceiveAsync(id, sent.TransferId!.Value, new(1, Guid.NewGuid(), true), a.Receiver);
        Assert.Equal(a.Receiver, received.PhysicalCustodianUserId); Assert.Equal(a.ReceiverDesk, received.PhysicalDeskId);
        var forwarded = await w.SendAsync(id, new(DakMovementAction.Forwarded, a.Room, a.Caretaker, DakDestinationKind.RecordRoom, true, "Store", null, 2, Guid.NewGuid()), a.Receiver);
        var pulled = await w.PullBackAsync(id, forwarded.TransferId!.Value, new("Additional work needed", 3, Guid.NewGuid()), a.Receiver);
        Assert.Equal("ReturnPending", pulled.PhysicalState); Assert.Equal(a.Receiver, pulled.PhysicalCustodianUserId);
        Assert.Equal(409, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.ResolveAsync(id, new(true, "Done", 4, Guid.NewGuid()), a.Receiver))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.ReceiveAsync(id, forwarded.TransferId!.Value, new(4, Guid.NewGuid(), true), a.Caretaker))).StatusCode);
        await w.ConfirmReturnAsync(id, forwarded.TransferId!.Value, new("Recovered the actual original from dispatch", 4, Guid.NewGuid()), a.Receiver);
        Assert.Equal(400, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.ResolveAsync(id, new(false, "Done", 5, Guid.NewGuid()), a.Receiver))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.ResolveAsync(id, new(true, " \t", 5, Guid.NewGuid()), a.Receiver))).StatusCode);
        var resolved = await w.ResolveAsync(id, new(true, "Reply sent; work complete", 5, Guid.NewGuid()), a.Receiver);
        Assert.Equal("Resolved", resolved.Status); Assert.Equal(a.Receiver, resolved.ConfirmedHolderUserId);
        var diary = (await db.Daks.SingleAsync()).DiaryNumber;
        Assert.Equal(409, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.RegisterAsync(
            new RegisterDakCommand($" {diary.ToUpperInvariant()} ", new(2026, 10, 7), "New receipt", "Sender", null, null, null, null, null,
                "Physical", DakPriority.Routine, null, null, null, null, null, null), a.Sender))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.ReopenAsync(id, new("Review", 6, Guid.NewGuid()), a.Receiver))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.ReopenAsync(id, new(" ", 6, Guid.NewGuid()), a.Supervisor))).StatusCode);
        var reopened = await w.ReopenAsync(id, new("New correspondence requires review", 6, Guid.NewGuid()), a.Supervisor);
        Assert.Equal(2, reopened.ProcessingCycle); Assert.Equal(a.Receiver, reopened.ConfirmedHolderUserId); Assert.Equal("InProcess", reopened.Status);
        Assert.DoesNotContain(db.Model.GetEntityTypes(), e => e.ClrType.Name == "DakRequiredWork");
        Assert.DoesNotContain(PermissionCodes.All, p => p.Code == "Dak.Assign");
    }

    [Fact]
    public async Task Digital_initial_pullback_restores_intake_and_idempotent_receipts_survive_later_events()
    {
        await using var db = Context(); var a = await CustodyActors.CreateAsync(db); var id = await a.RegisterAsync(db); var w = Workflow(db);
        var command = a.Mark(); var sent = await w.SendAsync(id, command, a.Sender);
        Assert.Equal(400, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.PullBackAsync(id, sent.TransferId!.Value, new(" ", 1, Guid.NewGuid()), a.Sender))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.PullBackAsync(id, sent.TransferId!.Value, new("Wrong destination", 1, Guid.NewGuid()), a.Supervisor))).StatusCode);
        var pullCommand = new PullBackDakCommand("Wrong destination", 1, Guid.NewGuid());
        var pulled = await w.PullBackAsync(id, sent.TransferId!.Value, pullCommand, a.Sender);
        Assert.Equal("Registered", pulled.Status); Assert.Equal("Unassigned", pulled.RoutingState);
        Assert.Equal(sent, await w.SendAsync(id, command, a.Sender));
        Assert.Equal(pulled, await w.PullBackAsync(id, sent.TransferId.Value, pullCommand, a.Sender));
        Assert.Equal(409, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.SendAsync(id, command with { Remarks = "Changed" }, a.Sender))).StatusCode);
        var sentAgain = await w.SendAsync(id, a.Mark() with { ExpectedRevision = 2 }, a.Sender);
        var receiveCommand = new ReceiveDakCommand(3, Guid.NewGuid());
        var received = await w.ReceiveAsync(id, sentAgain.TransferId!.Value, receiveCommand, a.Receiver);
        await w.ResolveAsync(id, new(true, "Work completed", 4, Guid.NewGuid()), a.Receiver);
        Assert.Equal(received, await w.ReceiveAsync(id, sentAgain.TransferId.Value, receiveCommand, a.Receiver));
        Assert.Equal(409, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.PullBackAsync(id, sentAgain.TransferId.Value, new("Too late", 5, Guid.NewGuid()), a.Sender))).StatusCode);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Movement_and_command_receipts_are_immutable_through_all_save_overloads(int overload)
    {
        await using var db = Context(); var a = await CustodyActors.CreateAsync(db); var id = await a.RegisterAsync(db);
        await Workflow(db).SendAsync(id, a.Mark(), a.Sender);
        async Task Save()
        {
            switch (overload) { case 0: db.SaveChanges(); break; case 1: db.SaveChanges(false); break;
                case 2: await db.SaveChangesAsync(); break; default: await db.SaveChangesAsync(false); break; }
        }
        var movement = await db.DakMovements.FirstAsync(); movement.Remarks = "Tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(Save); db.ChangeTracker.Clear();
        var receipt = await db.DakWorkflowCommandReceipts.SingleAsync(); db.Remove(receipt);
        await Assert.ThrowsAsync<InvalidOperationException>(Save); db.ChangeTracker.Clear();
        var audit = await db.AuditLogs.FirstAsync(r => r.EntityType == "Dak"); audit.EntityType = "Other";
        await Assert.ThrowsAsync<InvalidOperationException>(Save);
    }

    [Fact]
    public async Task Desk_only_legacy_nomination_is_initial_marking_and_pullback_restores_unconfirmed_allocation()
    {
        await using var db = Context(); var a = await CustodyActors.CreateAsync(db); var id = await a.RegisterAsync(db); var w = Workflow(db);
        var dak = await db.Daks.SingleAsync(); dak.Status = DakStatus.InProcess; dak.RoutingState = DakRoutingState.LegacyUnconfirmed;
        db.DakAssignments.Add(new DakAssignment { DakId = id, OfficeDeskId = a.SenderDesk, AssignedByUserId = a.Sender }); await db.SaveChangesAsync();
        var sent = await w.SendAsync(id, a.Mark(), a.Supervisor); Assert.Null(sent.ConfirmedHolderUserId);
        var pulled = await w.PullBackAsync(id, sent.TransferId!.Value, new("Review nomination", 1, Guid.NewGuid()), a.Supervisor);
        Assert.Equal("LegacyUnconfirmed", pulled.RoutingState); Assert.Equal("InProcess", pulled.Status);
        Assert.Null((await db.DakAssignments.SingleAsync()).ReceivedAt);
        var nominated = await w.SendAsync(id, a.Mark() with { ExpectedRevision = 2 }, a.Supervisor);
        var received = await w.ReceiveAsync(id, nominated.TransferId!.Value, new(3, Guid.NewGuid()), a.Receiver);
        Assert.Equal(a.Receiver, received.ConfirmedHolderUserId);
        Assert.Equal(403, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.SendAsync(id,
            new(DakMovementAction.Forwarded, a.SenderDesk, a.Sender, DakDestinationKind.Officer, false, "On behalf", null, 4, Guid.NewGuid()), a.Supervisor))).StatusCode);
    }

    [Fact]
    public async Task Legacy_confirmation_is_explicit_and_terminal_or_archived_dak_never_reopens()
    {
        await using var db = Context(); var a = await CustodyActors.CreateAsync(db); var id = await a.RegisterAsync(db); var w = Workflow(db);
        var dak = await db.Daks.SingleAsync(); dak.Status = DakStatus.InProcess; dak.RoutingState = DakRoutingState.LegacyUnconfirmed;
        db.DakAssignments.Add(new DakAssignment { DakId = id, OfficeDeskId = a.ReceiverDesk, AssignedUserId = a.Receiver, AssignedByUserId = a.Sender });
        await db.SaveChangesAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.ConfirmCustodyAsync(id, new(0, Guid.NewGuid()), a.Supervisor))).StatusCode);
        await w.ConfirmCustodyAsync(id, new(0, Guid.NewGuid()), a.Receiver);
        foreach (var status in new[] { DakStatus.Disposed, DakStatus.Cancelled })
        {
            db.ChangeTracker.Clear(); dak = await db.Daks.SingleAsync(); dak.Status = status; await db.SaveChangesAsync();
            Assert.Equal(409, (await Assert.ThrowsAsync<DakWorkflowException>(() => w.ReopenAsync(id, new("Review", 1, Guid.NewGuid()), a.Supervisor))).StatusCode);
        }
    }
}
