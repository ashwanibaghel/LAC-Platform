using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

public sealed record CoreIntakeBatchItem(string FileName, CoreIntakeView? Intake, int? ErrorStatus = null, string? Error = null, string? Code = null, object? Conflicts = null);

public static class CoreDocumentIntakeEndpoints
{
    private static IResult Error(CoreIntakeException ex) => Results.Json(new { code = ex.Code, message = ex.Message, conflicts = ex.Conflicts }, statusCode: ex.StatusCode);
    public static void MapCoreDocumentIntakeEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/villages/{villageId:guid}/core-document-intake", async (Guid villageId, HttpRequest request,
            CoreDocumentIntakeService service, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.Problem("Upload multipart/form-data with a PDF file or files.", statusCode: 400);
            var form = await request.ReadFormAsync(ct);
            if (form.Files.Count is < 1 or > CoreDocumentIntakeService.MaxBatchFiles)
                return Results.Problem("Upload between 1 and 20 PDFs.", statusCode: 400);
            var batch = form.Files.Count > 1 || form.Files.Any(x => x.Name == "files");
            var results = new List<CoreIntakeBatchItem>();
            foreach (var file in form.Files)
            {
                try
                {
                    await using var source = file.OpenReadStream();
                    var intake = await service.StageAsync(villageId, source, file.FileName, ct);
                    if (!batch) return Results.Created($"/api/core-document-intakes/{intake.IntakeId}", intake);
                    results.Add(new(file.FileName, intake));
                }
                catch (CoreIntakeException ex)
                {
                    if (!batch || ex.StatusCode is 401 or 403 or 404) return Error(ex);
                    results.Add(new(file.FileName, null, ex.StatusCode, ex.Message, ex.Code, ex.Conflicts));
                }
            }
            return Results.Ok(new { items = results });
        }).DisableAntiforgery().RequireAuthorization();

        api.MapGet("/core-document-intakes/{id:guid}", async (Guid id, CoreDocumentIntakeService service, CancellationToken ct) =>
        {
            try { return Results.Ok(await service.GetAsync(id, ct)); }
            catch (CoreIntakeException ex) { return Error(ex); }
        }).RequireAuthorization();

        api.MapGet("/villages/{villageId:guid}/core-document-intakes", async (Guid villageId, int? skip, int? take, CoreDocumentIntakeService service, CancellationToken ct) =>
        {
            try { return Results.Ok(await service.ListAsync(villageId, skip ?? 0, take ?? 50, ct)); }
            catch (CoreIntakeException ex) { return Error(ex); }
        }).RequireAuthorization();

        api.MapPost("/core-document-intakes/{id:guid}/confirm", async (Guid id, CoreIntakeConfirmation request, CoreDocumentIntakeService service, CancellationToken ct) =>
        {
            try { return Results.Ok(await service.ConfirmAsync(id, request, ct)); }
            catch (CoreIntakeException ex) { return Error(ex); }
        }).RequireAuthorization();

        api.MapGet("/core-document-intakes/{id:guid}/source-evidence", async (Guid id, CoreDocumentIntakeService service, CancellationToken ct) =>
        {
            try { return Results.Ok(await service.SourceEvidenceAsync(id, ct)); }
            catch (CoreIntakeException ex) { return Error(ex); }
        }).RequireAuthorization();

        api.MapGet("/core-document-intakes/{id:guid}/file", async (Guid id, bool? download, CoreDocumentIntakeService service,
            LacDbContext db, IDocumentStorage storage, CancellationToken ct) =>
        {
            try
            {
                var intake = await service.GetAsync(id, ct);
                var document = await db.Documents.AsNoTracking().SingleAsync(x => x.Id == intake.DocumentId, ct);
                if (document.RecordStatus != RecordStatus.Active) return Results.NotFound();
                var source = await storage.OpenReadAsync(document.StoragePath, ct);
                if (source is null) return Results.NotFound();
                return Results.File(source, "application/pdf", download == true ? document.OriginalFileName : null, enableRangeProcessing: true);
            }
            catch (CoreIntakeException ex) { return Error(ex); }
        }).RequireAuthorization();
    }
}
