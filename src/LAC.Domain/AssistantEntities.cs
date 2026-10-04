namespace LAC.Domain;

// Private local conversations, not canonical office records. Never copied into
// official-record audit payloads or model training/hidden-reasoning storage.
public sealed class AssistantConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = "New conversation";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ActiveModule { get; set; } = "General";
    public Guid? ActiveEntityId { get; set; }
    public int ContextRevision { get; set; }
    public long Revision { get; set; }
    public int MessageCount { get; set; }
}

public sealed class AssistantMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public int Sequence { get; set; }
    public string Role { get; set; } = "User";
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? AgentUsed { get; set; }
    public string? Mode { get; set; }
    public string Status { get; set; } = "Answered";
    public string Module { get; set; } = "General";
    public Guid? EntityId { get; set; }
    public int ContextRevision { get; set; }
    // Public answer metadata only: citations, coverage and status. No prompts,
    // permissions, profile identifiers, tool secrets or hidden model reasoning.
    public string MetadataJson { get; set; } = "{}";
}
