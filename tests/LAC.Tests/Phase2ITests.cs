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
        OfficeDesk desk;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var courtWs = await db.Workstreams.FirstOrDefaultAsync(w => w.Code == WorkstreamCodes.CourtReferences);
            if (courtWs == null)
            {
                courtWs = new Workstream { Code = WorkstreamCodes.CourtReferences, Name = "Court References" };
                db.Workstreams.Add(courtWs);
                await db.SaveChangesAsync();
            }
            desk = new OfficeDesk { WorkstreamId = courtWs.Id, Name = "Legal Cell Desk" };
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

        // User with Court.Assign, who is an active member of target desk
        var (assignerClient, targetUser) = await CreateUserWithPermissionsAsync(
            $"usr_assign_{Guid.NewGuid():N}",
            "Pass123!",
            new[] { (PermissionCodes.CourtView, ScopeMode.All), (PermissionCodes.CourtAssign, ScopeMode.All) },
            deskId: desk.Id);

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
        var p1Json = await p1Res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var p1Id = p1Json.GetProperty("id").GetGuid();

        // INVARIANT (Audit Item 8): Proceeding does NOT automatically generate or mutate operational calendar items behind the caller's back!
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var sched = await db.ScheduledEvents.FirstOrDefaultAsync(s => s.CourtCaseId == caseId && s.Status == ScheduledEventStatus.Scheduled);
            Assert.Null(sched);
        }

        // Explicit promotion creates the first schedule
        var promoteRes = await admin.PostAsJsonAsync($"/api/scheduled-events/from-court-proceeding/{p1Id}", new { title = "Hearing for WP" });
        Assert.Equal(HttpStatusCode.Created, promoteRes.StatusCode);

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

    // =========================================================================
    // 9. AUDIT ITEM 1: ZERO RUNTIME FALLBACK FROM AWARD PERMISSIONS TO COURT
    // =========================================================================

    [Fact]
    public async Task AwardPermissions_NoRuntimeFallbackToCourt_FailsForbidden()
    {
        var admin = await CreateAdminClientAsync();
        var cmd = new { caseNumber = $"WP_NOFALLBACK_{Guid.NewGuid():N}", courtName = "Delhi High Court" };
        var cRes = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        Assert.Equal(HttpStatusCode.Created, cRes.StatusCode);
        var caseId = (await cRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // User with Award.View and Award.Edit ONLY (ZERO Court permissions)
        var (awardUserClient, _) = await CreateUserWithPermissionsAsync(
            $"usr_award_nofallback_{Guid.NewGuid():N}",
            "Pass123!",
            new[]
            {
                (PermissionCodes.AwardView, ScopeMode.All),
                (PermissionCodes.AwardEdit, ScopeMode.All)
            }
        );

        // 1. Cannot list court cases
        var listRes = await awardUserClient.GetAsync("/api/court-cases");
        Assert.Equal(HttpStatusCode.Forbidden, listRes.StatusCode);

        // 2. Cannot get court case detail
        var detailRes = await awardUserClient.GetAsync($"/api/court-cases/{caseId}");
        Assert.Equal(HttpStatusCode.Forbidden, detailRes.StatusCode);

        // 3. Cannot create court case
        var createRes = await awardUserClient.PostAsJsonAsync("/api/court-cases", new { caseNumber = "WP_99", courtName = "HC" });
        Assert.Equal(HttpStatusCode.Forbidden, createRes.StatusCode);

        // 4. Cannot record proceeding
        var procRes = await awardUserClient.PostAsJsonAsync($"/api/court-cases/{caseId}/proceedings", new { proceedingDate = "2026-06-01" });
        Assert.Equal(HttpStatusCode.Forbidden, procRes.StatusCode);

        // 5. Cannot get documents
        var docRes = await awardUserClient.GetAsync($"/api/court-cases/{caseId}/documents");
        Assert.Equal(HttpStatusCode.Forbidden, docRes.StatusCode);
    }

    // =========================================================================
    // 10. AUDIT ITEM 2: SCOPE MODE ENFORCEMENT (OWN FAILS CLOSED, ASSIGNED REQUIRES DESK)
    // =========================================================================

    [Fact]
    public async Task CourtAuthorization_ScopeModeOwn_FailsClosed()
    {
        var admin = await CreateAdminClientAsync();
        var cmd = new { caseNumber = $"WP_OWN_{Guid.NewGuid():N}", courtName = "Delhi High Court" };
        var cRes = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        Assert.Equal(HttpStatusCode.Created, cRes.StatusCode);
        var caseId = (await cRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // User with Court.View and Court.Edit in ScopeMode.Own
        var (ownUserClient, ownUser) = await CreateUserWithPermissionsAsync(
            $"usr_court_own_{Guid.NewGuid():N}",
            "Pass123!",
            new[]
            {
                (PermissionCodes.CourtView, ScopeMode.Own),
                (PermissionCodes.CourtEdit, ScopeMode.Own)
            }
        );

        // ScopeMode.Own must fail closed: empty list
        var listRes = await ownUserClient.GetAsync("/api/court-cases");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var paged = await listRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var items = paged.GetProperty("items").EnumerateArray().ToList();
        Assert.Empty(items);

        // ScopeMode.Own detail access returns 403 Forbidden
        var detailRes = await ownUserClient.GetAsync($"/api/court-cases/{caseId}");
        Assert.Equal(HttpStatusCode.Forbidden, detailRes.StatusCode);
    }

    [Fact]
    public async Task CourtAuthorization_ScopeModeAssigned_StrictDeskMembershipRequired_AssignedUserNeverGrantsAcl()
    {
        var admin = await CreateAdminClientAsync();

        OfficeDesk deskA;
        OfficeDesk deskB;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var courtWs = await db.Workstreams.FirstOrDefaultAsync(w => w.Code == WorkstreamCodes.CourtReferences);
            if (courtWs == null)
            {
                courtWs = new Workstream { Code = WorkstreamCodes.CourtReferences, Name = "Court References" };
                db.Workstreams.Add(courtWs);
                await db.SaveChangesAsync();
            }
            deskA = new OfficeDesk { WorkstreamId = courtWs.Id, Name = $"Desk_A_{Guid.NewGuid():N}" };
            deskB = new OfficeDesk { WorkstreamId = courtWs.Id, Name = $"Desk_B_{Guid.NewGuid():N}" };
            db.OfficeDesks.AddRange(deskA, deskB);
            await db.SaveChangesAsync();
        }

        // Case assigned to Desk A
        var cmd = new
        {
            caseNumber = $"WP_DESK_A_{Guid.NewGuid():N}",
            courtName = "Delhi High Court",
            responsibleOfficeDeskId = deskA.Id
        };
        var cRes = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        Assert.Equal(HttpStatusCode.Created, cRes.StatusCode);
        var caseId = (await cRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // User 1 is member of Desk B (NOT Desk A) with ScopeMode.Assigned
        var (userBClient, userB) = await CreateUserWithPermissionsAsync(
            $"usr_desk_b_{Guid.NewGuid():N}",
            "Pass123!",
            new[] { (PermissionCodes.CourtView, ScopeMode.Assigned) },
            deskId: deskB.Id
        );

        // Assign userB as named handler (AssignedUserId) on the CourtCase in database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var c = await db.CourtCases.FirstAsync(x => x.Id == caseId);
            c.AssignedUserId = userB.Id; // Named routing handler ONLY
            await db.SaveChangesAsync();
        }

        // INVARIANT (Audit Item 2): AssignedUserId is routing metadata and NEVER grants ACL access!
        // userB is not a member of Desk A -> Access MUST be denied!
        var userBDetailRes = await userBClient.GetAsync($"/api/court-cases/{caseId}");
        Assert.Equal(HttpStatusCode.Forbidden, userBDetailRes.StatusCode);

        var userBListRes = await userBClient.GetAsync("/api/court-cases");
        Assert.Equal(HttpStatusCode.OK, userBListRes.StatusCode);
        var userBPaged = await userBListRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var userBItems = userBPaged.GetProperty("items").EnumerateArray().ToList();
        Assert.DoesNotContain(userBItems, x => x.GetProperty("id").GetGuid() == caseId);

        // User 2 is member of Desk A with ScopeMode.Assigned
        var (userAClient, _) = await CreateUserWithPermissionsAsync(
            $"usr_desk_a_{Guid.NewGuid():N}",
            "Pass123!",
            new[] { (PermissionCodes.CourtView, ScopeMode.Assigned) },
            deskId: deskA.Id
        );

        // userA is a member of Desk A -> Access GRANTED!
        var userADetailRes = await userAClient.GetAsync($"/api/court-cases/{caseId}");
        Assert.Equal(HttpStatusCode.OK, userADetailRes.StatusCode);

        var userAListRes = await userAClient.GetAsync("/api/court-cases");
        Assert.Equal(HttpStatusCode.OK, userAListRes.StatusCode);
        var userAPaged = await userAListRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var userAItems = userAPaged.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(userAItems, x => x.GetProperty("id").GetGuid() == caseId);
    }

    // =========================================================================
    // 11. AUDIT ITEMS 9 & 11: DISPOSED DATE >= FILED DATE VALIDATION & CHECK CONSTRAINT
    // =========================================================================

    [Fact]
    public async Task CourtCase_DisposedDateBeforeFiledDate_FailsValidation()
    {
        var admin = await CreateAdminClientAsync();

        // 1. Create with DisposedDate < FiledDate -> 400 Bad Request
        var badCmd = new
        {
            caseNumber = $"WP_DATE_FAIL_{Guid.NewGuid():N}",
            courtName = "Delhi High Court",
            filedDate = "2026-05-10",
            disposedDate = "2026-05-01"
        };
        var badRes = await admin.PostAsJsonAsync("/api/court-cases", badCmd);
        Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);

        // 2. Create valid case
        var validCmd = new
        {
            caseNumber = $"WP_DATE_OK_{Guid.NewGuid():N}",
            courtName = "Delhi High Court",
            filedDate = "2026-05-10",
            disposedDate = "2026-05-10"
        };
        var validRes = await admin.PostAsJsonAsync("/api/court-cases", validCmd);
        Assert.Equal(HttpStatusCode.Created, validRes.StatusCode);
        var caseId = (await validRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // 3. Update with DisposedDate < FiledDate -> 400 Bad Request
        var badUpdate = new
        {
            caseTitle = "Updated Title",
            filedDate = "2026-05-10",
            disposedDate = "2026-04-30",
            expectedRevision = 1
        };
        var badUpRes = await admin.PutAsJsonAsync($"/api/court-cases/{caseId}", badUpdate);
        Assert.Equal(HttpStatusCode.BadRequest, badUpRes.StatusCode);

        // 4. Update with DisposedDate >= FiledDate -> 200 OK
        var goodUpdate = new
        {
            caseTitle = "Updated Title",
            filedDate = "2026-05-10",
            disposedDate = "2026-05-20",
            expectedRevision = 1
        };
        var goodUpRes = await admin.PutAsJsonAsync($"/api/court-cases/{caseId}", goodUpdate);
        Assert.Equal(HttpStatusCode.OK, goodUpRes.StatusCode);
    }

    // =========================================================================
    // 12. AUDIT ITEM 12: PARTY & REPRESENTATIVE VALIDATION
    // =========================================================================

    [Fact]
    public async Task CourtCase_PartyAndRepresentativeValidation_RequiresDisplayNameAndRole()
    {
        var admin = await CreateAdminClientAsync();
        var cmd = new { caseNumber = $"WP_PARTY_VAL_{Guid.NewGuid():N}", courtName = "Delhi High Court" };
        var cRes = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        var caseId = (await cRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // 1. Party with empty name -> 400
        var emptyNameRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/parties", new
        {
            partyType = "Petitioner",
            partyName = "   "
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptyNameRes.StatusCode);

        // 2. Party with empty role -> 400
        var emptyRoleRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/parties", new
        {
            partyType = "   ",
            partyName = "Valid Name"
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptyRoleRes.StatusCode);

        // 3. Representative with empty name -> 400
        var emptyRepRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/representatives", new
        {
            representativeType = "Standing Counsel",
            name = "   "
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptyRepRes.StatusCode);
    }

    // =========================================================================
    // 13. AUDIT ITEM 5: CONCURRENCY OPTIMISTIC LOCKING ON ALL MUTATIONS
    // =========================================================================

    [Fact]
    public async Task CourtCase_Concurrency_OptimisticLocking_ThrowsConflictOnStaleRevision()
    {
        var admin = await CreateAdminClientAsync();
        var (village, award, khasra) = await SeedVillageAwardKhasraAsync();

        var cmd = new { caseNumber = $"WP_CONCUR_{Guid.NewGuid():N}", courtName = "Delhi High Court" };
        var cRes = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        var caseId = (await cRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // Stale revision = 99
        // 1. Update Case
        var upRes = await admin.PutAsJsonAsync($"/api/court-cases/{caseId}", new { caseTitle = "Conflict Test", expectedRevision = 99 });
        Assert.Equal(HttpStatusCode.Conflict, upRes.StatusCode);

        // 2. Record Proceeding
        var procRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/proceedings", new { proceedingDate = "2026-06-01", expectedRevision = 99 });
        Assert.Equal(HttpStatusCode.Conflict, procRes.StatusCode);

        // 3. Link Award
        var awRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/awards", new { awardId = award.Id, expectedRevision = 99 });
        Assert.Equal(HttpStatusCode.Conflict, awRes.StatusCode);

        // 4. Link Khasra
        var khRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/khasras", new { khasraId = khasra.Id, expectedRevision = 99 });
        Assert.Equal(HttpStatusCode.Conflict, khRes.StatusCode);

        // 5. Add Party
        var partyRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/parties", new { partyType = "Petitioner", partyName = "Party A", expectedRevision = 99 });
        Assert.Equal(HttpStatusCode.Conflict, partyRes.StatusCode);

        // 6. Add Representative
        var repRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/representatives", new { representativeType = "Counsel", name = "Counsel A", expectedRevision = 99 });
        Assert.Equal(HttpStatusCode.Conflict, repRes.StatusCode);

        // Successful mutation with matching revision (1) increments revision to 2
        var okUpRes = await admin.PutAsJsonAsync($"/api/court-cases/{caseId}", new { caseTitle = "Updated Title", expectedRevision = 1 });
        Assert.Equal(HttpStatusCode.OK, okUpRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var c = await db.CourtCases.FirstAsync(x => x.Id == caseId);
            Assert.Equal(2, c.Revision);
        }
    }

    // =========================================================================
    // 14. AUDIT ITEMS 7 & 13: PROJECTION ISOLATION & CAPABILITY MATRIX
    // =========================================================================

    [Fact]
    public async Task CourtCase_ProjectionIsolation_PrivacyAndCapabilityMatrix()
    {
        var admin = await CreateAdminClientAsync();
        var (village, award, khasra) = await SeedVillageAwardKhasraAsync();

        // Seed Matter
        Guid matterId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var courtWs = await db.Workstreams.FirstAsync(w => w.Code == WorkstreamCodes.CourtReferences);
            var m = new Matter
            {
                Title = $"Matter_{Guid.NewGuid():N}",
                MatterType = "Litigation",
                WorkstreamId = courtWs.Id
            };
            db.Matters.Add(m);
            await db.SaveChangesAsync();
            matterId = m.Id;
        }

        var cmd = new
        {
            caseNumber = $"WP_ISOLATION_{Guid.NewGuid():N}",
            courtName = "Delhi High Court",
            awardIds = new[] { award.Id },
            khasraIds = new[] { khasra.Id },
            matterIds = new[] { matterId }
        };
        var cRes = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        Assert.Equal(HttpStatusCode.Created, cRes.StatusCode);
        var caseId = (await cRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // User with Court.View ONLY (NO Award.View, NO Khasra.View, NO Matter.View)
        var (courtOnlyClient, _) = await CreateUserWithPermissionsAsync(
            $"usr_court_isolated_{Guid.NewGuid():N}",
            "Pass123!",
            new[] { (PermissionCodes.CourtView, ScopeMode.All) }
        );

        var detailRes = await courtOnlyClient.GetAsync($"/api/court-cases/{caseId}");
        Assert.Equal(HttpStatusCode.OK, detailRes.StatusCode);
        var detail = await detailRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);

        // INVARIANT (Audit Item 7): Cross-domain counts MUST be null when caller lacks respective view permissions!
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("awardsCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("khasrasCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("mattersCount").ValueKind);

        // Linked collections MUST be empty!
        Assert.Empty(detail.GetProperty("awards").EnumerateArray());
        Assert.Empty(detail.GetProperty("khasras").EnumerateArray());

        // Capabilities must truthfully reflect missing permissions
        var caps = detail.GetProperty("capabilities");
        Assert.False(caps.GetProperty("canLinkAward").GetBoolean());
        Assert.False(caps.GetProperty("canLinkKhasra").GetBoolean());
        Assert.False(caps.GetProperty("canLinkMatter").GetBoolean());
        Assert.False(caps.GetProperty("canEdit").GetBoolean());

        // Work tab endpoint must return empty collection without Matter.View
        var workRes = await courtOnlyClient.GetAsync($"/api/court-cases/{caseId}/work");
        Assert.Equal(HttpStatusCode.OK, workRes.StatusCode);
        var workItems = await workRes.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        Assert.Empty(workItems!);
    }

    // =========================================================================
    // 15. AUDIT ITEM 14: FILTER OPTIONS RESTRICTED TO COURT REFERENCES WORKSTREAM
    // =========================================================================

    [Fact]
    public async Task CourtCase_FilterOptions_RestrictedToCourtReferencesDesksAndActiveMembers()
    {
        var admin = await CreateAdminClientAsync();

        OfficeDesk courtDesk;
        OfficeDesk otherDesk;
        AppUser activeCourtMember;
        AppUser inactiveCourtMember;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var courtWs = await db.Workstreams.FirstOrDefaultAsync(w => w.Code == WorkstreamCodes.CourtReferences);
            if (courtWs == null)
            {
                courtWs = new Workstream { Code = WorkstreamCodes.CourtReferences, Name = "Court References" };
                db.Workstreams.Add(courtWs);
                await db.SaveChangesAsync();
            }

            var otherWs = await db.Workstreams.FirstOrDefaultAsync(w => w.Code == WorkstreamCodes.Award);
            courtDesk = new OfficeDesk { WorkstreamId = courtWs.Id, Name = $"CourtDesk_{Guid.NewGuid():N}" };
            otherDesk = new OfficeDesk { WorkstreamId = otherWs!.Id, Name = $"OtherDesk_{Guid.NewGuid():N}" };
            db.OfficeDesks.AddRange(courtDesk, otherDesk);
            await db.SaveChangesAsync();

            activeCourtMember = new AppUser { Username = $"officer_active_{Guid.NewGuid():N}", DisplayName = "Active Officer", IsActive = true };
            inactiveCourtMember = new AppUser { Username = $"officer_inactive_{Guid.NewGuid():N}", DisplayName = "Inactive Officer", IsActive = false };
            db.AppUsers.AddRange(activeCourtMember, inactiveCourtMember);
            await db.SaveChangesAsync();

            db.UserDeskMemberships.Add(new UserDeskMembership { UserId = activeCourtMember.Id, OfficeDeskId = courtDesk.Id, IsActive = true });
            db.UserDeskMemberships.Add(new UserDeskMembership { UserId = inactiveCourtMember.Id, OfficeDeskId = courtDesk.Id, IsActive = false });
            await db.SaveChangesAsync();
        }

        var res = await admin.GetAsync("/api/court-cases/filter-options");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var options = await res.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);

        var desks = options.GetProperty("desks").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(courtDesk.Id, desks);
        Assert.DoesNotContain(otherDesk.Id, desks);

        var officers = options.GetProperty("officers").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(activeCourtMember.Id, officers);
        Assert.DoesNotContain(inactiveCourtMember.Id, officers);
    }

    // =========================================================================
    // 16. AUDIT ITEM 3: CASE-SPECIFIC SCHEDULE AUTHORIZATION
    // =========================================================================

    [Fact]
    public async Task Schedule_CaseSpecificAuthorization_RequiresCourtCaseAccess()
    {
        var admin = await CreateAdminClientAsync();

        OfficeDesk deskA;
        OfficeDesk deskB;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var courtWs = await db.Workstreams.FirstAsync(w => w.Code == WorkstreamCodes.CourtReferences);
            deskA = new OfficeDesk { WorkstreamId = courtWs.Id, Name = $"DeskA_{Guid.NewGuid():N}" };
            deskB = new OfficeDesk { WorkstreamId = courtWs.Id, Name = $"DeskB_{Guid.NewGuid():N}" };
            db.OfficeDesks.AddRange(deskA, deskB);
            await db.SaveChangesAsync();
        }

        // Create court case in Desk A
        var cmd = new
        {
            caseNumber = $"WP_SCHED_AUTH_{Guid.NewGuid():N}",
            courtName = "Delhi High Court",
            responsibleOfficeDeskId = deskA.Id
        };
        var cRes = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        var caseId = (await cRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // Record proceeding with NDOH
        var pRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/proceedings", new
        {
            proceedingDate = "2026-06-01",
            nextDate = "2026-07-15",
            orderType = "Notice"
        });
        var procId = (await pRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // Promote to calendar — explicitly assign to deskA so userA's membership grants access
        var promoteRes = await admin.PostAsJsonAsync($"/api/scheduled-events/from-court-proceeding/{procId}", new { title = "Hearing on Notice", responsibleOfficeDeskId = deskA.Id });
        Assert.Equal(HttpStatusCode.Created, promoteRes.StatusCode);
        var eventId = (await promoteRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // User with Schedule.View & Court.View in ScopeMode.Assigned, belonging ONLY to Desk B
        var (userBClient, _) = await CreateUserWithPermissionsAsync(
            $"usr_sched_desk_b_{Guid.NewGuid():N}",
            "Pass123!",
            new[]
            {
                (PermissionCodes.ScheduleView, ScopeMode.All),
                (PermissionCodes.CourtView, ScopeMode.Assigned)
            },
            deskId: deskB.Id
        );

        // INVARIANT (Audit Item 3): User cannot view court scheduled event because they cannot access case in Desk A!
        var getRes = await userBClient.GetAsync($"/api/scheduled-events/{eventId}");
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);

        // User belonging to Desk A CAN view it
        var (userAClient, _) = await CreateUserWithPermissionsAsync(
            $"usr_sched_desk_a_{Guid.NewGuid():N}",
            "Pass123!",
            new[]
            {
                (PermissionCodes.ScheduleView, ScopeMode.All),
                (PermissionCodes.CourtView, ScopeMode.Assigned)
            },
            deskId: deskA.Id
        );

        var getARes = await userAClient.GetAsync($"/api/scheduled-events/{eventId}");
        Assert.Equal(HttpStatusCode.OK, getARes.StatusCode);
    }

    // =========================================================================
    // 17. AUDIT ITEM 18: ACTIVITY FEED ZERO COURT LEAK WITHOUT COURT.VIEW
    // =========================================================================

    [Fact]
    public async Task ActivityFeed_ZeroCourtLeaksWhenLackingCourtView()
    {
        var admin = await CreateAdminClientAsync();

        // 1. Admin creates a court case, adds a proceeding, uploads a document
        var cmd = new { caseNumber = $"WP_FEED_{Guid.NewGuid():N}", courtName = "Delhi High Court" };
        var cRes = await admin.PostAsJsonAsync("/api/court-cases", cmd);
        var caseId = (await cRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        var pRes = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/proceedings", new
        {
            proceedingDate = "2026-06-01",
            nextDate = "2026-07-15",
            orderType = "Stay"
        });
        var procId = (await pRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("id").GetGuid();

        // Promote to calendar
        await admin.PostAsJsonAsync($"/api/scheduled-events/from-court-proceeding/{procId}", new { title = "Hearing for WP" });

        // User with Audit.View (ScopeMode.All) but ZERO Court.View
        var (auditOnlyClient, _) = await CreateUserWithPermissionsAsync(
            $"usr_feed_audit_{Guid.NewGuid():N}",
            "Pass123!",
            new[]
            {
                (PermissionCodes.AuditView, ScopeMode.All)
            }
        );

        // Query team activity feed
        var feedRes = await auditOnlyClient.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, feedRes.StatusCode);
        var feed = await feedRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var events = feed.GetProperty("items").EnumerateArray().ToList();

        // Zero items referencing CourtCase or court hearing scheduled event
        Assert.DoesNotContain(events, e =>
            e.TryGetProperty("entityType", out var et) &&
            (et.GetString()?.Equals("CourtCase", StringComparison.OrdinalIgnoreCase) == true ||
             et.GetString()?.Equals("courtcase", StringComparison.OrdinalIgnoreCase) == true));
    }
}
