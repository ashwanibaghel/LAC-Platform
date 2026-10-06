using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LAC.Tests;

public sealed class AllocationTestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class DynamicAllocationApiTests : IDisposable
{
    private readonly RbacFactory root = new();
    private readonly WebApplicationFactory<Program> factory;
    private readonly AllocationTestClock clock = new();
    public DynamicAllocationApiTests() => factory = root.WithWebHostBuilder(b => b.ConfigureServices(s =>
    {
        s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(clock);
    }));
    public void Dispose() { factory.Dispose(); root.Dispose(); }
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
    private async Task<HttpClient> Login(string name, string password)
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, password))).StatusCode);
        return client;
    }
    private Task<HttpClient> Admin() => Login(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass);
    private async Task<Guid> Role(HttpClient admin, params string[] codes)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/roles", new CreateRoleRequest($"T_{Guid.NewGuid():N}", "Test role", null,
            codes.Select(c => new RolePermissionInput(c, ScopeMode.All)).ToList()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }
    private async Task<(Guid Id, string Username, string Password, HttpClient Client)> Officer(HttpClient admin, params Guid[] roles)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var name = $"officer_{Guid.NewGuid():N}"; var password = TestCredentials.NewPassword();
        var response = await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(name, "Proof Officer", password,
            (await db.Designations.SingleAsync(x => x.Code == "PATWARI")).Id, roles, null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        return (id, name, password, await Login(name, password));
    }
    private async Task<(Guid Home, Guid Second, Guid Cross, Guid Work)> Geography()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var subdivisions = await db.SubDivisions.Take(3).ToListAsync();
        var home = new Village { Name = "PROOF Bijwasan", SubDivisionId = subdivisions[0].Id };
        var second = new Village { Name = "PROOF Dwarka", SubDivisionId = subdivisions[0].Id };
        var cross = new Village { Name = "PROOF Najafgarh", SubDivisionId = subdivisions[1].Id };
        db.AddRange(home, second, cross); await db.SaveChangesAsync();
        return (home.Id, second.Id, cross.Id, (await db.WorkDefinitions.SingleAsync(x => x.Code == "LR")).Id);
    }
    private WorkAllocationInput Input(Guid work, params Guid[] villages) => new(work, clock.Now.AddDays(-1), null, "PROOF-ORDER", null,
        villages.Select(v => new AllocationScopeInput(AllocationScopeKind.Village, VillageId: v)).ToList());
    private async Task<Guid> Allocate(HttpClient admin, Guid user, WorkAllocationInput input)
    {
        var response = await admin.PostAsJsonAsync($"/api/admin/users/{user}/allocations", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }
    private Task<HttpResponseMessage> Write(HttpClient client, Guid village) => client.PostAsJsonAsync("/api/khatauni",
        new { villageId = village, referenceNumber = $"PROOF_{Guid.NewGuid():N}", verificationStatus = "Draft" });

    [Fact]
    public async Task Broad_view_is_preserved_but_actions_require_home_or_temporary_allocation()
    {
        using var admin = await Admin(); var geo = await Geography();
        var role = await Role(admin, PermissionCodes.LrView, PermissionCodes.LrEdit);
        var officer = await Officer(admin, role); using var client = officer.Client;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/villages/{geo.Cross}/khatauni")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(client, geo.Home)).StatusCode);
        await Allocate(admin, officer.Id, Input(geo.Work, geo.Home, geo.Second));
        Assert.Equal(HttpStatusCode.Created, (await Write(client, geo.Home)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Write(client, geo.Second)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(client, geo.Cross)).StatusCode);
        await Allocate(admin, officer.Id, Input(geo.Work, geo.Cross) with { ValidTo = clock.Now.AddHours(1) });
        Assert.Equal(HttpStatusCode.Created, (await Write(client, geo.Cross)).StatusCode);
        clock.Now = clock.Now.AddHours(2);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(client, geo.Cross)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/villages/{geo.Cross}/khatauni")).StatusCode);
        Assert.Equal("PATWARI", (await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Designation!.Code);
    }

    [Fact]
    public async Task Catalog_is_separate_from_roles_and_allocation_never_grants_permission()
    {
        using var admin = await Admin(); var geo = await Geography();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var roleCount = await db.Roles.CountAsync(); var lrStream = (await db.Workstreams.SingleAsync(x => x.Code == WorkstreamCodes.LandRecords)).Id;
        var created = await admin.PostAsJsonAsync("/api/admin/works", new CreateWorkRequest($"CUSTOM_{Guid.NewGuid():N}", "Reusable LR duty", null, OperationalWorkKind.LandRecords, lrStream));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode); Assert.Equal(roleCount, await db.Roles.CountAsync());
        var validationRole = await Role(admin, PermissionCodes.LrView);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/roles/{validationRole}",
            new UpdateRoleRequest("Invalid scope", null, [new(PermissionCodes.LrView, (ScopeMode)999)]))).StatusCode);
        var monitor = await Officer(admin, await Role(admin, PermissionCodes.LrView)); using var client = monitor.Client;
        await Allocate(admin, monitor.Id, Input(geo.Work, geo.Home));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/villages/{geo.Home}/khatauni")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(client, geo.Home)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/admin/users/{monitor.Id}/allocations", Input(geo.Work, geo.Cross))).StatusCode);
    }

    [Fact]
    public async Task Allocation_validation_revision_future_and_revocation_are_enforced()
    {
        using var admin = await Admin(); var geo = await Geography();
        var officer = await Officer(admin, await Role(admin, PermissionCodes.LrEdit)); using var client = officer.Client;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/users/{officer.Id}/allocations",
            Input(geo.Work, geo.Home) with { Scopes = [new(AllocationScopeKind.Village, DistrictId: Guid.NewGuid(), VillageId: geo.Home)] })).StatusCode);
        var id = await Allocate(admin, officer.Id, Input(geo.Work, geo.Home) with { ValidFrom = clock.Now.AddHours(1) });
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(client, geo.Home)).StatusCode);
        clock.Now = clock.Now.AddHours(2);
        Assert.Equal(HttpStatusCode.Created, (await Write(client, geo.Home)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/users/{officer.Id}/allocations/{id}/revoke", new RevisionRequest(4))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/users/{officer.Id}/allocations/{id}/revoke", new RevisionRequest(0))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(client, geo.Home)).StatusCode);
    }

    [Fact]
    public async Task Account_create_edit_allocations_are_atomic_and_independent_of_designation()
    {
        using var admin = await Admin(); var geo = await Geography(); var role = await Role(admin, PermissionCodes.LrView, PermissionCodes.LrEdit);
        var name = $"atomic_{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(name, "Atomic", TestCredentials.NewPassword(), null,
            [role], null, null, [Input(Guid.NewGuid(), geo.Home)]))).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        Assert.False(await db.AppUsers.AnyAsync(x => x.Username == name));
        var officer = await Officer(admin, role); using var client = officer.Client;
        var designation = (await db.AppUsers.SingleAsync(x => x.Id == officer.Id)).DesignationId;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/users/{officer.Id}",
            new UpdateUserRequest("Edited officer", designation, null, null, null, [Input(geo.Work, geo.Home)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Write(client, geo.Home)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/users/{officer.Id}",
            new UpdateUserRequest("Edited officer", designation, null, null, null, [Input(geo.Work, geo.Cross)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(client, geo.Home)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Write(client, geo.Cross)).StatusCode);
        Assert.Equal(designation, (await db.AppUsers.AsNoTracking().SingleAsync(x => x.Id == officer.Id)).DesignationId);
    }

    private async Task<(Guid Id, string Name, HttpClient Client)> Assistant(HttpClient officer, Guid role, Guid parent, WorkAllocationInput allocation, params string[] permissions)
    {
        var name = $"deo_{Guid.NewGuid():N}";
        var response = await officer.PostAsJsonAsync("/api/officers/me/assistants", new CreateAssistantRequest(name,
            new AssistantInput("Proof DEO", null, [role], permissions, [allocation with { DelegatedFromAllocationId = parent }], [])));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid(); var temporary = json.RootElement.GetProperty("temporaryCredential").GetString()!;
        var client = await Login(name, temporary);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(client, allocation.Scopes[0].VillageId!.Value)).StatusCode);
        var permanent = TestCredentials.NewPassword();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(temporary, permanent))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, permanent))).StatusCode);
        return (id, name, client);
    }

    [Fact]
    public async Task Assistants_have_individual_logins_bounded_permissions_scopes_and_dual_actor_audit()
    {
        using var admin = await Admin(); var geo = await Geography();
        var childRole = await Role(admin, PermissionCodes.LrView, PermissionCodes.LrEdit);
        var officer = await Officer(admin, childRole, await Role(admin, PermissionCodes.AssistantsManage)); using var owner = officer.Client;
        var parent = await Allocate(admin, officer.Id, Input(geo.Work, geo.Home, geo.Second));
        var reader = await Assistant(owner, childRole, parent, Input(geo.Work, geo.Home), PermissionCodes.LrView); using var readerClient = reader.Client;
        var writer = await Assistant(owner, childRole, parent, Input(geo.Work, geo.Second), PermissionCodes.LrView, PermissionCodes.LrEdit); using var writerClient = writer.Client;
        Assert.NotEqual(reader.Id, writer.Id);
        Assert.Equal(HttpStatusCode.OK, (await readerClient.GetAsync($"/api/villages/{geo.Home}/khatauni")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readerClient.GetAsync($"/api/villages/{geo.Second}/khatauni")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(readerClient, geo.Home)).StatusCode);
        var written = await Write(writerClient, geo.Second); Assert.Equal(HttpStatusCode.Created, written.StatusCode);
        var recordId = (await written.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var audit = await db.AuditLogs.SingleAsync(x => x.EntityId == recordId && x.Action == "Created");
        Assert.Equal(writer.Id, audit.ActorUserId); Assert.Equal(officer.Id, audit.OnBehalfOfUserId);
        Assert.Equal("DEO Proof DEO on behalf of Officer Proof Officer", audit.ActorLabel);
        Assert.Equal(writer.Id.ToString(), (await db.KhatauniRecords.SingleAsync(x => x.Id == recordId)).CreatedBy);
        Assert.Equal(HttpStatusCode.Forbidden, (await writerClient.PostAsJsonAsync("/api/officers/me/assistants", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/officers/me/assistants", new CreateAssistantRequest($"invalid_{Guid.NewGuid():N}",
            new AssistantInput("Invalid DEO", null, [childRole], [PermissionCodes.LrEdit], [Input(geo.Work, geo.Cross) with { DelegatedFromAllocationId = parent }], [])))).StatusCode);
    }

    [Fact]
    public async Task Parent_permission_and_allocation_changes_affect_assistants_without_relogin()
    {
        using var admin = await Admin(); var geo = await Geography(); var childRole = await Role(admin, PermissionCodes.LrView, PermissionCodes.LrEdit);
        var assistantManage = await Role(admin, PermissionCodes.AssistantsManage);
        var officer = await Officer(admin, childRole, assistantManage); using var owner = officer.Client;
        var parent = await Allocate(admin, officer.Id, Input(geo.Work, geo.Home, geo.Second));
        var assistant = await Assistant(owner, childRole, parent, Input(geo.Work, geo.Home), PermissionCodes.LrView, PermissionCodes.LrEdit); using var client = assistant.Client;
        Assert.Equal(HttpStatusCode.Created, (await Write(client, geo.Home)).StatusCode);
        var readerRole = await Role(admin, PermissionCodes.LrView);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/users/{officer.Id}",
            new UpdateUserRequest("Proof Officer", null, [readerRole, assistantManage], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(client, geo.Home)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/villages/{geo.Home}/khatauni")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/users/{officer.Id}/allocations/{parent}",
            new UpdateAllocationRequest(Input(geo.Work, geo.Second), 0))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/villages/{geo.Home}/khatauni")).StatusCode);
    }

    [Fact]
    public async Task Account_maintenance_cannot_take_over_another_officers_allocation_by_password_reset()
    {
        using var admin = await Admin(); var geo = await Geography();
        var operations = await Role(admin, PermissionCodes.LrView, PermissionCodes.LrEdit);
        var maintainer = await Officer(admin, operations, await Role(admin, PermissionCodes.UsersManage)); using var client = maintainer.Client;
        var target = await Officer(admin, operations); using var targetClient = target.Client;
        await Allocate(admin, maintainer.Id, Input(geo.Work, geo.Home));
        await Allocate(admin, target.Id, Input(geo.Work, geo.Cross));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/admin/users/{target.Id}/reset-password", new ResetPasswordRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Write(targetClient, geo.Cross)).StatusCode);
    }

    [Fact]
    public async Task Verify_commit_and_record_reclassification_check_server_targets_and_both_workstreams()
    {
        using var admin = await Admin(); var geo = await Geography();
        var role = await Role(admin, PermissionCodes.LrView, PermissionCodes.LrEdit, PermissionCodes.LrVerify, PermissionCodes.LrCommit,
            PermissionCodes.MatterView, PermissionCodes.MatterCreate, PermissionCodes.MatterEdit);
        var officer = await Officer(admin, role); using var client = officer.Client;
        await Allocate(admin, officer.Id, Input(geo.Work, geo.Home));
        var crossRecord = await Write(admin, geo.Cross);
        Assert.Equal(HttpStatusCode.Created, crossRecord.StatusCode);
        var recordId = (await crossRecord.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/khatauni/{recordId}/verify", new { expectedVersion = 0, villageId = geo.Home })).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var crossLr = new VillageLR { VillageId = geo.Cross };
        var entry = new LREntry { VillageLR = crossLr, RawKhasraText = "Proof source", VerificationStatus = VerificationStatus.Verified };
        db.Add(entry); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/lr-entries/{entry.Id}/commit", new { expectedRevision = 0 })).StatusCode);
        var sourceStream = (await db.WorkDefinitions.SingleAsync(x => x.Id == geo.Work)).WorkstreamId;
        var court = await db.WorkDefinitions.SingleAsync(x => x.Code == "COURT");
        var matterRes = await client.PostAsJsonAsync("/api/matters", new { villageId = geo.Home, title = "Reclassification proof", workstreamId = sourceStream });
        Assert.Equal(HttpStatusCode.Created, matterRes.StatusCode);
        var matterId = (await matterRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var change = new ReclassifyMatterApiRequest(court.WorkstreamId, "Temporary responsibility change", 0);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/matters/{matterId}/reclassify", change)).StatusCode);
        await Allocate(admin, officer.Id, Input(court.Id, geo.Home));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/matters/{matterId}/reclassify", change)).StatusCode);
    }

    [Fact]
    public async Task Parent_revocation_and_temporary_credential_expiry_take_effect_on_existing_sessions()
    {
        using var admin = await Admin(); var geo = await Geography();
        var role = await Role(admin, PermissionCodes.LrView, PermissionCodes.LrEdit);
        var officer = await Officer(admin, role, await Role(admin, PermissionCodes.AssistantsManage)); using var owner = officer.Client;
        var parent = await Allocate(admin, officer.Id, Input(geo.Work, geo.Home));
        var assistant = await Assistant(owner, role, parent, Input(geo.Work, geo.Home), PermissionCodes.LrView, PermissionCodes.LrEdit); using var client = assistant.Client;
        Assert.Equal(HttpStatusCode.Created, (await Write(client, geo.Home)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/users/{officer.Id}/allocations/{parent}/revoke", new RevisionRequest(0))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(client, geo.Home)).StatusCode);
        var reset = await owner.PostAsJsonAsync($"/api/officers/me/assistants/{assistant.Id}/reset-credential", new RevisionRequest(0));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var json = JsonDocument.Parse(await reset.Content.ReadAsStringAsync()); var secret = json.RootElement.GetProperty("temporaryCredential").GetString()!;
        using var temporaryClient = await Login(assistant.Name, secret);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var user = await db.AppUsers.SingleAsync(x => x.Id == assistant.Id); user.TemporaryCredentialExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await temporaryClient.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(secret, TestCredentials.NewPassword()))).StatusCode);
        using var fresh = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.PostAsJsonAsync("/api/auth/login", new LoginRequest(assistant.Name, secret))).StatusCode);
    }

    [Fact]
    public async Task Assistant_edit_reset_revoke_and_ownership_are_enforced()
    {
        using var admin = await Admin(); var geo = await Geography(); var childRole = await Role(admin, PermissionCodes.LrView, PermissionCodes.LrEdit);
        var managingRole = await Role(admin, PermissionCodes.AssistantsManage);
        var officer = await Officer(admin, childRole, managingRole); using var owner = officer.Client;
        var other = await Officer(admin, childRole, managingRole); using var otherOwner = other.Client;
        var parent = await Allocate(admin, officer.Id, Input(geo.Work, geo.Home));
        var assistant = await Assistant(owner, childRole, parent, Input(geo.Work, geo.Home), PermissionCodes.LrView, PermissionCodes.LrEdit); using var client = assistant.Client;
        Assert.Equal(HttpStatusCode.NotFound, (await otherOwner.PostAsJsonAsync($"/api/officers/me/assistants/{assistant.Id}/reset-credential", new RevisionRequest(0))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/officers/me/assistants/{assistant.Id}", new UpdateAssistantRequest(
            new AssistantInput("Edited DEO", null, [childRole], [PermissionCodes.LrView], [Input(geo.Work, geo.Home) with { DelegatedFromAllocationId = parent }], []), 0))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(client, geo.Home)).StatusCode);
        var reset = await owner.PostAsJsonAsync($"/api/officers/me/assistants/{assistant.Id}/reset-credential", new RevisionRequest(1));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var resetJson = JsonDocument.Parse(await reset.Content.ReadAsStringAsync());
        var secret = resetJson.RootElement.GetProperty("temporaryCredential").GetString()!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        using var fresh = await Login(assistant.Name, secret);
        Assert.Equal(HttpStatusCode.Forbidden, (await fresh.GetAsync($"/api/villages/{geo.Home}/khatauni")).StatusCode);
        var detail = await owner.GetStringAsync($"/api/officers/me/assistants/{assistant.Id}");
        Assert.DoesNotContain(secret, detail); Assert.DoesNotContain("passwordHash", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/officers/me/assistants/{assistant.Id}/revoke", new RevisionRequest(2))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.GetAsync("/api/auth/me")).StatusCode);
    }
}
