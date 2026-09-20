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
        Guid? deskId = null)
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
            IsActive = true,
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
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

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
            "Hearing",
            "High Court Compliance Hearing",
            "Urgent hearing compliance",
            today.AddDays(3),
            new TimeOnly(10, 30),
            "High"
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
            "Medium"
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
            "Medium"
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
            "Low"
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
            "Low"
        );
        var failRes = await client.PostAsJsonAsync("/api/scheduled-events", failReq);
        Assert.Equal(HttpStatusCode.Forbidden, failRes.StatusCode);
    }

    [Fact]
    public async Task CreateScheduledEvent_InactiveUserOrWorkstreamOrDesk_FailsClosed()
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
            "Medium"
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
            "Medium"
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
            "Medium"
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
            ws.Id, desk.Id, null, "Hearing", "Hearing to Reschedule", null, today.AddDays(2), new TimeOnly(11, 0), "High"
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

        // Check history
        Assert.Contains(updated.History, h => h.Action == "Rescheduled" && h.Reason != null && h.Reason.Contains("state counsel"));
    }

    [Fact]
    public async Task Concurrency_ExpectedRevisionMismatch_Returns409Conflict()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_CONC_{Guid.NewGuid():N}", "Concurrency WS");
        var desk = await CreateDeskAsync($"DSK_CONC_{Guid.NewGuid():N}", "Concurrency Desk", ws.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var createRes = await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "Meeting", "Concurrency Meeting", null, today.AddDays(3), null, "Medium"
        ));
        var evt = await createRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(evt);

        // Pass incorrect ExpectedRevision (e.g. 999 instead of 1)
        var conflictRes = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt.Id}/reschedule", new RescheduleEventApiRequest(
            today.AddDays(4), null, "Invalid revision test", 999
        ));
        Assert.Equal(HttpStatusCode.Conflict, conflictRes.StatusCode);
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
            ws.Id, desk.Id, null, "Hearing", "Event to Complete", null, today.AddDays(1), null, "Medium"
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
            ws.Id, desk.Id, null, "NoticeExpiry", "Event to Cancel", null, today.AddDays(2), null, "Low"
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
            ws.Id, deskSource.Id, null, "SiteInspection", "Inspection to Reassign", null, today.AddDays(3), null, "High"
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
    // 3. SCHEDULED EVENT IMMUTABILITY TEST
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

    // =========================================================================
    // 4. REMINDERS MANAGEMENT TESTS
    // =========================================================================

    [Fact]
    public async Task Reminders_AddCalculatesTargetDate_DuplicateDaysBeforeRejected_RemoveDeactivates()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_REM_{Guid.NewGuid():N}", "Reminder WS");
        var desk = await CreateDeskAsync($"DSK_REM_{Guid.NewGuid():N}", "Reminder Desk", ws.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var eventDate = today.AddDays(10);

        var createRes = await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "Hearing", "Hearing with Reminders", null, eventDate, null, "High"
        ));
        var evt = await createRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(evt);

        // Add 3 days before reminder -> surfaces on eventDate - 3 days
        var addRes = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt.Id}/reminders", new AddReminderApiRequest(
            3, null, evt.Revision
        ));
        Assert.Equal(HttpStatusCode.OK, addRes.StatusCode);

        var updated = await addRes.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(updated);
        var rem = Assert.Single(updated.Reminders, r => r.DaysBefore == 3 && r.IsActive);
        Assert.Equal(3, rem.DaysBefore);

        // Duplicate DaysBefore rejected
        var dupRes = await admin.PostAsJsonAsync($"/api/scheduled-events/{evt.Id}/reminders", new AddReminderApiRequest(
            3, null, updated.Revision
        ));
        Assert.Equal(HttpStatusCode.BadRequest, dupRes.StatusCode);

        // Deactivate reminder
        var delRes = await admin.DeleteAsync($"/api/scheduled-events/{evt.Id}/reminders/{rem.Id}");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);

        var refreshed = await admin.GetFromJsonAsync<ScheduledEventDetailDto>($"/api/scheduled-events/{evt.Id}", JsonOpts);
        Assert.NotNull(refreshed);
        Assert.DoesNotContain(refreshed.Reminders, r => r.IsActive);
    }

    // =========================================================================
    // 5. UNIFIED ATTENTION PROJECTION TESTS
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
            ws.Id, desk.Id, null, "Hearing", "Today Item", null, delhiToday, null, "High"
        ));

        // Tomorrow event
        await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, desk.Id, null, "Meeting", "Tomorrow Item", null, delhiToday.AddDays(1), null, "Medium"
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
    public async Task MyAttention_NeedsRouting_SurfacesUnassignedScheduledEvents()
    {
        var admin = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync($"WS_ROUT_{Guid.NewGuid():N}", "Needs Routing WS");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Create scheduled event without responsible desk
        var createRes = await admin.PostAsJsonAsync("/api/scheduled-events", new CreateScheduledEventApiRequest(
            ws.Id, null, null, "NoticeExpiry", "Unrouted Notice Event", null, today.AddDays(2), null, "High"
        ));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        var res = await admin.GetAsync("/api/attention/my?bucket=NeedsRouting");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var feed = await res.Content.ReadFromJsonAsync<AttentionFeedResult>(JsonOpts);
        Assert.NotNull(feed);
        Assert.Contains(feed.Items, i => i.Title == "Unrouted Notice Event");
    }

    // =========================================================================
    // 6. COURT PROCEEDING PROMOTION TESTS
    // =========================================================================

    [Fact]
    public async Task PromoteCourtProceeding_WithNextDate_Succeeds_DuplicatePrevented()
    {
        var admin = await CreateAdminClientAsync();

        Guid procId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var courtCase = new CourtCase
            {
                Id = Guid.NewGuid(),
                CaseNumber = $"WP_{Guid.NewGuid():N}",
                CourtName = "High Court of Delhi",
                CaseType = "Writ Petition",
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
                ProceedingDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                NextDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
                Summary = "Counter affidavit directed",
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.CourtProceedings.Add(proc);
            await db.SaveChangesAsync();

            procId = proc.Id;
        }

        // Promote to ScheduledEvent
        var promoteReq = new CreateFromCourtProceedingApiRequest(
            null, null, "Next Hearing: State vs Landowners", null, "High", null
        );

        var res = await admin.PostAsJsonAsync($"/api/scheduled-events/from-court-proceeding/{procId}", promoteReq);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var created = await res.Content.ReadFromJsonAsync<ScheduledEventDetailDto>(JsonOpts);
        Assert.NotNull(created);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)), created.ScheduledDate);
        Assert.Equal("CourtHearing", created.EventKind);
        Assert.Equal("CourtProceeding", created.Origin);

        // Attempting to promote the same proceeding again fails with conflict
        var dupRes = await admin.PostAsJsonAsync($"/api/scheduled-events/from-court-proceeding/{procId}", promoteReq);
        Assert.Equal(HttpStatusCode.Conflict, dupRes.StatusCode);
    }

    [Fact]
    public async Task PromoteCourtProceeding_WithoutNextDate_RejectedWithBadRequest()
    {
        var admin = await CreateAdminClientAsync();

        Guid procId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var courtCase = new CourtCase
            {
                Id = Guid.NewGuid(),
                CaseNumber = $"LAA_{Guid.NewGuid():N}",
                CourtName = "High Court",
                CaseType = "Appeal",
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
                NextDate = null, // No NextDate!
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.CourtProceedings.Add(proc);
            await db.SaveChangesAsync();
            procId = proc.Id;
        }

        var res = await admin.PostAsJsonAsync($"/api/scheduled-events/from-court-proceeding/{procId}", new CreateFromCourtProceedingApiRequest(
            null, null, null, null, "Medium", null
        ));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
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
            ws.Id, desk.Id, null, "ReportSubmission", "Official Submission Date", null, today.AddDays(7), null, "High"
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
            ws.Id, desk.Id, null, "SiteInspection", "History Inspection Event", "Inspecting land boundaries", today.AddDays(4), null, "High"
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
}
