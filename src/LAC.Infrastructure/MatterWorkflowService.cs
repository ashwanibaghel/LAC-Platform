namespace LAC.Infrastructure;

using System.IO;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public class MatterWorkflowException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed record CreateMatterCommand(
    Guid VillageId,
    string Title,
    string MatterType,
    Guid WorkstreamId,
    string? ReferenceNumber = null,
    string? Remarks = null,
    string? KhasraReferenceText = null,
    Guid? AwardId = null
);

public sealed record UpdateMatterMetadataCommand(
    string Title,
    string MatterType,
    string? ReferenceNumber,
    string? Remarks,
    string? KhasraReferenceText,
    int ExpectedRevision,
    string? Status = null,
    Guid? AwardId = null
);

public sealed record ReclassifyWorkstreamCommand(
    Guid TargetWorkstreamId,
    string? Reason,
    int ExpectedRevision
);

public sealed record UploadMatterDocumentCommand(
    Stream DocumentStream,
    string DocumentFileName,
    string? DocumentContentType,
    string? DocumentRole,
    string? DisplayName,
    int ExpectedRevision
);

public sealed record LinkExistingDocumentCommand(
    Guid DocumentId,
    string? DocumentRole,
    string? DisplayName,
    int ExpectedRevision
);

public sealed record ArchiveMatterCommand(
    string? Reason,
    int ExpectedRevision
);

public sealed record ExtractMatterDocumentPagesCommand(
    Guid SourceDocumentId,
    string PageRangeText,
    string? DocumentRole,
    string? ItemNumber,
    string? KhasraReferenceText,
    string? ContextLabel,
    string? DisplayName,
    int ExpectedRevision
);

public sealed record UpdateMatterDocumentCommand(
    string? DocumentRole,
    string? DisplayName,
    int ExpectedRevision
);

public sealed record RemoveMatterDocumentLinkCommand(
    int ExpectedRevision
);

