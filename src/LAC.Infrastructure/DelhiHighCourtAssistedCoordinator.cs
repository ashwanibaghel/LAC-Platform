using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace LAC.Infrastructure;

public sealed record DhcAssistedChallenge(Guid RunId, string Kind, string? OfficialText,
    bool ImageAvailable, string Operation);

// Manual, one-shot, office-wide session. No hosted/periodic worker is registered.
public sealed class DelhiHighCourtAssistedCoordinator(
    IServiceScopeFactory scopes, IConfiguration configuration,
    IHostApplicationLifetime lifetime, ILogger<DelhiHighCourtAssistedCoordinator> logger,
    Func<DelhiHighCourtAssistedSession>? sessionFactory = null, TimeProvider? timeProvider = null)
{
    private sealed class Active(Guid runId, Guid ownerId, DelhiHighCourtAssistedSession session,
        DateTimeOffset createdAt)
    {
        public Guid RunId { get; } = runId;
        public Guid OwnerId { get; } = ownerId;
        public DelhiHighCourtAssistedSession Session { get; } = session;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        private long lastActivityTicks = createdAt.UtcTicks;
        public DateTimeOffset LastActivityAt => new(Interlocked.Read(ref lastActivityTicks), TimeSpan.Zero);
        public void Touch(DateTimeOffset now) => Interlocked.Exchange(ref lastActivityTicks, now.UtcTicks);
        public CancellationTokenSource Cancel { get; } = new();
        public CancellationTokenSource MonitorCancel { get; } = new();
        public bool Busy { get; set; }
        public bool OrdersChallenge { get; set; }
        public int InvalidAnswers { get; set; }
        public int Finished;
        public bool Expired;
    }

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan idleTimeout = TimeSpan.FromMinutes(Math.Clamp(
        configuration.GetValue<int?>("CourtSync:DelhiHighCourt:AssistedIdleMinutes") ?? 15, 1, 60));
    private readonly TimeSpan maxLifetime = TimeSpan.FromMinutes(Math.Clamp(
        configuration.GetValue<int?>("CourtSync:DelhiHighCourt:AssistedMaxSessionMinutes") ?? 60, 1, 240));
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
            await ExpireIfNeededAsync();
            if (active != null) throw await BusyExceptionAsync(scope.ServiceProvider, active, ct);
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            await MarkInterruptedAsync(db, ct);
            var session = sessionFactory?.Invoke() ?? new DelhiHighCourtAssistedSession(configuration);
            try
            {
                await session.LoadFormAsync(false, ct);
                var run = await service.CreateRunAsync(userId, request, ct);
                active = new Active(run.Id, userId, session, clock.GetUtcNow());
                StartMonitor(active);
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
            await ExpireIfNeededAsync();
            if (active != null) throw await BusyExceptionAsync(scope.ServiceProvider, active, ct);
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            await MarkInterruptedAsync(db, ct);
            var run = await db.DhcAssistedSyncRuns.SingleOrDefaultAsync(x => x.Id == runId, ct)
                ?? throw new CourtWorkflowException("Assisted run not found.", 404);
            if (run.StartedByUserId != userId) throw new CourtWorkflowException("Only the run owner may resume verification.", 403);
            if (run.Status is not (DhcAssistedRunStatus.Interrupted or DhcAssistedRunStatus.Failed))
                throw new CourtWorkflowException("This assisted run cannot be resumed.", 409);
            var statusItems = await db.DhcAssistedSyncItems.Where(x => x.RunId == runId).ToListAsync(ct);
            if (statusItems.Count == 1 && statusItems[0].Reason == DelhiHighCourtAssistedService.FullHistoryReason)
            {
                // The opaque Orders link belongs to the exact human-verified
                // status result. A lost session obtains it again; never guess it
                // or switch to the separate judgment-search form.
                var fullSession = sessionFactory?.Invoke() ?? new DelhiHighCourtAssistedSession(configuration);
                try
                {
                    // Orders are a public read-only GET. Resume the same run's
                    // exact accepted status provenance after a transport failure,
                    // without replaying a CAPTCHA POST or constructing a URL.
                    var item = statusItems[0];
                    var observation = run.Phase == DhcAssistedPhase.OrderLookup
                        ? await db.CourtExternalCaseStatusObservations.AsNoTracking()
                            .Where(x => x.RunItemId == item.Id && x.CourtCaseId == item.CourtCaseId &&
                                x.NormalizedCaseIdentity == item.NormalizedCaseIdentity &&
                                x.Status == DhcAssistedEvidenceStatus.Accepted)
                            .OrderByDescending(x => x.ObservedAt).FirstOrDefaultAsync(ct) : null;
                    if (observation != null && observation.ObservedAt >= clock.GetUtcNow().AddHours(-1) &&
                        observation.ObservedAt <= clock.GetUtcNow().AddMinutes(1) &&
                        fullSession.RestoreRecordedStatusOrderList(item.NormalizedCaseIdentity,
                            observation.RawCaseNumber, observation.RawEvidenceText, observation.EvidenceSha256))
                    {
                        item.Status = DhcAssistedItemStatus.StatusCaptured;
                        item.FailureCode = null;
                        item.FailureMessage = null;
                        item.CompletedAt = null;
                        run.Status = DhcAssistedRunStatus.Running;
                        run.CompletedCases = run.FailedCases = run.NeedsReviewCases = 0;
                        run.CompletedAt = null;
                        run.FailureMessage = null;
                        run.LastActivityAt = clock.GetUtcNow();
                        await db.SaveChangesAsync(ct);
                        active = new Active(run.Id, userId, fullSession, clock.GetUtcNow()) { Busy = true };
                        StartMonitor(active);
                        var resumed = active;
                        _ = Task.Run(() => ProcessQueueAsync(resumed), CancellationToken.None);
                        return run.Id;
                    }
                    await fullSession.LoadFormAsync(false, ct);
                    statusItems[0].Status = DhcAssistedItemStatus.Queued;
                    statusItems[0].FailureCode = null;
                    statusItems[0].FailureMessage = null;
                    statusItems[0].CompletedAt = null;
                    run.Phase = DhcAssistedPhase.StatusLookup;
                    run.Status = DhcAssistedRunStatus.WaitingForCaptcha;
                    run.CompletedCases = run.FailedCases = run.NeedsReviewCases = 0;
                    run.CompletedAt = null;
                    run.FailureMessage = null;
                    run.CaptchaChallenges++;
                    run.LastActivityAt = clock.GetUtcNow();
                    await db.SaveChangesAsync(ct);
                    active = new Active(run.Id, userId, fullSession, clock.GetUtcNow());
                    StartMonitor(active);
                    return run.Id;
                }
                catch { await fullSession.DisposeAsync(); throw; }
            }
            if (run.Phase == DhcAssistedPhase.StatusLookup && statusItems.Count == run.TotalCases &&
                statusItems.All(x => x.Status is DhcAssistedItemStatus.StatusCaptured or
                    DhcAssistedItemStatus.Completed or DhcAssistedItemStatus.NeedsReview or
                    DhcAssistedItemStatus.NotFound or DhcAssistedItemStatus.Skipped))
            {
                run.Status = DhcAssistedRunStatus.ReadyForOrders;
                run.CompletedAt = null;
                run.FailureMessage = null;
                run.LastActivityAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
                return run.Id;
            }
            var pending = await db.DhcAssistedSyncItems.Where(x => x.RunId == runId &&
                    x.Status != DhcAssistedItemStatus.Completed &&
                    x.Status != DhcAssistedItemStatus.NeedsReview &&
                    x.Status != DhcAssistedItemStatus.NotFound &&
                    x.Status != DhcAssistedItemStatus.Skipped &&
                    x.Status != DhcAssistedItemStatus.Cancelled)
                .OrderBy(x => x.QueueOrder).FirstOrDefaultAsync(ct);
            if (pending == null)
                throw new CourtWorkflowException("This assisted run has no remaining cases to resume.", 409);
            var orders = run.Phase == DhcAssistedPhase.OrderLookup;
            var session = sessionFactory?.Invoke() ?? new DelhiHighCourtAssistedSession(configuration);
            try
            {
                await session.LoadFormAsync(orders, ct);
                run.Status = DhcAssistedRunStatus.WaitingForCaptcha;
                run.CompletedAt = null;
                run.FailureMessage = null;
                run.CaptchaChallenges++;
                run.LastActivityAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                active = new Active(run.Id, userId, session, clock.GetUtcNow()) { OrdersChallenge = orders };
                StartMonitor(active);
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
            var image = await current.Session.GetChallengeImageAsync(current.OrdersChallenge, ct);
            return image;
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
            Touch(current);
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
            Touch(current);
            if (!valid)
            {
                current.InvalidAnswers++;
                await current.Session.LoadFormAsync(current.OrdersChallenge, ct);
                Touch(current);
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

    public async Task StartOrdersAsync(Guid runId, Guid userId, CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            using var authScope = scopes.CreateScope();
            await authScope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>()
                .RequireOperatorAsync(userId, ct);
            await ExpireIfNeededAsync();
            if (active != null && active.RunId != runId)
                throw await BusyExceptionAsync(authScope.ServiceProvider, active, ct);
            if (active != null && active.OwnerId != userId)
                throw new CourtWorkflowException("Only the run owner may start order lookup.", 403);
            if (active?.Busy == true) throw new CourtWorkflowException("Verification is already running.", 409);
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var run = await db.DhcAssistedSyncRuns.SingleAsync(x => x.Id == runId, ct);
            if (run.StartedByUserId != userId)
                throw new CourtWorkflowException("Only the run owner may start order lookup.", 403);
            if (run.Status != DhcAssistedRunStatus.ReadyForOrders)
                throw new CourtWorkflowException("Complete case-status verification before order lookup.", 409);
            var fullItems = await db.DhcAssistedSyncItems.Where(x => x.RunId == runId).ToListAsync(ct);
            if (fullItems.Count == 1 && fullItems[0].Reason == DelhiHighCourtAssistedService.FullHistoryReason)
            {
                if (active?.Session.StatusOrderListUrl(fullItems[0].NormalizedCaseIdentity) == null)
                    throw new CourtWorkflowException("The exact official status result did not provide an Orders link. Resume fresh official status verification.", 409);
                run.Phase = DhcAssistedPhase.OrderLookup;
                run.Status = DhcAssistedRunStatus.Running;
                run.LastActivityAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
                var fullActive = active!;
                fullActive.Busy = true;
                _ = Task.Run(() => ProcessQueueAsync(fullActive), CancellationToken.None);
                return;
            }
            var newSession = active == null;
            var session = active?.Session ?? (sessionFactory?.Invoke() ?? new DelhiHighCourtAssistedSession(configuration));
            try { await session.LoadFormAsync(true, ct); }
            catch
            {
                if (newSession) await session.DisposeAsync();
                throw;
            }
            if (newSession)
            {
                active = new Active(run.Id, userId, session, clock.GetUtcNow());
                StartMonitor(active);
            }
            else Touch(active!);
            active!.OrdersChallenge = true;
            run.Phase = DhcAssistedPhase.OrderLookup;
            run.Status = DhcAssistedRunStatus.WaitingForCaptcha;
            run.FailureMessage = null;
            run.CaptchaChallenges++;
            run.LastActivityAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        finally { mutex.Release(); }
    }

    public async Task FinishSessionAsync(Guid runId, Guid userId, CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            using var authScope = scopes.CreateScope();
            await authScope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>()
                .RequireOperatorAsync(userId, ct);
            await ExpireIfNeededAsync();
            if (active != null && active.RunId != runId)
                throw await BusyExceptionAsync(authScope.ServiceProvider, active, ct);
            if (active != null && active.OwnerId != userId)
                throw new CourtWorkflowException("Only the run owner may finish verification.", 403);
            if (active?.Busy == true) throw new CourtWorkflowException("Verification is already running.", 409);
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var run = await db.DhcAssistedSyncRuns.SingleAsync(x => x.Id == runId, ct);
            if (run.StartedByUserId != userId)
                throw new CourtWorkflowException("Only the run owner may finish verification.", 403);
            if (run.Status != DhcAssistedRunStatus.ReadyForOrders)
                throw new CourtWorkflowException("Order lookup can be skipped only after status verification.", 409);
            if (active != null) await FinishAsync(active, DhcAssistedRunStatus.Completed, null);
            else
            {
                run.Status = DhcAssistedRunStatus.Completed;
                run.CompletedAt = clock.GetUtcNow();
                run.LastActivityAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
            }
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
                if (run.Phase == DhcAssistedPhase.StatusLookup && item.Status is
                    (DhcAssistedItemStatus.StatusCaptured or DhcAssistedItemStatus.Completed or
                     DhcAssistedItemStatus.NeedsReview or DhcAssistedItemStatus.NotFound or
                     DhcAssistedItemStatus.Skipped or DhcAssistedItemStatus.Cancelled))
                    continue;
                if (run.Phase == DhcAssistedPhase.OrderLookup && item.Status is not
                    (DhcAssistedItemStatus.StatusCaptured or DhcAssistedItemStatus.CheckingOrders or
                     DhcAssistedItemStatus.Failed))
                    continue;
                if (item.Status == DhcAssistedItemStatus.Failed)
                {
                    run.FailedCases = Math.Max(0, run.FailedCases - 1);
                    item.Status = await db.CourtExternalCaseStatusObservations
                        .AnyAsync(x => x.RunItemId == item.Id, ct)
                        ? DhcAssistedItemStatus.StatusCaptured
                        : DhcAssistedItemStatus.Queued;
                    item.FailureCode = null;
                    item.FailureMessage = null;
                    await db.SaveChangesAsync(ct);
                    if (run.Phase == DhcAssistedPhase.StatusLookup &&
                        item.Status == DhcAssistedItemStatus.StatusCaptured) continue;
                }
                if (!await service.ValidateQueuedCaseAsync(run, item,
                    run.Phase == DhcAssistedPhase.StatusLookup, ct)) continue;
                var identity = item.NormalizedCaseIdentity;
                var parts = DelhiHighCourtAssistedForms.NumberAndYear(identity);
                if (parts == null)
                {
                    item.Status = DhcAssistedItemStatus.NeedsReview;
                    item.FailureCode = "IdentityNeedsReview";
                    item.FailureMessage = "Queued case identity cannot be searched exactly.";
                    run.NeedsReviewCases++;
                    if (run.Phase == DhcAssistedPhase.StatusLookup) run.CompletedCases++;
                    await db.SaveChangesAsync(ct);
                    continue;
                }
                if (run.Phase == DhcAssistedPhase.StatusLookup)
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
                    item.StartedAt ??= clock.GetUtcNow();
                    item.AttemptCount++;
                    await db.SaveChangesAsync(ct);
                    if (!await service.ValidateQueuedCaseAsync(run, item, true, ct)) continue;
                    EnsureWithinLifetime(current);
                    Touch(current);
                    var response = await current.Session.SearchStatusAsync(mapped, parts.Value.Number,
                        parts.Value.Year, ct);
                    Touch(current);
                    if (DelhiHighCourtAssistedForms.CaptchaRequired(response))
                    {
                        item.Status = DhcAssistedItemStatus.CaptchaRequired;
                        await PauseAsync(current, db, run, false, ct);
                        return;
                    }
                    await service.ProcessStatusAsync(run, item, response, ct);
                    Touch(current);
                    continue;
                }
                // Order phase starts only after every status item is terminal.
                // The official order form has an independent type map/challenge.
                if (!await db.CourtExternalCaseStatusObservations.AnyAsync(x =>
                    x.RunItemId == item.Id && x.NormalizedCaseIdentity == item.NormalizedCaseIdentity, ct))
                {
                    item.Status = DhcAssistedItemStatus.NeedsReview;
                    item.FailureCode = "MissingExactStatusEvidence";
                    item.FailureMessage = "Order lookup skipped because exact status evidence is unavailable.";
                    run.NeedsReviewCases++;
                    await db.SaveChangesAsync(ct);
                    continue;
                }
                if (item.Reason == DelhiHighCourtAssistedService.FullHistoryReason)
                {
                    item.Status = DhcAssistedItemStatus.CheckingOrders;
                    await db.SaveChangesAsync(ct);
                    if (!await service.ValidateQueuedCaseAsync(run, item, false, ct)) continue;
                    EnsureWithinLifetime(current);
                    Touch(current);
                    var detailResponse = await current.Session.SearchStatusOrdersAsync(identity, ct);
                    Touch(current);
                    await service.ProcessOrdersAsync(run, item, detailResponse, ct,
                        current.Session.StatusOrderListUrl(identity));
                    continue;
                }
                if (!current.Session.Verified || current.Session.OrderForm == null)
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
                    if (item.FailureCode == null)
                    {
                        item.FailureCode = "UnsupportedOrderCaseType";
                        run.NeedsReviewCases++;
                    }
                    item.CompletedAt = clock.GetUtcNow();
                    await db.SaveChangesAsync(ct);
                    continue;
                }
                item.Status = DhcAssistedItemStatus.CheckingOrders;
                await db.SaveChangesAsync(ct);
                if (!await service.ValidateQueuedCaseAsync(run, item, false, ct)) continue;
                EnsureWithinLifetime(current);
                Touch(current);
                var orderResponse = await current.Session.SearchOrdersAsync(orderType,
                    parts.Value.Number, parts.Value.Year, ct);
                Touch(current);
                var assessment = DelhiHighCourtAssistedForms.AssessOrderResponse(orderResponse,
                    orderType, parts.Value.Number, parts.Value.Year);
                if (assessment.Kind != DelhiHighCourtAssistedForms.OrderResponseKind.Result)
                    logger.LogWarning("DHC order response {Category}: fields {FieldNames}, content type {ContentType}, result rows {HasResultRows}, captcha form {HasCaptchaForm}, captcha error {HasCaptchaError}, form error {HasFormError}",
                        assessment.Kind, string.Join(",", SafeFieldNames(current.Session.LastOrderPostFieldNames)),
                        current.Session.LastOrderResponseContentType, assessment.HasResultRows,
                        assessment.HasCaptchaForm, assessment.HasCaptchaError, assessment.HasFormError);
                if (assessment.Kind == DelhiHighCourtAssistedForms.OrderResponseKind.CaptchaRequired)
                {
                    await PauseAsync(current, db, run, true, ct);
                    return;
                }
                if (assessment.Kind != DelhiHighCourtAssistedForms.OrderResponseKind.Result)
                {
                    item.FailureCode = $"OrderResponse{assessment.Kind}";
                    item.FailureMessage = SafeOrderResponseSummary(assessment,
                        current.Session.LastOrderPostFieldNames, current.Session.LastOrderResponseContentType);
                    await db.SaveChangesAsync(ct);
                    throw new InvalidDataException("DHC order form returned without a proven result or explicit verification request.");
                }
                try
                {
                    await service.ProcessOrdersAsync(run, item, orderResponse, ct);
                }
                catch (InvalidDataException)
                {
                    // A recognized table can still contain an unsafe or malformed row.
                    // Persist only its response shape, never its HTML or values.
                    item.FailureCode = "OrderResultParseFailed";
                    item.FailureMessage = SafeOrderResponseSummary(assessment,
                        current.Session.LastOrderPostFieldNames, current.Session.LastOrderResponseContentType);
                    await db.SaveChangesAsync(ct);
                    throw;
                }
            }
            if (run.Phase == DhcAssistedPhase.StatusLookup)
            {
                await mutex.WaitAsync(ct);
                try
                {
                    run.Status = DhcAssistedRunStatus.ReadyForOrders;
                    run.FailureMessage = null;
                    run.LastActivityAt = clock.GetUtcNow();
                    await db.SaveChangesAsync(ct);
                    Touch(current);
                    current.Busy = false;
                }
                finally { mutex.Release(); }
            }
            else await FinishAsync(current, DhcAssistedRunStatus.Completed, null);
        }
        catch (OperationCanceledException)
        {
            await FinishAsync(current, current.Expired || !current.Cancel.IsCancellationRequested ?
                DhcAssistedRunStatus.Interrupted : DhcAssistedRunStatus.Cancelled,
                current.Expired ? "Assisted DHC session expired. Resume with a new official verification code." : null);
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
        EnsureWithinLifetime(current);
        await current.Session.LoadFormAsync(orders, ct);
        await mutex.WaitAsync(ct);
        try
        {
            current.OrdersChallenge = orders;
            run.Status = DhcAssistedRunStatus.PausedForCaptcha;
            run.CaptchaChallenges++;
            run.LastActivityAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            Touch(current);
            current.Busy = false;
        }
        finally { mutex.Release(); }
    }

    private async Task FinishAsync(Active current, DhcAssistedRunStatus status, string? message)
    {
        if (Interlocked.Exchange(ref current.Finished, 1) != 0) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var run = await db.DhcAssistedSyncRuns.Include(x => x.Items)
            .SingleAsync(x => x.Id == current.RunId, CancellationToken.None);
        run.Status = status;
        run.CompletedAt = status is DhcAssistedRunStatus.Interrupted ? null : clock.GetUtcNow();
        run.LastActivityAt = clock.GetUtcNow();
        run.FailureMessage = message;
        if (status == DhcAssistedRunStatus.Failed)
        {
            var pending = run.Items.OrderBy(x => x.QueueOrder).FirstOrDefault(x =>
                x.Status is DhcAssistedItemStatus.CheckingStatus or DhcAssistedItemStatus.CheckingOrders);
            if (pending != null)
            {
                pending.Status = DhcAssistedItemStatus.Failed;
                pending.FailureCode ??= message?.Contains("page format", StringComparison.OrdinalIgnoreCase) == true
                    ? "ParseFailed" : "OfficialSiteUnavailable";
                pending.FailureMessage ??= message;
                run.FailedCases++;
            }
        }
        if (status == DhcAssistedRunStatus.Cancelled)
            foreach (var item in run.Items.Where(x => x.Status is DhcAssistedItemStatus.Queued or
                         DhcAssistedItemStatus.CaptchaRequired or DhcAssistedItemStatus.CheckingStatus or
                         DhcAssistedItemStatus.CheckingOrders)) item.Status = DhcAssistedItemStatus.Cancelled;
        await db.SaveChangesAsync(CancellationToken.None);
        current.MonitorCancel.Cancel();
        await current.Session.DisposeAsync();
        current.Cancel.Dispose();
        current.MonitorCancel.Dispose();
        if (ReferenceEquals(active, current)) active = null;
    }

    private static IReadOnlyList<string> SafeFieldNames(IEnumerable<string> names) => names
        .Where(x => Regex.IsMatch(x, @"^[A-Za-z_][A-Za-z0-9_.-]{0,63}$"))
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(40).ToArray();

    private static string SafeOrderResponseSummary(
        DelhiHighCourtAssistedForms.OrderResponseAssessment assessment,
        IReadOnlyList<string> outgoingNames, string? contentType)
    {
        var safeType = contentType is { Length: <= 30 } &&
            Regex.IsMatch(contentType, @"^[A-Za-z0-9.+-]+/[A-Za-z0-9.+-]+$")
            ? contentType : "unknown";
        static string BoundedNames(IEnumerable<string> names)
        {
            var value = string.Join(",", names);
            return value.Length > 80 ? value[..77] + "..." : value;
        }
        var summary = $"Category={assessment.Kind}; ContentType={safeType}; " +
            $"HasResultTable={assessment.HasResultTable}; HasValidResultColumns={assessment.HasValidResultColumns}; " +
            $"ResultDataRowCount={assessment.ResultDataRowCount}; HasCaptchaForm={assessment.HasCaptchaForm}; " +
            $"HasCaptchaError={assessment.HasCaptchaError}; HasFormError={assessment.HasFormError}; " +
            $"EchoesCaseType={assessment.EchoesCaseType}; EchoesCaseNumber={assessment.EchoesCaseNumber}; " +
            $"EchoesYear={assessment.EchoesYear}; " +
            $"ReturnedControls=[{BoundedNames(assessment.ReturnedControlNames)}]; " +
            $"OutgoingFields=[{BoundedNames(SafeFieldNames(outgoingNames))}].";
        return summary.Length <= 500 ? summary : summary[..500];
    }

    private async Task<Active> RequireOwnerAsync(Guid runId, Guid userId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DelhiHighCourtAssistedService>()
            .RequireOperatorAsync(userId, ct);
        await ExpireIfNeededAsync();
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
            x.Status == DhcAssistedRunStatus.PausedForCaptcha ||
            x.Status == DhcAssistedRunStatus.ReadyForOrders).ToListAsync(ct);
        foreach (var run in runs)
        {
            run.Status = DhcAssistedRunStatus.Interrupted;
            run.FailureMessage = "Application session ended. Resume with a new official verification code.";
        }
        if (runs.Count > 0) await db.SaveChangesAsync(ct);
    }

    private void Touch(Active current) => current.Touch(clock.GetUtcNow());

    private void EnsureWithinLifetime(Active current)
    {
        var now = clock.GetUtcNow();
        if (current.Expired || now - current.LastActivityAt >= idleTimeout ||
            now - current.CreatedAt >= maxLifetime)
        {
            current.Expired = true;
            current.Cancel.Cancel();
            throw new OperationCanceledException("Assisted DHC session expired.");
        }
    }

    private void StartMonitor(Active current)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (!current.MonitorCancel.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), current.MonitorCancel.Token);
                    await SweepExpiredAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logger.LogWarning("Assisted DHC expiry monitor stopped: {Category}", ex.GetType().Name);
            }
        });
    }

    // Also called by actions, so a late timer tick cannot permit stale use.
    // Tests advance a controllable TimeProvider and call this without waiting.
    public async Task SweepExpiredAsync()
    {
        await mutex.WaitAsync();
        try { await ExpireIfNeededAsync(); }
        finally { mutex.Release(); }
    }

    private async Task ExpireIfNeededAsync()
    {
        var current = active;
        if (current == null || current.Finished != 0 || current.Expired) return;
        var now = clock.GetUtcNow();
        if (now - current.LastActivityAt < idleTimeout && now - current.CreatedAt < maxLifetime) return;
        current.Expired = true;
        current.Cancel.Cancel();
        if (!current.Busy)
            await FinishAsync(current, DhcAssistedRunStatus.Interrupted,
                "Assisted DHC session expired. Resume with a new official verification code.");
    }
}
