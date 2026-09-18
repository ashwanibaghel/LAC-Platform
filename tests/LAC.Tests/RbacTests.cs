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

        var adminUsersRes = await client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, adminUsersRes.StatusCode);

        var postKhasraRes = await client.PostAsJsonAsync($"/api/villages/{Guid.NewGuid()}/khasras", new { rawKhasraNumber = "1" });
        Assert.Equal(HttpStatusCode.Unauthorized, postKhasraRes.StatusCode);
    }

    [Fact]
    public async Task Login_with_invalid_credentials_returns_401()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, "WrongPassword@123"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Full_login_me_and_logout_lifecycle()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        // 1. Login as bootstrap admin
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var currentUser = await loginResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(currentUser);
        Assert.Equal(RbacFactory.TestAdminUser, currentUser.Username);
        Assert.Contains("SYSTEM_ADMIN", currentUser.Roles);

        // 2. Call /api/auth/me
        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(me);
        Assert.Equal(RbacFactory.TestAdminUser, me.Username);

        // 3. Call protected domain endpoint
        var villagesResponse = await client.GetAsync("/api/villages?page=0&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, villagesResponse.StatusCode);

        // 4. Logout
        var logoutResponse = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        // 5. Protected endpoint should now return 401
        var afterLogoutResponse = await client.GetAsync("/api/villages?page=0&pageSize=5");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogoutResponse.StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_cannot_log_in()
    {
        var username = $"inactive_{Guid.NewGuid():N}";
        const string testPass = "TestP@ss!123";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
            var user = new AppUser
            {
                Username = username,
                NormalizedUsername = username.ToUpperInvariant(),
                DisplayName = "Inactive User",
                IsActive = false
            };
            user.PasswordHash = hasher.HashPassword(user, testPass);
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, testPass));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Limited_user_with_only_village_view_is_forbidden_from_other_modules_and_mutations()
    {
        var username = $"limited_{Guid.NewGuid():N}";
        const string testPass = "LimitedP@ss!456";
        Guid villageId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();

            var village = await db.Villages.FirstAsync();
            villageId = village.Id;

            // Role with ONLY Village.View
            var role = new Role
            {
                Code = $"ROLE_VIEW_{Guid.NewGuid():N}",
                Name = "Viewer Only",
                IsActive = true
            };
            db.Roles.Add(role);

            var villageViewPerm = await db.Permissions.FirstAsync(p => p.Code == PermissionCodes.VillageView);
            db.RolePermissions.Add(new RolePermission
            {
                Role = role,
                Permission = villageViewPerm,
                ScopeMode = ScopeMode.All
            });

            var user = new AppUser
            {
                Username = username,
                NormalizedUsername = username.ToUpperInvariant(),
                DisplayName = "Limited User",
                IsActive = true
            };
            user.PasswordHash = hasher.HashPassword(user, testPass);
            db.AppUsers.Add(user);
            db.UserRoles.Add(new UserRole { User = user, Role = role });

            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, testPass));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Allowed: View villages
        var viewResponse = await client.GetAsync("/api/villages?page=0&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, viewResponse.StatusCode);

        // Forbidden: Create khasra (mutation requires Khasra.Edit)
        var postKhasraResponse = await client.PostAsJsonAsync($"/api/villages/{villageId}/khasras", new { rawKhasraNumber = "99" });
        Assert.Equal(HttpStatusCode.Forbidden, postKhasraResponse.StatusCode);

        // Forbidden: Award endpoints (requires Award.View)
        var awardsResponse = await client.GetAsync("/api/awards");
        Assert.Equal(HttpStatusCode.Forbidden, awardsResponse.StatusCode);

        // Forbidden: LR review (requires Lr.View)
        var lrReviewResponse = await client.GetAsync("/api/lr-review");
        Assert.Equal(HttpStatusCode.Forbidden, lrReviewResponse.StatusCode);

        // Forbidden: Admin endpoints (requires Admin.UserManage)
        var adminUsersResponse = await client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, adminUsersResponse.StatusCode);
    }

    [Fact]
    public async Task Scope_evaluation_handles_all_workstream_assigned_and_own_modes()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();

        var ws1 = await db.Workstreams.FirstAsync(w => w.Code == "LAND_RECORDS");
        var ws2 = await db.Workstreams.FirstAsync(w => w.Code == "AWARD");

        // Create user with Workstream scope role
        var testUser = new AppUser
        {
            Username = $"scopeuser_{Guid.NewGuid():N}",
            NormalizedUsername = $"SCOPEUSER_{Guid.NewGuid():N}",
            DisplayName = "Scope User",
            IsActive = true
        };
        testUser.PasswordHash = hasher.HashPassword(testUser, "ScopeP@ss!123");
        db.AppUsers.Add(testUser);

        // Assign to ws1
        db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership
        {
            User = testUser,
            Workstream = ws1,
            IsActive = true
        });

        var roleWs = new Role { Code = $"ROLE_WS_{Guid.NewGuid():N}", Name = "WS Scoped", IsActive = true };
        var roleOwn = new Role { Code = $"ROLE_OWN_{Guid.NewGuid():N}", Name = "Own Scoped", IsActive = true };
        var roleAssigned = new Role { Code = $"ROLE_ASGN_{Guid.NewGuid():N}", Name = "Assigned Scoped", IsActive = true };
        db.Roles.AddRange(roleWs, roleOwn, roleAssigned);

        var perm = await db.Permissions.FirstAsync(p => p.Code == PermissionCodes.LrEdit);

        db.RolePermissions.Add(new RolePermission { Role = roleWs, Permission = perm, ScopeMode = ScopeMode.Workstream });
        db.RolePermissions.Add(new RolePermission { Role = roleOwn, Permission = perm, ScopeMode = ScopeMode.Own });
        db.RolePermissions.Add(new RolePermission { Role = roleAssigned, Permission = perm, ScopeMode = ScopeMode.Assigned });

        db.UserRoles.Add(new UserRole { User = testUser, Role = roleWs });
        db.UserRoles.Add(new UserRole { User = testUser, Role = roleOwn });
        db.UserRoles.Add(new UserRole { User = testUser, Role = roleAssigned });

        await db.SaveChangesAsync();

        var testUserContext = new TestUserContext(testUser.Id, testUser.Username, [ws1.Id], [ws1.Code]);
        var accessControl = new AccessControlService(db, testUserContext);

        // 1. Workstream matching
        Assert.True(await accessControl.CanAsync(PermissionCodes.LrEdit, new AccessResourceContext(WorkstreamId: ws1.Id)));
        Assert.True(await accessControl.CanAsync(PermissionCodes.LrEdit, new AccessResourceContext(WorkstreamCode: ws1.Code)));
        Assert.False(await accessControl.CanAsync(PermissionCodes.LrEdit, new AccessResourceContext(WorkstreamId: ws2.Id)));

        // 2. Own matching
        Assert.True(await accessControl.CanAsync(PermissionCodes.LrEdit, new AccessResourceContext(OwnerUserId: testUser.Id)));
        Assert.False(await accessControl.CanAsync(PermissionCodes.LrEdit, new AccessResourceContext(OwnerUserId: Guid.NewGuid())));

        // 3. Assigned mode fails closed in Phase 1
        Assert.False(await accessControl.CanAsync(PermissionCodes.LrEdit, new AccessResourceContext(AssignedUserId: testUser.Id)));

        // 4. Scoped permission without resource context fails closed
        Assert.False(await accessControl.CanAsync(PermissionCodes.LrEdit, null));
    }

    [Fact]
    public async Task Admin_user_management_crud_and_security()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));

        var newUsername = $"newuser_{Guid.NewGuid():N}";
        var createRequest = new CreateUserRequest(
            newUsername,
            "New Test User",
            "SecretPass@123!",
            null,
            null,
            null,
            null
        );

        // 1. Create user
        var createResponse = await client.PostAsJsonAsync("/api/admin/users", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdId = (await createResponse.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // 2. Prevent duplicate username
        var duplicateResponse = await client.PostAsJsonAsync("/api/admin/users", createRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        // 3. Verify user list does NOT expose password or hash
        var usersResponse = await client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.OK, usersResponse.StatusCode);
        var responseString = await usersResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", responseString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", responseString, StringComparison.OrdinalIgnoreCase);

        // 4. Toggle status
        var toggleResponse = await client.PostAsync($"/api/admin/users/{createdId}/toggle-status", null);
        Assert.Equal(HttpStatusCode.OK, toggleResponse.StatusCode);

        // 5. Reset password
        var resetResponse = await client.PostAsJsonAsync($"/api/admin/users/{createdId}/reset-password", new ResetPasswordRequest("BrandNewPass@123!"));
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
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

        // Health endpoint succeeds and app starts up without crashing
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

        // Verify password hasher successfully verifies
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

        // Run seed again
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        await SeedData.SeedAsync(db, config);

        var countAfter = await db.AppUsers.CountAsync();
        Assert.Equal(countBefore, countAfter);
    }

    private sealed class TestUserContext(Guid userId, string username, IReadOnlyList<Guid> workstreamIds, IReadOnlyList<string> workstreamCodes) : ICurrentUserContext
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
    }
}
