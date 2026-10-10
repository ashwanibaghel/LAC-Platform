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

// Each test starts with a fresh database; all identities are synthetic and all requests use real cookies.
public sealed class OfficeAccountV3Tests
{
    internal Func<WebApplicationFactory<Program>> NewFactory { get; init; } = () => new RbacFactory();
    private sealed class Harness(WebApplicationFactory<Program> factory) : IDisposable
    {
        public readonly WebApplicationFactory<Program> Factory = factory;
        public HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        public async Task<HttpClient> Login(string name, string password)
        {
            var client = Client();
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, password))).StatusCode);
            return client;
        }
        public Task<HttpClient> Admin() => Login(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass);
        public async Task<Guid> Designation(string code)
        {
            using var scope = Factory.Services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<LacDbContext>().Designations.SingleAsync(d => d.Code == code)).Id;
        }
        public async Task<(Guid Id, string Name, string Password, HttpClient Client)> Create(HttpClient admin, OfficeAccountInput input)
        {
            var name = $"office_{Guid.NewGuid():N}";
            var response = await admin.PostAsJsonAsync("/api/office/accounts", new CreateOfficeAccountRequest(name, input));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var id = json.GetProperty("account").GetProperty("id").GetGuid();
            var credential = json.GetProperty("temporaryCredential").GetString()!;
            Assert.InRange(json.GetProperty("credentialExpiresAt").GetDateTimeOffset(), DateTimeOffset.UtcNow.AddHours(23.9), DateTimeOffset.UtcNow.AddHours(24.1));
            using var first = await Login(name, credential);
            Assert.Equal(HttpStatusCode.Forbidden, (await first.GetAsync("/api/villages?page=0&pageSize=10")).StatusCode);
            var password = TestCredentials.NewPassword();
            Assert.Equal(HttpStatusCode.OK, (await first.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(credential, password))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/me")).StatusCode);
            return (id, name, password, await Login(name, password));
        }
        public async Task<int> Revision(HttpClient admin, Guid id)
        {
            // Test-only revision lookup: System Admin no longer browses office accounts.
            using var scope = Factory.Services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<LacDbContext>().AppUsers.AsNoTracking().SingleAsync(u => u.Id == id)).OfficeRevision;
        }
        public void Dispose() => Factory.Dispose();
    }
    private static OfficeAccountInput Input(OfficeAuthority authority = OfficeAuthority.STANDARD_OFFICER, LandAccessLevel land = LandAccessLevel.None,
        Guid? designation = null, params OfficeModule[] modules) => new("Synthetic Officer", designation, null, authority, modules, false, land, []);

    [Fact]
    public async Task System_admin_creates_ADM_with_explicit_office_authority_and_designation_edits_never_promote()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin();
        var adm = await h.Designation("ADM");
        var head = await h.Create(admin, Input(designation: adm)); using var headClient = head.Client;
        var headMe = await headClient.GetFromJsonAsync<JsonElement>("/api/office/me");
        Assert.Equal("OFFICE_ADMIN", headMe.GetProperty("authority").GetString());
        var ordinary = await h.Create(admin, Input()); using var ordinaryClient = ordinary.Client;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/office/accounts/{ordinary.Id}",
            new UpdateOfficeAccountRequest(Input(designation: adm), await h.Revision(admin, ordinary.Id)))).StatusCode);
        using var fresh = await h.Login(ordinary.Name, ordinary.Password);
        Assert.Equal("STANDARD_OFFICER", (await fresh.GetFromJsonAsync<JsonElement>("/api/office/me")).GetProperty("authority").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await ordinaryClient.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/roles")).StatusCode);
    }

    [Theory]
    [InlineData(OfficeAuthority.OFFICE_ADMIN)]
    [InlineData(OfficeAuthority.OFFICE_SUPERVISOR)]
    public async Task Office_authorities_have_full_operations_but_cannot_touch_system_or_reserved_roles(OfficeAuthority authority)
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin();
        var manager = await h.Create(admin, Input(authority)); using var client = manager.Client;
        var me = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        foreach (var permission in OfficeAccessPresets.Operations) Assert.Contains(me.Permissions, p => p.Code == permission && p.Scope == "All");
        Assert.DoesNotContain(me.Permissions, p => p.Code == PermissionCodes.AccessManage);
        using var scope = h.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var village = await db.Villages.FirstAsync();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/khatauni", new { villageId = village.Id, verificationStatus = "Draft" })).StatusCode);
        var ordinary = await h.Create(client, Input(land: LandAccessLevel.ViewOnly)); using var ordinaryClient = ordinary.Client;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/office/accounts/{ordinary.Id}", new UpdateOfficeAccountRequest(Input(modules: [OfficeModule.Rti, OfficeModule.Accounts]), await h.Revision(client, ordinary.Id)))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/office/accounts/{ordinary.Id}/helpers",
            new CreateOfficeHelperRequest($"managed_helper_{Guid.NewGuid():N}", new OfficeHelperInput("Managed helper", null, "Office support", null, HelperAccessLevel.None)))).StatusCode);
        var blocked = new List<OfficeAuthority> { OfficeAuthority.SYSTEM_ADMIN, OfficeAuthority.OFFICE_ADMIN };
        if (authority == OfficeAuthority.OFFICE_SUPERVISOR) blocked.Add(OfficeAuthority.OFFICE_SUPERVISOR);
        foreach (var level in blocked)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/office/accounts", new CreateOfficeAccountRequest($"attack_{Guid.NewGuid():N}", Input(level)))).StatusCode);
            var role = await db.Roles.SingleAsync(r => r.Code == level.ToString());
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{ordinary.Id}", new UpdateUserRequest("Attack", null, [role.Id], null, null))).StatusCode);
        }
        var bootstrap = SeedData.BootstrapAdminId;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{bootstrap}", new UpdateUserRequest("Attack", null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/admin/users/{bootstrap}/reset-password", new ResetPasswordRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/admin/users/{bootstrap}/toggle-status", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/admin/users/{bootstrap}/desks", new AssignDeskRequest(Guid.NewGuid(), false))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/roles/{SeedData.SystemAdminRoleId}", new UpdateRoleRequest("Attack", null, []))).StatusCode);
        var options = await client.GetFromJsonAsync<JsonElement>("/api/office/accounts/options");
        Assert.Equal(authority == OfficeAuthority.OFFICE_ADMIN, options.GetProperty("canAssignOfficeSupervisor").GetBoolean());
        if (authority == OfficeAuthority.OFFICE_ADMIN)
        {
            var supervisor = await h.Create(client, Input(OfficeAuthority.OFFICE_SUPERVISOR)); supervisor.Client.Dispose();
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"V3_{Guid.NewGuid():N}", "Office desk", null, null))).StatusCode);
        }
        Assert.Null((await db.AppUsers.SingleAsync(u => u.Id == bootstrap)).DesignationId);
    }

    [Theory]
    [InlineData(LandAccessLevel.None, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(LandAccessLevel.ViewOnly, HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [InlineData(LandAccessLevel.ViewWrite, HttpStatusCode.OK, HttpStatusCode.Created)]
    public async Task Land_presets_enforce_real_read_and_write_boundaries(LandAccessLevel land, HttpStatusCode read, HttpStatusCode write)
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin(); var officer = await h.Create(admin, Input(land: land)); using var client = officer.Client;
        using var scope = h.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var village = await db.Villages.FirstAsync();
        Assert.Equal(read, (await client.GetAsync("/api/villages?page=0&pageSize=10")).StatusCode);
        Assert.Equal(read, (await client.GetAsync($"/api/villages/{village.Id}/khatauni")).StatusCode);
        Assert.Equal(write, (await client.PostAsJsonAsync("/api/khatauni", new { villageId = village.Id, verificationStatus = "Draft" })).StatusCode);
        var me = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        var expected = land == LandAccessLevel.ViewWrite ? OfficeAccessPresets.LandWrite : land == LandAccessLevel.ViewOnly ? OfficeAccessPresets.LandView : [];
        Assert.Equal(expected.OrderBy(c => c), me.Permissions.Where(p => p.Code != PermissionCodes.AssistantsManage).Select(p => p.Code).OrderBy(c => c));
    }

    [Fact]
    public async Task Registry_is_independent_of_Dak_Matters_and_future_modules_persist_without_fake_powers()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin();
        var ordinary = await h.Create(admin, Input(modules: [OfficeModule.DakMatters, OfficeModule.Rti, OfficeModule.Accounts])); using var client = ordinary.Client;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dak/lookups/registration")).StatusCode);
        var ordinaryMe = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        Assert.DoesNotContain(ordinaryMe.Permissions, p => p.Code == PermissionCodes.DakRegister || p.Code == PermissionCodes.DakMark);
        var registry = await h.Create(admin, Input() with { CanRegisterInwardDak = true }); using var registryClient = registry.Client;
        Assert.Equal(HttpStatusCode.OK, (await registryClient.GetAsync("/api/dak/lookups/registration")).StatusCode);
        using var form = new MultipartFormDataContent { { new StringContent($"REGISTRY/{Guid.NewGuid():N}"), "diaryNumber" }, { new StringContent("2026-10-09"), "receivedDate" }, { new StringContent("Fixture"), "subject" }, { new StringContent("Synthetic sender"), "senderName" } };
        Assert.Equal(HttpStatusCode.Created, (await registryClient.PostAsync("/api/dak", form)).StatusCode);
        var detail = await client.GetFromJsonAsync<JsonElement>("/api/office/me");
        Assert.Equal(3, detail.GetProperty("modules").GetArrayLength());
    }

    [Fact]
    public async Task Custom_designation_is_normalized_displayed_and_never_grants_authority()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin();
        var custom = await h.Create(admin, Input() with { CustomDesignation = "  Office Support Officer  " }); using var client = custom.Client;
        var me = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        Assert.Equal("Office Support Officer", me.Designation!.Name); Assert.Equal("CUSTOM", me.Designation.Code);
        Assert.Equal("STANDARD_OFFICER", (await client.GetFromJsonAsync<JsonElement>("/api/office/me")).GetProperty("authority").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/admin/users/{custom.Id}")).StatusCode);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<List<UserListItem>>("/api/admin/users"))!, u => u.Id == custom.Id);
        Assert.Equal("Office Support Officer", (await client.GetFromJsonAsync<JsonElement>("/api/office/me")).GetProperty("effectiveDesignation").GetString());
        foreach (var text in new[] { "\nForged", new string('x', 201) })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/office/accounts", new CreateOfficeAccountRequest($"invalid_{Guid.NewGuid():N}", Input() with { CustomDesignation = text }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/office/accounts", new CreateOfficeAccountRequest($"invalid_{Guid.NewGuid():N}", Input(designation: await h.Designation("ADM")) with { CustomDesignation = "Other" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/office/accounts", new { username = $"crafted_{Guid.NewGuid():N}", account = Input(), roleIds = new[] { SeedData.SystemAdminRoleId } })).StatusCode);
        using var scope = h.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        Assert.False(await db.Designations.AnyAsync(d => d.Name == "Office Support Officer"));
    }

    [Fact]
    public async Task Ordinary_and_helper_self_service_cannot_modify_identity_or_create_normal_accounts()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin(); var officer = await h.Create(admin, Input(land: LandAccessLevel.ViewOnly)); using var client = officer.Client;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/office/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/office/accounts", new CreateOfficeAccountRequest("forbidden", Input()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{officer.Id}", new UpdateUserRequest("Attack", await h.Designation("ADM"), [SeedData.SystemAdminRoleId], null, null))).StatusCode);
        var create = await client.PostAsJsonAsync("/api/office/me/helpers", new CreateOfficeHelperRequest($"helper_{Guid.NewGuid():N}", new OfficeHelperInput("Helper", await h.Designation("DEO"), null, null, HelperAccessLevel.ReadOnly)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var json = await create.Content.ReadFromJsonAsync<JsonElement>(); var id = json.GetProperty("account").GetProperty("id").GetGuid();
        var name = json.GetProperty("account").GetProperty("username").GetString()!; var temporary = json.GetProperty("temporaryCredential").GetString()!;
        using var helper = await h.Login(name, temporary);
        var password = TestCredentials.NewPassword();
        Assert.Equal(HttpStatusCode.OK, (await helper.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(temporary, password))).StatusCode);
        using var helperClient = await h.Login(name, password);
        Assert.Equal(HttpStatusCode.OK, (await helperClient.GetAsync("/api/office/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await helperClient.PostAsJsonAsync("/api/office/me/helpers", new CreateOfficeHelperRequest("forbidden", new OfficeHelperInput("Attack", null, "Custom", null, HelperAccessLevel.None)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await helperClient.GetAsync("/api/office/accounts/options")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{id}", new UpdateUserRequest("Attack", null, [SeedData.SystemAdminRoleId], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/office/me/helpers", new CreateOfficeHelperRequest($"bad_{Guid.NewGuid():N}", new OfficeHelperInput("Attack", null, "Custom", null, HelperAccessLevel.ReadWrite, [PermissionCodes.LrEdit])))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/office/accounts/{officer.Id}", new UpdateOfficeAccountRequest(Input(), await h.Revision(admin, officer.Id)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await helperClient.GetAsync("/api/auth/me")).StatusCode);
        using var freshHelper = await h.Login(name, password);
        Assert.Equal(HttpStatusCode.Forbidden, (await freshHelper.GetAsync("/api/villages?page=0&pageSize=10")).StatusCode);
    }

    [Fact]
    public async Task Admin_reset_issues_fresh_24_hour_credential_and_invalidates_all_old_sessions()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin(); var officer = await h.Create(admin, Input()); using var client = officer.Client;
        using var secondSession = await h.Login(officer.Name, officer.Password);
        var reset = await admin.PostAsJsonAsync($"/api/office/accounts/{officer.Id}/reset-credential", new RevisionRequest(await h.Revision(admin, officer.Id)));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var result = await reset.Content.ReadFromJsonAsync<JsonElement>(); var temporary = result.GetProperty("temporaryCredential").GetString()!;
        Assert.NotEqual(officer.Password, temporary);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondSession.GetAsync("/api/auth/me")).StatusCode);
        using var scope = h.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var user = await db.AppUsers.SingleAsync(u => u.Id == officer.Id);
        Assert.NotEqual(temporary, user.PasswordHash); Assert.True(user.MustChangePassword);
        user.TemporaryCredentialExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        using var expired = h.Client(); Assert.Equal(HttpStatusCode.Unauthorized, (await expired.PostAsJsonAsync("/api/auth/login", new LoginRequest(officer.Name, temporary))).StatusCode);
        foreach (var log in await db.AuditLogs.Where(a => a.EntityId == officer.Id).ToListAsync())
        foreach (var values in new[] { log.OldValues, log.NewValues }.Where(v => v is not null))
        {
            Assert.DoesNotContain(temporary, values!);
            using var audit = JsonDocument.Parse(values!);
            Assert.DoesNotContain(audit.RootElement.EnumerateObject(), p => !AuditRedaction.Include(p.Name));
        }
    }

    [Fact]
    public async Task Helper_scope_desk_and_permission_requests_fail_closed_and_edits_invalidate_sessions()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin();
        var officer = await h.Create(admin, Input(land: LandAccessLevel.ViewWrite)); using var parent = officer.Client;
        var deskId = (await (await admin.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"V3_{Guid.NewGuid():N}", "Parent desk", null, null))).Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var otherDesk = (await (await admin.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"V3_{Guid.NewGuid():N}", "Other desk", null, null))).Content.ReadFromJsonAsync<IdResponse>())!.Id;
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/admin/users/{officer.Id}/desks", new AssignDeskRequest(deskId, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await parent.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.PostAsJsonAsync("/api/auth/login", new LoginRequest(officer.Name, officer.Password))).StatusCode);
        var baseHelper = new OfficeHelperInput("Synthetic helper", await h.Designation("DEO"), null, deskId, HelperAccessLevel.ReadWrite);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.PostAsJsonAsync("/api/office/me/helpers", new CreateOfficeHelperRequest($"bad_{Guid.NewGuid():N}", baseHelper with { DeskId = otherDesk }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.PostAsJsonAsync("/api/office/me/helpers", new CreateOfficeHelperRequest($"bad_{Guid.NewGuid():N}", baseHelper with { PermissionCodes = [PermissionCodes.CourtEdit] }))).StatusCode);
        using var scope = h.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var village = await db.Villages.FirstAsync(); var work = await db.WorkDefinitions.SingleAsync(w => w.Code == "LR");
        // Narrow the parent's responsibility via the existing allocation API; geographic View remains broad.
        var existing = await db.WorkAllocations.Where(a => a.UserId == officer.Id && a.WorkDefinitionId == work.Id && a.RevokedAt == null).SingleAsync();
        var bounded = new WorkAllocationInput(work.Id, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), "BOUND-V3", null, [new AllocationScopeInput(AllocationScopeKind.Village, VillageId: village.Id)]);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/users/{officer.Id}/allocations/{existing.Id}", new UpdateAllocationRequest(bounded, existing.Revision))).StatusCode);
        await db.Entry(existing).ReloadAsync();
        // Echo the authoritative saved interval; PostgreSQL timestamps have microsecond precision.
        bounded = bounded with { ValidFrom = existing.ValidFrom, ValidTo = existing.ValidTo };
        Assert.Equal(HttpStatusCode.OK, (await parent.PostAsJsonAsync("/api/auth/login", new LoginRequest(officer.Name, officer.Password))).StatusCode);
        var expanded = bounded with { Scopes = [new AllocationScopeInput(AllocationScopeKind.Global)], DelegatedFromAllocationId = existing.Id };
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.PostAsJsonAsync("/api/office/me/helpers", new CreateOfficeHelperRequest($"bad_{Guid.NewGuid():N}", baseHelper with { Allocations = [expanded] }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.PostAsJsonAsync("/api/office/me/helpers", new CreateOfficeHelperRequest($"bad_{Guid.NewGuid():N}", baseHelper with { Allocations = [bounded with { ValidTo = DateTimeOffset.UtcNow.AddDays(2), DelegatedFromAllocationId = existing.Id }] }))).StatusCode);
        var response = await parent.PostAsJsonAsync("/api/office/me/helpers", new CreateOfficeHelperRequest($"helper_{Guid.NewGuid():N}", baseHelper with { Allocations = [bounded with { DelegatedFromAllocationId = existing.Id }] }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(); var detail = result.GetProperty("account");
        var id = detail.GetProperty("id").GetGuid(); var name = detail.GetProperty("username").GetString()!;
        var temporary = result.GetProperty("temporaryCredential").GetString()!;
        using var first = await h.Login(name, temporary); var password = TestCredentials.NewPassword();
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(temporary, password))).StatusCode);
        using var helper = await h.Login(name, password);
        var written = await helper.PostAsJsonAsync("/api/khatauni", new { villageId = village.Id, verificationStatus = "Draft" });
        Assert.Equal(HttpStatusCode.Created, written.StatusCode); var record = (await written.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var log = await db.AuditLogs.SingleAsync(a => a.EntityId == record && a.Action == "Created");
        Assert.Equal(id, log.ActorUserId); Assert.Equal(officer.Id, log.OnBehalfOfUserId);
        Assert.Contains("on behalf of Officer", log.ActorLabel);
        Assert.Equal(HttpStatusCode.OK, (await parent.PutAsJsonAsync($"/api/office/me/helpers/{id}", new UpdateOfficeHelperRequest(baseHelper with { Access = HelperAccessLevel.ReadOnly }, detail.GetProperty("assistantRevision").GetInt32()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await helper.GetAsync("/api/auth/me")).StatusCode);
        using var fresh = await h.Login(name, password);
        Assert.Equal(HttpStatusCode.Forbidden, (await fresh.PostAsJsonAsync("/api/khatauni", new { villageId = village.Id, verificationStatus = "Draft" })).StatusCode);
        var unrelatedVillage = await db.Villages.FirstAsync(v => v.Id != village.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await fresh.GetAsync($"/api/villages/{unrelatedVillage.Id}/khatauni")).StatusCode);
    }

    [Fact]
    public async Task Reserved_roles_dormant_targets_and_raw_access_manage_cannot_bypass_hierarchy()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin();
        var head = await h.Create(admin, Input(OfficeAuthority.OFFICE_ADMIN)); using var headClient = head.Client;
        var supervisor = await h.Create(headClient, Input(OfficeAuthority.OFFICE_SUPERVISOR)); using var supervisorClient = supervisor.Client;
        Assert.Equal(HttpStatusCode.Forbidden, (await headClient.PutAsJsonAsync($"/api/office/accounts/{head.Id}", new UpdateOfficeAccountRequest(Input(OfficeAuthority.OFFICE_ADMIN), await h.Revision(admin, head.Id)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisorClient.PostAsJsonAsync($"/api/office/accounts/{head.Id}/reset-credential", new RevisionRequest(await h.Revision(admin, head.Id)))).StatusCode);
        using var scope = h.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var officeRole = await db.Roles.SingleAsync(r => r.Code == "OFFICE_ADMIN");
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisorClient.PutAsJsonAsync($"/api/admin/roles/{officeRole.Id}", new UpdateRoleRequest("Attack", null, []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/roles/{officeRole.Id}", new UpdateRoleRequest("Attack", null, []))).StatusCode);
        // A dormant reserved membership still protects a target from takeover.
        officeRole.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisorClient.PostAsync($"/api/admin/users/{head.Id}/toggle-status", null)).StatusCode);
        officeRole.IsActive = true; await db.SaveChangesAsync();
        var customRole = new Role { Code = $"LEGACY_{Guid.NewGuid():N}", Name = "Legacy technical grant" };
        db.AddRange(customRole, new RolePermission { RoleId = customRole.Id, PermissionId = (await db.Permissions.SingleAsync(p => p.Code == PermissionCodes.AccessManage)).Id, ScopeMode = ScopeMode.All });
        var ordinary = await h.Create(admin, Input()); using var original = ordinary.Client;
        db.UserRoles.Add(new UserRole { UserId = ordinary.Id, RoleId = customRole.Id }); await db.SaveChangesAsync();
        using var crafted = await h.Login(ordinary.Name, ordinary.Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await crafted.PostAsJsonAsync("/api/admin/users", new CreateUserRequest($"attack_{Guid.NewGuid():N}", "Attack", TestCredentials.NewPassword(), null, [SeedData.SystemAdminRoleId], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await headClient.PostAsJsonAsync($"/api/admin/users/{ordinary.Id}/reset-password", new ResetPasswordRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/roles", new CreateRoleRequest("OFFICE_SUPERVISOR", "Forged", null, []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/office/accounts", new CreateOfficeAccountRequest($"bad_{Guid.NewGuid():N}", Input((OfficeAuthority)999)))).StatusCode);
    }

    [Fact]
    public async Task Seeding_is_idempotent_and_legacy_land_to_court_migration_never_expands_office_presets()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin();
        var officer = await h.Create(admin, Input(land: LandAccessLevel.ViewWrite)); using var client = officer.Client;
        using var scope = h.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var roles = await db.Roles.CountAsync(); var users = await db.AppUsers.CountAsync();
        for (var iteration = 0; iteration < 2; iteration++) await SeedData.SeedAsync(db);
        Assert.Equal(roles, await db.Roles.CountAsync()); Assert.Equal(users, await db.AppUsers.CountAsync());
        var me = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        Assert.DoesNotContain(me.Permissions, p => p.Code.StartsWith("Court.", StringComparison.Ordinal));
        Assert.Equal(OfficeAuthority.STANDARD_OFFICER, me.Authority);
    }

    [Fact]
    public async Task System_admin_handover_promotion_and_last_active_protection_remain_role_based()
    {
        using var h = new Harness(NewFactory()); using var bootstrap = await h.Admin();
        var technical = await h.Create(bootstrap, Input(OfficeAuthority.SYSTEM_ADMIN)); using var system = technical.Client;
        var me = (await system.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        Assert.Null(me.Designation); Assert.Equal(OfficeAuthority.SYSTEM_ADMIN, me.Authority);
        Assert.Equal(PermissionCodes.All.Select(p => p.Code).OrderBy(c => c), me.Permissions.Select(p => p.Code).OrderBy(c => c));
        var officer = await h.Create(system, Input()); using var oldSession = officer.Client;
        Assert.Equal(HttpStatusCode.OK, (await system.PutAsJsonAsync($"/api/office/accounts/{officer.Id}", new UpdateOfficeAccountRequest(Input(OfficeAuthority.OFFICE_SUPERVISOR), await h.Revision(system, officer.Id)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/auth/me")).StatusCode);
        using var promoted = await h.Login(officer.Name, officer.Password);
        Assert.Equal(OfficeAuthority.OFFICE_SUPERVISOR, (await promoted.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Authority);
        Assert.Equal(HttpStatusCode.OK, (await system.PostAsJsonAsync($"/api/office/accounts/{SeedData.BootstrapAdminId}/toggle-status", new RevisionRequest(await h.Revision(system, SeedData.BootstrapAdminId)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bootstrap.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await system.PostAsJsonAsync($"/api/office/accounts/{technical.Id}/toggle-status", new RevisionRequest(await h.Revision(system, technical.Id)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await system.PutAsJsonAsync($"/api/office/accounts/{technical.Id}", new UpdateOfficeAccountRequest(Input(), await h.Revision(system, technical.Id)))).StatusCode);
    }

    [Fact]
    public async Task Helper_current_user_contract_reports_parent_scope_instead_of_all_scope()
    {
        using var h = new Harness(NewFactory()); using var admin = await h.Admin();
        var officer = await h.Create(admin, Input(modules: [OfficeModule.Court])); using var parent = officer.Client;
        var response = await parent.PostAsJsonAsync("/api/office/me/helpers", new CreateOfficeHelperRequest($"helper_{Guid.NewGuid():N}",
            new OfficeHelperInput("Court helper", null, "Court support", null, HelperAccessLevel.ReadOnly)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var name = json.GetProperty("account").GetProperty("username").GetString()!;
        using var helper = await h.Login(name, json.GetProperty("temporaryCredential").GetString()!);
        var me = (await helper.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        Assert.Equal(OfficeAuthority.HELPER, me.Authority);
        var view = Assert.Single(me.Permissions, p => p.Code == PermissionCodes.CourtView);
        Assert.Equal("Workstream", view.Scope);
        Assert.DoesNotContain(me.Permissions, p => p.Code == PermissionCodes.CourtEdit || p.Code == PermissionCodes.UsersManage);
    }
}
