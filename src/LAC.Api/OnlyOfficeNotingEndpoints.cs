namespace LAC.Api;

using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;

public static class OnlyOfficeNotingEndpoints
{
    private static Guid Key(HttpRequest r) => Guid.TryParse(r.Headers["Idempotency-Key"], out var key) && key != Guid.Empty ? key : throw new MatterWorkflowException("A UUID Idempotency-Key is required.", 428);
    private static int Revision(HttpRequest r) => int.TryParse(r.Headers.IfMatch.ToString().Trim('"'), out var n) && n >= 0 ? n : throw new MatterWorkflowException("A quoted nonnegative If-Match revision is required.", 428);
    private static async Task<IResult> Run(ICurrentUserContext user, HttpResponse response, Func<Guid, Task<object?>> action)
    {
        response.Headers.CacheControl = "no-store";
        if (!user.UserId.HasValue) return Results.Unauthorized();
        try { return Results.Ok(await action(user.UserId.Value)); }
        catch (MatterWorkflowException e) { return Results.Json(new { message = e.Message }, statusCode: e.StatusCode); }
        catch (DakWorkflowException e) { return Results.Json(new { message = e.Message }, statusCode: e.StatusCode); }
        catch (HttpRequestException) { return Results.Json(new { message = "Document Server is unavailable; last committed content is retained." }, statusCode: 503); }
        catch (TaskCanceledException) { return Results.Json(new { message = "Document Server operation timed out; no save or submission success is implied." }, statusCode: 503); }
    }
    private static async Task<IResult> SignedRead(HttpResponse response, Func<Task<Stream>> read, string mime)
    {
        response.Headers.CacheControl = "no-store"; response.Headers["X-Content-Type-Options"] = "nosniff";
        try { return Results.Stream(await read(), mime); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        catch (Exception e) when (e is KeyNotFoundException or JsonException or FormatException or InvalidOperationException) { return Results.Unauthorized(); }
        catch (MatterWorkflowException e) { return Results.Json(new { message = e.Message }, statusCode: e.StatusCode); }
    }
    public static void MapOnlyOfficeNotingEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/matters/{matter:guid}/noting/office/sessions", (Guid matter, OpenOfficeNoteCommand cmd, OnlyOfficeNotingService service, ICurrentUserContext user, HttpRequest request, HttpResponse response, CancellationToken ct) =>
            Run(user, response, async actor => await service.OpenAsync(matter, cmd, Revision(request), actor, Key(request), ct)));
        api.MapGet("/noting-office/sessions/{session:guid}/config", (Guid session, OnlyOfficeNotingService service, ICurrentUserContext user, HttpResponse response, CancellationToken ct) =>
            Run(user, response, actor => service.ConfigAsync(session, actor, ct)!));
        api.MapGet("/noting-office/sessions/{session:guid}/file", (Guid session, string? token, OnlyOfficeNotingService service, HttpResponse response, CancellationToken ct) =>
            token is null ? Task.FromResult<IResult>(Results.Unauthorized()) : SignedRead(response, () => service.FileAsync(session, token, ct), MatterDraftDocx.MimeType)).AllowAnonymous();
        api.MapPost("/noting-office/sessions/{session:guid}/callback", async (Guid session, HttpRequest request, OnlyOfficeTokens tokens, OnlyOfficeNotingService service, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            try
            {
                using var input = new MemoryStream();
                var buffer = new byte[8192]; int count;
                while ((count = await request.Body.ReadAsync(buffer, ct)) > 0)
                {
                    if (input.Length + count > 128 * 1024) return Results.Json(new { error = 1 }, statusCode: 413);
                    await input.WriteAsync(buffer.AsMemory(0, count), ct);
                }
                input.Position = 0;
                using var json = await JsonDocument.ParseAsync(input, cancellationToken: ct);
                var body = json.RootElement;
                var header = request.Headers.Authorization.ToString();
                var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : body.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                if (string.IsNullOrEmpty(token)) return Results.Json(new { error = 1 }, statusCode: 401);
                return Results.Json(new { error = await service.CallbackAsync(session, tokens.Verify(token), ct) });
            }
            catch (UnauthorizedAccessException) { return Results.Json(new { error = 1 }, statusCode: 401); }
            catch (BadHttpRequestException e) when (e.StatusCode == 413) { return Results.Json(new { error = 1 }, statusCode: 413); }
            catch (JsonException) { return Results.Json(new { error = 1 }, statusCode: 400); }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                loggerFactory.CreateLogger("OfficialOfficeCallback").LogWarning("Official Office callback rejected for {SessionId} ({FailureType}); committed history retained", session, e.GetType().Name);
                return Results.Json(new { error = 1 });
            }
        }).AllowAnonymous();
        api.MapPost("/noting-office/sessions/{session:guid}/saves", (Guid session, SaveOfficeNoteCommand cmd, OnlyOfficeNotingService service, ICurrentUserContext user, HttpRequest request, HttpResponse response, CancellationToken ct) =>
            Run(user, response, async actor => await service.SaveAsync(session, cmd, Revision(request), actor, Key(request), ct)));
        api.MapGet("/noting-office/sessions/{session:guid}/saves/{save:guid}", (Guid session, Guid save, OnlyOfficeNotingService service, ICurrentUserContext user, HttpResponse response, CancellationToken ct) =>
            Run(user, response, async actor => await service.StatusAsync(session, save, actor, ct)));
        api.MapPost("/noting-office/sessions/{session:guid}/save-check", (Guid session, CheckOfficeSaveCommand cmd, OnlyOfficeNotingService service, ICurrentUserContext user, HttpRequest request, HttpResponse response, CancellationToken ct) =>
            Run(user,response,async actor=>await service.CheckSaveAsync(session,cmd,Revision(request),actor,Key(request),ct)));
        api.MapGet("/noting-office/sessions/{session:guid}/preview", (Guid session, OnlyOfficeNotingService service, ICurrentUserContext user, HttpResponse response, CancellationToken ct) =>
            Run(user,response,actor=>service.FrozenPreviewAsync(session,actor,ct)!));
        api.MapPost("/noting-office/sessions/{session:guid}/previews", (Guid session, PreviewOfficeNoteCommand cmd, OnlyOfficeNotingService service, ICurrentUserContext user, HttpRequest request, HttpResponse response, CancellationToken ct) =>
            Run(user, response, async actor => await service.PreviewAsync(session, cmd, Revision(request), actor, Key(request), ct)));
        api.MapPost("/noting-office/sessions/{session:guid}/revoke", (Guid session, OnlyOfficeNotingService service, ICurrentUserContext user, HttpRequest request, HttpResponse response, CancellationToken ct) =>
            Run(user, response, async actor => await service.RevokeAsync(session, Revision(request), actor, Key(request), ct)));
        api.MapPost("/matters/{matter:guid}/noting/office/confirm", (Guid matter, ConfirmOfficeNoteCommand cmd, OnlyOfficeNotingService service, ICurrentUserContext user, HttpRequest request, HttpResponse response, CancellationToken ct) =>
            Run(user, response, async actor => await service.ConfirmAsync(matter, cmd, Revision(request), actor, Key(request), ct)));
        api.MapPost("/matters/{matter:guid}/noting/green-sheets", (Guid matter, CreateGreenSheetCommand cmd, GreenSheetService service, ICurrentUserContext user, HttpRequest request, HttpResponse response, CancellationToken ct) =>
            Run(user, response, actor => service.CreateAsync(matter, cmd, actor, Key(request), ct)!));
        api.MapGet("/noting-green-sheets/{id:guid}", (Guid id, GreenSheetService service, ICurrentUserContext user, HttpResponse response, CancellationToken ct) =>
            Run(user, response, actor => service.MetadataAsync(id, actor, ct)!));
        api.MapGet("/noting-green-sheets/{id:guid}/pdf", async (Guid id, GreenSheetService service, ICurrentUserContext user, HttpResponse response, CancellationToken ct) =>
        {
            if (!user.UserId.HasValue) return Results.Unauthorized();
            return await SignedRead(response, () => service.PdfAsync(id, user.UserId.Value, ct), "application/pdf");
        });
        api.MapGet("/noting-green-sources/{id:guid}/file", (Guid id, string? token, GreenSheetService service, HttpResponse response, CancellationToken ct) =>
            token is null ? Task.FromResult<IResult>(Results.Unauthorized()) : SignedRead(response, () => service.SourceFileAsync(id, token, ct), MatterDraftDocx.MimeType)).AllowAnonymous();
    }
}
