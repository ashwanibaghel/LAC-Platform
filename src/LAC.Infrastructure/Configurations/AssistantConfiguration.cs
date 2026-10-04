using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure.Configurations;

public static class AssistantConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        var conversations = model.Entity<AssistantConversation>();
        conversations.Property(x => x.Title).HasMaxLength(120);
        conversations.Property(x => x.ActiveModule).HasMaxLength(64);
        conversations.Property(x => x.Revision).IsConcurrencyToken();
        conversations.HasIndex(x => new { x.UserId, x.UpdatedAt });
        conversations.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        var messages = model.Entity<AssistantMessage>();
        messages.Property(x => x.Role).HasMaxLength(16);
        messages.Property(x => x.Text).HasMaxLength(16000);
        messages.Property(x => x.AgentUsed).HasMaxLength(64);
        messages.Property(x => x.Mode).HasMaxLength(64);
        messages.Property(x => x.Module).HasMaxLength(64);
        messages.Property(x => x.Status).HasMaxLength(32);
        messages.HasIndex(x => new { x.ConversationId, x.Sequence }).IsUnique();
        messages.HasIndex(x => new { x.ConversationId, x.ContextRevision, x.Sequence });
        messages.HasOne<AssistantConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
    }
}
