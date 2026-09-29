namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed record CourtImportDecisionRequest(
    CourtImportResolutionAction Action,
    Guid? ResolvedCourtCaseId = null,
    string? ApprovedCaseNumber = null,
    string? ApprovedCaseTitle = null,
    string? ApprovedCourtName = null,
    string? ApprovedStatus = null,
    bool ApplyStatusToExisting = false,
    CourtImportNdohAction? NdohAction = null,
    string? ReviewerNotes = null);

public sealed record CourtImportReviewSummary(
    int Unresolved, int Ready, int Committed, int Skipped, int Failed, int SafeBulkCandidates,
    int RetryableSafe);

public sealed record CourtImportCommitResult(
    CourtImportReviewSummary Summary, int CommittedThisRun, IReadOnlyList<string> Failures);

public interface ICourtImportReviewService
{
    Task<CourtImportReviewSummary> SummaryAsync(Guid batchId, Guid userId, CancellationToken ct = default);
    Task<CourtImportReviewSummary> DecideAsync(Guid batchId, Guid rowId, CourtImportDecisionRequest request, Guid userId, CancellationToken ct = default);
    Task<CourtImportReviewSummary> ClearDecisionAsync(Guid batchId, Guid rowId, Guid userId, CancellationToken ct = default);
    Task<CourtImportReviewSummary> ApproveSafeAsync(Guid batchId, Guid userId, CancellationToken ct = default);
    Task<CourtImportCommitResult> AddReadyAsync(Guid batchId, Guid userId, CancellationToken ct = default);
    Task<CourtImportCommitResult> CommitAsync(Guid batchId, Guid userId, CancellationToken ct = default);
}

