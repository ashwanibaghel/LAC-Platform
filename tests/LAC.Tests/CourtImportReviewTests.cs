using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LAC.Tests;

public sealed class CourtImportReviewTests
{
    [Fact]
    public async Task OneOfficerAction_AddsOnlyReadyRowsAndLeavesRiskyRowsForLater()
    {
        var (db, actor, batch, review, _) = await SetupAsync();
        using (db)
        {
            for (var n = 3; n < 313; n++)
                db.CourtImportRows.Add(Row(batch, n, n < 217 ? CourtImportRowStatus.NewCandidate : CourtImportRowStatus.NeedsReview));
            await db.SaveChangesAsync();
            var before = await review.SummaryAsync(batch.Id, actor.Id);
            Assert.Equal(214, before.SafeBulkCandidates);
            var result = await review.AddReadyAsync(batch.Id, actor.Id);
            Assert.Equal(214, result.CommittedThisRun);
            Assert.Empty(result.Failures);
            Assert.Equal(214, result.Summary.Committed);
            Assert.Equal(96, result.Summary.Unresolved);
            Assert.Equal(214, await db.CourtCases.CountAsync());
            Assert.Equal(96, await db.CourtImportRows.CountAsync(x => x.ResolutionAction == null));
        }
    }

    [Fact]
    public async Task FailedSafeRow_RemainsVisibleForOneActionRetry()
    {
        var (db, actor, batch, review, _) = await SetupAsync();
        using (db)
        {
            var row = Row(batch, 101);
            row.ResolutionAction = CourtImportResolutionAction.ImportAsNewCase;
            row.ApprovedCaseNumber = row.RawCaseNumber;
            row.ApprovedCaseTitle = row.RawCaseTitle;
            row.ApprovedCourtName = row.SuggestedCourtName;
            row.ApprovedStatus = "Pending";
            row.ReviewerNotes = "Safe deterministic candidate bulk-approved";
            row.ReviewedByUserId = actor.Id;
            row.ReviewedAt = DateTimeOffset.UtcNow;
            row.CommitStatus = CourtImportCommitStatus.Failed;
            db.CourtImportRows.Add(row);
            await db.SaveChangesAsync();
            Assert.Equal(1, (await review.SummaryAsync(batch.Id, actor.Id)).RetryableSafe);
            var result = await review.AddReadyAsync(batch.Id, actor.Id);
            Assert.Equal(1, result.CommittedThisRun);
            Assert.Equal(0, result.Summary.RetryableSafe);
        }
    }

    private sealed class NoStorage : IDocumentStorage
    {
        public Task<string> SaveAsync(Stream content, string name, CancellationToken ct) => throw new NotSupportedException();
        public Task<DocumentStorageWriteResult> SaveAndHashAsync(Stream content, string name, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public StorageHealth GetHealth() => new("test", true, null, null);
    }

