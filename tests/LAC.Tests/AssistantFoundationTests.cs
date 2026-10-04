using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LAC.Tests;

public sealed class AssistantFoundationTests
{
    [Fact]
    public async Task Conversation_persists_both_turns_and_full_visible_thread_across_requests()
    {
        using var env = new Harness(); var id = await env.Create();
        var first = await env.Ask(id, "hello"); var second = await env.Ask(id, "how are you");
        Assert.NotEqual(first.GetProperty("messageId").GetGuid(), second.GetProperty("messageId").GetGuid());
        using var response = await env.Client.GetAsync($"/api/assistant/conversations/{id}/messages");
        response.EnsureSuccessStatusCode(); var thread = await response.Content.ReadFromJsonAsync<JsonElement>();
        var messages = thread.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(new[] { "User", "Assistant", "User", "Assistant" }, messages.Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("hello", messages[0].GetProperty("text").GetString());
        Assert.Equal("Hello!", messages[1].GetProperty("text").GetString());
        Assert.Equal(4, messages.Length); Assert.False(thread.GetProperty("hasMore").GetBoolean());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        using var get = await env.Client.GetAsync($"/api/assistant/conversations/{id}");
        var conversation = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, conversation.GetProperty("messageCount").GetInt32());
        Assert.False(conversation.TryGetProperty("userId", out _));
    }

    [Fact]
    public async Task Authenticated_identity_comes_from_live_profile_without_model_call_or_client_override()
    {
        using var env = new Harness();
        using (var scope = env.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var user = await db.AppUsers.Include(u => u.Designation).SingleAsync(u => u.Id == SeedData.BootstrapAdminId);
            user.DisplayName = "Ashwani Baghel";
            var designation = new Designation { Code = "ASSISTANT_TEST_ADM", Name = "Additional District Magistrate" };
            db.Add(designation); user.Designation = designation; await db.SaveChangesAsync();
        }
        var id = await env.Create();
        using var response = await env.Client.PostAsJsonAsync($"/api/assistant/conversations/{id}/messages", new
        { message = "mera naam kya hai?", userId = Guid.NewGuid(), appContext = new { displayName = "Forged identity", designation = "Forged designation" } });
        response.EnsureSuccessStatusCode(); var reply = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Aapka naam Ashwani Baghel hai aur aapki designation Additional District Magistrate hai.", reply.GetProperty("answer").GetString());
        Assert.Equal("AuthenticatedContext", reply.GetProperty("mode").GetString()); Assert.Empty(env.Transport.Requests);
        Assert.DoesNotContain(SeedData.BootstrapAdminId.ToString(), reply.GetRawText());
    }

    [Fact]
    public async Task Court_adapter_reuses_verified_engine_and_sends_only_user_intent_for_follow_up()
    {
        using var env = new Harness(); var court = await env.Register(42); var id = await env.Create(court.Id);
        var first = await env.Ask(id, "Latest order me LAC ko kya karna hai?");
        var second = await env.Ask(id, "Iski deadline kya hai?");
        Assert.Equal("CourtIntelligence", first.GetProperty("agentUsed").GetString());
        Assert.Equal("CourtGrounded", second.GetProperty("mode").GetString());
        Assert.Single(second.GetProperty("citations").EnumerateArray());
        var calls = env.Transport.Requests.ToArray(); Assert.Equal(2, calls.Length);
        Assert.All(calls, c => { Assert.Equal("/ask", c.Path); Assert.True(c.Body.GetProperty("groundedOnly").GetBoolean());
            Assert.True(c.Body.GetProperty("structuredOnly").GetBoolean()); Assert.True(c.Body.GetProperty("deterministicOnly").GetBoolean()); });
        Assert.Equal("Latest order me LAC ko kya karna hai?", Assert.Single(calls[1].Body.GetProperty("conversationQuestions").EnumerateArray()).GetString());
        Assert.Equal(JsonValueKind.Null, calls[1].Body.GetProperty("history").ValueKind);
        Assert.Equal(court.CaseNumber, second.GetProperty("contextUsed").GetProperty("displayLabel").GetString());
    }

