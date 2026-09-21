namespace LAC.Infrastructure;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class CourtWorkflowException : Exception
{
    public int StatusCode { get; }
    public CourtWorkflowException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}

public sealed record CreateCourtCasePartyDto(
    Guid? PartyId = null,
    string? DisplayName = null,
    string? Role = null,
    string? PartyName = null,
    string? PartyType = null,
    string? AdvocateName = null,
    string? ContactDetails = null,
    bool? IsPrimary = null,
    string? FatherOrSpouseName = null,
    string? AddressText = null,
    string? Remarks = null,
    int Sequence = 0
)
{
    public string ResolvedDisplayName => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName.Trim() : (PartyName?.Trim() ?? "");
    public string ResolvedRole => !string.IsNullOrWhiteSpace(Role) ? Role.Trim() : (PartyType?.Trim() ?? "Party");
}

public sealed record CreateCourtCaseRepresentativeDto(
    string? DisplayName = null,
    string? Name = null,
    string RepresentativeType = "Counsel",
    string? Designation = null,
    string? BarRegistrationNumber = null,
    string? RepresentsRole = null,
    string? ContactText = null,
    string? ContactDetails = null,
    bool? IsLeadCounsel = null,
    string? Remarks = null,
    Guid? CourtCasePartyId = null
)
{
    public string ResolvedDisplayName => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName.Trim() : (Name?.Trim() ?? "");
    public string ResolvedRepresentativeType => !string.IsNullOrWhiteSpace(RepresentativeType) ? RepresentativeType.Trim() : "Counsel";
}

public sealed record CreateCourtCaseCommand(
    string CaseNumber,
    string CourtName,
    string? CaseTitle = null,
    string? CaseType = null,
    DateOnly? FiledDate = null,
    string? CurrentStatus = null,
    string? Remarks = null,
    Guid? ResponsibleOfficeDeskId = null,
    Guid? AssignedUserId = null,
    IReadOnlyList<Guid>? AwardIds = null,
    IReadOnlyList<Guid>? KhasraIds = null,
    IReadOnlyList<Guid>? MatterIds = null,
    IReadOnlyList<CreateCourtCasePartyDto>? Parties = null,
    IReadOnlyList<CreateCourtCaseRepresentativeDto>? Representatives = null
);

public sealed record UpdateCourtCaseMetadataCommand(
    string CaseNumber,
    string CourtName,
    string? CaseTitle,
    string? CaseType,
    DateOnly? FiledDate,
    string? CurrentStatus,
    DateOnly? DisposedDate,
    string? Remarks,
    int ExpectedRevision
);

public sealed record AssignCourtCaseCommand(
    Guid? ResponsibleOfficeDeskId = null,
    Guid? TargetResponsibleDeskId = null,
    Guid? AssignedUserId = null,
    Guid? TargetAssignedUserId = null,
    string? Reason = null,
    string? ReassignmentNotes = null,
    int ExpectedRevision = 0
)
{
    public Guid? EffectiveDeskId => ResponsibleOfficeDeskId ?? TargetResponsibleDeskId;
    public Guid? EffectiveUserId => AssignedUserId ?? TargetAssignedUserId;
    public string? EffectiveReason => !string.IsNullOrWhiteSpace(Reason) ? Reason.Trim() : (!string.IsNullOrWhiteSpace(ReassignmentNotes) ? ReassignmentNotes.Trim() : null);
}

public sealed record RecordCourtProceedingCommand(
    DateOnly? ProceedingDate,
    string? OrderType,
    string? RestraintNature,
    string? Summary,
    DateOnly? NextDate
);

public sealed record CourtProceedingDto(
    Guid Id,
    Guid CourtCaseId,
    DateOnly? ProceedingDate,
    string? OrderType,
    string? RestraintNature,
    string? Summary,
    DateOnly? NextDate,
    DateTimeOffset CreatedAt
);

public sealed record CourtCasePartyDto(
    Guid Id,
    Guid CourtCaseId,
    Guid? PartyId,
    string DisplayName,
    string Role,
    string? FatherOrSpouseName,
    string? AddressText,
    string? Remarks,
    int Sequence
);

public sealed record CourtCaseRepresentativeDto(
    Guid Id,
    Guid CourtCaseId,
    Guid? CourtCasePartyId,
    string DisplayName,
    string RepresentativeType,
    string? RepresentsRole,
    string? ContactText,
    string? Remarks
);

public sealed record CourtCaseDocumentDto(
    Guid Id,
    Guid CourtCaseId,
    Guid DocumentId,
    string OriginalFileName,
    string DocumentType,
    string? DocumentRole,
    string? DisplayName,
    Guid? CourtProceedingId,
    long? FileSize,
    string? MimeType,
    DateTimeOffset UploadedAt
);

