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
    public async Task Group06_Forwarding_deactivates_previous_assignment_and_activates_new_desk()
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
}
