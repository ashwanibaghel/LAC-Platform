using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LAC.Api;

public static class OnlyOfficeEndpoints
{
    public static void MapOnlyOfficeEndpoints(this RouteGroupBuilder api)
    {
        var routes = api.MapGroup("/matter-drafts");
        routes.MapGet("/{id:guid}/office-config", async (Guid id, ICurrentUserContext user,
            IMatterAuthorizationService auth, OnlyOfficeDraftService office, IOptions<OnlyOfficeOptions> options,
            HttpResponse response, CancellationToken ct) =>
        {
            if (!user.UserId.HasValue) return Results.Unauthorized();
            if (!await auth.CanAccessDraftAsync(id, PermissionCodes.DraftView, user.UserId.Value, ct)) return Results.Forbid();
            if (!options.Value.Enabled) return Results.Problem(statusCode: 503, title: "ONLYOFFICE is disabled.");
            response.Headers.CacheControl = "no-store";
            var edit = await auth.CanAccessDraftAsync(id, PermissionCodes.DraftEdit, user.UserId.Value, ct);
            try
            {
                return Results.Ok(await office.ConfigurationAsync(id, user.UserId.Value, user.DisplayName ?? "LAC user", edit, ct));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Problem(statusCode: 409, title: "Draft changed while opening. Please retry.");
            }
        });

        routes.MapGet("/{id:guid}/office-file", async (Guid id, string? token, LacDbContext db,
            OnlyOfficeTokens tokens, IDocumentStorage storage, IOptions<OnlyOfficeOptions> options,
            HttpResponse response, CancellationToken ct) =>
        {
            if (!options.Value.Enabled || string.IsNullOrEmpty(token)) return Results.Unauthorized();
            var draft = await db.MatterDrafts.AsNoTracking().Include(x => x.OfficeDocument).Include(x => x.Matter)
                .SingleOrDefaultAsync(x => x.Id == id && x.RecordStatus == RecordStatus.Active, ct);
            if (draft?.OfficeDocument is not { } doc || draft.Matter.RecordStatus != RecordStatus.Active
                || doc.RecordStatus != RecordStatus.Active || doc.Status != "Active"
                || !tokens.CanDownload(token, id, doc.Id)) return Results.Unauthorized();
            var stream = await storage.OpenReadAsync(doc.StoragePath, ct);
            if (stream is null) return Results.NotFound();
            response.Headers.CacheControl = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.Stream(stream, MatterDraftDocx.MimeType);
        }).AllowAnonymous();

        routes.MapPost("/{id:guid}/onlyoffice-callback", async (Guid id, JsonElement body, HttpRequest request,
            OnlyOfficeTokens tokens, OnlyOfficeDraftService office, IOptions<OnlyOfficeOptions> options,
            ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            if (!options.Value.Enabled) return Results.Json(new { error = 1 }, statusCode: 503);
            try
            {
                var header = request.Headers.Authorization.ToString();
                var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..]
                    : body.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                if (string.IsNullOrEmpty(token)) return Results.Json(new { error = 1 }, statusCode: 401);
                // Use only authenticated payload fields, never the untrusted outer request body.
                return Results.Json(new { error = await office.CallbackAsync(id, tokens.Verify(token), ct) });
            }
            catch (UnauthorizedAccessException) { return Results.Json(new { error = 1 }, statusCode: 401); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // No callback URL, token, document text, filesystem path or secret in logs.
                loggerFactory.CreateLogger("OnlyOfficeCallback").LogWarning(
                    "ONLYOFFICE callback failed for draft {DraftId} ({FailureType}); last committed document retained", id, ex.GetType().Name);
                return Results.Json(new { error = 1 });
            }
        }).AllowAnonymous();
    }
}
