namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public class ScheduleWorkflowException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed record CreateScheduledEventCommand(
    Guid WorkstreamId,
    Guid? ResponsibleOfficeDeskId,
    Guid? AssignedUserId,
    ScheduledEventKind EventKind,
    string Title,
    string? Description,
    DateOnly ScheduledDate,
    TimeOnly? ScheduledTime,
    ScheduledEventPriority Priority,
    Guid? MatterId = null,
    Guid? DakId = null,
    Guid? OutwardId = null,
    Guid? WorkItemId = null,
    Guid? CourtCaseId = null,
    Guid? CourtProceedingId = null,
    IReadOnlyList<int>? Reminders = null
);

public sealed record RescheduleEventCommand(
    DateOnly NewScheduledDate,
    TimeOnly? NewScheduledTime,
    string? Reason,
    int ExpectedRevision
);

public sealed record ReassignEventCommand(
    Guid? TargetDeskId,
    Guid? TargetUserId,
    string? Reason,
    int ExpectedRevision
);

public sealed record CompleteEventCommand(
    string? Notes,
    int ExpectedRevision
);

public sealed record CancelEventCommand(
    string Reason,
    int ExpectedRevision
);

public sealed record AddReminderCommand(
    int DaysBefore,
    TimeOnly? ReminderTime,
    int ExpectedRevision
);

public sealed record RemoveReminderCommand(
    int ExpectedRevision
);

public sealed record LinkWorkItemCommand(
    Guid WorkItemId,
    int ExpectedRevision
);

public sealed record UnlinkWorkItemCommand(
    int ExpectedRevision
);

public sealed record CreateFromCourtProceedingCommand(
    Guid? ResponsibleOfficeDeskId,
    Guid? AssignedUserId,
    string? Title,
    string? Description,
    ScheduledEventPriority? Priority,
    IReadOnlyList<int>? Reminders
);

public interface IScheduleWorkflowService
{
    Task<ScheduledEvent> CreateScheduledEventAsync(
        CreateScheduledEventCommand command,
        Guid callerUserId,
        CancellationToken ct = default);

    Task<ScheduledEvent> RescheduleEventAsync(
        Guid id,
        RescheduleEventCommand command,
        Guid callerUserId,
        CancellationToken ct = default);

    Task<ScheduledEvent> ReassignEventAsync(
        Guid id,
        ReassignEventCommand command,
        Guid callerUserId,
        CancellationToken ct = default);

    Task<ScheduledEvent> CompleteEventAsync(
        Guid id,
        CompleteEventCommand command,
        Guid callerUserId,
        CancellationToken ct = default);

    Task<ScheduledEvent> CancelEventAsync(
        Guid id,
        CancelEventCommand command,
        Guid callerUserId,
        CancellationToken ct = default);

    Task<ScheduledReminder> AddReminderAsync(
        Guid id,
        AddReminderCommand command,
        Guid callerUserId,
        CancellationToken ct = default);

    Task<ScheduledEvent> RemoveReminderAsync(
        Guid id,
        Guid reminderId,
        RemoveReminderCommand command,
        Guid callerUserId,
        CancellationToken ct = default);

    Task<ScheduledEvent> LinkWorkItemAsync(
        Guid id,
        LinkWorkItemCommand command,
        Guid callerUserId,
        CancellationToken ct = default);

    Task<ScheduledEvent> UnlinkWorkItemAsync(
        Guid id,
        UnlinkWorkItemCommand command,
        Guid callerUserId,
        CancellationToken ct = default);

    Task<ScheduledEvent> CreateFromCourtProceedingAsync(
        Guid courtProceedingId,
        CreateFromCourtProceedingCommand command,
        Guid callerUserId,
        CancellationToken ct = default);
}

