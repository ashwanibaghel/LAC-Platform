using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace LAC.Tests;

public sealed class SeedNotingE2ETests
{
    private sealed class EmptyServices : IServiceProvider { public object? GetService(Type type) => null; }

    [Fact]
    public async Task Seed_Matter_Noting_E2E_Data()
    {
        var connectionString = Environment.GetEnvironmentVariable("LAC_E2E_CONNECTION")
            ?? "Host=127.0.0.1;Port=55440;Database=lac_noting_e2e;Username=postgres;Pooling=false";
        var docDir = Environment.GetEnvironmentVariable("LAC_E2E_DOC_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "lac-e2e-docs");

        Directory.CreateDirectory(docDir);

        // Ensure database exists
        var adminConn = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        var targetDb = new NpgsqlConnectionStringBuilder(connectionString).Database ?? "lac_noting_e2e";

        await using (var admin = new NpgsqlConnection(adminConn))
        {
            await admin.OpenAsync();
            await using var checkCmd = new NpgsqlCommand($"SELECT 1 FROM pg_database WHERE datname = '{targetDb}'", admin);
            var exists = await checkCmd.ExecuteScalarAsync();
            if (exists is null)
            {
                await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{targetDb}\"", admin);
                await createCmd.ExecuteNonQueryAsync();
            }
        }

        var options = new DbContextOptionsBuilder<LacDbContext>()
            .UseNpgsql(connectionString, o => o.EnableRetryOnFailure(2))
            .Options;

        await using var db = new LacDbContext(options);
        await db.Database.MigrateAsync();

        // 1. Seed Identity & Foundation
        await SeedData.SeedAsync(db, null, null, CancellationToken.None);

        // 2. Ensure Permissions catalog & SYSTEM_ADMIN role has ScopeMode.All
        var adminRole = await db.Roles.SingleAsync(r => r.Code == "SYSTEM_ADMIN");
        var allPerms = await db.Permissions.ToListAsync();
        var existingRolePerms = await db.RolePermissions.Where(rp => rp.RoleId == adminRole.Id).ToListAsync();
        foreach (var p in allPerms)
        {
            var rp = existingRolePerms.FirstOrDefault(x => x.PermissionId == p.Id);
            if (rp is null)
            {
                db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = p.Id, ScopeMode = ScopeMode.All });
            }
            else if (rp.ScopeMode != ScopeMode.All)
            {
                rp.ScopeMode = ScopeMode.All;
            }
        }
        await db.SaveChangesAsync();

        // 3. Officers & Desks
        var hasher = new PasswordHasher<AppUser>();
        const string defaultPass = "Password@123!";

        // Officer 1: Kanungo
        var kanungo = await db.AppUsers.Include(u => u.UserRoles).SingleOrDefaultAsync(u => u.NormalizedUsername == "KANUNGO");
        if (kanungo is null)
        {
            kanungo = new AppUser
            {
                Username = "kanungo",
                NormalizedUsername = "KANUNGO",
                DisplayName = "Kanungo (Officer 1)",
                IsActive = true,
                RecordStatus = RecordStatus.Active,
                MustChangePassword = false,
                SessionVersion = Guid.NewGuid(),
                PasswordChangedAt = DateTimeOffset.UtcNow
            };
            kanungo.PasswordHash = hasher.HashPassword(kanungo, defaultPass);
            db.AppUsers.Add(kanungo);
            await db.SaveChangesAsync();
            db.UserRoles.Add(new UserRole { UserId = kanungo.Id, RoleId = adminRole.Id, AssignedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        else
        {
            kanungo.PasswordHash = hasher.HashPassword(kanungo, defaultPass);
            kanungo.MustChangePassword = false;
            kanungo.IsActive = true;
            kanungo.RecordStatus = RecordStatus.Active;
            await db.SaveChangesAsync();
        }

        // Officer 2: Tehsildar
        var tehsildar = await db.AppUsers.Include(u => u.UserRoles).SingleOrDefaultAsync(u => u.NormalizedUsername == "TEHSILDAR");
        if (tehsildar is null)
        {
            tehsildar = new AppUser
            {
                Username = "tehsildar",
                NormalizedUsername = "TEHSILDAR",
                DisplayName = "Tehsildar (Officer 2)",
                IsActive = true,
                RecordStatus = RecordStatus.Active,
                MustChangePassword = false,
                SessionVersion = Guid.NewGuid(),
                PasswordChangedAt = DateTimeOffset.UtcNow
            };
            tehsildar.PasswordHash = hasher.HashPassword(tehsildar, defaultPass);
            db.AppUsers.Add(tehsildar);
            await db.SaveChangesAsync();
            db.UserRoles.Add(new UserRole { UserId = tehsildar.Id, RoleId = adminRole.Id, AssignedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        else
        {
            tehsildar.PasswordHash = hasher.HashPassword(tehsildar, defaultPass);
            tehsildar.MustChangePassword = false;
            tehsildar.IsActive = true;
            tehsildar.RecordStatus = RecordStatus.Active;
            await db.SaveChangesAsync();
        }

        // Desks
        var kanungoDesk = await db.OfficeDesks.SingleOrDefaultAsync(d => d.Code == "KANUNGO_DESK");
        if (kanungoDesk is null)
        {
            kanungoDesk = new OfficeDesk { Code = "KANUNGO_DESK", Name = "Kanungo Desk", Purpose = OfficeDeskPurpose.General };
            db.OfficeDesks.Add(kanungoDesk);
            await db.SaveChangesAsync();
        }
        if (!await db.UserDeskMemberships.AnyAsync(m => m.UserId == kanungo.Id && m.OfficeDeskId == kanungoDesk.Id))
        {
            db.UserDeskMemberships.Add(new UserDeskMembership { UserId = kanungo.Id, OfficeDeskId = kanungoDesk.Id });
            await db.SaveChangesAsync();
        }

        var tehsildarDesk = await db.OfficeDesks.SingleOrDefaultAsync(d => d.Code == "TEHSILDAR_DESK");
        if (tehsildarDesk is null)
        {
            tehsildarDesk = new OfficeDesk { Code = "TEHSILDAR_DESK", Name = "Tehsildar Desk", Purpose = OfficeDeskPurpose.General };
            db.OfficeDesks.Add(tehsildarDesk);
            await db.SaveChangesAsync();
        }
        if (!await db.UserDeskMemberships.AnyAsync(m => m.UserId == tehsildar.Id && m.OfficeDeskId == tehsildarDesk.Id))
        {
            db.UserDeskMemberships.Add(new UserDeskMembership { UserId = tehsildar.Id, OfficeDeskId = tehsildarDesk.Id });
            await db.SaveChangesAsync();
        }

        // Workstream & Allocations
        var dakStream = await db.Workstreams.SingleAsync(w => w.Code == "DAK_CORRESPONDENCE");
        if (!await db.UserWorkstreamMemberships.AnyAsync(m => m.UserId == kanungo.Id && m.WorkstreamId == dakStream.Id))
            db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership { UserId = kanungo.Id, WorkstreamId = dakStream.Id, IsPrimary = true, IsActive = true, AssignedAt = DateTimeOffset.UtcNow });
        if (!await db.UserWorkstreamMemberships.AnyAsync(m => m.UserId == tehsildar.Id && m.WorkstreamId == dakStream.Id))
            db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership { UserId = tehsildar.Id, WorkstreamId = dakStream.Id, IsPrimary = true, IsActive = true, AssignedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await TestWorkAllocations.GrantGlobalAsync(db, kanungo.Id);
        await TestWorkAllocations.GrantGlobalAsync(db, tehsildar.Id);

        // 4. Village Galib Pur
        var village = await db.Villages.FirstOrDefaultAsync(v => v.Name == "GALIB PUR");
        if (village is null)
        {
            var district = new District { Name = "South West Delhi" };
            var sub = new SubDivision { District = district, Name = "Matiala" };
            village = new Village { SubDivision = sub, Name = "GALIB PUR" };
            db.Villages.Add(village);
            await db.SaveChangesAsync();
        }

        // 5. Register Dak and Move to Kanungo
        var dakWorkflow = new DakWorkflowService(db, new TestInMemoryDocumentStorage());
        var villageAuth = new CourtAuthorizationService(db, new WorkItemAuthorizationService(db), new MatterAuthorizationService(db), new EmptyServices());
        var notingWorkflow = new MatterNotingWorkflow(db, new MatterAuthorizationService(db), dakWorkflow, new TestInMemoryDocumentStorage(), villageAuth);

        var existingDak = await db.Daks.SingleOrDefaultAsync(d => d.Subject == "Galib Pur Corridor - Site Inspection & Compensation Proposals");
        Guid dakId;
        Guid matterId;

        if (existingDak is null)
        {
            var regCmd = new RegisterDakCommand(
                DiaryNumber: $"DAK/2026/{Random.Shared.Next(1000, 9999)}",
                ReceivedDate: DateOnly.FromDateTime(DateTime.UtcNow),
                Subject: "Galib Pur Corridor - Site Inspection & Compensation Proposals",
                SenderName: "Executive Engineer, PWD",
                SenderDesignation: "EE (Civil)",
                SenderDepartment: "PWD Delhi",
                SenderAddress: "ITO, New Delhi",
                SenderReferenceNumber: "PWD/2026/L-101",
                SenderLetterDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
                InwardMode: "Physical",
                Priority: DakPriority.Routine,
                DueDate: null,
                CategoryId: null,
                WorkstreamId: dakStream.Id,
                DocumentStream: null,
                DocumentFileName: null,
                DocumentContentType: null
            );

            var reg = await dakWorkflow.RegisterAsync(regCmd, kanungo.Id);
            dakId = reg.Id;

            // Mark/Move to Kanungo Desk
            var sendCmd = new SendDakCommand(
                DakMovementAction.Marked,
                kanungoDesk.Id,
                kanungo.Id,
                DakDestinationKind.Officer,
                false,
                "For verification and noting",
                "Action requested",
                0,
                Guid.NewGuid()
            );
            var sent = await dakWorkflow.SendAsync(dakId, sendCmd, kanungo.Id);
            await dakWorkflow.ReceiveAsync(dakId, sent.TransferId!.Value, new ReceiveDakCommand(1, Guid.NewGuid(), false), kanungo.Id);

            // Classify Dak as VillageSpecific -> Galib Pur
            await notingWorkflow.ClassifyAsync(dakId, new VillageClassificationCommand("VillageSpecific", [village.Id], 2), kanungo.Id, Guid.NewGuid(), default);

            // Create Workspace & Matter
            var wsCmd = new MatterWorkspaceCommand(village.Id, dakStream.Id, null, "Galib Pur Corridor Land Inspection & Valuation", "LandAcquisition", 3);
            var wsRes = await notingWorkflow.WorkspaceAsync(dakId, wsCmd, kanungo.Id, Guid.NewGuid(), default);
            matterId = wsRes.MatterId;
        }
        else
        {
            dakId = existingDak.Id;
            var link = await db.DakMatterLinks.FirstAsync(x => x.DakId == dakId);
            matterId = link.MatterId;
        }

        // 6. Test PDF Document
        var doc = await db.Documents.SingleOrDefaultAsync(d => d.OriginalFileName == "evidence-revenue-map.pdf");
        if (doc is null)
        {
            var pdfBuilder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
            var page = pdfBuilder.AddPage(600, 800);
            var bytes = pdfBuilder.Build();

            var storageFileName = $"{Guid.NewGuid():N}-evidence-revenue-map.pdf";
            var filePath = Path.Combine(docDir, storageFileName);
            await File.WriteAllBytesAsync(filePath, bytes);

            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            doc = new Document
            {
                OriginalFileName = "evidence-revenue-map.pdf",
                StoragePath = storageFileName,
                MimeType = "application/pdf",
                FileSize = bytes.Length,
                Sha256Hash = hash,
                Status = "Active"
            };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();

            db.MatterDocuments.Add(new MatterDocument
            {
                MatterId = matterId,
                DocumentId = doc.Id,
                DocumentRole = "Evidence",
                DisplayName = "Revenue Survey Map Page 1"
            });
            await db.SaveChangesAsync();
        }

        // 7. Output Seed Metadata JSON
        var repoRootEnv = Environment.GetEnvironmentVariable("LAC_REPO_ROOT");
        DirectoryInfo? cur = new DirectoryInfo(AppContext.BaseDirectory);
        while (cur != null && !File.Exists(Path.Combine(cur.FullName, "LAC.sln"))) cur = cur.Parent;
        var effectiveRoot = repoRootEnv ?? cur?.FullName ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
        var fixtureDir = Path.Combine(effectiveRoot, "tests", "fixtures");
        if (!Directory.Exists(fixtureDir)) Directory.CreateDirectory(fixtureDir);

        var seedJsonPath = Path.Combine(fixtureDir, "noting-e2e-seed.json");
        var seedData = new
        {
            apiUrl = "http://127.0.0.1:5399",
            officer1 = new
            {
                username = "kanungo",
                password = defaultPass,
                displayName = "Kanungo (Officer 1)",
                deskName = "Kanungo Desk",
                deskId = kanungoDesk.Id
            },
            officer2 = new
            {
                username = "tehsildar",
                password = defaultPass,
                displayName = "Tehsildar (Officer 2)",
                deskName = "Tehsildar Desk",
                deskId = tehsildarDesk.Id
            },
            matterId = matterId,
            dakId = dakId,
            documentId = doc.Id,
            villageId = village.Id,
            villageName = village.Name
        };

        await File.WriteAllTextAsync(seedJsonPath, JsonSerializer.Serialize(seedData, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(File.Exists(seedJsonPath));
    }
}
