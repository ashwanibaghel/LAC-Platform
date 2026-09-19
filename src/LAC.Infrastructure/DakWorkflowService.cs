namespace LAC.Infrastructure;

using System.IO;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

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
    string? DocumentContentType
);

public sealed record MoveDakCommand(
    DakMovementAction Action,
    Guid ToDeskId,
    Guid? ToUserId,
    string? Remarks,
    string? Instructions,
    int ExpectedRevision
);

public sealed record DisposeDakCommand(
    string Remarks,
    int ExpectedRevision
);

public sealed record CancelDakCommand(
    string Reason,
    int ExpectedRevision
);

public sealed class DakWorkflowService(LacDbContext db, IDocumentStorage storage)
{
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

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();

                var isRelational = db.Database.IsRelational();
                await using var tx = isRelational ? await db.Database.BeginTransactionAsync(ct) : null;

                Document? document = null;
                if (fileResult is not null)
                {
                    document = new Document
                    {
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
                    DiaryNumber = cmd.DiaryNumber.Trim(),
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
                    MainDocument = document,
                    Revision = 0
                };
                db.Daks.Add(dak);

                // Sequence 1 movement (Registered) with FromDesk = null, ToDesk = null
                var movement = new DakMovement
                {
                    Dak = dak,
                    SequenceNumber = 1,
                    Action = DakMovementAction.Registered,
                    FromDeskId = null,
                    FromUserId = null,
                    ToDeskId = null,
                    ToUserId = null,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = DateTimeOffset.UtcNow,
                    Remarks = "Registered in official inward correspondence"
                };
                db.DakMovements.Add(movement);

                await db.SaveChangesAsync(ct);

                if (tx is not null)
                    await tx.CommitAsync(ct);

                return dak;
            });
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
        // 1. Strict Server-Side Action Whitelist
        if (cmd.Action != DakMovementAction.Marked &&
            cmd.Action != DakMovementAction.Forwarded &&
            cmd.Action != DakMovementAction.Returned)
        {
            throw new DakWorkflowException($"Action '{cmd.Action}' is not permitted via Move. Only Marked, Forwarded, and Returned are allowed.");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            var isRelational = db.Database.IsRelational();
            await using var tx = isRelational ? await db.Database.BeginTransactionAsync(ct) : null;

            // Provider-aware locking seam
            Dak? dak;
            if (isRelational)
            {
                dak = await db.Daks
                    .FromSqlInterpolated($"SELECT * FROM \"Daks\" WHERE \"Id\" = {dakId} FOR UPDATE")
                    .Include(d => d.CurrentAssignment)
                        .ThenInclude(a => a!.OfficeDesk)
                    .Include(d => d.CurrentAssignment)
                        .ThenInclude(a => a!.AssignedUser)
                    .FirstOrDefaultAsync(ct);
            }
            else
            {
                dak = await db.Daks
                    .Include(d => d.CurrentAssignment)
                        .ThenInclude(a => a!.OfficeDesk)
                    .Include(d => d.CurrentAssignment)
                        .ThenInclude(a => a!.AssignedUser)
                    .FirstOrDefaultAsync(d => d.Id == dakId, ct);
            }

            if (dak is null)
                throw new DakWorkflowException("Dak record not found.", 404);

            // Concurrency token validation
            if (dak.Revision != cmd.ExpectedRevision)
                throw new DakWorkflowException("This Dak was modified by another officer. Refresh before proceeding.", 409);

            // 2. Lifecycle Transition Invariants
            if (dak.Status == DakStatus.Disposed || dak.Status == DakStatus.Cancelled)
                throw new DakWorkflowException($"Cannot move a Dak that is in terminal status '{dak.Status}'.");

            if (dak.Status == DakStatus.Registered)
            {
                if (cmd.Action != DakMovementAction.Marked)
                    throw new DakWorkflowException($"Dak in Registered status must be Marked first. Action '{cmd.Action}' is not permitted.");
            }
            else if (dak.Status == DakStatus.InProcess)
            {
                if (cmd.Action == DakMovementAction.Marked)
                    throw new DakWorkflowException("Dak is already actively assigned and InProcess; subsequent movements must be Forwarded or Returned.");
            }

            // 3. Desk & User Eligibility Validations using live DB state
            var toDesk = await db.OfficeDesks.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == cmd.ToDeskId && d.IsActive && d.RecordStatus == RecordStatus.Active, ct)
                ?? throw new DakWorkflowException("Target Office Desk does not exist or is inactive.");

            AppUser? toUser = null;
            if (cmd.ToUserId.HasValue)
            {
                toUser = await db.AppUsers.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == cmd.ToUserId.Value && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
                    ?? throw new DakWorkflowException("Target user account does not exist or is inactive.");

                var isEligibleMember = await db.UserDeskMemberships.AsNoTracking()
                    .AnyAsync(m => m.UserId == cmd.ToUserId.Value
                                && m.OfficeDeskId == cmd.ToDeskId
                                && m.IsActive
                                && m.RemovedAt == null
                                && m.OfficeDesk.IsActive
                                && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);

                if (!isEligibleMember)
                    throw new DakWorkflowException("Target user is not an active member of the designated Office Desk.");
            }

            var actionUser = await db.AppUsers.AsNoTracking()
                .SingleAsync(u => u.Id == currentUserId, ct);

            // 4. Safe Sequence Calculation: (maxSequence ?? 0) + 1
            var maxSeq = await db.DakMovements
                .Where(m => m.DakId == dakId)
                .MaxAsync(m => (int?)m.SequenceNumber, ct);
            var nextSeq = (maxSeq ?? 0) + 1;

            var currentAssignment = dak.CurrentAssignment;

            // 5. Append Movement with Event-Time Identity Snapshots
            var movement = new DakMovement
            {
                DakId = dakId,
                SequenceNumber = nextSeq,
                Action = cmd.Action,
                FromDeskId = currentAssignment?.OfficeDeskId,
                FromUserId = currentAssignment?.AssignedUserId,
                FromDeskCodeSnapshot = currentAssignment?.OfficeDesk?.Code,
                FromDeskNameSnapshot = currentAssignment?.OfficeDesk?.Name,
                FromUserDisplayNameSnapshot = currentAssignment?.AssignedUser?.DisplayName,
                ToDeskId = cmd.ToDeskId,
                ToUserId = cmd.ToUserId,
                ToDeskCodeSnapshot = toDesk.Code,
                ToDeskNameSnapshot = toDesk.Name,
                ToUserDisplayNameSnapshot = toUser?.DisplayName,
                ActionByUserId = currentUserId,
                ActionByDisplayNameSnapshot = actionUser.DisplayName,
                ActionAt = DateTimeOffset.UtcNow,
                Remarks = cmd.Remarks?.Trim(),
                InstructionsSnapshot = cmd.Instructions?.Trim()
            };
            db.DakMovements.Add(movement);

            // 6. Update Single Current Assignment Projection Row
            if (currentAssignment is null)
            {
                currentAssignment = new DakAssignment
                {
                    DakId = dakId,
                    OfficeDeskId = cmd.ToDeskId,
                    AssignedUserId = cmd.ToUserId,
                    AssignedByUserId = currentUserId,
                    AssignedAt = DateTimeOffset.UtcNow,
                    Instructions = cmd.Instructions?.Trim(),
                    IsActive = true
                };
                db.DakAssignments.Add(currentAssignment);
            }
            else
            {
                currentAssignment.OfficeDeskId = cmd.ToDeskId;
                currentAssignment.AssignedUserId = cmd.ToUserId;
                currentAssignment.AssignedByUserId = currentUserId;
                currentAssignment.AssignedAt = DateTimeOffset.UtcNow;
                currentAssignment.Instructions = cmd.Instructions?.Trim();
                currentAssignment.IsActive = true;
                currentAssignment.ClosedAt = null;
            }

            // 7. Transition Status to InProcess and Increment Revision
            dak.Status = DakStatus.InProcess;
            dak.Revision++;

            await db.SaveChangesAsync(ct);

            if (tx is not null)
                await tx.CommitAsync(ct);

            return dak;
        });
    }

    public async Task<Dak> DisposeAsync(Guid dakId, DisposeDakCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Remarks))
            throw new DakWorkflowException("Disposal remarks/justification are required.");

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            var isRelational = db.Database.IsRelational();
            await using var tx = isRelational ? await db.Database.BeginTransactionAsync(ct) : null;

            Dak? dak;
            if (isRelational)
            {
                dak = await db.Daks
                    .FromSqlInterpolated($"SELECT * FROM \"Daks\" WHERE \"Id\" = {dakId} FOR UPDATE")
                    .Include(d => d.CurrentAssignment)
                        .ThenInclude(a => a!.OfficeDesk)
                    .Include(d => d.CurrentAssignment)
                        .ThenInclude(a => a!.AssignedUser)
                    .FirstOrDefaultAsync(ct);
            }
            else
            {
                dak = await db.Daks
                    .Include(d => d.CurrentAssignment)
                        .ThenInclude(a => a!.OfficeDesk)
                    .Include(d => d.CurrentAssignment)
                        .ThenInclude(a => a!.AssignedUser)
                    .FirstOrDefaultAsync(d => d.Id == dakId, ct);
            }

            if (dak is null)
                throw new DakWorkflowException("Dak record not found.", 404);

            if (dak.Revision != cmd.ExpectedRevision)
                throw new DakWorkflowException("This Dak was modified by another officer. Refresh before proceeding.", 409);

            if (dak.Status == DakStatus.Disposed || dak.Status == DakStatus.Cancelled)
                throw new DakWorkflowException($"Cannot dispose a Dak that is already in status '{dak.Status}'.");

            if (dak.Status == DakStatus.Registered)
                throw new DakWorkflowException("Cannot dispose unassigned Registered Dak; it must be marked and processed first.");

            var actionUser = await db.AppUsers.AsNoTracking()
                .SingleAsync(u => u.Id == currentUserId, ct);

            var currentAssignment = dak.CurrentAssignment;

            var maxSeq = await db.DakMovements
                .Where(m => m.DakId == dakId)
                .MaxAsync(m => (int?)m.SequenceNumber, ct);
            var nextSeq = (maxSeq ?? 0) + 1;

            var movement = new DakMovement
            {
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
                Remarks = cmd.Remarks.Trim()
            };
            db.DakMovements.Add(movement);

            if (currentAssignment is not null)
            {
                currentAssignment.IsActive = false;
                currentAssignment.ClosedAt = DateTimeOffset.UtcNow;
            }

            dak.Status = DakStatus.Disposed;
            dak.Revision++;

            await db.SaveChangesAsync(ct);

            if (tx is not null)
                await tx.CommitAsync(ct);

            return dak;
        });
    }

    public async Task<Dak> CancelAsync(Guid dakId, CancelDakCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            throw new DakWorkflowException("Cancellation reason is mandatory.");

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            var isRelational = db.Database.IsRelational();
            await using var tx = isRelational ? await db.Database.BeginTransactionAsync(ct) : null;

            Dak? dak;
            if (isRelational)
            {
                dak = await db.Daks
                    .FromSqlInterpolated($"SELECT * FROM \"Daks\" WHERE \"Id\" = {dakId} FOR UPDATE")
                    .Include(d => d.CurrentAssignment)
                        .ThenInclude(a => a!.OfficeDesk)
                    .Include(d => d.CurrentAssignment)
                        .ThenInclude(a => a!.AssignedUser)
                    .FirstOrDefaultAsync(ct);
            }
            else
            {
                dak = await db.Daks
                    .Include(d => d.CurrentAssignment)
                        .ThenInclude(a => a!.OfficeDesk)
                    .Include(d => d.CurrentAssignment)
                        .ThenInclude(a => a!.AssignedUser)
                    .FirstOrDefaultAsync(d => d.Id == dakId, ct);
            }

            if (dak is null)
                throw new DakWorkflowException("Dak record not found.", 404);

            if (dak.Revision != cmd.ExpectedRevision)
                throw new DakWorkflowException("This Dak was modified by another officer. Refresh before proceeding.", 409);

            if (dak.Status == DakStatus.Disposed || dak.Status == DakStatus.Cancelled)
                throw new DakWorkflowException($"Cannot cancel a Dak that is already in terminal status '{dak.Status}'.");

            var actionUser = await db.AppUsers.AsNoTracking()
                .SingleAsync(u => u.Id == currentUserId, ct);

            var currentAssignment = dak.CurrentAssignment;

            var maxSeq = await db.DakMovements
                .Where(m => m.DakId == dakId)
                .MaxAsync(m => (int?)m.SequenceNumber, ct);
            var nextSeq = (maxSeq ?? 0) + 1;

            var movement = new DakMovement
            {
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
                Remarks = cmd.Reason.Trim()
            };
            db.DakMovements.Add(movement);

            if (currentAssignment is not null)
            {
                currentAssignment.IsActive = false;
                currentAssignment.ClosedAt = DateTimeOffset.UtcNow;
            }

            dak.Status = DakStatus.Cancelled;
            dak.Revision++;

            await db.SaveChangesAsync(ct);

            if (tx is not null)
                await tx.CommitAsync(ct);

            return dak;
        });
    }
}
