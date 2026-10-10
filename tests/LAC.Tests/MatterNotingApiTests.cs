namespace LAC.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class MatterNotingApiTests
{
    [DakPostgresFact]
    public async Task Raw_HTTP_bypass_headers_immutability_private_drafts_and_document_revocation()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); NotingFixture f; Guid fresh; Guid docId;
        await using (var db = database.Context())
        {
            f = await NotingFixture.CreateAsync(db); await f.SaveAsync(db);
            fresh = await f.Actors.RegisterAsync(db); var dw = new DakWorkflowService(db, NotingFixture.Storage);
            var marked = await dw.SendAsync(fresh, f.Actors.Mark(), f.Actors.Sender); await dw.ReceiveAsync(fresh, marked.TransferId!.Value, new(1, Guid.NewGuid()), f.Actors.Receiver);
            var doc = new Document { OriginalFileName = "shared.pdf", StoragePath = "http-test.pdf", DocumentType = "MatterDocument", MimeType = "application/pdf" };
            doc.StoragePath = await NotingFixture.Storage.SaveAsync(new MemoryStream("%PDF-1.4\nTest"u8.ToArray()), doc.StoragePath, default);
            db.Documents.Add(doc); db.MatterDocuments.Add(new MatterDocument { MatterId = f.Matter, Document = doc }); await db.SaveChangesAsync(); docId = doc.Id;
        }
        await using var factory = new Factory(database.ConnectionString); using var client = factory.CreateClient();
        var url = $"/api/matters/{f.Matter}/noting/draft";
        client.DefaultRequestHeaders.Add("X-Noting-Actor", f.Actors.Receiver.ToString());
        var dakDetail = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/dak/{f.Dak}");
        Assert.Equal("VillageSpecific", dakDetail.GetProperty("villageClassification").GetString());
        var recovered = await client.GetAsync(url); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode); Assert.Equal("\"1\"", recovered.Headers.ETag!.Tag);
        Assert.Equal((HttpStatusCode)428, (await client.PutAsJsonAsync(url, new SaveWorkingNoteCommand(MatterNotingTextTests.Text("New text"), f.Dak))).StatusCode);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString()); client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"0\"");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(url, new SaveWorkingNoteCommand(MatterNotingTextTests.Text("New text"), f.Dak))).StatusCode);
        foreach (var endpoint in new[] { $"/api/dak/{fresh}/matters/{f.Matter}", $"/api/dak/{fresh}/links/matters" })
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(endpoint, new { entityId = f.Matter })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/matters/{f.Matter}/daks/{fresh}", new { expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/dak/{fresh}/village-classification", new VillageClassificationCommand("VillageSpecific", [f.Village], 2))).StatusCode);
        await using (var db = database.Context())
        {
            var role = await db.UserRoles.Where(x => x.UserId == f.Actors.Receiver).Select(x => x.RoleId).SingleAsync();
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == role && x.Permission.Code == PermissionCodes.DakEdit)); await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/dak/{fresh}/matter-workspace", new MatterWorkspaceCommand(f.Village, f.Stream, f.Matter, null, null, 3))).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Noting-Actor"); client.DefaultRequestHeaders.Add("X-Noting-Actor", f.Actors.Supervisor.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/dak/{f.Dak}/matter-workspace", new MatterWorkspaceCommand(f.Village, f.Stream, f.Matter, null, null, 4))).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Noting-Actor"); client.DefaultRequestHeaders.Add("X-Noting-Actor", f.Actors.Receiver.ToString());
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Remove("If-Match"); client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");
        var submit = await client.PostAsJsonAsync($"/api/matters/{f.Matter}/noting/submit", new SubmitWorkingNoteCommand(f.Dak)); Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var note = await submit.Content.ReadFromJsonAsync<MatterOfficialNote>();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/matters/{f.Matter}/notes/{note!.Id}", new { contentJson = "forged" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/matters/{f.Matter}/notes/{note.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/matters/{f.Matter}/documents/{docId}/content?expectedVersion=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/matters/{f.Matter}/documents/{docId}/content?expectedVersion=0")).StatusCode);
        await using (var db = database.Context()) { var join = await db.MatterDocuments.SingleAsync(x => x.DocumentId == docId); join.RecordStatus = RecordStatus.Archived; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/matters/{f.Matter}/documents/{docId}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/matters/{f.Matter}/documents/{docId}/annotations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/matters/{f.Matter}/export", new { documentIds = new[] { docId } })).StatusCode);
        await using (var db = database.Context()) { foreach (var allocation in await db.WorkAllocations.Where(x => x.UserId == f.Actors.Caretaker).ToListAsync()) allocation.RevokedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); }
        client.DefaultRequestHeaders.Remove("X-Noting-Actor"); client.DefaultRequestHeaders.Add("X-Noting-Actor", f.Actors.Caretaker.ToString());
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/villages/{f.Village}/matter-workspace", new NativeMatterCommand(f.Stream, "No allocation", null))).StatusCode);
    }

    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Startup:RunDatabaseBootstrap"] = "false", ["BackgroundWorkers:Enabled"] = "false" }));
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<LacDbContext>(); s.RemoveAll<DbContextOptions<LacDbContext>>(); s.AddDbContext<LacDbContext>(o => o.UseNpgsql(connection));
                s.RemoveAll<IDocumentStorage>(); s.AddSingleton<IDocumentStorage>(NotingFixture.Storage);
                s.AddAuthentication(o => { o.DefaultAuthenticateScheme = "NotingTest"; o.DefaultChallengeScheme = "NotingTest"; })
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuth>("NotingTest", _ => { });
            });
        }
    }
    private sealed class HeaderAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Noting-Actor"], out var actor)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, actor.ToString()), new System.Security.Claims.Claim(ClaimTypes.Name, "Noting test actor") };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}
