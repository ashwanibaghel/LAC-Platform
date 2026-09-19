namespace LAC.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Claim = System.Security.Claims.Claim;
using System.Text.Json;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

public sealed class RbacFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"rbac-tests-{Guid.NewGuid()}";
    public const string TestAdminUser = "testadmin";
    public const string TestAdminPass = "TestAdminPass!789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = TestAdminUser,
                ["BootstrapAdmin:Password"] = TestAdminPass,
                ["BootstrapAdmin:DisplayName"] = "Test Administrator"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LacDbContext>>();
            services.RemoveAll<LacDbContext>();
            services.AddDbContext<LacDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}

public sealed class NoBootstrapAdminFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"no-bootstrap-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = "",
                ["BootstrapAdmin:Password"] = ""
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LacDbContext>>();
            services.RemoveAll<LacDbContext>();
            services.AddDbContext<LacDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}

public sealed class RbacTests : IClassFixture<RbacFactory>
{
    private readonly RbacFactory _factory;

    public RbacTests(RbacFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_to_health_succeeds()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_to_protected_endpoints_return_401()
    {
        using var client = _factory.CreateClient();

        var villagesRes = await client.GetAsync("/api/villages");
        Assert.Equal(HttpStatusCode.Unauthorized, villagesRes.StatusCode);

        var awardsRes = await client.GetAsync("/api/awards");
        Assert.Equal(HttpStatusCode.Unauthorized, awardsRes.StatusCode);

        var adminRes = await client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, adminRes.StatusCode);

        var desksRes = await client.GetAsync("/api/admin/desks");
        Assert.Equal(HttpStatusCode.Unauthorized, desksRes.StatusCode);
    }

    [Fact]
    public async Task Admin_login_receives_cookie_and_full_permissions()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var user = await loginResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(user);
        Assert.Equal(RbacFactory.TestAdminUser, user.Username);
        Assert.Contains("SYSTEM_ADMIN", user.Roles);
        Assert.Contains(user.Permissions, p => p.Code == PermissionCodes.UsersManage);
        Assert.Contains(user.Permissions, p => p.Code == PermissionCodes.AccessManage);

        // Verify authenticated /me returns same user
        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var meUser = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(meUser);
        Assert.Equal(user.Id, meUser.Id);

