namespace LAC.Tests;

using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

public sealed class CoreRecordTestFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"core-record-tests-{Guid.NewGuid()}";
    public const string TestAdminUser = "corerecord_admin";
    public const string TestAdminPass = "CoreRecordAdminPass!123";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = TestAdminUser,
                ["BootstrapAdmin:Password"] = TestAdminPass,
                ["BootstrapAdmin:DisplayName"] = "Core Record Test Administrator"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LacDbContext>>();
            services.RemoveAll<LacDbContext>();
            services.AddDbContext<LacDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}

public sealed class CoreRecordManagementTests : IClassFixture<CoreRecordTestFactory>
{
    private readonly CoreRecordTestFactory _factory;

    public CoreRecordManagementTests(CoreRecordTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(CoreRecordTestFactory.TestAdminUser, CoreRecordTestFactory.TestAdminPass));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        return client;
    }

    [Fact]
    public async Task Award_edit_metadata_authorization_and_audit()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var award = new Award { AwardNumber = "AWD-100-TEST", AwardType = "General", Status = "Active" };
        db.Awards.Add(award);
        await db.SaveChangesAsync();

        // Unauthorized edit attempt (no login cookie)
        var unauthClient = _factory.CreateClient();
        var unauthRes = await unauthClient.PutAsJsonAsync($"/api/awards/{award.Id}", new { AwardNumber = "AWD-EDITED", AwardType = "Supplementary" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

        // Authorized edit attempt (logged in as Admin with Award.Edit)
        var authClient = await CreateAdminClientAsync();
        var authRes = await authClient.PutAsJsonAsync($"/api/awards/{award.Id}", new { AwardNumber = "AWD-100-EDITED", AwardDate = "2026-05-10", AwardType = "Supplementary" });
        Assert.Equal(HttpStatusCode.OK, authRes.StatusCode);

        // Verify database state
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<LacDbContext>();
        var updated = await db2.Awards.FindAsync(award.Id);
        Assert.NotNull(updated);
        Assert.Equal("AWD-100-EDITED", updated.AwardNumber);
        Assert.Equal("Supplementary", updated.AwardType);
        Assert.Equal(new DateOnly(2026, 5, 10), updated.AwardDate);

        // Verify audit log
        var audit = await db2.AuditLogs.FirstOrDefaultAsync(x => x.EntityId == award.Id && x.Action == "AwardUpdated");
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task Core_document_replace_and_remove_preserves_historical_document_evidence()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var village = new Village { Name = "Village Alpha" };
        var award = new Award { AwardNumber = "AWD-DOC-TEST" };
        db.Villages.Add(village);
        db.Awards.Add(award);
        db.AwardVillages.Add(new AwardVillage { Award = award, Village = village });

        var initialDoc = new Document { DocumentType = "Award", OriginalFileName = "old_award.pdf", StoragePath = "/path/old.pdf", Sha256Hash = "hash1" };
        db.Documents.Add(initialDoc);
        db.DocumentAwards.Add(new DocumentAward { Award = award, Document = initialDoc, CoreDocumentRole = "Award" });
        await db.SaveChangesAsync();

        // Unauthorized replace attempt (no login cookie)
        var unauthClient = _factory.CreateClient();
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "new_award.pdf");
        var unauthRes = await unauthClient.PutAsync($"/api/awards/{award.Id}/core-documents?role=Award&reason=Test", content);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

        // Authorized replace attempt (logged in Admin)
        var authClient = await CreateAdminClientAsync();
        var replaceContent = new MultipartFormDataContent();
        replaceContent.Add(new ByteArrayContent(new byte[] { 4, 5, 6 }), "file", "new_award.pdf");
        var replaceRes = await authClient.PutAsync($"/api/awards/{award.Id}/core-documents?role=Award&reason=Replacing%20with%20signed%20copy", replaceContent);
        Assert.Equal(HttpStatusCode.OK, replaceRes.StatusCode);

        // Verify historical document evidence preserved and new document active
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<LacDbContext>();

        // Old physical document still exists!
        var oldDocInDb = await db2.Documents.FindAsync(initialDoc.Id);
        Assert.NotNull(oldDocInDb);
        Assert.Equal("old_award.pdf", oldDocInDb.OriginalFileName);

        // Core document relationships: old core document role cleared, new core document active
        var activeCoreDocs = await db2.DocumentAwards.Where(x => x.AwardId == award.Id && x.CoreDocumentRole == "Award").ToListAsync();
        Assert.Single(activeCoreDocs);
        Assert.NotEqual(initialDoc.Id, activeCoreDocs[0].DocumentId);

        // Audit log recorded replacement
        var replaceAudit = await db2.AuditLogs.FirstOrDefaultAsync(x => x.Action == "CoreDocumentReplaced");
        Assert.NotNull(replaceAudit);
        Assert.Contains("Replacing with signed copy", replaceAudit.NewValues);

        // Test Removal with reason
        var removeRes = await authClient.DeleteAsync($"/api/awards/{award.Id}/core-documents?role=Award&reason=Unlinking%20erroneous%20file");
        Assert.Equal(HttpStatusCode.OK, removeRes.StatusCode);

        using var scope3 = _factory.Services.CreateScope();
        var db3 = scope3.ServiceProvider.GetRequiredService<LacDbContext>();

        // No active core document for role "Award"
        var remainingActive = await db3.DocumentAwards.Where(x => x.AwardId == award.Id && x.CoreDocumentRole == "Award").ToListAsync();
        Assert.Empty(remainingActive);

        // Physical Document record STILL exists in database!
        var removedDocId = activeCoreDocs[0].DocumentId;
        var removedDocInDb = await db3.Documents.FindAsync(removedDocId);
        Assert.NotNull(removedDocInDb);

        // Removal audit log recorded
        var removeAudit = await db3.AuditLogs.FirstOrDefaultAsync(x => x.Action == "CoreDocumentRemoved");
        Assert.NotNull(removeAudit);
        Assert.Contains("Unlinking erroneous file", removeAudit.NewValues);
    }
}
