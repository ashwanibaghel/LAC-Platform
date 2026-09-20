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
    public async Task DocumentAccess_OpenAndPreview_DeduplicatedWithin5Minutes()
    {
        var adminClient = await CreateAdminClientAsync();
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

        var req = new LogAccessRequest(
            DocumentId: docId,
            Action: RecordAccessAction.Opened,
            ContextEntityType: "matter",
            ContextEntityId: matterId,
            DocumentTitle: "dedup_test.pdf"
        );

        // First call
        var res1 = await adminClient.PostAsJsonAsync("/api/activity/log-access", req);
        Assert.Equal(HttpStatusCode.Accepted, res1.StatusCode);

        // Immediate second call (within 5-min bucket)
        var res2 = await adminClient.PostAsJsonAsync("/api/activity/log-access", req);
        Assert.Equal(HttpStatusCode.Accepted, res2.StatusCode);

        // Check database count
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
        var adminClient = await CreateAdminClientAsync();
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

        var req = new LogAccessRequest(
            DocumentId: docId,
            Action: RecordAccessAction.Downloaded,
            ContextEntityType: "matter",
            ContextEntityId: matterId,
            DocumentTitle: "download_test.pdf"
        );

        // First call
        var res1 = await adminClient.PostAsJsonAsync("/api/activity/log-access", req);
        Assert.Equal(HttpStatusCode.Accepted, res1.StatusCode);

        // Second call
        var res2 = await adminClient.PostAsJsonAsync("/api/activity/log-access", req);
        Assert.Equal(HttpStatusCode.Accepted, res2.StatusCode);

        // Check database count: must be exactly 2 because Downloads are never deduplicated
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var count = await db.RecordAccessEvents.CountAsync(e => e.DocumentId == docId && e.Action == RecordAccessAction.Downloaded);
            Assert.Equal(2, count);
        }
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
}