    private static async Task<(LacDbContext Db, AppUser Actor, CourtImportBatch Batch, CourtImportReviewService Review, ICourtAuthorizationService Auth)> SetupAsync(
        ScopeMode scope = ScopeMode.All)
    {
        var options = new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new LacDbContext(options);
        var actor = new AppUser { Username = "importer", NormalizedUsername = "IMPORTER", DisplayName = "Import Officer" };
        var role = new Role { Code = "COURT_IMPORT_TEST", Name = "Court import test" };
        db.AppUsers.Add(actor); db.Roles.Add(role);
        db.UserRoles.Add(new UserRole { UserId = actor.Id, RoleId = role.Id });
        foreach (var code in new[] { PermissionCodes.CourtView, PermissionCodes.CourtCreate, PermissionCodes.CourtEdit })
        {
            var permission = new Permission { Code = code, Name = code, Category = "Court" };
            db.Permissions.Add(permission);
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = scope });
        }
        var batch = new CourtImportBatch
        {
            SourceDocument = new Document { OriginalFileName = "synthetic.xlsx", StoragePath = "memory" },
            CreatedByUserId = actor.Id, Status = CourtImportBatchStatus.Parsed
        };
        db.CourtImportBatches.Add(batch);
        await db.SaveChangesAsync();
        var auth = new CourtAuthorizationService(db, null!, null!, null!);
        var workflow = new CourtWorkflowService(db, auth, new NoStorage());
        return (db, actor, batch, new CourtImportReviewService(db, auth, workflow), auth);
    }

    private static CourtImportRow Row(CourtImportBatch batch, int sourceRow, CourtImportRowStatus status = CourtImportRowStatus.NewCandidate) =>
        new()
        {
            BatchId = batch.Id, SourceRowNumber = sourceRow, RowStatus = status,
            RawCaseNumber = "WP(C) " + sourceRow + "/2026", RawCaseTitle = "Office case " + sourceRow,
            RawCourt = "Delhi High Court", SuggestedCourtName = "Delhi High Court",
            SuggestedCaseType = "wpc", SuggestedCaseNumber = sourceRow.ToString(), SuggestedCaseYear = 2026,
            IdentityKey = "delhihighcourt|wpc|" + sourceRow + "|2026",
            RawStatus = "Pending", SuggestedStatusClass = CourtImportStatusClass.Pending,
            ValidationIssuesJson = "[]", RawDirections = "Register direction",
            RawBriefFacts = "Separate brief facts", RawAwardNumber = "Unresolved award",
            RawVillage = "Unresolved village", RawLastOrderLink = "chrome-extension://unsafe",
            LastOrderLinkState = "NeedsReview"
        };

    [Fact]
    public async Task UnreviewedCannotCommit_SafeApprovalCreatesOneCaseAndLegacyNdohOnce()
    {
        var (db, actor, batch, review, auth) = await SetupAsync();
        using (db)
        {
            var row = Row(batch, 101);
            row.ParsedNdoh = new DateOnly(2026, 10, 15);
            row.RawNdoh = "15.10.2026";
            row.RawAdvocate = " Advocate A ";
            db.CourtImportRows.Add(row); await db.SaveChangesAsync();
            Assert.Equal(0, (await review.CommitAsync(batch.Id, actor.Id)).CommittedThisRun);
            Assert.Empty(db.CourtCases);
            var approved = await review.ApproveSafeAsync(batch.Id, actor.Id);
            Assert.Equal(1, approved.Ready);
            var first = await review.CommitAsync(batch.Id, actor.Id);
            Assert.Equal(1, first.CommittedThisRun);
            Assert.Empty(first.Failures);
            var courtCase = await db.CourtCases.SingleAsync();
            Assert.Equal("WP(C) 101/2026", courtCase.CaseNumber);
            Assert.Equal("Delhi High Court", courtCase.CourtName);
            Assert.Equal("Office case 101", courtCase.CaseTitle);
            Assert.Equal("Pending", courtCase.CurrentStatus);
            Assert.Null(courtCase.FiledDate); Assert.Null(courtCase.DisposedDate);
            var proceeding = await db.CourtProceedings.SingleAsync();
            Assert.Null(proceeding.ProceedingDate); Assert.Null(proceeding.Summary);
            Assert.Equal(new DateOnly(2026, 10, 15), proceeding.NextDate);
            Assert.Equal("LegacyRegisterNDOH", proceeding.SourceKind);
            var projection = new CourtProjectionService(db, auth, null!);
            var page = await projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), actor.Id);
            Assert.Equal(new DateOnly(2026, 10, 15), Assert.Single(page.Items).NextHearingDate);
            Assert.Equal("Advocate A", (await db.CourtCaseRepresentatives.SingleAsync()).DisplayName);
            Assert.Empty(db.Parties); Assert.Empty(db.Awards); Assert.Empty(db.Villages);
            Assert.Empty(db.Set<CourtCaseAward>()); Assert.Empty(db.Set<CourtCaseKhasra>());
            var saved = await db.CourtImportRows.SingleAsync();
            Assert.Equal(row.RawDirections, saved.RawDirections);
            Assert.Equal(row.RawBriefFacts, saved.RawBriefFacts);
            Assert.Equal("NeedsReview", saved.LastOrderLinkState);
            Assert.Equal(courtCase.Id, saved.CommittedCourtCaseId);
            Assert.Equal(proceeding.Id, saved.CommittedProceedingId);
            Assert.Contains(await db.CourtCaseEvents.ToListAsync(), e =>
                e.Action == CourtCaseAction.Created && e.CourtCaseId == courtCase.Id);
            Assert.Contains(await db.CourtCaseEvents.ToListAsync(), e =>
                e.Action == CourtCaseAction.ImportApplied && e.CourtImportBatchId == batch.Id &&
                e.CourtImportRowId == row.Id);
            Assert.Equal(0, (await review.CommitAsync(batch.Id, actor.Id)).CommittedThisRun);
            Assert.Single(db.CourtCases); Assert.Single(db.CourtProceedings);
            Assert.Single(db.CourtCaseRepresentatives);
        }
    }

    [Fact]
    public async Task BulkApprovalExcludesReviewConflictsAttentionInvalidAndSkip()
    {
        var (db, actor, batch, review, _) = await SetupAsync();
        using (db)
        {
            var safe = Row(batch, 201);
            var duplicate = Row(batch, 202, CourtImportRowStatus.PotentialDuplicate);
            var conflict = Row(batch, 203, CourtImportRowStatus.IdentityConflict);
            var needsReview = Row(batch, 204, CourtImportRowStatus.NeedsReview);
            var invalid = Row(batch, 205, CourtImportRowStatus.Invalid);
            var sineDie = Row(batch, 206, CourtImportRowStatus.NeedsReview);
            sineDie.RawStatus = "SINE-DIE"; sineDie.SuggestedStatusClass = CourtImportStatusClass.Attention;
            var skipped = Row(batch, 207);
            var legacyUnknown = Row(batch, 208);
            legacyUnknown.RawCourt = "District Court Rohini";
            legacyUnknown.SuggestedCourtName = "District Court Rohini";
            db.CourtImportRows.AddRange(safe, duplicate, conflict, needsReview, invalid, sineDie, skipped, legacyUnknown);
            await db.SaveChangesAsync();
            await review.DecideAsync(batch.Id, skipped.Id, new CourtImportDecisionRequest(CourtImportResolutionAction.Skip), actor.Id);
            var summary = await review.ApproveSafeAsync(batch.Id, actor.Id);
            Assert.Equal(1, summary.Ready); Assert.Equal(6, summary.Unresolved); Assert.Equal(1, summary.Skipped);
            Assert.Equal(CourtImportResolutionAction.ImportAsNewCase,
                (await db.CourtImportRows.SingleAsync(x => x.Id == safe.Id)).ResolutionAction);
            Assert.All(await db.CourtImportRows.Where(x => x.Id != safe.Id && x.Id != skipped.Id).ToListAsync(),
                x => Assert.Null(x.ResolutionAction));
            await review.CommitAsync(batch.Id, actor.Id);
            Assert.Single(db.CourtCases);
            Assert.Equal(6, (await review.SummaryAsync(batch.Id, actor.Id)).Unresolved);
            await Assert.ThrowsAsync<CourtWorkflowException>(() => review.DecideAsync(batch.Id, invalid.Id,
                new CourtImportDecisionRequest(CourtImportResolutionAction.ImportAsNewCase, ReviewerNotes: "Reviewed"), actor.Id));
        }
    }

    [Fact]
    public async Task LinkRequiresSelectionAndConflictingNdohChoice_PreservesMetadataAndHistory()
    {
        var (db, actor, batch, review, _) = await SetupAsync();
        using (db)
        {
            var existing = new CourtCase
            {
                CaseNumber = "OLD-1/2024", CourtName = "District Court",
                CaseTitle = "Trusted title", CurrentStatus = "Pending"
            };
            db.CourtCases.Add(existing);
            db.CourtProceedings.Add(new CourtProceeding
            {
                CourtCaseId = existing.Id, ProceedingDate = new DateOnly(2026, 1, 10),
                NextDate = new DateOnly(2026, 2, 10)
            });
            var row = Row(batch, 301, CourtImportRowStatus.IdentityConflict);
            row.ParsedNdoh = new DateOnly(2026, 3, 10);
            row.RawAdvocate = "Counsel X";
            db.CourtImportRows.Add(row); await db.SaveChangesAsync();
            await Assert.ThrowsAsync<CourtWorkflowException>(() => review.DecideAsync(batch.Id, row.Id,
                new CourtImportDecisionRequest(CourtImportResolutionAction.LinkToExistingCase,
                    ReviewerNotes: "Checked file"), actor.Id));
            await Assert.ThrowsAsync<CourtWorkflowException>(() => review.DecideAsync(batch.Id, row.Id,
                new CourtImportDecisionRequest(CourtImportResolutionAction.LinkToExistingCase,
                    ResolvedCourtCaseId: existing.Id, NdohAction: CourtImportNdohAction.UseImported,
                    ReviewerNotes: "Conflicting NDOH"), actor.Id));
            await review.DecideAsync(batch.Id, row.Id,
                new CourtImportDecisionRequest(CourtImportResolutionAction.LinkToExistingCase,
                    ResolvedCourtCaseId: existing.Id, NdohAction: CourtImportNdohAction.KeepExisting,
                    ReviewerNotes: "Keep verified date"), actor.Id);
            var result = await review.CommitAsync(batch.Id, actor.Id);
            Assert.Equal(1, result.CommittedThisRun); Assert.Empty(result.Failures);
            var persisted = await db.CourtCases.SingleAsync();
            Assert.Equal("OLD-1/2024", persisted.CaseNumber);
            Assert.Equal("District Court", persisted.CourtName);
            Assert.Equal("Trusted title", persisted.CaseTitle);
            Assert.Equal("Pending", persisted.CurrentStatus);
            Assert.Single(db.CourtProceedings);
            Assert.Empty(db.CourtCaseRepresentatives);
            Assert.Equal(existing.Id, (await db.CourtImportRows.SingleAsync()).CommittedCourtCaseId);
        }
    }

    [Fact]
    public async Task DuplicateAtCommitFailsWithoutCreatingHalfCase_OtherApprovedRowCommits()
    {
        var (db, actor, batch, review, _) = await SetupAsync();
        using (db)
        {
            var duplicate = Row(batch, 401);
            var other = Row(batch, 402);
            db.CourtImportRows.AddRange(duplicate, other); await db.SaveChangesAsync();
            await review.ApproveSafeAsync(batch.Id, actor.Id);
            db.CourtCases.Add(new CourtCase
            {
                CaseNumber = duplicate.RawCaseNumber!, CourtName = duplicate.RawCourt!,
                CaseTitle = "Already registered"
            });
            await db.SaveChangesAsync();
            var result = await review.CommitAsync(batch.Id, actor.Id);
            Assert.Equal(1, result.CommittedThisRun);
            Assert.Single(result.Failures);
            Assert.Equal(2, await db.CourtCases.CountAsync());
            Assert.Equal(CourtImportCommitStatus.Failed,
                (await db.CourtImportRows.SingleAsync(x => x.Id == duplicate.Id)).CommitStatus);
            Assert.Equal(CourtImportCommitStatus.Committed,
                (await db.CourtImportRows.SingleAsync(x => x.Id == other.Id)).CommitStatus);
            Assert.Equal(0, (await review.CommitAsync(batch.Id, actor.Id)).CommittedThisRun);
            Assert.Equal(2, await db.CourtCases.CountAsync());
        }
    }

    [Fact]
    public async Task AssignedOnlyCannotReviewOrCommit()
    {
        var (db, actor, batch, review, _) = await SetupAsync(ScopeMode.Assigned);
        using (db)
        {
            await Assert.ThrowsAsync<CourtWorkflowException>(() => review.SummaryAsync(batch.Id, actor.Id));
            await Assert.ThrowsAsync<CourtWorkflowException>(() => review.ApproveSafeAsync(batch.Id, actor.Id));
            await Assert.ThrowsAsync<CourtWorkflowException>(() => review.CommitAsync(batch.Id, actor.Id));
        }
    }

    [Fact]
    public async Task ReviewedDisposedAndAttentionRows_DoNotInventDatesOrPromoteDirections()
    {
        var (db, actor, batch, review, _) = await SetupAsync();
        using (db)
        {
            var disposed = Row(batch, 501);
            disposed.RawStatus = "Disposed off";
            disposed.SuggestedStatusClass = CourtImportStatusClass.Disposed;
            disposed.ParsedNdoh = null;
            disposed.RawNdoh = "not a date";
            var attention = Row(batch, 502, CourtImportRowStatus.NeedsReview);
            attention.RawStatus = "SINE-DIE";
            attention.SuggestedStatusClass = CourtImportStatusClass.Attention;
            attention.ValidationIssuesJson = "[\"Case status needs classification review.\"]";
            db.CourtImportRows.AddRange(disposed, attention);
            await db.SaveChangesAsync();
            var bulk = await review.ApproveSafeAsync(batch.Id, actor.Id);
            Assert.Equal(1, bulk.Ready);
            Assert.Equal(1, bulk.Unresolved);
            await Assert.ThrowsAsync<CourtWorkflowException>(() => review.DecideAsync(batch.Id, attention.Id,
                new CourtImportDecisionRequest(CourtImportResolutionAction.ImportAsNewCase,
                    ReviewerNotes: "Verified manually"), actor.Id));
            await review.DecideAsync(batch.Id, attention.Id,
                new CourtImportDecisionRequest(CourtImportResolutionAction.ImportAsNewCase,
                    ApprovedStatus: "Pending", ReviewerNotes: "Office officer reviewed status"), actor.Id);
            var committed = await review.CommitAsync(batch.Id, actor.Id);
            Assert.Equal(2, committed.CommittedThisRun);
            var cases = await db.CourtCases.OrderBy(x => x.CaseNumber).ToListAsync();
            Assert.Contains(cases, x => x.CurrentStatus == "Disposed" && x.DisposedDate == null);
            Assert.Contains(cases, x => x.CurrentStatus == "Pending");
            Assert.Empty(db.CourtProceedings);
            Assert.All(await db.CourtImportRows.ToListAsync(), x => Assert.Equal("Register direction", x.RawDirections));
        }
    }

    [Fact]
    public async Task IsolatedPostgres_MigrationAndTransactionalCommit_WhenConfigured()
    {
        var connection = Environment.GetEnvironmentVariable("COURT_IMPORT_POSTGRES_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection).Options;
        using var db = new LacDbContext(options);
        await db.Database.MigrateAsync();
        var actor = new AppUser { Username = "postgres_importer", NormalizedUsername = "POSTGRES_IMPORTER", DisplayName = "Postgres Smoke Officer" };
        var role = new Role { Code = "PG_COURT_IMPORT", Name = "PG Court Import" };
        db.AppUsers.Add(actor); db.Roles.Add(role);
        db.UserRoles.Add(new UserRole { UserId = actor.Id, RoleId = role.Id });
        foreach (var code in new[] { PermissionCodes.CourtView, PermissionCodes.CourtCreate, PermissionCodes.CourtEdit })
        {
            var permission = await db.Permissions.SingleAsync(x => x.Code == code);
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = ScopeMode.All });
        }
        var batch = new CourtImportBatch
        {
            SourceDocument = new Document { OriginalFileName = "pg-synthetic.xlsx", StoragePath = "smoke" },
            CreatedByUserId = actor.Id, Status = CourtImportBatchStatus.Parsed
        };
        db.CourtImportBatches.Add(batch);
        var row = Row(batch, 901);
        row.ParsedNdoh = new DateOnly(2026, 11, 15);
        db.CourtImportRows.Add(row);
        await db.SaveChangesAsync();
        var auth = new CourtAuthorizationService(db, null!, null!, null!);
        var review = new CourtImportReviewService(db, auth, new CourtWorkflowService(db, auth, new NoStorage()));
        Assert.Equal(1, (await review.ApproveSafeAsync(batch.Id, actor.Id)).Ready);
        var committed = await review.CommitAsync(batch.Id, actor.Id);
        Assert.Equal(1, committed.CommittedThisRun);
        Assert.Empty(committed.Failures);
        Assert.Equal(0, (await review.CommitAsync(batch.Id, actor.Id)).CommittedThisRun);
        Assert.Single(await db.CourtCases.ToListAsync());
        Assert.Single(await db.CourtProceedings.ToListAsync());
        Assert.Equal(CourtImportCommitStatus.Committed,
            (await db.CourtImportRows.SingleAsync(x => x.Id == row.Id)).CommitStatus);
    }
}
