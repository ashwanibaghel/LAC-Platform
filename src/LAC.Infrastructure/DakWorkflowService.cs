namespace LAC.Infrastructure;

using System.IO;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

public class DakWorkflowException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed record RegisterDakCommand(
    string DiaryNumber,
    DateOnly ReceivedDate,
    string Subject,
    string SenderName,
    string? SenderDesignation,
    string? SenderDepartment,
    string? SenderAddress,
    string? SenderReferenceNumber,
    DateOnly? SenderLetterDate,
    string InwardMode,
    DakPriority Priority,
    DateOnly? DueDate,
    Guid? CategoryId,
    Guid? WorkstreamId,
    Stream? DocumentStream,
    string? DocumentFileName,
    string? DocumentContentType,
    Guid? RequestId = null
);

public sealed record MoveDakCommand(
    DakMovementAction Action,
    Guid ToDeskId,
    Guid? ToUserId,
    string? Remarks,
    string? Instructions,
    int ExpectedRevision,
    Guid? RequestId = null
);

public sealed record DisposeDakCommand(
    string Remarks,
    int ExpectedRevision
);

public sealed record CancelDakCommand(
    string Reason,
    int ExpectedRevision
);

public sealed partial class DakWorkflowService(
    LacDbContext db,
    IDocumentStorage storage,
    Func<Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy>? strategyFactory = null)
{
    private async Task<TResult> ExecuteWorkflowTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        Func<CancellationToken, Task<bool>> verifySucceeded,
        CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return await operation(ct);
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

    public async Task<Dak> RegisterAsync(RegisterDakCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.DiaryNumber))
            throw new DakWorkflowException("Diary Number is required.");
        if (string.IsNullOrWhiteSpace(cmd.Subject))
            throw new DakWorkflowException("Subject is required.");
        if (string.IsNullOrWhiteSpace(cmd.SenderName))
            throw new DakWorkflowException("Sender Name is required.");

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
            ?? throw new DakWorkflowException("Current user account is inactive or not found.", 403);

        if (cmd.RequestId == Guid.Empty)
            throw new DakWorkflowException("Registration request ID must be a non-empty UUID.");
        var requestHash = cmd.RequestId.HasValue ? await RegistrationHashAsync(cmd, ct) : "";
        var replay = await FindRegistrationReplayAsync(cmd, currentUserId, requestHash, ct);
        if (replay is not null) return replay;
        var diaryKey = DakDiaryNumber.Normalize(cmd.DiaryNumber.Trim());
        if (diaryKey.Length == 0) throw new DakWorkflowException("Diary Number is required.");
        if (await db.Daks.AsNoTracking().AnyAsync(d => d.DiaryNumberKey == diaryKey, ct))
        {
            // Another identical request may commit between the replay lookup and the diary lookup.
            replay = await FindRegistrationReplayAsync(cmd, currentUserId, requestHash, ct);
            if (replay is not null) return replay;
            throw new DakWorkflowException("Diary Number is already registered and permanently reserved.", 409);
        }

        // Validate Category if provided
        if (cmd.CategoryId.HasValue)
        {
            var catExists = await db.DakCategories.AsNoTracking()
                .AnyAsync(c => c.Id == cmd.CategoryId.Value && c.IsActive && c.RecordStatus == RecordStatus.Active, ct);
            if (!catExists)
                throw new DakWorkflowException("The specified Dak category does not exist or is inactive.");
        }

        // Validate Workstream if provided
        if (cmd.WorkstreamId.HasValue)
        {
            var wsExists = await db.Workstreams.AsNoTracking()
                .AnyAsync(w => w.Id == cmd.WorkstreamId.Value && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);
            if (!wsExists)
                throw new DakWorkflowException("The specified Workstream does not exist or is inactive.");
        }

        DocumentStorageWriteResult? fileResult = null;
        string? savedStoragePath = null;

        if (cmd.DocumentStream is not null && !string.IsNullOrWhiteSpace(cmd.DocumentFileName))
        {
            var ext = Path.GetExtension(cmd.DocumentFileName).ToLowerInvariant();
            if (ext != ".pdf" && ext != ".png" && ext != ".jpg" && ext != ".jpeg")
                throw new DakWorkflowException("Only PDF and standard image files are permitted for scanned intake documents.");

            fileResult = await storage.SaveAndHashAsync(cmd.DocumentStream, cmd.DocumentFileName, ct);
            savedStoragePath = fileResult.StoragePath;
        }

        // Stable operation identifiers generated ONCE outside the retry unit
        var dakId = Guid.NewGuid();
        var movementId = Guid.NewGuid();
        Guid? documentId = fileResult is not null ? Guid.NewGuid() : null;

        try
        {
            return await ExecuteWorkflowTransactionAsync(
                async opCt =>
                {
                    db.ChangeTracker.Clear();

                    Document? document = null;
                    if (fileResult is not null && documentId.HasValue)
                    {
                        document = new Document
                        {
                            Id = documentId.Value,
                            OriginalFileName = cmd.DocumentFileName!,
                            StoragePath = fileResult.StoragePath,
                            Sha256Hash = fileResult.Sha256Hash,
                            FileSize = fileResult.FileSize,
                            MimeType = cmd.DocumentContentType ?? "application/pdf",
                            DocumentType = "DakScan",
                            UploadedBy = actionUser.DisplayName
                        };
                        db.Documents.Add(document);
                    }

                    var dak = new Dak
                    {
                        Id = dakId,
                        DiaryNumber = cmd.DiaryNumber.Trim(),
                        DiaryNumberKey = diaryKey,
                        RegistrationRequestId = cmd.RequestId,
                        RegistrationRequestHash = cmd.RequestId.HasValue ? requestHash : null,
                        RegisteredByUserId = currentUserId,
                        ReceivedDate = cmd.ReceivedDate,
                        Subject = cmd.Subject.Trim(),
                        SenderName = cmd.SenderName.Trim(),
                        SenderDesignation = cmd.SenderDesignation?.Trim(),
                        SenderDepartment = cmd.SenderDepartment?.Trim(),
                        SenderAddress = cmd.SenderAddress?.Trim(),
                        SenderReferenceNumber = cmd.SenderReferenceNumber?.Trim(),
                        SenderLetterDate = cmd.SenderLetterDate,
                        InwardMode = string.IsNullOrWhiteSpace(cmd.InwardMode) ? "Physical" : cmd.InwardMode.Trim(),
                        Priority = cmd.Priority,
                        DueDate = cmd.DueDate,
                        CategoryId = cmd.CategoryId,
                        WorkstreamId = cmd.WorkstreamId,
                        Status = DakStatus.Registered,
                        MainDocumentId = documentId,
                        MainDocument = document,
                        Revision = 0
                    };
                    db.Daks.Add(dak);

                    string? wsName = null;
                    if (cmd.WorkstreamId.HasValue)
                    {
                        wsName = await db.Workstreams.AsNoTracking()
                            .Where(w => w.Id == cmd.WorkstreamId.Value)
                            .Select(w => w.Name)
                            .FirstOrDefaultAsync(opCt);
                    }

                    // Sequence 1 movement (Registered) with FromDesk = null, ToDesk = null
                    var movement = new DakMovement
                    {
                        Id = movementId,
                        DakId = dakId,
                        SequenceNumber = 1,
                        Action = DakMovementAction.Registered,
                        FromDeskId = null,
                        FromUserId = null,
                        ToDeskId = null,
                        ToUserId = null,
                        ActionByUserId = currentUserId,
                        ActionByDisplayNameSnapshot = actionUser.DisplayName,
                        ActionAt = DateTimeOffset.UtcNow,
                        Remarks = "Registered in official inward correspondence",
                        WorkstreamIdSnapshot = cmd.WorkstreamId,
                        WorkstreamNameSnapshot = wsName
                    };
                    db.DakMovements.Add(movement);

                    await db.SaveChangesAsync(opCt);
                    return dak;
                },
                async verifyCt =>
                {
                    db.ChangeTracker.Clear();

                    var dakExists = await db.Daks.AsNoTracking()
                        .AnyAsync(d => d.Id == dakId && (!documentId.HasValue || d.MainDocumentId == documentId.Value), verifyCt);
                    if (!dakExists) return false;

                    var movementExists = await db.DakMovements.AsNoTracking()
                        .AnyAsync(m => m.Id == movementId
                                    && m.DakId == dakId
                                    && m.SequenceNumber == 1
                                    && m.Action == DakMovementAction.Registered, verifyCt);
                    if (!movementExists) return false;

                    if (documentId.HasValue)
                    {
                        var docExists = await db.Documents.AsNoTracking()
                            .AnyAsync(doc => doc.Id == documentId.Value, verifyCt);
                        if (!docExists) return false;
                    }

                    return true;
                },
                ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            if (savedStoragePath is not null)
                try { await storage.DeleteAsync(savedStoragePath, CancellationToken.None); } catch { }
            db.ChangeTracker.Clear();
            var committed = await FindRegistrationReplayAsync(cmd, currentUserId, requestHash, ct);
            if (committed is not null) return committed;
            throw new DakWorkflowException("Diary Number is already registered and permanently reserved.", 409);
        }
        catch
        {
            // Filesystem compensation: delete newly uploaded file if DB save failed
            if (savedStoragePath is not null)
            {
                try { await storage.DeleteAsync(savedStoragePath, CancellationToken.None); } catch { /* best effort */ }
            }
            throw;
        }
    }

    public async Task<Dak> MoveAsync(Guid dakId, MoveDakCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (cmd.ToUserId is not { } receiver) throw new DakWorkflowException("A nominated receiving officer is required.");
        await SendAsync(dakId, new SendDakCommand(cmd.Action, cmd.ToDeskId, receiver, DakDestinationKind.Officer,
            false, cmd.Remarks, cmd.Instructions, cmd.ExpectedRevision, cmd.RequestId ?? Guid.NewGuid()), currentUserId, ct);
        return await db.Daks.AsNoTracking().SingleAsync(d => d.Id == dakId, ct);
    }

    public async Task<Dak> DisposeAsync(Guid dakId, DisposeDakCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Remarks))
            throw new DakWorkflowException("Disposal remarks/justification are required.");

        var movementId = Guid.NewGuid();
        var isRelational = db.Database.IsRelational();

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();

                Dak? dak;
                if (isRelational)
                {
                    dak = await db.Daks
                        .FromSqlInterpolated($"SELECT * FROM \"Daks\" WHERE \"Id\" = {dakId} FOR UPDATE")
                        .Include(d => d.Workstream)
                        .Include(d => d.CurrentAssignment)
                            .ThenInclude(a => a!.OfficeDesk)
                        .Include(d => d.CurrentAssignment)
                            .ThenInclude(a => a!.AssignedUser)
                        .FirstOrDefaultAsync(opCt);
                }
                else
                {
                    dak = await db.Daks
                        .Include(d => d.Workstream)
                        .Include(d => d.CurrentAssignment)
                            .ThenInclude(a => a!.OfficeDesk)
                        .Include(d => d.CurrentAssignment)
                            .ThenInclude(a => a!.AssignedUser)
                        .FirstOrDefaultAsync(d => d.Id == dakId, opCt);
                }

                if (dak is null)
                    throw new DakWorkflowException("Dak record not found.", 404);

                if (dak.RecordStatus != RecordStatus.Active)
                    throw new DakWorkflowException("Archived Dak is read-only.", 409);
                if (dak.Revision != cmd.ExpectedRevision)
                    throw new DakWorkflowException("This Dak was modified by another officer. Refresh before proceeding.", 409);

                if (dak.Status is DakStatus.Disposed or DakStatus.Cancelled or DakStatus.Resolved)
                    throw new DakWorkflowException($"Cannot dispose a Dak that is already in status '{dak.Status}'.");

                if (dak.Status == DakStatus.Registered)
                    throw new DakWorkflowException("Cannot dispose unassigned Registered Dak; it must be marked and processed first.");

                await LockCustodyEligibilityAsync(currentUserId, null, opCt);
                if (!await new DakAuthorizationService(db).CanAccessDakAsync(dakId, PermissionCodes.DakDispose, currentUserId, opCt))
                    throw new DakWorkflowException("Disposal permission is not granted in scope.", 403);
                await EnsureNoDeliveryAsync(dak, opCt);
                await EnsureConfirmedHolderAsync(dak, currentUserId, opCt);
                var actionUser = await db.AppUsers.AsNoTracking()
                    .SingleAsync(u => u.Id == currentUserId, opCt);

                var currentAssignment = dak.CurrentAssignment;

                var maxSeq = await db.DakMovements
                    .Where(m => m.DakId == dakId)
                    .MaxAsync(m => (int?)m.SequenceNumber, opCt);
                var nextSeq = (maxSeq ?? 0) + 1;

                var movement = new DakMovement
                {
                    Id = movementId,
                    DakId = dakId,
                    SequenceNumber = nextSeq,
                    Action = DakMovementAction.Disposed,
                    FromDeskId = currentAssignment?.OfficeDeskId,
                    FromUserId = currentAssignment?.AssignedUserId,
                    FromDeskCodeSnapshot = currentAssignment?.OfficeDesk?.Code,
                    FromDeskNameSnapshot = currentAssignment?.OfficeDesk?.Name,
                    FromUserDisplayNameSnapshot = currentAssignment?.AssignedUser?.DisplayName,
                    ToDeskId = null,
                    ToUserId = null,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = DateTimeOffset.UtcNow,
                    Remarks = cmd.Remarks.Trim(),
                    WorkstreamIdSnapshot = dak.WorkstreamId,
                    WorkstreamNameSnapshot = dak.Workstream?.Name
                };
                db.DakMovements.Add(movement);

                if (currentAssignment is not null)
                {
                    currentAssignment.IsActive = false;
                    currentAssignment.ClosedAt = DateTimeOffset.UtcNow;
                }

                dak.Status = DakStatus.Disposed;
                dak.Revision++;

                await db.SaveChangesAsync(opCt);
                return dak;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();

                return await db.DakMovements.AsNoTracking()
                    .AnyAsync(m => m.Id == movementId
                                && m.DakId == dakId
                                && m.Action == DakMovementAction.Disposed, verifyCt);
            },
            ct);
    }

    public async Task<Dak> CancelAsync(Guid dakId, CancelDakCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            throw new DakWorkflowException("Cancellation reason is mandatory.");

        var movementId = Guid.NewGuid();
        var isRelational = db.Database.IsRelational();

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();

                Dak? dak;
                if (isRelational)
                {
                    dak = await db.Daks
                        .FromSqlInterpolated($"SELECT * FROM \"Daks\" WHERE \"Id\" = {dakId} FOR UPDATE")
                        .Include(d => d.Workstream)
                        .Include(d => d.CurrentAssignment)
                            .ThenInclude(a => a!.OfficeDesk)
                        .Include(d => d.CurrentAssignment)
                            .ThenInclude(a => a!.AssignedUser)
                        .FirstOrDefaultAsync(opCt);
                }
                else
                {
                    dak = await db.Daks
                        .Include(d => d.Workstream)
                        .Include(d => d.CurrentAssignment)
                            .ThenInclude(a => a!.OfficeDesk)
                        .Include(d => d.CurrentAssignment)
                            .ThenInclude(a => a!.AssignedUser)
                        .FirstOrDefaultAsync(d => d.Id == dakId, opCt);
                }

                if (dak is null)
                    throw new DakWorkflowException("Dak record not found.", 404);

                if (dak.RecordStatus != RecordStatus.Active)
                    throw new DakWorkflowException("Archived Dak is read-only.", 409);
                if (dak.Revision != cmd.ExpectedRevision)
                    throw new DakWorkflowException("This Dak was modified by another officer. Refresh before proceeding.", 409);

                if (dak.Status is DakStatus.Disposed or DakStatus.Cancelled or DakStatus.Resolved)
                    throw new DakWorkflowException($"Cannot cancel a Dak that is already in terminal status '{dak.Status}'.");

                await EnsureNoDeliveryAsync(dak, opCt);
                var actionUser = await db.AppUsers.AsNoTracking()
                    .SingleAsync(u => u.Id == currentUserId, opCt);

                var currentAssignment = dak.CurrentAssignment;

                var maxSeq = await db.DakMovements
                    .Where(m => m.DakId == dakId)
                    .MaxAsync(m => (int?)m.SequenceNumber, opCt);
                var nextSeq = (maxSeq ?? 0) + 1;

                var movement = new DakMovement
                {
                    Id = movementId,
                    DakId = dakId,
                    SequenceNumber = nextSeq,
                    Action = DakMovementAction.Cancelled,
                    FromDeskId = currentAssignment?.OfficeDeskId,
                    FromUserId = currentAssignment?.AssignedUserId,
                    FromDeskCodeSnapshot = currentAssignment?.OfficeDesk?.Code,
                    FromDeskNameSnapshot = currentAssignment?.OfficeDesk?.Name,
                    FromUserDisplayNameSnapshot = currentAssignment?.AssignedUser?.DisplayName,
                    ToDeskId = null,
                    ToUserId = null,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = DateTimeOffset.UtcNow,
                    Remarks = cmd.Reason.Trim(),
                    WorkstreamIdSnapshot = dak.WorkstreamId,
                    WorkstreamNameSnapshot = dak.Workstream?.Name
                };
                db.DakMovements.Add(movement);

                if (currentAssignment is not null)
                {
                    currentAssignment.IsActive = false;
                    currentAssignment.ClosedAt = DateTimeOffset.UtcNow;
                }

                dak.Status = DakStatus.Cancelled;
                dak.Revision++;

                await db.SaveChangesAsync(opCt);
                return dak;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();

                return await db.DakMovements.AsNoTracking()
                    .AnyAsync(m => m.Id == movementId
                                && m.DakId == dakId
                                && m.Action == DakMovementAction.Cancelled, verifyCt);
            },
            ct);
    }
}
