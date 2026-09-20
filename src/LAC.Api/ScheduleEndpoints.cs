namespace LAC.Api;

using System.Linq;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

public sealed record CreateScheduledEventApiRequest(
    Guid WorkstreamId,
    Guid? ResponsibleOfficeDeskId,
    Guid? AssignedUserId,
    string EventKind,
    string Title,
    string? Description,
    DateOnly ScheduledDate,
    TimeOnly? ScheduledTime,
    string? Priority,
    Guid? MatterId = null,
    Guid? DakId = null,
    Guid? OutwardId = null,
    Guid? WorkItemId = null,
    Guid? CourtCaseId = null,
    Guid? CourtProceedingId = null,
    IReadOnlyList<int>? Reminders = null
);

public sealed record RescheduleEventApiRequest(
    DateOnly NewScheduledDate,
    TimeOnly? NewScheduledTime,
    string? Reason,
    int ExpectedRevision
);

public sealed record ReassignEventApiRequest(
    Guid? TargetDeskId,
    Guid? TargetUserId,
    string? Reason,
    int ExpectedRevision
);

public sealed record CompleteEventApiRequest(
    string? Notes,
    int ExpectedRevision
);

public sealed record CancelEventApiRequest(
    string Reason,
    int ExpectedRevision
);

public sealed record AddReminderApiRequest(
    int DaysBefore,
    TimeOnly? ReminderTime,
    int ExpectedRevision
);

public sealed record RemoveReminderApiRequest(
    int ExpectedRevision
);

public sealed record LinkWorkItemApiRequest(
    Guid WorkItemId,
    int ExpectedRevision
);

public sealed record UnlinkWorkItemApiRequest(
    int ExpectedRevision
);

public sealed record CreateFromCourtProceedingApiRequest(
    Guid? ResponsibleOfficeDeskId,
    Guid? AssignedUserId,
    string? Title,
    string? Description,
    string? Priority,
    IReadOnlyList<int>? Reminders
);

public static class ScheduleEndpoints
{
    public static RouteGroupBuilder MapScheduleEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/scheduled-events");

