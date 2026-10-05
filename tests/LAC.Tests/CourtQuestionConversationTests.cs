using System.Text.Json;
using LAC.Api;
using Xunit;

namespace LAC.Tests;

public sealed class CourtQuestionConversationTests
{
    private static JsonElement Answer(Guid id) => JsonSerializer.SerializeToElement(new {
        caseId = id, mode = "CourtGrounded", answer = "Never forwarded as evidence.",
        claims = new[] { new { text = "Also never stored as evidence.", source = new {
            orderDate = "2026-09-18", officialUrl = "https://delhihighcourt.nic.in/app/showlogo/test.pdf/2026" } } } });

    [Fact]
    public void Context_is_bounded_and_contains_no_assistant_prose_or_facts()
    {
        var store = new CourtQuestionConversation(TimeProvider.System);
        var user = Guid.NewGuid(); var id = Guid.NewGuid();
        for (var i = 0; i < 8; i++) store.Remember(store.Enter(user, "session", id), "question " + i, Answer(id));
        var context = store.Enter(user, "session", id).Context;
        Assert.Equal(4, context.Turns.Count);
        Assert.Equal("question 4", context.Turns[0].Question);
        var json = JsonSerializer.Serialize(context, JsonSerializerOptions.Web);
        Assert.DoesNotContain("Never forwarded", json); Assert.DoesNotContain("never stored", json);
        Assert.DoesNotContain("answer", json);
        Assert.Equal("2026-09-18", context.Turns[^1].Sources[0].OrderDate);
    }

    [Fact]
    public void A_to_B_to_A_switch_clears_context_and_rejects_late_old_answer()
    {
        var store = new CourtQuestionConversation(TimeProvider.System);
        var user = Guid.NewGuid(); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var oldLease = store.Enter(user, "session", a);
        store.Remember(oldLease, "old A question", Answer(a));
        Assert.Empty(store.Enter(user, "session", b).Context.Turns);
        Assert.Empty(store.Enter(user, "session", a).Context.Turns);
        store.Remember(oldLease, "late A response", Answer(a));
        Assert.Empty(store.Enter(user, "session", a).Context.Turns);
    }

    [Fact]
    public void User_login_session_and_case_identity_are_isolated()
    {
        var store = new CourtQuestionConversation(TimeProvider.System);
        var user = Guid.NewGuid(); var id = Guid.NewGuid();
        store.Remember(store.Enter(user, "session-a", id), "latest direction", Answer(id));
        Assert.Empty(store.Enter(user, "session-b", id).Context.Turns);
        Assert.Empty(store.Enter(Guid.NewGuid(), "session-a", id).Context.Turns);
        var lease = store.Enter(user, "session-a", id);
        store.Remember(lease, "wrong case", Answer(Guid.NewGuid()));
        Assert.Single(store.Enter(user, "session-a", id).Context.Turns);
    }

    [Fact]
    public void Assistant_output_alone_or_general_chat_cannot_create_grounded_referent()
    {
        var store = new CourtQuestionConversation(TimeProvider.System);
        var user = Guid.NewGuid(); var id = Guid.NewGuid(); var lease = store.Enter(user, "session", id);
        store.Remember(lease, "LAC action?", JsonSerializer.SerializeToElement(new {
            caseId = id, mode = "CourtGrounded", answer = "LAC must file in four weeks.", claims = Array.Empty<object>() }));
        store.Remember(lease, "hello", JsonSerializer.SerializeToElement(new {
            caseId = id, mode = "GeneralLocal", answer = "hello", claims = Array.Empty<object>() }));
        Assert.Empty(Assert.Single(store.Enter(user, "session", id).Context.Turns).Sources);
    }

    [Fact]
    public void Expired_context_is_dropped()
    {
        var clock = new Clock(); var store = new CourtQuestionConversation(clock);
        var user = Guid.NewGuid(); var id = Guid.NewGuid();
        store.Remember(store.Enter(user, "session", id), "latest", Answer(id));
        clock.Now += TimeSpan.FromMinutes(21);
        Assert.Empty(store.Enter(user, "session", id).Context.Turns);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
