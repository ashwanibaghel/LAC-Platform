using System.Text.Json;

namespace LAC.Api;

public sealed record AssistantWorkspaceContext(string Module = "General", Guid? EntityId = null);
public sealed record AssistantUserContext(string? DisplayName, string? Designation);
public sealed record AssistantResolvedWorkspace(string Module, Guid? EntityId = null, string? DisplayLabel = null,
    CourtIntelligenceCaseIndex? CourtIndex = null);
public sealed record AssistantVisibleContext(string Module, Guid? EntityId, string? DisplayLabel);
public sealed record AssistantHistoryTurn(string Role, string Text, string? AgentUsed);
public sealed record AssistantAgentRequest(string Message, AssistantUserContext User,
    AssistantResolvedWorkspace Workspace, IReadOnlyList<AssistantHistoryTurn> RecentHistory);
public sealed record AssistantAgentAnswer(string Answer, string AgentUsed, string Mode, string Status,
    JsonElement Citations, string? CoverageNote = null, JsonElement? Coverage = null)
{
    public static JsonElement EmptyCitations => JsonSerializer.SerializeToElement(Array.Empty<object>());
    public static AssistantAgentAnswer Unavailable(string agent, string mode) => new(
        "The local assistant is temporarily unavailable. Your existing conversation and verified records remain available.",
        agent, mode, "Unavailable", EmptyCitations);
}
public interface IAssistantAgent
{
    string Code { get; }
    int Priority(AssistantAgentRequest request); // negative means unsupported
    Task<AssistantAgentAnswer> AnswerAsync(AssistantAgentRequest request, CancellationToken ct);
}
public interface IAssistantWorkspaceResolver
{
    string Module { get; }
    Task<AssistantResolvedWorkspace> ResolveAsync(Guid userId, Guid? entityId, CancellationToken ct);
}
// Future summarization plugs in here; it may narrow routing context, never add
// factual evidence. The initial policy has no model memory or summaries.
public interface IAssistantHistoryPolicy
{
    IReadOnlyList<AssistantHistoryTurn> Bound(IEnumerable<AssistantHistoryTurn> history);
}
public sealed class BoundedAssistantHistory : IAssistantHistoryPolicy
{
    public IReadOnlyList<AssistantHistoryTurn> Bound(IEnumerable<AssistantHistoryTurn> history) => history.TakeLast(8)
        .Select(t => t with { Text = t.Text[..Math.Min(t.Text.Length, t.Role == "User" ? 600 : 1200)] }).ToArray();
}
public sealed class AssistantRequestException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
public sealed record CreateAssistantConversationRequest(string? Title = null, AssistantWorkspaceContext? Context = null);
public sealed record AppendAssistantMessageRequest(string Message, AssistantWorkspaceContext? Context = null);
