using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LAC.Infrastructure;

public sealed record DhcAssistedChallenge(Guid RunId, string Kind, string? OfficialText,
    bool ImageAvailable, string Operation);

// Manual, one-shot, office-wide session. No hosted/periodic worker is registered.
public sealed class DelhiHighCourtAssistedCoordinator(
    IServiceScopeFactory scopes, IConfiguration configuration,
    IHostApplicationLifetime lifetime, ILogger<DelhiHighCourtAssistedCoordinator> logger,
    Func<DelhiHighCourtAssistedSession>? sessionFactory = null)
{
    private sealed class Active(Guid runId, Guid ownerId, DelhiHighCourtAssistedSession session)
    {
        public Guid RunId { get; } = runId;
        public Guid OwnerId { get; } = ownerId;
        public DelhiHighCourtAssistedSession Session { get; } = session;
        public CancellationTokenSource Cancel { get; } = new();
        public bool Busy { get; set; }
        public bool OrdersChallenge { get; set; }
        public int InvalidAnswers { get; set; }
        public int Finished;
    }

    private readonly SemaphoreSlim mutex = new(1, 1);
    private Active? active;
    public Guid? ActiveRunId => active?.RunId;

    public async Task<Guid> StartAsync(Guid userId, DhcAssistedStartRequest request, CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>();
            await service.RequireOperatorAsync(userId, ct);
            if (active != null) throw await BusyExceptionAsync(scope.ServiceProvider, active, ct);
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            await MarkInterruptedAsync(db, ct);
            var session = sessionFactory?.Invoke() ?? new DelhiHighCourtAssistedSession(configuration);
            try
            {
                await session.LoadFormAsync(false, ct);
                var run = await service.CreateRunAsync(userId, request, ct);
                active = new Active(run.Id, userId, session);
                return run.Id;
            }
            catch { await session.DisposeAsync(); throw; }
        }
        finally { mutex.Release(); }
    }

    public async Task<Guid> ResumeAsync(Guid runId, Guid userId, CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>();
            await service.RequireOperatorAsync(userId, ct);
            if (active != null) throw await BusyExceptionAsync(scope.ServiceProvider, active, ct);
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            await MarkInterruptedAsync(db, ct);
            var run = await db.DhcAssistedSyncRuns.SingleOrDefaultAsync(x => x.Id == runId, ct)
                ?? throw new CourtWorkflowException("Assisted run not found.", 404);
            if (run.StartedByUserId != userId) throw new CourtWorkflowException("Only the run owner may resume verification.", 403);
            if (run.Status is not (DhcAssistedRunStatus.Interrupted or DhcAssistedRunStatus.Failed))
                throw new CourtWorkflowException("This assisted run cannot be resumed.", 409);
            var pending = await db.DhcAssistedSyncItems.Where(x => x.RunId == runId &&
                    x.Status != DhcAssistedItemStatus.Completed &&
                    x.Status != DhcAssistedItemStatus.NeedsReview &&
                    x.Status != DhcAssistedItemStatus.NotFound &&
                    x.Status != DhcAssistedItemStatus.Skipped &&
                    x.Status != DhcAssistedItemStatus.Cancelled)
                .OrderBy(x => x.QueueOrder).FirstOrDefaultAsync(ct);
            if (pending == null)
                throw new CourtWorkflowException("This assisted run has no remaining cases to resume.", 409);
            var orders = pending.Status == DhcAssistedItemStatus.CheckingOrders ||
                pending.Status == DhcAssistedItemStatus.Failed &&
                await db.CourtExternalCaseStatusObservations.AnyAsync(x => x.RunItemId == pending.Id, ct);
            var session = sessionFactory?.Invoke() ?? new DelhiHighCourtAssistedSession(configuration);
            try
            {
                await session.LoadFormAsync(orders, ct);
                run.Status = DhcAssistedRunStatus.WaitingForCaptcha;
                run.CaptchaChallenges++;
                run.LastActivityAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                active = new Active(run.Id, userId, session) { OrdersChallenge = orders };
                return run.Id;
            }
            catch { await session.DisposeAsync(); throw; }
        }
        finally { mutex.Release(); }
    }

    public async Task<DhcAssistedChallenge> ChallengeAsync(Guid runId, Guid userId, CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            var current = await RequireOwnerAsync(runId, userId, ct);
            var form = current.OrdersChallenge ? current.Session.OrderForm : current.Session.StatusForm;
            if (form == null || current.Busy) throw new CourtWorkflowException("No DHC verification is pending.", 409);
            return new(runId, form.VisibleChallenge == null ? "Image" : "Text",
                form.VisibleChallenge, form.ImageChallenge != null,
                current.OrdersChallenge ? "Order search" : "Case status");
        }
        finally { mutex.Release(); }
    }

    public async Task<DhcOfficialChallengeImage> ChallengeImageAsync(Guid runId, Guid userId, CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            var current = await RequireOwnerAsync(runId, userId, ct);
            return await current.Session.GetChallengeImageAsync(current.OrdersChallenge, ct);
        }
        finally { mutex.Release(); }
    }

    public async Task<DhcAssistedChallenge> RefreshAsync(Guid runId, Guid userId, CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            var current = await RequireOwnerAsync(runId, userId, ct);
            if (current.Busy) throw new CourtWorkflowException("Verification is already running.", 409);
            await current.Session.LoadFormAsync(current.OrdersChallenge, ct);
            current.InvalidAnswers = 0;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var run = await db.DhcAssistedSyncRuns.SingleAsync(x => x.Id == runId, ct);
            run.CaptchaChallenges++;
            run.LastActivityAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            var form = current.OrdersChallenge ? current.Session.OrderForm! : current.Session.StatusForm!;
            return new(runId, form.VisibleChallenge == null ? "Image" : "Text",
                form.VisibleChallenge, form.ImageChallenge != null,
                current.OrdersChallenge ? "Order search" : "Case status");
        }
        finally { mutex.Release(); }
    }

    public async Task<bool> SubmitHumanAnswerAsync(Guid runId, Guid userId, string answer, CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            var current = await RequireOwnerAsync(runId, userId, ct);
            if (current.Busy) throw new CourtWorkflowException("Verification is already running.", 409);
            if (current.InvalidAnswers >= 5)
                throw new CourtWorkflowException("Refresh the official challenge before retrying.", 409);
            // Never compare with the displayed challenge, store, log or replay
            // the officer's answer. Submit it solely to the normal official form.
            var valid = await current.Session.ValidateHumanAnswerAsync(answer, current.OrdersChallenge, ct);
            if (!valid)
            {
                current.InvalidAnswers++;
                await current.Session.LoadFormAsync(current.OrdersChallenge, ct);
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
                var run = await db.DhcAssistedSyncRuns.SingleAsync(x => x.Id == runId, ct);
                run.CaptchaChallenges++;
                run.LastActivityAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return false;
            }
            current.InvalidAnswers = 0;
            current.Busy = true;
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
                var run = await db.DhcAssistedSyncRuns.SingleAsync(x => x.Id == runId, ct);
                run.Status = DhcAssistedRunStatus.Running;
                run.LastActivityAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            _ = Task.Run(() => ProcessQueueAsync(current), CancellationToken.None);
            return true;
        }
        finally { mutex.Release(); }
    }

    public async Task CancelAsync(Guid runId, Guid userId, CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            var current = await RequireOwnerAsync(runId, userId, ct);
            current.Cancel.Cancel();
            if (!current.Busy) await FinishAsync(current, DhcAssistedRunStatus.Cancelled, null);
        }
        finally { mutex.Release(); }
    }

    private async Task ProcessQueueAsync(Active current)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(current.Cancel.Token,
            lifetime.ApplicationStopping);
        var ct = linked.Token;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>();
            var run = await db.DhcAssistedSyncRuns.Include(x => x.Items)
                .SingleAsync(x => x.Id == current.RunId, ct);
            foreach (var item in run.Items.OrderBy(x => x.QueueOrder))
            {
                ct.ThrowIfCancellationRequested();
                if (item.Status is DhcAssistedItemStatus.Completed or DhcAssistedItemStatus.NeedsReview or
                    DhcAssistedItemStatus.NotFound or DhcAssistedItemStatus.Skipped or DhcAssistedItemStatus.Cancelled)
                    continue;
                if (item.Status == DhcAssistedItemStatus.Failed)
                {
                    run.FailedCases = Math.Max(0, run.FailedCases - 1);
                    item.Status = await db.CourtExternalCaseStatusObservations
                        .AnyAsync(x => x.RunItemId == item.Id, ct)
                        ? DhcAssistedItemStatus.CheckingOrders : DhcAssistedItemStatus.Queued;
                    item.FailureCode = null;
                    item.FailureMessage = null;
                    await db.SaveChangesAsync(ct);
                }
                var courtCase = await db.CourtCases.AsNoTracking().SingleAsync(x => x.Id == item.CourtCaseId, ct);
                var identity = CourtImportService.Identity(courtCase.CourtName, courtCase.CaseNumber);
                var parts = identity == null ? null : DelhiHighCourtAssistedForms.NumberAndYear(identity);
                if (parts == null)
                {
                    item.Status = DhcAssistedItemStatus.Skipped;
                    item.FailureCode = "IdentityNeedsReview";
                    await db.SaveChangesAsync(ct);
                    continue;
                }
                if (item.Status != DhcAssistedItemStatus.CheckingOrders)
                {
                    if (!current.Session.Verified)
                    {
                        item.Status = DhcAssistedItemStatus.CaptchaRequired;
                        await PauseAsync(current, db, run, false, ct);
                        return;
                    }
                    var mapped = DelhiHighCourtAssistedForms.ExactCaseTypeValue(identity!,
                        current.Session.StatusForm!, false);
                    if (mapped == null)
                    {
                        item.Status = DhcAssistedItemStatus.NeedsReview;
                        item.FailureCode = "UnsupportedCaseType";
                        run.NeedsReviewCases++;
                        run.CompletedCases++;
                        await db.SaveChangesAsync(ct);
                        continue;
                    }
                    item.Status = DhcAssistedItemStatus.CheckingStatus;
                    item.StartedAt ??= DateTimeOffset.UtcNow;
                    item.AttemptCount++;
                    await db.SaveChangesAsync(ct);
                    var response = await current.Session.SearchStatusAsync(mapped, parts.Value.Number,
                        parts.Value.Year, ct);
                    if (DelhiHighCourtAssistedForms.CaptchaRequired(response))
                    {
                        item.Status = DhcAssistedItemStatus.CaptchaRequired;
                        await PauseAsync(current, db, run, false, ct);
                        return;
                    }
                    await service.ProcessStatusAsync(run, item, response, ct);
                    if (item.Status is DhcAssistedItemStatus.NotFound or DhcAssistedItemStatus.NeedsReview)
                        continue;
                }
                // Order search has an independently audited option map and its
                // own normal form. Loading that form presents its own challenge;
                // no earlier answer is replayed.
                if (current.Session.OrderForm == null || item.Status != DhcAssistedItemStatus.CheckingOrders)
                {
                    item.Status = DhcAssistedItemStatus.CheckingOrders;
                    await PauseAsync(current, db, run, true, ct);
                    return;
                }
                var orderType = DelhiHighCourtAssistedForms.ExactCaseTypeValue(identity!,
                    current.Session.OrderForm, true);
                if (orderType == null)
                {
                    item.Status = DhcAssistedItemStatus.NeedsReview;
                    item.FailureCode = "UnsupportedOrderCaseType";
                    run.NeedsReviewCases++;
                    run.CompletedCases++;
                    item.CompletedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                    continue;
                }
                var orderResponse = await current.Session.SearchOrdersAsync(orderType,
                    parts.Value.Number, parts.Value.Year, ct);
                if (DelhiHighCourtAssistedForms.CaptchaRequired(orderResponse))
                {
                    await PauseAsync(current, db, run, true, ct);
                    return;
                }
                await service.ProcessOrdersAsync(run, item, orderResponse, ct);
            }
            await FinishAsync(current, DhcAssistedRunStatus.Completed, null);
        }
        catch (OperationCanceledException)
        {
            await FinishAsync(current, current.Cancel.IsCancellationRequested ?
                DhcAssistedRunStatus.Cancelled : DhcAssistedRunStatus.Interrupted, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning("DHC assisted run {RunId} stopped: {Category}", current.RunId,
                ex is InvalidDataException ? "ParseFailed" : "OfficialSiteUnavailable");
            await FinishAsync(current, DhcAssistedRunStatus.Failed,
                ex is InvalidDataException ? "Delhi High Court page format appears to have changed. No further case data was updated." :
                "Official Delhi High Court service could not complete the remaining queue.");
        }
    }

    private async Task PauseAsync(Active current, LacDbContext db, DhcAssistedSyncRun run,
        bool orders, CancellationToken ct)
    {
        await current.Session.LoadFormAsync(orders, ct);
        current.OrdersChallenge = orders;
        run.Status = DhcAssistedRunStatus.PausedForCaptcha;
        run.CaptchaChallenges++;
        run.LastActivityAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        current.Busy = false;
    }

    private async Task FinishAsync(Active current, DhcAssistedRunStatus status, string? message)
    {
        if (Interlocked.Exchange(ref current.Finished, 1) != 0) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var run = await db.DhcAssistedSyncRuns.Include(x => x.Items)
            .SingleAsync(x => x.Id == current.RunId, CancellationToken.None);
        run.Status = status;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.LastActivityAt = run.CompletedAt.Value;
        run.FailureMessage = message;
        if (status == DhcAssistedRunStatus.Failed)
        {
            var pending = run.Items.OrderBy(x => x.QueueOrder).FirstOrDefault(x =>
                x.Status is DhcAssistedItemStatus.CheckingStatus or DhcAssistedItemStatus.CheckingOrders);
            if (pending != null)
            {
                pending.Status = DhcAssistedItemStatus.Failed;
                pending.FailureCode = message?.Contains("page format", StringComparison.OrdinalIgnoreCase) == true
                    ? "ParseFailed" : "OfficialSiteUnavailable";
                pending.FailureMessage = message;
                run.FailedCases++;
            }
        }
        if (status == DhcAssistedRunStatus.Cancelled)
            foreach (var item in run.Items.Where(x => x.Status is DhcAssistedItemStatus.Queued or
                         DhcAssistedItemStatus.CaptchaRequired or DhcAssistedItemStatus.CheckingStatus or
                         DhcAssistedItemStatus.CheckingOrders)) item.Status = DhcAssistedItemStatus.Cancelled;
        await db.SaveChangesAsync(CancellationToken.None);
        await current.Session.DisposeAsync();
        current.Cancel.Dispose();
        if (ReferenceEquals(active, current)) active = null;
    }

    private async Task<Active> RequireOwnerAsync(Guid runId, Guid userId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>()
            .RequireOperatorAsync(userId, ct);
        if (active == null || active.RunId != runId)
            throw new CourtWorkflowException("No active DHC session. Resume with a new official challenge.", 409);
        if (active.OwnerId != userId)
            throw new CourtWorkflowException("Only the run owner may enter the official verification code.", 403);
        return active;
    }

    private static async Task<CourtWorkflowException> BusyExceptionAsync(
        IServiceProvider provider, Active current, CancellationToken ct)
    {
        var db = provider.GetRequiredService<LacDbContext>();
        var owner = await db.AppUsers.AsNoTracking()
            .Where(x => x.Id == current.OwnerId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
        var since = await db.DhcAssistedSyncRuns.AsNoTracking().Where(x => x.Id == current.RunId)
            .Select(x => x.StartedAt).SingleAsync(ct);
        return new CourtWorkflowException($"Delhi High Court assisted verification is already being run by {owner ?? "an officer"} since {since:u}.", 409);
    }

    private static async Task MarkInterruptedAsync(LacDbContext db, CancellationToken ct)
    {
        var runs = await db.DhcAssistedSyncRuns.Where(x =>
            x.Status == DhcAssistedRunStatus.Running || x.Status == DhcAssistedRunStatus.WaitingForCaptcha ||
            x.Status == DhcAssistedRunStatus.PausedForCaptcha).ToListAsync(ct);
        foreach (var run in runs)
        {
            run.Status = DhcAssistedRunStatus.Interrupted;
            run.FailureMessage = "Application session ended. Resume with a new official verification code.";
        }
        if (runs.Count > 0) await db.SaveChangesAsync(ct);
    }
}
