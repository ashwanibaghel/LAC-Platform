using System.Text.RegularExpressions;
using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

public sealed class AssistantOrchestrator(IEnumerable<IAssistantAgent> agents)
{
    private readonly IAssistantAgent[] registry = agents.ToArray();
    public async Task<AssistantAgentAnswer> AnswerAsync(AssistantAgentRequest request, CancellationToken ct)
    {
        if (AssistantRoutingHints.Identity(request.Message)) return GeneralLocalAssistantAgent.IdentityAnswer(request.User);
        var supported = registry.Select(a => (Agent: a, Priority: a.Priority(request)))
            .Where(x => x.Priority >= 0).OrderByDescending(x => x.Priority).ThenBy(x => x.Agent.Code, StringComparer.Ordinal).ToArray();
        if (supported.Length == 0) throw new AssistantRequestException(400, "No registered agent supports this context.");
        return await supported[0].Agent.AnswerAsync(request, ct);
    }
}

public static class AssistantRoutingHints
{
    public static bool Identity(string text) => Regex.IsMatch(text,
        @"\b(?:my name|who am i|my designation|my role|mera naam|meri designation|mera designation|meri post)\b|मेरा नाम|मेरी पदवी|मेरा पद",
        RegexOptions.IgnoreCase);
    public static bool ExplicitGeneral(string text) => Identity(text) || !CourtIntelligenceQuestions.IsCourtQuestion(text)
        && Regex.IsMatch(text, @"^\s*(?:hello\b|hi\b|hey\b|namaste\b|नमस्ते|kaise ho|how are you|thank|thanks|good morning|good evening|translate\b|अनुवाद|explain (?:this|the) sentence|what does .+ mean)", RegexOptions.IgnoreCase);
}

public sealed class GeneralAssistantWorkspaceResolver : IAssistantWorkspaceResolver
{
    public string Module => "General";
    public Task<AssistantResolvedWorkspace> ResolveAsync(Guid userId, Guid? entityId, CancellationToken ct) =>
        entityId.HasValue ? throw new AssistantRequestException(400, "General context has no entity reference.")
            : Task.FromResult(new AssistantResolvedWorkspace(Module));
}
public sealed class CourtAssistantWorkspaceResolver(LacDbContext db, ICourtAuthorizationService authorization) : IAssistantWorkspaceResolver
{
    public string Module => "Court";
    public async Task<AssistantResolvedWorkspace> ResolveAsync(Guid userId, Guid? entityId, CancellationToken ct)
    {
        if (!entityId.HasValue || entityId == Guid.Empty) throw new AssistantRequestException(400, "Court context requires a registered case ID.");
        if (!await authorization.CanViewCourtCaseAsync(entityId.Value, userId, ct)) throw new AssistantRequestException(403, "This context is not accessible.");
        var index = await CourtIntelligenceCaseData.LoadAsync(db, entityId.Value, ct)
            ?? throw new AssistantRequestException(404, "This context is not available.");
        return new(Module, entityId, index.CaseNumber, index);
    }
}

