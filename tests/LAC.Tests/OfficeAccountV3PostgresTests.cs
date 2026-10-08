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

// Run the same direct API hierarchy/security matrix against real PostgreSQL.
public sealed class OfficeAccountV3PostgresTests(OfficeV3PostgresSchema schema) : IClassFixture<OfficeV3PostgresSchema>
{
    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
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
    private Task Run(Func<OfficeAccountV3Tests, Task> proof) => WithDatabase(connection =>
        proof(new OfficeAccountV3Tests { NewFactory = () => new Factory(connection) }));

    private async Task WithDatabase(Func<string, Task> proof, bool emptySchema = false)
    {
        var configured = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LAC_RBAC_TEST_SERVER"));
        if (configured.Host != "127.0.0.1" || configured.Port != 55438 || configured.Database != "postgres")
            throw new InvalidOperationException("V3 tests require the dedicated disposable loopback PostgreSQL server on port 55438.");
        var name = $"lac_rbac_v3_test_{Guid.NewGuid():N}";
        await using var server = new NpgsqlConnection(configured.ConnectionString); await server.OpenAsync();
        var template = emptySchema ? "" : $" TEMPLATE \"{schema.TemplateName}\"";
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"{template}", server)) await create.ExecuteNonQueryAsync();
        configured.Database = name; configured.Pooling = false;
        try { await proof(configured.ConnectionString); }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", server); await drop.ExecuteNonQueryAsync();
        }
    }
    [RbacPostgresFact] public Task System_admin_ADM_and_explicit_authority() => Run(t => t.System_admin_creates_ADM_with_explicit_office_authority_and_designation_edits_never_promote());
    [RbacPostgresFact] public Task Office_admin_hierarchy() => Run(t => t.Office_authorities_have_full_operations_but_cannot_touch_system_or_reserved_roles(OfficeAuthority.OFFICE_ADMIN));
    [RbacPostgresFact] public Task Office_supervisor_hierarchy() => Run(t => t.Office_authorities_have_full_operations_but_cannot_touch_system_or_reserved_roles(OfficeAuthority.OFFICE_SUPERVISOR));
    [RbacPostgresFact] public Task Land_none() => Run(t => t.Land_presets_enforce_real_read_and_write_boundaries(LandAccessLevel.None, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden));
    [RbacPostgresFact] public Task Land_view() => Run(t => t.Land_presets_enforce_real_read_and_write_boundaries(LandAccessLevel.ViewOnly, HttpStatusCode.OK, HttpStatusCode.Forbidden));
    [RbacPostgresFact] public Task Land_write() => Run(t => t.Land_presets_enforce_real_read_and_write_boundaries(LandAccessLevel.ViewWrite, HttpStatusCode.OK, HttpStatusCode.Created));
    [RbacPostgresFact] public Task Registry_and_persisted_future_modules() => Run(t => t.Registry_is_independent_of_Dak_Matters_and_future_modules_persist_without_fake_powers());
    [RbacPostgresFact] public Task Custom_designation_and_crafted_payloads() => Run(t => t.Custom_designation_is_normalized_displayed_and_never_grants_authority());
    [RbacPostgresFact] public Task Helper_and_parent_revocation() => Run(t => t.Ordinary_and_helper_self_service_cannot_modify_identity_or_create_normal_accounts());
    [RbacPostgresFact] public Task Credentials_and_audit_redaction() => Run(t => t.Admin_reset_issues_fresh_24_hour_credential_and_invalidates_all_old_sessions());
    [RbacPostgresFact] public Task Helper_scope_desk_and_time_ceiling() => Run(t => t.Helper_scope_desk_and_permission_requests_fail_closed_and_edits_invalidate_sessions());
    [RbacPostgresFact] public Task Dormant_authority_and_raw_API_attacks() => Run(t => t.Reserved_roles_dormant_targets_and_raw_access_manage_cannot_bypass_hierarchy());
    [RbacPostgresFact] public Task Idempotent_preset_seeding() => Run(t => t.Seeding_is_idempotent_and_legacy_land_to_court_migration_never_expands_office_presets());
    [RbacPostgresFact] public Task System_handover_and_last_active_admin() => Run(t => t.System_admin_handover_promotion_and_last_active_protection_remain_role_based());
    [RbacPostgresFact] public Task Helper_response_scope_ceiling() => Run(t => t.Helper_current_user_contract_reports_parent_scope_instead_of_all_scope());

    [RbacPostgresFact]
    public Task Concurrent_admin_deactivations_keep_one_active_system_administrator() => WithDatabase(async connection =>
    {
        using var factory = new Factory(connection);
        using var first = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass))).StatusCode);
        var name = $"technical_{Guid.NewGuid():N}"; var password = TestCredentials.NewPassword();
        var response = await first.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(name, "Technical authority", password, null, [SeedData.SystemAdminRoleId], null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var secondId = (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        using var second = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, password))).StatusCode);
        var attempts = await Task.WhenAll(first.PostAsync($"/api/admin/users/{secondId}/toggle-status", null), second.PostAsync($"/api/admin/users/{SeedData.BootstrapAdminId}/toggle-status", null));
        Assert.Single(attempts, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(attempts, r => r.StatusCode == HttpStatusCode.Unauthorized);
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection).Options);
        Assert.Equal(1, await db.AppUsers.CountAsync(u => u.IsActive && u.RecordStatus == RecordStatus.Active && u.UserRoles.Any(r => r.Role.Code == "SYSTEM_ADMIN")));
        var active = await db.AppUsers.SingleAsync(u => u.IsActive && u.UserRoles.Any(r => r.Role.Code == "SYSTEM_ADMIN"));
        var surviving = active.Id == secondId ? second : first;
        var detail = await surviving.GetFromJsonAsync<JsonElement>($"/api/office/accounts/{active.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, (await surviving.PostAsJsonAsync($"/api/office/accounts/{active.Id}/toggle-status", new RevisionRequest(detail.GetProperty("revision").GetInt32()))).StatusCode);
    });

    [RbacPostgresFact]
    public Task Upgrade_preserves_existing_identities_without_promoting_ADM_and_enforces_new_constraints() => WithDatabase(async connection =>
    {
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection).Options);
        var migrations = db.Database.GetMigrations().ToList();
        var index = migrations.IndexOf("20261008194507_AddOfficeAccountAuthorityV3"); Assert.True(index > 0);
        await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
        var designation = Guid.NewGuid(); var officer = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Designations\" (\"Id\",\"Code\",\"Name\",\"DisplayOrder\",\"IsActive\",\"CreatedAt\",\"UpdatedAt\",\"RecordStatus\") VALUES ({designation},'ADM','Additional District Magistrate',1,true,now(),now(),'Active')");
        foreach (var id in new[] { SeedData.BootstrapAdminId, officer })
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AppUsers\" (\"Id\",\"Username\",\"NormalizedUsername\",\"DisplayName\",\"PasswordHash\",\"IsActive\",\"DesignationId\",\"CreatedAt\",\"UpdatedAt\",\"RecordStatus\") VALUES ({id},{id.ToString()},{id.ToString()},'Legacy identity','synthetic-hash',true,{(id == officer ? (Guid?)designation : null)},now(),now(),'Active')");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Roles\" (\"Id\",\"Code\",\"Name\",\"IsSystemRole\",\"IsActive\",\"CreatedAt\",\"UpdatedAt\",\"RecordStatus\") VALUES ({SeedData.SystemAdminRoleId},'SYSTEM_ADMIN','System Administrator',true,true,now(),now(),'Active')");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"UserRoles\" (\"Id\",\"UserId\",\"RoleId\",\"AssignedAt\") VALUES ({Guid.NewGuid()},{SeedData.BootstrapAdminId},{SeedData.SystemAdminRoleId},now())");
        await db.Database.MigrateAsync();
        await SeedData.SeedAsync(db);
        db.ChangeTracker.Clear();
        var existing = await db.AppUsers.SingleAsync(u => u.Id == officer);
        Assert.Equal(designation, existing.DesignationId); Assert.Null(existing.CustomDesignation);
        Assert.False(existing.OfficeAccessManaged); Assert.Equal(LandAccessLevel.None, existing.LandAccess);
        Assert.False(existing.CanRegisterInwardDak); Assert.Equal(0, existing.OfficeRevision);
        Assert.False(await db.UserRoles.AnyAsync(r => r.UserId == officer));
        Assert.Empty(await db.OfficeModuleMemberships.ToListAsync());
        Assert.Equal(OfficeAuthority.SYSTEM_ADMIN, await OfficeAuthorityService.GetAsync(db, SeedData.BootstrapAdminId));
        Assert.Equal("synthetic-hash", existing.PasswordHash);
        existing.CustomDesignation = "Both choices";
        var invalid = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(invalid.InnerException).SqlState);
        db.ChangeTracker.Clear();
        db.OfficeModuleMemberships.AddRange(new OfficeModuleMembership { UserId = officer, Module = OfficeModule.Rti }, new OfficeModuleMembership { UserId = officer, Module = OfficeModule.Rti });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        db.ChangeTracker.Clear();
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }, emptySchema: true);
}

// Schema-only template: no users, role grants or seeded operational data are shared between proofs.
public sealed class OfficeV3PostgresSchema : IAsyncLifetime
{
    public string TemplateName { get; private set; } = "";
    private string server = "";
    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("LAC_RBAC_TEST_SERVER");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var cs = new NpgsqlConnectionStringBuilder(configured);
        if (cs.Host != "127.0.0.1" || cs.Port != 55438 || cs.Database != "postgres")
            throw new InvalidOperationException("V3 schema template requires dedicated loopback test server 55438.");
        cs.Pooling = false; server = cs.ConnectionString;
        TemplateName = $"lac_rbac_v3_template_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(server); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{TemplateName}\"", admin)) await create.ExecuteNonQueryAsync();
        cs.Database = TemplateName;
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(cs.ConnectionString).Options);
        await db.Database.MigrateAsync();
        Assert.Empty(await db.AppUsers.ToListAsync());
    }
    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(TemplateName)) return;
        if (!TemplateName.StartsWith("lac_rbac_v3_template_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe template cleanup.");
        await using var admin = new NpgsqlConnection(server); await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{TemplateName}\" WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
    }
}
