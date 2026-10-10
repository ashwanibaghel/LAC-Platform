namespace LAC.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Shared synthetic fixture for API, PostgreSQL and production-bundle browser verification.
public sealed record DakMovementFixtureData(Guid Sender, Guid Receiver, Guid Caretaker, Guid Monitor,
    Guid SenderDesk, Guid ReceiverDesk, Guid Room, Guid EmptyDesk, Guid UnauthorizedUser,
    string SenderLogin, string ReceiverLogin, string CaretakerLogin, string Password);

public static class DakMovementFixtures
{
    public static async Task<DakMovementFixtureData> CreateAsync(LacDbContext db, string password)
    {
        var actors = await CustodyActors.CreateAsync(db);
        var ids = new[] { actors.Sender, actors.Receiver, actors.Caretaker, actors.Supervisor };
        var users = await db.AppUsers.Where(u => ids.Contains(u.Id)).ToListAsync();
        foreach (var u in users) u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, password);
        var empty = new OfficeDesk { Code = $"EMPTY-{Guid.NewGuid():N}", Name = "Correspondence review desk (no eligible members)" };
        var invalid = new AppUser { Username = $"ineligible-{Guid.NewGuid():N}", DisplayName = "Member without receipt authority" };
        invalid.NormalizedUsername = invalid.Username.ToUpperInvariant();
        invalid.PasswordHash = new PasswordHasher<AppUser>().HashPassword(invalid, password);
        db.AddRange(empty, invalid);
        db.UserDeskMemberships.Add(new UserDeskMembership { User = invalid, OfficeDesk = empty });
        await db.SaveChangesAsync();
        string Login(Guid id) => users.Single(u => u.Id == id).Username;
        return new(actors.Sender, actors.Receiver, actors.Caretaker, actors.Supervisor, actors.SenderDesk, actors.ReceiverDesk,
            actors.Room, empty.Id, invalid.Id, Login(actors.Sender), Login(actors.Receiver), Login(actors.Caretaker), password);
    }
}

