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
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

public sealed class Phase2HTestFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"phase2h-tests-{Guid.NewGuid()}";
    public const string TestAdminUser = "phase2h_admin";
    public const string TestAdminPass = "Phase2HAdminPass!123";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = TestAdminUser,
                ["BootstrapAdmin:Password"] = TestAdminPass,
                ["BootstrapAdmin:DisplayName"] = "Phase2H Test Administrator"
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

public sealed class Phase2HTests : IClassFixture<Phase2HTestFactory>
{
    private readonly Phase2HTestFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Phase2HTests(Phase2HTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(Phase2HTestFactory.TestAdminUser, Phase2HTestFactory.TestAdminPass));
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
                    Category = "Test"
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
                IsPrimary = true,
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
                IsPrimary = true,
                AssignedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        if (isActive)
        {
            var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        return (client, user);
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

    // =========================================================================
    // 1. SCHEDULE AUTHORIZATION TESTS
    // =========================================================================

    [Fact]
    public async Task CreateScheduledEvent_AuthorizedWithScopeAll_Succeeds()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_{Guid.NewGuid():N}", "All Scope WS");
        var desk = await CreateDeskAsync($"DSK_{Guid.NewGuid():N}", "All Scope Desk", ws.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var req = new CreateScheduledEventApiRequest(
            ws.Id,
            desk.Id,
            null,
            "CourtHearing",
            "High Court Compliance Hearing",
            "Urgent hearing compliance",
            today.AddDays(3),
            new TimeOnly(10, 30),
            "Urgent"
        );

        var res = await admin.PostAsJsonAsync("/api/scheduled-events", req);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var created = await res.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(created);
        Assert.Equal("High Court Compliance Hearing", created.Title);
        Assert.Equal("Scheduled", created.Status);
        Assert.Equal(1, created.Revision);
    }

    [Fact]
    public async Task CreateScheduledEvent_ScopeModeWorkstream_EnforcesWorkstreamBoundary()
    {
        var wsAllowed = await CreateWorkstreamAsync($"WS_A_{Guid.NewGuid():N}", "Allowed WS");
        var wsDenied = await CreateWorkstreamAsync($"WS_D_{Guid.NewGuid():N}", "Denied WS");
        var deskAllowed = await CreateDeskAsync($"DSK_A_{Guid.NewGuid():N}", "Allowed Desk", wsAllowed.Id);

        var (client, _) = await CreateUserWithPermissionsAsync(
            $"ws_user_{Guid.NewGuid():N}",
            "UserPass123!",
            new[]
            {
                (PermissionCodes.ScheduleView, ScopeMode.Workstream),
                (PermissionCodes.ScheduleCreate, ScopeMode.Workstream)
            },
            workstreamId: wsAllowed.Id,
            deskId: deskAllowed.Id
        );

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Allowed in user's workstream
        var allowedReq = new CreateScheduledEventApiRequest(
            wsAllowed.Id,
            deskAllowed.Id,
            null,
            "ComplianceDeadline",
            "Allowed Workstream Event",
            null,
            today.AddDays(5),
            null,
            "Routine"
        );
        var allowedRes = await client.PostAsJsonAsync("/api/scheduled-events", allowedReq);
        Assert.Equal(HttpStatusCode.Created, allowedRes.StatusCode);

        // Denied in other workstream
        var deniedReq = new CreateScheduledEventApiRequest(
            wsDenied.Id,
            null,
            null,
            "ComplianceDeadline",
            "Denied Workstream Event",
            null,
            today.AddDays(5),
            null,
            "Routine"
        );
        var deniedRes = await client.PostAsJsonAsync("/api/scheduled-events", deniedReq);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRes.StatusCode);
    }

    [Fact]
    public async Task CreateScheduledEvent_ScopeModeAssigned_AllowsOnlyMemberDesks()
    {
        var ws = await CreateWorkstreamAsync($"WS_AS_{Guid.NewGuid():N}", "Assigned WS");
        var myDesk = await CreateDeskAsync($"DSK_MINE_{Guid.NewGuid():N}", "My Member Desk", ws.Id);
        var otherDesk = await CreateDeskAsync($"DSK_OTHER_{Guid.NewGuid():N}", "Other Desk", ws.Id);

        var (client, _) = await CreateUserWithPermissionsAsync(
            $"assigned_user_{Guid.NewGuid():N}",
            "UserPass123!",
            new[]
            {
                (PermissionCodes.ScheduleView, ScopeMode.Assigned),
                (PermissionCodes.ScheduleCreate, ScopeMode.Assigned)
            },
            workstreamId: ws.Id,
            deskId: myDesk.Id
        );

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Allowed on desk where caller is active member
        var okReq = new CreateScheduledEventApiRequest(
            ws.Id,
            myDesk.Id,
            null,
            "SiteInspection",
            "Inspection Event",
            null,
            today.AddDays(2),
            null,
            "Routine"
        );
        var okRes = await client.PostAsJsonAsync("/api/scheduled-events", okReq);
        Assert.Equal(HttpStatusCode.Created, okRes.StatusCode);

        // Denied on other desk
        var failReq = new CreateScheduledEventApiRequest(
            ws.Id,
            otherDesk.Id,
            null,
            "SiteInspection",
            "Inspection Event 2",
            null,
            today.AddDays(2),
            null,
            "Routine"
        );
        var failRes = await client.PostAsJsonAsync("/api/scheduled-events", failReq);
        Assert.Equal(HttpStatusCode.Forbidden, failRes.StatusCode);
    }

    [Fact]
    public async Task ScheduleView_ScopeModeOwn_FailsClosed_ReturnsEmpty()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_OWN_{Guid.NewGuid():N}", "Own Scope WS");
        var desk = await CreateDeskAsync($"DSK_OWN_{Guid.NewGuid():N}", "Own Scope Desk", ws.Id);

