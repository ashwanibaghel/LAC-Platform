using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

public static class AssistantEndpoints
{
    public static IServiceCollection AddAssistantFoundation(this IServiceCollection services)
    {
        services.AddScoped<IAssistantAgent, GeneralLocalAssistantAgent>();
        services.AddScoped<IAssistantAgent, CourtIntelligenceAssistantAgent>();
        services.AddScoped<IAssistantWorkspaceResolver, GeneralAssistantWorkspaceResolver>();
        services.AddScoped<IAssistantWorkspaceResolver, CourtAssistantWorkspaceResolver>();
        services.AddSingleton<IAssistantHistoryPolicy, BoundedAssistantHistory>();
        services.AddSingleton<AssistantConversationGate>();
        services.AddScoped<AssistantOrchestrator>();
        services.AddScoped<AssistantConversationService>();
        return services;
    }
    public static RouteGroupBuilder MapAssistantEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/assistant/conversations").RequireAuthorization();
        group.MapGet("", (HttpContext http, AssistantConversationService service, CancellationToken ct) =>
            Execute(http, () => service.ListAsync(Number(http, "offset", 0), Number(http, "limit", 20), ct)));
        group.MapPost("", (HttpContext http, AssistantConversationService service, CancellationToken ct) =>
            Execute(http, async () => await service.CreateAsync(await Read<CreateAssistantConversationRequest>(http, ct), ct), 201));
        group.MapGet("/{id:guid}", (Guid id, HttpContext http, AssistantConversationService service, CancellationToken ct) =>
            Execute(http, () => service.GetAsync(id, ct)));
        group.MapGet("/{id:guid}/messages", (Guid id, HttpContext http, AssistantConversationService service, CancellationToken ct) =>
            Execute(http, () => service.MessagesAsync(id, Number(http, "after", 0), Number(http, "limit", 50), ct)));
        group.MapPost("/{id:guid}/messages", (Guid id, HttpContext http, AssistantConversationService service, CancellationToken ct) =>
            Execute(http, async () => await service.AppendAsync(id, await Read<AppendAssistantMessageRequest>(http, ct), ct)));
        return group;
    }
    private static int Number(HttpContext http, string name, int fallback) => !http.Request.Query.ContainsKey(name) ? fallback
        : int.TryParse(http.Request.Query[name], out var value) ? value : throw new AssistantRequestException(400, "Invalid message page.");
    private static async Task<T> Read<T>(HttpContext http, CancellationToken ct)
    {
        using var buffer = new MemoryStream(); var chunk = new byte[1024]; int count;
        while ((count = await http.Request.Body.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + count > 8192) throw new AssistantRequestException(413, "Assistant request exceeds its bound.");
            buffer.Write(chunk, 0, count);
        }
        try { return JsonSerializer.Deserialize<T>(buffer.ToArray(), JsonSerializerOptions.Web)
            ?? throw new AssistantRequestException(400, "Invalid assistant request."); }
        catch (JsonException) { throw new AssistantRequestException(400, "Invalid assistant JSON request."); }
    }
    private static async Task<IResult> Execute(HttpContext http, Func<Task<object>> action, int status = 200)
    {
        http.Response.Headers.CacheControl = "no-store";
        try { return Results.Json(await action(), statusCode: status); }
        catch (AssistantRequestException ex) { return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "The conversation changed; reload before retrying." }); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException or DbUpdateException or JsonException)
        { return Results.Json(new { error = "Assistant storage or verified context is temporarily unavailable." }, statusCode: 503); }
    }
}