    [Fact]
    public async Task Stored_assistant_answer_cannot_replace_missing_current_verified_evidence()
    {
        using var env = new Harness(); var court = await env.Register(42); var id = await env.Create(court.Id);
        await env.Ask(id, "Latest Court directions?");
        await env.Write(court.Id, Artifact(court.Id, 42, false));
        var reply = await env.Ask(id, "Iski deadline kya hai?");
        Assert.Equal("Unavailable", reply.GetProperty("status").GetString());
        Assert.Empty(reply.GetProperty("citations").EnumerateArray());
        Assert.DoesNotContain("file a status report", reply.GetProperty("answer").GetString());
        var last = env.Transport.Requests.Last().Body;
        Assert.Equal(JsonValueKind.Null, last.GetProperty("history").ValueKind);
        Assert.DoesNotContain("file a status report", last.GetProperty("conversationQuestions").GetRawText());
    }

    [Fact]
    public async Task Case_A_to_B_and_back_never_reuses_previous_context_segment()
    {
        using var env = new Harness(); var a = await env.Register(42); var b = await env.Register(43);
        var id = await env.Create(a.Id); await env.Ask(id, "Latest Court directions?");
        var next = await env.Ask(id, "Aur Court ne kya kaha?", new("Court", b.Id));
        Assert.Equal(b.Id, next.GetProperty("contextUsed").GetProperty("entityId").GetGuid());
        Assert.Contains("43", next.GetProperty("citations")[0].GetProperty("source").GetProperty("officialUrl").GetString());
        Assert.Empty(env.Transport.Requests.Last().Body.GetProperty("conversationQuestions").EnumerateArray());
        await env.Ask(id, "Us order me kya hua?", new("Court", a.Id));
        Assert.Empty(env.Transport.Requests.Last().Body.GetProperty("conversationQuestions").EnumerateArray());
    }

