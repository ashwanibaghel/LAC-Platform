namespace LAC.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
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

public sealed class Phase2ITestFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"phase2i-tests-{Guid.NewGuid()}";
    public const string TestAdminUser = "phase2i_admin";
    public const string TestAdminPass = "Phase2IAdminPass!123";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = TestAdminUser,
                ["BootstrapAdmin:Password"] = TestAdminPass,
                ["BootstrapAdmin:DisplayName"] = "Phase2I Test Administrator"
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

public sealed class Phase2ITests : IClassFixture<Phase2ITestFactory>
{
    private readonly Phase2ITestFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Phase2ITests(Phase2ITestFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Phase2ITestFactory.TestAdminUser, Phase2ITestFactory.TestAdminPass));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return client;
    }

    private async Task<(HttpClient Client, AppUser User)> CreateUserWithPermissionsAsync(
        string username,
        string password,
        IEnumerable<(string Code, ScopeMode Scope)> permissions,
        Guid? workstreamId = null,
        Guid? deskId = null,
        bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var hasher = new PasswordHasher<AppUser>();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = $"User {username}",
            IsActive = isActive,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            PasswordChangedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.AppUsers.Add(user);

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Code = $"ROLE_{username.ToUpperInvariant()}",
            Name = $"Role for {username}",
            IsActive = true,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Roles.Add(role);

        db.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            AssignedAt = DateTimeOffset.UtcNow
        });

        foreach (var (code, scopeMode) in permissions)
        {
            var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
            if (perm == null)
            {
                perm = new Permission
                {
                    Id = Guid.NewGuid(),
                    Code = code,
                    Name = code,
                    Category = "Court"
                };
                db.Permissions.Add(perm);
            }

            db.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = perm.Id,
                ScopeMode = scopeMode
            });
        }

        if (workstreamId.HasValue)
        {
            db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership
            {
                UserId = user.Id,
                WorkstreamId = workstreamId.Value,
                IsActive = true,
                AssignedAt = DateTimeOffset.UtcNow
            });
        }

        if (deskId.HasValue)
        {
            db.UserDeskMemberships.Add(new UserDeskMembership
            {
                UserId = user.Id,
                OfficeDeskId = deskId.Value,
                IsActive = true,
                AssignedAt = DateTimeOffset.UtcNow,
                RecordStatus = RecordStatus.Active
            });
        }

        await db.SaveChangesAsync();

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        return (client, user);
    }

    private async Task<(Village Village, Award Award, Khasra Khasra)> SeedVillageAwardKhasraAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var district = new District { Name = $"District_{Guid.NewGuid():N}" };
        db.Districts.Add(district);

        var subDivision = new SubDivision { Name = $"SubDiv_{Guid.NewGuid():N}", District = district };
        db.SubDivisions.Add(subDivision);

        var village = new Village { Name = $"Village_{Guid.NewGuid():N}", SubDivision = subDivision };
        db.Villages.Add(village);

        var award = new Award
        {
            AwardNumber = $"AW_{Guid.NewGuid():N}",
            AwardDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            AwardType = "Regular",
            Status = "Announced"
        };
        db.Awards.Add(award);

        var khasra = new Khasra
        {
            Village = village,
            DisplayNumber = "100/1",
            NormalizedNumber = "100/1",
            TotalArea = 5.5m,
            AreaUnit = "Bigha"
        };
        db.Khasras.Add(khasra);

        db.Add(new AwardVillage { Award = award, Village = village });
        db.Add(new AwardKhasra { Award = award, Khasra = khasra, AcquiredArea = 5.5m });

        await db.SaveChangesAsync();
        return (village, award, khasra);
    }

    // =========================================================================
    // 1. RBAC & PERMISSION SUITE
    // =========================================================================

    [Fact]
    public void PermissionCodes_CourtPermissions_RegisteredInAll()
    {
        var allCodes = PermissionCodes.All.Select(p => p.Code).ToList();
        Assert.Contains(PermissionCodes.CourtView, allCodes);
        Assert.Contains(PermissionCodes.CourtCreate, allCodes);
        Assert.Contains(PermissionCodes.CourtEdit, allCodes);
        Assert.Contains(PermissionCodes.CourtAssign, allCodes);
        Assert.Contains(PermissionCodes.CourtProceedingManage, allCodes);
        Assert.Contains(PermissionCodes.CourtDocumentManage, allCodes);
    }

    [Fact]
    public async Task SeedData_MigrateCourtPermissions_IdempotentAndNoAssignAutoGrant()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var role = new Role { Code = $"ROLE_LEGACY_{Guid.NewGuid():N}", Name = "Legacy Role" };
        db.Roles.Add(role);

        var permAwardView = await db.Permissions.FirstAsync(p => p.Code == PermissionCodes.AwardView);
        var permAwardEdit = await db.Permissions.FirstAsync(p => p.Code == PermissionCodes.AwardEdit);

        db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permAwardView.Id, ScopeMode = ScopeMode.Workstream });
        db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permAwardEdit.Id, ScopeMode = ScopeMode.Workstream });
        await db.SaveChangesAsync();

        // Run migration
        await SeedData.MigrateCourtPermissionsAsync(db);

        // Verify Court.View, Court.Edit, Court.Proceeding.Manage, Court.Document.Manage migrated
        var migratedPerms = await (
            from rp in db.RolePermissions
            join p in db.Permissions on rp.PermissionId equals p.Id
            where rp.RoleId == role.Id
            select p.Code
        ).ToListAsync();

        Assert.Contains(PermissionCodes.CourtView, migratedPerms);
        Assert.Contains(PermissionCodes.CourtCreate, migratedPerms);
        Assert.Contains(PermissionCodes.CourtEdit, migratedPerms);
        Assert.Contains(PermissionCodes.CourtProceedingManage, migratedPerms);
        Assert.Contains(PermissionCodes.CourtDocumentManage, migratedPerms);

        // CRITICAL INVARIANT: Court.Assign MUST NOT be automatically granted from Award.Edit
        Assert.DoesNotContain(PermissionCodes.CourtAssign, migratedPerms);

        // Run second time to ensure idempotency
        await SeedData.MigrateCourtPermissionsAsync(db);
    }

    // =========================================================================
    // 2. STANDALONE CASE CREATION & CONCURRENCY
    // =========================================================================

    [Fact]
    public async Task CreateCourtCase_InitializesRevision1_AndAppendsEventSequence1()
    {
        var admin = await CreateAdminClientAsync();
        var (_, award, khasra) = await SeedVillageAwardKhasraAsync();

        var cmd = new
        {
            caseNumber = $"WP_{Guid.NewGuid():N}",
            courtName = "High Court of Delhi",
            caseTitle = "Test Petitioner v. LAC",
            caseType = "Writ Petition",
            currentStatus = "Pending",
            filedDate = "2026-01-15",
            awardIds = new[] { award.Id },
            khasraIds = new[] { khasra.Id },
            remarks = "Initial filing"
        };

        var res = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var created = await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var caseId = created.GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var courtCase = await db.CourtCases
            .Include(c => c.Awards)
            .Include(c => c.Khasras)
            .Include(c => c.Events)
            .FirstAsync(c => c.Id == caseId);

        Assert.Equal(1, courtCase.Revision);
        Assert.Single(courtCase.Awards);
        Assert.Single(courtCase.Khasras);

        var firstEvent = Assert.Single(courtCase.Events);
        Assert.Equal(1, firstEvent.SequenceNumber);
        Assert.Equal(CourtCaseAction.Created, firstEvent.Action);
    }

    [Fact]
    public async Task UpdateMetadata_ConcurrencyTokenMismatch_Returns409Conflict()
    {
        var admin = await CreateAdminClientAsync();
        var cmd = new { caseNumber = $"WP_REV_{Guid.NewGuid():N}", courtName = "Supreme Court" };
        var res = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        var caseId = (await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // Pass WRONG revision (e.g. 99 instead of 1)
        var updateCmd = new
        {
            caseTitle = "Updated Title",
            expectedRevision = 99
        };

        var updateRes = await admin.PutAsJsonAsync($"/api/court-cases/{caseId}", updateCmd);
        Assert.Equal(HttpStatusCode.Conflict, updateRes.StatusCode);

        // Pass CORRECT revision (1)
        var updateValid = new
        {
            caseTitle = "Updated Title",
            expectedRevision = 1
        };
        var validRes = await admin.PutAsJsonAsync($"/api/court-cases/{caseId}", updateValid);
        Assert.Equal(HttpStatusCode.OK, validRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var courtCase = await db.CourtCases.Include(c => c.Events).FirstAsync(c => c.Id == caseId);
        Assert.Equal(2, courtCase.Revision);
        Assert.Equal(2, courtCase.Events.Count);
    }

    [Fact]
    public async Task ReassignCase_RequiresCourtAssign_AndChecksRevision()
    {
        var admin = await CreateAdminClientAsync();
        var ws = new Workstream { Code = $"WS_COURT_{Guid.NewGuid():N}", Name = "Court WS" };
        var desk = new OfficeDesk { Workstream = ws, Name = "Legal Cell Desk" };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Workstreams.Add(ws);
            db.OfficeDesks.Add(desk);
            await db.SaveChangesAsync();
        }

        var cmd = new { caseNumber = $"WP_ASSIGN_{Guid.NewGuid():N}", courtName = "High Court" };
        var res = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        var caseId = (await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // User with Court.View only (CANNOT assign)
        var (viewerClient, _) = await CreateUserWithPermissionsAsync($"usr_view_{Guid.NewGuid():N}", "Pass123!", new[] { (PermissionCodes.CourtView, ScopeMode.All) });
        var forbidRes = await viewerClient.PostAsJsonAsync($"/api/court-cases/{caseId}/reassign", new { targetResponsibleDeskId = desk.Id, expectedRevision = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, forbidRes.StatusCode);

        // User with Court.Assign
        var (assignerClient, targetUser) = await CreateUserWithPermissionsAsync($"usr_assign_{Guid.NewGuid():N}", "Pass123!", new[] { (PermissionCodes.CourtView, ScopeMode.All), (PermissionCodes.CourtAssign, ScopeMode.All) });
        var assignRes = await assignerClient.PostAsJsonAsync($"/api/court-cases/{caseId}/reassign", new
        {
            targetResponsibleDeskId = desk.Id,
            targetAssignedUserId = targetUser.Id,
            reassignmentNotes = "Assigned for hearing preparation",
            expectedRevision = 1
        });
        Assert.Equal(HttpStatusCode.OK, assignRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var c = await db.CourtCases.FirstAsync(x => x.Id == caseId);
            Assert.Equal(desk.Id, c.ResponsibleOfficeDeskId);
            Assert.Equal(targetUser.Id, c.AssignedUserId);
            Assert.Equal(2, c.Revision);
        }
    }

    // =========================================================================
    // 3. IMMUTABILITY OF COURT CASE EVENTS
    // =========================================================================

    [Fact]
    public async Task CourtCaseEvent_EFChangeTracker_ThrowsOnUpdateOrDelete()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var cCase = new CourtCase { CaseNumber = $"WP_IMM_{Guid.NewGuid():N}", CourtName = "High Court", Revision = 1 };
        db.CourtCases.Add(cCase);

        var evt = new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCase = cCase,
            SequenceNumber = 1,
            Action = CourtCaseAction.Created,
            ActionAt = DateTimeOffset.UtcNow,
            CaseNumberSnapshot = cCase.CaseNumber,
            ActorDisplayNameSnapshot = "Admin"
        };
        db.CourtCaseEvents.Add(evt);
        await db.SaveChangesAsync();

        // Attempt to modify
        evt.CaseTitleSnapshot = "Tampered title";
        var exUpdate = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("immutable", exUpdate.Message, StringComparison.OrdinalIgnoreCase);

        // Detach and attempt delete
        db.Entry(evt).State = EntityState.Detached;
        var trackedEvt = await db.CourtCaseEvents.FirstAsync(e => e.Id == evt.Id);
        db.CourtCaseEvents.Remove(trackedEvt);
        var exDelete = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("immutable", exDelete.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 4. NDOH CHRONOLOGY & HISTORICAL BACKFILL INVARIANT
    // =========================================================================

    [Fact]
    public async Task RecordProceeding_AuthoritativeNdoh_ChronologyAndBackfillSafety()
    {
        var admin = await CreateAdminClientAsync();
        var cmd = new { caseNumber = $"WP_NDOH_{Guid.NewGuid():N}", courtName = "High Court" };
        var res = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        var caseId = (await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // 1. Record proceeding for 2026-05-10 with NDOH 2026-06-15
        var proc1 = new
        {
            proceedingDate = "2026-05-10",
            orderType = "Interim Order",
            restraintNature = "Stay on Dispossession",
            summary = "Interim stay granted until next hearing",
            nextDate = "2026-06-15"
        };
        var p1Res = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/proceedings", proc1);
        Assert.Equal(HttpStatusCode.Created, p1Res.StatusCode);

        // Verify active ScheduledEvent was created
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var sched = await db.ScheduledEvents.FirstOrDefaultAsync(s => s.CourtCaseId == caseId && s.Status == ScheduledEventStatus.Scheduled);
            Assert.NotNull(sched);
            Assert.Equal(DateOnly.Parse("2026-06-15"), sched.ScheduledDate);
            Assert.Equal(ScheduledEventKind.CourtHearing, sched.EventKind);
        }

        // 2. Backfill an OLDER proceeding: 2026-03-01 with NDOH 2026-04-10
        // INVARIANT: Historical backfill must NOT overwrite the authoritative NDOH from 2026-05-10!
        var procHistorical = new
        {
            proceedingDate = "2026-03-01",
            orderType = "Notice",
            summary = "Notice issued to respondents",
            nextDate = "2026-04-10"
        };
        var pHistRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/proceedings", procHistorical);
        Assert.Equal(HttpStatusCode.Created, pHistRes.StatusCode);

        // Verify active ScheduledEvent STILL points to 2026-06-15 (NOT 2026-04-10!)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var sched = await db.ScheduledEvents.FirstOrDefaultAsync(s => s.CourtCaseId == caseId && s.Status == ScheduledEventStatus.Scheduled);
            Assert.NotNull(sched);
            Assert.Equal(DateOnly.Parse("2026-06-15"), sched.ScheduledDate);
        }

        // 3. Record a NEWER proceeding: 2026-06-15 with nextDate 2026-08-20
        // This IS the latest proceeding, so it SHOULD update the authoritative schedule!
        var procNewer = new
        {
            proceedingDate = "2026-06-15",
            orderType = "Adjournment",
            summary = "Arguments concluded, adjourned for orders",
            nextDate = "2026-08-20"
        };
        var pNewerRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/proceedings", procNewer);
        Assert.Equal(HttpStatusCode.Created, pNewerRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var sched = await db.ScheduledEvents.FirstOrDefaultAsync(s => s.CourtCaseId == caseId && s.Status == ScheduledEventStatus.Scheduled);
            Assert.NotNull(sched);
            Assert.Equal(DateOnly.Parse("2026-08-20"), sched.ScheduledDate);
        }
    }

    // =========================================================================
    // 5. CROSS-DOMAIN ANTI-LEAK SECURITY SUITE
    // =========================================================================

    [Fact]
    public async Task CrossDomainAntiLeak_ZeroLeakWhenLackingCourtView()
    {
        var admin = await CreateAdminClientAsync();
        var (village, award, khasra) = await SeedVillageAwardKhasraAsync();

        // Create court case linked to award and khasra
        var cmd = new
        {
            caseNumber = $"WP_LEAK_{Guid.NewGuid():N}",
            courtName = "Delhi High Court",
            caseTitle = "Confidential Litigation",
            awardIds = new[] { award.Id },
            khasraIds = new[] { khasra.Id }
        };
        var cRes = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        Assert.Equal(HttpStatusCode.Created, cRes.StatusCode);

        // 1. User with Khasra.View only (NO Court.View)
        var (khasraClient, _) = await CreateUserWithPermissionsAsync(
            $"usr_khasra_only_{Guid.NewGuid():N}",
            "Pass123!",
            new[] { (PermissionCodes.KhasraView, ScopeMode.All) }
        );

        // Khasra history: court array MUST be empty!
        var khHistoryRes = await khasraClient.GetAsync($"/api/khasras/{khasra.Id}/history");
        Assert.Equal(HttpStatusCode.OK, khHistoryRes.StatusCode);
        var khHistoryJson = await khHistoryRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var courtArr = khHistoryJson.GetProperty("courtCases");
        Assert.Equal(0, courtArr.GetArrayLength());

        // 2. User with Award.View (without CourtReferences workstream and without Court.View)
        Workstream awardWs;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            awardWs = await db.Workstreams.FirstAsync(w => w.Code == WorkstreamCodes.Award);
        }

        var (awardClient, _) = await CreateUserWithPermissionsAsync(
            $"usr_award_only_{Guid.NewGuid():N}",
            "Pass123!",
            new[] { (PermissionCodes.AwardView, ScopeMode.Workstream) },
            workstreamId: awardWs.Id
        );

        // Award workspace: courtCaseCount MUST be 0 and courtStatus MUST be "Not Added"
        var wsRes = await awardClient.GetAsync($"/api/awards/{award.Id}/workspace");
        Assert.Equal(HttpStatusCode.OK, wsRes.StatusCode);
        var wsJson = await wsRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(0, wsJson.GetProperty("courtCaseCount").GetInt32());
        Assert.Equal("Not Added", wsJson.GetProperty("litigationData").GetString());

        // GET /api/awards/{id}/court-cases MUST return 403 Forbidden!
        var awardCourtRes = await awardClient.GetAsync($"/api/awards/{award.Id}/court-cases");
        Assert.Equal(HttpStatusCode.Forbidden, awardCourtRes.StatusCode);

        // 3. User with Village.View only (NO Court.View) in /api/search
        var (searchClient, _) = await CreateUserWithPermissionsAsync(
            $"usr_search_{Guid.NewGuid():N}",
            "Pass123!",
            new[] { (PermissionCodes.VillageView, ScopeMode.All) }
        );
        var searchRes = await searchClient.GetAsync("/api/search?q=Confidential");
        Assert.Equal(HttpStatusCode.OK, searchRes.StatusCode);
        var searchItems = await searchRes.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        Assert.DoesNotContain(searchItems!, item => item.GetProperty("type").GetString() == "Court Case");

        // 4. Now verify that user WITH Court.View CAN see them!
        var (courtUserClient, _) = await CreateUserWithPermissionsAsync(
            $"usr_court_full_{Guid.NewGuid():N}",
            "Pass123!",
            new[]
            {
                (PermissionCodes.KhasraView, ScopeMode.All),
                (PermissionCodes.AwardView, ScopeMode.All),
                (PermissionCodes.VillageView, ScopeMode.All),
                (PermissionCodes.CourtView, ScopeMode.All)
            }
        );

        // Khasra history contains court item
        var khAllowed = await courtUserClient.GetAsync($"/api/khasras/{khasra.Id}/history");
        var khAllowedJson = await khAllowed.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.True(khAllowedJson.GetProperty("courtCases").GetArrayLength() > 0);

        // Award workspace contains court case count
        var awAllowed = await courtUserClient.GetAsync($"/api/awards/{award.Id}/workspace");
        var awAllowedJson = await awAllowed.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        Assert.Equal(1, awAllowedJson.GetProperty("courtCaseCount").GetInt32());
        Assert.Equal("Available", awAllowedJson.GetProperty("litigationData").GetString());

        // GET /api/awards/{id}/court-cases returns 200 OK
        var awCourtAllowed = await courtUserClient.GetAsync($"/api/awards/{award.Id}/court-cases");
        Assert.Equal(HttpStatusCode.OK, awCourtAllowed.StatusCode);

        // Search returns Court Case
        var searchAllowed = await courtUserClient.GetAsync("/api/search?q=Confidential");
        var searchAllowedItems = await searchAllowed.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        Assert.Contains(searchAllowedItems!, item => item.GetProperty("type").GetString() == "Court Case");
    }

    // =========================================================================
    // 6. REWIRED AWARD COURT CASE CREATION
    // =========================================================================

    [Fact]
    public async Task RewiredAwardCourtPost_CreatesCaseAndEvent_Returns201()
    {
        var admin = await CreateAdminClientAsync();
        var (_, award, khasra) = await SeedVillageAwardKhasraAsync();

        var payload = new
        {
            caseNumber = $"AW_POST_{Guid.NewGuid():N}",
            courtName = "Delhi High Court",
            caseType = "Writ",
            currentStatus = "Pending",
            remarks = "From award workspace",
            khasraIds = new[] { khasra.Id }
        };

        var res = await admin.PostAsJsonAsync($"/api/awards/{award.Id}/court-cases", payload);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var created = await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var caseId = created.GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var courtCase = await db.CourtCases.Include(c => c.Awards).Include(c => c.Events).FirstAsync(c => c.Id == caseId);

        Assert.Equal(1, courtCase.Revision);
        Assert.Single(courtCase.Awards);
        Assert.Equal(award.Id, courtCase.Awards.First().AwardId);
        Assert.Equal(CourtCaseAction.Created, courtCase.Events.First().Action);
    }

    // =========================================================================
    // 7. DOCUMENT UPLOAD & STREAMING ACCESS LOGGING
    // =========================================================================

    [Fact]
    public async Task DocumentUploadAndStreaming_LogsAccessWithCourtCaseContext()
    {
        var admin = await CreateAdminClientAsync();
        var cmd = new { caseNumber = $"WP_DOC_{Guid.NewGuid():N}", courtName = "High Court" };
        var res = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        var caseId = (await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // 1. Upload Document
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("dummy pdf content"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "StayOrder.pdf");
        form.Add(new StringContent("Court Order"), "documentRole");
        form.Add(new StringContent("Interim Stay Order"), "displayName");

        var uploadRes = await admin.PostAsync($"/api/court-cases/{caseId}/documents", form);
        Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);

        var uploaded = await uploadRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var documentId = uploaded.GetProperty("documentId").GetGuid();
        var docRelationshipId = uploaded.GetProperty("id").GetGuid();

        // 2. Stream / Download content
        var contentRes = await admin.GetAsync($"/api/court-cases/{caseId}/documents/{documentId}/content?download=true");
        Assert.Equal(HttpStatusCode.OK, contentRes.StatusCode);

        // 3. Verify RecordAccessEvent was logged with ContextEntityType = "CourtCase"
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var accessLog = await db.RecordAccessEvents
                .Where(e => e.ContextEntityType == "CourtCase" && e.ContextEntityId == caseId && e.DocumentId == documentId)
                .FirstOrDefaultAsync();

            Assert.NotNull(accessLog);
            Assert.Equal(RecordAccessAction.Downloaded, accessLog.Action);
        }

        // 4. Unlink document
        var deleteRes = await admin.DeleteAsync($"/api/court-cases/{caseId}/documents/{docRelationshipId}");
        Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var docLink = await db.CourtCaseDocuments.FirstAsync(d => d.Id == docRelationshipId);
            Assert.Equal(RecordStatus.Archived, docLink.RecordStatus);
        }
    }

    // =========================================================================
    // 8. PARTIES & REPRESENTATIVES
    // =========================================================================

    [Fact]
    public async Task PartiesAndRepresentatives_AddUpdateRemove()
    {
        var admin = await CreateAdminClientAsync();
        var cmd = new { caseNumber = $"WP_PR_{Guid.NewGuid():N}", courtName = "High Court" };
        var res = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        var caseId = (await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // Add Party
        var pRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/parties", new
        {
            partyType = "Petitioner",
            partyName = "Suresh Chand",
            advocateName = "Adv. Ramesh",
            isPrimary = true
        });
        Assert.Equal(HttpStatusCode.Created, pRes.StatusCode);
        var partyId = (await pRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // Update Party
        var pUpRes = await admin.PutAsJsonAsync($"/api/court-cases/{caseId}/parties/{partyId}", new
        {
            partyType = "Petitioner",
            partyName = "Suresh Chand & Ors.",
            advocateName = "Adv. Ramesh Senior",
            isPrimary = true
        });
        Assert.Equal(HttpStatusCode.OK, pUpRes.StatusCode);

        // Add Representative
        var rRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/representatives", new
        {
            representativeType = "Standing Counsel",
            name = "Govt Counsel A",
            isLeadCounsel = true
        });
        Assert.Equal(HttpStatusCode.Created, rRes.StatusCode);
        var repId = (await rRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // Remove Representative
        var rDelRes = await admin.DeleteAsync($"/api/court-cases/{caseId}/representatives/{repId}");
        Assert.Equal(HttpStatusCode.OK, rDelRes.StatusCode);

        // Remove Party
        var pDelRes = await admin.DeleteAsync($"/api/court-cases/{caseId}/parties/{partyId}");
        Assert.Equal(HttpStatusCode.OK, pDelRes.StatusCode);
    }
}
