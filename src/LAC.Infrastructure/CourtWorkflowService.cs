namespace LAC.Infrastructure;

using System;
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

public interface ICourtWorkflowService
{
    Task<CourtProceedingDto> RecordProceedingAsync(
        Guid courtCaseId,
        RecordCourtProceedingCommand command,
        Guid callerUserId,
        CancellationToken ct = default);
}

public sealed class CourtWorkflowService(
    LacDbContext db,
    ICourtAuthorizationService courtAuth,
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

    public async Task<CourtProceedingDto> RecordProceedingAsync(
        Guid courtCaseId,
        RecordCourtProceedingCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var canEdit = await courtAuth.CanEditCourtReferencesAsync(callerUserId, ct);
        if (!canEdit)
            throw new CourtWorkflowException("You do not have permission to record court proceedings.", 403);

        var courtCase = await db.CourtCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courtCaseId, ct);
        if (courtCase is null)
            throw new CourtWorkflowException("Court case not found.", 404);

        if (command.NextDate.HasValue && command.ProceedingDate.HasValue && command.NextDate.Value < command.ProceedingDate.Value)
            throw new CourtWorkflowException("NextDate cannot be earlier than ProceedingDate.", 400);

        var actorUser = await db.AppUsers.AsNoTracking()
            .Include(u => u.Designation)
            .FirstOrDefaultAsync(u => u.Id == callerUserId, ct)
            ?? throw new CourtWorkflowException("Actor user not found.", 401);

        var actorDisplayName = actorUser.DisplayName;
        var actorDesignation = actorUser.Designation?.Name;

        var proceedingId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
        {
            return await db.Set<CourtProceeding>().AsNoTracking().AnyAsync(p => p.Id == proceedingId, c);
        };

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

            // Look for active CourtProceeding-origin schedule projection for this court case
            var existingSchedule = await db.ScheduledEvents
                .FirstOrDefaultAsync(e => e.CourtCaseId == courtCaseId
                                       && e.Origin == ScheduledEventOrigin.CourtProceeding
                                       && e.Status == ScheduledEventStatus.Scheduled
                                       && e.RecordStatus == RecordStatus.Active, c);

            if (existingSchedule != null)
            {
                var currentSeq = await db.ScheduledEventEvents
                    .Where(e => e.ScheduledEventId == existingSchedule.Id)
                    .MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

                var workstream = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Id == existingSchedule.WorkstreamId, c);
                var workstreamName = workstream?.Name ?? "Court References";

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
                        WorkstreamNameSnapshot = workstreamName,
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
                        WorkstreamNameSnapshot = workstreamName,
                        Reason = "Superseded: latest court proceeding has no future hearing date",
                        Notes = $"Terminal transition from Court Proceeding recorded on {now:yyyy-MM-dd HH:mm:ss} UTC"
                    });
                }
            }

            await db.SaveChangesAsync(c);

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
}