public sealed class DakMovementTargetsTests
{
    [Theory]
    [InlineData("no-receive")][InlineData("own-only")][InlineData("inactive-role")]
    [InlineData("inactive-user")][InlineData("removed-membership")][InlineData("inactive-desk")]
    [InlineData("helper-without-delegation")]
    [InlineData("no-receipt-allocation")]
    public async Task Ineligible_recipient_is_excluded_and_crafted_send_is_rejected(string failure)
    {
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase($"target-{Guid.NewGuid()}").Options);
        var a = await CustodyActors.CreateAsync(db); var id = await a.RegisterAsync(db);
        var receiver = await db.AppUsers.SingleAsync(u => u.Id == a.Receiver);
        if (failure is "no-receive" or "own-only" or "inactive-role")
        {
            db.UserRoles.RemoveRange(await db.UserRoles.Where(u => u.UserId == a.Receiver).ToListAsync());
            var role = new Role { Code = Guid.NewGuid().ToString("N"), Name = "Test receipt role", IsActive = failure != "inactive-role" };
            db.Roles.Add(role); db.UserRoles.Add(new UserRole { UserId = receiver.Id, Role = role });
            if (failure != "no-receive") db.RolePermissions.Add(new RolePermission { Role = role,
                PermissionId = (await db.Permissions.SingleAsync(p => p.Code == PermissionCodes.DakReceive)).Id,
                ScopeMode = failure == "own-only" ? ScopeMode.Own : ScopeMode.All });
        }
        if (failure == "inactive-user") receiver.IsActive = false;
        if (failure == "removed-membership") (await db.UserDeskMemberships.SingleAsync(m => m.UserId == receiver.Id)).RemovedAt = DateTimeOffset.UtcNow;
        if (failure == "inactive-desk") (await db.OfficeDesks.SingleAsync(d => d.Id == a.ReceiverDesk)).IsActive = false;
        if (failure == "helper-without-delegation") receiver.SupervisingOfficerId = a.Sender;
        if (failure == "no-receipt-allocation") db.WorkAllocations.RemoveRange(await db.WorkAllocations.Where(w => w.UserId == receiver.Id).ToListAsync());
        await db.SaveChangesAsync();
        var dak = await db.Daks.SingleAsync(d => d.Id == id);
        Assert.False(await DakRecipientEligibility.CanReceiveAsync(db, dak, a.Receiver, a.ReceiverDesk, default));
        var error = await Assert.ThrowsAsync<DakWorkflowException>(() => new DakWorkflowService(db, new TestInMemoryDocumentStorage()).SendAsync(id, a.Mark(), a.Sender));
        Assert.Equal(400, error.StatusCode);
        Assert.Empty(await db.DakTransfers.ToListAsync()); Assert.Equal(0, dak.Revision);
    }

    [Fact]
    public async Task Cookie_API_distinguishes_empty_configuration_members_and_holder_authority()
    {
        using var factory = new DakTestFactory();
        using var admin = await Login(factory, DakTestFactory.TestAdminUser, DakTestFactory.TestAdminPass);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var registrar = await db.AppUsers.SingleAsync(u => u.Username == DakTestFactory.TestAdminUser);
        var id = (await new DakWorkflowService(db, new TestInMemoryDocumentStorage()).RegisterAsync(Command(), registrar.Id)).Id;
        var emptyResponse = await admin.GetAsync($"/api/dak/{id}/movement-targets"); Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        Assert.Empty((await emptyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("desks").EnumerateArray());
        var a = await DakMovementFixtures.CreateAsync(db, TestCredentials.SharedPassword);
        await VerifyCookieWorkflow(factory, db, a, id, admin);
    }

    [DakPostgresFact]
    public async Task Real_Postgres_targets_and_send_receive_forward_return_pullback_keep_custody_and_audit()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        using var factory = new DakDirectoryPgFactory(database.ConnectionString);
        using var admin = await Login(factory, DakTestFactory.TestAdminUser, DakTestFactory.TestAdminPass);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var a = await DakMovementFixtures.CreateAsync(db, TestCredentials.SharedPassword);
        var id = (await new DakWorkflowService(db, new TestInMemoryDocumentStorage()).RegisterAsync(Command(), a.Sender)).Id;
        await VerifyCookieWorkflow(factory, db, a, id, admin);
    }

    [DakPostgresFact]
    public async Task Receipt_permission_revoked_after_target_lookup_rejects_send_without_transfer()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); await using var db = database.Context();
        var a = await CustodyActors.CreateAsync(db); var id = await a.RegisterAsync(db);
        var dak = await db.Daks.SingleAsync(d => d.Id == id);
        Assert.True(await DakRecipientEligibility.CanReceiveAsync(db, dak, a.Receiver, a.ReceiverDesk, default));
        db.UserRoles.RemoveRange(await db.UserRoles.Where(u => u.UserId == a.Receiver).ToListAsync()); await db.SaveChangesAsync();
        Assert.Equal(400, (await Assert.ThrowsAsync<DakWorkflowException>(() => new DakWorkflowService(db, new TestInMemoryDocumentStorage()).SendAsync(id, a.Mark(), a.Sender))).StatusCode);
        Assert.Empty(await db.DakTransfers.ToListAsync()); Assert.Equal(0, (await db.Daks.SingleAsync()).Revision);
    }

    private static RegisterDakCommand Command() => new($"UI-AUDIT/{Guid.NewGuid():N}", new(2026, 10, 10), "Synthetic official correspondence", "Synthetic department", null, null, null, null, null, "Physical", DakPriority.Routine, null, null, null, null, null, null);
    private static async Task<HttpClient> Login(WebApplicationFactory<Program> factory, string name, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, password))).StatusCode); return client;
    }
    private static Task<HttpResponseMessage> Post(HttpClient client, string url, object payload, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString()); return client.SendAsync(request);
    }
    private static async Task VerifyCookieWorkflow(WebApplicationFactory<Program> factory, LacDbContext db, DakMovementFixtureData a, Guid id, HttpClient admin)
    {
        using var sender = await Login(factory, a.SenderLogin, a.Password);
        using var receiver = await Login(factory, a.ReceiverLogin, a.Password);
        using var caretaker = await Login(factory, a.CaretakerLogin, a.Password);
        var initial = await admin.GetFromJsonAsync<JsonElement>($"/api/dak/{id}"); Assert.Equal("Unassigned", initial.GetProperty("routingState").GetString());
        var targets = await admin.GetFromJsonAsync<JsonElement>($"/api/dak/{id}/movement-targets");
        var empty = targets.GetProperty("desks").EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == a.EmptyDesk);
        Assert.Empty(empty.GetProperty("members").EnumerateArray());
        var eligible = targets.GetProperty("desks").EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == a.ReceiverDesk);
        Assert.Equal(a.Receiver, Assert.Single(eligible.GetProperty("members").EnumerateArray()).GetProperty("userId").GetGuid());
        var key = Guid.NewGuid(); var mark = new SendDakRequest("Marked", a.ReceiverDesk, a.Receiver, "Officer", false, 0, "Examine and report", "Official instruction");
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, $"/api/dak/{id}/transfers", mark with { ToDeskId = a.EmptyDesk, ToUserId = a.UnauthorizedUser })).StatusCode);
        var sent = await (await Post(admin, $"/api/dak/{id}/transfers", mark, key)).Content.ReadFromJsonAsync<DakCommandResult>(); Assert.NotNull(sent);
        Assert.Equal(sent, await (await Post(admin, $"/api/dak/{id}/transfers", mark, key)).Content.ReadFromJsonAsync<DakCommandResult>());
        Assert.Equal("InTransit", sent.RoutingState); Assert.Null(sent.ConfirmedHolderUserId);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(sender, $"/api/dak/{id}/transfers/{sent.TransferId}/receive", new ReceiveDakRequest(1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(receiver, $"/api/dak/{id}/transfers/{sent.TransferId}/receive", new ReceiveDakRequest(1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/dak/{id}/movement-targets")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, $"/api/dak/{id}/transfers", new SendDakRequest("Forwarded", a.Room, a.Caretaker, "RecordRoom", false, 2))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await receiver.GetAsync($"/api/dak/{id}/movement-targets")).StatusCode);
        var forward = await (await Post(receiver, $"/api/dak/{id}/transfers", new SendDakRequest("Forwarded", a.Room, a.Caretaker, "RecordRoom", false, 2))).Content.ReadFromJsonAsync<DakCommandResult>(); Assert.NotNull(forward);
        Assert.Equal(HttpStatusCode.OK, (await Post(caretaker, $"/api/dak/{id}/transfers/{forward.TransferId}/receive", new ReceiveDakRequest(3))).StatusCode);
        var returned = await (await Post(caretaker, $"/api/dak/{id}/transfers", new SendDakRequest("Returned", a.ReceiverDesk, a.Receiver, "Officer", false, 4))).Content.ReadFromJsonAsync<DakCommandResult>(); Assert.NotNull(returned);
        Assert.Equal(HttpStatusCode.OK, (await Post(caretaker, $"/api/dak/{id}/transfers/{returned.TransferId}/pull-back", new ReasonDakRequest("Retain for further action", 5))).StatusCode);
        db.ChangeTracker.Clear(); var dak = await db.Daks.Include(d => d.CurrentAssignment).SingleAsync(d => d.Id == id);
        Assert.Equal(a.Caretaker, dak.CurrentAssignment!.AssignedUserId); Assert.Null(dak.PhysicalOriginalUserId); Assert.Equal(DakPhysicalState.Unknown, dak.PhysicalState);
        Assert.Equal(3, await db.DakTransfers.CountAsync(t => t.DakId == id));
        Assert.Equal(new[] { "Registered", "Marked", "Received", "Forwarded", "Received", "Returned", "PulledBack" }, await db.DakMovements.Where(m => m.DakId == id).OrderBy(m => m.SequenceNumber).Select(m => m.Action.ToString()).ToArrayAsync());
    }
}
