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

// Direct cookie/API proofs continued from the requested base.
// Security regressions now assert denial; the earlier baseline evidence is preserved in the audit report.
public sealed class RbacBaselineProofTests : IClassFixture<RbacFactory>
{
    private readonly RbacFactory factory;
    public RbacBaselineProofTests(RbacFactory factory) => this.factory = factory;

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<HttpClient> Admin()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass))).StatusCode);
        return client;
    }

    private async Task<Guid> Role(HttpClient admin, params RolePermissionInput[] permissions)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/roles",
            new CreateRoleRequest($"PROOF_{Guid.NewGuid():N}", "Proof bundle", null, permissions));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private async Task<(Guid Id, string Username, string Password, HttpClient Client)> User(
        HttpClient admin, Guid? designation = null, List<Guid>? roles = null, List<Guid>? workstreams = null)
    {
        var username = $"proof_{Guid.NewGuid():N}";
        var password = $"Proof!{Guid.NewGuid():N}7a";
        var response = await admin.PostAsJsonAsync("/api/admin/users",
            new CreateUserRequest(username, "Proof officer", password, designation, roles, workstreams, workstreams?.FirstOrDefault()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var client = Client();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password))).StatusCode);
        return (id, username, password, client);
    }

    [Fact]
    public async Task Anonymous_direct_admin_read_and_write_are_401()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/admin/users",
            new CreateUserRequest("unauthorized", "Unauthorized", "", null, null, null, null))).StatusCode);
    }

    [Fact]
    public async Task Designation_and_workstream_without_role_grant_no_authority()
    {
        using var admin = await Admin();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var designation = await db.Designations.SingleAsync(d => d.Code == "ADM");
        var workstream = await db.Workstreams.SingleAsync(w => w.Code == WorkstreamCodes.LandRecords);
        var village = await db.Villages.FirstAsync();
        var user = await User(admin, designation.Id, workstreams: [workstream.Id]);
        using var client = user.Client;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/villages/{village.Id}/khatauni")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/admin/roles",
            new CreateRoleRequest("UNAUTHORIZED", "Unauthorized", null, []))).StatusCode);
    }

    [Fact]
    public async Task Same_permission_bundle_works_independently_of_designation()
    {
        using var admin = await Admin();
        var role = await Role(admin, new RolePermissionInput(PermissionCodes.LrView, ScopeMode.All));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var village = await db.Villages.FirstAsync();
        foreach (var code in new[] { "PATWARI", "DEO", "ADM" })
        {
            var designation = await db.Designations.SingleAsync(d => d.Code == code);
            var user = await User(admin, designation.Id, [role]);
            using var client = user.Client;
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/villages/{village.Id}/khatauni")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/khatauni",
                new { villageId = village.Id, verificationStatus = "Draft" })).StatusCode);
        }
    }

    [Fact]
    public async Task Workstream_and_role_removal_revoke_existing_cookie_access()
    {
        using var admin = await Admin();
        var role = await Role(admin, new RolePermissionInput(PermissionCodes.LrView, ScopeMode.Workstream));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var workstream = await db.Workstreams.SingleAsync(w => w.Code == WorkstreamCodes.LandRecords);
        var village = await db.Villages.FirstAsync();
        var user = await User(admin, roles: [role], workstreams: [workstream.Id]);
        using var client = user.Client;
        var path = $"/api/villages/{village.Id}/khatauni";
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/users/{user.Id}",
            new UpdateUserRequest("Proof officer", null, [role], [], null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/users/{user.Id}",
            new UpdateUserRequest("Proof officer", null, [], [workstream.Id], workstream.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_and_edit_multiple_roles_and_workstreams_independently_of_designation()
    {
        using var admin = await Admin();
        var lrRole = await Role(admin, new RolePermissionInput(PermissionCodes.LrView, ScopeMode.Workstream));
        var awardRole = await Role(admin, new RolePermissionInput(PermissionCodes.AwardView, ScopeMode.Workstream));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var designation = await db.Designations.SingleAsync(d => d.Code == "PATWARI");
        var lrWorkstream = await db.Workstreams.SingleAsync(w => w.Code == WorkstreamCodes.LandRecords);
        var awardWorkstream = await db.Workstreams.SingleAsync(w => w.Code == WorkstreamCodes.Award);
        var village = await db.Villages.FirstAsync();
        var award = await db.Awards.FirstAsync();
        var user = await User(admin, designation.Id, [lrRole, awardRole], [lrWorkstream.Id, awardWorkstream.Id]);
        using var client = user.Client;
        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");
        Assert.Equal(2, me!.Roles.Count);
        Assert.Equal(2, me.Workstreams.Count);
        Assert.Equal("PATWARI", me.Designation!.Code);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/villages/{village.Id}/khatauni")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/awards/{award.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/users/{user.Id}",
            new UpdateUserRequest("Edited proof officer", designation.Id, [lrRole], [lrWorkstream.Id], lrWorkstream.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, user.Password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/villages/{village.Id}/khatauni")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/awards/{award.Id}")).StatusCode);
        me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");
        Assert.Single(me!.Roles);
        Assert.Single(me.Workstreams);
        Assert.Equal("PATWARI", me.Designation!.Code);
    }

    [Fact]
    public async Task Deactivated_account_cannot_use_existing_cookie_or_login()
    {
        using var admin = await Admin();
        var role = await Role(admin, new RolePermissionInput(PermissionCodes.UsersManage, ScopeMode.All));
        var user = await User(admin, roles: [role]);
        using var client = user.Client;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/users/{user.Id}/toggle-status", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/users")).StatusCode);
        using var fresh = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(user.Username, user.Password))).StatusCode);
    }

    [Fact]
    public async Task UsersManage_alone_cannot_promote_self_or_others_or_reset_a_privileged_account()
    {
        using var admin = await Admin();
        var role = await Role(admin, new RolePermissionInput(PermissionCodes.UsersManage, ScopeMode.All));
        var user = await User(admin, roles: [role]);
        using var client = user.Client;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{user.Id}",
            new UpdateUserRequest("Proof officer", null, [SeedData.SystemAdminRoleId], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/roles")).StatusCode);
        var other = await User(admin);
        using var otherClient = other.Client;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{other.Id}",
            new UpdateUserRequest("Other officer", null, [SeedData.SystemAdminRoleId], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/admin/users",
            new CreateUserRequest($"attack_{Guid.NewGuid():N}", "Attack", TestCredentials.NewPassword(), null,
                [SeedData.SystemAdminRoleId], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/admin/users/{SeedData.BootstrapAdminId}/reset-password",
            new ResetPasswordRequest(TestCredentials.NewPassword()))).StatusCode);
        var dormantRole = await Role(admin, new RolePermissionInput(PermissionCodes.AccessManage, ScopeMode.All));
        var dormant = await User(admin, roles: [dormantRole]); using var dormantClient = dormant.Client;
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        (await db.Roles.SingleAsync(x => x.Id == dormantRole)).IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/admin/users/{dormant.Id}/reset-password", new ResetPasswordRequest())).StatusCode);
    }

    [Fact]
    public async Task RolesAssign_cannot_expand_Dak_scope_through_collection_entry_authority_or_credential_takeover()
    {
        using var admin = await Admin();
        var administration = await Role(admin, new(PermissionCodes.UsersManage, ScopeMode.All),
            new(PermissionCodes.RolesAssign, ScopeMode.All), new(PermissionCodes.AllocationsManage, ScopeMode.All));
        var assigned = await Role(admin, new RolePermissionInput(PermissionCodes.DakView, ScopeMode.Assigned));
        var broader = await Role(admin, new RolePermissionInput(PermissionCodes.DakView, ScopeMode.All));
        var manager = await User(admin, roles: [administration, assigned]); using var client = manager.Client;
        var target = await User(admin, roles: [broader]); using var targetClient = target.Client;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{manager.Id}",
            new UpdateUserRequest("Proof officer", null, [broader], null, null))).StatusCode);
        var empty = await User(admin); using var emptyClient = empty.Client;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{empty.Id}",
            new UpdateUserRequest("Proof officer", null, [broader], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/admin/users/{target.Id}/reset-password", new ResetPasswordRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{empty.Id}",
            new UpdateUserRequest("Proof officer", null, [assigned], null, null))).StatusCode);
    }

    [Fact]
    public async Task Password_reset_invalidates_all_existing_cookies()
    {
        using var admin = await Admin();
        var role = await Role(admin, new RolePermissionInput(PermissionCodes.UsersManage, ScopeMode.All));
        var user = await User(admin, roles: [role]);
        using var client = user.Client;
        using var secondSession = Client();
        Assert.Equal(HttpStatusCode.OK, (await secondSession.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(user.Username, user.Password))).StatusCode);
        var newPassword = $"Reset!{Guid.NewGuid():N}7a";
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/users/{user.Id}/reset-password",
            new ResetPasswordRequest(newPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondSession.GetAsync("/api/admin/users")).StatusCode);
        using var fresh = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(user.Username, user.Password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(user.Username, newPassword))).StatusCode);
    }

    [Fact]
    public async Task Password_is_hashed_and_user_responses_do_not_expose_credentials()
    {
        using var admin = await Admin();
        var user = await User(admin);
        using var client = user.Client;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var stored = await db.AppUsers.SingleAsync(u => u.Id == user.Id);
        Assert.NotEqual(user.Password, stored.PasswordHash);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(stored, stored.PasswordHash, user.Password));
        foreach (var path in new[] { "/api/admin/users", $"/api/admin/users/{user.Id}", "/api/auth/me" })
        {
            var json = await (path == "/api/auth/me" ? client : admin).GetStringAsync(path);
            Assert.DoesNotContain(user.Password, json);
            Assert.DoesNotContain(stored.PasswordHash, json);
            Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        }
        var auditJson = string.Join("", await db.AuditLogs.Where(a => a.EntityId == user.Id).Select(a => a.NewValues).ToListAsync());
        Assert.DoesNotContain(user.Password, auditJson);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/users/{user.Id}/reset-password",
            new ResetPasswordRequest(TestCredentials.NewPassword()))).StatusCode);
        foreach (var log in await db.AuditLogs.Where(a => a.EntityId == user.Id).ToListAsync())
        foreach (var values in new[] { log.OldValues, log.NewValues }.Where(x => x is not null))
        {
            using var audit = JsonDocument.Parse(values!);
            Assert.DoesNotContain(audit.RootElement.EnumerateObject(), p => !AuditRedaction.Include(p.Name));
        }
    }

    [Fact]
    public async Task Broad_LR_read_permission_is_independent_of_geographic_allocations()
    {
        using var admin = await Admin();
        var role = await Role(admin, new RolePermissionInput(PermissionCodes.LrView, ScopeMode.Workstream));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var workstream = await db.Workstreams.SingleAsync(w => w.Code == WorkstreamCodes.LandRecords);
        var firstVillage = await db.Villages.FirstAsync();
        var secondVillage = new Village { Name = $"Cross-area proof {Guid.NewGuid():N}", SubDivisionId = firstVillage.SubDivisionId };
        db.Villages.Add(secondVillage);
        await db.SaveChangesAsync();
        var user = await User(admin, roles: [role], workstreams: [workstream.Id]);
        using var client = user.Client;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/villages/{firstVillage.Id}/khatauni")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/villages/{secondVillage.Id}/khatauni")).StatusCode);
    }
}
