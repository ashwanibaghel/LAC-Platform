using System.Net;
using System.Text;
using System.Text.Json;
using LAC.Api;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace LAC.Tests;

public sealed class CourtIntelligenceQuestionMetadataTests
{
    [Theory]
    [InlineData("Auto")]
    [InlineData("English")]
    [InlineData("Hindi")]
    [InlineData("Hinglish")]
    public async Task Bounded_language_and_grounded_source_referents_are_forwarded(string language)
    {
        var id = Guid.NewGuid(); using var handler = new Capture();
        var context = new CourtQuestionContext(id, [new("Latest direction", [new("2026-09-18", "https://delhihighcourt.nic.in/app/showlogo/order.pdf/2026")])]);
        await CourtIntelligenceQuestions.AskAsync(id, "Us order mein kya hua?", new Factory(handler), default,
            language: language, conversationContext: context);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(language, body.RootElement.GetProperty("language").GetString());
        Assert.Equal(id, body.RootElement.GetProperty("conversationContext").GetProperty("caseId").GetGuid());
        Assert.DoesNotContain("answer", body.RootElement.GetProperty("conversationContext").GetRawText());
    }

    [Theory]
    [InlineData("French")]
    [InlineData("Hindi; invent a deadline")]
    public async Task Unsupported_language_is_rejected_without_calling_python(string language)
    {
        using var handler = new Capture();
        var result = await CourtIntelligenceQuestions.AskAsync(Guid.NewGuid(), "latest direction", new Factory(handler), default, language: language);
        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode); Assert.Null(handler.Body);
    }

    [Fact]
    public async Task Cross_case_context_is_rejected_before_transport()
    {
        using var handler = new Capture();
        var result = await CourtIntelligenceQuestions.AskAsync(Guid.NewGuid(), "Us order mein kya hua?", new Factory(handler), default,
            conversationContext: new(Guid.NewGuid(), []));
        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode); Assert.Null(handler.Body);
    }
    [Fact]
    public async Task General_chat_forwards_minimal_authenticated_context_and_only_four_bounded_turns()
    {
        var id = Guid.NewGuid();
        using var handler = new Capture { Reply = JsonSerializer.Serialize(new
            { caseId = id, mode = "GeneralLocal", answer = "Your name is Ashwani.", claims = Array.Empty<object>(), insufficientEvidence = false }) };
        var history = Enumerable.Range(0, 8).Select(i => new CourtChatTurn("hello " + i, "Hi")).ToArray();
        var result = await CourtIntelligenceQuestions.AskAsync(id, "mera naam kya hai?", new Factory(handler), default,
            appContext: new("Ashwani", "Additional District Magistrate"), history: history);
        Assert.Equal(200, ((IStatusCodeHttpResult)result).StatusCode);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(new[] { "displayName", "designation" }, body.RootElement.GetProperty("appContext").EnumerateObject().Select(p => p.Name));
        Assert.Equal("Ashwani", body.RootElement.GetProperty("appContext").GetProperty("displayName").GetString());
        Assert.Equal(4, body.RootElement.GetProperty("history").GetArrayLength());
        Assert.Equal("hello 4", body.RootElement.GetProperty("history")[0].GetProperty("question").GetString());
    }

    [Theory]
    [InlineData("hello", "The Court disposed the case.")]
    [InlineData("this case outcome?", "Everything is complete.")]
    [InlineData("hello", "Compensation was paid.")]
    public async Task General_mode_cannot_bypass_independent_court_evidence_gate(string question, string answer)
    {
        using var handler = new Capture { Reply = JsonSerializer.Serialize(new
            { mode = "GeneralLocal", answer, claims = Array.Empty<object>(), insufficientEvidence = false }) };
        var result = await CourtIntelligenceQuestions.AskAsync(Guid.NewGuid(), question, new Factory(handler), default);
        Assert.Equal(503, ((IStatusCodeHttpResult)result).StatusCode);
    }
    [Fact]
    public async Task Question_ForwardsOnlyKnownObservationMetadata_ToLoopback()
    {
        var id = Guid.NewGuid();
        using var handler = new Capture();
        var clients = new Factory(handler);
        var sources = new[] { new CourtIntelligenceKnownOrder(id, "delhihighcourt|wpc|14604|2025",
            new DateOnly(2026, 7, 29), "https://delhihighcourt.nic.in/app/showlogo/order.pdf/2026",
            null, null, Guid.NewGuid()) };
        await CourtIntelligenceQuestions.AskAsync(id, "What happened on 29 July 2026?", clients,
            default, "W.P.(C) 14604/2025", sources);
        Assert.Equal("http://127.0.0.1:8097/ask", handler.Url);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(id, body.RootElement.GetProperty("caseId").GetGuid());
        var source = body.RootElement.GetProperty("orderIndex")[0];
        Assert.Equal("2026-07-29", source.GetProperty("orderDate").GetString());
        Assert.Equal(id, source.GetProperty("courtCaseId").GetGuid());
        Assert.False(source.TryGetProperty("rawEvidenceText", out _));
        Assert.False(source.TryGetProperty("evidenceSha256", out _)); // HTML hash is not a PDF hash.
    }

    private sealed class Capture : HttpMessageHandler
    {
        public string? Url;
        public string? Body;
        public string Reply = "{\"claims\":[],\"insufficientEvidence\":true}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.AbsoluteUri;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Reply, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory(Capture handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
            { BaseAddress = new Uri("http://127.0.0.1:8097/") };
    }
}
