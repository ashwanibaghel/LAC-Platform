using System.Net.Http.Json;
using System.Text.Json;
using System.Text;

namespace LAC.Api;

public sealed record CourtChatTurn(string Question, string Answer);
public sealed record AskCourtIntelligenceRequest(string Question, IReadOnlyList<CourtChatTurn>? History = null);
public sealed record CourtAssistantContext(string? DisplayName, string? Designation);
public sealed record CourtIntelligenceKnownOrder(Guid CourtCaseId, string NormalizedCaseIdentity,
    DateOnly? OrderDate, string? OfficialUrl, string? CorrigendumUrl, DateOnly? UploadDate, Guid SourceObservationId,
    string? SourceEvidenceSha256 = null, string? SourceKind = null, string? RawCaseNumber = null,
    string? RawOrderDate = null, string? SourceUrl = null, string? RawEvidenceText = null);

public static class CourtIntelligenceQuestions
{
    public static async Task<IResult> AskAsync(Guid caseId, string question, IHttpClientFactory clients, CancellationToken ct,
        string? caseNumber = null, IReadOnlyList<CourtIntelligenceKnownOrder>? orderIndex = null,
        string? extractionRoot = null, CourtAssistantContext? appContext = null, IReadOnlyList<CourtChatTurn>? history = null)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > 600)
            return Results.BadRequest(new { error = "Please ask a question of up to 600 characters." });
        try
        {
            // Fixed literal loopback origin; the Python service retrieves only this GUID.
            var boundedHistory = history?.TakeLast(4).Where(x => x is { Question.Length: <= 600, Answer.Length: <= 1200 }).ToArray();
            JsonElement? currentView = extractionRoot is not null ? await CourtIntelligenceCaseData.ViewAsync(extractionRoot,
                new CourtIntelligenceCaseIndex(caseId, caseNumber!, orderIndex!), ct) : null;
            JsonElement? courtCoverage = currentView is { } view && view.TryGetProperty("pipelineSummary", out var summary) ? summary : null;
            using var response = await PostLocalAsync(clients, "ask", new { caseId, question, caseNumber,
                orderIndex = ProcessingSources(orderIndex), appContext, courtCoverage, history = boundedHistory }, ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 128 * 1024)
                return Unavailable();
            using var document = await ReadResponseAsync(response, ct);
            if (!document.RootElement.TryGetProperty("claims", out var claims) || claims.ValueKind != JsonValueKind.Array)
                return Unavailable();
            if (document.RootElement.TryGetProperty("mode", out var mode) && mode.GetString() == "GeneralLocal"
                && (claims.GetArrayLength() != 0 || IsCourtQuestion(question) ||
                    !document.RootElement.TryGetProperty("answer", out var generalText) ||
                    generalText.ValueKind != JsonValueKind.String || generalText.GetString()!.Length > 1200 ||
                    IsCourtQuestion(generalText.GetString()!))) return Unavailable();
            if (extractionRoot is not null)
            {
                if (document.RootElement.GetProperty("caseId").GetGuid() != caseId) return Unavailable();
                if (claims.GetArrayLength() > 0)
                {
                    // An exact-date question may process its one known source lazily.
                    // Validate citations against the artifact published by that request.
                    var artifact = await CourtIntelligenceCaseData.ViewAsync(extractionRoot,
                        new CourtIntelligenceCaseIndex(caseId, caseNumber!, orderIndex!), ct);
                    CourtIntelligenceCaseData.ValidateAnswer(document.RootElement, caseId, artifact);
                }
            }
            return Results.Ok(document.RootElement.Clone());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
            or IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException or UnauthorizedAccessException)
        {
            return Unavailable();
        }
    }

    private static bool IsCourtQuestion(string text) => System.Text.RegularExpressions.Regex.IsMatch(text,
        @"\b(court|case|matter|order|hearing|petition\w*|respondent|party|parties|lac|dhc|compensation|payment|paid|deposit|possession|reference|section|award|khasra|status|directions?|compliance|disposed|deadline|ndoh|muaw\w*|kab[zj]\w*|tarikh|tareekh)\b|केस|मामल|कोर्ट|न्यायालय|आदेश|सुनवाई|मुआव|भुगतान|कब्ज|निर्देश|याचिका|प्रतिवादी|खसरा|अवार्ड|तारीख|अनुपालन",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static async Task<IResult> RefreshAsync(CourtIntelligenceCaseIndex index, IHttpClientFactory clients, CancellationToken ct)
    {
        if (!index.Orders.Any(o => CourtIntelligenceCaseData.Eligible(index, o)))
            return Results.BadRequest(new { error = "No exact dated official DHC order source is available for this matter." });
        try
        {
            using var response = await PostLocalAsync(clients, "refresh",
                new { caseId = index.CaseId, caseNumber = index.CaseNumber, orderIndex = ProcessingSources(index.Orders) }, ct);
            if ((int)response.StatusCode == 409) return Results.Conflict(new { error = "Another local Court intelligence check is in progress." });
            if ((int)response.StatusCode != 202) return Unavailable();
            using var document = await ReadResponseAsync(response, ct);
            if (document.RootElement.GetProperty("caseId").GetGuid() != index.CaseId) return Unavailable();
            return Results.Json(document.RootElement.Clone(), statusCode: 202);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
            or IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException) { return Unavailable(); }
    }

    private static object? ProcessingSources(IReadOnlyList<CourtIntelligenceKnownOrder>? orders) => orders?.Select(source => new {
        source.CourtCaseId, source.NormalizedCaseIdentity, source.OrderDate, source.OfficialUrl, source.CorrigendumUrl,
        source.UploadDate, source.SourceObservationId, source.SourceEvidenceSha256, source.SourceKind
    }).ToArray();

    private static async Task<HttpResponseMessage> PostLocalAsync<T>(IHttpClientFactory clients, string path, T payload, CancellationToken ct)
    {
        // The bounded local Python HTTP receiver reads Content-Length. JsonContent
        // streams chunked JSON with no length; buffer the same Web JSON contract
        // before sending instead of relaxing the receiver or replaying requests.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonSerializerOptions.Web);
        if (bytes.Length > 512 * 1024) throw new InvalidDataException("Local request exceeds safety limit.");
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        using var client = clients.CreateClient("CourtCaseQuestions");
        return await client.PostAsync(path, content, ct);
    }

    private static async Task<JsonDocument> ReadResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int size;
        while ((size = await body.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + size > 128 * 1024) throw new InvalidDataException("Local response exceeds safety limit.");
            buffer.Write(chunk, 0, size);
        }
        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, new JsonDocumentOptions { MaxDepth = 32 }, ct);
    }

    private static IResult Unavailable() => Results.Json(new
    {
        error = "Question answering is temporarily unavailable. Case intelligence remains available."
    }, statusCode: 503);
}
