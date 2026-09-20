namespace LAC.Infrastructure;

using System.IO;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public class WorkItemWorkflowException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed record WorkItemAttachmentUpload(
    Stream Stream,
    string FileName,
    string ContentType,
    string? Title = null,
    string? AttachmentType = null
);

public sealed record CreateWorkItemCommand(
    string Title,
    string? Instructions,
    Guid WorkstreamId,
    Guid OfficeDeskId,
    Guid? AssignedUserId,
    WorkItemPriority Priority,
    DateTimeOffset? DueAt,
    Guid? MatterId = null,
    Guid? DakId = null,
    IReadOnlyList<WorkItemAttachmentUpload>? Attachments = null
);

public sealed record AddWorkItemUpdateCommand(
    string Message,
    int ExpectedRevision
);

public sealed record StartWorkCommand(
    int ExpectedRevision
);

public sealed record UploadWorkItemAttachmentCommand(
    Stream Stream,
    string FileName,
    string ContentType,
    string? Title,
    string? AttachmentType,
    int ExpectedRevision
);

public sealed record RemoveWorkItemAttachmentCommand(
    int ExpectedRevision
);

public sealed record CreateWorkItemResult(
    Guid WorkItemId,
    int Revision,
    IReadOnlyList<string> UploadedFiles,
    IReadOnlyList<string> FailedFiles
);

