using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LAC.Api;

public sealed record DhcCaptchaAnswerRequest(string Answer);
public sealed record DhcAssistedReviewRequest(bool Accept, string Reason);
public sealed record DhcAssistedStatusConfirmationRequest(string Reason);

public static class DhcAssistedEndpoints
{
    public static void MapDhcAssistedEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/dhc-assisted/preview", async (DelhiHighCourtAssistedService service,
            ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } id) return Results.Unauthorized();
            try { return Results.Ok(await service.PreviewAsync(id, ct)); }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapGet("/dhc-assisted/active", async (DelhiHighCourtAssistedCoordinator coordinator,
            DelhiHighCourtAssistedService service,
            ICourtAuthorizationService auth, ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } id) return Results.Unauthorized();
            if (!await auth.CanViewCourtReferencesAsync(id, ct)) return Results.Forbid();
            return Results.Ok(new { runId = coordinator.ActiveRunId ?? await service.RecoverableRunIdAsync(id, ct) });
        });
        group.MapPost("/dhc-assisted/runs", async (DhcAssistedStartRequest request,
            DelhiHighCourtAssistedCoordinator coordinator, ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } id) return Results.Unauthorized();
            try
            {
                var runId = await coordinator.StartAsync(id, request, ct);
                return Results.Created($"/api/court-cases/dhc-assisted/runs/{runId}", new { runId });
            }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapGet("/dhc-assisted/runs/{runId:guid}", async (Guid runId,
            DelhiHighCourtAssistedService service, ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } id) return Results.Unauthorized();
            try
            {
                var result = await service.GetRunAsync(runId, id, ct);
                var run = result.Run;
                return Results.Ok(new
                {
                    run.Id, Status = run.Status.ToString(), run.StartedAt, run.LastActivityAt,
                    run.CompletedAt, run.TotalCases, run.CompletedCases, run.UpdatedCases,
                    Phase = run.Phase.ToString(),
                    run.NoChangeCases, run.NeedsReviewCases, run.FailedCases,
                    run.CaptchaChallenges, run.FailureMessage, OwnerName = result.OwnerName,
                    IsOwner = run.StartedByUserId == id,
                    Items = result.Items.Select(x => new
                    {
                        x.Id, x.CourtCaseId, x.NormalizedCaseIdentity,
                        CaseNumber = x.CourtCase.CaseNumber,
                        x.QueueOrder, x.Reason, Status = x.Status.ToString(),
                        x.AttemptCount, x.StartedAt, x.CompletedAt, x.FailureCode, x.FailureMessage
                    })
                });
            }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapGet("/dhc-assisted/runs/{runId:guid}/captcha", async (Guid runId,
            DelhiHighCourtAssistedCoordinator coordinator, ICurrentUserContext user,
            HttpContext http, CancellationToken ct) =>
        {
            if (user.UserId is not { } id) return Results.Unauthorized();
            NoStore(http);
            try { return Results.Ok(await coordinator.ChallengeAsync(runId, id, ct)); }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapGet("/dhc-assisted/runs/{runId:guid}/captcha/image", async (Guid runId,
            DelhiHighCourtAssistedCoordinator coordinator, ICurrentUserContext user,
            HttpContext http, CancellationToken ct) =>
        {
            if (user.UserId is not { } id) return Results.Unauthorized();
            NoStore(http);
            try
            {
                var image = await coordinator.ChallengeImageAsync(runId, id, ct);
                return Results.File(image.Bytes, image.ContentType);
            }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapPost("/dhc-assisted/runs/{runId:guid}/captcha/refresh", async (Guid runId,
            DelhiHighCourtAssistedCoordinator coordinator, ICurrentUserContext user,
            HttpContext http, CancellationToken ct) =>
        {
            if (user.UserId is not { } id) return Results.Unauthorized();
            NoStore(http);
            try { return Results.Ok(await coordinator.RefreshAsync(runId, id, ct)); }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapPost("/dhc-assisted/runs/{runId:guid}/captcha", async (Guid runId,
            DhcCaptchaAnswerRequest request, DelhiHighCourtAssistedCoordinator coordinator,
            ICurrentUserContext user, HttpContext http, CancellationToken ct) =>
        {
            if (user.UserId is not { } id) return Results.Unauthorized();
            NoStore(http);
            try { return Results.Ok(new { accepted = await coordinator.SubmitHumanAnswerAsync(runId, id, request.Answer, ct) }); }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapPost("/dhc-assisted/runs/{runId:guid}/resume", async (Guid runId,
            DelhiHighCourtAssistedCoordinator coordinator, ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } id) return Results.Unauthorized();
            try { return Results.Ok(new { runId = await coordinator.ResumeAsync(runId, id, ct) }); }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapPost("/dhc-assisted/runs/{runId:guid}/cancel", async (Guid runId,
            DelhiHighCourtAssistedCoordinator coordinator, ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } id) return Results.Unauthorized();
            try { await coordinator.CancelAsync(runId, id, ct); return Results.NoContent(); }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapPost("/dhc-assisted/runs/{runId:guid}/orders", async (Guid runId,
            DelhiHighCourtAssistedCoordinator coordinator, ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } id) return Results.Unauthorized();
            try { await coordinator.StartOrdersAsync(runId, id, ct); return Results.NoContent(); }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapPost("/dhc-assisted/runs/{runId:guid}/finish", async (Guid runId,
            DelhiHighCourtAssistedCoordinator coordinator, ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } id) return Results.Unauthorized();
            try { await coordinator.FinishSessionAsync(runId, id, ct); return Results.NoContent(); }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapGet("/{id:guid}/dhc-status-observations", async (Guid id,
            DelhiHighCourtAssistedService service, ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } uid) return Results.Unauthorized();
            try
            {
                var rows = await service.StatusObservationsAsync(id, uid, ct);
                return Results.Ok(rows.Select(x => new
                {
                    x.Id, x.CourtCaseId, x.ObservedAt, x.NormalizedCaseIdentity,
                    x.RawCaseNumber, x.RawDiaryNumber, x.RawStatus, x.RawParties,
                    x.RawListingDate, x.ListingDate, x.RawCourtNumber, x.SourceUrl,
                    x.RawEvidenceText, x.EvidenceSha256, Status = x.Status.ToString(), x.ReviewReason
                }));
            }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapGet("/{id:guid}/dhc-order-observations", async (Guid id,
            DelhiHighCourtAssistedService service, ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } uid) return Results.Unauthorized();
            try
            {
                var rows = await service.OrderObservationsAsync(id, uid, ct);
                return Results.Ok(rows.Select(x => new
                {
                    x.Id, x.CourtCaseId, x.ObservedAt, x.NormalizedCaseIdentity,
                    x.RawCaseNumber, x.OrderDate, x.RawOrderDate, x.OfficialUrl,
                    x.CorrigendumUrl, x.UploadDate, x.RawUploadDate, x.RawRemark,
                    x.SourceUrl, x.RawEvidenceText, x.EvidenceSha256
                }));
            }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapGet("/dhc-assisted/reviews", async (DelhiHighCourtAssistedService service,
            ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } id) return Results.Unauthorized();
            try
            {
                var rows = await service.ReviewsAsync(id, ct);
                return Results.Ok(rows.Select(x => new
                {
                    x.Id, x.CourtCaseId, x.ObservedAt, x.RawCaseNumber, x.RawStatus,
                    x.ListingDate, x.RawCourtNumber, x.RawEvidenceText, x.ReviewReason,
                    CanonicalStatus = x.CourtCase.CurrentStatus
                }));
            }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapPost("/dhc-assisted/reviews/{id:guid}/decision", async (Guid id,
            DhcAssistedReviewRequest request, DelhiHighCourtAssistedService service,
            ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } uid) return Results.Unauthorized();
            try { await service.ReviewAsync(id, request.Accept, request.Reason, uid, ct); return Results.NoContent(); }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
        group.MapPost("/dhc-assisted/reviews/{id:guid}/confirm-canonical-status", async (Guid id,
            DhcAssistedStatusConfirmationRequest request, DelhiHighCourtAssistedService service,
            ICurrentUserContext user, CancellationToken ct) =>
        {
            if (user.UserId is not { } uid) return Results.Unauthorized();
            try { await service.ConfirmCanonicalStatusAsync(id, request.Reason, uid, ct); return Results.NoContent(); }
            catch (CourtWorkflowException ex) { return CourtEndpoints.ToProblem(ex); }
        });
    }

    private static void NoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
}
