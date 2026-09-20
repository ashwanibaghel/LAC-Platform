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
        Assert.DoesNotContain(desk2.Id, desks); // Desk 2 MUST NOT be leaked to WS1-scoped caller

        // 4. Scope All caller can query any active workstream
        var resAdminWs2 = await adminClient.GetAsync($"/api/work-items/assignment-options?workstreamId={ws2.Id}");
        Assert.Equal(HttpStatusCode.OK, resAdminWs2.StatusCode);
        var bodyAdminWs2 = await resAdminWs2.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var desksAdmin = bodyAdminWs2.GetProperty("desks").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(desk2.Id, desksAdmin);
        Assert.DoesNotContain(desk1.Id, desksAdmin);
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
}

