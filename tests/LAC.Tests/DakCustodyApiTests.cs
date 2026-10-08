namespace LAC.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class DakCustodyApiTests(DakTestFactory factory) : IClassFixture<DakTestFactory>
{
    private async Task<HttpClient> Login(string name)
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, DakTestFactory.TestAdminPass))).StatusCode);
        return client;
    }
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object body, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Http_contract_enforces_recipient_holder_supervisor_and_physical_acknowledgment()
    {
        using var admin = await Login(DakTestFactory.TestAdminUser);
        Guid adminId; Guid roleId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); adminId = await db.AppUsers.Where(u => u.Username == DakTestFactory.TestAdminUser).Select(u => u.Id).SingleAsync();
            var role = new Role { Code = $"recipient-{Guid.NewGuid():N}", Name = "Configured recipient" }; roleId = role.Id;
            db.Roles.Add(role);
            foreach (var code in new[] { PermissionCodes.DakView, PermissionCodes.DakReceive, PermissionCodes.DakMove, PermissionCodes.DakPullBack, PermissionCodes.DakResolve })
                db.RolePermissions.Add(new RolePermission { Role = role, PermissionId = await db.Permissions.Where(p => p.Code == code).Select(p => p.Id).SingleAsync(), ScopeMode = ScopeMode.Assigned });
            await db.SaveChangesAsync();
        }
        async Task<Guid> Desk(string name)
        {
            var response = await admin.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"D-{Guid.NewGuid():N}", name, null, null));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        }
        var from = await Desk("Sender"); var to = await Desk("Receiver");
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/admin/users/{adminId}/desks", new AssignDeskRequest(from, true))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/auth/login", new LoginRequest(DakTestFactory.TestAdminUser, DakTestFactory.TestAdminPass))).StatusCode);
        var username = $"recipient-{Guid.NewGuid():N}";
        var create = await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(username, "Receiver", DakTestFactory.TestAdminPass, null, [roleId], null, null));
        var receiverId = (await create.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        using (var scope = factory.Services.CreateScope())
            await TestWorkAllocations.GrantGlobalAsync(scope.ServiceProvider.GetRequiredService<LacDbContext>(), receiverId);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/admin/users/{receiverId}/desks", new AssignDeskRequest(to, true))).StatusCode);
        using var receiver = await Login(username);
        using var form = new MultipartFormDataContent { { new StringContent($"API/{Guid.NewGuid():N}"), "diaryNumber" },
            { new StringContent("2026-10-07"), "receivedDate" }, { new StringContent("Intake"), "subject" }, { new StringContent("Sender"), "senderName" } };
        var registered = await admin.PostAsync("/api/dak", form); Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var id = (await registered.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/dak/{id}/physical-original", new PhysicalOriginalRequest(true, from, adminId, null, "Observed paper in hand", 0))).StatusCode);
        var body = new SendDakRequest("Marked", to, receiverId, "Officer", true, 1, "Prepare reply");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/dak/{id}/transfers", body)).StatusCode);
        var key = Guid.NewGuid(); var dispatched = await Post(admin, $"/api/dak/{id}/transfers", body, key);
        Assert.Equal(HttpStatusCode.OK, dispatched.StatusCode); var sent = (await dispatched.Content.ReadFromJsonAsync<DakCommandResult>())!;
        var incoming = await receiver.GetFromJsonAsync<JsonElement>("/api/dak/delivery-queue?bucket=incoming"); Assert.Equal(id, incoming.GetProperty("items")[0].GetProperty("id").GetGuid());
        var detail = await receiver.GetFromJsonAsync<JsonElement>($"/api/dak/{id}"); Assert.Equal(JsonValueKind.Null, detail.GetProperty("currentAssignment").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, $"/api/dak/{id}/transfers/{sent.TransferId}/receive", new ReceiveDakRequest(2, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(receiver, $"/api/dak/{id}/transfers/{sent.TransferId}/receive", new ReceiveDakRequest(2))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(receiver, $"/api/dak/{id}/transfers/{sent.TransferId}/receive", new ReceiveDakRequest(2, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, $"/api/dak/{id}/move", new MoveDakRequest("Forwarded", from, adminId, null, null, 3))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/dak/{id}/physical-original", new PhysicalOriginalRequest(true, from, adminId, null, "Cannot bypass receive", 3))).StatusCode);
        var forward = await Post(receiver, $"/api/dak/{id}/transfers", new SendDakRequest("Returned", from, adminId, "Officer", true, 3));
        Assert.Equal(HttpStatusCode.OK, forward.StatusCode); var next = (await forward.Content.ReadFromJsonAsync<DakCommandResult>())!;
        Assert.Equal(HttpStatusCode.OK, (await Post(receiver, $"/api/dak/{id}/transfers/{next.TransferId}/pull-back", new ReasonDakRequest("Need file again", 4))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(receiver, $"/api/dak/{id}/resolve", new ResolveDakRequest(true, "Done", 5))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(receiver, $"/api/dak/{id}/transfers/{next.TransferId}/confirm-return", new PhysicalReturnDakRequest("Actual original recovered", 5))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(receiver, $"/api/dak/{id}/resolve", new ResolveDakRequest(false, "Done", 6))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(receiver, $"/api/dak/{id}/resolve", new ResolveDakRequest(true, "Reply completed", 6))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(receiver, $"/api/dak/{id}/reopen", new ReasonDakRequest("Review", 7))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(admin, $"/api/dak/{id}/reopen", new ReasonDakRequest("Supervisory review", 7))).StatusCode);
        var timeline = await receiver.GetFromJsonAsync<JsonElement>($"/api/dak/{id}/timeline");
        var resolution = Assert.Single(timeline.EnumerateArray(), m => m.GetProperty("action").GetString() == "Resolved");
        Assert.True(resolution.GetProperty("stateChanges").GetProperty("completionAttested").GetBoolean());
        Assert.False(resolution.GetProperty("stateChanges").TryGetProperty("command", out _));
        var receiptEvent = Assert.Single(timeline.EnumerateArray(), m => m.GetProperty("action").GetString() == "Received");
        Assert.Equal("InTransit", receiptEvent.GetProperty("stateChanges").GetProperty("before").GetProperty("physicalState").GetString());
        Assert.Equal(receiverId, receiptEvent.GetProperty("stateChanges").GetProperty("after").GetProperty("physicalCustodianUserId").GetGuid());
        Assert.Equal(sent, await (await Post(admin, $"/api/dak/{id}/transfers", body, key)).Content.ReadFromJsonAsync<DakCommandResult>());
        Assert.Equal(HttpStatusCode.NotFound, (await receiver.PostAsJsonAsync($"/api/dak/{id}/required-work", new { workItemId = Guid.NewGuid() })).StatusCode);
        // An initial marker's scoped access may change after B accepts. An unchanged key still
        // returns the original immutable dispatch receipt through the legacy /move adapter.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var grant = await db.RolePermissions.Include(p => p.Role).Include(p => p.Permission)
                .SingleAsync(p => p.Role.Code == "SYSTEM_ADMIN" && p.Permission.Code == PermissionCodes.DakMark);
            grant.ScopeMode = ScopeMode.Workstream;
            var intake = await db.Workstreams.SingleAsync(w => w.Code == WorkstreamCodes.DakCorrespondence);
            if (!await db.UserWorkstreamMemberships.AnyAsync(m => m.UserId == adminId && m.WorkstreamId == intake.Id))
                db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership { UserId = adminId, WorkstreamId = intake.Id, IsActive = true });
            await db.SaveChangesAsync();
        }
        using var another = new MultipartFormDataContent { { new StringContent($"API-RETRY/{Guid.NewGuid():N}"), "diaryNumber" },
            { new StringContent("2026-10-07"), "receivedDate" }, { new StringContent("Retry"), "subject" }, { new StringContent("Sender"), "senderName" } };
        var nextId = (await (await admin.PostAsync("/api/dak", another)).Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var moveBody = new MoveDakRequest("Marked", to, receiverId, "Review", null, 0); var moveKey = Guid.NewGuid();
        var legacy = await Post(admin, $"/api/dak/{nextId}/move", moveBody, moveKey); Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        var legacyResult = await legacy.Content.ReadFromJsonAsync<JsonElement>(); var transferId = legacyResult.GetProperty("transferId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Post(receiver, $"/api/dak/{nextId}/transfers/{transferId}/receive", new ReceiveDakRequest(1))).StatusCode);
        var replay = await Post(admin, $"/api/dak/{nextId}/move", moveBody, moveKey); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayResult = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, replayResult.GetProperty("revision").GetInt32()); Assert.Equal("InTransit", replayResult.GetProperty("routingState").GetString());
        Assert.Equal(legacyResult.GetProperty("commandId").GetGuid(), replayResult.GetProperty("commandId").GetGuid());
    }
}
