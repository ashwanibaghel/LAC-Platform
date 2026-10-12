using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace LAC.Tests;

public sealed class StaffPostgresFactAttribute : FactAttribute
{
    public StaffPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LAC_STAFF_TEST_POSTGRES"))) Skip = "Requires explicitly isolated loopback PostgreSQL; see verification document.";
    }
}
public sealed class StaffRollbackInterceptor : SaveChangesInterceptor
{
    public Guid? FailAwardId { get; set; }
    public bool FailNextCoreAttachment { get; set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<DocumentAward>().Any(x => x.State == EntityState.Added && (FailNextCoreAttachment || FailAwardId is Guid id && x.Entity.AwardId == id)))
        {
            FailAwardId = null;
            FailNextCoreAttachment = false;
            throw new DbUpdateException("Synthetic isolated transaction failure.");
        }
        return ValueTask.FromResult(result);
    }
}
public sealed class StaffPostgresFactory : WebApplicationFactory<Program>
{
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "lac-staff-v2-tests-" + Guid.NewGuid().ToString("N"));
    public StaffRollbackInterceptor Failure { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LAC_STAFF_TEST_POSTGRES") ?? "");
        if (connection.Host != "127.0.0.1" || connection.Port != 55441 || connection.Database != "lac_staff_workflow_v2_test" || connection.Username != "staff_workflow_test")
            throw new InvalidOperationException("Acceptance tests refuse non-isolated PostgreSQL configuration.");
        connection.SearchPath = "staff_v2_" + Guid.NewGuid().ToString("N");
        using (var setup = new NpgsqlConnection(connection.ConnectionString))
        {
            setup.Open(); using var command = new NpgsqlCommand($"CREATE SCHEMA {connection.SearchPath}", setup); command.ExecuteNonQuery();
        }
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
            ["BootstrapAdmin:Username"] = "staff-v2-admin", ["BootstrapAdmin:Password"] = "SyntheticStaffV2!791",
            ["BootstrapAdmin:DisplayName"] = "Synthetic isolated staff administrator",
            ["PdfImport:MaxFileSizeMb"] = "1", ["Storage:DocumentRoot"] = Path.Combine(StorageRoot, "documents"), ["Storage:ExtractionRoot"] = Path.Combine(StorageRoot, "extraction"),
            ["Storage:BackupRoot"] = Path.Combine(StorageRoot, "backups"), ["DocumentIntelligence:Enabled"] = "false", ["OnlyOffice:Enabled"] = "false"
        }));
        builder.ConfigureServices(services => {
            services.RemoveAll<DbContextOptions<LacDbContext>>(); services.RemoveAll<LacDbContext>();
            services.AddDbContext<LacDbContext>(options => options.UseNpgsql(connection.ConnectionString).AddInterceptors(Failure));
            foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(AwardPdfExtractionWorker)).ToArray()) services.Remove(descriptor);
        });
    }
}

