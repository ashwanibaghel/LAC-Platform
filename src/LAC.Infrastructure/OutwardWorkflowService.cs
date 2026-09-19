namespace LAC.Infrastructure;

using System.IO;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public class OutwardWorkflowException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed record RegisterOutwardCommand(
    string OutwardNumber,
    DateOnly OutwardDate,
    string Subject,
    string RecipientName,
    string? RecipientDesignation,
    string? RecipientDepartment,
    string? RecipientAddress,
    string? RecipientEmail,
    string? RecipientPhone,
    Guid IssuingDeskId,
    Guid? WorkstreamId,
    string? OfficeReferenceNumber,
    string? Remarks,
    Guid? PrimaryDakId,
    Guid? MatterId,
    Guid? ExistingDocumentId,
    Stream? DocumentStream,
    string? DocumentFileName,
    string? DocumentContentType
);

public sealed record UpdateOutwardMetadataCommand(
    string Subject,
    string RecipientName,
    string? RecipientDesignation,
    string? RecipientDepartment,
    string? RecipientAddress,
    string? RecipientEmail,
    string? RecipientPhone,
    Guid IssuingDeskId,
    Guid? WorkstreamId,
    string? OfficeReferenceNumber,
    string? Remarks,
    int ExpectedRevision,
    Guid? MatterId = null
);

public sealed record ChangeMainDocumentCommand(
    Guid? ExistingDocumentId,
    Stream? DocumentStream,
    string? DocumentFileName,
    string? DocumentContentType,
    int ExpectedRevision
);

public sealed record AddAttachmentCommand(
    string Title,
    string AttachmentType,
    int SequenceOrder,
    Guid? ExistingDocumentId,
    Stream? DocumentStream,
    string? DocumentFileName,
    string? DocumentContentType,
    int ExpectedRevision
);

public sealed record RemoveAttachmentCommand(
    int ExpectedRevision
);

public sealed record AddDakLinkCommand(
    Guid DakId,
    bool IsPrimary,
    string RelationshipType,
    int ExpectedRevision
);

public sealed record RemoveDakLinkCommand(
    int ExpectedRevision
);

public sealed record DispatchOutwardCommand(
    DateOnly DispatchDate,
    string DispatchMode,
    string? DispatchReferenceNumber,
    int ExpectedRevision
);

public sealed record CancelOutwardCommand(
    string Reason,
    int ExpectedRevision
);

