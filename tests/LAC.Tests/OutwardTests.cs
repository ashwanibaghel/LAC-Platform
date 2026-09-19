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

public sealed class OutwardTestFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"outward-tests-{Guid.NewGuid()}";
    public const string TestAdminUser = "outward_admin";
    public const string TestAdminPass = "OutwardAdminPass!123";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = TestAdminUser,
                ["BootstrapAdmin:Password"] = TestAdminPass,
                ["BootstrapAdmin:DisplayName"] = "Outward Test Administrator"
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

public sealed class OutwardTests : IClassFixture<OutwardTestFactory>
{
    private readonly OutwardTestFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly byte[] SamplePdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 sample content for outward testing");
    private static readonly byte[] SamplePngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
    private static readonly byte[] SampleJpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };

    public OutwardTests(OutwardTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(OutwardTestFactory.TestAdminUser, OutwardTestFactory.TestAdminPass));
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
        bool isDeskActive = true)
    {
        var adminClient = await CreateAdminClientAsync();

        var permCodes = new[]
        {
            PermissionCodes.OutwardView,
            PermissionCodes.OutwardCreate,
            PermissionCodes.OutwardEdit,
            PermissionCodes.OutwardDispatch,
            PermissionCodes.OutwardCancel,
            PermissionCodes.MatterView
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

        var userPass = "OutwardScopedPass!123";
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
                var m = await db.UserDeskMemberships.FirstAsync(x => x.UserId == userId && x.OfficeDeskId == deskId.Value);
                m.IsActive = false;
                await db.SaveChangesAsync();
            }
        }

        var userClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginRes = await userClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, userPass));
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        return (userClient, userId);
    }

    private static MultipartFormDataContent CreateRegisterForm(
        string outwardNumber,
        Guid issuingDeskId,
        string subject = "Official Outward Notice",
        string recipientName = "Collector Office",
        Guid? workstreamId = null,
        byte[]? fileBytes = null,
        string? fileName = null,
        Guid? existingDocumentId = null,
        Guid? matterId = null,
        Guid? primaryDakId = null,
        string contentType = "application/pdf")
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(outwardNumber), "outwardNumber");
        form.Add(new StringContent(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")), "outwardDate");
        form.Add(new StringContent(issuingDeskId.ToString()), "issuingDeskId");
        form.Add(new StringContent(subject), "subject");
        form.Add(new StringContent(recipientName), "recipientName");

        if (workstreamId.HasValue)
            form.Add(new StringContent(workstreamId.Value.ToString()), "workstreamId");

        if (matterId.HasValue)
            form.Add(new StringContent(matterId.Value.ToString()), "matterId");

        if (primaryDakId.HasValue)
            form.Add(new StringContent(primaryDakId.Value.ToString()), "primaryDakId");

        if (existingDocumentId.HasValue)
            form.Add(new StringContent(existingDocumentId.Value.ToString()), "existingDocumentId");

        if (fileBytes != null && fileName != null)
        {
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(fileContent, "file", fileName);
        }

        return form;
    }

    private async Task<(Guid MatterId, Guid DocumentId)> CreateSampleMatterWithDocumentAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var matter = new Matter
        {
            Id = Guid.NewGuid(),
            Title = $"WP(C) {Guid.NewGuid():N}"[..15],
            MatterType = "Court Case",
            Status = "Pending",
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Matters.Add(matter);

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            OriginalFileName = "matter_doc.pdf",
            StoragePath = $"documents/{Guid.NewGuid():N}.pdf",
            MimeType = "application/pdf",
            FileSize = 1024,
            Sha256Hash = "dummyhash",
            RecordStatus = RecordStatus.Active,
            UploadedAt = DateTimeOffset.UtcNow
        };
        db.Documents.Add(doc);

        var matterDoc = new MatterDocument
        {
            Id = Guid.NewGuid(),
            MatterId = matter.Id,
            DocumentId = doc.Id
        };
        db.MatterDocuments.Add(matterDoc);

        await db.SaveChangesAsync();
        return (matter.Id, doc.Id);
    }

    private async Task<(Guid DakId, Guid DocumentId)> CreateSampleDakWithDocumentAsync(HttpClient adminClient, string diaryNo)
    {
        var dakId = await CreateSampleDakAsync(adminClient, diaryNo);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var dak = await db.Daks.SingleAsync(d => d.Id == dakId);

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            OriginalFileName = "dak_doc.pdf",
            StoragePath = $"documents/{Guid.NewGuid():N}.pdf",
            MimeType = "application/pdf",
            FileSize = 1024,
            Sha256Hash = "dummyhash",
            RecordStatus = RecordStatus.Active,
            UploadedAt = DateTimeOffset.UtcNow
        };
        db.Documents.Add(doc);

        dak.MainDocumentId = doc.Id;
        await db.SaveChangesAsync();

        return (dakId, doc.Id);
    }

    // ========================================================================
    // CATEGORY 1: SCOPE & INSTITUTIONAL DESK AUTHORIZATION (Tests 1-8)
    // ========================================================================

    [Fact]
    public async Task Test01_Outward_View_ScopeModeAll_CanViewAnyDesk()
    {
        var desk1 = await CreateDeskAsync($"D1_{Guid.NewGuid():N}"[..10], "Desk 1");
        var desk2 = await CreateDeskAsync($"D2_{Guid.NewGuid():N}"[..10], "Desk 2");

        using var admin = await CreateAdminClientAsync();
        var num = $"OUT_ALL_{Guid.NewGuid():N}"[..14];
        using var regForm = CreateRegisterForm(num, desk2.Id);
        var regRes = await admin.PostAsync("/api/outward", regForm);
        Assert.Equal(HttpStatusCode.Created, regRes.StatusCode);
        var outId = (await regRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // User with ScopeMode.All assigned to desk1 can view outId belonging to desk2
        var (userClient, _) = await CreateScopedUserClientAsync($"user_all_{Guid.NewGuid():N}"[..12], "R_ALL", ScopeMode.All, desk1.Id);
        var viewRes = await userClient.GetAsync($"/api/outward/{outId}");
        Assert.Equal(HttpStatusCode.OK, viewRes.StatusCode);
    }

    [Fact]
    public async Task Test02_Outward_View_ScopeModeDesk_CanViewOnlyAssignedDesk()
    {
        var deskA = await CreateDeskAsync($"DA_{Guid.NewGuid():N}"[..10], "Desk A");
        var deskB = await CreateDeskAsync($"DB_{Guid.NewGuid():N}"[..10], "Desk B");

        using var admin = await CreateAdminClientAsync();
        var numA = $"OUT_DA_{Guid.NewGuid():N}"[..14];
        using var regFormA = CreateRegisterForm(numA, deskA.Id);
        var regResA = await admin.PostAsync("/api/outward", regFormA);
        Assert.Equal(HttpStatusCode.Created, regResA.StatusCode);
        var outA = (await regResA.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var numB = $"OUT_DB_{Guid.NewGuid():N}"[..14];
        using var regFormB = CreateRegisterForm(numB, deskB.Id);
        var regResB = await admin.PostAsync("/api/outward", regFormB);
        Assert.Equal(HttpStatusCode.Created, regResB.StatusCode);
        var outB = (await regResB.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var (userClient, _) = await CreateScopedUserClientAsync($"user_desk_{Guid.NewGuid():N}"[..12], "R_DESK", ScopeMode.Assigned, deskA.Id);

        // Can view deskA
        var resA = await userClient.GetAsync($"/api/outward/{outA}");
        Assert.Equal(HttpStatusCode.OK, resA.StatusCode);

        // Cannot view deskB -> 403
        var resB = await userClient.GetAsync($"/api/outward/{outB}");
        Assert.Equal(HttpStatusCode.Forbidden, resB.StatusCode);
    }

    [Fact]
    public async Task Test03_Outward_View_ScopeModeWorkstream_CanViewOnlyMatchingWorkstream()
    {
        var ws1 = await CreateWorkstreamAsync($"WS1_{Guid.NewGuid():N}"[..8], "Workstream 1");
        var ws2 = await CreateWorkstreamAsync($"WS2_{Guid.NewGuid():N}"[..8], "Workstream 2");
        var desk = await CreateDeskAsync($"D_WS_{Guid.NewGuid():N}"[..10], "Desk WS", ws1.Id);

        using var admin = await CreateAdminClientAsync();
        var num1 = $"OUT_WS1_{Guid.NewGuid():N}"[..14];
        using var form1 = CreateRegisterForm(num1, desk.Id, workstreamId: ws1.Id);
        var res1 = await admin.PostAsync("/api/outward", form1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        var out1 = (await res1.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var num2 = $"OUT_WS2_{Guid.NewGuid():N}"[..14];
        using var form2 = CreateRegisterForm(num2, desk.Id, workstreamId: ws2.Id);
        var res2 = await admin.PostAsync("/api/outward", form2);
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        var out2 = (await res2.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var (userClient, _) = await CreateScopedUserClientAsync($"user_ws_{Guid.NewGuid():N}"[..12], "R_WS", ScopeMode.Workstream, desk.Id, ws1.Id);

        var get1 = await userClient.GetAsync($"/api/outward/{out1}");
        Assert.Equal(HttpStatusCode.OK, get1.StatusCode);

        var get2 = await userClient.GetAsync($"/api/outward/{out2}");
        Assert.Equal(HttpStatusCode.Forbidden, get2.StatusCode);
    }

    [Fact]
    public async Task Test04_Outward_View_ScopeModeOwn_FailsClosed()
    {
        var desk = await CreateDeskAsync($"D_OWN_{Guid.NewGuid():N}"[..10], "Desk Own");

        using var admin = await CreateAdminClientAsync();
        var num = $"OUT_OWN_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // ScopeMode.Own fails closed for Outward
        var (userClient, _) = await CreateScopedUserClientAsync($"user_own_{Guid.NewGuid():N}"[..12], "R_OWN", ScopeMode.Own, desk.Id);
        var getRes = await userClient.GetAsync($"/api/outward/{outId}");
        Assert.Equal(HttpStatusCode.Forbidden, getRes.StatusCode);
    }

    [Fact]
    public async Task Test05_Outward_Create_ScopeModeAll_RequiresActiveIssuingDeskMembership()
    {
        var deskMember = await CreateDeskAsync($"D_MEM_{Guid.NewGuid():N}"[..10], "Desk Member");
        var deskOther = await CreateDeskAsync($"D_OTH_{Guid.NewGuid():N}"[..10], "Desk Other");

        var (userClient, _) = await CreateScopedUserClientAsync($"user_call_{Guid.NewGuid():N}"[..12], "R_CALL", ScopeMode.All, deskMember.Id);

        // Allowed to create on deskMember
        var num1 = $"OUT_OK_{Guid.NewGuid():N}"[..14];
        using var form1 = CreateRegisterForm(num1, deskMember.Id);
        var res1 = await userClient.PostAsync("/api/outward", form1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Disallowed to create on deskOther even with ScopeMode.All -> 403
        var num2 = $"OUT_FAIL_{Guid.NewGuid():N}"[..14];
        using var form2 = CreateRegisterForm(num2, deskOther.Id);
        var res2 = await userClient.PostAsync("/api/outward", form2);
        Assert.Equal(HttpStatusCode.Forbidden, res2.StatusCode);
    }

    [Fact]
    public async Task Test06_Outward_Create_ScopeModeDesk_RequiresActiveIssuingDeskMembership()
    {
        var deskMember = await CreateDeskAsync($"D_DMEM_{Guid.NewGuid():N}"[..10], "Desk DMember");
        var deskOther = await CreateDeskAsync($"D_DOTH_{Guid.NewGuid():N}"[..10], "Desk DOther");

        var (userClient, _) = await CreateScopedUserClientAsync($"user_cdesk_{Guid.NewGuid():N}"[..12], "R_CDESK", ScopeMode.Assigned, deskMember.Id);

        var num1 = $"OUT_DOK_{Guid.NewGuid():N}"[..14];
        using var form1 = CreateRegisterForm(num1, deskMember.Id);
        var res1 = await userClient.PostAsync("/api/outward", form1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        var num2 = $"OUT_DFAIL_{Guid.NewGuid():N}"[..14];
        using var form2 = CreateRegisterForm(num2, deskOther.Id);
        var res2 = await userClient.PostAsync("/api/outward", form2);
        Assert.Equal(HttpStatusCode.Forbidden, res2.StatusCode);
    }

    [Fact]
    public async Task Test07_Outward_Create_ScopeModeOwn_FailsClosed()
    {
        var desk = await CreateDeskAsync($"D_COWN_{Guid.NewGuid():N}"[..10], "Desk COwn");
        var (userClient, _) = await CreateScopedUserClientAsync($"user_cown_{Guid.NewGuid():N}"[..12], "R_COWN", ScopeMode.Own, desk.Id);

        var num = $"OUT_COWN_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await userClient.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Test08_Outward_Create_InactiveDeskMembership_Rejected()
    {
        var desk = await CreateDeskAsync($"D_INACT_{Guid.NewGuid():N}"[..10], "Desk Inactive");
        var (userClient, _) = await CreateScopedUserClientAsync($"user_inact_{Guid.NewGuid():N}"[..12], "R_INACT", ScopeMode.Assigned, desk.Id, isDeskActive: false);

        var num = $"OUT_INACT_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await userClient.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // ========================================================================
    // CATEGORY 2: REGISTRATION & NUMBER RESERVATION (Tests 9-14)
    // ========================================================================

    [Fact]
    public async Task Test09_Outward_Register_GeneratesSequentialEvent_Sequence1()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_SEQ_{Guid.NewGuid():N}"[..10], "Desk Seq");

        var num = $"OUT_SEQ_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var ev = await db.OutwardEvents.SingleAsync(e => e.OutwardId == outId);
        Assert.Equal(1, ev.SequenceNumber);
        Assert.Equal(OutwardEventAction.Registered, ev.Action);
    }

    [Fact]
    public async Task Test10_Outward_Register_NormalizesOutwardNumber_CaseInsensitive()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_NORM_{Guid.NewGuid():N}"[..10], "Desk Norm");

        var rawNum = $"  lac/out/2026/001  ";
        using var form = CreateRegisterForm(rawNum, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var record = await db.Outwards.SingleAsync(o => o.Id == outId);
        Assert.Equal("LAC/OUT/2026/001", record.NormalizedOutwardNumber);
    }

    [Fact]
    public async Task Test11_Outward_Register_DuplicateNormalizedNumber_RejectedWithConflict()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DUP_{Guid.NewGuid():N}"[..10], "Desk Dup");

        var num1 = $"LAC/DUP/2026/999";
        using var form1 = CreateRegisterForm(num1, desk.Id);
        var res1 = await admin.PostAsync("/api/outward", form1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Exact same normalized number with different casing and whitespace
        var num2 = $"  lac/dup/2026/999  ";
        using var form2 = CreateRegisterForm(num2, desk.Id);
        var res2 = await admin.PostAsync("/api/outward", form2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
    }

    [Fact]
    public async Task Test12_Outward_Register_WithDocument_CreatesDocumentAndEventSnapshot()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DOC_{Guid.NewGuid():N}"[..10], "Desk Doc");

        var num = $"OUT_DOC_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "signed_order.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var outward = await db.Outwards.Include(o => o.MainDocument).SingleAsync(o => o.Id == outId);
        Assert.NotNull(outward.MainDocumentId);
        Assert.Equal("signed_order.pdf", outward.MainDocument!.OriginalFileName);

        var ev = await db.OutwardEvents.SingleAsync(e => e.OutwardId == outId);
        Assert.Equal(outward.MainDocumentId, ev.DocumentId);
    }

    [Fact]
    public async Task Test13_Outward_Register_WithExistingDocument_ReusesPhysicalFile()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_REUSE_{Guid.NewGuid():N}"[..10], "Desk Reuse");

        // First outward with new file
        var num1 = $"OUT_ORIG_{Guid.NewGuid():N}"[..14];
        using var form1 = CreateRegisterForm(num1, desk.Id, fileBytes: SamplePdfBytes, fileName: "original.pdf");
        var res1 = await admin.PostAsync("/api/outward", form1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        var out1 = (await res1.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        Guid existingDocId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            existingDocId = (await db.Outwards.SingleAsync(o => o.Id == out1)).MainDocumentId!.Value;
        }

        // Second outward reusing existingDocId
        var num2 = $"OUT_REUSE_{Guid.NewGuid():N}"[..14];
        using var form2 = CreateRegisterForm(num2, desk.Id, existingDocumentId: existingDocId);
        var res2 = await admin.PostAsync("/api/outward", form2);
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        var out2 = (await res2.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var o2 = await db.Outwards.SingleAsync(o => o.Id == out2);
            Assert.Equal(existingDocId, o2.MainDocumentId);
        }
    }

    [Fact]
    public async Task Test14_Outward_Register_SetsInitialStatusRegistered_Revision0()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_INIT_{Guid.NewGuid():N}"[..10], "Desk Init");

        var num = $"OUT_INIT_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        Assert.Equal("Registered", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("revision").GetInt32());
    }

    // ========================================================================
    // CATEGORY 3: DOCUMENT UPLOAD, REPLACE & AUTHORIZATION (Tests 15-22)
    // ========================================================================

    [Fact]
    public async Task Test15_Outward_MainDocument_UploadNew_IncrementsRevision_AppendsEvent()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_UPL_{Guid.NewGuid():N}"[..10], "Desk Upl");

        var num = $"OUT_UPL_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Upload main doc
        var uploadForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SamplePdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        uploadForm.Add(fileContent, "file", "new_main.pdf");
        uploadForm.Add(new StringContent("0"), "expectedRevision");

        var putRes = await admin.PutAsync($"/api/outward/{outId}/document", uploadForm);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var outward = await db.Outwards.SingleAsync(o => o.Id == outId);
        Assert.Equal(1, outward.Revision);

        var events = await db.OutwardEvents.Where(e => e.OutwardId == outId).OrderBy(e => e.SequenceNumber).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.Equal(OutwardEventAction.MainDocumentChanged, events[1].Action);
        Assert.Equal(outward.MainDocumentId, events[1].DocumentId);
    }

    [Fact]
    public async Task Test16_Outward_MainDocument_Replace_UpdatesPointer_AppendsEvent()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_REP_{Guid.NewGuid():N}"[..10], "Desk Rep");

        var num = $"OUT_REP_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "v1.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Replace with v2
        var uploadForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SamplePdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        uploadForm.Add(fileContent, "file", "v2.pdf");
        uploadForm.Add(new StringContent("0"), "expectedRevision");

        var putRes = await admin.PutAsync($"/api/outward/{outId}/document", uploadForm);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var outward = await db.Outwards.Include(o => o.MainDocument).SingleAsync(o => o.Id == outId);
        Assert.Equal("v2.pdf", outward.MainDocument!.OriginalFileName);
        Assert.Equal(1, outward.Revision);
    }

    [Fact]
    public async Task Test17_Outward_MainDocument_ReuseExistingDocument_ValidatesAuthorization()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_EXAUTH_{Guid.NewGuid():N}"[..10], "Desk ExAuth");

        var num1 = $"OUT_EX1_{Guid.NewGuid():N}"[..14];
        using var form1 = CreateRegisterForm(num1, desk.Id, fileBytes: SamplePdfBytes, fileName: "auth_doc.pdf");
        var res1 = await admin.PostAsync("/api/outward", form1);
        var out1 = (await res1.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        Guid docId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            docId = (await db.Outwards.SingleAsync(o => o.Id == out1)).MainDocumentId!.Value;
        }

        // Valid reuse via JSON payload
        var num2 = $"OUT_EX2_{Guid.NewGuid():N}"[..14];
        using var form2 = CreateRegisterForm(num2, desk.Id);
        var res2 = await admin.PostAsync("/api/outward", form2);
        var out2 = (await res2.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var reuseReq = new { existingDocumentId = docId, expectedRevision = 0 };
        var putRes = await admin.PutAsJsonAsync($"/api/outward/{out2}/document", reuseReq);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
    }

    [Fact]
    public async Task Test18_Outward_MainDocument_MagicBytesMismatch_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_MAGIC_{Guid.NewGuid():N}"[..10], "Desk Magic");

        var num = $"OUT_MAGIC_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Fake PDF with invalid magic bytes
        var uploadForm = new MultipartFormDataContent();
        var badBytes = Encoding.UTF8.GetBytes("THIS IS NOT A VALID PDF FILE HEADER");
        var fileContent = new ByteArrayContent(badBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        uploadForm.Add(fileContent, "file", "corrupt.pdf");
        uploadForm.Add(new StringContent("0"), "expectedRevision");

        var putRes = await admin.PutAsync($"/api/outward/{outId}/document", uploadForm);
        Assert.Equal(HttpStatusCode.BadRequest, putRes.StatusCode);
    }

    [Fact]
    public async Task Test19_Outward_MainDocument_ZeroBytesOrWhitespaceFileName_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_ZERO_{Guid.NewGuid():N}"[..10], "Desk Zero");

        var num = $"OUT_ZERO_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Zero bytes
        var uploadForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Array.Empty<byte>());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        uploadForm.Add(fileContent, "file", "empty.pdf");
        uploadForm.Add(new StringContent("0"), "expectedRevision");

        var putRes = await admin.PutAsync($"/api/outward/{outId}/document", uploadForm);
        Assert.Equal(HttpStatusCode.BadRequest, putRes.StatusCode);
    }

    [Fact]
    public async Task Test20_Outward_MainDocument_DispatchedOutward_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DISPDOC_{Guid.NewGuid():N}"[..10], "Desk DispDoc");

        var num = $"OUT_DD_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "before_dispatch.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Dispatch outward (requires main document — supplied at registration)
        var dispReq = new { dispatchDate = "2026-09-19", dispatchMode = "SpeedPost", expectedRevision = 0 };
        var dispRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);
        Assert.Equal(HttpStatusCode.OK, dispRes.StatusCode);

        // Attempt document mutation on dispatched outward -> Conflict 409
        var uploadForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SamplePdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        uploadForm.Add(fileContent, "file", "post_dispatch.pdf");
        uploadForm.Add(new StringContent("1"), "expectedRevision");

        var putRes = await admin.PutAsync($"/api/outward/{outId}/document", uploadForm);
        Assert.Equal(HttpStatusCode.Conflict, putRes.StatusCode);
    }

    [Fact]
    public async Task Test21_Outward_MainDocument_CancelledOutward_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_CANCDOC_{Guid.NewGuid():N}"[..10], "Desk CancDoc");

        var num = $"OUT_CD_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Cancel outward
        var cancReq = new { cancellationReason = "Order rescinded by department", expectedRevision = 0 };
        var cancRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/cancel", cancReq);
        Assert.Equal(HttpStatusCode.OK, cancRes.StatusCode);

        // Attempt document mutation -> Conflict 409
        var uploadForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SamplePdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        uploadForm.Add(fileContent, "file", "post_cancel.pdf");
        uploadForm.Add(new StringContent("1"), "expectedRevision");

        var putRes = await admin.PutAsync($"/api/outward/{outId}/document", uploadForm);
        Assert.Equal(HttpStatusCode.Conflict, putRes.StatusCode);
    }

    [Fact]
    public async Task Test22_Outward_MainDocument_ContentDownload_HonorsOutwardViewAuthorization()
    {
        var desk1 = await CreateDeskAsync($"D_DOWN1_{Guid.NewGuid():N}"[..10], "Desk Down 1");
        var desk2 = await CreateDeskAsync($"D_DOWN2_{Guid.NewGuid():N}"[..10], "Desk Down 2");

        using var admin = await CreateAdminClientAsync();
        var num = $"OUT_DOWN_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk1.Id, fileBytes: SamplePdfBytes, fileName: "viewable.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // User on desk1 can download
        var (client1, _) = await CreateScopedUserClientAsync($"user_d1_{Guid.NewGuid():N}"[..12], "R_D1", ScopeMode.Assigned, desk1.Id);
        var down1 = await client1.GetAsync($"/api/outward/{outId}/document/content");
        Assert.Equal(HttpStatusCode.OK, down1.StatusCode);

        // User on desk2 cannot download -> 403
        var (client2, _) = await CreateScopedUserClientAsync($"user_d2_{Guid.NewGuid():N}"[..12], "R_D2", ScopeMode.Assigned, desk2.Id);
        var down2 = await client2.GetAsync($"/api/outward/{outId}/document/content");
        Assert.Equal(HttpStatusCode.Forbidden, down2.StatusCode);
    }

    // ========================================================================
    // CATEGORY 4: ATTACHMENTS MANAGEMENT (Tests 23-30)
    // ========================================================================

    [Fact]
    public async Task Test23_Outward_Attachment_AddNew_IncrementsRevision_AppendsEvent()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_ATT1_{Guid.NewGuid():N}"[..10], "Desk Att1");

        var num = $"OUT_ATT1_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var attForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SamplePdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        attForm.Add(fileContent, "file", "annexure_a.pdf");
        attForm.Add(new StringContent("Annexure A"), "title");
        attForm.Add(new StringContent("Annexure"), "attachmentType");
        attForm.Add(new StringContent("0"), "expectedRevision");

        var postRes = await admin.PostAsync($"/api/outward/{outId}/attachments", attForm);
        Assert.Equal(HttpStatusCode.Created, postRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var outward = await db.Outwards.Include(o => o.Attachments).SingleAsync(o => o.Id == outId);
        Assert.Equal(1, outward.Revision);
        Assert.Single(outward.Attachments);

        var ev = await db.OutwardEvents.OrderByDescending(e => e.SequenceNumber).FirstAsync(e => e.OutwardId == outId);
        Assert.Equal(OutwardEventAction.AttachmentAdded, ev.Action);
    }

    [Fact]
    public async Task Test24_Outward_Attachment_AddExistingDocument_ZeroPhysicalCopy()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_ATTEX_{Guid.NewGuid():N}"[..10], "Desk AttEx");

        var num1 = $"OUT_ATTEX1_{Guid.NewGuid():N}"[..14];
        using var form1 = CreateRegisterForm(num1, desk.Id, fileBytes: SamplePdfBytes, fileName: "shared_evidence.pdf");
        var res1 = await admin.PostAsync("/api/outward", form1);
        var out1 = (await res1.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        Guid docId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            docId = (await db.Outwards.SingleAsync(o => o.Id == out1)).MainDocumentId!.Value;
        }

        var num2 = $"OUT_ATTEX2_{Guid.NewGuid():N}"[..14];
        using var form2 = CreateRegisterForm(num2, desk.Id);
        var res2 = await admin.PostAsync("/api/outward", form2);
        var out2 = (await res2.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var req = new
        {
            existingDocumentId = docId,
            title = "Shared Evidence Copy",
            attachmentType = "Annexure",
            expectedRevision = 0
        };
        var postRes = await admin.PostAsJsonAsync($"/api/outward/{out2}/attachments", req);
        Assert.Equal(HttpStatusCode.Created, postRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var att = await db.OutwardAttachments.SingleAsync(a => a.OutwardId == out2);
            Assert.Equal(docId, att.DocumentId);
        }
    }

    [Fact]
    public async Task Test25_Outward_Attachment_DuplicateDocument_RejectedWithConflict()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_ATTDUP_{Guid.NewGuid():N}"[..10], "Desk AttDup");

        var num = $"OUT_ATTDUP_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "original_file.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        Guid docId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            docId = (await db.Outwards.SingleAsync(o => o.Id == outId)).MainDocumentId!.Value;
        }

        // Add attachment pointing to docId
        var req1 = new { existingDocumentId = docId, title = "Att 1", attachmentType = "Annexure", expectedRevision = 0 };
        var postRes1 = await admin.PostAsJsonAsync($"/api/outward/{outId}/attachments", req1);
        Assert.Equal(HttpStatusCode.Created, postRes1.StatusCode);

        // Attempt duplicate attachment pointing to same docId -> Conflict 409
        var req2 = new { existingDocumentId = docId, title = "Att 2", attachmentType = "Annexure", expectedRevision = 1 };
        var postRes2 = await admin.PostAsJsonAsync($"/api/outward/{outId}/attachments", req2);
        Assert.Equal(HttpStatusCode.Conflict, postRes2.StatusCode);
    }

    [Fact]
    public async Task Test26_Outward_Attachment_Remove_SoftDeletes_AppendsEvent()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_ATTREM_{Guid.NewGuid():N}"[..10], "Desk AttRem");

        var num = $"OUT_ATTREM_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var attForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SamplePdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        attForm.Add(fileContent, "file", "to_delete.pdf");
        attForm.Add(new StringContent("To Delete"), "title");
        attForm.Add(new StringContent("Annexure"), "attachmentType");
        attForm.Add(new StringContent("0"), "expectedRevision");

        var postRes = await admin.PostAsync($"/api/outward/{outId}/attachments", attForm);
        var attId = (await postRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Delete attachment
        var delRes = await admin.DeleteAsync($"/api/outward/{outId}/attachments/{attId}?expectedRevision=1");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var att = await db.OutwardAttachments.SingleAsync(a => a.Id == attId);
        Assert.Equal(RecordStatus.Archived, att.RecordStatus);

        var outward = await db.Outwards.SingleAsync(o => o.Id == outId);
        Assert.Equal(2, outward.Revision);

        var ev = await db.OutwardEvents.OrderByDescending(e => e.SequenceNumber).FirstAsync(e => e.OutwardId == outId);
        Assert.Equal(OutwardEventAction.AttachmentRemoved, ev.Action);
    }

    [Fact]
    public async Task Test27_Outward_Attachment_RemoveNonExistent_NotFound()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_ATTNO_{Guid.NewGuid():N}"[..10], "Desk AttNo");

        var num = $"OUT_ATTNO_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var delRes = await admin.DeleteAsync($"/api/outward/{outId}/attachments/{Guid.NewGuid()}?expectedRevision=0");
        Assert.Equal(HttpStatusCode.NotFound, delRes.StatusCode);
    }

    [Fact]
    public async Task Test28_Outward_Attachment_DispatchedOutward_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_ATTDISP_{Guid.NewGuid():N}"[..10], "Desk AttDisp");

        var num = $"OUT_ATTD_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Dispatch
        var dispReq = new { dispatchDate = "2026-09-19", dispatchMode = "SpeedPost", expectedRevision = 0 };
        await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);

        // Attempt attachment upload -> 409 Conflict
        var attForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SamplePdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        attForm.Add(fileContent, "file", "blocked.pdf");
        attForm.Add(new StringContent("Blocked"), "title");
        attForm.Add(new StringContent("Annexure"), "attachmentType");
        attForm.Add(new StringContent("1"), "expectedRevision");

        var postRes = await admin.PostAsync($"/api/outward/{outId}/attachments", attForm);
        Assert.Equal(HttpStatusCode.Conflict, postRes.StatusCode);
    }

    [Fact]
    public async Task Test29_Outward_Attachment_CancelledOutward_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_ATTCANC_{Guid.NewGuid():N}"[..10], "Desk AttCanc");

        var num = $"OUT_ATTC_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Cancel
        var cancReq = new { cancellationReason = "Cancelled dispatch", expectedRevision = 0 };
        await admin.PostAsJsonAsync($"/api/outward/{outId}/cancel", cancReq);

        // Attempt attachment upload -> 409 Conflict
        var attForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SamplePdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        attForm.Add(fileContent, "file", "blocked_canc.pdf");
        attForm.Add(new StringContent("Blocked"), "title");
        attForm.Add(new StringContent("Annexure"), "attachmentType");
        attForm.Add(new StringContent("1"), "expectedRevision");

        var postRes = await admin.PostAsync($"/api/outward/{outId}/attachments", attForm);
        Assert.Equal(HttpStatusCode.Conflict, postRes.StatusCode);
    }

    [Fact]
    public async Task Test30_Outward_Attachment_ContentDownload_HonorsOutwardViewAuthorization()
    {
        var desk1 = await CreateDeskAsync($"D_ATTDN1_{Guid.NewGuid():N}"[..10], "Desk AttDn1");
        var desk2 = await CreateDeskAsync($"D_ATTDN2_{Guid.NewGuid():N}"[..10], "Desk AttDn2");

        using var admin = await CreateAdminClientAsync();
        var num = $"OUT_ATTDN_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk1.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var attForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SamplePdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        attForm.Add(fileContent, "file", "att_auth.pdf");
        attForm.Add(new StringContent("Att Auth"), "title");
        attForm.Add(new StringContent("Annexure"), "attachmentType");
        attForm.Add(new StringContent("0"), "expectedRevision");

        var postRes = await admin.PostAsync($"/api/outward/{outId}/attachments", attForm);
        var attId = (await postRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // User on desk1 can download
        var (client1, _) = await CreateScopedUserClientAsync($"user_ad1_{Guid.NewGuid():N}"[..12], "R_AD1", ScopeMode.Assigned, desk1.Id);
        var down1 = await client1.GetAsync($"/api/outward/{outId}/attachments/{attId}/content");
        Assert.Equal(HttpStatusCode.OK, down1.StatusCode);

        // User on desk2 cannot download -> 403
        var (client2, _) = await CreateScopedUserClientAsync($"user_ad2_{Guid.NewGuid():N}"[..12], "R_AD2", ScopeMode.Assigned, desk2.Id);
        var down2 = await client2.GetAsync($"/api/outward/{outId}/attachments/{attId}/content");
        Assert.Equal(HttpStatusCode.Forbidden, down2.StatusCode);
    }

    // ========================================================================
    // CATEGORY 5: DAK LINKS & PRIMARY REPLY (Tests 31-38)
    // ========================================================================

    private async Task<Guid> CreateSampleDakAsync(HttpClient adminClient, string diaryNo)
    {
        var dakForm = new MultipartFormDataContent();
        dakForm.Add(new StringContent(diaryNo), "diaryNumber");
        dakForm.Add(new StringContent(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")), "receivedDate");
        dakForm.Add(new StringContent("Dak Subject"), "subject");
        dakForm.Add(new StringContent("Sender Name"), "senderName");
        dakForm.Add(new StringContent("Physical / By Hand"), "inwardMode");
        dakForm.Add(new StringContent("Routine"), "priority");

        var res = await adminClient.PostAsync("/api/dak", dakForm);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    [Fact]
    public async Task Test31_Outward_DakLink_Add_DefaultsIsPrimaryFalse_AppendsEvent()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DAKL1_{Guid.NewGuid():N}"[..10], "Desk DakL1");

        var dakId = await CreateSampleDakAsync(admin, $"DAK_L1_{Guid.NewGuid():N}"[..14]);

        var num = $"OUT_DL1_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var req = new
        {
            dakId,
            relationshipType = "Reference",
            isPrimary = false,
            expectedRevision = 0
        };
        var linkRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dak-links", req);
        Assert.Equal(HttpStatusCode.Created, linkRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var link = await db.OutwardDakLinks.SingleAsync(l => l.OutwardId == outId);
        Assert.False(link.IsPrimary);
        Assert.Equal("Reference", link.RelationshipType);

        var ev = await db.OutwardEvents.OrderByDescending(e => e.SequenceNumber).FirstAsync(e => e.OutwardId == outId);
        Assert.Equal(OutwardEventAction.DakLinkAdded, ev.Action);
    }

    [Fact]
    public async Task Test32_Outward_DakLink_AddPrimary_Succeeds()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DAKL2_{Guid.NewGuid():N}"[..10], "Desk DakL2");

        var dakId = await CreateSampleDakAsync(admin, $"DAK_L2_{Guid.NewGuid():N}"[..14]);

        var num = $"OUT_DL2_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var req = new
        {
            dakId,
            relationshipType = "PrimaryReply",
            isPrimary = true,
            expectedRevision = 0
        };
        var linkRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dak-links", req);
        Assert.Equal(HttpStatusCode.Created, linkRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var link = await db.OutwardDakLinks.SingleAsync(l => l.OutwardId == outId);
        Assert.True(link.IsPrimary);
    }

    [Fact]
    public async Task Test33_Outward_DakLink_SecondPrimary_RejectedWithConflict()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DAKL3_{Guid.NewGuid():N}"[..10], "Desk DakL3");

        var dak1 = await CreateSampleDakAsync(admin, $"DAK_P1_{Guid.NewGuid():N}"[..14]);
        var dak2 = await CreateSampleDakAsync(admin, $"DAK_P2_{Guid.NewGuid():N}"[..14]);

        var num = $"OUT_DL3_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // First primary link
        var req1 = new { dakId = dak1, relationshipType = "PrimaryReply", isPrimary = true, expectedRevision = 0 };
        var res1 = await admin.PostAsJsonAsync($"/api/outward/{outId}/dak-links", req1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Second primary link -> Conflict 409
        var req2 = new { dakId = dak2, relationshipType = "PrimaryReply", isPrimary = true, expectedRevision = 1 };
        var res2 = await admin.PostAsJsonAsync($"/api/outward/{outId}/dak-links", req2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
    }

    [Fact]
    public async Task Test34_Outward_DakLink_DuplicateActiveLink_RejectedWithConflict()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DAKL4_{Guid.NewGuid():N}"[..10], "Desk DakL4");

        var dak = await CreateSampleDakAsync(admin, $"DAK_D4_{Guid.NewGuid():N}"[..14]);

        var num = $"OUT_DL4_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var req1 = new { dakId = dak, relationshipType = "Reference", isPrimary = false, expectedRevision = 0 };
        var res1 = await admin.PostAsJsonAsync($"/api/outward/{outId}/dak-links", req1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Link same dak again -> Conflict 409
        var req2 = new { dakId = dak, relationshipType = "RelatedPetition", isPrimary = false, expectedRevision = 1 };
        var res2 = await admin.PostAsJsonAsync($"/api/outward/{outId}/dak-links", req2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
    }

    [Fact]
    public async Task Test35_Outward_DakLink_InvalidRelationshipType_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DAKL5_{Guid.NewGuid():N}"[..10], "Desk DakL5");

        var dak = await CreateSampleDakAsync(admin, $"DAK_INV_{Guid.NewGuid():N}"[..14]);

        var num = $"OUT_DL5_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var req = new { dakId = dak, relationshipType = "InvalidTypeXYZ", isPrimary = false, expectedRevision = 0 };
        var linkRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dak-links", req);
        Assert.Equal(HttpStatusCode.BadRequest, linkRes.StatusCode);
    }

    [Fact]
    public async Task Test36_Outward_DakLink_Remove_SoftDeletes_AppendsEvent()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DAKL6_{Guid.NewGuid():N}"[..10], "Desk DakL6");

        var dak = await CreateSampleDakAsync(admin, $"DAK_REM_{Guid.NewGuid():N}"[..14]);

        var num = $"OUT_DL6_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var req = new { dakId = dak, relationshipType = "Reference", isPrimary = false, expectedRevision = 0 };
        var linkRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dak-links", req);
        var linkId = (await linkRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var delRes = await admin.DeleteAsync($"/api/outward/{outId}/dak-links/{linkId}?expectedRevision=1");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var link = await db.OutwardDakLinks.SingleAsync(l => l.Id == linkId);
        Assert.Equal(RecordStatus.Archived, link.RecordStatus);

        var ev = await db.OutwardEvents.OrderByDescending(e => e.SequenceNumber).FirstAsync(e => e.OutwardId == outId);
        Assert.Equal(OutwardEventAction.DakLinkRemoved, ev.Action);
    }

    [Fact]
    public async Task Test37_Outward_DakLink_DispatchedOutward_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DAKL7_{Guid.NewGuid():N}"[..10], "Desk DakL7");

        var dak = await CreateSampleDakAsync(admin, $"DAK_DD7_{Guid.NewGuid():N}"[..14]);

        var num = $"OUT_DL7_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Dispatch
        var dispReq = new { dispatchDate = "2026-09-19", dispatchMode = "SpeedPost", expectedRevision = 0 };
        await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);

        // Attempt Dak link -> 409 Conflict
        var req = new { dakId = dak, relationshipType = "Reference", isPrimary = false, expectedRevision = 1 };
        var linkRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dak-links", req);
        Assert.Equal(HttpStatusCode.Conflict, linkRes.StatusCode);
    }

    [Fact]
    public async Task Test38_Outward_DakLink_CancelledOutward_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DAKL8_{Guid.NewGuid():N}"[..10], "Desk DakL8");

        var dak = await CreateSampleDakAsync(admin, $"DAK_CD8_{Guid.NewGuid():N}"[..14]);

        var num = $"OUT_DL8_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Cancel
        var cancReq = new { cancellationReason = "Revoked", expectedRevision = 0 };
        await admin.PostAsJsonAsync($"/api/outward/{outId}/cancel", cancReq);

        // Attempt Dak link -> 409 Conflict
        var req = new { dakId = dak, relationshipType = "Reference", isPrimary = false, expectedRevision = 1 };
        var linkRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dak-links", req);
        Assert.Equal(HttpStatusCode.Conflict, linkRes.StatusCode);
    }

    // ========================================================================
    // CATEGORY 6: LEGAL MATTER ASSOCIATION (Tests 39-43)
    // ========================================================================

    private async Task<Guid> CreateSampleMatterAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var matter = new Matter
        {
            Id = Guid.NewGuid(),
            Title = "WP(C) 1234/2026",
            MatterType = "Court Case",
            Status = "Pending",
            RecordStatus = RecordStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Matters.Add(matter);
        await db.SaveChangesAsync();
        return matter.Id;
    }

    [Fact]
    public async Task Test39_Outward_MatterLink_ValidActiveMatter_Succeeds()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_MAT1_{Guid.NewGuid():N}"[..10], "Desk Mat1");
        var matterId = await CreateSampleMatterAsync();

        var num = $"OUT_MAT1_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, matterId: matterId);
        var res = await admin.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var outward = await db.Outwards.SingleAsync(o => o.Id == outId);
        Assert.Equal(matterId, outward.MatterId);
    }

    [Fact]
    public async Task Test40_Outward_MatterLink_NonExistentMatter_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_MAT2_{Guid.NewGuid():N}"[..10], "Desk Mat2");

        var num = $"OUT_MAT2_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, matterId: Guid.NewGuid());
        var res = await admin.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Test41_Outward_MatterLink_CallerWithoutMatterView_FailsClosed()
    {
        var desk = await CreateDeskAsync($"D_MAT3_{Guid.NewGuid():N}"[..10], "Desk Mat3");
        var matterId = await CreateSampleMatterAsync();

        // Create user with Outward permissions ONLY (no Matter.View)
        var adminClient = await CreateAdminClientAsync();
        var roleCode = $"R_NOMAT_{Guid.NewGuid():N}"[..12];
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var role = new Role { Code = roleCode, Name = roleCode };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var outPerms = await db.Permissions.Where(p => p.Code.StartsWith("Outward.")).ToListAsync();
            foreach (var p in outPerms)
            {
                db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = p.Id, ScopeMode = ScopeMode.All });
            }
            await db.SaveChangesAsync();
        }

        Guid roleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            roleId = (await db.Roles.FirstAsync(r => r.Code == roleCode)).Id;
        }

        var uname = $"user_nomat_{Guid.NewGuid():N}"[..12];
        var createReq = new CreateUserRequest(uname, uname, "Pass!123", null, [roleId], null, null);
        var createRes = await adminClient.PostAsJsonAsync("/api/admin/users", createReq);
        var userId = (await createRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        await adminClient.PostAsJsonAsync($"/api/admin/users/{userId}/desks", new AssignDeskRequest(desk.Id, IsPrimary: true));

        var userClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await userClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(uname, "Pass!123"));

        // Attempting to link matter without Matter.View fails closed -> 403 Forbidden
        var num = $"OUT_NOMAT_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, matterId: matterId);
        var res = await userClient.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Test42_Outward_MatterLink_Unlink_Succeeds()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_MAT4_{Guid.NewGuid():N}"[..10], "Desk Mat4");
        var matterId = await CreateSampleMatterAsync();

        var num = $"OUT_MAT4_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, matterId: matterId);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Unlink matter via metadata update
        var updateReq = new
        {
            subject = "Updated Notice",
            recipientName = "Recipient",
            issuingDeskId = desk.Id,
            matterId = (Guid?)null,
            expectedRevision = 0
        };
        var putRes = await admin.PutAsJsonAsync($"/api/outward/{outId}", updateReq);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var outward = await db.Outwards.SingleAsync(o => o.Id == outId);
        Assert.Null(outward.MatterId);
    }

    [Fact]
    public async Task Test43_Outward_MatterLink_DispatchedOutward_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_MAT5_{Guid.NewGuid():N}"[..10], "Desk Mat5");
        var matterId = await CreateSampleMatterAsync();

        var num = $"OUT_MAT5_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Dispatch
        var dispReq = new { dispatchDate = "2026-09-19", dispatchMode = "SpeedPost", expectedRevision = 0 };
        await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);

        // Attempt metadata update to link matter -> 409 Conflict
        var updateReq = new
        {
            subject = "Subject",
            recipientName = "Recipient",
            issuingDeskId = desk.Id,
            matterId = (Guid?)matterId,
            expectedRevision = 1
        };
        var putRes = await admin.PutAsJsonAsync($"/api/outward/{outId}", updateReq);
        Assert.Equal(HttpStatusCode.Conflict, putRes.StatusCode);
    }

    // ========================================================================
    // CATEGORY 7: DISPATCH LIFECYCLE & RECORD SEALING (Tests 44-50)
    // ========================================================================

    [Fact]
    public async Task Test44_Outward_Dispatch_RegisteredOutward_TransitionsToDispatched()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DISP1_{Guid.NewGuid():N}"[..10], "Desk Disp1");

        var num = $"OUT_DSP1_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "dispatch1.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var dispReq = new
        {
            dispatchDate = "2026-09-19",
            dispatchMode = "SpeedPost",
            dispatchReferenceNumber = "ED123456789IN",
            expectedRevision = 0
        };
        var dispRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);
        Assert.Equal(HttpStatusCode.OK, dispRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var outward = await db.Outwards.SingleAsync(o => o.Id == outId);
        Assert.Equal(OutwardStatus.Dispatched, outward.Status);
        Assert.Equal(1, outward.Revision);
        Assert.NotNull(outward.DispatchedAt);

        var ev = await db.OutwardEvents.OrderByDescending(e => e.SequenceNumber).FirstAsync(e => e.OutwardId == outId);
        Assert.Equal(OutwardEventAction.Dispatched, ev.Action);
        Assert.Equal("SpeedPost", ev.DispatchMode);
        Assert.Equal("ED123456789IN", ev.DispatchReferenceNumber);
    }

    [Theory]
    [InlineData("SpeedPost")]
    [InlineData("RegisteredPost")]
    [InlineData("ByHand")]
    [InlineData("SpecialMessenger")]
    [InlineData("Courier")]
    [InlineData("Email")]
    [InlineData("Other")]
    public async Task Test45_Outward_Dispatch_WhitelistedModes_Accepted(string mode)
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_MODE_{Guid.NewGuid():N}"[..10], "Desk Mode");

        var num = $"OUT_M_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "mode.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var dispReq = new { dispatchDate = "2026-09-19", dispatchMode = mode, expectedRevision = 0 };
        var dispRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);
        Assert.Equal(HttpStatusCode.OK, dispRes.StatusCode);
    }

    [Fact]
    public async Task Test46_Outward_Dispatch_InvalidMode_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_INVMOD_{Guid.NewGuid():N}"[..10], "Desk InvMod");

        var num = $"OUT_IM_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var dispReq = new { dispatchDate = "2026-09-19", dispatchMode = "CarrierPigeon", expectedRevision = 0 };
        var dispRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);
        Assert.Equal(HttpStatusCode.BadRequest, dispRes.StatusCode);
    }

    [Fact]
    public async Task Test47_Outward_Dispatch_AlreadyDispatched_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_ALRDSP_{Guid.NewGuid():N}"[..10], "Desk AlrDsp");

        var num = $"OUT_AD_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "alrdsp.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var dispReq1 = new { dispatchDate = "2026-09-19", dispatchMode = "SpeedPost", expectedRevision = 0 };
        var dispRes1 = await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq1);
        Assert.Equal(HttpStatusCode.OK, dispRes1.StatusCode);

        // Attempt second dispatch -> Conflict 409
        var dispReq2 = new { dispatchDate = "2026-09-19", dispatchMode = "SpeedPost", expectedRevision = 1 };
        var dispRes2 = await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq2);
        Assert.Equal(HttpStatusCode.Conflict, dispRes2.StatusCode);
    }

    [Fact]
    public async Task Test48_Outward_Dispatch_Cancelled_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DSPCNC_{Guid.NewGuid():N}"[..10], "Desk DspCnc");

        var num = $"OUT_DC_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "dspcnc.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Cancel
        var cancReq = new { cancellationReason = "Cancelled first", expectedRevision = 0 };
        await admin.PostAsJsonAsync($"/api/outward/{outId}/cancel", cancReq);

        // Attempt dispatch on cancelled outward -> Conflict 409
        var dispReq = new { dispatchDate = "2026-09-19", dispatchMode = "SpeedPost", expectedRevision = 1 };
        var dispRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);
        Assert.Equal(HttpStatusCode.Conflict, dispRes.StatusCode);
    }

    [Fact]
    public async Task Test49_Outward_Dispatch_SealsRecord_BlocksAllMutations()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_SEAL_{Guid.NewGuid():N}"[..10], "Desk Seal");

        var num = $"OUT_SEAL_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "seal.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Dispatch
        var dispReq = new { dispatchDate = "2026-09-19", dispatchMode = "SpeedPost", expectedRevision = 0 };
        await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);

        // 1. Edit metadata -> Conflict 409
        var updateReq = new { subject = "Mutated Subject", recipientName = "Recipient", issuingDeskId = desk.Id, expectedRevision = 1 };
        var updateRes = await admin.PutAsJsonAsync($"/api/outward/{outId}", updateReq);
        Assert.Equal(HttpStatusCode.Conflict, updateRes.StatusCode);

        // 2. Upload main document -> Conflict 409
        var uploadForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SamplePdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        uploadForm.Add(fileContent, "file", "post_seal.pdf");
        uploadForm.Add(new StringContent("1"), "expectedRevision");
        var docRes = await admin.PutAsync($"/api/outward/{outId}/document", uploadForm);
        Assert.Equal(HttpStatusCode.Conflict, docRes.StatusCode);
    }

    [Fact]
    public async Task Test50_Outward_Dispatch_CancelledAttemptOnDispatched_Forbidden()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DISPCANC_{Guid.NewGuid():N}"[..10], "Desk DispCanc");

        var num = $"OUT_DCNC_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "dispcanc.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Dispatch
        var dispReq = new { dispatchDate = "2026-09-19", dispatchMode = "SpeedPost", expectedRevision = 0 };
        await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);

        // Attempt cancel on dispatched -> Conflict 409 (dispatched cannot be cancelled)
        var cancReq = new { cancellationReason = "Try cancel dispatched", expectedRevision = 1 };
        var cancRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/cancel", cancReq);
        Assert.Equal(HttpStatusCode.Conflict, cancRes.StatusCode);
    }

    // ========================================================================
    // CATEGORY 8: CANCELLATION LIFECYCLE (Tests 51-55)
    // ========================================================================

    [Fact]
    public async Task Test51_Outward_Cancel_RegisteredOutward_TransitionsToCancelled()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_CANC1_{Guid.NewGuid():N}"[..10], "Desk Canc1");

        var num = $"OUT_CNC1_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var cancReq = new { cancellationReason = "Defective dispatch details", expectedRevision = 0 };
        var cancRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/cancel", cancReq);
        Assert.Equal(HttpStatusCode.OK, cancRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var outward = await db.Outwards.SingleAsync(o => o.Id == outId);
        Assert.Equal(OutwardStatus.Cancelled, outward.Status);
        Assert.Equal("Defective dispatch details", outward.CancellationReason);
        Assert.NotNull(outward.CancelledAt);

        var ev = await db.OutwardEvents.OrderByDescending(e => e.SequenceNumber).FirstAsync(e => e.OutwardId == outId);
        Assert.Equal(OutwardEventAction.Cancelled, ev.Action);
    }

    [Fact]
    public async Task Test52_Outward_Cancel_RequiresMeaningfulReason_Min5Chars()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_CANC2_{Guid.NewGuid():N}"[..10], "Desk Canc2");

        var num = $"OUT_CNC2_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Blank or <5 chars reason
        var cancReq = new { cancellationReason = "bad", expectedRevision = 0 };
        var cancRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/cancel", cancReq);
        Assert.Equal(HttpStatusCode.BadRequest, cancRes.StatusCode);
    }

    [Fact]
    public async Task Test53_Outward_Cancel_PermanentNumberReservation_CannotReuseNumber()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_CANC3_{Guid.NewGuid():N}"[..10], "Desk Canc3");

        var reservedNum = $"LAC/RESERVED/2026/001";
        using var form1 = CreateRegisterForm(reservedNum, desk.Id);
        var res1 = await admin.PostAsync("/api/outward", form1);
        var outId = (await res1.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Cancel the outward
        var cancReq = new { cancellationReason = "Cancelled notice", expectedRevision = 0 };
        await admin.PostAsJsonAsync($"/api/outward/{outId}/cancel", cancReq);

        // Attempt to reuse reservedNum on new registration -> 409 Conflict
        using var form2 = CreateRegisterForm(reservedNum, desk.Id);
        var res2 = await admin.PostAsync("/api/outward", form2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
    }

    [Fact]
    public async Task Test54_Outward_Cancel_AlreadyCancelled_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_CANC4_{Guid.NewGuid():N}"[..10], "Desk Canc4");

        var num = $"OUT_CNC4_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var cancReq1 = new { cancellationReason = "Cancelled first", expectedRevision = 0 };
        var cancRes1 = await admin.PostAsJsonAsync($"/api/outward/{outId}/cancel", cancReq1);
        Assert.Equal(HttpStatusCode.OK, cancRes1.StatusCode);

        // Cancel again -> Conflict 409
        var cancReq2 = new { cancellationReason = "Cancelled second", expectedRevision = 1 };
        var cancRes2 = await admin.PostAsJsonAsync($"/api/outward/{outId}/cancel", cancReq2);
        Assert.Equal(HttpStatusCode.Conflict, cancRes2.StatusCode);
    }

    [Fact]
    public async Task Test55_Outward_Cancel_DispatchedOutward_Forbidden()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_CANC5_{Guid.NewGuid():N}"[..10], "Desk Canc5");

        var num = $"OUT_CNC5_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "canc5.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Dispatch
        var dispReq = new { dispatchDate = "2026-09-19", dispatchMode = "SpeedPost", expectedRevision = 0 };
        await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);

        // Dispatched outward cannot be cancelled
        var cancReq = new { cancellationReason = "Attempt cancel dispatched", expectedRevision = 1 };
        var cancRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/cancel", cancReq);
        Assert.Equal(HttpStatusCode.Conflict, cancRes.StatusCode);
    }

    // ========================================================================
    // CATEGORY 9: CONCURRENCY, AMBIGUOUS COMMITS & IMMUTABILITY (Tests 56-58)
    // ========================================================================

    [Fact]
    public async Task Test56_Outward_ConcurrencyToken_RevisionMismatch_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_CONC_{Guid.NewGuid():N}"[..10], "Desk Conc");

        var num = $"OUT_CONC_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Outward is at revision 0, but client passes expectedRevision = 5 -> Conflict 409
        var updateReq = new
        {
            subject = "Conflicting update",
            recipientName = "Recipient",
            issuingDeskId = desk.Id,
            expectedRevision = 5
        };
        var putRes = await admin.PutAsJsonAsync($"/api/outward/{outId}", updateReq);
        Assert.Equal(HttpStatusCode.Conflict, putRes.StatusCode);
    }

    [Fact]
    public async Task Test57_Outward_AmbiguousCommit_VerifySucceeded_Idempotent()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_IDEM_{Guid.NewGuid():N}"[..10], "Desk Idem");

        var num = $"OUT_IDEM_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Verify registration event exists and matches stable id
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var ev = await db.OutwardEvents.SingleAsync(e => e.OutwardId == outId && e.SequenceNumber == 1);
        Assert.Equal(OutwardEventAction.Registered, ev.Action);
    }

    [Fact]
    public async Task Test58_OutwardEvent_ThreeLayerImmutability_ProhibitsUpdateAndDelete()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var ev = new OutwardEvent
        {
            Id = Guid.NewGuid(),
            OutwardId = Guid.NewGuid(),
            SequenceNumber = 1,
            Action = OutwardEventAction.Registered,
            ActionByUserId = Guid.NewGuid(),
            ActionByDisplayNameSnapshot = "Test Officer",
            ActionAt = DateTimeOffset.UtcNow
        };
        db.OutwardEvents.Add(ev);
        await db.SaveChangesAsync();

        // 1. Attempting to modify OutwardEvent throws InvalidOperationException in SaveChangesAsync
        ev.ActionByDisplayNameSnapshot = "Tampered officer";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        // Reset entry state
        db.Entry(ev).State = EntityState.Unchanged;

        // 2. Attempting to delete OutwardEvent throws InvalidOperationException in SaveChangesAsync
        db.OutwardEvents.Remove(ev);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    // ========================================================================
    // CATEGORY 10: HARDENING & SECURITY (Tests 59-84)
    // ========================================================================

    // --- Category A: Document Signature & Upload Security (Tests 59-62) ---

    [Fact]
    public async Task Test59_Register_PdfWithPngExtension_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_SEC59_{Guid.NewGuid():N}"[..10], "Desk Sec59");

        var num = $"OUT_SEC59_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "test.png", contentType: "image/png");
        var res = await admin.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Test60_Register_PdfMagicBytesMismatch_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_SEC60_{Guid.NewGuid():N}"[..10], "Desk Sec60");

        var num = $"OUT_SEC60_{Guid.NewGuid():N}"[..14];
        var invalidBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: invalidBytes, fileName: "test.pdf", contentType: "application/pdf");
        var res = await admin.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Test61_AddAttachment_FullPng8ByteHeader_Accepted()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_SEC61_{Guid.NewGuid():N}"[..10], "Desk Sec61");

        var num = $"OUT_SEC61_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var attForm = new MultipartFormDataContent();
        attForm.Add(new StringContent("Valid PNG Annexure"), "title");
        attForm.Add(new StringContent("Annexure"), "attachmentType");
        attForm.Add(new StringContent("1"), "sequenceOrder");
        attForm.Add(new StringContent("0"), "expectedRevision");
        var fileContent = new ByteArrayContent(SamplePngBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        attForm.Add(fileContent, "file", "valid.png");

        var attRes = await admin.PostAsync($"/api/outward/{outId}/attachments", attForm);
        Assert.Equal(HttpStatusCode.Created, attRes.StatusCode);
    }

    [Fact]
    public async Task Test62_AddAttachment_Partial4BytePng_Rejected()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_SEC62_{Guid.NewGuid():N}"[..10], "Desk Sec62");

        var num = $"OUT_SEC62_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var attForm = new MultipartFormDataContent();
        attForm.Add(new StringContent("Partial PNG Annexure"), "title");
        attForm.Add(new StringContent("Annexure"), "attachmentType");
        attForm.Add(new StringContent("1"), "sequenceOrder");
        attForm.Add(new StringContent("0"), "expectedRevision");
        var partialBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // Only 4 bytes
        var fileContent = new ByteArrayContent(partialBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        attForm.Add(fileContent, "file", "partial.png");

        var attRes = await admin.PostAsync($"/api/outward/{outId}/attachments", attForm);
        Assert.Equal(HttpStatusCode.BadRequest, attRes.StatusCode);
    }

    // --- Category B: Document Provenance (Tests 63-68) ---

    [Fact]
    public async Task Test63_Register_ExistingDocFromUnrelatedEntity_RejectedWith403()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_PROV63_{Guid.NewGuid():N}"[..10], "Desk Prov63");

        // Create an unrelated document
        Guid unrelatedDocId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var doc = new Document
            {
                Id = Guid.NewGuid(),
                OriginalFileName = "unrelated.pdf",
                StoragePath = $"documents/{Guid.NewGuid():N}.pdf",
                MimeType = "application/pdf",
                FileSize = 1024,
                Sha256Hash = "dummyhash",
                RecordStatus = RecordStatus.Active,
                UploadedAt = DateTimeOffset.UtcNow
            };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();
            unrelatedDocId = doc.Id;
        }

        var num = $"OUT_PRV63_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, existingDocumentId: unrelatedDocId);
        var res = await admin.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Test64_Register_ExistingDocFromActiveMatter_Accepted()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_PROV64_{Guid.NewGuid():N}"[..10], "Desk Prov64");
        var (matterId, matterDocId) = await CreateSampleMatterWithDocumentAsync();

        var num = $"OUT_PRV64_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, matterId: matterId, existingDocumentId: matterDocId);
        var res = await admin.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var outward = await db.Outwards.SingleAsync(o => o.Id == outId);
        Assert.Equal(matterDocId, outward.MainDocumentId);
    }

    [Fact]
    public async Task Test65_Register_ExistingDocFromPrimaryDak_Accepted()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_PROV65_{Guid.NewGuid():N}"[..10], "Desk Prov65");
        var (dakId, dakDocId) = await CreateSampleDakWithDocumentAsync(admin, $"DAK_PR65_{Guid.NewGuid():N}"[..14]);

        var num = $"OUT_PRV65_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, primaryDakId: dakId, existingDocumentId: dakDocId);
        var res = await admin.PostAsync("/api/outward", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var outward = await db.Outwards.SingleAsync(o => o.Id == outId);
        Assert.Equal(dakDocId, outward.MainDocumentId);
    }

    [Fact]
    public async Task Test66_ChangeMainDoc_ExistingDocFromLinkedDak_Accepted()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_PROV66_{Guid.NewGuid():N}"[..10], "Desk Prov66");

        var num = $"OUT_PRV66_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var (dakId, dakDocId) = await CreateSampleDakWithDocumentAsync(admin, $"DAK_PR66_{Guid.NewGuid():N}"[..14]);

        // Link Dak to outward
        var linkReq = new { dakId, relationshipType = "PrimaryReply", isPrimary = true, expectedRevision = 0 };
        var linkRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dak-links", linkReq);
        Assert.Equal(HttpStatusCode.Created, linkRes.StatusCode);

        // Change main document referencing Dak's document
        var docReq = new { existingDocumentId = dakDocId, expectedRevision = 1 };
        var docRes = await admin.PutAsJsonAsync($"/api/outward/{outId}/document", docReq);
        Assert.Equal(HttpStatusCode.OK, docRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var outward = await db.Outwards.SingleAsync(o => o.Id == outId);
        Assert.Equal(dakDocId, outward.MainDocumentId);
    }

    [Fact]
    public async Task Test67_ChangeMainDoc_ExistingDocFromUnlinkedMatter_RejectedWith403()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_PROV67_{Guid.NewGuid():N}"[..10], "Desk Prov67");

        var num = $"OUT_PRV67_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var (_, matterDocId) = await CreateSampleMatterWithDocumentAsync();

        // Attempt to set main document to an unlinked matter's document -> 403 Forbidden
        var docReq = new { existingDocumentId = matterDocId, expectedRevision = 0 };
        var docRes = await admin.PutAsJsonAsync($"/api/outward/{outId}/document", docReq);
        Assert.Equal(HttpStatusCode.Forbidden, docRes.StatusCode);
    }

    [Fact]
    public async Task Test68_AddAttachment_ExistingDocFromSameOutward_Accepted()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_PROV68_{Guid.NewGuid():N}"[..10], "Desk Prov68");

        var num = $"OUT_PRV68_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "main68.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        Guid mainDocId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var outward = await db.Outwards.SingleAsync(o => o.Id == outId);
            mainDocId = outward.MainDocumentId!.Value;
        }

        var attReq = new
        {
            title = "Attachment Reusing Main Doc",
            attachmentType = "Annexure",
            sequenceOrder = 1,
            existingDocumentId = mainDocId,
            expectedRevision = 0
        };
        var attRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/attachments", attReq);
        Assert.Equal(HttpStatusCode.Created, attRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var att = await db.OutwardAttachments.SingleAsync(a => a.OutwardId == outId);
            Assert.Equal(mainDocId, att.DocumentId);
        }
    }

    // --- Category C: Dispatch Main Document Precondition (Tests 69-71) ---

    [Fact]
    public async Task Test69_Dispatch_WithoutMainDocument_RejectedWith400()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DISP69_{Guid.NewGuid():N}"[..10], "Desk Disp69");

        var num = $"OUT_DSP69_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id); // No main document file
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var dispReq = new { dispatchDate = "2026-09-19", dispatchMode = "SpeedPost", expectedRevision = 0 };
        var dispRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);
        Assert.Equal(HttpStatusCode.BadRequest, dispRes.StatusCode);
    }

    [Fact]
    public async Task Test70_Dispatch_WithSoftDeletedMainDocument_RejectedWith400()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DISP70_{Guid.NewGuid():N}"[..10], "Desk Disp70");

        var num = $"OUT_DSP70_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "softdel.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // Soft-delete main document directly in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var outward = await db.Outwards.SingleAsync(o => o.Id == outId);
            var doc = await db.Documents.SingleAsync(d => d.Id == outward.MainDocumentId);
            doc.RecordStatus = RecordStatus.Archived;
            await db.SaveChangesAsync();
        }

        var dispReq = new { dispatchDate = "2026-09-19", dispatchMode = "SpeedPost", expectedRevision = 0 };
        var dispRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);
        Assert.Equal(HttpStatusCode.BadRequest, dispRes.StatusCode);
    }

    [Fact]
    public async Task Test71_Dispatch_WithValidMainDocument_Succeeds()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_DISP71_{Guid.NewGuid():N}"[..10], "Desk Disp71");

        var num = $"OUT_DSP71_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "valid71.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var dispReq = new { dispatchDate = "2026-09-19", dispatchMode = "SpeedPost", expectedRevision = 0 };
        var dispRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);
        Assert.Equal(HttpStatusCode.OK, dispRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var outward = await db.Outwards.SingleAsync(o => o.Id == outId);
        Assert.Equal(OutwardStatus.Dispatched, outward.Status);
    }

    // --- Category D: Scope Mode Authorization & Lookups (Tests 72-76) ---

    [Fact]
    public async Task Test72_Lookups_Registration_UserWithNoDeskMembership_Returns403()
    {
        var (client, _) = await CreateScopedUserClientAsync($"user_nodesk_{Guid.NewGuid():N}"[..12], "R_NODESK72", ScopeMode.All, deskId: null);
        var res = await client.GetAsync("/api/outward/lookups/registration");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Test73_Lookups_Registration_UserWithAssignedScope_ReturnsOnlyAssignedDesks()
    {
        var deskA = await CreateDeskAsync($"D73A_{Guid.NewGuid():N}"[..10], "Desk 73A");
        var deskB = await CreateDeskAsync($"D73B_{Guid.NewGuid():N}"[..10], "Desk 73B");

        var (client, _) = await CreateScopedUserClientAsync($"user_73_{Guid.NewGuid():N}"[..12], "R_73", ScopeMode.Assigned, deskId: deskA.Id);
        var res = await client.GetAsync("/api/outward/lookups/registration");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var desks = json.GetProperty("desks").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(deskA.Id, desks);
        Assert.DoesNotContain(deskB.Id, desks);
    }

    [Fact]
    public async Task Test74_Lookups_Registration_UserWithWorkstreamScope_ReturnsOnlyWorkstreamDesks()
    {
        var ws = await CreateWorkstreamAsync($"WS74_{Guid.NewGuid():N}"[..10], "Workstream 74");
        var deskInWs = await CreateDeskAsync($"D74A_{Guid.NewGuid():N}"[..10], "Desk 74A", workstreamId: ws.Id);

        var (client, _) = await CreateScopedUserClientAsync($"user_74_{Guid.NewGuid():N}"[..12], "R_74", ScopeMode.Workstream, deskId: deskInWs.Id, workstreamId: ws.Id);
        var res = await client.GetAsync("/api/outward/lookups/registration");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var desks = json.GetProperty("desks").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(deskInWs.Id, desks);
    }

    [Fact]
    public async Task Test75_Lookups_Registration_UserWithOwnScope_Returns403()
    {
        var desk75 = await CreateDeskAsync($"D75_{Guid.NewGuid():N}"[..10], "Desk 75");
        var (client, _) = await CreateScopedUserClientAsync($"user_75_{Guid.NewGuid():N}"[..12], "R_75", ScopeMode.Own, deskId: desk75.Id);
        var res = await client.GetAsync("/api/outward/lookups/registration");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Test76_UpdateMetadata_TargetDeskScopeEscape_Returns403()
    {
        var deskEsc1 = await CreateDeskAsync($"D76A_{Guid.NewGuid():N}"[..10], "Desk 76A");
        var deskEsc2 = await CreateDeskAsync($"D76B_{Guid.NewGuid():N}"[..10], "Desk 76B");

        using var admin = await CreateAdminClientAsync();
        var num = $"OUT_76_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, deskEsc1.Id);
        var regRes = await admin.PostAsync("/api/outward", form);
        var outId = (await regRes.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // User assigned to deskEsc1 only attempts to set issuingDeskId to deskEsc2
        var (client, _) = await CreateScopedUserClientAsync($"user_76_{Guid.NewGuid():N}"[..12], "R_76", ScopeMode.Assigned, deskId: deskEsc1.Id);
        var updateReq = new
        {
            subject = "Escaped Subject",
            recipientName = "Recipient",
            issuingDeskId = deskEsc2.Id,
            expectedRevision = 0
        };
        var updateRes = await client.PutAsJsonAsync($"/api/outward/{outId}", updateReq);
        Assert.Equal(HttpStatusCode.Forbidden, updateRes.StatusCode);
    }

    // --- Category E: Ambiguous-Commit Verification Hardening (Tests 77-80) ---

    [Fact]
    public async Task Test77_Register_CommitAmbiguity_VerifiedViaEventIdempotently()
    {
        var dbName = $"out-retry-reg-{Guid.NewGuid():N}";
        var (db, adminUser, desk) = await CreateOutwardWorkflowContextAsync(dbName);
        var storage = new TestInMemoryDocumentStorage();
        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var accessControl = new TestOutwardAccessControlService();
        var dakAuth = new DakAuthorizationService(db);
        var workflow = new OutwardWorkflowService(db, storage, dakAuth, accessControl, () => strategy!);
        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2);

        using var ms = new MemoryStream(SamplePdfBytes);
        var cmd = new RegisterOutwardCommand(
            OutwardNumber: $"OUT_AMB_REG_{Guid.NewGuid():N}"[..14],
            OutwardDate: new DateOnly(2026, 9, 19),
            Subject: "Commit Ambiguity Registration",
            RecipientName: "Recipient Ambiguous",
            RecipientDesignation: null,
            RecipientDepartment: null,
            RecipientAddress: null,
            RecipientEmail: null,
            RecipientPhone: null,
            IssuingDeskId: desk.Id,
            WorkstreamId: null,
            OfficeReferenceNumber: null,
            Remarks: null,
            PrimaryDakId: null,
            MatterId: null,
            ExistingDocumentId: null,
            DocumentStream: ms,
            DocumentFileName: "ambiguous.pdf",
            DocumentContentType: "application/pdf"
        );

        var outward = await workflow.RegisterAsync(cmd, adminUser.Id);

        Assert.NotNull(outward);
        Assert.Equal(OutwardStatus.Registered, outward.Status);
        Assert.Equal(1, strategy.AttemptCount);
        Assert.Equal(1, strategy.VerifyCount);

        var evCount = await db.OutwardEvents.CountAsync(e => e.OutwardId == outward.Id && e.Action == OutwardEventAction.Registered);
        Assert.Equal(1, evCount);
    }

    [Fact]
    public async Task Test78_ChangeMainDoc_CommitAmbiguity_VerifiedViaEventIdempotently()
    {
        var dbName = $"out-retry-cmd-{Guid.NewGuid():N}";
        var (db, adminUser, desk) = await CreateOutwardWorkflowContextAsync(dbName);
        var storage = new TestInMemoryDocumentStorage();
        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var accessControl = new TestOutwardAccessControlService();
        var dakAuth = new DakAuthorizationService(db);
        var workflow = new OutwardWorkflowService(db, storage, dakAuth, accessControl, () => strategy!);

        // 1. Initial register without commit ambiguity
        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: false, maxRetries: 2);
        using var initMs = new MemoryStream(SamplePdfBytes);
        var regCmd = new RegisterOutwardCommand(
            OutwardNumber: $"OUT_AMB_CMD_{Guid.NewGuid():N}"[..14],
            OutwardDate: new DateOnly(2026, 9, 19),
            Subject: "Initial Subject",
            RecipientName: "Initial Recipient",
            RecipientDesignation: null,
            RecipientDepartment: null,
            RecipientAddress: null,
            RecipientEmail: null,
            RecipientPhone: null,
            IssuingDeskId: desk.Id,
            WorkstreamId: null,
            OfficeReferenceNumber: null,
            Remarks: null,
            PrimaryDakId: null,
            MatterId: null,
            ExistingDocumentId: null,
            DocumentStream: initMs,
            DocumentFileName: "initial.pdf",
            DocumentContentType: "application/pdf"
        );
        var outward = await workflow.RegisterAsync(regCmd, adminUser.Id);

        // 2. Change main document with commit ambiguity
        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2);
        using var newMs = new MemoryStream(SamplePdfBytes);
        var cmd = new ChangeMainDocumentCommand(
            ExistingDocumentId: null,
            DocumentStream: newMs,
            DocumentFileName: "changed.pdf",
            DocumentContentType: "application/pdf",
            ExpectedRevision: 0
        );

        var updated = await workflow.ChangeMainDocumentAsync(outward.Id, cmd, adminUser.Id);

        Assert.NotNull(updated);
        Assert.Equal(1, strategy.AttemptCount);
        Assert.Equal(1, strategy.VerifyCount);

        var evCount = await db.OutwardEvents.CountAsync(e => e.OutwardId == outward.Id && e.Action == OutwardEventAction.MainDocumentChanged);
        Assert.Equal(1, evCount);
    }

    [Fact]
    public async Task Test79_AddAttachment_CommitAmbiguity_VerifiedViaEventIdempotently()
    {
        var dbName = $"out-retry-att-{Guid.NewGuid():N}";
        var (db, adminUser, desk) = await CreateOutwardWorkflowContextAsync(dbName);
        var storage = new TestInMemoryDocumentStorage();
        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var accessControl = new TestOutwardAccessControlService();
        var dakAuth = new DakAuthorizationService(db);
        var workflow = new OutwardWorkflowService(db, storage, dakAuth, accessControl, () => strategy!);

        // 1. Initial register
        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: false, maxRetries: 2);
        using var initMs = new MemoryStream(SamplePdfBytes);
        var regCmd = new RegisterOutwardCommand(
            OutwardNumber: $"OUT_AMB_ATT_{Guid.NewGuid():N}"[..14],
            OutwardDate: new DateOnly(2026, 9, 19),
            Subject: "Initial Subject",
            RecipientName: "Initial Recipient",
            RecipientDesignation: null,
            RecipientDepartment: null,
            RecipientAddress: null,
            RecipientEmail: null,
            RecipientPhone: null,
            IssuingDeskId: desk.Id,
            WorkstreamId: null,
            OfficeReferenceNumber: null,
            Remarks: null,
            PrimaryDakId: null,
            MatterId: null,
            ExistingDocumentId: null,
            DocumentStream: initMs,
            DocumentFileName: "initial.pdf",
            DocumentContentType: "application/pdf"
        );
        var outward = await workflow.RegisterAsync(regCmd, adminUser.Id);

        // 2. Add attachment with commit ambiguity
        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2);
        using var attMs = new MemoryStream(SamplePdfBytes);
        var cmd = new AddAttachmentCommand(
            Title: "Ambiguous Attachment",
            AttachmentType: "Annexure",
            SequenceOrder: 1,
            ExistingDocumentId: null,
            DocumentStream: attMs,
            DocumentFileName: "amb_att.pdf",
            DocumentContentType: "application/pdf",
            ExpectedRevision: 0
        );

        var att = await workflow.AddAttachmentAsync(outward.Id, cmd, adminUser.Id);

        Assert.NotNull(att);
        Assert.Equal(1, strategy.AttemptCount);
        Assert.Equal(1, strategy.VerifyCount);

        var evCount = await db.OutwardEvents.CountAsync(e => e.OutwardId == outward.Id && e.Action == OutwardEventAction.AttachmentAdded);
        Assert.Equal(1, evCount);
    }

    [Fact]
    public async Task Test80_RemoveAttachment_CommitAmbiguity_VerifiedViaEventIdempotently()
    {
        var dbName = $"out-retry-rmatt-{Guid.NewGuid():N}";
        var (db, adminUser, desk) = await CreateOutwardWorkflowContextAsync(dbName);
        var storage = new TestInMemoryDocumentStorage();
        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var accessControl = new TestOutwardAccessControlService();
        var dakAuth = new DakAuthorizationService(db);
        var workflow = new OutwardWorkflowService(db, storage, dakAuth, accessControl, () => strategy!);

        // 1. Initial register
        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: false, maxRetries: 2);
        using var initMs = new MemoryStream(SamplePdfBytes);
        var regCmd = new RegisterOutwardCommand(
            OutwardNumber: $"OUT_AMB_RMATT_{Guid.NewGuid():N}"[..14],
            OutwardDate: new DateOnly(2026, 9, 19),
            Subject: "Initial Subject",
            RecipientName: "Initial Recipient",
            RecipientDesignation: null,
            RecipientDepartment: null,
            RecipientAddress: null,
            RecipientEmail: null,
            RecipientPhone: null,
            IssuingDeskId: desk.Id,
            WorkstreamId: null,
            OfficeReferenceNumber: null,
            Remarks: null,
            PrimaryDakId: null,
            MatterId: null,
            ExistingDocumentId: null,
            DocumentStream: initMs,
            DocumentFileName: "initial.pdf",
            DocumentContentType: "application/pdf"
        );
        var outward = await workflow.RegisterAsync(regCmd, adminUser.Id);

        // 2. Add attachment
        using var attMs = new MemoryStream(SamplePdfBytes);
        var addCmd = new AddAttachmentCommand(
            Title: "Attachment To Remove",
            AttachmentType: "Annexure",
            SequenceOrder: 1,
            ExistingDocumentId: null,
            DocumentStream: attMs,
            DocumentFileName: "to_remove.pdf",
            DocumentContentType: "application/pdf",
            ExpectedRevision: 0
        );
        var att = await workflow.AddAttachmentAsync(outward.Id, addCmd, adminUser.Id);

        // 3. Remove attachment with commit ambiguity
        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2);
        var rmCmd = new RemoveAttachmentCommand(ExpectedRevision: 1);

        var updated = await workflow.RemoveAttachmentAsync(outward.Id, att.Id, rmCmd, adminUser.Id);

        Assert.True(updated);
        Assert.Equal(1, strategy.AttemptCount);
        Assert.Equal(1, strategy.VerifyCount);

        var evCount = await db.OutwardEvents.CountAsync(e => e.OutwardId == outward.Id && e.Action == OutwardEventAction.AttachmentRemoved);
        Assert.Equal(1, evCount);
    }

    // --- Category F: Three-Layer Immutability & Event Completeness (Tests 81-84) ---

    [Fact]
    public async Task Test81_OutwardEvent_SaveInterceptor_BlocksUpdate()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var ev = new OutwardEvent
        {
            Id = Guid.NewGuid(),
            OutwardId = Guid.NewGuid(),
            SequenceNumber = 1,
            Action = OutwardEventAction.Registered,
            ActionByUserId = Guid.NewGuid(),
            ActionByDisplayNameSnapshot = "Officer 81",
            ActionAt = DateTimeOffset.UtcNow
        };
        db.OutwardEvents.Add(ev);
        await db.SaveChangesAsync();

        ev.ActionByDisplayNameSnapshot = "Tampered 81";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Test82_OutwardEvent_SaveInterceptor_BlocksDelete()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var ev = new OutwardEvent
        {
            Id = Guid.NewGuid(),
            OutwardId = Guid.NewGuid(),
            SequenceNumber = 1,
            Action = OutwardEventAction.Registered,
            ActionByUserId = Guid.NewGuid(),
            ActionByDisplayNameSnapshot = "Officer 82",
            ActionAt = DateTimeOffset.UtcNow
        };
        db.OutwardEvents.Add(ev);
        await db.SaveChangesAsync();

        db.OutwardEvents.Remove(ev);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Test83_DispatchEvent_CapturesAllDispatchMetadata()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_EV83_{Guid.NewGuid():N}"[..10], "Desk EV83");

        var num = $"OUT_EV83_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id, fileBytes: SamplePdfBytes, fileName: "ev83.pdf");
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var dispReq = new
        {
            dispatchDate = "2026-09-19",
            dispatchMode = "Courier",
            dispatchReferenceNumber = "TRACK_EV83_999",
            expectedRevision = 0
        };
        var dispRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/dispatch", dispReq);
        Assert.Equal(HttpStatusCode.OK, dispRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var ev = await db.OutwardEvents.OrderByDescending(e => e.SequenceNumber).FirstAsync(e => e.OutwardId == outId);

        Assert.Equal(OutwardEventAction.Dispatched, ev.Action);
        Assert.Equal("Courier", ev.DispatchMode);
        Assert.Equal("TRACK_EV83_999", ev.DispatchReferenceNumber);
        Assert.Equal(new DateOnly(2026, 9, 19), ev.DispatchDate);
        Assert.NotEqual(Guid.Empty, ev.ActionByUserId);
        Assert.False(string.IsNullOrWhiteSpace(ev.ActionByDisplayNameSnapshot));
    }

    [Fact]
    public async Task Test84_CancelEvent_CapturesCancellationReason()
    {
        using var admin = await CreateAdminClientAsync();
        var desk = await CreateDeskAsync($"D_EV84_{Guid.NewGuid():N}"[..10], "Desk EV84");

        var num = $"OUT_EV84_{Guid.NewGuid():N}"[..14];
        using var form = CreateRegisterForm(num, desk.Id);
        var res = await admin.PostAsync("/api/outward", form);
        var outId = (await res.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var cancReq = new
        {
            cancellationReason = "Cancellation reason for test 84 audit record",
            expectedRevision = 0
        };
        var cancRes = await admin.PostAsJsonAsync($"/api/outward/{outId}/cancel", cancReq);
        Assert.Equal(HttpStatusCode.OK, cancRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var ev = await db.OutwardEvents.OrderByDescending(e => e.SequenceNumber).FirstAsync(e => e.OutwardId == outId);

        Assert.Equal(OutwardEventAction.Cancelled, ev.Action);
        Assert.Equal("Cancellation reason for test 84 audit record", ev.CancellationReason);
        Assert.NotEqual(Guid.Empty, ev.ActionByUserId);
        Assert.False(string.IsNullOrWhiteSpace(ev.ActionByDisplayNameSnapshot));
    }

    private static async Task<(LacDbContext db, AppUser adminUser, OfficeDesk targetDesk)> CreateOutwardWorkflowContextAsync(
        string dbName,
        params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var seedBuilder = new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));

        using (var seedDb = new LacDbContext(seedBuilder.Options))
        {
            var admin = new AppUser
            {
                Id = Guid.NewGuid(),
                Username = $"admin_out_{Guid.NewGuid():N}",
                NormalizedUsername = $"ADMIN_OUT_{Guid.NewGuid():N}",
                DisplayName = "Admin Outward",
                PasswordHash = "hash",
                IsActive = true,
                RecordStatus = RecordStatus.Active
            };
            var desk = new OfficeDesk
            {
                Id = Guid.NewGuid(),
                Code = $"DSK_{Guid.NewGuid():N}"[..10],
                Name = "Desk Outward",
                IsActive = true,
                RecordStatus = RecordStatus.Active
            };
            var membership = new UserDeskMembership
            {
                Id = Guid.NewGuid(),
                UserId = admin.Id,
                User = admin,
                OfficeDeskId = desk.Id,
                OfficeDesk = desk,
                IsActive = true,
                RecordStatus = RecordStatus.Active
            };

            seedDb.AppUsers.Add(admin);
            seedDb.OfficeDesks.Add(desk);
            seedDb.UserDeskMemberships.Add(membership);
            await seedDb.SaveChangesAsync();
        }

        var testBuilder = new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));

        foreach (var interceptor in interceptors)
        {
            testBuilder.AddInterceptors(interceptor);
        }

        var db = new LacDbContext(testBuilder.Options);
        var adminUser = await db.AppUsers.FirstAsync(u => u.DisplayName == "Admin Outward");
        var targetDesk = await db.OfficeDesks.FirstAsync();

        return (db, adminUser, targetDesk);
    }
}