    [Fact]
    public async Task Entity_authorization_is_revalidated_for_answers_and_saved_history()
    {
        using var env = new Harness(); var court = await env.Register(42); var id = await env.Create(court.Id);
        await env.Ask(id, "Latest Court directions?"); await env.DenyCourt();
        foreach (var suffix in new[] { "", "/messages" })
        {
            using var get = await env.Client.GetAsync($"/api/assistant/conversations/{id}{suffix}"); Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        }
        using var ask = await env.Client.PostAsJsonAsync($"/api/assistant/conversations/{id}/messages", new { message = "Aur Court ne kya kaha?" });
        Assert.Equal(HttpStatusCode.Forbidden, ask.StatusCode); Assert.Single(env.Transport.Requests);
        using var create = await env.Client.PostAsJsonAsync("/api/assistant/conversations", new { context = new { module = "Court", entityId = court.Id } });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Permission_revoked_during_inference_prevents_commit_and_return_of_answer()
    {
        using var env = new Harness(); var court = await env.Register(42); var id = await env.Create(court.Id);
        env.Transport.BeforeAnswer = env.DenyCourt;
        using var ask = await env.Client.PostAsJsonAsync($"/api/assistant/conversations/{id}/messages", new { message = "Latest Court directions?" });
        Assert.Equal(HttpStatusCode.Forbidden, ask.StatusCode);
        using var scope = env.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        Assert.Empty(await db.AssistantMessages.ToListAsync());
    }

    [Fact]
    public async Task Conversation_owner_boundary_rejects_a_known_other_users_guid()
    {
        using var env = new Harness(); var id = await env.Create();
        using (var scope = env.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var owner = new AppUser { Username = "other", NormalizedUsername = "OTHER", DisplayName = "Other user" };
            db.Add(owner); (await db.AssistantConversations.SingleAsync(c => c.Id == id)).UserId = owner.Id; await db.SaveChangesAsync();
        }
        using var get = await env.Client.GetAsync($"/api/assistant/conversations/{id}/messages"); Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        using var ask = await env.Client.PostAsJsonAsync($"/api/assistant/conversations/{id}/messages", new { message = "hello" }); Assert.Equal(HttpStatusCode.NotFound, ask.StatusCode);
        Assert.Empty(env.Transport.Requests);
    }

    [Fact]
    public async Task General_mode_rejects_case_facts_and_rejects_model_generated_judicial_claims()
    {
        using var env = new Harness(); var id = await env.Create();
        var contextRequired = await env.Ask(id, "What did Court order?");
        Assert.Equal("RequiresContext", contextRequired.GetProperty("status").GetString()); Assert.Empty(env.Transport.Requests);
        env.Transport.GeneralText = "The Court ordered compensation payment.";
        var rejected = await env.Ask(id, "hello");
        Assert.Equal("Unavailable", rejected.GetProperty("status").GetString()); Assert.Empty(rejected.GetProperty("citations").EnumerateArray());
    }

    [Fact]
    public async Task Explicit_greeting_in_Court_context_uses_general_without_Court_history_or_labels_from_client()
    {
        using var env = new Harness(); var court = await env.Register(42); var id = await env.Create(court.Id);
        await env.Ask(id, "Latest Court directions?");
        using var response = await env.Client.PostAsJsonAsync($"/api/assistant/conversations/{id}/messages", new
        { message = "hello", context = new { module = "Court", entityId = court.Id, displayLabel = "Forged case label" } });
        response.EnsureSuccessStatusCode(); var greeting = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("GeneralLocal", greeting.GetProperty("agentUsed").GetString());
        Assert.Equal(court.CaseNumber, greeting.GetProperty("contextUsed").GetProperty("displayLabel").GetString());
        Assert.Empty(env.Transport.Requests.Last().Body.GetProperty("history").EnumerateArray());
        Assert.False(env.Transport.Requests.Last().Body.TryGetProperty("orderIndex", out _));
    }

    [Fact]
    public async Task Bounded_history_and_paging_preserve_all_messages_without_sending_an_unbounded_thread()
    {
        using var env = new Harness(); var id = await env.Create();
        for (var i = 0; i < 7; i++) await env.Ask(id, "hello turn " + i);
        Assert.All(env.Transport.Requests, r => Assert.InRange(r.Body.GetProperty("history").GetArrayLength(), 0, 4));
        Assert.Equal(4, env.Transport.Requests.Last().Body.GetProperty("history").GetArrayLength());
        var after = 0; var total = 0;
        while (true)
        {
            var page = await env.Client.GetFromJsonAsync<JsonElement>($"/api/assistant/conversations/{id}/messages?after={after}&limit=3");
            total += page.GetProperty("messages").GetArrayLength(); after = page.GetProperty("nextAfter").GetInt32();
            if (!page.GetProperty("hasMore").GetBoolean()) break;
        }
        Assert.Equal(14, total);
        var bounded = new BoundedAssistantHistory().Bound(Enumerable.Range(0, 20).Select(i => new AssistantHistoryTurn("User", new string('x', 700), "dummy")));
        Assert.Equal(8, bounded.Count); Assert.All(bounded, t => Assert.Equal(600, t.Text.Length));
    }

    [Fact]
    public async Task A_future_registered_agent_and_resolver_need_no_database_or_router_switch_change()
    {
        using var env = new Harness(future: true);
        using var create = await env.Client.PostAsJsonAsync("/api/assistant/conversations", new { context = new { module = "FutureTest" } });
        create.EnsureSuccessStatusCode(); var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var answer = await env.Ask(id, "test future capability"); Assert.Equal("FutureTest", answer.GetProperty("agentUsed").GetString());
        var identity = await env.Ask(id, "who am i?"); Assert.Equal("AuthenticatedContext", identity.GetProperty("mode").GetString());
        Assert.Empty(env.Transport.Requests);
    }

    [Fact]
    public async Task Same_conversation_concurrent_append_returns_conflict_without_overwriting_or_extra_model_call()
    {
        using var env = new Harness(); var id = await env.Create();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        env.Transport.BeforeAnswer = async () => { started.SetResult(); await release.Task; };
        var first = env.Client.PostAsJsonAsync($"/api/assistant/conversations/{id}/messages", new { message = "hello" });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var second = await env.Client.PostAsJsonAsync($"/api/assistant/conversations/{id}/messages", new { message = "how are you" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode); release.SetResult();
        using var completed = await first; completed.EnsureSuccessStatusCode(); Assert.Single(env.Transport.Requests);
    }

    [Fact]
    public async Task Conversation_list_exposes_only_owned_currently_accessible_threads()
    {
        using var env = new Harness(); var general = await env.Create(); var court = await env.Register(42);
        await env.Create(court.Id); await env.DenyCourt();
        var list = await env.Client.GetFromJsonAsync<JsonElement>("/api/assistant/conversations");
        var visible = Assert.Single(list.GetProperty("conversations").EnumerateArray());
        Assert.Equal(general, visible.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Request_context_and_message_bounds_fail_closed()
    {
        using var env = new Harness(); var id = await env.Create();
        using var unknown = await env.Client.PostAsJsonAsync($"/api/assistant/conversations/{id}/messages", new { message = "hello", context = new { module = "Award", entityId = Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using var missing = await env.Client.PostAsJsonAsync($"/api/assistant/conversations/{id}/messages", new { message = "hello", context = new { module = "Court" } });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var tooLong = await env.Client.PostAsJsonAsync($"/api/assistant/conversations/{id}/messages", new { message = new string('x', 601) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        using var oversized = await env.Client.PostAsync($"/api/assistant/conversations/{id}/messages", new StringContent("{\"message\":\"" + new string('x', 9000) + "\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode); Assert.Empty(env.Transport.Requests);
    }

    private static string Url(int number) => $"https://delhihighcourt.nic.in/app/showlogo/assistant-{number}2026.pdf/2026";
    private static JsonObject Artifact(Guid id, int number, bool facts = true) => new()
    {
        ["version"] = 1, ["caseId"] = id.ToString(), ["caseNumber"] = $"W.P.(C) {number}/2026", ["status"] = "Validated", ["processingComplete"] = true,
        ["currentPosition"] = new JsonArray(), ["beforeNextHearing"] = new JsonArray(),
        ["orders"] = new JsonArray(new JsonObject { ["orderDate"] = "2026-01-01", ["officialUrl"] = Url(number), ["status"] = "Validated",
            ["facts"] = facts ? new JsonArray(new JsonObject { ["value"] = "The LAC shall file a status report.", ["evidence"] = "The LAC shall file a status report.",
                ["page"] = 1, ["category"] = "COURT_DIRECTION", ["field"] = "direction", ["scope"] = "Current" }) : new JsonArray() })
    };
    private sealed class FutureAgent : IAssistantAgent
    {
        public string Code => "FutureTest";
        public int Priority(AssistantAgentRequest r) => r.Workspace.Module == Code ? 300 : -1;
        public Task<AssistantAgentAnswer> AnswerAsync(AssistantAgentRequest r, CancellationToken ct) => Task.FromResult(new AssistantAgentAnswer("Registered future capability.", Code, Code, "Answered", AssistantAgentAnswer.EmptyCitations));
    }
    private sealed class FutureResolver : IAssistantWorkspaceResolver
    {
        public string Module => "FutureTest";
        public Task<AssistantResolvedWorkspace> ResolveAsync(Guid userId, Guid? entityId, CancellationToken ct) => Task.FromResult(new AssistantResolvedWorkspace(Module));
    }
    private sealed class Harness : IDisposable
    {
        private readonly ApiFactory baseline = new();
        private readonly string root = Path.Combine(Path.GetTempPath(), "assistant-test-" + Guid.NewGuid());
        public CaptureTransport Transport { get; } = new();
        public WebApplicationFactory<Program> Factory { get; }
        public HttpClient Client { get; }
        public Harness(bool future = false)
        {
            Factory = baseline.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> {
                    ["Storage:ExtractionRoot"] = root, ["Storage:DocumentRoot"] = Path.Combine(root, "documents"),
                    ["Storage:BackupRoot"] = Path.Combine(root, "backups"), ["BackgroundWorkers:Enabled"] = "false" }));
                builder.ConfigureServices(s => { s.RemoveAll<IHttpClientFactory>(); s.AddSingleton<IHttpClientFactory>(Transport);
                    if (future) { s.AddScoped<IAssistantAgent, FutureAgent>(); s.AddScoped<IAssistantWorkspaceResolver, FutureResolver>(); } });
            }); Client = Factory.CreateClient();
        }
        public async Task<Guid> Create(Guid? court = null)
        {
            using var result = await Client.PostAsJsonAsync("/api/assistant/conversations", new { context = new AssistantWorkspaceContext(court.HasValue ? "Court" : "General", court) });
            Assert.Equal(HttpStatusCode.Created, result.StatusCode); return (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        public async Task<JsonElement> Ask(Guid id, string message, AssistantWorkspaceContext? context = null)
        {
            using var result = await Client.PostAsJsonAsync($"/api/assistant/conversations/{id}/messages", new { message, context });
            result.EnsureSuccessStatusCode(); return await result.Content.ReadFromJsonAsync<JsonElement>();
        }
        public async Task<CourtCase> Register(int number)
        {
            using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var court = new CourtCase { CaseNumber = $"W.P.(C) {number}/2026", CourtName = "Delhi High Court", CurrentStatus = "Pending" };
            db.Add(court); db.Add(new CourtExternalOrderObservation { CourtCaseId = court.Id, RunItemId = Guid.NewGuid(),
                NormalizedCaseIdentity = $"delhihighcourt|wpc|{number}|2026", OrderDate = new(2026, 1, 1), OfficialUrl = Url(number),
                EvidenceSha256 = "fixture", RawEvidenceText = "fixture", SourceUrl = "https://delhihighcourt.nic.in/" });
            await db.SaveChangesAsync(); await Write(court.Id, Artifact(court.Id, number)); return court;
        }
        public async Task Write(Guid id, JsonObject artifact)
        {
            var path = Path.Combine(root, "court-intelligence", "v1", id.ToString(), "current.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, artifact.ToJsonString());
        }
        public async Task DenyCourt()
        {
            using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var permission = await db.Permissions.SingleAsync(p => p.Code == PermissionCodes.CourtView);
            db.RemoveRange(await db.RolePermissions.Where(r => r.PermissionId == permission.Id).ToListAsync()); await db.SaveChangesAsync();
        }
        public void Dispose() { Client.Dispose(); Factory.Dispose(); baseline.Dispose(); Transport.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class CaptureTransport : HttpMessageHandler, IHttpClientFactory
    {
        public ConcurrentQueue<(string Path, JsonElement Body)> Requests { get; } = new();
        public string GeneralText { get; set; } = "Hello!";
        public Func<Task>? BeforeAnswer { get; set; }
        public HttpClient CreateClient(string name) => new(this, false) { BaseAddress = new Uri("http://127.0.0.1:8097/") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct)); Requests.Enqueue((request.RequestUri!.AbsolutePath, body));
            if (BeforeAnswer is not null) await BeforeAnswer();
            object result = request.RequestUri.AbsolutePath == "/assistant/general" ? new { mode = "GeneralLocal", answer = GeneralText, claims = Array.Empty<object>() }
                : new { caseId = body.GetProperty("caseId").GetGuid(), mode = "CourtGrounded", answer = "The LAC shall file a status report.", insufficientEvidence = false,
                    claims = new[] { new { text = "The LAC shall file a status report.", attribution = "Court direction", source = new {
                        officialUrl = body.GetProperty("orderIndex")[0].GetProperty("officialUrl").GetString(), orderDate = "2026-01-01", page = 1, evidence = "The LAC shall file a status report." } } } };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
        }
    }
}
