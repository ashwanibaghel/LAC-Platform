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

public sealed record AddContributorCommand(
    Guid UserId,
    string? Instructions,
    int ExpectedRevision
);

public sealed record AddContributorResult(
    Guid ContributorId,
    int Revision
);

public sealed record SubmitContributionCommand(
    int ExpectedRevision,
    string? Note
);

public sealed record SubmitContributionResult(
    int Revision
);

public sealed record ReturnContributionCommand(
    int ExpectedRevision,
    string Remarks
);

public sealed record ReturnContributionResult(
    int Revision
);

public sealed record AcceptContributionCommand(
    int ExpectedRevision,
    string? Remarks
);

public sealed record AcceptContributionResult(
    int Revision
);

public sealed record RemoveContributorCommand(
    int ExpectedRevision,
    string? Reason
);

public sealed record RemoveContributorResult(
    int Revision
);
public sealed record ReassignWorkItemCommand(Guid OfficeDeskId, Guid? AssignedUserId, string Reason, int ExpectedRevision);
public sealed record ReassignWorkItemResult(Guid AssignmentId, int Revision);

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
        var matterEventId = command.MatterId.HasValue ? Guid.NewGuid() : (Guid?)null;
        var dakEventId = command.DakId.HasValue ? Guid.NewGuid() : (Guid?)null;

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
            {
                // 1. Exact WorkItem ID exists
                var itemExists = await db.WorkItems.AsNoTracking().AnyAsync(w => w.Id == workItemId, c);
                if (!itemExists) return false;

                // 2. Exact Created event ID + Created action
                var createdExists = await db.WorkItemEvents.AsNoTracking().AnyAsync(e =>
                    e.Id == createdEventId &&
                    e.WorkItemId == workItemId &&
                    e.Action == WorkItemEventAction.Created, c);
                if (!createdExists) return false;

                // 3. Exact Assigned event ID + Assigned action + target desk/user snapshots
                var assignedExists = await db.WorkItemEvents.AsNoTracking().AnyAsync(e =>
                    e.Id == assignedEventId &&
                    e.WorkItemId == workItemId &&
                    e.Action == WorkItemEventAction.Assigned &&
                    e.TargetDeskId == command.OfficeDeskId &&
                    e.TargetUserId == command.AssignedUserId, c);
                if (!assignedExists) return false;

                // 4. Exact Matter/Dak typed link IDs where requested
                if (matterLinkId.HasValue && command.MatterId.HasValue)
                {
                    var targetMatterId = command.MatterId.Value;
                    var matterLinkExists = await db.WorkItemMatterLinks.AsNoTracking().AnyAsync(l =>
                        l.Id == matterLinkId.Value &&
                        l.WorkItemId == workItemId &&
                        l.MatterId == targetMatterId, c);
                    if (!matterLinkExists) return false;
                }

                if (dakLinkId.HasValue && command.DakId.HasValue)
                {
                    var targetDakId = command.DakId.Value;
                    var dakLinkExists = await db.WorkItemDakLinks.AsNoTracking().AnyAsync(l =>
                        l.Id == dakLinkId.Value &&
                        l.WorkItemId == workItemId &&
                        l.DakId == targetDakId, c);
                    if (!dakLinkExists) return false;
                }

                // 5. Exact stable ContextLinked event IDs where requested
                if (matterEventId.HasValue && command.MatterId.HasValue)
                {
                    var targetMatterId = command.MatterId.Value;
                    var matterEventExists = await db.WorkItemEvents.AsNoTracking().AnyAsync(e =>
                        e.Id == matterEventId.Value &&
                        e.WorkItemId == workItemId &&
                        e.Action == WorkItemEventAction.ContextLinked &&
                        e.ContextType == "Matter" &&
                        e.ContextEntityId == targetMatterId, c);
                    if (!matterEventExists) return false;
                }

                if (dakEventId.HasValue && command.DakId.HasValue)
                {
                    var targetDakId = command.DakId.Value;
                    var dakEventExists = await db.WorkItemEvents.AsNoTracking().AnyAsync(e =>
                        e.Id == dakEventId.Value &&
                        e.WorkItemId == workItemId &&
                        e.Action == WorkItemEventAction.ContextLinked &&
                        e.ContextType == "Dak" &&
                        e.ContextEntityId == targetDakId, c);
                    if (!dakEventExists) return false;
                }

                // 6. Attachment immutable event IDs when attachments were part of the service command
                if (savedFiles.Count > 0)
                {
                    var savedEventIds = savedFiles.Select(s => s.EventId).ToList();
                    var foundCount = await db.WorkItemEvents.AsNoTracking().CountAsync(e =>
                        savedEventIds.Contains(e.Id) &&
                        e.WorkItemId == workItemId &&
                        e.Action == WorkItemEventAction.AttachmentAdded, c);
                    if (foundCount != savedEventIds.Count) return false;
                }

                return true;
            };

            var result = await ExecuteWorkflowTransactionAsync(async c =>
            {
                db.ChangeTracker.Clear();

                if (await verifySucceeded(c))
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

                if (command.MatterId.HasValue && matterLinkId.HasValue && matterEventId.HasValue)
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
                        Id = matterEventId.Value,
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

                if (command.DakId.HasValue && dakLinkId.HasValue && dakEventId.HasValue)
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
                        Id = dakEventId.Value,
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

        // Verify caller is part of current live responsibility:
        // Caller must have a live active UserDeskMembership in the assigned active OfficeDesk.
        // For direct named assignment, caller must also be the assigned user.
        // For unnamed assignment, any live active desk member qualifies.
        var isDeskMember = await db.UserDeskMemberships.AsNoTracking()
            .AnyAsync(m => m.UserId == callerUserId
                        && m.OfficeDeskId == assignment.OfficeDeskId
                        && m.IsActive
                        && m.RemovedAt == null
                        && m.RecordStatus == RecordStatus.Active
                        && m.OfficeDesk.IsActive
                        && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);

        if (!isDeskMember) return false;


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
                var isDeskMember = await db.UserDeskMemberships.AsNoTracking()
                    .AnyAsync(m => m.UserId == callerUserId
                                && m.OfficeDeskId == assignment.OfficeDeskId
                                && m.IsActive
                                && m.RemovedAt == null
                                && m.RecordStatus == RecordStatus.Active
                                && m.OfficeDesk.IsActive
                                && m.OfficeDesk.RecordStatus == RecordStatus.Active, c);

                var isResponsible = isDeskMember;

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
                var isDeskMember = await db.UserDeskMemberships.AsNoTracking()
                    .AnyAsync(m => m.UserId == callerUserId
                                && m.OfficeDeskId == assignment.OfficeDeskId
                                && m.IsActive
                                && m.RemovedAt == null
                                && m.RecordStatus == RecordStatus.Active
                                && m.OfficeDesk.IsActive
                                && m.OfficeDesk.RecordStatus == RecordStatus.Active, c);

                var isResponsible = isDeskMember;

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

            var activeContributor = await db.WorkItemContributors.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WorkItemId == workItemId
                                       && x.UserId == callerUserId
                                       && x.IsActive
                                       && x.RecordStatus == RecordStatus.Active
                                       && (x.Status == WorkItemContributorStatus.Active || x.Status == WorkItemContributorStatus.Returned), c);

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
                ContributorId = activeContributor?.Id,
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

                var activeContributor = await db.WorkItemContributors.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.WorkItemId == workItemId
                                           && x.UserId == callerUserId
                                           && x.IsActive
                                           && x.RecordStatus == RecordStatus.Active
                                           && (x.Status == WorkItemContributorStatus.Active || x.Status == WorkItemContributorStatus.Returned), c);

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
                    ContributorId = activeContributor?.Id,
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

    // ========================================================================
    // 7. ADD CONTRIBUTOR
    // ========================================================================
    public async Task<AddContributorResult> AddContributorAsync(
        Guid workItemId,
        AddContributorCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var canAssign = await workItemAuth.CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemAssign, callerUserId, ct);
        if (!canAssign)
            throw new WorkItemWorkflowException("You do not have permission to add contributors on this work item.", 403);

        if (command.UserId == Guid.Empty)
            throw new WorkItemWorkflowException("A valid target user is required.", 400);

        var isEligible = await workItemAuth.IsUserEligibleContributorAsync(workItemId, command.UserId, ct);
        if (!isEligible)
            throw new WorkItemWorkflowException("Selected user is not eligible to participate as a contributor.", 400);

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var stableContributorId = Guid.NewGuid();
        var stableEventId = Guid.NewGuid();

        // Immutable event as the stable commit marker — stronger than mutable contributor row.
        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
            await db.WorkItemEvents.AsNoTracking().AnyAsync(e =>
                e.Id == stableEventId
                && e.WorkItemId == workItemId
                && e.Action == WorkItemEventAction.ContributorAdded
                && e.ContributorId == stableContributorId
                && e.TargetUserId == command.UserId, c)
            && await db.WorkItemContributors.AsNoTracking().AnyAsync(x =>
                x.Id == stableContributorId
                && x.WorkItemId == workItemId
                && x.UserId == command.UserId, c);

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            // Idempotency: if the stable event and contributor row already exist, the operation committed successfully
            // on a previous attempt. Return the stored revision without mutating again.
            if (await verifySucceeded(c))
            {
                var existingItem = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workItemId, c);
                return new AddContributorResult(stableContributorId, existingItem?.Revision ?? 0);
            }

            var item = await LockWorkItemAsync(workItemId, c);

            if (item.Revision != command.ExpectedRevision)
                throw new WorkItemWorkflowException("Work item was modified by another operation. Please refresh.", 409);

            if (item.Status == WorkItemStatus.Completed || item.Status == WorkItemStatus.Cancelled)
                throw new WorkItemWorkflowException($"Cannot add contributors to a {item.Status} work item.", 400);

            var existingActive = await db.WorkItemContributors.AnyAsync(x =>
                x.WorkItemId == workItemId &&
                x.UserId == command.UserId &&
                x.IsActive &&
                x.RecordStatus == RecordStatus.Active &&
                (x.Status == WorkItemContributorStatus.Active ||
                 x.Status == WorkItemContributorStatus.Submitted ||
                 x.Status == WorkItemContributorStatus.Returned), c);

            if (existingActive)
                throw new WorkItemWorkflowException("User is already an active contributor on this work item.", 400);

            var now = DateTimeOffset.UtcNow;
            var instructions = command.Instructions?.Trim();

            var contributor = new WorkItemContributor
            {
                Id = stableContributorId,
                WorkItemId = workItemId,
                UserId = command.UserId,
                AddedByUserId = callerUserId,
                AddedAt = now,
                Instructions = instructions,
                Status = WorkItemContributorStatus.Active,
                IsActive = true,
                RecordStatus = RecordStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = actorDisplayName
            };
            db.WorkItemContributors.Add(contributor);

            item.Revision += 1;
            item.LastActivityAt = now;

            var maxSeq = await db.WorkItemEvents
                .Where(e => e.WorkItemId == workItemId)
                .MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            var contributorEvent = new WorkItemEvent
            {
                Id = stableEventId,
                WorkItemId = workItemId,
                SequenceNumber = maxSeq + 1,
                Action = WorkItemEventAction.ContributorAdded,
                ActionByUserId = callerUserId,
                ActionByDisplayNameSnapshot = actorDisplayName,
                ActionByDesignationSnapshot = actorDesignation,
                ActionAt = now,
                TargetUserId = command.UserId,
                ContributorId = stableContributorId,
                RemarksSnapshot = instructions
            };
            db.WorkItemEvents.Add(contributorEvent);

            await db.SaveChangesAsync(c);
            return new AddContributorResult(stableContributorId, item.Revision);
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 8. SUBMIT CONTRIBUTION
    // ========================================================================
    public async Task<SubmitContributionResult> SubmitContributionAsync(
        Guid workItemId,
        Guid contributorId,
        SubmitContributionCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var canContribute = await workItemAuth.CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemContribute, callerUserId, ct);
        if (!canContribute)
            throw new WorkItemWorkflowException("You do not have permission to contribute to this work item.", 403);

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var stableEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
            await db.WorkItemEvents.AsNoTracking().AnyAsync(e =>
                e.Id == stableEventId
                && e.WorkItemId == workItemId
                && e.Action == WorkItemEventAction.ContributorSubmitted
                && e.ContributorId == contributorId
                && e.TargetUserId == callerUserId, c);

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                var existingItem = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workItemId, c);
                return new SubmitContributionResult(existingItem?.Revision ?? 0);
            }

            var item = await LockWorkItemAsync(workItemId, c);

            if (item.Revision != command.ExpectedRevision)
                throw new WorkItemWorkflowException("Work item was modified by another operation. Please refresh.", 409);

            if (item.Status == WorkItemStatus.Completed || item.Status == WorkItemStatus.Cancelled)
                throw new WorkItemWorkflowException($"Cannot submit contributions on a {item.Status} work item.", 400);

            var contributor = await db.WorkItemContributors
                .FirstOrDefaultAsync(x => x.Id == contributorId && x.WorkItemId == workItemId && x.RecordStatus == RecordStatus.Active, c);

            if (contributor is null)
                throw new WorkItemWorkflowException("Contributor relationship not found.", 404);

            // STRICT ACTOR RULE: caller must be the actual contributor
            if (contributor.UserId != callerUserId)
                throw new WorkItemWorkflowException("Only the assigned contributor may submit their contribution.", 403);

            if (contributor.Status is not (WorkItemContributorStatus.Active or WorkItemContributorStatus.Returned))
                throw new WorkItemWorkflowException($"Cannot submit contribution from status {contributor.Status}.", 400);

            var now = DateTimeOffset.UtcNow;
            contributor.Status = WorkItemContributorStatus.Submitted;
            contributor.SubmittedAt = now;
            contributor.IsActive = true;
            contributor.UpdatedAt = now;

            item.Revision += 1;
            item.LastActivityAt = now;

            var maxSeq = await db.WorkItemEvents
                .Where(e => e.WorkItemId == workItemId)
                .MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            var submitEvent = new WorkItemEvent
            {
                Id = stableEventId,
                WorkItemId = workItemId,
                SequenceNumber = maxSeq + 1,
                Action = WorkItemEventAction.ContributorSubmitted,
                ActionByUserId = callerUserId,
                ActionByDisplayNameSnapshot = actorDisplayName,
                ActionByDesignationSnapshot = actorDesignation,
                ActionAt = now,
                TargetUserId = contributor.UserId,
                ContributorId = contributorId,
                RemarksSnapshot = command.Note?.Trim()
            };
            db.WorkItemEvents.Add(submitEvent);

            await db.SaveChangesAsync(c);
            return new SubmitContributionResult(item.Revision);
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 9. RETURN CONTRIBUTION FOR CORRECTION
    // ========================================================================
    public async Task<ReturnContributionResult> ReturnContributionAsync(
        Guid workItemId,
        Guid contributorId,
        ReturnContributionCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Remarks))
            throw new WorkItemWorkflowException("Remarks are required when returning a contribution for correction.", 400);

        var canReview = await workItemAuth.CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemReview, callerUserId, ct);
        if (!canReview)
            throw new WorkItemWorkflowException("You do not have permission to review contributions on this work item.", 403);

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var targetContributor = await db.WorkItemContributors.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == contributorId && x.WorkItemId == workItemId && x.RecordStatus == RecordStatus.Active, ct);
        if (targetContributor is null)
            throw new WorkItemWorkflowException("Contributor relationship not found.", 404);
        var targetUserId = targetContributor.UserId;

        var stableEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
            await db.WorkItemEvents.AsNoTracking().AnyAsync(e =>
                e.Id == stableEventId
                && e.WorkItemId == workItemId
                && e.Action == WorkItemEventAction.ContributorReturned
                && e.ContributorId == contributorId
                && e.TargetUserId == targetUserId, c);

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                var existingItem = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workItemId, c);
                return new ReturnContributionResult(existingItem?.Revision ?? 0);
            }

            var item = await LockWorkItemAsync(workItemId, c);

            if (item.Revision != command.ExpectedRevision)
                throw new WorkItemWorkflowException("Work item was modified by another operation. Please refresh.", 409);

            if (item.Status == WorkItemStatus.Completed || item.Status == WorkItemStatus.Cancelled)
                throw new WorkItemWorkflowException($"Cannot return contributions on a {item.Status} work item.", 400);

            var contributor = await db.WorkItemContributors
                .FirstOrDefaultAsync(x => x.Id == contributorId && x.WorkItemId == workItemId && x.RecordStatus == RecordStatus.Active, c);

            if (contributor is null)
                throw new WorkItemWorkflowException("Contributor relationship not found.", 404);

            if (contributor.Status != WorkItemContributorStatus.Submitted)
                throw new WorkItemWorkflowException("Only submitted contributions can be returned for correction.", 400);

            var now = DateTimeOffset.UtcNow;
            contributor.Status = WorkItemContributorStatus.Returned;
            contributor.ReviewedAt = now;
            contributor.ReviewedByUserId = callerUserId;
            contributor.IsActive = true;
            contributor.UpdatedAt = now;

            item.Revision += 1;
            item.LastActivityAt = now;

            var maxSeq = await db.WorkItemEvents
                .Where(e => e.WorkItemId == workItemId)
                .MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            var returnEvent = new WorkItemEvent
            {
                Id = stableEventId,
                WorkItemId = workItemId,
                SequenceNumber = maxSeq + 1,
                Action = WorkItemEventAction.ContributorReturned,
                ActionByUserId = callerUserId,
                ActionByDisplayNameSnapshot = actorDisplayName,
                ActionByDesignationSnapshot = actorDesignation,
                ActionAt = now,
                TargetUserId = contributor.UserId,
                ContributorId = contributorId,
                RemarksSnapshot = command.Remarks.Trim()
            };
            db.WorkItemEvents.Add(returnEvent);

            await db.SaveChangesAsync(c);
            return new ReturnContributionResult(item.Revision);
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 10. ACCEPT CONTRIBUTION
    // ========================================================================
    public async Task<AcceptContributionResult> AcceptContributionAsync(
        Guid workItemId,
        Guid contributorId,
        AcceptContributionCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var canReview = await workItemAuth.CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemReview, callerUserId, ct);
        if (!canReview)
            throw new WorkItemWorkflowException("You do not have permission to review contributions on this work item.", 403);

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var targetContributor = await db.WorkItemContributors.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == contributorId && x.WorkItemId == workItemId && x.RecordStatus == RecordStatus.Active, ct);
        if (targetContributor is null)
            throw new WorkItemWorkflowException("Contributor relationship not found.", 404);
        var targetUserId = targetContributor.UserId;

        var stableEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
            await db.WorkItemEvents.AsNoTracking().AnyAsync(e =>
                e.Id == stableEventId
                && e.WorkItemId == workItemId
                && e.Action == WorkItemEventAction.ContributorAccepted
                && e.ContributorId == contributorId
                && e.TargetUserId == targetUserId, c);

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                var existingItem = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workItemId, c);
                return new AcceptContributionResult(existingItem?.Revision ?? 0);
            }

            var item = await LockWorkItemAsync(workItemId, c);

            if (item.Revision != command.ExpectedRevision)
                throw new WorkItemWorkflowException("Work item was modified by another operation. Please refresh.", 409);

            if (item.Status == WorkItemStatus.Completed || item.Status == WorkItemStatus.Cancelled)
                throw new WorkItemWorkflowException($"Cannot accept contributions on a {item.Status} work item.", 400);

            var contributor = await db.WorkItemContributors
                .FirstOrDefaultAsync(x => x.Id == contributorId && x.WorkItemId == workItemId && x.RecordStatus == RecordStatus.Active, c);

            if (contributor is null)
                throw new WorkItemWorkflowException("Contributor relationship not found.", 404);

            if (contributor.Status != WorkItemContributorStatus.Submitted)
                throw new WorkItemWorkflowException("Only submitted contributions can be accepted.", 400);

            var now = DateTimeOffset.UtcNow;
            contributor.Status = WorkItemContributorStatus.Accepted;
            contributor.ReviewedAt = now;
            contributor.ReviewedByUserId = callerUserId;
            contributor.IsActive = false;
            contributor.UpdatedAt = now;

            item.Revision += 1;
            item.LastActivityAt = now;

            var maxSeq = await db.WorkItemEvents
                .Where(e => e.WorkItemId == workItemId)
                .MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            var acceptEvent = new WorkItemEvent
            {
                Id = stableEventId,
                WorkItemId = workItemId,
                SequenceNumber = maxSeq + 1,
                Action = WorkItemEventAction.ContributorAccepted,
                ActionByUserId = callerUserId,
                ActionByDisplayNameSnapshot = actorDisplayName,
                ActionByDesignationSnapshot = actorDesignation,
                ActionAt = now,
                TargetUserId = contributor.UserId,
                ContributorId = contributorId,
                RemarksSnapshot = command.Remarks?.Trim()
            };
            db.WorkItemEvents.Add(acceptEvent);

            await db.SaveChangesAsync(c);
            return new AcceptContributionResult(item.Revision);
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 11. REMOVE CONTRIBUTOR
    // ========================================================================
    public async Task<RemoveContributorResult> RemoveContributorAsync(
        Guid workItemId,
        Guid contributorId,
        RemoveContributorCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var canAssign = await workItemAuth.CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemAssign, callerUserId, ct);
        if (!canAssign)
            throw new WorkItemWorkflowException("You do not have permission to remove contributors from this work item.", 403);

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var targetContributor = await db.WorkItemContributors.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == contributorId && x.WorkItemId == workItemId && x.RecordStatus == RecordStatus.Active, ct);
        if (targetContributor is null)
            throw new WorkItemWorkflowException("Contributor relationship not found.", 404);
        var targetUserId = targetContributor.UserId;

        var stableEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
            await db.WorkItemEvents.AsNoTracking().AnyAsync(e =>
                e.Id == stableEventId
                && e.WorkItemId == workItemId
                && e.Action == WorkItemEventAction.ContributorRemoved
                && e.ContributorId == contributorId
                && e.TargetUserId == targetUserId, c);

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                var existingItem = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workItemId, c);
                return new RemoveContributorResult(existingItem?.Revision ?? 0);
            }

            var item = await LockWorkItemAsync(workItemId, c);

            if (item.Revision != command.ExpectedRevision)
                throw new WorkItemWorkflowException("Work item was modified by another operation. Please refresh.", 409);


            if (item.Status == WorkItemStatus.Completed || item.Status == WorkItemStatus.Cancelled)
                throw new WorkItemWorkflowException($"Cannot remove contributors from a {item.Status} work item.", 400);

            var contributor = await db.WorkItemContributors
                .FirstOrDefaultAsync(x => x.Id == contributorId && x.WorkItemId == workItemId && x.RecordStatus == RecordStatus.Active, c);

            if (contributor is null)
                throw new WorkItemWorkflowException("Contributor relationship not found.", 404);

            if (contributor.Status is not (WorkItemContributorStatus.Active or WorkItemContributorStatus.Returned or WorkItemContributorStatus.Submitted))
                throw new WorkItemWorkflowException($"Cannot remove contributor in status {contributor.Status}.", 400);

            var now = DateTimeOffset.UtcNow;
            contributor.Status = WorkItemContributorStatus.Removed;
            contributor.IsActive = false;
            contributor.UpdatedAt = now;

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
                Action = WorkItemEventAction.ContributorRemoved,
                ActionByUserId = callerUserId,
                ActionByDisplayNameSnapshot = actorDisplayName,
                ActionByDesignationSnapshot = actorDesignation,
                ActionAt = now,
                TargetUserId = contributor.UserId,
                ContributorId = contributorId,
                RemarksSnapshot = command.Reason?.Trim()
            };
            db.WorkItemEvents.Add(removeEvent);

            await db.SaveChangesAsync(c);
            return new RemoveContributorResult(item.Revision);
        }, verifySucceeded, ct);
    }

    public async Task<ReassignWorkItemResult> ReassignAsync(Guid workItemId, ReassignWorkItemCommand command, Guid callerUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Reason)) throw new WorkItemWorkflowException("Reassignment reason is required.", 400);
        if (!await workItemAuth.CanAccessWorkItemAsync(workItemId, PermissionCodes.WorkItemAssign, callerUserId, ct))
            throw new WorkItemWorkflowException("You do not have permission to reassign this work item.", 403);

        var targetDesk = await db.OfficeDesks.AsNoTracking().FirstOrDefaultAsync(d => d.Id == command.OfficeDeskId && d.IsActive && d.RecordStatus == RecordStatus.Active, ct)
            ?? throw new WorkItemWorkflowException("Target office desk is inactive or does not exist.", 400);
        string? targetUserName = null;
        if (command.AssignedUserId.HasValue)
        {
            var targetUser = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == command.AssignedUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
                ?? throw new WorkItemWorkflowException("Target user is inactive or does not exist.", 400);
            var eligible = await db.UserDeskMemberships.AsNoTracking().AnyAsync(m => m.UserId == targetUser.Id && m.OfficeDeskId == targetDesk.Id && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active, ct);
            if (!eligible) throw new WorkItemWorkflowException("Target user is not an active member of the target desk.", 400);
            targetUserName = targetUser.DisplayName;
        }
        var (actorName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var newAssignmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var reason = command.Reason.Trim();
        Guid sourceAssignmentId = Guid.Empty, sourceDeskId = Guid.Empty; Guid? sourceUserId = null; string sourceDeskName = ""; string? sourceUserName = null;

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();
            if (sourceAssignmentId != Guid.Empty && await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == eventId && e.WorkItemId == workItemId && e.Action == WorkItemEventAction.Reassigned && e.SourceAssignmentId == sourceAssignmentId && e.TargetAssignmentId == newAssignmentId && e.SourceDeskId == sourceDeskId && e.TargetDeskId == command.OfficeDeskId && e.SourceUserId == sourceUserId && e.TargetUserId == command.AssignedUserId && e.SourceDeskNameSnapshot == sourceDeskName && e.SourceUserDisplayNameSnapshot == sourceUserName && e.TargetDeskNameSnapshot == targetDesk.Name && e.TargetUserDisplayNameSnapshot == targetUserName && e.RemarksSnapshot == reason, c))
            {
                var existing = await db.WorkItems.AsNoTracking().FirstAsync(w => w.Id == workItemId, c);
                return new ReassignWorkItemResult(newAssignmentId, existing.Revision);
            }
            var item = await LockWorkItemAsync(workItemId, c);
            // Revalidate every mutable routing target inside this retry attempt.
            targetDesk = await db.OfficeDesks.FirstOrDefaultAsync(d => d.Id == command.OfficeDeskId && d.IsActive && d.RecordStatus == RecordStatus.Active, c)
                ?? throw new WorkItemWorkflowException("Target office desk is inactive or does not exist.", 400);
            if (command.AssignedUserId.HasValue)
            {
                var targetUser = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == command.AssignedUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, c)
                    ?? throw new WorkItemWorkflowException("Target user is inactive or does not exist.", 400);
                if (!await db.UserDeskMemberships.AnyAsync(m => m.UserId == targetUser.Id && m.OfficeDeskId == targetDesk.Id && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active, c))
                    throw new WorkItemWorkflowException("Target user is not an active member of the target desk.", 400);
                targetUserName = targetUser.DisplayName;
            }
            if (item.RecordStatus != RecordStatus.Active) throw new WorkItemWorkflowException("Work item not found.", 404);
            if (item.Status is WorkItemStatus.Completed or WorkItemStatus.Cancelled) throw new WorkItemWorkflowException("Terminal work items cannot be reassigned.", 400);
            if (item.Revision != command.ExpectedRevision) throw new WorkItemWorkflowException("Work item was modified by another operation. Please refresh.", 409);
            var active = await db.WorkItemAssignments.Include(a => a.OfficeDesk).Include(a => a.AssignedUser)
                .Where(a => a.WorkItemId == workItemId && a.IsActive && a.RecordStatus == RecordStatus.Active).ToListAsync(c);
            if (active.Count != 1) throw new WorkItemWorkflowException("Work item assignment invariant violated.", 400);
            var old = active[0];
            if (old.OfficeDeskId == command.OfficeDeskId && old.AssignedUserId == command.AssignedUserId) throw new WorkItemWorkflowException("Reassignment target is unchanged.", 400);
            sourceAssignmentId = old.Id; sourceDeskId = old.OfficeDeskId; sourceUserId = old.AssignedUserId; sourceDeskName = old.OfficeDesk.Name; sourceUserName = old.AssignedUser?.DisplayName;
            var now = DateTimeOffset.UtcNow;
            old.IsActive = false; old.ClosedAt = now;
            var replacement = new WorkItemAssignment { Id = newAssignmentId, WorkItemId = item.Id, OfficeDeskId = targetDesk.Id, AssignedUserId = command.AssignedUserId, AssignedByUserId = callerUserId, AssignedAt = now, IsActive = true };
            db.WorkItemAssignments.Add(replacement);
            var sequence = (await db.WorkItemEvents.Where(e => e.WorkItemId == workItemId).MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0) + 1;
            db.WorkItemEvents.Add(new WorkItemEvent { Id = eventId, WorkItemId = item.Id, SequenceNumber = sequence, Action = WorkItemEventAction.Reassigned, ActionByUserId = callerUserId, ActionByDisplayNameSnapshot = actorName, ActionByDesignationSnapshot = actorDesignation, ActionAt = now, SourceAssignmentId = old.Id, TargetAssignmentId = newAssignmentId, SourceDeskId = old.OfficeDeskId, TargetDeskId = targetDesk.Id, SourceUserId = old.AssignedUserId, TargetUserId = command.AssignedUserId, SourceDeskNameSnapshot = old.OfficeDesk.Name, SourceUserDisplayNameSnapshot = old.AssignedUser?.DisplayName, TargetDeskNameSnapshot = targetDesk.Name, TargetUserDisplayNameSnapshot = targetUserName, RemarksSnapshot = reason });
            item.Revision++; item.LastActivityAt = now;
            await db.SaveChangesAsync(c);
            return new ReassignWorkItemResult(newAssignmentId, item.Revision);
        }, async c => sourceAssignmentId != Guid.Empty && await db.WorkItemEvents.AsNoTracking().AnyAsync(e => e.Id == eventId && e.WorkItemId == workItemId && e.Action == WorkItemEventAction.Reassigned && e.SourceAssignmentId == sourceAssignmentId && e.TargetAssignmentId == newAssignmentId && e.SourceDeskId == sourceDeskId && e.TargetDeskId == command.OfficeDeskId && e.SourceUserId == sourceUserId && e.TargetUserId == command.AssignedUserId && e.SourceDeskNameSnapshot == sourceDeskName && e.SourceUserDisplayNameSnapshot == sourceUserName && e.TargetDeskNameSnapshot == targetDesk.Name && e.TargetUserDisplayNameSnapshot == targetUserName && e.RemarksSnapshot == reason && db.WorkItemAssignments.Any(a => a.Id == newAssignmentId && a.WorkItemId == workItemId && a.OfficeDeskId == command.OfficeDeskId && a.AssignedUserId == command.AssignedUserId && a.IsActive && a.RecordStatus == RecordStatus.Active), c), ct);
    }
}
