namespace LAC.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

public sealed class Phase2GTestFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"phase2g-tests-{Guid.NewGuid()}";
    public const string TestAdminUser = "phase2g_admin";
    public const string TestAdminPass = "Phase2GAdminPass!123";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = TestAdminUser,
                ["BootstrapAdmin:Password"] = TestAdminPass,
                ["BootstrapAdmin:DisplayName"] = "Phase2G Test Administrator"
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

public sealed class Phase2GTests : IClassFixture<Phase2GTestFactory>
{
    private readonly Phase2GTestFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Phase2GTests(Phase2GTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Phase2GTestFactory.TestAdminUser, Phase2GTestFactory.TestAdminPass));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return client;
    }

    private async Task<Workstream> CreateWorkstreamAsync(string code, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var ws = new Workstream
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            IsActive = true,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Workstreams.Add(ws);
        await db.SaveChangesAsync();
        return ws;
    }

    private async Task<OfficeDesk> CreateDeskAsync(string code, string name, Guid? workstreamId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var desk = new OfficeDesk
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            WorkstreamId = workstreamId,
            IsActive = true,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.OfficeDesks.Add(desk);
        await db.SaveChangesAsync();
        return desk;
    }

    private async Task<Village> CreateVillageAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var village = new Village
        {
            Id = Guid.NewGuid(),
            Name = name,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Villages.Add(village);
        await db.SaveChangesAsync();
        return village;
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateScopedUserClientAsync(
        string username,
        string roleCode,
        ScopeMode scopeMode,
        Guid? deskId = null,
        Guid? workstreamId = null,
        string[]? permissions = null)
    {
        var adminClient = await CreateAdminClientAsync();

        var permCodes = permissions?.ToList() ?? new List<string> { PermissionCodes.AuditView };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var role = await db.Roles.Include(r => r.RolePermissions).FirstOrDefaultAsync(r => r.Code == roleCode);
            if (role == null)
            {
                role = new Role
                {
                    Code = roleCode,
                    Name = roleCode,
                    Description = "Scoped Test Role",
                    IsSystemRole = false
                };
                db.Roles.Add(role);
                await db.SaveChangesAsync();

                var perms = await db.Permissions.Where(p => permCodes.Contains(p.Code)).ToListAsync();
                foreach (var p in perms)
                {
                    db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.Id,
                        PermissionId = p.Id,
                        ScopeMode = scopeMode
                    });
                }
                await db.SaveChangesAsync();
            }
        }

        var userPass = "ScopedPass!123";
        Guid roleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            roleId = (await db.Roles.FirstAsync(r => r.Code == roleCode)).Id;
        }

        var createReq = new CreateUserRequest(
            Username: username,
            DisplayName: $"User {username}",
            Password: userPass,
            DesignationId: null,
            RoleIds: [roleId],
            WorkstreamIds: workstreamId.HasValue ? [workstreamId.Value] : null,
            PrimaryWorkstreamId: workstreamId
        );
        var createRes = await adminClient.PostAsJsonAsync("/api/admin/users", createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var userId = (await createRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        if (deskId.HasValue)
        {
            var assignRes = await adminClient.PostAsJsonAsync($"/api/admin/users/{userId}/desks", new AssignDeskRequest(deskId.Value, IsPrimary: true));
            Assert.Equal(HttpStatusCode.Created, assignRes.StatusCode);
        }

        var userClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginRes = await userClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, userPass));
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        return (userClient, userId);
    }

    [Fact]
    public async Task MyHistory_ReturnsOnlyCallerActions_AcrossAllDomains()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS_MYHIST", "My History Workstream");
        var desk = await CreateDeskAsync("DSK_MYHIST", "My History Desk", ws.Id);
        var village = await CreateVillageAsync("My History Village");

        var (otherClient, otherUserId) = await CreateScopedUserClientAsync(
            "other_user_hist", "ROLE_OTHER_HIST", ScopeMode.All, desk.Id, ws.Id,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemCreate]);

        Guid dakId = Guid.NewGuid();
        Guid matterId = Guid.NewGuid();
        Guid outwardId = Guid.NewGuid();
        Guid workItemId = Guid.NewGuid();
        Guid otherWorkItemId = Guid.NewGuid();
        Guid docId = Guid.NewGuid();

        var baseTime = DateTimeOffset.UtcNow.AddHours(-1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            // 1. Dak & DakMovement
            var dak = new Dak
            {
                Id = dakId,
                DiaryNumber = "DAK-MYHIST-001",
                Subject = "My History Dak",
                SenderName = "Test Sender",
                Status = DakStatus.Registered,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active,
                CreatedAt = baseTime,
                UpdatedAt = baseTime
            };
            db.Daks.Add(dak);
            db.DakMovements.Add(new DakMovement
            {
                DakId = dakId,
                SequenceNumber = 1,
                Action = DakMovementAction.Registered,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = baseTime.AddMinutes(5),
                ToDeskId = desk.Id
            });

            // 2. Matter & MatterEvent
            var matter = new Matter
            {
                Id = matterId,
                VillageId = village.Id,
                ReferenceNumber = "MAT-MYHIST-001",
                Title = "My History Matter",
                MatterType = "CIVIL",
                Status = "Active",
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active,
                CreatedAt = baseTime,
                UpdatedAt = baseTime
            };
            db.Matters.Add(matter);
            db.MatterEvents.Add(new MatterEvent
            {
                MatterId = matterId,
                SequenceNumber = 1,
                Action = MatterEventAction.Created,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = baseTime.AddMinutes(10),
                WorkstreamIdSnapshot = ws.Id
            });

            // 3. Outward & OutwardEvent
            var outward = new Outward
            {
                Id = outwardId,
                OutwardNumber = "OUT-MYHIST-001",
                Subject = "My History Outward",
                Status = OutwardStatus.Registered,
                IssuingDeskId = desk.Id,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active,
                CreatedAt = baseTime,
                UpdatedAt = baseTime
            };
            db.Outwards.Add(outward);
            db.OutwardEvents.Add(new OutwardEvent
            {
                OutwardId = outwardId,
                SequenceNumber = 1,
                Action = OutwardEventAction.Registered,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = baseTime.AddMinutes(15),
                IssuingDeskIdSnapshot = desk.Id,
                WorkstreamIdSnapshot = ws.Id
            });

            // 4. WorkItem & WorkItemEvent
            var workItem = new WorkItem
            {
                Id = workItemId,
                Title = "My History WorkItem",
                Status = WorkItemStatus.InProgress,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active,
                CreatedAt = baseTime,
                UpdatedAt = baseTime
            };
            db.WorkItems.Add(workItem);
            db.WorkItemEvents.Add(new WorkItemEvent
            {
                WorkItemId = workItemId,
                SequenceNumber = 1,
                Action = WorkItemEventAction.Created,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = baseTime.AddMinutes(20)
            });

            // 5. Document & RecordAccessEvent (for Admin)
            var doc = new Document
            {
                Id = docId,
                DocumentType = "NOTE",
                OriginalFileName = "my_history_doc.pdf",
                StoragePath = "my_history_doc.pdf",
                RecordStatus = RecordStatus.Active,
                CreatedAt = baseTime,
                UpdatedAt = baseTime
            };
            db.Documents.Add(doc);
            db.RecordAccessEvents.Add(new RecordAccessEvent
            {
                ActorUserId = SeedData.BootstrapAdminId,
                ActorDisplayNameSnapshot = "Admin User",
                Action = RecordAccessAction.Opened,
                DocumentId = docId,
                DocumentTitleSnapshot = "my_history_doc.pdf",
                ContextEntityType = "matter",
                ContextEntityId = matterId,
                WorkstreamId = ws.Id,
                OccurredAt = baseTime.AddMinutes(25)
            });

            // 6. Other User's WorkItem & Event (Should NOT appear in Admin's My History)
            var otherWorkItem = new WorkItem
            {
                Id = otherWorkItemId,
                Title = "Other User WorkItem",
                Status = WorkItemStatus.InProgress,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active,
                CreatedAt = baseTime,
                UpdatedAt = baseTime
            };
            db.WorkItems.Add(otherWorkItem);
            db.WorkItemEvents.Add(new WorkItemEvent
            {
                WorkItemId = otherWorkItemId,
                SequenceNumber = 1,
                Action = WorkItemEventAction.Created,
                ActionByUserId = otherUserId,
                ActionByDisplayNameSnapshot = "Other User",
                ActionAt = baseTime.AddMinutes(30)
            });

            await db.SaveChangesAsync();
        }

        // Admin queries My History
        var res = await adminClient.GetAsync("/api/activity/my-history");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);
        Assert.True(feed.Items.Count >= 5);

        // Verify all returned items belong to Admin
        Assert.All(feed.Items, item => Assert.Equal(SeedData.BootstrapAdminId, item.ActorUserId));

        // Verify other user's event is NOT present
        Assert.DoesNotContain(feed.Items, i => i.EntityId == otherWorkItemId);

        // Verify presence of all 5 admin events
        Assert.Contains(feed.Items, i => string.Equals(i.EntityType, "dak", StringComparison.OrdinalIgnoreCase) && i.EntityId == dakId);
        Assert.Contains(feed.Items, i => string.Equals(i.EntityType, "matter", StringComparison.OrdinalIgnoreCase) && i.EntityId == matterId);
        Assert.Contains(feed.Items, i => string.Equals(i.EntityType, "outward", StringComparison.OrdinalIgnoreCase) && i.EntityId == outwardId);
        Assert.Contains(feed.Items, i => string.Equals(i.EntityType, "workitem", StringComparison.OrdinalIgnoreCase) && i.EntityId == workItemId);
        Assert.Contains(feed.Items, i => i.IsReadEvent && string.Equals(i.EntityType, "document", StringComparison.OrdinalIgnoreCase) && i.EntityId == docId);

        // Verify chronological descending sort
        for (int i = 0; i < feed.Items.Count - 1; i++)
        {
            Assert.True(feed.Items[i].OccurredAt >= feed.Items[i + 1].OccurredAt);
        }
    }

    [Fact]
    public async Task MyHistory_InactiveUser_Returns403()
    {
        var ws = await CreateWorkstreamAsync("WS_INACTIVE", "Inactive WS");
        var (client, userId) = await CreateScopedUserClientAsync(
            "inactive_test_user", "ROLE_INACTIVE_TEST", ScopeMode.All, null, ws.Id,
            [PermissionCodes.DakView]);

        // Deactivate user in database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var user = await db.AppUsers.FindAsync(userId);
            Assert.NotNull(user);
            user.IsActive = false;
            await db.SaveChangesAsync();
        }

        var res = await client.GetAsync("/api/activity/my-history");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task MyHistory_ArchivedOrTerminalRecords_RetainedInHistory()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS_TERMINAL", "Terminal WS");
        var village = await CreateVillageAsync("Terminal Village");

        Guid archivedMatterId = Guid.NewGuid();
        Guid completedWorkItemId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            var matter = new Matter
            {
                Id = archivedMatterId,
                VillageId = village.Id,
                ReferenceNumber = "MAT-ARCH-001",
                Title = "Archived Matter",
                MatterType = "CIVIL",
                Status = "Closed",
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Archived, // Frozen invariant: Archived records must be retained
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now
            };
            db.Matters.Add(matter);
            db.MatterEvents.Add(new MatterEvent
            {
                MatterId = archivedMatterId,
                SequenceNumber = 1,
                Action = MatterEventAction.Archived,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = now,
                WorkstreamIdSnapshot = ws.Id
            });

            var workItem = new WorkItem
            {
                Id = completedWorkItemId,
                Title = "Completed Work Item",
                Status = WorkItemStatus.Completed,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active,
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now
            };
            db.WorkItems.Add(workItem);
            db.WorkItemEvents.Add(new WorkItemEvent
            {
                WorkItemId = completedWorkItemId,
                SequenceNumber = 1,
                Action = WorkItemEventAction.Completed,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = now
            });

            await db.SaveChangesAsync();
        }

        var res = await adminClient.GetAsync("/api/activity/my-history");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);

        // Verify archived matter and completed workitem are retained in history
        Assert.Contains(feed.Items, i => i.EntityId == archivedMatterId && i.Action == "Archived");
        Assert.Contains(feed.Items, i => i.EntityId == completedWorkItemId && i.Action == "Completed");
    }

    [Fact]
    public async Task MyHistory_CanOpenEvaluatesCurrentDomainPermissions()
    {
        var ws = await CreateWorkstreamAsync("WS_PERMEVAL", "Perm Eval WS");
        var desk = await CreateDeskAsync("DSK_PERMEVAL", "Perm Eval Desk", ws.Id);

        // User has Dak.View, but NOT WorkItem.View
        var (client, userId) = await CreateScopedUserClientAsync(
            "user_permeval", "ROLE_PERMEVAL", ScopeMode.All, desk.Id, ws.Id,
            [PermissionCodes.DakView]);

        Guid dakId = Guid.NewGuid();
        Guid workItemId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            var dak = new Dak
            {
                Id = dakId,
                DiaryNumber = "DAK-PERM-001",
                Subject = "Perm Dak",
                SenderName = "Sender",
                Status = DakStatus.Registered,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active
            };
            db.Daks.Add(dak);
            db.DakMovements.Add(new DakMovement
            {
                DakId = dakId,
                SequenceNumber = 1,
                Action = DakMovementAction.Registered,
                ActionByUserId = userId,
                ActionByDisplayNameSnapshot = "Perm User",
                ActionAt = now.AddMinutes(-5)
            });

            var workItem = new WorkItem
            {
                Id = workItemId,
                Title = "Perm WorkItem",
                Status = WorkItemStatus.InProgress,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active
            };
            db.WorkItems.Add(workItem);
            db.WorkItemEvents.Add(new WorkItemEvent
            {
                WorkItemId = workItemId,
                SequenceNumber = 1,
                Action = WorkItemEventAction.Created,
                ActionByUserId = userId,
                ActionByDisplayNameSnapshot = "Perm User",
                ActionAt = now.AddMinutes(-3)
            });

            await db.SaveChangesAsync();
        }

        var res = await client.GetAsync("/api/activity/my-history");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);

        var dakItem = feed.Items.FirstOrDefault(i => i.EntityId == dakId);
        var workItemItem = feed.Items.FirstOrDefault(i => i.EntityId == workItemId);

        Assert.NotNull(dakItem);
        Assert.True(dakItem.CanOpen);
        Assert.NotNull(dakItem.NavigationUrl);
        Assert.StartsWith("/dak/", dakItem.NavigationUrl);

        Assert.NotNull(workItemItem);
        Assert.False(workItemItem.CanOpen);
        Assert.Null(workItemItem.NavigationUrl);
    }

    [Fact]
    public async Task TeamActivity_RequiresAuditView_Returns403IfMissing()
    {
        var ws = await CreateWorkstreamAsync("WS_NOAUDIT", "No Audit WS");
        // User without Audit.View
        var (client, _) = await CreateScopedUserClientAsync(
            "user_no_audit", "ROLE_NO_AUDIT", ScopeMode.All, null, ws.Id,
            [PermissionCodes.DakView]);

        var res = await client.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task TeamActivity_OwnScopeOnly_FailsClosed()
    {
        var ws = await CreateWorkstreamAsync("WS_OWNSCOPE", "Own Scope WS");
        // User with Audit.View scoped to Own (must fail closed)
        var (client, _) = await CreateScopedUserClientAsync(
            "user_own_audit", "ROLE_OWN_AUDIT", ScopeMode.Own, null, ws.Id,
            [PermissionCodes.AuditView]);

        var res = await client.GetAsync("/api/activity/team");
        // Frozen invariant: Own scope fails closed on Team Activity (returns 403 Forbidden)
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task TeamActivity_WorkstreamScope_FiltersToCallerWorkstreams()
    {
        var wsA = await CreateWorkstreamAsync("WS_TEAM_A", "Team WS A");
        var wsB = await CreateWorkstreamAsync("WS_TEAM_B", "Team WS B");
        var village = await CreateVillageAsync("Team Village");

        var (clientA, _) = await CreateScopedUserClientAsync(
            "user_ws_a", "ROLE_WS_A", ScopeMode.Workstream, null, wsA.Id,
            [PermissionCodes.AuditView, PermissionCodes.MatterView]);

        Guid matterAId = Guid.NewGuid();
        Guid matterBId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            var matterA = new Matter
            {
                Id = matterAId,
                VillageId = village.Id,
                ReferenceNumber = "MAT-TEAM-A",
                Title = "Matter in WS A",
                MatterType = "CIVIL",
                Status = "Active",
                WorkstreamId = wsA.Id,
                RecordStatus = RecordStatus.Active
            };
            db.Matters.Add(matterA);
            db.MatterEvents.Add(new MatterEvent
            {
                MatterId = matterAId,
                SequenceNumber = 1,
                Action = MatterEventAction.Created,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = now.AddMinutes(-10),
                WorkstreamIdSnapshot = wsA.Id
            });

            var matterB = new Matter
            {
                Id = matterBId,
                VillageId = village.Id,
                ReferenceNumber = "MAT-TEAM-B",
                Title = "Matter in WS B",
                MatterType = "CIVIL",
                Status = "Active",
                WorkstreamId = wsB.Id,
                RecordStatus = RecordStatus.Active
            };
            db.Matters.Add(matterB);
            db.MatterEvents.Add(new MatterEvent
            {
                MatterId = matterBId,
                SequenceNumber = 1,
                Action = MatterEventAction.Created,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = now.AddMinutes(-5),
                WorkstreamIdSnapshot = wsB.Id
            });

            await db.SaveChangesAsync();
        }

        var res = await clientA.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);

        // WS A user must see WS A events, but NEVER WS B events
        Assert.Contains(feed.Items, i => i.EntityId == matterAId);
        Assert.DoesNotContain(feed.Items, i => i.EntityId == matterBId);
    }

    [Fact]
    public async Task TeamActivity_AssignedScope_FiltersToCallerDesks()
    {
        var ws = await CreateWorkstreamAsync("WS_DESK_SCOPE", "Desk Scope WS");
        var desk1 = await CreateDeskAsync("DSK_SCOPE_1", "Desk 1", ws.Id);
        var desk2 = await CreateDeskAsync("DSK_SCOPE_2", "Desk 2", ws.Id);

        var (clientDesk1, _) = await CreateScopedUserClientAsync(
            "user_desk_1", "ROLE_DESK_1", ScopeMode.Assigned, desk1.Id, ws.Id,
            [PermissionCodes.AuditView, PermissionCodes.OutwardView]);

        Guid outward1Id = Guid.NewGuid();
        Guid outward2Id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            var outward1 = new Outward
            {
                Id = outward1Id,
                OutwardNumber = "OUT-DESK-1",
                Subject = "Outward Desk 1",
                Status = OutwardStatus.Registered,
                IssuingDeskId = desk1.Id,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active
            };
            db.Outwards.Add(outward1);
            db.OutwardEvents.Add(new OutwardEvent
            {
                OutwardId = outward1Id,
                SequenceNumber = 1,
                Action = OutwardEventAction.Registered,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = now.AddMinutes(-10),
                IssuingDeskIdSnapshot = desk1.Id,
                WorkstreamIdSnapshot = ws.Id
            });

            var outward2 = new Outward
            {
                Id = outward2Id,
                OutwardNumber = "OUT-DESK-2",
                Subject = "Outward Desk 2",
                Status = OutwardStatus.Registered,
                IssuingDeskId = desk2.Id,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active
            };
            db.Outwards.Add(outward2);
            db.OutwardEvents.Add(new OutwardEvent
            {
                OutwardId = outward2Id,
                SequenceNumber = 1,
                Action = OutwardEventAction.Registered,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = now.AddMinutes(-5),
                IssuingDeskIdSnapshot = desk2.Id,
                WorkstreamIdSnapshot = ws.Id
            });

            await db.SaveChangesAsync();
        }

        var res = await clientDesk1.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);

        // Desk 1 user must see Desk 1 event, but NOT Desk 2 event
        Assert.Contains(feed.Items, i => i.EntityId == outward1Id);
        Assert.DoesNotContain(feed.Items, i => i.EntityId == outward2Id);
    }

    [Fact]
    public async Task TeamActivity_ScopedUser_UnclassifiedMatterFailsClosed_WhileAllScopeSeesIt()
    {
        var ws = await CreateWorkstreamAsync("WS_UNCLASS", "Unclass WS");
        var village = await CreateVillageAsync("Unclass Village");
        var (clientScoped, scopedUserId) = await CreateScopedUserClientAsync(
            "user_scoped_unclass", "ROLE_SCOPED_UNCLASS", ScopeMode.Workstream, null, ws.Id,
            [PermissionCodes.AuditView, PermissionCodes.MatterView]);

        var adminClient = await CreateAdminClientAsync();

        Guid unclassMatterId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            // Unclassified matter (WorkstreamIdSnapshot == null)
            var unclassMatter = new Matter
            {
                Id = unclassMatterId,
                VillageId = village.Id,
                ReferenceNumber = "MAT-UNCLASS-001",
                Title = "Unclassified Matter",
                MatterType = "CIVIL",
                Status = "Active",
                WorkstreamId = null,
                RecordStatus = RecordStatus.Active
            };
            db.Matters.Add(unclassMatter);
            db.MatterEvents.Add(new MatterEvent
            {
                MatterId = unclassMatterId,
                SequenceNumber = 1,
                Action = MatterEventAction.Created,
                ActionByUserId = scopedUserId,
                ActionByDisplayNameSnapshot = "Scoped User",
                ActionAt = now.AddMinutes(-5),
                WorkstreamIdSnapshot = null // Historical context unknowable
            });

            await db.SaveChangesAsync();
        }

        // 1. Scoped user calls Team Activity: MUST FAIL CLOSED (unclassified record not attributable)
        var scopedTeamRes = await clientScoped.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, scopedTeamRes.StatusCode);
        var scopedTeamFeed = await scopedTeamRes.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(scopedTeamFeed);
        Assert.DoesNotContain(scopedTeamFeed.Items, i => i.EntityId == unclassMatterId);

        // 2. Scoped user calls My History: visible because scoped user was the actor
        var scopedMyHistRes = await clientScoped.GetAsync("/api/activity/my-history");
        Assert.Equal(HttpStatusCode.OK, scopedMyHistRes.StatusCode);
        var scopedMyHistFeed = await scopedMyHistRes.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(scopedMyHistFeed);
        Assert.Contains(scopedMyHistFeed.Items, i => i.EntityId == unclassMatterId);

        // 3. Admin (ScopeMode.All) calls Team Activity: visible because All scope sees everything
        var adminTeamRes = await adminClient.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, adminTeamRes.StatusCode);
        var adminTeamFeed = await adminTeamRes.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(adminTeamFeed);
        Assert.Contains(adminTeamFeed.Items, i => i.EntityId == unclassMatterId);
    }

    [Fact]
    public async Task LogAccess_PublicDirectEndpoint_DoesNotExist()
    {
        var adminClient = await CreateAdminClientAsync();
        var res = await adminClient.PostAsJsonAsync("/api/activity/log-access", new
        {
            documentId = Guid.NewGuid(),
            action = "Opened"
        });
        Assert.True(res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task DocumentAccess_OpenAndPreview_DeduplicatedWithin5Minutes()
    {
        var village = await CreateVillageAsync("Dedup Village");
        Guid docId = Guid.NewGuid();
        Guid matterId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Documents.Add(new Document
            {
                Id = docId,
                DocumentType = "NOTE",
                OriginalFileName = "dedup_test.pdf",
                StoragePath = "dedup_test.pdf"
            });
            db.Matters.Add(new Matter
            {
                Id = matterId,
                VillageId = village.Id,
                ReferenceNumber = "MAT-DEDUP-001",
                Title = "Dedup Matter",
                MatterType = "CIVIL"
            });
            await db.SaveChangesAsync();
        }

        var cmd = new RecordAccessCommand(
            ActorUserId: SeedData.BootstrapAdminId,
            Action: RecordAccessAction.Opened,
            DocumentId: docId,
            ContextEntityType: "matter",
            ContextEntityId: matterId,
            DocumentTitleSnapshot: "dedup_test.pdf"
        );

        using (var scope = _factory.Services.CreateScope())
        {
            var logger = scope.ServiceProvider.GetRequiredService<IRecordAccessLogger>();
            await logger.LogAccessAsync(cmd);
            await logger.LogAccessAsync(cmd);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var count = await db.RecordAccessEvents.CountAsync(e => e.DocumentId == docId && e.Action == RecordAccessAction.Opened);
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task DocumentAccess_Download_NeverDeduplicated()
    {
        var village = await CreateVillageAsync("DL Village");
        Guid docId = Guid.NewGuid();
        Guid matterId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Documents.Add(new Document
            {
                Id = docId,
                DocumentType = "NOTE",
                OriginalFileName = "download_test.pdf",
                StoragePath = "download_test.pdf"
            });
            db.Matters.Add(new Matter
            {
                Id = matterId,
                VillageId = village.Id,
                ReferenceNumber = "MAT-DL-001",
                Title = "Download Matter",
                MatterType = "CIVIL"
            });
            await db.SaveChangesAsync();
        }

        var cmd = new RecordAccessCommand(
            ActorUserId: SeedData.BootstrapAdminId,
            Action: RecordAccessAction.Downloaded,
            DocumentId: docId,
            ContextEntityType: "matter",
            ContextEntityId: matterId,
            DocumentTitleSnapshot: "download_test.pdf"
        );

        using (var scope = _factory.Services.CreateScope())
        {
            var logger = scope.ServiceProvider.GetRequiredService<IRecordAccessLogger>();
            await logger.LogAccessAsync(cmd);
            await logger.LogAccessAsync(cmd);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var count = await db.RecordAccessEvents.CountAsync(e => e.DocumentId == docId && e.Action == RecordAccessAction.Downloaded);
            Assert.Equal(2, count);
        }
    }

    [Fact]
    public async Task TeamActivity_DakAssignedScope_MatchesHistoricalDesks()
    {
        var ws = await CreateWorkstreamAsync("WS_DAK_DESKS", "Dak Desks WS");
        var desk1 = await CreateDeskAsync("DSK_DAK_1", "Desk 1", ws.Id);
        var desk2 = await CreateDeskAsync("DSK_DAK_2", "Desk 2", ws.Id);
        var desk3 = await CreateDeskAsync("DSK_DAK_3", "Desk 3", ws.Id);

        var (clientDesk1, _) = await CreateScopedUserClientAsync(
            "user_dak_desk1", "ROLE_DAK_DSK1", ScopeMode.Assigned, desk1.Id, ws.Id,
            [PermissionCodes.AuditView, PermissionCodes.DakView]);

        var (clientDesk2, _) = await CreateScopedUserClientAsync(
            "user_dak_desk2", "ROLE_DAK_DSK2", ScopeMode.Assigned, desk2.Id, ws.Id,
            [PermissionCodes.AuditView, PermissionCodes.DakView]);

        var (clientDesk3, _) = await CreateScopedUserClientAsync(
            "user_dak_desk3", "ROLE_DAK_DSK3", ScopeMode.Assigned, desk3.Id, ws.Id,
            [PermissionCodes.AuditView, PermissionCodes.DakView]);

        Guid dakId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            var dak = new Dak
            {
                Id = dakId,
                DiaryNumber = "DAK-DESK-TEST-01",
                Subject = "Desk Routing Dak",
                SenderName = "Sender",
                Status = DakStatus.Registered,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active
            };
            db.Daks.Add(dak);

            // Movement from Desk 1 to Desk 2
            db.DakMovements.Add(new DakMovement
            {
                DakId = dakId,
                SequenceNumber = 1,
                Action = DakMovementAction.Marked,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = now.AddMinutes(-5),
                FromDeskId = desk1.Id,
                FromDeskNameSnapshot = desk1.Name,
                ToDeskId = desk2.Id,
                ToDeskNameSnapshot = desk2.Name,
                WorkstreamIdSnapshot = ws.Id,
                WorkstreamNameSnapshot = ws.Name
            });

            await db.SaveChangesAsync();
        }

        // Desk 1 user sees it (matched via FromDeskId)
        var res1 = await clientDesk1.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var feed1 = await res1.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed1);
        Assert.Contains(feed1.Items, i => i.EntityId == dakId);

        // Desk 2 user sees it (matched via ToDeskId)
        var res2 = await clientDesk2.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var feed2 = await res2.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed2);
        Assert.Contains(feed2.Items, i => i.EntityId == dakId);

        // Desk 3 user does NOT see it
        var res3 = await clientDesk3.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, res3.StatusCode);
        var feed3 = await res3.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed3);
        Assert.DoesNotContain(feed3.Items, i => i.EntityId == dakId);
    }


    [Fact]
    public async Task TeamActivity_DakHistoricalWorkstreamContext_AndWorkstreamFiltering()
    {
        var wsA = await CreateWorkstreamAsync("WS_DAK_A", "Dak WS A");
        var wsB = await CreateWorkstreamAsync("WS_DAK_B", "Dak WS B");
        var deskA = await CreateDeskAsync("DSK_DAK_A", "Desk A", wsA.Id);

        var (clientA, _) = await CreateScopedUserClientAsync(
            "user_dak_a", "ROLE_DAK_A", ScopeMode.Workstream, null, wsA.Id,
            [PermissionCodes.AuditView, PermissionCodes.DakView]);

        Guid dak1Id = Guid.NewGuid();
        Guid dak2Id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            // Dak 1 with WS A snapshot
            var dak1 = new Dak
            {
                Id = dak1Id,
                DiaryNumber = "DAK-HIST-WS-01",
                Subject = "Dak with WS A Snapshot",
                SenderName = "Sender",
                Status = DakStatus.Registered,
                WorkstreamId = wsA.Id,
                RecordStatus = RecordStatus.Active
            };
            db.Daks.Add(dak1);
            db.DakMovements.Add(new DakMovement
            {
                DakId = dak1Id,
                SequenceNumber = 1,
                Action = DakMovementAction.Registered,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = now.AddMinutes(-10),
                ToDeskId = deskA.Id,
                WorkstreamIdSnapshot = wsA.Id,
                WorkstreamNameSnapshot = wsA.Name
            });

            // Dak 2 (Legacy with null WorkstreamIdSnapshot)
            var dak2 = new Dak
            {
                Id = dak2Id,
                DiaryNumber = "DAK-HIST-LEGACY-02",
                Subject = "Legacy Dak Null Snapshot",
                SenderName = "Sender",
                Status = DakStatus.Registered,
                WorkstreamId = wsA.Id, // Mutable current is wsA, but snapshot is null!
                RecordStatus = RecordStatus.Active
            };
            db.Daks.Add(dak2);
            db.DakMovements.Add(new DakMovement
            {
                DakId = dak2Id,
                SequenceNumber = 1,
                Action = DakMovementAction.Registered,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = now.AddMinutes(-5),
                ToDeskId = deskA.Id,
                WorkstreamIdSnapshot = null, // Must fail closed under Workstream scope
                WorkstreamNameSnapshot = null
            });

            await db.SaveChangesAsync();
        }

        // 1. Scoped user sees Dak 1, but legacy Dak 2 fails closed
        var res = await clientA.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);
        Assert.Contains(feed.Items, i => i.EntityId == dak1Id);
        Assert.DoesNotContain(feed.Items, i => i.EntityId == dak2Id);

        // 2. Query filter with matching workstreamId returns Dak 1
        var resFiltered = await clientA.GetAsync($"/api/activity/team?workstreamId={wsA.Id}");
        Assert.Equal(HttpStatusCode.OK, resFiltered.StatusCode);
        var feedFiltered = await resFiltered.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feedFiltered);
        Assert.Contains(feedFiltered.Items, i => i.EntityId == dak1Id);

        // 3. Query filter with non-matching workstreamId does NOT return Dak 1
        var resNonMatch = await clientA.GetAsync($"/api/activity/team?workstreamId={wsB.Id}");
        Assert.Equal(HttpStatusCode.OK, resNonMatch.StatusCode);
        var feedNonMatch = await resNonMatch.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feedNonMatch);
        Assert.DoesNotContain(feedNonMatch.Items, i => i.EntityId == dak1Id);
    }

    [Fact]
    public async Task TeamActivity_WorkItemHistoricalAssignedScope_OldDeskRetainsVisibility_NewDeskDoesNotInheritOldEvents()
    {
        var ws = await CreateWorkstreamAsync("WS_WI_HIST", "WI Hist WS");
        var deskA = await CreateDeskAsync("DSK_WI_A", "Desk A", ws.Id);
        var deskB = await CreateDeskAsync("DSK_WI_B", "Desk B", ws.Id);

        var (clientA, userAId) = await CreateScopedUserClientAsync(
            "user_wi_desk_a", "ROLE_WI_DESK_A", ScopeMode.Assigned, deskA.Id, ws.Id,
            [PermissionCodes.AuditView, PermissionCodes.WorkItemView]);

        var (clientB, userBId) = await CreateScopedUserClientAsync(
            "user_wi_desk_b", "ROLE_WI_DESK_B", ScopeMode.Assigned, deskB.Id, ws.Id,
            [PermissionCodes.AuditView, PermissionCodes.WorkItemView]);

        Guid workItemId = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow.AddHours(-2);
        var t1 = t0.AddMinutes(15);
        var t2 = t0.AddMinutes(30);
        var t3 = t0.AddMinutes(45);

        Guid event1Id = Guid.NewGuid();
        Guid reassignedEventId = Guid.NewGuid();
        Guid event2Id = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            var workItem = new WorkItem
            {
                Id = workItemId,
                Title = "Historical Scope WorkItem",
                Status = WorkItemStatus.InProgress,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active,
                CreatedAt = t0,
                UpdatedAt = t3
            };
            db.WorkItems.Add(workItem);

            // Assignment 1: on Desk A from t0 to t2
            var assignA = new WorkItemAssignment
            {
                Id = Guid.NewGuid(),
                WorkItemId = workItemId,
                OfficeDeskId = deskA.Id,
                AssignedByUserId = SeedData.BootstrapAdminId,
                AssignedAt = t0,
                ClosedAt = t2,
                IsActive = false,
                RecordStatus = RecordStatus.Active
            };
            db.WorkItemAssignments.Add(assignA);

            // Assignment 2: on Desk B from t2 onward
            var assignB = new WorkItemAssignment
            {
                Id = Guid.NewGuid(),
                WorkItemId = workItemId,
                OfficeDeskId = deskB.Id,
                AssignedByUserId = SeedData.BootstrapAdminId,
                AssignedAt = t2,
                ClosedAt = null,
                IsActive = true,
                RecordStatus = RecordStatus.Active
            };
            db.WorkItemAssignments.Add(assignB);

            // Event 1 at t1 (while Desk A was responsible)
            db.WorkItemEvents.Add(new WorkItemEvent
            {
                Id = event1Id,
                WorkItemId = workItemId,
                SequenceNumber = 1,
                Action = WorkItemEventAction.UpdateAdded,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = t1,
                RemarksSnapshot = "Note on Desk A"
            });

            // Reassigned Event at t2 (Source: Desk A, Target: Desk B)
            db.WorkItemEvents.Add(new WorkItemEvent
            {
                Id = reassignedEventId,
                WorkItemId = workItemId,
                SequenceNumber = 2,
                Action = WorkItemEventAction.Reassigned,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = t2,
                SourceDeskId = deskA.Id,
                SourceDeskNameSnapshot = deskA.Name,
                TargetDeskId = deskB.Id,
                TargetDeskNameSnapshot = deskB.Name,
                RemarksSnapshot = "Reassigned to Desk B"
            });

            // Event 2 at t3 (while Desk B was responsible)
            db.WorkItemEvents.Add(new WorkItemEvent
            {
                Id = event2Id,
                WorkItemId = workItemId,
                SequenceNumber = 3,
                Action = WorkItemEventAction.UpdateAdded,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = t3,
                RemarksSnapshot = "Note on Desk B"
            });

            await db.SaveChangesAsync();
        }

        // Desk A user: sees Event 1 and Reassignment, but NOT Event 2
        var resA = await clientA.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, resA.StatusCode);
        var feedA = await resA.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feedA);
        Assert.Contains(feedA.Items, i => i.EventId == event1Id);
        Assert.Contains(feedA.Items, i => i.EventId == reassignedEventId);
        Assert.DoesNotContain(feedA.Items, i => i.EventId == event2Id);

        // Desk B user: sees Reassignment and Event 2, but NOT Event 1 (does not inherit older events)
        var resB = await clientB.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, resB.StatusCode);
        var feedB = await resB.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feedB);
        Assert.DoesNotContain(feedB.Items, i => i.EventId == event1Id);
        Assert.Contains(feedB.Items, i => i.EventId == reassignedEventId);
        Assert.Contains(feedB.Items, i => i.EventId == event2Id);
    }

    [Fact]
    public async Task TeamActivity_LiveScope_MembershipDeactivation_ImmediatelyCutsOffVisibility()
    {
        var ws = await CreateWorkstreamAsync("WS_LIVE_SCOPE", "Live Scope WS");
        var desk = await CreateDeskAsync("DSK_LIVE_SCOPE", "Live Scope Desk", ws.Id);

        var (client, userId) = await CreateScopedUserClientAsync(
            "user_live_scope", "ROLE_LIVE_SCOPE", ScopeMode.Assigned, desk.Id, ws.Id,
            [PermissionCodes.AuditView, PermissionCodes.OutwardView]);

        Guid outwardId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var outward = new Outward
            {
                Id = outwardId,
                OutwardNumber = "OUT-LIVESCOPE-01",
                Subject = "Live Scope Outward",
                Status = OutwardStatus.Registered,
                IssuingDeskId = desk.Id,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active
            };
            db.Outwards.Add(outward);
            db.OutwardEvents.Add(new OutwardEvent
            {
                OutwardId = outwardId,
                SequenceNumber = 1,
                Action = OutwardEventAction.Registered,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                IssuingDeskIdSnapshot = desk.Id,
                WorkstreamIdSnapshot = ws.Id
            });
            await db.SaveChangesAsync();
        }

        // Active membership sees the event
        var res1 = await client.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var feed1 = await res1.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed1);
        Assert.Contains(feed1.Items, i => i.EntityId == outwardId);

        // Remove desk membership
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var membership = await db.UserDeskMemberships.FirstOrDefaultAsync(m => m.UserId == userId && m.OfficeDeskId == desk.Id);
            Assert.NotNull(membership);
            membership.RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // Inactive membership immediately cuts off visibility
        var res2 = await client.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var feed2 = await res2.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed2);
        Assert.DoesNotContain(feed2.Items, i => i.EntityId == outwardId);
    }

    [Fact]
    public async Task TeamActivity_ImmutableHistoricalNameSnapshots_PreservedAcrossMetadataChanges()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS_IMMUT_NAME", "Original Workstream Name");
        var desk = await CreateDeskAsync("DSK_IMMUT_NAME", "Original Desk Name", ws.Id);

        Guid outwardId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var outward = new Outward
            {
                Id = outwardId,
                OutwardNumber = "OUT-IMMUT-01",
                Subject = "Immutable Name Test",
                Status = OutwardStatus.Registered,
                IssuingDeskId = desk.Id,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active
            };
            db.Outwards.Add(outward);
            db.OutwardEvents.Add(new OutwardEvent
            {
                OutwardId = outwardId,
                SequenceNumber = 1,
                Action = OutwardEventAction.Registered,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                IssuingDeskIdSnapshot = desk.Id,
                IssuingDeskNameSnapshot = "Original Desk Name",
                WorkstreamIdSnapshot = ws.Id,
                WorkstreamNameSnapshot = "Original Workstream Name"
            });

            // Rename Desk and Workstream in DB
            desk.Name = "Renamed Mutable Desk";
            ws.Name = "Renamed Mutable Workstream";
            await db.SaveChangesAsync();
        }

        var res = await adminClient.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);

        var item = feed.Items.FirstOrDefault(i => i.EntityId == outwardId);
        Assert.NotNull(item);

        // Verify historical snapshot names are returned, NOT mutable renamed names
        Assert.Equal("Original Desk Name", item.DeskName);
        Assert.Equal("Original Workstream Name", item.WorkstreamName);
    }

    [Fact]
    public async Task TeamActivity_Pagination_BoundedAndDeterministicTieBreak()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS_PAGE_BOUND", "Page Bound WS");

        var baseTime = DateTimeOffset.UtcNow.AddHours(-1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            for (int i = 0; i < 50; i++)
            {
                var id = Guid.NewGuid();
                db.Matters.Add(new Matter
                {
                    Id = id,
                    ReferenceNumber = $"MAT-PGBOUND-{i:D3}",
                    Title = $"Matter Bound {i}",
                    MatterType = "CIVIL",
                    Status = "Active",
                    WorkstreamId = ws.Id,
                    RecordStatus = RecordStatus.Active
                });
                db.MatterEvents.Add(new MatterEvent
                {
                    MatterId = id,
                    SequenceNumber = 1,
                    Action = MatterEventAction.Created,
                    ActionByUserId = SeedData.BootstrapAdminId,
                    ActionByDisplayNameSnapshot = "Admin User",
                    ActionAt = baseTime, // Exact same timestamp to test tie-breaker
                    WorkstreamIdSnapshot = ws.Id,
                    WorkstreamNameSnapshot = ws.Name
                });
            }
            await db.SaveChangesAsync();
        }

        // Test clamping page > 100 to 100
        var resClampedPage = await adminClient.GetAsync("/api/activity/team?page=999&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, resClampedPage.StatusCode);
        var feedClampedPage = await resClampedPage.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feedClampedPage);
        Assert.Equal(100, feedClampedPage.Page);

        // Test clamping pageSize > 100 to 100
        var resClampedSize = await adminClient.GetAsync("/api/activity/team?page=1&pageSize=500");
        Assert.Equal(HttpStatusCode.OK, resClampedSize.StatusCode);
        var feedClampedSize = await resClampedSize.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feedClampedSize);
        Assert.Equal(100, feedClampedSize.PageSize);

        // Test deterministic tie-break across page 1 and page 2
        var resP1 = await adminClient.GetAsync("/api/activity/team?page=1&pageSize=10");
        var resP2 = await adminClient.GetAsync("/api/activity/team?page=2&pageSize=10");
        var feedP1 = await resP1.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        var feedP2 = await resP2.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);

        Assert.NotNull(feedP1);
        Assert.NotNull(feedP2);
        Assert.Equal(10, feedP1.Items.Count);
        Assert.Equal(10, feedP2.Items.Count);

        // Zero overlap between consecutive pages
        var idsP1 = feedP1.Items.Select(x => x.EventId).ToHashSet();
        var idsP2 = feedP2.Items.Select(x => x.EventId).ToHashSet();
        Assert.Empty(idsP1.Intersect(idsP2));
    }

    [Fact]
    public async Task TeamActivity_DateTo_InclusiveWholeDaySemantics()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS_DATETO", "DateTo WS");
        var today = DateTime.UtcNow.Date;
        var eventTime = new DateTimeOffset(today.AddHours(15).AddMinutes(30), TimeSpan.Zero);

        Guid matterId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var matter = new Matter
            {
                Id = matterId,
                ReferenceNumber = "MAT-DATE-001",
                Title = "DateTo Test Matter",
                MatterType = "CIVIL",
                Status = "Active",
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active
            };
            db.Matters.Add(matter);
            db.MatterEvents.Add(new MatterEvent
            {
                MatterId = matterId,
                SequenceNumber = 1,
                Action = MatterEventAction.Created,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin User",
                ActionAt = eventTime, // 3:30 PM UTC today
                WorkstreamIdSnapshot = ws.Id,
                WorkstreamNameSnapshot = ws.Name
            });
            await db.SaveChangesAsync();
        }

        // Query with dateTo = today (e.g. "2026-09-20")
        var dateToStr = today.ToString("yyyy-MM-dd");
        var res = await adminClient.GetAsync($"/api/activity/team?workstreamId={ws.Id}&dateTo={dateToStr}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);

        // The 3:30 PM event MUST be included due to inclusive whole-day semantics (< dateTo.Date.AddDays(1))
        Assert.Contains(feed.Items, i => i.EntityId == matterId);
    }

    [Fact]
    public async Task TeamActivity_FilterOptions_ReturnsAuthorizedOptions()
    {
        var ws1 = await CreateWorkstreamAsync("WS_FO_1", "Filter WS 1");
        var ws2 = await CreateWorkstreamAsync("WS_FO_2", "Filter WS 2");
        var desk1 = await CreateDeskAsync("DSK_FO_1", "Desk 1", ws1.Id);
        var desk2 = await CreateDeskAsync("DSK_FO_2", "Desk 2", ws2.Id);

        var (client1, _) = await CreateScopedUserClientAsync(
            "user_fo_1", "ROLE_FO_1", ScopeMode.Workstream, null, ws1.Id,
            [PermissionCodes.AuditView]);

        var res = await client1.GetAsync("/api/activity/team/filter-options");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var options = await res.Content.ReadFromJsonAsync<TeamFilterOptionsDto>(JsonOpts);
        Assert.NotNull(options);

        // Must include WS 1 but NOT WS 2
        Assert.Contains(options.Workstreams, w => w.Id == ws1.Id);
        Assert.DoesNotContain(options.Workstreams, w => w.Id == ws2.Id);

        // Must include Desk 1 (in WS 1) but NOT Desk 2 (in WS 2)
        Assert.Contains(options.Desks, d => d.Id == desk1.Id);
        Assert.DoesNotContain(options.Desks, d => d.Id == desk2.Id);
    }

    [Fact]
    public async Task RecordAccessEvent_Immutability_ThrowsOnUpdateOrDelete()
    {
        Guid eventId = Guid.NewGuid();
        Guid docId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var doc = new Document
            {
                Id = docId,
                DocumentType = "NOTE",
                OriginalFileName = "immutable_test.pdf",
                StoragePath = "immutable_test.pdf"
            };
            db.Documents.Add(doc);
            var ev = new RecordAccessEvent
            {
                Id = eventId,
                ActorUserId = SeedData.BootstrapAdminId,
                ActorDisplayNameSnapshot = "Admin User",
                Action = RecordAccessAction.Opened,
                DocumentId = docId,
                DocumentTitleSnapshot = "immutable_test.pdf"
            };
            db.RecordAccessEvents.Add(ev);
            await db.SaveChangesAsync();
        }

        // Test update mutation throws
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var ev = await db.RecordAccessEvents.FindAsync(eventId);
            Assert.NotNull(ev);
            ev.DocumentTitleSnapshot = "tampered_title.pdf";

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains("strictly immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Test delete mutation throws
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var ev = await db.RecordAccessEvents.FindAsync(eventId);
            Assert.NotNull(ev);
            db.RecordAccessEvents.Remove(ev);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains("strictly immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task DocumentAccess_ProtectedAwardContentEndpoint_CreatesAccessEventWithDynamicWorkstream_AndAppearsInTeamActivity()
    {
        // 1. Resolve or Create Award Workstream
        Guid awardWsId;
        string awardWsName;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var ws = await db.Workstreams.FirstOrDefaultAsync(w => w.Code == WorkstreamCodes.Award);
            if (ws == null)
            {
                ws = new Workstream
                {
                    Id = Guid.NewGuid(),
                    Code = WorkstreamCodes.Award,
                    Name = "Land Acquisition Award",
                    IsActive = true,
                    RecordStatus = RecordStatus.Active,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                db.Workstreams.Add(ws);
                await db.SaveChangesAsync();
            }
            awardWsId = ws.Id;
            awardWsName = ws.Name;
        }

        // 2. Save document to storage and database with award link
        Guid docId = Guid.NewGuid();
        Guid awardId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            using var mem = new MemoryStream([1, 2, 3, 4]);
            var storagePath = await storage.SaveAsync(mem, "award_protected_doc.pdf", default);

            var doc = new Document
            {
                Id = docId,
                DocumentType = "AWARD",
                OriginalFileName = "award_protected_doc.pdf",
                StoragePath = storagePath,
                MimeType = "application/pdf",
                Status = "Active",
                RecordStatus = RecordStatus.Active,
                UploadedAt = DateTimeOffset.UtcNow
            };
            db.Documents.Add(doc);

            var award = new Award
            {
                Id = awardId,
                AwardNumber = $"AW-{Guid.NewGuid():N}"[..12],
                Status = "Active",
                RecordStatus = RecordStatus.Active
            };
            db.Awards.Add(award);

            db.DocumentAwards.Add(new DocumentAward
            {
                Id = Guid.NewGuid(),
                AwardId = awardId,
                DocumentId = docId,
                CoreDocumentRole = "Award"
            });
            await db.SaveChangesAsync();
        }

        // 3. Create user with AwardView + AuditView with Workstream scope
        var (client, userId) = await CreateScopedUserClientAsync(
            $"usr_aw_{Guid.NewGuid():N}"[..12],
            $"R_AW_{Guid.NewGuid():N}"[..10],
            ScopeMode.Workstream,
            workstreamId: awardWsId,
            permissions: [PermissionCodes.AwardView, PermissionCodes.AuditView]
        );

        // 4. Access protected document content (inline open)
        var res = await client.GetAsync($"/api/documents/{docId}/content");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // 5. Verify RecordAccessEvent in DB with dynamic WorkstreamId
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var accessEvent = await db.RecordAccessEvents.FirstOrDefaultAsync(e => e.DocumentId == docId && e.ActorUserId == userId);
            Assert.NotNull(accessEvent);
            Assert.Equal(awardWsId, accessEvent.WorkstreamId);
            Assert.Equal(awardWsName, accessEvent.WorkstreamNameSnapshot);
            Assert.Equal(RecordAccessAction.Opened, accessEvent.Action);
            Assert.Equal("award_protected_doc.pdf", accessEvent.DocumentTitleSnapshot);
        }

        // 6. Verify event appears in Team Activity for Workstream-scoped user
        var teamRes = await client.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, teamRes.StatusCode);
        var feed = await teamRes.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);
        Assert.Contains(feed.Items, i => i.EntityId == docId && i.IsReadEvent && i.WorkstreamId == awardWsId);

        // 7. Access with download=true -> records Downloaded
        var dlRes = await client.GetAsync($"/api/documents/{docId}/content?download=true");
        Assert.Equal(HttpStatusCode.OK, dlRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var dlEvent = await db.RecordAccessEvents.FirstOrDefaultAsync(e => e.DocumentId == docId && e.ActorUserId == userId && e.Action == RecordAccessAction.Downloaded);
            Assert.NotNull(dlEvent);
            Assert.Equal(awardWsId, dlEvent.WorkstreamId);
        }
    }

    [Fact]
    public async Task DocumentAccess_UnauthorizedAccess_CreatesZeroAccessEvents()
    {
        Guid docId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Documents.Add(new Document
            {
                Id = docId,
                DocumentType = "AWARD",
                OriginalFileName = "unauth_test.pdf",
                StoragePath = "unauth_test.pdf",
                Status = "Active",
                RecordStatus = RecordStatus.Active
            });
            await db.SaveChangesAsync();
        }

        // 1. Unauthenticated client
        var anonClient = _factory.CreateClient();
        var unauthRes = await anonClient.GetAsync($"/api/documents/{docId}/content");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

        // 2. Non-privileged authenticated client
        var (nopermClient, _) = await CreateScopedUserClientAsync(
            $"usr_noperm_{Guid.NewGuid():N}"[..12],
            $"R_NOPERM_{Guid.NewGuid():N}"[..10],
            ScopeMode.Assigned,
            permissions: [PermissionCodes.DakView] // lacks AwardView
        );
        var forbidRes = await nopermClient.GetAsync($"/api/documents/{docId}/content");
        Assert.Equal(HttpStatusCode.Forbidden, forbidRes.StatusCode);

        // 3. Verify exactly 0 access events logged
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var count = await db.RecordAccessEvents.CountAsync(e => e.DocumentId == docId);
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task DocumentAccess_OpenAfterDedupWindow_CreatesNewEvent()
    {
        Guid docId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Documents.Add(new Document
            {
                Id = docId,
                DocumentType = "NOTE",
                OriginalFileName = "window_test.pdf",
                StoragePath = "window_test.pdf"
            });
            // Initial event seeded directly from an older 5-minute bucket
            db.RecordAccessEvents.Add(new RecordAccessEvent
            {
                Id = Guid.NewGuid(),
                ActorUserId = SeedData.BootstrapAdminId,
                Action = RecordAccessAction.Opened,
                DocumentId = docId,
                DeduplicationKey = $"{SeedData.BootstrapAdminId}:{docId}:None:None:1",
                OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                DocumentTitleSnapshot = "window_test.pdf",
                ActorDisplayNameSnapshot = "Admin User"
            });
            await db.SaveChangesAsync();
        }

        var cmd = new RecordAccessCommand(
            ActorUserId: SeedData.BootstrapAdminId,
            Action: RecordAccessAction.Opened,
            DocumentId: docId,
            DocumentTitleSnapshot: "window_test.pdf"
        );

        // Open in current bucket creates a second event
        using (var scope = _factory.Services.CreateScope())
        {
            var logger = scope.ServiceProvider.GetRequiredService<IRecordAccessLogger>();
            await logger.LogAccessAsync(cmd);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var count = await db.RecordAccessEvents.CountAsync(e => e.DocumentId == docId && e.Action == RecordAccessAction.Opened);
            Assert.Equal(2, count);
        }
    }

    [Fact]
    public async Task DocumentAccess_ConcurrentDuplicateOpen_CollapsesToOneEvent()
    {
        Guid docId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Documents.Add(new Document
            {
                Id = docId,
                DocumentType = "NOTE",
                OriginalFileName = "concurrent_test.pdf",
                StoragePath = "concurrent_test.pdf"
            });
            await db.SaveChangesAsync();
        }

        var cmd = new RecordAccessCommand(
            ActorUserId: SeedData.BootstrapAdminId,
            Action: RecordAccessAction.Opened,
            DocumentId: docId,
            DocumentTitleSnapshot: "concurrent_test.pdf"
        );

        // Run 8 concurrent log attempts in parallel
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<IRecordAccessLogger>();
            await logger.LogAccessAsync(cmd);
        })).ToArray();

        await Task.WhenAll(tasks);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var count = await db.RecordAccessEvents.CountAsync(e => e.DocumentId == docId);
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task DocumentAccess_AnotherUserAccess_AbsentFromMyHistory()
    {
        Guid docId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Documents.Add(new Document
            {
                Id = docId,
                DocumentType = "NOTE",
                OriginalFileName = "user_isolation.pdf",
                StoragePath = "user_isolation.pdf"
            });
            await db.SaveChangesAsync();
        }

        var (clientA, userAId) = await CreateScopedUserClientAsync(
            $"usr_iso_a_{Guid.NewGuid():N}"[..12],
            $"R_ISO_A_{Guid.NewGuid():N}"[..10],
            ScopeMode.Own
        );

        var (clientB, _) = await CreateScopedUserClientAsync(
            $"usr_iso_b_{Guid.NewGuid():N}"[..12],
            $"R_ISO_B_{Guid.NewGuid():N}"[..10],
            ScopeMode.Own
        );

        // User A accesses document
        using (var scope = _factory.Services.CreateScope())
        {
            var logger = scope.ServiceProvider.GetRequiredService<IRecordAccessLogger>();
            await logger.LogAccessAsync(new RecordAccessCommand(
                ActorUserId: userAId,
                Action: RecordAccessAction.Opened,
                DocumentId: docId,
                DocumentTitleSnapshot: "user_isolation.pdf"
            ));
        }

        // User B queries My History
        var resB = await clientB.GetAsync("/api/activity/my-history");
        Assert.Equal(HttpStatusCode.OK, resB.StatusCode);
        var feedB = await resB.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feedB);
        Assert.DoesNotContain(feedB.Items, i => i.EntityId == docId);

        // User A queries My History -> sees the event
        var resA = await clientA.GetAsync("/api/activity/my-history");
        Assert.Equal(HttpStatusCode.OK, resA.StatusCode);
        var feedA = await resA.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feedA);
        Assert.Contains(feedA.Items, i => i.EntityId == docId);
    }

    [Fact]
    public async Task ActorDisplayNameSnapshot_SurvivesActorRename()
    {
        var (client, userId) = await CreateScopedUserClientAsync(
            $"usr_ren_{Guid.NewGuid():N}"[..12],
            $"R_REN_{Guid.NewGuid():N}"[..10],
            ScopeMode.Own
        );

        Guid docId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Documents.Add(new Document
            {
                Id = docId,
                DocumentType = "NOTE",
                OriginalFileName = "rename_test.pdf",
                StoragePath = "rename_test.pdf"
            });
            await db.SaveChangesAsync();

            var logger = scope.ServiceProvider.GetRequiredService<IRecordAccessLogger>();
            await logger.LogAccessAsync(new RecordAccessCommand(
                ActorUserId: userId,
                Action: RecordAccessAction.Opened,
                DocumentId: docId,
                DocumentTitleSnapshot: "rename_test.pdf",
                ActorDisplayNameSnapshot: "Officer Original Name"
            ));
        }

        // Rename the user in AppUsers
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var u = await db.AppUsers.FindAsync(userId);
            Assert.NotNull(u);
            u.DisplayName = "Officer Mutated Name";
            await db.SaveChangesAsync();
        }

        // In My History, ActorDisplayName must still reflect the immutable snapshot
        var res = await client.GetAsync("/api/activity/my-history");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);
        var item = feed.Items.First(i => i.EntityId == docId);
        Assert.Equal("Officer Original Name", item.ActorDisplayName);
    }

    [Fact]
    public async Task WorkItem_HistoricalDisplay_DoesNotChangeAfterDeskOrWorkstreamRename()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_REN_{Guid.NewGuid():N}"[..10], "Original Workstream Name");
        var desk1 = await CreateDeskAsync($"D1_REN_{Guid.NewGuid():N}"[..10], "Desk One Original", ws.Id);
        var desk2 = await CreateDeskAsync($"D2_REN_{Guid.NewGuid():N}"[..10], "Desk Two Original", ws.Id);

        Guid workItemId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var wi = new WorkItem
            {
                Id = workItemId,
                WorkstreamId = ws.Id,
                Title = "WorkItem Historical Name Test",
                Status = WorkItemStatus.InProgress,
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.WorkItems.Add(wi);

            db.WorkItemEvents.Add(new WorkItemEvent
            {
                WorkItemId = workItemId,
                SequenceNumber = 1,
                Action = WorkItemEventAction.Reassigned,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin",
                ActionAt = DateTimeOffset.UtcNow,
                SourceDeskId = desk1.Id,
                SourceDeskNameSnapshot = "Desk One Original",
                TargetDeskId = desk2.Id,
                TargetDeskNameSnapshot = "Desk Two Original"
            });
            await db.SaveChangesAsync();
        }

        // Rename Desk and Workstream in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var d1 = await db.OfficeDesks.FindAsync(desk1.Id);
            var d2 = await db.OfficeDesks.FindAsync(desk2.Id);
            var w = await db.Workstreams.FindAsync(ws.Id);
            d1!.Name = "Desk One Mutated";
            d2!.Name = "Desk Two Mutated";
            w!.Name = "Mutated Workstream Name";
            await db.SaveChangesAsync();
        }

        // Query Team Activity as Admin
        var res = await adminClient.GetAsync($"/api/activity/team?workstreamId={ws.Id}&entityType=WorkItem");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);
        var item = feed.Items.First(i => i.EntityId == workItemId);

        // Immutable truth: DeskName must match snapshot ("Desk Two Original"), never mutated name
        Assert.Equal("Desk Two Original", item.DeskName);
        // Mutable Workstream name is suppressed (null)
        Assert.Null(item.WorkstreamName);
    }

    [Fact]
    public async Task TeamActivity_ScopeUnion_WorkstreamPlusAssigned_BehavesAsUnion()
    {
        var ws1 = await CreateWorkstreamAsync($"WS_U1_{Guid.NewGuid():N}"[..10], "Workstream 1");
        var desk1 = await CreateDeskAsync($"D_U1_{Guid.NewGuid():N}"[..10], "Desk 1 in WS1", ws1.Id);

        var ws2 = await CreateWorkstreamAsync($"WS_U2_{Guid.NewGuid():N}"[..10], "Workstream 2");
        var desk2 = await CreateDeskAsync($"D_U2_{Guid.NewGuid():N}"[..10], "Desk 2 in WS2", ws2.Id);
        var desk3 = await CreateDeskAsync($"D_U3_{Guid.NewGuid():N}"[..10], "Desk 3 in WS2", ws2.Id);

        // Create user with union of Workstream scope on WS1 AND Assigned scope on Desk2
        var (client, userId) = await CreateScopedUserClientAsync(
            $"usr_union_{Guid.NewGuid():N}"[..12],
            $"R_UNION_{Guid.NewGuid():N}"[..10],
            ScopeMode.Workstream,
            deskId: desk2.Id,
            workstreamId: ws1.Id,
            permissions: [PermissionCodes.AuditView]
        );

        // Add second RolePermission with ScopeMode.Assigned to the user's role
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var auditPerm = await db.Permissions.FirstAsync(p => p.Code == PermissionCodes.AuditView);
            var user = await db.AppUsers.Include(u => u.UserRoles).FirstAsync(u => u.Id == userId);
            var roleId = user.UserRoles.First().RoleId;
            db.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = auditPerm.Id,
                ScopeMode = ScopeMode.Assigned
            });
            await db.SaveChangesAsync();
        }

        var village = await CreateVillageAsync("Union Village");
        Guid dak1Id = Guid.NewGuid();
        Guid dak2Id = Guid.NewGuid();
        Guid dak3Id = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            // Event 1: In WS1 (Desk 1)
            var dak1 = new Dak { Id = dak1Id, DiaryNumber = $"DK-U1-{Guid.NewGuid():N}"[..12], Subject = "Dak WS1", SenderName = "Sender", Status = DakStatus.Registered, WorkstreamId = ws1.Id };
            db.Daks.Add(dak1);
            db.DakMovements.Add(new DakMovement
            {
                DakId = dak1Id,
                Action = DakMovementAction.Registered,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin",
                ActionAt = DateTimeOffset.UtcNow,
                WorkstreamIdSnapshot = ws1.Id,
                WorkstreamNameSnapshot = ws1.Name,
                ToDeskId = desk1.Id,
                ToDeskNameSnapshot = desk1.Name
            });

            // Event 2: In WS2 on Desk 2 (user's desk)
            var dak2 = new Dak { Id = dak2Id, DiaryNumber = $"DK-U2-{Guid.NewGuid():N}"[..12], Subject = "Dak WS2 Desk2", SenderName = "Sender", Status = DakStatus.Registered, WorkstreamId = ws2.Id };
            db.Daks.Add(dak2);
            db.DakMovements.Add(new DakMovement
            {
                DakId = dak2Id,
                Action = DakMovementAction.Registered,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin",
                ActionAt = DateTimeOffset.UtcNow,
                WorkstreamIdSnapshot = ws2.Id,
                WorkstreamNameSnapshot = ws2.Name,
                ToDeskId = desk2.Id,
                ToDeskNameSnapshot = desk2.Name
            });

            // Event 3: In WS2 on Desk 3 (different desk, unauthorized)
            var dak3 = new Dak { Id = dak3Id, DiaryNumber = $"DK-U3-{Guid.NewGuid():N}"[..12], Subject = "Dak WS2 Desk3", SenderName = "Sender", Status = DakStatus.Registered, WorkstreamId = ws2.Id };
            db.Daks.Add(dak3);
            db.DakMovements.Add(new DakMovement
            {
                DakId = dak3Id,
                Action = DakMovementAction.Registered,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin",
                ActionAt = DateTimeOffset.UtcNow,
                WorkstreamIdSnapshot = ws2.Id,
                WorkstreamNameSnapshot = ws2.Name,
                ToDeskId = desk3.Id,
                ToDeskNameSnapshot = desk3.Name
            });

            await db.SaveChangesAsync();
        }

        var res = await client.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);

        // Union: WS1 event is visible (via Workstream scope)
        Assert.Contains(feed.Items, i => i.EntityId == dak1Id);
        // Union: Desk2 event is visible (via Assigned desk scope)
        Assert.Contains(feed.Items, i => i.EntityId == dak2Id);
        // Desk3 in WS2 is NOT visible
        Assert.DoesNotContain(feed.Items, i => i.EntityId == dak3Id);
    }

    [Fact]
    public async Task TeamActivity_ContributorRequesterRelationship_DoesNotExpandTeamActivity()
    {
        var ws1 = await CreateWorkstreamAsync($"WS_C1_{Guid.NewGuid():N}"[..10], "WS Contrib 1");
        var desk1 = await CreateDeskAsync($"D_C1_{Guid.NewGuid():N}"[..10], "Desk Contrib 1", ws1.Id);

        var ws2 = await CreateWorkstreamAsync($"WS_C2_{Guid.NewGuid():N}"[..10], "WS Contrib 2");
        var desk2 = await CreateDeskAsync($"D_C2_{Guid.NewGuid():N}"[..10], "Desk Contrib 2", ws2.Id);

        // User is scoped to Desk 2
        var (client, userId) = await CreateScopedUserClientAsync(
            $"usr_cnt_{Guid.NewGuid():N}"[..12],
            $"R_CNT_{Guid.NewGuid():N}"[..10],
            ScopeMode.Assigned,
            deskId: desk2.Id,
            permissions: [PermissionCodes.AuditView]
        );

        Guid workItemId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var wi = new WorkItem
            {
                Id = workItemId,
                WorkstreamId = ws1.Id,
                Title = "WorkItem External Contributor",
                Status = WorkItemStatus.InProgress,
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.WorkItems.Add(wi);

            // Add Desk 1 assignment
            db.WorkItemAssignments.Add(new WorkItemAssignment
            {
                WorkItemId = workItemId,
                OfficeDeskId = desk1.Id,
                AssignedAt = DateTimeOffset.UtcNow.AddHours(-1),
                IsActive = true,
                RecordStatus = RecordStatus.Active
            });

            // Add caller as a contributor
            db.WorkItemContributors.Add(new WorkItemContributor
            {
                WorkItemId = workItemId,
                UserId = userId,
                AddedByUserId = SeedData.BootstrapAdminId,
                IsActive = true,
                Status = WorkItemContributorStatus.Active
            });

            db.WorkItemEvents.Add(new WorkItemEvent
            {
                WorkItemId = workItemId,
                SequenceNumber = 1,
                Action = WorkItemEventAction.Created,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin",
                ActionAt = DateTimeOffset.UtcNow,
                SourceDeskId = desk1.Id,
                SourceDeskNameSnapshot = "Desk Contrib 1"
            });
            await db.SaveChangesAsync();
        }

        // Query Team Activity: Desk 1 event MUST NOT appear
        var res = await client.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);
        Assert.DoesNotContain(feed.Items, i => i.EntityId == workItemId);
    }

    [Fact]
    public async Task TeamActivity_InactiveOrRemovedMemberships_FailClosed()
    {
        var ws = await CreateWorkstreamAsync($"WS_INACT_{Guid.NewGuid():N}"[..10], "Inactive Membership WS");
        var desk = await CreateDeskAsync($"D_INACT_{Guid.NewGuid():N}"[..10], "Inactive Membership Desk", ws.Id);

        var (client, userId) = await CreateScopedUserClientAsync(
            $"usr_rem_{Guid.NewGuid():N}"[..12],
            $"R_REM_{Guid.NewGuid():N}"[..10],
            ScopeMode.Assigned,
            deskId: desk.Id,
            permissions: [PermissionCodes.AuditView]
        );

        // Mark desk membership as removed / inactive
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var m = await db.UserDeskMemberships.FirstAsync(udm => udm.UserId == userId && udm.OfficeDeskId == desk.Id);
            m.RemovedAt = DateTimeOffset.UtcNow;
            m.IsActive = false;
            await db.SaveChangesAsync();
        }

        var village = await CreateVillageAsync("Removed Village");
        Guid dakId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var dak = new Dak { Id = dakId, DiaryNumber = $"DK-RM-{Guid.NewGuid():N}"[..12], Subject = "Dak RM", SenderName = "Sender", Status = DakStatus.Registered, WorkstreamId = ws.Id };
            db.Daks.Add(dak);
            db.DakMovements.Add(new DakMovement
            {
                DakId = dakId,
                Action = DakMovementAction.Registered,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin",
                ActionAt = DateTimeOffset.UtcNow,
                ToDeskId = desk.Id,
                ToDeskNameSnapshot = desk.Name
            });
            await db.SaveChangesAsync();
        }

        // Fails closed: removed desk membership means 0 events returned
        var res = await client.GetAsync("/api/activity/team");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);
        Assert.Empty(feed.Items);
    }

    [Fact]
    public async Task TeamActivity_FiltersNeverExpandAuthorization()
    {
        var wsAuthorized = await CreateWorkstreamAsync($"WS_AUTH_{Guid.NewGuid():N}"[..10], "Authorized WS");
        var wsUnauthorized = await CreateWorkstreamAsync($"WS_UNAUTH_{Guid.NewGuid():N}"[..10], "Unauthorized WS");

        var (client, _) = await CreateScopedUserClientAsync(
            $"usr_flt_{Guid.NewGuid():N}"[..12],
            $"R_FLT_{Guid.NewGuid():N}"[..10],
            ScopeMode.Workstream,
            workstreamId: wsAuthorized.Id,
            permissions: [PermissionCodes.AuditView]
        );

        var village = await CreateVillageAsync("Filter Sec Village");
        Guid unauthMatterId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Matters.Add(new Matter
            {
                Id = unauthMatterId,
                VillageId = village.Id,
                ReferenceNumber = "MAT-SEC-001",
                Title = "Unauthorized Matter",
                MatterType = "CIVIL",
                WorkstreamId = wsUnauthorized.Id
            });
            db.MatterEvents.Add(new MatterEvent
            {
                MatterId = unauthMatterId,
                SequenceNumber = 1,
                Action = MatterEventAction.Created,
                ActionByUserId = SeedData.BootstrapAdminId,
                ActionByDisplayNameSnapshot = "Admin",
                ActionAt = DateTimeOffset.UtcNow,
                WorkstreamIdSnapshot = wsUnauthorized.Id,
                WorkstreamNameSnapshot = wsUnauthorized.Name
            });
            await db.SaveChangesAsync();
        }

        // User queries with parameter pointing to unauthorized workstream
        var res = await client.GetAsync($"/api/activity/team?workstreamId={wsUnauthorized.Id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);
        Assert.Empty(feed.Items);
    }

    [Fact]
    public async Task TeamActivity_DelhiDateBoundary_ISTMidnightSemantics()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_DEL_{Guid.NewGuid():N}"[..10], "Delhi Date WS");
        var village = await CreateVillageAsync("Delhi Village");

        // Indian Standard Time is UTC + 5:30.
        // Target Delhi date: 2026-11-20
        // Delhi 2026-11-20 00:00:00 IST = 2026-11-19 18:30:00 UTC
        // Delhi 2026-11-20 23:59:59 IST = 2026-11-20 18:29:59 UTC
        // Next Delhi 2026-11-21 00:00:00 IST = 2026-11-20 18:30:00 UTC

        var timePre = new DateTimeOffset(2026, 11, 19, 18, 29, 59, TimeSpan.Zero);  // Delhi Nov 19 23:59:59
        var timeInside = new DateTimeOffset(2026, 11, 19, 18, 30, 01, TimeSpan.Zero); // Delhi Nov 20 00:00:01
        var timePost = new DateTimeOffset(2026, 11, 20, 18, 30, 01, TimeSpan.Zero); // Delhi Nov 21 00:00:01

        Guid idPre = Guid.NewGuid();
        Guid idInside = Guid.NewGuid();
        Guid idPost = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            void AddMatter(Guid id, string title, DateTimeOffset eventTime)
            {
                db.Matters.Add(new Matter
                {
                    Id = id,
                    VillageId = village.Id,
                    ReferenceNumber = $"MAT-{id:N}"[..12],
                    Title = title,
                    MatterType = "CIVIL",
                    WorkstreamId = ws.Id
                });
                db.MatterEvents.Add(new MatterEvent
                {
                    MatterId = id,
                    SequenceNumber = 1,
                    Action = MatterEventAction.Created,
                    ActionByUserId = SeedData.BootstrapAdminId,
                    ActionByDisplayNameSnapshot = "Admin",
                    ActionAt = eventTime,
                    WorkstreamIdSnapshot = ws.Id,
                    WorkstreamNameSnapshot = ws.Name
                });
            }

            AddMatter(idPre, "Matter Pre-Midnight Delhi", timePre);
            AddMatter(idInside, "Matter Inside Delhi Day", timeInside);
            AddMatter(idPost, "Matter Post-Midnight Delhi", timePost);

            await db.SaveChangesAsync();
        }

        // Query with fromDate=2026-11-20 and toDate=2026-11-20
        var res = await adminClient.GetAsync($"/api/activity/team?workstreamId={ws.Id}&fromDate=2026-11-20&toDate=2026-11-20");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var feed = await res.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(feed);

        Assert.Contains(feed.Items, i => i.EntityId == idInside);
        Assert.DoesNotContain(feed.Items, i => i.EntityId == idPre);
        Assert.DoesNotContain(feed.Items, i => i.EntityId == idPost);
    }
}