        // Verify logout clears session
        var logoutResponse = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        var meAfterLogout = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogout.StatusCode);
    }

    [Fact]
    public async Task Admin_foreign_key_and_permission_validations_return_400()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));

        // 1. Invalid DesignationId
        var invalidDesignationReq = new CreateUserRequest(
            $"user_fk1_{Guid.NewGuid():N}", "FK Test", "Pass!123", Guid.NewGuid(), null, null, null);
        var res1 = await client.PostAsJsonAsync("/api/admin/users", invalidDesignationReq);
        Assert.Equal(HttpStatusCode.BadRequest, res1.StatusCode);

        // 2. Invalid RoleId
        var invalidRoleReq = new CreateUserRequest(
            $"user_fk2_{Guid.NewGuid():N}", "FK Test", "Pass!123", null, [Guid.NewGuid()], null, null);
        var res2 = await client.PostAsJsonAsync("/api/admin/users", invalidRoleReq);
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);

        // 3. Invalid WorkstreamId
        var invalidWsReq = new CreateUserRequest(
            $"user_fk3_{Guid.NewGuid():N}", "FK Test", "Pass!123", null, null, [Guid.NewGuid()], null);
        var res3 = await client.PostAsJsonAsync("/api/admin/users", invalidWsReq);
        Assert.Equal(HttpStatusCode.BadRequest, res3.StatusCode);

        // 4. PrimaryWorkstreamId not in WorkstreamIds
        Guid realWsId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            realWsId = (await db.Workstreams.FirstAsync()).Id;
        }
        var mismatchedPrimaryReq = new CreateUserRequest(
            $"user_fk4_{Guid.NewGuid():N}", "FK Test", "Pass!123", null, null, [realWsId], Guid.NewGuid());
        var res4 = await client.PostAsJsonAsync("/api/admin/users", mismatchedPrimaryReq);
        Assert.Equal(HttpStatusCode.BadRequest, res4.StatusCode);

        // 5. Invalid PermissionCode in CreateRole
        var invalidPermReq = new CreateRoleRequest(
            $"ROLE_INV_{Guid.NewGuid():N}", "Invalid Perm Role", "Description", [new RolePermissionInput("NON_EXISTENT_PERMISSION_XYZ", ScopeMode.All)]);
        var res5 = await client.PostAsJsonAsync("/api/admin/roles", invalidPermReq);
        Assert.Equal(HttpStatusCode.BadRequest, res5.StatusCode);
    }

    [Fact]
    public async Task System_admin_invariants_are_protected()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));

        // 1. Cannot create role with reserved code SYSTEM_ADMIN
        var reservedRoleReq = new CreateRoleRequest("SYSTEM_ADMIN", "Fake Admin Role", "Description", []);
        var res1 = await client.PostAsJsonAsync("/api/admin/roles", reservedRoleReq);
        Assert.Equal(HttpStatusCode.BadRequest, res1.StatusCode);

        // 2. Cannot remove all permissions from SYSTEM_ADMIN role
        Guid sysAdminRoleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            sysAdminRoleId = (await db.Roles.FirstAsync(r => r.Code == "SYSTEM_ADMIN")).Id;
        }
        var emptyPermsReq = new UpdateRoleRequest("System Administrator", "Description", []);
        var res2 = await client.PutAsJsonAsync($"/api/admin/roles/{sysAdminRoleId}", emptyPermsReq);
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);

        // 3. Cannot deactivate or demote last active system administrator
        Guid bootstrapAdminId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            bootstrapAdminId = (await db.AppUsers.FirstAsync(u => u.Username == RbacFactory.TestAdminUser)).Id;
        }
        // Attempt toggle status (deactivate)
        var res3 = await client.PostAsync($"/api/admin/users/{bootstrapAdminId}/toggle-status", null);
        Assert.Equal(HttpStatusCode.BadRequest, res3.StatusCode);

        // Attempt remove role from last active admin
        var demoteReq = new UpdateUserRequest("Admin", null, [], null, null);
        var res4 = await client.PutAsJsonAsync($"/api/admin/users/{bootstrapAdminId}", demoteReq);
        Assert.Equal(HttpStatusCode.BadRequest, res4.StatusCode);
    }

    [Fact]
    public async Task Admin_can_crud_office_desks_and_enforce_validations()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));

        Guid lrWsId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            lrWsId = (await db.Workstreams.FirstAsync(w => w.Code == WorkstreamCodes.LandRecords)).Id;
        }

        // 1. Create Office Desk
        var deskCode = $"TEST_NT_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var createReq = new CreateDeskRequest(deskCode, "Test Naib Tehsildar Desk", "Operations for land verification", lrWsId);
        var createRes = await client.PostAsJsonAsync("/api/admin/desks", createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<IdResponse>();
        Assert.NotNull(created);
        var deskId = created.Id;

        // 2. Duplicate Code returns 409 Conflict
        var dupRes = await client.PostAsJsonAsync("/api/admin/desks", createReq);
        Assert.Equal(HttpStatusCode.Conflict, dupRes.StatusCode);

        // 3. Invalid Workstream returns 400 BadRequest
        var invalidWsReq = new CreateDeskRequest($"INV_{Guid.NewGuid():N}"[..10].ToUpperInvariant(), "Invalid WS Desk", null, Guid.NewGuid());
        var invalidRes = await client.PostAsJsonAsync("/api/admin/desks", invalidWsReq);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRes.StatusCode);

        // 4. Update Desk
        var updateReq = new UpdateDeskRequest("Updated NT Desk Name", "Updated description", lrWsId);
        var updateRes = await client.PutAsJsonAsync($"/api/admin/desks/{deskId}", updateReq);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        // 5. Toggle Desk Status
        var toggleRes = await client.PostAsync($"/api/admin/desks/{deskId}/toggle-status", null);
        Assert.Equal(HttpStatusCode.OK, toggleRes.StatusCode);

        // Verify inactive in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var deskInDb = await db.OfficeDesks.FindAsync(deskId);
            Assert.NotNull(deskInDb);
            Assert.False(deskInDb.IsActive);
            Assert.Equal("Updated NT Desk Name", deskInDb.Name);
        }

        // Toggle back to active
        await client.PostAsync($"/api/admin/desks/{deskId}/toggle-status", null);
    }

    [Fact]
    public async Task Desk_membership_history_period_preserves_old_rows_and_creates_new_row_on_reassignment()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));

        // Create a test user
        var username = $"desk_hist_user_{Guid.NewGuid():N}"[..18];
        var createUsrRes = await client.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(username, "History User", "Pass!123456", null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, createUsrRes.StatusCode);
        var userId = (await createUsrRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Create an office desk
        var deskCode = $"DESK_HIST_{Guid.NewGuid():N}"[..14].ToUpperInvariant();
        var createDeskRes = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest(deskCode, "History Period Desk", null, null));
        var deskId = (await createDeskRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Step 1: Assign user to desk
        var assignRes1 = await client.PostAsJsonAsync($"/api/admin/users/{userId}/desks", new AssignDeskRequest(deskId, IsPrimary: true));
        Assert.Equal(HttpStatusCode.Created, assignRes1.StatusCode);
        var mem1Id = (await assignRes1.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Verify active
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var m1 = await db.UserDeskMemberships.FindAsync(mem1Id);
            Assert.NotNull(m1);
            Assert.True(m1.IsActive);
            Assert.True(m1.IsPrimary);
            Assert.Null(m1.RemovedAt);
        }

        // Attempting duplicate active assignment while active returns 400 BadRequest
        var dupAssignRes = await client.PostAsJsonAsync($"/api/admin/users/{userId}/desks", new AssignDeskRequest(deskId, IsPrimary: false));
        Assert.Equal(HttpStatusCode.BadRequest, dupAssignRes.StatusCode);

        // Step 2: Remove membership
        var removeRes = await client.PostAsync($"/api/admin/users/{userId}/desks/{mem1Id}/remove", null);
        Assert.Equal(HttpStatusCode.OK, removeRes.StatusCode);

        // Verify membership is now inactive and preserves historical dates
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var m1 = await db.UserDeskMemberships.FindAsync(mem1Id);
            Assert.NotNull(m1);
            Assert.False(m1.IsActive);
            Assert.False(m1.IsPrimary);
            Assert.NotNull(m1.RemovedAt);
        }

        // Step 3: Reassign user to same desk months later -> MUST create a NEW row, NOT overwrite the old one!
        var assignRes2 = await client.PostAsJsonAsync($"/api/admin/users/{userId}/desks", new AssignDeskRequest(deskId, IsPrimary: true));
        Assert.Equal(HttpStatusCode.Created, assignRes2.StatusCode);
        var mem2Id = (await assignRes2.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        Assert.NotEqual(mem1Id, mem2Id);

        // Verify BOTH rows exist in database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var userMemberships = await db.UserDeskMemberships
                .Where(m => m.UserId == userId && m.OfficeDeskId == deskId)
                .OrderBy(m => m.AssignedAt)
                .ToListAsync();

            Assert.Equal(2, userMemberships.Count);

            var oldPeriod = userMemberships[0];
            Assert.Equal(mem1Id, oldPeriod.Id);
            Assert.False(oldPeriod.IsActive);
            Assert.False(oldPeriod.IsPrimary);
            Assert.NotNull(oldPeriod.RemovedAt);

            var newPeriod = userMemberships[1];
            Assert.Equal(mem2Id, newPeriod.Id);
            Assert.True(newPeriod.IsActive);
            Assert.True(newPeriod.IsPrimary);
            Assert.Null(newPeriod.RemovedAt);
        }
    }

    [Fact]
    public async Task Single_active_primary_desk_is_enforced_transactionally()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));

        // Create user
        var username = $"desk_prim_user_{Guid.NewGuid():N}"[..18];
        var createUsrRes = await client.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(username, "Primary Test User", "Pass!123456", null, null, null, null));
        var userId = (await createUsrRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Create Desk A & Desk B
        var resDeskA = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_A_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk A", null, null));
        var deskAId = (await resDeskA.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var resDeskB = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_B_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk B", null, null));
        var deskBId = (await resDeskB.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // 1. Assign Desk A as Primary
        var assignARes = await client.PostAsJsonAsync($"/api/admin/users/{userId}/desks", new AssignDeskRequest(deskAId, IsPrimary: true));
        var memAId = (await assignARes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // 2. Assign Desk B as Primary -> Desk A must automatically lose Primary status
        var assignBRes = await client.PostAsJsonAsync($"/api/admin/users/{userId}/desks", new AssignDeskRequest(deskBId, IsPrimary: true));
        var memBId = (await assignBRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var memA = await db.UserDeskMemberships.FindAsync(memAId);
            var memB = await db.UserDeskMemberships.FindAsync(memBId);

            Assert.NotNull(memA);
            Assert.NotNull(memB);
            Assert.False(memA.IsPrimary);
            Assert.True(memB.IsPrimary);
        }

        // 3. Set Desk A back to Primary via set-primary endpoint
        var setPrimRes = await client.PostAsync($"/api/admin/users/{userId}/desks/{memAId}/set-primary", null);
        Assert.Equal(HttpStatusCode.OK, setPrimRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var memA = await db.UserDeskMemberships.FindAsync(memAId);
            var memB = await db.UserDeskMemberships.FindAsync(memBId);

            Assert.NotNull(memA);
            Assert.NotNull(memB);
            Assert.True(memA.IsPrimary);
            Assert.False(memB.IsPrimary);
        }

        // 4. Remove Desk A and verify attempting to set inactive Desk A as primary returns 400 BadRequest
        await client.PostAsync($"/api/admin/users/{userId}/desks/{memAId}/remove", null);
        var setInactivePrimRes = await client.PostAsync($"/api/admin/users/{userId}/desks/{memAId}/set-primary", null);
        Assert.Equal(HttpStatusCode.BadRequest, setInactivePrimRes.StatusCode);
    }

    [Fact]
    public async Task Inactive_office_desk_cannot_be_set_as_primary()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));

        // 1. Create User A
        var username = $"user_inact_desk_{Guid.NewGuid():N}"[..18];
        var createUsrRes = await client.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(username, "Inactive Desk Primary Test", "Pass!123456", null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, createUsrRes.StatusCode);
        var userId = (await createUsrRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // 2. Create Desk A
        var deskCode = $"DESK_INACT_{Guid.NewGuid():N}"[..14].ToUpperInvariant();
        var createDeskRes = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest(deskCode, "Desk to Deactivate", null, null));
        Assert.Equal(HttpStatusCode.Created, createDeskRes.StatusCode);
        var deskId = (await createDeskRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // 3. Assign User A to Desk A (non-primary)
        var assignRes = await client.PostAsJsonAsync($"/api/admin/users/{userId}/desks", new AssignDeskRequest(deskId, IsPrimary: false));
        Assert.Equal(HttpStatusCode.Created, assignRes.StatusCode);
        var memId = (await assignRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // 4. Deactivate Desk A
        var toggleRes = await client.PostAsync($"/api/admin/desks/{deskId}/toggle-status", null);
        Assert.Equal(HttpStatusCode.OK, toggleRes.StatusCode);

        // 5. Call set-primary for that membership
        var setPrimRes = await client.PostAsync($"/api/admin/users/{userId}/desks/{memId}/set-primary", null);

        // 6. Expect 400 BadRequest
        Assert.Equal(HttpStatusCode.BadRequest, setPrimRes.StatusCode);

        // 7. Verify no invalid primary-state mutation occurred
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var membership = await db.UserDeskMemberships.FindAsync(memId);
            Assert.NotNull(membership);
            Assert.False(membership.IsPrimary); // Primary must NOT be set
            Assert.True(membership.IsActive);   // Membership row itself was preserved and not mutated
        }
    }


    [Fact]
    public async Task Inactive_desk_rejects_new_assignments()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));

        // Create user
        var username = $"desk_inact_user_{Guid.NewGuid():N}"[..18];
        var createUsrRes = await client.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(username, "Inactive Desk Test", "Pass!123456", null, null, null, null));
        var userId = (await createUsrRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Create desk and deactivate it
        var resDesk = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_OFF_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Deactivated Desk", null, null));
        var deskId = (await resDesk.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        await client.PostAsync($"/api/admin/desks/{deskId}/toggle-status", null);

        // Attempt assignment
        var assignRes = await client.PostAsJsonAsync($"/api/admin/users/{userId}/desks", new AssignDeskRequest(deskId, IsPrimary: true));
        Assert.Equal(HttpStatusCode.BadRequest, assignRes.StatusCode);
    }

    [Fact]
    public async Task Scoped_workstream_user_access_is_enforced_by_business_function()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));

        Guid lrWsId;
        Guid awardWsId;
        Guid villageId;
        Guid awardId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            lrWsId = (await db.Workstreams.FirstAsync(w => w.Code == WorkstreamCodes.LandRecords)).Id;
            awardWsId = (await db.Workstreams.FirstAsync(w => w.Code == WorkstreamCodes.Award)).Id;
            villageId = (await db.Villages.FirstAsync()).Id;
            awardId = (await db.Awards.FirstAsync()).Id;
        }

        // 1. Create a scoped Role: LR_READER with ScopeMode.Workstream on LrView, AwardView, and MatterView
        var roleCode = $"WS_ROLE_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var createRoleRes = await client.PostAsJsonAsync("/api/admin/roles", new CreateRoleRequest(
            roleCode, "Workstream Scoped Role", "Scoped to workstream",
            [
                new RolePermissionInput(PermissionCodes.LrView, ScopeMode.Workstream),
                new RolePermissionInput(PermissionCodes.AwardView, ScopeMode.Workstream),
                new RolePermissionInput(PermissionCodes.MatterView, ScopeMode.Workstream)
            ]));
        Assert.Equal(HttpStatusCode.Created, createRoleRes.StatusCode);
        var roleId = (await createRoleRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // 2. Create user with LAND_RECORDS workstream and this role
        var lrUsername = $"lr_ws_user_{Guid.NewGuid():N}"[..16];
        var createUsrRes = await client.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(
            lrUsername, "LR Scoped Officer", "Pass!123456", null, [roleId], [lrWsId], lrWsId));
        Assert.Equal(HttpStatusCode.Created, createUsrRes.StatusCode);

        // Login as this LR scoped user
        using var lrClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginRes = await lrClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(lrUsername, "Pass!123456"));
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        // A. Accessing LAND_RECORDS endpoint: /villages/{id}/khatauni -> ALLOWED (200 OK)
        var khatauniRes = await lrClient.GetAsync($"/api/villages/{villageId}/khatauni");
        Assert.Equal(HttpStatusCode.OK, khatauniRes.StatusCode);

        // B. Accessing AWARD-workstream endpoint: /awards/{id} -> FORBIDDEN (403) because user is only in LAND_RECORDS
        var awardRes = await lrClient.GetAsync($"/api/awards/{awardId}");
        Assert.Equal(HttpStatusCode.Forbidden, awardRes.StatusCode);

        // C. Accessing POSSESSION-workstream endpoint: /awards/{id}/possession-events -> FORBIDDEN (403)
        var possessionRes = await lrClient.GetAsync($"/api/awards/{awardId}/possession-events");
        Assert.Equal(HttpStatusCode.Forbidden, possessionRes.StatusCode);

        // D. Accessing CONTEXT-LESS endpoint requiring ScopeMode.All: /matters/{id} -> FORBIDDEN (403 fail closed)
        var matterRes = await lrClient.GetAsync($"/api/matters/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, matterRes.StatusCode);
    }

    [Fact]
    public async Task ScopeMode_Assigned_and_Own_fail_closed()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));

        Guid villageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            villageId = (await db.Villages.FirstAsync()).Id;
        }

        // Role with Assigned scope mode
        var assignedRoleCode = $"ASSIGNED_ROLE_{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        var res1 = await client.PostAsJsonAsync("/api/admin/roles", new CreateRoleRequest(
            assignedRoleCode, "Assigned Role", "ScopeMode Assigned",
            [new RolePermissionInput(PermissionCodes.LrView, ScopeMode.Assigned)]));
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        var assignedRoleId = (await res1.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Role with Own scope mode
        var ownRoleCode = $"OWN_ROLE_{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        var res2 = await client.PostAsJsonAsync("/api/admin/roles", new CreateRoleRequest(
            ownRoleCode, "Own Role", "ScopeMode Own",
            [new RolePermissionInput(PermissionCodes.LrView, ScopeMode.Own)]));
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        var ownRoleId = (await res2.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // User with Assigned role
        var usrAssigned = $"usr_assigned_{Guid.NewGuid():N}"[..16];
        await client.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(usrAssigned, "Assigned User", "Pass!123456", null, [assignedRoleId], null, null));

        using var clientAssigned = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await clientAssigned.PostAsJsonAsync("/api/auth/login", new LoginRequest(usrAssigned, "Pass!123456"));
        var assignedAttempt = await clientAssigned.GetAsync($"/api/villages/{villageId}/khatauni");
        Assert.Equal(HttpStatusCode.Forbidden, assignedAttempt.StatusCode);

        // User with Own role
        var usrOwn = $"usr_own_{Guid.NewGuid():N}"[..16];
        await client.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(usrOwn, "Own User", "Pass!123456", null, [ownRoleId], null, null));

        using var clientOwn = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await clientOwn.PostAsJsonAsync("/api/auth/login", new LoginRequest(usrOwn, "Pass!123456"));
        var ownAttempt = await clientOwn.GetAsync($"/api/villages/{villageId}/khatauni");
        Assert.Equal(HttpStatusCode.Forbidden, ownAttempt.StatusCode);
    }

    [Fact]
    public async Task Audit_trail_populates_CreatedBy_UpdatedBy_and_AuditLog_ChangedBy_with_userId_string()
    {
        var testActorId = Guid.NewGuid();
        var adminUserContext = new TestUserContext(testActorId, "testactor", [], []);

        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<LacDbContext>>();
        using var db = new LacDbContext(options, adminUserContext);

        var village = new Village
        {
            Name = $"AUDIT_TEST_VILLAGE_{Guid.NewGuid():N}",
            SubDivisionId = (await db.SubDivisions.FirstAsync()).Id
        };
        db.Villages.Add(village);
        await db.SaveChangesAsync();

        // Verify CreatedBy and UpdatedBy store the UserId string
        Assert.Equal(testActorId.ToString(), village.CreatedBy);
        Assert.Equal(testActorId.ToString(), village.UpdatedBy);

        // Verify AuditLog record
        var log = await db.AuditLogs.FirstOrDefaultAsync(l => l.EntityId == village.Id && l.Action == "Created");
        Assert.NotNull(log);
        Assert.Equal(testActorId.ToString(), log.ChangedBy);

        // Unauthenticated operation stores null
        var anonymousContext = new AnonymousUserContext();
        using var anonDb = new LacDbContext(options, anonymousContext);
        var anonVillage = new Village
        {
            Name = $"ANON_VILLAGE_{Guid.NewGuid():N}",
            SubDivisionId = (await anonDb.SubDivisions.FirstAsync()).Id
        };
        anonDb.Villages.Add(anonVillage);
        await anonDb.SaveChangesAsync();

        Assert.Null(anonVillage.CreatedBy);
        Assert.Null(anonVillage.UpdatedBy);
        var anonLog = await anonDb.AuditLogs.FirstOrDefaultAsync(l => l.EntityId == anonVillage.Id && l.Action == "Created");
        Assert.NotNull(anonLog);
        Assert.Null(anonLog.ChangedBy);
    }

    [Fact]
    public async Task Bootstrap_A_without_credentials_creates_no_admin_user()
    {
        using var noAdminFactory = new NoBootstrapAdminFactory();
        using var client = noAdminFactory.CreateClient();

        var healthRes = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, healthRes.StatusCode);

        using var scope = noAdminFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var userCount = await db.AppUsers.CountAsync();
        Assert.Equal(0, userCount);
    }

    [Fact]
    public async Task Bootstrap_B_with_credentials_creates_active_system_admin()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var admin = await db.AppUsers
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username == RbacFactory.TestAdminUser);

        Assert.NotNull(admin);
        Assert.True(admin.IsActive);
        Assert.NotEmpty(admin.PasswordHash);
        Assert.Contains(admin.UserRoles, ur => ur.Role.Code == "SYSTEM_ADMIN");

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        var verifyResult = hasher.VerifyHashedPassword(admin, admin.PasswordHash, RbacFactory.TestAdminPass);
        Assert.Equal(PasswordVerificationResult.Success, verifyResult);
    }

    [Fact]
    public async Task Bootstrap_C_when_users_exist_does_not_create_duplicate()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var countBefore = await db.AppUsers.CountAsync();
        Assert.True(countBefore >= 1);

        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        await SeedData.SeedAsync(db, config);

        var countAfter = await db.AppUsers.CountAsync();
        Assert.Equal(countBefore, countAfter);
    }

    private sealed class TestUserContext(
        Guid userId,
        string username,
        IReadOnlyList<Guid> workstreamIds,
        IReadOnlyList<string> workstreamCodes,
        IReadOnlyList<Guid>? deskIds = null,
        IReadOnlyList<string>? deskCodes = null,
        Guid? primaryDeskId = null) : ICurrentUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public string? Username => username;
        public string? DisplayName => username;
        public Guid? DesignationId => null;
        public string? DesignationCode => null;
        public string? DesignationName => null;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public IReadOnlyList<Guid> WorkstreamIds => workstreamIds;
        public IReadOnlyList<string> WorkstreamCodes => workstreamCodes;
        public IReadOnlyList<Guid> DeskIds => deskIds ?? [];
        public IReadOnlyList<string> DeskCodes => deskCodes ?? [];
        public Guid? PrimaryDeskId => primaryDeskId;
    }

    private sealed class AnonymousUserContext : ICurrentUserContext
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public string? Username => null;
        public string? DisplayName => null;
        public Guid? DesignationId => null;
        public string? DesignationCode => null;
        public string? DesignationName => null;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public IReadOnlyList<Guid> WorkstreamIds => [];
        public IReadOnlyList<string> WorkstreamCodes => [];
        public IReadOnlyList<Guid> DeskIds => [];
        public IReadOnlyList<string> DeskCodes => [];
        public Guid? PrimaryDeskId => null;
    }
}
