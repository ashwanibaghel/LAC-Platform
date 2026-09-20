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
    string? Status = null
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
                .FirstOrDefaultAsync(ct);
        }
        else
        {
            matter = await db.Matters.FirstOrDefaultAsync(m => m.Id == matterId, ct);
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

                var createdEvent = new MatterEvent
                {
                    Id = eventId,
                    MatterId = matterId,
                    SequenceNumber = 1,
                    Action = MatterEventAction.Created,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = now,
                    WorkstreamIdSnapshot = cmd.WorkstreamId
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
                    WorkstreamIdSnapshot = matter.WorkstreamId
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
                    TargetWorkstreamId = cmd.TargetWorkstreamId,
                    WorkstreamIdSnapshot = cmd.TargetWorkstreamId
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
                        WorkstreamIdSnapshot = matter.WorkstreamId
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
                    .AnyAsync(md => md.MatterId == matterId && md.DocumentId == cmd.DocumentId, opCt);
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
                    WorkstreamIdSnapshot = matter.WorkstreamId
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
                    WorkstreamIdSnapshot = matter.WorkstreamId
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
}
