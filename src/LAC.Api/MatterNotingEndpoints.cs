namespace LAC.Api;

using LAC.Infrastructure;
using LAC.Domain;

public static class MatterNotingEndpoints
{
    private static Guid Key(HttpRequest request)
    {
        if (!Guid.TryParse(request.Headers["Idempotency-Key"], out var key) || key == Guid.Empty) throw new MatterWorkflowException("A UUID Idempotency-Key is required.", 428);
        return key;
    }
    private static int Revision(HttpRequest request)
    {
        if (!int.TryParse(request.Headers.IfMatch.ToString().Trim('"'), out var revision) || revision < 0) throw new MatterWorkflowException("A saved nonnegative revision in If-Match is required.", 428);
        return revision;
    }
    private static async Task<IResult> Run(ICurrentUserContext user, Func<Guid, Task<object?>> action)
    {
        if (!user.UserId.HasValue) return Results.Unauthorized();
        try { return Results.Ok(await action(user.UserId.Value)); }
        catch (MatterWorkflowException ex) { return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode); }
        catch (DakWorkflowException ex) { return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode); }
    }
    private static object Note(LAC.Domain.MatterOfficialNote n) => new { n.Id, n.MatterId, n.Number, n.Version, n.ContentJson, n.CanonicalText, n.TextHash, n.AuthorUserId,
        n.AuthorName, n.Designation, n.DeskId, n.DeskName, n.SubmittedAt, n.SourceDakId, citations = System.Text.Json.JsonSerializer.Deserialize<object>(n.CitationsJson), n.DocumentManifestJson };

    public static void MapMatterNotingEndpoints(this RouteGroupBuilder api)
    {
        api.MapPut("/dak/{id:guid}/village-classification", (Guid id, VillageClassificationCommand cmd, MatterNotingWorkflow workflow, ICurrentUserContext user, HttpRequest request, CancellationToken ct) =>
            Run(user, async actor => await workflow.ClassifyAsync(id, cmd, actor, Key(request), ct)));
        api.MapGet("/dak/{id:guid}/matter-options", (Guid id, Guid villageId, string? search, MatterNotingWorkflow workflow, ICurrentUserContext user, CancellationToken ct) =>
            Run(user, actor => workflow.OptionsAsync(id, villageId, search, actor, ct)!));
        api.MapPost("/dak/{id:guid}/matter-workspace", (Guid id, MatterWorkspaceCommand cmd, MatterNotingWorkflow workflow, ICurrentUserContext user, HttpRequest request, CancellationToken ct) =>
            Run(user, async actor => await workflow.WorkspaceAsync(id, cmd, actor, Key(request), ct))).WithMetadata(new EndpointPermission(PermissionCodes.DakView));
        api.MapPost("/villages/{villageId:guid}/matter-workspace", (Guid villageId, NativeMatterCommand cmd, MatterNotingWorkflow workflow, ICurrentUserContext user, HttpRequest request, CancellationToken ct) =>
            Run(user, async actor => await workflow.NativeAsync(villageId, cmd, actor, Key(request), ct))).WithMetadata(new EndpointPermission(PermissionCodes.MatterCreate));
        api.MapGet("/matters/{id:guid}/noting", (Guid id, Guid? sourceDakId, MatterNotingWorkflow workflow, ICurrentUserContext user, CancellationToken ct) =>
            Run(user, actor => workflow.WorkspaceReadAsync(id, sourceDakId, actor, ct)!));
        api.MapGet("/matters/{id:guid}/noting/draft", (Guid id, MatterNotingWorkflow workflow, ICurrentUserContext user, HttpResponse response, CancellationToken ct) =>
            Run(user, async actor => { var draft = await workflow.DraftAsync(id, actor, ct); response.Headers.ETag = $"\"{draft?.Revision ?? 0}\""; response.Headers.CacheControl = "no-store";
                return draft is null ? new { revision = 0, contentJson = (string?)null, canonicalText = (string?)null, sourceDakId = (Guid?)null } : (object)draft; }));
        api.MapPut("/matters/{id:guid}/noting/draft", (Guid id, SaveWorkingNoteCommand cmd, MatterNotingWorkflow workflow, ICurrentUserContext user, HttpRequest request, HttpResponse response, CancellationToken ct) =>
            Run(user, async actor => { var draft = await workflow.SaveAsync(id, cmd, Revision(request), actor, Key(request), ct); response.Headers.ETag = $"\"{draft.Revision}\""; return draft; })).WithMetadata(new EndpointPermission(PermissionCodes.DraftEdit), new EndpointPermission(PermissionCodes.DraftCreate));
        api.MapPost("/matters/{id:guid}/noting/submit", (Guid id, SubmitWorkingNoteCommand cmd, MatterNotingWorkflow workflow, ICurrentUserContext user, HttpRequest request, CancellationToken ct) =>
            Run(user, async actor => Note(await workflow.SubmitAsync(id, cmd, Revision(request), actor, Key(request), ct)))).WithMetadata(new EndpointPermission(PermissionCodes.DraftEdit), new EndpointPermission(PermissionCodes.DraftCreate));
        api.MapPost("/matters/{id:guid}/noting/send", (Guid id, SendWorkingNoteCommand cmd, MatterNotingWorkflow workflow, ICurrentUserContext user, HttpRequest request, CancellationToken ct) =>
            Run(user, async actor => { var sent = await workflow.SendAsync(id, cmd, Revision(request), actor, Key(request), ct); return new { note = Note(sent.Note), dak = sent.Dak }; })).WithMetadata(new EndpointPermission(PermissionCodes.DraftEdit), new EndpointPermission(PermissionCodes.DraftCreate), new EndpointPermission(PermissionCodes.DakMove));
        api.MapGet("/matters/{matter:guid}/notes/{note:guid}/remarks", (Guid matter, Guid note, MatterNotingWorkflow workflow, ICurrentUserContext user, CancellationToken ct) =>
            Run(user, async actor => await workflow.RemarksAsync(matter, note, actor, ct)));
        api.MapPost("/matters/{matter:guid}/notes/{note:guid}/remarks", (Guid matter, Guid note, CreateRemarkCommand cmd, MatterNotingWorkflow workflow, ICurrentUserContext user, HttpRequest request, CancellationToken ct) =>
            Run(user, async actor => await workflow.RemarkAsync(matter, note, cmd, actor, Key(request), ct)));
        api.MapPut("/matters/{matter:guid}/notes/{note:guid}/remarks/{remark:guid}", (Guid matter, Guid note, Guid remark, ChangeRemarkCommand cmd, MatterNotingWorkflow workflow, ICurrentUserContext user, HttpRequest request, CancellationToken ct) =>
            Run(user, async actor => await workflow.ChangeRemarkAsync(matter, note, remark, cmd, actor, Key(request), ct)));
        api.MapPost("/matters/{matter:guid}/noting/draft/citations", (Guid matter, CreateCitationCommand cmd, MatterNotingWorkflow workflow, ICurrentUserContext user, HttpRequest request, CancellationToken ct) =>
            Run(user, async actor => await workflow.CiteAsync(matter, cmd, actor, Key(request), ct))).WithMetadata(new EndpointPermission(PermissionCodes.DraftEdit), new EndpointPermission(PermissionCodes.DraftCreate));
        api.MapGet("/matters/{matter:guid}/documents/{document:guid}/annotations", (Guid matter, Guid document, MatterNotingWorkflow workflow, ICurrentUserContext user, CancellationToken ct) =>
            Run(user, async actor => await workflow.AnnotationsAsync(matter, document, actor, ct)));
        api.MapPost("/matters/{matter:guid}/documents/{document:guid}/annotations", (Guid matter, Guid document, CreateAnnotationCommand cmd, MatterNotingWorkflow workflow, ICurrentUserContext user, HttpRequest request, CancellationToken ct) =>
            Run(user, async actor => await workflow.AnnotateAsync(matter, document, cmd, actor, Key(request), ct)));
    }
}