public sealed class ScheduleWorkflowService(
    LacDbContext db,
    IScheduleAuthorizationService scheduleAuth,
    IMatterAuthorizationService matterAuth,
    IDakAuthorizationService dakAuth,
    IOutwardAuthorizationService outwardAuth,
    IWorkItemAuthorizationService workItemAuth,
    Func<Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy>? strategyFactory = null) : IScheduleWorkflowService
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

    private async Task<ScheduledEvent> LockScheduledEventAsync(Guid eventId, CancellationToken ct)
    {
        ScheduledEvent? item;
        if (db.Database.IsRelational())
        {
            item = await db.ScheduledEvents
                .FromSqlInterpolated($"SELECT * FROM \"ScheduledEvents\" WHERE \"Id\" = {eventId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
        }
        else
        {
            item = await db.ScheduledEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct);
        }

        return item ?? throw new ScheduleWorkflowException("Scheduled event not found.", 404);
    }

    private async Task<(string DisplayName, string? Designation)> GetActorSnapshotAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.AppUsers.AsNoTracking()
            .Include(u => u.Designation)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            throw new ScheduleWorkflowException("Actor user not found.", 401);

        return (user.DisplayName, user.Designation?.Name);
    }

    // ========================================================================
    // 1. CREATE SCHEDULED EVENT
    // ========================================================================
    public async Task<ScheduledEvent> CreateScheduledEventAsync(
        CreateScheduledEventCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
            throw new ScheduleWorkflowException("Title is mandatory.", 400);

        var canCreate = await scheduleAuth.CanCreateScheduledEventAsync(
            command.WorkstreamId,
            command.ResponsibleOfficeDeskId,
            command.AssignedUserId,
            callerUserId,
            ct);

        if (!canCreate)
            throw new ScheduleWorkflowException("You do not have permission to create scheduled events in this workstream/desk.", 403);

        // Validate domain contexts
        if (command.MatterId.HasValue)
        {
            var matter = await db.Matters.AsNoTracking().FirstOrDefaultAsync(m => m.Id == command.MatterId.Value, ct);
            if (matter is null) throw new ScheduleWorkflowException("Linked matter not found.", 404);
            var canViewMatter = await matterAuth.CanAccessMatterAsync(command.MatterId.Value, PermissionCodes.MatterView, callerUserId, ct);
            if (!canViewMatter) throw new ScheduleWorkflowException("You do not have permission to access the linked matter.", 403);
        }

        if (command.DakId.HasValue)
        {
            var dak = await db.Daks.AsNoTracking().FirstOrDefaultAsync(d => d.Id == command.DakId.Value, ct);
            if (dak is null) throw new ScheduleWorkflowException("Linked dak not found.", 404);
            var canViewDak = await dakAuth.CanAccessDakAsync(command.DakId.Value, PermissionCodes.DakView, callerUserId, ct);
            if (!canViewDak) throw new ScheduleWorkflowException("You do not have permission to access the linked dak.", 403);
        }

        if (command.OutwardId.HasValue)
        {
            var outward = await db.Outwards.AsNoTracking().FirstOrDefaultAsync(o => o.Id == command.OutwardId.Value, ct);
            if (outward is null) throw new ScheduleWorkflowException("Linked outward correspondence not found.", 404);
            var canViewOutward = await outwardAuth.CanAccessOutwardAsync(command.OutwardId.Value, PermissionCodes.OutwardView, callerUserId, ct);
            if (!canViewOutward) throw new ScheduleWorkflowException("You do not have permission to access the linked outward correspondence.", 403);
        }

        if (command.WorkItemId.HasValue)
        {
            var workItem = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == command.WorkItemId.Value, ct);
            if (workItem is null) throw new ScheduleWorkflowException("Linked work item not found.", 404);
            var canViewWork = await workItemAuth.CanAccessWorkItemAsync(command.WorkItemId.Value, PermissionCodes.WorkItemView, callerUserId, ct);
            if (!canViewWork) throw new ScheduleWorkflowException("You do not have permission to access the linked work item.", 403);
        }

        if (command.CourtCaseId.HasValue)
        {
            var courtCase = await db.CourtCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == command.CourtCaseId.Value, ct);
            if (courtCase is null) throw new ScheduleWorkflowException("Linked court case not found.", 404);
        }

        if (command.CourtProceedingId.HasValue)
        {
            var proceeding = await db.CourtProceedings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == command.CourtProceedingId.Value, ct);
            if (proceeding is null) throw new ScheduleWorkflowException("Linked court proceeding not found.", 404);
        }

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var workstream = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Id == command.WorkstreamId, ct)
            ?? throw new ScheduleWorkflowException("Workstream not found.", 404);

        OfficeDesk? desk = null;
        if (command.ResponsibleOfficeDeskId.HasValue)
        {
            desk = await db.OfficeDesks.AsNoTracking().FirstOrDefaultAsync(d => d.Id == command.ResponsibleOfficeDeskId.Value, ct);
        }

        AppUser? assignedUser = null;
        if (command.AssignedUserId.HasValue)
        {
            assignedUser = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == command.AssignedUserId.Value, ct);
        }

        // Generate stable IDs outside retry loops
        var eventId = Guid.NewGuid();
        var createdEventId = Guid.NewGuid();
        var reminderSpecs = (command.Reminders ?? [])
            .Select(d => new { Id = Guid.NewGuid(), Days = Math.Clamp(d, 0, 365) })
            .ToList();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
        {
            var exists = await db.ScheduledEvents.AsNoTracking().AnyAsync(e => e.Id == eventId, c);
            if (!exists) return false;
            var historyExists = await db.ScheduledEventEvents.AsNoTracking().AnyAsync(e => e.Id == createdEventId, c);
            return historyExists;
        };

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                return (await db.ScheduledEvents.Include(e => e.Reminders).FirstOrDefaultAsync(e => e.Id == eventId, c))!;
            }

            var now = DateTimeOffset.UtcNow;
            var evt = new ScheduledEvent
            {
                Id = eventId,
                WorkstreamId = command.WorkstreamId,
                ResponsibleOfficeDeskId = command.ResponsibleOfficeDeskId,
                AssignedUserId = command.AssignedUserId,
                EventKind = command.EventKind,
                Title = command.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
                ScheduledDate = command.ScheduledDate,
                ScheduledTime = command.ScheduledTime,
                Priority = command.Priority,
                Status = ScheduledEventStatus.Scheduled,
                Revision = 1,
                CreatedByUserId = callerUserId,
                CreatedByDisplayNameSnapshot = actorDisplayName,
                CreatedByDesignationSnapshot = actorDesignation,
                LastActivityAt = now,
                MatterId = command.MatterId,
                DakId = command.DakId,
                OutwardId = command.OutwardId,
                WorkItemId = command.WorkItemId,
                CourtCaseId = command.CourtCaseId,
                CourtProceedingId = command.CourtProceedingId,
                Origin = ScheduledEventOrigin.Manual
            };

            db.ScheduledEvents.Add(evt);

            // Add reminders
            foreach (var r in reminderSpecs)
            {
                db.ScheduledReminders.Add(new ScheduledReminder
                {
                    Id = r.Id,
                    ScheduledEventId = eventId,
                    DaysBefore = r.Days,
                    IsActive = true,
                    CreatedByUserId = callerUserId,
                    CreatedByDisplayNameSnapshot = actorDisplayName
                });
            }

            // Append official immutable history event
            var historyEvent = new ScheduledEventEvent
            {
                Id = createdEventId,
                ScheduledEventId = eventId,
                SequenceNumber = 1,
                Action = ScheduledEventAction.Created,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                WorkstreamIdSnapshot = workstream.Id,
                WorkstreamNameSnapshot = workstream.Name,
                TargetDeskId = desk?.Id,
                TargetDeskNameSnapshot = desk?.Name,
                TargetUserId = assignedUser?.Id,
                TargetUserDisplayNameSnapshot = assignedUser?.DisplayName,
                NewScheduledDate = command.ScheduledDate,
                NewScheduledTime = command.ScheduledTime,
                Notes = evt.Title
            };

            db.ScheduledEventEvents.Add(historyEvent);
            await db.SaveChangesAsync(c);

            return evt;
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 2. RESCHEDULE EVENT
    // ========================================================================
    public async Task<ScheduledEvent> RescheduleEventAsync(
        Guid id,
        RescheduleEventCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var rescheduleEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
        {
            var item = await db.ScheduledEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, c);
            if (item is null) return false;
            if (item.ScheduledDate != command.NewScheduledDate || item.ScheduledTime != command.NewScheduledTime) return false;
            return await db.ScheduledEventEvents.AsNoTracking().AnyAsync(e => e.Id == rescheduleEventId, c);
        };

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                return (await db.ScheduledEvents.FirstOrDefaultAsync(e => e.Id == id, c))!;
            }

            var evt = await LockScheduledEventAsync(id, c);

            if (evt.Revision != command.ExpectedRevision)
                throw new ScheduleWorkflowException($"Revision conflict. Expected revision {command.ExpectedRevision}, current is {evt.Revision}.", 409);

            if (evt.Status is ScheduledEventStatus.Completed or ScheduledEventStatus.Cancelled)
                throw new ScheduleWorkflowException("Terminal scheduled events cannot be rescheduled.", 400);

            var canUpdate = await scheduleAuth.CanUpdateScheduledEventAsync(evt, callerUserId, c);
            if (!canUpdate)
                throw new ScheduleWorkflowException("You do not have permission to reschedule this event.", 403);

            var oldDate = evt.ScheduledDate;
            var oldTime = evt.ScheduledTime;
            var now = DateTimeOffset.UtcNow;

            evt.ScheduledDate = command.NewScheduledDate;
            evt.ScheduledTime = command.NewScheduledTime;
            evt.Revision++;
            evt.LastActivityAt = now;

            var workstream = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Id == evt.WorkstreamId, c);
            var currentSeq = await db.ScheduledEventEvents.Where(e => e.ScheduledEventId == id).MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            db.ScheduledEventEvents.Add(new ScheduledEventEvent
            {
                Id = rescheduleEventId,
                ScheduledEventId = id,
                SequenceNumber = currentSeq + 1,
                Action = ScheduledEventAction.Rescheduled,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                WorkstreamIdSnapshot = workstream?.Id ?? evt.WorkstreamId,
                WorkstreamNameSnapshot = workstream?.Name,
                OldScheduledDate = oldDate,
                OldScheduledTime = oldTime,
                NewScheduledDate = command.NewScheduledDate,
                NewScheduledTime = command.NewScheduledTime,
                Reason = command.Reason
            });

            await db.SaveChangesAsync(c);
            return evt;
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 3. REASSIGN RESPONSIBILITY
    // ========================================================================
    public async Task<ScheduledEvent> ReassignEventAsync(
        Guid id,
        ReassignEventCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var reassignEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
        {
            var item = await db.ScheduledEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, c);
            if (item is null) return false;
            if (item.ResponsibleOfficeDeskId != command.TargetDeskId || item.AssignedUserId != command.TargetUserId) return false;
            return await db.ScheduledEventEvents.AsNoTracking().AnyAsync(e => e.Id == reassignEventId, c);
        };

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                return (await db.ScheduledEvents.FirstOrDefaultAsync(e => e.Id == id, c))!;
            }

            var evt = await LockScheduledEventAsync(id, c);

            if (evt.Revision != command.ExpectedRevision)
                throw new ScheduleWorkflowException($"Revision conflict. Expected revision {command.ExpectedRevision}, current is {evt.Revision}.", 409);

            if (evt.Status is ScheduledEventStatus.Completed or ScheduledEventStatus.Cancelled)
                throw new ScheduleWorkflowException("Terminal scheduled events cannot be reassigned.", 400);

            var canAssign = await scheduleAuth.CanAssignScheduledEventAsync(
                evt,
                command.TargetDeskId,
                command.TargetUserId,
                callerUserId,
                c);

            if (!canAssign)
                throw new ScheduleWorkflowException("You do not have permission to assign or reassign this scheduled event.", 403);

            var sourceDeskId = evt.ResponsibleOfficeDeskId;
            var sourceUserId = evt.AssignedUserId;
            string? sourceDeskName = null;
            string? sourceUserName = null;

            if (sourceDeskId.HasValue)
            {
                sourceDeskName = await db.OfficeDesks.Where(d => d.Id == sourceDeskId.Value).Select(d => d.Name).FirstOrDefaultAsync(c);
            }
            if (sourceUserId.HasValue)
            {
                sourceUserName = await db.AppUsers.Where(u => u.Id == sourceUserId.Value).Select(u => u.DisplayName).FirstOrDefaultAsync(c);
            }

            string? targetDeskName = null;
            string? targetUserName = null;

            if (command.TargetDeskId.HasValue)
            {
                targetDeskName = await db.OfficeDesks.Where(d => d.Id == command.TargetDeskId.Value).Select(d => d.Name).FirstOrDefaultAsync(c);
            }
            if (command.TargetUserId.HasValue)
            {
                targetUserName = await db.AppUsers.Where(u => u.Id == command.TargetUserId.Value).Select(u => u.DisplayName).FirstOrDefaultAsync(c);
            }

            var now = DateTimeOffset.UtcNow;
            evt.ResponsibleOfficeDeskId = command.TargetDeskId;
            evt.AssignedUserId = command.TargetUserId;
            evt.Revision++;
            evt.LastActivityAt = now;

            var workstream = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Id == evt.WorkstreamId, c);
            var currentSeq = await db.ScheduledEventEvents.Where(e => e.ScheduledEventId == id).MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            db.ScheduledEventEvents.Add(new ScheduledEventEvent
            {
                Id = reassignEventId,
                ScheduledEventId = id,
                SequenceNumber = currentSeq + 1,
                Action = ScheduledEventAction.ResponsibilityChanged,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                WorkstreamIdSnapshot = workstream?.Id ?? evt.WorkstreamId,
                WorkstreamNameSnapshot = workstream?.Name,
                SourceDeskId = sourceDeskId,
                SourceDeskNameSnapshot = sourceDeskName,
                TargetDeskId = command.TargetDeskId,
                TargetDeskNameSnapshot = targetDeskName,
                SourceUserId = sourceUserId,
                SourceUserDisplayNameSnapshot = sourceUserName,
                TargetUserId = command.TargetUserId,
                TargetUserDisplayNameSnapshot = targetUserName,
                Reason = command.Reason
            });

            await db.SaveChangesAsync(c);
            return evt;
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 4. COMPLETE EVENT
    // ========================================================================
    public async Task<ScheduledEvent> CompleteEventAsync(
        Guid id,
        CompleteEventCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var completeEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
        {
            var item = await db.ScheduledEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, c);
            if (item is null) return false;
            if (item.Status != ScheduledEventStatus.Completed) return false;
            return await db.ScheduledEventEvents.AsNoTracking().AnyAsync(e => e.Id == completeEventId, c);
        };

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                return (await db.ScheduledEvents.FirstOrDefaultAsync(e => e.Id == id, c))!;
            }

            var evt = await LockScheduledEventAsync(id, c);

            if (evt.Revision != command.ExpectedRevision)
                throw new ScheduleWorkflowException($"Revision conflict. Expected revision {command.ExpectedRevision}, current is {evt.Revision}.", 409);

            if (evt.Status is ScheduledEventStatus.Completed or ScheduledEventStatus.Cancelled)
                throw new ScheduleWorkflowException("Scheduled event is already in a terminal state.", 400);

            var canComplete = await scheduleAuth.CanCompleteScheduledEventAsync(evt, callerUserId, c);
            if (!canComplete)
                throw new ScheduleWorkflowException("You do not have permission to complete this scheduled event.", 403);

            var now = DateTimeOffset.UtcNow;
            evt.Status = ScheduledEventStatus.Completed;
            evt.CompletedAt = now;
            evt.Revision++;
            evt.LastActivityAt = now;

            var workstream = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Id == evt.WorkstreamId, c);
            var currentSeq = await db.ScheduledEventEvents.Where(e => e.ScheduledEventId == id).MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            db.ScheduledEventEvents.Add(new ScheduledEventEvent
            {
                Id = completeEventId,
                ScheduledEventId = id,
                SequenceNumber = currentSeq + 1,
                Action = ScheduledEventAction.Completed,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                WorkstreamIdSnapshot = workstream?.Id ?? evt.WorkstreamId,
                WorkstreamNameSnapshot = workstream?.Name,
                Notes = command.Notes
            });

            await db.SaveChangesAsync(c);
            return evt;
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 5. CANCEL EVENT
    // ========================================================================
    public async Task<ScheduledEvent> CancelEventAsync(
        Guid id,
        CancelEventCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new ScheduleWorkflowException("Cancellation requires a mandatory reason.", 400);

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var cancelEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
        {
            var item = await db.ScheduledEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, c);
            if (item is null) return false;
            if (item.Status != ScheduledEventStatus.Cancelled) return false;
            return await db.ScheduledEventEvents.AsNoTracking().AnyAsync(e => e.Id == cancelEventId, c);
        };

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                return (await db.ScheduledEvents.FirstOrDefaultAsync(e => e.Id == id, c))!;
            }

            var evt = await LockScheduledEventAsync(id, c);

            if (evt.Revision != command.ExpectedRevision)
                throw new ScheduleWorkflowException($"Revision conflict. Expected revision {command.ExpectedRevision}, current is {evt.Revision}.", 409);

            if (evt.Status is ScheduledEventStatus.Completed or ScheduledEventStatus.Cancelled)
                throw new ScheduleWorkflowException("Scheduled event is already in a terminal state.", 400);

            var canCancel = await scheduleAuth.CanCancelScheduledEventAsync(evt, callerUserId, c);
            if (!canCancel)
                throw new ScheduleWorkflowException("You do not have permission to cancel this scheduled event.", 403);

            var now = DateTimeOffset.UtcNow;
            evt.Status = ScheduledEventStatus.Cancelled;
            evt.CancelledAt = now;
            evt.CancellationReason = command.Reason.Trim();
            evt.Revision++;
            evt.LastActivityAt = now;

            var workstream = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Id == evt.WorkstreamId, c);
            var currentSeq = await db.ScheduledEventEvents.Where(e => e.ScheduledEventId == id).MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            db.ScheduledEventEvents.Add(new ScheduledEventEvent
            {
                Id = cancelEventId,
                ScheduledEventId = id,
                SequenceNumber = currentSeq + 1,
                Action = ScheduledEventAction.Cancelled,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                WorkstreamIdSnapshot = workstream?.Id ?? evt.WorkstreamId,
                WorkstreamNameSnapshot = workstream?.Name,
                Reason = command.Reason.Trim()
            });

            await db.SaveChangesAsync(c);
            return evt;
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 6. ADD REMINDER
    // ========================================================================
    public async Task<ScheduledReminder> AddReminderAsync(
        Guid id,
        AddReminderCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var boundedDays = Math.Clamp(command.DaysBefore, 0, 365);
        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var reminderId = Guid.NewGuid();
        var addReminderEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
        {
            var r = await db.ScheduledReminders.AsNoTracking().FirstOrDefaultAsync(rem => rem.Id == reminderId, c);
            if (r is null) return false;
            return await db.ScheduledEventEvents.AsNoTracking().AnyAsync(e => e.Id == addReminderEventId, c);
        };

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                return (await db.ScheduledReminders.FirstOrDefaultAsync(rem => rem.Id == reminderId, c))!;
            }

            var evt = await LockScheduledEventAsync(id, c);

            if (evt.Revision != command.ExpectedRevision)
                throw new ScheduleWorkflowException($"Revision conflict. Expected revision {command.ExpectedRevision}, current is {evt.Revision}.", 409);

            if (evt.Status is ScheduledEventStatus.Completed or ScheduledEventStatus.Cancelled)
                throw new ScheduleWorkflowException("Cannot add reminders to terminal scheduled events.", 400);

            var canUpdate = await scheduleAuth.CanUpdateScheduledEventAsync(evt, callerUserId, c);
            if (!canUpdate)
                throw new ScheduleWorkflowException("You do not have permission to update reminders on this event.", 403);

            var duplicateReminder = await db.ScheduledReminders
                .AnyAsync(r => r.ScheduledEventId == id && r.DaysBefore == boundedDays && r.IsActive, c);
            if (duplicateReminder)
                throw new ScheduleWorkflowException($"An active reminder for {boundedDays} day(s) before already exists.", 400);

            var now = DateTimeOffset.UtcNow;
            var reminder = new ScheduledReminder
            {
                Id = reminderId,
                ScheduledEventId = id,
                DaysBefore = boundedDays,
                ReminderTime = command.ReminderTime,
                IsActive = true,
                CreatedByUserId = callerUserId,
                CreatedByDisplayNameSnapshot = actorDisplayName
            };

            db.ScheduledReminders.Add(reminder);

            evt.Revision++;
            evt.LastActivityAt = now;

            var workstream = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Id == evt.WorkstreamId, c);
            var currentSeq = await db.ScheduledEventEvents.Where(e => e.ScheduledEventId == id).MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            db.ScheduledEventEvents.Add(new ScheduledEventEvent
            {
                Id = addReminderEventId,
                ScheduledEventId = id,
                SequenceNumber = currentSeq + 1,
                Action = ScheduledEventAction.ReminderAdded,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                WorkstreamIdSnapshot = workstream?.Id ?? evt.WorkstreamId,
                WorkstreamNameSnapshot = workstream?.Name,
                ReminderId = reminderId,
                ReminderDaysBefore = boundedDays
            });

            await db.SaveChangesAsync(c);
            return reminder;
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 7. REMOVE REMINDER
    // ========================================================================
    public async Task<ScheduledEvent> RemoveReminderAsync(
        Guid id,
        Guid reminderId,
        RemoveReminderCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var removeReminderEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
        {
            var r = await db.ScheduledReminders.AsNoTracking().FirstOrDefaultAsync(rem => rem.Id == reminderId, c);
            if (r is { IsActive: true }) return false;
            return await db.ScheduledEventEvents.AsNoTracking().AnyAsync(e => e.Id == removeReminderEventId, c);
        };

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                return (await db.ScheduledEvents.FirstOrDefaultAsync(e => e.Id == id, c))!;
            }

            var evt = await LockScheduledEventAsync(id, c);

            if (evt.Revision != command.ExpectedRevision)
                throw new ScheduleWorkflowException($"Revision conflict. Expected revision {command.ExpectedRevision}, current is {evt.Revision}.", 409);

            if (evt.Status is ScheduledEventStatus.Completed or ScheduledEventStatus.Cancelled)
                throw new ScheduleWorkflowException("Cannot modify reminders on terminal scheduled events.", 400);

            var canUpdate = await scheduleAuth.CanUpdateScheduledEventAsync(evt, callerUserId, c);
            if (!canUpdate)
                throw new ScheduleWorkflowException("You do not have permission to update reminders on this event.", 403);

            var reminder = await db.ScheduledReminders.FirstOrDefaultAsync(r => r.Id == reminderId && r.ScheduledEventId == id, c);
            if (reminder is null)
                throw new ScheduleWorkflowException("Reminder not found.", 404);

            var daysBefore = reminder.DaysBefore;
            reminder.IsActive = false;

            var now = DateTimeOffset.UtcNow;
            evt.Revision++;
            evt.LastActivityAt = now;

            var workstream = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Id == evt.WorkstreamId, c);
            var currentSeq = await db.ScheduledEventEvents.Where(e => e.ScheduledEventId == id).MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            db.ScheduledEventEvents.Add(new ScheduledEventEvent
            {
                Id = removeReminderEventId,
                ScheduledEventId = id,
                SequenceNumber = currentSeq + 1,
                Action = ScheduledEventAction.ReminderRemoved,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                WorkstreamIdSnapshot = workstream?.Id ?? evt.WorkstreamId,
                WorkstreamNameSnapshot = workstream?.Name,
                ReminderId = reminderId,
                ReminderDaysBefore = daysBefore
            });

            await db.SaveChangesAsync(c);
            return evt;
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 8. LINK WORK ITEM
    // ========================================================================
    public async Task<ScheduledEvent> LinkWorkItemAsync(
        Guid id,
        LinkWorkItemCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var linkEventId = Guid.NewGuid();

        var workItem = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == command.WorkItemId, ct)
            ?? throw new ScheduleWorkflowException("Work item not found.", 404);

        var canViewWorkItem = await workItemAuth.CanAccessWorkItemAsync(command.WorkItemId, PermissionCodes.WorkItemView, callerUserId, ct);
        if (!canViewWorkItem)
            throw new ScheduleWorkflowException("You do not have permission to view this work item.", 403);

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
        {
            var item = await db.ScheduledEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, c);
            if (item is null || item.WorkItemId != command.WorkItemId) return false;
            return await db.ScheduledEventEvents.AsNoTracking().AnyAsync(e => e.Id == linkEventId, c);
        };

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                return (await db.ScheduledEvents.FirstOrDefaultAsync(e => e.Id == id, c))!;
            }

            var evt = await LockScheduledEventAsync(id, c);

            if (evt.Revision != command.ExpectedRevision)
                throw new ScheduleWorkflowException($"Revision conflict. Expected revision {command.ExpectedRevision}, current is {evt.Revision}.", 409);

            if (evt.Status is ScheduledEventStatus.Completed or ScheduledEventStatus.Cancelled)
                throw new ScheduleWorkflowException("Cannot link work items to terminal scheduled events.", 400);

            var canUpdate = await scheduleAuth.CanUpdateScheduledEventAsync(evt, callerUserId, c);
            if (!canUpdate)
                throw new ScheduleWorkflowException("You do not have permission to update this scheduled event.", 403);

            var now = DateTimeOffset.UtcNow;
            evt.WorkItemId = command.WorkItemId;
            evt.Revision++;
            evt.LastActivityAt = now;

            var workstream = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Id == evt.WorkstreamId, c);
            var currentSeq = await db.ScheduledEventEvents.Where(e => e.ScheduledEventId == id).MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            db.ScheduledEventEvents.Add(new ScheduledEventEvent
            {
                Id = linkEventId,
                ScheduledEventId = id,
                SequenceNumber = currentSeq + 1,
                Action = ScheduledEventAction.WorkItemLinked,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                WorkstreamIdSnapshot = workstream?.Id ?? evt.WorkstreamId,
                WorkstreamNameSnapshot = workstream?.Name,
                LinkedWorkItemId = command.WorkItemId,
                Notes = workItem.Title
            });

            await db.SaveChangesAsync(c);
            return evt;
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 9. UNLINK WORK ITEM
    // ========================================================================
    public async Task<ScheduledEvent> UnlinkWorkItemAsync(
        Guid id,
        UnlinkWorkItemCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);
        var unlinkEventId = Guid.NewGuid();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
        {
            var item = await db.ScheduledEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, c);
            if (item is null || item.WorkItemId != null) return false;
            return await db.ScheduledEventEvents.AsNoTracking().AnyAsync(e => e.Id == unlinkEventId, c);
        };

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                return (await db.ScheduledEvents.FirstOrDefaultAsync(e => e.Id == id, c))!;
            }

            var evt = await LockScheduledEventAsync(id, c);

            if (evt.Revision != command.ExpectedRevision)
                throw new ScheduleWorkflowException($"Revision conflict. Expected revision {command.ExpectedRevision}, current is {evt.Revision}.", 409);

            if (evt.Status is ScheduledEventStatus.Completed or ScheduledEventStatus.Cancelled)
                throw new ScheduleWorkflowException("Cannot modify terminal scheduled events.", 400);

            var canUpdate = await scheduleAuth.CanUpdateScheduledEventAsync(evt, callerUserId, c);
            if (!canUpdate)
                throw new ScheduleWorkflowException("You do not have permission to update this scheduled event.", 403);

            var oldWorkItemId = evt.WorkItemId;
            var now = DateTimeOffset.UtcNow;
            evt.WorkItemId = null;
            evt.Revision++;
            evt.LastActivityAt = now;

            var workstream = await db.Workstreams.AsNoTracking().FirstOrDefaultAsync(w => w.Id == evt.WorkstreamId, c);
            var currentSeq = await db.ScheduledEventEvents.Where(e => e.ScheduledEventId == id).MaxAsync(e => (int?)e.SequenceNumber, c) ?? 0;

            db.ScheduledEventEvents.Add(new ScheduledEventEvent
            {
                Id = unlinkEventId,
                ScheduledEventId = id,
                SequenceNumber = currentSeq + 1,
                Action = ScheduledEventAction.WorkItemUnlinked,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                WorkstreamIdSnapshot = workstream?.Id ?? evt.WorkstreamId,
                WorkstreamNameSnapshot = workstream?.Name,
                LinkedWorkItemId = oldWorkItemId
            });

            await db.SaveChangesAsync(c);
            return evt;
        }, verifySucceeded, ct);
    }

    // ========================================================================
    // 10. CREATE FROM COURT PROCEEDING (PROMOTION)
    // ========================================================================
    public async Task<ScheduledEvent> CreateFromCourtProceedingAsync(
        Guid courtProceedingId,
        CreateFromCourtProceedingCommand command,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        var proceeding = await db.CourtProceedings
            .Include(p => p.CourtCase)
            .FirstOrDefaultAsync(p => p.Id == courtProceedingId, ct)
            ?? throw new ScheduleWorkflowException("Court proceeding not found.", 404);

        if (!proceeding.NextDate.HasValue)
            throw new ScheduleWorkflowException("Court proceeding does not have a scheduled NextDate. Promotion to scheduled event requires an explicit NextDate.", 400);

        // Resolve active Court References workstream
        var courtWs = await db.Workstreams
            .FirstOrDefaultAsync(w => w.Code == WorkstreamCodes.CourtReferences && w.IsActive && w.RecordStatus == RecordStatus.Active, ct)
            ?? throw new ScheduleWorkflowException("Active Court References workstream not found.", 500);

        // Check duplicate active ScheduledEvents for the same CourtProceeding
        var duplicateExists = await db.ScheduledEvents.AnyAsync(
            e => e.CourtProceedingId == courtProceedingId
              && e.Status != ScheduledEventStatus.Cancelled
              && e.RecordStatus == RecordStatus.Active, ct);

        if (duplicateExists)
            throw new ScheduleWorkflowException("An active scheduled event already exists for this court proceeding.", 409);

        var canCreate = await scheduleAuth.CanCreateScheduledEventAsync(
            courtWs.Id,
            command.ResponsibleOfficeDeskId,
            command.AssignedUserId,
            callerUserId,
            ct);

        if (!canCreate)
            throw new ScheduleWorkflowException("You do not have permission to create court hearing schedules.", 403);

        var (actorDisplayName, actorDesignation) = await GetActorSnapshotAsync(callerUserId, ct);

        OfficeDesk? desk = null;
        if (command.ResponsibleOfficeDeskId.HasValue)
        {
            desk = await db.OfficeDesks.AsNoTracking().FirstOrDefaultAsync(d => d.Id == command.ResponsibleOfficeDeskId.Value, ct);
        }

        AppUser? assignedUser = null;
        if (command.AssignedUserId.HasValue)
        {
            assignedUser = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == command.AssignedUserId.Value, ct);
        }

        var eventId = Guid.NewGuid();
        var createdEventId = Guid.NewGuid();
        var reminderSpecs = (command.Reminders ?? [])
            .Select(d => new { Id = Guid.NewGuid(), Days = Math.Clamp(d, 0, 365) })
            .ToList();

        Func<CancellationToken, Task<bool>> verifySucceeded = async c =>
        {
            var exists = await db.ScheduledEvents.AsNoTracking().AnyAsync(e => e.Id == eventId, c);
            if (!exists) return false;
            return await db.ScheduledEventEvents.AsNoTracking().AnyAsync(e => e.Id == createdEventId, c);
        };

        return await ExecuteWorkflowTransactionAsync(async c =>
        {
            db.ChangeTracker.Clear();

            if (await verifySucceeded(c))
            {
                return (await db.ScheduledEvents.Include(e => e.Reminders).FirstOrDefaultAsync(e => e.Id == eventId, c))!;
            }

            // Concurrency check within transaction
            var innerDuplicate = await db.ScheduledEvents.AnyAsync(
                e => e.CourtProceedingId == courtProceedingId
                  && e.Status != ScheduledEventStatus.Cancelled
                  && e.RecordStatus == RecordStatus.Active, c);

            if (innerDuplicate)
                throw new ScheduleWorkflowException("An active scheduled event already exists for this court proceeding.", 409);

            var now = DateTimeOffset.UtcNow;
            var title = !string.IsNullOrWhiteSpace(command.Title)
                ? command.Title.Trim()
                : $"Court Hearing: {proceeding.CourtCase.CourtName} ({proceeding.CourtCase.CaseNumber})";

            var evt = new ScheduledEvent
            {
                Id = eventId,
                WorkstreamId = courtWs.Id,
                ResponsibleOfficeDeskId = command.ResponsibleOfficeDeskId,
                AssignedUserId = command.AssignedUserId,
                EventKind = ScheduledEventKind.CourtHearing,
                Title = title,
                Description = string.IsNullOrWhiteSpace(command.Description) ? proceeding.Summary : command.Description.Trim(),
                ScheduledDate = proceeding.NextDate.Value,
                ScheduledTime = null,
                Priority = command.Priority ?? ScheduledEventPriority.Urgent,
                Status = ScheduledEventStatus.Scheduled,
                Revision = 1,
                CreatedByUserId = callerUserId,
                CreatedByDisplayNameSnapshot = actorDisplayName,
                CreatedByDesignationSnapshot = actorDesignation,
                LastActivityAt = now,
                CourtCaseId = proceeding.CourtCaseId,
                CourtProceedingId = proceeding.Id,
                Origin = ScheduledEventOrigin.CourtProceeding
            };

            db.ScheduledEvents.Add(evt);

            foreach (var r in reminderSpecs)
            {
                db.ScheduledReminders.Add(new ScheduledReminder
                {
                    Id = r.Id,
                    ScheduledEventId = eventId,
                    DaysBefore = r.Days,
                    IsActive = true,
                    CreatedByUserId = callerUserId,
                    CreatedByDisplayNameSnapshot = actorDisplayName
                });
            }

            var historyEvent = new ScheduledEventEvent
            {
                Id = createdEventId,
                ScheduledEventId = eventId,
                SequenceNumber = 1,
                Action = ScheduledEventAction.Created,
                ActionAt = now,
                ActorUserId = callerUserId,
                ActorDisplayNameSnapshot = actorDisplayName,
                ActorDesignationSnapshot = actorDesignation,
                WorkstreamIdSnapshot = courtWs.Id,
                WorkstreamNameSnapshot = courtWs.Name,
                TargetDeskId = desk?.Id,
                TargetDeskNameSnapshot = desk?.Name,
                TargetUserId = assignedUser?.Id,
                TargetUserDisplayNameSnapshot = assignedUser?.DisplayName,
                NewScheduledDate = proceeding.NextDate.Value,
                Notes = $"Promoted from Court Proceeding {courtProceedingId}"
            };

            db.ScheduledEventEvents.Add(historyEvent);
            await db.SaveChangesAsync(c);

            return evt;
        }, verifySucceeded, ct);
    }
}