public sealed class OutwardWorkflowService(
    LacDbContext db,
    IDocumentStorage storage,
    IDakAuthorizationService dakAuth,
    IAccessControlService accessControl,
    Func<Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy>? strategyFactory = null)
{
    private static readonly HashSet<string> AllowedModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SpeedPost", "RegisteredPost", "ByHand", "SpecialMessenger", "Courier", "Email", "Other"
    };

    private static readonly HashSet<string> AllowedRelationshipTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PrimaryReply", "RelatedPetition", "Reference"
    };

    private static string CanonicalizeMode(string mode)
    {
        var match = AllowedModes.FirstOrDefault(m => string.Equals(m, mode, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new OutwardWorkflowException($"Invalid dispatch mode '{mode}'. Allowed modes: SpeedPost, RegisteredPost, ByHand, SpecialMessenger, Courier, Email, Other.");
        return match;
    }

    private static string CanonicalizeRelationshipType(string type)
    {
        var match = AllowedRelationshipTypes.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new OutwardWorkflowException($"Invalid relationship type '{type}'. Allowed types: PrimaryReply, RelatedPetition, Reference.");
        return match;
    }

    private static void ValidateMagicBytes(Stream stream, string ext)
    {
        var buffer = new byte[8];
        var read = stream.Read(buffer, 0, buffer.Length);
        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);

        if (ext == ".pdf")
        {
            // PDF header starts with %PDF- (0x25, 0x50, 0x44, 0x46, 0x2D)
            if (read < 5 || buffer[0] != 0x25 || buffer[1] != 0x50 || buffer[2] != 0x44 || buffer[3] != 0x46 || buffer[4] != 0x2D)
                throw new OutwardWorkflowException("File content does not match standard PDF file signature.");
        }
        else if (ext == ".png")
        {
            // PNG header starts with 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
            if (read < 8 || buffer[0] != 0x89 || buffer[1] != 0x50 || buffer[2] != 0x4E || buffer[3] != 0x47)
                throw new OutwardWorkflowException("File content does not match standard PNG file signature.");
        }
        else if (ext == ".jpg" || ext == ".jpeg")
        {
            // JPEG header starts with 0xFF, 0xD8, 0xFF
            if (read < 3 || buffer[0] != 0xFF || buffer[1] != 0xD8 || buffer[2] != 0xFF)
                throw new OutwardWorkflowException("File content does not match standard JPEG file signature.");
        }
    }

    private async Task<DocumentStorageWriteResult> SaveFileAsync(Stream stream, string fileName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new OutwardWorkflowException("Attached file must have a valid file name.", 400);

        if (stream.Length == 0)
            throw new OutwardWorkflowException("Cannot upload an empty file (0 bytes).", 400);

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext != ".pdf" && ext != ".png" && ext != ".jpg" && ext != ".jpeg")
            throw new OutwardWorkflowException("Only PDF and standard image files (.png, .jpg, .jpeg) are permitted.", 400);

        ValidateMagicBytes(stream, ext);
        return await storage.SaveAndHashAsync(stream, fileName, ct);
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

    private async Task<Outward> LockOutwardAsync(Guid outwardId, CancellationToken ct)
    {
        Outward? outward;
        if (db.Database.IsRelational())
        {
            outward = await db.Outwards
                .FromSqlInterpolated($"SELECT * FROM \"Outwards\" WHERE \"Id\" = {outwardId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
        }
        else
        {
            outward = await db.Outwards.FirstOrDefaultAsync(o => o.Id == outwardId, ct);
        }

        return outward ?? throw new OutwardWorkflowException("Outward record not found.", 404);
    }

    // ========================================================================
    // 1. REGISTER OUTWARD
    // ========================================================================
    public async Task<Outward> RegisterAsync(RegisterOutwardCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.OutwardNumber))
            throw new OutwardWorkflowException("Outward Number is mandatory.");
        if (string.IsNullOrWhiteSpace(cmd.Subject))
            throw new OutwardWorkflowException("Subject is mandatory.");
        if (string.IsNullOrWhiteSpace(cmd.RecipientName))
            throw new OutwardWorkflowException("Recipient Name is mandatory.");

        var normalizedNumber = cmd.OutwardNumber.Trim().ToUpperInvariant();

        // Application-level precheck for duplicate Outward Number
        var numberExists = await db.Outwards.AsNoTracking()
            .AnyAsync(o => o.NormalizedOutwardNumber == normalizedNumber, ct);
        if (numberExists)
            throw new OutwardWorkflowException("An outward record with this number already exists.", 409);

        // Verify active user
        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
            ?? throw new OutwardWorkflowException("Current user account is inactive or not found.", 403);

        // Verify Issuing Desk
        var isDeskMember = await db.UserDeskMemberships.AsNoTracking()
            .AnyAsync(m => m.UserId == currentUserId
                        && m.OfficeDeskId == cmd.IssuingDeskId
                        && m.IsActive
                        && m.RemovedAt == null
                        && m.RecordStatus == RecordStatus.Active
                        && m.OfficeDesk.IsActive
                        && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);
        if (!isDeskMember)
            throw new OutwardWorkflowException("Selected Issuing Desk is not a valid active desk membership of the caller.", 403);

        // Verify Workstream if supplied
        if (cmd.WorkstreamId.HasValue)
        {
            var wsExists = await db.Workstreams.AsNoTracking()
                .AnyAsync(w => w.Id == cmd.WorkstreamId.Value && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);
            if (!wsExists)
                throw new OutwardWorkflowException("Selected Workstream does not exist or is inactive.");
        }

        // Mutual exclusivity: new upload vs existing document
        if (cmd.DocumentStream is not null && cmd.ExistingDocumentId.HasValue)
            throw new OutwardWorkflowException("Cannot specify both a new uploaded file and an existing document ID.", 400);

        // Verify existing document if supplied
        if (cmd.ExistingDocumentId.HasValue)
        {
            var docExists = await db.Documents.AsNoTracking()
                .AnyAsync(d => d.Id == cmd.ExistingDocumentId.Value && d.RecordStatus == RecordStatus.Active, ct);
            if (!docExists)
                throw new OutwardWorkflowException("Existing document not found.", 404);
        }

        // Verify Primary Dak access if supplied
        if (cmd.PrimaryDakId.HasValue)
        {
            var canAccess = await dakAuth.CanAccessDakAsync(cmd.PrimaryDakId.Value, PermissionCodes.DakView, currentUserId, ct);
            if (!canAccess)
                throw new OutwardWorkflowException("You do not have permission to view the specified inward Dak.", 403);
        }

        // Verify Matter access if supplied
        if (cmd.MatterId.HasValue)
        {
            var matterExists = await db.Matters.AsNoTracking()
                .AnyAsync(m => m.Id == cmd.MatterId.Value && m.RecordStatus == RecordStatus.Active, ct);
            if (!matterExists)
                throw new OutwardWorkflowException("Specified Matter does not exist or is inactive.", 400);

            var canViewMatter = await accessControl.CanAsync(PermissionCodes.MatterView, null, ct);
            if (!canViewMatter)
                throw new OutwardWorkflowException("You do not have permission to view the specified Matter.", 403);
        }

        DocumentStorageWriteResult? fileResult = null;
        string? savedStoragePath = null;
        if (cmd.DocumentStream is not null)
        {
            if (string.IsNullOrWhiteSpace(cmd.DocumentFileName))
                throw new OutwardWorkflowException("Attached file must have a valid file name.", 400);

            fileResult = await SaveFileAsync(cmd.DocumentStream, cmd.DocumentFileName, ct);
            savedStoragePath = fileResult.StoragePath;
        }

        // --------------------------------------------------------------------
        // Stable operation IDs generated ONCE outside the retry unit
        // --------------------------------------------------------------------
        var outwardId = Guid.NewGuid();
        var registeredEventId = Guid.NewGuid();
        Guid? initialDocumentId = fileResult is not null ? Guid.NewGuid() : cmd.ExistingDocumentId;
        Guid? primaryDakLinkId = cmd.PrimaryDakId.HasValue ? Guid.NewGuid() : null;

        try
        {
            return await ExecuteWorkflowTransactionAsync(
                async opCt =>
                {
                    db.ChangeTracker.Clear();

                    Document? document = null;
                    if (fileResult is not null && initialDocumentId.HasValue)
                    {
                        var ext = Path.GetExtension(cmd.DocumentFileName!).ToLowerInvariant();
                        var mime = ext == ".pdf" ? "application/pdf" : ext == ".png" ? "image/png" : "image/jpeg";

                        document = new Document
                        {
                            Id = initialDocumentId.Value,
                            OriginalFileName = cmd.DocumentFileName!,
                            StoragePath = fileResult.StoragePath,
                            Sha256Hash = fileResult.Sha256Hash,
                            FileSize = fileResult.FileSize,
                            MimeType = cmd.DocumentContentType ?? mime,
                            DocumentType = "OutwardLetter",
                            UploadedBy = actionUser.DisplayName
                        };
                        db.Documents.Add(document);
                    }

                    var outward = new Outward
                    {
                        Id = outwardId,
                        OutwardNumber = cmd.OutwardNumber.Trim(),
                        NormalizedOutwardNumber = normalizedNumber,
                        OutwardDate = cmd.OutwardDate,
                        Subject = cmd.Subject.Trim(),
                        RecipientName = cmd.RecipientName.Trim(),
                        RecipientDesignation = cmd.RecipientDesignation?.Trim(),
                        RecipientDepartment = cmd.RecipientDepartment?.Trim(),
                        RecipientAddress = cmd.RecipientAddress?.Trim(),
                        RecipientEmail = cmd.RecipientEmail?.Trim(),
                        RecipientPhone = cmd.RecipientPhone?.Trim(),
                        IssuingDeskId = cmd.IssuingDeskId,
                        WorkstreamId = cmd.WorkstreamId,
                        OfficeReferenceNumber = cmd.OfficeReferenceNumber?.Trim(),
                        Remarks = cmd.Remarks?.Trim(),
                        Status = OutwardStatus.Registered,
                        Revision = 0,
                        MainDocumentId = initialDocumentId,
                        MatterId = cmd.MatterId
                    };
                    db.Outwards.Add(outward);

                    // Sequence 1 Registered Event with initial DocumentId snapshot
                    var regEvent = new OutwardEvent
                    {
                        Id = registeredEventId,
                        OutwardId = outwardId,
                        SequenceNumber = 1,
                        Action = OutwardEventAction.Registered,
                        ActionByUserId = currentUserId,
                        ActionByDisplayNameSnapshot = actionUser.DisplayName,
                        ActionAt = DateTimeOffset.UtcNow,
                        DocumentId = initialDocumentId
                    };
                    db.OutwardEvents.Add(regEvent);

                    if (cmd.PrimaryDakId.HasValue && primaryDakLinkId.HasValue)
                    {
                        var link = new OutwardDakLink
                        {
                            Id = primaryDakLinkId.Value,
                            OutwardId = outwardId,
                            DakId = cmd.PrimaryDakId.Value,
                            IsPrimary = true,
                            RelationshipType = "PrimaryReply"
                        };
                        db.OutwardDakLinks.Add(link);
                    }

                    try
                    {
                        await db.SaveChangesAsync(opCt);
                    }
                    catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Outwards_NormalizedOutwardNumber") == true)
                    {
                        throw new OutwardWorkflowException("An outward record with this number already exists.", 409);
                    }

                    return outward;
                },
                async verifyCt =>
                {
                    db.ChangeTracker.Clear();

                    var outwardExists = await db.Outwards.AsNoTracking()
                        .AnyAsync(o => o.Id == outwardId, verifyCt);
                    if (!outwardExists) return false;

                    var eventExists = await db.OutwardEvents.AsNoTracking()
                        .AnyAsync(e => e.Id == registeredEventId
                                    && e.OutwardId == outwardId
                                    && e.SequenceNumber == 1
                                    && e.Action == OutwardEventAction.Registered
                                    && e.DocumentId == initialDocumentId, verifyCt);
                    if (!eventExists) return false;

                    if (fileResult is not null && initialDocumentId.HasValue)
                    {
                        var docExists = await db.Documents.AsNoTracking()
                            .AnyAsync(d => d.Id == initialDocumentId.Value, verifyCt);
                        if (!docExists) return false;
                    }

                    return true;
                },
                ct);
        }
        catch
        {
            if (savedStoragePath is not null)
            {
                try { await storage.DeleteAsync(savedStoragePath, CancellationToken.None); } catch { /* best effort */ }
            }
            throw;
        }
    }

    // ========================================================================
    // 2. UPDATE METADATA
    // ========================================================================
    public async Task<Outward> UpdateMetadataAsync(Guid outwardId, UpdateOutwardMetadataCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Subject))
            throw new OutwardWorkflowException("Subject is mandatory.");
        if (string.IsNullOrWhiteSpace(cmd.RecipientName))
            throw new OutwardWorkflowException("Recipient Name is mandatory.");

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
            ?? throw new OutwardWorkflowException("Current user account is inactive or not found.", 403);

        // Verify Issuing Desk membership
        var isDeskMember = await db.UserDeskMemberships.AsNoTracking()
            .AnyAsync(m => m.UserId == currentUserId
                        && m.OfficeDeskId == cmd.IssuingDeskId
                        && m.IsActive
                        && m.RemovedAt == null
                        && m.RecordStatus == RecordStatus.Active
                        && m.OfficeDesk.IsActive
                        && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct);
        if (!isDeskMember)
            throw new OutwardWorkflowException("Selected Issuing Desk is not a valid active desk membership of the caller.", 403);

        if (cmd.WorkstreamId.HasValue)
        {
            var wsExists = await db.Workstreams.AsNoTracking()
                .AnyAsync(w => w.Id == cmd.WorkstreamId.Value && w.IsActive && w.RecordStatus == RecordStatus.Active, ct);
            if (!wsExists)
                throw new OutwardWorkflowException("Selected Workstream does not exist or is inactive.");
        }

        if (cmd.MatterId.HasValue)
        {
            var matterExists = await db.Matters.AsNoTracking()
                .AnyAsync(m => m.Id == cmd.MatterId.Value && m.RecordStatus == RecordStatus.Active, ct);
            if (!matterExists)
                throw new OutwardWorkflowException("Specified Matter does not exist or is inactive.", 400);

            var canViewMatter = await accessControl.CanAsync(PermissionCodes.MatterView, null, ct);
            if (!canViewMatter)
                throw new OutwardWorkflowException("You do not have permission to view the specified Matter.", 403);
        }

        var eventId = Guid.NewGuid();

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();
                var outward = await LockOutwardAsync(outwardId, opCt);

                if (outward.Revision != cmd.ExpectedRevision)
                    throw new OutwardWorkflowException("This outward record was modified by another officer. Refresh before proceeding.", 409);

                if (outward.Status != OutwardStatus.Registered)
                    throw new OutwardWorkflowException($"Cannot edit an outward record in terminal status '{outward.Status}'.", 409);

                outward.Subject = cmd.Subject.Trim();
                outward.RecipientName = cmd.RecipientName.Trim();
                outward.RecipientDesignation = cmd.RecipientDesignation?.Trim();
                outward.RecipientDepartment = cmd.RecipientDepartment?.Trim();
                outward.RecipientAddress = cmd.RecipientAddress?.Trim();
                outward.RecipientEmail = cmd.RecipientEmail?.Trim();
                outward.RecipientPhone = cmd.RecipientPhone?.Trim();
                outward.IssuingDeskId = cmd.IssuingDeskId;
                outward.WorkstreamId = cmd.WorkstreamId;
                outward.OfficeReferenceNumber = cmd.OfficeReferenceNumber?.Trim();
                outward.Remarks = cmd.Remarks?.Trim();
                outward.MatterId = cmd.MatterId;
                outward.Revision++;

                var maxSeq = await db.OutwardEvents
                    .Where(e => e.OutwardId == outwardId)
                    .MaxAsync(e => (int?)e.SequenceNumber, opCt);

                var evt = new OutwardEvent
                {
                    Id = eventId,
                    OutwardId = outwardId,
                    SequenceNumber = (maxSeq ?? 0) + 1,
                    Action = OutwardEventAction.MetadataUpdated,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = DateTimeOffset.UtcNow
                };
                db.OutwardEvents.Add(evt);

                await db.SaveChangesAsync(opCt);
                return outward;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();
                return await db.OutwardEvents.AsNoTracking()
                    .AnyAsync(e => e.Id == eventId
                                && e.OutwardId == outwardId
                                && e.Action == OutwardEventAction.MetadataUpdated, verifyCt);
            },
            ct);
    }

    // ========================================================================
    // 3. CHANGE MAIN DOCUMENT
    // ========================================================================
    public async Task<Outward> ChangeMainDocumentAsync(Guid outwardId, ChangeMainDocumentCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (cmd.DocumentStream is not null && cmd.ExistingDocumentId.HasValue)
            throw new OutwardWorkflowException("Cannot specify both a new uploaded file and an existing document ID.", 400);

        if (cmd.DocumentStream is null && !cmd.ExistingDocumentId.HasValue)
            throw new OutwardWorkflowException("Must supply either a new uploaded file or an existing document ID.", 400);

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
            ?? throw new OutwardWorkflowException("Current user account is inactive or not found.", 403);

        if (cmd.ExistingDocumentId.HasValue)
        {
            var docExists = await db.Documents.AsNoTracking()
                .AnyAsync(d => d.Id == cmd.ExistingDocumentId.Value && d.RecordStatus == RecordStatus.Active, ct);
            if (!docExists)
                throw new OutwardWorkflowException("Existing document not found.", 404);
        }

        DocumentStorageWriteResult? fileResult = null;
        string? savedStoragePath = null;
        if (cmd.DocumentStream is not null)
        {
            if (string.IsNullOrWhiteSpace(cmd.DocumentFileName))
                throw new OutwardWorkflowException("Attached file must have a valid file name.", 400);

            fileResult = await SaveFileAsync(cmd.DocumentStream, cmd.DocumentFileName, ct);
            savedStoragePath = fileResult.StoragePath;
        }

        var eventId = Guid.NewGuid();
        Guid? newDocumentId = fileResult is not null ? Guid.NewGuid() : cmd.ExistingDocumentId;

        try
        {
            return await ExecuteWorkflowTransactionAsync(
                async opCt =>
                {
                    db.ChangeTracker.Clear();
                    var outward = await LockOutwardAsync(outwardId, opCt);

                    if (outward.Revision != cmd.ExpectedRevision)
                        throw new OutwardWorkflowException("This outward record was modified by another officer. Refresh before proceeding.", 409);

                    if (outward.Status != OutwardStatus.Registered)
                        throw new OutwardWorkflowException($"Cannot change document for an outward record in terminal status '{outward.Status}'.", 409);

                    if (fileResult is not null && newDocumentId.HasValue)
                    {
                        var ext = Path.GetExtension(cmd.DocumentFileName!).ToLowerInvariant();
                        var mime = ext == ".pdf" ? "application/pdf" : ext == ".png" ? "image/png" : "image/jpeg";

                        var document = new Document
                        {
                            Id = newDocumentId.Value,
                            OriginalFileName = cmd.DocumentFileName!,
                            StoragePath = fileResult.StoragePath,
                            Sha256Hash = fileResult.Sha256Hash,
                            FileSize = fileResult.FileSize,
                            MimeType = cmd.DocumentContentType ?? mime,
                            DocumentType = "OutwardLetter",
                            UploadedBy = actionUser.DisplayName
                        };
                        db.Documents.Add(document);
                    }

                    outward.MainDocumentId = newDocumentId;
                    outward.Revision++;

                    var maxSeq = await db.OutwardEvents
                        .Where(e => e.OutwardId == outwardId)
                        .MaxAsync(e => (int?)e.SequenceNumber, opCt);

                    var evt = new OutwardEvent
                    {
                        Id = eventId,
                        OutwardId = outwardId,
                        SequenceNumber = (maxSeq ?? 0) + 1,
                        Action = OutwardEventAction.MainDocumentChanged,
                        ActionByUserId = currentUserId,
                        ActionByDisplayNameSnapshot = actionUser.DisplayName,
                        ActionAt = DateTimeOffset.UtcNow,
                        DocumentId = newDocumentId
                    };
                    db.OutwardEvents.Add(evt);

                    await db.SaveChangesAsync(opCt);
                    return outward;
                },
                async verifyCt =>
                {
                    db.ChangeTracker.Clear();
                    return await db.OutwardEvents.AsNoTracking()
                        .AnyAsync(e => e.Id == eventId
                                    && e.OutwardId == outwardId
                                    && e.Action == OutwardEventAction.MainDocumentChanged
                                    && e.DocumentId == newDocumentId, verifyCt);
                },
                ct);
        }
        catch
        {
            if (savedStoragePath is not null)
            {
                try { await storage.DeleteAsync(savedStoragePath, CancellationToken.None); } catch { /* best effort */ }
            }
            throw;
        }
    }

    // ========================================================================
    // 4. ADD ATTACHMENT
    // ========================================================================
    public async Task<OutwardAttachment> AddAttachmentAsync(Guid outwardId, AddAttachmentCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (cmd.DocumentStream is not null && cmd.ExistingDocumentId.HasValue)
            throw new OutwardWorkflowException("Cannot specify both a new uploaded file and an existing document ID.", 400);

        if (cmd.DocumentStream is null && !cmd.ExistingDocumentId.HasValue)
            throw new OutwardWorkflowException("Must supply either a new uploaded file or an existing document ID.", 400);

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
            ?? throw new OutwardWorkflowException("Current user account is inactive or not found.", 403);

        if (cmd.ExistingDocumentId.HasValue)
        {
            var docExists = await db.Documents.AsNoTracking()
                .AnyAsync(d => d.Id == cmd.ExistingDocumentId.Value && d.RecordStatus == RecordStatus.Active, ct);
            if (!docExists)
                throw new OutwardWorkflowException("Existing document not found.", 404);
        }

        DocumentStorageWriteResult? fileResult = null;
        string? savedStoragePath = null;
        if (cmd.DocumentStream is not null)
        {
            if (string.IsNullOrWhiteSpace(cmd.DocumentFileName))
                throw new OutwardWorkflowException("Attached file must have a valid file name.", 400);

            fileResult = await SaveFileAsync(cmd.DocumentStream, cmd.DocumentFileName, ct);
            savedStoragePath = fileResult.StoragePath;
        }

        var attachmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        Guid documentId = fileResult is not null ? Guid.NewGuid() : cmd.ExistingDocumentId!.Value;

        try
        {
            return await ExecuteWorkflowTransactionAsync(
                async opCt =>
                {
                    db.ChangeTracker.Clear();
                    var outward = await LockOutwardAsync(outwardId, opCt);

                    if (outward.Revision != cmd.ExpectedRevision)
                        throw new OutwardWorkflowException("This outward record was modified by another officer. Refresh before proceeding.", 409);

                    if (outward.Status != OutwardStatus.Registered)
                        throw new OutwardWorkflowException($"Cannot add attachment to an outward record in terminal status '{outward.Status}'.", 409);

                    // Check duplicate attachment
                    var alreadyAttached = await db.OutwardAttachments.AsNoTracking()
                        .AnyAsync(a => a.OutwardId == outwardId && a.DocumentId == documentId && a.RecordStatus == RecordStatus.Active, opCt);
                    if (alreadyAttached)
                        throw new OutwardWorkflowException("This document is already attached to this outward record.", 409);

                    if (fileResult is not null)
                    {
                        var ext = Path.GetExtension(cmd.DocumentFileName!).ToLowerInvariant();
                        var mime = ext == ".pdf" ? "application/pdf" : ext == ".png" ? "image/png" : "image/jpeg";

                        var document = new Document
                        {
                            Id = documentId,
                            OriginalFileName = cmd.DocumentFileName!,
                            StoragePath = fileResult.StoragePath,
                            Sha256Hash = fileResult.Sha256Hash,
                            FileSize = fileResult.FileSize,
                            MimeType = cmd.DocumentContentType ?? mime,
                            DocumentType = "OutwardAttachment",
                            UploadedBy = actionUser.DisplayName
                        };
                        db.Documents.Add(document);
                    }

                    var attachment = new OutwardAttachment
                    {
                        Id = attachmentId,
                        OutwardId = outwardId,
                        DocumentId = documentId,
                        Title = string.IsNullOrWhiteSpace(cmd.Title) ? (cmd.DocumentFileName ?? "Attachment") : cmd.Title.Trim(),
                        AttachmentType = string.IsNullOrWhiteSpace(cmd.AttachmentType) ? "Enclosure" : cmd.AttachmentType.Trim(),
                        SequenceOrder = cmd.SequenceOrder <= 0 ? 1 : cmd.SequenceOrder
                    };
                    db.OutwardAttachments.Add(attachment);

                    outward.Revision++;

                    var maxSeq = await db.OutwardEvents
                        .Where(e => e.OutwardId == outwardId)
                        .MaxAsync(e => (int?)e.SequenceNumber, opCt);

                    var evt = new OutwardEvent
                    {
                        Id = eventId,
                        OutwardId = outwardId,
                        SequenceNumber = (maxSeq ?? 0) + 1,
                        Action = OutwardEventAction.AttachmentAdded,
                        ActionByUserId = currentUserId,
                        ActionByDisplayNameSnapshot = actionUser.DisplayName,
                        ActionAt = DateTimeOffset.UtcNow,
                        AttachmentId = attachmentId,
                        DocumentId = documentId
                    };
                    db.OutwardEvents.Add(evt);

                    await db.SaveChangesAsync(opCt);
                    return attachment;
                },
                async verifyCt =>
                {
                    db.ChangeTracker.Clear();
                    return await db.OutwardEvents.AsNoTracking()
                        .AnyAsync(e => e.Id == eventId
                                    && e.OutwardId == outwardId
                                    && e.Action == OutwardEventAction.AttachmentAdded
                                    && e.AttachmentId == attachmentId, verifyCt);
                },
                ct);
        }
        catch
        {
            if (savedStoragePath is not null)
            {
                try { await storage.DeleteAsync(savedStoragePath, CancellationToken.None); } catch { /* best effort */ }
            }
            throw;
        }
    }

    // ========================================================================
    // 5. REMOVE ATTACHMENT
    // ========================================================================
    public async Task<bool> RemoveAttachmentAsync(Guid outwardId, Guid attachmentId, RemoveAttachmentCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
            ?? throw new OutwardWorkflowException("Current user account is inactive or not found.", 403);

        var eventId = Guid.NewGuid();

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();
                var outward = await LockOutwardAsync(outwardId, opCt);

                if (outward.Revision != cmd.ExpectedRevision)
                    throw new OutwardWorkflowException("This outward record was modified by another officer. Refresh before proceeding.", 409);

                if (outward.Status != OutwardStatus.Registered)
                    throw new OutwardWorkflowException($"Cannot remove attachment from an outward record in terminal status '{outward.Status}'.", 409);

                var attachment = await db.OutwardAttachments
                    .FirstOrDefaultAsync(a => a.Id == attachmentId && a.OutwardId == outwardId && a.RecordStatus == RecordStatus.Active, opCt);
                if (attachment is null)
                    throw new OutwardWorkflowException("Attachment not found or already removed.", 404);

                attachment.RecordStatus = RecordStatus.Archived;
                outward.Revision++;

                var maxSeq = await db.OutwardEvents
                    .Where(e => e.OutwardId == outwardId)
                    .MaxAsync(e => (int?)e.SequenceNumber, opCt);

                var evt = new OutwardEvent
                {
                    Id = eventId,
                    OutwardId = outwardId,
                    SequenceNumber = (maxSeq ?? 0) + 1,
                    Action = OutwardEventAction.AttachmentRemoved,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = DateTimeOffset.UtcNow,
                    AttachmentId = attachmentId,
                    DocumentId = attachment.DocumentId
                };
                db.OutwardEvents.Add(evt);

                await db.SaveChangesAsync(opCt);
                return true;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();
                return await db.OutwardEvents.AsNoTracking()
                    .AnyAsync(e => e.Id == eventId
                                && e.OutwardId == outwardId
                                && e.Action == OutwardEventAction.AttachmentRemoved
                                && e.AttachmentId == attachmentId, verifyCt);
            },
            ct);
    }

    // ========================================================================
    // 6. ADD DAK LINK
    // ========================================================================
    public async Task<OutwardDakLink> AddDakLinkAsync(Guid outwardId, AddDakCommandWrapper cmd, Guid currentUserId, CancellationToken ct = default)
    {
        var relType = CanonicalizeRelationshipType(cmd.RelationshipType);

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
            ?? throw new OutwardWorkflowException("Current user account is inactive or not found.", 403);

        var canAccessDak = await dakAuth.CanAccessDakAsync(cmd.DakId, PermissionCodes.DakView, currentUserId, ct);
        if (!canAccessDak)
            throw new OutwardWorkflowException("You do not have permission to view the specified inward Dak.", 403);

        var linkId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();
                var outward = await LockOutwardAsync(outwardId, opCt);

                if (outward.Revision != cmd.ExpectedRevision)
                    throw new OutwardWorkflowException("This outward record was modified by another officer. Refresh before proceeding.", 409);

                if (outward.Status != OutwardStatus.Registered)
                    throw new OutwardWorkflowException($"Cannot add Dak link to an outward record in terminal status '{outward.Status}'.", 409);

                var linkExists = await db.OutwardDakLinks.AsNoTracking()
                    .AnyAsync(l => l.OutwardId == outwardId && l.DakId == cmd.DakId && l.RecordStatus == RecordStatus.Active, opCt);
                if (linkExists)
                    throw new OutwardWorkflowException("This inward Dak is already linked to this outward record.", 409);

                if (cmd.IsPrimary)
                {
                    var hasPrimary = await db.OutwardDakLinks.AsNoTracking()
                        .AnyAsync(l => l.OutwardId == outwardId && l.IsPrimary && l.RecordStatus == RecordStatus.Active, opCt);
                    if (hasPrimary)
                        throw new OutwardWorkflowException("This outward record already has an active primary Dak link. Second primary link is not permitted.", 409);
                }

                var link = new OutwardDakLink
                {
                    Id = linkId,
                    OutwardId = outwardId,
                    DakId = cmd.DakId,
                    IsPrimary = cmd.IsPrimary,
                    RelationshipType = relType
                };
                db.OutwardDakLinks.Add(link);

                outward.Revision++;

                var maxSeq = await db.OutwardEvents
                    .Where(e => e.OutwardId == outwardId)
                    .MaxAsync(e => (int?)e.SequenceNumber, opCt);

                var evt = new OutwardEvent
                {
                    Id = eventId,
                    OutwardId = outwardId,
                    SequenceNumber = (maxSeq ?? 0) + 1,
                    Action = OutwardEventAction.DakLinkAdded,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = DateTimeOffset.UtcNow,
                    DakId = cmd.DakId
                };
                db.OutwardEvents.Add(evt);

                await db.SaveChangesAsync(opCt);
                return link;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();
                return await db.OutwardEvents.AsNoTracking()
                    .AnyAsync(e => e.Id == eventId
                                && e.OutwardId == outwardId
                                && e.Action == OutwardEventAction.DakLinkAdded
                                && e.DakId == cmd.DakId, verifyCt);
            },
            ct);
    }

    // ========================================================================
    // 7. REMOVE DAK LINK
    // ========================================================================
    public async Task<bool> RemoveDakLinkAsync(Guid outwardId, Guid linkId, RemoveDakLinkCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
            ?? throw new OutwardWorkflowException("Current user account is inactive or not found.", 403);

        var eventId = Guid.NewGuid();

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();
                var outward = await LockOutwardAsync(outwardId, opCt);

                if (outward.Revision != cmd.ExpectedRevision)
                    throw new OutwardWorkflowException("This outward record was modified by another officer. Refresh before proceeding.", 409);

                if (outward.Status != OutwardStatus.Registered)
                    throw new OutwardWorkflowException($"Cannot remove Dak link from an outward record in terminal status '{outward.Status}'.", 409);

                var link = await db.OutwardDakLinks
                    .FirstOrDefaultAsync(l => l.Id == linkId && l.OutwardId == outwardId && l.RecordStatus == RecordStatus.Active, opCt);
                if (link is null)
                    throw new OutwardWorkflowException("Dak link not found or already removed.", 404);

                link.RecordStatus = RecordStatus.Archived;
                outward.Revision++;

                var maxSeq = await db.OutwardEvents
                    .Where(e => e.OutwardId == outwardId)
                    .MaxAsync(e => (int?)e.SequenceNumber, opCt);

                var evt = new OutwardEvent
                {
                    Id = eventId,
                    OutwardId = outwardId,
                    SequenceNumber = (maxSeq ?? 0) + 1,
                    Action = OutwardEventAction.DakLinkRemoved,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = DateTimeOffset.UtcNow,
                    DakId = link.DakId
                };
                db.OutwardEvents.Add(evt);

                await db.SaveChangesAsync(opCt);
                return true;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();
                return await db.OutwardEvents.AsNoTracking()
                    .AnyAsync(e => e.Id == eventId
                                && e.OutwardId == outwardId
                                && e.Action == OutwardEventAction.DakLinkRemoved, verifyCt);
            },
            ct);
    }

    // ========================================================================
    // 8. DISPATCH OUTWARD
    // ========================================================================
    public async Task<Outward> DispatchAsync(Guid outwardId, DispatchOutwardCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        var mode = CanonicalizeMode(cmd.DispatchMode);

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
            ?? throw new OutwardWorkflowException("Current user account is inactive or not found.", 403);

        var eventId = Guid.NewGuid();

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();
                var outward = await LockOutwardAsync(outwardId, opCt);

                if (outward.Revision != cmd.ExpectedRevision)
                    throw new OutwardWorkflowException("This outward record was modified by another officer. Refresh before proceeding.", 409);

                if (outward.Status != OutwardStatus.Registered)
                    throw new OutwardWorkflowException($"Cannot dispatch an outward record in status '{outward.Status}'. Only Registered records can be dispatched.", 409);

                var now = DateTimeOffset.UtcNow;
                outward.Status = OutwardStatus.Dispatched;
                outward.DispatchDate = cmd.DispatchDate;
                outward.DispatchMode = mode;
                outward.DispatchReferenceNumber = cmd.DispatchReferenceNumber?.Trim();
                outward.DispatchedByUserId = currentUserId;
                outward.DispatchedAt = now;
                outward.Revision++;

                var maxSeq = await db.OutwardEvents
                    .Where(e => e.OutwardId == outwardId)
                    .MaxAsync(e => (int?)e.SequenceNumber, opCt);

                var evt = new OutwardEvent
                {
                    Id = eventId,
                    OutwardId = outwardId,
                    SequenceNumber = (maxSeq ?? 0) + 1,
                    Action = OutwardEventAction.Dispatched,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = now,
                    DispatchDate = cmd.DispatchDate,
                    DispatchMode = mode,
                    DispatchReferenceNumber = cmd.DispatchReferenceNumber?.Trim()
                };
                db.OutwardEvents.Add(evt);

                await db.SaveChangesAsync(opCt);
                return outward;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();
                return await db.OutwardEvents.AsNoTracking()
                    .AnyAsync(e => e.Id == eventId
                                && e.OutwardId == outwardId
                                && e.Action == OutwardEventAction.Dispatched, verifyCt);
            },
            ct);
    }

    // ========================================================================
    // 9. CANCEL OUTWARD
    // ========================================================================
    public async Task<Outward> CancelAsync(Guid outwardId, CancelOutwardCommand cmd, Guid currentUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason) || cmd.Reason.Trim().Length < 5)
            throw new OutwardWorkflowException("Cancellation reason is mandatory and must be at least 5 characters.", 400);

        var actionUser = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
            ?? throw new OutwardWorkflowException("Current user account is inactive or not found.", 403);

        var eventId = Guid.NewGuid();

        return await ExecuteWorkflowTransactionAsync(
            async opCt =>
            {
                db.ChangeTracker.Clear();
                var outward = await LockOutwardAsync(outwardId, opCt);

                if (outward.Revision != cmd.ExpectedRevision)
                    throw new OutwardWorkflowException("This outward record was modified by another officer. Refresh before proceeding.", 409);

                if (outward.Status != OutwardStatus.Registered)
                    throw new OutwardWorkflowException($"Cannot cancel an outward record in status '{outward.Status}'. Only Registered records can be cancelled.", 409);

                var now = DateTimeOffset.UtcNow;
                outward.Status = OutwardStatus.Cancelled;
                outward.CancellationReason = cmd.Reason.Trim();
                outward.CancelledByUserId = currentUserId;
                outward.CancelledAt = now;
                outward.Revision++;

                var maxSeq = await db.OutwardEvents
                    .Where(e => e.OutwardId == outwardId)
                    .MaxAsync(e => (int?)e.SequenceNumber, opCt);

                var evt = new OutwardEvent
                {
                    Id = eventId,
                    OutwardId = outwardId,
                    SequenceNumber = (maxSeq ?? 0) + 1,
                    Action = OutwardEventAction.Cancelled,
                    ActionByUserId = currentUserId,
                    ActionByDisplayNameSnapshot = actionUser.DisplayName,
                    ActionAt = now,
                    CancellationReason = cmd.Reason.Trim()
                };
                db.OutwardEvents.Add(evt);

                await db.SaveChangesAsync(opCt);
                return outward;
            },
            async verifyCt =>
            {
                db.ChangeTracker.Clear();
                return await db.OutwardEvents.AsNoTracking()
                    .AnyAsync(e => e.Id == eventId
                                && e.OutwardId == outwardId
                                && e.Action == OutwardEventAction.Cancelled, verifyCt);
            },
            ct);
    }
}

public sealed record AddDakCommandWrapper(
    Guid DakId,
    bool IsPrimary,
    string RelationshipType,
    int ExpectedRevision
);
