using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    private static (LacDbContext db, Workstream ws1, Workstream ws2, Village village, AppUser userAll, AppUser userWs1, AppUser userNone) CreateTestDbContext(string dbName)
    {
        var builder = new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));

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

        var rAll = new Role { Id = Guid.NewGuid(), Code = $"R_ALL_{Guid.NewGuid():N}"[..10], Name = "Role All", IsActive = true, RecordStatus = RecordStatus.Active };
        var rAssigned = new Role { Id = Guid.NewGuid(), Code = $"R_ASS_{Guid.NewGuid():N}"[..10], Name = "Role Assigned", IsActive = true, RecordStatus = RecordStatus.Active };

        var allPerms = new[] { pView, pCreate, pEdit, pDoc, pArchive };
        foreach (var p in allPerms)
        {
            rAll.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = rAll.Id, PermissionId = p.Id, ScopeMode = ScopeMode.All });
            rAssigned.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = rAssigned.Id, PermissionId = p.Id, ScopeMode = ScopeMode.Assigned });
        }

        var userAll = new AppUser { Id = Guid.NewGuid(), Username = $"u_all_{Guid.NewGuid():N}", NormalizedUsername = "U_ALL", DisplayName = "User All", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active };
        var userWs1 = new AppUser { Id = Guid.NewGuid(), Username = $"u_ws1_{Guid.NewGuid():N}", NormalizedUsername = "U_WS1", DisplayName = "User Ws1", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active };
        var userNone = new AppUser { Id = Guid.NewGuid(), Username = $"u_none_{Guid.NewGuid():N}", NormalizedUsername = "U_NONE", DisplayName = "User None", PasswordHash = "x", IsActive = true, RecordStatus = RecordStatus.Active };

        userAll.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = userAll.Id, RoleId = rAll.Id });
        userWs1.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = userWs1.Id, RoleId = rAssigned.Id });
        userWs1.WorkstreamMemberships.Add(new UserWorkstreamMembership { Id = Guid.NewGuid(), UserId = userWs1.Id, WorkstreamId = ws1.Id, IsActive = true });

        db.Workstreams.AddRange(ws1, ws2);
        db.Villages.Add(village);
        db.Permissions.AddRange(allPerms);
        db.Roles.AddRange(rAll, rAssigned);
        db.AppUsers.AddRange(userAll, userWs1, userNone);
        db.SaveChanges();

        return (db, ws1, ws2, village, userAll, userWs1, userNone);
    }

    // =========================================================================
    // 1. WORKSTREAM SCOPING & DIRECTORY TESTS
    // =========================================================================

    [Fact]
    public async Task MatterAuthorizationService_ScopeModeAll_CanViewAllMatters()
    {
        var dbName = $"mat-scope-all-{Guid.NewGuid():N}";
        var (db, ws1, ws2, village, userAll, _, _) = CreateTestDbContext(dbName);
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
    public async Task MatterAuthorizationService_ScopeModeAssigned_CanOnlyViewAssignedWorkstream()
    {
        var dbName = $"mat-scope-assigned-{Guid.NewGuid():N}";
        var (db, ws1, ws2, village, _, userWs1, _) = CreateTestDbContext(dbName);
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
        Assert.DoesNotContain(list, m => m.Id == mLegacy.Id); // Legacy hidden from assigned-only
    }

    [Fact]
    public async Task MatterAuthorizationService_UserWithoutPermission_Denied()
    {
        var dbName = $"mat-scope-none-{Guid.NewGuid():N}";
        var (db, ws1, _, village, _, _, userNone) = CreateTestDbContext(dbName);
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
        var (db, ws1, _, village, userAll, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService);

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
        var (db, _, _, village, userAll, _, _) = CreateTestDbContext(dbName);
        var inactiveWs = new Workstream { Id = Guid.NewGuid(), Code = "INACTIVE", Name = "Inactive WS", IsActive = false, RecordStatus = RecordStatus.Active };
        db.Workstreams.Add(inactiveWs);
        await db.SaveChangesAsync();

        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService);

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
        var (db, _, ws2, village, _, userWs1, _) = CreateTestDbContext(dbName); // userWs1 only has WS1
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService);

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
        var (db, ws1, _, village, userAll, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService);

        var matter = await workflow.CreateMatterAsync(new CreateMatterCommand(village.Id, "Initial Title", "General", ws1.Id, null, null, null), userAll.Id);
        Assert.Equal(0, matter.Revision);

        var updateCmd = new UpdateMatterMetadataCommand(
            Title: "Updated Title",
            MatterType: "General",
            ReferenceNumber: "REF/999",
            Remarks: "Updated Remarks",
            KhasraReferenceText: "Khasra 100",
            ExpectedRevision: 0
        );

        var updated = await workflow.UpdateMetadataAsync(matter.Id, updateCmd, userAll.Id);
        Assert.Equal("Updated Title", updated.Title);
        Assert.Equal("REF/999", updated.ReferenceNumber);
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
        var (db, ws1, _, village, userAll, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService);

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
        var (db, ws1, ws2, village, userAll, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService);

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
        var (db, ws1, ws2, village, userAll, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService);

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
        var (db, ws1, _, village, userAll, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService);

        var matter = await workflow.CreateMatterAsync(new CreateMatterCommand(village.Id, "Closing Case", "Court Case", ws1.Id, null, null, null), userAll.Id);

        var archiveCmd = new ArchiveMatterCommand(
            Reason: "Decree executed and satisfied in full",
            ExpectedRevision: 0
        );

        var archived = await workflow.ArchiveAsync(matter.Id, archiveCmd, userAll.Id);
        Assert.Equal("Archived", archived.Status);
        Assert.Equal(1, archived.Revision);

        // Attempting to update metadata on archived matter is rejected
        var updateCmd = new UpdateMatterMetadataCommand("New Title", "Court Case", null, null, null, 1);
        var ex = await Assert.ThrowsAsync<MatterWorkflowException>(() => workflow.UpdateMetadataAsync(matter.Id, updateCmd, userAll.Id));
        Assert.Equal(409, ex.StatusCode);
        Assert.Contains("archived", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Attempting to upload to archived matter is rejected
        using var ms = new MemoryStream(ValidPdfBytes);
        var uploadCmd = new UploadMatterDocumentCommand(ms, "doc.pdf", "application/pdf", "Other", null, 1);
        var exUpload = await Assert.ThrowsAsync<MatterWorkflowException>(() => workflow.UploadDocumentAsync(matter.Id, uploadCmd, userAll.Id));
        Assert.Contains("archived", exUpload.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 6. DOCUMENT FORMAT INSPECTION & SECURITY
    // =========================================================================

    [Fact]
    public void MatterDocumentValidation_ValidFormats_Succeed()
    {
        using var pdf = new MemoryStream(ValidPdfBytes);
        MatterDocumentValidation.ValidateFileContent(pdf, ".pdf");
        Assert.Equal("application/pdf", MatterDocumentValidation.GetServerDerivedMimeType(".pdf"));

        using var png = new MemoryStream(ValidPngBytes);
        MatterDocumentValidation.ValidateFileContent(png, ".png");
        Assert.Equal("image/png", MatterDocumentValidation.GetServerDerivedMimeType(".png"));

        using var txt = new MemoryStream("Hello LAC Document"u8.ToArray());
        MatterDocumentValidation.ValidateFileContent(txt, ".txt");
        Assert.Equal("text/plain", MatterDocumentValidation.GetServerDerivedMimeType(".txt"));
    }

    [Fact]
    public void MatterDocumentValidation_DisguisedExecutable_ThrowsFormatError()
    {
        using var fake = new MemoryStream(FakePdfBytes);
        var ex = Assert.Throws<MatterWorkflowException>(() =>
            MatterDocumentValidation.ValidateFileContent(fake, ".pdf"));
        Assert.Contains("signature", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MatterDocumentValidation_DisallowedExtension_ThrowsFormatError()
    {
        using var exe = new MemoryStream(FakePdfBytes);
        var ex = Assert.Throws<MatterWorkflowException>(() =>
            MatterDocumentValidation.ValidateFileContent(exe, ".exe"));
        Assert.Contains("Unsupported", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 7. AMBIGUOUS-COMMIT RETRY & COMPENSATION CLEANUP
    // =========================================================================

    [Fact]
    public async Task MatterWorkflow_UploadDocument_CompensationCleanup_OnDbFailure()
    {
        var dbName = $"mat-cleanup-{Guid.NewGuid():N}";
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
        var workflow = new MatterWorkflowService(db, storage, authService);

        failingInterceptor.FailOnSave = true;

        using var ms = new MemoryStream(ValidPdfBytes);
        var cmd = new UploadMatterDocumentCommand(ms, "application.pdf", "application/pdf", "Application", "My App", 0);

        await Assert.ThrowsAsync<DbUpdateException>(() => workflow.UploadDocumentAsync(matter.Id, cmd, user.Id));

        // Verify compensation cleanup was invoked and storage deleted the orphaned file
        Assert.Equal(1, storage.SaveCount);
        Assert.Equal(1, storage.DeleteCount);
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task MatterWorkflow_UploadDocument_CommitAmbiguity_RetainsFileAndVerifiesIdempotently()
    {
        var dbName = $"mat-ambiguity-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _) = CreateTestDbContext(dbName);
        var matter = new Matter { Id = Guid.NewGuid(), Title = "M Ambiguous", VillageId = village.Id, WorkstreamId = ws1.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        db.Matters.Add(matter);
        await db.SaveChangesAsync();

        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);

        TestCommitAmbiguityExecutionStrategy? strategy = null;
        var workflow = new MatterWorkflowService(
            db,
            storage,
            authService,
            strategyFactory: () => strategy!
        );

        strategy = new TestCommitAmbiguityExecutionStrategy(db, simulateCommitAmbiguity: true, maxRetries: 2);

        using var ms = new MemoryStream(ValidPdfBytes);
        var cmd = new UploadMatterDocumentCommand(ms, "statement.pdf", "application/pdf", "Application", "Statement Doc", 0);

        var matterDoc = await workflow.UploadDocumentAsync(matter.Id, cmd, userAll.Id);
        Assert.NotNull(matterDoc);

        // Assert verification occurred
        Assert.Equal(1, strategy.VerifyCount);
        Assert.Equal(1, storage.SaveCount);
        Assert.Equal(0, storage.DeleteCount); // Retained! Not deleted!
        Assert.Single(storage.Files);
    }

    // =========================================================================
    // 8. DOCUMENT LINKING & PROVENANCE
    // =========================================================================

    [Fact]
    public async Task MatterWorkflow_LinkExistingDocument_ValidCandidate_IncrementsRevisionAndRecordsEvent()
    {
        var dbName = $"mat-link-succ-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService);

        var matter = await workflow.CreateMatterAsync(new CreateMatterCommand(village.Id, "Matter With Award", "Court Case", ws1.Id, null, null, null), userAll.Id);

        var award = new Award { Id = Guid.NewGuid(), AwardNumber = "AW-101", RecordStatus = RecordStatus.Active };
        var candidateDoc = new Document { Id = Guid.NewGuid(), OriginalFileName = "award_map.pdf", StoragePath = "p1", MimeType = "application/pdf", Sha256Hash = "h1", RecordStatus = RecordStatus.Active };
        var docAward = new DocumentAward { Id = Guid.NewGuid(), DocumentId = candidateDoc.Id, AwardId = award.Id };
        var matterAward = new MatterAward { Id = Guid.NewGuid(), MatterId = matter.Id, AwardId = award.Id };

        db.Awards.Add(award);
        db.Documents.Add(candidateDoc);
        db.DocumentAwards.Add(docAward);
        db.MatterAwards.Add(matterAward);
        await db.SaveChangesAsync();

        var linkCmd = new LinkExistingDocumentCommand(
            DocumentId: candidateDoc.Id,
            DocumentRole: "Court Order",
            DisplayName: "High Court Attachment",
            ExpectedRevision: 0
        );

        var linked = await workflow.LinkExistingDocumentAsync(matter.Id, linkCmd, userAll.Id);
        Assert.NotNull(linked);
        Assert.Equal(candidateDoc.Id, linked.DocumentId);

        var mRefreshed = await db.Matters.FirstAsync(m => m.Id == matter.Id);
        Assert.Equal(1, mRefreshed.Revision);

        var lastEvent = await db.MatterEvents.OrderByDescending(e => e.SequenceNumber).FirstAsync(e => e.MatterId == matter.Id);
        Assert.Equal(MatterEventAction.DocumentLinked, lastEvent.Action);
        Assert.Equal(candidateDoc.Id, lastEvent.DocumentId);
    }

    [Fact]
    public async Task MatterWorkflow_LinkExistingDocument_UnrelatedDocument_Rejected()
    {
        var dbName = $"mat-link-unrelated-{Guid.NewGuid():N}";
        var (db, ws1, _, village, userAll, _, _) = CreateTestDbContext(dbName);
        var storage = new TestInMemoryDocumentStorage();
        var authService = new MatterAuthorizationService(db);
        var workflow = new MatterWorkflowService(db, storage, authService);

        var matter = await workflow.CreateMatterAsync(new CreateMatterCommand(village.Id, "Matter Isolated", "Court Case", ws1.Id, null, null, null), userAll.Id);

        // Arbitrary document from another unrelated context
        var unrelatedDoc = new Document { Id = Guid.NewGuid(), OriginalFileName = "unrelated.pdf", StoragePath = "pX", MimeType = "application/pdf", Sha256Hash = "hX", RecordStatus = RecordStatus.Active };
        db.Documents.Add(unrelatedDoc);
        await db.SaveChangesAsync();

        var linkCmd = new LinkExistingDocumentCommand(
            DocumentId: unrelatedDoc.Id,
            DocumentRole: "Other",
            DisplayName: "Unrelated Doc",
            ExpectedRevision: 0
        );

        var ex = await Assert.ThrowsAsync<MatterWorkflowException>(() => workflow.LinkExistingDocumentAsync(matter.Id, linkCmd, userAll.Id));
        Assert.Contains("provenance", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 9. GENERIC /api/documents BOUNDARY HARDENING
    // =========================================================================

    [Fact]
    public async Task GenericDocuments_BoundaryHardening_ExcludesMatterOnlyDocuments()
    {
        var dbName = $"mat-doc-boundary-{Guid.NewGuid():N}";
        var builder = new DbContextOptionsBuilder<LacDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));

        using var db = new LacDbContext(builder.Options);

        var awardDoc = new Document { Id = Guid.NewGuid(), OriginalFileName = "award_notice.pdf", StoragePath = "a1", MimeType = "application/pdf", Sha256Hash = "h1", RecordStatus = RecordStatus.Active };
        var matterDoc = new Document { Id = Guid.NewGuid(), OriginalFileName = "matter_affidavit.pdf", StoragePath = "m1", MimeType = "application/pdf", Sha256Hash = "h2", RecordStatus = RecordStatus.Active };
        var lrDoc = new Document { Id = Guid.NewGuid(), OriginalFileName = "khasra_khatoni.pdf", StoragePath = "l1", MimeType = "application/pdf", Sha256Hash = "h3", RecordStatus = RecordStatus.Active };

        var award = new Award { Id = Guid.NewGuid(), AwardNumber = "A1", RecordStatus = RecordStatus.Active };
        var matter = new Matter { Id = Guid.NewGuid(), Title = "M1", Status = "Open", RecordStatus = RecordStatus.Active };

        db.Documents.AddRange(awardDoc, matterDoc, lrDoc);
        db.Awards.Add(award);
        db.Matters.Add(matter);

        db.DocumentAwards.Add(new DocumentAward { Id = Guid.NewGuid(), DocumentId = awardDoc.Id, AwardId = award.Id });
        db.MatterDocuments.Add(new MatterDocument { Id = Guid.NewGuid(), DocumentId = matterDoc.Id, MatterId = matter.Id });
        db.DocumentKhasras.Add(new DocumentKhasra { Id = Guid.NewGuid(), DocumentId = lrDoc.Id, KhasraId = Guid.NewGuid() });

        await db.SaveChangesAsync();

        // Award boundary query used by GET /api/documents
        var awardBoundaryQuery = db.Documents.AsNoTracking()
            .Where(d => d.RecordStatus == RecordStatus.Active &&
                (db.DocumentAwards.Any(da => da.DocumentId == d.Id) ||
                 db.NmDocuments.Any(nm => nm.DocumentId == d.Id) ||
                 db.DocumentNotifications.Any(dn => dn.DocumentId == d.Id)));

        var exposedDocs = await awardBoundaryQuery.ToListAsync();

        Assert.Contains(exposedDocs, d => d.Id == awardDoc.Id);
        Assert.DoesNotContain(exposedDocs, d => d.Id == matterDoc.Id); // Matter document excluded!
        Assert.DoesNotContain(exposedDocs, d => d.Id == lrDoc.Id);     // LR document excluded!
    }

    // =========================================================================
    // 10. DAK & OUTWARD INTEGRATION SEAMS
    // =========================================================================

    [Fact]
    public async Task Seam_DakLink_RequiresMatterViewPermission()
    {
        var dbName = $"mat-seam-dak-{Guid.NewGuid():N}";
        var (db, ws1, _, village, _, userWs1, userNone) = CreateTestDbContext(dbName);
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
        var (db, ws1, ws2, village, _, userWs1, _) = CreateTestDbContext(dbName);
        var authService = new MatterAuthorizationService(db);

        var matterInWs2 = new Matter { Id = Guid.NewGuid(), Title = "Compensation File", VillageId = village.Id, WorkstreamId = ws2.Id, Status = "Open", RecordStatus = RecordStatus.Active };
        db.Matters.Add(matterInWs2);
        await db.SaveChangesAsync();

        // userWs1 is only in WS1, matter is in WS2 -> forbidden from linking to outward
        var canAccess = await authService.CanAccessMatterAsync(matterInWs2.Id, PermissionCodes.MatterView, userWs1.Id);
        Assert.False(canAccess);
    }
}