public sealed class TestOutwardAccessControlService : IAccessControlService
{
    public bool AllowAll { get; set; } = true;
    public Task<bool> CanAsync(string permissionCode, AccessResourceContext? resourceContext = null, CancellationToken cancellationToken = default) => Task.FromResult(AllowAll);
    public Task<IReadOnlyDictionary<string, ScopeMode>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, ScopeMode>>(new Dictionary<string, ScopeMode>());
}

public sealed class TrackingOutwardSaveChangesInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    public int FailTimes { get; set; }
    public int SaveCalls { get; private set; }
    public readonly List<Guid> AttemptOutwardIds = new();
    public readonly List<Guid> AttemptEventIds = new();

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        if (eventData.Context != null)
        {
            var outward = eventData.Context.ChangeTracker.Entries<Outward>().FirstOrDefault()?.Entity;
            if (outward != null) AttemptOutwardIds.Add(outward.Id);
            var ev = eventData.Context.ChangeTracker.Entries<OutwardEvent>().FirstOrDefault()?.Entity;
            if (ev != null) AttemptEventIds.Add(ev.Id);
        }

        if (FailTimes > 0 && SaveCalls <= FailTimes)
        {
            throw new DbUpdateException("Simulated transient DB failure on SaveChangesAsync");
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
