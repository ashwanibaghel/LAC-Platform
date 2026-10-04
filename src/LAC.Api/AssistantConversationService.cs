using System.Collections.Concurrent;
using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

public sealed class AssistantConversationGate
{
    private readonly ConcurrentDictionary<Guid, byte> active = new();
    public bool Enter(Guid id) => active.TryAdd(id, 0);
    public void Exit(Guid id) => active.TryRemove(id, out _);
}

public sealed class AssistantConversationService(LacDbContext db, ICurrentUserContext currentUser,
    IEnumerable<IAssistantWorkspaceResolver> resolvers, AssistantOrchestrator orchestrator,
    IAssistantHistoryPolicy historyPolicy, AssistantConversationGate gate)
{
    private async Task<(Guid Id, AssistantUserContext Context)> UserAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue) throw new AssistantRequestException(401, "Authentication required.");
        var profile = await db.AppUsers.AsNoTracking().Where(u => u.Id == currentUser.UserId.Value && u.IsActive && u.RecordStatus == RecordStatus.Active)
            .Select(u => new { u.Id, u.DisplayName, Designation = u.Designation == null ? null : u.Designation.Name }).SingleOrDefaultAsync(ct);
        if (profile is null) throw new AssistantRequestException(401, "Authentication required.");
        return (profile.Id, new(profile.DisplayName, profile.Designation));
    }
    private async Task<AssistantConversation> OwnedAsync(Guid id, Guid userId, CancellationToken ct) =>
        await db.AssistantConversations.SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct)
            ?? throw new AssistantRequestException(404, "Conversation not found.");
    private Task<AssistantResolvedWorkspace> ResolveAsync(Guid userId, AssistantWorkspaceContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.Module) || context.Module.Length > 64) throw new AssistantRequestException(400, "Invalid workspace module.");
        var resolver = resolvers.SingleOrDefault(r => r.Module.Equals(context.Module, StringComparison.OrdinalIgnoreCase))
            ?? throw new AssistantRequestException(400, "This workspace module is not registered.");
        return resolver.ResolveAsync(userId, context.EntityId, ct);
    }
    private async Task<AssistantResolvedWorkspace> AuthorizeHistoryAsync(AssistantConversation conversation, CancellationToken ct)
    {
        var contexts = await db.AssistantMessages.AsNoTracking().Where(m => m.ConversationId == conversation.Id)
            .Select(m => new { m.Module, m.EntityId }).Distinct().ToListAsync(ct);
        foreach (var context in contexts)
            await ResolveAsync(conversation.UserId, new(context.Module, context.EntityId), ct);
        return await ResolveAsync(conversation.UserId, new(conversation.ActiveModule, conversation.ActiveEntityId), ct);
    }
    private static AssistantVisibleContext Visible(AssistantResolvedWorkspace context) => new(context.Module, context.EntityId, context.DisplayLabel);
    private static object ConversationView(AssistantConversation c, AssistantResolvedWorkspace workspace) => new
    { c.Id, c.Title, c.CreatedAt, c.UpdatedAt, c.MessageCount, context = Visible(workspace) };

    public async Task<object> CreateAsync(CreateAssistantConversationRequest request, CancellationToken ct)
    {
        var user = await UserAsync(ct);
        if (request.Title?.Length > 120) throw new AssistantRequestException(400, "Conversation title exceeds 120 characters.");
        var workspace = await ResolveAsync(user.Id, request.Context ?? new(), ct);
        var conversation = new AssistantConversation { UserId = user.Id,
            Title = string.IsNullOrWhiteSpace(request.Title) ? "New conversation" : request.Title.Trim(),
            ActiveModule = workspace.Module, ActiveEntityId = workspace.EntityId };
        db.Add(conversation); await db.SaveChangesAsync(ct);
        return ConversationView(conversation, workspace);
    }
    public async Task<object> GetAsync(Guid id, CancellationToken ct)
    {
        var user = await UserAsync(ct); var conversation = await OwnedAsync(id, user.Id, ct);
        return ConversationView(conversation, await AuthorizeHistoryAsync(conversation, ct));
    }
    public async Task<object> ListAsync(int offset, int limit, CancellationToken ct)
    {
        if (offset < 0 || limit is < 1 or > 50) throw new AssistantRequestException(400, "Invalid conversation page.");
        var user = await UserAsync(ct);
        var page = await db.AssistantConversations.Where(c => c.UserId == user.Id)
            .OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id).Skip(offset).Take(limit + 1).ToListAsync(ct);
        var visible = new List<object>();
        foreach (var conversation in page.Take(limit))
        {
            try { visible.Add(ConversationView(conversation, await AuthorizeHistoryAsync(conversation, ct))); }
            catch (AssistantRequestException ex) when (ex.StatusCode is 403 or 404) { /* No inaccessible title/context is returned. */ }
        }
        return new { conversations = visible, hasMore = page.Count > limit, nextOffset = offset + Math.Min(page.Count, limit) };
    }
    public async Task<object> MessagesAsync(Guid id, int after, int limit, CancellationToken ct)
    {
        if (after < 0 || limit is < 1 or > 100) throw new AssistantRequestException(400, "Invalid message page.");
        var user = await UserAsync(ct); var conversation = await OwnedAsync(id, user.Id, ct);
        await AuthorizeHistoryAsync(conversation, ct);
        var page = await db.AssistantMessages.AsNoTracking().Where(m => m.ConversationId == id && m.Sequence > after)
            .OrderBy(m => m.Sequence).Take(limit + 1).ToListAsync(ct);
        var messages = page.Take(limit).Select(m => new { messageId = m.Id, m.Sequence, m.Role, text = m.Text, m.CreatedAt,
            m.AgentUsed, m.Mode, m.Status, context = new AssistantWorkspaceContext(m.Module, m.EntityId),
            metadata = JsonSerializer.Deserialize<JsonElement>(m.MetadataJson) }).ToArray();
        return new { conversationId = id, messages, hasMore = page.Count > limit,
            nextAfter = messages.Length == 0 ? after : messages[^1].Sequence };
    }
    public async Task<object> AppendAsync(Guid id, AppendAssistantMessageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 600)
            throw new AssistantRequestException(400, "Please send a message of up to 600 characters.");
        var user = await UserAsync(ct); var conversation = await OwnedAsync(id, user.Id, ct);
        if (!gate.Enter(id)) throw new AssistantRequestException(409, "This conversation is answering another message. Retry after it completes.");
        try
        {
            if (conversation.MessageCount >= 4096) throw new AssistantRequestException(400, "Start a new conversation; this thread has reached its message limit.");
            var workspace = await ResolveAsync(user.Id, request.Context ?? new(conversation.ActiveModule, conversation.ActiveEntityId), ct);
            var changed = workspace.Module != conversation.ActiveModule || workspace.EntityId != conversation.ActiveEntityId;
            var segment = conversation.ContextRevision + (changed ? 1 : 0);
            var recent = await db.AssistantMessages.AsNoTracking().Where(m => m.ConversationId == id && m.ContextRevision == segment)
                .OrderByDescending(m => m.Sequence).Take(8).ToListAsync(ct);
            var history = historyPolicy.Bound(recent.OrderBy(m => m.Sequence).Select(m => new AssistantHistoryTurn(m.Role, m.Text, m.AgentUsed)));
            var answer = await orchestrator.AnswerAsync(new(request.Message.Trim(), user.Context, workspace, history), ct);
            // Entity permission can change during slow local inference. Recheck
            // before returning or committing an answer, not just on initial bind.
            await ResolveAsync(user.Id, new(workspace.Module, workspace.EntityId), ct);
            if (string.IsNullOrWhiteSpace(answer.Answer) || answer.Answer.Length > 16000 || answer.Citations.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Invalid public assistant answer.");
            var metadata = JsonSerializer.Serialize(new { answer.Citations, answer.CoverageNote, answer.Coverage }, JsonSerializerOptions.Web);
            if (System.Text.Encoding.UTF8.GetByteCount(metadata) > 128 * 1024) throw new InvalidDataException("Assistant metadata exceeds its bound.");
            var now = DateTimeOffset.UtcNow;
            var input = new AssistantMessage { ConversationId = id, Sequence = conversation.MessageCount + 1, Role = "User", Text = request.Message.Trim(),
                CreatedAt = now, AgentUsed = answer.AgentUsed, Module = workspace.Module, EntityId = workspace.EntityId, ContextRevision = segment };
            var output = new AssistantMessage { ConversationId = id, Sequence = conversation.MessageCount + 2, Role = "Assistant", Text = answer.Answer,
                CreatedAt = now, AgentUsed = answer.AgentUsed, Mode = answer.Mode, Status = answer.Status, MetadataJson = metadata,
                Module = workspace.Module, EntityId = workspace.EntityId, ContextRevision = segment };
            conversation.ActiveModule = workspace.Module; conversation.ActiveEntityId = workspace.EntityId; conversation.ContextRevision = segment;
            conversation.Revision++; conversation.MessageCount += 2; conversation.UpdatedAt = now;
            if (conversation.MessageCount == 2 && conversation.Title == "New conversation") conversation.Title = input.Text[..Math.Min(120, input.Text.Length)];
            db.AddRange(input, output);
            // One transaction appends both messages and advances the thread. A
            // concurrency token rejects stale writers without replacing history.
            await db.SaveChangesAsync(ct);
            return new { conversationId = id, userMessageId = input.Id, messageId = output.Id, answer.Answer, answer.AgentUsed,
                answer.Mode, answer.Status, answer.Citations, answer.CoverageNote, answer.Coverage, contextUsed = Visible(workspace),
                output.CreatedAt, conversationRevision = conversation.Revision };
        }
        finally { gate.Exit(id); }
    }
}
