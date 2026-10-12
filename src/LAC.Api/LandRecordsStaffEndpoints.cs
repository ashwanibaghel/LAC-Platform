using System.Globalization;
using LAC.Domain;
using LAC.Infrastructure;

namespace LAC.Api;

public static class LandRecordsStaffEndpoints
{
    public static void MapLandRecordsStaffEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/awards/{id:guid}/staff-workflow", (Guid id, LandRecordsStaffWorkflowService service, CancellationToken ct) => Run(async () => Results.Ok(await service.WorkspaceAsync(id, ct)))).RequireAuthorization();
        api.MapPost("/awards/{id:guid}/metadata/preview", (Guid id, AwardMetadataInput input, LandRecordsStaffWorkflowService service, CancellationToken ct) => Run(async () => Results.Ok(new { conflicts = await service.PreviewMetadataAsync(id, input, ct) }))).RequireAuthorization();
        api.MapPut("/awards/{id:guid}/metadata", (Guid id, AwardMetadataInput input, LandRecordsStaffWorkflowService service, CancellationToken ct) => Run(async () => Results.Ok(await service.UpdateMetadataAsync(id, input, ct)))).RequireAuthorization();
        api.MapPost("/awards/{id:guid}/lifecycle", (Guid id, AwardLifecycleInput input, LandRecordsStaffWorkflowService service, CancellationToken ct) => Run(async () => Results.Ok(await service.LifecycleAsync(id, input, ct)))).RequireAuthorization();
        api.MapGet("/awards/{id:guid}/core-documents", (Guid id, LandRecordsStaffWorkflowService service, CancellationToken ct) => Run(async () => Results.Ok(await service.AttachmentsAsync(id, false, ct)))).RequireAuthorization();
        api.MapPost("/awards/{id:guid}/core-documents", (Guid id, HttpRequest request, IFormFile file, LandRecordsStaffWorkflowService service, AwardPdfExtractionService extraction, CancellationToken ct) => Run(async () =>
        {
            var input = CoreQuery(request); await using var stream = file.OpenReadStream();
            var result = await service.UploadCoreAsync(id, input, stream, file.FileName, ct);
            if (input.Mode == "UploadAndAnalyze") result = result with { JobId = (await extraction.AnalyzeAsync(result.DocumentId, id, result.VillageId, ct)).JobId };
            return Results.Created($"/api/documents/{result.DocumentId}", result);
        })).DisableAntiforgery().RequireAuthorization();
        api.MapPut("/awards/{id:guid}/core-document-links/{attachmentId:guid}", (Guid id, Guid attachmentId, AttachmentCorrectionInput input, LandRecordsStaffWorkflowService service, CancellationToken ct) => Run(async () => Results.Ok(await service.CorrectAttachmentAsync(id, attachmentId, input, ct)))).RequireAuthorization();
        api.MapPost("/award-pdf-extractions", (HttpRequest request, IFormFile file, LandRecordsStaffWorkflowService service, AwardPdfExtractionService extraction, CancellationToken ct) => Run(async () =>
        {
            var awardId = GuidQuery(request, "targetAwardId") ?? throw new StaffWorkflowException("AwardContextRequired", "Choose an Award before uploading its PDF.");
            var input = CoreQuery(request) with { Role = "Award", VillageId = GuidQuery(request, "selectedVillageId") };
            await using var stream = file.OpenReadStream(); var uploaded = await service.UploadCoreAsync(awardId, input, stream, file.FileName, ct);
            var jobId = input.Mode == "UploadAndAnalyze" ? (await extraction.AnalyzeAsync(uploaded.DocumentId, awardId, uploaded.VillageId, ct)).JobId : null;
            return Results.Created($"/api/documents/{uploaded.DocumentId}", new AwardPdfUploadResult(jobId, uploaded.DocumentId, uploaded.Duplicate));
        })).DisableAntiforgery().RequireAuthorization();
        api.MapPost("/awards/{id:guid}/notification-documents", (Guid id, HttpRequest request, IFormFile file, LandRecordsStaffWorkflowService service, CancellationToken ct) => Run(async () =>
        {
            var input = new NotificationUploadInput(RequiredGuid(request, "mutationId"), RequiredGuid(request, "villageId"), GuidQuery(request, "notificationId"),
                Text(request, "sectionType"), Text(request, "notificationNumber"), Text(request, "numberAbsentReason"), DateQuery(request, "notificationDate"), Text(request, "remarks"));
            await using var stream = file.OpenReadStream(); return Results.Created("/api/notifications", await service.UploadNotificationAsync(id, input, stream, file.FileName, ct));
        })).DisableAntiforgery().RequireAuthorization();
        api.MapPost("/notifications/{id:guid}/finalize", (Guid id, FinalizeNotificationInput input, LandRecordsStaffWorkflowService service, CancellationToken ct) => Run(async () => Results.Ok(await service.FinalizeNotificationAsync(id, input, ct)))).RequireAuthorization();
        api.MapPut("/notifications/{id:guid}/metadata", (Guid id, NotificationMetadataInput input, LandRecordsStaffWorkflowService service, CancellationToken ct) => Run(async () => Results.Ok(await service.CorrectDraftNotificationAsync(id, input, ct)))).RequireAuthorization();
        api.MapGet("/villages/{id:guid}/acquisition-timeline", (Guid id, LandRecordsStaffWorkflowService service, CancellationToken ct) => Run(async () => Results.Ok(new { events = await service.TimelineAsync(id, ct) }))).RequireAuthorization();
        api.MapPost("/villages/{id:guid}/temporary-acquisition-proceedings", (Guid id, HttpRequest request, IFormFile file, LandRecordsStaffWorkflowService service, CancellationToken ct) => Run(async () =>
        {
            var input = new TemporaryProceedingInput(RequiredGuid(request, "mutationId"), Text(request, "documentType") ?? "", Text(request, "actRegime") ?? "", Text(request, "legalSection") ?? "", Text(request, "purpose") ?? "",
                Text(request, "referenceNumber"), IntQuery(request, "durationMonths"), DateQuery(request, "legalDate"), DateQuery(request, "commencementDate"), DateQuery(request, "expiryDate"),
                Text(request, "compensationReference"), Text(request, "remarks"), GuidQuery(request, "acquisitionProjectId"), GuidQuery(request, "awardId"));
            await using var stream = file.OpenReadStream(); var result = await service.UploadTemporaryAsync(id, input, stream, file.FileName, ct);
            return Results.Created($"/api/temporary-acquisition-proceedings/{result.ProceedingId}", result);
        })).DisableAntiforgery().RequireAuthorization();
        api.MapGet("/temporary-acquisition-proceedings/{id:guid}", (Guid id, LandRecordsStaffWorkflowService service, CancellationToken ct) => Run(async () => Results.Ok(await service.TemporaryProceedingAsync(id, ct)))).RequireAuthorization();
        api.MapPost("/documents/{id:guid}/award-link-resolutions", (Guid id, AwardPdfLinkResolutionInput input, LandRecordsStaffWorkflowService service, CancellationToken ct) => Run(async () => Results.Ok(await service.ResolvePdfOwnershipAsync(id, input, ct)))).RequireAuthorization();
    }
    public static async Task<IResult> Run(Func<Task<IResult>> apply)
    {
        try { return await apply(); }
        catch (StaffWorkflowException ex) { return Results.Json(new { ex.Code, message = ex.Message, ex.Conflicts }, statusCode: ex.StatusCode); }
        catch (AwardIngestionException ex) { return ex.Code is null ? Results.Problem(ex.Message, statusCode: ex.StatusCode) : Results.Json(new { ex.Code, message = ex.Message, ex.Conflicts }, statusCode: ex.StatusCode); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException) { return Results.Json(new { code = "RevisionConflict", message = "Record changed. Refresh before saving." }, statusCode: 409); }
    }
    private static CoreUploadInput CoreQuery(HttpRequest request) => new(Text(request, "role") ?? "Award", GuidQuery(request, "villageId"), DateQuery(request, "legalDate"),
        Text(request, "referenceNumber"), Text(request, "partNumber"), Text(request, "title"), Text(request, "remarks"),
        request.Query["khasraIds"].Select(x => Guid.TryParse(x, out var id) && id != Guid.Empty ? id : throw Invalid("khasraIds")).ToArray(), Text(request, "mode") ?? "Direct");
    private static string? Text(HttpRequest request, string name) => request.Query.TryGetValue(name, out var value) ? value.ToString() : null;
    private static Guid RequiredGuid(HttpRequest request, string name) => GuidQuery(request, name) ?? throw Invalid(name);
    private static Guid? GuidQuery(HttpRequest request, string name) => Text(request, name) is not { } raw ? null : Guid.TryParse(raw, out var value) && value != Guid.Empty ? value : throw Invalid(name);
    private static DateOnly? DateQuery(HttpRequest request, string name) => Text(request, name) is not { } raw ? null : DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : throw Invalid(name);
    private static int? IntQuery(HttpRequest request, string name) => Text(request, name) is not { } raw ? null : int.TryParse(raw, out var value) ? value : throw Invalid(name);
    private static StaffWorkflowException Invalid(string name) => new("InvalidQuery", $"Invalid or missing {name}.");
}
