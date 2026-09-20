namespace LAC.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

public sealed class WorkItemTestFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"workitem-tests-{Guid.NewGuid()}";
    public const string TestAdminUser = "workitem_admin";
    public const string TestAdminPass = "WorkItemAdminPass!123";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = TestAdminUser,
                ["BootstrapAdmin:Password"] = TestAdminPass,
                ["BootstrapAdmin:DisplayName"] = "WorkItem Test Administrator"
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

public sealed class WorkItemTests : IClassFixture<WorkItemTestFactory>
{
    private readonly WorkItemTestFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly byte[] SamplePdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 sample content for work item testing");

    public WorkItemTests(WorkItemTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(WorkItemTestFactory.TestAdminUser, WorkItemTestFactory.TestAdminPass));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return client;
    }

    private async Task<OfficeDesk> CreateDeskAsync(string code, string name, Guid? workstreamId = null, bool isActive = true, bool assignAdmin = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var desk = new OfficeDesk
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            WorkstreamId = workstreamId,
            IsActive = isActive,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.OfficeDesks.Add(desk);

        if (assignAdmin)
        {
            db.UserDeskMemberships.Add(new UserDeskMembership
            {
                Id = Guid.NewGuid(),
                UserId = SeedData.BootstrapAdminId,
                OfficeDeskId = desk.Id,
                IsPrimary = false,
                IsActive = true,
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return desk;
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

    private async Task<(HttpClient Client, Guid UserId)> CreateScopedUserClientAsync(
        string username,
        string roleCode,
        ScopeMode scopeMode,
        Guid? deskId = null,
        Guid? workstreamId = null,
        bool isDeskActive = true,
        string[]? extraPermissions = null)
    {
        var adminClient = await CreateAdminClientAsync();

        var permCodes = new List<string>
        {
            PermissionCodes.WorkItemView,
            PermissionCodes.WorkItemCreate,
            PermissionCodes.WorkItemAssign,
            PermissionCodes.WorkItemUpdate,
            PermissionCodes.WorkItemContribute,
            PermissionCodes.WorkItemReview,
            PermissionCodes.WorkItemComplete,
            PermissionCodes.WorkItemCancel
        };

        if (extraPermissions != null)
        {
            permCodes.AddRange(extraPermissions);
        }

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
                    Description = "Scoped Work Item Test Role",
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

        var userPass = "WorkItemScopedPass!123";
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

            if (!isDeskActive)
            {
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
                var desk = await db.OfficeDesks.FindAsync(deskId.Value);
                if (desk != null)
                {
                    desk.IsActive = false;
                    await db.SaveChangesAsync();
                }
            }
        }

        var userClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginRes = await userClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, userPass));
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        return (userClient, userId);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateCustomUserClientAsync(
        string username,
        string roleCode,
        ScopeMode scopeMode,
        string[] permissionCodes,
        Guid? deskId = null,
        Guid? workstreamId = null)
    {
        var adminClient = await CreateAdminClientAsync();

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
                    Description = "Custom Work Item Test Role",
                    IsSystemRole = false
                };
                db.Roles.Add(role);
                await db.SaveChangesAsync();

                var perms = await db.Permissions.Where(p => permissionCodes.Contains(p.Code)).ToListAsync();
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

        var userPass = "WorkItemCustomPass!123";
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
    public async Task WorkItem_Create_And_ReadDetails_Succeeds_WithTimelineAndAssignment()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-CR1", "Workstream Create 1");
        var desk = await CreateDeskAsync("DSK-CR1", "Desk Create 1", ws.Id);

        var req = new CreateWorkItemApiRequest(
            Title: "Prepare notification under Section 19",
            Instructions: "Review compensation schedule and draft notification",
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Urgent",
            DueAt: DateTimeOffset.UtcNow.AddDays(2),
            MatterId: null,
            DakId: null
        );

        var res = await adminClient.PostAsJsonAsync("/api/work-items", req);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var created = await res.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts);
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.WorkItemId);

        // Fetch details
        var getRes = await adminClient.GetAsync($"/api/work-items/{created.WorkItemId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var detail = await getRes.Content.ReadFromJsonAsync<WorkItemDetailDto>(JsonOpts);
        Assert.NotNull(detail);
        Assert.Equal("Prepare notification under Section 19", detail.Title);
        Assert.Equal("Urgent", detail.Priority);
        Assert.Equal("Assigned", detail.Status);
        Assert.NotNull(detail.CurrentAssignment);
        Assert.Equal(desk.Id, detail.CurrentAssignment.OfficeDeskId);
        Assert.Equal(SeedData.BootstrapAdminId, detail.CurrentAssignment.AssignedUserId);

        // Fetch timeline
        var timelineRes = await adminClient.GetAsync($"/api/work-items/{created.WorkItemId}/timeline");
        Assert.Equal(HttpStatusCode.OK, timelineRes.StatusCode);
        var timeline = await timelineRes.Content.ReadFromJsonAsync<List<WorkItemEventDto>>(JsonOpts);
        Assert.NotNull(timeline);
        Assert.True(timeline.Count >= 2); // Created and Assigned events
        Assert.Contains(timeline, e => e.Action == "Created");
        Assert.Contains(timeline, e => e.Action == "Assigned");
    }

    [Fact]
    public async Task WorkItem_MarkSeen_IsIdempotent()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-SEEN", "Workstream Seen");
        var desk = await CreateDeskAsync("DSK-SEEN", "Desk Seen", ws.Id);

        var createRes = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Verify revenue map markings",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var id = (await createRes.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // Mark Seen 1
        var seenRes1 = await adminClient.PostAsync($"/api/work-items/{id}/seen", null);
        Assert.Equal(HttpStatusCode.OK, seenRes1.StatusCode);

        // Fetch detail to check FirstSeenAt
        var detail1 = await (await adminClient.GetAsync($"/api/work-items/{id}")).Content.ReadFromJsonAsync<WorkItemDetailDto>(JsonOpts);
        Assert.NotNull(detail1!.CurrentAssignment?.FirstSeenAt);
        var firstSeenAt = detail1.CurrentAssignment.FirstSeenAt.Value;

        // Mark Seen 2 (idempotent, should not change timestamp)
        var seenRes2 = await adminClient.PostAsync($"/api/work-items/{id}/seen", null);
        Assert.Equal(HttpStatusCode.OK, seenRes2.StatusCode);

        var detail2 = await (await adminClient.GetAsync($"/api/work-items/{id}")).Content.ReadFromJsonAsync<WorkItemDetailDto>(JsonOpts);
        Assert.Equal(firstSeenAt, detail2!.CurrentAssignment?.FirstSeenAt);
    }

    [Fact]
    public async Task WorkItem_Start_TransitionsStatus_And_EmitsStartedEvent()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-START", "Workstream Start");
        var desk = await CreateDeskAsync("DSK-START", "Desk Start", ws.Id);

        var createRes = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Survey site verification",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var id = (await createRes.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // Start work
        var startRes = await adminClient.PostAsJsonAsync($"/api/work-items/{id}/start", new StartWorkApiRequest(0));
        Assert.Equal(HttpStatusCode.OK, startRes.StatusCode);

        var detail = await (await adminClient.GetAsync($"/api/work-items/{id}")).Content.ReadFromJsonAsync<WorkItemDetailDto>(JsonOpts);
        Assert.Equal("InProgress", detail!.Status);
        Assert.Equal(1, detail.Revision);
        Assert.NotNull(detail.CurrentAssignment?.FirstActionAt);

        // Timeline check
        var timeline = await (await adminClient.GetAsync($"/api/work-items/{id}/timeline")).Content.ReadFromJsonAsync<List<WorkItemEventDto>>(JsonOpts);
        Assert.Contains(timeline!, e => e.Action == "Started");
    }

    [Fact]
    public async Task WorkItem_AddUpdate_AppendsNote_And_Rejects_InvalidRevision_With409()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-UPD", "Workstream Update");
        var desk = await CreateDeskAsync("DSK-UPD", "Desk Update", ws.Id);

        var createRes = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Draft valuation summary",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var id = (await createRes.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // Conflict check: pass wrong revision 99
        var conflictRes = await adminClient.PostAsJsonAsync($"/api/work-items/{id}/updates", new AddUpdateApiRequest(
            Message: "Wrong revision note",
            ExpectedRevision: 99
        ));
        Assert.Equal(HttpStatusCode.Conflict, conflictRes.StatusCode);

        // Valid update with ExpectedRevision = 0
        var okRes = await adminClient.PostAsJsonAsync($"/api/work-items/{id}/updates", new AddUpdateApiRequest(
            Message: "Field survey completed today, compiling measurement table.",
            ExpectedRevision: 0
        ));
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);

        var detail = await (await adminClient.GetAsync($"/api/work-items/{id}")).Content.ReadFromJsonAsync<WorkItemDetailDto>(JsonOpts);
        Assert.Equal(1, detail!.Revision);
        Assert.Single(detail.Updates);
        Assert.Equal("Field survey completed today, compiling measurement table.", detail.Updates[0].Message);
    }

    [Fact]
    public async Task WorkItem_Attachment_Upload_ValidatesMime_And_StreamsContent()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-ATT", "Workstream Attach");
        var desk = await CreateDeskAsync("DSK-ATT", "Desk Attach", ws.Id);

        var createRes = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Title deed verification",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var id = (await createRes.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // Valid PDF upload
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SamplePdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "verified_title_deed.pdf");
        content.Add(new StringContent("0"), "expectedRevision");

        var uploadRes = await adminClient.PostAsync($"/api/work-items/{id}/attachments", content);
        Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);

        var uploadJson = await uploadRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var attachmentId = uploadJson.GetProperty("attachmentId").GetGuid();

        // Check details has attachment
        var detailWithAtt = await (await adminClient.GetAsync($"/api/work-items/{id}")).Content.ReadFromJsonAsync<WorkItemDetailDto>(JsonOpts);
        Assert.Single(detailWithAtt!.Attachments);
        Assert.Equal("verified_title_deed.pdf", detailWithAtt.Attachments[0].FileName);
        Assert.Equal("application/pdf", detailWithAtt.Attachments[0].ContentType);

        // Download stream content
        var downloadRes = await adminClient.GetAsync($"/api/work-items/{id}/attachments/{attachmentId}/content");
        Assert.Equal(HttpStatusCode.OK, downloadRes.StatusCode);
        var downloadedBytes = await downloadRes.Content.ReadAsByteArrayAsync();
        Assert.Equal(SamplePdfBytes, downloadedBytes);

        // Delete attachment
        var delRes = await adminClient.DeleteAsync($"/api/work-items/{id}/attachments/{attachmentId}?expectedRevision={detailWithAtt.Revision}");
        var delBody = await delRes.Content.ReadAsStringAsync();
        Assert.True(delRes.StatusCode == HttpStatusCode.OK, $"Delete returned {delRes.StatusCode}: {delBody}");

        // Ensure attachment marked inactive
        var detail = await (await adminClient.GetAsync($"/api/work-items/{id}")).Content.ReadFromJsonAsync<WorkItemDetailDto>(JsonOpts);
        Assert.Empty(detail!.Attachments);
    }

    [Fact]
    public async Task WorkItem_Attachment_Upload_Rejects_ForgedContent()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-ATT2", "Workstream Attach 2");
        var desk = await CreateDeskAsync("DSK-ATT2", "Desk Attach 2", ws.Id);

        var createRes = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Fraud detection check",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var id = (await createRes.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // Plain text masquerading as PDF
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("Just plain text masquerading as pdf"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "fake.pdf");
        content.Add(new StringContent("0"), "expectedRevision");

        var uploadRes = await adminClient.PostAsync($"/api/work-items/{id}/attachments", content);
        Assert.Equal(HttpStatusCode.BadRequest, uploadRes.StatusCode);
    }

    [Fact]
    public async Task WorkItem_Event_And_Update_Immutability_Guarded_By_EFCore()
    {
        var ws = await CreateWorkstreamAsync("WS-IMM", "Workstream Immutability");
        var desk = await CreateDeskAsync("DSK-IMM", "Desk Immutability", ws.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var wi = new WorkItem
        {
            Id = Guid.NewGuid(),
            WorkstreamId = ws.Id,
            Title = "Immutable test item",
            Priority = WorkItemPriority.Routine,
            Status = WorkItemStatus.Assigned,
            Origin = WorkItemOrigin.Manual,
            RequestedByUserId = SeedData.BootstrapAdminId,
            RequestedByDisplayNameSnapshot = "Admin",
            LastActivityAt = DateTimeOffset.UtcNow,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.WorkItems.Add(wi);

        var ev = new WorkItemEvent
        {
            Id = Guid.NewGuid(),
            WorkItemId = wi.Id,
            SequenceNumber = 1,
            Action = WorkItemEventAction.Created,
            ActionByUserId = SeedData.BootstrapAdminId,
            ActionByDisplayNameSnapshot = "Admin",
            ActionAt = DateTimeOffset.UtcNow
        };
        db.WorkItemEvents.Add(ev);

        var upd = new WorkItemUpdate
        {
            Id = Guid.NewGuid(),
            WorkItemId = wi.Id,
            Message = "Initial note",
            AddedByUserId = SeedData.BootstrapAdminId,
            AddedByDisplayNameSnapshot = "Admin",
            AddedAt = DateTimeOffset.UtcNow
        };
        db.WorkItemUpdates.Add(upd);

        await db.SaveChangesAsync();

        // 1. Try modifying WorkItemEvent -> MUST THROW InvalidOperationException
        ev.RemarksSnapshot = "Illegal modification attempt";
        db.Entry(ev).State = EntityState.Modified;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        // Detach
        db.Entry(ev).State = EntityState.Detached;

        // 2. Try deleting WorkItemEvent -> MUST THROW InvalidOperationException
        db.Entry(ev).State = EntityState.Deleted;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.Entry(ev).State = EntityState.Detached;

        // 3. Try modifying WorkItemUpdate -> MUST THROW InvalidOperationException
        upd.Message = "Illegal edit attempt";
        db.Entry(upd).State = EntityState.Modified;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.Entry(upd).State = EntityState.Detached;

        // 4. Try deleting WorkItemUpdate -> MUST THROW InvalidOperationException
        db.Entry(upd).State = EntityState.Deleted;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task WorkItem_Scope_All_vs_Workstream_vs_Assigned_AccessControl()
    {
        var wsA = await CreateWorkstreamAsync("WS-SC-A", "Workstream Scope A");
        var wsB = await CreateWorkstreamAsync("WS-SC-B", "Workstream Scope B");
        var deskA = await CreateDeskAsync("DSK-SC-A", "Desk Scope A", wsA.Id, assignAdmin: false);
        var deskB = await CreateDeskAsync("DSK-SC-B", "Desk Scope B", wsB.Id, assignAdmin: false);

        // User A has Workstream scope in wsA
        var (clientUserA, userAId) = await CreateScopedUserClientAsync(
            "user_ws_a",
            "ROLE_WS_A",
            ScopeMode.Workstream,
            deskId: deskA.Id,
            workstreamId: wsA.Id
        );

        // User B has Assigned scope
        var (clientUserB, userBId) = await CreateScopedUserClientAsync(
            "user_assigned_b",
            "ROLE_ASSIGNED_B",
            ScopeMode.Assigned,
            deskId: deskB.Id,
            workstreamId: wsB.Id
        );

        // Create Item in wsA assigned to deskA/userA
        var adminClient = await CreateAdminClientAsync();
        var resItemA = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Task inside wsA",
            Instructions: null,
            WorkstreamId: wsA.Id,
            OfficeDeskId: deskA.Id,
            AssignedUserId: userAId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        Assert.Equal(HttpStatusCode.Created, resItemA.StatusCode);
        var itemAId = (await resItemA.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // Create Item in wsB assigned to deskB/userB
        var resItemB = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Task inside wsB",
            Instructions: null,
            WorkstreamId: wsB.Id,
            OfficeDeskId: deskB.Id,
            AssignedUserId: userBId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        Assert.Equal(HttpStatusCode.Created, resItemB.StatusCode);
        var itemBId = (await resItemB.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // User A can view itemA (belongs to wsA)
        var userAGetA = await clientUserA.GetAsync($"/api/work-items/{itemAId}");
        Assert.Equal(HttpStatusCode.OK, userAGetA.StatusCode);

        // User A cannot view itemB (belongs to wsB, 403 Forbidden)
        var userAGetB = await clientUserA.GetAsync($"/api/work-items/{itemBId}");
        Assert.Equal(HttpStatusCode.Forbidden, userAGetB.StatusCode);

        // User B has Assigned scope: can view itemB
        var userBGetB = await clientUserB.GetAsync($"/api/work-items/{itemBId}");
        Assert.Equal(HttpStatusCode.OK, userBGetB.StatusCode);

        // User B cannot view itemA (not assigned to userB or deskB)
        var userBGetA = await clientUserB.GetAsync($"/api/work-items/{itemAId}");
        Assert.Equal(HttpStatusCode.Forbidden, userBGetA.StatusCode);
    }

    [Fact]
    public async Task WorkItem_ContextIsolation_RedactsMatterAndDakWithoutPermission()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-ISO", "Workstream Isolation");
        var desk = await CreateDeskAsync("DSK-ISO", "Desk Isolation", ws.Id);

        // Create a Matter and a Dak using admin
        Guid matterId;
        Guid dakId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var district = await db.Districts.FirstAsync();
            var subDiv = await db.SubDivisions.FirstAsync(s => s.DistrictId == district.Id);
            var village = await db.Villages.FirstAsync(v => v.SubDivisionId == subDiv.Id);

            var matter = new Matter
            {
                Id = Guid.NewGuid(),
                VillageId = village.Id,
                WorkstreamId = ws.Id,
                Title = "Confidential Land Acquisition Dispute #882",
                MatterType = "Acquisition",
                Status = "Open",
                ReferenceNumber = "CONF-882-LAC",
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Matters.Add(matter);

            var dak = new Dak
            {
                Id = Guid.NewGuid(),
                DiaryNumber = "DAK-CONF-991",
                Subject = "Sensitive vigilance inquiry letter",
                SenderName = "Chief Vigilance Officer",
                ReceivedDate = DateOnly.FromDateTime(DateTime.UtcNow),
                InwardMode = "SpeedPost",
                Priority = DakPriority.Urgent,
                Status = DakStatus.Registered,
                WorkstreamId = ws.Id,
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Daks.Add(dak);

            await db.SaveChangesAsync();
            matterId = matter.Id;
            dakId = dak.Id;
        }

        // Create work item linking both Matter and Dak
        var createRes = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Verify confidential case links",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Urgent",
            DueAt: null,
            MatterId: matterId,
            DakId: dakId
        ));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var workItemId = (await createRes.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // User WITHOUT Matter.View and Dak.View
        var (userClientNoPerms, _) = await CreateScopedUserClientAsync(
            "user_no_matter_perm",
            "ROLE_WORK_ONLY",
            ScopeMode.Workstream,
            deskId: desk.Id,
            workstreamId: ws.Id
        );

        // Fetch detail with user without Matter/Dak permission
        var resRedacted = await userClientNoPerms.GetAsync($"/api/work-items/{workItemId}");
        Assert.Equal(HttpStatusCode.OK, resRedacted.StatusCode);
        var detailRedacted = await resRedacted.Content.ReadFromJsonAsync<WorkItemDetailDto>(JsonOpts);
        Assert.NotNull(detailRedacted);

        // Target titles and references MUST be null/redacted
        Assert.NotEmpty(detailRedacted.MatterLinks);
        Assert.False(detailRedacted.MatterLinks[0].IsAuthorized);
        Assert.Null(detailRedacted.MatterLinks[0].Title);
        Assert.Null(detailRedacted.MatterLinks[0].ReferenceNumber);

        Assert.NotEmpty(detailRedacted.DakLinks);
        Assert.False(detailRedacted.DakLinks[0].IsAuthorized);
        Assert.Null(detailRedacted.DakLinks[0].Subject);
        Assert.Null(detailRedacted.DakLinks[0].DiaryNumber);

        // Admin has Matter.View and Dak.View, so Admin sees full details
        var resAdmin = await adminClient.GetAsync($"/api/work-items/{workItemId}");
        Assert.Equal(HttpStatusCode.OK, resAdmin.StatusCode);
        var detailAdmin = await resAdmin.Content.ReadFromJsonAsync<WorkItemDetailDto>(JsonOpts);
        Assert.True(detailAdmin!.MatterLinks[0].IsAuthorized);
        Assert.Equal("Confidential Land Acquisition Dispute #882", detailAdmin.MatterLinks[0].Title);
        Assert.True(detailAdmin.DakLinks[0].IsAuthorized);
        Assert.Equal("Sensitive vigilance inquiry letter", detailAdmin.DakLinks[0].Subject);
    }

    [Fact]
    public async Task WorkItem_MyWork_SummaryCards_And_DelhiDateBuckets()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-DELHI", "Workstream Delhi");
        var desk = await CreateDeskAsync("DSK-DELHI", "Desk Delhi", ws.Id);

        var nowUtc = DateTimeOffset.UtcNow;

        // 1. Overdue item (Due 2 days ago)
        await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Overdue item",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Immediate",
            DueAt: nowUtc.AddDays(-2),
            MatterId: null,
            DakId: null
        ));

        // 2. Due Today item (Delhi time today)
        await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Due today item",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Urgent",
            DueAt: nowUtc.AddMinutes(30),
            MatterId: null,
            DakId: null
        ));

        // 3. This Week item (Due in 4 days)
        await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Due this week item",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: nowUtc.AddDays(4),
            MatterId: null,
            DakId: null
        ));

        // Query My Work
        var res = await adminClient.GetAsync("/api/work-items/my-work?scope=all");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var myWork = await res.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.NotNull(myWork);
        Assert.True(myWork.Summary.Overdue >= 1);
        Assert.True(myWork.Summary.DueToday >= 1);
        Assert.True(myWork.Summary.DueThisWeek >= 1);
        Assert.True(myWork.Summary.AssignedToMe >= 3);
        Assert.True(myWork.Items.Count >= 3);

        // Filter specifically for Overdue
        var resOverdue = await adminClient.GetAsync("/api/work-items/my-work?due=overdue");
        var overdueWork = await resOverdue.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.All(overdueWork!.Items, item => Assert.True(item.DueAt < DateTimeOffset.UtcNow));
    }

    // ========================================================================
    // AUDIT GATE 1: ASSIGNMENT OPTIONS SCOPE LEAK ENFORCEMENT
    // ========================================================================
    [Fact]
    public async Task WorkItem_AssignmentOptions_ScopeLeak_Enforcement()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws1 = await CreateWorkstreamAsync("WS-OPT1", "Workstream Options 1");
        var ws2 = await CreateWorkstreamAsync("WS-OPT2", "Workstream Options 2");

        var desk1 = await CreateDeskAsync("DSK-OPT1", "Desk Options 1", ws1.Id);
        var desk2 = await CreateDeskAsync("DSK-OPT2", "Desk Options 2", ws2.Id);

        // 1. Missing workstreamId -> 400 Bad Request
        var resMissing = await adminClient.GetAsync("/api/work-items/assignment-options");
        Assert.Equal(HttpStatusCode.BadRequest, resMissing.StatusCode);

        // 2. Inactive workstream -> 400 Bad Request
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var wsInactive = new Workstream
            {
                Id = Guid.NewGuid(),
                Code = "WS-INACT-OPT",
                Name = "Inactive WS Options",
                IsActive = false,
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Workstreams.Add(wsInactive);
            await db.SaveChangesAsync();

            var resInactive = await adminClient.GetAsync($"/api/work-items/assignment-options?workstreamId={wsInactive.Id}");
            Assert.Equal(HttpStatusCode.BadRequest, resInactive.StatusCode);
        }

        // 3. Workstream-scoped user in WS1
        var (userWs1Client, _) = await CreateScopedUserClientAsync(
            "user_ws1_opts",
            "ROLE_WS1_OPTS",
            ScopeMode.Workstream,
            deskId: desk1.Id,
            workstreamId: ws1.Id
        );

        // Caller from WS1 cannot query WS2 -> 403 Forbidden
        var resForbidden = await userWs1Client.GetAsync($"/api/work-items/assignment-options?workstreamId={ws2.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, resForbidden.StatusCode);

        // Caller from WS1 queries WS1 -> 200 OK
        var resAllowed = await userWs1Client.GetAsync($"/api/work-items/assignment-options?workstreamId={ws1.Id}");
        Assert.Equal(HttpStatusCode.OK, resAllowed.StatusCode);
        var bodyAllowed = await resAllowed.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var desks = bodyAllowed.GetProperty("desks").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(desk1.Id, desks);
        Assert.Contains(desk2.Id, desks); // Desk 2 classified under WS2 is still an eligible routing target for WS1 WorkItem

        // 4. Scope All caller can query any active workstream
        var resAdminWs2 = await adminClient.GetAsync($"/api/work-items/assignment-options?workstreamId={ws2.Id}");
        Assert.Equal(HttpStatusCode.OK, resAdminWs2.StatusCode);
        var bodyAdminWs2 = await resAdminWs2.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var desksAdmin = bodyAdminWs2.GetProperty("desks").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(desk2.Id, desksAdmin);
        Assert.Contains(desk1.Id, desksAdmin);
    }

    // ========================================================================
    // AUDIT GATE 2: EXPECTED REVISION STRICTNESS ACROSS MUTATION ENDPOINTS
    // ========================================================================
    [Fact]
    public async Task WorkItem_ExpectedRevision_Mandatory_Across_All_Mutation_Endpoints()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-REV-STRICT", "Workstream Rev Strict");
        var desk = await CreateDeskAsync("DSK-REV-STRICT", "Desk Rev Strict", ws.Id);

        var createRes = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Revision strictness testing",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var id = (await createRes.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // --- A. START WORK ---
        // Missing revision -> 400
        var startMissing = await adminClient.PostAsJsonAsync($"/api/work-items/{id}/start", new { });
        Assert.Equal(HttpStatusCode.BadRequest, startMissing.StatusCode);

        // Stale revision -> 409
        var startStale = await adminClient.PostAsJsonAsync($"/api/work-items/{id}/start", new StartWorkApiRequest(99));
        Assert.Equal(HttpStatusCode.Conflict, startStale.StatusCode);

        // Valid revision (0) -> 200, item revision becomes 1
        var startOk = await adminClient.PostAsJsonAsync($"/api/work-items/{id}/start", new StartWorkApiRequest(0));
        Assert.Equal(HttpStatusCode.OK, startOk.StatusCode);

        // --- B. ADD UPDATE ---
        // Missing revision -> 400
        var updateMissing = await adminClient.PostAsJsonAsync($"/api/work-items/{id}/updates", new { message = "Note without rev" });
        Assert.Equal(HttpStatusCode.BadRequest, updateMissing.StatusCode);

        // Stale revision -> 409
        var updateStale = await adminClient.PostAsJsonAsync($"/api/work-items/{id}/updates", new AddUpdateApiRequest("Note", 99));
        Assert.Equal(HttpStatusCode.Conflict, updateStale.StatusCode);

        // Valid revision (1) -> 200, item revision becomes 2
        var updateOk = await adminClient.PostAsJsonAsync($"/api/work-items/{id}/updates", new AddUpdateApiRequest("Valid note", 1));
        Assert.Equal(HttpStatusCode.OK, updateOk.StatusCode);

        // --- C. UPLOAD ATTACHMENT ---
        // Missing revision -> 400
        using (var contentNoRev = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent(SamplePdfBytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            contentNoRev.Add(file, "file", "test.pdf");
            var uploadMissing = await adminClient.PostAsync($"/api/work-items/{id}/attachments", contentNoRev);
            Assert.Equal(HttpStatusCode.BadRequest, uploadMissing.StatusCode);
        }

        // Stale revision -> 409
        using (var contentStale = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent(SamplePdfBytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            contentStale.Add(file, "file", "test.pdf");
            contentStale.Add(new StringContent("99"), "expectedRevision");
            var uploadStale = await adminClient.PostAsync($"/api/work-items/{id}/attachments", contentStale);
            Assert.Equal(HttpStatusCode.Conflict, uploadStale.StatusCode);
        }

        // Valid revision (2) -> 201, item revision becomes 3
        Guid attachmentId;
        using (var contentOk = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent(SamplePdfBytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            contentOk.Add(file, "file", "test.pdf");
            contentOk.Add(new StringContent("2"), "expectedRevision");
            var uploadOk = await adminClient.PostAsync($"/api/work-items/{id}/attachments", contentOk);
            Assert.Equal(HttpStatusCode.Created, uploadOk.StatusCode);
            var body = await uploadOk.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
            attachmentId = body.GetProperty("attachmentId").GetGuid();
        }

        // --- D. REMOVE ATTACHMENT ---
        // Missing revision -> 400
        var delMissing = await adminClient.DeleteAsync($"/api/work-items/{id}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.BadRequest, delMissing.StatusCode);

        // Stale revision -> 409
        var delStale = await adminClient.DeleteAsync($"/api/work-items/{id}/attachments/{attachmentId}?expectedRevision=99");
        Assert.Equal(HttpStatusCode.Conflict, delStale.StatusCode);

        // Valid revision (3) -> 200, item revision becomes 4
        var delOk = await adminClient.DeleteAsync($"/api/work-items/{id}/attachments/{attachmentId}?expectedRevision=3");
        Assert.Equal(HttpStatusCode.OK, delOk.StatusCode);

        var finalDetail = await (await adminClient.GetAsync($"/api/work-items/{id}")).Content.ReadFromJsonAsync<WorkItemDetailDto>(JsonOpts);
        Assert.Equal(4, finalDetail!.Revision);
        Assert.Empty(finalDetail.Attachments);
    }

    // ========================================================================
    // AUDIT GATE 3: ATTACHMENT CONTENT FINAL ACTIVE-STATE RECHECK
    // ========================================================================
    [Fact]
    public async Task WorkItem_Attachment_Content_Final_ActiveState_Recheck()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-STREAM-CHK", "Workstream Stream Chk");
        var desk = await CreateDeskAsync("DSK-STREAM-CHK", "Desk Stream Chk", ws.Id);

        var createRes = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Attachment state stream check",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var id = (await createRes.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // Upload attachment
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SamplePdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "stream_check.pdf");
        content.Add(new StringContent("0"), "expectedRevision");
        var uploadRes = await adminClient.PostAsync($"/api/work-items/{id}/attachments", content);
        Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);
        var attachmentId = (await uploadRes.Content.ReadFromJsonAsync<JsonElement>(JsonOpts)).GetProperty("attachmentId").GetGuid();

        // 1. Active attachment & document -> 200 OK
        var okRes = await adminClient.GetAsync($"/api/work-items/{id}/attachments/{attachmentId}/content");
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);

        // 2. Attachment RecordStatus archived -> 404
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var att = await db.WorkItemAttachments.FindAsync(attachmentId);
            att!.RecordStatus = RecordStatus.Archived;
            await db.SaveChangesAsync();
        }
        var resArchivedAtt = await adminClient.GetAsync($"/api/work-items/{id}/attachments/{attachmentId}/content");
        Assert.Equal(HttpStatusCode.NotFound, resArchivedAtt.StatusCode);

        // Restore attachment to Active, but archive Document RecordStatus -> 404
        Guid docId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var att = await db.WorkItemAttachments.FindAsync(attachmentId);
            att!.RecordStatus = RecordStatus.Active;
            docId = att.DocumentId;

            var doc = await db.Documents.FindAsync(docId);
            doc!.RecordStatus = RecordStatus.Archived;
            await db.SaveChangesAsync();
        }
        var resArchivedDoc = await adminClient.GetAsync($"/api/work-items/{id}/attachments/{attachmentId}/content");
        Assert.Equal(HttpStatusCode.NotFound, resArchivedDoc.StatusCode);

        // Restore doc RecordStatus to Active, but set business Status != "Active" (e.g. "Archived") -> 404
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var doc = await db.Documents.FindAsync(docId);
            doc!.RecordStatus = RecordStatus.Active;
            doc.Status = "Archived";
            await db.SaveChangesAsync();
        }
        var resNonActiveDoc = await adminClient.GetAsync($"/api/work-items/{id}/attachments/{attachmentId}/content");
        Assert.Equal(HttpStatusCode.NotFound, resNonActiveDoc.StatusCode);

        // Restore doc Status to "Active" -> 200 OK
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var doc = await db.Documents.FindAsync(docId);
            doc!.Status = "Active";
            await db.SaveChangesAsync();
        }
        var resRestored = await adminClient.GetAsync($"/api/work-items/{id}/attachments/{attachmentId}/content");
        Assert.Equal(HttpStatusCode.OK, resRestored.StatusCode);
    }

    // ========================================================================
    // AUDIT GATE 4: RETRY / COMMIT AMBIGUITY PROOF
    // ========================================================================
    [Fact]
    public async Task WorkItem_ExecutionStrategy_Retry_PreCommit_Failure_Proof()
    {
        var dbName = $"wi-retry-precommit-{Guid.NewGuid():N}";
        var interceptor = new TrackingSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;

        using var db = new LacDbContext(options);
        var storage = new TestInMemoryDocumentStorage();
        var workItemAuth = new WorkItemAuthorizationService(db);
        var matterAuth = new MatterAuthorizationService(db);
        var dakAuth = new DakAuthorizationService(db);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new WorkItemWorkflowService(
            db,
            storage,
            workItemAuth,
            matterAuth,
            dakAuth,
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: false, maxRetries: 2);

        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS-RETRY", Name = "Retry WS", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var desk = new OfficeDesk { Id = Guid.NewGuid(), Code = "DSK-RETRY", Name = "Retry Desk", WorkstreamId = ws.Id, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var user = new AppUser { Id = Guid.NewGuid(), Username = "user_retry", DisplayName = "Retry User", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var wi = new WorkItem
        {
            Id = Guid.NewGuid(),
            WorkstreamId = ws.Id,
            Title = "Pre-commit Retry Test",
            Priority = WorkItemPriority.Routine,
            Status = WorkItemStatus.InProgress,
            Origin = WorkItemOrigin.Manual,
            RequestedByUserId = user.Id,
            RequestedByDisplayNameSnapshot = user.DisplayName,
            Revision = 0,
            LastActivityAt = DateTimeOffset.UtcNow,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var assignment = new WorkItemAssignment
        {
            Id = Guid.NewGuid(),
            WorkItemId = wi.Id,
            OfficeDeskId = desk.Id,
            AssignedUserId = user.Id,
            IsActive = true,
            AssignedAt = DateTimeOffset.UtcNow,
            RecordStatus = RecordStatus.Active
        };
        var role = new Role { Id = Guid.NewGuid(), Code = "ROLE_RETRY", Name = "Retry Role", IsSystemRole = false };
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.WorkItemUpdate)
                   ?? new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemUpdate, Name = "Update" };
        if (perm.Id == Guid.Empty) perm.Id = Guid.NewGuid();

        db.Workstreams.Add(ws);
        db.OfficeDesks.Add(desk);
        db.AppUsers.Add(user);
        db.WorkItems.Add(wi);
        db.WorkItemAssignments.Add(assignment);
        db.Roles.Add(role);
        db.Permissions.Add(perm);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = perm.Id, ScopeMode = ScopeMode.All });
        await db.SaveChangesAsync();

        // Arm interceptor to fail exactly once on the first async SaveChanges call (before commit)
        interceptor.FailTimes = interceptor.SaveCalls + 1;

        using var ms = new MemoryStream(SamplePdfBytes);
        var cmd = new UploadWorkItemAttachmentCommand(ms, "retry_doc.pdf", "application/pdf", "Evidence", "SupportingDocument", 0);

        var attachmentId = await workflow.UploadAttachmentAsync(wi.Id, cmd, user.Id);
        Assert.NotEqual(Guid.Empty, attachmentId);

        // Verification of Pre-commit failure:
        // AttemptCount == 2
        Assert.Equal(2, strategy.AttemptCount);
        // Physical storage SaveCount == 1 (file saved once)
        Assert.Equal(1, storage.SaveCount);
        // Item revision increments once from 0 to 1
        var itemAfter = await db.WorkItems.FindAsync(wi.Id);
        Assert.Equal(1, itemAfter!.Revision);
        // Exactly one attachment row
        var attCount = await db.WorkItemAttachments.CountAsync(a => a.WorkItemId == wi.Id);
        Assert.Equal(1, attCount);
        // Exactly one WorkItemEvent row for AttachmentAdded
        var evCount = await db.WorkItemEvents.CountAsync(e => e.WorkItemId == wi.Id && e.Action == WorkItemEventAction.AttachmentAdded);
        Assert.Equal(1, evCount);
    }

    [Fact]
    public async Task WorkItem_ExecutionStrategy_PostCommit_Ambiguity_Proof()
    {
        var dbName = $"wi-postcommit-ambig-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var db = new LacDbContext(options);
        var storage = new TestInMemoryDocumentStorage();
        var workItemAuth = new WorkItemAuthorizationService(db);
        var matterAuth = new MatterAuthorizationService(db);
        var dakAuth = new DakAuthorizationService(db);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new WorkItemWorkflowService(
            db,
            storage,
            workItemAuth,
            matterAuth,
            dakAuth,
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2);

        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS-AMB", Name = "Ambig WS", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var desk = new OfficeDesk { Id = Guid.NewGuid(), Code = "DSK-AMB", Name = "Ambig Desk", WorkstreamId = ws.Id, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var user = new AppUser { Id = Guid.NewGuid(), Username = "user_amb", DisplayName = "Ambig User", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var wi = new WorkItem
        {
            Id = Guid.NewGuid(),
            WorkstreamId = ws.Id,
            Title = "Post-commit Ambiguity Test",
            Priority = WorkItemPriority.Routine,
            Status = WorkItemStatus.Assigned,
            Origin = WorkItemOrigin.Manual,
            RequestedByUserId = user.Id,
            RequestedByDisplayNameSnapshot = user.DisplayName,
            Revision = 0,
            LastActivityAt = DateTimeOffset.UtcNow,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var assignment = new WorkItemAssignment
        {
            Id = Guid.NewGuid(),
            WorkItemId = wi.Id,
            OfficeDeskId = desk.Id,
            AssignedUserId = user.Id,
            IsActive = true,
            AssignedAt = DateTimeOffset.UtcNow,
            RecordStatus = RecordStatus.Active
        };
        var role = new Role { Id = Guid.NewGuid(), Code = "ROLE_AMB", Name = "Ambig Role", IsSystemRole = false };
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.WorkItemUpdate)
                   ?? new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemUpdate, Name = "Update" };
        if (perm.Id == Guid.Empty) perm.Id = Guid.NewGuid();

        db.Workstreams.Add(ws);
        db.OfficeDesks.Add(desk);
        db.AppUsers.Add(user);
        db.WorkItems.Add(wi);
        db.WorkItemAssignments.Add(assignment);
        db.Roles.Add(role);
        db.Permissions.Add(perm);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = perm.Id, ScopeMode = ScopeMode.All });
        await db.SaveChangesAsync();

        // StartWorkAsync triggers ambiguity: commit succeeds, simulated timeout throws, verifier checks stable WorkItemEvent.Id
        var rev = await workflow.StartWorkAsync(wi.Id, new StartWorkCommand(0), user.Id);

        Assert.Equal(1, rev);
        Assert.True(strategy.VerifyCount >= 1, $"Expected VerifyCount >= 1 but got {strategy.VerifyCount}");

        var itemAfter = await db.WorkItems.FindAsync(wi.Id);
        Assert.Equal(WorkItemStatus.InProgress, itemAfter!.Status);
        Assert.Equal(1, itemAfter.Revision);

        // Exactly one Started event
        var startedEvents = await db.WorkItemEvents.Where(e => e.WorkItemId == wi.Id && e.Action == WorkItemEventAction.Started).ToListAsync();
        Assert.Single(startedEvents);
    }

    // ========================================================================
    // AUDIT GATE 5: AUTHORIZATION REGRESSION MATRIX (12 FOCUSED TESTS)
    // ========================================================================
    [Fact]
    public async Task AuthMatrix_01_Inactive_User_Denied()
    {
        var dbName = $"wi-auth-01-{Guid.NewGuid():N}";
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(dbName).Options);
        var auth = new WorkItemAuthorizationService(db);

        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS1", Name = "WS1", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var desk = new OfficeDesk { Id = Guid.NewGuid(), Code = "D1", Name = "D1", WorkstreamId = ws.Id, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var user = new AppUser { Id = Guid.NewGuid(), Username = "inactive_u", DisplayName = "Inactive U", PasswordHash = "x", IsActive = false, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var wi = new WorkItem { Id = Guid.NewGuid(), WorkstreamId = ws.Id, Title = "Test", Priority = WorkItemPriority.Routine, Status = WorkItemStatus.Assigned, Origin = WorkItemOrigin.Manual, RequestedByUserId = user.Id, RequestedByDisplayNameSnapshot = "U", Revision = 0, LastActivityAt = DateTimeOffset.UtcNow, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var role = new Role { Id = Guid.NewGuid(), Code = "ADMIN", Name = "Admin", IsSystemRole = true };
        var perm = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemView, Name = "View" };

        db.Workstreams.Add(ws);
        db.OfficeDesks.Add(desk);
        db.AppUsers.Add(user);
        db.WorkItems.Add(wi);
        db.Roles.Add(role);
        db.Permissions.Add(perm);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = perm.Id, ScopeMode = ScopeMode.All });
        await db.SaveChangesAsync();

        var canAccess = await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemView, user.Id);
        Assert.False(canAccess, "Inactive user must be denied access even with All scope.");

        var optRes = await auth.GetAssignmentOptionsForWorkstreamAsync(ws.Id, user.Id);
        Assert.False(optRes.Allowed);
        Assert.Equal(403, optRes.StatusCode);
    }

    [Fact]
    public async Task AuthMatrix_02_No_WorkItem_Permission_Denied()
    {
        var dbName = $"wi-auth-02-{Guid.NewGuid():N}";
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(dbName).Options);
        var auth = new WorkItemAuthorizationService(db);

        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS1", Name = "WS1", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var user = new AppUser { Id = Guid.NewGuid(), Username = "user_dak_only", DisplayName = "Dak Only", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var wi = new WorkItem { Id = Guid.NewGuid(), WorkstreamId = ws.Id, Title = "Test", Priority = WorkItemPriority.Routine, Status = WorkItemStatus.Assigned, Origin = WorkItemOrigin.Manual, RequestedByUserId = user.Id, RequestedByDisplayNameSnapshot = "U", Revision = 0, LastActivityAt = DateTimeOffset.UtcNow, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var role = new Role { Id = Guid.NewGuid(), Code = "DAK_ROLE", Name = "Dak Role", IsSystemRole = false };
        var perm = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.DakView, Name = "View" };

        db.Workstreams.Add(ws);
        db.AppUsers.Add(user);
        db.WorkItems.Add(wi);
        db.Roles.Add(role);
        db.Permissions.Add(perm);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = perm.Id, ScopeMode = ScopeMode.All });
        await db.SaveChangesAsync();

        var canAccess = await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemView, user.Id);
        Assert.False(canAccess, "Caller without WorkItem.View permission must be denied.");
    }

    [Fact]
    public async Task AuthMatrix_03_Workstream_Membership_Removal_Immediately_Revokes_Access()
    {
        var dbName = $"wi-auth-03-{Guid.NewGuid():N}";
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(dbName).Options);
        var auth = new WorkItemAuthorizationService(db);

        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS1", Name = "WS1", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var user = new AppUser { Id = Guid.NewGuid(), Username = "user_ws", DisplayName = "WS User", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var wi = new WorkItem { Id = Guid.NewGuid(), WorkstreamId = ws.Id, Title = "Test", Priority = WorkItemPriority.Routine, Status = WorkItemStatus.Assigned, Origin = WorkItemOrigin.Manual, RequestedByUserId = user.Id, RequestedByDisplayNameSnapshot = "U", Revision = 0, LastActivityAt = DateTimeOffset.UtcNow, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var role = new Role { Id = Guid.NewGuid(), Code = "WS_ROLE", Name = "WS Role", IsSystemRole = false };
        var perm = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemView, Name = "View" };
        var membership = new UserWorkstreamMembership { Id = Guid.NewGuid(), UserId = user.Id, WorkstreamId = ws.Id, IsActive = true, AssignedAt = DateTimeOffset.UtcNow };

        db.Workstreams.Add(ws);
        db.AppUsers.Add(user);
        db.WorkItems.Add(wi);
        db.Roles.Add(role);
        db.Permissions.Add(perm);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = perm.Id, ScopeMode = ScopeMode.Workstream });
        db.UserWorkstreamMemberships.Add(membership);
        await db.SaveChangesAsync();

        // Initially granted
        Assert.True(await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemView, user.Id));

        // Revoke membership
        membership.IsActive = false;
        await db.SaveChangesAsync();

        // Immediately revoked
        Assert.False(await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemView, user.Id), "Revoking workstream membership must immediately revoke access.");
    }

    [Fact]
    public async Task AuthMatrix_04_Desk_Membership_Removal_Immediately_Revokes_Access()
    {
        var dbName = $"wi-auth-04-{Guid.NewGuid():N}";
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(dbName).Options);
        var auth = new WorkItemAuthorizationService(db);

        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS1", Name = "WS1", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var desk = new OfficeDesk { Id = Guid.NewGuid(), Code = "D1", Name = "D1", WorkstreamId = ws.Id, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var user = new AppUser { Id = Guid.NewGuid(), Username = "user_desk", DisplayName = "Desk User", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var wi = new WorkItem { Id = Guid.NewGuid(), WorkstreamId = ws.Id, Title = "Test", Priority = WorkItemPriority.Routine, Status = WorkItemStatus.Assigned, Origin = WorkItemOrigin.Manual, RequestedByUserId = user.Id, RequestedByDisplayNameSnapshot = "U", Revision = 0, LastActivityAt = DateTimeOffset.UtcNow, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var assignment = new WorkItemAssignment { Id = Guid.NewGuid(), WorkItemId = wi.Id, OfficeDeskId = desk.Id, AssignedUserId = null, IsActive = true, AssignedAt = DateTimeOffset.UtcNow, RecordStatus = RecordStatus.Active };
        var role = new Role { Id = Guid.NewGuid(), Code = "DESK_ROLE", Name = "Desk Role", IsSystemRole = false };
        var perm = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemView, Name = "View" };
        var membership = new UserDeskMembership { Id = Guid.NewGuid(), UserId = user.Id, OfficeDeskId = desk.Id, IsPrimary = true, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };

        db.Workstreams.Add(ws);
        db.OfficeDesks.Add(desk);
        db.AppUsers.Add(user);
        db.WorkItems.Add(wi);
        db.WorkItemAssignments.Add(assignment);
        db.Roles.Add(role);
        db.Permissions.Add(perm);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = perm.Id, ScopeMode = ScopeMode.Assigned });
        db.UserDeskMemberships.Add(membership);
        await db.SaveChangesAsync();

        // Initially granted via desk membership
        Assert.True(await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemView, user.Id));

        // Revoke desk membership
        membership.IsActive = false;
        await db.SaveChangesAsync();

        // Immediately revoked
        Assert.False(await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemView, user.Id), "Revoking desk membership must immediately revoke access.");
    }

    [Fact]
    public async Task AuthMatrix_05_PrimaryDesk_Has_No_Authorization_Meaning()
    {
        var dbName = $"wi-auth-05-{Guid.NewGuid():N}";
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(dbName).Options);
        var auth = new WorkItemAuthorizationService(db);

        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS1", Name = "WS1", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var desk1 = new OfficeDesk { Id = Guid.NewGuid(), Code = "D1", Name = "D1 (Secondary)", WorkstreamId = ws.Id, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var desk2 = new OfficeDesk { Id = Guid.NewGuid(), Code = "D2", Name = "D2 (Primary)", WorkstreamId = ws.Id, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var user = new AppUser { Id = Guid.NewGuid(), Username = "user_nonprimary", DisplayName = "NonPrimary User", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };

        // Item assigned to Desk 1
        var wi = new WorkItem { Id = Guid.NewGuid(), WorkstreamId = ws.Id, Title = "Test", Priority = WorkItemPriority.Routine, Status = WorkItemStatus.Assigned, Origin = WorkItemOrigin.Manual, RequestedByUserId = user.Id, RequestedByDisplayNameSnapshot = "U", Revision = 0, LastActivityAt = DateTimeOffset.UtcNow, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var assignment = new WorkItemAssignment { Id = Guid.NewGuid(), WorkItemId = wi.Id, OfficeDeskId = desk1.Id, AssignedUserId = null, IsActive = true, AssignedAt = DateTimeOffset.UtcNow, RecordStatus = RecordStatus.Active };

        var role = new Role { Id = Guid.NewGuid(), Code = "ASSIGNED_ROLE", Name = "Assigned Role", IsSystemRole = false };
        var perm = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemView, Name = "View" };

        // User is NON-PRIMARY member of Desk 1, PRIMARY member of Desk 2
        var mem1 = new UserDeskMembership { Id = Guid.NewGuid(), UserId = user.Id, OfficeDeskId = desk1.Id, IsPrimary = false, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var mem2 = new UserDeskMembership { Id = Guid.NewGuid(), UserId = user.Id, OfficeDeskId = desk2.Id, IsPrimary = true, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };

        db.Workstreams.Add(ws);
        db.OfficeDesks.AddRange(desk1, desk2);
        db.AppUsers.Add(user);
        db.WorkItems.Add(wi);
        db.WorkItemAssignments.Add(assignment);
        db.Roles.Add(role);
        db.Permissions.Add(perm);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = perm.Id, ScopeMode = ScopeMode.Assigned });
        db.UserDeskMemberships.AddRange(mem1, mem2);
        await db.SaveChangesAsync();

        // Non-primary membership in Desk 1 MUST still grant access
        Assert.True(await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemView, user.Id), "PrimaryDesk flag must not dictate access; active membership is what matters.");
    }

    [Fact]
    public async Task AuthMatrix_06_ScopeMode_Own_Fails_Closed()
    {
        var dbName = $"wi-auth-06-{Guid.NewGuid():N}";
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(dbName).Options);
        var auth = new WorkItemAuthorizationService(db);

        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS1", Name = "WS1", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var user = new AppUser { Id = Guid.NewGuid(), Username = "user_own", DisplayName = "Own User", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var wi = new WorkItem { Id = Guid.NewGuid(), WorkstreamId = ws.Id, Title = "Test", Priority = WorkItemPriority.Routine, Status = WorkItemStatus.Assigned, Origin = WorkItemOrigin.Manual, RequestedByUserId = user.Id, RequestedByDisplayNameSnapshot = "U", Revision = 0, LastActivityAt = DateTimeOffset.UtcNow, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var role = new Role { Id = Guid.NewGuid(), Code = "OWN_ROLE", Name = "Own Role", IsSystemRole = false };
        var perm = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemView, Name = "View" };

        db.Workstreams.Add(ws);
        db.AppUsers.Add(user);
        db.WorkItems.Add(wi);
        db.Roles.Add(role);
        db.Permissions.Add(perm);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = perm.Id, ScopeMode = ScopeMode.Own });
        await db.SaveChangesAsync();

        // ScopeMode.Own must fail closed for WorkItem
        Assert.False(await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemView, user.Id), "ScopeMode.Own must fail closed for work items.");
    }

    [Fact]
    public async Task AuthMatrix_07_Designation_Does_Not_Confer_Authority()
    {
        var dbName = $"wi-auth-07-{Guid.NewGuid():N}";
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(dbName).Options);
        var auth = new WorkItemAuthorizationService(db);

        var desig = new Designation { Id = Guid.NewGuid(), Code = "DIR", Name = "Director General", RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS1", Name = "WS1", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var user = new AppUser { Id = Guid.NewGuid(), Username = "user_dir", DisplayName = "Director User", PasswordHash = "x", DesignationId = desig.Id, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var wi = new WorkItem { Id = Guid.NewGuid(), WorkstreamId = ws.Id, Title = "Test", Priority = WorkItemPriority.Routine, Status = WorkItemStatus.Assigned, Origin = WorkItemOrigin.Manual, RequestedByUserId = user.Id, RequestedByDisplayNameSnapshot = "U", Revision = 0, LastActivityAt = DateTimeOffset.UtcNow, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };

        db.Designations.Add(desig);
        db.Workstreams.Add(ws);
        db.AppUsers.Add(user);
        db.WorkItems.Add(wi);
        await db.SaveChangesAsync();

        // Has prestigious designation, but NO roles/permissions -> MUST fail
        Assert.False(await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemView, user.Id), "Designation alone must never confer authority.");
    }

    [Fact]
    public async Task AuthMatrix_08_Assigned_User_Must_Be_Active_And_Live_Member_Of_Desk()
    {
        var dbName = $"wi-auth-08-{Guid.NewGuid():N}";
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(dbName).Options);
        var auth = new WorkItemAuthorizationService(db);

        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS1", Name = "WS1", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var desk = new OfficeDesk { Id = Guid.NewGuid(), Code = "D1", Name = "D1", WorkstreamId = ws.Id, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var creator = new AppUser { Id = Guid.NewGuid(), Username = "creator", DisplayName = "Creator", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var targetNonMember = new AppUser { Id = Guid.NewGuid(), Username = "target_outsider", DisplayName = "Target Outsider", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };

        var role = new Role { Id = Guid.NewGuid(), Code = "ADMIN_ROLE", Name = "Admin", IsSystemRole = true };
        var permCreate = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemCreate, Name = "Create" };
        var permAssign = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemAssign, Name = "Assign" };

        db.Workstreams.Add(ws);
        db.OfficeDesks.Add(desk);
        db.AppUsers.AddRange(creator, targetNonMember);
        db.Roles.Add(role);
        db.Permissions.AddRange(permCreate, permAssign);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = creator.Id, RoleId = role.Id });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = permCreate.Id, ScopeMode = ScopeMode.All });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = permAssign.Id, ScopeMode = ScopeMode.All });
        await db.SaveChangesAsync();

        // Case A: Target user is NOT a member of desk
        var canCreateA = await auth.CanCreateWorkItemAsync(ws.Id, desk.Id, targetNonMember.Id, creator.Id);
        Assert.False(canCreateA, "Cannot assign work item to user who is not a member of target desk.");

        // Case B: Target user is a member of desk, but inactive user
        var membership = new UserDeskMembership { Id = Guid.NewGuid(), UserId = targetNonMember.Id, OfficeDeskId = desk.Id, IsPrimary = true, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        targetNonMember.IsActive = false;
        db.UserDeskMemberships.Add(membership);
        await db.SaveChangesAsync();

        var canCreateB = await auth.CanCreateWorkItemAsync(ws.Id, desk.Id, targetNonMember.Id, creator.Id);
        Assert.False(canCreateB, "Cannot assign work item to inactive user even if member of desk.");
    }

    [Fact]
    public async Task AuthMatrix_09_Contributor_Cannot_Obtain_Assign_Review_Complete_Authority()
    {
        var dbName = $"wi-auth-09-{Guid.NewGuid():N}";
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(dbName).Options);
        var auth = new WorkItemAuthorizationService(db);

        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS1", Name = "WS1", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var contributorUser = new AppUser { Id = Guid.NewGuid(), Username = "contrib_u", DisplayName = "Contrib User", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var wi = new WorkItem { Id = Guid.NewGuid(), WorkstreamId = ws.Id, Title = "Test", Priority = WorkItemPriority.Routine, Status = WorkItemStatus.InProgress, Origin = WorkItemOrigin.Manual, RequestedByUserId = Guid.NewGuid(), RequestedByDisplayNameSnapshot = "Req", Revision = 0, LastActivityAt = DateTimeOffset.UtcNow, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };

        var contrib = new WorkItemContributor
        {
            Id = Guid.NewGuid(),
            WorkItemId = wi.Id,
            UserId = contributorUser.Id,
            AddedByUserId = Guid.NewGuid(),
            Status = WorkItemContributorStatus.Active,
            IsActive = true,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var role = new Role { Id = Guid.NewGuid(), Code = "ASSIGNED_BASE", Name = "Assigned Base", IsSystemRole = false };
        var pView = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemView, Name = "View" };
        var pUpd = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemUpdate, Name = "Update" };
        var pCont = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemContribute, Name = "Contribute" };
        var pAsgn = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemAssign, Name = "Assign" };
        var pRev = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemReview, Name = "Review" };
        var pCmp = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemComplete, Name = "Complete" };

        db.Workstreams.Add(ws);
        db.AppUsers.Add(contributorUser);
        db.WorkItems.Add(wi);
        db.WorkItemContributors.Add(contrib);
        db.Roles.Add(role);
        db.Permissions.AddRange(pView, pUpd, pCont, pAsgn, pRev, pCmp);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = contributorUser.Id, RoleId = role.Id });

        // Grant ScopeMode.Assigned on all permissions
        foreach (var p in new[] { pView, pUpd, pCont, pAsgn, pRev, pCmp })
        {
            db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = p.Id, ScopeMode = ScopeMode.Assigned });
        }
        await db.SaveChangesAsync();

        // Contributor CAN View, Update, Contribute
        Assert.True(await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemView, contributorUser.Id));
        Assert.True(await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemUpdate, contributorUser.Id));
        Assert.True(await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemContribute, contributorUser.Id));

        // Contributor CANNOT Assign, Review, Complete
        Assert.False(await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemAssign, contributorUser.Id), "Contributor must never have Assign authority.");
        Assert.False(await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemReview, contributorUser.Id), "Contributor must never have Review authority.");
        Assert.False(await auth.CanAccessWorkItemAsync(wi.Id, PermissionCodes.WorkItemComplete, contributorUser.Id), "Contributor must never have Complete authority.");
    }

    [Fact]
    public async Task AuthMatrix_10_Terminal_Items_Excluded_From_Default_MyWork()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-TERM", "Workstream Terminal Filter");
        var desk = await CreateDeskAsync("DSK-TERM", "Desk Terminal Filter", ws.Id);

        // Create open item
        var openRes = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Open item terminal check",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var openId = (await openRes.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // Create item and complete it
        var compRes = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Completed item terminal check",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var compId = (await compRes.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var item = await db.WorkItems.FindAsync(compId);
            item!.Status = WorkItemStatus.Completed;
            await db.SaveChangesAsync();
        }

        // Query default My Work
        var res = await adminClient.GetAsync("/api/work-items/my-work");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = await res.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        var itemIds = data!.Items.Select(i => i.Id).ToList();

        Assert.Contains(openId, itemIds);
        Assert.DoesNotContain(compId, itemIds); // Completed item MUST be excluded from default view
    }

    [Fact]
    public async Task AuthMatrix_11_Summary_Computed_PrePagination()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-PAG", "Workstream Pagination");
        var desk = await CreateDeskAsync("DSK-PAG", "Desk Pagination", ws.Id);

        // Create 3 open items
        for (int i = 1; i <= 3; i++)
        {
            await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
                Title: $"Pagination item {i}",
                Instructions: null,
                WorkstreamId: ws.Id,
                OfficeDeskId: desk.Id,
                AssignedUserId: SeedData.BootstrapAdminId,
                Priority: "Routine",
                DueAt: null,
                MatterId: null,
                DakId: null
            ));
        }

        // Request page size = 1
        var res = await adminClient.GetAsync("/api/work-items/my-work?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = await res.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);

        Assert.NotNull(data);
        Assert.Single(data.Items); // Exactly 1 item returned due to pagination
        Assert.True(data.TotalCount >= 3);
        Assert.True(data.Summary.TotalOpen >= 3, "Summary metrics must be computed pre-pagination, not sliced to page size.");
    }

    [Fact]
    public async Task AuthMatrix_12_Deterministic_Ordering()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-ORDER", "Workstream Ordering");
        var desk = await CreateDeskAsync("DSK-ORDER", "Desk Ordering", ws.Id);

        var nowUtc = DateTimeOffset.UtcNow;

        // Item A: Due in 1 day, Urgent
        var resA = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Order item A (due tomorrow)",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Urgent",
            DueAt: nowUtc.AddDays(1),
            MatterId: null,
            DakId: null
        ));
        var idA = (await resA.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // Item B: Due in 10 days, Routine
        var resB = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Order item B (due later)",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: nowUtc.AddDays(10),
            MatterId: null,
            DakId: null
        ));
        var idB = (await resB.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        var res = await adminClient.GetAsync("/api/work-items/my-work");
        var data = await res.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);

        var idxA = data!.Items.ToList().FindIndex(i => i.Id == idA);
        var idxB = data.Items.ToList().FindIndex(i => i.Id == idB);

        Assert.True(idxA >= 0 && idxB >= 0);
        Assert.True(idxA < idxB, "Item with earlier due date and higher priority must appear before later item.");
    }

    // ========================================================================
    // FINAL AUDIT MICRO-PATCH REGRESSION TESTS
    // ========================================================================
    [Fact]
    public async Task WorkItem_DirectAssignment_Requires_Live_Desk_Membership_Under_Assigned_Scope()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-LIVE-MW", "Workstream Live MW");
        var desk = await CreateDeskAsync("DSK-LIVE-MW", "Desk Live MW", ws.Id, assignAdmin: false);

        var (userClient, userId) = await CreateScopedUserClientAsync(
            "user_direct_mw",
            "ROLE_DIR_MW",
            ScopeMode.Assigned,
            deskId: desk.Id,
            workstreamId: ws.Id
        );

        // Create item directly assigned to user on desk
        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Direct Item for Live Membership Test",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: userId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        Assert.Equal(HttpStatusCode.Created, resCreate.StatusCode);
        var createResult = await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts);
        var workItemId = createResult!.WorkItemId;

        // 1. User sees it in My Work initially while active member of desk
        var resMwBefore = await userClient.GetAsync("/api/work-items/my-work");
        Assert.Equal(HttpStatusCode.OK, resMwBefore.StatusCode);
        var mwBefore = await resMwBefore.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.Contains(mwBefore!.Items, i => i.Id == workItemId);

        // 2. User can access work item detail
        var resDetailBefore = await userClient.GetAsync($"/api/work-items/{workItemId}");
        Assert.Equal(HttpStatusCode.OK, resDetailBefore.StatusCode);

        // 3. Remove/close user's desk membership
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var membership = await db.UserDeskMemberships.FirstOrDefaultAsync(m => m.UserId == userId && m.OfficeDeskId == desk.Id);
            Assert.NotNull(membership);
            membership.IsActive = false;
            membership.RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // 4. Confirm direct assignee access is immediately revoked -> 403 Forbidden
        var resDetailAfter = await userClient.GetAsync($"/api/work-items/{workItemId}");
        Assert.Equal(HttpStatusCode.Forbidden, resDetailAfter.StatusCode);

        // 5. Confirm it disappears from Assigned My Work
        var resMwAfter = await userClient.GetAsync("/api/work-items/my-work");
        Assert.Equal(HttpStatusCode.OK, resMwAfter.StatusCode);
        var mwAfter = await resMwAfter.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.DoesNotContain(mwAfter!.Items, i => i.Id == workItemId);
    }

    [Fact]
    public async Task WorkItem_OfficeDesk_Workstream_Classification_Only_Not_Routing_Restriction()
    {
        var adminClient = await CreateAdminClientAsync();
        var wsA = await CreateWorkstreamAsync("WS-ROUT-A", "Workstream Routing A");
        var wsB = await CreateWorkstreamAsync("WS-ROUT-B", "Workstream Routing B");

        var deskA = await CreateDeskAsync("DSK-ROUT-A", "Desk Routing A", wsA.Id, assignAdmin: false);
        var deskB = await CreateDeskAsync("DSK-ROUT-B", "Desk Routing B", wsB.Id, assignAdmin: false);

        // Caller is authorized to assign in Workstream A (Workstream scope in wsA, member of deskA)
        var (userAClient, userAId) = await CreateScopedUserClientAsync(
            "user_assign_wsa",
            "ROLE_ASSIGN_WSA",
            ScopeMode.Workstream,
            deskId: deskA.Id,
            workstreamId: wsA.Id
        );

        // 1. Caller queries assignment-options for Workstream A -> Desk B (classified Workstream B) MUST appear as an eligible routing target
        var resOptions = await userAClient.GetAsync($"/api/work-items/assignment-options?workstreamId={wsA.Id}");
        Assert.Equal(HttpStatusCode.OK, resOptions.StatusCode);
        var optionsBody = await resOptions.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var deskIds = optionsBody.GetProperty("desks").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(deskB.Id, deskIds);

        // 2. Proves Desk B appearing does NOT grant caller Workstream B record authority:
        // Attempting to query assignment-options for Workstream B MUST return 403 Forbidden
        var resWsBOptions = await userAClient.GetAsync($"/api/work-items/assignment-options?workstreamId={wsB.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, resWsBOptions.StatusCode);

        // 3. Attempting to query Work items in Workstream B MUST NOT leak Workstream B items
        var resMyWork = await userAClient.GetAsync($"/api/work-items/my-work?workstreamId={wsB.Id}");
        Assert.Equal(HttpStatusCode.OK, resMyWork.StatusCode);
        var mwData = await resMyWork.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.Empty(mwData!.Items);
    }

    [Fact]
    public async Task WorkItem_ExecutionStrategy_Create_Commit_Ambiguity_Proof()
    {
        var dbName = $"wi-create-commit-ambig-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var db = new LacDbContext(options);
        var storage = new TestInMemoryDocumentStorage();
        var workItemAuth = new WorkItemAuthorizationService(db);
        var matterAuth = new MatterAuthorizationService(db);
        var dakAuth = new DakAuthorizationService(db);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new WorkItemWorkflowService(
            db,
            storage,
            workItemAuth,
            matterAuth,
            dakAuth,
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2);

        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS-CREATE-AMB", Name = "Create Amb WS", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var desk = new OfficeDesk { Id = Guid.NewGuid(), Code = "DSK-CREATE-AMB", Name = "Create Amb Desk", WorkstreamId = ws.Id, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var user = new AppUser { Id = Guid.NewGuid(), Username = "user_create_amb", DisplayName = "Create Amb User", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };

        var village = new Village { Id = Guid.NewGuid(), Name = "Village Amb", RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var matter = new Matter
        {
            Id = Guid.NewGuid(),
            VillageId = village.Id,
            WorkstreamId = ws.Id,
            Title = "Ambiguity Matter",
            Status = "Open",
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var dak = new Dak
        {
            Id = Guid.NewGuid(),
            WorkstreamId = ws.Id,
            DiaryNumber = "D/AMB/1",
            Subject = "Ambiguity Dak",
            Status = DakStatus.Registered,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var role = new Role { Id = Guid.NewGuid(), Code = "ROLE_CREATE_AMB", Name = "Create Amb Role", IsSystemRole = false };
        var permCreate = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.WorkItemCreate)
                         ?? new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemCreate, Name = "Create" };
        var permAssign = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.WorkItemAssign)
                         ?? new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemAssign, Name = "Assign" };
        var permMatter = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.MatterView)
                         ?? new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.MatterView, Name = "Matter View" };
        var permDak = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.DakView)
                      ?? new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.DakView, Name = "Dak View" };

        if (permCreate.Id == Guid.Empty) permCreate.Id = Guid.NewGuid();
        if (permAssign.Id == Guid.Empty) permAssign.Id = Guid.NewGuid();
        if (permMatter.Id == Guid.Empty) permMatter.Id = Guid.NewGuid();
        if (permDak.Id == Guid.Empty) permDak.Id = Guid.NewGuid();

        var deskMembership = new UserDeskMembership
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OfficeDeskId = desk.Id,
            IsActive = true,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.Villages.Add(village);
        db.Workstreams.Add(ws);
        db.OfficeDesks.Add(desk);
        db.AppUsers.Add(user);
        db.Matters.Add(matter);
        db.Daks.Add(dak);
        db.Roles.Add(role);
        db.Permissions.AddRange(permCreate, permAssign, permMatter, permDak);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id });
        db.RolePermissions.AddRange(
            new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = permCreate.Id, ScopeMode = ScopeMode.All },
            new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = permAssign.Id, ScopeMode = ScopeMode.All },
            new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = permMatter.Id, ScopeMode = ScopeMode.All },
            new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = permDak.Id, ScopeMode = ScopeMode.All }
        );
        db.UserDeskMemberships.Add(deskMembership);
        await db.SaveChangesAsync();

        using var ms = new MemoryStream(SamplePdfBytes);
        var cmd = new CreateWorkItemCommand(
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: user.Id,
            Title: "Create Commit Ambiguity Work Item",
            Instructions: "Check stable IDs and immutable event markers on retry",
            Priority: WorkItemPriority.Urgent,
            DueAt: DateTimeOffset.UtcNow.AddDays(2),
            MatterId: matter.Id,
            DakId: dak.Id,
            Attachments: [new WorkItemAttachmentUpload(ms, "ambig_doc.pdf", "application/pdf", "Initial Reference", "SupportingDocument")]
        );

        // CreateWorkItemAsync triggers commit ambiguity on attempt 1:
        // Transaction commits, simulated network timeout throws, verifySucceeded verifies exact immutable marker set:
        // - WorkItem ID exists
        // - Created event ID + Action
        // - Assigned event ID + Action + target desk/user
        // - Matter and Dak link IDs + stable ContextLinked events
        // - Attachment events
        var result = await workflow.CreateWorkItemAsync(cmd, user.Id);

        Assert.NotEqual(Guid.Empty, result.WorkItemId);
        Assert.True(strategy.VerifyCount >= 1, $"Expected VerifyCount >= 1 but got {strategy.VerifyCount}");

        // Exactly one WorkItem entity
        var items = await db.WorkItems.Where(w => w.Id == result.WorkItemId).ToListAsync();
        Assert.Single(items);
        Assert.Equal(0, items[0].Revision);

        // Exactly one assignment
        var assignments = await db.WorkItemAssignments.Where(a => a.WorkItemId == result.WorkItemId).ToListAsync();
        Assert.Single(assignments);
        Assert.Equal(user.Id, assignments[0].AssignedUserId);

        // Exactly one matter link
        var matterLinks = await db.WorkItemMatterLinks.Where(l => l.WorkItemId == result.WorkItemId).ToListAsync();
        Assert.Single(matterLinks);
        Assert.Equal(matter.Id, matterLinks[0].MatterId);

        // Exactly one dak link
        var dakLinks = await db.WorkItemDakLinks.Where(l => l.WorkItemId == result.WorkItemId).ToListAsync();
        Assert.Single(dakLinks);
        Assert.Equal(dak.Id, dakLinks[0].DakId);

        // Exactly one attachment
        var attachments = await db.WorkItemAttachments.Where(a => a.WorkItemId == result.WorkItemId).ToListAsync();
        Assert.Single(attachments);

        // Verify event counts: 1 Created, 1 Assigned, 2 ContextLinked, 1 AttachmentAdded = exactly 5 events
        var events = await db.WorkItemEvents.Where(e => e.WorkItemId == result.WorkItemId).ToListAsync();
        Assert.Equal(5, events.Count);
        Assert.Single(events, e => e.Action == WorkItemEventAction.Created);
        Assert.Single(events, e => e.Action == WorkItemEventAction.Assigned);
        Assert.Equal(2, events.Count(e => e.Action == WorkItemEventAction.ContextLinked));
        Assert.Single(events, e => e.Action == WorkItemEventAction.AttachmentAdded);
    }

    [Fact]
    public async Task WorkItem_OfficeDesk_Is_Institutional_Responsibility_Not_Private_Acl()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-INST", "Workstream Institutional");
        var desk = await CreateDeskAsync("DSK-INST", "Desk Institutional", ws.Id, assignAdmin: false);

        var (userAClient, userAId) = await CreateScopedUserClientAsync(
            "user_inst_a",
            "ROLE_INST_A",
            ScopeMode.Assigned,
            deskId: desk.Id,
            workstreamId: ws.Id
        );

        var (userBClient, userBId) = await CreateScopedUserClientAsync(
            "user_inst_b",
            "ROLE_INST_B",
            ScopeMode.Assigned,
            deskId: desk.Id,
            workstreamId: ws.Id
        );

        // Create work item assigned to Desk D with AssignedUserId = User A
        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Institutional Desk Assignment Item",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk.Id,
            AssignedUserId: userAId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        Assert.Equal(HttpStatusCode.Created, resCreate.StatusCode);
        var createResult = await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts);
        var workItemId = createResult!.WorkItemId;

        // 1. User A can access
        var resDetailA = await userAClient.GetAsync($"/api/work-items/{workItemId}");
        Assert.Equal(HttpStatusCode.OK, resDetailA.StatusCode);

        // 2. User B can also access (Desk D is institutional responsibility)
        var resDetailB = await userBClient.GetAsync($"/api/work-items/{workItemId}");
        Assert.Equal(HttpStatusCode.OK, resDetailB.StatusCode);

        // 3. Both see item in assigned My Work
        var resMwA = await userAClient.GetAsync("/api/work-items/my-work?relationship=assigned");
        Assert.Equal(HttpStatusCode.OK, resMwA.StatusCode);
        var mwA = await resMwA.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.Contains(mwA!.Items, i => i.Id == workItemId);

        var resMwB = await userBClient.GetAsync("/api/work-items/my-work?relationship=assigned");
        Assert.Equal(HttpStatusCode.OK, resMwB.StatusCode);
        var mwB = await resMwB.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.Contains(mwB!.Items, i => i.Id == workItemId);

        // 4. Assert AssignedToMe summary includes item for User B
        Assert.True(mwB.Summary.AssignedToMe >= 1, "AssignedToMe summary must count all items on caller's responsible desks.");

        // 5. Remove User A's desk membership
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var membershipA = await db.UserDeskMemberships.FirstOrDefaultAsync(m => m.UserId == userAId && m.OfficeDeskId == desk.Id);
            Assert.NotNull(membershipA);
            membershipA.IsActive = false;
            membershipA.RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // 6. User A immediately loses Assigned access -> 403
        var resDetailAAfter = await userAClient.GetAsync($"/api/work-items/{workItemId}");
        Assert.Equal(HttpStatusCode.Forbidden, resDetailAAfter.StatusCode);

        // 7. User B remains authorized through live Desk D membership -> 200
        var resDetailBAfter = await userBClient.GetAsync($"/api/work-items/{workItemId}");
        Assert.Equal(HttpStatusCode.OK, resDetailBAfter.StatusCode);
    }

    [Fact]
    public async Task WorkItem_Contributor_Lifecycle_Add_Submit_Return_Resubmit_Accept()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-CONTRIB-1", "Workstream Contributor 1");
        var desk1 = await CreateDeskAsync("DSK-RESP-1", "Responsible Desk 1", ws.Id, assignAdmin: true);
        var desk2 = await CreateDeskAsync("DSK-HELP-1", "Helper Desk 1", ws.Id, assignAdmin: false);

        // Contributor-only helper user has ScopeMode.Assigned on non-responsible desk2
        var (helperClient, helperId) = await CreateCustomUserClientAsync(
            "helper_user_1",
            "ROLE_HELPER_1",
            ScopeMode.Assigned,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute, PermissionCodes.WorkItemUpdate],
            deskId: desk2.Id,
            workstreamId: ws.Id
        );

        // 1. Create work item
        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Draft Section 19 Acquisition Order",
            Instructions: "Prepare detailed property schedule",
            WorkstreamId: ws.Id,
            OfficeDeskId: desk1.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Urgent",
            DueAt: DateTimeOffset.UtcNow.AddDays(5),
            MatterId: null,
            DakId: null
        ));
        Assert.Equal(HttpStatusCode.Created, resCreate.StatusCode);
        var createResult = await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts);
        var workItemId = createResult!.WorkItemId;

        // 2. Add Helper as Contributor
        var resAdd = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: helperId,
            Instructions: "Focus on survey numbers and cadastral maps",
            ExpectedRevision: 0
        ));
        Assert.Equal(HttpStatusCode.OK, resAdd.StatusCode);
        var addResult = await resAdd.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts);
        Assert.NotNull(addResult);
        var contributorId = addResult.ContributorId;
        Assert.Equal(1, addResult.Revision);

        // 3. Helper accesses work item
        var resDetailHelper = await helperClient.GetAsync($"/api/work-items/{workItemId}");
        Assert.Equal(HttpStatusCode.OK, resDetailHelper.StatusCode);
        var detail = await resDetailHelper.Content.ReadFromJsonAsync<WorkItemDetailDto>(JsonOpts);
        Assert.NotNull(detail);
        Assert.True(detail.Capabilities.CanContribute);
        Assert.True(detail.Capabilities.CanAddUpdate);
        Assert.True(detail.Capabilities.CanSubmitContribution);
        Assert.False(detail.Capabilities.CanAddContributor);
        Assert.False(detail.Capabilities.CanReviewContributions);

        var contrib = Assert.Single(detail.Contributors);
        Assert.Equal("Active", contrib.Status);
        Assert.Equal(helperId, contrib.UserId);

        // 4. Helper adds update
        var resUpdate = await helperClient.PostAsJsonAsync($"/api/work-items/{workItemId}/updates", new AddUpdateApiRequest(
            Message: "Completed extraction of 14 survey numbers",
            ExpectedRevision: 1
        ));
        Assert.Equal(HttpStatusCode.OK, resUpdate.StatusCode);

        // 5. Verify FirstActionAt is NOT set on assignment by helper action
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var item = await db.WorkItems.Include(w => w.Assignments).FirstAsync(w => w.Id == workItemId);
            Assert.Null(item.Assignments.Single(a => a.IsActive && a.RecordStatus == RecordStatus.Active).FirstActionAt);

            // Verify WorkItemEvent has ContributorId populated
            var updateEvent = await db.WorkItemEvents.FirstAsync(e => e.WorkItemId == workItemId && e.Action == WorkItemEventAction.UpdateAdded);
            Assert.Equal(contributorId, updateEvent.ContributorId);
        }

        // 6. Helper submits contribution
        var resSubmit = await helperClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{contributorId}/submit", new SubmitContributionApiRequest(
            ExpectedRevision: 2,
            Note: "All 14 survey numbers verified with field reports"
        ));
        Assert.Equal(HttpStatusCode.OK, resSubmit.StatusCode);
        var submitResult = await resSubmit.Content.ReadFromJsonAsync<SubmitContributionResult>(JsonOpts);
        Assert.Equal(3, submitResult!.Revision);

        // 7. While Submitted, helper mutation is blocked
        var resUpdateBlocked = await helperClient.PostAsJsonAsync($"/api/work-items/{workItemId}/updates", new AddUpdateApiRequest(
            Message: "Attempting edit while under review",
            ExpectedRevision: 3
        ));
        Assert.Equal(HttpStatusCode.Forbidden, resUpdateBlocked.StatusCode);

        // 8. Return for correction requires mandatory remarks
        var resReturnEmpty = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{contributorId}/return", new ReturnContributionApiRequest(
            ExpectedRevision: 3,
            Remarks: "   "
        ));
        Assert.Equal(HttpStatusCode.BadRequest, resReturnEmpty.StatusCode);

        // 9. Return with remarks succeeds
        var resReturn = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{contributorId}/return", new ReturnContributionApiRequest(
            ExpectedRevision: 3,
            Remarks: "Survey number 12 requires demarcation boundary clarification"
        ));
        Assert.Equal(HttpStatusCode.OK, resReturn.StatusCode);
        var returnResult = await resReturn.Content.ReadFromJsonAsync<ReturnContributionResult>(JsonOpts);
        Assert.Equal(4, returnResult!.Revision);

        // 10. Helper can now update and submit again
        var resUpdate2 = await helperClient.PostAsJsonAsync($"/api/work-items/{workItemId}/updates", new AddUpdateApiRequest(
            Message: "Attached boundary clarification for parcel 12",
            ExpectedRevision: 4
        ));
        Assert.Equal(HttpStatusCode.OK, resUpdate2.StatusCode);

        var resSubmit2 = await helperClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{contributorId}/submit", new SubmitContributionApiRequest(
            ExpectedRevision: 5,
            Note: "Resubmitted with boundary clarification"
        ));
        Assert.Equal(HttpStatusCode.OK, resSubmit2.StatusCode);

        // 11. Admin accepts contribution
        var resAccept = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{contributorId}/accept", new AcceptContributionApiRequest(
            ExpectedRevision: 6,
            Remarks: "Approved and merged into main draft"
        ));
        Assert.Equal(HttpStatusCode.OK, resAccept.StatusCode);
        var acceptResult = await resAccept.Content.ReadFromJsonAsync<AcceptContributionResult>(JsonOpts);
        Assert.Equal(7, acceptResult!.Revision);

        // 12. Helper access ends upon acceptance (not on responsible desk)
        var resDetailHelperAfter = await helperClient.GetAsync($"/api/work-items/{workItemId}");
        Assert.Equal(HttpStatusCode.Forbidden, resDetailHelperAfter.StatusCode);

        // 13. Work item remains open
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var item = await db.WorkItems.FirstAsync(w => w.Id == workItemId);
            Assert.NotEqual(WorkItemStatus.Completed, item.Status);
            Assert.NotEqual(WorkItemStatus.Cancelled, item.Status);

            var contribRecord = await db.WorkItemContributors.FirstAsync(c => c.Id == contributorId);
            Assert.Equal(WorkItemContributorStatus.Accepted, contribRecord.Status);
            Assert.False(contribRecord.IsActive);
        }
    }

    [Fact]
    public async Task WorkItem_Contributor_Strict_Actor_And_Review_Authorization()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-CONTRIB-2", "Workstream Contributor 2");
        var desk1 = await CreateDeskAsync("DSK-RESP-2", "Responsible Desk 2", ws.Id, assignAdmin: true);
        var desk2 = await CreateDeskAsync("DSK-HELP-2", "Helper Desk 2", ws.Id, assignAdmin: false);

        var (helper1Client, helper1Id) = await CreateCustomUserClientAsync(
            "helper_actor_1",
            "ROLE_HELPER_ACTOR_1",
            ScopeMode.Workstream,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute, PermissionCodes.WorkItemUpdate],
            deskId: desk2.Id,
            workstreamId: ws.Id
        );

        var (helper2Client, _) = await CreateCustomUserClientAsync(
            "helper_actor_2",
            "ROLE_HELPER_ACTOR_2",
            ScopeMode.Workstream,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute, PermissionCodes.WorkItemUpdate],
            deskId: desk2.Id,
            workstreamId: ws.Id
        );

        // 1. Create work item
        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Strict Actor Test Item",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk1.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var createResult = await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts);
        var workItemId = createResult!.WorkItemId;

        // 2. Add Helper 1
        var resAdd = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: helper1Id,
            Instructions: null,
            ExpectedRevision: 0
        ));
        var addResult = await resAdd.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts);
        var contributorId = addResult!.ContributorId;

        // 3. Helper 2 attempts to submit Helper 1's contribution -> 403 Forbidden
        var resSubmitImposter = await helper2Client.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{contributorId}/submit", new SubmitContributionApiRequest(
            ExpectedRevision: 1,
            Note: "Unauthorized submit"
        ));
        Assert.Equal(HttpStatusCode.Forbidden, resSubmitImposter.StatusCode);

        // 4. Helper 1 submits legitimately
        var resSubmitReal = await helper1Client.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{contributorId}/submit", new SubmitContributionApiRequest(
            ExpectedRevision: 1,
            Note: "Valid submit"
        ));
        Assert.Equal(HttpStatusCode.OK, resSubmitReal.StatusCode);

        // 5. Helper 2 (lacking WorkItem.Review) attempts to Accept or Return -> 403 Forbidden
        var resAcceptUnauthorized = await helper2Client.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{contributorId}/accept", new AcceptContributionApiRequest(
            ExpectedRevision: 2
        ));
        Assert.Equal(HttpStatusCode.Forbidden, resAcceptUnauthorized.StatusCode);

        var resReturnUnauthorized = await helper2Client.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{contributorId}/return", new ReturnContributionApiRequest(
            ExpectedRevision: 2,
            Remarks: "Unauthorized return"
        ));
        Assert.Equal(HttpStatusCode.Forbidden, resReturnUnauthorized.StatusCode);
    }

    [Fact]
    public async Task WorkItem_Contributor_Eligibility_And_Options()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-CONTRIB-3", "Workstream Contributor 3");
        var desk1 = await CreateDeskAsync("DSK-RESP-3", "Responsible Desk 3", ws.Id, assignAdmin: true);
        var desk2 = await CreateDeskAsync("DSK-HELP-3", "Helper Desk 3", ws.Id, assignAdmin: false);

        // User without WorkItem.Contribute (View only)
        var (_, viewOnlyId) = await CreateCustomUserClientAsync(
            "view_only_user",
            "ROLE_VIEW_ONLY",
            ScopeMode.Workstream,
            [PermissionCodes.WorkItemView],
            deskId: desk2.Id,
            workstreamId: ws.Id
        );

        // User with View + Update but NO WorkItem.Contribute (ineligible because SubmitContribution requires WorkItem.Contribute)
        var (_, updateOnlyId) = await CreateCustomUserClientAsync(
            "update_only_user",
            "ROLE_UPDATE_ONLY",
            ScopeMode.Workstream,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemUpdate],
            deskId: desk2.Id,
            workstreamId: ws.Id
        );

        // User with ScopeMode.Own only
        var (_, ownScopeId) = await CreateCustomUserClientAsync(
            "own_scope_user",
            "ROLE_OWN_SCOPE",
            ScopeMode.Own,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute],
            deskId: desk2.Id,
            workstreamId: ws.Id
        );

        // Eligible user (View + Contribute under Workstream)
        var (_, eligibleUserId) = await CreateCustomUserClientAsync(
            "eligible_helper_user",
            "ROLE_ELIGIBLE",
            ScopeMode.Workstream,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute],
            deskId: desk2.Id,
            workstreamId: ws.Id
        );

        // Create work item
        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Eligibility Test Item",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk1.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var createResult = await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts);
        var workItemId = createResult!.WorkItemId;

        // 1. Ineligible: View only -> 400
        var resAddViewOnly = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: viewOnlyId,
            Instructions: null,
            ExpectedRevision: 0
        ));
        Assert.Equal(HttpStatusCode.BadRequest, resAddViewOnly.StatusCode);

        // 1b. Ineligible: View + Update but NO WorkItem.Contribute -> 400
        var resAddUpdateOnly = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: updateOnlyId,
            Instructions: null,
            ExpectedRevision: 0
        ));
        Assert.Equal(HttpStatusCode.BadRequest, resAddUpdateOnly.StatusCode);

        // 2. Ineligible: ScopeMode.Own -> 400
        var resAddOwn = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: ownScopeId,
            Instructions: null,
            ExpectedRevision: 0
        ));
        Assert.Equal(HttpStatusCode.BadRequest, resAddOwn.StatusCode);

        // 3. Query contributor options -> includes eligible user, excludes view-only, update-only, and own-scope
        var resOptions = await adminClient.GetAsync($"/api/work-items/{workItemId}/contributor-options");
        Assert.Equal(HttpStatusCode.OK, resOptions.StatusCode);
        var optionsJson = await resOptions.Content.ReadFromJsonAsync<JsonElement>();
        var options = optionsJson.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("userId").GetGuid()).ToList();
        Assert.Contains(eligibleUserId, options);
        Assert.DoesNotContain(viewOnlyId, options);
        Assert.DoesNotContain(updateOnlyId, options);
        Assert.DoesNotContain(ownScopeId, options);

        // 4. Add eligible user -> 200 OK
        var resAddEligible = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: eligibleUserId,
            Instructions: null,
            ExpectedRevision: 0
        ));
        Assert.Equal(HttpStatusCode.OK, resAddEligible.StatusCode);

        // 5. Re-query contributor options -> eligible user now excluded (already active)
        var resOptions2 = await adminClient.GetAsync($"/api/work-items/{workItemId}/contributor-options");
        var optionsJson2 = await resOptions2.Content.ReadFromJsonAsync<JsonElement>();
        var options2 = optionsJson2.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("userId").GetGuid()).ToList();
        Assert.DoesNotContain(eligibleUserId, options2);

        // 6. Duplicate add -> 400
        var resAddDup = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: eligibleUserId,
            Instructions: null,
            ExpectedRevision: 1
        ));
        Assert.Equal(HttpStatusCode.BadRequest, resAddDup.StatusCode);
    }

    [Fact]
    public async Task WorkItem_Contributor_Remove_Soft_Deletes_And_Revokes_Access()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-CONTRIB-4", "Workstream Contributor 4");
        var desk1 = await CreateDeskAsync("DSK-RESP-4", "Responsible Desk 4", ws.Id, assignAdmin: true);
        var desk2 = await CreateDeskAsync("DSK-HELP-4", "Helper Desk 4", ws.Id, assignAdmin: false);

        var (helperClient, helperId) = await CreateCustomUserClientAsync(
            "helper_removable",
            "ROLE_REMOVABLE",
            ScopeMode.Assigned,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute, PermissionCodes.WorkItemUpdate],
            deskId: desk2.Id,
            workstreamId: ws.Id
        );

        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Removable Helper Test Item",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk1.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var createResult = await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts);
        var workItemId = createResult!.WorkItemId;

        var resAdd = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: helperId,
            Instructions: null,
            ExpectedRevision: 0
        ));
        var addResult = await resAdd.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts);
        var contributorId = addResult!.ContributorId;

        // Helper has access
        var resDetail1 = await helperClient.GetAsync($"/api/work-items/{workItemId}");
        Assert.Equal(HttpStatusCode.OK, resDetail1.StatusCode);

        // Remove contributor
        var resRemove = await adminClient.DeleteAsync($"/api/work-items/{workItemId}/contributors/{contributorId}?expectedRevision=1&reason=ReassignedElsewhere");
        Assert.Equal(HttpStatusCode.OK, resRemove.StatusCode);

        // Helper loses access
        var resDetail2 = await helperClient.GetAsync($"/api/work-items/{workItemId}");
        Assert.Equal(HttpStatusCode.Forbidden, resDetail2.StatusCode);

        // Verify soft delete in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var contrib = await db.WorkItemContributors.FirstAsync(c => c.Id == contributorId);
            Assert.Equal(WorkItemContributorStatus.Removed, contrib.Status);
            Assert.False(contrib.IsActive);

            var removeEvent = await db.WorkItemEvents.FirstAsync(e => e.WorkItemId == workItemId && e.Action == WorkItemEventAction.ContributorRemoved);
            Assert.Equal(contributorId, removeEvent.ContributorId);
        }
    }

    [Fact]
    public async Task WorkItem_Contributor_Concurrency_Conflict_Returns_409()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-CONTRIB-5", "Workstream Contributor 5");
        var desk1 = await CreateDeskAsync("DSK-RESP-5", "Responsible Desk 5", ws.Id, assignAdmin: true);
        var desk2 = await CreateDeskAsync("DSK-HELP-5", "Helper Desk 5", ws.Id, assignAdmin: false);

        var (helperClient, helperId) = await CreateCustomUserClientAsync(
            "helper_conflict",
            "ROLE_CONFLICT",
            ScopeMode.Workstream,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute, PermissionCodes.WorkItemUpdate],
            deskId: desk2.Id,
            workstreamId: ws.Id
        );

        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Conflict Test Item",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk1.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var createResult = await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts);
        var workItemId = createResult!.WorkItemId;

        // 1. Add with stale revision 99 -> 409
        var resAddConflict = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: helperId,
            Instructions: null,
            ExpectedRevision: 99
        ));
        Assert.Equal(HttpStatusCode.Conflict, resAddConflict.StatusCode);

        // Add with correct revision 0
        var resAdd = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: helperId,
            Instructions: null,
            ExpectedRevision: 0
        ));
        var addResult = await resAdd.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts);
        var contributorId = addResult!.ContributorId;

        // 2. Submit with stale revision 0 -> 409 (item is now at revision 1)
        var resSubmitConflict = await helperClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{contributorId}/submit", new SubmitContributionApiRequest(
            ExpectedRevision: 0,
            Note: null
        ));
        Assert.Equal(HttpStatusCode.Conflict, resSubmitConflict.StatusCode);
    }

    [Fact]
    public async Task WorkItem_Multiple_Contributors_Independent_Progress()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-CONTRIB-6", "Workstream Contributor 6");
        var desk1 = await CreateDeskAsync("DSK-RESP-6", "Responsible Desk 6", ws.Id, assignAdmin: true);
        var desk2 = await CreateDeskAsync("DSK-HELP-6A", "Helper Desk 6A", ws.Id, assignAdmin: false);
        var desk3 = await CreateDeskAsync("DSK-HELP-6B", "Helper Desk 6B", ws.Id, assignAdmin: false);

        var (helper1Client, helper1Id) = await CreateCustomUserClientAsync(
            "multi_helper_1",
            "ROLE_MULTI_1",
            ScopeMode.Assigned,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute, PermissionCodes.WorkItemUpdate],
            deskId: desk2.Id,
            workstreamId: ws.Id
        );

        var (helper2Client, helper2Id) = await CreateCustomUserClientAsync(
            "multi_helper_2",
            "ROLE_MULTI_2",
            ScopeMode.Assigned,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute, PermissionCodes.WorkItemUpdate],
            deskId: desk3.Id,
            workstreamId: ws.Id
        );

        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Multi-Contributor Independent Progress",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk1.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var createResult = await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts);
        var workItemId = createResult!.WorkItemId;

        // Add Helper 1
        var resAdd1 = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: helper1Id,
            Instructions: "Prepare legal citations",
            ExpectedRevision: 0
        ));
        var c1 = (await resAdd1.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts))!.ContributorId;

        // Add Helper 2
        var resAdd2 = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: helper2Id,
            Instructions: "Extract survey land maps",
            ExpectedRevision: 1
        ));
        var c2 = (await resAdd2.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts))!.ContributorId;

        // Helper 1 submits -> Helper 1 is Submitted, Helper 2 remains Active
        var resSubmit1 = await helper1Client.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{c1}/submit", new SubmitContributionApiRequest(
            ExpectedRevision: 2,
            Note: "Citations ready"
        ));
        Assert.Equal(HttpStatusCode.OK, resSubmit1.StatusCode);

        var resDetail = await adminClient.GetAsync($"/api/work-items/{workItemId}");
        var detail = await resDetail.Content.ReadFromJsonAsync<WorkItemDetailDto>(JsonOpts);
        var contributor1 = detail!.Contributors.First(c => c.ContributorId == c1);
        var contributor2 = detail.Contributors.First(c => c.ContributorId == c2);

        Assert.Equal("Submitted", contributor1.Status);
        Assert.Equal("Active", contributor2.Status);

        // Helper 2 can post updates while Helper 1 cannot
        var resUpdate2 = await helper2Client.PostAsJsonAsync($"/api/work-items/{workItemId}/updates", new AddUpdateApiRequest(
            Message: "Map 3 extracted",
            ExpectedRevision: 3
        ));
        Assert.Equal(HttpStatusCode.OK, resUpdate2.StatusCode);

        var resUpdate1Blocked = await helper1Client.PostAsJsonAsync($"/api/work-items/{workItemId}/updates", new AddUpdateApiRequest(
            Message: "Blocked while submitted",
            ExpectedRevision: 4
        ));
        Assert.Equal(HttpStatusCode.Forbidden, resUpdate1Blocked.StatusCode);
    }

    [Fact]
    public async Task WorkItem_MyWork_Delegated_Assistance_Metrics_And_Filters()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-CONTRIB-7", "Workstream Contributor 7");
        var deskResp = await CreateDeskAsync("DSK-RESP-7", "Responsible Desk 7", ws.Id, assignAdmin: true);
        var deskHelp = await CreateDeskAsync("DSK-HELP-7", "Helper Desk 7", ws.Id, assignAdmin: false);

        var (helperClient, helperId) = await CreateCustomUserClientAsync(
            "mywork_helper_user",
            "ROLE_MW_HELPER",
            ScopeMode.Workstream,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute, PermissionCodes.WorkItemUpdate],
            deskId: deskHelp.Id,
            workstreamId: ws.Id
        );

        // Create work item
        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "MyWork Delegated Assistance Metrics Item",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: deskResp.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var createResult = await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts);
        var workItemId = createResult!.WorkItemId;

        // Add helper
        var resAdd = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: helperId,
            Instructions: "Help with analysis",
            ExpectedRevision: 0
        ));
        var cId = (await resAdd.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts))!.ContributorId;

        // 1. Check Helper's MyWork: Helping count includes this item
        var resMwHelper = await helperClient.GetAsync("/api/work-items/my-work");
        Assert.Equal(HttpStatusCode.OK, resMwHelper.StatusCode);
        var mwHelper = await resMwHelper.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.True(mwHelper!.Summary.Helping >= 1);
        Assert.Equal(0, mwHelper.Summary.ReturnedToMe);

        // Check relationship=contributing filter
        var resMwContrib = await helperClient.GetAsync("/api/work-items/my-work?relationship=contributing");
        Assert.Equal(HttpStatusCode.OK, resMwContrib.StatusCode);
        var mwContrib = await resMwContrib.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.Contains(mwContrib!.Items, i => i.Id == workItemId);

        // 2. Check Admin's MyWork: WaitingOnOthers count includes this item
        var resMwAdmin = await adminClient.GetAsync("/api/work-items/my-work");
        var mwAdmin = await resMwAdmin.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.True(mwAdmin!.Summary.WaitingOnOthers >= 1);

        var resMwWaiting = await adminClient.GetAsync("/api/work-items/my-work?relationship=waiting");
        var mwWaiting = await resMwWaiting.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.Contains(mwWaiting!.Items, i => i.Id == workItemId);

        // 3. Helper submits -> Admin's NeedsReview increments
        await helperClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{cId}/submit", new SubmitContributionApiRequest(
            ExpectedRevision: 1,
            Note: "Ready"
        ));

        var resMwAdminAfterSubmit = await adminClient.GetAsync("/api/work-items/my-work");
        var mwAdminAfterSubmit = await resMwAdminAfterSubmit.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.True(mwAdminAfterSubmit!.Summary.NeedsReview >= 1);

        var resMwReview = await adminClient.GetAsync("/api/work-items/my-work?relationship=review");
        var mwReview = await resMwReview.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.Contains(mwReview!.Items, i => i.Id == workItemId);

        // 4. Admin returns contribution -> Helper's ReturnedToMe increments
        await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{cId}/return", new ReturnContributionApiRequest(
            ExpectedRevision: 2,
            Remarks: "Please add survey summary"
        ));

        var resMwHelperAfterReturn = await helperClient.GetAsync("/api/work-items/my-work");
        var mwHelperAfterReturn = await resMwHelperAfterReturn.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.True(mwHelperAfterReturn!.Summary.ReturnedToMe >= 1);
    }

    // ========================================================================
    // AUDIT 2F-B HARDENING: INDEPENDENT AUTHORITY SURVIVES CONTRIBUTOR STATE
    // ========================================================================
    [Fact]
    public async Task WorkItem_Contributor_IndependentWorkstreamAuth_Survives_SubmittedStatus()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-INDEP-1", "Independent WS 1");
        var desk1 = await CreateDeskAsync("DSK-RESP-I1", "Responsible Desk I1", ws.Id, assignAdmin: true);
        var desk2 = await CreateDeskAsync("DSK-HELP-I1", "Helper Desk I1", ws.Id, assignAdmin: false);

        // User has independent Workstream scope for View, Contribute, and Update
        var (helperClient, helperId) = await CreateCustomUserClientAsync(
            "ws_officer_submitted",
            "ROLE_WS_OFFICER_1",
            ScopeMode.Workstream,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute, PermissionCodes.WorkItemUpdate],
            deskId: desk2.Id,
            workstreamId: ws.Id
        );

        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Independent WS Auth Survives Submitted Test",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk1.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var workItemId = (await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // Add officer as contributor
        var resAdd = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: helperId,
            Instructions: null,
            ExpectedRevision: 0
        ));
        var cId = (await resAdd.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts))!.ContributorId;

        // Officer submits contribution -> contributor status is now Submitted
        var resSubmit = await helperClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{cId}/submit", new SubmitContributionApiRequest(
            ExpectedRevision: 1,
            Note: "Submitted for review"
        ));
        Assert.Equal(HttpStatusCode.OK, resSubmit.StatusCode);

        // Officer makes an update -> Independent Workstream authority survives! (200 OK, NOT 403)
        var resUpdate = await helperClient.PostAsJsonAsync($"/api/work-items/{workItemId}/updates", new AddUpdateApiRequest(
            Message: "Independent update through Workstream authority",
            ExpectedRevision: 2
        ));
        Assert.Equal(HttpStatusCode.OK, resUpdate.StatusCode);
    }

    [Fact]
    public async Task WorkItem_Contributor_IndependentWorkstreamAuth_Survives_RemovedStatus()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-INDEP-2", "Independent WS 2");
        var desk1 = await CreateDeskAsync("DSK-RESP-I2", "Responsible Desk I2", ws.Id, assignAdmin: true);
        var desk2 = await CreateDeskAsync("DSK-HELP-I2", "Helper Desk I2", ws.Id, assignAdmin: false);

        // User has independent Workstream scope for View, Contribute, and Update
        var (helperClient, helperId) = await CreateCustomUserClientAsync(
            "ws_officer_removed",
            "ROLE_WS_OFFICER_2",
            ScopeMode.Workstream,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute, PermissionCodes.WorkItemUpdate],
            deskId: desk2.Id,
            workstreamId: ws.Id
        );

        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Independent WS Auth Survives Removed Test",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk1.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var workItemId = (await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // Add officer as contributor
        var resAdd = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: helperId,
            Instructions: null,
            ExpectedRevision: 0
        ));
        var cId = (await resAdd.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts))!.ContributorId;

        // Admin removes contributor -> status = Removed, IsActive = false
        var resRemove = await adminClient.DeleteAsync($"/api/work-items/{workItemId}/contributors/{cId}?expectedRevision=1&reason=NoLongerNeeded");
        Assert.Equal(HttpStatusCode.OK, resRemove.StatusCode);

        // Officer calls GET /work-items/{id} -> Independent Workstream View survives! (200 OK, NOT 403)
        var resGet = await helperClient.GetAsync($"/api/work-items/{workItemId}");
        Assert.Equal(HttpStatusCode.OK, resGet.StatusCode);
    }

    [Fact]
    public async Task WorkItem_Contributor_ResponsibleDeskOfficer_Auth_Not_Affected_By_ContributorStatus()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-INDEP-3", "Independent WS 3");
        var desk1 = await CreateDeskAsync("DSK-RESP-I3", "Responsible Desk I3", ws.Id, assignAdmin: false);

        // Officer is on the responsible desk (desk1) with ScopeMode.Assigned
        var (officerClient, officerId) = await CreateCustomUserClientAsync(
            "desk_officer_contrib",
            "ROLE_DESK_OFFICER",
            ScopeMode.Assigned,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute, PermissionCodes.WorkItemUpdate],
            deskId: desk1.Id,
            workstreamId: ws.Id
        );

        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Responsible Desk Officer Contributor Auth Test",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk1.Id,
            AssignedUserId: officerId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var workItemId = (await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // Add desk officer as contributor too
        var resAdd = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: officerId,
            Instructions: null,
            ExpectedRevision: 0
        ));
        var cId = (await resAdd.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts))!.ContributorId;

        // Desk officer submits contribution -> contributor status is Submitted
        var resSubmit = await officerClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{cId}/submit", new SubmitContributionApiRequest(
            ExpectedRevision: 1,
            Note: "Ready"
        ));
        Assert.Equal(HttpStatusCode.OK, resSubmit.StatusCode);

        // Desk officer can still post updates via responsible desk authority!
        var resUpdate = await officerClient.PostAsJsonAsync($"/api/work-items/{workItemId}/updates", new AddUpdateApiRequest(
            Message: "Responsible desk update survives contributor Submitted state",
            ExpectedRevision: 2
        ));
        Assert.Equal(HttpStatusCode.OK, resUpdate.StatusCode);
    }

    // ========================================================================
    // AUDIT 2F-B HARDENING: MISSING EXPECTED REVISION RETURNS 400
    // ========================================================================
    [Fact]
    public async Task WorkItem_Contributor_MissingExpectedRevision_Returns_400()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-REV-400", "Revision 400 WS");
        var desk1 = await CreateDeskAsync("DSK-REV-400A", "Revision Desk 400A", ws.Id, assignAdmin: true);
        var desk2 = await CreateDeskAsync("DSK-REV-400B", "Revision Desk 400B", ws.Id, assignAdmin: false);

        var (helperClient, helperId) = await CreateCustomUserClientAsync(
            "helper_rev_400",
            "ROLE_REV_400",
            ScopeMode.Assigned,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute, PermissionCodes.WorkItemUpdate],
            deskId: desk2.Id,
            workstreamId: ws.Id
        );

        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Revision 400 Regression Item",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk1.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var workItemId = (await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        // 1. POST /contributors without expectedRevision -> 400
        var resNoRevAdd = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new
        {
            userId = helperId,
            instructions = "test"
        });
        Assert.Equal(HttpStatusCode.BadRequest, resNoRevAdd.StatusCode);

        // Add validly
        var resAdd = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: helperId,
            Instructions: null,
            ExpectedRevision: 0
        ));
        var cId = (await resAdd.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts))!.ContributorId;

        // 2. POST /submit without expectedRevision -> 400
        var resNoRevSubmit = await helperClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{cId}/submit", new
        {
            note = "missing rev"
        });
        Assert.Equal(HttpStatusCode.BadRequest, resNoRevSubmit.StatusCode);

        // Submit validly
        await helperClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{cId}/submit", new SubmitContributionApiRequest(
            ExpectedRevision: 1,
            Note: "ready"
        ));

        // 3. POST /return without expectedRevision -> 400
        var resNoRevReturn = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{cId}/return", new
        {
            remarks = "needs work"
        });
        Assert.Equal(HttpStatusCode.BadRequest, resNoRevReturn.StatusCode);

        // 4. POST /accept without expectedRevision -> 400
        var resNoRevAccept = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{cId}/accept", new
        {
            remarks = "ok"
        });
        Assert.Equal(HttpStatusCode.BadRequest, resNoRevAccept.StatusCode);

        // 5. DELETE without expectedRevision -> 400
        var resNoRevDelete = await adminClient.DeleteAsync($"/api/work-items/{workItemId}/contributors/{cId}");
        Assert.Equal(HttpStatusCode.BadRequest, resNoRevDelete.StatusCode);
    }

    // ========================================================================
    // AUDIT 2F-B HARDENING: RETRY & COMMIT AMBIGUITY PROOFS FOR CONTRIBUTORS
    // ========================================================================
    [Fact]
    public async Task WorkItem_Contributor_AddContributor_PreCommit_Retry_Proof()
    {
        var dbName = $"wi-contrib-retry-precommit-{Guid.NewGuid():N}";
        var interceptor = new TrackingSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;

        using var db = new LacDbContext(options);
        var storage = new TestInMemoryDocumentStorage();
        var workItemAuth = new WorkItemAuthorizationService(db);
        var matterAuth = new MatterAuthorizationService(db);
        var dakAuth = new DakAuthorizationService(db);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new WorkItemWorkflowService(
            db,
            storage,
            workItemAuth,
            matterAuth,
            dakAuth,
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: false, maxRetries: 2);

        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS-RETRY-C", Name = "Retry Contrib WS", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var desk1 = new OfficeDesk { Id = Guid.NewGuid(), Code = "DSK-R1", Name = "Desk R1", WorkstreamId = ws.Id, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var desk2 = new OfficeDesk { Id = Guid.NewGuid(), Code = "DSK-R2", Name = "Desk R2", WorkstreamId = ws.Id, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var adminUser = new AppUser { Id = Guid.NewGuid(), Username = "user_admin_c", DisplayName = "Admin C", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var helperUser = new AppUser { Id = Guid.NewGuid(), Username = "user_helper_c", DisplayName = "Helper C", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };

        var wi = new WorkItem
        {
            Id = Guid.NewGuid(),
            WorkstreamId = ws.Id,
            Title = "AddContributor PreCommit Retry Test",
            Priority = WorkItemPriority.Routine,
            Status = WorkItemStatus.InProgress,
            Origin = WorkItemOrigin.Manual,
            RequestedByUserId = adminUser.Id,
            RequestedByDisplayNameSnapshot = adminUser.DisplayName,
            Revision = 0,
            LastActivityAt = DateTimeOffset.UtcNow,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var assignment = new WorkItemAssignment
        {
            Id = Guid.NewGuid(),
            WorkItemId = wi.Id,
            OfficeDeskId = desk1.Id,
            AssignedUserId = adminUser.Id,
            IsActive = true,
            AssignedAt = DateTimeOffset.UtcNow,
            RecordStatus = RecordStatus.Active
        };

        // Admin role has WorkItemAssign (ScopeMode.All)
        var adminRole = new Role { Id = Guid.NewGuid(), Code = "ROLE_ADMIN_C", Name = "Admin Role", IsSystemRole = false };
        var permAssign = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.WorkItemAssign)
                         ?? new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemAssign, Name = "Assign" };
        if (permAssign.Id == Guid.Empty) permAssign.Id = Guid.NewGuid();

        // Helper role has WorkItemView and WorkItemContribute (ScopeMode.Workstream)
        var helperRole = new Role { Id = Guid.NewGuid(), Code = "ROLE_HELPER_C", Name = "Helper Role", IsSystemRole = false };
        var permView = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.WorkItemView)
                       ?? new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemView, Name = "View" };
        if (permView.Id == Guid.Empty) permView.Id = Guid.NewGuid();
        var permContribute = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.WorkItemContribute)
                             ?? new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemContribute, Name = "Contribute" };
        if (permContribute.Id == Guid.Empty) permContribute.Id = Guid.NewGuid();

        db.Workstreams.Add(ws);
        db.OfficeDesks.AddRange(desk1, desk2);
        db.AppUsers.AddRange(adminUser, helperUser);
        db.WorkItems.Add(wi);
        db.WorkItemAssignments.Add(assignment);
        db.Roles.AddRange(adminRole, helperRole);
        db.Permissions.AddRange(permAssign, permView, permContribute);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = adminUser.Id, RoleId = adminRole.Id });
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = helperUser.Id, RoleId = helperRole.Id });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = adminRole.Id, PermissionId = permAssign.Id, ScopeMode = ScopeMode.All });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = helperRole.Id, PermissionId = permView.Id, ScopeMode = ScopeMode.Workstream });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = helperRole.Id, PermissionId = permContribute.Id, ScopeMode = ScopeMode.Workstream });
        db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership { Id = Guid.NewGuid(), UserId = helperUser.Id, WorkstreamId = ws.Id, IsActive = true });
        await db.SaveChangesAsync();

        // Arm interceptor to fail exactly once on the first async SaveChanges call (before commit)
        interceptor.FailTimes = interceptor.SaveCalls + 1;

        var cmd = new AddContributorCommand(helperUser.Id, "Instructions for precommit retry", 0);
        var result = await workflow.AddContributorAsync(wi.Id, cmd, adminUser.Id);

        Assert.NotEqual(Guid.Empty, result.ContributorId);
        Assert.Equal(1, result.Revision);

        // Verification of pre-commit failure:
        // AttemptCount == 2
        Assert.Equal(2, strategy.AttemptCount);
        // Item revision increments once from 0 to 1
        var itemAfter = await db.WorkItems.FindAsync(wi.Id);
        Assert.Equal(1, itemAfter!.Revision);
        // Exactly one contributor row with matching identity
        var contribs = await db.WorkItemContributors.Where(c => c.WorkItemId == wi.Id).ToListAsync();
        Assert.Single(contribs);
        Assert.Equal(result.ContributorId, contribs[0].Id);
        Assert.Equal(helperUser.Id, contribs[0].UserId);
        Assert.Equal(WorkItemContributorStatus.Active, contribs[0].Status);
        // Exactly one WorkItemEvent row for ContributorAdded with matching identity
        var events = await db.WorkItemEvents.Where(e => e.WorkItemId == wi.Id && e.Action == WorkItemEventAction.ContributorAdded).ToListAsync();
        Assert.Single(events);
        Assert.Equal(result.ContributorId, events[0].ContributorId);
        Assert.Equal(helperUser.Id, events[0].TargetUserId);
    }

    [Fact]
    public async Task WorkItem_Contributor_SubmitContribution_PostCommit_Ambiguity_Proof()
    {
        var dbName = $"wi-contrib-postcommit-ambig-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var db = new LacDbContext(options);
        var storage = new TestInMemoryDocumentStorage();
        var workItemAuth = new WorkItemAuthorizationService(db);
        var matterAuth = new MatterAuthorizationService(db);
        var dakAuth = new DakAuthorizationService(db);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new WorkItemWorkflowService(
            db,
            storage,
            workItemAuth,
            matterAuth,
            dakAuth,
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2);

        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS-AMB-C", Name = "Ambiguity Contrib WS", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var desk = new OfficeDesk { Id = Guid.NewGuid(), Code = "DSK-AMB-C", Name = "Ambiguity Desk", WorkstreamId = ws.Id, IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var helperUser = new AppUser { Id = Guid.NewGuid(), Username = "user_helper_amb", DisplayName = "Ambiguity Helper", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };

        var wi = new WorkItem
        {
            Id = Guid.NewGuid(),
            WorkstreamId = ws.Id,
            Title = "SubmitContribution PostCommit Ambiguity Test",
            Priority = WorkItemPriority.Routine,
            Status = WorkItemStatus.InProgress,
            Origin = WorkItemOrigin.Manual,
            RequestedByUserId = helperUser.Id,
            RequestedByDisplayNameSnapshot = helperUser.DisplayName,
            Revision = 0,
            LastActivityAt = DateTimeOffset.UtcNow,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var contributor = new WorkItemContributor
        {
            Id = Guid.NewGuid(),
            WorkItemId = wi.Id,
            UserId = helperUser.Id,
            AddedByUserId = helperUser.Id,
            AddedAt = DateTimeOffset.UtcNow,
            Instructions = "Pre-existing active contributor",
            Status = WorkItemContributorStatus.Active,
            IsActive = true,
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var role = new Role { Id = Guid.NewGuid(), Code = "ROLE_AMB_HELPER", Name = "Amb Helper Role", IsSystemRole = false };
        var permView = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.WorkItemView)
                       ?? new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemView, Name = "View" };
        if (permView.Id == Guid.Empty) permView.Id = Guid.NewGuid();
        var permContribute = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.WorkItemContribute)
                             ?? new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.WorkItemContribute, Name = "Contribute" };
        if (permContribute.Id == Guid.Empty) permContribute.Id = Guid.NewGuid();

        db.Workstreams.Add(ws);
        db.OfficeDesks.Add(desk);
        db.AppUsers.Add(helperUser);
        db.WorkItems.Add(wi);
        db.WorkItemContributors.Add(contributor);
        db.Roles.Add(role);
        db.Permissions.AddRange(permView, permContribute);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = helperUser.Id, RoleId = role.Id });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = permView.Id, ScopeMode = ScopeMode.Assigned });
        db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = permContribute.Id, ScopeMode = ScopeMode.Assigned });
        await db.SaveChangesAsync();

        // SubmitContribution triggers commit ambiguity on attempt 1:
        // Commit succeeds, simulated timeout throws, verifier detects stable event with matching snapshots
        var cmd = new SubmitContributionCommand(0, "Note for postcommit ambiguity");
        var result = await workflow.SubmitContributionAsync(wi.Id, contributor.Id, cmd, helperUser.Id);

        Assert.Equal(1, result.Revision);
        Assert.True(strategy.VerifyCount >= 1, $"Expected VerifyCount >= 1 but got {strategy.VerifyCount}");

        // Revision increments once
        var itemAfter = await db.WorkItems.FindAsync(wi.Id);
        Assert.Equal(1, itemAfter!.Revision);

        // Contributor is now Submitted
        var contribAfter = await db.WorkItemContributors.FindAsync(contributor.Id);
        Assert.Equal(WorkItemContributorStatus.Submitted, contribAfter!.Status);

        // Exactly one ContributorSubmitted event with exact snapshot matching
        var submitEvents = await db.WorkItemEvents.Where(e => e.WorkItemId == wi.Id && e.Action == WorkItemEventAction.ContributorSubmitted).ToListAsync();
        Assert.Single(submitEvents);
        Assert.Equal(contributor.Id, submitEvents[0].ContributorId);
        Assert.Equal(helperUser.Id, submitEvents[0].TargetUserId);
    }

    // ========================================================================
    // AUDIT 2F-B HARDENING: DUAL SCOPE UNION IN MY WORK / REVIEW PROJECTION
    // ========================================================================
    [Fact]
    public async Task WorkItem_MyWork_DualScope_Union_Includes_Both_Paths()
    {
        var adminClient = await CreateAdminClientAsync();

        // Workstream A & B
        var wsA = await CreateWorkstreamAsync("WS-UNION-A", "Union Workstream A");
        var wsB = await CreateWorkstreamAsync("WS-UNION-B", "Union Workstream B");

        var deskA = await CreateDeskAsync("DSK-UA", "Desk UA", wsA.Id, assignAdmin: true);
        var deskB = await CreateDeskAsync("DSK-UB", "Desk UB", wsB.Id, assignAdmin: false);

        // Create dual-scoped officer:
        // Has Role A: WorkItemView + WorkItemReview under ScopeMode.Workstream in WS A
        // Has Role B: WorkItemView + WorkItemReview under ScopeMode.Assigned on Desk B (in WS B)
        string officerUsername = "dual_scope_officer";
        string officerPass = "DualScopePass!123";
        Guid officerId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            var roleA = new Role { Id = Guid.NewGuid(), Code = "ROLE_UNION_WS_A", Name = "Role Union WS A", IsSystemRole = false };
            var roleB = new Role { Id = Guid.NewGuid(), Code = "ROLE_UNION_ASSIGNED_B", Name = "Role Union Assigned B", IsSystemRole = false };
            db.Roles.AddRange(roleA, roleB);

            var pView = await db.Permissions.FirstAsync(p => p.Code == PermissionCodes.WorkItemView);
            var pReview = await db.Permissions.FirstAsync(p => p.Code == PermissionCodes.WorkItemReview);

            db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = roleA.Id, PermissionId = pView.Id, ScopeMode = ScopeMode.Workstream });
            db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = roleA.Id, PermissionId = pReview.Id, ScopeMode = ScopeMode.Workstream });

            db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = roleB.Id, PermissionId = pView.Id, ScopeMode = ScopeMode.Assigned });
            db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = roleB.Id, PermissionId = pReview.Id, ScopeMode = ScopeMode.Assigned });

            await db.SaveChangesAsync();

            var createRes = await adminClient.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(
                Username: officerUsername,
                DisplayName: "Dual Scope Officer",
                Password: officerPass,
                DesignationId: null,
                RoleIds: [roleA.Id, roleB.Id],
                WorkstreamIds: [wsA.Id],
                PrimaryWorkstreamId: wsA.Id
            ));
            officerId = (await createRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

            // Assign officer to Desk B (responsible desk for WS B)
            await adminClient.PostAsJsonAsync($"/api/admin/users/{officerId}/desks", new AssignDeskRequest(deskB.Id, IsPrimary: true));
        }

        var officerClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginRes = await officerClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(officerUsername, officerPass));
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        // Helper user to submit contributions
        var (helperClient, helperId) = await CreateCustomUserClientAsync(
            "union_helper_user",
            "ROLE_UNION_HELPER",
            ScopeMode.Assigned,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute],
            deskId: deskA.Id,
            workstreamId: wsA.Id
        );

        // Work Item 1: in Workstream A (assigned to Desk A). Officer authorizes review via Workstream scope.
        var resCreate1 = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Work Item in WS A for Union Test",
            Instructions: null,
            WorkstreamId: wsA.Id,
            OfficeDeskId: deskA.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var item1Id = (await resCreate1.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        var resAdd1 = await adminClient.PostAsJsonAsync($"/api/work-items/{item1Id}/contributors", new AddContributorApiRequest(
            UserId: helperId,
            Instructions: null,
            ExpectedRevision: 0
        ));
        var c1Id = (await resAdd1.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts))!.ContributorId;

        await helperClient.PostAsJsonAsync($"/api/work-items/{item1Id}/contributors/{c1Id}/submit", new SubmitContributionApiRequest(
            ExpectedRevision: 1,
            Note: "Ready in WS A"
        ));

        // Work Item 2: in Workstream B (assigned to Desk B). Officer authorizes review via Assigned responsible desk scope.
        var resCreate2 = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Work Item in WS B for Union Test",
            Instructions: null,
            WorkstreamId: wsB.Id,
            OfficeDeskId: deskB.Id,
            AssignedUserId: officerId,
            Priority: "Urgent",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        var item2Id = (await resCreate2.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts))!.WorkItemId;

        var resAdd2 = await adminClient.PostAsJsonAsync($"/api/work-items/{item2Id}/contributors", new AddContributorApiRequest(
            UserId: helperId,
            Instructions: null,
            ExpectedRevision: 0
        ));
        var c2Id = (await resAdd2.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts))!.ContributorId;

        await helperClient.PostAsJsonAsync($"/api/work-items/{item2Id}/contributors/{c2Id}/submit", new SubmitContributionApiRequest(
            ExpectedRevision: 1,
            Note: "Ready in WS B"
        ));

        // Officer queries My Work with relationship=review:
        // Must contain BOTH items from the union of Workstream scope and Assigned desk scope!
        var resReview = await officerClient.GetAsync("/api/work-items/my-work?relationship=review");
        Assert.Equal(HttpStatusCode.OK, resReview.StatusCode);
        var reviewData = await resReview.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);

        Assert.NotNull(reviewData);
        Assert.True(reviewData.Summary.NeedsReview >= 2, $"Expected NeedsReview >= 2, got {reviewData.Summary.NeedsReview}");
        Assert.Contains(reviewData.Items, i => i.Id == item1Id);
        Assert.Contains(reviewData.Items, i => i.Id == item2Id);
    }

    // ========================================================================
    // AUDIT 2F-B: MY WORK OPERATIONAL PARTICIPATION REGRESSIONS
    // ========================================================================
    [Fact]
    public async Task MyWork_AllScope_DoesNotBecome_AllWorkItems_Directory()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-ALL-DIR", "All Scope Directory Test WS");
        var deskOther = await CreateDeskAsync("DSK-OTHER-DIR", "Other Desk", ws.Id, assignAdmin: false);

        var (otherUserClient, otherUserId) = await CreateCustomUserClientAsync(
            "other_user_dir",
            "ROLE_OTHER_DIR",
            ScopeMode.Assigned,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute],
            deskId: deskOther.Id,
            workstreamId: ws.Id
        );

        // Work item created for other user on other desk — admin has no operational participation
        Guid unrelatedItemId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var item = new WorkItem
            {
                Id = Guid.NewGuid(),
                WorkstreamId = ws.Id,
                Title = "Unrelated Item Not In Admin My Work",
                Priority = WorkItemPriority.Routine,
                Status = WorkItemStatus.Assigned,
                Origin = WorkItemOrigin.Manual,
                RequestedByUserId = otherUserId,
                RequestedByDisplayNameSnapshot = "Other User",
                Revision = 0,
                LastActivityAt = DateTimeOffset.UtcNow,
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var assignment = new WorkItemAssignment
            {
                Id = Guid.NewGuid(),
                WorkItemId = item.Id,
                OfficeDeskId = deskOther.Id,
                AssignedUserId = otherUserId,
                IsActive = true,
                AssignedAt = DateTimeOffset.UtcNow,
                RecordStatus = RecordStatus.Active
            };
            db.WorkItems.Add(item);
            db.WorkItemAssignments.Add(assignment);
            await db.SaveChangesAsync();
            unrelatedItemId = item.Id;
        }

        Assert.NotEqual(Guid.Empty, unrelatedItemId);

        // Admin has ScopeMode.All on WorkItem.View, but has NO operational participation on this item
        var resMwAdmin = await adminClient.GetAsync("/api/work-items/my-work");
        Assert.Equal(HttpStatusCode.OK, resMwAdmin.StatusCode);
        var mwAdmin = await resMwAdmin.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.NotNull(mwAdmin);

        // Unrelated item must NOT appear in Admin's My Work
        Assert.DoesNotContain(mwAdmin.Items, i => i.Id == unrelatedItemId);
    }

    [Fact]
    public async Task MyWork_WorkstreamScope_DoesNotBecome_WorkstreamDirectory()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-DIR-TEST", "Workstream Directory Test WS");
        var desk1 = await CreateDeskAsync("DSK-WSDIR-1", "Desk WSDIR 1", ws.Id, assignAdmin: false);
        var desk2 = await CreateDeskAsync("DSK-WSDIR-2", "Desk WSDIR 2", ws.Id, assignAdmin: false);

        var (callerClient, callerId) = await CreateCustomUserClientAsync(
            "ws_caller_user",
            "ROLE_WS_CALLER",
            ScopeMode.Workstream,
            [PermissionCodes.WorkItemView],
            deskId: desk1.Id,
            workstreamId: ws.Id
        );

        // Unrelated work item in same workstream, but assigned institutionally to desk2 (no named user) and requested by admin
        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Unrelated Item In Same Workstream",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: desk2.Id,
            AssignedUserId: null,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        Assert.Equal(HttpStatusCode.Created, resCreate.StatusCode);
        var createResult = await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts);
        Assert.NotNull(createResult);
        Assert.NotEqual(Guid.Empty, createResult.WorkItemId);
        var unrelatedItemId = createResult.WorkItemId;

        // Caller has Workstream View authority, but NO operational relationship (not on desk2, did not request, not contributor)
        var resMw = await callerClient.GetAsync("/api/work-items/my-work");
        Assert.Equal(HttpStatusCode.OK, resMw.StatusCode);
        var mw = await resMw.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.NotNull(mw);

        // Item must NOT appear in caller's My Work
        Assert.DoesNotContain(mw.Items, i => i.Id == unrelatedItemId);
    }

    [Fact]
    public async Task MyWork_RequestedBy_DoesNot_Bypass_ViewAuthorization()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-REQ-AUTH", "Requested By Auth WS");
        var deskResponsible = await CreateDeskAsync("DSK-RESP-REQ", "Responsible Desk Req", ws.Id, assignAdmin: true);
        var deskCaller = await CreateDeskAsync("DSK-CALLER-REQ", "Caller Desk Req", ws.Id, assignAdmin: false);

        // Caller has ScopeMode.Assigned ONLY for WorkItem.View (on deskCaller)
        var (callerClient, callerId) = await CreateCustomUserClientAsync(
            "caller_req_user",
            "ROLE_CALLER_REQ",
            ScopeMode.Assigned,
            [PermissionCodes.WorkItemView],
            deskId: deskCaller.Id,
            workstreamId: ws.Id
        );

        // Create work item assigned to deskResponsible, requested by caller
        Guid workItemId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var item = new WorkItem
            {
                Id = Guid.NewGuid(),
                WorkstreamId = ws.Id,
                Title = "Item Requested By Caller But Assigned To Other Desk",
                Priority = WorkItemPriority.Routine,
                Status = WorkItemStatus.Assigned,
                Origin = WorkItemOrigin.Manual,
                RequestedByUserId = callerId,
                RequestedByDisplayNameSnapshot = "Caller User",
                Revision = 0,
                LastActivityAt = DateTimeOffset.UtcNow,
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var assignment = new WorkItemAssignment
            {
                Id = Guid.NewGuid(),
                WorkItemId = item.Id,
                OfficeDeskId = deskResponsible.Id,
                AssignedUserId = SeedData.BootstrapAdminId,
                IsActive = true,
                AssignedAt = DateTimeOffset.UtcNow,
                RecordStatus = RecordStatus.Active
            };
            db.WorkItems.Add(item);
            db.WorkItemAssignments.Add(assignment);
            await db.SaveChangesAsync();
            workItemId = item.Id;
        }
        Assert.NotEqual(Guid.Empty, workItemId);

        // Caller requested it (operational relevance matches), but caller only has ScopeMode.Assigned
        // and is NOT on responsible desk and NOT an active contributor.
        // Therefore WorkItem.View authorization fails!
        var resMw = await callerClient.GetAsync("/api/work-items/my-work");
        Assert.Equal(HttpStatusCode.OK, resMw.StatusCode);
        var mw = await resMw.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.NotNull(mw);

        Assert.DoesNotContain(mw.Items, i => i.Id == workItemId);
    }

    [Fact]
    public async Task Review_Participation_Is_Operational_Participation()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-REV-OP", "Review Op WS");
        var deskResp = await CreateDeskAsync("DSK-RESP-ROP", "Responsible Desk ROp", ws.Id, assignAdmin: true);
        var deskReviewer = await CreateDeskAsync("DSK-REV-ROP", "Reviewer Desk ROp", ws.Id, assignAdmin: false);

        // Reviewer has Workstream scope on View and Review, but is NOT on responsible desk, did NOT request, is NOT contributor
        var (reviewerClient, _) = await CreateCustomUserClientAsync(
            "reviewer_user_rop",
            "ROLE_REVIEWER_ROP",
            ScopeMode.Workstream,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemReview],
            deskId: deskReviewer.Id,
            workstreamId: ws.Id
        );

        // Helper user
        var (helperClient, helperId) = await CreateCustomUserClientAsync(
            "helper_user_rop",
            "ROLE_HELPER_ROP",
            ScopeMode.Assigned,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute],
            deskId: deskReviewer.Id,
            workstreamId: ws.Id
        );

        var resCreate = await adminClient.PostAsJsonAsync("/api/work-items", new CreateWorkItemApiRequest(
            Title: "Review Op Test Item",
            Instructions: null,
            WorkstreamId: ws.Id,
            OfficeDeskId: deskResp.Id,
            AssignedUserId: SeedData.BootstrapAdminId,
            Priority: "Routine",
            DueAt: null,
            MatterId: null,
            DakId: null
        ));
        Assert.Equal(HttpStatusCode.Created, resCreate.StatusCode);
        var createResult = await resCreate.Content.ReadFromJsonAsync<CreateWorkItemResult>(JsonOpts);
        Assert.NotNull(createResult);
        Assert.NotEqual(Guid.Empty, createResult.WorkItemId);
        var workItemId = createResult.WorkItemId;

        // Add helper as contributor
        var resAdd = await adminClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors", new AddContributorApiRequest(
            UserId: helperId,
            Instructions: null,
            ExpectedRevision: 0
        ));
        Assert.Equal(HttpStatusCode.OK, resAdd.StatusCode);
        var addResult = await resAdd.Content.ReadFromJsonAsync<AddContributorResult>(JsonOpts);
        Assert.NotNull(addResult);
        Assert.NotEqual(Guid.Empty, addResult.ContributorId);
        var cId = addResult.ContributorId;

        // Before submit: Reviewer has NO operational participation on this item -> not in My Work
        var resMwBefore = await reviewerClient.GetAsync("/api/work-items/my-work");
        Assert.Equal(HttpStatusCode.OK, resMwBefore.StatusCode);
        var mwBefore = await resMwBefore.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.NotNull(mwBefore);
        Assert.DoesNotContain(mwBefore.Items, i => i.Id == workItemId);

        // Helper submits contribution -> Actionable Review Participation activates for Workstream reviewer!
        var resSubmit = await helperClient.PostAsJsonAsync($"/api/work-items/{workItemId}/contributors/{cId}/submit", new SubmitContributionApiRequest(
            ExpectedRevision: 1,
            Note: "Ready for review"
        ));
        Assert.Equal(HttpStatusCode.OK, resSubmit.StatusCode);

        // Now reviewer has operational relevance via Actionable Review Participation!
        var resMwAfter = await reviewerClient.GetAsync("/api/work-items/my-work?relationship=review");
        Assert.Equal(HttpStatusCode.OK, resMwAfter.StatusCode);
        var mwAfter = await resMwAfter.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.NotNull(mwAfter);

        Assert.True(mwAfter.Summary.NeedsReview >= 1);
        Assert.Contains(mwAfter.Items, i => i.Id == workItemId);
    }

    [Fact]
    public async Task MyWork_AssignedReview_Requires_Active_CurrentAssignment()
    {
        var adminClient = await CreateAdminClientAsync();
        var ws = await CreateWorkstreamAsync("WS-STALE-REV", "Stale Assigned Review WS");
        var deskD = await CreateDeskAsync("DSK-STALE-D", "Desk D", ws.Id, assignAdmin: false);

        // Caller user:
        // - WorkItem.View via ScopeMode.Workstream (in ws)
        // - WorkItem.Review via ScopeMode.Assigned (on deskD)
        // - Member of ws and Desk D
        // - Caller does NOT have WorkItem.Review via Workstream or All.
        string callerUsername = "stale_review_caller";
        string callerPass = "StaleRevPass!123";
        Guid callerId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

            var roleView = new Role { Id = Guid.NewGuid(), Code = "ROLE_STALE_VIEW_WS", Name = "Role Stale View WS", IsSystemRole = false };
            var roleReview = new Role { Id = Guid.NewGuid(), Code = "ROLE_STALE_REV_ASSIGNED", Name = "Role Stale Review Assigned", IsSystemRole = false };
            db.Roles.AddRange(roleView, roleReview);

            var pView = await db.Permissions.FirstAsync(p => p.Code == PermissionCodes.WorkItemView);
            var pReview = await db.Permissions.FirstAsync(p => p.Code == PermissionCodes.WorkItemReview);

            db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = roleView.Id, PermissionId = pView.Id, ScopeMode = ScopeMode.Workstream });
            db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = roleReview.Id, PermissionId = pReview.Id, ScopeMode = ScopeMode.Assigned });

            await db.SaveChangesAsync();

            var createRes = await adminClient.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(
                Username: callerUsername,
                DisplayName: "Stale Review Caller",
                Password: callerPass,
                DesignationId: null,
                RoleIds: [roleView.Id, roleReview.Id],
                WorkstreamIds: [ws.Id],
                PrimaryWorkstreamId: ws.Id
            ));
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            callerId = (await createRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

            // Assign caller to Desk D (live active desk membership)
            var assignDeskRes = await adminClient.PostAsJsonAsync($"/api/admin/users/{callerId}/desks", new AssignDeskRequest(deskD.Id, IsPrimary: true));
            Assert.Equal(HttpStatusCode.Created, assignDeskRes.StatusCode);
        }

        var callerClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginRes = await callerClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(callerUsername, callerPass));
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        // Helper user for contributor submissions
        var (helperClient, helperId) = await CreateCustomUserClientAsync(
            "stale_helper_user",
            "ROLE_STALE_HELPER",
            ScopeMode.Assigned,
            [PermissionCodes.WorkItemView, PermissionCodes.WorkItemContribute],
            deskId: deskD.Id,
            workstreamId: ws.Id
        );

        // Other requester user (so caller is NOT requester)
        var (_, otherRequesterId) = await CreateCustomUserClientAsync(
            "stale_other_req",
            "ROLE_STALE_REQ",
            ScopeMode.Assigned,
            [PermissionCodes.WorkItemView],
            deskId: deskD.Id,
            workstreamId: ws.Id
        );

        // Item 1: Points to Desk D, has a Submitted contributor, but assignment IsActive = false
        Guid staleItemId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var item1 = new WorkItem
            {
                Id = Guid.NewGuid(),
                WorkstreamId = ws.Id,
                Title = "Stale Assignment Review Item",
                Priority = WorkItemPriority.Routine,
                Status = WorkItemStatus.Assigned,
                Origin = WorkItemOrigin.Manual,
                RequestedByUserId = otherRequesterId,
                RequestedByDisplayNameSnapshot = "Other Requester",
                Revision = 1,
                LastActivityAt = DateTimeOffset.UtcNow,
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var assignment1 = new WorkItemAssignment
            {
                Id = Guid.NewGuid(),
                WorkItemId = item1.Id,
                OfficeDeskId = deskD.Id,
                AssignedUserId = null,
                IsActive = false, // INACTIVE ASSIGNMENT
                AssignedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ClosedAt = DateTimeOffset.UtcNow,
                RecordStatus = RecordStatus.Active
            };
            var contributor1 = new WorkItemContributor
            {
                Id = Guid.NewGuid(),
                WorkItemId = item1.Id,
                UserId = helperId,
                Status = WorkItemContributorStatus.Submitted,
                IsActive = true,
                AddedByUserId = SeedData.BootstrapAdminId,
                AddedAt = DateTimeOffset.UtcNow.AddDays(-1),
                SubmittedAt = DateTimeOffset.UtcNow,
                RecordStatus = RecordStatus.Active
            };
            db.WorkItems.Add(item1);
            db.WorkItemAssignments.Add(assignment1);
            db.WorkItemContributors.Add(contributor1);
            await db.SaveChangesAsync();
            staleItemId = item1.Id;
        }

        // Item 1 verification:
        // Caller has WorkItem.View via Workstream, WorkItem.Review via Assigned on Desk D.
        // Item assignment is inactive, so Assigned review must NOT match.
        // Item must NOT appear in relationship=review and must NOT increment NeedsReview.
        var resMwStale = await callerClient.GetAsync("/api/work-items/my-work?relationship=review");
        Assert.Equal(HttpStatusCode.OK, resMwStale.StatusCode);
        var mwStale = await resMwStale.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.NotNull(mwStale);
        Assert.Equal(0, mwStale.Summary.NeedsReview);
        Assert.DoesNotContain(mwStale.Items, i => i.Id == staleItemId);

        // Item 2: Equivalent item with ACTIVE assignment on Desk D and a Submitted contributor
        Guid activeItemId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var item2 = new WorkItem
            {
                Id = Guid.NewGuid(),
                WorkstreamId = ws.Id,
                Title = "Active Assignment Review Item",
                Priority = WorkItemPriority.Routine,
                Status = WorkItemStatus.Assigned,
                Origin = WorkItemOrigin.Manual,
                RequestedByUserId = otherRequesterId,
                RequestedByDisplayNameSnapshot = "Other Requester",
                Revision = 1,
                LastActivityAt = DateTimeOffset.UtcNow,
                RecordStatus = RecordStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var assignment2 = new WorkItemAssignment
            {
                Id = Guid.NewGuid(),
                WorkItemId = item2.Id,
                OfficeDeskId = deskD.Id,
                AssignedUserId = null,
                IsActive = true, // ACTIVE ASSIGNMENT
                AssignedAt = DateTimeOffset.UtcNow,
                RecordStatus = RecordStatus.Active
            };
            var contributor2 = new WorkItemContributor
            {
                Id = Guid.NewGuid(),
                WorkItemId = item2.Id,
                UserId = helperId,
                Status = WorkItemContributorStatus.Submitted,
                IsActive = true,
                AddedByUserId = SeedData.BootstrapAdminId,
                AddedAt = DateTimeOffset.UtcNow.AddHours(-2),
                SubmittedAt = DateTimeOffset.UtcNow,
                RecordStatus = RecordStatus.Active
            };
            db.WorkItems.Add(item2);
            db.WorkItemAssignments.Add(assignment2);
            db.WorkItemContributors.Add(contributor2);
            await db.SaveChangesAsync();
            activeItemId = item2.Id;
        }

        // Item 2 verification:
        // With an active assignment, Assigned review matches Desk D.
        // It DOES appear in relationship=review and increments NeedsReview to 1.
        var resMwActive = await callerClient.GetAsync("/api/work-items/my-work?relationship=review");
        Assert.Equal(HttpStatusCode.OK, resMwActive.StatusCode);
        var mwActive = await resMwActive.Content.ReadFromJsonAsync<MyWorkResponseDto>(JsonOpts);
        Assert.NotNull(mwActive);
        Assert.Equal(1, mwActive.Summary.NeedsReview);
        Assert.Contains(mwActive.Items, i => i.Id == activeItemId);
        Assert.DoesNotContain(mwActive.Items, i => i.Id == staleItemId);
    }

    [Fact]
    public async Task Phase2FC_BranchPulse_Aggregates_CurrentDesk_And_Excludes_Terminal()
    {
        var client = await CreateAdminClientAsync(); var ws = await CreateWorkstreamAsync("PULSE-" + Guid.NewGuid().ToString("N")[..6], "Pulse"); var desk = await CreateDeskAsync("PD-" + Guid.NewGuid().ToString("N")[..6], "Pulse Desk", ws.Id);
        using (var scope = _factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); foreach (var status in new[] { WorkItemStatus.Assigned, WorkItemStatus.InProgress, WorkItemStatus.Completed }) { var item = new WorkItem { Id = Guid.NewGuid(), WorkstreamId = ws.Id, Title = status.ToString(), Status = status, Priority = WorkItemPriority.Urgent, RequestedByUserId = SeedData.BootstrapAdminId, RequestedByDisplayNameSnapshot = "Admin", Revision = 1, LastActivityAt = DateTimeOffset.UtcNow.AddDays(-8), RecordStatus = RecordStatus.Active }; db.Add(item); db.Add(new WorkItemAssignment { Id = Guid.NewGuid(), WorkItemId = item.Id, OfficeDeskId = desk.Id, IsActive = true, AssignedAt = DateTimeOffset.UtcNow, RecordStatus = RecordStatus.Active }); } await db.SaveChangesAsync(); }
        var response = await client.GetAsync("/api/work-items/branch-pulse?attention=open&staleDays=7"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal(2, json.RootElement.GetProperty("summary").GetProperty("open").GetInt32());
        var workload = json.RootElement.GetProperty("deskWorkloads").EnumerateArray().Single(x => x.GetProperty("officeDeskId").GetGuid() == desk.Id); Assert.Equal(2, workload.GetProperty("openCount").GetInt32()); Assert.Equal(2, workload.GetProperty("staleCount").GetInt32());
    }

    [Fact]
    public async Task Phase2FC_Reassign_Closes_Cycle_And_Emits_Immutable_Snapshots()
    {
        var client = await CreateAdminClientAsync(); var ws = await CreateWorkstreamAsync("ROUTE-" + Guid.NewGuid().ToString("N")[..6], "Routing"); var oldDesk = await CreateDeskAsync("OLD-" + Guid.NewGuid().ToString("N")[..6], "Court Desk", ws.Id); var newDesk = await CreateDeskAsync("NEW-" + Guid.NewGuid().ToString("N")[..6], "Land Records"); Guid id; Guid oldId;
        using (var scope = _factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var item = new WorkItem { Id = Guid.NewGuid(), WorkstreamId = ws.Id, Title = "Route", Status = WorkItemStatus.InProgress, RequestedByUserId = SeedData.BootstrapAdminId, RequestedByDisplayNameSnapshot = "Admin", Revision = 4, LastActivityAt = DateTimeOffset.UtcNow, RecordStatus = RecordStatus.Active }; var old = new WorkItemAssignment { Id = Guid.NewGuid(), WorkItemId = item.Id, OfficeDeskId = oldDesk.Id, IsActive = true, AssignedAt = DateTimeOffset.UtcNow, FirstSeenAt = DateTimeOffset.UtcNow, FirstActionAt = DateTimeOffset.UtcNow, RecordStatus = RecordStatus.Active }; db.Add(item); db.Add(old); await db.SaveChangesAsync(); id = item.Id; oldId = old.Id; }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/work-items/{id}/reassign", new ReassignWorkItemApiRequest(newDesk.Id, null, "Verification required", 4))).StatusCode);
        using var scope2 = _factory.Services.CreateScope(); var db2 = scope2.ServiceProvider.GetRequiredService<LacDbContext>(); var cycles = await db2.WorkItemAssignments.Where(a => a.WorkItemId == id).ToListAsync(); Assert.Equal(2, cycles.Count); Assert.False(cycles.Single(a => a.Id == oldId).IsActive); var current = cycles.Single(a => a.IsActive); Assert.Null(current.FirstSeenAt); Assert.Equal(newDesk.Id, current.OfficeDeskId); var audit = await db2.WorkItemEvents.SingleAsync(e => e.WorkItemId == id && e.Action == WorkItemEventAction.Reassigned); Assert.Equal(oldId, audit.SourceAssignmentId); Assert.Equal(current.Id, audit.TargetAssignmentId); Assert.Equal("Court Desk", audit.SourceDeskNameSnapshot); Assert.Equal("Land Records", audit.TargetDeskNameSnapshot);
    }

    [Fact]
    public async Task Phase2FC_BranchPulse_Attention_Uses_Derived_Open_States_Before_Pagination()
    {
        var client = await CreateAdminClientAsync(); var ws = await CreateWorkstreamAsync("ATTN-" + Guid.NewGuid().ToString("N")[..6], "Attention"); var desk = await CreateDeskAsync("ATD-" + Guid.NewGuid().ToString("N")[..6], "Attention Desk", ws.Id); var now = DateTimeOffset.UtcNow;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            foreach (var spec in new[] { ("Overdue", WorkItemStatus.Assigned, now.AddHours(-1), now.AddDays(-8)), ("Fresh", WorkItemStatus.InProgress, (DateTimeOffset?)null, now), ("Done", WorkItemStatus.Completed, now.AddHours(-1), now.AddDays(-8)) })
            { var item = new WorkItem { Id = Guid.NewGuid(), WorkstreamId = ws.Id, Title = spec.Item1, Status = spec.Item2, DueAt = spec.Item3, RequestedByUserId = SeedData.BootstrapAdminId, RequestedByDisplayNameSnapshot = "Admin", Revision = 1, LastActivityAt = spec.Item4, RecordStatus = RecordStatus.Active }; db.Add(item); db.Add(new WorkItemAssignment { Id = Guid.NewGuid(), WorkItemId = item.Id, OfficeDeskId = desk.Id, IsActive = true, AssignedAt = now, RecordStatus = RecordStatus.Active }); }
            await db.SaveChangesAsync();
        }
        var res = await client.GetAsync("/api/work-items/branch-pulse?attention=open&staleDays=7&pageSize=1"); Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()); var summary = json.RootElement.GetProperty("summary"); Assert.Equal(2, summary.GetProperty("open").GetInt32()); Assert.Equal(1, summary.GetProperty("overdue").GetInt32()); Assert.Equal(1, summary.GetProperty("stale").GetInt32()); Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32()); Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
    }
}