public sealed partial class LandRecordsStaffPersistenceTests(StaffPostgresFactory factory) : IClassFixture<StaffPostgresFactory>
{
    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "staff-v2-admin", password = "SyntheticStaffV2!791" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); return client;
    }
    private async Task<(Guid Award, Guid Village)> ContextAsync(string? number = null, DateOnly? date = null)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var subdivision = await db.SubDivisions.FirstAsync();
        var village = new Village { SubDivisionId = subdivision.Id, Name = "Synthetic staff Village " + Guid.NewGuid().ToString("N") };
        var award = new Award { AwardNumber = number ?? "SYNTHETIC-" + Guid.NewGuid().ToString("N"), AwardDate = date, ActRegime = "LAA1894", CreatedBy = SeedData.BootstrapAdminId.ToString() };
        db.AddRange(village, award, new AwardVillage { Village = village, Award = award }); await db.SaveChangesAsync(); return (award.Id, village.Id);
    }
    private static byte[] Pdf(string text)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return QuestPDF.Fluent.Document.Create(c => c.Page(p => p.Content().Text(text + " Synthetic source evidence for isolated staff workflow acceptance only."))).GeneratePdf();
    }
    private static async Task<HttpResponseMessage> Upload(HttpClient client, string path, byte[] bytes)
    {
        using var form = new MultipartFormDataContent(); var file = new ByteArrayContent(bytes); file.Headers.ContentType = new("application/pdf"); form.Add(file, "file", "synthetic.pdf");
        return await client.PostAsync(path, form);
    }
    private static async Task<JsonElement> Body(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.Created)
    {
        var text = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }
    private static string Core((Guid Award, Guid Village) context, string role = "Award", string suffix = "") => $"/api/awards/{context.Award}/core-documents?role={role}&villageId={context.Village}{suffix}";
    private async Task<T> Read<T>(Func<LacDbContext, Task<T>> read)
    {
        using var scope = factory.Services.CreateScope(); return await read(scope.ServiceProvider.GetRequiredService<LacDbContext>());
    }

    [StaffPostgresFact]
    public async Task Direct_upload_is_durable_idempotent_and_explicit_later_analysis_uses_same_document()
    {
        using var client = await ClientAsync(); var context = await ContextAsync(); var bytes = Pdf("Award No: SYNTHETIC test upload with manually selected explicit role.");
        var first = await Body(await Upload(client, Core(context), bytes)); var id = first.GetProperty("documentId").GetGuid();
        Assert.Equal("Attached", first.GetProperty("attachmentStatus").GetString()); Assert.Equal(JsonValueKind.Null, first.GetProperty("jobId").ValueKind);
        Assert.False(first.TryGetProperty("detectedType", out _)); Assert.False(first.TryGetProperty("confidence", out _));
        var retry = await Body(await Upload(client, $"/api/award-pdf-extractions?targetAwardId={context.Award}&selectedVillageId={context.Village}", bytes));
        Assert.Equal(id, retry.GetProperty("documentId").GetGuid()); Assert.True(retry.GetProperty("alreadyAttached").GetBoolean());
        Assert.Equal(0, await Read(db => db.AwardDocumentExtractionJobs.CountAsync(x => x.TargetAwardId == context.Award)));
        var downloaded = await client.GetByteArrayAsync($"/api/documents/{id}/content"); Assert.Equal(bytes, downloaded);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), await Read(db => db.Documents.Where(x => x.Id == id).Select(x => x.Sha256Hash).SingleAsync()));
        var analyzed = await Body(await client.PostAsync($"/api/awards/{context.Award}/documents/{id}/analyze?villageId={context.Village}", null), HttpStatusCode.Accepted);
        var again = await Body(await client.PostAsync($"/api/awards/{context.Award}/documents/{id}/analyze?villageId={context.Village}", null), HttpStatusCode.Accepted);
        Assert.Equal(analyzed.GetProperty("jobId").GetGuid(), again.GetProperty("jobId").GetGuid()); Assert.Equal(1, await Read(db => db.DocumentAwards.CountAsync(x => x.AwardId == context.Award)));
        var explicitMode = await Body(await Upload(client, Core(context, suffix: "&mode=UploadAndAnalyze"), bytes));
        Assert.Equal(id, explicitMode.GetProperty("documentId").GetGuid()); Assert.Equal(analyzed.GetProperty("jobId").GetGuid(), explicitMode.GetProperty("jobId").GetGuid());
        Assert.Equal(0, await Read(db => db.SourceEvidence.CountAsync(x => x.AwardId == context.Award)));
    }

    [StaffPostgresFact]
    public async Task Cross_award_conflicts_are_structured_identical_across_routes_and_modes()
    {
        using var client = await ClientAsync(); var owner = await ContextAsync(); var target = await ContextAsync(); var bytes = Pdf("Shared hash conflict test");
        var stored = await Body(await Upload(client, Core(owner), bytes)); var id = stored.GetProperty("documentId").GetGuid();
        var paths = new[] { Core(target), Core(target, suffix: "&mode=UploadAndAnalyze"), $"/api/award-pdf-extractions?targetAwardId={target.Award}&selectedVillageId={target.Village}", $"/api/award-pdf-extractions?targetAwardId={target.Award}&selectedVillageId={target.Village}&mode=UploadAndAnalyze" };
        foreach (var path in paths)
        {
            var conflict = await Body(await Upload(client, path, bytes), HttpStatusCode.Conflict); Assert.Equal("PDF_OWNERSHIP_CONFLICT", conflict.GetProperty("code").GetString());
            var details = conflict.GetProperty("conflicts"); Assert.Equal(id, details.GetProperty("documentId").GetGuid());
            Assert.Equal(owner.Award, details.GetProperty("existingLinks")[0].GetProperty("awardId").GetGuid());
            Assert.Equal(owner.Village, details.GetProperty("existingLinks")[0].GetProperty("villages")[0].GetProperty("villageId").GetGuid());
            Assert.False(conflict.TryGetProperty("detectedType", out _)); Assert.False(conflict.TryGetProperty("confidence", out _));
        }
        var linkConflict = await Body(await client.PostAsync($"/api/awards/{target.Award}/documents/{id}/link?villageId={target.Village}", null), HttpStatusCode.Conflict);
        Assert.Equal("PDF_OWNERSHIP_CONFLICT", linkConflict.GetProperty("code").GetString());
        Assert.Equal(0, await Read(db => db.DocumentAwards.CountAsync(x => x.AwardId == target.Award)));
        Assert.Equal(0, await Read(db => db.AwardDocumentExtractionJobs.CountAsync(x => x.TargetAwardId == target.Award)));
    }

    [StaffPostgresFact]
    public async Task Concurrent_cross_award_uploads_have_one_owner_and_same_award_retries_have_one_attachment()
    {
        using var client = await ClientAsync(); var a = await ContextAsync(); var b = await ContextAsync(); var bytes = Pdf("Concurrent PDF ownership fixture " + Guid.NewGuid());
        var uploads = await Task.WhenAll(Upload(client, Core(a), bytes), Upload(client, $"/api/award-pdf-extractions?targetAwardId={b.Award}&selectedVillageId={b.Village}", bytes));
        Assert.Single(uploads, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(uploads, x => x.StatusCode == HttpStatusCode.Conflict);
        var first = await Body(uploads.Single(x => x.StatusCode == HttpStatusCode.Created)); var id = first.GetProperty("documentId").GetGuid();
        var owningAward = await Read(db => db.DocumentAwards.Where(x => x.DocumentId == id).Select(x => x.AwardId).SingleAsync()); var context = owningAward == a.Award ? a : b;
        var retries = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Upload(client, Core(context), bytes)));
        foreach (var retry in retries) Assert.True((await Body(retry)).GetProperty("duplicate").GetBoolean());
        Assert.Equal(1, await Read(db => db.DocumentAwards.CountAsync(x => x.DocumentId == id))); Assert.Equal(1, await Read(db => db.Documents.CountAsync(x => x.Sha256Hash == Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant())));
    }

    [StaffPostgresFact]
    public async Task Officer_relink_preserves_source_hash_evidence_and_extraction_history_and_requires_confirmation()
    {
        using var client = await ClientAsync(); var a = await ContextAsync(); var b = await ContextAsync(); var bytes = Pdf("Incorrect attachment fixture");
        var first = await Body(await Upload(client, Core(a), bytes)); var id = first.GetProperty("documentId").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Add(new SourceEvidence { DocumentId = id, AwardId = a.Award, PageNumber = 1, FactName = "historicSourceFact", VerifiedBy = "Synthetic reviewer", VerifiedAt = DateTimeOffset.UtcNow });
            db.Add(new AwardDocumentExtractionJob { DocumentId = id, TargetAwardId = a.Award, Status = AwardDocumentExtractionJobStatus.NeedsReview }); await db.SaveChangesAsync();
        }
        var input = new AwardPdfLinkResolutionInput(Guid.NewGuid(), "RelinkIncorrectAttachment", a.Award, b.Award, b.Village, 0, 0, 0,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), false, "Officer checked wrong prior attachment", null, null);
        await Body(await client.PostAsJsonAsync($"/api/documents/{id}/award-link-resolutions", input), HttpStatusCode.BadRequest);
        input = input with { OfficerConfirmed = true };
        await Body(await client.PostAsJsonAsync($"/api/documents/{id}/award-link-resolutions", input), HttpStatusCode.OK);
        await Body(await client.PostAsJsonAsync($"/api/documents/{id}/award-link-resolutions", input), HttpStatusCode.OK);
        Assert.True(await Read(db => db.DocumentAwards.Where(x => x.DocumentId == id && x.AwardId == a.Award).Select(x => x.IsDetached).SingleAsync()));
        Assert.False(await Read(db => db.DocumentAwards.Where(x => x.DocumentId == id && x.AwardId == b.Award).Select(x => x.IsDetached).SingleAsync()));
        Assert.Equal(1, await Read(db => db.SourceEvidence.CountAsync(x => x.DocumentId == id && x.AwardId == a.Award)));
        Assert.Equal(1, await Read(db => db.AwardDocumentExtractionJobs.CountAsync(x => x.DocumentId == id && x.TargetAwardId == a.Award)));
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/documents/{id}/content"));
        var changed = input with { Reason = "Different reason" }; await Body(await client.PostAsJsonAsync($"/api/documents/{id}/award-link-resolutions", changed), HttpStatusCode.Conflict);
    }

    [StaffPostgresFact]
    public async Task Explicit_shared_source_requires_coverage_and_never_allows_a_third_accidental_owner()
    {
        using var client = await ClientAsync(); var a = await ContextAsync(); var b = await ContextAsync(); var c = await ContextAsync(); var bytes = Pdf("PDF genuinely contains more than one Award reference");
        var first = await Body(await Upload(client, Core(a), bytes)); var id = first.GetProperty("documentId").GetGuid();
        var input = new AwardPdfLinkResolutionInput(Guid.NewGuid(), "ShareSource", a.Award, b.Award, b.Village, 0, 0, 0, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), true, "Officer verified common source", null, null);
        await Body(await client.PostAsJsonAsync($"/api/documents/{id}/award-link-resolutions", input), HttpStatusCode.BadRequest);
        input = input with { SourcePages = [1], CoverageDescription = "Source page 1 describes both selected Awards" };
        await Body(await client.PostAsJsonAsync($"/api/documents/{id}/award-link-resolutions", input), HttpStatusCode.OK);
        Assert.Equal(2, await Read(db => db.DocumentAwards.CountAsync(x => x.DocumentId == id && x.IsSharedSource && !x.IsDetached)));
        Assert.True((await Body(await Upload(client, Core(a), bytes))).GetProperty("duplicate").GetBoolean());
        Assert.True((await Body(await Upload(client, Core(b), bytes))).GetProperty("duplicate").GetBoolean());
        await Body(await Upload(client, Core(c), bytes), HttpStatusCode.Conflict);
    }

    [StaffPostgresFact]
    public async Task Metadata_correction_audits_original_values_revision_and_scoped_duplicate_identity()
    {
        using var client = await ClientAsync(); var a = await ContextAsync("12/2001-02", new(2002, 1, 1));
        var input = new AwardMetadataInput(Guid.NewGuid(), 0, "13/2001-02", new(2002, 2, 2), "Permanent", "LAA1894", "Synthetic drain", null, null, "Corrected register transcription");
        var updated = await Body(await client.PutAsJsonAsync($"/api/awards/{a.Award}/metadata", input), HttpStatusCode.OK); Assert.Equal(1, updated.GetProperty("revision").GetInt32());
        await Body(await client.PutAsJsonAsync($"/api/awards/{a.Award}/metadata", input), HttpStatusCode.OK);
        await Body(await client.PutAsJsonAsync($"/api/awards/{a.Award}/metadata", input with { MutationId = Guid.NewGuid() }), HttpStatusCode.Conflict);
        var audit = await Read(db => db.AuditLogs.Where(x => x.EntityId == a.Award && x.Action == "StaffMetadataCorrected").SingleAsync());
        Assert.Contains("12/2001-02", audit.OldValues); Assert.Contains("13/2001-02", audit.NewValues); Assert.Equal(SeedData.BootstrapAdminId.ToString(), audit.ChangedBy);
        await Body(await client.PostAsJsonAsync($"/api/villages/{a.Village}/awards", new { awardNumber = "13 / 2001 - 2002", actRegime = "LAA1894" }), HttpStatusCode.Conflict);
        await Body(await client.PostAsJsonAsync($"/api/villages/{a.Village}/awards", new { awardNumber = "13/2001-02", actRegime = "RFCTLARR2013" }));
        var other = await ContextAsync(); await Body(await client.PostAsJsonAsync($"/api/villages/{other.Village}/awards", new { awardNumber = "13/2001-02", actRegime = "LAA1894" }));
    }

    [StaffPostgresFact]
    public async Task Void_archive_restore_preserve_attachments_and_hide_from_active_lists()
    {
        using var client = await ClientAsync(); var a = await ContextAsync(); var first = await Body(await Upload(client, Core(a), Pdf("Lifecycle source"))); var id = first.GetProperty("documentId").GetGuid();
        await Body(await client.PostAsJsonAsync($"/api/awards/{a.Award}/lifecycle", new AwardLifecycleInput(Guid.NewGuid(), 0, "Void", "Mistaken duplicate registration")), HttpStatusCode.OK);
        var list = await Body(await client.GetAsync($"/api/villages/{a.Village}/core-records"), HttpStatusCode.OK); Assert.Empty(list.EnumerateArray());
        Assert.Equal(1, await Read(db => db.DocumentAwards.CountAsync(x => x.DocumentId == id)));
        await Body(await client.GetAsync($"/api/awards/{a.Award}/staff-workflow"), HttpStatusCode.OK);
        await Body(await client.PostAsJsonAsync($"/api/awards/{a.Award}/lifecycle", new AwardLifecycleInput(Guid.NewGuid(), 1, "Restore", "Confirmed genuine record")), HttpStatusCode.OK);
        await Body(await client.PostAsJsonAsync($"/api/awards/{a.Award}/lifecycle", new AwardLifecycleInput(Guid.NewGuid(), 2, "Archive", "Administrative archive")), HttpStatusCode.OK);
        await Body(await client.PostAsJsonAsync($"/api/awards/{a.Award}/lifecycle", new AwardLifecycleInput(Guid.NewGuid(), 2, "Restore", "Stale correction")), HttpStatusCode.Conflict);
        Assert.NotEmpty(await client.GetByteArrayAsync($"/api/documents/{id}/content"));
    }

    [StaffPostgresFact]
    public async Task Multiple_PP_same_date_statement_A_and_NM_dates_optional_and_corrections_preserve_history()
    {
        using var client = await ClientAsync(); var a = await ContextAsync();
        await Body(await Upload(client, Core(a, "PossessionProceeding"), Pdf("Missing date")), HttpStatusCode.BadRequest);
        var days = new[] { "2002-03-01", "2002-04-01", "2002-04-01" }; var attachments = new List<JsonElement>();
        for (var i = 0; i < days.Length; i++) attachments.Add(await Body(await Upload(client, Core(a, "PossessionProceeding", "&legalDate=" + days[i]), Pdf("Possession proceedings part " + i))));
        foreach (var role in new[] { "StatementA", "NM" }) for (var i = 0; i < 2; i++) await Body(await Upload(client, Core(a, role, "&partNumber=" + i), Pdf(role + " part " + i)));
        Assert.Equal(3, await Read(db => db.PossessionEvents.CountAsync(x => x.AwardId == a.Award))); Assert.Equal(2, await Read(db => db.NmDocuments.CountAsync(x => x.AwardId == a.Award)));
        Assert.Equal(0, await Read(db => db.Set<PossessionKhasra>().CountAsync(x => x.PossessionEvent.AwardId == a.Award)));
        var first = attachments[0]; var linkId = first.GetProperty("attachmentId").GetGuid(); var eventId = first.GetProperty("possessionEventId").GetGuid();
        var correction = new AttachmentCorrectionInput(Guid.NewGuid(), 0, "Update", new(2002, 2, 1), null, null, null, null, "Corrected actual source date");
        await Body(await client.PutAsJsonAsync($"/api/awards/{a.Award}/core-document-links/{linkId}", correction), HttpStatusCode.OK);
        Assert.Equal(new DateOnly(2002, 2, 1), await Read(db => db.PossessionEvents.Where(x => x.Id == eventId).Select(x => x.PossessionDate).SingleAsync()));
        await Body(await client.PutAsJsonAsync($"/api/awards/{a.Award}/core-document-links/{linkId}", correction with { MutationId = Guid.NewGuid(), ExpectedRevision = 1, Action = "Detach" }), HttpStatusCode.OK);
        var timeline = await Body(await client.GetAsync($"/api/villages/{a.Village}/acquisition-timeline"), HttpStatusCode.OK); Assert.Equal(2, timeline.GetProperty("events").EnumerateArray().Count(x => x.GetProperty("eventType").GetString() == "PossessionProceedings"));
        Assert.Equal(7, await Read(db => db.DocumentAwards.CountAsync(x => x.AwardId == a.Award)));
    }

    [StaffPostgresFact]
    public async Task Dated_notifications_reuse_canonical_identity_source_bytes_and_timeline_deduplicates_paths()
    {
        using var client = await ClientAsync(); var a = await ContextAsync(date: new(2003, 1, 1)); var other = await ContextAsync(); var notifications = new List<JsonElement>(); var sourcePdfs = new Dictionary<string, byte[]>();
        foreach (var section in new[] { "4(1)", "6", "9", "17(1)", "1(custom)" })
        {
            var path = $"/api/awards/{a.Award}/notification-documents?mutationId={Guid.NewGuid()}&villageId={a.Village}&sectionType={Uri.EscapeDataString(section)}&notificationNumber=SYN-{Uri.EscapeDataString(section)}&notificationDate=2002-01-01";
            sourcePdfs[section] = Pdf("Notification Section " + section);
            notifications.Add(await Body(await Upload(client, path, sourcePdfs[section])));
        }
        var first = notifications[0]; var id = first.GetProperty("notificationId").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var khasra = new Khasra { VillageId = a.Village, DisplayNumber = "1//1", NormalizedNumber = "1//1" };
            db.AddRange(khasra, new NotificationKhasra { Khasra = khasra, NotificationId = id }); await db.SaveChangesAsync();
        }
        var reuse = $"/api/awards/{other.Award}/notification-documents?mutationId={Guid.NewGuid()}&villageId={other.Village}&notificationId={id}";
        var reused = await Body(await Upload(client, reuse, sourcePdfs["4(1)"])); Assert.Equal(first.GetProperty("documentId").GetGuid(), reused.GetProperty("documentId").GetGuid());
        await Body(await Upload(client, $"/api/awards/{a.Award}/notification-documents?mutationId={Guid.NewGuid()}&villageId={a.Village}&notificationId={id}", Pdf("Additional Gazette source for Section 4(1)")));
        var timeline = await Body(await client.GetAsync($"/api/villages/{a.Village}/acquisition-timeline"), HttpStatusCode.OK);
        Assert.Equal(5, timeline.GetProperty("events").EnumerateArray().Count(x => x.GetProperty("eventType").GetString() == "Notification"));
        Assert.Equal("Notification", timeline.GetProperty("events")[0].GetProperty("eventType").GetString());
        Assert.Equal(2, await Read(db => db.DocumentNotifications.CountAsync(x => x.NotificationId == id)));
        var missingDate = $"/api/awards/{a.Award}/notification-documents?mutationId={Guid.NewGuid()}&villageId={a.Village}&sectionType=9&notificationNumber=UNDATED";
        await Body(await Upload(client, missingDate, Pdf("Undated new source")), HttpStatusCode.BadRequest);
    }

    [StaffPostgresFact]
    public async Task Temporary_offer_does_not_create_an_Award_or_invent_dates_and_is_readable_from_timeline()
    {
        using var client = await ClientAsync(); var a = await ContextAsync(); var count = await Read(db => db.Awards.CountAsync());
        var bytes = Pdf("Offer No. SYN-35 Section 35(1) Temporary occupation for synthetic Bagrola Drain one year compensation proceedings.");
        var path = $"/api/villages/{a.Village}/temporary-acquisition-proceedings?mutationId={Guid.NewGuid()}&documentType=TemporaryOffer&actRegime=LAA1894&legalSection=35(1)&purpose=Synthetic%20Bagrola%20Drain&durationMonths=12";
        var result = await Body(await Upload(client, path, bytes)); Assert.Equal(count, await Read(db => db.Awards.CountAsync()));
        var detail = await Body(await client.GetAsync($"/api/temporary-acquisition-proceedings/{result.GetProperty("proceedingId").GetGuid()}"), HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("commencementDate").ValueKind); Assert.Equal(JsonValueKind.Null, detail.GetProperty("expiryDate").ValueKind);
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/documents/{result.GetProperty("documentId").GetGuid()}/content"));
        await Body(await Upload(client, Core(a), bytes), HttpStatusCode.Conflict);
    }

    [StaffPostgresFact]
    public async Task Invalid_PDF_wrong_village_unauthenticated_and_transaction_failure_leave_no_attachment_or_orphan_bytes()
    {
        using var client = await ClientAsync(); var a = await ContextAsync(); var other = await ContextAsync();
        await Body(await Upload(client, Core(a), "%PDF-not-readable"u8.ToArray()), HttpStatusCode.BadRequest);
        var oversized = new byte[1024 * 1024 + 1]; "%PDF-"u8.CopyTo(oversized);
        await Body(await Upload(client, Core(a), oversized), HttpStatusCode.RequestEntityTooLarge);
        await Body(await Upload(client, $"/api/awards/{a.Award}/core-documents?role=Award&villageId={other.Village}", Pdf("Wrong village")), HttpStatusCode.BadRequest);
        using var anonymous = factory.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(anonymous, Core(a), Pdf("Unauthorized upload"))).StatusCode);
        var filesBefore = Directory.GetFiles(Path.Combine(factory.StorageRoot, "documents")).Length;
        factory.Failure.FailAwardId = a.Award;
        var response = await Upload(client, Core(a), Pdf("Rollback source " + Guid.NewGuid())); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await Read(db => db.DocumentAwards.CountAsync(x => x.AwardId == a.Award)));
        Assert.Equal(filesBefore, Directory.GetFiles(Path.Combine(factory.StorageRoot, "documents")).Length);
    }

    [StaffPostgresFact]
    public async Task Own_scope_conflicts_redact_other_records_and_live_revocation_blocks_upload_and_correction()
    {
        using var admin = await ClientAsync(); var source = await ContextAsync(); var target = await ContextAsync(); var bytes = Pdf("Restricted source identity test");
        var stored = await Body(await Upload(admin, Core(source), bytes)); var documentId = stored.GetProperty("documentId").GetGuid();
        var username = "synthetic-own-" + Guid.NewGuid().ToString("N"); Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var user = new AppUser { Username = username, NormalizedUsername = username.ToUpperInvariant(), DisplayName = username, IsActive = true };
            user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>().HashPassword(user, "SyntheticOwn!791");
            var role = new Role { Code = username, Name = username, IsActive = true };
            db.AddRange(user, role, new UserRole { User = user, Role = role });
            foreach (var code in new[] { PermissionCodes.AwardView, PermissionCodes.AwardEdit, PermissionCodes.AwardCoreDocumentUpload, PermissionCodes.AuditView })
                db.Add(new RolePermission { Role = role, PermissionId = await db.Permissions.Where(x => x.Code == code).Select(x => x.Id).SingleAsync(), ScopeMode = ScopeMode.Own });
            var award = await db.Awards.SingleAsync(x => x.Id == target.Award); award.CreatedBy = user.Id.ToString(); await db.SaveChangesAsync(); userId = user.Id;
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { username, password = "SyntheticOwn!791" })).StatusCode);
        var conflict = await Body(await Upload(client, Core(target), bytes), HttpStatusCode.Conflict);
        var details = conflict.GetProperty("conflicts"); Assert.True(details.GetProperty("hasRestrictedLinks").GetBoolean()); Assert.Empty(details.GetProperty("existingLinks").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, details.GetProperty("documentId").ValueKind); Assert.DoesNotContain(source.Award.ToString(), conflict.ToString());
        var correction = new AwardPdfLinkResolutionInput(Guid.NewGuid(), "RelinkIncorrectAttachment", source.Award, target.Award, target.Village, 0, 1, 0,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), true, "Unauthorized source correction attempt", null, null);
        await Body(await client.PostAsJsonAsync($"/api/documents/{documentId}/award-link-resolutions", correction), HttpStatusCode.Forbidden);
        var own = await Body(await Upload(client, Core(target), Pdf("Authorized Own scope upload and job retrieval")));
        var ownDocumentId = own.GetProperty("documentId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/documents/{ownDocumentId}/content")).StatusCode);
        var analysis = await Body(await client.PostAsync($"/api/awards/{target.Award}/documents/{ownDocumentId}/analyze?villageId={target.Village}", null), HttpStatusCode.Accepted);
        await Body(await client.GetAsync($"/api/award-pdf-extractions/{analysis.GetProperty("jobId").GetGuid()}"), HttpStatusCode.OK);
        await Body(await client.GetAsync($"/api/awards/{target.Award}/pdf-extractions"), HttpStatusCode.OK);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var user = await db.AppUsers.SingleAsync(x => x.Id == userId); user.IsActive = false; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(client, Core(target), Pdf("Revoked user upload"))).StatusCode);
        Assert.Equal(1, await Read(db => db.DocumentAwards.CountAsync(x => x.AwardId == target.Award)));
    }

    [StaffPostgresFact]
    public async Task Duplicate_Award_resolution_voids_only_registration_and_preserves_dependent_official_graph()
    {
        using var client = await ClientAsync(); var source = await ContextAsync("44/2001-02"); Guid targetId, khasraId, courtId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var target = new Award { AwardNumber = "44/2001-2002", ActRegime = "LAA1894", CreatedBy = SeedData.BootstrapAdminId.ToString() };
            var khasra = new Khasra { VillageId = source.Village, DisplayNumber = "2//2", NormalizedNumber = "2//2" };
            var court = new CourtCase { CaseNumber = "SYNTHETIC-DEPENDENCY-" + Guid.NewGuid(), CourtName = "Synthetic test court" };
            db.AddRange(target, new AwardVillage { Award = target, VillageId = source.Village }, khasra, new AwardKhasra { AwardId = source.Award, Khasra = khasra },
                court, new CourtCaseAward { CourtCase = court, AwardId = source.Award }, new PossessionEvent { AwardId = source.Award, PossessionDate = new(2002, 3, 1) });
            await db.SaveChangesAsync(); targetId = target.Id; khasraId = khasra.Id; courtId = court.Id;
        }
        var bytes = Pdf("Duplicate Award source"); var stored = await Body(await Upload(client, Core(source), bytes)); var documentId = stored.GetProperty("documentId").GetGuid();
        var input = new AwardPdfLinkResolutionInput(Guid.NewGuid(), "VoidDuplicateAndRelink", source.Award, targetId, source.Village, 0, 0, 0,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), true, "Officer confirmed duplicate registration against original register", null, null);
        await Body(await client.PostAsJsonAsync($"/api/documents/{documentId}/award-link-resolutions", input), HttpStatusCode.OK);
        Assert.Equal(RecordStatus.Archived, await Read(db => db.Awards.Where(x => x.Id == source.Award).Select(x => x.RecordStatus).SingleAsync()));
        Assert.Equal("Void", await Read(db => db.Awards.Where(x => x.Id == source.Award).Select(x => x.ArchiveKind).SingleAsync()));
        Assert.Equal(1, await Read(db => db.Set<AwardKhasra>().CountAsync(x => x.AwardId == source.Award && x.KhasraId == khasraId)));
        Assert.Equal(1, await Read(db => db.Set<CourtCaseAward>().CountAsync(x => x.AwardId == source.Award && x.CourtCaseId == courtId)));
        Assert.Equal(1, await Read(db => db.PossessionEvents.CountAsync(x => x.AwardId == source.Award)));
        Assert.Equal(2, await Read(db => db.DocumentAwards.CountAsync(x => x.DocumentId == documentId)));
    }

    [StaffPostgresFact]
    public async Task Link_resolution_failure_rolls_back_source_detach_target_link_audit_and_receipt()
    {
        using var client = await ClientAsync(); var source = await ContextAsync(); var target = await ContextAsync(); var bytes = Pdf("Officer correction rollback test");
        var stored = await Body(await Upload(client, Core(source), bytes)); var documentId = stored.GetProperty("documentId").GetGuid();
        var input = new AwardPdfLinkResolutionInput(Guid.NewGuid(), "RelinkIncorrectAttachment", source.Award, target.Award, target.Village, 0, 0, 0,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), true, "Synthetic rollback test", null, null);
        factory.Failure.FailAwardId = target.Award;
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync($"/api/documents/{documentId}/award-link-resolutions", input)).StatusCode);
        Assert.False(await Read(db => db.DocumentAwards.Where(x => x.DocumentId == documentId && x.AwardId == source.Award).Select(x => x.IsDetached).SingleAsync()));
        Assert.Equal(0, await Read(db => db.DocumentAwards.CountAsync(x => x.DocumentId == documentId && x.AwardId == target.Award)));
        Assert.Equal(0, await Read(db => db.Set<LandRecordMutationReceipt>().CountAsync(x => x.MutationId == input.MutationId)));
        Assert.Equal(0, await Read(db => db.AuditLogs.CountAsync(x => x.EntityId == stored.GetProperty("attachmentId").GetGuid() && x.Action == "StaffPdfOwnershipResolved")));
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/documents/{documentId}/content"));
        await Body(await client.PostAsJsonAsync($"/api/documents/{documentId}/award-link-resolutions", input), HttpStatusCode.OK);
    }

    [StaffPostgresFact]
    public async Task Legacy_undated_notifications_stay_readable_and_new_absent_reference_requires_reason()
    {
        using var client = await ClientAsync(); var context = await ContextAsync(date: new(2000, 1, 1)); Guid legacyId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var legacy = new Notification { SectionType = "4", NotificationNumber = "LEGACY-UNDATED-" + Guid.NewGuid() };
            db.AddRange(legacy, new AwardNotification { AwardId = context.Award, Notification = legacy }); await db.SaveChangesAsync(); legacyId = legacy.Id;
        }
        await Body(await client.GetAsync($"/api/notifications/{legacyId}"), HttpStatusCode.OK);
        await Body(await client.PostAsJsonAsync($"/api/notifications/{legacyId}/finalize", new FinalizeNotificationInput(Guid.NewGuid(), 0, "Attempt without source date")), HttpStatusCode.BadRequest);
        var path = $"/api/awards/{context.Award}/notification-documents?mutationId={Guid.NewGuid()}&villageId={context.Village}&sectionType=17(1)&notificationDate=2001-01-01";
        var bytes = Pdf("Notification source with absent reference number");
        await Body(await Upload(client, path, bytes), HttpStatusCode.BadRequest);
        await Body(await Upload(client, path + "&numberAbsentReason=Source%20reference%20is%20unreadable", bytes));
        var timeline = await Body(await client.GetAsync($"/api/villages/{context.Village}/acquisition-timeline"), HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null, timeline.GetProperty("events")[2].GetProperty("legalDate").ValueKind);
        Assert.True(timeline.GetProperty("events")[1].GetProperty("needsReview").GetBoolean());
    }

    [StaffPostgresFact]
    public async Task Legacy_NM_upload_route_reuses_same_review_and_rejects_wrong_Award()
    {
        using var client = await ClientAsync(); var context = await ContextAsync(); var other = await ContextAsync(); var bytes = Pdf("NM source route reconciliation");
        var core = await Body(await Upload(client, Core(context, "NM"), bytes));
        var retry = await Body(await Upload(client, $"/api/awards/{context.Award}/nm-documents?villageId={context.Village}", bytes));
        Assert.Equal(core.GetProperty("nmDocumentId").GetGuid(), retry.GetProperty("nmDocumentId").GetGuid());
        Assert.True(retry.GetProperty("duplicate").GetBoolean());
        await Body(await Upload(client, $"/api/awards/{other.Award}/nm-documents?villageId={other.Village}", bytes), HttpStatusCode.Conflict);
        Assert.Equal(1, await Read(db => db.NmDocuments.CountAsync(x => x.AwardId == context.Award)));
    }

    [Fact]
    public void Section_35_offer_with_incidental_Award_reference_never_emits_final_Award_candidates()
    {
        var page = new NormalizedDocumentPage(1, 600, 800, AwardDocumentExtractionMethod.EmbeddedText,
            "Offer No. SYN-35\nSection 35(1) compensation proceedings for temporary occupation of synthetic Bagrola Drain.\nAward No: REFERENCE-ONLY", []);
        var rules = new AwardExtractionRuleEngine(new TextConceptMatcher(), new StrictKhasraParser(), new StrictDateParser(), new StrictAreaParser());
        var candidates = rules.Extract([page], new(null, null, null, []));
        Assert.DoesNotContain(candidates, x => x.Input.CandidateType == AwardIngestionCandidateType.AwardCore);
        Assert.Contains(candidates, x => x.Input.CandidateType == AwardIngestionCandidateType.UnmappedAwardFinding);
    }

    [StaffPostgresFact]
    public async Task Legacy_notification_date_can_be_reviewed_then_finalized_and_distinct_absent_number_sources_are_valid()
    {
        using var client = await ClientAsync(); var context = await ContextAsync(); Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var notification = new Notification { SectionType = "9", NotificationNumber = "SYN-LEGACY-" + Guid.NewGuid() };
            db.AddRange(notification, new AwardNotification { AwardId = context.Award, Notification = notification }); await db.SaveChangesAsync(); id = notification.Id;
        }
        await Body(await Upload(client, $"/api/awards/{context.Award}/notification-documents?mutationId={Guid.NewGuid()}&villageId={context.Village}&notificationId={id}", Pdf("Legacy dated source for correction")));
        var number = await Read(db => db.Notifications.Where(x => x.Id == id).Select(x => x.NotificationNumber).SingleAsync());
        var input = new NotificationMetadataInput(Guid.NewGuid(), 0, "9", number, new(2002, 1, 1), null, null, "Officer transcribed actual issue date from source");
        await Body(await client.PutAsJsonAsync($"/api/notifications/{id}/metadata", input), HttpStatusCode.OK);
        var finalized = await Body(await client.PostAsJsonAsync($"/api/notifications/{id}/finalize", new FinalizeNotificationInput(Guid.NewGuid(), 1, "Source reviewed against corrected issue date")), HttpStatusCode.OK);
        Assert.True(finalized.GetProperty("finalized").GetBoolean()); Assert.Equal(id, finalized.GetProperty("notificationId").GetGuid());
        await Body(await client.PutAsJsonAsync($"/api/notifications/{id}/metadata", input with { MutationId = Guid.NewGuid(), ExpectedRevision = 2, NotificationDate = new(2003, 1, 1) }), HttpStatusCode.Conflict);
        var firstPath = $"/api/awards/{context.Award}/notification-documents?mutationId={Guid.NewGuid()}&villageId={context.Village}&sectionType=1(custom)&notificationDate=2001-01-01&numberAbsentReason=Reference%20not%20printed";
        var secondPath = firstPath.Replace(firstPath.Split("mutationId=")[1].Split('&')[0], Guid.NewGuid().ToString());
        var first = await Body(await Upload(client, firstPath, Pdf("First genuine source with no printed reference")));
        var second = await Body(await Upload(client, secondPath, Pdf("Second distinct genuine source with no printed reference")));
        Assert.NotEqual(first.GetProperty("notificationId").GetGuid(), second.GetProperty("notificationId").GetGuid());
    }

    [StaffPostgresFact]
    public async Task Legacy_duplicate_Document_rows_are_preserved_while_hash_wide_ownership_blocks_new_accidental_links()
    {
        using var client = await ClientAsync(); var a = await ContextAsync(); var b = await ContextAsync(); var c = await ContextAsync(); var bytes = Pdf("Historical duplicate hash rows fixture");
        var first = await Body(await Upload(client, Core(a), bytes)); var id = first.GetProperty("documentId").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var original = await db.Documents.SingleAsync(x => x.Id == id);
            var duplicate = new LAC.Domain.Document { DocumentType = original.DocumentType, OriginalFileName = original.OriginalFileName, StoragePath = original.StoragePath, Sha256Hash = original.Sha256Hash, MimeType = original.MimeType, FileSize = original.FileSize };
            db.AddRange(duplicate, new DocumentAward { AwardId = b.Award, Document = duplicate, CoreDocumentRole = "Award", VillageId = b.Village }); await db.SaveChangesAsync();
        }
        var conflict = await Body(await Upload(client, Core(c), bytes), HttpStatusCode.Conflict);
        Assert.Equal(2, conflict.GetProperty("conflicts").GetProperty("existingLinks").GetArrayLength());
        Assert.True((await Body(await Upload(client, Core(a), bytes))).GetProperty("duplicate").GetBoolean());
        Assert.True((await Body(await Upload(client, Core(b), bytes))).GetProperty("duplicate").GetBoolean());
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); Assert.Equal(2, await Read(db => db.Documents.CountAsync(x => x.Sha256Hash == hash)));
        var resolution = new AwardPdfLinkResolutionInput(Guid.NewGuid(), "ShareSource", a.Award, c.Award, c.Village, 0, 0, 0,
            hash, true, "Cannot bypass another historical exclusive owner", new[] { 1 }, "Synthetic common source coverage");
        var rejected = await Body(await client.PostAsJsonAsync($"/api/documents/{id}/award-link-resolutions", resolution), HttpStatusCode.Conflict);
        Assert.Equal("UnreviewedSharedOwnership", rejected.GetProperty("code").GetString());
        var alreadyLinked = resolution with { MutationId = Guid.NewGuid(), TargetAwardId = b.Award, VillageId = b.Village };
        var targetConflict = await Body(await client.PostAsJsonAsync($"/api/documents/{id}/award-link-resolutions", alreadyLinked), HttpStatusCode.Conflict);
        Assert.Equal("AlreadyAttached", targetConflict.GetProperty("code").GetString());
        Assert.Equal(1, await Read(db => db.DocumentAwards.CountAsync(x => x.AwardId == b.Award)));
        Assert.Equal(0, await Read(db => db.DocumentAwards.CountAsync(x => x.AwardId == c.Award)));
    }

    [StaffPostgresFact]
    public async Task Mixed_visibility_hash_conflict_uses_only_authorized_document_identity_and_blocks_hidden_owner_bypass()
    {
        using var admin = await ClientAsync(); var hidden = await ContextAsync(); var visible = await ContextAsync(); var target = await ContextAsync();
        var bytes = Pdf("Mixed visibility historical hash ownership");
        var stored = await Body(await Upload(admin, Core(hidden), bytes)); var hiddenId = stored.GetProperty("documentId").GetGuid(); Guid visibleId;
        var username = "synthetic-mixed-" + Guid.NewGuid().ToString("N");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var user = new AppUser { Username = username, NormalizedUsername = username.ToUpperInvariant(), DisplayName = username, IsActive = true };
            user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>().HashPassword(user, "SyntheticOwn!791");
            var role = new Role { Code = username, Name = username, IsActive = true };
            db.AddRange(user, role, new UserRole { User = user, Role = role });
            foreach (var code in new[] { PermissionCodes.AwardView, PermissionCodes.AwardEdit, PermissionCodes.AwardCoreDocumentUpload, PermissionCodes.AuditView })
                db.Add(new RolePermission { Role = role, PermissionId = await db.Permissions.Where(x => x.Code == code).Select(x => x.Id).SingleAsync(), ScopeMode = ScopeMode.Own });
            foreach (var id in new[] { visible.Award, target.Award }) (await db.Awards.SingleAsync(x => x.Id == id)).CreatedBy = user.Id.ToString();
            var original = await db.Documents.SingleAsync(x => x.Id == hiddenId); original.OriginalFileName = "hidden-source-title.pdf";
            var duplicate = new LAC.Domain.Document { DocumentType = original.DocumentType, OriginalFileName = "authorized-source.pdf", StoragePath = original.StoragePath, Sha256Hash = original.Sha256Hash, MimeType = original.MimeType, FileSize = original.FileSize };
            db.AddRange(duplicate, new DocumentAward { AwardId = visible.Award, Document = duplicate, CoreDocumentRole = "Award", VillageId = visible.Village });
            await db.SaveChangesAsync(); visibleId = duplicate.Id;
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { username, password = "SyntheticOwn!791" })).StatusCode);
        var conflict = await Body(await Upload(client, Core(target), bytes), HttpStatusCode.Conflict); var details = conflict.GetProperty("conflicts");
        Assert.True(details.GetProperty("hasRestrictedLinks").GetBoolean()); Assert.Equal(1, details.GetProperty("existingLinks").GetArrayLength());
        Assert.Equal(visibleId, details.GetProperty("documentId").GetGuid()); Assert.Equal("authorized-source.pdf", details.GetProperty("documentReference").GetString());
        Assert.Equal($"/api/documents/{visibleId}/content", details.GetProperty("contentPath").GetString());
        Assert.DoesNotContain(hidden.Award.ToString(), conflict.ToString()); Assert.DoesNotContain(hiddenId.ToString(), conflict.ToString()); Assert.DoesNotContain("hidden-source-title", conflict.ToString());
        var input = new AwardPdfLinkResolutionInput(Guid.NewGuid(), "ShareSource", visible.Award, target.Award, target.Village, 1, 1, 0,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), true, "Attempt to bypass hidden ownership", new[] { 1 }, "Synthetic page coverage");
        await Body(await client.PostAsJsonAsync($"/api/documents/{visibleId}/award-link-resolutions", input), HttpStatusCode.Forbidden);
        Assert.Equal(0, await Read(db => db.DocumentAwards.CountAsync(x => x.AwardId == target.Award)));
    }
}
