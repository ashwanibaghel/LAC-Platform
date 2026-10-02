using System.Net;
using System.Text;
using System.Text.Json;
using LAC.Api;
using Xunit;

namespace LAC.Tests;

public sealed class CourtIntelligenceQuestionMetadataTests
{
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
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.AbsoluteUri;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"claims\":[],\"insufficientEvidence\":true}", Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory(Capture handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
            { BaseAddress = new Uri("http://127.0.0.1:8097/") };
    }
}
