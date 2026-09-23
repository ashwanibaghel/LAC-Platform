namespace LAC.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    [Fact]
    public async Task Core_records_projection_isolates_documents_strictly_without_inheriting_old_document_jobs()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var village = new Village { Name = "Isolation Village" };
        var award = new Award { AwardNumber = "AWD-ISO-TEST" };
        db.Villages.Add(village);
        db.Awards.Add(award);
        db.AwardVillages.Add(new AwardVillage { Award = award, Village = village });

        var oldDoc = new Document { DocumentType = "Award", OriginalFileName = "old_award.pdf", StoragePath = "/tmp/old.pdf" };
        var newDoc = new Document { DocumentType = "Award", OriginalFileName = "new_award.pdf", StoragePath = "/tmp/new.pdf" };
        db.Documents.AddRange(oldDoc, newDoc);

        // oldDoc had a completed extraction job, but is no longer an active core document
        db.DocumentAwards.Add(new DocumentAward { Award = award, Document = oldDoc, CoreDocumentRole = null });
        db.DocumentAwards.Add(new DocumentAward { Award = award, Document = newDoc, CoreDocumentRole = "Award" });

        var oldJob = new AwardDocumentExtractionJob
        {
            DocumentId = oldDoc.Id,
            TargetAwardId = award.Id,
            Status = AwardDocumentExtractionJobStatus.Completed,
            TotalPages = 10,
            ProcessedPages = 10,
            CurrentStage = "Completed"
        };
        db.AwardDocumentExtractionJobs.Add(oldJob);
        await db.SaveChangesAsync();

        var authClient = await CreateAdminClientAsync();

        // Get Core Records for Village
        var res = await authClient.GetAsync($"/api/villages/{village.Id}/core-records");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement[]>();
        Assert.NotNull(json);
        Assert.Single(json);

        var awardElem = json[0];
        var docs = awardElem.GetProperty("documents").EnumerateArray().ToList();
        Assert.Single(docs); // Only active core document (newDoc) is listed

        var activeDoc = docs[0];
        Assert.Equal(newDoc.Id, activeDoc.GetProperty("documentId").GetGuid());
        // Must NOT inherit oldDoc's extraction job!
        Assert.Equal(System.Text.Json.JsonValueKind.Null, activeDoc.GetProperty("extractionJobId").ValueKind);

        // Verify legacy POST /api/awards/{id}/extract is removed (404 Not Found)
        var extractRes = await authClient.PostAsync($"/api/awards/{award.Id}/extract?villageId={village.Id}", null);
        Assert.Equal(HttpStatusCode.NotFound, extractRes.StatusCode);
    }

    [Fact]
    public async Task Core_records_projection_returns_exact_candidate_status_counts()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var village = new Village { Name = "Counts Test Village" };
        var award = new Award { AwardNumber = "AWD-COUNTS-TEST" };
        db.Villages.Add(village);
        db.Awards.Add(award);
        db.AwardVillages.Add(new AwardVillage { Award = award, Village = village });

        var doc = new Document { DocumentType = "Award", OriginalFileName = "counts_doc.pdf", StoragePath = "/tmp/counts.pdf" };
        db.Documents.Add(doc);
        db.DocumentAwards.Add(new DocumentAward { Award = award, Document = doc, CoreDocumentRole = "Award" });

        var session = new AwardIngestionSession
        {
            Id = Guid.NewGuid(),
            SourceDocumentId = doc.Id,
            TargetAwardId = award.Id,
            SelectedVillageId = village.Id,
            Status = AwardIngestionSessionStatus.Parsed,
            Candidates = new List<AwardIngestionCandidate>()
        };
        db.AwardIngestionSessions.Add(session);

        // 1 NeedsReview (Unresolved)
        db.AwardIngestionCandidates.Add(new AwardIngestionCandidate
        {
            SessionId = session.Id,
            CandidateType = AwardIngestionCandidateType.AwardKhasra,
            Status = AwardIngestionCandidateStatus.NeedsReview,
            StructuredPayloadJson = "{}",
            SourceLocatorJson = "{}"
        });
        // 1 Ready with VerifiedAt != null (Verified waiting commit)
        db.AwardIngestionCandidates.Add(new AwardIngestionCandidate
        {
            SessionId = session.Id,
            CandidateType = AwardIngestionCandidateType.AwardKhasra,
            Status = AwardIngestionCandidateStatus.Ready,
            VerifiedAt = DateTimeOffset.UtcNow,
            VerifiedBy = "Tester",
            StructuredPayloadJson = "{}",
            SourceLocatorJson = "{}"
        });
        // 1 Committed
        db.AwardIngestionCandidates.Add(new AwardIngestionCandidate
        {
            SessionId = session.Id,
            CandidateType = AwardIngestionCandidateType.AwardKhasra,
            Status = AwardIngestionCandidateStatus.Committed,
            StructuredPayloadJson = "{}",
            SourceLocatorJson = "{}"
        });
        // 1 Skipped
        db.AwardIngestionCandidates.Add(new AwardIngestionCandidate
        {
            SessionId = session.Id,
            CandidateType = AwardIngestionCandidateType.AwardKhasra,
            Status = AwardIngestionCandidateStatus.Skipped,
            StructuredPayloadJson = "{}",
            SourceLocatorJson = "{}"
        });
        // 1 Rejected
        db.AwardIngestionCandidates.Add(new AwardIngestionCandidate
        {
            SessionId = session.Id,
            CandidateType = AwardIngestionCandidateType.AwardKhasra,
            Status = AwardIngestionCandidateStatus.Rejected,
            StructuredPayloadJson = "{}",
            SourceLocatorJson = "{}"
        });

        await db.SaveChangesAsync();

        var authClient = await CreateAdminClientAsync();
        var res = await authClient.GetAsync($"/api/villages/{village.Id}/core-records");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement[]>();
        Assert.NotNull(json);
        Assert.Single(json);

        var awardElem = json[0];
        Assert.Equal(5, awardElem.GetProperty("totalCandidates").GetInt32());
        Assert.Equal(1, awardElem.GetProperty("unresolvedCandidates").GetInt32());
        Assert.Equal(1, awardElem.GetProperty("verifiedWaitingCommit").GetInt32());
        Assert.Equal(1, awardElem.GetProperty("committedCandidates").GetInt32());
        Assert.Equal(1, awardElem.GetProperty("skippedCandidates").GetInt32());
        Assert.Equal(1, awardElem.GetProperty("rejectedCandidates").GetInt32());

        var docElem = awardElem.GetProperty("documents").EnumerateArray().First();
        Assert.Equal(5, docElem.GetProperty("totalCandidates").GetInt32());
        Assert.Equal(1, docElem.GetProperty("unresolvedCandidates").GetInt32());
        Assert.Equal(1, docElem.GetProperty("verifiedWaitingCommit").GetInt32());
        Assert.Equal(1, docElem.GetProperty("committedCandidates").GetInt32());
        Assert.Equal(1, docElem.GetProperty("skippedCandidates").GetInt32());
        Assert.Equal(1, docElem.GetProperty("rejectedCandidates").GetInt32());
    }

    [Fact]
    public async Task Unverified_ready_candidate_is_counted_as_unresolved_in_core_records()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var village = new Village { Name = "Unverified Ready Village" };
        var award = new Award { AwardNumber = "AWD-UNVERIFIED-READY" };
        db.Villages.Add(village);
        db.Awards.Add(award);
        db.AwardVillages.Add(new AwardVillage { Award = award, Village = village });

        var doc = new Document { DocumentType = "Award", OriginalFileName = "unverified_ready.pdf", StoragePath = "/tmp/unverified.pdf" };
        db.Documents.Add(doc);
        db.DocumentAwards.Add(new DocumentAward { Award = award, Document = doc, CoreDocumentRole = "Award" });

        var session = new AwardIngestionSession
        {
            Id = Guid.NewGuid(),
            SourceDocumentId = doc.Id,
            TargetAwardId = award.Id,
            SelectedVillageId = village.Id,
            Status = AwardIngestionSessionStatus.Parsed
        };
        db.AwardIngestionSessions.Add(session);

        // Candidate status is Ready, but VerifiedAt is NULL (not human-confirmed yet)
        db.AwardIngestionCandidates.Add(new AwardIngestionCandidate
        {
            SessionId = session.Id,
            CandidateType = AwardIngestionCandidateType.AwardKhasra,
            Status = AwardIngestionCandidateStatus.Ready,
            VerifiedAt = null,
            StructuredPayloadJson = "{}",
            SourceLocatorJson = "{}"
        });
        await db.SaveChangesAsync();

        var authClient = await CreateAdminClientAsync();
        var res = await authClient.GetAsync($"/api/villages/{village.Id}/core-records");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement[]>();
        Assert.NotNull(json);
        Assert.Single(json);

        var awardElem = json[0];
        Assert.Equal(1, awardElem.GetProperty("totalCandidates").GetInt32());
        // Unverified Ready MUST be counted as unresolved!
        Assert.Equal(1, awardElem.GetProperty("unresolvedCandidates").GetInt32());
        Assert.Equal(0, awardElem.GetProperty("verifiedWaitingCommit").GetInt32());

        var docElem = awardElem.GetProperty("documents").EnumerateArray().First();
        Assert.Equal(1, docElem.GetProperty("totalCandidates").GetInt32());
        Assert.Equal(1, docElem.GetProperty("unresolvedCandidates").GetInt32());
        Assert.Equal(0, docElem.GetProperty("verifiedWaitingCommit").GetInt32());
    }

    [Fact]
    public async Task Document_A_never_inherits_document_B_analysis_status_or_session()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var village = new Village { Name = "Isolation MultiDoc Village" };
        var award = new Award { AwardNumber = "AWD-MULTIDOC-ISO" };
        db.Villages.Add(village);
        db.Awards.Add(award);
        db.AwardVillages.Add(new AwardVillage { Award = award, Village = village });

        var docA = new Document { DocumentType = "NM", OriginalFileName = "docA.pdf", StoragePath = "/tmp/docA.pdf" };
        var docB = new Document { DocumentType = "NM", OriginalFileName = "docB.pdf", StoragePath = "/tmp/docB.pdf" };
        db.Documents.AddRange(docA, docB);
        db.DocumentAwards.Add(new DocumentAward { Award = award, Document = docA, CoreDocumentRole = "NM" });
        db.DocumentAwards.Add(new DocumentAward { Award = award, Document = docB, CoreDocumentRole = "NM" });

        // Document B has an extraction job and ingestion session
        var jobB = new AwardDocumentExtractionJob
        {
            DocumentId = docB.Id,
            TargetAwardId = award.Id,
            SelectedVillageId = village.Id,
            TotalPages = 5,
            ProcessedPages = 5,
            Status = AwardDocumentExtractionJobStatus.Completed
        };
        db.AwardDocumentExtractionJobs.Add(jobB);

        var sessionB = new AwardIngestionSession
        {
            Id = Guid.NewGuid(),
            SourceDocumentId = docB.Id,
            TargetAwardId = award.Id,
            SelectedVillageId = village.Id,
            Status = AwardIngestionSessionStatus.NeedsReview
        };
        db.AwardIngestionSessions.Add(sessionB);
        await db.SaveChangesAsync();

        var authClient = await CreateAdminClientAsync();
        var res = await authClient.GetAsync($"/api/villages/{village.Id}/core-records");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement[]>();
        Assert.NotNull(json);
        var awardElem = json[0];
        var docs = awardElem.GetProperty("documents").EnumerateArray().ToList();
        Assert.Equal(2, docs.Count);

        var elemA = docs.First(d => d.GetProperty("documentId").GetGuid() == docA.Id);
        var elemB = docs.First(d => d.GetProperty("documentId").GetGuid() == docB.Id);

        // Document A must NOT inherit Document B's extraction job or session
        Assert.Equal(System.Text.Json.JsonValueKind.Null, elemA.GetProperty("extractionJobId").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, elemA.GetProperty("ingestionSessionId").ValueKind);

        // Document B has its own job and session
        Assert.Equal(jobB.Id, elemB.GetProperty("extractionJobId").GetGuid());
        Assert.Equal(sessionB.Id, elemB.GetProperty("ingestionSessionId").GetGuid());
    }

    [Fact]
    public async Task Ingestion_mutation_endpoints_override_client_actor_with_authenticated_user_context()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var village = new Village { Name = "Actor Override Village" };
        var award = new Award { AwardNumber = "AWD-ACTOR-TEST" };
        db.Villages.Add(village);
        db.Awards.Add(award);
        db.AwardVillages.Add(new AwardVillage { Award = award, Village = village });

        var doc = new Document { DocumentType = "Award", OriginalFileName = "actor_test.pdf", StoragePath = "/tmp/actor.pdf" };
        db.Documents.Add(doc);
        db.DocumentAwards.Add(new DocumentAward { Award = award, Document = doc, CoreDocumentRole = "Award" });

        var job = new AwardDocumentExtractionJob
        {
            DocumentId = doc.Id,
            TargetAwardId = award.Id,
            SelectedVillageId = village.Id,
            TotalPages = 1,
            ProcessedPages = 1,
            Status = AwardDocumentExtractionJobStatus.NeedsReview
        };
        db.AwardDocumentExtractionJobs.Add(job);

        var session = new AwardIngestionSession
        {
            Id = Guid.NewGuid(),
            SourceDocumentId = doc.Id,
            TargetAwardId = award.Id,
            SelectedVillageId = village.Id,
            Status = AwardIngestionSessionStatus.Parsed
        };
        db.AwardIngestionSessions.Add(session);

        var candidate = new AwardIngestionCandidate
        {
            SessionId = session.Id,
            CandidateType = AwardIngestionCandidateType.AwardVillage,
            Status = AwardIngestionCandidateStatus.NeedsReview,
            SourcePage = 1,
            StructuredPayloadJson = System.Text.Json.JsonSerializer.Serialize(new AwardVillageCandidate("OCR Name", null)),
            SourceLocatorJson = "{\"page\":1}"
        };
        db.AwardIngestionCandidates.Add(candidate);
        await db.SaveChangesAsync();

        // 1. Unauthenticated request -> 401 Unauthorized
        var unauthClient = _factory.CreateClient();
        var unauthReq = new VerifyExtractedFactRequest(
            VerifiedBy: "Anonymous Hacker",
            CorrectedPayloadJson: System.Text.Json.JsonSerializer.Serialize(new AwardVillageCandidate(village.Name, village.Name))
        );
        var unauthRes = await unauthClient.PostAsJsonAsync($"/api/award-ingestion-candidates/{candidate.Id}/verify", unauthReq);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

        // 2. Authenticated request with spoofed actor in DTO -> Uses authenticated user identity
        var authClient = await CreateAdminClientAsync(); // Logged in as "Core Record Test Administrator"
        var request = new VerifyExtractedFactRequest(
            VerifiedBy: "Hacker Name",
            CorrectedPayloadJson: System.Text.Json.JsonSerializer.Serialize(new AwardVillageCandidate(village.Name, village.Name))
        );

        var response = await authClient.PostAsJsonAsync($"/api/award-ingestion-candidates/{candidate.Id}/verify", request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<LacDbContext>();
        var updatedCandidate = await db2.AwardIngestionCandidates.FindAsync(candidate.Id);
        Assert.NotNull(updatedCandidate);
        Assert.Equal("Core Record Test Administrator", updatedCandidate.VerifiedBy);
        Assert.NotEqual("Hacker Name", updatedCandidate.VerifiedBy);
    }

    [Fact]
    public async Task Legacy_ingestion_commit_ignores_spoofed_committedBy_and_uses_authenticated_actor()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var village = new Village { Name = "Commit Actor Village" };
        var award = new Award { AwardNumber = "AWD-COMMIT-ACTOR" };
        db.Villages.Add(village);
        db.Awards.Add(award);
        db.AwardVillages.Add(new AwardVillage { Award = award, Village = village });

        var session = new AwardIngestionSession
        {
            Id = Guid.NewGuid(),
            TargetAwardId = award.Id,
            SelectedVillageId = village.Id,
            Status = AwardIngestionSessionStatus.ReadyToCommit
        };
        db.AwardIngestionSessions.Add(session);
        var candidate = new AwardIngestionCandidate
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            CandidateType = AwardIngestionCandidateType.Notification,
            Status = AwardIngestionCandidateStatus.Ready,
            StructuredPayloadJson = JsonSerializer.Serialize(new NotificationCandidate("Section 4", "NOTIF-100", new DateOnly(2020, 1, 1))),
            SourceLocatorJson = "{}"
        };
        db.AwardIngestionCandidates.Add(candidate);
        await db.SaveChangesAsync();

        // 1. Unauthenticated request -> 401 Unauthorized
        var unauthClient = _factory.CreateClient();
        var unauthRes = await unauthClient.PostAsJsonAsync($"/api/award-ingestion-sessions/{session.Id}/commit", new { CandidateIds = new List<Guid> { candidate.Id }, CommittedBy = "Spoofed Name" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

        // 2. Authenticated request -> Succeeds and uses authenticated user actor
        var authClient = await CreateAdminClientAsync();
        var authRes = await authClient.PostAsJsonAsync($"/api/award-ingestion-sessions/{session.Id}/commit", new { CandidateIds = new List<Guid> { candidate.Id }, CommittedBy = "Spoofed Name" });
        Assert.Equal(HttpStatusCode.OK, authRes.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<LacDbContext>();
        var audit = await db2.AuditLogs.FirstOrDefaultAsync(x => x.EntityId == candidate.Id && x.Action == "IngestionCandidateCommitted");
        Assert.NotNull(audit);
        Assert.Equal("Core Record Test Administrator", audit.ChangedBy);
    }

    [Fact]
    public async Task Candidate_resolve_audit_log_records_authenticated_changedBy()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();

        var session = new AwardIngestionSession
        {
            Id = Guid.NewGuid(),
            Status = AwardIngestionSessionStatus.NeedsReview
        };
        db.AwardIngestionSessions.Add(session);

        var candidate = new AwardIngestionCandidate
        {
            SessionId = session.Id,
            CandidateType = AwardIngestionCandidateType.AwardKhasra,
            Status = AwardIngestionCandidateStatus.NeedsReview,
            StructuredPayloadJson = "{}",
            SourceLocatorJson = "{}"
        };
        db.AwardIngestionCandidates.Add(candidate);
        await db.SaveChangesAsync();

        // 1. Unauthenticated request -> 401 Unauthorized
        var unauthClient = _factory.CreateClient();
        var unauthRes = await unauthClient.PostAsJsonAsync($"/api/award-ingestion-candidates/{candidate.Id}/resolve", new { Action = "SkipCandidate" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

        // 2. Authenticated request -> AuditLog created with ChangedBy = authenticated user
        var authClient = await CreateAdminClientAsync();
        var authRes = await authClient.PostAsJsonAsync($"/api/award-ingestion-candidates/{candidate.Id}/resolve", new { Action = "SkipCandidate" });
        Assert.Equal(HttpStatusCode.NoContent, authRes.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<LacDbContext>();
        var audit = await db2.AuditLogs.FirstOrDefaultAsync(x => x.EntityId == candidate.Id && x.Action == "IngestionCandidateSkipCandidate");
        Assert.NotNull(audit);
        Assert.Equal("Core Record Test Administrator", audit.ChangedBy);
    }
}
