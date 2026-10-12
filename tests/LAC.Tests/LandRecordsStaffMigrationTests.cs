using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace LAC.Tests;

public sealed class LandRecordsStaffMigrationTests
{
    [StaffPostgresFact]
    public async Task Additive_migration_preserves_legacy_identity_and_hash_and_backfills_only_unambiguous_links()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LAC_STAFF_TEST_POSTGRES") ?? "");
        if (connection.Host != "127.0.0.1" || connection.Port != 55441 || connection.Database != "lac_staff_workflow_v2_test" || connection.Username != "staff_workflow_test") throw new InvalidOperationException("Refusing non-isolated migration test.");
        connection.SearchPath = "staff_v2_migration_" + Guid.NewGuid().ToString("N");
        await using (var setup = new NpgsqlConnection(connection.ConnectionString))
        {
            await setup.OpenAsync(); await using var create = new NpgsqlCommand($"CREATE SCHEMA {connection.SearchPath}", setup); await create.ExecuteNonQueryAsync();
        }
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection.ConnectionString).Options);
        var migrations = db.Database.GetMigrations().ToArray();
        var staffMigration = Array.FindIndex(migrations, x => x.EndsWith("_LandRecordsStaffWorkflowV2", StringComparison.Ordinal));
        Assert.True(staffMigration > 0);
        await db.GetService<IMigrator>().MigrateAsync(migrations[staffMigration - 1]);
        var district = Guid.NewGuid(); var subdivision = Guid.NewGuid(); var village = Guid.NewGuid(); var village2 = Guid.NewGuid();
        var award = Guid.NewGuid(); var multiAward = Guid.NewGuid(); var document = Guid.NewGuid(); var multiDocument = Guid.NewGuid(); var linkId = Guid.NewGuid(); var multiLinkId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow; var hash = new string('a', 64);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Districts\" (\"Id\",\"Name\",\"CreatedAt\",\"UpdatedAt\",\"RecordStatus\") VALUES ({district}, 'Synthetic migration district', {timestamp}, {timestamp}, 'Active')");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SubDivisions\" (\"Id\",\"DistrictId\",\"Name\",\"CreatedAt\",\"UpdatedAt\",\"RecordStatus\") VALUES ({subdivision}, {district}, 'Synthetic subdivision', {timestamp}, {timestamp}, 'Active')");
        foreach (var id in new[] { village, village2 }) await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Villages\" (\"Id\",\"SubDivisionId\",\"Name\",\"CreatedAt\",\"UpdatedAt\",\"RecordStatus\") VALUES ({id}, {subdivision}, {id.ToString()}, {timestamp}, {timestamp}, 'Active')");
        foreach (var id in new[] { award, multiAward }) await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Awards\" (\"Id\",\"AwardNumber\",\"Status\",\"CreatedAt\",\"UpdatedAt\",\"RecordStatus\") VALUES ({id}, 'SYN-MIGRATION', 'Draft', {timestamp}, {timestamp}, 'Active')");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AwardVillages\" (\"Id\",\"AwardId\",\"VillageId\") VALUES ({Guid.NewGuid()}, {award}, {village}), ({Guid.NewGuid()}, {multiAward}, {village}), ({Guid.NewGuid()}, {multiAward}, {village2})");
        foreach (var id in new[] { document, multiDocument }) await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Documents\" (\"Id\",\"DocumentType\",\"OriginalFileName\",\"StoragePath\",\"Sha256Hash\",\"Version\",\"Status\",\"UploadedAt\",\"CreatedAt\",\"UpdatedAt\",\"RecordStatus\") VALUES ({id}, 'Award PDF', 'synthetic-legacy.pdf', 'synthetic-legacy.pdf', {hash}, 1, 'Active', {timestamp}, {timestamp}, {timestamp}, 'Active')");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"DocumentAwards\" (\"Id\",\"AwardId\",\"DocumentId\") VALUES ({linkId}, {award}, {document}), ({multiLinkId}, {multiAward}, {multiDocument})");
        await db.GetService<IMigrator>().MigrateAsync();
        var links = await db.DocumentAwards.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var single = links.Single(x => x.Id == linkId); var multi = links.Single(x => x.Id == multiLinkId);
        Assert.Equal(village, single.VillageId); Assert.Null(multi.VillageId); Assert.Equal("Award", single.CoreDocumentRole); Assert.Equal("Award", multi.CoreDocumentRole);
        Assert.Null(single.LegalDate); Assert.Null(single.PossessionEventId); Assert.False(single.IsDetached); Assert.False(single.IsSharedSource);
        Assert.Equal(hash, await db.Documents.Where(x => x.Id == document).Select(x => x.Sha256Hash).SingleAsync());
        Assert.Equal(2, await db.Documents.CountAsync()); Assert.Equal(2, await db.Awards.CountAsync());
        Assert.Equal(0, await db.PossessionEvents.CountAsync()); Assert.Equal(3, await db.AuditLogs.CountAsync(x => x.ChangedBy == "Migration:LandRecordsStaffWorkflowV2"));
        Assert.Equal(1, await db.DocumentVillages.CountAsync());
    }
}