public sealed class MatterWorkflowService(
    LacDbContext db,
    IDocumentStorage storage,
    IMatterAuthorizationService matterAuth,
    IAccessControlService accessControl,
    Func<Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy>? strategyFactory = null)
{
    private sealed class DenyAllAccessControlService : IAccessControlService
    {
        public Task<bool> CanAsync(string permissionCode, AccessResourceContext? context = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyDictionary<string, ScopeMode>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, ScopeMode>>(new Dictionary<string, ScopeMode>());
    }

    public MatterWorkflowService(
        LacDbContext db,
        IDocumentStorage storage,
        IMatterAuthorizationService matterAuth)
        : this(db, storage, matterAuth, new DenyAllAccessControlService(), null)
    {
    }

    public MatterWorkflowService(
        LacDbContext db,
        IDocumentStorage storage,
        IMatterAuthorizationService matterAuth,
        Func<Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy>? strategyFactory)
        : this(db, storage, matterAuth, new DenyAllAccessControlService(), strategyFactory)
    {
    }

    private async Task<TResult> ExecuteWorkflowTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        Func<CancellationToken, Task<bool>> verifySucceeded,
        CancellationToken ct)
    {
        var strategy = strategyFactory?.Invoke() ?? db.Database.CreateExecutionStrategy();
        if (db.Database.IsRelational() || strategyFactory != null)
        {
            return await strategy.ExecuteInTransactionAsync(operation, verifySucceeded, ct);
        }

        try
        {
            return await strategy.ExecuteInTransactionAsync(operation, verifySucceeded, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("TransactionIgnoredWarning"))
        {
            return await strategy.ExecuteAsync(async () => await operation(ct));
        }
    }

    private async Task<Matter> LockMatterAsync(Guid matterId, CancellationToken ct)
    {
        Matter? matter;
        if (db.Database.IsRelational())
        {
            matter = await db.Matters
                .FromSqlInterpolated($"SELECT * FROM \"Matters\" WHERE \"Id\" = {matterId} FOR UPDATE")
                .Include(m => m.Workstream)
                .FirstOrDefaultAsync(ct);
        }
        else
        {
            matter = await db.Matters.Include(m => m.Workstream).FirstOrDefaultAsync(m => m.Id == matterId, ct);
        }

        return matter ?? throw new MatterWorkflowException("Matter record not found.", 404);
    }

    private async Task VerifyDocumentLinkingProvenanceAsync(Matter matter, Guid documentId, CancellationToken ct)
    {
        var eligibleMap = await MatterDocumentProvenanceHelper.GetEligibleDocumentCandidateMapAsync(db, matter, accessControl, ct);
        if (!eligibleMap.ContainsKey(documentId))
        {
            throw new MatterWorkflowException("Document provenance not verified or permission denied for linking.", 400);
        }
    }

    // ========================================================================
    // 1. CREATE MATTER
    // ========================================================================
    public async Task<Matter> CreateMatterAsync(CreateMatterCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Title))
            throw new MatterWorkflowException("Title is mandatory.");
        if (string.IsNullOrWhiteSpace(cmd.MatterType))
            throw new MatterWorkflowException("MatterType is mandatory.");

        var villageExists = await db.Villages.AsNoTracking()
            .AnyAsync(v => v.Id == cmd.VillageId && v.RecordStatus == RecordStatus.Active, ct);
        if (!villageExists)
            throw new MatterWorkflowException("Specified Village does not exist or is inactive.", 404);

        var ws = await db.Workstreams.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == cmd.WorkstreamId && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);
        if (ws is null)
            throw new MatterWorkflowException("Specified Workstream does not exist or is inactive.", 400);

        var canCreate = await matterAuth.CanCreateMatterInWorkstreamAsync(cmd.WorkstreamId, currentUserId, ct);
        if (!canCreate)
            throw new MatterWorkflowException("You do not have permission to create a Matter in this workstream.", 403);

        if (cmd.AwardId.HasValue)
        {
            var awardBelongsToVillage = await db.AwardVillages.AsNoTracking()
                .AnyAsync(av => av.AwardId == cmd.AwardId.Value && av.VillageId == cmd.VillageId, ct);
            if (!awardBelongsToVillage)
                throw new MatterWorkflowException("Award does not belong to the selected village.", 400);

            var canViewAward = await accessControl.CanAsync(PermissionCodes.AwardView, new AccessResourceContext(WorkstreamCode: WorkstreamCodes.Award), ct);
            if (!canViewAward)
                throw new MatterWorkflowException("Caller lacks Award.View permission to associate this award.", 403);
        }

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId, ct)
            ?? throw new MatterWorkflowException("Current user not found.", 401);

        var matterId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var matterAwardId = cmd.AwardId.HasValue ? Guid.NewGuid() : Guid.Empty;
        var now = DateTimeOffset.UtcNow;

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();

                var matter = new Matter
                {
                    Id = matterId,
                    VillageId = cmd.VillageId,
                    WorkstreamId = cmd.WorkstreamId,
                    Revision = 0,
                    Title = cmd.Title.Trim(),
                    MatterType = cmd.MatterType.Trim(),
                    Status = "Open",
                    ReferenceNumber = cmd.ReferenceNumber?.Trim(),
                    Remarks = cmd.Remarks?.Trim(),
                    KhasraReferenceText = cmd.KhasraReferenceText?.Trim(),
                    CreatedBy = actionUser.DisplayName,
                    UpdatedBy = actionUser.DisplayName,
                    RecordStatus = RecordStatus.Active,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Matters.Add(matter);

                if (cmd.AwardId.HasValue)
                {
                    var matterAward = new MatterAward
                    {
                        Id = matterAwardId,
                        MatterId = matterId,
                        AwardId = cmd.AwardId.Value,
                        IsPrimary = true
                    };
                    db.MatterAwards.Add(matterAward);
                }

                string? wsName = null;
                if (cmd.WorkstreamId != Guid.Empty)
                {
                    wsName = await db.Workstreams.AsNoTracking()
                        .Where(w => w.Id == cmd.WorkstreamId)
                        .Select(w => w.Name)
                        .FirstOrDefaultAsync(opCt);
                }

                var createdEvent = new MatterEvent
                {
                    Id = eventId,
                    MatterId = matterId,
                    SequenceNumber = 1,
                    Action = MatterEventAction.Created,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = now,
                    WorkstreamIdSnapshot = cmd.WorkstreamId,
                    WorkstreamNameSnapshot = wsName
                };
                db.MatterEvents.Add(createdEvent);

                await db.SaveChangesAsync(opCt);
                return matter;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();
                var exists = await db.Matters.AsNoTracking().AnyAsync(m => m.Id == matterId, verifyCt);
                if (!exists) return false;

                var eventExists = await db.MatterEvents.AsNoTracking()
                    .AnyAsync(e => e.Id == eventId && e.MatterId == matterId && e.SequenceNumber == 1 && e.Action == MatterEventAction.Created, verifyCt);
                if (!eventExists) return false;

                if (cmd.AwardId.HasValue)
                {
                    var maExists = await db.MatterAwards.AsNoTracking()
                        .AnyAsync(ma => ma.Id == matterAwardId && ma.MatterId == matterId && ma.AwardId == cmd.AwardId.Value, verifyCt);
                    if (!maExists) return false;
                }

                return true;
            },
            ct);
    }

    // ========================================================================
    // 2. UPDATE METADATA
    // ========================================================================
    public async Task<Matter> UpdateMetadataAsync(Guid matterId, UpdateMatterMetadataCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Title))
            throw new MatterWorkflowException("Title is mandatory.");
        if (string.IsNullOrWhiteSpace(cmd.MatterType))
            throw new MatterWorkflowException("MatterType is mandatory.");

        var canEdit = await matterAuth.CanAccessMatterAsync(matterId, PermissionCodes.MatterEdit, currentUserId, ct);
        if (!canEdit)
            throw new MatterWorkflowException("You do not have permission to edit this Matter.", 403);

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId, ct)
            ?? throw new MatterWorkflowException("Current user not found.", 401);

        var eventId = Guid.NewGuid();

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();

                var matter = await LockMatterAsync(matterId, opCt);
                if (matter.RecordStatus == RecordStatus.Archived)
                    throw new MatterWorkflowException("Cannot update an archived matter.", 409);

                if (matter.Revision != cmd.ExpectedRevision)
                    throw new MatterWorkflowException($"Concurrency conflict: expected revision {cmd.ExpectedRevision} but found {matter.Revision}.", 409);

                var maxSeq = await db.MatterEvents
                    .Where(e => e.MatterId == matterId)
                    .MaxAsync(e => (int?)e.SequenceNumber, opCt) ?? 0;
                var seq = maxSeq + 1;

                matter.Title = cmd.Title.Trim();
                matter.MatterType = cmd.MatterType.Trim();
                matter.ReferenceNumber = cmd.ReferenceNumber?.Trim();
                matter.Remarks = cmd.Remarks?.Trim();
                matter.KhasraReferenceText = cmd.KhasraReferenceText?.Trim();
                if (!string.IsNullOrWhiteSpace(cmd.Status))
                {
                    matter.Status = cmd.Status.Trim();
                }

                if (cmd.AwardId.HasValue)
                {
                    var existingPrimary = await db.MatterAwards.FirstOrDefaultAsync(ma => ma.MatterId == matterId && ma.IsPrimary, opCt);
                    if (existingPrimary != null)
                    {
                        if (cmd.AwardId.Value == Guid.Empty)
                        {
                            db.MatterAwards.Remove(existingPrimary);
                        }
                        else if (existingPrimary.AwardId != cmd.AwardId.Value)
                        {
                            existingPrimary.AwardId = cmd.AwardId.Value;
                        }
                    }
                    else if (cmd.AwardId.Value != Guid.Empty)
                    {
                        db.MatterAwards.Add(new MatterAward
                        {
                            Id = Guid.NewGuid(),
                            MatterId = matterId,
                            AwardId = cmd.AwardId.Value,
                            IsPrimary = true
                        });
                    }
                }

                matter.Revision++;
                matter.UpdatedBy = actionUser.DisplayName;
                matter.UpdatedAt = DateTimeOffset.UtcNow;

                var ev = new MatterEvent
                {
                    Id = eventId,
                    MatterId = matterId,
                    SequenceNumber = seq,
                    Action = MatterEventAction.MetadataUpdated,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = DateTimeOffset.UtcNow,
                    WorkstreamIdSnapshot = matter.WorkstreamId,
                    WorkstreamNameSnapshot = matter.Workstream?.Name
                };
                db.MatterEvents.Add(ev);

                await db.SaveChangesAsync(opCt);
                return matter;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();
                var evExists = await db.MatterEvents.AsNoTracking()
                    .AnyAsync(e => e.Id == eventId && e.MatterId == matterId && e.Action == MatterEventAction.MetadataUpdated, verifyCt);
                return evExists;
            },
            ct);
    }

    // ========================================================================
    // 3. RECLASSIFY WORKSTREAM
    // ========================================================================
    public async Task<Matter> ReclassifyWorkstreamAsync(Guid matterId, ReclassifyWorkstreamCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            throw new MatterWorkflowException("Reclassification reason is mandatory.", 400);

        var targetWs = await db.Workstreams.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == cmd.TargetWorkstreamId && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);
        if (targetWs is null)
            throw new MatterWorkflowException("Target Workstream does not exist or is inactive.", 400);

        var canReclassify = await matterAuth.CanReclassifyMatterAsync(matterId, cmd.TargetWorkstreamId, currentUserId, ct);
        if (!canReclassify)
            throw new MatterWorkflowException("You do not have permission to reclassify this Matter.", 403);

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId, ct)
            ?? throw new MatterWorkflowException("Current user not found.", 401);

        var eventId = Guid.NewGuid();

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();

                var matter = await LockMatterAsync(matterId, opCt);
                if (matter.RecordStatus == RecordStatus.Archived)
                    throw new MatterWorkflowException("Cannot reclassify an archived matter.", 409);

                if (matter.Revision != cmd.ExpectedRevision)
                    throw new MatterWorkflowException($"Concurrency conflict: expected revision {cmd.ExpectedRevision} but found {matter.Revision}.", 409);

                if (matter.WorkstreamId == cmd.TargetWorkstreamId)
                    throw new MatterWorkflowException("Matter is already in the specified workstream.", 400);

                var maxSeq = await db.MatterEvents
                    .Where(e => e.MatterId == matterId)
                    .MaxAsync(e => (int?)e.SequenceNumber, opCt) ?? 0;
                var seq = maxSeq + 1;

                var sourceWorkstreamId = matter.WorkstreamId;
                var sourceWsName = matter.Workstream?.Name;
                var targetWsName = await db.Workstreams.AsNoTracking()
                    .Where(w => w.Id == cmd.TargetWorkstreamId)
                    .Select(w => w.Name)
                    .FirstOrDefaultAsync(opCt);

                matter.WorkstreamId = cmd.TargetWorkstreamId;
                matter.Revision++;
                matter.UpdatedBy = actionUser.DisplayName;
                matter.UpdatedAt = DateTimeOffset.UtcNow;

                var ev = new MatterEvent
                {
                    Id = eventId,
                    MatterId = matterId,
                    SequenceNumber = seq,
                    Action = MatterEventAction.WorkstreamReclassified,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = DateTimeOffset.UtcNow,
                    SourceWorkstreamId = sourceWorkstreamId,
                    SourceWorkstreamNameSnapshot = sourceWsName,
                    TargetWorkstreamId = cmd.TargetWorkstreamId,
                    TargetWorkstreamNameSnapshot = targetWsName,
                    WorkstreamIdSnapshot = cmd.TargetWorkstreamId,
                    WorkstreamNameSnapshot = targetWsName
                };
                db.MatterEvents.Add(ev);

                await db.SaveChangesAsync(opCt);
                return matter;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();
                var evExists = await db.MatterEvents.AsNoTracking()
                    .AnyAsync(e => e.Id == eventId && e.MatterId == matterId && e.Action == MatterEventAction.WorkstreamReclassified && e.TargetWorkstreamId == cmd.TargetWorkstreamId, verifyCt);
                return evExists;
            },
            ct);
    }

    // ========================================================================
    // 4. UPLOAD DOCUMENT
    // ========================================================================
    public async Task<MatterDocument> UploadDocumentAsync(Guid matterId, UploadMatterDocumentCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        var canManage = await matterAuth.CanAccessMatterAsync(matterId, PermissionCodes.MatterDocumentManage, currentUserId, ct);
        if (!canManage)
            throw new MatterWorkflowException("You do not have permission to manage documents for this Matter.", 403);

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId, ct)
            ?? throw new MatterWorkflowException("Current user not found.", 401);

        if (string.IsNullOrWhiteSpace(cmd.DocumentFileName))
            throw new MatterWorkflowException("File name is mandatory.", 400);

        var normalizedFileName = Path.GetFileName(cmd.DocumentFileName).Trim();
        var ext = Path.GetExtension(normalizedFileName);
        MatterDocumentValidation.ValidateFileContent(cmd.DocumentStream, ext);
        var inspectedMime = MatterDocumentValidation.GetServerDerivedMimeType(ext);

        var fileResult = await storage.SaveAndHashAsync(cmd.DocumentStream, normalizedFileName, ct);
        var savedStoragePath = fileResult.StoragePath;

        var documentId = Guid.NewGuid();
        var matterDocId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        try
        {
            return await ExecuteWorkflowTransactionAsync(
                async opCt =>
                {
                    db.ChangeTracker.Clear();

                    var matter = await LockMatterAsync(matterId, opCt);
                    if (matter.RecordStatus == RecordStatus.Archived)
                        throw new MatterWorkflowException("Cannot add documents to an archived matter.", 409);

                    if (matter.Revision != cmd.ExpectedRevision)
                        throw new MatterWorkflowException($"Concurrency conflict: expected revision {cmd.ExpectedRevision} but found {matter.Revision}.", 409);

                    var maxSeq = await db.MatterEvents
                        .Where(e => e.MatterId == matterId)
                        .MaxAsync(e => (int?)e.SequenceNumber, opCt) ?? 0;
                    var seq = maxSeq + 1;

                    var doc = new Document
                    {
                        Id = documentId,
                        OriginalFileName = normalizedFileName,
                        StoragePath = fileResult.StoragePath,
                        Sha256Hash = fileResult.Sha256Hash,
                        FileSize = fileResult.FileSize,
                        MimeType = inspectedMime,
                        DocumentType = "MatterDocument",
                        UploadedBy = actionUser.DisplayName,
                        RecordStatus = RecordStatus.Active,
                        Status = "Active",
                        Version = 1,
                        UploadedAt = DateTimeOffset.UtcNow,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    db.Documents.Add(doc);

                    var matterDoc = new MatterDocument
                    {
                        Id = matterDocId,
                        MatterId = matterId,
                        DocumentId = documentId,
                        DocumentRole = cmd.DocumentRole,
                        DisplayName = !string.IsNullOrWhiteSpace(cmd.DisplayName) ? cmd.DisplayName.Trim() : normalizedFileName
                    };
                    db.MatterDocuments.Add(matterDoc);

                    matter.Revision++;
                    matter.UpdatedBy = actionUser.DisplayName;
                    matter.UpdatedAt = DateTimeOffset.UtcNow;

                    var ev = new MatterEvent
                    {
                        Id = eventId,
                        MatterId = matterId,
                        SequenceNumber = seq,
                        Action = MatterEventAction.DocumentUploaded,
                        ActionByUserId = currentUserId,
                        ActionByDisplayNameSnapshot = actionUser.DisplayName,
                        ActionAt = DateTimeOffset.UtcNow,
                        DocumentId = documentId,
                        MatterDocumentId = matterDocId,
                        WorkstreamIdSnapshot = matter.WorkstreamId,
                        WorkstreamNameSnapshot = matter.Workstream?.Name
                    };
                    db.MatterEvents.Add(ev);

                    await db.SaveChangesAsync(opCt);
                    matterDoc.Document = doc;
                    return matterDoc;
                },
                async verifyCt =>
                {
                    db.ChangeTracker.Clear();
                    var evExists = await db.MatterEvents.AsNoTracking()
                        .AnyAsync(e => e.Id == eventId && e.MatterId == matterId && e.Action == MatterEventAction.DocumentUploaded && e.DocumentId == documentId && e.MatterDocumentId == matterDocId, verifyCt);
                    return evExists;
                },
                ct);
        }
        catch
        {
            if (savedStoragePath is not null)
            {
                var committed = false;
                try
                {
                    committed = await db.MatterEvents.AsNoTracking()
                        .AnyAsync(e => e.Id == eventId && e.MatterId == matterId && e.Action == MatterEventAction.DocumentUploaded);
                }
                catch
                {
                    committed = true;
                }

                if (!committed)
                {
                    try { await storage.DeleteAsync(savedStoragePath, CancellationToken.None); } catch { /* best effort */ }
                }
            }
            throw;
        }
    }

    // ========================================================================
    // 5. LINK EXISTING DOCUMENT
    // ========================================================================
    public async Task<MatterDocument> LinkExistingDocumentAsync(Guid matterId, LinkExistingDocumentCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        var canManage = await matterAuth.CanAccessMatterAsync(matterId, PermissionCodes.MatterDocumentManage, currentUserId, ct);
        if (!canManage)
            throw new MatterWorkflowException("You do not have permission to manage documents for this Matter.", 403);

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId, ct)
            ?? throw new MatterWorkflowException("Current user not found.", 401);

        var matterDocId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();

                var matter = await LockMatterAsync(matterId, opCt);
                if (matter.RecordStatus == RecordStatus.Archived)
                    throw new MatterWorkflowException("Cannot link documents to an archived matter.", 409);

                if (matter.Revision != cmd.ExpectedRevision)
                    throw new MatterWorkflowException($"Concurrency conflict: expected revision {cmd.ExpectedRevision} but found {matter.Revision}.", 409);

                var alreadyLinked = await db.MatterDocuments.AsNoTracking()
                    .AnyAsync(md => md.MatterId == matterId && md.DocumentId == cmd.DocumentId && md.RecordStatus == RecordStatus.Active, opCt);
                if (alreadyLinked)
                    throw new MatterWorkflowException("Document is already linked to this matter.", 400);

                await VerifyDocumentLinkingProvenanceAsync(matter, cmd.DocumentId, opCt);

                var maxSeq = await db.MatterEvents
                    .Where(e => e.MatterId == matterId)
                    .MaxAsync(e => (int?)e.SequenceNumber, opCt) ?? 0;
                var seq = maxSeq + 1;

                var targetDoc = await db.Documents.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == cmd.DocumentId, opCt);

                var matterDoc = new MatterDocument
                {
                    Id = matterDocId,
                    MatterId = matterId,
                    DocumentId = cmd.DocumentId,
                    DocumentRole = cmd.DocumentRole,
                    DisplayName = !string.IsNullOrWhiteSpace(cmd.DisplayName) ? cmd.DisplayName.Trim() : targetDoc?.OriginalFileName
                };
                db.MatterDocuments.Add(matterDoc);

                matter.Revision++;
                matter.UpdatedBy = actionUser.DisplayName;
                matter.UpdatedAt = DateTimeOffset.UtcNow;

                var ev = new MatterEvent
                {
                    Id = eventId,
                    MatterId = matterId,
                    SequenceNumber = seq,
                    Action = MatterEventAction.DocumentLinked,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = DateTimeOffset.UtcNow,
                    DocumentId = cmd.DocumentId,
                    MatterDocumentId = matterDocId,
                    WorkstreamIdSnapshot = matter.WorkstreamId,
                    WorkstreamNameSnapshot = matter.Workstream?.Name
                };
                db.MatterEvents.Add(ev);

                await db.SaveChangesAsync(opCt);
                if (targetDoc != null) matterDoc.Document = targetDoc;
                return matterDoc;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();
                var evExists = await db.MatterEvents.AsNoTracking()
                    .AnyAsync(e => e.Id == eventId && e.MatterId == matterId && e.Action == MatterEventAction.DocumentLinked && e.DocumentId == cmd.DocumentId && e.MatterDocumentId == matterDocId, verifyCt);
                return evExists;
            },
            ct);
    }

    // ========================================================================
    // 6. ARCHIVE MATTER
    // ========================================================================
    public async Task<Matter> ArchiveAsync(Guid matterId, ArchiveMatterCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            throw new MatterWorkflowException("Archival reason is mandatory.", 400);

        var canArchive = await matterAuth.CanAccessMatterAsync(matterId, PermissionCodes.MatterArchive, currentUserId, ct);
        if (!canArchive)
            throw new MatterWorkflowException("You do not have permission to archive this Matter.", 403);

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId, ct)
            ?? throw new MatterWorkflowException("Current user not found.", 401);

        var eventId = Guid.NewGuid();

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();

                var matter = await LockMatterAsync(matterId, opCt);
                if (matter.RecordStatus == RecordStatus.Archived)
                    throw new MatterWorkflowException("Matter is already archived.", 409);

                if (matter.Revision != cmd.ExpectedRevision)
                    throw new MatterWorkflowException($"Concurrency conflict: expected revision {cmd.ExpectedRevision} but found {matter.Revision}.", 409);

                var maxSeq = await db.MatterEvents
                    .Where(e => e.MatterId == matterId)
                    .MaxAsync(e => (int?)e.SequenceNumber, opCt) ?? 0;
                var seq = maxSeq + 1;

                matter.RecordStatus = RecordStatus.Archived;
                if (!string.IsNullOrWhiteSpace(cmd.Reason))
                {
                    matter.Remarks = string.IsNullOrWhiteSpace(matter.Remarks)
                        ? $"[Archived: {cmd.Reason.Trim()}]"
                        : $"{matter.Remarks} [Archived: {cmd.Reason.Trim()}]";
                }
                matter.Revision++;
                matter.UpdatedBy = actionUser.DisplayName;
                matter.UpdatedAt = DateTimeOffset.UtcNow;

                var ev = new MatterEvent
                {
                    Id = eventId,
                    MatterId = matterId,
                    SequenceNumber = seq,
                    Action = MatterEventAction.Archived,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = DateTimeOffset.UtcNow,
                    WorkstreamIdSnapshot = matter.WorkstreamId,
                    WorkstreamNameSnapshot = matter.Workstream?.Name
                };
                db.MatterEvents.Add(ev);

                await db.SaveChangesAsync(opCt);
                return matter;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();
                var evExists = await db.MatterEvents.AsNoTracking()
                    .AnyAsync(e => e.Id == eventId && e.MatterId == matterId && e.Action == MatterEventAction.Archived, verifyCt);
                return evExists;
            },
            ct);
    }

    // ========================================================================
    // 7. EXTRACT & ATTACH DOCUMENT PAGES
    // ========================================================================
    public async Task<MatterDocument> ExtractAndAttachMatterDocumentPagesAsync(
        Guid matterId,
        ExtractMatterDocumentPagesCommand cmd,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        var canManage = await matterAuth.CanAccessMatterAsync(matterId, PermissionCodes.MatterDocumentManage, currentUserId, ct);
        if (!canManage)
            throw new MatterWorkflowException("You do not have permission to manage documents for this Matter.", 403);

        var matter = await db.Matters.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matterId && m.RecordStatus == RecordStatus.Active, ct);
        if (matter is null)
            throw new MatterWorkflowException("Matter not found.", 404);

        if (matter.Status == "Archived" || matter.RecordStatus == RecordStatus.Archived)
            throw new MatterWorkflowException("Cannot modify an archived matter.", 400);

        if (cmd.ExpectedRevision != matter.Revision)
            throw new MatterWorkflowException($"Concurrency conflict: expected revision {cmd.ExpectedRevision} but found {matter.Revision}.", 409);

        // Authorization: Check if source document is in eligible candidate map OR is ALREADY linked to this matter
        var isAlreadyLinked = await db.MatterDocuments.AsNoTracking()
            .AnyAsync(md => md.MatterId == matterId && md.DocumentId == cmd.SourceDocumentId, ct);

        if (!isAlreadyLinked)
        {
            var eligibleMap = await MatterDocumentProvenanceHelper.GetEligibleDocumentCandidateMapAsync(db, matter, accessControl, ct);
            if (!eligibleMap.ContainsKey(cmd.SourceDocumentId))
            {
                throw new MatterWorkflowException("Source document provenance not verified or permission denied for page extraction.", 403);
            }
        }

        var sourceDoc = await db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == cmd.SourceDocumentId && d.RecordStatus == RecordStatus.Active, ct);
        if (sourceDoc is null)
            throw new MatterWorkflowException("Source document record not found or inactive.", 404);

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId, ct)
            ?? throw new MatterWorkflowException("Current user not found.", 401);

        Stream? sourceStream;
        try
        {
            sourceStream = await storage.OpenReadAsync(sourceDoc.StoragePath, ct);
        }
        catch (Exception ex)
        {
            throw new MatterWorkflowException($"Failed to access source document storage stream: {ex.Message}", 500);
        }

        if (sourceStream is null)
            throw new MatterWorkflowException("Source document file stream not found in storage.", 404);

        byte[] sourcePdfBytes;
        using (sourceStream)
        using (var ms = new MemoryStream())
        {
            await sourceStream.CopyToAsync(ms, ct);
            sourcePdfBytes = ms.ToArray();
        }
        var sourceSha256 = sourceDoc.Sha256Hash ?? ComputeSha256Hash(sourcePdfBytes);

        (byte[] extractedBytes, List<int> sortedPages, string normalizedPagesText) extractionResult;
        using (var readMs = new MemoryStream(sourcePdfBytes))
        {
            extractionResult = ExtractPdfPages(readMs, cmd.PageRangeText);
        }

        var extractedSha256 = ComputeSha256Hash(extractionResult.extractedBytes);
        var role = !string.IsNullOrWhiteSpace(cmd.DocumentRole) ? cmd.DocumentRole.Trim() : "Matter Extract";
        var itemNoStr = !string.IsNullOrWhiteSpace(cmd.ItemNumber) ? cmd.ItemNumber.Trim() : null;
        var khasraRefStr = !string.IsNullOrWhiteSpace(cmd.KhasraReferenceText) ? cmd.KhasraReferenceText.Trim() : null;
        var contextLabelStr = !string.IsNullOrWhiteSpace(cmd.ContextLabel) ? cmd.ContextLabel.Trim() : null;

        string finalDisplayName;
        if (!string.IsNullOrWhiteSpace(cmd.DisplayName))
        {
            finalDisplayName = cmd.DisplayName.Trim();
        }
        else
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(itemNoStr)) parts.Add($"Item #{itemNoStr}");
            parts.Add($"{role} Extract");
            if (!string.IsNullOrEmpty(khasraRefStr)) parts.Add($"Khasra {khasraRefStr}");
            parts.Add($"pp. {extractionResult.normalizedPagesText}");
            finalDisplayName = string.Join(" · ", parts);
        }

        var documentId = Guid.NewGuid();
        var matterDocId = Guid.NewGuid();
        var extractProvId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var extractedFileName = $"Extract_p{extractionResult.normalizedPagesText.Replace(", ", "_").Replace("-", "to")}_{sourceDoc.OriginalFileName}";

        string? savedStoragePath = null;
        try
        {
            using (var saveMs = new MemoryStream(extractionResult.extractedBytes))
            {
                var fileResult = await storage.SaveAndHashAsync(saveMs, extractedFileName, ct);
                savedStoragePath = fileResult.StoragePath;
            }

            return await ExecuteWorkflowTransactionAsync(
                async opCt =>
                {
                    db.ChangeTracker.Clear();

                    var lockedMatter = await LockMatterAsync(matterId, opCt);
                    if (lockedMatter.RecordStatus == RecordStatus.Archived)
                        throw new MatterWorkflowException("Cannot add documents to an archived matter.", 409);

                    if (lockedMatter.Revision != cmd.ExpectedRevision)
                        throw new MatterWorkflowException($"Concurrency conflict: expected revision {cmd.ExpectedRevision} but found {lockedMatter.Revision}.", 409);

                    var maxSeq = await db.MatterEvents
                        .Where(e => e.MatterId == matterId)
                        .MaxAsync(e => (int?)e.SequenceNumber, opCt) ?? 0;
                    var seq = maxSeq + 1;

                    var newDoc = new Document
                    {
                        Id = documentId,
                        OriginalFileName = extractedFileName,
                        StoragePath = savedStoragePath,
                        Sha256Hash = extractedSha256,
                        FileSize = extractionResult.extractedBytes.Length,
                        MimeType = "application/pdf",
                        DocumentType = "MatterExtract",
                        UploadedBy = actionUser.DisplayName,
                        RecordStatus = RecordStatus.Active,
                        Status = "Active",
                        Version = 1,
                        UploadedAt = DateTimeOffset.UtcNow,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    db.Documents.Add(newDoc);

                    var matterDoc = new MatterDocument
                    {
                        Id = matterDocId,
                        MatterId = matterId,
                        DocumentId = documentId,
                        DocumentRole = role,
                        DisplayName = finalDisplayName
                    };
                    db.MatterDocuments.Add(matterDoc);

                    var extractProv = new MatterDocumentExtract
                    {
                        Id = extractProvId,
                        MatterDocumentId = matterDocId,
                        SourceDocumentId = sourceDoc.Id,
                        SourceSha256Hash = sourceSha256,
                        NormalizedSourcePagesText = extractionResult.normalizedPagesText,
                        NormalizedPageNumbersJson = System.Text.Json.JsonSerializer.Serialize(extractionResult.sortedPages),
                        ItemNumber = itemNoStr,
                        KhasraReferenceText = khasraRefStr,
                        ContextLabel = contextLabelStr,
                        ExtractedByUserId = currentUserId,
                        ExtractedByUserNameSnapshot = actionUser.DisplayName,
                        ExtractedAt = DateTimeOffset.UtcNow
                    };
                    db.MatterDocumentExtracts.Add(extractProv);

                    lockedMatter.Revision++;
                    lockedMatter.UpdatedBy = actionUser.DisplayName;
                    lockedMatter.UpdatedAt = DateTimeOffset.UtcNow;

                    var ev = new MatterEvent
                    {
                        Id = eventId,
                        MatterId = matterId,
                        SequenceNumber = seq,
                        Action = MatterEventAction.DocumentPagesExtracted,
                        ActionByUserId = currentUserId,
                        ActionByDisplayNameSnapshot = actionUser.DisplayName,
                        ActionAt = DateTimeOffset.UtcNow,
                        DocumentId = documentId,
                        MatterDocumentId = matterDocId,
                        WorkstreamIdSnapshot = lockedMatter.WorkstreamId,
                        WorkstreamNameSnapshot = lockedMatter.Workstream?.Name
                    };
                    db.MatterEvents.Add(ev);

                    await db.SaveChangesAsync(opCt);
                    matterDoc.Document = newDoc;
                    matterDoc.ExtractProvenance = extractProv;
                    return matterDoc;
                },
                async verifyCt =>
                {
                    db.ChangeTracker.Clear();
                    var evExists = await db.MatterEvents.AsNoTracking()
                        .AnyAsync(e => e.Id == eventId && e.MatterId == matterId && e.Action == MatterEventAction.DocumentPagesExtracted && e.DocumentId == documentId && e.MatterDocumentId == matterDocId, verifyCt);
                    return evExists;
                },
                ct);
        }
        catch
        {
            if (savedStoragePath is not null)
            {
                try { await storage.DeleteAsync(savedStoragePath, CancellationToken.None); } catch { }
            }
            throw;
        }
    }

    // ========================================================================
    // 8. UPDATE MATTER DOCUMENT METADATA
    // ========================================================================
    public async Task<MatterDocument> UpdateMatterDocumentMetadataAsync(
        Guid matterId,
        Guid documentId,
        UpdateMatterDocumentCommand cmd,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        var canManage = await matterAuth.CanAccessMatterAsync(matterId, PermissionCodes.MatterDocumentManage, currentUserId, ct);
        if (!canManage)
            throw new MatterWorkflowException("You do not have permission to manage documents for this Matter.", 403);

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId, ct)
            ?? throw new MatterWorkflowException("Current user not found.", 401);

        var eventId = Guid.NewGuid();

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();

                var matter = await LockMatterAsync(matterId, opCt);
                if (matter.RecordStatus == RecordStatus.Archived)
                    throw new MatterWorkflowException("Cannot modify documents on an archived matter.", 409);

                if (matter.Revision != cmd.ExpectedRevision)
                    throw new MatterWorkflowException($"Concurrency conflict: expected revision {cmd.ExpectedRevision} but found {matter.Revision}.", 409);

                var matterDoc = await db.MatterDocuments
                    .Include(md => md.Document)
                    .Include(md => md.ExtractProvenance)
                    .FirstOrDefaultAsync(md => md.MatterId == matterId && md.DocumentId == documentId && md.RecordStatus == RecordStatus.Active, opCt);
                if (matterDoc is null)
                    throw new MatterWorkflowException("Matter document link not found or inactive.", 404);

                if (!string.IsNullOrWhiteSpace(cmd.DocumentRole))
                    matterDoc.DocumentRole = cmd.DocumentRole.Trim();
                if (cmd.DisplayName != null)
                    matterDoc.DisplayName = string.IsNullOrWhiteSpace(cmd.DisplayName) ? null : cmd.DisplayName.Trim();

                matterDoc.UpdatedBy = actionUser.DisplayName;
                matterDoc.UpdatedAt = DateTimeOffset.UtcNow;

                var maxSeq = await db.MatterEvents
                    .Where(e => e.MatterId == matterId)
                    .MaxAsync(e => (int?)e.SequenceNumber, opCt) ?? 0;
                var seq = maxSeq + 1;

                matter.Revision++;
                matter.UpdatedBy = actionUser.DisplayName;
                matter.UpdatedAt = DateTimeOffset.UtcNow;

                var ev = new MatterEvent
                {
                    Id = eventId,
                    MatterId = matterId,
                    SequenceNumber = seq,
                    Action = MatterEventAction.DocumentMetadataUpdated,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = DateTimeOffset.UtcNow,
                    DocumentId = documentId,
                    MatterDocumentId = matterDoc.Id,
                    WorkstreamIdSnapshot = matter.WorkstreamId,
                    WorkstreamNameSnapshot = matter.Workstream?.Name
                };
                db.MatterEvents.Add(ev);

                await db.SaveChangesAsync(opCt);
                return matterDoc;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();
                var evExists = await db.MatterEvents.AsNoTracking()
                    .AnyAsync(e => e.Id == eventId && e.MatterId == matterId && e.Action == MatterEventAction.DocumentMetadataUpdated && e.DocumentId == documentId, verifyCt);
                return evExists;
            },
            ct);
    }

    // ========================================================================
    // 9. REMOVE / SOFT-UNLINK MATTER DOCUMENT
    // ========================================================================
    public async Task RemoveMatterDocumentLinkAsync(
        Guid matterId,
        Guid documentId,
        RemoveMatterDocumentLinkCommand cmd,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        var canManage = await matterAuth.CanAccessMatterAsync(matterId, PermissionCodes.MatterDocumentManage, currentUserId, ct);
        if (!canManage)
            throw new MatterWorkflowException("You do not have permission to manage documents for this Matter.", 403);

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId, ct)
            ?? throw new MatterWorkflowException("Current user not found.", 401);

        var eventId = Guid.NewGuid();

        await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();

                var matter = await LockMatterAsync(matterId, opCt);
                if (matter.RecordStatus == RecordStatus.Archived)
                    throw new MatterWorkflowException("Cannot modify documents on an archived matter.", 409);

                if (matter.Revision != cmd.ExpectedRevision)
                    throw new MatterWorkflowException($"Concurrency conflict: expected revision {cmd.ExpectedRevision} but found {matter.Revision}.", 409);

                var matterDoc = await db.MatterDocuments
                    .FirstOrDefaultAsync(md => md.MatterId == matterId && md.DocumentId == documentId && md.RecordStatus == RecordStatus.Active, opCt);
                if (matterDoc is null)
                    throw new MatterWorkflowException("Matter document link not found or inactive.", 404);

                matterDoc.RecordStatus = RecordStatus.Archived;
                matterDoc.UpdatedBy = actionUser.DisplayName;
                matterDoc.UpdatedAt = DateTimeOffset.UtcNow;

                var maxSeq = await db.MatterEvents
                    .Where(e => e.MatterId == matterId)
                    .MaxAsync(e => (int?)e.SequenceNumber, opCt) ?? 0;
                var seq = maxSeq + 1;

                matter.Revision++;
                matter.UpdatedBy = actionUser.DisplayName;
                matter.UpdatedAt = DateTimeOffset.UtcNow;

                var ev = new MatterEvent
                {
                    Id = eventId,
                    MatterId = matterId,
                    SequenceNumber = seq,
                    Action = MatterEventAction.DocumentUnlinked,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = DateTimeOffset.UtcNow,
                    DocumentId = documentId,
                    MatterDocumentId = matterDoc.Id,
                    WorkstreamIdSnapshot = matter.WorkstreamId,
                    WorkstreamNameSnapshot = matter.Workstream?.Name
                };
                db.MatterEvents.Add(ev);

                await db.SaveChangesAsync(opCt);
                return true;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();
                var evExists = await db.MatterEvents.AsNoTracking()
                    .AnyAsync(e => e.Id == eventId && e.MatterId == matterId && e.Action == MatterEventAction.DocumentUnlinked && e.DocumentId == documentId, verifyCt);
                return evExists;
            },
            ct);
    }

    private static string ComputeSha256Hash(byte[] bytes)
    {
        var hashBytes = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hashBytes);
    }

    public static List<int> ParseAndValidatePageRange(string pageRangeText, int maxAllowedPages = 50)
    {
        if (string.IsNullOrWhiteSpace(pageRangeText))
            throw new MatterWorkflowException("Page range specification is mandatory.", 400);

        var rawPages = new HashSet<int>();
        var parts = pageRangeText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            throw new MatterWorkflowException("Invalid page range format.", 400);

        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                var rangeParts = part.Split('-', StringSplitOptions.TrimEntries);
                if (rangeParts.Length != 2 || !int.TryParse(rangeParts[0], out var start) || !int.TryParse(rangeParts[1], out var end))
                {
                    throw new MatterWorkflowException($"Invalid page range format '{part}'. Expected format like '9-11'.", 400);
                }
                if (start <= 0 || end <= 0)
                {
                    throw new MatterWorkflowException($"Page numbers must be positive integers (1-based). Got '{part}'.", 400);
                }
                if (start > end)
                {
                    throw new MatterWorkflowException($"Invalid reversed page range '{part}'. Start page cannot be greater than end page.", 400);
                }
                for (int p = start; p <= end; p++)
                {
                    rawPages.Add(p);
                }
            }
            else
            {
                if (!int.TryParse(part, out var singlePage) || singlePage <= 0)
                {
                    throw new MatterWorkflowException($"Invalid page number '{part}'. Must be a positive integer (1-based).", 400);
                }
                rawPages.Add(singlePage);
            }
        }

        var sorted = rawPages.OrderBy(x => x).ToList();
        if (sorted.Count == 0)
            throw new MatterWorkflowException("No valid pages were specified.", 400);

        if (sorted.Count > maxAllowedPages)
            throw new MatterWorkflowException($"Selected page count ({sorted.Count}) exceeds maximum allowed limit of {maxAllowedPages} pages per extraction job.", 400);

        return sorted;
    }

    private static (byte[] bytes, List<int> sortedPages, string normalizedPagesText) ExtractPdfPages(Stream sourceStream, string pageRangeText, int maxAllowedPages = 50)
    {
        var sortedPages = ParseAndValidatePageRange(pageRangeText, maxAllowedPages);

        using var sourcePdf = UglyToad.PdfPig.PdfDocument.Open(sourceStream);
        var totalPages = sourcePdf.NumberOfPages;

        foreach (var p in sortedPages)
        {
            if (p < 1 || p > totalPages)
            {
                throw new MatterWorkflowException($"Page number {p} is out of bounds for the source PDF (total pages in source: {totalPages}).", 400);
            }
        }

        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        foreach (var pageNum in sortedPages)
        {
            builder.AddPage(sourcePdf, pageNum);
        }

        var newBytes = builder.Build();

        using var verifyPdf = UglyToad.PdfPig.PdfDocument.Open(newBytes);
        if (verifyPdf.NumberOfPages != sortedPages.Count)
        {
            throw new MatterWorkflowException("Extracted PDF page count verification failed.", 500);
        }

        var normalizedPagesText = FormatPageRanges(sortedPages);
        return (newBytes, sortedPages, normalizedPagesText);
    }

    private static string FormatPageRanges(List<int> sortedPages)
    {
        if (sortedPages.Count == 0) return "";
        var ranges = new List<string>();
        int rangeStart = sortedPages[0];
        int prev = sortedPages[0];

        for (int i = 1; i < sortedPages.Count; i++)
        {
            if (sortedPages[i] == prev + 1)
            {
                prev = sortedPages[i];
            }
            else
            {
                ranges.Add(rangeStart == prev ? rangeStart.ToString() : $"{rangeStart}-{prev}");
                rangeStart = sortedPages[i];
                prev = sortedPages[i];
            }
        }
        ranges.Add(rangeStart == prev ? rangeStart.ToString() : $"{rangeStart}-{prev}");
        return string.Join(", ", ranges);
    }
}
