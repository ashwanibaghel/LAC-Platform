using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LAC.Tests;

public sealed class OfficeConsolidatedTests
{
    internal Func<WebApplicationFactory<Program>> NewFactory { get; init; } = () => new RbacFactory();
    private sealed record Account(Guid Id, string Name, string Password, HttpClient Client);
    private sealed class Harness(WebApplicationFactory<Program> factory) : IDisposable
    {
        public WebApplicationFactory<Program> Factory => factory;
        public async Task<HttpClient> Login(string name, string password)
        {
            var client = factory.CreateClient(new() { HandleCookies = true });
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, password))).StatusCode);
            return client;
        }
        public Task<HttpClient> Admin() => Login(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass);
        public async Task<Account> Created(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var account = json.GetProperty("account"); var name = account.GetProperty("username").GetString()!;
            var credential = json.GetProperty("temporaryCredential").GetString()!;
            var password = TestCredentials.NewPassword();
            using var temporary = await Login(name, credential);
            Assert.Equal(HttpStatusCode.OK, (await temporary.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(credential, password))).StatusCode);
            return new(account.GetProperty("id").GetGuid(), name, password, await Login(name, password));
        }
        public async Task<Account> Create(HttpClient actor, OfficeAuthority level, LandAccessLevel land = LandAccessLevel.None, params OfficeModule[] modules) =>
            await Created(await actor.PostAsJsonAsync("/api/office/accounts", new CreateOfficeAccountRequest("audit." + Guid.NewGuid().ToString("N"), Input(level, land, modules))));
        public async Task<Account> Helper(Account parent, HelperAccessLevel access) => await Created(await parent.Client.PostAsJsonAsync("/api/office/me/helpers",
            new CreateOfficeHelperRequest("helper." + Guid.NewGuid().ToString("N"), new("Synthetic helper", null, "Assistant", null, access))));
        public async Task<int> Revision(Guid id, bool helper = false)
        {
            using var scope = factory.Services.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LacDbContext>().AppUsers.AsNoTracking().SingleAsync(u => u.Id == id);
            return helper ? user.AssistantRevision : user.OfficeRevision;
        }
        public void Dispose() => factory.Dispose();
    }
    private static OfficeAccountInput Input(OfficeAuthority level, LandAccessLevel land = LandAccessLevel.None, params OfficeModule[] modules) =>
        new("Synthetic officer", null, "Audit officer", level, modules, false, land, []);
    private static async Task<HashSet<Guid>> Directory(HttpClient client, string path = "/api/office/accounts")
    {
        var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
    }

    [Fact]
    public async Task Directory_and_guessed_profiles_are_scoped_for_every_authority()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/office/accounts/recovery/lookup", new RecoveryLookup(RbacFactory.TestAdminUser))).StatusCode);
        var technical = await h.Create(admin, OfficeAuthority.SYSTEM_ADMIN);
        var adm = await h.Create(admin, OfficeAuthority.OFFICE_ADMIN); var peerAdm = await h.Create(admin, OfficeAuthority.OFFICE_ADMIN);
        var supervisor = await h.Create(adm.Client, OfficeAuthority.OFFICE_SUPERVISOR);
        var peerSupervisor = await h.Create(adm.Client, OfficeAuthority.OFFICE_SUPERVISOR);
        var staff = await h.Create(supervisor.Client, OfficeAuthority.STANDARD_OFFICER);
        var helper = await h.Helper(staff, HelperAccessLevel.ReadOnly); var protectedHelper = await h.Helper(peerAdm, HelperAccessLevel.ReadOnly);
        var ownHelper = await h.Helper(adm, HelperAccessLevel.ReadOnly);
        Assert.True((await Directory(admin)).SetEquals([SeedData.BootstrapAdminId, technical.Id]));
        Assert.True((await Directory(adm.Client)).SetEquals([adm.Id, supervisor.Id, peerSupervisor.Id, staff.Id, helper.Id, ownHelper.Id]));
        Assert.True((await Directory(supervisor.Client)).SetEquals([supervisor.Id, staff.Id, helper.Id]));
        foreach (var caller in new[] { staff.Client, helper.Client })
            Assert.Equal(HttpStatusCode.Forbidden, (await caller.GetAsync("/api/office/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await helper.Client.GetAsync("/api/office/me/helpers")).StatusCode);
        var own = await staff.Client.GetFromJsonAsync<JsonElement>("/api/office/me/helpers");
        Assert.Equal(helper.Id, Assert.Single(own.EnumerateArray()).GetProperty("id").GetGuid());
        foreach (var (caller, hidden) in new[] {
            (admin, adm.Id), (admin, helper.Id), (adm.Client, technical.Id), (adm.Client, peerAdm.Id),
            (adm.Client, protectedHelper.Id), (supervisor.Client, adm.Id), (supervisor.Client, peerSupervisor.Id) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await caller.GetAsync("/api/office/accounts/" + hidden)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await caller.GetAsync("/api/admin/users/" + hidden)).StatusCode);
            Assert.DoesNotContain(hidden, await Directory(caller, "/api/admin/users"));
            if (caller != admin)
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await caller.PutAsJsonAsync("/api/office/accounts/" + hidden,
                    new UpdateOfficeAccountRequest(Input(OfficeAuthority.STANDARD_OFFICER), 0))).StatusCode);
                foreach (var action in new[] { "reset-credential", "toggle-status" })
                    Assert.Equal(HttpStatusCode.Forbidden, (await caller.PostAsJsonAsync("/api/office/accounts/" + hidden + "/" + action, new RevisionRequest(0))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await caller.PostAsJsonAsync("/api/office/accounts/" + hidden + "/helpers",
                    new CreateOfficeHelperRequest("blocked." + Guid.NewGuid().ToString("N"), new("Blocked helper", null, "Assistant", null, HelperAccessLevel.None)))).StatusCode);
            }
        }
        var self = await supervisor.Client.GetFromJsonAsync<JsonElement>("/api/office/accounts/" + supervisor.Id);
        Assert.False(self.GetProperty("capabilities").GetProperty("canEdit").GetBoolean());
        var managed = await supervisor.Client.GetFromJsonAsync<JsonElement>("/api/office/accounts/" + staff.Id);
        Assert.True(managed.GetProperty("capabilities").GetProperty("canEdit").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.Client.PutAsJsonAsync("/api/office/accounts/" + supervisor.Id,
            new UpdateOfficeAccountRequest(Input(OfficeAuthority.OFFICE_SUPERVISOR), await h.Revision(supervisor.Id)))).StatusCode);
        foreach (var account in new[] {technical,adm,peerAdm,supervisor,peerSupervisor,staff,helper,protectedHelper,ownHelper}) account.Client.Dispose();
    }

    [Fact]
    public async Task Technical_directory_retains_one_time_ADM_provisioning_and_exact_credential_recovery()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin();
        using var scope = h.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var admDesignation = await db.Designations.SingleAsync(d => d.Code == "ADM");
        var adm = await h.Created(await admin.PostAsJsonAsync("/api/office/accounts",
            new CreateOfficeAccountRequest("adm." + Guid.NewGuid().ToString("N"), Input(OfficeAuthority.STANDARD_OFFICER) with { DesignationId = admDesignation.Id, CustomDesignation = null })));
        Assert.Equal("OFFICE_ADMIN", (await adm.Client.GetFromJsonAsync<JsonElement>("/api/office/me")).GetProperty("authority").GetString());
        Assert.DoesNotContain(adm.Id, await Directory(admin));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/office/accounts/" + adm.Id)).StatusCode);
        var lookup = await admin.PostAsJsonAsync("/api/office/accounts/recovery/lookup", new RecoveryLookup(adm.Name));
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode); var receipt = await lookup.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(receipt.TryGetProperty("modules", out _)); Assert.False(receipt.TryGetProperty("supervisingOfficerId", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/office/accounts/recovery/lookup", new RecoveryLookup("adm.*"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adm.Client.PostAsJsonAsync("/api/office/accounts/recovery/lookup", new RecoveryLookup(adm.Name))).StatusCode);
        var reset = await admin.PostAsJsonAsync("/api/office/accounts/recovery/" + adm.Id + "/reset-credential", new RevisionRequest(await h.Revision(adm.Id)));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode); var result = await reset.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(result.TryGetProperty("account", out _)); Assert.False(result.TryGetProperty("modules", out _));
        Assert.Equal(HttpStatusCode.Unauthorized, (await adm.Client.GetAsync("/api/auth/me")).StatusCode);
        using var fresh = await h.Login(adm.Name, result.GetProperty("temporaryCredential").GetString()!);
        Assert.Equal("OFFICE_ADMIN", (await fresh.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("authority").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/office/accounts/recovery/" + adm.Id + "/enable", new RevisionRequest(await h.Revision(adm.Id)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/office/accounts/" + adm.Id + "/toggle-status", new RevisionRequest(await h.Revision(adm.Id)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/office/accounts/recovery/" + adm.Id + "/enable", new RevisionRequest(await h.Revision(adm.Id)))).StatusCode);
        Assert.DoesNotContain(adm.Id, await Directory(admin));
        adm.Client.Dispose();
    }

    [Fact]
    public async Task Canonical_ADM_is_reserved_but_custom_ADM_never_escalates()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin();
        var head = await h.Create(admin, OfficeAuthority.OFFICE_ADMIN); var supervisor = await h.Create(head.Client, OfficeAuthority.OFFICE_SUPERVISOR);
        using var scope = h.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var designation = await db.Designations.SingleAsync(d => d.Code == "ADM");
        var options = await supervisor.Client.GetFromJsonAsync<JsonElement>("/api/office/accounts/options");
        Assert.DoesNotContain(options.GetProperty("designations").EnumerateArray(), d => d.GetProperty("code").GetString() == "ADM");
        foreach (var actor in new[] { head.Client, supervisor.Client })
            Assert.Equal(HttpStatusCode.Forbidden, (await actor.PostAsJsonAsync("/api/office/accounts",
                new CreateOfficeAccountRequest("attack." + Guid.NewGuid().ToString("N"), Input(OfficeAuthority.STANDARD_OFFICER) with { DesignationId = designation.Id, CustomDesignation = null }))).StatusCode);
        var custom = await h.Created(await supervisor.Client.PostAsJsonAsync("/api/office/accounts",
            new CreateOfficeAccountRequest("custom." + Guid.NewGuid().ToString("N"), Input(OfficeAuthority.STANDARD_OFFICER) with { CustomDesignation = "ADM Support" })));
        Assert.Equal("STANDARD_OFFICER", (await custom.Client.GetFromJsonAsync<JsonElement>("/api/office/me")).GetProperty("authority").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.Client.PutAsJsonAsync("/api/admin/users/" + custom.Id,
            new UpdateUserRequest("Attack", null, [SeedData.SystemAdminRoleId], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await supervisor.Client.PostAsJsonAsync("/api/office/accounts", new {
            username = "crafted." + Guid.NewGuid().ToString("N"), account = Input(OfficeAuthority.STANDARD_OFFICER), roleIds = new[] {SeedData.SystemAdminRoleId} })).StatusCode);
        head.Client.Dispose(); supervisor.Client.Dispose(); custom.Client.Dispose();
    }

    [Fact]
    public async Task Helpers_obey_live_parent_permission_workstream_allocation_and_land_ceilings()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin();
        var head = await h.Create(admin, OfficeAuthority.OFFICE_ADMIN);
        var parent = await h.Create(head.Client, OfficeAuthority.STANDARD_OFFICER, LandAccessLevel.ViewOnly, OfficeModule.Court);
        var child = await h.Helper(parent, HelperAccessLevel.ReadWrite);
        using var scope = h.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var village = await db.Villages.FirstAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await child.Client.PostAsJsonAsync("/api/khatauni",new { villageId=village.Id, verificationStatus="Draft" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await child.Client.GetAsync("/api/court-cases")).StatusCode);
        var context = await child.Client.GetFromJsonAsync<JsonElement>("/api/matters/context?villageId=" + village.Id);
        Assert.Empty(context.GetProperty("workstreams").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await head.Client.PutAsJsonAsync("/api/office/accounts/" + parent.Id,
            new UpdateOfficeAccountRequest(Input(OfficeAuthority.STANDARD_OFFICER), await h.Revision(parent.Id)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await child.Client.GetAsync("/api/court-cases")).StatusCode);
        using var fresh = await h.Login(child.Name, child.Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await fresh.GetAsync("/api/court-cases")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fresh.GetAsync("/api/villages")).StatusCode);
        head.Client.Dispose(); parent.Client.Dispose(); child.Client.Dispose();
    }

    [Fact]
    public async Task Court_import_malformed_requests_never_crash_and_readonly_authorization_runs_first()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin();
        var parent = await h.Create(admin, OfficeAuthority.STANDARD_OFFICER, LandAccessLevel.None, OfficeModule.Court);
        var child = await h.Helper(parent, HelperAccessLevel.ReadOnly);
        Assert.Equal(HttpStatusCode.Forbidden, (await child.Client.PostAsync("/api/court-cases/imports",null)).StatusCode);
        Assert.False((await child.Client.GetFromJsonAsync<JsonElement>("/api/office/me/capabilities")).GetProperty("canImportCourt").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await parent.Client.PutAsJsonAsync("/api/office/me/helpers/" + child.Id,
            new UpdateOfficeHelperRequest(new("Synthetic helper", null, "Assistant", null, HelperAccessLevel.ReadWrite), await h.Revision(child.Id,true)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await child.Client.GetAsync("/api/auth/me")).StatusCode);
        using var fresh = await h.Login(child.Name,child.Password);
        foreach (var body in new HttpContent?[] {null,new StringContent("{}"),new MultipartFormDataContent(),new StringContent("invalid",System.Text.Encoding.UTF8,"multipart/form-data")})
        {
            var response=await fresh.PostAsync("/api/court-cases/imports",body);
            Assert.Contains(response.StatusCode,new[]{HttpStatusCode.BadRequest,HttpStatusCode.UnsupportedMediaType});
        }
        Assert.True((await fresh.GetFromJsonAsync<JsonElement>("/api/office/me/capabilities")).GetProperty("canImportCourt").GetBoolean());
        Assert.Empty(await dbRows(h.Factory));
        parent.Client.Dispose();child.Client.Dispose();
        static async Task<List<CourtImportBatch>> dbRows(WebApplicationFactory<Program> factory) {
            using var scope=factory.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<LacDbContext>().CourtImportBatches.ToListAsync();
        }
    }

    [Fact]
    public async Task Legacy_System_Admin_details_show_effective_operations_and_protected_self_actions()
    {
        using var h=new Harness(NewFactory());using var admin=await h.Admin();
        var detail=await admin.GetFromJsonAsync<JsonElement>("/api/office/accounts/"+SeedData.BootstrapAdminId);
        Assert.Equal("ViewWrite",detail.GetProperty("landAccess").GetString());Assert.True(detail.GetProperty("canRegisterInwardDak").GetBoolean());
        Assert.Equal(5,detail.GetProperty("modules").GetArrayLength());Assert.False(detail.GetProperty("capabilities").GetProperty("canEdit").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.PostAsJsonAsync("/api/office/accounts/"+SeedData.BootstrapAdminId+"/reset-credential",
            new RevisionRequest(await h.Revision(SeedData.BootstrapAdminId)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync("/api/office/accounts/"+SeedData.BootstrapAdminId+"/toggle-status",
            new RevisionRequest(await h.Revision(SeedData.BootstrapAdminId)))).StatusCode);
    }

    [Fact]
    public async Task Parent_workstream_and_allocation_revocation_invalidate_child_without_child_edit()
    {
        using var h=new Harness(NewFactory());using var admin=await h.Admin();
        var parent=await h.Create(admin,OfficeAuthority.STANDARD_OFFICER,LandAccessLevel.None,OfficeModule.Court);
        var child=await h.Helper(parent,HelperAccessLevel.ReadWrite);
        Assert.Equal(HttpStatusCode.OK,(await child.Client.GetAsync("/api/court-cases")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync("/api/admin/users/"+parent.Id,
            new UpdateUserRequest("Synthetic officer",null,null,[],null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await child.Client.GetAsync("/api/court-cases")).StatusCode);
        using var fresh=await h.Login(child.Name,child.Password);
        Assert.Equal(HttpStatusCode.Forbidden,(await fresh.GetAsync("/api/court-cases")).StatusCode);
        Assert.False((await fresh.GetFromJsonAsync<JsonElement>("/api/office/me/capabilities")).GetProperty("canImportCourt").GetBoolean());
        // Restore only the membership; the original parent allocation IDs remain intact.
        using var scope=h.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var courtStream=await db.Workstreams.SingleAsync(w=>w.Code==WorkstreamCodes.CourtReferences);
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync("/api/admin/users/"+parent.Id,
            new UpdateUserRequest("Synthetic officer",null,null,[courtStream.Id],null))).StatusCode);
        using var restored=await h.Login(child.Name,child.Password);
        Assert.Equal(HttpStatusCode.OK,(await restored.GetAsync("/api/court-cases")).StatusCode);
        var allocations=await db.WorkAllocations.AsNoTracking().Where(a=>a.UserId==parent.Id && a.RevokedAt==null).Select(a=>new{a.Id,a.Revision}).ToListAsync();
        foreach(var allocation in allocations)
            Assert.Equal(HttpStatusCode.OK,(await admin.PostAsJsonAsync("/api/admin/users/"+parent.Id+"/allocations/"+allocation.Id+"/revoke",new RevisionRequest(allocation.Revision))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await restored.GetAsync("/api/court-cases")).StatusCode);
        using var unallocated=await h.Login(child.Name,child.Password);
        using var validMultipart=new MultipartFormDataContent { { new StringContent("allocation-denial-proof"), "note" } };
        Assert.Equal(HttpStatusCode.Forbidden,(await unallocated.PostAsync("/api/court-cases/imports",validMultipart)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await unallocated.PostAsJsonAsync("/api/court-cases",new {caseNumber="REVOKED-"+Guid.NewGuid().ToString("N"),courtName="District Court"})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await unallocated.GetAsync("/api/court-cases")).StatusCode);
        parent.Client.Dispose();child.Client.Dispose();
    }
}
