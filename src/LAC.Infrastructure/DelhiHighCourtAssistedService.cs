using System.Text.RegularExpressions;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed record DhcAssistedPreviewCase(Guid CourtCaseId, string CaseNumber,
    DateOnly? OperationalNdoh, string Reason, bool IdentityNeedsReview);
public sealed record DhcAssistedPreview(int RecommendedCount, int NoNdohCount,
    int OverdueCount, int ReviewCount, int SkippedIdentityCount,
    IReadOnlyList<DhcAssistedPreviewCase> Cases);
public sealed record DhcAssistedStartRequest(string Scope, IReadOnlyList<Guid>? CaseIds);
public sealed record DhcAssistedResult(DhcAssistedSyncRun Run,
    IReadOnlyList<DhcAssistedSyncItem> Items, string? OwnerName);

public sealed class DelhiHighCourtAssistedService(
    LacDbContext db, ICourtAuthorizationService authorization, IOfficeClock clock,
    ICourtWorkflowService? workflow = null)
{
    public async Task RequireOperatorAsync(Guid userId, CancellationToken ct)
    {
        if (!await authorization.CanViewCourtReferencesAsync(userId, ct) ||
            !await authorization.CanEditCourtReferencesAsync(userId, ct))
            throw new CourtWorkflowException("Global or Court-workstream Court view and edit access is required.", 403);
    }

    public async Task<DhcAssistedPreview> PreviewAsync(Guid userId, CancellationToken ct)
    {
        await RequireOperatorAsync(userId, ct);
        var today = clock.GetCurrentDate();
        var cases = await CourtOperationalNdohQuery.Resolve(db.CourtCases.AsNoTracking()
                .Where(x => x.RecordStatus == RecordStatus.Active && x.CourtName == "Delhi High Court" &&
                    x.CurrentStatus != null && x.CurrentStatus.Trim().ToLower() == "pending"), db, today)
            .Select(x => new { x.Case.Id, x.Case.CaseNumber, x.OperationalNdoh }).ToListAsync(ct);
        var ids = cases.Select(x => x.Id).ToList();
        var reviewIds = await db.CourtExternalListingObservations.AsNoTracking()
            .Where(x => x.CourtCaseId != null && ids.Contains(x.CourtCaseId.Value) &&
                x.Status == CourtExternalListingStatus.NeedsReview)
            .Select(x => x.CourtCaseId!.Value).Distinct().ToListAsync(ct);
        var reviewSet = reviewIds.ToHashSet();
        var result = cases.Select(x =>
        {
            var no = x.OperationalNdoh == null;
            var overdue = x.OperationalNdoh < today;
            var review = reviewSet.Contains(x.Id);
            var reason = string.Join(", ", new[] { no ? "No NDOH" : null,
                overdue ? "Overdue" : null, review ? "Cause-list review" : null }.Where(x => x != null));
            var invalid = CourtImportService.Identity("Delhi High Court", x.CaseNumber) == null;
            return new DhcAssistedPreviewCase(x.Id, x.CaseNumber, x.OperationalNdoh, reason, invalid);
        }).OrderBy(x => x.CaseNumber).ToList();
        return new(result.Count(x => x.Reason.Length > 0 && !x.IdentityNeedsReview),
            result.Count(x => x.Reason.Contains("No NDOH", StringComparison.Ordinal)),
            result.Count(x => x.Reason.Contains("Overdue", StringComparison.Ordinal)),
            result.Count(x => x.Reason.Contains("Cause-list review", StringComparison.Ordinal)),
            result.Count(x => x.IdentityNeedsReview), result);
    }

    public async Task<DhcAssistedSyncRun> CreateRunAsync(Guid userId, DhcAssistedStartRequest request, CancellationToken ct)
    {
        var preview = await PreviewAsync(userId, ct);
        var today = clock.GetCurrentDate();
        var selected = request.Scope switch
        {
            "NoNdoh" => preview.Cases.Where(x => x.OperationalNdoh == null),
            "Overdue" => preview.Cases.Where(x => x.OperationalNdoh < today),
            "Selected" => preview.Cases.Where(x => request.CaseIds?.Contains(x.CourtCaseId) == true),
            "Recommended" => preview.Cases.Where(x => x.Reason.Length > 0),
            _ => throw new CourtWorkflowException("Choose a supported assisted queue scope.", 400)
        };
        var queue = selected.Where(x => !x.IdentityNeedsReview).ToList();
        if (request.Scope == "Selected" && (request.CaseIds == null ||
            request.CaseIds.Distinct().Count() != queue.Count))
            throw new CourtWorkflowException("Selected cases must be active, pending, exact Delhi High Court identities.", 400);
        if (queue.Count == 0) throw new CourtWorkflowException("No eligible Delhi High Court cases in this queue.", 400);
        if (queue.Count > 100)
            throw new CourtWorkflowException("Assisted verification is limited to 100 cases per run. Narrow the queue or select up to 100 cases.", 400);
        var now = clock.GetUtcNow();
        var run = new DhcAssistedSyncRun
        {
            StartedByUserId = userId, StartedAt = now, LastActivityAt = now,
            TotalCases = queue.Count, CaptchaChallenges = 1
        };
        foreach (var (item, index) in queue.Select((x, i) => (x, i)))
        {
            var courtCase = await db.CourtCases.AsNoTracking().SingleAsync(x => x.Id == item.CourtCaseId, ct);
            var identity = CourtImportService.Identity(courtCase.CourtName, courtCase.CaseNumber);
            if (courtCase.RecordStatus != RecordStatus.Active || courtCase.CourtName != "Delhi High Court" ||
                !string.Equals(courtCase.CurrentStatus?.Trim(), "Pending", StringComparison.OrdinalIgnoreCase) ||
                identity == null || identity.Length > 512)
                throw new CourtWorkflowException("Selected cases must still be active, pending, exact Delhi High Court identities.", 409);
            run.Items.Add(new DhcAssistedSyncItem
            {
                CourtCaseId = item.CourtCaseId, QueueOrder = index, Reason = item.Reason,
                NormalizedCaseIdentity = identity
            });
        }
        db.DhcAssistedSyncRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    public async Task<DhcAssistedResult> GetRunAsync(Guid runId, Guid userId, CancellationToken ct)
    {
        if (!await authorization.CanViewCourtReferencesAsync(userId, ct))
            throw new CourtWorkflowException("Court view access is required.", 403);
        var run = await db.DhcAssistedSyncRuns.AsNoTracking()
            .Include(x => x.StartedByUser).Include(x => x.Items).ThenInclude(x => x.CourtCase)
            .SingleOrDefaultAsync(x => x.Id == runId, ct) ?? throw new CourtWorkflowException("Assisted run not found.", 404);
        return new(run, run.Items.OrderBy(x => x.QueueOrder).ToList(), run.StartedByUser.DisplayName);
    }

    public async Task<Guid?> RecoverableRunIdAsync(Guid userId, CancellationToken ct)
    {
        return await db.DhcAssistedSyncRuns.AsNoTracking()
            .Where(x => x.StartedByUserId == userId &&
                (x.Status == DhcAssistedRunStatus.Interrupted ||
                 x.Status == DhcAssistedRunStatus.Failed ||
                 x.Status == DhcAssistedRunStatus.ReadyForOrders))
            .OrderByDescending(x => x.StartedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<CourtExternalCaseStatusObservation>> StatusObservationsAsync(
        Guid caseId, Guid userId, CancellationToken ct)
    {
        if (!await authorization.CanViewCourtCaseAsync(caseId, userId, ct))
            throw new CourtWorkflowException("Court case view access is required.", 403);
        return await db.CourtExternalCaseStatusObservations.AsNoTracking()
            .Where(x => x.CourtCaseId == caseId).OrderByDescending(x => x.ObservedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CourtExternalOrderObservation>> OrderObservationsAsync(
        Guid caseId, Guid userId, CancellationToken ct)
    {
        if (!await authorization.CanViewCourtCaseAsync(caseId, userId, ct))
            throw new CourtWorkflowException("Court case view access is required.", 403);
        return await db.CourtExternalOrderObservations.AsNoTracking()
            .Where(x => x.CourtCaseId == caseId)
            .OrderByDescending(x => x.OrderDate).ThenByDescending(x => x.UploadDate).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CourtExternalCaseStatusObservation>> ReviewsAsync(
        Guid userId, CancellationToken ct)
    {
        await RequireOperatorAsync(userId, ct);
        return await db.CourtExternalCaseStatusObservations.AsNoTracking()
            .Include(x => x.CourtCase)
            .Where(x => x.Status == DhcAssistedEvidenceStatus.NeedsReview)
            .OrderByDescending(x => x.ObservedAt).Take(200).ToListAsync(ct);
    }

    public async Task ReviewAsync(Guid observationId, bool accept, string reason,
        Guid userId, CancellationToken ct)
    {
        await RequireOperatorAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(reason))
            throw new CourtWorkflowException("A review reason is required.", 400);
        var chosen = await db.CourtExternalCaseStatusObservations
            .Include(x => x.CourtCase).SingleOrDefaultAsync(x => x.Id == observationId, ct)
            ?? throw new CourtWorkflowException("Official observation not found.", 404);
        if (chosen.Status != DhcAssistedEvidenceStatus.NeedsReview)
            throw new CourtWorkflowException("Only pending official evidence may be reviewed.", 409);
        if (accept)
        {
            var identity = CourtImportService.Identity(chosen.CourtCase.CourtName,
                chosen.CourtCase.CaseNumber);
            if (identity != chosen.NormalizedCaseIdentity ||
                !ContainsRequestedIdentity(chosen.RawCaseNumber, identity!) ||
                chosen.CourtCase.RecordStatus != RecordStatus.Active ||
                !string.Equals(chosen.CourtCase.CurrentStatus?.Trim(), "Pending", StringComparison.OrdinalIgnoreCase) ||
                (chosen.ListingDate is null || chosen.ListingDate < clock.GetCurrentDate()))
                throw new CourtWorkflowException("Only exact, current official evidence for an active pending case can be accepted.", 409);
            var today = clock.GetCurrentDate();
            var others = await db.CourtExternalCaseStatusObservations
                .Where(x => x.CourtCaseId == chosen.CourtCaseId && x.Id != chosen.Id &&
                    x.ListingDate >= today &&
                    (x.Status == DhcAssistedEvidenceStatus.Accepted || x.ReviewReason == "DateConflict"))
                .ToListAsync(ct);
            foreach (var other in others)
            {
                if (other.Status == DhcAssistedEvidenceStatus.Accepted &&
                    other.ListingDate == chosen.ListingDate) continue;
                var from = other.Status;
                other.Status = DhcAssistedEvidenceStatus.NeedsReview;
                other.ReviewReason = "ResolvedByOfficer";
                db.CourtExternalAssistedDecisions.Add(new CourtExternalAssistedDecision
                {
                    ObservationId = other.Id, FromStatus = from, ToStatus = other.Status,
                    ActorUserId = userId, DecidedAt = clock.GetUtcNow(),
                    Reason = "Conflicting official date resolved by officer: " + reason.Trim()
                });
            }
            var listings = await db.CourtExternalListingObservations
                .Where(x => x.CourtCaseId == chosen.CourtCaseId && x.Mode == CourtExternalSyncMode.LiveWindow &&
                    x.Status == CourtExternalListingStatus.Accepted && x.ListingDate >= today &&
                    x.ListingDate != chosen.ListingDate).ToListAsync(ct);
            foreach (var listing in listings)
            {
                listing.Status = CourtExternalListingStatus.Superseded;
                listing.SupersededAt = clock.GetUtcNow();
                db.CourtExternalListingDecisions.Add(new CourtExternalListingDecision
                {
                    ObservationId = listing.Id, FromStatus = CourtExternalListingStatus.Accepted,
                    ToStatus = CourtExternalListingStatus.Superseded,
                    ActorUserId = userId, DecidedAt = clock.GetUtcNow(),
                    Reason = "Conflicting assisted official date resolved by officer: " + reason.Trim()
                });
            }
        }
        var previous = chosen.Status;
        chosen.Status = accept ? DhcAssistedEvidenceStatus.Accepted : DhcAssistedEvidenceStatus.Rejected;
        chosen.ReviewReason = accept ? null : "RejectedByOfficer";
        db.CourtExternalAssistedDecisions.Add(new CourtExternalAssistedDecision
        {
            ObservationId = chosen.Id, FromStatus = previous, ToStatus = chosen.Status,
            ActorUserId = userId, DecidedAt = clock.GetUtcNow(), Reason = reason.Trim()
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task ConfirmCanonicalStatusAsync(Guid observationId, string reason,
        Guid userId, CancellationToken ct)
    {
        await RequireOperatorAsync(userId, ct);
        if (workflow == null) throw new InvalidOperationException("Court workflow is unavailable.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new CourtWorkflowException("A status-confirmation reason of at most 500 characters is required.", 400);
        var observed = await db.CourtExternalCaseStatusObservations.AsNoTracking()
            .Include(x => x.CourtCase).SingleOrDefaultAsync(x => x.Id == observationId, ct)
            ?? throw new CourtWorkflowException("Official observation not found.", 404);
        if (!await authorization.CanEditCourtCaseAsync(observed.CourtCaseId, userId, ct))
            throw new CourtWorkflowException("Court case edit access is required.", 403);
        if (observed.Status == DhcAssistedEvidenceStatus.Rejected ||
            observed.ReviewReason is "IdentityMismatch" or "MultipleExactRows" or "DateConflict" ||
            CourtImportService.Identity(observed.CourtCase.CourtName, observed.CourtCase.CaseNumber) !=
                observed.NormalizedCaseIdentity ||
            !ContainsRequestedIdentity(observed.RawCaseNumber, observed.NormalizedCaseIdentity))
            throw new CourtWorkflowException("Only exact, non-conflicting official evidence can confirm case status.", 409);
        var proposed = observed.RawStatus?.Trim() switch
        {
            var value when string.Equals(value, "Disposed", StringComparison.OrdinalIgnoreCase) => "Disposed",
            var value when string.Equals(value, "Pending", StringComparison.OrdinalIgnoreCase) => "Pending",
            _ => throw new CourtWorkflowException("Official status is not a supported canonical status.", 409)
        };
        var courtCase = observed.CourtCase;
        if (courtCase.RecordStatus != RecordStatus.Active ||
            string.Equals(courtCase.CurrentStatus, proposed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(courtCase.CurrentStatus, "Disposed", StringComparison.OrdinalIgnoreCase))
            throw new CourtWorkflowException("This case has no eligible status change; disposed cases are never reactivated here.", 409);
        var command = new UpdateCourtCaseMetadataCommand(courtCase.CaseNumber, courtCase.CourtName,
            courtCase.CaseTitle, courtCase.CaseType, courtCase.FiledDate, proposed,
            courtCase.DisposedDate, courtCase.Remarks, courtCase.Revision);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct) : null;
        await workflow.UpdateMetadataWithAuditReasonAsync(courtCase.Id, command,
            $"DHC assisted observation {observationId}; officer reason: {reason.Trim()}", userId, ct);
        db.ChangeTracker.Clear();
        var current = await db.CourtExternalCaseStatusObservations.SingleAsync(x => x.Id == observationId, ct);
        var before = current.Status;
        if (before == DhcAssistedEvidenceStatus.NeedsReview)
        {
            current.Status = DhcAssistedEvidenceStatus.Accepted;
            current.ReviewReason = null;
        }
        db.CourtExternalAssistedDecisions.Add(new CourtExternalAssistedDecision
        {
            ObservationId = observationId, FromStatus = before,
            ToStatus = DhcAssistedEvidenceStatus.Accepted, ActorUserId = userId,
            DecidedAt = clock.GetUtcNow(),
            Reason = $"Canonical status explicitly confirmed as {proposed}. {reason.Trim()}"
        });
        await db.SaveChangesAsync(ct);
        if (transaction != null) await transaction.CommitAsync(ct);
    }

    // No canonical CourtCase, CourtProceeding, ScheduledEvent or imported URL
    // is ever modified here. An accepted observation changes only the resolver.
    public async Task<bool> ValidateQueuedCaseAsync(DhcAssistedSyncRun run, DhcAssistedSyncItem item,
        bool statusPhase, CancellationToken ct)
    {
        var courtCase = await db.CourtCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == item.CourtCaseId, ct);
        if (courtCase != null && courtCase.RecordStatus == RecordStatus.Active &&
            courtCase.CourtName == "Delhi High Court" &&
            string.Equals(courtCase.CurrentStatus?.Trim(), "Pending", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(item.NormalizedCaseIdentity) &&
            CourtImportService.Identity(courtCase.CourtName, courtCase.CaseNumber) == item.NormalizedCaseIdentity)
            return true;
        item.Status = DhcAssistedItemStatus.NeedsReview;
        item.FailureCode = "CaseChangedSinceQueue";
        item.FailureMessage = "Case identity or eligibility changed after this verification run was created.";
        item.CompletedAt = clock.GetUtcNow();
        run.NeedsReviewCases++;
        if (statusPhase) run.CompletedCases++;
        await db.SaveChangesAsync(ct);
        return false;
    }

    public async Task ProcessStatusAsync(DhcAssistedSyncRun run, DhcAssistedSyncItem item,
        string response, CancellationToken ct)
    {
        if (!await ValidateQueuedCaseAsync(run, item, true, ct)) return;
        var courtCase = await db.CourtCases.SingleAsync(x => x.Id == item.CourtCaseId, ct);
        var requested = item.NormalizedCaseIdentity;
        var rows = DelhiHighCourtAssistedForms.ParseStatusRows(response);
        var exact = rows.Where(x => ContainsRequestedIdentity(x.RawCaseNumber, requested)).ToList();
        var now = clock.GetUtcNow();
        if (exact.Count == 0)
        {
            item.Status = rows.Count == 0 ? DhcAssistedItemStatus.NotFound : DhcAssistedItemStatus.NeedsReview;
            item.FailureCode = rows.Count == 0 ? "NotFound" : "IdentityMismatch";
            item.FailureMessage = rows.Count == 0 ? "Official search returned no exact matching row." :
                "Official result identity differs from the requested case.";
            if (item.Status == DhcAssistedItemStatus.NeedsReview) run.NeedsReviewCases++;
            if (rows.Count > 0) await StoreReviewEvidenceAsync(courtCase.Id, item.Id, requested,
                rows, "IdentityMismatch", now, ct);
        }
        else if (exact.Count > 1)
        {
            item.Status = DhcAssistedItemStatus.NeedsReview;
            item.FailureCode = "MultipleExactRows";
            item.FailureMessage = "Official search returned more than one exact row.";
            run.NeedsReviewCases++;
            await StoreReviewEvidenceAsync(courtCase.Id, item.Id, requested,
                exact, "MultipleExactRows", now, ct);
        }
        else
        {
            var row = exact[0];
            var existing = await db.CourtExternalCaseStatusObservations
                .AnyAsync(x => x.RunItemId == item.Id && x.EvidenceSha256 == row.EvidenceSha256, ct);
            if (!existing)
            {
                var today = clock.GetCurrentDate();
                var officialDates = await db.CourtExternalListingObservations.AsNoTracking()
                    .Where(x => x.CourtCaseId == courtCase.Id && x.Mode == CourtExternalSyncMode.LiveWindow &&
                        x.Status == CourtExternalListingStatus.Accepted && x.ListingDate >= today)
                    .Select(x => x.ListingDate).ToListAsync(ct);
                officialDates.AddRange(await db.CourtExternalCaseStatusObservations.AsNoTracking()
                    .Where(x => x.CourtCaseId == courtCase.Id && x.ListingDate >= today &&
                        x.Status == DhcAssistedEvidenceStatus.Accepted)
                    .Select(x => x.ListingDate!.Value).ToListAsync(ct));
                var unresolvedConflict = await db.CourtExternalCaseStatusObservations.AsNoTracking()
                    .AnyAsync(x => x.CourtCaseId == courtCase.Id && x.ListingDate >= today &&
                        x.ReviewReason == "DateConflict", ct);
                var conflict = row.ListingDate >= today &&
                    (unresolvedConflict || officialDates.Any(x => x != row.ListingDate));
                var statusDifference = !string.IsNullOrWhiteSpace(row.RawStatus) &&
                    !string.Equals(row.RawStatus.Trim(), courtCase.CurrentStatus?.Trim(), StringComparison.OrdinalIgnoreCase);
                var review = conflict || statusDifference;
                var previousOperationalDate = row.ListingDate >= today
                    ? await CourtOperationalNdohQuery.Resolve(db.CourtCases.AsNoTracking()
                        .Where(x => x.Id == courtCase.Id), db, today)
                        .Select(x => x.OperationalNdoh).SingleAsync(ct)
                    : null;
                db.CourtExternalCaseStatusObservations.Add(new CourtExternalCaseStatusObservation
                {
                    CourtCaseId = courtCase.Id, RunItemId = item.Id, ObservedAt = now,
                    NormalizedCaseIdentity = requested, RawCaseNumber = row.RawCaseNumber,
                    RawDiaryNumber = row.RawDiaryNumber, RawStatus = row.RawStatus, RawParties = row.RawParties,
                    RawListingDate = row.RawListingDate, ListingDate = row.ListingDate,
                    RawCourtNumber = row.RawCourtNumber, SourceUrl = DelhiHighCourtAssistedForms.StatusUrl,
                    RawEvidenceText = row.RawEvidenceText, EvidenceSha256 = row.EvidenceSha256,
                    Status = review ? DhcAssistedEvidenceStatus.NeedsReview : DhcAssistedEvidenceStatus.Accepted,
                    ReviewReason = conflict ? "DateConflict" : statusDifference ? "StatusDifference" : null
                });
                if (review)
                {
                    item.FailureCode = conflict ? "DateConflict" : "StatusDifference";
                    item.FailureMessage = conflict ? "Current official listing dates disagree; officer review required." :
                        "Official case status differs from the LAC record; officer review required.";
                    run.NeedsReviewCases++;
                }
                else if (row.ListingDate >= today && row.ListingDate != previousOperationalDate)
                    run.UpdatedCases++;
                else run.NoChangeCases++;
            }
        }
        if (item.Status is not (DhcAssistedItemStatus.NotFound or DhcAssistedItemStatus.NeedsReview))
            item.Status = DhcAssistedItemStatus.StatusCaptured;
        if (item.Status is DhcAssistedItemStatus.NotFound or DhcAssistedItemStatus.NeedsReview or
            DhcAssistedItemStatus.StatusCaptured)
        {
            item.CompletedAt = now;
            run.CompletedCases++;
        }
        run.LastActivityAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task ProcessOrdersAsync(DhcAssistedSyncRun run, DhcAssistedSyncItem item,
        string response, CancellationToken ct)
    {
        if (!await ValidateQueuedCaseAsync(run, item, false, ct)) return;
        var courtCase = await db.CourtCases.AsNoTracking().SingleAsync(x => x.Id == item.CourtCaseId, ct);
        var requested = item.NormalizedCaseIdentity;
        var rows = DelhiHighCourtAssistedForms.ParseOrderRows(response);
        var exactRows = rows.Where(x => ContainsRequestedIdentity(x.RawCaseNumber, requested)).ToList();
        if (rows.Count > 0 && exactRows.Count == 0)
        {
            item.Status = DhcAssistedItemStatus.NeedsReview;
            if (item.FailureCode == null)
            {
                item.FailureCode = "OrderIdentityMismatch";
                item.FailureMessage = "Official order result identity differs from the requested case.";
                run.NeedsReviewCases++;
            }
        }
        if (exactRows.Any(x => x.OfficialUrl != null && !string.IsNullOrWhiteSpace(x.RawOrderDate) &&
            x.OrderDate == null))
        {
            item.Status = DhcAssistedItemStatus.NeedsReview;
            if (item.FailureCode == null)
            {
                item.FailureCode = "OrderDateNeedsReview";
                item.FailureMessage = "An official order link has a date that could not be parsed deterministically.";
                run.NeedsReviewCases++;
            }
        }
        foreach (var row in exactRows)
        {
            if (await db.CourtExternalOrderObservations.AnyAsync(x =>
                    x.RunItemId == item.Id && x.EvidenceSha256 == row.EvidenceSha256, ct)) continue;
            db.CourtExternalOrderObservations.Add(new CourtExternalOrderObservation
            {
                CourtCaseId = courtCase.Id, RunItemId = item.Id, ObservedAt = clock.GetUtcNow(),
                NormalizedCaseIdentity = requested, RawCaseNumber = row.RawCaseNumber,
                RawOrderDate = row.RawOrderDate, OrderDate = row.OrderDate,
                OfficialUrl = row.OfficialUrl, CorrigendumUrl = row.CorrigendumUrl,
                UploadDate = row.UploadDate, RawUploadDate = row.RawUploadDate,
                RawRemark = row.RawRemark, SourceUrl = DelhiHighCourtAssistedForms.OrderUrl,
                EvidenceSha256 = row.EvidenceSha256, RawEvidenceText = row.RawEvidenceText
            });
        }
        if (item.Status is not (DhcAssistedItemStatus.NeedsReview or DhcAssistedItemStatus.NotFound))
            item.Status = item.FailureCode is "DateConflict" or "StatusDifference"
                ? DhcAssistedItemStatus.NeedsReview : DhcAssistedItemStatus.Completed;
        item.CompletedAt = clock.GetUtcNow();
        run.LastActivityAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private static bool ContainsRequestedIdentity(string raw, string requested)
    {
        foreach (Match match in Regex.Matches(raw,
                     @"[A-Za-z][A-Za-z.() ]*?\s*(?:No\.?\s*)?[-/]?\s*\d+\s*/\s*(?:19|20)\d{2}"))
        {
            var identity = CourtImportService.Identity("Delhi High Court", match.Value);
            if (identity == requested) return true;
        }
        return false;
    }

    private async Task StoreReviewEvidenceAsync(Guid caseId, Guid itemId, string requested,
        IEnumerable<DelhiHighCourtAssistedForms.StatusRow> rows, string reason,
        DateTimeOffset observedAt, CancellationToken ct)
    {
        foreach (var row in rows.DistinctBy(x => x.EvidenceSha256))
        {
            if (await db.CourtExternalCaseStatusObservations.AnyAsync(x =>
                    x.RunItemId == itemId && x.EvidenceSha256 == row.EvidenceSha256, ct)) continue;
            db.CourtExternalCaseStatusObservations.Add(new CourtExternalCaseStatusObservation
            {
                CourtCaseId = caseId, RunItemId = itemId, ObservedAt = observedAt,
                NormalizedCaseIdentity = requested, RawCaseNumber = row.RawCaseNumber,
                RawDiaryNumber = row.RawDiaryNumber, RawStatus = row.RawStatus,
                RawParties = row.RawParties, RawListingDate = row.RawListingDate,
                ListingDate = row.ListingDate, RawCourtNumber = row.RawCourtNumber,
                SourceUrl = DelhiHighCourtAssistedForms.StatusUrl,
                RawEvidenceText = row.RawEvidenceText, EvidenceSha256 = row.EvidenceSha256,
                Status = DhcAssistedEvidenceStatus.NeedsReview, ReviewReason = reason
            });
        }
    }
}
