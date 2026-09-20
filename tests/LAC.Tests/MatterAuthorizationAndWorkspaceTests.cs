using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using SecurityClaim = System.Security.Claims.Claim;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace LAC.Tests;

public sealed class MatterAuthorizationAndWorkspaceTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    private static readonly byte[] ValidPdfBytes = "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Count 0/Kids[]>>endobj\nxref\n0 3\n0000000000 65535 f\n0000000009 00000 n\n0000000052 00000 n\ntrailer<</Size 3/Root 1 0 R>>\nstartxref\n99\n%%EOF"u8.ToArray();
    private static readonly byte[] FakePdfBytes = "MZ\x90\x00\x03\x00\x00\x00"u8.ToArray(); // Disguised PE binary
    private static readonly byte[] ValidPngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };

    public MatterAuthorizationAndWorkspaceTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private sealed class DynamicTestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userIdStr = Request.Headers["X-Test-User-Id"].FirstOrDefault();
            Guid userId = Guid.TryParse(userIdStr, out var parsed) ? parsed : SeedData.BootstrapAdminId;

            var claims = new List<SecurityClaim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Name, "testuser"),
                new("username", "testuser"),
                new("display_name", "Test User")
            };
            var identity = new ClaimsIdentity(claims, "DynamicTest");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "DynamicTest");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class AllowAllAccessControlService : IAccessControlService
    {
        public Task<bool> CanAsync(string permissionCode, AccessResourceContext? context = null, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyDictionary<string, ScopeMode>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, ScopeMode>>(new Dictionary<string, ScopeMode>());
    }

    private sealed class DenyAllAccessControlServiceStub : IAccessControlService
    {
        public Task<bool> CanAsync(string permissionCode, AccessResourceContext? context = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyDictionary<string, ScopeMode>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, ScopeMode>>(new Dictionary<string, ScopeMode>());
    }

    private static (LacDbContext db, Workstream ws1, Workstream ws2, Village village, AppUser userAll, AppUser userWs1, AppUser userAssigned, AppUser userNone) CreateTestDbContext(string dbName, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));

        if (interceptor != null)
        {
            builder.AddInterceptors(interceptor);
        }

        var db = new LacDbContext(builder.Options);

        var ws1 = new Workstream
        {
            Id = Guid.NewGuid(),
            Code = $"WS_COURT_{Guid.NewGuid():N}"[..15],
            Name = "Court References",
            IsActive = true,
            RecordStatus = RecordStatus.Active
        };

        var ws2 = new Workstream
        {
            Id = Guid.NewGuid(),
            Code = $"WS_COMP_{Guid.NewGuid():N}"[..15],
            Name = "Compensation & Accounts",
            IsActive = true,
            RecordStatus = RecordStatus.Active
        };

        var village = new Village
        {
            Id = Guid.NewGuid(),
            Name = "Test Village Alpha",
            RecordStatus = RecordStatus.Active
        };

        var pView = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.MatterView, Name = "Matter View", Category = "Matter" };
        var pCreate = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.MatterCreate, Name = "Matter Create", Category = "Matter" };
        var pEdit = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.MatterEdit, Name = "Matter Edit", Category = "Matter" };
        var pDoc = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.MatterDocumentManage, Name = "Matter Doc", Category = "Matter" };
        var pArchive = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.MatterArchive, Name = "Matter Archive", Category = "Matter" };
        var pDraftView = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.DraftView, Name = "Draft View", Category = "Draft" };
        var pDraftCreate = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.DraftCreate, Name = "Draft Create", Category = "Draft" };
        var pDraftEdit = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.DraftEdit, Name = "Draft Edit", Category = "Draft" };

        var rAll = new Role { Id = Guid.NewGuid(), Code = $"R_ALL_{Guid.NewGuid():N}"[..10], Name = "Role All", IsActive = true, RecordStatus = RecordStatus.Active };
        var rWs = new Role { Id = Guid.NewGuid(), Code = $"R_WS_{Guid.NewGuid():N}"[..10], Name = "Role Workstream", IsActive = true, RecordStatus = RecordStatus.Active };
        var rAssigned = new Role { Id = Guid.NewGuid(), Code = $"R_ASS_{Guid.NewGuid():N}"[..10], Name = "Role Assigned", IsActive = true, RecordStatus = RecordStatus.Active };

        var allPerms = new[] { pView, pCreate, pEdit, pDoc, pArchive, pDraftView, pDraftCreate, pDraftEdit };
        foreach (var p in allPerms)
        {
            rAll.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = rAll.Id, PermissionId = p.Id, ScopeMode = ScopeMode.All });
            rWs.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = rWs.Id, PermissionId = p.Id, ScopeMode = ScopeMode.Workstream });
            rAssigned.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = rAssigned.Id, PermissionId = p.Id, ScopeMode = ScopeMode.Assigned });
        }

        var userAll = new AppUser { Id = Guid.NewGuid(), Username = $"u_all_{Guid.NewGuid():N}", NormalizedUsername = "U_ALL", DisplayName = "User All", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active };
        var userWs1 = new AppUser { Id = Guid.NewGuid(), Username = $"u_ws1_{Guid.NewGuid():N}", NormalizedUsername = "U_WS1", DisplayName = "User Ws1", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active };
        var userAssigned = new AppUser { Id = Guid.NewGuid(), Username = $"u_ass_{Guid.NewGuid():N}", NormalizedUsername = "U_ASS", DisplayName = "User Assigned", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active };
        var userNone = new AppUser { Id = Guid.NewGuid(), Username = $"u_none_{Guid.NewGuid():N}", NormalizedUsername = "U_NONE", DisplayName = "User None", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active };

        userAll.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = userAll.Id, RoleId = rAll.Id });
        userWs1.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = userWs1.Id, RoleId = rWs.Id });
        userWs1.WorkstreamMemberships.Add(new UserWorkstreamMembership { Id = Guid.NewGuid(), UserId = userWs1.Id, WorkstreamId = ws1.Id, IsActive = true, Workstream = ws1 });

        userAssigned.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = userAssigned.Id, RoleId = rAssigned.Id });
        userAssigned.WorkstreamMemberships.Add(new UserWorkstreamMembership { Id = Guid.NewGuid(), UserId = userAssigned.Id, WorkstreamId = ws1.Id, IsActive = true, Workstream = ws1 });

        db.Workstreams.AddRange(ws1, ws2);
        db.Villages.Add(village);
        db.Permissions.AddRange(allPerms);
        db.Roles.AddRange(rAll, rWs, rAssigned);
        db.AppUsers.AddRange(userAll, userWs1, userAssigned, userNone);
        db.SaveChanges();

        return (db, ws1, ws2, village, userAll, userWs1, userAssigned, userNone);
    }

    private static byte[] CreateValidDocxCrcZip()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            zip.CreateEntry("[Content_Types].xml");
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/>");
        }
        return ms.ToArray();
    }

    private static byte[] CreateValidXlsxCrcZip()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            zip.CreateEntry("[Content_Types].xml");
            var entry = zip.CreateEntry("xl/workbook.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"/>");
        }
        return ms.ToArray();
    }

    private static byte[] CreateValidCfb(string streamName)
    {
        var data = new byte[1536];
        byte[] sig = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        Array.Copy(sig, 0, data, 0, 8);
        BitConverter.GetBytes((ushort)9).CopyTo(data, 30);
        BitConverter.GetBytes((ushort)6).CopyTo(data, 32);
        BitConverter.GetBytes((uint)1).CopyTo(data, 44);
        BitConverter.GetBytes((uint)1).CopyTo(data, 48);
        BitConverter.GetBytes((uint)0).CopyTo(data, 76);
        for (int i = 1; i < 109; i++)
        {
            BitConverter.GetBytes(0xFFFFFFFF).CopyTo(data, 76 + i * 4);
        }

        BitConverter.GetBytes(0xFFFFFFFD).CopyTo(data, 512);
        BitConverter.GetBytes(0xFFFFFFFE).CopyTo(data, 516);
        for (int i = 2; i < 128; i++)
        {
            BitConverter.GetBytes(0xFFFFFFFF).CopyTo(data, 512 + i * 4);
        }

        var rootNameBytes = Encoding.Unicode.GetBytes("Root Entry\0");
        Array.Copy(rootNameBytes, 0, data, 1024, rootNameBytes.Length);
        BitConverter.GetBytes((ushort)rootNameBytes.Length).CopyTo(data, 1024 + 64);
        data[1024 + 66] = 5;

        var streamNameBytes = Encoding.Unicode.GetBytes(streamName + "\0");
        Array.Copy(streamNameBytes, 0, data, 1152, streamNameBytes.Length);
        BitConverter.GetBytes((ushort)streamNameBytes.Length).CopyTo(data, 1152 + 64);
        data[1152 + 66] = 2;

        return data;
    }

    // =========================================================================
    // 1. WORKSTREAM SCOPING & DIRECTORY TESTS
    // =========================================================================

    [Fact]
    public async Task MatterAuthorizationService_ScopeModeAll_CanViewAllMatters()
    {
        var dbName = $"mat-scope-all-{Guid.NewGuid():N}";
        var (db, ws1, ws2, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var authService = new MatterAuthorizationService(db);

        var m1 = new Matter { Id = Guid.NewGuid(), Title = "Matter 1", VillageId = village.Id, WorkstreamId = ws1.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        var m2 = new Matter { Id = Guid.NewGuid(), Title = "Matter 2", VillageId = village.Id, WorkstreamId = ws2.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        var mLegacy = new Matter { Id = Guid.NewGuid(), Title = "Legacy Matter", VillageId = village.Id, WorkstreamId = null, Status = "Open", RecordStatus = RecordStatus.Active };
        db.Matters.AddRange(m1, m2, mLegacy);
        await db.SaveChangesAsync();

        var result = await authService.AuthorizeListQueryAsync(db.Matters, PermissionCodes.MatterView, userAll.Id);
        Assert.True(result.HasPermission);

        var authorizedMatters = await result.Query.ToListAsync();
        Assert.Contains(authorizedMatters, m => m.Id == m1.Id);
        Assert.Contains(authorizedMatters, m => m.Id == m2.Id);
        Assert.Contains(authorizedMatters, m => m.Id == mLegacy.Id); // Legacy visible to All-scoped
    }

    [Fact]
    public async Task MatterAuthorizationService_ScopeModeWorkstream_CanOnlyViewAssignedWorkstream()
    {
        var dbName = $"mat-scope-ws-{Guid.NewGuid():N}";
        var (db, ws1, ws2, village, _, userWs1, _, _) = CreateTestDbContext(dbName);
        var authService = new MatterAuthorizationService(db);

        var m1 = new Matter { Id = Guid.NewGuid(), Title = "Matter WS1", VillageId = village.Id, WorkstreamId = ws1.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        var m2 = new Matter { Id = Guid.NewGuid(), Title = "Matter WS2", VillageId = village.Id, WorkstreamId = ws2.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        var mLegacy = new Matter { Id = Guid.NewGuid(), Title = "Legacy Matter", VillageId = village.Id, WorkstreamId = null, Status = "Open", RecordStatus = RecordStatus.Active };
        db.Matters.AddRange(m1, m2, mLegacy);
        await db.SaveChangesAsync();

        var result = await authService.AuthorizeListQueryAsync(db.Matters, PermissionCodes.MatterView, userWs1.Id);
        Assert.True(result.HasPermission);

        var list = await result.Query.ToListAsync();
        Assert.Contains(list, m => m.Id == m1.Id);
        Assert.DoesNotContain(list, m => m.Id == m2.Id); // Hidden
        Assert.DoesNotContain(list, m => m.Id == mLegacy.Id); // Legacy hidden from workstream-only
    }

    [Fact]
    public async Task MatterAuthorizationService_ScopeModeAssigned_FailsClosedAcrossAllOperations()
    {
        var dbName = $"mat-scope-assigned-{Guid.NewGuid():N}";
        var (db, ws1, ws2, village, _, _, userAssigned, _) = CreateTestDbContext(dbName);
        var authService = new MatterAuthorizationService(db);

        var m1 = new Matter { Id = Guid.NewGuid(), Title = "Matter WS1", VillageId = village.Id, WorkstreamId = ws1.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        var draft = new MatterDraft { Id = Guid.NewGuid(), MatterId = m1.Id, Title = "Draft 1", DraftType = MatterDraftType.Letter, Status = MatterDraftStatus.Draft, RecordStatus = RecordStatus.Active };
        db.Matters.Add(m1);
        db.MatterDrafts.Add(draft);
        await db.SaveChangesAsync();

        // 1. AuthorizeListQueryAsync fails closed
        var listResult = await authService.AuthorizeListQueryAsync(db.Matters, PermissionCodes.MatterView, userAssigned.Id);
        Assert.False(listResult.HasPermission);
        Assert.Empty(await listResult.Query.ToListAsync());

        // 2. CanAccessMatterAsync fails closed for all permissions
        Assert.False(await authService.CanAccessMatterAsync(m1.Id, PermissionCodes.MatterView, userAssigned.Id));
        Assert.False(await authService.CanAccessMatterAsync(m1.Id, PermissionCodes.MatterEdit, userAssigned.Id));
        Assert.False(await authService.CanAccessMatterAsync(m1.Id, PermissionCodes.MatterDocumentManage, userAssigned.Id));
        Assert.False(await authService.CanAccessMatterAsync(m1.Id, PermissionCodes.MatterArchive, userAssigned.Id));

        // 3. CanCreateMatterInWorkstreamAsync fails closed
        Assert.False(await authService.CanCreateMatterInWorkstreamAsync(ws1.Id, userAssigned.Id));

        // 4. CanReclassifyMatterAsync fails closed
        Assert.False(await authService.CanReclassifyMatterAsync(m1.Id, ws2.Id, userAssigned.Id));

        // 5. CanAccessMatterDraftCapabilityAsync and CanAccessDraftAsync fail closed
        Assert.False(await authService.CanAccessMatterDraftCapabilityAsync(m1.Id, PermissionCodes.DraftView, userAssigned.Id));
        Assert.False(await authService.CanAccessMatterDraftCapabilityAsync(m1.Id, PermissionCodes.DraftCreate, userAssigned.Id));
        Assert.False(await authService.CanAccessDraftAsync(draft.Id, PermissionCodes.DraftView, userAssigned.Id));
        Assert.False(await authService.CanAccessDraftAsync(draft.Id, PermissionCodes.DraftEdit, userAssigned.Id));
    }

    [Fact]
    public async Task MatterAuthorizationService_UserWithoutPermission_Denied()
    {
        var dbName = $"mat-scope-none-{Guid.NewGuid():N}";
        var (db, ws1, _, village, _, _, _, userNone) = CreateTestDbContext(dbName);
        var authService = new MatterAuthorizationService(db);

        var m1 = new Matter { Id = Guid.NewGuid(), Title = "Matter WS1", VillageId = village.Id, WorkstreamId = ws1.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        db.Matters.Add(m1);
        await db.SaveChangesAsync();

        var result = await authService.AuthorizeListQueryAsync(db.Matters, PermissionCodes.MatterView, userNone.Id);
        Assert.False(result.HasPermission);

        var canAccess = await authService.CanAccessMatterAsync(m1.Id, PermissionCodes.MatterView, userNone.Id);
        Assert.False(canAccess);
    }

    // =========================================================================
    // 2. MATTER CREATION WORKFLOW TESTS
    // =========================================================================

    [Fact]
    public async Task MatterWorkflow_CreateMatter_MandatoryWorkstream_Success()
    {
        var dbName = $"mat-create-succ-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        var cmd = new CreateMatterCommand(
            VillageId: village.Id,
            Title: "High Court WP 4512/2026",
            MatterType: "Court Case",
            WorkstreamId: ws1.Id,
            ReferenceNumber: "HC/4512",
            Remarks: "Urgent status report",
            KhasraReferenceText: "22/1, 22/2"
        );

        var matter = await workflow.CreateMatterAsync(cmd, userAll.Id);
        Assert.NotNull(matter);
        Assert.Equal(ws1.Id, matter.WorkstreamId);
        Assert.Equal(0, matter.Revision);
        Assert.Equal("Open", matter.Status);

        // Verify initial event generated
        var ev = await db.MatterEvents.FirstOrDefaultAsync(e => e.MatterId == matter.Id);
        Assert.NotNull(ev);
        Assert.Equal(1, ev.SequenceNumber);
        Assert.Equal(MatterEventAction.Created, ev.Action);
        Assert.Equal(userAll.Id, ev.ActionByUserId);
    }

    [Fact]
    public async Task MatterWorkflow_CreateMatter_InactiveWorkstream_ThrowsException()
    {
        var dbName = $"mat-create-inactive-{Guid.NewGuid():N}";
        var (db, _, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var inactiveWs = new Workstream { Id = Guid.NewGuid(), Code = "INACTIVE", Name = "Inactive WS", IsActive = false, RecordStatus = RecordStatus.Active };
        db.Workstreams.Add(inactiveWs);
        await db.SaveChangesAsync();

        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        var cmd = new CreateMatterCommand(
            VillageId: village.Id,
            Title: "Invalid WS Matter",
            MatterType: "Court Case",
            WorkstreamId: inactiveWs.Id,
            ReferenceNumber: null,
            Remarks: null,
            KhasraReferenceText: null
        );

        var ex = await Assert.ThrowsAsync<MatterWorkflowException>(() => workflow.CreateMatterAsync(cmd, userAll.Id));
        Assert.Contains("inactive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MatterWorkflow_CreateMatter_ScopedUserOutsideWorkstream_ThrowsForbidden()
    {
        var dbName = $"mat-create-forbid-{Guid.NewGuid():N}";
        var (db, _, ws2, village, _, userWs1, _, _) = CreateTestDbContext(dbName); // userWs1 only has WS1
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        var cmd = new CreateMatterCommand(
            VillageId: village.Id,
            Title: "Escaped WS Matter",
            MatterType: "Compensation",
            WorkstreamId: ws2.Id, // WS2 not assigned to userWs1
            ReferenceNumber: null,
            Remarks: null,
            KhasraReferenceText: null
        );

        var ex = await Assert.ThrowsAsync<MatterWorkflowException>(() => workflow.CreateMatterAsync(cmd, userWs1.Id));
        Assert.Equal(403, ex.StatusCode);
    }

    // =========================================================================
    // 3. METADATA UPDATE & CONCURRENCY
    // =========================================================================

    [Fact]
    public async Task MatterWorkflow_UpdateMetadata_MatchingRevision_IncrementsRevisionAndRecordsEvent()
    {
        var dbName = $"mat-update-succ-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        var matter = await workflow.CreateMatterAsync(new CreateMatterCommand(village.Id, "Initial Title", "General", ws1.Id, null, null, null), userAll.Id);
        Assert.Equal(0, matter.Revision);

        var updateCmd = new UpdateMatterMetadataCommand(
            Title: "Updated Title",
            MatterType: "General",
            ReferenceNumber: "REF/999",
            Remarks: "Updated Remarks",
            KhasraReferenceText: "Khasra 100",
            ExpectedRevision: 0,
            Status: "Under Review"
        );

        var updated = await workflow.UpdateMetadataAsync(matter.Id, updateCmd, userAll.Id);
        Assert.Equal("Updated Title", updated.Title);
        Assert.Equal("REF/999", updated.ReferenceNumber);
        Assert.Equal("Under Review", updated.Status);
        Assert.Equal(1, updated.Revision); // Incremented
        Assert.Equal(ws1.Id, updated.WorkstreamId); // Workstream untouched

        var events = await db.MatterEvents.Where(e => e.MatterId == matter.Id).OrderBy(e => e.SequenceNumber).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.Equal(MatterEventAction.Created, events[0].Action);
        Assert.Equal(MatterEventAction.MetadataUpdated, events[1].Action);
        Assert.Equal(2, events[1].SequenceNumber);
    }

    [Fact]
    public async Task MatterWorkflow_UpdateMetadata_MismatchedRevision_ThrowsConflict()
    {
        var dbName = $"mat-update-conf-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        var matter = await workflow.CreateMatterAsync(new CreateMatterCommand(village.Id, "Initial Title", "General", ws1.Id, null, null, null), userAll.Id);

        var updateCmd = new UpdateMatterMetadataCommand(
            Title: "Stale Edit",
            MatterType: "General",
            ReferenceNumber: null,
            Remarks: null,
            KhasraReferenceText: null,
            ExpectedRevision: 99 // Wrong revision
        );

        var ex = await Assert.ThrowsAsync<MatterWorkflowException>(() => workflow.UpdateMetadataAsync(matter.Id, updateCmd, userAll.Id));
        Assert.Equal(409, ex.StatusCode);
    }

    // =========================================================================
    // 4. RECLASSIFICATION WORKFLOW
    // =========================================================================

    [Fact]
    public async Task MatterWorkflow_Reclassify_CrossWorkstreamAuthorized_Success()
    {
        var dbName = $"mat-reclass-succ-{Guid.NewGuid():N}";
        var (db, ws1, ws2, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        var matter = await workflow.CreateMatterAsync(new CreateMatterCommand(village.Id, "Transfer Case", "Court Case", ws1.Id, null, null, null), userAll.Id);

        var cmd = new ReclassifyWorkstreamCommand(
            TargetWorkstreamId: ws2.Id,
            Reason: "Reassigned to Compensation branch for execution",
            ExpectedRevision: 0
        );

        var reclassified = await workflow.ReclassifyWorkstreamAsync(matter.Id, cmd, userAll.Id);
        Assert.Equal(ws2.Id, reclassified.WorkstreamId);
        Assert.Equal(1, reclassified.Revision);

        var lastEvent = await db.MatterEvents.OrderByDescending(e => e.SequenceNumber).FirstAsync(e => e.MatterId == matter.Id);
        Assert.Equal(MatterEventAction.WorkstreamReclassified, lastEvent.Action);
        Assert.Equal(ws2.Id, lastEvent.TargetWorkstreamId);
    }

    [Fact]
    public async Task MatterWorkflow_Reclassify_EmptyReason_ThrowsException()
    {
        var dbName = $"mat-reclass-reason-{Guid.NewGuid():N}";
        var (db, ws1, ws2, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        var matter = await workflow.CreateMatterAsync(new CreateMatterCommand(village.Id, "Transfer Case", "Court Case", ws1.Id, null, null, null), userAll.Id);

        var cmd = new ReclassifyWorkstreamCommand(
            TargetWorkstreamId: ws2.Id,
            Reason: "   ", // Empty reason
            ExpectedRevision: 0
        );

        var ex = await Assert.ThrowsAsync<MatterWorkflowException>(() => workflow.ReclassifyWorkstreamAsync(matter.Id, cmd, userAll.Id));
        Assert.Contains("mandatory", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 5. ARCHIVAL & TERMINAL IMMUTABILITY
    // =========================================================================

    [Fact]
    public async Task MatterWorkflow_Archive_TerminalState_PreventsFurtherMutations()
    {
        var dbName = $"mat-archive-term-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        var matter = await workflow.CreateMatterAsync(new CreateMatterCommand(village.Id, "Closing Case", "Court Case", ws1.Id, null, null, null), userAll.Id);

        var archiveCmd = new ArchiveMatterCommand(
            Reason: "Decree executed and satisfied in full",
            ExpectedRevision: 0
        );

        var archived = await workflow.ArchiveAsync(matter.Id, archiveCmd, userAll.Id);
        Assert.Equal(RecordStatus.Archived, archived.RecordStatus);
        Assert.Equal("Open", archived.Status); // Preserves business status
        Assert.Equal(1, archived.Revision);

        // Attempting to update metadata on archived matter is rejected
        var updateCmd = new UpdateMatterMetadataCommand("New Title", "Court Case", null, null, null, 1);
        var ex = await Assert.ThrowsAsync<MatterWorkflowException>(() => workflow.UpdateMetadataAsync(matter.Id, updateCmd, userAll.Id));
        Assert.True(ex.StatusCode is 403 or 409);

        // Attempting to upload to archived matter is rejected
        using var ms = new MemoryStream(ValidPdfBytes);
        var uploadCmd = new UploadMatterDocumentCommand(ms, "doc.pdf", "application/pdf", "Other", null, 1);
        var exUpload = await Assert.ThrowsAsync<MatterWorkflowException>(() => workflow.UploadDocumentAsync(matter.Id, uploadCmd, userAll.Id));
        Assert.True(exUpload.StatusCode is 403 or 409);
    }

    // =========================================================================
    // 6. DOCUMENT FORMAT INSPECTION & SECURITY
    // =========================================================================

    [Fact]
    public void MatterDocumentValidation_PositiveMatrix_AllSupportedFormatsValid()
    {
        // 1. PDF
        using var pdf = new MemoryStream(ValidPdfBytes);
        MatterDocumentValidation.ValidateFileContent(pdf, ".pdf");
        Assert.Equal("application/pdf", MatterDocumentValidation.GetServerDerivedMimeType(".pdf"));

        // 2. PNG
        using var png = new MemoryStream(ValidPngBytes);
        MatterDocumentValidation.ValidateFileContent(png, ".png");
        Assert.Equal("image/png", MatterDocumentValidation.GetServerDerivedMimeType(".png"));

        // 3. JPEG
        byte[] jpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00];
        using var jpeg = new MemoryStream(jpegBytes);
        MatterDocumentValidation.ValidateFileContent(jpeg, ".jpg");
        Assert.Equal("image/jpeg", MatterDocumentValidation.GetServerDerivedMimeType(".jpg"));

        // 4. TIFF Little-Endian
        byte[] tiffLeBytes = [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00];
        using var tiffLe = new MemoryStream(tiffLeBytes);
        MatterDocumentValidation.ValidateFileContent(tiffLe, ".tiff");
        Assert.Equal("image/tiff", MatterDocumentValidation.GetServerDerivedMimeType(".tiff"));

        // 5. TIFF Big-Endian
        byte[] tiffBeBytes = [0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08];
        using var tiffBe = new MemoryStream(tiffBeBytes);
        MatterDocumentValidation.ValidateFileContent(tiffBe, ".tif");
        Assert.Equal("image/tiff", MatterDocumentValidation.GetServerDerivedMimeType(".tif"));

        // 6. DOC (CFB with WordDocument stream)
        var docBytes = CreateValidCfb("WordDocument");
        using var doc = new MemoryStream(docBytes);
        MatterDocumentValidation.ValidateFileContent(doc, ".doc");
        Assert.Equal("application/msword", MatterDocumentValidation.GetServerDerivedMimeType(".doc"));

        // 7. XLS (CFB with Workbook stream)
        var xlsBytes = CreateValidCfb("Workbook");
        using var xls = new MemoryStream(xlsBytes);
        MatterDocumentValidation.ValidateFileContent(xls, ".xls");
        Assert.Equal("application/vnd.ms-excel", MatterDocumentValidation.GetServerDerivedMimeType(".xls"));

        // 8. DOCX (Zip with word/document.xml)
        var docxBytes = CreateValidDocxCrcZip();
        using var docx = new MemoryStream(docxBytes);
        MatterDocumentValidation.ValidateFileContent(docx, ".docx");
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", MatterDocumentValidation.GetServerDerivedMimeType(".docx"));

        // 9. XLSX (Zip with xl/workbook.xml)
        var xlsxBytes = CreateValidXlsxCrcZip();
        using var xlsx = new MemoryStream(xlsxBytes);
        MatterDocumentValidation.ValidateFileContent(xlsx, ".xlsx");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", MatterDocumentValidation.GetServerDerivedMimeType(".xlsx"));

        // 10. TXT (Valid UTF-8)
        using var txt = new MemoryStream("Hello LAC Matter Workspace"u8.ToArray());
        MatterDocumentValidation.ValidateFileContent(txt, ".txt");
        Assert.Equal("text/plain", MatterDocumentValidation.GetServerDerivedMimeType(".txt"));
    }

    [Fact]
    public void MatterDocumentValidation_NegativeMatrix_CfbHeaderWithoutStreamFails()
    {
        // CFB header present, but stream is "Irrelevant" instead of "WordDocument"
        var bogusCfb = CreateValidCfb("SomeRandomOtherStream");
        using var doc = new MemoryStream(bogusCfb);
        var ex = Assert.Throws<MatterWorkflowException>(() => MatterDocumentValidation.ValidateFileContent(doc, ".doc"));
        Assert.Contains("legacy document", ex.Message, StringComparison.OrdinalIgnoreCase);

        using var xls = new MemoryStream(bogusCfb);
        var exXls = Assert.Throws<MatterWorkflowException>(() => MatterDocumentValidation.ValidateFileContent(xls, ".xls"));
        Assert.Contains("legacy spreadsheet", exXls.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MatterDocumentValidation_NegativeMatrix_InvalidUtf8OrNulBytesFail()
    {
        // Text with NUL byte
        byte[] nulText = [0x48, 0x65, 0x6C, 0x00, 0x6C, 0x6F];
        using var msNul = new MemoryStream(nulText);
        var exNul = Assert.Throws<MatterWorkflowException>(() => MatterDocumentValidation.ValidateFileContent(msNul, ".txt"));
        Assert.Contains("control characters", exNul.Message, StringComparison.OrdinalIgnoreCase);

        // Text with invalid UTF-8 sequence
        byte[] invalidUtf8 = [0xC0, 0xAF, 0x48, 0x65];
        using var msInvalid = new MemoryStream(invalidUtf8);
        var exInvalid = Assert.Throws<MatterWorkflowException>(() => MatterDocumentValidation.ValidateFileContent(msInvalid, ".txt"));
        Assert.Contains("UTF-8", exInvalid.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MatterDocumentValidation_NegativeMatrix_DisallowedExtensionFails()
    {
        using var exe = new MemoryStream(FakePdfBytes);
        var ex = Assert.Throws<MatterWorkflowException>(() => MatterDocumentValidation.ValidateFileContent(exe, ".exe"));
        Assert.Contains("Unsupported", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 7. AMBIGUOUS-COMMIT RETRY WITH ADVANCING MUTABLE STATE (REQ 15)
    // =========================================================================

    [Fact]
    public async Task MatterWorkflow_CreateMatter_CommitAmbiguity_AdvancesMutableState_SucceedsViaImmutableEvent()
    {
        var dbName = $"mat-ambig-create-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new MatterWorkflowService(
            db,
            storage,
            authService,
            new AllowAllAccessControlService(),
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2, onBeforeVerify: async () =>
        {
            // Verify callback only relies on immutable MatterEvent.Created
            await Task.CompletedTask;
        });

        var cmd = new CreateMatterCommand(village.Id, "Ambiguous Create", "Court Case", ws1.Id, "REF-AMB", null, null);
        var matter = await workflow.CreateMatterAsync(cmd, userAll.Id);

        Assert.NotNull(matter);
        Assert.Equal(1, strategy.VerifyCount);
        Assert.Equal("Ambiguous Create", matter.Title);
    }

    [Fact]
    public async Task MatterWorkflow_UpdateMetadata_CommitAmbiguity_AdvancesMutableState_SucceedsViaImmutableEvent()
    {
        var dbName = $"mat-ambig-meta-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);

        var matter = await new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService())
            .CreateMatterAsync(new CreateMatterCommand(village.Id, "Original Title", "Court Case", ws1.Id, null, null, null), userAll.Id);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new MatterWorkflowService(
            db,
            storage,
            authService,
            new AllowAllAccessControlService(),
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2, onBeforeVerify: async () =>
        {
            // Advance mutable state in DB before verify runs to prove verifier checks immutable event only
            var direct = await db.Matters.FirstAsync(m => m.Id == matter.Id);
            direct.Revision += 5;
            direct.Status = "OtherInterferingState";
            await db.SaveChangesAsync();
        });

        var cmd = new UpdateMatterMetadataCommand("Updated Title Ambiguous", "Court Case", "REF-123", "Remarks", null, 0);
        var updated = await workflow.UpdateMetadataAsync(matter.Id, cmd, userAll.Id);

        Assert.NotNull(updated);
        Assert.Equal(1, strategy.VerifyCount);
        Assert.Equal("Updated Title Ambiguous", updated.Title);
    }

    [Fact]
    public async Task MatterWorkflow_ReclassifyWorkstream_CommitAmbiguity_AdvancesMutableState_SucceedsViaImmutableEvent()
    {
        var dbName = $"mat-ambig-reclass-{Guid.NewGuid():N}";
        var (db, ws1, ws2, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);

        var matter = await new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService())
            .CreateMatterAsync(new CreateMatterCommand(village.Id, "Reclass Case", "Court Case", ws1.Id, null, null, null), userAll.Id);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new MatterWorkflowService(
            db,
            storage,
            authService,
            new AllowAllAccessControlService(),
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2, onBeforeVerify: async () =>
        {
            // Advance mutable state (Revision, Status) to prove verifier relies strictly on immutable event
            var direct = await db.Matters.FirstAsync(m => m.Id == matter.Id);
            direct.Revision += 10;
            direct.Status = "OtherInterferingState";
            await db.SaveChangesAsync();
        });

        var cmd = new ReclassifyWorkstreamCommand(ws2.Id, "Legitimate reclassification", 0);
        var reclassified = await workflow.ReclassifyWorkstreamAsync(matter.Id, cmd, userAll.Id);

        Assert.NotNull(reclassified);
        Assert.Equal(1, strategy.VerifyCount);
        Assert.Equal(ws2.Id, reclassified.WorkstreamId);
    }

    [Fact]
    public async Task MatterWorkflow_UploadDocument_CommitAmbiguity_AdvancesMutableState_SucceedsViaImmutableEvent()
    {
        var dbName = $"mat-ambig-upload-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);

        var matter = await new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService())
            .CreateMatterAsync(new CreateMatterCommand(village.Id, "Upload Case", "Court Case", ws1.Id, null, null, null), userAll.Id);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new MatterWorkflowService(
            db,
            storage,
            authService,
            new AllowAllAccessControlService(),
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2, onBeforeVerify: async () =>
        {
            var direct = await db.Matters.FirstAsync(m => m.Id == matter.Id);
            direct.Revision += 5;
            await db.SaveChangesAsync();
        });

        using var ms = new MemoryStream(ValidPdfBytes);
        var cmd = new UploadMatterDocumentCommand(ms, "affidavit.pdf", "application/pdf", "Application", "App Affidavit", 0);
        var matterDoc = await workflow.UploadDocumentAsync(matter.Id, cmd, userAll.Id);

        Assert.NotNull(matterDoc);
        Assert.Equal(1, strategy.VerifyCount);
        Assert.Equal(1, storage.SaveCount);
        Assert.Equal(0, storage.DeleteCount);
        Assert.Single(storage.Files);
    }

    [Fact]
    public async Task MatterWorkflow_LinkExistingDocument_CommitAmbiguity_AdvancesMutableState_SucceedsViaImmutableEvent()
    {
        var dbName = $"mat-ambig-link-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);

        var matter = await new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService())
            .CreateMatterAsync(new CreateMatterCommand(village.Id, "Link Case", "Court Case", ws1.Id, null, null, null), userAll.Id);

        var award = new Award { Id = Guid.NewGuid(), AwardNumber = "AW-LINK-1", RecordStatus = RecordStatus.Active };
        var candidateDoc = new Document { Id = Guid.NewGuid(), OriginalFileName = "award_map.pdf", StoragePath = "p1", MimeType = "application/pdf", Sha256Hash = "h1", RecordStatus = RecordStatus.Active, Status = "Active" };
        var docAward = new DocumentAward { Id = Guid.NewGuid(), DocumentId = candidateDoc.Id, AwardId = award.Id };
        var matterAward = new MatterAward { Id = Guid.NewGuid(), MatterId = matter.Id, AwardId = award.Id };

        db.Awards.Add(award);
        db.Documents.Add(candidateDoc);
        db.DocumentAwards.Add(docAward);
        db.MatterAwards.Add(matterAward);
        await db.SaveChangesAsync();

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new MatterWorkflowService(
            db,
            storage,
            authService,
            new AllowAllAccessControlService(),
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2, onBeforeVerify: async () =>
        {
            var direct = await db.Matters.FirstAsync(m => m.Id == matter.Id);
            direct.Revision += 5;
            await db.SaveChangesAsync();
        });

        var cmd = new LinkExistingDocumentCommand(candidateDoc.Id, "Award Map", "Candidate Attachment", 0);
        var linked = await workflow.LinkExistingDocumentAsync(matter.Id, cmd, userAll.Id);

        Assert.NotNull(linked);
        Assert.Equal(1, strategy.VerifyCount);
        Assert.Equal(candidateDoc.Id, linked.DocumentId);
    }

    [Fact]
    public async Task MatterWorkflow_Archive_CommitAmbiguity_AdvancesMutableState_SucceedsViaImmutableEvent()
    {
        var dbName = $"mat-ambig-archive-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);

        var matter = await new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService())
            .CreateMatterAsync(new CreateMatterCommand(village.Id, "Archive Case", "Court Case", ws1.Id, null, null, null), userAll.Id);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new MatterWorkflowService(
            db,
            storage,
            authService,
            new AllowAllAccessControlService(),
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2, onBeforeVerify: async () =>
        {
            // Advance mutable state (Revision, Status) to prove verifier relies strictly on immutable event
            var direct = await db.Matters.FirstAsync(m => m.Id == matter.Id);
            direct.Revision += 10;
            direct.Status = "OtherInterferingState";
            await db.SaveChangesAsync();
        });

        var cmd = new ArchiveMatterCommand("Disposed in full", 0);
        var archived = await workflow.ArchiveAsync(matter.Id, cmd, userAll.Id);

        Assert.NotNull(archived);
        Assert.Equal(1, strategy.VerifyCount);
        Assert.Equal(RecordStatus.Archived, archived.RecordStatus);
    }

    // =========================================================================
    // 8. UPLOAD COMPENSATION RACE CONDITIONS (REQ 14)
    // =========================================================================

    [Fact]
    public async Task MatterWorkflow_UploadDocument_CompensatingAction_DeletesFileWhenTransactionAborted()
    {
        var dbName = $"mat-comp-abort-{Guid.NewGuid():N}";
        var failingInterceptor = new FailingSaveChangesInterceptor();
        var builder = new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(failingInterceptor)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));

        var db = new LacDbContext(builder.Options);
        var ws = new Workstream { Id = Guid.NewGuid(), Code = "WS1", Name = "WS1", IsActive = true, RecordStatus = RecordStatus.Active };
        var village = new Village { Id = Guid.NewGuid(), Name = "V1", RecordStatus = RecordStatus.Active };
        var matter = new Matter { Id = Guid.NewGuid(), Title = "M1", VillageId = village.Id, WorkstreamId = ws.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        var user = new AppUser { Id = Guid.NewGuid(), Username = "u_clean", NormalizedUsername = "U_CLEAN", DisplayName = "User Clean", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active };
        var role = new Role { Id = Guid.NewGuid(), Code = "R_CLEAN", Name = "Clean Role", IsActive = true, RecordStatus = RecordStatus.Active };
        var perm = new Permission { Id = Guid.NewGuid(), Code = PermissionCodes.MatterDocumentManage, Name = "Doc", Category = "Matter" };
        role.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = perm.Id, ScopeMode = ScopeMode.All });
        user.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id });

        db.Workstreams.Add(ws);
        db.Villages.Add(village);
        db.Matters.Add(matter);
        db.Permissions.Add(perm);
        db.Roles.Add(role);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        failingInterceptor.FailOnSave = true;

        using var ms = new MemoryStream(ValidPdfBytes);
        var cmd = new UploadMatterDocumentCommand(ms, "application.pdf", "application/pdf", "Application", "My App", 0);

        await Assert.ThrowsAsync<DbUpdateException>(() => workflow.UploadDocumentAsync(matter.Id, cmd, user.Id));

        // When transaction aborted, physical file must be cleaned up
        Assert.Equal(1, storage.SaveCount);
        Assert.Equal(1, storage.DeleteCount);
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task MatterWorkflow_UploadDocument_CompensatingAction_RetainsFileWhenTransactionCommitted()
    {
        var dbName = $"mat-comp-retain-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);

        var matter = new Matter { Id = Guid.NewGuid(), Title = "M Committed", VillageId = village.Id, WorkstreamId = ws1.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        db.Matters.Add(matter);
        await db.SaveChangesAsync();

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new MatterWorkflowService(
            db,
            storage,
            authService,
            new AllowAllAccessControlService(),
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2);

        using var ms = new MemoryStream(ValidPdfBytes);
        var cmd = new UploadMatterDocumentCommand(ms, "statement.pdf", "application/pdf", "Application", "Statement Doc", 0);

        var matterDoc = await workflow.UploadDocumentAsync(matter.Id, cmd, userAll.Id);
        Assert.NotNull(matterDoc);

        // Verification succeeded and file was retained!
        Assert.Equal(1, storage.SaveCount);
        Assert.Equal(0, storage.DeleteCount);
        Assert.Single(storage.Files);
    }

    // =========================================================================
    // 9. OPTIONAL AWARD IN MATTER CREATION (REQ 23)
    // =========================================================================

    [Fact]
    public async Task MatterWorkflow_CreateMatter_WithoutAward_SucceedsWithEmptyAwardLinks()
    {
        var dbName = $"mat-opt-noaward-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        var cmd = new CreateMatterCommand(village.Id, "Matter Without Award", "Court Case", ws1.Id, null, null, null, AwardId: null);
        var matter = await workflow.CreateMatterAsync(cmd, userAll.Id);

        Assert.NotNull(matter);
        var awardLinks = await db.MatterAwards.Where(ma => ma.MatterId == matter.Id).ToListAsync();
        Assert.Empty(awardLinks);
    }

    [Fact]
    public async Task MatterWorkflow_CreateMatter_WithValidAward_CreatesAtomicMatterAward()
    {
        var dbName = $"mat-opt-award-ok-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        var award = new Award { Id = Guid.NewGuid(), AwardNumber = "AW-ATOM-1", RecordStatus = RecordStatus.Active };
        db.Awards.Add(award);
        db.AwardVillages.Add(new AwardVillage { AwardId = award.Id, VillageId = village.Id });
        await db.SaveChangesAsync();

        var cmd = new CreateMatterCommand(village.Id, "Matter With Award", "Court Case", ws1.Id, null, null, null, AwardId: award.Id);
        var matter = await workflow.CreateMatterAsync(cmd, userAll.Id);

        Assert.NotNull(matter);
        var awardLinks = await db.MatterAwards.Where(ma => ma.MatterId == matter.Id).ToListAsync();
        var link = Assert.Single(awardLinks);
        Assert.Equal(award.Id, link.AwardId);
        Assert.True(link.IsPrimary);
    }

    [Fact]
    public async Task MatterWorkflow_CreateMatter_WithMismatchedVillageAward_ThrowsBadRequest()
    {
        var dbName = $"mat-opt-award-mismatch-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var otherVillage = new Village { Id = Guid.NewGuid(), Name = "Other Village", RecordStatus = RecordStatus.Active };
        var award = new Award { Id = Guid.NewGuid(), AwardNumber = "AW-OTHER-1", RecordStatus = RecordStatus.Active };

        db.Villages.Add(otherVillage);
        db.Awards.Add(award);
        db.AwardVillages.Add(new AwardVillage { AwardId = award.Id, VillageId = otherVillage.Id }); // Different village!
        await db.SaveChangesAsync();

        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        var cmd = new CreateMatterCommand(village.Id, "Mismatch Matter", "Court Case", ws1.Id, null, null, null, AwardId: award.Id);
        var ex = await Assert.ThrowsAsync<MatterWorkflowException>(() => workflow.CreateMatterAsync(cmd, userAll.Id));
        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("village", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MatterWorkflow_CreateMatter_WithAward_WhenUserLacksAwardView_ThrowsForbidden()
    {
        var dbName = $"mat-opt-award-denied-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var award = new Award { Id = Guid.NewGuid(), AwardNumber = "AW-DENIED-1", RecordStatus = RecordStatus.Active };
        db.Awards.Add(award);
        db.AwardVillages.Add(new AwardVillage { AwardId = award.Id, VillageId = village.Id });
        await db.SaveChangesAsync();

        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new DenyAllAccessControlServiceStub());

        var cmd = new CreateMatterCommand(village.Id, "Denied Award Matter", "Court Case", ws1.Id, null, null, null, AwardId: award.Id);
        var ex = await Assert.ThrowsAsync<MatterWorkflowException>(() => workflow.CreateMatterAsync(cmd, userAll.Id));
        Assert.Equal(403, ex.StatusCode);
        Assert.Contains("Award.View", ex.Message);
    }

    // =========================================================================
    // 10. DRAFT SECURITY HARDENING (REQ 24)
    // =========================================================================

    [Fact]
    public async Task MatterAuthorization_DraftAccess_RequiresMatterViewAndDraftCapability()
    {
        var dbName = $"mat-draft-sec-{Guid.NewGuid():N}";
        var (db, ws1, ws2, village, userAll, userWs1, _, userNone) = CreateTestDbContext(dbName);
        var authService = new MatterAuthorizationService(db);

        var matterWs1 = new Matter { Id = Guid.NewGuid(), Title = "M WS1", VillageId = village.Id, WorkstreamId = ws1.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        var matterWs2 = new Matter { Id = Guid.NewGuid(), Title = "M WS2", VillageId = village.Id, WorkstreamId = ws2.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        var draftWs1 = new MatterDraft { Id = Guid.NewGuid(), MatterId = matterWs1.Id, Title = "Draft 1", DraftType = MatterDraftType.Letter, Status = MatterDraftStatus.Draft, RecordStatus = RecordStatus.Active };
        var draftWs2 = new MatterDraft { Id = Guid.NewGuid(), MatterId = matterWs2.Id, Title = "Draft 2", DraftType = MatterDraftType.Letter, Status = MatterDraftStatus.Draft, RecordStatus = RecordStatus.Active };

        db.Matters.AddRange(matterWs1, matterWs2);
        db.MatterDrafts.AddRange(draftWs1, draftWs2);
        await db.SaveChangesAsync();

        // userAll has ScopeMode.All -> access granted
        Assert.True(await authService.CanAccessDraftAsync(draftWs1.Id, PermissionCodes.DraftView, userAll.Id));
        Assert.True(await authService.CanAccessMatterDraftCapabilityAsync(matterWs1.Id, PermissionCodes.DraftCreate, userAll.Id));

        // userWs1 is member of WS1 -> granted for WS1, denied for WS2
        Assert.True(await authService.CanAccessDraftAsync(draftWs1.Id, PermissionCodes.DraftView, userWs1.Id));
        Assert.False(await authService.CanAccessDraftAsync(draftWs2.Id, PermissionCodes.DraftView, userWs1.Id));

        // userNone lacks permissions -> denied
        Assert.False(await authService.CanAccessDraftAsync(draftWs1.Id, PermissionCodes.DraftView, userNone.Id));
    }

    [Fact]
    public async Task MatterAuthorization_DraftAccess_ArchivedMatter_Denied()
    {
        var dbName = $"mat-draft-arch-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var authService = new MatterAuthorizationService(db);

        var archivedMatter = new Matter { Id = Guid.NewGuid(), Title = "Archived M", VillageId = village.Id, WorkstreamId = ws1.Id, Status = "Disposed", RecordStatus = RecordStatus.Archived };
        var draft = new MatterDraft { Id = Guid.NewGuid(), MatterId = archivedMatter.Id, Title = "Draft in Archived", DraftType = MatterDraftType.Letter, Status = MatterDraftStatus.Draft, RecordStatus = RecordStatus.Active };

        db.Matters.Add(archivedMatter);
        db.MatterDrafts.Add(draft);
        await db.SaveChangesAsync();

        // Archived matter fails CanAccessDraftAsync and CanAccessMatterDraftCapabilityAsync
        Assert.False(await authService.CanAccessDraftAsync(draft.Id, PermissionCodes.DraftView, userAll.Id));
        Assert.False(await authService.CanAccessMatterDraftCapabilityAsync(archivedMatter.Id, PermissionCodes.DraftCreate, userAll.Id));
    }

    [Fact]
    public async Task MatterAuthorization_DraftAccess_UnclassifiedMatterWithWorkstreamScope_Denied()
    {
        var dbName = $"mat-draft-unclass-{Guid.NewGuid():N}";
        var (db, _, _, village, _, userWs1, _, _) = CreateTestDbContext(dbName);
        var authService = new MatterAuthorizationService(db);

        // Unclassified matter (WorkstreamId == null)
        var unclassMatter = new Matter { Id = Guid.NewGuid(), Title = "Legacy M", VillageId = village.Id, WorkstreamId = null, Status = "Open", RecordStatus = RecordStatus.Active };
        var draft = new MatterDraft { Id = Guid.NewGuid(), MatterId = unclassMatter.Id, Title = "Draft Unclass", DraftType = MatterDraftType.Letter, Status = MatterDraftStatus.Draft, RecordStatus = RecordStatus.Active };

        db.Matters.Add(unclassMatter);
        db.MatterDrafts.Add(draft);
        await db.SaveChangesAsync();

        // userWs1 has ScopeMode.Workstream -> cannot access unclassified matter drafts
        Assert.False(await authService.CanAccessDraftAsync(draft.Id, PermissionCodes.DraftView, userWs1.Id));
        Assert.False(await authService.CanAccessMatterDraftCapabilityAsync(unclassMatter.Id, PermissionCodes.DraftCreate, userWs1.Id));
    }

    // =========================================================================
    // 11. TRANSITIVE AWARD LEAK PREVENTION (REQ 25)
    // =========================================================================

    [Fact]
    public async Task Matter_DoesNotTransitivelyLeakAwardDocuments()
    {
        var dbName = $"mat-leak-test-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        var award = new Award { Id = Guid.NewGuid(), AwardNumber = "AW-LEAK-1", RecordStatus = RecordStatus.Active };
        var awardDoc = new Document { Id = Guid.NewGuid(), OriginalFileName = "award_only.pdf", StoragePath = "p1", RecordStatus = RecordStatus.Active, Status = "Active" };
        db.Awards.Add(award);
        db.Documents.Add(awardDoc);
        db.AwardVillages.Add(new AwardVillage { AwardId = award.Id, VillageId = village.Id });
        db.DocumentAwards.Add(new DocumentAward { AwardId = award.Id, DocumentId = awardDoc.Id, CoreDocumentRole = "Award" });
        await db.SaveChangesAsync();

        var cmd = new CreateMatterCommand(village.Id, "Matter With Award", "Court Case", ws1.Id, null, null, null, AwardId: award.Id);
        var matter = await workflow.CreateMatterAsync(cmd, userAll.Id);

        // Explicit MatterDocument joins MUST BE EMPTY initially
        var joins = await db.MatterDocuments.Where(md => md.MatterId == matter.Id).ToListAsync();
        Assert.Empty(joins);

        // Uploading an explicit matter document creates a single explicit join
        using var ms = new MemoryStream(ValidPdfBytes);
        var uploadCmd = new UploadMatterDocumentCommand(ms, "matter_specific.pdf", "application/pdf", "Application", "Explicit doc", 0);
        var uploaded = await workflow.UploadDocumentAsync(matter.Id, uploadCmd, userAll.Id);

        var matterDocs = await db.MatterDocuments.Where(md => md.MatterId == matter.Id).ToListAsync();
        var singleDoc = Assert.Single(matterDocs);
        Assert.Equal(uploaded.DocumentId, singleDoc.DocumentId);
        Assert.NotEqual(awardDoc.Id, singleDoc.DocumentId); // Award doc was NEVER copied
    }

    // =========================================================================
    // 12. BARE DOCUMENTVILLAGE REGRESSION PREVENTION (REQ 26)
    // =========================================================================

    [Fact]
    public async Task MatterWorkflow_BareDocumentVillage_NotEligibleAndCannotBeLinked()
    {
        var dbName = $"mat-bare-village-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService, new AllowAllAccessControlService());

        var matter = await workflow.CreateMatterAsync(new CreateMatterCommand(village.Id, "Matter LR Test", "Court Case", ws1.Id, null, null, null), userAll.Id);

        // Document linked ONLY to DocumentVillage (bare)
        var bareDoc = new Document { Id = Guid.NewGuid(), OriginalFileName = "bare_village.pdf", StoragePath = "b1", RecordStatus = RecordStatus.Active, Status = "Active" };
        db.Documents.Add(bareDoc);
        db.DocumentVillages.Add(new DocumentVillage { VillageId = village.Id, DocumentId = bareDoc.Id });
        await db.SaveChangesAsync();

        // 1. Provenance helper does NOT include bare DocumentVillage
        var eligibleMap = await MatterDocumentProvenanceHelper.GetEligibleDocumentCandidateMapAsync(db, matter, new AllowAllAccessControlService());
        Assert.DoesNotContain(bareDoc.Id, eligibleMap.Keys);

        // 2. Attempting to link bare DocumentVillage throws provenance error
        var linkCmd = new LinkExistingDocumentCommand(bareDoc.Id, "Other", "Bare Doc", 0);
        var ex = await Assert.ThrowsAsync<MatterWorkflowException>(() => workflow.LinkExistingDocumentAsync(matter.Id, linkCmd, userAll.Id));
        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("provenance", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 13. DAK & OUTWARD INTEGRATION SEAMS
    // =========================================================================

    [Fact]
    public async Task Seam_DakLink_RequiresMatterViewPermission()
    {
        var dbName = $"mat-seam-dak-{Guid.NewGuid():N}";
        var (db, ws1, _, village, _, userWs1, _, userNone) = CreateTestDbContext(dbName);
        var authService = new MatterAuthorizationService(db);

        var matter = new Matter { Id = Guid.NewGuid(), Title = "Litigation File", VillageId = village.Id, WorkstreamId = ws1.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        db.Matters.Add(matter);
        await db.SaveChangesAsync();

        // userWs1 has WS1 access -> permitted
        var canWs1 = await authService.CanAccessMatterAsync(matter.Id, PermissionCodes.MatterView, userWs1.Id);
        Assert.True(canWs1);

        // userNone lacks access -> denied
        var canNone = await authService.CanAccessMatterAsync(matter.Id, PermissionCodes.MatterView, userNone.Id);
        Assert.False(canNone);
    }

    [Fact]
    public async Task Seam_OutwardLink_RequiresMatterViewPermission()
    {
        var dbName = $"mat-seam-outward-{Guid.NewGuid():N}";
        var (db, ws1, ws2, village, _, userWs1, _, _) = CreateTestDbContext(dbName);
        var authService = new MatterAuthorizationService(db);

        var matterInWs2 = new Matter { Id = Guid.NewGuid(), Title = "Compensation File", VillageId = village.Id, WorkstreamId = ws2.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        db.Matters.Add(matterInWs2);
        await db.SaveChangesAsync();

        // userWs1 is only in WS1, matter is in WS2 -> forbidden from linking to outward
        var canAccess = await authService.CanAccessMatterAsync(matterInWs2.Id, PermissionCodes.MatterView, userWs1.Id);
        Assert.False(canAccess);
    }

    // =========================================================================
    // 14. POST-IMPLEMENTATION HARDENING REGRESSION TESTS
    // =========================================================================

    [Fact]
    public async Task MatterWorkflow_UploadDocument_PreCommitFailure_RetriesAndSucceeds_AttemptCountGt1()
    {
        var dbName = $"mat-true-retry-upload-{Guid.NewGuid():N}";
        var interceptor = new TrackingSaveChangesInterceptor();
        var (db, ws1, _, village, userAll, _, _, _) = CreateTestDbContext(dbName, interceptor);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new MatterWorkflowService(
            db,
            storage,
            authService,
            new AllowAllAccessControlService(),
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: false, maxRetries: 2);

        var matter = new Matter
        {
            Id = Guid.NewGuid(),
            VillageId = village.Id,
            WorkstreamId = ws1.Id,
            Revision = 0,
            Title = "Retry Upload Matter",
            MatterType = "Court Case",
            Status = "Open",
            RecordStatus = RecordStatus.Active
        };
        db.Matters.Add(matter);
        db.SaveChanges();

        // Arm interceptor to fail exactly once on the first async SaveChanges call (before commit)
        interceptor.FailTimes = 1;

        using var ms = new MemoryStream(ValidPdfBytes);
        var cmd = new UploadMatterDocumentCommand(ms, "retry_upload.pdf", "application/pdf", "Evidence", "Retry Document", 0);

        var matterDoc = await workflow.UploadDocumentAsync(matter.Id, cmd, userAll.Id);

        Assert.NotNull(matterDoc);
        Assert.True(strategy.AttemptCount > 1, $"Expected AttemptCount > 1 but got {strategy.AttemptCount}");
        Assert.Equal(2, strategy.AttemptCount);
        Assert.Equal(1, storage.SaveCount);

        var updatedMatter = await db.Matters.FirstAsync(m => m.Id == matter.Id);
        Assert.Equal(1, updatedMatter.Revision);

        var evCount = await db.MatterEvents.CountAsync(e => e.MatterId == matter.Id && e.Action == MatterEventAction.DocumentUploaded);
        Assert.Equal(1, evCount);

        var matterDocCount = await db.MatterDocuments.CountAsync(md => md.MatterId == matter.Id);
        Assert.Equal(1, matterDocCount);

        var docCount = await db.Documents.CountAsync(d => d.Id == matterDoc.DocumentId);
        Assert.Equal(1, docCount);
    }

    [Fact]
    public async Task EligibleDocuments_Requires_MatterDocumentManage_ApiRegression()
    {
        using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "DynamicTest";
                    options.DefaultChallengeScheme = "DynamicTest";
                }).AddScheme<AuthenticationSchemeOptions, DynamicTestAuthHandler>("DynamicTest", _ => { });
            });
        });

        var client = customFactory.CreateClient();

        Guid villageId, workstreamId, matterId, userViewOnlyId, userDocManageId;
        using (var scope = customFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var village = await db.Villages.FirstAsync();
            villageId = village.Id;
            var ws = await db.Workstreams.FirstAsync(w => w.IsActive && w.RecordStatus == RecordStatus.Active);
            workstreamId = ws.Id;

            var matter = new Matter
            {
                Id = Guid.NewGuid(),
                VillageId = villageId,
                WorkstreamId = workstreamId,
                Title = "Eligible Docs Matter",
                MatterType = "Court Case",
                Status = "Open",
                RecordStatus = RecordStatus.Active
            };
            db.Matters.Add(matter);

            var pView = await db.Permissions.FirstAsync(p => p.Code == PermissionCodes.MatterView);
            var pDocManage = await db.Permissions.FirstAsync(p => p.Code == PermissionCodes.MatterDocumentManage);

            var roleViewOnly = new Role { Id = Guid.NewGuid(), Code = $"R_VO_{Guid.NewGuid():N}"[..12], Name = "View Only", IsActive = true, RecordStatus = RecordStatus.Active };
            roleViewOnly.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = roleViewOnly.Id, PermissionId = pView.Id, ScopeMode = ScopeMode.All });

            var roleDocManage = new Role { Id = Guid.NewGuid(), Code = $"R_DM_{Guid.NewGuid():N}"[..12], Name = "Doc Manage", IsActive = true, RecordStatus = RecordStatus.Active };
            roleDocManage.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = roleDocManage.Id, PermissionId = pView.Id, ScopeMode = ScopeMode.All });
            roleDocManage.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = roleDocManage.Id, PermissionId = pDocManage.Id, ScopeMode = ScopeMode.All });

            var userView = new AppUser { Id = Guid.NewGuid(), Username = $"u_vo_{Guid.NewGuid():N}"[..12], NormalizedUsername = "U_VO", DisplayName = "View User", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active };
            userView.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = userView.Id, RoleId = roleViewOnly.Id });

            var userDoc = new AppUser { Id = Guid.NewGuid(), Username = $"u_dm_{Guid.NewGuid():N}"[..12], NormalizedUsername = "U_DM", DisplayName = "Doc User", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active };
            userDoc.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = userDoc.Id, RoleId = roleDocManage.Id });

            db.Roles.AddRange(roleViewOnly, roleDocManage);
            db.AppUsers.AddRange(userView, userDoc);
            await db.SaveChangesAsync();

            matterId = matter.Id;
            userViewOnlyId = userView.Id;
            userDocManageId = userDoc.Id;
        }

        // 1. User with Matter.View ONLY receives 403 Forbidden on eligible-documents
        var reqViewOnly = new HttpRequestMessage(HttpMethod.Get, $"/api/matters/{matterId}/eligible-documents");
        reqViewOnly.Headers.Add("X-Test-User-Id", userViewOnlyId.ToString());
        var resViewOnly = await client.SendAsync(reqViewOnly);
        Assert.Equal(HttpStatusCode.Forbidden, resViewOnly.StatusCode);

        // 2. User with Matter.Document.Manage receives 200 OK on eligible-documents
        var reqDocManage = new HttpRequestMessage(HttpMethod.Get, $"/api/matters/{matterId}/eligible-documents");
        reqDocManage.Headers.Add("X-Test-User-Id", userDocManageId.ToString());
        var resDocManage = await client.SendAsync(reqDocManage);
        Assert.Equal(HttpStatusCode.OK, resDocManage.StatusCode);
    }

    [Fact]
    public async Task MatterDocuments_List_ExcludesLogicallyInactiveDocument()
    {
        Guid matterId, activeDocId, inactiveDocId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var village = await db.Villages.FirstAsync();
            var ws = await db.Workstreams.FirstAsync(w => w.IsActive && w.RecordStatus == RecordStatus.Active);

            var matter = new Matter
            {
                Id = Guid.NewGuid(),
                VillageId = village.Id,
                WorkstreamId = ws.Id,
                Title = "Docs Filter Test Matter",
                MatterType = "Court Case",
                Status = "Open",
                RecordStatus = RecordStatus.Active
            };
            db.Matters.Add(matter);

            var activeDoc = new Document
            {
                Id = Guid.NewGuid(),
                OriginalFileName = "active.pdf",
                StoragePath = "path1",
                MimeType = "application/pdf",
                DocumentType = "MatterDocument",
                UploadedBy = "Admin",
                RecordStatus = RecordStatus.Active,
                Status = "Active"
            };
            var inactiveDoc = new Document
            {
                Id = Guid.NewGuid(),
                OriginalFileName = "inactive.pdf",
                StoragePath = "path2",
                MimeType = "application/pdf",
                DocumentType = "MatterDocument",
                UploadedBy = "Admin",
                RecordStatus = RecordStatus.Active,
                Status = "Inactive"
            };
            db.Documents.AddRange(activeDoc, inactiveDoc);

            db.MatterDocuments.Add(new MatterDocument
            {
                Id = Guid.NewGuid(),
                MatterId = matter.Id,
                DocumentId = activeDoc.Id,
                DisplayName = "Active Document",
                DocumentRole = "Other"
            });
            db.MatterDocuments.Add(new MatterDocument
            {
                Id = Guid.NewGuid(),
                MatterId = matter.Id,
                DocumentId = inactiveDoc.Id,
                DisplayName = "Inactive Document",
                DocumentRole = "Other"
            });

            await db.SaveChangesAsync();
            matterId = matter.Id;
            activeDocId = activeDoc.Id;
            inactiveDocId = inactiveDoc.Id;
        }

        var response = await _client.GetAsync($"/api/matters/{matterId}/documents");
        response.EnsureSuccessStatusCode();

        var docs = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(docs);
        var returnedDocIds = docs.Select(d => d.GetProperty("documentId").GetGuid()).ToList();

        Assert.Contains(activeDocId, returnedDocIds);
        Assert.DoesNotContain(inactiveDocId, returnedDocIds);
    }

    [Fact]
    public async Task MatterDocument_Content_RejectsInactiveBusinessStatusDocument()
    {
        Guid matterId, inactiveDocId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var village = await db.Villages.FirstAsync();
            var ws = await db.Workstreams.FirstAsync(w => w.IsActive && w.RecordStatus == RecordStatus.Active);

            var matter = new Matter
            {
                Id = Guid.NewGuid(),
                VillageId = village.Id,
                WorkstreamId = ws.Id,
                Title = "Download Inactive Matter",
                MatterType = "Court Case",
                Status = "Open",
                RecordStatus = RecordStatus.Active
            };
            db.Matters.Add(matter);

            var inactiveDoc = new Document
            {
                Id = Guid.NewGuid(),
                OriginalFileName = "inactive_content.pdf",
                StoragePath = "path_inact",
                MimeType = "application/pdf",
                DocumentType = "MatterDocument",
                UploadedBy = "Admin",
                RecordStatus = RecordStatus.Active,
                Status = "Inactive"
            };
            db.Documents.Add(inactiveDoc);

            db.MatterDocuments.Add(new MatterDocument
            {
                Id = Guid.NewGuid(),
                MatterId = matter.Id,
                DocumentId = inactiveDoc.Id,
                DisplayName = "Inactive Download Doc",
                DocumentRole = "Other"
            });

            await db.SaveChangesAsync();
            matterId = matter.Id;
            inactiveDocId = inactiveDoc.Id;
        }

        var response = await _client.GetAsync($"/api/matters/{matterId}/documents/{inactiveDocId}/content");
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Village_DocumentsAndCounts_ExcludeInactiveAwardFamilyDocuments()
    {
        Guid villageId, activeDocId, inactiveDocId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var v = new Village
            {
                Id = Guid.NewGuid(),
                Name = $"CountTestVillage_{Guid.NewGuid():N}"[..20],
                SubDivisionId = (await db.SubDivisions.FirstAsync()).Id,
                RecordStatus = RecordStatus.Active
            };
            db.Villages.Add(v);

            var project = await db.AcquisitionProjects.FirstAsync();
            var award = new Award
            {
                Id = Guid.NewGuid(),
                AwardNumber = $"AWD_CNT_{Guid.NewGuid():N}"[..12],
                AcquisitionProjectId = project.Id,
                Status = "Published",
                RecordStatus = RecordStatus.Active
            };
            db.Awards.Add(award);

            var activeDoc = new Document
            {
                Id = Guid.NewGuid(),
                OriginalFileName = "active_award.pdf",
                StoragePath = "act_path",
                DocumentType = "AwardDocument",
                UploadedBy = "Admin",
                RecordStatus = RecordStatus.Active,
                Status = "Active"
            };
            var inactiveDoc = new Document
            {
                Id = Guid.NewGuid(),
                OriginalFileName = "inactive_award.pdf",
                StoragePath = "inact_path",
                DocumentType = "AwardDocument",
                UploadedBy = "Admin",
                RecordStatus = RecordStatus.Active,
                Status = "Inactive"
            };
            db.Documents.AddRange(activeDoc, inactiveDoc);

            db.DocumentVillages.Add(new DocumentVillage { VillageId = v.Id, DocumentId = activeDoc.Id });
            db.DocumentVillages.Add(new DocumentVillage { VillageId = v.Id, DocumentId = inactiveDoc.Id });

            db.DocumentAwards.Add(new DocumentAward { AwardId = award.Id, DocumentId = activeDoc.Id });
            db.DocumentAwards.Add(new DocumentAward { AwardId = award.Id, DocumentId = inactiveDoc.Id });

            db.AwardVillages.Add(new AwardVillage { VillageId = v.Id, AwardId = award.Id });

            await db.SaveChangesAsync();
            villageId = v.Id;
            activeDocId = activeDoc.Id;
            inactiveDocId = inactiveDoc.Id;
        }

        // 1. GET /api/villages/{id}/documents returns only active document
        var docsRes = await _client.GetAsync($"/api/villages/{villageId}/documents");
        docsRes.EnsureSuccessStatusCode();
        var docs = await docsRes.Content.ReadFromJsonAsync<List<DocumentListItem>>();
        Assert.NotNull(docs);
        Assert.Contains(docs, d => d.Id == activeDocId);
        Assert.DoesNotContain(docs, d => d.Id == inactiveDocId);

        // 2. GET /api/villages/{id} DocumentCount is 1
        var detailRes = await _client.GetAsync($"/api/villages/{villageId}");
        detailRes.EnsureSuccessStatusCode();
        var detail = await detailRes.Content.ReadFromJsonAsync<VillageDetail>();
        Assert.NotNull(detail);
        Assert.Equal(1, detail.DocumentCount);

        // 3. GET /api/villages/{id}/overview Village.DocumentCount is 1
        var overviewRes = await _client.GetAsync($"/api/villages/{villageId}/overview");
        overviewRes.EnsureSuccessStatusCode();
        var overview = await overviewRes.Content.ReadFromJsonAsync<VillageOverviewResponse>();
        Assert.NotNull(overview);
        Assert.Equal(1, overview.Village.DocumentCount);
    }
}