public interface ICourtWorkflowService
{
    Task<Guid> CreateCourtCaseAsync(CreateCourtCaseCommand command, Guid callerUserId, CancellationToken ct = default);
    Task UpdateMetadataAsync(Guid courtCaseId, UpdateCourtCaseMetadataCommand command, Guid callerUserId, CancellationToken ct = default);
    Task AssignAsync(Guid courtCaseId, AssignCourtCaseCommand command, Guid callerUserId, CancellationToken ct = default);
    Task<CourtProceedingDto> RecordProceedingAsync(Guid courtCaseId, RecordCourtProceedingCommand command, Guid callerUserId, CancellationToken ct = default);
    Task LinkAwardAsync(Guid courtCaseId, Guid awardId, Guid callerUserId, CancellationToken ct = default);
    Task UnlinkAwardAsync(Guid courtCaseId, Guid awardId, Guid callerUserId, CancellationToken ct = default);
    Task LinkKhasraAsync(Guid courtCaseId, Guid khasraId, Guid callerUserId, CancellationToken ct = default);
    Task UnlinkKhasraAsync(Guid courtCaseId, Guid khasraId, Guid callerUserId, CancellationToken ct = default);
    Task LinkMatterAsync(Guid courtCaseId, Guid matterId, Guid callerUserId, CancellationToken ct = default);
    Task UnlinkMatterAsync(Guid courtCaseId, Guid matterId, Guid callerUserId, CancellationToken ct = default);
    Task<CourtCasePartyDto> AddPartyAsync(Guid courtCaseId, CreateCourtCasePartyDto dto, Guid callerUserId, CancellationToken ct = default);
    Task<CourtCasePartyDto> UpdatePartyAsync(Guid courtCaseId, Guid partyEntryId, CreateCourtCasePartyDto dto, Guid callerUserId, CancellationToken ct = default);
    Task RemovePartyAsync(Guid courtCaseId, Guid partyEntryId, Guid callerUserId, CancellationToken ct = default);
    Task<CourtCaseRepresentativeDto> AddRepresentativeAsync(Guid courtCaseId, CreateCourtCaseRepresentativeDto dto, Guid callerUserId, CancellationToken ct = default);
    Task<CourtCaseRepresentativeDto> UpdateRepresentativeAsync(Guid courtCaseId, Guid representativeId, CreateCourtCaseRepresentativeDto dto, Guid callerUserId, CancellationToken ct = default);
    Task RemoveRepresentativeAsync(Guid courtCaseId, Guid representativeId, Guid callerUserId, CancellationToken ct = default);
    Task<CourtCaseDocumentDto> UploadDocumentAsync(Guid courtCaseId, Stream stream, string fileName, string? contentType, string? documentRole, string? displayName, Guid? proceedingId, Guid callerUserId, CancellationToken ct = default);
    Task<CourtCaseDocumentDto> LinkDocumentAsync(Guid courtCaseId, Guid documentId, string? documentRole, string? displayName, Guid? proceedingId, Guid callerUserId, CancellationToken ct = default);
    Task UnlinkDocumentAsync(Guid courtCaseId, Guid documentLinkId, Guid callerUserId, CancellationToken ct = default);
}