public sealed class CourtIntelligenceAssistantAgent(IHttpClientFactory clients, LocalStoragePaths paths) : IAssistantAgent
{
    public string Code => "CourtIntelligence";
    public int Priority(AssistantAgentRequest request) => request.Workspace.CourtIndex is not null
        && !AssistantRoutingHints.ExplicitGeneral(request.Message) ? 100 : -1;
    public async Task<AssistantAgentAnswer> AnswerAsync(AssistantAgentRequest request, CancellationToken ct)
    {
        var index = request.Workspace.CourtIndex!;
        // Only recent USER intent in this context segment crosses the boundary.
        // Previous assistant output, citations and old-case text are never evidence.
        var questions = request.RecentHistory.Where(h => h.Role == "User" && h.AgentUsed == Code).TakeLast(4).Select(h => h.Text).ToArray();
        var result = await CourtIntelligenceQuestions.AskAsync(index.CaseId, request.Message, clients, ct,
            index.CaseNumber, index.Orders, paths.ExtractionRoot, new(request.User.DisplayName, request.User.Designation),
            structuredOnly: true, groundedOnly: true, deterministicOnly: true, conversationQuestions: questions);
        if (result is not IValueHttpResult value || value.Value is not JsonElement response || !response.TryGetProperty("answer", out var text))
            return AssistantAgentAnswer.Unavailable(Code, "CourtGrounded");
        var view = await CourtIntelligenceCaseData.ViewAsync(paths.ExtractionRoot, index, ct);
        var coverage = view.GetProperty("progressSummary").Clone();
        var note = response.TryGetProperty("coverageNote", out var coverageNote) ? coverageNote.GetString() : null;
        if (string.IsNullOrWhiteSpace(note) && !coverage.GetProperty("coverageComplete").GetBoolean())
            note = "This answer uses only individually verified evidence. Source coverage or review remains incomplete.";
        if (text.GetString()!.Length > 16000) return new(
            "This verified history is too long for one reply. Ask for a narrower date range or topic.", Code, "CourtGrounded", "NeedsNarrowing",
            AssistantAgentAnswer.EmptyCitations, note, coverage);
        return new(text.GetString()!, Code, "CourtGrounded", response.TryGetProperty("insufficientEvidence", out var insufficient)
            && insufficient.GetBoolean() ? "InsufficientEvidence" : "Answered", response.GetProperty("claims").Clone(),
            note, coverage);
    }
}

public sealed class GeneralLocalAssistantAgent(IHttpClientFactory clients) : IAssistantAgent
{
    public string Code => "GeneralLocal";
    public static AssistantAgentAnswer IdentityAnswer(AssistantUserContext user) => new(
        $"Aapka naam {user.DisplayName ?? "authenticated profile mein available nahi"} hai aur aapki designation {user.Designation ?? "available nahi"} hai.",
        "GeneralLocal", "AuthenticatedContext", "Answered", AssistantAgentAnswer.EmptyCitations);
    public int Priority(AssistantAgentRequest request) => AssistantRoutingHints.ExplicitGeneral(request.Message) ? 200 : 0;
    public async Task<AssistantAgentAnswer> AnswerAsync(AssistantAgentRequest request, CancellationToken ct)
    {
        if (CourtIntelligenceQuestions.IsCourtQuestion(request.Message)) return new(
            "Select an authorized Court matter to answer this from verified evidence.", Code, "GeneralLocal", "RequiresContext", AssistantAgentAnswer.EmptyCitations);
        if (AssistantRoutingHints.Identity(request.Message)) return IdentityAnswer(request.User);
        try
        {
            var turns = request.RecentHistory.Where(h => h.AgentUsed == Code).ToArray();
            var history = turns.Zip(turns.Skip(1)).Where(pair => pair.First.Role == "User" && pair.Second.Role == "Assistant")
                .Select(pair => new CourtChatTurn(pair.First.Text, pair.Second.Text)).TakeLast(4).ToArray();
            using var response = await CourtIntelligenceQuestions.PostLocalAsync(clients, "assistant/general",
                new { question = request.Message, appContext = new CourtAssistantContext(request.User.DisplayName, request.User.Designation), history }, ct);
            if (!response.IsSuccessStatusCode) return AssistantAgentAnswer.Unavailable(Code, "GeneralLocal");
            using var json = await CourtIntelligenceQuestions.ReadResponseAsync(response, ct);
            var result = json.RootElement;
            var text = result.GetProperty("answer").GetString();
            if (result.GetProperty("mode").GetString() != "GeneralLocal" || result.GetProperty("claims").GetArrayLength() != 0
                || string.IsNullOrWhiteSpace(text) || text.Length > 1200 || CourtIntelligenceQuestions.IsCourtQuestion(text))
                return AssistantAgentAnswer.Unavailable(Code, "GeneralLocal");
            return new(text, Code, "GeneralLocal", "Answered", AssistantAgentAnswer.EmptyCitations);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
        { return AssistantAgentAnswer.Unavailable(Code, "GeneralLocal"); }
    }
}
