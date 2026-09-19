namespace LAC.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

public sealed class DakTestFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"dak-tests-{Guid.NewGuid()}";
    public const string TestAdminUser = "dak_admin";
    public const string TestAdminPass = "DakAdminPass!123";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = TestAdminUser,
                ["BootstrapAdmin:Password"] = TestAdminPass,
                ["BootstrapAdmin:DisplayName"] = "Dak Test Administrator"
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

public sealed class DakTests : IClassFixture<DakTestFactory>
{
    private readonly DakTestFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public DakTests(DakTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(DakTestFactory.TestAdminUser, DakTestFactory.TestAdminPass));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return client;
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateScopedUserClientAsync(string username, string roleCode, ScopeMode scopeMode, Guid? deskId = null, Guid? workstreamId = null)
    {
        var adminClient = await CreateAdminClientAsync();

        // 1. Create Role with Dak permissions
        var permCodes = new[] { PermissionCodes.DakView, PermissionCodes.DakRegister, PermissionCodes.DakEdit, PermissionCodes.DakMove, PermissionCodes.DakDispose, PermissionCodes.DakCancel };
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

        // 2. Create User
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

        // 3. Assign Desk if requested
        if (deskId.HasValue)
        {
            var assignRes = await adminClient.PostAsJsonAsync($"/api/admin/users/{userId}/desks", new AssignDeskRequest(deskId.Value, IsPrimary: true));
            Assert.Equal(HttpStatusCode.Created, assignRes.StatusCode);
        }

        // 4. Create logged-in client for this scoped user
        var userClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginRes = await userClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, userPass));
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        return (userClient, userId);
    }

    private static MultipartFormDataContent CreateRegisterForm(string diaryNumber, string subject, string senderName, string? priority = "Routine", string? workstreamId = null, byte[]? fileBytes = null, string? fileName = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(diaryNumber), "diaryNumber");
        form.Add(new StringContent(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")), "receivedDate");
        form.Add(new StringContent(subject), "subject");
        form.Add(new StringContent(senderName), "senderName");
        form.Add(new StringContent("Physical / By Hand"), "inwardMode");
        form.Add(new StringContent(priority ?? "Routine"), "priority");

        if (!string.IsNullOrEmpty(workstreamId))
            form.Add(new StringContent(workstreamId), "workstreamId");

        if (fileBytes != null && fileName != null)
        {
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
            form.Add(fileContent, "file", fileName);
        }

        return form;
    }

    // ------------------------------------------------------------------------
    // GROUP 1: Registration without assignment (Initial Intake)
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group01_Registration_without_assignment_creates_Registered_dak_and_initial_movement()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        using var form = CreateRegisterForm(diaryNo, "Representation regarding village land", "Ram Singh");
        var res = await client.PostAsync("/api/dak", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        var id = doc.RootElement.GetProperty("id").GetGuid();
        var status = doc.RootElement.GetProperty("status").GetString();
        var revision = doc.RootElement.GetProperty("revision").GetInt32();

        Assert.Equal("Registered", status);
        Assert.Equal(0, revision);

        // Verify in DB directly
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var dak = await db.Daks
            .Include(d => d.CurrentAssignment)
            .Include(d => d.Movements)
            .FirstOrDefaultAsync(d => d.Id == id);

        Assert.NotNull(dak);
        Assert.Equal(DakStatus.Registered, dak.Status);
        Assert.Null(dak.CurrentAssignment);
        Assert.Single(dak.Movements);

        var m1 = dak.Movements.First();
        Assert.Equal(1, m1.SequenceNumber);
        Assert.Equal(DakMovementAction.Registered, m1.Action);
        Assert.Null(m1.FromDeskId);
        Assert.Null(m1.ToDeskId);
    }

    // ------------------------------------------------------------------------
    // GROUP 2: Immediate marking to desk transitions status to InProcess
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group02_Marking_dak_transitions_status_to_InProcess_and_creates_assignment()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        // Create active desk
        var deskCode = $"DESK_{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var deskRes = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest(deskCode, "Tehsildar Desk", null, null));
        var deskId = (await deskRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Register Dak
        using var form = CreateRegisterForm(diaryNo, "Subject 2", "Sender 2");
        var regRes = await client.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // Move -> Marked
        var moveReq = new MoveDakRequest(
            Action: "Marked",
            ToDeskId: deskId,
            ToUserId: null,
            Remarks: "Marked to Tehsildar for inquiry",
            Instructions: "Please submit report within 7 days",
            ExpectedRevision: 0
        );
        var moveRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/move", moveReq);
        Assert.Equal(HttpStatusCode.OK, moveRes.StatusCode);

        var moveDoc = await JsonDocument.ParseAsync(await moveRes.Content.ReadAsStreamAsync());
        Assert.Equal("InProcess", moveDoc.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, moveDoc.RootElement.GetProperty("revision").GetInt32());

        // Verify DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var dak = await db.Daks.Include(d => d.CurrentAssignment).Include(d => d.Movements).FirstAsync(d => d.Id == dakId);
        Assert.Equal(DakStatus.InProcess, dak.Status);
        Assert.NotNull(dak.CurrentAssignment);
        Assert.True(dak.CurrentAssignment.IsActive);
        Assert.Equal(deskId, dak.CurrentAssignment.OfficeDeskId);
        Assert.Equal(2, dak.Movements.Count);

        var m2 = dak.Movements.OrderBy(m => m.SequenceNumber).Last();
        Assert.Equal(2, m2.SequenceNumber);
        Assert.Equal(DakMovementAction.Marked, m2.Action);
        Assert.Equal(deskId, m2.ToDeskId);
    }

    // ------------------------------------------------------------------------
    // GROUP 3: Marking validation (inactive or nonexistent desk)
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group03_Move_fails_if_target_desk_does_not_exist_or_is_inactive()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        using var form = CreateRegisterForm(diaryNo, "Subject 3", "Sender 3");
        var regRes = await client.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // Nonexistent desk
        var nonExistentRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Marked", Guid.NewGuid(), null, null, null, 0));
        Assert.Equal(HttpStatusCode.BadRequest, nonExistentRes.StatusCode);

        // Inactive desk
        var deskCode = $"DESK_INACT_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var deskRes = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest(deskCode, "Inactive Desk", null, null));
        var inactiveDeskId = (await deskRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        await client.PostAsync($"/api/admin/desks/{inactiveDeskId}/toggle-status", null);

        var inactRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Marked", inactiveDeskId, null, null, null, 0));
        Assert.Equal(HttpStatusCode.BadRequest, inactRes.StatusCode);
    }

    // ------------------------------------------------------------------------
    // GROUP 4: Earmarked user validation
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group04_Move_fails_if_target_user_is_not_an_active_member_of_target_desk()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        // Desk
        var deskCode = $"DESK_USER_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var deskRes = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest(deskCode, "Desk with Users", null, null));
        var deskId = (await deskRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // User not in desk
        var nonMemberRes = await client.PostAsJsonAsync("/api/admin/users", new CreateUserRequest($"user_nomem_{Guid.NewGuid():N}"[..16], "No Member", "Pass!123", null, null, null, null));
        var nonMemberId = (await nonMemberRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // User who IS in desk
        var memberRes = await client.PostAsJsonAsync("/api/admin/users", new CreateUserRequest($"user_mem_{Guid.NewGuid():N}"[..16], "Real Member", "Pass!123", null, null, null, null));
        var memberId = (await memberRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        await client.PostAsJsonAsync($"/api/admin/users/{memberId}/desks", new AssignDeskRequest(deskId, IsPrimary: true));

        // Register Dak
        using var form = CreateRegisterForm(diaryNo, "Subject 4", "Sender 4");
        var regRes = await client.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // 1. Move to Desk with non-member user -> fails 400
        var failRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Marked", deskId, nonMemberId, null, null, 0));
        Assert.Equal(HttpStatusCode.BadRequest, failRes.StatusCode);

        // 2. Move to Desk with valid member user -> succeeds 200
        var okRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Marked", deskId, memberId, null, null, 0));
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var dak = await db.Daks.Include(d => d.CurrentAssignment).FirstAsync(d => d.Id == dakId);
        Assert.Equal(memberId, dak.CurrentAssignment!.AssignedUserId);
    }

    // ------------------------------------------------------------------------
    // GROUP 5: Whitelist of allowed movement actions on /move
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group05_Move_endpoint_rejects_unapproved_actions_with_400()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        var deskCode = $"DESK_WL_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var deskRes = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest(deskCode, "Desk WL", null, null));
        var deskId = (await deskRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var form = CreateRegisterForm(diaryNo, "Subject 5", "Sender 5");
        var regRes = await client.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // Try Disposed on /move
        var r1 = await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Disposed", deskId, null, null, null, 0));
        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);

        // Try Cancelled on /move
        var r2 = await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Cancelled", deskId, null, null, null, 0));
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);

        // Try Registered on /move
        var r3 = await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Registered", deskId, null, null, null, 0));
        Assert.Equal(HttpStatusCode.BadRequest, r3.StatusCode);

        // Try Random garbage
        var r4 = await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("InvalidAction", deskId, null, null, null, 0));
        Assert.Equal(HttpStatusCode.BadRequest, r4.StatusCode);
    }

    // ------------------------------------------------------------------------
    // GROUP 6: Forwarding transitions between desks
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group06_Forwarding_updates_single_assignment_projection_and_records_movement_history()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        var resA = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_6A_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk 6A", null, null));
        var deskAId = (await resA.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var resB = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_6B_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk 6B", null, null));
        var deskBId = (await resB.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var form = CreateRegisterForm(diaryNo, "Subject 6", "Sender 6");
        var regRes = await client.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // 1. Initial Mark to Desk A
        await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Marked", deskAId, null, null, null, 0));

        // 2. Forward to Desk B (revision = 1)
        var forwardRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Forwarded", deskBId, null, "Forwarded for verification", null, 1));
        Assert.Equal(HttpStatusCode.OK, forwardRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var dak = await db.Daks.Include(d => d.CurrentAssignment).Include(d => d.Movements).FirstAsync(d => d.Id == dakId);

        Assert.Equal(DakStatus.InProcess, dak.Status);
        Assert.Equal(2, dak.Revision);
        Assert.Equal(deskBId, dak.CurrentAssignment!.OfficeDeskId);
        Assert.True(dak.CurrentAssignment.IsActive);

        // Sequence #3
        Assert.Equal(3, dak.Movements.Count);
        var m3 = dak.Movements.OrderBy(m => m.SequenceNumber).Last();
        Assert.Equal(3, m3.SequenceNumber);
        Assert.Equal(DakMovementAction.Forwarded, m3.Action);
        Assert.Equal(deskAId, m3.FromDeskId);
        Assert.Equal(deskBId, m3.ToDeskId);
    }

    // ------------------------------------------------------------------------
    // GROUP 7: Returning dak back to previous desk
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group07_Returning_records_Returned_action_and_transfers_custody()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        var resA = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_7A_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk 7A", null, null));
        var deskAId = (await resA.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var resB = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_7B_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk 7B", null, null));
        var deskBId = (await resB.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var form = CreateRegisterForm(diaryNo, "Subject 7", "Sender 7");
        var regRes = await client.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // 1. Mark to Desk A
        await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Marked", deskAId, null, null, null, 0));
        // 2. Forward to Desk B
        await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Forwarded", deskBId, null, null, null, 1));

        // 3. Return back to Desk A (revision = 2)
        var returnRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Returned", deskAId, null, "Returned with report", null, 2));
        Assert.Equal(HttpStatusCode.OK, returnRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var dak = await db.Daks.Include(d => d.CurrentAssignment).Include(d => d.Movements).FirstAsync(d => d.Id == dakId);

        Assert.Equal(3, dak.Revision);
        Assert.Equal(deskAId, dak.CurrentAssignment!.OfficeDeskId);

        var m4 = dak.Movements.OrderBy(m => m.SequenceNumber).Last();
        Assert.Equal(4, m4.SequenceNumber);
        Assert.Equal(DakMovementAction.Returned, m4.Action);
        Assert.Equal(deskBId, m4.FromDeskId);
        Assert.Equal(deskAId, m4.ToDeskId);
    }

    // ------------------------------------------------------------------------
    // GROUP 8: Concurrency control via ExpectedRevision returns 409 Conflict
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group08_Stale_ExpectedRevision_returns_409_Conflict()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        var resA = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_8_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk 8", null, null));
        var deskId = (await resA.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var form = CreateRegisterForm(diaryNo, "Subject 8", "Sender 8");
        var regRes = await client.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // 1. Successful move advances revision to 1
        await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Marked", deskId, null, null, null, 0));

        // 2. Another attempt using stale revision 0 -> 409 Conflict
        var staleMoveRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Forwarded", deskId, null, null, null, 0));
        Assert.Equal(HttpStatusCode.Conflict, staleMoveRes.StatusCode);

        // 3. Metadata update using stale revision 0 -> 409 Conflict
        var stalePutRes = await client.PutAsJsonAsync($"/api/dak/{dakId}", new UpdateDakMetadataRequest(
            "New Subject", "New Sender", null, null, null, null, null, "Email", DakPriority.Urgent, null, null, null, ExpectedRevision: 0));
        Assert.Equal(HttpStatusCode.Conflict, stalePutRes.StatusCode);

        // 4. Dispose using stale revision 0 -> 409 Conflict
        var staleDisposeRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/dispose", new DisposeDakRequest("Disposed", ExpectedRevision: 0));
        Assert.Equal(HttpStatusCode.Conflict, staleDisposeRes.StatusCode);

        // 5. Cancel using stale revision 0 -> 409 Conflict
        var staleCancelRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/cancel", new CancelDakRequest("Cancel", ExpectedRevision: 0));
        Assert.Equal(HttpStatusCode.Conflict, staleCancelRes.StatusCode);
    }

    // ------------------------------------------------------------------------
    // GROUP 9: Disposal transitions to terminal Disposed status
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group09_Disposal_deactivates_assignment_and_blocks_further_mutation()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        var resDesk = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_9_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk 9", null, null));
        var deskId = (await resDesk.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var form = CreateRegisterForm(diaryNo, "Subject 9", "Sender 9");
        var regRes = await client.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // Mark to desk
        await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Marked", deskId, null, null, null, 0));

        // Dispose Dak (revision = 1)
        var disposeRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/dispose", new DisposeDakRequest("Compliance completed and communicated to applicant", ExpectedRevision: 1));
        Assert.Equal(HttpStatusCode.OK, disposeRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var dak = await db.Daks.Include(d => d.CurrentAssignment).Include(d => d.Movements).FirstAsync(d => d.Id == dakId);

        Assert.Equal(DakStatus.Disposed, dak.Status);
        Assert.NotNull(dak.CurrentAssignment);
        Assert.False(dak.CurrentAssignment.IsActive); // Deactivated

        var lastMovement = dak.Movements.OrderBy(m => m.SequenceNumber).Last();
        Assert.Equal(DakMovementAction.Disposed, lastMovement.Action);
        Assert.Equal(deskId, lastMovement.FromDeskId);

        // Attempting further movement on disposed dak returns 400
        var postDisposeMoveRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Forwarded", deskId, null, null, null, 2));
        Assert.Equal(HttpStatusCode.BadRequest, postDisposeMoveRes.StatusCode);
    }

    // ------------------------------------------------------------------------
    // GROUP 10: Cancellation transitions to terminal Cancelled status
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group10_Cancellation_marks_Cancelled_status_and_blocks_further_movement()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        using var form = CreateRegisterForm(diaryNo, "Subject 10", "Sender 10");
        var regRes = await client.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // Cancel (revision = 0)
        var cancelRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/cancel", new CancelDakRequest("Registered twice by error", ExpectedRevision: 0));
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var dak = await db.Daks.Include(d => d.Movements).FirstAsync(d => d.Id == dakId);

        Assert.Equal(DakStatus.Cancelled, dak.Status);
        var lastMovement = dak.Movements.OrderBy(m => m.SequenceNumber).Last();
        Assert.Equal(DakMovementAction.Cancelled, lastMovement.Action);

        // Subsequent operations fail with 400
        var postCancelRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/dispose", new DisposeDakRequest("Remarks", ExpectedRevision: 1));
        Assert.Equal(HttpStatusCode.BadRequest, postCancelRes.StatusCode);
    }

    // ------------------------------------------------------------------------
    // GROUP 11: Movement immutability guard
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group11_DakMovement_cannot_be_updated_or_deleted_in_dbcontext()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        using var form = CreateRegisterForm(diaryNo, "Subject 11", "Sender 11");
        var regRes = await client.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var movement = await db.DakMovements.FirstAsync(m => m.DakId == dakId);

        // Attempt to update remarks
        movement.Remarks = "Tampered Remarks";
        var exUpdate = await Assert.ThrowsAsync<InvalidOperationException>(async () => await db.SaveChangesAsync());
        Assert.Contains("immutable", exUpdate.Message, StringComparison.OrdinalIgnoreCase);

        // Detach and attempt to remove
        db.Entry(movement).State = EntityState.Detached;
        var freshMovement = await db.DakMovements.FirstAsync(m => m.DakId == dakId);
        db.DakMovements.Remove(freshMovement);
        var exDelete = await Assert.ThrowsAsync<InvalidOperationException>(async () => await db.SaveChangesAsync());
        Assert.Contains("immutable", exDelete.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------------
    // GROUP 12: Union-of-scopes collection query filtering
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group12_Union_of_scopes_correctly_filters_collection_query()
    {
        using var adminClient = await CreateAdminClientAsync();

        // 1. Create two desks
        var desk1Res = await adminClient.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_12A_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk 12A", null, null));
        var desk1Id = (await desk1Res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var desk2Res = await adminClient.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_12B_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk 12B", null, null));
        var desk2Id = (await desk2Res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // 2. Create two Daks: Dak1 assigned to Desk1, Dak2 assigned to Desk2
        using var form1 = CreateRegisterForm($"DAK_12_1_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Subject 12-1", "Sender 1");
        var r1 = await adminClient.PostAsync("/api/dak", form1);
        var dak1Id = (await JsonDocument.ParseAsync(await r1.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();
        await adminClient.PostAsJsonAsync($"/api/dak/{dak1Id}/move", new MoveDakRequest("Marked", desk1Id, null, null, null, 0));

        using var form2 = CreateRegisterForm($"DAK_12_2_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Subject 12-2", "Sender 2");
        var r2 = await adminClient.PostAsync("/api/dak", form2);
        var dak2Id = (await JsonDocument.ParseAsync(await r2.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();
        await adminClient.PostAsJsonAsync($"/api/dak/{dak2Id}/move", new MoveDakRequest("Marked", desk2Id, null, null, null, 0));

        // 3. User with ScopeMode.Assigned on Desk1
        var (userAssignedClient, _) = await CreateScopedUserClientAsync(
            $"user_assigned_{Guid.NewGuid():N}"[..16], "ROLE_ASSIGNED_ONLY", ScopeMode.Assigned, deskId: desk1Id);

        var listRes = await userAssignedClient.GetAsync("/api/dak");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);

        var listDoc = await JsonDocument.ParseAsync(await listRes.Content.ReadAsStreamAsync());
        var items = listDoc.RootElement.GetProperty("items").EnumerateArray().ToList();

        // Must see Dak1, must NOT see Dak2
        Assert.Contains(items, item => item.GetProperty("id").GetGuid() == dak1Id);
        Assert.DoesNotContain(items, item => item.GetProperty("id").GetGuid() == dak2Id);
    }

    // ------------------------------------------------------------------------
    // GROUP 13: Scoped document content streaming
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group13_Scoped_document_content_streaming_denies_unauthorized_users()
    {
        using var adminClient = await CreateAdminClientAsync();

        var desk1Res = await adminClient.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_13A_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk 13A", null, null));
        var desk1Id = (await desk1Res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var desk2Res = await adminClient.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_13B_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk 13B", null, null));
        var desk2Id = (await desk2Res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Register Dak with main document PDF assigned to Desk1
        var pdfBytes = "%PDF-1.4 sample content"u8.ToArray();
        using var form = CreateRegisterForm($"DAK_13_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Subject 13", "Sender 13", fileBytes: pdfBytes, fileName: "letter.pdf");
        var regRes = await adminClient.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();
        await adminClient.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Marked", desk1Id, null, null, null, 0));

        // Authorized user (Assigned to Desk 1) can download
        var (authClient, _) = await CreateScopedUserClientAsync($"user_auth13_{Guid.NewGuid():N}"[..16], "ROLE_AUTH13", ScopeMode.Assigned, deskId: desk1Id);
        var okStreamRes = await authClient.GetAsync($"/api/dak/{dakId}/content");
        Assert.Equal(HttpStatusCode.OK, okStreamRes.StatusCode);

        // Unauthorized user (Assigned to Desk 2) is forbidden (403)
        var (unauthClient, _) = await CreateScopedUserClientAsync($"user_unauth13_{Guid.NewGuid():N}"[..16], "ROLE_UNAUTH13", ScopeMode.Assigned, deskId: desk2Id);
        var forbidStreamRes = await unauthClient.GetAsync($"/api/dak/{dakId}/content");
        Assert.Equal(HttpStatusCode.Forbidden, forbidStreamRes.StatusCode);
    }

    // ------------------------------------------------------------------------
    // GROUP 14: Attachments upload and soft-delete
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group14_Attachments_can_be_added_and_soft_deleted()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        using var form = CreateRegisterForm(diaryNo, "Subject 14", "Sender 14");
        var regRes = await client.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // Add attachment
        using var attachForm = new MultipartFormDataContent();
        var pdfBytes = "%PDF-1.4 attachment content"u8.ToArray();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        attachForm.Add(fileContent, "file", "annexure1.pdf");
        attachForm.Add(new StringContent("Site Plan Annexure"), "title");
        attachForm.Add(new StringContent("Annexure"), "attachmentType");

        var attachRes = await client.PostAsync($"/api/dak/{dakId}/attachments", attachForm);
        Assert.Equal(HttpStatusCode.Created, attachRes.StatusCode);
        var attachId = (await JsonDocument.ParseAsync(await attachRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // Verify attachment is present in detail
        var detailRes1 = await client.GetAsync($"/api/dak/{dakId}");
        var detailDoc1 = await JsonDocument.ParseAsync(await detailRes1.Content.ReadAsStreamAsync());
        var attList1 = detailDoc1.RootElement.GetProperty("attachments").EnumerateArray().ToList();
        Assert.Single(attList1);
        Assert.Equal(attachId, attList1[0].GetProperty("id").GetGuid());

        // Soft delete attachment
        var delRes = await client.DeleteAsync($"/api/dak/{dakId}/attachments/{attachId}");
        Assert.True(delRes.StatusCode == HttpStatusCode.NoContent || delRes.StatusCode == HttpStatusCode.OK);

        // Verify attachment is removed from detail
        var detailRes2 = await client.GetAsync($"/api/dak/{dakId}");
        var detailDoc2 = await JsonDocument.ParseAsync(await detailRes2.Content.ReadAsStreamAsync());
        var attList2 = detailDoc2.RootElement.GetProperty("attachments").EnumerateArray().ToList();
        Assert.Empty(attList2);

        // Verify DB soft-delete flag
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var attInDb = await db.DakAttachments.FirstAsync(a => a.Id == attachId);
        Assert.Equal(RecordStatus.Archived, attInDb.RecordStatus);
    }

    // ------------------------------------------------------------------------
    // GROUP 15: Strongly-typed domain links (Awards, Villages, Matters, Khasras)
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group15_Domain_links_can_be_created_duplicate_prevented_and_soft_deleted()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        // Seed a Village and an Award
        Guid villageId;
        Guid awardId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var district = new District { Name = "North East" };
            db.Districts.Add(district);
            var subDiv = new SubDivision { Name = "Yamuna Vihar", District = district };
            db.SubDivisions.Add(subDiv);
            var village = new Village { Name = "Mandoli", SubDivision = subDiv };
            db.Villages.Add(village);
            var award = new Award { AwardNumber = $"AW_{Guid.NewGuid():N}"[..10] };
            award.VillageLinks.Add(new AwardVillage { Award = award, Village = village });
            db.Awards.Add(award);
            await db.SaveChangesAsync();

            villageId = village.Id;
            awardId = award.Id;
        }

        using var form = CreateRegisterForm(diaryNo, "Subject 15", "Sender 15");
        var regRes = await client.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // 1. Link Award
        var linkAwardRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/links/awards", new { entityId = awardId });
        Assert.Equal(HttpStatusCode.Created, linkAwardRes.StatusCode);
        var linkId = (await JsonDocument.ParseAsync(await linkAwardRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("linkId").GetGuid();

        // 2. Duplicate active link returns 409 Conflict
        var dupAwardRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/links/awards", new { entityId = awardId });
        Assert.Equal(HttpStatusCode.Conflict, dupAwardRes.StatusCode);

        // 3. Link Village
        var linkVillageRes = await client.PostAsJsonAsync($"/api/dak/{dakId}/links/villages", new { entityId = villageId });
        Assert.Equal(HttpStatusCode.Created, linkVillageRes.StatusCode);

        // 4. Verify in detail view
        var detailRes = await client.GetAsync($"/api/dak/{dakId}");
        var doc = await JsonDocument.ParseAsync(await detailRes.Content.ReadAsStreamAsync());
        Assert.Single(doc.RootElement.GetProperty("awardLinks").EnumerateArray());
        Assert.Single(doc.RootElement.GetProperty("villageLinks").EnumerateArray());

        // 5. Delete Award link
        var delRes = await client.DeleteAsync($"/api/dak/{dakId}/links/awards/{linkId}");
        Assert.True(delRes.StatusCode == HttpStatusCode.NoContent || delRes.StatusCode == HttpStatusCode.OK);

        // Verify soft delete in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var linkInDb = await db.DakAwardLinks.FirstAsync(l => l.Id == linkId);
            Assert.Equal(RecordStatus.Archived, linkInDb.RecordStatus);
        }
    }

    // ------------------------------------------------------------------------
    // GROUP 16: Custody Attention / Health Flags
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group16_NeedsAttention_flag_triggers_when_assigned_desk_or_user_becomes_inactive()
    {
        using var client = await CreateAdminClientAsync();
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        // Create Desk and Member
        var deskCode = $"DESK_16_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var deskRes = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest(deskCode, "Desk 16", null, null));
        var deskId = (await deskRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var memberRes = await client.PostAsJsonAsync("/api/admin/users", new CreateUserRequest($"user_16_{Guid.NewGuid():N}"[..16], "Member 16", "Pass!123", null, null, null, null));
        var memberId = (await memberRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var memRes = await client.PostAsJsonAsync($"/api/admin/users/{memberId}/desks", new AssignDeskRequest(deskId, IsPrimary: true));
        var membershipId = (await memRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Register Dak & Mark to Desk with member user
        using var form = CreateRegisterForm(diaryNo, "Subject 16", "Sender 16");
        var regRes = await client.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        await client.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Marked", deskId, memberId, null, null, 0));

        // 1. Initially healthy
        var d1 = await client.GetAsync($"/api/dak/{dakId}");
        var doc1 = await JsonDocument.ParseAsync(await d1.Content.ReadAsStreamAsync());
        var currentAssign1 = doc1.RootElement.GetProperty("currentAssignment");
        Assert.False(currentAssign1.GetProperty("needsAttention").GetBoolean());
        Assert.True(currentAssign1.GetProperty("isDeskActive").GetBoolean());
        Assert.True(currentAssign1.GetProperty("isUserEligible").GetBoolean());

        // 2. Remove user from desk
        await client.PostAsync($"/api/admin/users/{memberId}/desks/{membershipId}/remove", null);

        var d2 = await client.GetAsync($"/api/dak/{dakId}");
        var doc2 = await JsonDocument.ParseAsync(await d2.Content.ReadAsStreamAsync());
        var currentAssign2 = doc2.RootElement.GetProperty("currentAssignment");
        Assert.True(currentAssign2.GetProperty("needsAttention").GetBoolean());
        Assert.False(currentAssign2.GetProperty("isUserEligible").GetBoolean());

        // 3. Inactive Desk
        await client.PostAsync($"/api/admin/desks/{deskId}/toggle-status", null);
        var d3 = await client.GetAsync($"/api/dak/{dakId}");
        var doc3 = await JsonDocument.ParseAsync(await d3.Content.ReadAsStreamAsync());
        var currentAssign3 = doc3.RootElement.GetProperty("currentAssignment");
        Assert.True(currentAssign3.GetProperty("needsAttention").GetBoolean());
        Assert.False(currentAssign3.GetProperty("isDeskActive").GetBoolean());
    }

    // ------------------------------------------------------------------------
    // GROUP 17: Dak registration scoped authorization rules
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group17_Dak_registration_scoped_authorization_enforces_rules()
    {
        using var adminClient = await CreateAdminClientAsync();

        // Find workstream IDs for DAK_CORRESPONDENCE and AWARD
        Guid dakWsId;
        Guid awardWsId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            dakWsId = (await db.Workstreams.FirstAsync(w => w.Code == WorkstreamCodes.DakCorrespondence)).Id;
            awardWsId = (await db.Workstreams.FirstAsync(w => w.Code == WorkstreamCodes.Award)).Id;
        }

        // 1. User with ScopeMode.All -> allowed
        var (clientAll, _) = await CreateScopedUserClientAsync($"user_reg_all_{Guid.NewGuid():N}"[..16], "ROLE_REG_ALL", ScopeMode.All);
        using (var f1 = CreateRegisterForm($"DAK_ALL_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Subject All", "Sender All"))
        {
            var r1 = await clientAll.PostAsync("/api/dak", f1);
            Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        }

        // 2. User with ScopeMode.Workstream in DAK_CORRESPONDENCE -> allowed
        var (clientWsDak, _) = await CreateScopedUserClientAsync($"user_reg_ws_dak_{Guid.NewGuid():N}"[..16], "ROLE_REG_WS_DAK", ScopeMode.Workstream, workstreamId: dakWsId);
        using (var f2 = CreateRegisterForm($"DAK_WS_OK_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Subject WS Dak", "Sender WS Dak"))
        {
            var r2 = await clientWsDak.PostAsync("/api/dak", f2);
            Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        }

        // 3. User with ScopeMode.Workstream in AWARD (not DAK_CORRESPONDENCE) -> forbidden 403
        var (clientWsAward, _) = await CreateScopedUserClientAsync($"user_reg_ws_awd_{Guid.NewGuid():N}"[..16], "ROLE_REG_WS_AWD", ScopeMode.Workstream, workstreamId: awardWsId);
        using (var f3 = CreateRegisterForm($"DAK_WS_NO_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Subject WS Award", "Sender WS Award"))
        {
            var r3 = await clientWsAward.PostAsync("/api/dak", f3);
            Assert.Equal(HttpStatusCode.Forbidden, r3.StatusCode);
        }

        // 4. User with ScopeMode.Assigned -> forbidden 403
        var (clientAssigned, _) = await CreateScopedUserClientAsync($"user_reg_asg_{Guid.NewGuid():N}"[..16], "ROLE_REG_ASG", ScopeMode.Assigned);
        using (var f4 = CreateRegisterForm($"DAK_ASG_NO_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Subject Assigned", "Sender Assigned"))
        {
            var r4 = await clientAssigned.PostAsync("/api/dak", f4);
            Assert.Equal(HttpStatusCode.Forbidden, r4.StatusCode);
        }

        // 5. User with ScopeMode.Own -> forbidden 403
        var (clientOwn, _) = await CreateScopedUserClientAsync($"user_reg_own_{Guid.NewGuid():N}"[..16], "ROLE_REG_OWN", ScopeMode.Own);
        using (var f5 = CreateRegisterForm($"DAK_OWN_NO_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Subject Own", "Sender Own"))
        {
            var r5 = await clientOwn.PostAsync("/api/dak", f5);
            Assert.Equal(HttpStatusCode.Forbidden, r5.StatusCode);
        }
    }

    // ------------------------------------------------------------------------
    // GROUP 18: Strict input validation on registration
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group18_Dak_registration_strict_input_validation()
    {
        using var client = await CreateAdminClientAsync();

        // 1. Missing received date
        var formBadDate = new MultipartFormDataContent();
        formBadDate.Add(new StringContent($"DAK_{Guid.NewGuid():N}"[..16]), "diaryNumber");
        formBadDate.Add(new StringContent("not-a-date"), "receivedDate");
        formBadDate.Add(new StringContent("Subject"), "subject");
        formBadDate.Add(new StringContent("Sender"), "senderName");
        var rBadDate = await client.PostAsync("/api/dak", formBadDate);
        Assert.Equal(HttpStatusCode.BadRequest, rBadDate.StatusCode);

        // 2. Invalid priority
        var formBadPriority = new MultipartFormDataContent();
        formBadPriority.Add(new StringContent($"DAK_{Guid.NewGuid():N}"[..16]), "diaryNumber");
        formBadPriority.Add(new StringContent("2026-09-19"), "receivedDate");
        formBadPriority.Add(new StringContent("Subject"), "subject");
        formBadPriority.Add(new StringContent("Sender"), "senderName");
        formBadPriority.Add(new StringContent("SuperUrgent"), "priority");
        var rBadPriority = await client.PostAsync("/api/dak", formBadPriority);
        Assert.Equal(HttpStatusCode.BadRequest, rBadPriority.StatusCode);

        // 3. Malformed categoryId
        var formBadCat = new MultipartFormDataContent();
        formBadCat.Add(new StringContent($"DAK_{Guid.NewGuid():N}"[..16]), "diaryNumber");
        formBadCat.Add(new StringContent("2026-09-19"), "receivedDate");
        formBadCat.Add(new StringContent("Subject"), "subject");
        formBadCat.Add(new StringContent("Sender"), "senderName");
        formBadCat.Add(new StringContent("invalid-guid"), "categoryId");
        var rBadCat = await client.PostAsync("/api/dak", formBadCat);
        Assert.Equal(HttpStatusCode.BadRequest, rBadCat.StatusCode);

        // 4. Malformed workstreamId
        var formBadWs = new MultipartFormDataContent();
        formBadWs.Add(new StringContent($"DAK_{Guid.NewGuid():N}"[..16]), "diaryNumber");
        formBadWs.Add(new StringContent("2026-09-19"), "receivedDate");
        formBadWs.Add(new StringContent("Subject"), "subject");
        formBadWs.Add(new StringContent("Sender"), "senderName");
        formBadWs.Add(new StringContent("invalid-guid"), "workstreamId");
        var rBadWs = await client.PostAsync("/api/dak", formBadWs);
        Assert.Equal(HttpStatusCode.BadRequest, rBadWs.StatusCode);

        // 5. Empty diaryNumber
        var formEmptyDiary = new MultipartFormDataContent();
        formEmptyDiary.Add(new StringContent("   "), "diaryNumber");
        formEmptyDiary.Add(new StringContent("2026-09-19"), "receivedDate");
        formEmptyDiary.Add(new StringContent("Subject"), "subject");
        formEmptyDiary.Add(new StringContent("Sender"), "senderName");
        var rEmptyDiary = await client.PostAsync("/api/dak", formEmptyDiary);
        Assert.Equal(HttpStatusCode.BadRequest, rEmptyDiary.StatusCode);
    }

    // ------------------------------------------------------------------------
    // GROUP 19: Server-side MIME & Magic Bytes Validation
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group19_Server_side_MIME_and_magic_bytes_validation()
    {
        using var client = await CreateAdminClientAsync();

        // 1. Spoofed PDF with HTML text -> rejected 400
        var fakePdfBytes = "<html><body>evil script</body></html>"u8.ToArray();
        using var formFakePdf = CreateRegisterForm($"DAK_SPOOF_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Spoof Subject", "Sender", fileBytes: fakePdfBytes, fileName: "exploit.pdf");
        var resFakePdf = await client.PostAsync("/api/dak", formFakePdf);
        Assert.Equal(HttpStatusCode.BadRequest, resFakePdf.StatusCode);

        // 2. Spoofed PNG with plain text -> rejected 400
        var fakePngBytes = "Plain text pretending to be an image"u8.ToArray();
        using var formFakePng = new MultipartFormDataContent();
        formFakePng.Add(new StringContent($"DAK_PNG_{Guid.NewGuid():N}"[..16]), "diaryNumber");
        formFakePng.Add(new StringContent("2026-09-19"), "receivedDate");
        formFakePng.Add(new StringContent("PNG Subject"), "subject");
        formFakePng.Add(new StringContent("Sender"), "senderName");
        var fakePngContent = new ByteArrayContent(fakePngBytes);
        fakePngContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        formFakePng.Add(fakePngContent, "file", "fake.png");
        var resFakePng = await client.PostAsync("/api/dak", formFakePng);
        Assert.Equal(HttpStatusCode.BadRequest, resFakePng.StatusCode);

        // 3. Genuine PDF -> 201 Created and MimeType is application/pdf
        var validPdfBytes = "%PDF-1.7 Valid Header content"u8.ToArray();
        using var formValidPdf = CreateRegisterForm($"DAK_VALID_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Valid Subject", "Sender", fileBytes: validPdfBytes, fileName: "document.pdf");
        var resValidPdf = await client.PostAsync("/api/dak", formValidPdf);
        Assert.Equal(HttpStatusCode.Created, resValidPdf.StatusCode);

        var dakId = (await JsonDocument.ParseAsync(await resValidPdf.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var dak = await db.Daks.Include(d => d.MainDocument).FirstAsync(d => d.Id == dakId);
        Assert.NotNull(dak.MainDocument);
        Assert.Equal("application/pdf", dak.MainDocument.MimeType);
    }

    // ------------------------------------------------------------------------
    // GROUP 20: Operational lookups allow non-admin authorized users
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group20_Operational_lookups_allow_authorized_non_admin_users()
    {
        using var adminClient = await CreateAdminClientAsync();

        // Create a desk and a Dak
        var deskRes = await adminClient.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_20_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk 20", null, null));
        var deskId = (await deskRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var form = CreateRegisterForm($"DAK_20_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Subject 20", "Sender 20");
        var regRes = await adminClient.PostAsync("/api/dak", form);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();
        await adminClient.PostAsJsonAsync($"/api/dak/{dakId}/move", new MoveDakRequest("Marked", deskId, null, null, null, 0));

        // Create a non-admin user assigned to this desk
        var (nonAdminClient, userId) = await CreateScopedUserClientAsync($"user_op_{Guid.NewGuid():N}"[..16], "ROLE_OPERATIONAL_TEST", ScopeMode.Assigned, deskId: deskId);

        // 1. Directory lookup (/api/dak/lookups/directory) -> 200 OK
        var dirRes = await nonAdminClient.GetAsync("/api/dak/lookups/directory");
        Assert.Equal(HttpStatusCode.OK, dirRes.StatusCode);
        var dirDoc = await JsonDocument.ParseAsync(await dirRes.Content.ReadAsStreamAsync());
        Assert.True(dirDoc.RootElement.GetProperty("desks").EnumerateArray().Any());

        // 2. Edit lookup (/api/dak/{id}/lookups/edit) -> 200 OK
        var editLookupRes = await nonAdminClient.GetAsync($"/api/dak/{dakId}/lookups/edit");
        Assert.Equal(HttpStatusCode.OK, editLookupRes.StatusCode);
        var editDoc = await JsonDocument.ParseAsync(await editLookupRes.Content.ReadAsStreamAsync());
        Assert.True(editDoc.RootElement.TryGetProperty("categories", out _));
        Assert.True(editDoc.RootElement.TryGetProperty("workstreams", out _));

        // 3. Movement targets lookup (/api/dak/{id}/movement-targets) -> 200 OK
        var moveTargetsRes = await nonAdminClient.GetAsync($"/api/dak/{dakId}/movement-targets");
        Assert.Equal(HttpStatusCode.OK, moveTargetsRes.StatusCode);
        var moveDoc = await JsonDocument.ParseAsync(await moveTargetsRes.Content.ReadAsStreamAsync());
        Assert.True(moveDoc.RootElement.TryGetProperty("desks", out var targetDesks));
        Assert.True(targetDesks.EnumerateArray().Any());
    }

    // ------------------------------------------------------------------------
    // GROUP 21: Multi-scope union visibility
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group21_Multi_scope_union_visibility()
    {
        using var adminClient = await CreateAdminClientAsync();

        Guid dakWsId;
        Guid otherWsId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            dakWsId = (await db.Workstreams.FirstAsync(w => w.Code == WorkstreamCodes.DakCorrespondence)).Id;
            otherWsId = (await db.Workstreams.FirstAsync(w => w.Code == WorkstreamCodes.LandAcquisition)).Id;
        }

        var desk1Res = await adminClient.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_21A_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk 21A", null, null));
        var desk1Id = (await desk1Res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var desk2Res = await adminClient.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"DESK_21B_{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Desk 21B", null, null));
        var desk2Id = (await desk2Res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Dak 1: in DAK_CORRESPONDENCE workstream, unassigned
        using var f1 = CreateRegisterForm($"DAK_21_WS_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Sub WS", "Sender 1", workstreamId: dakWsId.ToString());
        var r1 = await adminClient.PostAsync("/api/dak", f1);
        var dak1Id = (await JsonDocument.ParseAsync(await r1.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // Dak 2: assigned to Desk 1, in other workstream
        using var f2 = CreateRegisterForm($"DAK_21_DSK_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Sub Desk", "Sender 2", workstreamId: otherWsId.ToString());
        var r2 = await adminClient.PostAsync("/api/dak", f2);
        var dak2Id = (await JsonDocument.ParseAsync(await r2.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();
        await adminClient.PostAsJsonAsync($"/api/dak/{dak2Id}/move", new MoveDakRequest("Marked", desk1Id, null, null, null, 0));

        // Dak 3: in other workstream, assigned to Desk 2
        using var f3 = CreateRegisterForm($"DAK_21_OUT_{Guid.NewGuid():N}"[..16].ToUpperInvariant(), "Sub Outside", "Sender 3", workstreamId: otherWsId.ToString());
        var r3 = await adminClient.PostAsync("/api/dak", f3);
        var dak3Id = (await JsonDocument.ParseAsync(await r3.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();
        await adminClient.PostAsJsonAsync($"/api/dak/{dak3Id}/move", new MoveDakRequest("Marked", desk2Id, null, null, null, 0));

        // User with ScopeMode.Workstream on DAK_CORRESPONDENCE AND ScopeMode.Assigned on Desk 1
        Guid wsRoleId;
        Guid asgRoleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var pView = await db.Permissions.FirstAsync(p => p.Code == PermissionCodes.DakView);

            var rWs = new Role { Code = $"ROLE_UNION_WS_{Guid.NewGuid():N}"[..16], Name = "Union WS", IsActive = true };
            rWs.RolePermissions.Add(new RolePermission { PermissionId = pView.Id, ScopeMode = ScopeMode.Workstream });
            db.Roles.Add(rWs);

            var rAsg = new Role { Code = $"ROLE_UNION_ASG_{Guid.NewGuid():N}"[..16], Name = "Union ASG", IsActive = true };
            rAsg.RolePermissions.Add(new RolePermission { PermissionId = pView.Id, ScopeMode = ScopeMode.Assigned });
            db.Roles.Add(rAsg);

            await db.SaveChangesAsync();
            wsRoleId = rWs.Id;
            asgRoleId = rAsg.Id;
        }

        var uName = $"user_union_{Guid.NewGuid():N}"[..16];
        var uPass = "UnionPass!123";
        var createRes = await adminClient.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(
            Username: uName,
            DisplayName: "Union User",
            Password: uPass,
            DesignationId: null,
            RoleIds: [wsRoleId, asgRoleId],
            WorkstreamIds: [dakWsId],
            PrimaryWorkstreamId: dakWsId
        ));
        var userId = (await createRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        await adminClient.PostAsJsonAsync($"/api/admin/users/{userId}/desks", new AssignDeskRequest(desk1Id, IsPrimary: true));

        var userClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await userClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(uName, uPass));

        var listRes = await userClient.GetAsync("/api/dak");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var listDoc = await JsonDocument.ParseAsync(await listRes.Content.ReadAsStreamAsync());
        var items = listDoc.RootElement.GetProperty("items").EnumerateArray().ToList();

        // Must see Dak 1 (via Workstream) and Dak 2 (via Desk assignment)
        Assert.Contains(items, i => i.GetProperty("id").GetGuid() == dak1Id);
        Assert.Contains(items, i => i.GetProperty("id").GetGuid() == dak2Id);
        // Must NOT see Dak 3 (outside both)
        Assert.DoesNotContain(items, i => i.GetProperty("id").GetGuid() == dak3Id);
    }

    // ------------------------------------------------------------------------
    // GROUP 22: Workflow Execution Strategy, Concurrency, and Compensation
    // ------------------------------------------------------------------------
    [Fact]
    public async Task Group22_Workflow_registration_file_compensation_and_lifecycle_retries()
    {
        var dbName = $"dak-retry-test-{Guid.NewGuid():N}";
        var interceptor = new FailingSaveChangesInterceptor();
        var (db, adminUser, targetDesk, targetUser) = await CreateWorkflowTestContextAsync(dbName, interceptor);

        var storage = new TestInMemoryDocumentStorage();
        var workflow = new DakWorkflowService(db, storage);

        var pdfBytes = "%PDF-1.4 dummy dak content"u8.ToArray();
        using var msFail = new MemoryStream(pdfBytes);
        var cmd = new RegisterDakCommand(
            DiaryNumber: "DIARY_G22_001",
            ReceivedDate: new DateOnly(2026, 9, 19),
            Subject: "Compensation Test",
            SenderName: "Test Sender",
            SenderDesignation: null,
            SenderDepartment: null,
            SenderAddress: null,
            SenderReferenceNumber: null,
            SenderLetterDate: null,
            InwardMode: "Physical",
            Priority: DakPriority.Routine,
            DueDate: null,
            CategoryId: null,
            WorkstreamId: null,
            DocumentStream: msFail,
            DocumentFileName: "test_file.pdf",
            DocumentContentType: "application/pdf"
        );

        // 1. Induce failure on DB save inside execution strategy -> file must be compensated (deleted)
        interceptor.FailOnSave = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => workflow.RegisterAsync(cmd, adminUser.Id));

        Assert.Equal(1, storage.SaveCount);
        Assert.Equal(1, storage.DeleteCount);
        Assert.Empty(storage.Files);

        // 2. Disable failure -> registration inside execution strategy succeeds cleanly
        interceptor.FailOnSave = false;
        using var msSuccess = new MemoryStream(pdfBytes);
        var cmdSuccess = cmd with { DocumentStream = msSuccess };
        var dak = await workflow.RegisterAsync(cmdSuccess, adminUser.Id);

        Assert.NotNull(dak);
        Assert.Equal(DakStatus.Registered, dak.Status);
        Assert.Equal(0, dak.Revision);
        Assert.Equal(2, storage.SaveCount);
        Assert.Equal(1, storage.DeleteCount);
        Assert.Single(storage.Files);

        // 3. MoveDak inside execution strategy
        var moveCmd = new MoveDakCommand(
            Action: DakMovementAction.Marked,
            ToDeskId: targetDesk.Id,
            ToUserId: targetUser.Id,
            Remarks: "Assigning to target desk",
            Instructions: "Action immediately",
            ExpectedRevision: 0
        );
        var moved = await workflow.MoveAsync(dak.Id, moveCmd, adminUser.Id);
        Assert.Equal(DakStatus.InProcess, moved.Status);
        Assert.Equal(1, moved.Revision);

        // 4. Concurrency conflict check inside MoveAsync
        var staleMoveCmd = new MoveDakCommand(
            Action: DakMovementAction.Forwarded,
            ToDeskId: targetDesk.Id,
            ToUserId: targetUser.Id,
            Remarks: "Stale update",
            Instructions: null,
            ExpectedRevision: 0
        );
        var ex = await Assert.ThrowsAsync<DakWorkflowException>(() => workflow.MoveAsync(dak.Id, staleMoveCmd, adminUser.Id));
        Assert.Equal(409, ex.StatusCode);

        // 5. DisposeDak inside execution strategy
        var disposeCmd = new DisposeDakCommand("Disposed properly", ExpectedRevision: 1);
        var disposed = await workflow.DisposeAsync(dak.Id, disposeCmd, adminUser.Id);
        Assert.Equal(DakStatus.Disposed, disposed.Status);
        Assert.Equal(2, disposed.Revision);

        // 6. CancelDak inside execution strategy
        using var ms2 = new MemoryStream(pdfBytes);
        var dak2 = await workflow.RegisterAsync(cmd with { DiaryNumber = "DIARY_G22_002", DocumentStream = ms2 }, adminUser.Id);
        Assert.Equal(0, dak2.Revision);

        var cancelCmd = new CancelDakCommand("Cancellation valid", ExpectedRevision: 0);
        var cancelled = await workflow.CancelAsync(dak2.Id, cancelCmd, adminUser.Id);
        Assert.Equal(DakStatus.Cancelled, cancelled.Status);
        Assert.Equal(1, cancelled.Revision);
    }

    [Fact]
    public async Task Group22_A_Stable_workflow_ids_reused_across_retry_attempts()
    {
        var dbName = $"dak-retry-a-{Guid.NewGuid():N}";
        var trackingInterceptor = new TrackingSaveChangesInterceptor { FailTimes = 1 };
        var (db, adminUser, _, _) = await CreateWorkflowTestContextAsync(dbName, trackingInterceptor);

        var storage = new TestInMemoryDocumentStorage();
        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new DakWorkflowService(db, storage, () => strategy!);
        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: false, maxRetries: 2);

        var pdfBytes = "%PDF-1.4 dummy content"u8.ToArray();
        using var ms = new MemoryStream(pdfBytes);
        var cmd = new RegisterDakCommand(
            DiaryNumber: "DIARY_STABLE_IDS",
            ReceivedDate: new DateOnly(2026, 9, 19),
            Subject: "Stable ID Test",
            SenderName: "Revenue Office",
            SenderDesignation: null,
            SenderDepartment: null,
            SenderAddress: null,
            SenderReferenceNumber: null,
            SenderLetterDate: null,
            InwardMode: "Physical",
            Priority: DakPriority.Routine,
            DueDate: null,
            CategoryId: null,
            WorkstreamId: null,
            DocumentStream: ms,
            DocumentFileName: "stable_id.pdf",
            DocumentContentType: "application/pdf"
        );

        var dak = await workflow.RegisterAsync(cmd, adminUser.Id);

        Assert.NotNull(dak);
        Assert.Equal(2, strategy.AttemptCount);
        // Ensure entity IDs used on Attempt 1 match entity IDs used on Attempt 2 exactly
        Assert.Equal(2, trackingInterceptor.AttemptDakIds.Count);
        Assert.Equal(trackingInterceptor.AttemptDakIds[0], trackingInterceptor.AttemptDakIds[1]);
        Assert.Equal(dak.Id, trackingInterceptor.AttemptDakIds[0]);

        Assert.Equal(2, trackingInterceptor.AttemptMovementIds.Count);
        Assert.Equal(trackingInterceptor.AttemptMovementIds[0], trackingInterceptor.AttemptMovementIds[1]);
    }

    [Fact]
    public async Task Group22_B_Registration_commit_ambiguity_returns_success_without_duplicate()
    {
        var dbName = $"dak-retry-b-{Guid.NewGuid():N}";
        var (db, adminUser, _, _) = await CreateWorkflowTestContextAsync(dbName);

        var storage = new TestInMemoryDocumentStorage();
        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new DakWorkflowService(db, storage, () => strategy!);
        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2);

        var pdfBytes = "%PDF-1.4 file content"u8.ToArray();
        using var ms = new MemoryStream(pdfBytes);
        var cmd = new RegisterDakCommand(
            DiaryNumber: "DIARY_COMMIT_AMBIGUITY_REG",
            ReceivedDate: new DateOnly(2026, 9, 19),
            Subject: "Commit Ambiguity Registration",
            SenderName: "Test Sender B",
            SenderDesignation: null,
            SenderDepartment: null,
            SenderAddress: null,
            SenderReferenceNumber: null,
            SenderLetterDate: null,
            InwardMode: "Physical",
            Priority: DakPriority.Immediate,
            DueDate: null,
            CategoryId: null,
            WorkstreamId: null,
            DocumentStream: ms,
            DocumentFileName: "doc_b.pdf",
            DocumentContentType: "application/pdf"
        );

        var dak = await workflow.RegisterAsync(cmd, adminUser.Id);

        Assert.NotNull(dak);
        Assert.Equal(DakStatus.Registered, dak.Status);
        Assert.Equal(0, dak.Revision);
        Assert.Equal(1, strategy.AttemptCount);
        Assert.Equal(1, strategy.VerifyCount);

        // Verify that only 1 Dak was committed to database (no duplicate registration)
        var count = await db.Daks.CountAsync(d => d.DiaryNumber == "DIARY_COMMIT_AMBIGUITY_REG");
        Assert.Equal(1, count);

        // Verify movement sequence 1 was committed
        var movements = await db.DakMovements.Where(m => m.DakId == dak.Id).ToListAsync();
        Assert.Single(movements);
        Assert.Equal(1, movements[0].SequenceNumber);
        Assert.Equal(DakMovementAction.Registered, movements[0].Action);

        // Verify storage file was preserved (not compensated)
        Assert.Equal(1, storage.SaveCount);
        Assert.Equal(0, storage.DeleteCount);
        Assert.Single(storage.Files);
    }

    [Fact]
    public async Task Group22_C_Move_commit_ambiguity_avoids_false_409_concurrency_conflict()
    {
        var dbName = $"dak-retry-c-{Guid.NewGuid():N}";
        var (db, adminUser, targetDesk, targetUser) = await CreateWorkflowTestContextAsync(dbName);

        var storage = new TestInMemoryDocumentStorage();
        var initialWorkflow = new DakWorkflowService(db, storage);

        var cmd = new RegisterDakCommand(
            DiaryNumber: "DIARY_COMMIT_AMBIGUITY_MOVE",
            ReceivedDate: new DateOnly(2026, 9, 19),
            Subject: "Commit Ambiguity Move",
            SenderName: "Test Sender C",
            SenderDesignation: null,
            SenderDepartment: null,
            SenderAddress: null,
            SenderReferenceNumber: null,
            SenderLetterDate: null,
            InwardMode: "Physical",
            Priority: DakPriority.Urgent,
            DueDate: null,
            CategoryId: null,
            WorkstreamId: null,
            DocumentStream: null,
            DocumentFileName: null,
            DocumentContentType: null
        );

        var dak = await initialWorkflow.RegisterAsync(cmd, adminUser.Id);
        Assert.Equal(0, dak.Revision);

        // Configure workflow service with commit ambiguity strategy
        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new DakWorkflowService(db, storage, () => strategy!);
        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2);

        var moveCmd = new MoveDakCommand(
            Action: DakMovementAction.Marked,
            ToDeskId: targetDesk.Id,
            ToUserId: targetUser.Id,
            Remarks: "Forwarding with ambiguity simulation",
            Instructions: "Action quickly",
            ExpectedRevision: 0
        );

        // MoveAsync should succeed via verifySucceeded without throwing 409
        var moved = await workflow.MoveAsync(dak.Id, moveCmd, adminUser.Id);

        Assert.NotNull(moved);
        Assert.Equal(1, moved.Revision);
        Assert.Equal(DakStatus.InProcess, moved.Status);
        Assert.Equal(1, strategy.AttemptCount);
        Assert.Equal(1, strategy.VerifyCount);

        var dbDak = await db.Daks.Include(d => d.CurrentAssignment).SingleAsync(d => d.Id == dak.Id);
        Assert.Equal(1, dbDak.Revision);
        Assert.Equal(DakStatus.InProcess, dbDak.Status);
        Assert.NotNull(dbDak.CurrentAssignment);
        Assert.Equal(targetDesk.Id, dbDak.CurrentAssignment.OfficeDeskId);
        Assert.Equal(targetUser.Id, dbDak.CurrentAssignment.AssignedUserId);
        Assert.True(dbDak.CurrentAssignment.IsActive);
    }

    [Fact]
    public async Task Group22_D_Dispose_commit_ambiguity_recognized_by_verify_succeeded()
    {
        var dbName = $"dak-retry-d-{Guid.NewGuid():N}";
        var (db, adminUser, targetDesk, targetUser) = await CreateWorkflowTestContextAsync(dbName);

        var storage = new TestInMemoryDocumentStorage();
        var initialWorkflow = new DakWorkflowService(db, storage);

        var cmd = new RegisterDakCommand(
            DiaryNumber: "DIARY_COMMIT_AMBIGUITY_DISPOSE",
            ReceivedDate: new DateOnly(2026, 9, 19),
            Subject: "Commit Ambiguity Dispose",
            SenderName: "Test Sender D",
            SenderDesignation: null,
            SenderDepartment: null,
            SenderAddress: null,
            SenderReferenceNumber: null,
            SenderLetterDate: null,
            InwardMode: "Physical",
            Priority: DakPriority.Routine,
            DueDate: null,
            CategoryId: null,
            WorkstreamId: null,
            DocumentStream: null,
            DocumentFileName: null,
            DocumentContentType: null
        );

        var dak = await initialWorkflow.RegisterAsync(cmd, adminUser.Id);

        // Dak must be InProcess before it can be Disposed
        var moveCmd = new MoveDakCommand(
            Action: DakMovementAction.Marked,
            ToDeskId: targetDesk.Id,
            ToUserId: targetUser.Id,
            Remarks: "Marking before disposal",
            Instructions: null,
            ExpectedRevision: 0
        );
        var moved = await initialWorkflow.MoveAsync(dak.Id, moveCmd, adminUser.Id);
        Assert.Equal(1, moved.Revision);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new DakWorkflowService(db, storage, () => strategy!);
        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2);

        var disposeCmd = new DisposeDakCommand("Disposal remarks", ExpectedRevision: 1);
        var disposed = await workflow.DisposeAsync(dak.Id, disposeCmd, adminUser.Id);

        Assert.NotNull(disposed);
        Assert.Equal(2, disposed.Revision);
        Assert.Equal(DakStatus.Disposed, disposed.Status);
        Assert.Equal(1, strategy.VerifyCount);

        var dbDak = await db.Daks.Include(d => d.CurrentAssignment).SingleAsync(d => d.Id == dak.Id);
        Assert.Equal(DakStatus.Disposed, dbDak.Status);
        Assert.Equal(2, dbDak.Revision);
        Assert.NotNull(dbDak.CurrentAssignment);
        Assert.False(dbDak.CurrentAssignment.IsActive);
    }

    [Fact]
    public async Task Group22_E_Cancel_commit_ambiguity_recognized_by_verify_succeeded()
    {
        var dbName = $"dak-retry-e-{Guid.NewGuid():N}";
        var (db, adminUser, _, _) = await CreateWorkflowTestContextAsync(dbName);

        var storage = new TestInMemoryDocumentStorage();
        var initialWorkflow = new DakWorkflowService(db, storage);

        var cmd = new RegisterDakCommand(
            DiaryNumber: "DIARY_COMMIT_AMBIGUITY_CANCEL",
            ReceivedDate: new DateOnly(2026, 9, 19),
            Subject: "Commit Ambiguity Cancel",
            SenderName: "Test Sender E",
            SenderDesignation: null,
            SenderDepartment: null,
            SenderAddress: null,
            SenderReferenceNumber: null,
            SenderLetterDate: null,
            InwardMode: "Physical",
            Priority: DakPriority.Routine,
            DueDate: null,
            CategoryId: null,
            WorkstreamId: null,
            DocumentStream: null,
            DocumentFileName: null,
            DocumentContentType: null
        );

        var dak = await initialWorkflow.RegisterAsync(cmd, adminUser.Id);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new DakWorkflowService(db, storage, () => strategy!);
        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2);

        var cancelCmd = new CancelDakCommand("Cancellation remarks", ExpectedRevision: 0);
        var cancelled = await workflow.CancelAsync(dak.Id, cancelCmd, adminUser.Id);

        Assert.NotNull(cancelled);
        Assert.Equal(1, cancelled.Revision);
        Assert.Equal(DakStatus.Cancelled, cancelled.Status);
        Assert.Equal(1, strategy.VerifyCount);

        var dbDak = await db.Daks.Include(d => d.CurrentAssignment).SingleAsync(d => d.Id == dak.Id);
        Assert.Equal(DakStatus.Cancelled, dbDak.Status);
        Assert.Equal(1, dbDak.Revision);
    }

    [Fact]
    public async Task Group22_F_Permanent_failure_compensates_and_deletes_physical_file()
    {
        var dbName = $"dak-retry-f-{Guid.NewGuid():N}";
        var interceptor = new FailingSaveChangesInterceptor { FailOnSave = true };
        var (db, adminUser, _, _) = await CreateWorkflowTestContextAsync(dbName, interceptor);

        var storage = new TestInMemoryDocumentStorage();
        var workflow = new DakWorkflowService(db, storage);

        var pdfBytes = "%PDF-1.4 file content"u8.ToArray();
        using var ms = new MemoryStream(pdfBytes);
        var cmd = new RegisterDakCommand(
            DiaryNumber: "DIARY_FAIL_COMPENSATE",
            ReceivedDate: new DateOnly(2026, 9, 19),
            Subject: "Permanent Fail Compensation",
            SenderName: "Test Sender F",
            SenderDesignation: null,
            SenderDepartment: null,
            SenderAddress: null,
            SenderReferenceNumber: null,
            SenderLetterDate: null,
            InwardMode: "Physical",
            Priority: DakPriority.Routine,
            DueDate: null,
            CategoryId: null,
            WorkstreamId: null,
            DocumentStream: ms,
            DocumentFileName: "fail_file.pdf",
            DocumentContentType: "application/pdf"
        );

        await Assert.ThrowsAsync<DbUpdateException>(() => workflow.RegisterAsync(cmd, adminUser.Id));

        Assert.Equal(1, storage.SaveCount);
        Assert.Equal(1, storage.DeleteCount);
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task Group22_G_True_rollback_and_retry_preserves_single_file_save()
    {
        var dbName = $"dak-retry-g-{Guid.NewGuid():N}";
        var interceptor = new TrackingSaveChangesInterceptor { FailTimes = 1 };
        var (db, adminUser, _, _) = await CreateWorkflowTestContextAsync(dbName, interceptor);

        var storage = new TestInMemoryDocumentStorage();
        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new DakWorkflowService(db, storage, () => strategy!);
        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: false, maxRetries: 2);

        var pdfBytes = "%PDF-1.4 file content"u8.ToArray();
        using var ms = new MemoryStream(pdfBytes);
        var cmd = new RegisterDakCommand(
            DiaryNumber: "DIARY_ROLLBACK_RETRY_FILE",
            ReceivedDate: new DateOnly(2026, 9, 19),
            Subject: "Rollback and Retry File",
            SenderName: "Test Sender G",
            SenderDesignation: null,
            SenderDepartment: null,
            SenderAddress: null,
            SenderReferenceNumber: null,
            SenderLetterDate: null,
            InwardMode: "Physical",
            Priority: DakPriority.Routine,
            DueDate: null,
            CategoryId: null,
            WorkstreamId: null,
            DocumentStream: ms,
            DocumentFileName: "retry_file.pdf",
            DocumentContentType: "application/pdf"
        );

        var dak = await workflow.RegisterAsync(cmd, adminUser.Id);

        Assert.NotNull(dak);
        Assert.Equal(2, strategy.AttemptCount);
        // Physical file must have been written ONCE outside retries and NOT deleted
        Assert.Equal(1, storage.SaveCount);
        Assert.Equal(0, storage.DeleteCount);
        Assert.Single(storage.Files);
    }

    [Fact]
    public async Task Group22_H_Registration_commit_ambiguity_succeeds_even_if_dak_state_advanced_before_verification()
    {
        var dbName = $"dak-retry-h-{Guid.NewGuid():N}";
        var (db, adminUser, _, _) = await CreateWorkflowTestContextAsync(dbName);

        var storage = new TestInMemoryDocumentStorage();
        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new DakWorkflowService(db, storage, () => strategy!);

        var pdfBytes = "%PDF-1.4 advanced state test"u8.ToArray();
        using var ms = new MemoryStream(pdfBytes);
        var cmd = new RegisterDakCommand(
            DiaryNumber: "DIARY_RACE_REGISTER",
            ReceivedDate: new DateOnly(2026, 9, 19),
            Subject: "Race Register Test",
            SenderName: "High Court",
            SenderDesignation: null,
            SenderDepartment: null,
            SenderAddress: null,
            SenderReferenceNumber: null,
            SenderLetterDate: null,
            InwardMode: "Physical",
            Priority: DakPriority.Immediate,
            DueDate: null,
            CategoryId: null,
            WorkstreamId: null,
            DocumentStream: ms,
            DocumentFileName: "scanned_petition.pdf",
            DocumentContentType: "application/pdf"
        );

        // Before verification evaluates, mutate the committed Dak to simulate another legitimate transaction advancing the aggregate
        strategy = new TestCommitAmbiguityExecutionStrategy(
            db,
            simulateCommitAmbiguity: true,
            maxRetries: 2,
            onBeforeVerify: async () =>
            {
                var builder = new DbContextOptionsBuilder<LacDbContext>()
                    .UseInMemoryDatabase(dbName)
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
                using var mutateDb = new LacDbContext(builder.Options);

                var commitedDak = await mutateDb.Daks.SingleAsync(d => d.DiaryNumber == "DIARY_RACE_REGISTER");
                // Advance status and revision
                commitedDak.Status = DakStatus.InProcess;
                commitedDak.Revision = 10;
                await mutateDb.SaveChangesAsync();
            });

        var dak = await workflow.RegisterAsync(cmd, adminUser.Id);

        Assert.NotNull(dak);
        Assert.Equal(1, strategy.AttemptCount);
        Assert.Equal(1, strategy.VerifyCount);

        // Assert registration was recognized as committed despite later status/revision advancement
        var daksInDb = await db.Daks.Where(d => d.DiaryNumber == "DIARY_RACE_REGISTER").ToListAsync();
        Assert.Single(daksInDb);

        // Assert physical file was NOT compensated/deleted
        Assert.Equal(1, storage.SaveCount);
        Assert.Equal(0, storage.DeleteCount);
        Assert.Single(storage.Files);
    }

    [Fact]
    public async Task Group22_I_Move_commit_ambiguity_succeeds_even_if_aggregate_advanced_before_verification()
    {
        var dbName = $"dak-retry-i-{Guid.NewGuid():N}";
        var (db, adminUser, targetDesk, targetUser) = await CreateWorkflowTestContextAsync(dbName);

        var storage = new TestInMemoryDocumentStorage();
        var initialWorkflow = new DakWorkflowService(db, storage);

        var cmd = new RegisterDakCommand(
            DiaryNumber: "DIARY_RACE_MOVE",
            ReceivedDate: new DateOnly(2026, 9, 19),
            Subject: "Race Move Test",
            SenderName: "District Collector",
            SenderDesignation: null,
            SenderDepartment: null,
            SenderAddress: null,
            SenderReferenceNumber: null,
            SenderLetterDate: null,
            InwardMode: "Physical",
            Priority: DakPriority.Routine,
            DueDate: null,
            CategoryId: null,
            WorkstreamId: null,
            DocumentStream: null,
            DocumentFileName: null,
            DocumentContentType: null
        );

        var dak = await initialWorkflow.RegisterAsync(cmd, adminUser.Id);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new DakWorkflowService(db, storage, () => strategy!);

        var moveCmd = new MoveDakCommand(
            Action: DakMovementAction.Marked,
            ToDeskId: targetDesk.Id,
            ToUserId: targetUser.Id,
            Remarks: "First Movement",
            Instructions: null,
            ExpectedRevision: 0
        );

        // Before verification evaluates, mutate the aggregate to simulate a later legitimate Forward/Dispose
        strategy = new TestCommitAmbiguityExecutionStrategy(
            db,
            simulateCommitAmbiguity: true,
            maxRetries: 2,
            onBeforeVerify: async () =>
            {
                var builder = new DbContextOptionsBuilder<LacDbContext>()
                    .UseInMemoryDatabase(dbName)
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
                using var mutateDb = new LacDbContext(builder.Options);

                var commitedDak = await mutateDb.Daks.Include(d => d.CurrentAssignment).SingleAsync(d => d.Id == dak.Id);
                // Later transaction advanced the aggregate
                commitedDak.Status = DakStatus.Disposed;
                commitedDak.Revision = 99;
                if (commitedDak.CurrentAssignment != null)
                {
                    commitedDak.CurrentAssignment.IsActive = false;
                }
                await mutateDb.SaveChangesAsync();
            });

        // Verification must still succeed because the immutable DakMovement committed
        var moved = await workflow.MoveAsync(dak.Id, moveCmd, adminUser.Id);

        Assert.NotNull(moved);
        Assert.Equal(1, strategy.AttemptCount);
        Assert.Equal(1, strategy.VerifyCount);

        // Verify the original movement exists immutably in the history
        var moveRecorded = await db.DakMovements.AnyAsync(m => m.DakId == dak.Id
                                                           && m.Action == DakMovementAction.Marked
                                                           && m.ToDeskId == targetDesk.Id
                                                           && m.ToUserId == targetUser.Id);
        Assert.True(moveRecorded);
    }

    private static async Task<(LacDbContext db, AppUser adminUser, OfficeDesk targetDesk, AppUser targetUser)> CreateWorkflowTestContextAsync(
        string dbName,
        params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        // 1. Seed initial users and desks using a separate un-intercepted DbContext
        var seedBuilder = new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));

        using (var seedDb = new LacDbContext(seedBuilder.Options))
        {
            var admin = new AppUser
            {
                Username = $"admin_{Guid.NewGuid():N}",
                DisplayName = "Admin G22",
                PasswordHash = "hash",
                IsActive = true,
                RecordStatus = RecordStatus.Active
            };
            var desk = new OfficeDesk
            {
                Code = $"DESK_{Guid.NewGuid():N}"[..10],
                Name = "Desk G22",
                IsActive = true,
                RecordStatus = RecordStatus.Active
            };
            var user = new AppUser
            {
                Username = $"target_{Guid.NewGuid():N}",
                DisplayName = "Target G22",
                PasswordHash = "hash",
                IsActive = true,
                RecordStatus = RecordStatus.Active
            };
            var membership = new UserDeskMembership
            {
                UserId = user.Id,
                User = user,
                OfficeDeskId = desk.Id,
                OfficeDesk = desk,
                IsActive = true,
                RecordStatus = RecordStatus.Active
            };

            seedDb.AppUsers.AddRange(admin, user);
            seedDb.OfficeDesks.Add(desk);
            seedDb.UserDeskMemberships.Add(membership);
            await seedDb.SaveChangesAsync();
        }

        // 2. Return test DbContext with interceptors attached
        var testBuilder = new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));

        foreach (var interceptor in interceptors)
        {
            testBuilder.AddInterceptors(interceptor);
        }

        var db = new LacDbContext(testBuilder.Options);
        var adminUser = await db.AppUsers.FirstAsync(u => u.DisplayName == "Admin G22");
        var targetDesk = await db.OfficeDesks.FirstAsync();
        var targetUser = await db.AppUsers.FirstAsync(u => u.DisplayName == "Target G22");

        return (db, adminUser, targetDesk, targetUser);
    }
}

public sealed class TestInMemoryDocumentStorage : IDocumentStorage
{
    public readonly Dictionary<string, byte[]> Files = new();
    public int SaveCount { get; private set; }
    public int DeleteCount { get; private set; }

    public Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct) => throw new NotImplementedException();

    public async Task<DocumentStorageWriteResult> SaveAndHashAsync(Stream content, string fileName, CancellationToken ct)
    {
        SaveCount++;
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var key = $"{Guid.NewGuid():N}_{fileName}";
        Files[key] = bytes;
        return new DocumentStorageWriteResult(key, "hash123", bytes.Length);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        DeleteCount++;
        Files.Remove(storagePath);
        return Task.CompletedTask;
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct)
    {
        if (Files.TryGetValue(storagePath, out var bytes))
            return Task.FromResult<Stream?>(new MemoryStream(bytes));
        return Task.FromResult<Stream?>(null);
    }

    public StorageHealth GetHealth() => new StorageHealth("TestStorage", true, 1000000, 1000000);
}

public sealed class FailingSaveChangesInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    public bool FailOnSave { get; set; }

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (FailOnSave)
            throw new DbUpdateException("Simulated failure inside execution strategy transaction");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

public sealed class TrackingSaveChangesInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    public int FailTimes { get; set; }
    public int SaveCalls { get; private set; }
    public readonly List<Guid> AttemptDakIds = new();
    public readonly List<Guid> AttemptMovementIds = new();

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        if (eventData.Context != null)
        {
            var dak = eventData.Context.ChangeTracker.Entries<Dak>().FirstOrDefault()?.Entity;
            if (dak != null) AttemptDakIds.Add(dak.Id);
            var mov = eventData.Context.ChangeTracker.Entries<DakMovement>().FirstOrDefault()?.Entity;
            if (mov != null) AttemptMovementIds.Add(mov.Id);
        }

        if (FailTimes > 0 && SaveCalls <= FailTimes)
        {
            throw new DbUpdateException("Simulated transient DB failure on SaveChangesAsync");
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

public sealed class TestCommitAmbiguityExecutionStrategy(
    LacDbContext db,
    bool simulateCommitAmbiguity = true,
    int maxRetries = 2,
    Func<Task>? onBeforeVerify = null) : Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy
{
    public bool RetriesOnFailure => true;
    public int AttemptCount { get; private set; }
    public int VerifyCount { get; private set; }

    public TResult Execute<TState, TResult>(
        TState state,
        Func<DbContext, TState, TResult> operation,
        Func<DbContext, TState, Microsoft.EntityFrameworkCore.Storage.ExecutionResult<TResult>>? verifySucceeded)
        => throw new NotImplementedException();

    public async Task<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
        Func<DbContext, TState, CancellationToken, Task<Microsoft.EntityFrameworkCore.Storage.ExecutionResult<TResult>>>? verifySucceeded,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            AttemptCount++;
            try
            {
                var result = await operation(db, state, cancellationToken);
                if (simulateCommitAmbiguity && AttemptCount == 1)
                {
                    throw new DbUpdateException("Simulated network timeout during transaction commit");
                }
                return result;
            }
            catch (DbUpdateException)
            {
                if (onBeforeVerify != null)
                {
                    await onBeforeVerify();
                }

                if (verifySucceeded != null)
                {
                    VerifyCount++;
                    var verification = await verifySucceeded(db, state, cancellationToken);
                    if (verification.IsSuccessful)
                    {
                        return verification.Result;
                    }
                }

                if (AttemptCount > maxRetries)
                {
                    throw;
                }
            }
        }
    }
}