public sealed class CourtWorkflowService(
    LacDbContext db,
    ICourtAuthorizationService courtAuth,
    IDocumentStorage storage,
    Func<Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy>? strategyFactory = null) : ICourtWorkflowService
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

    private async Task<(AppUser User, string DisplayName, string? Designation)> ResolveActorAsync(Guid userId, CancellationToken ct)
    {
        var actor = await db.AppUsers.AsNoTracking()
            .Include(u => u.Designation)
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Actor user not found or inactive.", 401);

        return (actor, actor.DisplayName, actor.Designation?.Name);
    }

    public async Task<Guid> CreateCourtCaseAsync(CreateCourtCaseCommand command, Guid callerUserId, CancellationToken ct = default)
    {
        var canCreate = await courtAuth.CanCreateCourtCaseAsync(callerUserId, ct);
        if (!canCreate)
            throw new CourtWorkflowException("You do not have permission to create court cases.", 403);

        if (string.IsNullOrWhiteSpace(command.CaseNumber))
            throw new CourtWorkflowException("CaseNumber is required.", 400);
        if (string.IsNullOrWhiteSpace(command.CourtName))
            throw new CourtWorkflowException("CourtName is required.", 400);

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);

        // Validate desk and user if supplied
        string? targetDeskName = null;
        if (command.ResponsibleOfficeDeskId.HasValue)
        {
            var desk = await db.OfficeDesks.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == command.ResponsibleOfficeDeskId.Value && d.IsActive && d.RecordStatus == RecordStatus.Active, ct)
                ?? throw new CourtWorkflowException("Responsible office desk not found or inactive.", 400);
            targetDeskName = desk.Name;
        }

        string? targetUserName = null;
        if (command.AssignedUserId.HasValue)
        {
            var user = await db.AppUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == command.AssignedUserId.Value && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
                ?? throw new CourtWorkflowException("Assigned user not found or inactive.", 400);
            targetUserName = user.DisplayName;
        }

        var caseId = Guid.NewGuid();
        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
            await db.CourtCases.AsNoTracking().AnyAsync(x => x.Id == caseId, c);

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();
            if (await verifySucceeded(c)) return caseId;

            var now = DateTimeOffset.UtcNow;
            var courtCase = new CourtCase
            {
                Id = caseId,
                CaseNumber = command.CaseNumber.Trim(),
                CourtName = command.CourtName.Trim(),
                CaseTitle = string.IsNullOrWhiteSpace(command.CaseTitle) ? null : command.CaseTitle.Trim(),
                CaseType = string.IsNullOrWhiteSpace(command.CaseType) ? null : command.CaseType.Trim(),
                FiledDate = command.FiledDate,
                CurrentStatus = string.IsNullOrWhiteSpace(command.CurrentStatus) ? "Pending" : command.CurrentStatus.Trim(),
                Remarks = string.IsNullOrWhiteSpace(command.Remarks) ? null : command.Remarks.Trim(),
                ResponsibleOfficeDeskId = command.ResponsibleOfficeDeskId,
                AssignedUserId = command.AssignedUserId,
                Revision = 1,
                RecordStatus = RecordStatus.Active,
                CreatedBy = actorDisplayName,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.CourtCases.Add(courtCase);

            var courtWs = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Code == WorkstreamCodes.CourtReferences, c);
            var courtWsId = courtWs?.Id;
            var courtWsName = courtWs?.Name ?? "Court References";

            // Event 1: Created
            db.CourtCaseEvents.Add(new CourtCaseEvent
            {
                Id = Guid.NewGuid(),
                CourtCaseId = caseId,
                SequenceNumber = 1,
                Action = CourtCaseAction.Created,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                CaseNumberSnapshot = courtCase.CaseNumber,
                CaseTitleSnapshot = courtCase.CaseTitle,
                WorkstreamIdSnapshot = courtWsId,
                WorkstreamNameSnapshot = courtWsName,
                TargetDeskId = command.ResponsibleOfficeDeskId,
                TargetDeskNameSnapshot = targetDeskName,
                TargetUserId = command.AssignedUserId,
                TargetUserDisplayNameSnapshot = targetUserName,
                NewStatus = courtCase.CurrentStatus,
                Notes = "Court case registered"
            });

            // Initial linked awards
            if (command.AwardIds != null && command.AwardIds.Count > 0)
            {
                foreach (var aid in command.AwardIds.Distinct())
                {
                    db.Set<CourtCaseAward>().Add(new CourtCaseAward
                    {
                        Id = Guid.NewGuid(),
                        CourtCaseId = caseId,
                        AwardId = aid
                    });
                }
            }

            // Initial linked khasras
            if (command.KhasraIds != null && command.KhasraIds.Count > 0)
            {
                foreach (var kid in command.KhasraIds.Distinct())
                {
                    db.Set<CourtCaseKhasra>().Add(new CourtCaseKhasra
                    {
                        Id = Guid.NewGuid(),
                        CourtCaseId = caseId,
                        KhasraId = kid
                    });
                }
            }

            // Initial linked matters
            if (command.MatterIds != null && command.MatterIds.Count > 0)
            {
                foreach (var mid in command.MatterIds.Distinct())
                {
                    db.CourtCaseMatters.Add(new CourtCaseMatter
                    {
                        Id = Guid.NewGuid(),
                        CourtCaseId = caseId,
                        MatterId = mid,
                        RecordStatus = RecordStatus.Active,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = actorDisplayName
                    });
                }
            }

            // Initial parties
            if (command.Parties != null && command.Parties.Count > 0)
            {
                var seq = 1;
                foreach (var p in command.Parties)
                {
                    var pDisplayName = p.ResolvedDisplayName;
                    var pRole = p.ResolvedRole;
                    if (string.IsNullOrWhiteSpace(pDisplayName)) pDisplayName = "Party";
                    if (string.IsNullOrWhiteSpace(pRole)) pRole = "Party";

                    db.CourtCaseParties.Add(new CourtCaseParty
                    {
                        Id = Guid.NewGuid(),
                        CourtCaseId = caseId,
                        PartyId = p.PartyId,
                        DisplayName = pDisplayName,
                        Role = pRole,
                        FatherOrSpouseName = p.FatherOrSpouseName?.Trim(),
                        AddressText = p.AddressText?.Trim(),
                        Remarks = p.Remarks?.Trim(),
                        Sequence = p.Sequence > 0 ? p.Sequence : seq++,
                        RecordStatus = RecordStatus.Active,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = actorDisplayName
                    });
                }
            }

            // Initial representatives
            if (command.Representatives != null && command.Representatives.Count > 0)
            {
                foreach (var r in command.Representatives)
                {
                    var rDisplayName = r.ResolvedDisplayName;
                    if (string.IsNullOrWhiteSpace(rDisplayName)) rDisplayName = "Counsel";

                    db.CourtCaseRepresentatives.Add(new CourtCaseRepresentative
                    {
                        Id = Guid.NewGuid(),
                        CourtCaseId = caseId,
                        CourtCasePartyId = r.CourtCasePartyId,
                        DisplayName = rDisplayName,
                        RepresentativeType = r.ResolvedRepresentativeType,
                        RepresentsRole = r.RepresentsRole?.Trim(),
                        ContactText = r.ContactText?.Trim(),
                        Remarks = r.Remarks?.Trim(),
                        RecordStatus = RecordStatus.Active,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = actorDisplayName
                    });
                }
            }

            await db.SaveChangesAsync(c);
            return caseId;
        }, verifySucceeded, ct);
    }

    public async Task UpdateMetadataAsync(Guid courtCaseId, UpdateCourtCaseMetadataCommand command, Guid callerUserId, CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canEdit)
            throw new CourtWorkflowException("You do not have permission to edit this court case.", 403);

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
        {
            var row = await db.CourtCases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == courtCaseId, c);
            return row != null && row.Revision > command.ExpectedRevision;
        };

        await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();
            var courtCase = await db.CourtCases.FirstOrDefaultAsync(x => x.Id == courtCaseId && x.RecordStatus == RecordStatus.Active, c)
                ?? throw new CourtWorkflowException("Court case not found.", 404);

            if (courtCase.Revision != command.ExpectedRevision)
                throw new CourtWorkflowException("Conflict: Court case was modified by another user. Please reload.", 409);

            var now = DateTimeOffset.UtcNow;
            var oldStatus = courtCase.CurrentStatus;
            var statusChanged = !string.Equals(oldStatus, command.CurrentStatus, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(command.CaseNumber))
                courtCase.CaseNumber = command.CaseNumber.Trim();
            if (!string.IsNullOrWhiteSpace(command.CourtName))
                courtCase.CourtName = command.CourtName.Trim();
            courtCase.CaseTitle = string.IsNullOrWhiteSpace(command.CaseTitle) ? null : command.CaseTitle.Trim();
            courtCase.CaseType = string.IsNullOrWhiteSpace(command.CaseType) ? null : command.CaseType.Trim();
            courtCase.FiledDate = command.FiledDate;
            courtCase.CurrentStatus = string.IsNullOrWhiteSpace(command.CurrentStatus) ? "Pending" : command.CurrentStatus.Trim();
            courtCase.DisposedDate = command.DisposedDate;
            courtCase.Remarks = string.IsNullOrWhiteSpace(command.Remarks) ? null : command.Remarks.Trim();
            courtCase.Revision++;
            courtCase.UpdatedAt = now;
            courtCase.UpdatedBy = actorDisplayName;

            var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;
            nextSeq++;

            var courtWs = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Code == WorkstreamCodes.CourtReferences, c);

            db.CourtCaseEvents.Add(new CourtCaseEvent
            {
                Id = Guid.NewGuid(),
                CourtCaseId = courtCaseId,
                SequenceNumber = nextSeq,
                Action = statusChanged ? CourtCaseAction.StatusChanged : CourtCaseAction.MetadataUpdated,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                CaseNumberSnapshot = courtCase.CaseNumber,
                CaseTitleSnapshot = courtCase.CaseTitle,
                WorkstreamIdSnapshot = courtWs?.Id,
                WorkstreamNameSnapshot = courtWs?.Name ?? "Court References",
                OldStatus = oldStatus,
                NewStatus = courtCase.CurrentStatus,
                Notes = statusChanged ? $"Status changed from {oldStatus} to {courtCase.CurrentStatus}" : "Metadata updated"
            });

            await db.SaveChangesAsync(c);
            return true;
        }, verifySucceeded, ct);
    }

    public async Task AssignAsync(Guid courtCaseId, AssignCourtCaseCommand command, Guid callerUserId, CancellationToken ct = default)
    {
        var canAssign = await courtAuth.CanAssignCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canAssign)
            throw new CourtWorkflowException("You do not have permission to assign this court case.", 403);

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);

        var effectiveDeskId = command.EffectiveDeskId;
        var effectiveUserId = command.EffectiveUserId;
        var effectiveReason = command.EffectiveReason;

        string? targetDeskName = null;
        if (effectiveDeskId.HasValue)
        {
            var desk = await db.OfficeDesks.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == effectiveDeskId.Value && d.IsActive && d.RecordStatus == RecordStatus.Active, ct)
                ?? throw new CourtWorkflowException("Target office desk not found or inactive.", 400);
            targetDeskName = desk.Name;
        }

        string? targetUserName = null;
        if (effectiveUserId.HasValue)
        {
            var user = await db.AppUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == effectiveUserId.Value && u.IsActive && u.RecordStatus == RecordStatus.Active, ct)
                ?? throw new CourtWorkflowException("Target assigned user not found or inactive.", 400);
            targetUserName = user.DisplayName;
        }

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
        {
            var row = await db.CourtCases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == courtCaseId, c);
            return row != null && row.Revision > command.ExpectedRevision;
        };

        await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();
            var courtCase = await db.CourtCases
                .Include(x => x.ResponsibleOfficeDesk)
                .Include(x => x.AssignedUser)
                .FirstOrDefaultAsync(x => x.Id == courtCaseId && x.RecordStatus == RecordStatus.Active, c)
                ?? throw new CourtWorkflowException("Court case not found.", 404);

            if (courtCase.Revision != command.ExpectedRevision)
                throw new CourtWorkflowException("Conflict: Court case was modified by another user. Please reload.", 409);

            var now = DateTimeOffset.UtcNow;
            var sourceDeskId = courtCase.ResponsibleOfficeDeskId;
            var sourceDeskName = courtCase.ResponsibleOfficeDesk?.Name;
            var sourceUserId = courtCase.AssignedUserId;
            var sourceUserName = courtCase.AssignedUser?.DisplayName;

            courtCase.ResponsibleOfficeDeskId = effectiveDeskId;
            courtCase.AssignedUserId = effectiveUserId;
            courtCase.Revision++;
            courtCase.UpdatedAt = now;
            courtCase.UpdatedBy = actorDisplayName;

            var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;
            nextSeq++;

            var courtWs = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Code == WorkstreamCodes.CourtReferences, c);

            db.CourtCaseEvents.Add(new CourtCaseEvent
            {
                Id = Guid.NewGuid(),
                CourtCaseId = courtCaseId,
                SequenceNumber = nextSeq,
                Action = CourtCaseAction.ResponsibilityChanged,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                CaseNumberSnapshot = courtCase.CaseNumber,
                CaseTitleSnapshot = courtCase.CaseTitle,
                WorkstreamIdSnapshot = courtWs?.Id,
                WorkstreamNameSnapshot = courtWs?.Name ?? "Court References",
                SourceDeskId = sourceDeskId,
                SourceDeskNameSnapshot = sourceDeskName,
                SourceUserId = sourceUserId,
                SourceUserDisplayNameSnapshot = sourceUserName,
                TargetDeskId = effectiveDeskId,
                TargetDeskNameSnapshot = targetDeskName,
                TargetUserId = effectiveUserId,
                TargetUserDisplayNameSnapshot = targetUserName,
                Reason = effectiveReason,
                Notes = "Responsibility reassigned"
            });

            await db.SaveChangesAsync(c);
            return true;
        }, verifySucceeded, ct);
    }

    public async Task<CourtProceedingDto> RecordProceedingAsync(
        Guid courtCaseId,
        RecordCourtProceedingCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var canManage = await courtAuth.CanManageProceedingsAsync(courtCaseId, callerUserId, ct);
        if (!canManage)
            canManage = await courtAuth.CanEditCourtReferencesAsync(callerUserId, ct);

        if (!canManage)
            throw new CourtWorkflowException("You do not have permission to record court proceedings.", 403);

        var courtCase = await db.CourtCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct);
        if (courtCase is null)
            throw new CourtWorkflowException("Court case not found.", 404);

        if (command.NextDate.HasValue && command.ProceedingDate.HasValue && command.NextDate.Value < command.ProceedingDate.Value)
            throw new CourtWorkflowException("NextDate cannot be earlier than ProceedingDate.", 400);

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);

        var proceedingId = Guid.NewGuid();
        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
            await db.Set<CourtProceeding>().AsNoTracking().AnyAsync(p => p.Id == proceedingId, c);

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                var p = await db.Set<CourtProceeding>().AsNoTracking().FirstAsync(p => p.Id == proceedingId, c);
                return new CourtProceedingDto(p.Id, p.CourtCaseId, p.ProceedingDate, p.OrderType, p.RestraintNature, p.Summary, p.NextDate, p.CreatedAt);
            }

            var now = DateTimeOffset.UtcNow;
            var proceeding = new CourtProceeding
            {
                Id = proceedingId,
                CourtCaseId = courtCaseId,
                ProceedingDate = command.ProceedingDate,
                OrderType = command.OrderType,
                RestraintNature = command.RestraintNature,
                Summary = command.Summary,
                NextDate = command.NextDate,
                CreatedBy = actorDisplayName,
                RecordStatus = RecordStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Set<CourtProceeding>().Add(proceeding);

            // Append CourtCaseEvent
            var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;
            nextSeq++;

            var courtWs = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Code == WorkstreamCodes.CourtReferences, c);

            db.CourtCaseEvents.Add(new CourtCaseEvent
            {
                Id = Guid.NewGuid(),
                CourtCaseId = courtCaseId,
                SequenceNumber = nextSeq,
                Action = CourtCaseAction.ProceedingRecorded,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                CaseNumberSnapshot = courtCase.CaseNumber,
                CaseTitleSnapshot = courtCase.CaseTitle,
                WorkstreamIdSnapshot = courtWs?.Id,
                WorkstreamNameSnapshot = courtWs?.Name ?? "Court References",
                CourtProceedingId = proceedingId,
                Notes = $"Proceeding on {command.ProceedingDate:yyyy-MM-dd}: {command.OrderType ?? "Hearing"}"
            });

            await db.SaveChangesAsync(c);

            // Deterministic authoritative current proceeding evaluation
            var allActiveProceedings = await db.CourtProceedings.AsNoTracking()
                .Where(p => p.CourtCaseId == courtCaseId && p.RecordStatus == RecordStatus.Active)
                .ToListAsync(c);

            var authoritative = allActiveProceedings
                .OrderByDescending(p => p.ProceedingDate.HasValue)
                .ThenByDescending(p => p.ProceedingDate)
                .ThenByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .FirstOrDefault();

            // ONLY modify or close active ScheduledEvent if newly entered proceeding is the authoritative current proceeding
            if (authoritative != null && authoritative.Id == proceedingId)
            {
                var existingSchedule = await db.ScheduledEvents
                    .FirstOrDefaultAsync(e => e.CourtCaseId == courtCaseId
                                           && e.Origin == ScheduledEventOrigin.CourtProceeding
                                           && e.Status == ScheduledEventStatus.Scheduled
                                           && e.RecordStatus == RecordStatus.Active, c);

                var courtWsId = courtWs?.Id ?? Guid.Empty;
                var courtWsName = courtWs?.Name ?? "Court References";

                if (existingSchedule != null)
                {
                    var currentSeq = await db.ScheduledEventEvents
                        .Where(e => e.ScheduledEventId == existingSchedule.Id)
                        .MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

                    if (command.NextDate.HasValue)
                    {
                        var oldDate = existingSchedule.ScheduledDate;
                        var oldTime = existingSchedule.ScheduledTime;

                        existingSchedule.ScheduledDate = command.NextDate.Value;
                        existingSchedule.CourtProceedingId = proceeding.Id;
                        existingSchedule.Revision++;
                        existingSchedule.LastActivityAt = now;

                        db.ScheduledEventEvents.Add(new ScheduledEventEvent
                        {
                            Id = Guid.NewGuid(),
                            ScheduledEventId = existingSchedule.Id,
                            SequenceNumber = currentSeq + 1,
                            Action = ScheduledEventAction.Rescheduled,
                            ActionAt = now,
                            ActorUserId = callerUserId,
                            ActorDisplayNameSnapshot = actorDisplayName,
                            ActorDesignationSnapshot = actorDesignation,
                            WorkstreamIdSnapshot = existingSchedule.WorkstreamId,
                            WorkstreamNameSnapshot = courtWsName,
                            OldScheduledDate = oldDate,
                            OldScheduledTime = oldTime,
                            NewScheduledDate = command.NextDate.Value,
                            NewScheduledTime = null,
                            Reason = "Authoritative Court Proceeding NextDate updated",
                            Notes = $"Synchronized from Court Proceeding recorded on {now:yyyy-MM-dd HH:mm:ss} UTC"
                        });
                    }
                    else
                    {
                        existingSchedule.Status = ScheduledEventStatus.Completed;
                        existingSchedule.CompletedAt = now;
                        existingSchedule.CourtProceedingId = proceeding.Id;
                        existingSchedule.Revision++;
                        existingSchedule.LastActivityAt = now;

                        db.ScheduledEventEvents.Add(new ScheduledEventEvent
                        {
                            Id = Guid.NewGuid(),
                            ScheduledEventId = existingSchedule.Id,
                            SequenceNumber = currentSeq + 1,
                            Action = ScheduledEventAction.Completed,
                            ActionAt = now,
                            ActorUserId = callerUserId,
                            ActorDisplayNameSnapshot = actorDisplayName,
                            ActorDesignationSnapshot = actorDesignation,
                            WorkstreamIdSnapshot = existingSchedule.WorkstreamId,
                            WorkstreamNameSnapshot = courtWsName,
                            Reason = "Superseded: latest court proceeding has no future hearing date",
                            Notes = $"Terminal transition from Court Proceeding recorded on {now:yyyy-MM-dd HH:mm:ss} UTC"
                        });
                    }
                }
                else if (command.NextDate.HasValue)
                {
                    // Create new active ScheduledEvent for this authoritative NDOH
                    var scheduledEvent = new ScheduledEvent
                    {
                        Id = Guid.NewGuid(),
                        Title = $"Court Hearing: {courtCase.CaseNumber} - {courtCase.CourtName}",
                        Description = $"Authoritative NDOH from proceeding on {command.ProceedingDate:yyyy-MM-dd}",
                        ScheduledDate = command.NextDate.Value,
                        ScheduledTime = null,
                        EventKind = ScheduledEventKind.CourtHearing,
                        Priority = ScheduledEventPriority.Urgent,
                        Status = ScheduledEventStatus.Scheduled,
                        Origin = ScheduledEventOrigin.CourtProceeding,
                        WorkstreamId = courtWsId,
                        ResponsibleOfficeDeskId = courtCase.ResponsibleOfficeDeskId,
                        AssignedUserId = courtCase.AssignedUserId,
                        CourtCaseId = courtCaseId,
                        CourtProceedingId = proceeding.Id,
                        CreatedByUserId = callerUserId,
                        CreatedByDisplayNameSnapshot = actorDisplayName,
                        CreatedByDesignationSnapshot = actorDesignation,
                        LastActivityAt = now,
                        RecordStatus = RecordStatus.Active,
                        CreatedAt = now,
                        UpdatedAt = now,
                        Revision = 1
                    };
                    db.ScheduledEvents.Add(scheduledEvent);

                    db.ScheduledEventEvents.Add(new ScheduledEventEvent
                    {
                        Id = Guid.NewGuid(),
                        ScheduledEventId = scheduledEvent.Id,
                        SequenceNumber = 1,
                        Action = ScheduledEventAction.Created,
                        ActionAt = now,
                        ActorUserId = callerUserId,
                        ActorDisplayNameSnapshot = actorDisplayName,
                        ActorDesignationSnapshot = actorDesignation,
                        WorkstreamIdSnapshot = courtWsId,
                        WorkstreamNameSnapshot = courtWsName,
                        NewScheduledDate = command.NextDate.Value,
                        Reason = "Initial authoritative Court Hearing scheduled",
                        Notes = $"Synchronized from Court Proceeding recorded on {now:yyyy-MM-dd HH:mm:ss} UTC"
                    });
                }

                await db.SaveChangesAsync(c);
            }

            return new CourtProceedingDto(
                proceeding.Id,
                proceeding.CourtCaseId,
                proceeding.ProceedingDate,
                proceeding.OrderType,
                proceeding.RestraintNature,
                proceeding.Summary,
                proceeding.NextDate,
                proceeding.CreatedAt
            );
        }, verifySucceeded, ct);
    }

    public async Task LinkAwardAsync(Guid courtCaseId, Guid awardId, Guid callerUserId, CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canEdit) throw new CourtWorkflowException("Forbidden", 403);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var awardExists = await db.Awards.AnyAsync(a => a.Id == awardId && a.RecordStatus == RecordStatus.Active, ct);
        if (!awardExists) throw new CourtWorkflowException("Award not found.", 404);

        var alreadyLinked = await db.Set<CourtCaseAward>().AnyAsync(x => x.CourtCaseId == courtCaseId && x.AwardId == awardId, ct);
        if (alreadyLinked) return;

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        db.Set<CourtCaseAward>().Add(new CourtCaseAward
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            AwardId = awardId
        });

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.AwardLinked,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            AwardId = awardId,
            Notes = "Award linked"
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task UnlinkAwardAsync(Guid courtCaseId, Guid awardId, Guid callerUserId, CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canEdit) throw new CourtWorkflowException("Forbidden", 403);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var link = await db.Set<CourtCaseAward>().FirstOrDefaultAsync(x => x.CourtCaseId == courtCaseId && x.AwardId == awardId, ct);
        if (link is null) return;

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        db.Set<CourtCaseAward>().Remove(link);

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.AwardUnlinked,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            AwardId = awardId,
            Notes = "Award unlinked"
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task LinkKhasraAsync(Guid courtCaseId, Guid khasraId, Guid callerUserId, CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canEdit) throw new CourtWorkflowException("Forbidden", 403);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var khasraExists = await db.Khasras.AnyAsync(k => k.Id == khasraId && k.RecordStatus == RecordStatus.Active, ct);
        if (!khasraExists) throw new CourtWorkflowException("Khasra not found.", 404);

        var alreadyLinked = await db.Set<CourtCaseKhasra>().AnyAsync(x => x.CourtCaseId == courtCaseId && x.KhasraId == khasraId, ct);
        if (alreadyLinked) return;

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        db.Set<CourtCaseKhasra>().Add(new CourtCaseKhasra
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            KhasraId = khasraId
        });

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.KhasraLinked,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            KhasraId = khasraId,
            Notes = "Khasra linked"
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task UnlinkKhasraAsync(Guid courtCaseId, Guid khasraId, Guid callerUserId, CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canEdit) throw new CourtWorkflowException("Forbidden", 403);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var link = await db.Set<CourtCaseKhasra>().FirstOrDefaultAsync(x => x.CourtCaseId == courtCaseId && x.KhasraId == khasraId, ct);
        if (link is null) return;

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        db.Set<CourtCaseKhasra>().Remove(link);

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.KhasraUnlinked,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            KhasraId = khasraId,
            Notes = "Khasra unlinked"
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task LinkMatterAsync(Guid courtCaseId, Guid matterId, Guid callerUserId, CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canEdit) throw new CourtWorkflowException("Forbidden", 403);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var matterExists = await db.Matters.AnyAsync(m => m.Id == matterId && m.RecordStatus == RecordStatus.Active, ct);
        if (!matterExists) throw new CourtWorkflowException("Matter not found.", 404);

        var existing = await db.CourtCaseMatters.FirstOrDefaultAsync(x => x.CourtCaseId == courtCaseId && x.MatterId == matterId, ct);
        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        if (existing != null)
        {
            if (existing.RecordStatus == RecordStatus.Active) return;
            existing.RecordStatus = RecordStatus.Active;
            existing.UpdatedAt = now;
            existing.UpdatedBy = actorDisplayName;
        }
        else
        {
            db.CourtCaseMatters.Add(new CourtCaseMatter
            {
                Id = Guid.NewGuid(),
                CourtCaseId = courtCaseId,
                MatterId = matterId,
                RecordStatus = RecordStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = actorDisplayName
            });
        }

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.MatterLinked,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            MatterId = matterId,
            Notes = "Matter linked"
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task UnlinkMatterAsync(Guid courtCaseId, Guid matterId, Guid callerUserId, CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canEdit) throw new CourtWorkflowException("Forbidden", 403);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var existing = await db.CourtCaseMatters.FirstOrDefaultAsync(x => x.CourtCaseId == courtCaseId && x.MatterId == matterId && x.RecordStatus == RecordStatus.Active, ct);
        if (existing is null) return;

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        existing.RecordStatus = RecordStatus.Archived;
        existing.UpdatedAt = now;
        existing.UpdatedBy = actorDisplayName;

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.MatterUnlinked,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            MatterId = matterId,
            Notes = "Matter unlinked"
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<CourtCasePartyDto> AddPartyAsync(Guid courtCaseId, CreateCourtCasePartyDto dto, Guid callerUserId, CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canEdit) throw new CourtWorkflowException("Forbidden", 403);

        var displayName = dto.ResolvedDisplayName;
        var role = dto.ResolvedRole;
        if (string.IsNullOrWhiteSpace(displayName)) throw new CourtWorkflowException("DisplayName is required.", 400);
        if (string.IsNullOrWhiteSpace(role)) throw new CourtWorkflowException("Role is required.", 400);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        var party = new CourtCaseParty
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            PartyId = dto.PartyId,
            DisplayName = displayName,
            Role = role,
            FatherOrSpouseName = dto.FatherOrSpouseName?.Trim(),
            AddressText = dto.AddressText?.Trim(),
            Remarks = dto.Remarks?.Trim(),
            Sequence = dto.Sequence,
            RecordStatus = RecordStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorDisplayName
        };
        db.CourtCaseParties.Add(party);

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.PartyAdded,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            CourtCasePartyId = party.Id,
            Notes = $"Party added: {party.DisplayName} ({party.Role})"
        });

        await db.SaveChangesAsync(ct);
        return new CourtCasePartyDto(party.Id, party.CourtCaseId, party.PartyId, party.DisplayName, party.Role, party.FatherOrSpouseName, party.AddressText, party.Remarks, party.Sequence);
    }

    public async Task<CourtCasePartyDto> UpdatePartyAsync(Guid courtCaseId, Guid partyEntryId, CreateCourtCasePartyDto dto, Guid callerUserId, CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canEdit) throw new CourtWorkflowException("Forbidden", 403);

        var displayName = dto.ResolvedDisplayName;
        var role = dto.ResolvedRole;
        if (string.IsNullOrWhiteSpace(displayName)) throw new CourtWorkflowException("DisplayName is required.", 400);
        if (string.IsNullOrWhiteSpace(role)) throw new CourtWorkflowException("Role is required.", 400);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var party = await db.CourtCaseParties.FirstOrDefaultAsync(p => p.Id == partyEntryId && p.CourtCaseId == courtCaseId && p.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Party entry not found.", 404);

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        party.DisplayName = displayName;
        party.Role = role;
        party.FatherOrSpouseName = dto.FatherOrSpouseName?.Trim();
        party.AddressText = dto.AddressText?.Trim();
        party.Remarks = dto.Remarks?.Trim();
        party.Sequence = dto.Sequence;
        party.UpdatedAt = now;
        party.UpdatedBy = actorDisplayName;

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.PartyUpdated,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            CourtCasePartyId = party.Id,
            Notes = $"Party updated: {party.DisplayName} ({party.Role})"
        });

        await db.SaveChangesAsync(ct);
        return new CourtCasePartyDto(party.Id, party.CourtCaseId, party.PartyId, party.DisplayName, party.Role, party.FatherOrSpouseName, party.AddressText, party.Remarks, party.Sequence);
    }

    public async Task RemovePartyAsync(Guid courtCaseId, Guid partyEntryId, Guid callerUserId, CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canEdit) throw new CourtWorkflowException("Forbidden", 403);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var party = await db.CourtCaseParties.FirstOrDefaultAsync(p => p.Id == partyEntryId && p.CourtCaseId == courtCaseId && p.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Party entry not found.", 404);

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        party.RecordStatus = RecordStatus.Archived;
        party.UpdatedAt = now;
        party.UpdatedBy = actorDisplayName;

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.PartyRemoved,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            CourtCasePartyId = party.Id,
            Notes = $"Party removed: {party.DisplayName}"
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<CourtCaseRepresentativeDto> AddRepresentativeAsync(Guid courtCaseId, CreateCourtCaseRepresentativeDto dto, Guid callerUserId, CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canEdit) throw new CourtWorkflowException("Forbidden", 403);

        var displayName = dto.ResolvedDisplayName;
        if (string.IsNullOrWhiteSpace(displayName)) throw new CourtWorkflowException("DisplayName is required.", 400);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        var rep = new CourtCaseRepresentative
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            CourtCasePartyId = dto.CourtCasePartyId,
            DisplayName = displayName,
            RepresentativeType = dto.ResolvedRepresentativeType,
            RepresentsRole = dto.RepresentsRole?.Trim(),
            ContactText = dto.ContactText?.Trim(),
            Remarks = dto.Remarks?.Trim(),
            RecordStatus = RecordStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorDisplayName
        };
        db.CourtCaseRepresentatives.Add(rep);

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.RepresentativeAdded,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            CourtCaseRepresentativeId = rep.Id,
            Notes = $"Representative added: {rep.DisplayName} ({rep.RepresentativeType})"
        });

        await db.SaveChangesAsync(ct);
        return new CourtCaseRepresentativeDto(rep.Id, rep.CourtCaseId, rep.CourtCasePartyId, rep.DisplayName, rep.RepresentativeType, rep.RepresentsRole, rep.ContactText, rep.Remarks);
    }

    public async Task<CourtCaseRepresentativeDto> UpdateRepresentativeAsync(Guid courtCaseId, Guid representativeId, CreateCourtCaseRepresentativeDto dto, Guid callerUserId, CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canEdit) throw new CourtWorkflowException("Forbidden", 403);

        var displayName = dto.ResolvedDisplayName;
        if (string.IsNullOrWhiteSpace(displayName)) throw new CourtWorkflowException("DisplayName is required.", 400);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var rep = await db.CourtCaseRepresentatives.FirstOrDefaultAsync(r => r.Id == representativeId && r.CourtCaseId == courtCaseId && r.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Representative not found.", 404);

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        rep.DisplayName = displayName;
        rep.RepresentativeType = dto.ResolvedRepresentativeType;
        rep.RepresentsRole = dto.RepresentsRole?.Trim();
        rep.ContactText = dto.ContactText?.Trim();
        rep.Remarks = dto.Remarks?.Trim();
        rep.CourtCasePartyId = dto.CourtCasePartyId;
        rep.UpdatedAt = now;
        rep.UpdatedBy = actorDisplayName;

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.RepresentativeUpdated,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            CourtCaseRepresentativeId = rep.Id,
            Notes = $"Representative updated: {rep.DisplayName} ({rep.RepresentativeType})"
        });

        await db.SaveChangesAsync(ct);
        return new CourtCaseRepresentativeDto(rep.Id, rep.CourtCaseId, rep.CourtCasePartyId, rep.DisplayName, rep.RepresentativeType, rep.RepresentsRole, rep.ContactText, rep.Remarks);
    }

    public async Task RemoveRepresentativeAsync(Guid courtCaseId, Guid representativeId, Guid callerUserId, CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtCaseAsync(courtCaseId, callerUserId, ct);
        if (!canEdit) throw new CourtWorkflowException("Forbidden", 403);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var rep = await db.CourtCaseRepresentatives.FirstOrDefaultAsync(r => r.Id == representativeId && r.CourtCaseId == courtCaseId && r.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Representative not found.", 404);

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        rep.RecordStatus = RecordStatus.Archived;
        rep.UpdatedAt = now;
        rep.UpdatedBy = actorDisplayName;

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.RepresentativeRemoved,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            CourtCaseRepresentativeId = rep.Id,
            Notes = $"Representative removed: {rep.DisplayName}"
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<CourtCaseDocumentDto> UploadDocumentAsync(
        Guid courtCaseId,
        Stream stream,
        string fileName,
        string? contentType,
        string? documentRole,
        string? displayName,
        Guid? proceedingId,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var canManage = await courtAuth.CanManageDocumentsAsync(courtCaseId, callerUserId, ct);
        if (!canManage) throw new CourtWorkflowException("You do not have permission to manage court documents.", 403);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        if (proceedingId.HasValue)
        {
            var procExists = await db.CourtProceedings.AnyAsync(p => p.Id == proceedingId.Value && p.CourtCaseId == courtCaseId, ct);
            if (!procExists) throw new CourtWorkflowException("Court proceeding not found.", 404);
        }

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        var storageResult = await storage.SaveAndHashAsync(stream, fileName, ct);

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            DocumentType = "CourtCase",
            OriginalFileName = Path.GetFileName(fileName),
            StoragePath = storageResult.StoragePath,
            Sha256Hash = storageResult.Sha256Hash,
            MimeType = string.IsNullOrWhiteSpace(contentType) ? "application/pdf" : contentType,
            FileSize = storageResult.FileSize,
            UploadedAt = now,
            UploadedBy = actorDisplayName,
            RecordStatus = RecordStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Documents.Add(doc);

        var ccd = new CourtCaseDocument
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            DocumentId = doc.Id,
            CourtProceedingId = proceedingId,
            DocumentRole = string.IsNullOrWhiteSpace(documentRole) ? "General" : documentRole.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? doc.OriginalFileName : displayName.Trim(),
            RecordStatus = RecordStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorDisplayName
        };
        db.CourtCaseDocuments.Add(ccd);

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.DocumentUploaded,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            DocumentId = doc.Id,
            CourtProceedingId = proceedingId,
            Notes = $"Document uploaded: {ccd.DisplayName}"
        });

        await db.SaveChangesAsync(ct);

        return new CourtCaseDocumentDto(
            ccd.Id,
            courtCaseId,
            doc.Id,
            doc.OriginalFileName,
            doc.DocumentType,
            ccd.DocumentRole,
            ccd.DisplayName,
            ccd.CourtProceedingId,
            doc.FileSize,
            doc.MimeType,
            doc.UploadedAt
        );
    }

    public async Task<CourtCaseDocumentDto> LinkDocumentAsync(
        Guid courtCaseId,
        Guid documentId,
        string? documentRole,
        string? displayName,
        Guid? proceedingId,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var canManage = await courtAuth.CanManageDocumentsAsync(courtCaseId, callerUserId, ct);
        if (!canManage) throw new CourtWorkflowException("Forbidden", 403);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Document not found.", 404);

        if (proceedingId.HasValue)
        {
            var procExists = await db.CourtProceedings.AnyAsync(p => p.Id == proceedingId.Value && p.CourtCaseId == courtCaseId, ct);
            if (!procExists) throw new CourtWorkflowException("Court proceeding not found.", 404);
        }

        var existing = await db.CourtCaseDocuments.FirstOrDefaultAsync(x => x.CourtCaseId == courtCaseId && x.DocumentId == documentId, ct);
        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        CourtCaseDocument ccd;
        if (existing != null)
        {
            if (existing.RecordStatus == RecordStatus.Active)
            {
                return new CourtCaseDocumentDto(existing.Id, courtCaseId, doc.Id, doc.OriginalFileName, doc.DocumentType, existing.DocumentRole, existing.DisplayName, existing.CourtProceedingId, doc.FileSize, doc.MimeType, doc.UploadedAt);
            }
            existing.RecordStatus = RecordStatus.Active;
            existing.DocumentRole = string.IsNullOrWhiteSpace(documentRole) ? existing.DocumentRole : documentRole.Trim();
            existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? existing.DisplayName : displayName.Trim();
            existing.CourtProceedingId = proceedingId ?? existing.CourtProceedingId;
            existing.UpdatedAt = now;
            existing.UpdatedBy = actorDisplayName;
            ccd = existing;
        }
        else
        {
            ccd = new CourtCaseDocument
            {
                Id = Guid.NewGuid(),
                CourtCaseId = courtCaseId,
                DocumentId = doc.Id,
                CourtProceedingId = proceedingId,
                DocumentRole = string.IsNullOrWhiteSpace(documentRole) ? "Linked" : documentRole.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? doc.OriginalFileName : displayName.Trim(),
                RecordStatus = RecordStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = actorDisplayName
            };
            db.CourtCaseDocuments.Add(ccd);
        }

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.DocumentLinked,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            DocumentId = doc.Id,
            CourtProceedingId = proceedingId,
            Notes = $"Document linked: {ccd.DisplayName}"
        });

        await db.SaveChangesAsync(ct);

        return new CourtCaseDocumentDto(
            ccd.Id,
            courtCaseId,
            doc.Id,
            doc.OriginalFileName,
            doc.DocumentType,
            ccd.DocumentRole,
            ccd.DisplayName,
            ccd.CourtProceedingId,
            doc.FileSize,
            doc.MimeType,
            doc.UploadedAt
        );
    }

    public async Task UnlinkDocumentAsync(Guid courtCaseId, Guid documentLinkId, Guid callerUserId, CancellationToken ct = default)
    {
        var canManage = await courtAuth.CanManageDocumentsAsync(courtCaseId, callerUserId, ct);
        if (!canManage) throw new CourtWorkflowException("Forbidden", 403);

        var courtCase = await db.CourtCases.FirstOrDefaultAsync(c => c.Id == courtCaseId && c.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Court case not found.", 404);

        var link = await db.CourtCaseDocuments.FirstOrDefaultAsync(d => d.Id == documentLinkId && d.CourtCaseId == courtCaseId && d.RecordStatus == RecordStatus.Active, ct)
            ?? throw new CourtWorkflowException("Document link not found.", 404);

        var (_, actorDisplayName, actorDesignation) = await ResolveActorAsync(callerUserId, ct);
        var now = DateTimeOffset.UtcNow;

        link.RecordStatus = RecordStatus.Archived;
        link.UpdatedAt = now;
        link.UpdatedBy = actorDisplayName;

        var nextSeq = await db.CourtCaseEvents.Where(e => e.CourtCaseId == courtCaseId).MaxAsync(e => (int?)e.SequenceNumber, ct) ?? 0;
        nextSeq++;

        db.CourtCaseEvents.Add(new CourtCaseEvent
        {
            Id = Guid.NewGuid(),
            CourtCaseId = courtCaseId,
            SequenceNumber = nextSeq,
            Action = CourtCaseAction.DocumentUnlinked,
            ActionAt = now,
            ActorUserId = callerUserId,
            ActorDisplayNameSnapshot = actorDisplayName,
            ActorDesignationSnapshot = actorDesignation,
            CaseNumberSnapshot = courtCase.CaseNumber,
            CaseTitleSnapshot = courtCase.CaseTitle,
            DocumentId = link.DocumentId,
            Notes = $"Document unlinked: {link.DisplayName}"
        });

        await db.SaveChangesAsync(ct);
    }
}
