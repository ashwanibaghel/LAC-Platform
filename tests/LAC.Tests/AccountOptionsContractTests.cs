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

public sealed class AccountOptionsContractTests(RbacFactory factory) : IClassFixture<RbacFactory>
{
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<HttpClient> Login(string username, string password)
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password))).StatusCode);
        return client;
    }

    private Task<HttpClient> Admin() => Login(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass);

    private async Task<Guid> Role(HttpClient admin, params string[] permissions)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/roles", new CreateRoleRequest($"OPTIONS_{Guid.NewGuid():N}", "Options test role", null,
            permissions.Select(x => new RolePermissionInput(x, ScopeMode.All)).ToList()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private async Task<(Guid Id, HttpClient Client)> User(HttpClient admin, params Guid[] roles)
    {
        var username = $"options_{Guid.NewGuid():N}"; var password = TestCredentials.NewPassword();
        var response = await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(username, "Options test user", password, null, roles, null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<IdResponse>())!.Id, await Login(username, password));
    }

    private static async Task<JsonElement> Options(HttpClient client)
    {
        var response = await client.GetAsync("/api/admin/account-options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Guid[] Ids(JsonElement options, string key) => options.GetProperty(key).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();

    private static void AssertMetadataShape(JsonElement options)
    {
        Assert.NotEmpty(options.GetProperty("workstreams").EnumerateArray());
        foreach (var item in options.GetProperty("workstreams").EnumerateArray())
            Assert.Equal(new[] { "code", "id", "name" }, item.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
        foreach (var item in options.GetProperty("desks").EnumerateArray())
            Assert.Equal(new[] { "code", "id", "name", "workstreamId" }, item.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
    }

    [Fact]
    public async Task Account_and_allocation_manager_gets_only_active_metadata_without_receiving_grants()
    {
        using var admin = await Admin(); var role = await Role(admin, PermissionCodes.UsersManage, PermissionCodes.AllocationsManage);
        var user = await User(admin, role); using var client = user.Client;
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var streams = new[] { new Workstream(), new Workstream { IsActive = false }, new Workstream { RecordStatus = RecordStatus.Archived }, new Workstream { RecordStatus = RecordStatus.Inactive } };
        foreach (var stream in streams) { stream.Code = $"OPTIONS_{Guid.NewGuid():N}"; stream.Name = "Options stream"; }
        var desks = new[] { new OfficeDesk(), new OfficeDesk { IsActive = false }, new OfficeDesk { RecordStatus = RecordStatus.Archived }, new OfficeDesk { RecordStatus = RecordStatus.Inactive } };
        foreach (var desk in desks) { desk.Code = $"OPTIONS_{Guid.NewGuid():N}"; desk.Name = "Options desk"; desk.WorkstreamId = streams[0].Id; }
        var standalone = new OfficeDesk { Code = $"OPTIONS_{Guid.NewGuid():N}", Name = "Unclassified seat" };
        db.AddRange(streams); db.AddRange(desks); db.Add(standalone); await db.SaveChangesAsync();
        var before = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        Assert.DoesNotContain(before.Permissions, x => x.Code == PermissionCodes.AccessManage);
        var options = await Options(client); AssertMetadataShape(options);
        Assert.False(options.GetProperty("canAssignRoles").GetBoolean()); Assert.Empty(Ids(options, "roles"));
        Assert.True(options.GetProperty("canManageAllocations").GetBoolean());
        Assert.Contains(streams[0].Id, Ids(options, "workstreams")); Assert.Contains(desks[0].Id, Ids(options, "desks"));
        foreach (var stream in streams.Skip(1)) Assert.DoesNotContain(stream.Id, Ids(options, "workstreams"));
        foreach (var desk in desks.Skip(1)) Assert.DoesNotContain(desk.Id, Ids(options, "desks"));
        var seat = options.GetProperty("desks").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == standalone.Id);
        Assert.Equal(JsonValueKind.Null, seat.GetProperty("workstreamId").ValueKind);
        Assert.Equal(streams[0].Id, options.GetProperty("desks").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == desks[0].Id).GetProperty("workstreamId").GetGuid());
        var after = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        Assert.Equal(before.Permissions.Select(x => x.Code).OrderBy(x => x), after.Permissions.Select(x => x.Code).OrderBy(x => x));
        Assert.Equal(new[] { role }, await db.UserRoles.Where(x => x.UserId == user.Id).Select(x => x.RoleId).ToArrayAsync());
        Assert.False(await db.WorkAllocations.AnyAsync(x => x.UserId == user.Id));
        Assert.False(await db.UserWorkstreamMemberships.AnyAsync(x => x.UserId == user.Id));
        Assert.False(await db.UserDeskMemberships.AnyAsync(x => x.UserId == user.Id));
    }

    [Fact]
    public async Task Catalog_manager_gets_workstream_options_without_security_or_allocation_authority()
    {
        using var admin = await Admin(); var user = await User(admin, await Role(admin, PermissionCodes.WorkCatalogManage)); using var client = user.Client;
        var profile = (await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        Assert.DoesNotContain(profile.Permissions, x => x.Code == PermissionCodes.AccessManage);
        var options = await Options(client); AssertMetadataShape(options);
        Assert.False(options.GetProperty("canAssignRoles").GetBoolean()); Assert.Empty(Ids(options, "roles"));
        Assert.False(options.GetProperty("canManageAllocations").GetBoolean());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var expected = await db.Workstreams.Where(x => x.IsActive && x.RecordStatus == RecordStatus.Active).Select(x => x.Id).ToListAsync();
        Assert.Equal(expected.OrderBy(x => x), Ids(options, "workstreams").OrderBy(x => x));
    }

    [Fact]
    public async Task Unprivileged_and_anonymous_callers_cannot_read_options()
    {
        using var admin = await Admin(); var user = await User(admin); using var client = user.Client;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/account-options")).StatusCode);
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/account-options")).StatusCode);
    }

    [Fact]
    public async Task Role_assignment_flag_and_permission_ceiling_filter_are_preserved()
    {
        using var admin = await Admin();
        var reader = await Role(admin, PermissionCodes.LrView); var writer = await Role(admin, PermissionCodes.LrEdit);
        var privileged = await Role(admin, PermissionCodes.AccessManage);
        var inactive = await Role(admin, PermissionCodes.LrView); var archived = await Role(admin, PermissionCodes.LrView);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        (await db.Roles.SingleAsync(x => x.Id == inactive)).IsActive = false;
        (await db.Roles.SingleAsync(x => x.Id == archived)).RecordStatus = RecordStatus.Archived; await db.SaveChangesAsync();
        var user = await User(admin, await Role(admin, PermissionCodes.UsersManage, PermissionCodes.RolesAssign, PermissionCodes.LrView)); using var client = user.Client;
        var options = await Options(client); Assert.True(options.GetProperty("canAssignRoles").GetBoolean());
        Assert.Contains(reader, Ids(options, "roles"));
        foreach (var id in new[] { writer, privileged, inactive, archived, SeedData.SystemAdminRoleId }) Assert.DoesNotContain(id, Ids(options, "roles"));
        var adminOptions = await Options(admin); Assert.True(adminOptions.GetProperty("canAssignRoles").GetBoolean());
        foreach (var id in new[] { reader, writer, privileged, SeedData.SystemAdminRoleId }) Assert.Contains(id, Ids(adminOptions, "roles"));
        Assert.DoesNotContain(inactive, Ids(adminOptions, "roles")); Assert.DoesNotContain(archived, Ids(adminOptions, "roles"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Metadata_does_not_widen_AccessManage_routes(bool catalogOnly)
    {
        using var admin = await Admin();
        var role = catalogOnly ? await Role(admin, PermissionCodes.WorkCatalogManage) : await Role(admin, PermissionCodes.UsersManage, PermissionCodes.AllocationsManage);
        var user = await User(admin, role); using var client = user.Client;
        var options = await Options(client);
        var protectedRole = await Role(admin, PermissionCodes.LrView);
        var stream = Ids(options, "workstreams").First();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var desk = new OfficeDesk { Code = $"OPTIONS_{Guid.NewGuid():N}", Name = "Protected desk", WorkstreamId = stream };
        db.Add(desk); await db.SaveChangesAsync();
        foreach (var route in new[] { "/roles", $"/roles/{protectedRole}", "/permissions", "/designations", "/workstreams", "/desks" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin" + route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/admin/roles", new CreateRoleRequest($"DENIED_{Guid.NewGuid():N}", "Denied", null, []))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/roles/{protectedRole}", new UpdateRoleRequest("Denied", null, []))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/admin/desks", new { code = $"DENIED_{Guid.NewGuid():N}", name = "Denied", workstreamId = stream })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/desks/{desk.Id}", new { name = "Denied", workstreamId = stream })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/admin/desks/{desk.Id}/toggle-status", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/admin/users/{user.Id}", new UpdateUserRequest("Options test user", null, [SeedData.SystemAdminRoleId], null, null))).StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal("Protected desk", (await db.OfficeDesks.SingleAsync(x => x.Id == desk.Id)).Name);
        Assert.True((await db.OfficeDesks.SingleAsync(x => x.Id == desk.Id)).IsActive);
        Assert.Equal(new[] { role }, await db.UserRoles.Where(x => x.UserId == user.Id).Select(x => x.RoleId).ToArrayAsync());
    }
}
