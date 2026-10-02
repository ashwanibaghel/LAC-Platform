using System.Net.Http.Json;
using System.Text.Json;

namespace LAC.Api;

public sealed record AskCourtIntelligenceRequest(string Question);
public sealed record CourtIntelligenceKnownOrder(Guid CourtCaseId, string NormalizedCaseIdentity,
    DateOnly? OrderDate, string? OfficialUrl, string? CorrigendumUrl, DateOnly? UploadDate, Guid SourceObservationId);

public static class CourtIntelligenceQuestions
{
    public static async Task<IResult> AskAsync(Guid caseId, string question, IHttpClientFactory clients, CancellationToken ct,
        string? caseNumber = null, IReadOnlyList<CourtIntelligenceKnownOrder>? orderIndex = null)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > 600)
            return Results.BadRequest(new { error = "Please ask a question of up to 600 characters." });
        try
        {
            // Fixed literal loopback origin; the Python service retrieves only this GUID.
            using var response = await clients.CreateClient("CourtCaseQuestions")
                .PostAsJsonAsync("ask", new { caseId, question, caseNumber, orderIndex }, ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 128 * 1024)
                return Unavailable();
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(body, new JsonDocumentOptions { MaxDepth = 20 }, ct);
            if (!document.RootElement.TryGetProperty("claims", out var claims) || claims.ValueKind != JsonValueKind.Array)
                return Unavailable();
            return Results.Ok(document.RootElement.Clone());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return Unavailable();
        }
    }

    private static IResult Unavailable() => Results.Json(new
    {
        error = "Question answering is temporarily unavailable. Case intelligence remains available."
    }, statusCode: 503);
}