public sealed class CourtImportReviewService(
    LacDbContext db,
    ICourtAuthorizationService authorization,
    ICourtWorkflowService workflow) : ICourtImportReviewService
{
    private async Task<(string Name, bool CanCreate, bool CanEdit)> AuthorizeAsync(Guid userId, CancellationToken ct)
    {
        if (!await authorization.CanViewCourtReferencesAsync(userId, ct))
            throw new CourtWorkflowException("Global or Court-workstream access is required.", 403);
        var canCreate = await authorization.CanCreateCourtCaseAsync(userId, ct);
        var canEdit = await authorization.CanEditCourtReferencesAsync(userId, ct);
        if (!canCreate && !canEdit)
            throw new CourtWorkflowException("Court create or edit permission is required.", 403);
        var name = await db.AppUsers.AsNoTracking().Where(x => x.Id == userId && x.IsActive && x.RecordStatus == RecordStatus.Active)
            .Select(x => x.DisplayName).SingleOrDefaultAsync(ct)
            ?? throw new CourtWorkflowException("Active officer not found.", 401);
        return (name, canCreate, canEdit);
    }

    private static void RequirePermissions(CourtImportResolutionAction action, bool canCreate, bool canEdit)
    {
        if (action == CourtImportResolutionAction.ImportAsNewCase && (!canCreate || !canEdit))
            throw new CourtWorkflowException("Court create and edit permissions are required for reviewed new-case import.", 403);
        if (action == CourtImportResolutionAction.LinkToExistingCase && !canEdit)
            throw new CourtWorkflowException("Court edit permission is required for existing-case import.", 403);
    }

    private async Task<CourtImportBatch> ParsedBatchAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await db.CourtImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == batchId, ct)
            ?? throw new CourtWorkflowException("Import batch not found.", 404);
        if (batch.Status != CourtImportBatchStatus.Parsed)
            throw new CourtWorkflowException("Only parsed batches can be reviewed.", 409);
        return batch;
    }

    public async Task<CourtImportReviewSummary> SummaryAsync(Guid batchId, Guid userId, CancellationToken ct = default)
    {
        await AuthorizeAsync(userId, ct);
        await ParsedBatchAsync(batchId, ct);
        return await BuildSummaryAsync(batchId, ct);
    }

    private async Task<CourtImportReviewSummary> BuildSummaryAsync(Guid batchId, CancellationToken ct)
    {
        var rows = await db.CourtImportRows.AsNoTracking().Where(x => x.BatchId == batchId)
            .Select(x => new { x.ResolutionAction, x.CommitStatus, x.RowStatus, x.IdentityKey,
                x.ReviewerNotes, x.RawCaseNumber, x.ApprovedCaseNumber, x.ApprovedCourtName, x.ApprovedStatus,
                x.SuggestedStatusClass, x.SuggestedCourtName, x.ValidationIssuesJson, x.CandidateCourtCaseId }).ToListAsync(ct);
        return new CourtImportReviewSummary(
            rows.Count(x => x.ResolutionAction == null),
            rows.Count(x => x.ResolutionAction is CourtImportResolutionAction.ImportAsNewCase or CourtImportResolutionAction.LinkToExistingCase
                            && x.CommitStatus == CourtImportCommitStatus.NotCommitted),
            rows.Count(x => x.CommitStatus == CourtImportCommitStatus.Committed),
            rows.Count(x => x.ResolutionAction == CourtImportResolutionAction.Skip),
            rows.Count(x => x.CommitStatus == CourtImportCommitStatus.Failed),
            rows.Count(x => x.ResolutionAction == null && IsSafe(x.RowStatus, x.IdentityKey, x.SuggestedStatusClass,
                x.SuggestedCourtName, x.ValidationIssuesJson, x.CandidateCourtCaseId)),
            rows.Count(x => x.CommitStatus == CourtImportCommitStatus.Failed &&
                x.ResolutionAction == CourtImportResolutionAction.ImportAsNewCase &&
                x.ReviewerNotes == "Safe deterministic candidate bulk-approved" &&
                IsSafe(x.RowStatus, x.IdentityKey, x.SuggestedStatusClass,
                    x.SuggestedCourtName, x.ValidationIssuesJson, x.CandidateCourtCaseId) &&
                Trim(x.ApprovedCaseNumber) == Trim(x.RawCaseNumber) &&
                Trim(x.ApprovedCourtName) == Trim(x.SuggestedCourtName) &&
                x.ApprovedStatus == x.SuggestedStatusClass!.Value.ToString()));
    }

    private static bool IsSafe(CourtImportRowStatus status, string? identity,
        CourtImportStatusClass? statusClass, string? suggestedCourtName, string issues, Guid? candidateId) =>
        status == CourtImportRowStatus.NewCandidate &&
        identity != null &&
        statusClass is CourtImportStatusClass.Pending or CourtImportStatusClass.Disposed &&
        CourtImportService.IsApprovedCanonicalCourtName(suggestedCourtName) &&
        issues == "[]" &&
        candidateId == null;

    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static string CanonicalStatus(CourtImportRow row, string? approved) =>
        Trim(approved) switch
        {
            "Pending" => "Pending",
            "Disposed" => "Disposed",
            null when row.SuggestedStatusClass == CourtImportStatusClass.Pending => "Pending",
            null when row.SuggestedStatusClass == CourtImportStatusClass.Disposed => "Disposed",
            _ => throw new CourtWorkflowException("Explicit canonical Pending or Disposed status is required.", 400)
        };

    public async Task<CourtImportReviewSummary> DecideAsync(
        Guid batchId, Guid rowId, CourtImportDecisionRequest request, Guid userId, CancellationToken ct = default)
    {
        var (name, canCreate, canEdit) = await AuthorizeAsync(userId, ct);
        await ParsedBatchAsync(batchId, ct);
        if (!Enum.IsDefined(request.Action))
            throw new CourtWorkflowException("Unknown import resolution action.", 400);
        RequirePermissions(request.Action, canCreate, canEdit);
        var row = await db.CourtImportRows.SingleOrDefaultAsync(x => x.Id == rowId && x.BatchId == batchId, ct)
            ?? throw new CourtWorkflowException("Import row not found.", 404);
        if (row.CommitStatus == CourtImportCommitStatus.Committed)
            throw new CourtWorkflowException("A committed row cannot be reopened.", 409);
        if (row.RowStatus == CourtImportRowStatus.Invalid && request.Action != CourtImportResolutionAction.Skip)
            throw new CourtWorkflowException("Invalid source row cannot be committed until corrected.", 400);
        if (row.RowStatus != CourtImportRowStatus.NewCandidate &&
            request.Action != CourtImportResolutionAction.Skip && string.IsNullOrWhiteSpace(request.ReviewerNotes))
            throw new CourtWorkflowException("Reviewer notes are required for a non-safe row.", 400);

        row.ResolutionAction = request.Action;
        row.ResolvedCourtCaseId = null;
        row.ApprovedCaseNumber = null;
        row.ApprovedCaseTitle = null;
        row.ApprovedCourtName = null;
        row.ApprovedStatus = null;
        row.ApplyStatusToExisting = false;
        row.NdohAction = null;
        if (request.Action == CourtImportResolutionAction.ImportAsNewCase)
        {
            row.ApprovedCaseNumber = Trim(request.ApprovedCaseNumber) ?? Trim(row.RawCaseNumber)
                ?? throw new CourtWorkflowException("Approved case number is required.", 400);
            row.ApprovedCaseTitle = Trim(request.ApprovedCaseTitle) ?? Trim(row.RawCaseTitle);
            var approvedCourt = Trim(request.ApprovedCourtName) ?? Trim(row.SuggestedCourtName)
                ?? throw new CourtWorkflowException("Approved court name is required.", 400);
            row.ApprovedCourtName = CourtImportService.CanonicalCourtName(approvedCourt) ?? approvedCourt;
            row.ApprovedStatus = CanonicalStatus(row, request.ApprovedStatus);
            row.NdohAction = row.ParsedNdoh.HasValue ? CourtImportNdohAction.UseImported : null;
        }
        else if (request.Action == CourtImportResolutionAction.LinkToExistingCase)
        {
            if (!request.ResolvedCourtCaseId.HasValue)
                throw new CourtWorkflowException("Choose an existing CourtCase explicitly.", 400);
            var existing = await db.CourtCases.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == request.ResolvedCourtCaseId && x.RecordStatus == RecordStatus.Active, ct)
                ?? throw new CourtWorkflowException("Selected CourtCase not found.", 404);
            row.ResolvedCourtCaseId = existing.Id;
            row.ApplyStatusToExisting = request.ApplyStatusToExisting;
            row.ApprovedStatus = request.ApplyStatusToExisting
                ? CanonicalStatus(row, request.ApprovedStatus)
                : null;
            if (row.ParsedNdoh.HasValue)
            {
                row.NdohAction = request.NdohAction
                    ?? throw new CourtWorkflowException("Choose whether to retain existing NDOH or use the imported date.", 400);
                if (!Enum.IsDefined(row.NdohAction.Value))
                    throw new CourtWorkflowException("Unknown NDOH decision.", 400);
                if (row.NdohAction == CourtImportNdohAction.UseImported)
                {
                    var latest = await LatestProceedingAsync(existing.Id, ct);
                    if (latest?.ProceedingDate != null && latest.NextDate != row.ParsedNdoh)
                        throw new CourtWorkflowException("A dated proceeding is authoritative. Its NDOH cannot be replaced by an undated register import.", 409);
                }
            }
        }
        row.ReviewerNotes = Trim(request.ReviewerNotes);
        row.ReviewedByUserId = userId;
        row.ReviewedByDisplayNameSnapshot = name;
        row.ReviewedAt = DateTimeOffset.UtcNow;
        row.CommitStatus = CourtImportCommitStatus.NotCommitted;
        row.CommitError = null;
        await db.SaveChangesAsync(ct);
        return await BuildSummaryAsync(batchId, ct);
    }

    public async Task<CourtImportReviewSummary> ClearDecisionAsync(Guid batchId, Guid rowId, Guid userId, CancellationToken ct = default)
    {
        await AuthorizeAsync(userId, ct);
        await ParsedBatchAsync(batchId, ct);
        var row = await db.CourtImportRows.SingleOrDefaultAsync(x => x.BatchId == batchId && x.Id == rowId, ct)
            ?? throw new CourtWorkflowException("Import row not found.", 404);
        if (row.CommitStatus == CourtImportCommitStatus.Committed)
            throw new CourtWorkflowException("Committed rows cannot be reopened.", 409);
        row.ResolutionAction = null;
        row.ResolvedCourtCaseId = null;
        row.ApprovedCaseNumber = null;
        row.ApprovedCaseTitle = null;
        row.ApprovedCourtName = null;
        row.ApprovedStatus = null;
        row.ApplyStatusToExisting = false;
        row.NdohAction = null;
        row.ReviewerNotes = null;
        row.ReviewedByUserId = null;
        row.ReviewedByDisplayNameSnapshot = null;
        row.ReviewedAt = null;
        row.CommitStatus = CourtImportCommitStatus.NotCommitted;
        row.CommitError = null;
        await db.SaveChangesAsync(ct);
        return await BuildSummaryAsync(batchId, ct);
    }

    public async Task<CourtImportReviewSummary> ApproveSafeAsync(Guid batchId, Guid userId, CancellationToken ct = default)
    {
        var (name, canCreate, canEdit) = await AuthorizeAsync(userId, ct);
        if (!canCreate || !canEdit)
            throw new CourtWorkflowException("Court create and edit permissions are required for bulk approval.", 403);
        await ParsedBatchAsync(batchId, ct);
        var rows = await db.CourtImportRows.Where(x => x.BatchId == batchId &&
            x.ResolutionAction == null && x.RowStatus == CourtImportRowStatus.NewCandidate &&
            x.IdentityKey != null && x.CandidateCourtCaseId == null &&
            x.ValidationIssuesJson == "[]" &&
            (x.SuggestedStatusClass == CourtImportStatusClass.Pending ||
             x.SuggestedStatusClass == CourtImportStatusClass.Disposed)).ToListAsync(ct);
        // Re-check in memory so rows staged before this alias fix cannot bulk-promote
        // an arbitrary nonblank SuggestedCourtName into a canonical CourtCase.
        rows = rows.Where(x => CourtImportService.IsApprovedCanonicalCourtName(x.SuggestedCourtName)).ToList();
        foreach (var row in rows)
        {
            row.ResolutionAction = CourtImportResolutionAction.ImportAsNewCase;
            row.ApprovedCaseNumber = Trim(row.RawCaseNumber);
            row.ApprovedCaseTitle = Trim(row.RawCaseTitle);
            row.ApprovedCourtName = Trim(row.SuggestedCourtName);
            row.ApprovedStatus = row.SuggestedStatusClass!.Value.ToString();
            row.NdohAction = row.ParsedNdoh.HasValue ? CourtImportNdohAction.UseImported : null;
            row.ReviewedByUserId = userId;
            row.ReviewedByDisplayNameSnapshot = name;
            row.ReviewedAt = DateTimeOffset.UtcNow;
            row.ReviewerNotes = "Safe deterministic candidate bulk-approved";
        }
        await db.SaveChangesAsync(ct);
        return await BuildSummaryAsync(batchId, ct);
    }

    private async Task<CourtProceeding?> LatestProceedingAsync(Guid caseId, CancellationToken ct) =>
        await db.CourtProceedings.AsNoTracking()
            .Where(p => p.CourtCaseId == caseId && p.RecordStatus == RecordStatus.Active)
            .OrderByDescending(p => p.ProceedingDate.HasValue)
            .ThenByDescending(p => p.ProceedingDate)
            .ThenByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<CourtImportCommitResult> CommitAsync(Guid batchId, Guid userId, CancellationToken ct = default)
    {
        var (_, canCreate, canEdit) = await AuthorizeAsync(userId, ct);
        await ParsedBatchAsync(batchId, ct);
        var pendingActions = await db.CourtImportRows.AsNoTracking()
            .Where(x => x.BatchId == batchId && x.CommitStatus != CourtImportCommitStatus.Committed &&
                        (x.ResolutionAction == CourtImportResolutionAction.ImportAsNewCase ||
                         x.ResolutionAction == CourtImportResolutionAction.LinkToExistingCase))
            .Select(x => x.ResolutionAction).Distinct().ToListAsync(ct);
        foreach (var action in pendingActions)
            if (action.HasValue) RequirePermissions(action.Value, canCreate, canEdit);
        var rowIds = await db.CourtImportRows.AsNoTracking()
            .Where(x => x.BatchId == batchId &&
                        (x.ResolutionAction == CourtImportResolutionAction.ImportAsNewCase ||
                         x.ResolutionAction == CourtImportResolutionAction.LinkToExistingCase) &&
                        x.CommitStatus != CourtImportCommitStatus.Committed)
            .OrderBy(x => x.SourceRowNumber).Select(x => x.Id).ToListAsync(ct);
        return await CommitRowsAsync(batchId, userId, canCreate, canEdit, rowIds, ct);
    }

    public async Task<CourtImportCommitResult> AddReadyAsync(Guid batchId, Guid userId, CancellationToken ct = default)
    {
        var (_, canCreate, canEdit) = await AuthorizeAsync(userId, ct);
        if (!canCreate || !canEdit)
            throw new CourtWorkflowException("Court create and edit permissions are required.", 403);
        await ApproveSafeAsync(batchId, userId, ct);
        // This single officer action may write only the deterministic rows promoted by
        // the safe rule. Explicitly reviewed ambiguous rows remain a separate decision.
        var safeRows = await db.CourtImportRows.AsNoTracking()
            .Where(x => x.BatchId == batchId && x.RowStatus == CourtImportRowStatus.NewCandidate &&
                x.ReviewerNotes == "Safe deterministic candidate bulk-approved" &&
                x.ResolutionAction == CourtImportResolutionAction.ImportAsNewCase &&
                x.CommitStatus != CourtImportCommitStatus.Committed && x.IdentityKey != null &&
                x.CandidateCourtCaseId == null && x.ValidationIssuesJson == "[]" &&
                (x.SuggestedStatusClass == CourtImportStatusClass.Pending ||
                 x.SuggestedStatusClass == CourtImportStatusClass.Disposed))
            .OrderBy(x => x.SourceRowNumber).ToListAsync(ct);
        var rowIds = safeRows.Where(x => CourtImportService.IsApprovedCanonicalCourtName(x.SuggestedCourtName) &&
            Trim(x.ApprovedCourtName) == Trim(x.SuggestedCourtName) &&
            Trim(x.ApprovedCaseNumber) == Trim(x.RawCaseNumber) &&
            x.ApprovedStatus == x.SuggestedStatusClass!.Value.ToString())
            .Select(x => x.Id).ToList();
        return await CommitRowsAsync(batchId, userId, canCreate, canEdit, rowIds, ct);
    }

    private async Task<CourtImportCommitResult> CommitRowsAsync(Guid batchId, Guid userId,
        bool canCreate, bool canEdit, IReadOnlyList<Guid> rowIds, CancellationToken ct)
    {
        var committed = 0;
        var failures = new List<string>();
        foreach (var rowId in rowIds)
        {
            db.ChangeTracker.Clear();
            try
            {
                if (await CommitOneAsync(batchId, rowId, userId, canCreate, canEdit, ct)) committed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                var row = await db.CourtImportRows.SingleAsync(x => x.Id == rowId && x.BatchId == batchId, ct);
                if (row.CommitStatus != CourtImportCommitStatus.Committed)
                {
                    row.CommitStatus = CourtImportCommitStatus.Failed;
                    row.CommitError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                    await db.SaveChangesAsync(ct);
                }
                failures.Add($"Source row {row.SourceRowNumber}: {ex.Message}");
            }
        }
        return new CourtImportCommitResult(await BuildSummaryAsync(batchId, ct), committed, failures);
    }

    private async Task<bool> CommitOneAsync(Guid batchId, Guid rowId, Guid userId, bool canCreate, bool canEdit, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(ct) : null;
            try
            {
                db.ChangeTracker.Clear();
                var row = db.Database.IsRelational()
                    ? await db.CourtImportRows.FromSqlInterpolated(
                        $"SELECT * FROM \"CourtImportRows\" WHERE \"Id\" = {rowId} FOR UPDATE").SingleAsync(ct)
                    : await db.CourtImportRows.SingleAsync(x => x.Id == rowId, ct);
                if (row.BatchId != batchId) throw new CourtWorkflowException("Import row does not belong to batch.", 404);
                if (row.CommitStatus == CourtImportCommitStatus.Committed) return false;
                var action = row.ResolutionAction
                    ?? throw new CourtWorkflowException("Row has no approved decision.", 409);
                RequirePermissions(action, canCreate, canEdit);
                if (action == CourtImportResolutionAction.Skip || row.RowStatus == CourtImportRowStatus.Invalid)
                    throw new CourtWorkflowException("Skipped or invalid rows cannot commit.", 409);
                if (!row.ReviewedAt.HasValue || !row.ReviewedByUserId.HasValue)
                    throw new CourtWorkflowException("Officer approval is required.", 409);
                var actor = await db.AppUsers.AsNoTracking().Where(x => x.Id == userId)
                    .Select(x => new { x.DisplayName, Designation = x.Designation != null ? x.Designation.Name : null })
                    .SingleAsync(ct);
                Guid caseId;
                if (action == CourtImportResolutionAction.ImportAsNewCase)
                {
                    if (string.IsNullOrWhiteSpace(row.ApprovedCaseNumber) ||
                        string.IsNullOrWhiteSpace(row.ApprovedCourtName) ||
                        row.ApprovedStatus is not ("Pending" or "Disposed"))
                        throw new CourtWorkflowException("Reviewed new-case mapping is incomplete.", 409);
                    var existing = await db.CourtCases.AsNoTracking().Where(x => x.RecordStatus == RecordStatus.Active)
                        .Select(x => new { x.CourtName, x.CaseNumber }).ToListAsync(ct);
                    var approvedIdentity = CourtImportService.Identity(row.ApprovedCourtName, row.ApprovedCaseNumber);
                    if (existing.Any(x =>
                            (approvedIdentity != null && CourtImportService.Identity(x.CourtName, x.CaseNumber) == approvedIdentity) ||
                            (string.Equals(x.CourtName.Trim(), row.ApprovedCourtName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(x.CaseNumber.Trim(), row.ApprovedCaseNumber.Trim(), StringComparison.OrdinalIgnoreCase))))
                        throw new CourtWorkflowException("An active case now matches this reference. Review as LinkToExistingCase.", 409);
                    var reps = string.IsNullOrWhiteSpace(row.RawAdvocate) ? null
                        : new[] { new CreateCourtCaseRepresentativeDto(DisplayName: row.RawAdvocate.Trim(), RepresentativeType: "Counsel") };
                    caseId = await workflow.CreateCourtCaseAsync(new CreateCourtCaseCommand(
                        CaseNumber: row.ApprovedCaseNumber, CourtName: row.ApprovedCourtName,
                        CaseTitle: row.ApprovedCaseTitle, CaseType: row.SuggestedCaseType,
                        CurrentStatus: row.ApprovedStatus, Representatives: reps), userId, ct);
                }
                else
                {
                    caseId = row.ResolvedCourtCaseId
                        ?? throw new CourtWorkflowException("Explicit existing CourtCase selection is required.", 409);
                    var existing = await db.CourtCases.AsNoTracking().SingleOrDefaultAsync(
                        x => x.Id == caseId && x.RecordStatus == RecordStatus.Active, ct)
                        ?? throw new CourtWorkflowException("Selected CourtCase is unavailable.", 409);
                    if (row.ApplyStatusToExisting)
                    {
                        if (row.ApprovedStatus is not ("Pending" or "Disposed"))
                            throw new CourtWorkflowException("Approved status is missing.", 409);
                        await workflow.UpdateMetadataAsync(caseId, new UpdateCourtCaseMetadataCommand(
                            existing.CaseNumber, existing.CourtName, existing.CaseTitle, existing.CaseType,
                            existing.FiledDate, row.ApprovedStatus, existing.DisposedDate, existing.Remarks,
                            existing.Revision), userId, ct);
                    }
                }
                Guid? proceedingId = null;
                if (row.ParsedNdoh.HasValue && row.NdohAction == CourtImportNdohAction.UseImported)
                {
                    var latest = await LatestProceedingAsync(caseId, ct);
                    if (latest?.NextDate != row.ParsedNdoh)
                    {
                        if (latest?.ProceedingDate != null)
                            throw new CourtWorkflowException("A dated proceeding is authoritative; legacy NDOH cannot supersede it.", 409);
                        var revision = await db.CourtCases.AsNoTracking().Where(x => x.Id == caseId)
                            .Select(x => x.Revision).SingleAsync(ct);
                        var proceeding = await workflow.RecordProceedingAsync(caseId,
                            new RecordCourtProceedingCommand(
                                ProceedingDate: null, OrderType: "Legacy office-register NDOH",
                                RestraintNature: null, Summary: null, NextDate: row.ParsedNdoh,
                                ExpectedRevision: revision, SourceKind: "LegacyRegisterNDOH"),
                            userId, ct);
                        proceedingId = proceeding.Id;
                    }
                }
                db.ChangeTracker.Clear();
                var courtCase = await db.CourtCases.AsNoTracking().SingleAsync(x => x.Id == caseId, ct);
                var sequence = (await db.CourtCaseEvents.Where(x => x.CourtCaseId == caseId)
                    .MaxAsync(x => (int?)x.SequenceNumber, ct) ?? 0) + 1;
                var courtWorkstream = await db.Workstreams.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Code == WorkstreamCodes.CourtReferences, ct);
                db.CourtCaseEvents.Add(new CourtCaseEvent
                {
                    CourtCaseId = caseId, SequenceNumber = sequence, Action = CourtCaseAction.ImportApplied,
                    ActorUserId = userId, ActorDisplayNameSnapshot = actor.DisplayName,
                    ActorDesignationSnapshot = actor.Designation,
                    CaseNumberSnapshot = courtCase.CaseNumber, CaseTitleSnapshot = courtCase.CaseTitle,
                    WorkstreamIdSnapshot = courtWorkstream?.Id,
                    WorkstreamNameSnapshot = courtWorkstream?.Name ?? "Court References",
                    CourtImportBatchId = batchId, CourtImportRowId = rowId,
                    CourtProceedingId = proceedingId,
                    Notes = action == CourtImportResolutionAction.ImportAsNewCase
                        ? "Reviewed office-register row imported as new case"
                        : "Reviewed office-register row linked to existing case"
                });
                var committedRow = await db.CourtImportRows.SingleAsync(x => x.Id == rowId, ct);
                committedRow.CommitStatus = CourtImportCommitStatus.Committed;
                committedRow.CommittedCourtCaseId = caseId;
                committedRow.CommittedProceedingId = proceedingId;
                committedRow.CommittedAt = DateTimeOffset.UtcNow;
                committedRow.CommitError = null;
                await db.SaveChangesAsync(ct);
                if (transaction != null) await transaction.CommitAsync(ct);
                return true;
            }
            catch
            {
                if (transaction != null) await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
}
