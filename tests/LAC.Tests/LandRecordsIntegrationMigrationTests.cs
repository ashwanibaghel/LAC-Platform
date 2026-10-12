using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using QuestPDF.Fluent;
using Xunit;

namespace LAC.Tests;

public sealed class LandRecordsIntegrationMigrationTests
{
    [StaffPostgresFact]
    public async Task Both_Smart_first_and_V2_first_histories_upgrade_without_recreating_sources_or_losing_staged_evidence()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LAC_STAFF_TEST_POSTGRES"));
        if (connection.Host != "127.0.0.1" || connection.Port != 55441 || connection.Database != "lac_staff_workflow_v2_test" || connection.Username != "staff_workflow_test")
            throw new InvalidOperationException("Refusing non-isolated migration test.");
        foreach (var smartFirst in new[] { true, false })
        {
            connection.SearchPath = "staff_v2_integration_upgrade_" + Guid.NewGuid().ToString("N");
            await using (var setup = new NpgsqlConnection(connection.ConnectionString))
            {
                await setup.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE SCHEMA {connection.SearchPath}", setup); await command.ExecuteNonQueryAsync();
            }
            await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>(); var names = db.Database.GetMigrations().ToArray();
            var smart = Array.FindIndex(names, x => x.EndsWith("_AddSmartCoreDocumentIntake"));
            await migrator.MigrateAsync(names[smart - 1]);
            if (smartFirst) await migrator.MigrateAsync(names[smart]);
            else
            {
                // Reproduce the pushed backend's history: both V2 migrations applied,
                // with the earlier Smart migration absent. Execute their original Up
                // operations and record the same history IDs, without inventing tables.
                var assembly = db.GetService<IMigrationsAssembly>();
                foreach (var name in new[] { "20261012111643_LandRecordsStaffWorkflowV2", "20261012114817_AllowSourceIdentifiedNotificationReferences" })
                {
                    var migration = assembly.CreateMigration(assembly.Migrations[name], db.Database.ProviderName!);
                    await db.Database.OpenConnectionAsync();
                    foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, migration.TargetModel))
                    {
                        await using var sql = db.Database.GetDbConnection().CreateCommand(); sql.CommandText = command.CommandText; await sql.ExecuteNonQueryAsync();
                    }
                    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\",\"ProductVersion\") VALUES ({name}, '10.0.0')");
                }
            }
            var now = DateTimeOffset.UtcNow; var district = Guid.NewGuid(); var subdivision = Guid.NewGuid(); var village = Guid.NewGuid(); var document = Guid.NewGuid(); var intake = Guid.NewGuid();
            var source = Path.Combine(Path.GetTempPath(), "lac-integration-migration-" + Guid.NewGuid().ToString("N") + ".pdf");
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            var bytes = QuestPDF.Fluent.Document.Create(d => d.Page(p => p.Content().Text("Synthetic historical source " + document))).GeneratePdf();
            await File.WriteAllBytesAsync(source, bytes);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Districts\" (\"Id\",\"Name\",\"CreatedAt\",\"UpdatedAt\",\"RecordStatus\") VALUES ({district}, 'Synthetic integration district', {now}, {now}, 'Active')");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SubDivisions\" (\"Id\",\"DistrictId\",\"Name\",\"CreatedAt\",\"UpdatedAt\",\"RecordStatus\") VALUES ({subdivision}, {district}, 'Synthetic integration subdivision', {now}, {now}, 'Active')");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Villages\" (\"Id\",\"SubDivisionId\",\"Name\",\"CreatedAt\",\"UpdatedAt\",\"RecordStatus\") VALUES ({village}, {subdivision}, 'Synthetic integration Village', {now}, {now}, 'Active')");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Documents\" (\"Id\",\"DocumentType\",\"OriginalFileName\",\"StoragePath\",\"Sha256Hash\",\"Version\",\"Status\",\"UploadedAt\",\"CreatedAt\",\"UpdatedAt\",\"RecordStatus\") VALUES ({document}, 'Unclassified Core PDF', 'historical-source.pdf', {source}, {hash}, 1, 'Staged', {now}, {now}, {now}, 'Active')");
            const string proposal = "{\"historicalProposal\":true}"; const string pages = "[{\"pageNumber\":1,\"text\":\"Preserved historical extraction\"}]";
            if (smartFirst)
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"CoreDocumentIntakes\" (\"Id\",\"VillageId\",\"DocumentId\",\"Sha256Hash\",\"Status\",\"ProposalJson\",\"SourcePagesJson\",\"ClassifierVersion\",\"CreatedAt\",\"CreatedBy\",\"Revision\") VALUES ({intake}, {village}, {document}, {hash}, 'NeedsConfirmation', {proposal}, {pages}, 'historical-native', {now}, 'historical-officer', 7)");
            await migrator.MigrateAsync();
            Assert.Equal(names.Length, (await db.Database.GetAppliedMigrationsAsync()).Count());
            var preserved = await db.Documents.AsNoTracking().SingleAsync(); Assert.Equal(document, preserved.Id); Assert.Equal(hash, preserved.Sha256Hash); Assert.Equal(source, preserved.StoragePath);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(source));
            if (smartFirst)
            {
                var staged = await db.CoreDocumentIntakes.AsNoTracking().SingleAsync();
                Assert.Equal(intake, staged.Id); Assert.Equal(7, staged.Revision); Assert.Equal(proposal, staged.ProposalJson); Assert.Equal(pages, staged.SourcePagesJson); Assert.Equal("historical-officer", staged.CreatedBy);
            }
            else Assert.Empty(await db.CoreDocumentIntakes.ToListAsync());
            Assert.Empty(await db.Awards.ToListAsync()); Assert.Empty(await db.DocumentAwards.ToListAsync());
            // The new nonunique document index permits independent Village proposals,
            // while the unchanged Village/hash key remains unique.
            var secondVillage = new Village { Name = "Synthetic second integration Village", SubDivisionId = subdivision };
            db.Add(secondVillage); await db.SaveChangesAsync();
            if (!smartFirst) db.Add(new CoreDocumentIntake { VillageId = village, DocumentId = document, Sha256Hash = hash });
            db.Add(new CoreDocumentIntake { Village = secondVillage, DocumentId = document, Sha256Hash = hash }); await db.SaveChangesAsync();
            Assert.Equal(2, await db.CoreDocumentIntakes.CountAsync());
            await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261012114817_AllowSourceIdentifiedNotificationReferences"));
            Assert.Equal(2, await db.CoreDocumentIntakes.CountAsync()); Assert.Equal(bytes, await File.ReadAllBytesAsync(source));
            File.Delete(source);
        }
    }
}
