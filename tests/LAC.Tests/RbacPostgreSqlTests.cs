using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace LAC.Tests;

public sealed class RbacPostgresFactAttribute : FactAttribute
{
    public RbacPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LAC_RBAC_TEST_SERVER")))
            Skip = "Requires an isolated PostgreSQL test server via LAC_RBAC_TEST_SERVER (loopback port 55442).";
    }
}

public sealed class RbacPostgreSqlTests : IAsyncLifetime
{
    private string connection = "";
    private string server = "";
    private string database = "";
    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = RbacFactory.TestAdminUser,
                ["BootstrapAdmin:Password"] = RbacFactory.TestAdminPass,
                ["BackgroundWorkers:Enabled"] = "false"
            }));
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<DbContextOptions<LacDbContext>>(); s.RemoveAll<LacDbContext>();
                s.AddDbContext<LacDbContext>(o => o.UseNpgsql(connection));
            });
        }
    }
    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("LAC_RBAC_TEST_SERVER");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var cs = new NpgsqlConnectionStringBuilder(configured);
        // Do not accept the office runtime connection or an arbitrary database destination.
        RbacPostgresServer.RequireDedicatedServer(cs);
        server = cs.ConnectionString; database = $"lac_rbac_test_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(server); await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin); await create.ExecuteNonQueryAsync();
        cs.Database = database; connection = cs.ConnectionString;
    }
    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(database)) return;
        if (!database.StartsWith("lac_rbac_test_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe cleanup target.");
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(server); await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
    }
    private LacDbContext Db() => new(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection).Options);

    [RbacPostgresFact]
    public async Task Full_migration_and_real_cookie_allocation_security_flow()
    {
        using var factory = new Factory(connection);
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass))).StatusCode);
        await using var db = Db();
        var bootstrap = await db.AppUsers.SingleAsync(u => u.Id == SeedData.BootstrapAdminId);
        Assert.Null(bootstrap.DesignationId);
        var credentialState = (bootstrap.PasswordHash, bootstrap.SessionVersion);
        bootstrap.DesignationId = (await db.Designations.SingleAsync(d => d.Code == "ADM")).Id;
        await db.SaveChangesAsync();
        await SeedData.SeedAsync(db);
        db.ChangeTracker.Clear();
        bootstrap = await db.AppUsers.SingleAsync(u => u.Id == SeedData.BootstrapAdminId);
        Assert.Null(bootstrap.DesignationId);
        Assert.Equal(credentialState, (bootstrap.PasswordHash, bootstrap.SessionVersion));
        Assert.Null((await admin.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Designation);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
        var work = await db.WorkDefinitions.SingleAsync(x => x.Code == "LR"); var villages = await db.Villages.Take(2).Select(x => x.Id).ToListAsync();
        var roleRes = await admin.PostAsJsonAsync("/api/admin/roles", new CreateRoleRequest("PG_LR", "PG LR", null,
            [new(PermissionCodes.LrView, ScopeMode.All), new(PermissionCodes.LrEdit, ScopeMode.All), new(PermissionCodes.AssistantsManage, ScopeMode.All)]));
        Assert.Equal(HttpStatusCode.Created, roleRes.StatusCode); var role = (await roleRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var password = TestCredentials.NewPassword();
        var officerRes = await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest("pg_officer", "PG officer", password, null, [role], null, null));
        Assert.Equal(HttpStatusCode.Created, officerRes.StatusCode); var officer = (await officerRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("pg_officer", password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/villages/{villages[1]}/khatauni")).StatusCode);
        var input = new WorkAllocationInput(work.Id, DateTimeOffset.UtcNow.AddDays(-1), null, "PG-ORDER", null, [new(AllocationScopeKind.Village, VillageId: villages[0])]);
        var allocationRes = await admin.PostAsJsonAsync($"/api/admin/users/{officer}/allocations", input);
        Assert.Equal(HttpStatusCode.Created, allocationRes.StatusCode); var allocation = (await allocationRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("pg_officer", password))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/khatauni", new { villageId = villages[0], verificationStatus = "Draft" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/khatauni", new { villageId = villages[1], verificationStatus = "Draft" })).StatusCode);
        // Exercise a delegated account and composite-key edits on PostgreSQL, not just InMemory.
        var assistantInput = new AssistantInput("PG DEO", null, [role], [PermissionCodes.LrView, PermissionCodes.LrEdit], [input with { DelegatedFromAllocationId = allocation }], []);
        var deoRes = await client.PostAsJsonAsync("/api/officers/me/assistants", new CreateAssistantRequest("pg_deo", assistantInput));
        Assert.Equal(HttpStatusCode.Created, deoRes.StatusCode);
        using var deoJson = JsonDocument.Parse(await deoRes.Content.ReadAsStringAsync()); var deoId = deoJson.RootElement.GetProperty("id").GetGuid();
        using var deo = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var temporary = deoJson.RootElement.GetProperty("temporaryCredential").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await deo.PostAsJsonAsync("/api/auth/login", new LoginRequest("pg_deo", temporary))).StatusCode);
        var permanent = TestCredentials.NewPassword();
        Assert.Equal(HttpStatusCode.OK, (await deo.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(temporary, permanent))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await deo.PostAsJsonAsync("/api/auth/login", new LoginRequest("pg_deo", permanent))).StatusCode);
        var written = await deo.PostAsJsonAsync("/api/khatauni", new { villageId = villages[0], verificationStatus = "Draft" });
        Assert.Equal(HttpStatusCode.Created, written.StatusCode); var record = (await written.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var audit = await db.AuditLogs.SingleAsync(x => x.EntityId == record && x.Action == "Created");
        Assert.Equal(deoId, audit.ActorUserId); Assert.Equal(officer, audit.OnBehalfOfUserId);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/officers/me/assistants/{deoId}", new UpdateAssistantRequest(assistantInput with { PermissionCodes = [PermissionCodes.LrView] }, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await deo.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await deo.PostAsJsonAsync("/api/auth/login", new LoginRequest("pg_deo", permanent))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await deo.PostAsJsonAsync("/api/khatauni", new { villageId = villages[0], verificationStatus = "Draft" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/users/{officer}/allocations/{allocation}", new UpdateAllocationRequest(input with { Scopes = [new(AllocationScopeKind.Village, VillageId: villages[1])] }, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await deo.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await deo.PostAsJsonAsync("/api/auth/login", new LoginRequest("pg_deo", permanent))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await deo.GetAsync($"/api/villages/{villages[0]}/khatauni")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/users/{officer}/reset-password", new ResetPasswordRequest(TestCredentials.NewPassword()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        foreach (var entry in await db.AuditLogs.Where(x => x.EntityType == "AppUser").ToListAsync())
            foreach (var values in new[] { entry.OldValues, entry.NewValues }.Where(x => x is not null))
            {
                using var json = JsonDocument.Parse(values!);
                Assert.DoesNotContain(json.RootElement.EnumerateObject(), p => !AuditRedaction.Include(p.Name));
            }
    }

    [RbacPostgresFact]
    public async Task Upgrade_redacts_historical_credentials_and_schema_downgrade_reupgrade_succeeds()
    {
        await using var db = Db(); var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        var previous = migrations[migrations.IndexOf("20261006220754_AddDynamicWorkAllocationAndSessionSecurity") - 1];
        await migrator.MigrateAsync(previous);
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AppUsers\" (\"Id\",\"Username\",\"NormalizedUsername\",\"DisplayName\",\"PasswordHash\",\"IsActive\",\"CreatedAt\",\"UpdatedAt\",\"RecordStatus\") VALUES ({id}, 'old_officer', 'OLD_OFFICER', 'Old officer', 'synthetic-hash', true, now(), now(), 'Active')");
        var oldJson = "{\"PasswordHash\":\"synthetic-hash\",\"DisplayName\":\"Old officer\",\"SecurityStamp\":\"synthetic-stamp\"}";
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AuditLogs\" (\"Id\",\"EntityType\",\"EntityId\",\"Action\",\"ChangedAt\",\"NewValues\") VALUES ({Guid.NewGuid()}, 'AppUser', {id}, 'Created', now(), {oldJson})");
        await migrator.MigrateAsync();
        Assert.NotEqual(Guid.Empty, (await db.AppUsers.SingleAsync(x => x.Id == id)).SessionVersion);
        var audit = await db.AuditLogs.SingleAsync(x => x.EntityId == id);
        using var json = JsonDocument.Parse(audit.NewValues!);
        Assert.False(json.RootElement.TryGetProperty("PasswordHash", out _)); Assert.False(json.RootElement.TryGetProperty("SecurityStamp", out _));
        Assert.Equal("Old officer", json.RootElement.GetProperty("DisplayName").GetString());
        await migrator.MigrateAsync(previous); await migrator.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [RbacPostgresFact]
    public async Task PostgreSQL_enforces_scope_foreign_keys_interval_unique_codes_and_revision_concurrency()
    {
        using var factory = new Factory(connection); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        await using var db = Db(); var user = await db.AppUsers.FirstAsync(); var work = await db.WorkDefinitions.FirstAsync(); var village = await db.Villages.FirstAsync();
        var allocation = new WorkAllocation { UserId = user.Id, WorkDefinitionId = work.Id, ValidFrom = DateTimeOffset.UtcNow.AddDays(-1), WorkOrderReference = "PG", Scopes = [new() { Kind = AllocationScopeKind.Global }] };
        db.WorkAllocations.Add(allocation); await db.SaveChangesAsync();
        await using (var invalid = Db())
        {
            invalid.WorkAllocationScopes.Add(new WorkAllocationScope { WorkAllocationId = allocation.Id, Kind = AllocationScopeKind.Global, VillageId = village.Id });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync()); Assert.Equal("23514", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }
        await using (var invalid = Db())
        {
            invalid.WorkAllocationScopes.Add(new WorkAllocationScope { WorkAllocationId = allocation.Id, Kind = AllocationScopeKind.Village, VillageId = Guid.NewGuid() });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync()); Assert.Equal("23503", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }
        await using (var invalid = Db())
        {
            invalid.WorkAllocations.Add(new WorkAllocation { UserId = user.Id, WorkDefinitionId = work.Id, ValidFrom = DateTimeOffset.UtcNow, ValidTo = DateTimeOffset.UtcNow.AddDays(-1), WorkOrderReference = "Invalid" });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync()); Assert.Equal("23514", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }
        await using (var invalid = Db())
        {
            invalid.WorkDefinitions.Add(new WorkDefinition { Code = work.Code, Name = "Duplicate", WorkstreamId = work.WorkstreamId });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync()); Assert.Equal("23505", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }
        await using var first = Db(); await using var second = Db();
        var a = await first.WorkAllocations.SingleAsync(x => x.Id == allocation.Id); var b = await second.WorkAllocations.SingleAsync(x => x.Id == allocation.Id);
        a.Revision++; a.Reason = "First change"; await first.SaveChangesAsync();
        b.Revision++; b.Reason = "Stale change"; await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