public sealed class WorkItemWorkflowService(
    LacDbContext db,
    IDocumentStorage storage,
    IWorkItemAuthorizationService workItemAuth,
    IMatterAuthorizationService matterAuth,
    IDakAuthorizationService dakAuth,
    Func<Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy>? strategyFactory = null)
{
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

    private async Task<WorkItem> LockWorkItemAsync(Guid workItemId, CancellationToken ct)
    {
        WorkItem? item;
        if (db.Database.IsRelational())
        {
            item = await db.WorkItems
                .FromSqlInterpolated($"SELECT * FROM \"WorkItems\" WHERE \"Id\" = {workItemId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
        }
        else
        {
            item = await db.WorkItems.FirstOrDefaultAsync(w => w.Id == workItemId, ct);
        }

        return item ?? throw new WorkItemWorkflowException("Work item not found.", 404);
    }

    private async Task<(string DisplayName, string? Designation)> GetActorSnapshotAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.AppUsers.AsNoTracking()
            .Include(u => u.Designation)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            throw new WorkItemWorkflowException("Actor user not found.", 401);

        return (user.DisplayName, user.Designation?.Name);
    }

    private static void ValidateAttachmentStream(Stream stream, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new WorkItemWorkflowException("Attachment must have a valid file name.", 400);

        if (stream.Length == 0)
            throw new WorkItemWorkflowException("Cannot upload an empty file (0 bytes).", 400);

        var ext = Path.GetExtension(fileName);
        try
        {
            MatterDocumentValidation.ValidateFileContent(stream, ext);
        }
        catch (MatterWorkflowException ex)
        {
            throw new WorkItemWorkflowException(ex.Message, ex.StatusCode);
        }
    }

    // ========================================================================
    // 1. CREATE WORK ITEM
    // ========================================================================
    public async Task<CreateWorkItemResult> CreateWorkItemAsync(
        CreateWorkItemCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
            throw new WorkItemWorkflowException("Title is mandatory.", 400);

        var canCreate = await workItemAuth.CanCreateWorkItemAsync(
            command.WorkstreamId,
            command.OfficeDeskId,
            command.AssignedUserId,
            callerUserId,
            ct);

        if (!canCreate)
            throw new WorkItemWorkflowException("You do not have permission to create and assign work in this workstream/desk.", 403);

        if (command.MatterId.HasValue)
        {
            var canViewMatter = await matterAuth.CanAccessMatterAsync(command.MatterId.Value, PermissionCodes.MatterView, callerUserId, ct);
            if (!canViewMatter)
                throw new WorkItemWorkflowException("You do not have permission to access the linked matter.", 403);
        }

        if (command.DakId.HasValue)
        {
            var canViewDak = await dakAuth.CanAccessDakAsync(command.DakId.Value, PermissionCodes.DakView, callerUserId, ct);
            if (!canViewDak)
                throw new WorkItemWorkflowException("You do not have permission to access the linked dak.", 403);
        }

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);

        // Pre-allocate stable entity and event IDs outside retries
        var workItemId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var createdEventId = Guid.NewGuid();
        var assignedEventId = Guid.NewGuid();
        var matterLinkId = command.MatterId.HasValue ? Guid.NewGuid() : (Guid?)null;
        var dakLinkId = command.DakId.HasValue ? Guid.NewGuid() : (Guid?)null;

        // Process attachments outside retry loop
        var savedFiles = new List<(DocumentStorageWriteResult StorageResult, string FileName, string Ext, string? Title, string? AttachmentType, Guid DocId, Guid AttachId, Guid EventId)>();
        var uploadedNames = new List<string>();
        var failedNames = new List<string>();

        if (command.Attachments is { Count: > 0 })
        {
            foreach (var att in command.Attachments)
            {
                try
                {
                    ValidateAttachmentStream(att.Stream, att.FileName);
                    var ext = Path.GetExtension(att.FileName).ToLowerInvariant();
                    var storageRes = await storage.SaveAndHashAsync(att.Stream, att.FileName, ct);
                    savedFiles.Add((storageRes, att.FileName, ext, att.Title, att.AttachmentType, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
                    uploadedNames.Add(att.FileName);
                }
                catch
                {
                    failedNames.Add(att.FileName);
                }
            }
        }

        try
        {
            Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
                await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == createdEventId, c);

            var result = await ExecuteWorkflowTransactionAsync(async c =>
            {
                db.ChangeTracker.Clear();

                if (await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == createdEventId, c))
                {
                    var existingItem = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workItemId, c);
                    return new CreateWorkItemResult(workItemId, existingItem?.Revision ?? 0, uploadedNames, failedNames);
                }

                var now = DateTimeOffset.UtcNow;
                var workItem = new WorkItem
                {
                    Id = workItemId,
                    WorkstreamId = command.WorkstreamId,
                    Title = command.Title.Trim(),
                    Instructions = string.IsNullOrWhiteSpace(command.Instructions) ? null : command.Instructions.Trim(),
                    Priority = command.Priority,
                    Status = WorkItemStatus.Assigned,
                    Origin = WorkItemOrigin.Manual,
                    DueAt = command.DueAt,
                    Revision = 0,
                    RequestedByUserId = callerUserId,
                    RequestedByDisplayNameSnapshot = actorDisplayName,
                    RequestedByDesignationSnapshot = actorDesignation,
                    LastActivityAt = now,
                    RecordStatus = RecordStatus.Active
                };
                db.WorkItems.Add(workItem);

                var assignment = new WorkItemAssignment
                {
                    Id = assignmentId,
                    WorkItemId = workItemId,
                    OfficeDeskId = command.OfficeDeskId,
                    AssignedUserId = command.AssignedUserId,
                    AssignedByUserId = callerUserId,
                    AssignedAt = now,
                    IsActive = true,
                    RecordStatus = RecordStatus.Active
                };
                db.WorkItemAssignments.Add(assignment);

                var seq = 1;
                var createdEvent = new WorkItemEvent
                {
                    Id = createdEventId,
                    WorkItemId = workItemId,
                    SequenceNumber = seq++,
                    Action = WorkItemEventAction.Created,
                    ActionByUserId = callerUserId,
                    ActionByDisplayNameSnapshot = actorDisplayName,
                    ActionByDesignationSnapshot = actorDesignation,
                    ActionAt = now,
                    ToStatus = WorkItemStatus.Assigned,
                    RemarksSnapshot = "Work item created"
                };
                db.WorkItemEvents.Add(createdEvent);

                var assignedEvent = new WorkItemEvent
                {
                    Id = assignedEventId,
                    WorkItemId = workItemId,
                    SequenceNumber = seq++,
                    Action = WorkItemEventAction.Assigned,
                    ActionByUserId = callerUserId,
                    ActionByDisplayNameSnapshot = actorDisplayName,
                    ActionByDesignationSnapshot = actorDesignation,
                    ActionAt = now,
                    TargetDeskId = command.OfficeDeskId,
                    TargetUserId = command.AssignedUserId,
                    RemarksSnapshot = "Assigned to desk"
                };
                db.WorkItemEvents.Add(assignedEvent);

                if (command.MatterId.HasValue && matterLinkId.HasValue)
                {
                    var matterLink = new WorkItemMatterLink
                    {
                        Id = matterLinkId.Value,
                        WorkItemId = workItemId,
                        MatterId = command.MatterId.Value,
                        RecordStatus = RecordStatus.Active
                    };
                    db.WorkItemMatterLinks.Add(matterLink);

                    db.WorkItemEvents.Add(new WorkItemEvent
                    {
                        Id = Guid.NewGuid(),
                        WorkItemId = workItemId,
                        SequenceNumber = seq++,
                        Action = WorkItemEventAction.ContextLinked,
                        ActionByUserId = callerUserId,
                        ActionByDisplayNameSnapshot = actorDisplayName,
                        ActionByDesignationSnapshot = actorDesignation,
                        ActionAt = now,
                        ContextType = "Matter",
                        ContextEntityId = command.MatterId.Value,
                        RemarksSnapshot = "Matter context linked"
                    });
                }

                if (command.DakId.HasValue && dakLinkId.HasValue)
                {
                    var dakLink = new WorkItemDakLink
                    {
                        Id = dakLinkId.Value,
                        WorkItemId = workItemId,
                        DakId = command.DakId.Value,
                        RecordStatus = RecordStatus.Active
                    };
                    db.WorkItemDakLinks.Add(dakLink);

                    db.WorkItemEvents.Add(new WorkItemEvent
                    {
                        Id = Guid.NewGuid(),
                        WorkItemId = workItemId,
                        SequenceNumber = seq++,
                        Action = WorkItemEventAction.ContextLinked,
                        ActionByUserId = callerUserId,
                        ActionByDisplayNameSnapshot = actorDisplayName,
                        ActionByDesignationSnapshot = actorDesignation,
                        ActionAt = now,
                        ContextType = "Dak",
                        ContextEntityId = command.DakId.Value,
                        RemarksSnapshot = "Dak context linked"
                    });
                }

                foreach (var saved in savedFiles)
                {
                    var mime = MatterDocumentValidation.GetServerDerivedMimeType(saved.Ext);
                    var doc = new Document
                    {
                        Id = saved.DocId,
                        OriginalFileName = saved.FileName,
                        StoragePath = saved.StorageResult.StoragePath,
                        MimeType = mime,
                        Sha256Hash = saved.StorageResult.Sha256Hash,
                        FileSize = saved.StorageResult.FileSize,
                        DocumentType = "WorkItemAttachment",
                        UploadedBy = actorDisplayName,
                        Status = "Active",
                        Version = 1,
                        UploadedAt = now,
                        RecordStatus = RecordStatus.Active
                    };
                    db.Documents.Add(doc);

                    var attachment = new WorkItemAttachment
                    {
                        Id = saved.AttachId,
                        WorkItemId = workItemId,
                        DocumentId = saved.DocId,
                        Title = saved.Title ?? saved.FileName,
                        AttachmentType = saved.AttachmentType ?? "SupportingDocument",
                        RecordStatus = RecordStatus.Active
                    };
                    db.WorkItemAttachments.Add(attachment);

                    db.WorkItemEvents.Add(new WorkItemEvent
                    {
                        Id = saved.EventId,
                        WorkItemId = workItemId,
                        SequenceNumber = seq++,
                        Action = WorkItemEventAction.AttachmentAdded,
                        ActionByUserId = callerUserId,
                        ActionByDisplayNameSnapshot = actorDisplayName,
                        ActionByDesignationSnapshot = actorDesignation,
                        ActionAt = now,
                        DocumentId = saved.DocId,
                        RemarksSnapshot = saved.FileName
                    });
                }

                await db.SaveChangesAsync(c);
                return new CreateWorkItemResult(workItemId, 0, uploadedNames, failedNames);
            }, verifySucceeded, ct);

            return result;
        }
        catch
        {
            // Compensate newly saved physical files on definite DB failure
            foreach (var saved in savedFiles)
            {
                try
                {
                    var committed = await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == saved.EventId, CancellationToken.None);
                    if (!committed)
                    {
                        await storage.DeleteAsync(saved.StorageResult.StoragePath, CancellationToken.None);
                    }
                }
                catch { }
            }
            throw;
        }
    }

    // ========================================================================
    // 2. MARK SEEN (IDEMPOTENT)
    // ========================================================================
    public async Task<bool> MarkSeenAsync(
        Guid workItemId,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var canView = await workItemAuth.CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemView, callerUserId, ct);
        if (!canView)
            throw new WorkItemWorkflowException("You do not have permission to access this work item.", 403);

        var assignment = await db.WorkItemAssignments
            .FirstOrDefaultAsync(a => a.WorkItemId == workItemId && a.IsActive && a.RecordStatus == RecordStatus.Active, ct);

        if (assignment is null) return false;

        // Idempotent: already marked seen
        if (assignment.FirstSeenAt.HasValue) return false;

        // Verify caller is part of current responsibility
        var isResponsible = assignment.AssignedUserId == callerUserId;
        if (!isResponsible)
        {
            isResponsible = await db.UserDeskMemberships.AsNoTracking()
                .AnyAsync(m => m.UserId == callerUserId
                            && m.OfficeDeskId == assignment.OfficeDeskId
                            && m.IsActive
                            && m.RemovedAt == null
                            && m.RecordStatus == RecordStatus.Active
                            && m.OfficeDesk.IsActive
                            && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);
        }

        if (!isResponsible) return false;

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var stableEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
            await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == stableEventId, c);

        await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            var currentAssignment = await db.WorkItemAssignments
                .FirstOrDefaultAsync(a => a.WorkItemId == workItemId && a.IsActive && a.RecordStatus == RecordStatus.Active, c);

            if (currentAssignment is null || currentAssignment.FirstSeenAt.HasValue)
                return true;

            var now = DateTimeOffset.UtcNow;
            currentAssignment.FirstSeenAt = now;
            currentAssignment.FirstSeenByUserId = callerUserId;

            var maxSeq = await db.WorkItemEvents
                .Where(e => e.WorkItemId == workItemId)
                .MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            var seenEvent = new WorkItemEvent
            {
                Id = stableEventId,
                WorkItemId = workItemId,
                SequenceNumber = maxSeq + 1,
                Action = WorkItemEventAction.FirstSeen,
                ActionByUserId = callerUserId,
                ActionByDisplayNameSnapshot = actorDisplayName,
                ActionByDesignationSnapshot = actorDesignation,
                ActionAt = now,
                RemarksSnapshot = "First seen by responsible handler"
            };
            db.WorkItemEvents.Add(seenEvent);

            await db.SaveChangesAsync(c);
            return true;
        }, verifySucceeded, ct);

        return true;
    }

    // ========================================================================
    // 3. START WORK
    // ========================================================================
    public async Task<int> StartWorkAsync(
        Guid workItemId,
        StartWorkCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var canUpdate = await workItemAuth.CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemUpdate, callerUserId, ct);
        if (!canUpdate)
            throw new WorkItemWorkflowException("You do not have permission to update this work item.", 403);

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var stableEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
            await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == stableEventId, c);

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == stableEventId, c))
            {
                var existingItem = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workItemId, c);
                return existingItem?.Revision ?? 0;
            }

            var item = await LockWorkItemAsync(workItemId, c);

            if (item.Revision != command.ExpectedRevision)
                throw new WorkItemWorkflowException("Work item was modified by another operation. Please refresh.", 409);

            if (item.Status != WorkItemStatus.Assigned && item.Status != WorkItemStatus.ReturnedForCorrection)
                throw new WorkItemWorkflowException($"Cannot start work from status '{item.Status}'. Allowed from Assigned or ReturnedForCorrection.", 400);

            var oldStatus = item.Status;
            var now = DateTimeOffset.UtcNow;

            item.Status = WorkItemStatus.InProgress;
            item.Revision += 1;
            item.LastActivityAt = now;

            var assignment = await db.WorkItemAssignments
                .FirstOrDefaultAsync(a => a.WorkItemId == workItemId && a.IsActive && a.RecordStatus == RecordStatus.Active, c);

            if (assignment != null && !assignment.FirstActionAt.HasValue)
            {
                var isResponsible = assignment.AssignedUserId == callerUserId;
                if (!isResponsible)
                {
                    isResponsible = await db.UserDeskMemberships.AsNoTracking()
                        .AnyAsync(m => m.UserId == callerUserId
                                    && m.OfficeDeskId == assignment.OfficeDeskId
                                    && m.IsActive
                                    && m.RemovedAt == null
                                    && m.RecordStatus == RecordStatus.Active, c);
                }

                if (isResponsible)
                {
                    assignment.FirstActionAt = now;
                    assignment.FirstActionByUserId = callerUserId;
                }
            }

            var maxSeq = await db.WorkItemEvents
                .Where(e => e.WorkItemId == workItemId)
                .MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            var startedEvent = new WorkItemEvent
            {
                Id = stableEventId,
                WorkItemId = workItemId,
                SequenceNumber = maxSeq + 1,
                Action = WorkItemEventAction.Started,
                ActionByUserId = callerUserId,
                ActionByDisplayNameSnapshot = actorDisplayName,
                ActionByDesignationSnapshot = actorDesignation,
                ActionAt = now,
                FromStatus = oldStatus,
                ToStatus = WorkItemStatus.InProgress,
                RemarksSnapshot = "Work started"
            };
            db.WorkItemEvents.Add(startedEvent);

            await db.SaveChangesAsync(c);
            return item.Revision;
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 4. ADD PROGRESS UPDATE
    // ========================================================================
    public async Task<int> AddUpdateAsync(
        Guid workItemId,
        AddWorkItemUpdateCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Message))
            throw new WorkItemWorkflowException("Update message cannot be empty.", 400);

        var canUpdate = await workItemAuth.CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemUpdate, callerUserId, ct);
        if (!canUpdate)
        {
            var canContribute = await workItemAuth.CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemContribute, callerUserId, ct);
            if (!canContribute)
                throw new WorkItemWorkflowException("You do not have permission to update this work item.", 403);
        }

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var updateId = Guid.NewGuid();
        var stableEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
            await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == stableEventId, c);

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == stableEventId, c))
            {
                var existingItem = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workItemId, c);
                return existingItem?.Revision ?? 0;
            }

            var item = await LockWorkItemAsync(workItemId, c);

            if (item.Revision != command.ExpectedRevision)
                throw new WorkItemWorkflowException("Work item was modified by another operation. Please refresh.", 409);

            if (item.Status == WorkItemStatus.Completed || item.Status == WorkItemStatus.Cancelled)
                throw new WorkItemWorkflowException($"Cannot add updates to a {item.Status} work item.", 400);

            var now = DateTimeOffset.UtcNow;

            if (item.Status == WorkItemStatus.Assigned)
            {
                item.Status = WorkItemStatus.InProgress;
            }

            item.Revision += 1;
            item.LastActivityAt = now;

            var assignment = await db.WorkItemAssignments
                .FirstOrDefaultAsync(a => a.WorkItemId == workItemId && a.IsActive && a.RecordStatus == RecordStatus.Active, c);

            if (assignment != null && !assignment.FirstActionAt.HasValue)
            {
                var isResponsible = assignment.AssignedUserId == callerUserId;
                if (!isResponsible)
                {
                    isResponsible = await db.UserDeskMemberships.AsNoTracking()
                        .AnyAsync(m => m.UserId == callerUserId
                                    && m.OfficeDeskId == assignment.OfficeDeskId
                                    && m.IsActive
                                    && m.RemovedAt == null
                                    && m.RecordStatus == RecordStatus.Active, c);
                }

                if (isResponsible)
                {
                    assignment.FirstActionAt = now;
                    assignment.FirstActionByUserId = callerUserId;
                }
            }

            var update = new WorkItemUpdate
            {
                Id = updateId,
                WorkItemId = workItemId,
                Message = command.Message.Trim(),
                AddedByUserId = callerUserId,
                AddedByDisplayNameSnapshot = actorDisplayName,
                AddedByDesignationSnapshot = actorDesignation,
                AddedAt = now
            };
            db.WorkItemUpdates.Add(update);

            var maxSeq = await db.WorkItemEvents
                .Where(e => e.WorkItemId == workItemId)
                .MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            var updateEvent = new WorkItemEvent
            {
                Id = stableEventId,
                WorkItemId = workItemId,
                SequenceNumber = maxSeq + 1,
                Action = WorkItemEventAction.UpdateAdded,
                ActionByUserId = callerUserId,
                ActionByDisplayNameSnapshot = actorDisplayName,
                ActionByDesignationSnapshot = actorDesignation,
                ActionAt = now,
                WorkItemUpdateId = updateId,
                RemarksSnapshot = command.Message.Trim()
            };
            db.WorkItemEvents.Add(updateEvent);

            await db.SaveChangesAsync(c);
            return item.Revision;
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 5. UPLOAD ATTACHMENT
    // ========================================================================
    public async Task<Guid> UploadAttachmentAsync(
        Guid workItemId,
        UploadWorkItemAttachmentCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var canUpdate = await workItemAuth.CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemUpdate, callerUserId, ct);
        if (!canUpdate)
        {
            var canContribute = await workItemAuth.CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemContribute, callerUserId, ct);
            if (!canContribute)
                throw new WorkItemWorkflowException("You do not have permission to attach documents to this work item.", 403);
        }

        ValidateAttachmentStream(command.Stream, command.FileName);
        var ext = Path.GetExtension(command.FileName).ToLowerInvariant();
        var storageRes = await storage.SaveAndHashAsync(command.Stream, command.FileName, ct);

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var documentId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var stableEventId = Guid.NewGuid();

        try
        {
            Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
                await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == stableEventId, c);

            await ExecuteWorkflowTransactionAsync(async c =>
            {
                db.ChangeTracker.Clear();

                if (await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == stableEventId, c))
                    return attachmentId;

                var item = await LockWorkItemAsync(workItemId, c);

                if (item.Revision != command.ExpectedRevision)
                    throw new WorkItemWorkflowException("Work item was modified by another operation. Please refresh.", 409);

                if (item.Status == WorkItemStatus.Completed || item.Status == WorkItemStatus.Cancelled)
                    throw new WorkItemWorkflowException($"Cannot add attachments to a {item.Status} work item.", 400);

                var now = DateTimeOffset.UtcNow;
                var mime = MatterDocumentValidation.GetServerDerivedMimeType(ext);

                var doc = new Document
                {
                    Id = documentId,
                    OriginalFileName = command.FileName,
                    StoragePath = storageRes.StoragePath,
                    MimeType = mime,
                    Sha256Hash = storageRes.Sha256Hash,
                    FileSize = storageRes.FileSize,
                    DocumentType = "WorkItemAttachment",
                    UploadedBy = actorDisplayName,
                    Status = "Active",
                    Version = 1,
                    UploadedAt = now,
                    RecordStatus = RecordStatus.Active
                };
                db.Documents.Add(doc);

                var attachment = new WorkItemAttachment
                {
                    Id = attachmentId,
                    WorkItemId = workItemId,
                    DocumentId = documentId,
                    Title = string.IsNullOrWhiteSpace(command.Title) ? command.FileName : command.Title.Trim(),
                    AttachmentType = string.IsNullOrWhiteSpace(command.AttachmentType) ? "SupportingDocument" : command.AttachmentType.Trim(),
                    RecordStatus = RecordStatus.Active
                };
                db.WorkItemAttachments.Add(attachment);

                item.Revision += 1;
                item.LastActivityAt = now;

                var maxSeq = await db.WorkItemEvents
                    .Where(e => e.WorkItemId == workItemId)
                    .MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

                var attEvent = new WorkItemEvent
                {
                    Id = stableEventId,
                    WorkItemId = workItemId,
                    SequenceNumber = maxSeq + 1,
                    Action = WorkItemEventAction.AttachmentAdded,
                    ActionByUserId = callerUserId,
                    ActionByDisplayNameSnapshot = actorDisplayName,
                    ActionByDesignationSnapshot = actorDesignation,
                    ActionAt = now,
                    DocumentId = documentId,
                    RemarksSnapshot = command.FileName
                };
                db.WorkItemEvents.Add(attEvent);

                await db.SaveChangesAsync(c);
                return attachmentId;
            }, verifySucceeded, ct);

            return attachmentId;
        }
        catch
        {
            try
            {
                var committed = await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == stableEventId, CancellationToken.None);
                if (!committed)
                {
                    await storage.DeleteAsync(storageRes.StoragePath, CancellationToken.None);
                }
            }
            catch { }
            throw;
        }
    }

    // ========================================================================
    // 6. REMOVE ATTACHMENT
    // ========================================================================
    public async Task<int> RemoveAttachmentAsync(
        Guid workItemId,
        Guid attachmentId,
        RemoveWorkItemAttachmentCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var canUpdate = await workItemAuth.CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemUpdate, callerUserId, ct);
        if (!canUpdate)
            throw new WorkItemWorkflowException("You do not have permission to remove attachments from this work item.", 403);

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var stableEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
            await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == stableEventId, c);

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == stableEventId, c))
            {
                var existingItem = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workItemId, c);
                return existingItem?.Revision ?? 0;
            }

            var item = await LockWorkItemAsync(workItemId, c);

            if (item.Revision != command.ExpectedRevision)
                throw new WorkItemWorkflowException("Work item was modified by another operation. Please refresh.", 409);

            if (item.Status == WorkItemStatus.Completed || item.Status == WorkItemStatus.Cancelled)
                throw new WorkItemWorkflowException($"Cannot remove attachments from a {item.Status} work item.", 400);

            var attachment = await db.WorkItemAttachments
                .FirstOrDefaultAsync(a => a.Id == attachmentId && a.WorkItemId == workItemId && a.RecordStatus == RecordStatus.Active, c);

            if (attachment is null)
                throw new WorkItemWorkflowException("Active attachment not found.", 404);

            var now = DateTimeOffset.UtcNow;
            attachment.RecordStatus = RecordStatus.Archived;

            item.Revision += 1;
            item.LastActivityAt = now;

            var maxSeq = await db.WorkItemEvents
                .Where(e => e.WorkItemId == workItemId)
                .MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            var removeEvent = new WorkItemEvent
            {
                Id = stableEventId,
                WorkItemId = workItemId,
                SequenceNumber = maxSeq + 1,
                Action = WorkItemEventAction.AttachmentRemoved,
                ActionByUserId = callerUserId,
                ActionByDisplayNameSnapshot = actorDisplayName,
                ActionByDesignationSnapshot = actorDesignation,
                ActionAt = now,
                DocumentId = attachment.DocumentId,
                RemarksSnapshot = attachment.Title
            };
            db.WorkItemEvents.Add(removeEvent);

            await db.SaveChangesAsync(c);
            return item.Revision;
        }, verifySucceeded, ct);
    }
}