        var (client, user) = await CreateUserWithPermissionsAsync(
            $"own_user_{Guid.NewGuid():N}",
            "UserPass123!",
            new[] { (PermissionCodes.ScheduleView, ScopeMode.Own) },
            workstreamId: ws.Id,
            deskId: desk.Id
        );

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Admin creates event assigned to desk and user
        await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, user.Id, "CourtHearing", "Hearing for Own User", null, today.AddDays(2), null, "Urgent"
        ));

        // Calling GetMyAttention should fail closed on Schedule.View with ScopeMode.Own (no personal fallback)
        var res = await client.GetAsync("/api/attention/my");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var feed = await res.Content.ReadFromJsonAsync<AttentionFeedResult>(JsonOpts);
        Assert.NotNull(feed);
        // Scheduled events must be empty because ScopeMode.Own fails closed
        Assert.DoesNotContain(feed.Items, i => i.SourceType == "ScheduledEvent");
    }

    [Fact]
    public async Task InactiveUser_FailsClosed_OnAllScheduleAndAttentionEndpoints()
    {
        var ws = await CreateWorkstreamAsync($"WS_INACT_U_{Guid.NewGuid():N}", "Inactive WS");
        var desk = await CreateDeskAsync($"DSK_INACT_U_{Guid.NewGuid():N}", "Inactive Desk", ws.Id);

        // Create active user, then mark inactive directly in DB
        var (client, user) = await CreateUserWithPermissionsAsync(
            $"inact_user_{Guid.NewGuid():N}",
            "UserPass123!",
            new[]
            {
                (PermissionCodes.ScheduleView, ScopeMode.All),
                (PermissionCodes.ScheduleCreate, ScopeMode.All)
            },
            workstreamId: ws.Id,
            deskId: desk.Id
        );

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var u = await db.AppUsers.FirstAsync(x => x.Id == user.Id);
            u.IsActive = false;
            await db.SaveChangesAsync();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var attentionRes = await client.GetAsync("/api/attention/my");
        Assert.Equal(HttpStatusCode.Forbidden, attentionRes.StatusCode);

        var calendarRes = await client.GetAsync("/api/scheduled-events/calendar");
        Assert.Equal(HttpStatusCode.Forbidden, calendarRes.StatusCode);

        var createRes = await client.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "CourtHearing", "Inactive Test", null, today.AddDays(1), null, "Routine"
        ));
        Assert.Equal(HttpStatusCode.Forbidden, createRes.StatusCode);
    }

    [Fact]
    public async Task CreateScheduledEvent_InactiveWorkstreamOrDesk_FailsClosed()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_INACT_{Guid.NewGuid():N}", "Inactive WS");
        var desk = await CreateDeskAsync($"DSK_INACT_{Guid.NewGuid():N}", "Inactive Desk", ws.Id);

        // Deactivate workstream
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var wsEntity = await db.Workstreams.FirstAsync(w => w.Id == ws.Id);
            wsEntity.IsActive = false;
            await db.SaveChangesAsync();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var req = new CreateScheduledEventApiRequest(
            ws.Id,
            desk.Id,
            null,
            "Meeting",
            "Meeting with Landowners",
            null,
            today.AddDays(1),
            null,
            "Routine"
        );

        var res = await admin.PostAsJsonAsync("/api/scheduled-events", req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task CreateScheduledEvent_NamedHandler_ValidMemberAccepted_NonMemberRejected()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_NH_{Guid.NewGuid():N}", "Named Handler WS");
        var desk = await CreateDeskAsync($"DSK_NH_{Guid.NewGuid():N}", "Named Handler Desk", ws.Id);

        var (_, memberUser) = await CreateUserWithPermissionsAsync(
            $"member_{Guid.NewGuid():N}",
            "MemberPass123!",
            new[] { (PermissionCodes.ScheduleView, ScopeMode.Assigned) },
            workstreamId: ws.Id,
            deskId: desk.Id
        );

        var (_, nonMemberUser) = await CreateUserWithPermissionsAsync(
            $"nonmember_{Guid.NewGuid():N}",
            "NonMemberPass123!",
            new[] { (PermissionCodes.ScheduleView, ScopeMode.Assigned) },
            workstreamId: ws.Id,
            deskId: null
        );

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Member accepted
        var okReq = new CreateScheduledEventApiRequest(
            ws.Id,
            desk.Id,
            memberUser.Id,
            "OrderDelivery",
            "Order Delivery with Member",
            null,
            today.AddDays(4),
            null,
            "Routine"
        );
        var okRes = await admin.PostAsJsonAsync("/api/scheduled-events", okReq);
        Assert.Equal(HttpStatusCode.Created, okRes.StatusCode);

        // Non-member rejected (403 forbidden - named handler must be active desk member)
        var badReq = new CreateScheduledEventApiRequest(
            ws.Id,
            desk.Id,
            nonMemberUser.Id,
            "OrderDelivery",
            "Order Delivery with Non-Member",
            null,
            today.AddDays(4),
            null,
            "Routine"
        );
        var badRes = await admin.PostAsJsonAsync("/api/scheduled-events", badReq);
        Assert.Equal(HttpStatusCode.Forbidden, badRes.StatusCode);
    }

    // =========================================================================
    // 2. LIFECYCLE, CONCURRENCY & REASSIGNMENT TESTS
    // =========================================================================

    [Fact]
    public async Task Reschedule_AdvancesRevisionAndRecordsHistory()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_RS_{Guid.NewGuid():N}", "Reschedule WS");
        var desk = await CreateDeskAsync($"DSK_RS_{Guid.NewGuid():N}", "Reschedule Desk", ws.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var createRes = await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "CourtHearing", "Hearing to Reschedule", null, today.AddDays(2), new TimeOnly(11, 0), "Urgent"
        ));
        var evt = await createRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(evt);

        // Reschedule to 5 days later
        var newDate = today.AddDays(7);
        var reschedRes = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt.Id}/reschedule", new RescheduleEventApiRequest(
            newDate, new TimeOnly(14, 0), "Adjourned on request of state counsel", evt.Revision
        ));
        Assert.Equal(HttpStatusCode.OK, reschedRes.StatusCode);

        var updated = await reschedRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(updated);
        Assert.Equal(newDate, updated.ScheduledDate);
        Assert.Equal("Scheduled", updated.Status);
        Assert.Equal(2, updated.Revision);

        // Check history sequence and action
        Assert.Contains(updated.History, h => h.Action == "Rescheduled" && h.Reason != null && h.Reason.Contains("state counsel"));
        Assert.True(updated.History.All(h => h.SequenceNumber > 0));
    }

    [Fact]
    public async Task Concurrency_ExpectedRevisionMismatch_Returns409Conflict()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_CONC_{Guid.NewGuid():N}", "Concurrency WS");
        var desk = await CreateDeskAsync($"DSK_CONC_{Guid.NewGuid():N}", "Concurrency Desk", ws.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var createRes = await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "Meeting", "Concurrency Meeting", null, today.AddDays(3), null, "Routine"
        ));
        var evt = await createRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(evt);

        // 1. Reschedule mismatch
        var reschedConflict = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt.Id}/reschedule", new RescheduleEventApiRequest(
            today.AddDays(4), null, "Invalid revision test", 999
        ));
        Assert.Equal(HttpStatusCode.Conflict, reschedConflict.StatusCode);

        // 2. Complete mismatch
        var completeConflict = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt.Id}/complete", new CompleteEventApiRequest(
            "Notes", 999
        ));
        Assert.Equal(HttpStatusCode.Conflict, completeConflict.StatusCode);

        // 3. Cancel mismatch
        var cancelConflict = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt.Id}/cancel", new CancelEventApiRequest(
            "Reason", 999
        ));
        Assert.Equal(HttpStatusCode.Conflict, cancelConflict.StatusCode);
    }

    [Fact]
    public async Task CompleteAndCancel_LifecycleTransitions_ReasonMandatoryOnCancel()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_TERM_{Guid.NewGuid():N}", "Terminal WS");
        var desk = await CreateDeskAsync($"DSK_TERM_{Guid.NewGuid():N}", "Terminal Desk", ws.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 1. Complete
        var create1 = await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "CourtHearing", "Event to Complete", null, today.AddDays(1), null, "Routine"
        ));
        var evt1 = await create1.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(evt1);

        var compRes = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt1.Id}/complete", new CompleteEventApiRequest(
            "Hearing concluded successfully", evt1.Revision
        ));
        Assert.Equal(HttpStatusCode.OK, compRes.StatusCode);
        var completed = await compRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(completed);
        Assert.Equal("Completed", completed.Status);
        Assert.NotNull(completed.CompletedAt);

        // Updating a completed event is rejected
        var failUpdate = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt1.Id}/reschedule", new RescheduleEventApiRequest(
            today.AddDays(5), null, "Cannot reschedule completed", completed.Revision
        ));
        Assert.Equal(HttpStatusCode.BadRequest, failUpdate.StatusCode);

        // 2. Cancel - blank reason rejected
        var create2 = await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "NoticeExpiry", "Event to Cancel", null, today.AddDays(2), null, "Routine"
        ));
        var evt2 = await create2.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(evt2);

        var blankCancelRes = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt2.Id}/cancel", new CancelEventApiRequest(
            "   ", evt2.Revision
        ));
        Assert.Equal(HttpStatusCode.BadRequest, blankCancelRes.StatusCode);

        // Valid cancel with reason
        var validCancelRes = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt2.Id}/cancel", new CancelEventApiRequest(
            "Proceedings dropped by notification withdrawal", evt2.Revision
        ));
        Assert.Equal(HttpStatusCode.OK, validCancelRes.StatusCode);
        var cancelled = await validCancelRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(cancelled);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.NotNull(cancelled.CancelledAt);
        Assert.Equal("Proceedings dropped by notification withdrawal", cancelled.CancellationReason);
    }

    [Fact]
    public async Task Reassign_SourceDeskMemberCanReassignToTargetDesk_TargetValidated()
    {
        var ws = await CreateWorkstreamAsync($"WS_REAS_{Guid.NewGuid():N}", "Reassign WS");
        var deskSource = await CreateDeskAsync($"DSK_SRC_{Guid.NewGuid():N}", "Source Desk", ws.Id);
        var deskTarget = await CreateDeskAsync($"DSK_TGT_{Guid.NewGuid():N}", "Target Desk", ws.Id);

        var (sourceClient, _) = await CreateUserWithPermissionsAsync(
            $"src_user_{Guid.NewGuid():N}",
            "UserPass123!",
            new[]
            {
                (PermissionCodes.ScheduleView, ScopeMode.Workstream),
                (PermissionCodes.ScheduleCreate, ScopeMode.Workstream),
                (PermissionCodes.ScheduleAssign, ScopeMode.Workstream)
            },
            workstreamId: ws.Id,
            deskId: deskSource.Id
        );

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var createRes = await sourceClient.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, deskSource.Id, null, "SiteInspection", "Inspection to Reassign", null, today.AddDays(3), null, "Urgent"
        ));
        var evt = await createRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(evt);

        // Reassign to Target Desk
        var reassignRes = await sourceClient.PostAsJsonAsync($"/api/scheduled-events/{evt.Id}/reassign", new ReassignEventApiRequest(
            deskTarget.Id, null, "Transferred jurisdiction to South zone desk", evt.Revision
        ));
        Assert.Equal(HttpStatusCode.OK, reassignRes.StatusCode);

        var reassigned = await reassignRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(reassigned);
        Assert.Equal(deskTarget.Id, reassigned.ResponsibleOfficeDeskId);
        Assert.Equal(2, reassigned.Revision);
        Assert.Contains(reassigned.History, h => h.Action == "ResponsibilityChanged" && h.TargetDeskName == deskTarget.Name);
    }

    // =========================================================================
    // 3. SCHEDULED EVENT IMMUTABILITY & SEQUENCE NUMBER TESTS
    // =========================================================================

    [Fact]
    public async Task ScheduledEventEvent_IsStrictlyImmutable_ThrowsOnUpdateOrDelete()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var evtId = Guid.NewGuid();
        var historyEntry = new ScheduledEventEvent
        {
            Id = Guid.NewGuid(),
            ScheduledEventId = evtId,
            Action = ScheduledEventAction.Created,
            ActionAt = DateTimeOffset.UtcNow,
            ActorUserId = Guid.NewGuid(),
            ActorDisplayNameSnapshot = "Test User",
            SequenceNumber = 1,
            Notes = "Initial creation"
        };

        db.ScheduledEventEvents.Add(historyEntry);
        await db.SaveChangesAsync();

        // Attempting to modify
        historyEntry.Notes = "Attempted tampering";
        db.Entry(historyEntry).State = EntityState.Modified;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        // Reset state
        db.Entry(historyEntry).State = EntityState.Unchanged;

        // Attempting to delete
        db.ScheduledEventEvents.Remove(historyEntry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ScheduledEvent_InitialReminders_GenerateSequentialSequenceNumbers()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_SEQ_{Guid.NewGuid():N}", "Sequence WS");
        var desk = await CreateDeskAsync($"DSK_SEQ_{Guid.NewGuid():N}", "Sequence Desk", ws.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var req = new CreateScheduledEventApiRequest(
            ws.Id,
            desk.Id,
            null,
            "CourtHearing",
            "Hearing with Multiple Reminders",
            null,
            today.AddDays(10),
            null,
            "Urgent",
            Reminders: new[] { 1, 3, 7 }
        );

        var res = await admin.PostAsJsonAsync("/api/scheduled-events", req);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var detail = await res.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(detail);

        // Verify history has sequential sequence numbers starting at 1
        Assert.Equal(4, detail.History.Count); // Created + 3 reminders
        var seqNumbers = detail.History.Select(h => h.SequenceNumber).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4 }, seqNumbers);
    }

    // =========================================================================
    // 4. REMINDERS MANAGEMENT TESTS
    // =========================================================================

    [Fact]
    public async Task Reminders_ValidationAndExpectedRevision_EnforcedStrictly()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_REM_{Guid.NewGuid():N}", "Reminder WS");
        var desk = await CreateDeskAsync($"DSK_REM_{Guid.NewGuid():N}", "Reminder Desk", ws.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var eventDate = today.AddDays(10);

        // DaysBefore out of range on create -> 400
        var badCreateRes = await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "CourtHearing", "Bad Reminder", null, eventDate, null, "Urgent",
            Reminders: new[] { -1 }
        ));
        Assert.Equal(HttpStatusCode.BadRequest, badCreateRes.StatusCode);

        var createRes = await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "CourtHearing", "Hearing with Reminders", null, eventDate, null, "Urgent"
        ));
        var evt = await createRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(evt);

        // DaysBefore out of range on AddReminder -> 400
        var badAddRes = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt.Id}/reminders", new AddReminderApiRequest(
            400, null, evt.Revision
        ));
        Assert.Equal(HttpStatusCode.BadRequest, badAddRes.StatusCode);

        // Add valid 3 days before reminder
        var addRes = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt.Id}/reminders", new AddReminderApiRequest(
            3, null, evt.Revision
        ));
        Assert.Equal(HttpStatusCode.OK, addRes.StatusCode);

        var updated = await addRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(updated);
        var rem = Assert.Single(updated.Reminders, r => r.DaysBefore == 3 && r.IsActive);

        // Remove without expectedRevision query param -> 400
        var noRevRes = await admin.DeleteAsync($"/api/scheduled-events/{evt.Id}/reminders/{rem.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, noRevRes.StatusCode);

        // Remove with expectedRevision <= 0 -> 400
        var zeroRevRes = await admin.DeleteAsync($"/api/scheduled-events/{evt.Id}/reminders/{rem.Id}?expectedRevision=0");
        Assert.Equal(HttpStatusCode.BadRequest, zeroRevRes.StatusCode);

        // Remove with wrong expectedRevision -> 409 Conflict
        var wrongRevRes = await admin.DeleteAsync($"/api/scheduled-events/{evt.Id}/reminders/{rem.Id}?expectedRevision=999");
        Assert.Equal(HttpStatusCode.Conflict, wrongRevRes.StatusCode);

        // Remove with correct expectedRevision -> 200 OK
        var delRes = await admin.DeleteAsync($"/api/scheduled-events/{evt.Id}/reminders/{rem.Id}?expectedRevision={updated.Revision}");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);

        var refreshed = await admin.GetFromJsonAsync<ScheduledEventDetailDto>($"/api/scheduled-events/{evt.Id}", JsonOpts);
        Assert.NotNull(refreshed);
        Assert.DoesNotContain(refreshed.Reminders, r => r.IsActive);
    }

    // =========================================================================
    // 5. UNIFIED ATTENTION PROJECTION & CALENDAR TESTS
    // =========================================================================

    [Fact]
    public async Task MyAttention_DelhiOfficeDates_BucketsCorrectly()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_ATT_{Guid.NewGuid():N}", "Attention WS");
        var desk = await CreateDeskAsync($"DSK_ATT_{Guid.NewGuid():N}", "Attention Desk", ws.Id);

        // Clock in Delhi: UTC+5:30
        var delhiNow = DateTime.UtcNow.AddHours(5.5);
        var delhiToday = DateOnly.FromDateTime(delhiNow);

        // Overdue event (yesterday)
        await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "ComplianceDeadline", "Overdue Item", null, delhiToday.AddDays(-1), null, "Urgent"
        ));

        // Today event
        await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "CourtHearing", "Today Item", null, delhiToday, null, "Urgent"
        ));

        // Tomorrow event
        await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "Meeting", "Tomorrow Item", null, delhiToday.AddDays(1), null, "Routine"
        ));

        var res = await admin.GetAsync("/api/attention/my");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var feed = await res.Content.ReadFromJsonAsync<AttentionFeedResult>(JsonOpts);
        Assert.NotNull(feed);
        Assert.True(feed.Summary.Overdue >= 1);
        Assert.True(feed.Summary.Today >= 1);
        Assert.True(feed.Summary.Tomorrow >= 1);

        // Verify items returned
        Assert.Contains(feed.Items, i => i.Title == "Overdue Item");
        Assert.Contains(feed.Items, i => i.Title == "Today Item");
        Assert.Contains(feed.Items, i => i.Title == "Tomorrow Item");
    }

    [Fact]
    public async Task MyAttention_NoPermissions_ReturnsEmptyFeedWithZeroCounts()
    {
        // User with no Schedule.View, WorkItem.View, Dak.View
        var (client, _) = await CreateUserWithPermissionsAsync(
            $"noperm_{Guid.NewGuid():N}",
            "UserPass123!",
            new[] { ("SomeOther.Permission", ScopeMode.All) }
        );

        var res = await client.GetAsync("/api/attention/my");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var feed = await res.Content.ReadFromJsonAsync<AttentionFeedResult>(JsonOpts);
        Assert.NotNull(feed);
        Assert.Empty(feed.Items);
        Assert.Equal(0, feed.TotalCount);
        Assert.Equal(0, feed.Summary.Overdue);
        Assert.Equal(0, feed.Summary.Today);
        Assert.Equal(0, feed.Summary.Tomorrow);
        Assert.Equal(0, feed.Summary.Next7Days);
        Assert.Equal(0, feed.Summary.ReminderActive);
        Assert.Equal(0, feed.Summary.NeedsRouting);
    }

    [Fact]
    public async Task CalendarEvents_CalculatesSummaryAcrossFullResultSet_BeforePagination()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_CAL_SUM_{Guid.NewGuid():N}", "Calendar Summary WS");
        var desk = await CreateDeskAsync($"DSK_CAL_SUM_{Guid.NewGuid():N}", "Calendar Summary Desk", ws.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));

        // Create 5 events on today
        for (var i = 1; i <= 5; i++)
        {
            await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
                ws.Id, desk.Id, null, "Meeting", $"Calendar Event {i}", null, today, null, "Routine"
            ));
        }

        // Query with pageSize = 2
        var res = await admin.GetAsync($"/api/scheduled-events/calendar?workstreamId={ws.Id}&page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var result = await res.Content.ReadFromJsonAsync<AttentionFeedResult>(JsonOpts);
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        // Summary must reflect the full 5 events, not just the 2 on page
        Assert.Equal(5, result.Summary.Today);
    }

    // =========================================================================
    // 6. COURT PROCEEDINGS & PROMOTION WORKFLOW TESTS
    // =========================================================================

    [Fact]
    public async Task DirectCreate_WithCourtProceedingId_ThrowsBadRequest()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_DIR_PROC_{Guid.NewGuid():N}", "Direct Proc WS");
        var desk = await CreateDeskAsync($"DSK_DIR_PROC_{Guid.NewGuid():N}", "Direct Proc Desk", ws.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var req = new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "CourtHearing", "Direct Proc Hearing", null, today.AddDays(5), null, "Urgent",
            CourtProceedingId: Guid.NewGuid()
        );

        var res = await admin.PostAsJsonAsync("/api/scheduled-events", req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task CourtProceedingOrigin_CannotBeRescheduledOrCancelled_Directly()
    {
        var admin = await CreateAdminClientAsync();

        Guid procId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var courtCase = new CourtCase
            {
                Id = Guid.NewGuid(),
                CaseNumber = $"WP_ORIG_{Guid.NewGuid():N}",
                CourtName = "High Court",
                CaseType = "Writ",
                CurrentStatus = "Active",
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.CourtCases.Add(courtCase);

            var proc = new CourtProceeding
            {
                Id = Guid.NewGuid(),
                CourtCaseId = courtCase.Id,
                ProceedingDate = DateOnly.FromDateTime(DateTime.UtcNow),
                NextDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
                Summary = "Notice issued",
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.CourtProceedings.Add(proc);
            await db.SaveChangesAsync();
            procId = proc.Id;
        }

        // Promote to schedule
        var promoteRes = await admin.PostAsJsonAsync($"/api/scheduled-events/from-court-proceeding/{procId}", new CreateFromCourtProceedingApiRequest(
            null, null, "Official Hearing", null, "Urgent", null
        ));
        Assert.Equal(HttpStatusCode.Created, promoteRes.StatusCode);
        var created = await promoteRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(created);
        Assert.Equal("CourtProceeding", created.Origin);

        // Attempting to reschedule directly throws 400
        var reschedRes = await admin.PostAsJsonAsync($"/api/scheduled-events/{created.Id}/reschedule", new RescheduleEventApiRequest(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)), null, "Direct reschedule attempt", created.Revision
        ));
        Assert.Equal(HttpStatusCode.BadRequest, reschedRes.StatusCode);

        // Attempting to cancel directly throws 400
        var cancelRes = await admin.PostAsJsonAsync($"/api/scheduled-events/{created.Id}/cancel", new CancelEventApiRequest(
            "Direct cancel attempt", created.Revision
        ));
        Assert.Equal(HttpStatusCode.BadRequest, cancelRes.StatusCode);
    }

    [Fact]
    public async Task CourtProceeding_CreateWithNextDateEarlierThanProceedingDate_ThrowsBadRequest()
    {
        var admin = await CreateAdminClientAsync();

        Guid caseId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var courtCase = new CourtCase
            {
                Id = Guid.NewGuid(),
                CaseNumber = $"WP_REV_{Guid.NewGuid():N}",
                CourtName = "High Court",
                CaseType = "Writ",
                CurrentStatus = "Active",
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.CourtCases.Add(courtCase);
            await db.SaveChangesAsync();
            caseId = courtCase.Id;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var badReq = new CreateCourtProceedingApiRequest(
            ProceedingDate: today,
            OrderType: "Hearing",
            RestraintNature: null,
            Summary: "Invalid reverse date",
            NextDate: today.AddDays(-1) // earlier than proceeding date!
        );

        var res = await admin.PostAsJsonAsync($"/api/court-cases/{caseId}/proceedings", badReq);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task CourtProceedings_Authorization_AwardViewAndEditEnforced()
    {
        Guid caseId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var courtCase = new CourtCase
            {
                Id = Guid.NewGuid(),
                CaseNumber = $"WP_AUTH_{Guid.NewGuid():N}",
                CourtName = "High Court",
                CaseType = "Writ",
                CurrentStatus = "Active",
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.CourtCases.Add(courtCase);
            await db.SaveChangesAsync();
            caseId = courtCase.Id;
        }

        // 1. User without Award.View cannot view proceedings
        var (unauthClient, _) = await CreateUserWithPermissionsAsync(
            $"unauth_court_{Guid.NewGuid():N}",
            "UserPass123!",
            new[] { (PermissionCodes.ScheduleView, ScopeMode.All) }
        );
        var unauthRes = await unauthClient.GetAsync($"/api/court-cases/{caseId}/proceedings");
        Assert.Equal(HttpStatusCode.Forbidden, unauthRes.StatusCode);

        // 2. User with Award.View (COURT_REFERENCES) can view proceedings but cannot create or promote
        var (viewClient, _) = await CreateUserWithPermissionsAsync(
            $"view_court_{Guid.NewGuid():N}",
            "UserPass123!",
            new[]
            {
                (PermissionCodes.AwardView, ScopeMode.All),
                (PermissionCodes.ScheduleCreate, ScopeMode.All)
            }
        );
        var viewRes = await viewClient.GetAsync($"/api/court-cases/{caseId}/proceedings");
        Assert.Equal(HttpStatusCode.OK, viewRes.StatusCode);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var postRes = await viewClient.PostAsJsonAsync($"/api/court-cases/{caseId}/proceedings", new CreateCourtProceedingApiRequest(
            today, "Notice", null, "Record proceeding", today.AddDays(10)
        ));
        Assert.Equal(HttpStatusCode.Forbidden, postRes.StatusCode);

        // 3. User with Award.Edit (COURT_REFERENCES) can create proceeding
        var (editClient, _) = await CreateUserWithPermissionsAsync(
            $"edit_court_{Guid.NewGuid():N}",
            "UserPass123!",
            new[]
            {
                (PermissionCodes.AwardView, ScopeMode.All),
                (PermissionCodes.AwardEdit, ScopeMode.All)
            }
        );
        var editPostRes = await editClient.PostAsJsonAsync($"/api/court-cases/{caseId}/proceedings", new CreateCourtProceedingApiRequest(
            today, "Notice", null, "Record proceeding", today.AddDays(10)
        ));
        Assert.Equal(HttpStatusCode.Created, editPostRes.StatusCode);
    }

    [Fact]
    public async Task CourtCase_NavigationUrl_ReturnsAwardWhenLinked_NullWhenNotLinked()
    {
        using var scope = _factory.Services.CreateScope();
        var courtAuth = scope.ServiceProvider.GetRequiredService<ICourtAuthorizationService>();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var award = new Award
        {
            Id = Guid.NewGuid(),
            AwardNumber = $"AW_{Guid.NewGuid():N}",
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Awards.Add(award);

        var caseLinked = new CourtCase
        {
            Id = Guid.NewGuid(),
            CaseNumber = $"CASE_L_{Guid.NewGuid():N}",
            CourtName = "High Court",
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CourtCases.Add(caseLinked);

        var caseUnlinked = new CourtCase
        {
            Id = Guid.NewGuid(),
            CaseNumber = $"CASE_U_{Guid.NewGuid():N}",
            CourtName = "District Court",
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CourtCases.Add(caseUnlinked);

        db.Set<CourtCaseAward>().Add(new CourtCaseAward
        {
            CourtCaseId = caseLinked.Id,
            AwardId = award.Id
        });

        await db.SaveChangesAsync();

        var (client, adminUser) = await CreateUserWithPermissionsAsync(
            $"nav_admin_{Guid.NewGuid():N}",
            "UserPass123!",
            new[] { (PermissionCodes.AwardView, ScopeMode.All) }
        );

        var linkedNavUrl = await courtAuth.GetCourtCaseNavigationUrlAsync(caseLinked.Id, adminUser.Id);
        Assert.Equal($"/awards/{award.Id}", linkedNavUrl);

        var unlinkedNavUrl = await courtAuth.GetCourtCaseNavigationUrlAsync(caseUnlinked.Id, adminUser.Id);
        Assert.Null(unlinkedNavUrl); // Never returns /court-cases/{id}!
    }

    // =========================================================================
    // 7. WORK ITEM LINKING TESTS
    // =========================================================================

    [Fact]
    public async Task WorkItemLinking_LinksAndUnlinks_WithoutAlteringWorkItemDueAt()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_WIL_{Guid.NewGuid():N}", "Work Item Link WS");
        var desk = await CreateDeskAsync($"DSK_WIL_{Guid.NewGuid():N}", "Work Item Link Desk", ws.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var originalDueAt = DateTimeOffset.UtcNow.AddDays(14);

        // Create WorkItem
        Guid workItemId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var workItem = new WorkItem
            {
                Id = Guid.NewGuid(),
                WorkstreamId = ws.Id,
                Title = "Submit compliance report",
                Status = WorkItemStatus.Assigned,
                Priority = WorkItemPriority.Urgent,
                DueAt = originalDueAt,
                RequestedByUserId = Guid.NewGuid(),
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.WorkItems.Add(workItem);
            await db.SaveChangesAsync();
            workItemId = workItem.Id;
        }

        // Create ScheduledEvent
        var createRes = await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "ReportSubmission", "Official Submission Date", null, today.AddDays(7), null, "Urgent"
        ));
        var evt = await createRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(evt);

        // Link WorkItem
        var linkRes = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt.Id}/work-item", new LinkWorkItemApiRequest(
            workItemId, evt.Revision
        ));
        Assert.Equal(HttpStatusCode.OK, linkRes.StatusCode);

        var linked = await linkRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(linked);
        Assert.NotNull(linked.WorkItemContext);
        Assert.Equal(workItemId, linked.WorkItemContext.EntityId);

        // Verify WorkItem's own DueAt remains untouched
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var wi = await db.WorkItems.FirstAsync(w => w.Id == workItemId);
            Assert.Equal(originalDueAt, wi.DueAt);
        }

        // Unlink WorkItem
        var unlinkRes = await admin.DeleteAsync($"/api/scheduled-events/{evt.Id}/work-item?expectedRevision={linked.Revision}");
        Assert.Equal(HttpStatusCode.OK, unlinkRes.StatusCode);

        var unlinked = await unlinkRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(unlinked);
        Assert.Null(unlinked.WorkItemContext);
    }

    // =========================================================================
    // 8. UNIFIED HISTORY INTEGRATION TESTS
    // =========================================================================

    [Fact]
    public async Task UnifiedHistory_MyHistoryAndTeamActivity_IncludesScheduledEventEvents()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_HIST_{Guid.NewGuid():N}", "History WS");
        var desk = await CreateDeskAsync($"DSK_HIST_{Guid.NewGuid():N}", "History Desk", ws.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var createRes = await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "SiteInspection", "History Inspection Event", "Inspecting land boundaries", today.AddDays(4), null, "Urgent"
        ));
        var evt = await createRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(evt);

        // Query My History
        var myHistoryRes = await admin.GetAsync("/api/activity/my-history");
        Assert.Equal(HttpStatusCode.OK, myHistoryRes.StatusCode);
        var myHistory = await myHistoryRes.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(myHistory);
        Assert.Contains(myHistory.Items, i => i.EntityType == "ScheduledEvent" && i.EntityId == evt.Id);

        // Query Team Activity
        var teamRes = await admin.GetAsync($"/api/activity/team?workstreamId={ws.Id}");
        Assert.Equal(HttpStatusCode.OK, teamRes.StatusCode);
        var team = await teamRes.Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(team);
        Assert.Contains(team.Items, i => i.EntityType == "ScheduledEvent" && i.EntityId == evt.Id);
    }

    // =========================================================================
    // 9. AUDIT HARDENING PASS TESTS
    // =========================================================================

    [Fact]
    public async Task CourtAuthorization_StrictWorkstreamAndScopeTruth()
    {
        using var scope = _factory.Services.CreateScope();
        var courtAuth = scope.ServiceProvider.GetRequiredService<ICourtAuthorizationService>();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var wsCourt = await CreateWorkstreamAsync(WorkstreamCodes.CourtReferences, "Court References WS");
        var wsAward = await CreateWorkstreamAsync(WorkstreamCodes.Award, "Award WS");
        var deskCourt = await CreateDeskAsync($"DSK_CRT_{Guid.NewGuid():N}", "Court Desk", wsCourt.Id);

        var courtCase = new CourtCase
        {
            Id = Guid.NewGuid(),
            CaseNumber = $"CASE_AUTH_{Guid.NewGuid():N}",
            CourtName = "High Court",
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CourtCases.Add(courtCase);
        await db.SaveChangesAsync();

        // 1. User with Award.View on Award WS only cannot view court references
        var (awardUserClient, awardUser) = await CreateUserWithPermissionsAsync(
            $"usr_award_only_{Guid.NewGuid():N}",
            "UserPass123!",
            new[] { (PermissionCodes.AwardView, ScopeMode.Workstream) },
            workstreamId: wsAward.Id
        );
        var canAwardUserView = await courtAuth.CanViewCourtReferencesAsync(awardUser.Id);
        Assert.False(canAwardUserView);
        var awardUserRes = await awardUserClient.GetAsync($"/api/court-cases/{courtCase.Id}/proceedings");
        Assert.Equal(HttpStatusCode.Forbidden, awardUserRes.StatusCode);

        // 2. User with Award.View on Assigned desk under CourtReferences fails closed
        var (assignedUserClient, assignedUser) = await CreateUserWithPermissionsAsync(
            $"usr_court_assigned_{Guid.NewGuid():N}",
            "UserPass123!",
            new[] { (PermissionCodes.AwardView, ScopeMode.Assigned) },
            deskId: deskCourt.Id
        );
        var canAssignedUserView = await courtAuth.CanViewCourtReferencesAsync(assignedUser.Id);
        Assert.False(canAssignedUserView);

        // 3. User with Award.View on CourtReferences WS can view
        var (courtUserClient, courtUser) = await CreateUserWithPermissionsAsync(
            $"usr_court_ws_{Guid.NewGuid():N}",
            "UserPass123!",
            new[] { (PermissionCodes.AwardView, ScopeMode.Workstream) },
            workstreamId: wsCourt.Id
        );
        var canCourtUserView = await courtAuth.CanViewCourtReferencesAsync(courtUser.Id);
        Assert.True(canCourtUserView);
        var courtUserRes = await courtUserClient.GetAsync($"/api/court-cases/{courtCase.Id}/proceedings");
        Assert.Equal(HttpStatusCode.OK, courtUserRes.StatusCode);

        // 4. User with ScopeMode.All can view
        var (allUserClient, allUser) = await CreateUserWithPermissionsAsync(
            $"usr_court_all_{Guid.NewGuid():N}",
            "UserPass123!",
            new[] { (PermissionCodes.AwardView, ScopeMode.All) }
        );
        var canAllUserView = await courtAuth.CanViewCourtReferencesAsync(allUser.Id);
        Assert.True(canAllUserView);
    }

    [Fact]
    public async Task CourtNavigationTruth_GatedOnAwardWorkspaceAccess()
    {
        using var scope = _factory.Services.CreateScope();
        var courtAuth = scope.ServiceProvider.GetRequiredService<ICourtAuthorizationService>();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var wsCourt = await CreateWorkstreamAsync(WorkstreamCodes.CourtReferences, "Court WS Nav");
        var wsAward = await CreateWorkstreamAsync(WorkstreamCodes.Award, "Award WS Nav");

        var award = new Award
        {
            Id = Guid.NewGuid(),
            AwardNumber = $"AW_NAV_{Guid.NewGuid():N}",
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Awards.Add(award);

        var courtCase = new CourtCase
        {
            Id = Guid.NewGuid(),
            CaseNumber = $"CASE_NAV_{Guid.NewGuid():N}",
            CourtName = "High Court",
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CourtCases.Add(courtCase);

        db.Set<CourtCaseAward>().Add(new CourtCaseAward
        {
            CourtCaseId = courtCase.Id,
            AwardId = award.Id
        });
        await db.SaveChangesAsync();

        // 1. User with CourtReferences access ONLY: has court permission, but NOT Award workspace access
        var (_, courtOnlyUser) = await CreateUserWithPermissionsAsync(
            $"usr_court_only_nav_{Guid.NewGuid():N}",
            "UserPass123!",
            new[] { (PermissionCodes.AwardView, ScopeMode.Workstream) },
            workstreamId: wsCourt.Id
        );

        var canViewAwardWsCourtOnly = await courtAuth.CanViewAwardWorkspaceAsync(courtOnlyUser.Id);
        Assert.False(canViewAwardWsCourtOnly);

        var navUrlCourtOnly = await courtAuth.GetCourtCaseNavigationUrlAsync(courtCase.Id, courtOnlyUser.Id);
        Assert.Null(navUrlCourtOnly);

        // 2. User with both CourtReferences AND Award workspace access
        var (_, dualUser) = await CreateUserWithPermissionsAsync(
            $"usr_dual_nav_{Guid.NewGuid():N}",
            "UserPass123!",
            new[] { (PermissionCodes.AwardView, ScopeMode.Workstream) },
            workstreamId: wsCourt.Id
        );

        using (var updateScope = _factory.Services.CreateScope())
        {
            var updateDb = updateScope.ServiceProvider.GetRequiredService<LacDbContext>();
            updateDb.UserWorkstreamMemberships.Add(new UserWorkstreamMembership
            {
                UserId = dualUser.Id,
                WorkstreamId = wsAward.Id,
                IsActive = true,
                IsPrimary = false,
                AssignedAt = DateTimeOffset.UtcNow
            });
            await updateDb.SaveChangesAsync();
        }

        var canViewAwardWsDual = await courtAuth.CanViewAwardWorkspaceAsync(dualUser.Id);
        Assert.True(canViewAwardWsDual);

        var navUrlDual = await courtAuth.GetCourtCaseNavigationUrlAsync(courtCase.Id, dualUser.Id);
        Assert.Equal($"/awards/{award.Id}", navUrlDual);

        // 3. User with ScopeMode.All
        var (_, allUser) = await CreateUserWithPermissionsAsync(
            $"usr_all_nav_{Guid.NewGuid():N}",
            "UserPass123!",
            new[] { (PermissionCodes.AwardView, ScopeMode.All) }
        );
        var canViewAwardWsAll = await courtAuth.CanViewAwardWorkspaceAsync(allUser.Id);
        Assert.True(canViewAwardWsAll);

        var navUrlAll = await courtAuth.GetCourtCaseNavigationUrlAsync(courtCase.Id, allUser.Id);
        Assert.Equal($"/awards/{award.Id}", navUrlAll);
    }

    [Fact]
    public async Task ScheduledEventCapabilities_CourtProceedingAndReassignTruth()
    {
        var admin = await CreateAdminClientAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var ws1 = await CreateWorkstreamAsync($"WS_CAP1_{Guid.NewGuid():N}", "Cap WS 1");
        var ws2 = await CreateWorkstreamAsync($"WS_CAP2_{Guid.NewGuid():N}", "Cap WS 2");
        var desk1 = await CreateDeskAsync($"DSK_CAP1_{Guid.NewGuid():N}", "Cap Desk 1", ws1.Id);

        var courtCase = new CourtCase
        {
            Id = Guid.NewGuid(),
            CaseNumber = $"CASE_CAP_{Guid.NewGuid():N}",
            CourtName = "Supreme Court",
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CourtCases.Add(courtCase);

        var proceeding = new CourtProceeding
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCase.Id,
            ProceedingDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            OrderType = "Hearing",
            NextDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CourtProceedings.Add(proceeding);
        await db.SaveChangesAsync();

        // 1. Promote proceeding to official schedule
        var promoteRes = await admin.PostAsJsonAsync($"/api/scheduled-events/from-court-proceeding/{proceeding.Id}", new CreateFromCourtProceedingApiRequest(
            desk1.Id, null, "Court Proceeding Event", null, "Urgent", null
        ));
        Assert.Equal(HttpStatusCode.Created, promoteRes.StatusCode);
        var courtEvt = await promoteRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(courtEvt);
        Assert.Equal("CourtProceeding", courtEvt.Origin);

        // For court proceeding origin: CanReschedule=false, CanCancel=false, CanUpdate=false
        Assert.False(courtEvt.Capabilities.CanReschedule);
        Assert.False(courtEvt.Capabilities.CanCancel);
        Assert.False(courtEvt.Capabilities.CanUpdate);

        // 2. CanReassign test:
        // Create an event in WS1
        var createEvtRes = await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws1.Id, desk1.Id, null, "CourtHearing", "WS1 Event", null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), null, "Urgent"
        ));
        Assert.Equal(HttpStatusCode.Created, createEvtRes.StatusCode);
        var normalEvt = await createEvtRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(normalEvt);

        // User with ScheduleAssign in WS2 only cannot reassign event in WS1
        var (userWs2Client, userWs2) = await CreateUserWithPermissionsAsync(
            $"usr_reassign_ws2_{Guid.NewGuid():N}",
            "UserPass123!",
            new[]
            {
                (PermissionCodes.ScheduleView, ScopeMode.All),
                (PermissionCodes.ScheduleAssign, ScopeMode.Workstream)
            },
            workstreamId: ws2.Id
        );

        var detailWs2Res = await userWs2Client.GetAsync($"/api/scheduled-events/{normalEvt.Id}");
        Assert.Equal(HttpStatusCode.OK, detailWs2Res.StatusCode);
        var detailWs2 = await detailWs2Res.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(detailWs2);
        Assert.False(detailWs2.Capabilities.CanReassign);

        // User with ScheduleAssign in WS1 CAN reassign event in WS1
        var (userWs1Client, userWs1) = await CreateUserWithPermissionsAsync(
            $"usr_reassign_ws1_{Guid.NewGuid():N}",
            "UserPass123!",
            new[]
            {
                (PermissionCodes.ScheduleView, ScopeMode.All),
                (PermissionCodes.ScheduleAssign, ScopeMode.Workstream)
            },
            workstreamId: ws1.Id
        );

        var detailWs1Res = await userWs1Client.GetAsync($"/api/scheduled-events/{normalEvt.Id}");
        Assert.Equal(HttpStatusCode.OK, detailWs1Res.StatusCode);
        var detailWs1 = await detailWs1Res.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(detailWs1);
        Assert.True(detailWs1.Capabilities.CanReassign);
    }

    [Fact]
    public async Task CalendarAndAttentionQuery_StrictEnumValidation_Returns400()
    {
        var admin = await CreateAdminClientAsync();

        // 1. Calendar query with invalid eventKind
        var res1 = await admin.GetAsync("/api/scheduled-events?eventKind=InvalidKind");
        Assert.Equal(HttpStatusCode.BadRequest, res1.StatusCode);

        // 2. Calendar query with invalid status
        var res2 = await admin.GetAsync("/api/scheduled-events?status=Rescheduled");
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);

        // 3. Calendar query with invalid priority
        var res3 = await admin.GetAsync("/api/scheduled-events?priority=SuperUrgent");
        Assert.Equal(HttpStatusCode.BadRequest, res3.StatusCode);

        // 4. My Attention query with invalid priority
        var res4 = await admin.GetAsync("/api/attention/my?priority=BadPriority");
        Assert.Equal(HttpStatusCode.BadRequest, res4.StatusCode);

        // 5. Branch Attention query with invalid priority
        var res5 = await admin.GetAsync("/api/attention/branch?priority=BadPriority");
        Assert.Equal(HttpStatusCode.BadRequest, res5.StatusCode);
    }

    [Fact]
    public async Task AttentionSummary_FilterOrdering_MultiWorkstream()
    {
        var admin = await CreateAdminClientAsync();
        var wsA = await CreateWorkstreamAsync($"WS_FLT_A_{Guid.NewGuid():N}", "Filter WS A");
        var wsB = await CreateWorkstreamAsync($"WS_FLT_B_{Guid.NewGuid():N}", "Filter WS B");
        var deskA = await CreateDeskAsync($"DSK_FLT_A_{Guid.NewGuid():N}", "Filter Desk A", wsA.Id);
        var deskB = await CreateDeskAsync($"DSK_FLT_B_{Guid.NewGuid():N}", "Filter Desk B", wsB.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Create 2 events in WS A: 1 Overdue, 1 Today
        await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            wsA.Id, deskA.Id, null, "CourtHearing", "WS A Overdue Event", null, today.AddDays(-2), null, "Urgent"
        ));
        await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            wsA.Id, deskA.Id, null, "CourtHearing", "WS A Today Event", null, today, null, "Routine"
        ));

        // Create 1 event in WS B: Today
        await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            wsB.Id, deskB.Id, null, "CourtHearing", "WS B Today Event", null, today, null, "Routine"
        ));

        // Query branch attention filtered by wsA
        var resA = await admin.GetAsync($"/api/attention/branch?workstreamId={wsA.Id}");
        Assert.Equal(HttpStatusCode.OK, resA.StatusCode);
        var feedA = await resA.Content.ReadFromJsonAsync<AttentionFeedResult>(JsonOpts);
        Assert.NotNull(feedA);

        // Summary counts MUST strictly reflect wsA items only (1 overdue, 1 today, 0 from wsB)
        Assert.Equal(1, feedA.Summary.Overdue);
        Assert.Equal(1, feedA.Summary.Today);
        Assert.Equal(2, feedA.TotalCount);

        // Query branch attention filtered by wsA AND bucket=overdue
        var resABucket = await admin.GetAsync($"/api/attention/branch?workstreamId={wsA.Id}&bucket=overdue");
        Assert.Equal(HttpStatusCode.OK, resABucket.StatusCode);
        var feedABucket = await resABucket.Content.ReadFromJsonAsync<AttentionFeedResult>(JsonOpts);
        Assert.NotNull(feedABucket);

        // Summary is computed BEFORE bucket filter, so summary still has Overdue=1 and Today=1
        Assert.Equal(1, feedABucket.Summary.Overdue);
        Assert.Equal(1, feedABucket.Summary.Today);
        // But Items returned are filtered by bucket (only 1 overdue item)
        Assert.Equal(1, feedABucket.TotalCount);
        Assert.Single(feedABucket.Items);
        Assert.Contains("Overdue", feedABucket.Items[0].Buckets);
    }

    [Fact]
    public async Task Reminders_TrulyPrivatePerUser_Enforced()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_REM_{Guid.NewGuid():N}", "Reminders WS");
        var desk = await CreateDeskAsync($"DSK_REM_{Guid.NewGuid():N}", "Reminders Desk", ws.Id);

        var (userAClient, userA) = await CreateUserWithPermissionsAsync(
            $"usr_rem_a_{Guid.NewGuid():N}",
            "UserPass123!",
            new[]
            {
                (PermissionCodes.ScheduleView, ScopeMode.All),
                (PermissionCodes.ScheduleUpdate, ScopeMode.All),
                (PermissionCodes.AuditView, ScopeMode.All)
            },
            workstreamId: ws.Id,
            deskId: desk.Id
        );

        var (userBClient, userB) = await CreateUserWithPermissionsAsync(
            $"usr_rem_b_{Guid.NewGuid():N}",
            "UserPass123!",
            new[]
            {
                (PermissionCodes.ScheduleView, ScopeMode.All),
                (PermissionCodes.ScheduleUpdate, ScopeMode.All),
                (PermissionCodes.AuditView, ScopeMode.All)
            },
            workstreamId: ws.Id,
            deskId: desk.Id
        );

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var eventDate = today.AddDays(3);

        // 1. Create an event occurring in 3 days
        var createRes = await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "Meeting", "Privacy Test Event", null, eventDate, null, "Routine"
        ));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var evt = await createRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(evt);

        // 2. User A adds a reminder (5 days before)
        var addResA = await userAClient.PostAsJsonAsync($"/api/scheduled-events/{evt.Id}/reminders", new AddReminderApiRequest(
            5, null, evt.Revision
        ));
        Assert.Equal(HttpStatusCode.OK, addResA.StatusCode);
        var detailAfterA = await addResA.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(detailAfterA);
        Assert.Single(detailAfterA.Reminders);
        var reminderAId = detailAfterA.Reminders[0].Id;

        // 3. User A views event detail: sees reminder and ReminderAdded in history
        var detailA = await (await userAClient.GetAsync($"/api/scheduled-events/{evt.Id}")).Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(detailA);
        Assert.Single(detailA.Reminders);
        Assert.Equal(reminderAId, detailA.Reminders[0].Id);
        Assert.Contains(detailA.History, h => h.Action == "ReminderAdded" && h.ActorUserId == userA.Id);

        // 4. User B views event detail: A's reminder is invisible to B; B's history hides A's reminder action
        var detailB = await (await userBClient.GetAsync($"/api/scheduled-events/{evt.Id}")).Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(detailB);
        Assert.Empty(detailB.Reminders);
        Assert.DoesNotContain(detailB.History, h => h.Action == "ReminderAdded");

        // 5. User B adds their own reminder for 5 days before: succeeds (duplicate rule is per user, not global)
        var addResB = await userBClient.PostAsJsonAsync($"/api/scheduled-events/{evt.Id}/reminders", new AddReminderApiRequest(
            5, null, detailA.Revision
        ));
        Assert.Equal(HttpStatusCode.OK, addResB.StatusCode);
        var detailAfterB = await addResB.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(detailAfterB);
        Assert.Single(detailAfterB.Reminders);
        var reminderBId = detailAfterB.Reminders[0].Id;
        Assert.NotEqual(reminderAId, reminderBId);

        // 6. User A cannot delete User B's reminder (fails 403)
        var delBByA = await userAClient.DeleteAsync($"/api/scheduled-events/{evt.Id}/reminders/{reminderBId}?expectedRevision={detailAfterB.Revision}");
        Assert.Equal(HttpStatusCode.Forbidden, delBByA.StatusCode);

        // 7. Surfacing in My Attention:
        // Today is eventDate - 3. Reminder is 5 days before, so today >= eventDate - 5 (is active).
        // User A has 5-day reminder -> reminder-active is true for A
        var myAttA = await (await userAClient.GetAsync("/api/attention/my")).Content.ReadFromJsonAsync<AttentionFeedResult>(JsonOpts);
        Assert.NotNull(myAttA);
        var itemA = myAttA.Items.FirstOrDefault(i => i.Id == evt.Id);
        Assert.NotNull(itemA);
        Assert.True(itemA.IsReminderActive);
        Assert.Contains("Reminder Active", itemA.Buckets);

        // User B deletes their own reminder -> B no longer has an active reminder
        var delBByB = await userBClient.DeleteAsync($"/api/scheduled-events/{evt.Id}/reminders/{reminderBId}?expectedRevision={detailAfterB.Revision}");
        Assert.Equal(HttpStatusCode.OK, delBByB.StatusCode);

        // User B My Attention: A's reminder does NOT make B's My Attention reminder-active
        var myAttB = await (await userBClient.GetAsync("/api/attention/my")).Content.ReadFromJsonAsync<AttentionFeedResult>(JsonOpts);
        Assert.NotNull(myAttB);
        var itemB = myAttB.Items.FirstOrDefault(i => i.Id == evt.Id);
        Assert.NotNull(itemB);
        Assert.False(itemB.IsReminderActive);
        Assert.DoesNotContain("Reminder Active", itemB.Buckets);

        // 8. Calendar reminder indicator: considers only caller's reminders
        var calA = await (await userAClient.GetAsync("/api/scheduled-events")).Content.ReadFromJsonAsync<AttentionFeedResult>(JsonOpts);
        Assert.NotNull(calA);
        var calItemA = calA.Items.FirstOrDefault(i => i.Id == evt.Id);
        Assert.NotNull(calItemA);
        Assert.True(calItemA.IsReminderActive);
        Assert.Equal(1, calA.Summary.ReminderActive);

        var calB = await (await userBClient.GetAsync("/api/scheduled-events")).Content.ReadFromJsonAsync<AttentionFeedResult>(JsonOpts);
        Assert.NotNull(calB);
        var calItemB = calB.Items.FirstOrDefault(i => i.Id == evt.Id);
        Assert.NotNull(calItemB);
        Assert.False(calItemB.IsReminderActive);
        Assert.Equal(0, calB.Summary.ReminderActive);

        // 9. Team Activity: does NOT leak personal reminder actions
        var teamAct = await (await admin.GetAsync("/api/activity/team")).Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(teamAct);
        Assert.DoesNotContain(teamAct.Items, i => i.Action == "ReminderAdded" || i.Action == "ReminderRemoved");

        // 10. My History: User A sees their own ReminderAdded
        var myHistA = await (await userAClient.GetAsync("/api/activity/my-history")).Content.ReadFromJsonAsync<ActivityFeedResult>(JsonOpts);
        Assert.NotNull(myHistA);
        Assert.Contains(myHistA.Items, i => i.Action == "ReminderAdded" && i.ActorUserId == userA.Id);
    }

    [Fact]
    public async Task CourtNDOH_SingleCurrentProjectionPerCase_Synchronized()
    {
        var admin = await CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var courtWs = await db.Workstreams.FirstOrDefaultAsync(w => w.Code == WorkstreamCodes.CourtReferences && w.IsActive);
        if (courtWs == null)
        {
            courtWs = new Workstream
            {
                Id = Guid.NewGuid(),
                Code = WorkstreamCodes.CourtReferences,
                Name = "Court References",
                IsActive = true,
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Workstreams.Add(courtWs);
            await db.SaveChangesAsync();
        }

        var courtDesk = new OfficeDesk
        {
            Id = Guid.NewGuid(),
            Code = $"DSK_CRT_{Guid.NewGuid():N}",
            Name = "Court Desk",
            WorkstreamId = courtWs.Id,
            IsActive = true,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.OfficeDesks.Add(courtDesk);

        var courtCase = new CourtCase
        {
            Id = Guid.NewGuid(),
            CaseNumber = $"CASE_SYNC_{Guid.NewGuid():N}",
            CourtName = "High Court of Delhi",
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CourtCases.Add(courtCase);

        var p1 = new CourtProceeding
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCase.Id,
            ProceedingDate = today.AddDays(1),
            OrderType = "Notice Issued",
            NextDate = today.AddDays(10),
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CourtProceedings.Add(p1);
        await db.SaveChangesAsync();

        // 1. First promotion creates one current Court NDOH ScheduledEvent
        var promoteP1Res = await admin.PostAsJsonAsync($"/api/scheduled-events/from-court-proceeding/{p1.Id}", new CreateFromCourtProceedingApiRequest(
            courtDesk.Id, null, "Court NDOH Projection", null, "Urgent", null
        ));
        Assert.Equal(HttpStatusCode.Created, promoteP1Res.StatusCode);
        var schedEvt = await promoteP1Res.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(schedEvt);
        Assert.Equal(p1.NextDate, schedEvt.ScheduledDate);
        Assert.Equal(p1.Id, schedEvt.CourtProceedingContext?.EntityId);
        Assert.Equal("CourtProceeding", schedEvt.Origin);
        Assert.Equal("Scheduled", schedEvt.Status);

        // 2. Promoting P1 again fails with 409
        var promoteP1Again = await admin.PostAsJsonAsync($"/api/scheduled-events/from-court-proceeding/{p1.Id}", new CreateFromCourtProceedingApiRequest(
            courtDesk.Id, null, "Court NDOH Duplicate", null, "Urgent", null
        ));
        Assert.Equal(HttpStatusCode.Conflict, promoteP1Again.StatusCode);

        // 3. Record proceeding P2 with later NextDate (today + 20) via POST /api/court-cases/{id}/proceedings
        var createP2Res = await admin.PostAsJsonAsync($"/api/court-cases/{courtCase.Id}/proceedings", new CreateCourtProceedingApiRequest(
            today.AddDays(10), "Arguments Heard", null, "Matter adjourned for orders", today.AddDays(20)
        ));
        Assert.Equal(HttpStatusCode.Created, createP2Res.StatusCode);
        var p2 = await createP2Res.Content.ReadFromJsonAsync<CourtProceedingDto>(JsonOpts);
        Assert.NotNull(p2);

        // 4. Verify existing schedule projection was synchronized transactionally:
        // Same event ID, updated ScheduledDate = today + 20, updated CourtProceedingId = p2.Id, revision incremented, Rescheduled history added
        var detailAfterP2 = await (await admin.GetAsync($"/api/scheduled-events/{schedEvt.Id}")).Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(detailAfterP2);
        Assert.Equal(schedEvt.Id, detailAfterP2.Id);
        Assert.Equal(today.AddDays(20), detailAfterP2.ScheduledDate);
        Assert.Equal(p2.Id, detailAfterP2.CourtProceedingContext?.EntityId);
        Assert.True(detailAfterP2.Revision > schedEvt.Revision);
        var rescheduleHist = detailAfterP2.History.FirstOrDefault(h => h.Action == "Rescheduled");
        Assert.NotNull(rescheduleHist);
        Assert.Equal(today.AddDays(10), rescheduleHist.OldScheduledDate);
        Assert.Equal(today.AddDays(20), rescheduleHist.NewScheduledDate);

        // 5. Old date is no longer actionable; calendar only shows today + 20
        var calFeed = await (await admin.GetAsync("/api/scheduled-events")).Content.ReadFromJsonAsync<AttentionFeedResult>(JsonOpts);
        Assert.NotNull(calFeed);
        var currentEvtInCal = calFeed.Items.FirstOrDefault(i => i.Id == schedEvt.Id);
        Assert.NotNull(currentEvtInCal);
        Assert.Equal(today.AddDays(20), currentEvtInCal.ScheduledDate);

        // 6. Promoting P2 explicitly fails with 409 (cannot create second active projection for the case)
        var promoteP2Res = await admin.PostAsJsonAsync($"/api/scheduled-events/from-court-proceeding/{p2.Id}", new CreateFromCourtProceedingApiRequest(
            courtDesk.Id, null, "Duplicate Promotion P2", null, "Urgent", null
        ));
        Assert.Equal(HttpStatusCode.Conflict, promoteP2Res.StatusCode);

        // 7. Generic reschedule/cancel remains forbidden for CourtProceeding origin
        var genericReschedule = await admin.PostAsJsonAsync($"/api/scheduled-events/{schedEvt.Id}/reschedule", new RescheduleEventApiRequest(
            today.AddDays(25), null, "Manual change", detailAfterP2.Revision
        ));
        Assert.Equal(HttpStatusCode.BadRequest, genericReschedule.StatusCode);

        var genericCancel = await admin.PostAsJsonAsync($"/api/scheduled-events/{schedEvt.Id}/cancel", new CancelEventApiRequest(
            "Manual cancel", detailAfterP2.Revision
        ));
        Assert.Equal(HttpStatusCode.BadRequest, genericCancel.StatusCode);

        // 8. Record proceeding P3 with NO NextDate (null): clears current NDOH through terminal transition
        var createP3Res = await admin.PostAsJsonAsync($"/api/court-cases/{courtCase.Id}/proceedings", new CreateCourtProceedingApiRequest(
            today.AddDays(20), "Final Order Passed", null, "Disposed of", null
        ));
        Assert.Equal(HttpStatusCode.Created, createP3Res.StatusCode);

        var detailAfterP3 = await (await admin.GetAsync($"/api/scheduled-events/{schedEvt.Id}")).Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(detailAfterP3);
        Assert.Equal("Completed", detailAfterP3.Status);
        Assert.Contains(detailAfterP3.History, h => h.Action == "Completed" && h.Reason!.Contains("Superseded"));

        // 9. Cleared schedule no longer appears in actionable My Attention
        var myAttFeed = await (await admin.GetAsync("/api/attention/my")).Content.ReadFromJsonAsync<AttentionFeedResult>(JsonOpts);
        Assert.NotNull(myAttFeed);
        Assert.DoesNotContain(myAttFeed.Items, i => i.Id == schedEvt.Id);
    }
}