        // 1. Calendar / Events Query
        group.MapGet("", async (
            DateOnly? fromDate,
            DateOnly? toDate,
            string? sourceType,
            string? eventKind,
            Guid? workstreamId,
            Guid? deskId,
            string? status,
            string? priority,
            string? q,
            int? page,
            int? pageSize,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var query = new CalendarQuery(
                FromDate: fromDate,
                ToDate: toDate,
                SourceType: sourceType,
                EventKind: eventKind,
                WorkstreamId: workstreamId,
                DeskId: deskId,
                Status: status,
                Priority: priority,
                Search: q,
                Page: page ?? 1,
                PageSize: pageSize ?? 100
            );

            try
            {
                var result = await projection.GetCalendarEventsAsync(currentUser.UserId.Value, query, ct);
                return Results.Ok(result);
            }
            catch (ScheduleWorkflowException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // 2. Options for create / reassign UI
        group.MapGet("/options", async (
            IScheduleAuthorizationService auth,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var options = await auth.GetCreateOptionsAsync(currentUser.UserId.Value, ct);
            return Results.Ok(options);
        });

        // 3. Event Detail
        group.MapGet("/{id:guid}", async (
            Guid id,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var detail = await projection.GetScheduledEventDetailAsync(id, currentUser.UserId.Value, ct);
            return detail is null ? Results.NotFound(new { error = "Scheduled event not found or unauthorized." }) : Results.Ok(detail);
        });

        // 4. Create Scheduled Event
        group.MapPost("", async (
            CreateScheduledEventApiRequest req,
            IScheduleWorkflowService workflow,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();

            var kind = Enum.TryParse<ScheduledEventKind>(req.EventKind, true, out var parsedKind)
                ? parsedKind
                : ScheduledEventKind.Other;

            var priority = Enum.TryParse<ScheduledEventPriority>(req.Priority, true, out var parsedPriority)
                ? parsedPriority
                : ScheduledEventPriority.Routine;

            var command = new CreateScheduledEventCommand(
                WorkstreamId: req.WorkstreamId,
                ResponsibleOfficeDeskId: req.ResponsibleOfficeDeskId,
                AssignedUserId: req.AssignedUserId,
                EventKind: kind,
                Title: req.Title,
                Description: req.Description,
                ScheduledDate: req.ScheduledDate,
                ScheduledTime: req.ScheduledTime,
                Priority: priority,
                MatterId: req.MatterId,
                DakId: req.DakId,
                OutwardId: req.OutwardId,
                WorkItemId: req.WorkItemId,
                CourtCaseId: req.CourtCaseId,
                CourtProceedingId: req.CourtProceedingId,
                Reminders: req.Reminders
            );

            try
            {
                var evt = await workflow.CreateScheduledEventAsync(command, currentUser.UserId.Value, ct);
                var detail = await projection.GetScheduledEventDetailAsync(evt.Id, currentUser.UserId.Value, ct);
                return Results.Created($"/api/scheduled-events/{evt.Id}", detail ?? (object)new { id = evt.Id, revision = evt.Revision });
            }
            catch (ScheduleWorkflowException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // 5. Reschedule
        group.MapPost("/{id:guid}/reschedule", async (
            Guid id,
            RescheduleEventApiRequest req,
            IScheduleWorkflowService workflow,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var command = new RescheduleEventCommand(req.NewScheduledDate, req.NewScheduledTime, req.Reason, req.ExpectedRevision);

            try
            {
                var evt = await workflow.RescheduleEventAsync(id, command, currentUser.UserId.Value, ct);
                var detail = await projection.GetScheduledEventDetailAsync(evt.Id, currentUser.UserId.Value, ct);
                return Results.Ok(detail ?? (object)new { id = evt.Id, revision = evt.Revision, scheduledDate = evt.ScheduledDate, scheduledTime = evt.ScheduledTime });
            }
            catch (ScheduleWorkflowException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // 6. Reassign Responsibility
        group.MapPost("/{id:guid}/reassign", async (
            Guid id,
            ReassignEventApiRequest req,
            IScheduleWorkflowService workflow,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var command = new ReassignEventCommand(req.TargetDeskId, req.TargetUserId, req.Reason, req.ExpectedRevision);

            try
            {
                var evt = await workflow.ReassignEventAsync(id, command, currentUser.UserId.Value, ct);
                var detail = await projection.GetScheduledEventDetailAsync(evt.Id, currentUser.UserId.Value, ct);
                return Results.Ok(detail ?? (object)new { id = evt.Id, revision = evt.Revision, responsibleOfficeDeskId = evt.ResponsibleOfficeDeskId, assignedUserId = evt.AssignedUserId });
            }
            catch (ScheduleWorkflowException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // 7. Complete
        group.MapPost("/{id:guid}/complete", async (
            Guid id,
            CompleteEventApiRequest req,
            IScheduleWorkflowService workflow,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var command = new CompleteEventCommand(req.Notes, req.ExpectedRevision);

            try
            {
                var evt = await workflow.CompleteEventAsync(id, command, currentUser.UserId.Value, ct);
                var detail = await projection.GetScheduledEventDetailAsync(evt.Id, currentUser.UserId.Value, ct);
                return Results.Ok(detail ?? (object)new { id = evt.Id, status = evt.Status.ToString(), revision = evt.Revision });
            }
            catch (ScheduleWorkflowException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // 8. Cancel
        group.MapPost("/{id:guid}/cancel", async (
            Guid id,
            CancelEventApiRequest req,
            IScheduleWorkflowService workflow,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var command = new CancelEventCommand(req.Reason, req.ExpectedRevision);

            try
            {
                var evt = await workflow.CancelEventAsync(id, command, currentUser.UserId.Value, ct);
                var detail = await projection.GetScheduledEventDetailAsync(evt.Id, currentUser.UserId.Value, ct);
                return Results.Ok(detail ?? (object)new { id = evt.Id, status = evt.Status.ToString(), revision = evt.Revision });
            }
            catch (ScheduleWorkflowException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // 9. Add Reminder
        group.MapPost("/{id:guid}/reminders", async (
            Guid id,
            AddReminderApiRequest req,
            IScheduleWorkflowService workflow,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var command = new AddReminderCommand(req.DaysBefore, req.ReminderTime, req.ExpectedRevision);

            try
            {
                var reminder = await workflow.AddReminderAsync(id, command, currentUser.UserId.Value, ct);
                var detail = await projection.GetScheduledEventDetailAsync(id, currentUser.UserId.Value, ct);
                return Results.Ok(detail ?? (object)new { id = reminder.Id, daysBefore = reminder.DaysBefore, reminderTime = reminder.ReminderTime });
            }
            catch (ScheduleWorkflowException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // 10. Remove Reminder
        group.MapDelete("/{id:guid}/reminders/{reminderId:guid}", async (
            Guid id,
            Guid reminderId,
            int? expectedRevision,
            LacDbContext db,
            IScheduleWorkflowService workflow,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();

            int rev;
            if (expectedRevision.HasValue)
            {
                rev = expectedRevision.Value;
            }
            else
            {
                var current = await db.ScheduledEvents.AsNoTracking().Where(e => e.Id == id).Select(e => (int?)e.Revision).FirstOrDefaultAsync(ct);
                if (!current.HasValue) return Results.NotFound(new { error = "Scheduled event not found." });
                rev = current.Value;
            }

            var command = new RemoveReminderCommand(rev);

            try
            {
                var evt = await workflow.RemoveReminderAsync(id, reminderId, command, currentUser.UserId.Value, ct);
                var detail = await projection.GetScheduledEventDetailAsync(id, currentUser.UserId.Value, ct);
                return Results.Ok(detail ?? (object)new { id = evt.Id, revision = evt.Revision });
            }
            catch (ScheduleWorkflowException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // 11. Link Work Item
        group.MapPost("/{id:guid}/work-item", async (
            Guid id,
            LinkWorkItemApiRequest req,
            IScheduleWorkflowService workflow,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();
            var command = new LinkWorkItemCommand(req.WorkItemId, req.ExpectedRevision);

            try
            {
                var evt = await workflow.LinkWorkItemAsync(id, command, currentUser.UserId.Value, ct);
                var detail = await projection.GetScheduledEventDetailAsync(id, currentUser.UserId.Value, ct);
                return Results.Ok(detail ?? (object)new { id = evt.Id, workItemId = evt.WorkItemId, revision = evt.Revision });
            }
            catch (ScheduleWorkflowException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // 12. Unlink Work Item
        group.MapDelete("/{id:guid}/work-item", async (
            Guid id,
            int? expectedRevision,
            LacDbContext db,
            IScheduleWorkflowService workflow,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();

            int rev;
            if (expectedRevision.HasValue)
            {
                rev = expectedRevision.Value;
            }
            else
            {
                var current = await db.ScheduledEvents.AsNoTracking().Where(e => e.Id == id).Select(e => (int?)e.Revision).FirstOrDefaultAsync(ct);
                if (!current.HasValue) return Results.NotFound(new { error = "Scheduled event not found." });
                rev = current.Value;
            }

            var command = new UnlinkWorkItemCommand(rev);

            try
            {
                var evt = await workflow.UnlinkWorkItemAsync(id, command, currentUser.UserId.Value, ct);
                var detail = await projection.GetScheduledEventDetailAsync(id, currentUser.UserId.Value, ct);
                return Results.Ok(detail ?? (object)new { id = evt.Id, workItemId = evt.WorkItemId, revision = evt.Revision });
            }
            catch (ScheduleWorkflowException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // 13. Promote from Court Proceeding
        group.MapPost("/from-court-proceeding/{courtProceedingId:guid}", async (
            Guid courtProceedingId,
            CreateFromCourtProceedingApiRequest req,
            IScheduleWorkflowService workflow,
            IAttentionProjectionService projection,
            ICurrentUserContext currentUser,
            CancellationToken ct) =>
        {
            if (!currentUser.UserId.HasValue) return Results.Unauthorized();

            ScheduledEventPriority? priority = null;
            if (!string.IsNullOrWhiteSpace(req.Priority) && Enum.TryParse<ScheduledEventPriority>(req.Priority, true, out var parsedPriority))
            {
                priority = parsedPriority;
            }

            var command = new CreateFromCourtProceedingCommand(
                ResponsibleOfficeDeskId: req.ResponsibleOfficeDeskId,
                AssignedUserId: req.AssignedUserId,
                Title: req.Title,
                Description: req.Description,
                Priority: priority,
                Reminders: req.Reminders
            );

            try
            {
                var evt = await workflow.CreateFromCourtProceedingAsync(courtProceedingId, command, currentUser.UserId.Value, ct);
                var detail = await projection.GetScheduledEventDetailAsync(evt.Id, currentUser.UserId.Value, ct);
                return Results.Created($"/api/scheduled-events/{evt.Id}", detail ?? (object)new { id = evt.Id, revision = evt.Revision, courtProceedingId, scheduledDate = evt.ScheduledDate });
            }
            catch (ScheduleWorkflowException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        return api;
    }
}
