namespace LAC.Tests;

using System.Net;
using System.Net.Http.Json;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class DakTestCustodyFixtures
{
    // Existing CRUD/read tests need a settled holder. Execute both real operations explicitly;
    // no production endpoint or service automatically acknowledges a send.
    public static async Task<HttpResponseMessage> DispatchAsync(DakTestFactory factory, HttpClient client, Guid id,
        MoveDakRequest body, bool receive = true, bool refreshRevision = true)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var admin = await db.AppUsers.SingleAsync(u => u.Username == DakTestFactory.TestAdminUser);
        var receiver = body.ToUserId ?? admin.Id;
        if (body.ToUserId is null && await db.OfficeDesks.AnyAsync(d => d.Id == body.ToDeskId) &&
            !await db.UserDeskMemberships.AnyAsync(m => m.UserId == receiver && m.OfficeDeskId == body.ToDeskId))
        {
            db.UserDeskMemberships.Add(new UserDeskMembership { UserId = receiver, OfficeDeskId = body.ToDeskId }); await db.SaveChangesAsync();
        }
        var permissionId = await db.Permissions.Where(p => p.Code == PermissionCodes.DakReceive).Select(p => p.Id).SingleAsync();
        if (!await db.UserRoles.AnyAsync(u => u.UserId == receiver && u.Role.RolePermissions.Any(p => p.PermissionId == permissionId)))
        {
            var role = new Role { Code = $"fixture-receive-{Guid.NewGuid():N}", Name = "Explicit fixture receiver" };
            db.Roles.Add(role); db.UserRoles.Add(new UserRole { UserId = receiver, Role = role });
            db.RolePermissions.Add(new RolePermission { Role = role, PermissionId = permissionId, ScopeMode = ScopeMode.Assigned }); await db.SaveChangesAsync();
        }
        if (refreshRevision) body = body with { ExpectedRevision = await db.Daks.Where(d => d.Id == id).Select(d => d.Revision).SingleAsync() };
        await TestWorkAllocations.GrantGlobalAsync(db, receiver);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/dak/{id}/move") { Content = JsonContent.Create(body with { ToUserId = receiver }) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await client.SendAsync(request);
        if (!receive || !response.IsSuccessStatusCode) return response;
        db.ChangeTracker.Clear();
        var transfer = await db.DakTransfers.SingleAsync(t => t.DakId == id && t.State == DakTransferState.Pending);
        var revision = await db.Daks.Where(d => d.Id == id).Select(d => d.Revision).SingleAsync();
        var result = await new DakWorkflowService(db, new TestInMemoryDocumentStorage()).ReceiveAsync(id, transfer.Id, new(revision, Guid.NewGuid()), receiver);
        response.Dispose(); return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
    }
}
