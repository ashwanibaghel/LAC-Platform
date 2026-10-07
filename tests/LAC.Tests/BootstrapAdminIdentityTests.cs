using System.Net;
using System.Net.Http.Json;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LAC.Tests;

public sealed class BootstrapAdminIdentityTests
{
    private static async Task<HttpClient> Login(RbacFactory factory, string username, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password))).StatusCode);
        return client;
    }

    private static async Task<Guid> Created(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        }
    }

    private static async Task AssertTechnicalAdmin(HttpClient client)
    {
        var me = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        Assert.Null(me.Designation);
        Assert.Contains("SYSTEM_ADMIN", me.Roles);
        Assert.Equal(PermissionCodes.All.Select(p => p.Code).OrderBy(c => c), me.Permissions.Select(p => p.Code).OrderBy(c => c));
        Assert.All(me.Permissions, p => Assert.Equal("All", p.Scope));
    }

    [Fact]
    public async Task Fresh_bootstrap_has_no_civil_designation_and_authenticates_with_full_role_permissions()
    {
        using var factory = new RbacFactory();
        using var client = await Login(factory, RbacFactory.TestAdminUser, RbacFactory.TestAdminPass);
        await AssertTechnicalAdmin(client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        Assert.Null((await db.AppUsers.SingleAsync(u => u.Id == SeedData.BootstrapAdminId)).DesignationId);
        Assert.Equal(7, await db.Designations.CountAsync());
        Assert.DoesNotContain(await db.Designations.Select(d => d.Name).ToListAsync(), name => name == "System Administrator");
    }

    [Theory]
    [InlineData("ADM", true, true)]
    [InlineData("ADM", false, false)]
    [InlineData("PATWARI", true, false)]
    [InlineData("CUSTOM_ADM", true, false)]
    [InlineData(null, true, false)]
    public async Task Legacy_normalization_requires_exact_identity_role_and_seeded_ADM_and_is_idempotent(
        string? designationCode, bool holdsSystemAdmin, bool clearsDesignation)
    {
        using var factory = new RbacFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var bootstrap = await db.AppUsers.SingleAsync(u => u.Id == SeedData.BootstrapAdminId);
        var adm = await db.Designations.SingleAsync(d => d.Code == "ADM");
        if (designationCode == "CUSTOM_ADM")
        {
            db.Designations.Add(new Designation { Code = designationCode, Name = adm.Name });
            await db.SaveChangesAsync();
        }
        bootstrap.DesignationId = designationCode is null ? null : (await db.Designations.SingleAsync(d => d.Code == designationCode)).Id;
        if (!holdsSystemAdmin) db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == bootstrap.Id).ToListAsync());
        // Other ADM identities include another SYSTEM_ADMIN and a real officer with the same name.
        var otherAdmin = new AppUser { Username = "other-technical", NormalizedUsername = "OTHER-TECHNICAL", DisplayName = bootstrap.DisplayName, DesignationId = adm.Id, PasswordHash = bootstrap.PasswordHash };
        var officer = new AppUser { Username = "official-adm", NormalizedUsername = "OFFICIAL-ADM", DisplayName = bootstrap.DisplayName, DesignationId = adm.Id, PasswordHash = bootstrap.PasswordHash };
        db.AddRange(otherAdmin, officer, new UserRole { UserId = otherAdmin.Id, RoleId = SeedData.SystemAdminRoleId });
        await db.SaveChangesAsync();
        var originalDesignation = bootstrap.DesignationId;
        var identity = (bootstrap.Username, bootstrap.NormalizedUsername, bootstrap.DisplayName, bootstrap.PasswordHash,
            bootstrap.PasswordChangedAt, bootstrap.SessionVersion, bootstrap.IsActive);
        var roleIds = await db.UserRoles.Where(r => r.UserId == bootstrap.Id).Select(r => r.RoleId).ToListAsync();
        var userCount = await db.AppUsers.CountAsync();

        for (var run = 0; run < 2; run++)
        {
            await SeedData.SeedAsync(db, configuration);
            db.ChangeTracker.Clear();
            bootstrap = await db.AppUsers.SingleAsync(u => u.Id == SeedData.BootstrapAdminId);
            Assert.Equal(clearsDesignation ? null : originalDesignation, bootstrap.DesignationId);
            Assert.Equal(identity, (bootstrap.Username, bootstrap.NormalizedUsername, bootstrap.DisplayName, bootstrap.PasswordHash,
                bootstrap.PasswordChangedAt, bootstrap.SessionVersion, bootstrap.IsActive));
            Assert.Equal(roleIds, await db.UserRoles.Where(r => r.UserId == bootstrap.Id).Select(r => r.RoleId).ToListAsync());
            Assert.Equal(userCount, await db.AppUsers.CountAsync());
            Assert.Equal(adm.Id, (await db.AppUsers.SingleAsync(u => u.Id == otherAdmin.Id)).DesignationId);
            Assert.Equal(adm.Id, (await db.AppUsers.SingleAsync(u => u.Id == officer.Id)).DesignationId);
        }
    }

    [Fact]
    public async Task Missing_bootstrap_identity_does_not_normalize_another_SYSTEM_ADMIN_ADM()
    {
        using var factory = new NoBootstrapAdminFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var adm = await db.Designations.SingleAsync(d => d.Code == "ADM");
        var officer = new AppUser { Username = RbacFactory.TestAdminUser, NormalizedUsername = RbacFactory.TestAdminUser.ToUpperInvariant(), DisplayName = "System Administrator", DesignationId = adm.Id };
        db.AddRange(officer, new UserRole { UserId = officer.Id, RoleId = SeedData.SystemAdminRoleId });
        await db.SaveChangesAsync();
        await SeedData.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IConfiguration>());
        db.ChangeTracker.Clear();
        Assert.False(await db.AppUsers.AnyAsync(u => u.Id == SeedData.BootstrapAdminId));
        Assert.Equal(adm.Id, (await db.AppUsers.SingleAsync()).DesignationId);
    }

    [Fact]
    public async Task Normalized_bootstrap_can_handover_to_second_technical_admin_with_last_admin_safeguards()
    {
        using var factory = new RbacFactory();
        Guid admId;
        Guid workstreamId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            admId = (await db.Designations.SingleAsync(d => d.Code == "ADM")).Id;
            workstreamId = (await db.Workstreams.SingleAsync(w => w.Code == WorkstreamCodes.LandRecords)).Id;
            (await db.AppUsers.SingleAsync(u => u.Id == SeedData.BootstrapAdminId)).DesignationId = admId;
            await db.SaveChangesAsync();
            await SeedData.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IConfiguration>());
            db.ChangeTracker.Clear();
            Assert.Null((await db.AppUsers.SingleAsync(u => u.Id == SeedData.BootstrapAdminId)).DesignationId);
        }
        using var bootstrap = await Login(factory, RbacFactory.TestAdminUser, RbacFactory.TestAdminPass);
        await AssertTechnicalAdmin(bootstrap);
        Assert.Equal(HttpStatusCode.OK, (await bootstrap.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bootstrap.PostAsync($"/api/admin/users/{SeedData.BootstrapAdminId}/toggle-status", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bootstrap.PutAsJsonAsync($"/api/admin/users/{SeedData.BootstrapAdminId}", new UpdateUserRequest("Test Administrator", null, [], null, null))).StatusCode);

        var technicalName = $"technical_{Guid.NewGuid():N}";
        var technicalPassword = TestCredentials.NewPassword();
        var secondId = await Created(await bootstrap.PostAsJsonAsync("/api/admin/users",
            new CreateUserRequest(technicalName, "Technical administrator", technicalPassword, null, [SeedData.SystemAdminRoleId], null, null)));
        using var second = await Login(factory, technicalName, technicalPassword);
        await AssertTechnicalAdmin(second);
        Assert.Null((await second.GetFromJsonAsync<UserDetailResponse>($"/api/admin/users/{secondId}"))!.DesignationId);
        foreach (var path in new[] { "/api/admin/users", "/api/admin/roles", "/api/admin/permissions", "/api/admin/works", "/api/admin/desks", "/api/admin/account-options" })
            Assert.Equal(HttpStatusCode.OK, (await second.GetAsync(path)).StatusCode);

        var grants = new[] { new RolePermissionInput(PermissionCodes.LrView, ScopeMode.All) };
        var roleId = await Created(await second.PostAsJsonAsync("/api/admin/roles", new CreateRoleRequest($"HANDOVER_{Guid.NewGuid():N}", "Reader", null, grants)));
        Assert.Equal(HttpStatusCode.OK, (await second.PutAsJsonAsync($"/api/admin/roles/{roleId}", new UpdateRoleRequest("Edited reader", null, grants))).StatusCode);
        var officerName = $"official_{Guid.NewGuid():N}";
        var officerPassword = TestCredentials.NewPassword();
        var officerId = await Created(await second.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(officerName, "Official ADM", officerPassword, admId, null, null, null)));
        Assert.Equal(HttpStatusCode.OK, (await second.PutAsJsonAsync($"/api/admin/users/{officerId}", new UpdateUserRequest("Official ADM", admId, [roleId], [workstreamId], workstreamId))).StatusCode);
        var workId = await Created(await second.PostAsJsonAsync("/api/admin/works", new CreateWorkRequest($"HANDOVER_{Guid.NewGuid():N}", "Handover work", null, OperationalWorkKind.LandRecords, workstreamId)));
        await Created(await second.PostAsJsonAsync($"/api/admin/users/{officerId}/allocations", new WorkAllocationInput(workId, DateTimeOffset.UtcNow.AddMinutes(-1), null, "HANDOVER-TEST", null, [new AllocationScopeInput(AllocationScopeKind.Global)])));
        var deskId = await Created(await second.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"HANDOVER_{Guid.NewGuid():N}", "Handover desk", null, workstreamId)));
        Assert.Equal(HttpStatusCode.OK, (await second.PutAsJsonAsync($"/api/admin/desks/{deskId}", new UpdateDeskRequest("Edited desk", null, workstreamId))).StatusCode);
        await Created(await second.PostAsJsonAsync($"/api/admin/users/{officerId}/desks", new AssignDeskRequest(deskId, true)));

        using var officer = await Login(factory, officerName, officerPassword);
        var resetPassword = TestCredentials.NewPassword();
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsJsonAsync($"/api/admin/users/{officerId}/reset-password", new ResetPasswordRequest(resetPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await officer.GetAsync("/api/auth/me")).StatusCode);
        using var resetOfficer = await Login(factory, officerName, resetPassword);
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsync($"/api/admin/users/{officerId}/toggle-status", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await resetOfficer.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsync($"/api/admin/users/{officerId}/toggle-status", null)).StatusCode);
        using var reactivated = await Login(factory, officerName, resetPassword);
        Assert.Equal("ADM", (await reactivated.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Designation!.Code);

        Assert.Equal(HttpStatusCode.OK, (await second.PostAsync($"/api/admin/users/{SeedData.BootstrapAdminId}/toggle-status", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bootstrap.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await second.PostAsync($"/api/admin/users/{secondId}/toggle-status", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await second.PutAsJsonAsync($"/api/admin/users/{secondId}", new UpdateUserRequest("Technical administrator", null, [], null, null))).StatusCode);
        using var finalScope = factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<LacDbContext>();
        Assert.False((await finalDb.AppUsers.SingleAsync(u => u.Id == SeedData.BootstrapAdminId)).IsActive);
        Assert.True((await finalDb.AppUsers.SingleAsync(u => u.Id == secondId)).IsActive);
        Assert.True(await finalDb.AuditLogs.AnyAsync(a => a.ActorUserId == secondId && a.EntityId == officerId));
    }
}
