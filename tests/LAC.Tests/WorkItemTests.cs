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
}
