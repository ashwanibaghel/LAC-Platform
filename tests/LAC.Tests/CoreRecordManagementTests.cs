namespace LAC.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
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

    private async Task<HttpClient> CreateUserClientAsync(string username, string password)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        return client;
    }

    [Fact]
    public async Task Authenticated_user_lacking_permission_returns_403_forbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();

        var limitedUser = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "limited_user",
            NormalizedUsername = "LIMITED_USER",
            DisplayName = "Limited Test User",
            IsActive = true,
            RecordStatus = RecordStatus.Active
        };
        limitedUser.PasswordHash = hasher.HashPassword(limitedUser, "LimitedPass!123");
        db.AppUsers.Add(limitedUser);

        var village = new Village { Name = "Village Perm Test" };
        var award = new Award { AwardNumber = "AWD-PERM-TEST", AwardType = "General" };
        var doc = new Document { DocumentType = "Award", OriginalFileName = "test.pdf", StoragePath = "/tmp/test.pdf" };
        db.Villages.Add(village);
        db.Awards.Add(award);
        db.AwardVillages.Add(new AwardVillage { Award = award, Village = village });
        db.Documents.Add(doc);
        db.DocumentAwards.Add(new DocumentAward { Award = award, Document = doc, CoreDocumentRole = "Award" });
        await db.SaveChangesAsync();

        // Login as limitedUser (IsAuthenticated = true, but no Award.Edit or Award.CoreDocument.Upload permissions)
        var limitedClient = await CreateUserClientAsync("limited_user", "LimitedPass!123");

        // Award Edit without Award.Edit permission -> 403 Forbidden
        var editRes = await limitedClient.PutAsJsonAsync($"/api/awards/{award.Id}", new { AwardNumber = "AWD-NO-PERM" });
        Assert.Equal(HttpStatusCode.Forbidden, editRes.StatusCode);

        // Core Document Replace without Award.CoreDocument.Upload permission -> 403 Forbidden
        var replaceContent = new MultipartFormDataContent();
        replaceContent.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "new.pdf");
        var replaceRes = await limitedClient.PutAsync($"/api/awards/{award.Id}/core-documents/{doc.Id}?reason=Test", replaceContent);
        Assert.Equal(HttpStatusCode.Forbidden, replaceRes.StatusCode);

        // Core Document Remove without Award.CoreDocument.Upload permission -> 403 Forbidden
        var removeRes = await limitedClient.DeleteAsync($"/api/awards/{award.Id}/core-documents/{doc.Id}?reason=Test");
        Assert.Equal(HttpStatusCode.Forbidden, removeRes.StatusCode);
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

        // Verify database state and updated audit fields
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<LacDbContext>();
        var updated = await db2.Awards.FindAsync(award.Id);
        Assert.NotNull(updated);
        Assert.Equal("AWD-100-EDITED", updated.AwardNumber);
        Assert.Equal("Supplementary", updated.AwardType);
        Assert.Equal(new DateOnly(2026, 5, 10), updated.AwardDate);
        Assert.False(string.IsNullOrWhiteSpace(updated.UpdatedBy));

        // Verify audit log includes ChangedBy, OldValues, and NewValues
        var audit = await db2.AuditLogs.FirstOrDefaultAsync(x => x.EntityId == award.Id && x.Action == "AwardUpdated");
        Assert.NotNull(audit);
        Assert.False(string.IsNullOrWhiteSpace(audit.ChangedBy));
        Assert.NotNull(audit.OldValues);
        Assert.NotNull(audit.NewValues);
        Assert.Contains("AWD-100-TEST", audit.OldValues);
        Assert.Contains("AWD-100-EDITED", audit.NewValues);
    }

    [Fact]
    public async Task Replacement_reason_required_validation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var village = new Village { Name = "Village Reason Test" };
        var award = new Award { AwardNumber = "AWD-REASON-TEST" };
        db.Villages.Add(village);
        db.Awards.Add(award);
        db.AwardVillages.Add(new AwardVillage { Award = award, Village = village });
        var doc = new Document { DocumentType = "Award", OriginalFileName = "orig.pdf", StoragePath = "/tmp/orig.pdf" };
        db.Documents.Add(doc);
        db.DocumentAwards.Add(new DocumentAward { Award = award, Document = doc, CoreDocumentRole = "Award" });
        await db.SaveChangesAsync();

        var authClient = await CreateAdminClientAsync();

        // Attempt replace without reason -> Expect Validation Error (400 Bad Request)
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "new.pdf");
        var res = await authClient.PutAsync($"/api/awards/{award.Id}/core-documents/{doc.Id}?reason=", content);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Multiple_documents_per_core_role_targeted_operations()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var village = new Village { Name = "Village Multi" };
        var award = new Award { AwardNumber = "AWD-MULTI-TEST" };
        db.Villages.Add(village);
        db.Awards.Add(award);
        db.AwardVillages.Add(new AwardVillage { Award = award, Village = village });

        var doc1 = new Document { DocumentType = "NM", OriginalFileName = "nm_page1.pdf", StoragePath = "/path/p1.pdf" };
        var doc2 = new Document { DocumentType = "NM", OriginalFileName = "nm_page2.pdf", StoragePath = "/path/p2.pdf" };
        db.Documents.AddRange(doc1, doc2);
        db.DocumentAwards.Add(new DocumentAward { Award = award, Document = doc1, CoreDocumentRole = "NM" });
        db.DocumentAwards.Add(new DocumentAward { Award = award, Document = doc2, CoreDocumentRole = "NM" });
        await db.SaveChangesAsync();

        var authClient = await CreateAdminClientAsync();

        // Target remove ONLY doc1
        var removeRes = await authClient.DeleteAsync($"/api/awards/{award.Id}/core-documents/{doc1.Id}?reason=Unlinking%20first%20page");
        Assert.Equal(HttpStatusCode.OK, removeRes.StatusCode);

        using (var scope2 = _factory.Services.CreateScope())
        {
            var db2 = scope2.ServiceProvider.GetRequiredService<LacDbContext>();

            // doc1 link role cleared
            var link1 = await db2.DocumentAwards.FirstOrDefaultAsync(x => x.AwardId == award.Id && x.DocumentId == doc1.Id);
            Assert.NotNull(link1);
            Assert.Null(link1.CoreDocumentRole);

            // doc2 link role STILL active as "NM"
            var activeNm = await db2.DocumentAwards.Where(x => x.AwardId == award.Id && x.CoreDocumentRole == "NM").ToListAsync();
            Assert.Single(activeNm);
            Assert.Equal(doc2.Id, activeNm[0].DocumentId);
        }

        // Target replace ONLY doc2
        var replaceContent = new MultipartFormDataContent();
        replaceContent.Add(new ByteArrayContent(new byte[] { 7, 8, 9 }), "file", "nm_page2_new.pdf");
        var replaceRes = await authClient.PutAsync($"/api/awards/{award.Id}/core-documents/{doc2.Id}?reason=Replacing%20page2", replaceContent);
        Assert.Equal(HttpStatusCode.OK, replaceRes.StatusCode);

        using (var scope3 = _factory.Services.CreateScope())
        {
            var db3 = scope3.ServiceProvider.GetRequiredService<LacDbContext>();

            // Physical doc1 and doc2 both still exist in DB
            Assert.NotNull(await db3.Documents.FindAsync(doc1.Id));
            Assert.NotNull(await db3.Documents.FindAsync(doc2.Id));

            // doc2 link role cleared
            var link2 = await db3.DocumentAwards.FirstOrDefaultAsync(x => x.AwardId == award.Id && x.DocumentId == doc2.Id);
            Assert.NotNull(link2);
            Assert.Null(link2.CoreDocumentRole);

            // Exactly 1 active core document for role "NM" (the replacement document)
            var activeNmAfterReplace = await db3.DocumentAwards.Where(x => x.AwardId == award.Id && x.CoreDocumentRole == "NM").ToListAsync();
            Assert.Single(activeNmAfterReplace);
            Assert.NotEqual(doc2.Id, activeNmAfterReplace[0].DocumentId);

            // Verify Audit logs populated with ChangedBy and reason
            var auditReplace = await db3.AuditLogs.FirstOrDefaultAsync(x => x.Action == "CoreDocumentReplaced" && x.EntityId == link2.Id);
            Assert.NotNull(auditReplace);
            Assert.False(string.IsNullOrWhiteSpace(auditReplace.ChangedBy));
            Assert.Contains("Replacing page2", auditReplace.NewValues);
        }
    }

    [Fact]
    public async Task Shared_document_preservation_on_core_removal()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var village = new Village { Name = "Village Shared" };
        var award = new Award { AwardNumber = "AWD-SHARED-TEST" };
        db.Villages.Add(village);
        db.Awards.Add(award);

        var sharedDoc = new Document { DocumentType = "StatementA", OriginalFileName = "statement_a.pdf", StoragePath = "/path/sa.pdf" };
        db.Documents.Add(sharedDoc);
        db.DocumentAwards.Add(new DocumentAward { Award = award, Document = sharedDoc, CoreDocumentRole = "StatementA" });
        db.DocumentVillages.Add(new DocumentVillage { Village = village, Document = sharedDoc });
        await db.SaveChangesAsync();

        var authClient = await CreateAdminClientAsync();

        // Remove core document from Award
        var removeRes = await authClient.DeleteAsync($"/api/awards/{award.Id}/core-documents/{sharedDoc.Id}?reason=Removing%20core%20role");
        Assert.Equal(HttpStatusCode.OK, removeRes.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<LacDbContext>();

        // Physical document still exists in DB
        var docInDb = await db2.Documents.FindAsync(sharedDoc.Id);
        Assert.NotNull(docInDb);

        // DocumentVillage link STILL exists!
        var villageLink = await db2.DocumentVillages.FirstOrDefaultAsync(x => x.VillageId == village.Id && x.DocumentId == sharedDoc.Id);
        Assert.NotNull(villageLink);
    }
}
