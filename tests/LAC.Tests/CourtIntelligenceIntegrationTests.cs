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

public sealed class CourtIntelligenceIntegrationTests
{
    private const string Url = "https://delhihighcourt.nic.in/app/showlogo/synthetic-422026.pdf/2026";

    [Fact]
    public async Task Refresh_polling_accepts_atomic_state_replacement_without_changing_case_data()
    {
        using var env = new Harness();
        var courtCase = await env.Register("W.P.(C) 42/2026", "delhihighcourt|wpc|42|2026");
        var folder = Path.GetDirectoryName(env.ArtifactPath(courtCase.Id))!;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "refresh.json");
        var running = JsonSerializer.Serialize(new { caseId = courtCase.Id, status = "Running", @checked = 0 });
        var completed = JsonSerializer.Serialize(new { caseId = courtCase.Id, status = "Completed", @checked = 1 });
        await File.WriteAllTextAsync(path, running);
        var before = await env.Snapshot(courtCase.Id);
        // Poll real endpoint while atomically publishing same-directory files.
        var reads = Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                using var response = await env.Client.GetAsync($"/api/court-cases/{courtCase.Id}/intelligence");
                response.EnsureSuccessStatusCode();
                var view = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Contains(view.GetProperty("refreshState").GetProperty("status").GetString(), new[] { "Running", "Completed" });
            }
        });
        for (var i = 0; i < 50; i++)
        {
            var temp = Path.Combine(folder, "state-" + Guid.NewGuid() + ".tmp");
            await File.WriteAllTextAsync(temp, i % 2 == 0 ? running : completed);
            File.Move(temp, path, true);
            await Task.Delay(1);
        }
        await reads;
        Assert.Equal(before, await env.Snapshot(courtCase.Id));
        Assert.Empty(env.Transport.Requests);
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [Fact]
    public async Task Actual_registered_case_routes_forward_only_its_DB_order_index_without_canonical_writes()
    {
        using var env = new Harness();
        var a = await env.Register("W.P.(C) 42/2026", "delhihighcourt|wpc|42|2026");
        var b = await env.Register("W.P.(C) 43/2026", "delhihighcourt|wpc|43|2026");
        var before = await env.Snapshot(a.Id);
        using var get = await env.Client.GetAsync($"/api/court-cases/{a.Id}/intelligence");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("no-store", get.Headers.CacheControl!.ToString());
        var view = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.Id, view.GetProperty("caseId").GetGuid());
        Assert.Equal(1, view.GetProperty("unprocessedOrderCount").GetInt32());
        Assert.Empty(view.GetProperty("currentPosition").EnumerateArray());
        Assert.Empty(env.Transport.Requests);
        Assert.False(File.Exists(env.ArtifactPath(a.Id)));

        using var ask = await env.Client.PostAsJsonAsync($"/api/court-cases/{a.Id}/intelligence/ask", new { question = "What happened?" });
        Assert.Equal(HttpStatusCode.OK, ask.StatusCode);
        using var refresh = await env.Client.PostAsync($"/api/court-cases/{a.Id}/intelligence/refresh", null);
        Assert.Equal(HttpStatusCode.Accepted, refresh.StatusCode);
        var requests = env.Transport.Requests.ToArray();
        Assert.Equal(2, env.Transport.RequestContentLengths.Count);
        Assert.All(env.Transport.RequestContentLengths, length => Assert.True(length is > 0 and <= 512 * 1024,
            "The local Python receiver requires a bounded Content-Length, not chunked JSON."));
        Assert.Equal(new[] { "/ask", "/refresh" }, requests.Select(x => x.Path));
        foreach (var request in requests)
        {
            Assert.Equal("127.0.0.1", request.Host);
            Assert.Equal(a.Id, request.Body.GetProperty("caseId").GetGuid());
            Assert.Equal(a.CaseNumber, request.Body.GetProperty("caseNumber").GetString());
            var known = Assert.Single(request.Body.GetProperty("orderIndex").EnumerateArray());
            Assert.Equal(a.Id, known.GetProperty("courtCaseId").GetGuid());
            Assert.NotEqual(b.Id, known.GetProperty("courtCaseId").GetGuid());
            Assert.Equal("delhihighcourt|wpc|42|2026", known.GetProperty("normalizedCaseIdentity").GetString());
            Assert.Equal(Url, known.GetProperty("officialUrl").GetString());
            Assert.True(known.GetProperty("sourceObservationId").GetGuid() != Guid.Empty);
            Assert.Equal("2026-01-01", known.GetProperty("orderDate").GetString());
            Assert.Equal("2026-01-02", known.GetProperty("uploadDate").GetString());
            Assert.False(known.TryGetProperty("sha256", out _)); // DB EvidenceSha256 is HTML, not PDF bytes.
        }
        Assert.Equal(before, await env.Snapshot(a.Id));
    }

    [Fact]
    public async Task Unauthorized_case_cannot_read_ask_or_start_refresh_and_never_contacts_local_worker()
    {
        using var env = new Harness();
        var courtCase = await env.Register("W.P.(C) 42/2026", "delhihighcourt|wpc|42|2026");
        var id = courtCase.Id;
        await env.Write(id, Artifact(id));
        using (var scope = env.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.UserRoles.RemoveRange(await db.UserRoles.Where(x => x.UserId == SeedData.BootstrapAdminId).ToListAsync());
            await db.SaveChangesAsync(); // Fixture authorization only, never an office DB.
        }
        using var get = await env.Client.GetAsync($"/api/court-cases/{id}/intelligence");
        using var ask = await env.Client.PostAsJsonAsync($"/api/court-cases/{id}/intelligence/ask", new { question = "Explain" });
        using var refresh = await env.Client.PostAsync($"/api/court-cases/{id}/intelligence/refresh", null);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ask.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
        Assert.Empty(env.Transport.Requests);
    }

    [Fact]
    public async Task Missing_deleted_malformed_wrong_case_and_unknown_source_artifacts_fail_safely()
    {
        using var env = new Harness();
        var courtCase = await env.Register("W.P.(C) 42/2026", "delhihighcourt|wpc|42|2026");
        await env.Write(courtCase.Id, Artifact(courtCase.Id));
        using (var valid = await env.Client.GetAsync($"/api/court-cases/{courtCase.Id}/intelligence"))
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        File.Delete(env.ArtifactPath(courtCase.Id));
        using (var deleted = await env.Client.GetAsync($"/api/court-cases/{courtCase.Id}/intelligence"))
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        foreach (var text in new[] { "{bad", Artifact(Guid.NewGuid()).ToJsonString(), Artifact(courtCase.Id, "https://example.org/order.pdf").ToJsonString() })
        {
            await File.WriteAllTextAsync(env.ArtifactPath(courtCase.Id), text);
            using var rejected = await env.Client.GetAsync($"/api/court-cases/{courtCase.Id}/intelligence");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
            Assert.Equal(text, await File.ReadAllTextAsync(env.ArtifactPath(courtCase.Id)));
        }
        Assert.Empty(env.Transport.Requests);
    }

    [Fact]
    public async Task Ask_requires_same_case_order_page_verbatim_evidence_and_party_attribution()
    {
        using var env = new Harness();
        var courtCase = await env.Register("W.P.(C) 42/2026", "delhihighcourt|wpc|42|2026");
        await env.Write(courtCase.Id, Artifact(courtCase.Id, category: "PETITIONER_SUBMISSION"));
        var before = await env.Snapshot(courtCase.Id);
        JsonObject Answer(Guid id, int page, string url, string label, string evidence = "The petitioner submits that compensation is unpaid.") => new()
        {
            ["caseId"] = id.ToString(), ["claims"] = new JsonArray(new JsonObject
            {
                ["text"] = "Compensation is unpaid", ["attribution"] = label,
                ["source"] = new JsonObject { ["orderDate"] = "2026-01-01", ["officialUrl"] = url,
                    ["page"] = page, ["evidence"] = evidence }
            })
        };
        const string submission = "Petitioner submission (not an established Court fact)";
        env.Transport.Answer = Answer(courtCase.Id, 1, Url, submission);
        using (var valid = await env.Client.PostAsJsonAsync($"/api/court-cases/{courtCase.Id}/intelligence/ask", new { question = "Compensation?" }))
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        foreach (var invalid in new[] { Answer(Guid.NewGuid(), 1, Url, submission), Answer(courtCase.Id, 2, Url, submission),
            Answer(courtCase.Id, 1, "https://example.org/wrong.pdf", submission), Answer(courtCase.Id, 1, Url, "Court finding"),
            Answer(courtCase.Id, 1, Url, submission, "Invented source") })
        {
            env.Transport.Answer = invalid;
            using var response = await env.Client.PostAsJsonAsync($"/api/court-cases/{courtCase.Id}/intelligence/ask", new { question = "Compensation?" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        env.Transport.Unavailable = true;
        using var offline = await env.Client.PostAsJsonAsync($"/api/court-cases/{courtCase.Id}/intelligence/ask", new { question = "Compensation?" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, offline.StatusCode);
        using var stillAvailable = await env.Client.GetAsync($"/api/court-cases/{courtCase.Id}/intelligence");
        Assert.Equal(HttpStatusCode.OK, stillAvailable.StatusCode);
        Assert.Equal(before, await env.Snapshot(courtCase.Id));
    }

    [Fact]
    public async Task Partial_sources_keep_verified_history_but_newest_unprocessed_source_is_not_current_position()
    {
        using var env = new Harness();
        var courtCase = await env.Register("W.P.(C) 42/2026", "delhihighcourt|wpc|42|2026");
        await env.Write(courtCase.Id, Artifact(courtCase.Id));
        using (var scope = env.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Add(Observation(courtCase.Id, "delhihighcourt|wpc|42|2026", new DateOnly(2026, 2, 1), Url.Replace("synthetic", "later")));
            await db.SaveChangesAsync();
        }
        using var response = await env.Client.GetAsync($"/api/court-cases/{courtCase.Id}/intelligence");
        response.EnsureSuccessStatusCode();
        var view = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, view.GetProperty("orders").GetArrayLength());
        Assert.Empty(view.GetProperty("currentPosition").EnumerateArray());
        Assert.Equal("Unprocessed", view.GetProperty("latestOrder").GetProperty("status").GetString());
        Assert.Single(view.GetProperty("sourceCoverage").GetProperty("gaps").EnumerateArray());
        Assert.Empty(env.Transport.Requests);
    }

    [Fact]
    public void Optional_order_references_and_evidence_fragments_cannot_hide_malformed_or_cross_case_payloads()
    {
        var id = Guid.NewGuid();
        var index = new CourtIntelligenceCaseIndex(id, "W.P.(C) 42/2026", [new(id, "delhihighcourt|wpc|42|2026", new(2026, 1, 1), Url, null, null, Guid.NewGuid())]);
        var artifact = Artifact(id);
        CourtIntelligenceCaseData.ValidateArtifact(JsonSerializer.SerializeToElement(artifact), index);
        artifact["latestOrder"] = new JsonObject { ["officialUrl"] = Url, ["orderDate"] = "2026-01-01" };
        Assert.Throws<InvalidDataException>(() => CourtIntelligenceCaseData.ValidateArtifact(JsonSerializer.SerializeToElement(artifact), index));
        artifact = Artifact(id);
        artifact["orders"]![0]!["facts"]![0]!["evidenceParts"] = new JsonArray(new JsonObject { ["page"] = 0, ["evidence"] = "Source" });
        Assert.Throws<InvalidDataException>(() => CourtIntelligenceCaseData.ValidateArtifact(JsonSerializer.SerializeToElement(artifact), index));
    }

    private static JsonObject Artifact(Guid id, string url = Url, string category = "COURT_DIRECTION") => new()
    {
        ["version"] = 1, ["caseId"] = id.ToString(), ["caseNumber"] = "W.P.(C) 42/2026",
        ["status"] = "Validated", ["processingComplete"] = true,
        ["currentPosition"] = new JsonArray(), ["beforeNextHearing"] = new JsonArray(), ["latestOrder"] = null,
        ["orders"] = new JsonArray(new JsonObject
        {
            ["orderDate"] = "2026-01-01", ["officialUrl"] = url, ["status"] = "Validated",
            ["facts"] = new JsonArray(new JsonObject { ["page"] = 1,
                ["value"] = "Compensation is unpaid", ["evidence"] = "The petitioner submits that compensation is unpaid.",
                ["category"] = category, ["field"] = "compensation", ["scope"] = "Current" })
        })
    };

    private static CourtExternalOrderObservation Observation(Guid id, string identity, DateOnly day, string url = Url) => new()
    {
        CourtCaseId = id, RunItemId = Guid.NewGuid(), ObservedAt = DateTimeOffset.UtcNow,
        NormalizedCaseIdentity = identity, RawCaseNumber = identity, OrderDate = day,
        OfficialUrl = url, UploadDate = day.AddDays(1), SourceUrl = "https://delhihighcourt.nic.in/",
        EvidenceSha256 = "HTML-SHA-NOT-A-PDF-HASH", RawEvidenceText = "Synthetic fixture"
    };

    private sealed class Harness : IDisposable
    {
        private readonly ApiFactory baseFactory = new();
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "court-integration-test-" + Guid.NewGuid());
        public CaptureTransport Transport { get; } = new();
        public WebApplicationFactory<Program> Factory { get; }
        public HttpClient Client { get; }
        public Harness()
        {
            Factory = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:DocumentRoot"] = Path.Combine(Root, "documents"),
                    ["Storage:ExtractionRoot"] = Root,
                    ["Storage:BackupRoot"] = Path.Combine(Root, "backups"),
                    ["BackgroundWorkers:Enabled"] = "false"
                }));
                builder.ConfigureServices(services => { services.RemoveAll<IHttpClientFactory>(); services.AddSingleton<IHttpClientFactory>(Transport); });
            });
            Client = Factory.CreateClient();
        }
        public async Task<CourtCase> Register(string number, string identity)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var courtCase = new CourtCase { CourtName = "Delhi High Court", CaseNumber = number, CurrentStatus = "Pending" };
            db.Add(courtCase); db.Add(Observation(courtCase.Id, identity, new DateOnly(2026, 1, 1)));
            await db.SaveChangesAsync(); return courtCase;
        }
        public string ArtifactPath(Guid id) => Path.Combine(Root, "court-intelligence", "v1", id.ToString(), "current.json");
        public async Task Write(Guid id, JsonObject artifact)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ArtifactPath(id))!);
            await File.WriteAllTextAsync(ArtifactPath(id), artifact.ToJsonString());
        }
        public async Task<string> Snapshot(Guid id)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var courtCase = await db.CourtCases.AsNoTracking().SingleAsync(x => x.Id == id);
            return JsonSerializer.Serialize(new { courtCase.Id, courtCase.CaseNumber, courtCase.CurrentStatus, courtCase.Revision,
                courtCase.ResponsibleOfficeDeskId, courtCase.AssignedUserId,
                proceedings = await db.CourtProceedings.CountAsync(x => x.CourtCaseId == id),
                observations = await db.CourtExternalOrderObservations.CountAsync(x => x.CourtCaseId == id),
                awards = await db.Awards.CountAsync(), matters = await db.Matters.CountAsync(), workItems = await db.WorkItems.CountAsync() });
        }
        public void Dispose() { Client.Dispose(); Factory.Dispose(); baseFactory.Dispose(); Transport.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    private sealed class CaptureTransport : HttpMessageHandler, IHttpClientFactory
    {
        public ConcurrentQueue<(string Host, string Path, JsonElement Body)> Requests { get; } = new();
        public ConcurrentQueue<long?> RequestContentLengths { get; } = new();
        public JsonObject? Answer { get; set; }
        public bool Unavailable { get; set; }
        public HttpClient CreateClient(string name) => new(this, false) { BaseAddress = new Uri("http://127.0.0.1:8097/") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestContentLengths.Enqueue(request.Content!.Headers.ContentLength);
            var body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct));
            Requests.Enqueue((request.RequestUri!.Host, request.RequestUri.AbsolutePath, body));
            if (Unavailable) throw new HttpRequestException("Synthetic local model outage");
            var refreshing = request.RequestUri.AbsolutePath == "/refresh";
            var response = refreshing ? new JsonObject { ["caseId"] = body.GetProperty("caseId").GetString(), ["status"] = "Running" }
                : Answer ?? new JsonObject { ["caseId"] = body.GetProperty("caseId").GetString(), ["claims"] = new JsonArray(), ["insufficientEvidence"] = true };
            return new HttpResponseMessage(refreshing ? HttpStatusCode.Accepted : HttpStatusCode.OK)
                { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
