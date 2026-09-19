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

public sealed class MyDeskTests : IClassFixture<DakTestFactory>
{
    private readonly DakTestFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public MyDeskTests(DakTestFactory factory)
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

    private async Task<(HttpClient Client, Guid UserId)> CreateScopedUserClientAsync(
        string username,
        string roleCode,
        ScopeMode scopeMode,
        Guid? deskId = null,
        Guid? workstreamId = null,
        bool isPrimary = true)
    {
        var adminClient = await CreateAdminClientAsync();

        var permCodes = new[]
        {
            PermissionCodes.DakView,
            PermissionCodes.DakRegister,
            PermissionCodes.DakEdit,
            PermissionCodes.DakMove,
            PermissionCodes.DakDispose,
            PermissionCodes.DakCancel
        };

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
            var assignRes = await adminClient.PostAsJsonAsync($"/api/admin/users/{userId}/desks", new AssignDeskRequest(deskId.Value, IsPrimary: isPrimary));
            Assert.Equal(HttpStatusCode.Created, assignRes.StatusCode);
        }

        var userClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginRes = await userClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, userPass));
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        return (userClient, userId);
    }

    private static MultipartFormDataContent CreateRegisterForm(
        string diaryNumber,
        string subject,
        string senderName,
        string? priority = "Routine",
        string? dueDate = null,
        string? workstreamId = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(diaryNumber), "diaryNumber");
        form.Add(new StringContent(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")), "receivedDate");
        form.Add(new StringContent(subject), "subject");
        form.Add(new StringContent(senderName), "senderName");
        form.Add(new StringContent("Physical / By Hand"), "inwardMode");
        form.Add(new StringContent(priority ?? "Routine"), "priority");

        if (!string.IsNullOrEmpty(dueDate))
            form.Add(new StringContent(dueDate), "dueDate");

        if (!string.IsNullOrEmpty(workstreamId))
            form.Add(new StringContent(workstreamId), "workstreamId");

        return form;
    }

    private async Task<Guid> CreateDeskAsync(HttpClient adminClient, string deskName)
    {
        var deskCode = $"DSK_{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var res = await adminClient.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest(deskCode, deskName, null, null));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private async Task<Guid> RegisterAndMarkDakAsync(
        HttpClient adminClient,
        Guid deskId,
        string subject,
        string priority = "Routine",
        string? dueDate = null,
        string? workstreamId = null)
    {
        var diaryNo = $"DAK_{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        using var form = CreateRegisterForm(diaryNo, subject, "Test Sender", priority, dueDate, workstreamId);
        var regRes = await adminClient.PostAsync("/api/dak", form);
        Assert.Equal(HttpStatusCode.Created, regRes.StatusCode);
        var dakId = (await JsonDocument.ParseAsync(await regRes.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        var moveReq = new MoveDakRequest(
            Action: "Marked",
            ToDeskId: deskId,
            ToUserId: null,
            Remarks: "Marked to desk for testing",
            Instructions: "Process promptly",
            ExpectedRevision: 0
        );
        var moveRes = await adminClient.PostAsJsonAsync($"/api/dak/{dakId}/move", moveReq);
        Assert.Equal(HttpStatusCode.OK, moveRes.StatusCode);

        return dakId;
    }

    // ========================================================================
    // TESTS 01 to 07: Live Custody, Multi-Desk, Handler Filters
    // ========================================================================
    [Fact]
    public async Task MyDesk_01_To_07_Custody_MultiDesk_Handler_Filters()
    {
        using var adminClient = await CreateAdminClientAsync();

        // 1. Create two Desks: Desk A & Desk B
        var deskA = await CreateDeskAsync(adminClient, "LA Desk A");
        var deskB = await CreateDeskAsync(adminClient, "LA Desk B");

        // 2. Create Officer 1 on Desk A (Primary) and Desk B (Secondary)
        var officer1Username = $"officer1_{Guid.NewGuid():N}"[..12];
        var roleCode = $"ROLE_ASSG_{Guid.NewGuid():N}"[..12];
        var (client1, officer1Id) = await CreateScopedUserClientAsync(officer1Username, roleCode, ScopeMode.Assigned, deskA, isPrimary: true);

        // Assign Desk B to Officer 1 as secondary desk
        var assignBRes = await adminClient.PostAsJsonAsync($"/api/admin/users/{officer1Id}/desks", new AssignDeskRequest(deskB, IsPrimary: false));
        Assert.Equal(HttpStatusCode.Created, assignBRes.StatusCode);

        // 3. Create Officer 2 on Desk A
        var officer2Username = $"officer2_{Guid.NewGuid():N}"[..12];
        var (client2, officer2Id) = await CreateScopedUserClientAsync(officer2Username, roleCode, ScopeMode.Assigned, deskA);

        // 4. Create Daks:
        // Dak 1 on Desk A, unallocated
        var dak1 = await RegisterAndMarkDakAsync(adminClient, deskA, "Dak 1 on Desk A Unallocated");

        // Dak 2 on Desk B, assigned to Officer 1
        var dak2 = await RegisterAndMarkDakAsync(adminClient, deskB, "Dak 2 on Desk B");
        var assignDak2Res = await adminClient.PostAsJsonAsync($"/api/dak/{dak2}/move", new MoveDakRequest("Forwarded", deskB, officer1Id, "Forwarded to Off 1", null, 1));
        Assert.Equal(HttpStatusCode.OK, assignDak2Res.StatusCode);

        // Dak 3 on Desk A, assigned to Officer 2
        var dak3 = await RegisterAndMarkDakAsync(adminClient, deskA, "Dak 3 on Desk A");
        var assignDak3Res = await adminClient.PostAsJsonAsync($"/api/dak/{dak3}/move", new MoveDakRequest("Forwarded", deskA, officer2Id, "Forwarded to Off 2", null, 1));
        Assert.Equal(HttpStatusCode.OK, assignDak3Res.StatusCode);

        // --------------------------------------------------------------------
        // Test 1: Assigned scope sees active Dak on live desk
        // Test 2: Multiple active desks aggregate by default (sees Dak 1, 2, 3)
        // Test 3: Secondary desk remains visible despite PrimaryDesk
        // Test 4: AssignedUserId naming another officer does not hide item (Dak 3 visible to Officer 1)
        // --------------------------------------------------------------------
        var resAll = await client1.GetAsync("/api/dak/my-desk");
        Assert.Equal(HttpStatusCode.OK, resAll.StatusCode);
        var dataAll = await resAll.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.NotNull(dataAll);
        Assert.Equal(3, dataAll.TotalCount);
        Assert.Equal(2, dataAll.Desks.Count);

        // Verify primary desk flag
        var deskADto = dataAll.Desks.First(d => d.Id == deskA);
        Assert.True(deskADto.IsPrimary);
        var deskBDto = dataAll.Desks.First(d => d.Id == deskB);
        Assert.False(deskBDto.IsPrimary);

        // Verify Dak 3 has handlerState = AssignedToOther for Officer 1
        var item3 = dataAll.Items.First(i => i.Id == dak3);
        Assert.Equal("AssignedToOther", item3.Assignment.HandlerState);
        Assert.Equal(officer2Id, item3.Assignment.AssignedUserId);

        // --------------------------------------------------------------------
        // Test 5: handler=me returns only items assigned to caller
        // --------------------------------------------------------------------
        var resMe = await client1.GetAsync("/api/dak/my-desk?handler=me");
        Assert.Equal(HttpStatusCode.OK, resMe.StatusCode);
        var dataMe = await resMe.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.NotNull(dataMe);
        Assert.Equal(1, dataMe.TotalCount);
        Assert.Equal(dak2, dataMe.Items.Single().Id);
        Assert.Equal("AssignedToMe", dataMe.Items.Single().Assignment.HandlerState);

        // --------------------------------------------------------------------
        // Test 6: handler=unallocated returns only unallocated items
        // --------------------------------------------------------------------
        var resUnalloc = await client1.GetAsync("/api/dak/my-desk?handler=unallocated");
        Assert.Equal(HttpStatusCode.OK, resUnalloc.StatusCode);
        var dataUnalloc = await resUnalloc.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.NotNull(dataUnalloc);
        Assert.Equal(1, dataUnalloc.TotalCount);
        Assert.Equal(dak1, dataUnalloc.Items.Single().Id);
        Assert.Equal("Unallocated", dataUnalloc.Items.Single().Assignment.HandlerState);

        // --------------------------------------------------------------------
        // Test 7: handler=others returns items assigned to other officers
        // --------------------------------------------------------------------
        var resOthers = await client1.GetAsync("/api/dak/my-desk?handler=others");
        Assert.Equal(HttpStatusCode.OK, resOthers.StatusCode);
        var dataOthers = await resOthers.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.NotNull(dataOthers);
        Assert.Equal(1, dataOthers.TotalCount);
        Assert.Equal(dak3, dataOthers.Items.Single().Id);
        Assert.Equal("AssignedToOther", dataOthers.Items.Single().Assignment.HandlerState);
    }

    // ========================================================================
    // TESTS 08 to 12: Live Custody Membership & Desk Status Invariants
    // ========================================================================
    [Fact]
    public async Task MyDesk_08_To_12_Live_Custody_Membership_And_Desk_Status_Invariants()
    {
        using var adminClient = await CreateAdminClientAsync();

        var desk = await CreateDeskAsync(adminClient, "Invariants Desk");
        var officerUsername = $"officer_inv_{Guid.NewGuid():N}"[..12];
        var roleCode = $"ROLE_INV_{Guid.NewGuid():N}"[..12];
        var (client, officerId) = await CreateScopedUserClientAsync(officerUsername, roleCode, ScopeMode.Assigned, desk);

        var dak = await RegisterAndMarkDakAsync(adminClient, desk, "Dak for Invariants Testing");

        // Verify initially visible
        var res1 = await client.GetAsync("/api/dak/my-desk");
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var data1 = await res1.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.Equal(1, data1!.TotalCount);

        // --------------------------------------------------------------------
        // Test 8: Removed membership immediately removes items without re-login
        // Test 12: Stale cookie desk claims cannot preserve access
        // --------------------------------------------------------------------
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var membership = await db.UserDeskMemberships.FirstAsync(m => m.UserId == officerId && m.OfficeDeskId == desk);
            membership.RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // Call again with SAME client (stale cookie has desk claim, but live DB has RemovedAt)
        var resRemoved = await client.GetAsync("/api/dak/my-desk");
        Assert.Equal(HttpStatusCode.OK, resRemoved.StatusCode);
        var dataRemoved = await resRemoved.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.Equal(0, dataRemoved!.TotalCount);
        Assert.Empty(dataRemoved.Items);
        Assert.Empty(dataRemoved.Desks);

        // Restore membership
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var membership = await db.UserDeskMemberships.FirstAsync(m => m.UserId == officerId && m.OfficeDeskId == desk);
            membership.RemovedAt = null;
            await db.SaveChangesAsync();
        }

        // Verify visible again
        var resRestored = await client.GetAsync("/api/dak/my-desk");
        Assert.Equal(1, (await resRestored.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts))!.TotalCount);

        // --------------------------------------------------------------------
        // Test 9: Inactive membership removes items
        // --------------------------------------------------------------------
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var membership = await db.UserDeskMemberships.FirstAsync(m => m.UserId == officerId && m.OfficeDeskId == desk);
            membership.IsActive = false;
            await db.SaveChangesAsync();
        }

        var resInactiveMem = await client.GetAsync("/api/dak/my-desk");
        Assert.Equal(0, (await resInactiveMem.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts))!.TotalCount);

        // Restore IsActive
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var membership = await db.UserDeskMemberships.FirstAsync(m => m.UserId == officerId && m.OfficeDeskId == desk);
            membership.IsActive = true;
            await db.SaveChangesAsync();
        }

        // --------------------------------------------------------------------
        // Test 10: Inactive OfficeDesk removes items
        // --------------------------------------------------------------------
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var officeDesk = await db.OfficeDesks.FirstAsync(d => d.Id == desk);
            officeDesk.IsActive = false;
            await db.SaveChangesAsync();
        }

        var resInactiveDesk = await client.GetAsync("/api/dak/my-desk");
        Assert.Equal(0, (await resInactiveDesk.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts))!.TotalCount);

        // Restore OfficeDesk
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var officeDesk = await db.OfficeDesks.FirstAsync(d => d.Id == desk);
            officeDesk.IsActive = true;
            await db.SaveChangesAsync();
        }

        // --------------------------------------------------------------------
        // Test 11: Archived UserDeskMembership cannot grant custody
        // --------------------------------------------------------------------
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var membership = await db.UserDeskMemberships.FirstAsync(m => m.UserId == officerId && m.OfficeDeskId == desk);
            membership.RecordStatus = RecordStatus.Archived;
            await db.SaveChangesAsync();
        }

        var resArchivedMem = await client.GetAsync("/api/dak/my-desk");
        Assert.Equal(0, (await resArchivedMem.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts))!.TotalCount);
    }

    // ========================================================================
    // TESTS 13 to 18: Scope Authorization Intersection
    // ========================================================================
    [Fact]
    public async Task MyDesk_13_To_18_Scope_Authorization_Intersection()
    {
        using var adminClient = await CreateAdminClientAsync();

        var deskA = await CreateDeskAsync(adminClient, "Scope Desk A");
        var deskB = await CreateDeskAsync(adminClient, "Scope Desk B");

        // --------------------------------------------------------------------
        // Test 13: No Dak.View -> 403 Forbidden
        // --------------------------------------------------------------------
        var noPermUser = $"noperm_{Guid.NewGuid():N}"[..12];
        var emptyRole = $"ROLE_EMPTY_{Guid.NewGuid():N}"[..12];
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var role = new Role { Code = emptyRole, Name = emptyRole, Description = "No perms", IsSystemRole = false };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }
        var (clientNoPerm, _) = await CreateScopedUserClientAsync(noPermUser, emptyRole, ScopeMode.All, deskA);
        var resNoPerm = await clientNoPerm.GetAsync("/api/dak/my-desk");
        Assert.Equal(HttpStatusCode.Forbidden, resNoPerm.StatusCode);

        // --------------------------------------------------------------------
        // Test 14: Dak.View + no active desks -> 200 empty
        // --------------------------------------------------------------------
        var noDeskUser = $"nodesk_{Guid.NewGuid():N}"[..12];
        var roleAll = $"ROLE_ALL_{Guid.NewGuid():N}"[..12];
        var (clientNoDesk, _) = await CreateScopedUserClientAsync(noDeskUser, roleAll, ScopeMode.All, deskId: null);
        var resNoDesk = await clientNoDesk.GetAsync("/api/dak/my-desk");
        Assert.Equal(HttpStatusCode.OK, resNoDesk.StatusCode);
        var dataNoDesk = await resNoDesk.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.Equal(0, dataNoDesk!.TotalCount);
        Assert.Empty(dataNoDesk.Desks);
        Assert.Empty(dataNoDesk.Items);

        // --------------------------------------------------------------------
        // Test 15: All scope sees only own Desk custody, never org-wide Dak
        // --------------------------------------------------------------------
        var allUser = $"alluser_{Guid.NewGuid():N}"[..12];
        var (clientAll, _) = await CreateScopedUserClientAsync(allUser, roleAll, ScopeMode.All, deskA);

        var dakA = await RegisterAndMarkDakAsync(adminClient, deskA, "Dak on Desk A for All scope test");
        var dakB = await RegisterAndMarkDakAsync(adminClient, deskB, "Dak on Desk B for All scope test");

        var resAllScope = await clientAll.GetAsync("/api/dak/my-desk");
        Assert.Equal(HttpStatusCode.OK, resAllScope.StatusCode);
        var dataAllScope = await resAllScope.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.NotNull(dataAllScope);
        Assert.Equal(1, dataAllScope.TotalCount);
        Assert.Equal(dakA, dataAllScope.Items.Single().Id);

        // --------------------------------------------------------------------
        // Test 16 & 17: Workstream scope requires Desk custody AND matching functional Workstream
        // --------------------------------------------------------------------
        Guid wsLandAcq;
        Guid wsLandRecords;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            wsLandAcq = (await db.Workstreams.FirstAsync(w => w.Code == WorkstreamCodes.LandAcquisition)).Id;
            wsLandRecords = (await db.Workstreams.FirstAsync(w => w.Code == WorkstreamCodes.LandRecords)).Id;
        }

        var wsRoleCode = $"ROLE_WS_{Guid.NewGuid():N}"[..12];
        var wsUser = $"wsuser_{Guid.NewGuid():N}"[..12];
        var (clientWs, _) = await CreateScopedUserClientAsync(wsUser, wsRoleCode, ScopeMode.Workstream, deskA, workstreamId: wsLandAcq);

        // Dak on Desk A with matching Workstream (Land Acquisition)
        var dakWsMatch = await RegisterAndMarkDakAsync(adminClient, deskA, "Dak WS Match", workstreamId: wsLandAcq.ToString());

        // Dak on Desk A with MISMATCHED Workstream (Land Records)
        var dakWsMismatch = await RegisterAndMarkDakAsync(adminClient, deskA, "Dak WS Mismatch", workstreamId: wsLandRecords.ToString());

        var resWs = await clientWs.GetAsync("/api/dak/my-desk");
        Assert.Equal(HttpStatusCode.OK, resWs.StatusCode);
        var dataWs = await resWs.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.NotNull(dataWs);
        // DakWsMatch is visible; DakWsMismatch is hidden despite being on Desk A
        Assert.Contains(dataWs.Items, i => i.Id == dakWsMatch);
        Assert.DoesNotContain(dataWs.Items, i => i.Id == dakWsMismatch);

        // --------------------------------------------------------------------
        // Test 18: Workstream + Assigned grant union occurs INSIDE custody
        // --------------------------------------------------------------------
        // User with Role 1 (Workstream LA) and Role 2 (Assigned on Desk A)
        // should see dakWsMismatch on Desk A via Assigned grant!
        // But should NOT see dakB (on Desk B with Workstream LA) because Desk B is not in custody!
        var multiGrantUser = $"multig_{Guid.NewGuid():N}"[..12];
        var assgRoleCode = $"ROLE_ASSG_MULTI_{Guid.NewGuid():N}"[..12];
        var (clientMulti, multiUserId) = await CreateScopedUserClientAsync(multiGrantUser, assgRoleCode, ScopeMode.Assigned, deskA, workstreamId: wsLandAcq);

        // Add Workstream role to multiGrantUser
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var wsRole = await db.Roles.FirstAsync(r => r.Code == wsRoleCode);
            db.UserRoles.Add(new UserRole { UserId = multiUserId, RoleId = wsRole.Id });
            await db.SaveChangesAsync();
        }

        // Create Dak on Desk B with matching Workstream
        var dakBWithWs = await RegisterAndMarkDakAsync(adminClient, deskB, "Dak B with WS Match", workstreamId: wsLandAcq.ToString());

        var resMulti = await clientMulti.GetAsync("/api/dak/my-desk");
        Assert.Equal(HttpStatusCode.OK, resMulti.StatusCode);
        var dataMulti = await resMulti.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.NotNull(dataMulti);

        // dakWsMismatch is visible via Assigned grant on Desk A
        Assert.Contains(dataMulti.Items, i => i.Id == dakWsMismatch);
        // dakBWithWs is NOT visible because it is on Desk B (custody boundary holds)
        Assert.DoesNotContain(dataMulti.Items, i => i.Id == dakBWithWs);
    }

    // ========================================================================
    // TESTS 19 to 24: Lifecycle, Terminal, and Inactive Dak Exclusion
    // ========================================================================
    [Fact]
    public async Task MyDesk_19_To_24_Lifecycle_Terminal_And_Inactive_Dak_Exclusion()
    {
        using var adminClient = await CreateAdminClientAsync();

        var desk = await CreateDeskAsync(adminClient, "Lifecycle Desk");
        var officerUsername = $"officer_life_{Guid.NewGuid():N}"[..12];
        var roleCode = $"ROLE_LIFE_{Guid.NewGuid():N}"[..12];
        var (client, _) = await CreateScopedUserClientAsync(officerUsername, roleCode, ScopeMode.Assigned, desk);

        // --------------------------------------------------------------------
        // Test 19: Registered unassigned intake excluded (CurrentAssignment == null)
        // --------------------------------------------------------------------
        var diaryNo1 = $"DAK_REG_{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        using var form1 = CreateRegisterForm(diaryNo1, "Unassigned Intake", "Citizen");
        var regRes1 = await adminClient.PostAsync("/api/dak", form1);
        Assert.Equal(HttpStatusCode.Created, regRes1.StatusCode);
        var regDakId = (await JsonDocument.ParseAsync(await regRes1.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // --------------------------------------------------------------------
        // Test 20: Classified-but-unassigned intake excluded
        // --------------------------------------------------------------------
        Guid wsId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            wsId = (await db.Workstreams.FirstAsync(w => w.Code == WorkstreamCodes.LandAcquisition)).Id;
        }
        var diaryNo2 = $"DAK_CLASS_{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        using var form2 = CreateRegisterForm(diaryNo2, "Classified Unassigned Intake", "Citizen", workstreamId: wsId.ToString());
        var regRes2 = await adminClient.PostAsync("/api/dak", form2);
        var classDakId = (await JsonDocument.ParseAsync(await regRes2.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetGuid();

        // --------------------------------------------------------------------
        // Test 21: Disposed with non-null inactive CurrentAssignment excluded
        // --------------------------------------------------------------------
        var dakDispose = await RegisterAndMarkDakAsync(adminClient, desk, "Dak To Dispose");
        var dispRes = await adminClient.PostAsJsonAsync($"/api/dak/{dakDispose}/dispose", new DisposeDakRequest("Disposed per officer order", ExpectedRevision: 1));
        Assert.Equal(HttpStatusCode.OK, dispRes.StatusCode);

        // Verify in DB that CurrentAssignment IS NOT null, but IsActive is false
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var d = await db.Daks.Include(x => x.CurrentAssignment).FirstAsync(x => x.Id == dakDispose);
            Assert.Equal(DakStatus.Disposed, d.Status);
            Assert.NotNull(d.CurrentAssignment);
            Assert.False(d.CurrentAssignment.IsActive);
            Assert.NotNull(d.CurrentAssignment.ClosedAt);
        }

        // --------------------------------------------------------------------
        // Test 22: Cancelled with non-null inactive CurrentAssignment excluded
        // --------------------------------------------------------------------
        var dakCancel = await RegisterAndMarkDakAsync(adminClient, desk, "Dak To Cancel");
        var cancelRes = await adminClient.PostAsJsonAsync($"/api/dak/{dakCancel}/cancel", new CancelDakRequest("Duplicate registration cancelled", ExpectedRevision: 1));
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var d = await db.Daks.Include(x => x.CurrentAssignment).FirstAsync(x => x.Id == dakCancel);
            Assert.Equal(DakStatus.Cancelled, d.Status);
            Assert.NotNull(d.CurrentAssignment);
            Assert.False(d.CurrentAssignment.IsActive);
            Assert.NotNull(d.CurrentAssignment.ClosedAt);
        }

        // --------------------------------------------------------------------
        // Test 23: Archived / Inactive Dak excluded
        // --------------------------------------------------------------------
        var dakArchived = await RegisterAndMarkDakAsync(adminClient, desk, "Dak To Archive");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var d = await db.Daks.FirstAsync(x => x.Id == dakArchived);
            d.RecordStatus = RecordStatus.Archived;
            await db.SaveChangesAsync();
        }

        // --------------------------------------------------------------------
        // Test 24: Archived CurrentAssignment excluded
        // --------------------------------------------------------------------
        var dakArchivedAssg = await RegisterAndMarkDakAsync(adminClient, desk, "Dak with Archived Assignment");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var d = await db.Daks.Include(x => x.CurrentAssignment).FirstAsync(x => x.Id == dakArchivedAssg);
            d.CurrentAssignment!.RecordStatus = RecordStatus.Archived;
            await db.SaveChangesAsync();
        }

        // Active Dak for positive control
        var dakActive = await RegisterAndMarkDakAsync(adminClient, desk, "Dak Active Control");

        // Query My Desk
        var res = await client.GetAsync("/api/dak/my-desk");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = await res.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.NotNull(data);

        // Only dakActive should be returned
        Assert.Equal(1, data.TotalCount);
        Assert.Equal(dakActive, data.Items.Single().Id);

        // None of the terminal, unassigned, or archived records should appear
        Assert.DoesNotContain(data.Items, i => i.Id == regDakId);
        Assert.DoesNotContain(data.Items, i => i.Id == classDakId);
        Assert.DoesNotContain(data.Items, i => i.Id == dakDispose);
        Assert.DoesNotContain(data.Items, i => i.Id == dakCancel);
        Assert.DoesNotContain(data.Items, i => i.Id == dakArchived);
        Assert.DoesNotContain(data.Items, i => i.Id == dakArchivedAssg);
    }

    // ========================================================================
    // TESTS 25 to 30: Filters, Pagination, Summary, Due Date Clock, & Security
    // ========================================================================
    [Fact]
    public async Task MyDesk_25_To_30_Filters_Pagination_Summary_DueClock_And_Security()
    {
        using var adminClient = await CreateAdminClientAsync();

        var deskA = await CreateDeskAsync(adminClient, "Desk A Query");
        var deskB = await CreateDeskAsync(adminClient, "Desk B Query");

        var officerUsername = $"officer_q_{Guid.NewGuid():N}"[..12];
        var roleCode = $"ROLE_Q_{Guid.NewGuid():N}"[..12];
        var (client, _) = await CreateScopedUserClientAsync(officerUsername, roleCode, ScopeMode.Assigned, deskA);

        // --------------------------------------------------------------------
        // Test 25: Invalid deskId filter -> 400 Bad Request
        // --------------------------------------------------------------------
        var resInvalidDesk = await client.GetAsync($"/api/dak/my-desk?deskId={deskB}");
        Assert.Equal(HttpStatusCode.BadRequest, resInvalidDesk.StatusCode);

        // Invalid query enum values -> 400
        var resBadPriority = await client.GetAsync("/api/dak/my-desk?priority=superurgent");
        Assert.Equal(HttpStatusCode.BadRequest, resBadPriority.StatusCode);
        var resBadDue = await client.GetAsync("/api/dak/my-desk?due=yesterday");
        Assert.Equal(HttpStatusCode.BadRequest, resBadDue.StatusCode);
        var resBadHandler = await client.GetAsync("/api/dak/my-desk?handler=supervisor");
        Assert.Equal(HttpStatusCode.BadRequest, resBadHandler.StatusCode);

        // --------------------------------------------------------------------
        // Setup 5 Daks on Desk A with diverse priorities and due dates
        // --------------------------------------------------------------------
        using var scope = _factory.Services.CreateScope();
        var officeClock = scope.ServiceProvider.GetRequiredService<IOfficeClock>();
        var delhiToday = officeClock.GetCurrentDate();

        var pastDate = delhiToday.AddDays(-3).ToString("yyyy-MM-dd");
        var todayDate = delhiToday.ToString("yyyy-MM-dd");
        var futureDate = delhiToday.AddDays(5).ToString("yyyy-MM-dd");

        // Dak 1: Routine, Overdue
        var dak1 = await RegisterAndMarkDakAsync(adminClient, deskA, "Alpha Search One", "Routine", pastDate);
        // Dak 2: Urgent, Due Today
        var dak2 = await RegisterAndMarkDakAsync(adminClient, deskA, "Alpha Search Two", "Urgent", todayDate);
        // Dak 3: Immediate, Upcoming
        var dak3 = await RegisterAndMarkDakAsync(adminClient, deskA, "Beta Search Three", "Immediate", futureDate);
        // Dak 4: Routine, No Due Date
        var dak4 = await RegisterAndMarkDakAsync(adminClient, deskA, "Beta Search Four", "Routine", null);
        // Dak 5: Urgent, No Due Date
        var dak5 = await RegisterAndMarkDakAsync(adminClient, deskA, "Gamma Search Five", "Urgent", null);

        // Dak on Desk B (should never be seen)
        var dakOnB = await RegisterAndMarkDakAsync(adminClient, deskB, "Alpha Search on Desk B", "Immediate", todayDate);

        // --------------------------------------------------------------------
        // Test 26: Search and filters do not widen access
        // --------------------------------------------------------------------
        var resSearch = await client.GetAsync("/api/dak/my-desk?q=Alpha");
        Assert.Equal(HttpStatusCode.OK, resSearch.StatusCode);
        var dataSearch = await resSearch.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.NotNull(dataSearch);
        Assert.Equal(2, dataSearch.TotalCount); // Only Dak 1 and Dak 2 from Desk A
        Assert.DoesNotContain(dataSearch.Items, i => i.Id == dakOnB);

        // --------------------------------------------------------------------
        // Test 27 & 28: Pagination occurs after filtering, and Summary is computed over full filtered set
        // --------------------------------------------------------------------
        var resPage = await client.GetAsync("/api/dak/my-desk?pageSize=2&page=0");
        Assert.Equal(HttpStatusCode.OK, resPage.StatusCode);
        var dataPage = await resPage.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.NotNull(dataPage);

        // Sliced items count == 2, but totalCount == 5 and summary.total == 5
        Assert.Equal(2, dataPage.Items.Count);
        Assert.Equal(5, dataPage.TotalCount);
        Assert.Equal(5, dataPage.Summary.Total);
        Assert.Equal(1, dataPage.Summary.Immediate);
        Assert.Equal(2, dataPage.Summary.Urgent);
        Assert.Equal(1, dataPage.Summary.Overdue);
        Assert.Equal(1, dataPage.Summary.DueToday);

        // --------------------------------------------------------------------
        // Test 29: Office-date due logic is deterministic
        // --------------------------------------------------------------------
        var resOverdue = await client.GetAsync("/api/dak/my-desk?due=overdue");
        var dataOverdue = await resOverdue.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.Equal(1, dataOverdue!.TotalCount);
        Assert.Equal(dak1, dataOverdue.Items.Single().Id);

        var resToday = await client.GetAsync("/api/dak/my-desk?due=today");
        var dataToday = await resToday.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.Equal(1, dataToday!.TotalCount);
        Assert.Equal(dak2, dataToday.Items.Single().Id);

        var resUpcoming = await client.GetAsync("/api/dak/my-desk?due=upcoming");
        var dataUpcoming = await resUpcoming.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.Equal(1, dataUpcoming!.TotalCount);
        Assert.Equal(dak3, dataUpcoming.Items.Single().Id);

        var resNone = await client.GetAsync("/api/dak/my-desk?due=none");
        var dataNone = await resNone.Content.ReadFromJsonAsync<MyDeskResponseDto>(JsonOpts);
        Assert.Equal(2, dataNone!.TotalCount); // Dak 4 and Dak 5

        // --------------------------------------------------------------------
        // Test 30: No My Desk mutation API / persisted entity introduced
        // --------------------------------------------------------------------
        // POST to /api/dak/my-desk does not exist (returns 404 or 405)
        var resPost = await client.PostAsync("/api/dak/my-desk", null);
        Assert.True(resPost.StatusCode == HttpStatusCode.MethodNotAllowed || resPost.StatusCode == HttpStatusCode.NotFound);

        // Verify EF Core model has no MyDesk or MyDeskAssignment entities
        using (var verificationScope = _factory.Services.CreateScope())
        {
            var db = verificationScope.ServiceProvider.GetRequiredService<LacDbContext>();
            var modelEntityTypes = db.Model.GetEntityTypes().Select(e => e.ClrType.Name).ToList();
            Assert.DoesNotContain("MyDesk", modelEntityTypes);
            Assert.DoesNotContain("MyDeskAssignment", modelEntityTypes);
            Assert.DoesNotContain("QueueItem", modelEntityTypes);
            Assert.DoesNotContain("InboxItem", modelEntityTypes);
        }
    }
}
