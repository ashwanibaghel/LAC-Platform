using System.Net;
using System.Security.Cryptography;
using System.Text;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace LAC.Tests;

public sealed class DelhiHighCourtSyncTests
{
    [Fact]
    public void FiveHourSchedule_UsesLatestLiveAttemptAcrossRestartOrManualRun()
    {
        var config = new ConfigurationBuilder().Build();
        var interval = DelhiHighCourtSyncWorker.ConfiguredIntervalMinutes(config);
        Assert.Equal(300, interval);
        var now = new DateTimeOffset(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, DelhiHighCourtSyncWorker.RemainingDelay(null, now, interval));
        Assert.Equal(TimeSpan.FromHours(3), DelhiHighCourtSyncWorker.RemainingDelay(now.AddHours(-2), now, interval));
        Assert.Equal(TimeSpan.Zero, DelhiHighCourtSyncWorker.RemainingDelay(now.AddHours(-5), now, interval));
        // The worker reads the latest persisted LiveWindow attempt, whether manual or automatic.
        Assert.Equal(TimeSpan.FromHours(5), DelhiHighCourtSyncWorker.RemainingDelay(now, now, interval));
    }

    [Fact]
    public async Task RestartSchedule_UsesMostRecentPersistedLiveAttemptIncludingManualSync()
    {
        using var fixture = new Fixture();
        var now = new DateTimeOffset(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, await DelhiHighCourtSyncWorker.RemainingDelayAsync(fixture.Db, now, 300, default));
        fixture.Db.CourtExternalSyncRuns.Add(new CourtExternalSyncRun
        { Mode = CourtExternalSyncMode.HistoricalBackfill, StartedAt = now });
        fixture.Db.CourtExternalSyncRuns.Add(new CourtExternalSyncRun
        { Mode = CourtExternalSyncMode.LiveWindow, StartedAt = now.AddHours(-2) });
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(TimeSpan.FromHours(3), await DelhiHighCourtSyncWorker.RemainingDelayAsync(fixture.Db, now, 300, default));
        fixture.Db.CourtExternalSyncRuns.Add(new CourtExternalSyncRun
        { Mode = CourtExternalSyncMode.LiveWindow, StartedAt = now.AddMinutes(-20) });
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(TimeSpan.FromHours(4) + TimeSpan.FromMinutes(40),
            await DelhiHighCourtSyncWorker.RemainingDelayAsync(fixture.Db, now, 300, default));
        fixture.Db.CourtExternalSyncRuns.Add(new CourtExternalSyncRun
        { Mode = CourtExternalSyncMode.LiveWindow, StartedAt = now,
            Status = CourtExternalSyncRunStatus.Completed,
            FailureMessage = "No Delhi High Court matters are currently registered." });
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(TimeSpan.FromHours(4) + TimeSpan.FromMinutes(40),
            await DelhiHighCourtSyncWorker.RemainingDelayAsync(fixture.Db, now, 300, default));
    }

    [Fact]
    public async Task NoRegisteredDhcCases_IsHealthyNoOpWithoutOfficerReview()
    {
        using var fixture = new Fixture();
        fixture.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "unrelated.pdf",
            "1 W.P.(C)-9999/2026");
        var run = await fixture.Sync.RunAsync(null, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, run.Status);
        Assert.Equal(0, run.ObservationsCreated);
        Assert.Empty(fixture.Db.CourtExternalListingObservations);
        Assert.Empty(await fixture.Sync.ObservationsAsync(null, true, fixture.User.Id, default));
    }

    [Fact]
    public async Task LargePublicList_PersistsOnlyExactRegisteredDhcTargets()
    {
        using var fixture = new Fixture();
        fixture.Case("W.P.(C) 7003/2026");
        fixture.Case("W.P.(C) 7004/2026");
        fixture.Case("W.P.(C) 7005/2026");
        var lines = Enumerable.Range(1, 2500)
            .Select(n => $"{n} W.P.(C)-{n + 6000}/2026 TEST PARTY").ToArray();
        fixture.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "large.pdf", lines);
        var run = await fixture.Sync.RunAsync(null, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, run.Status);
        Assert.Equal(3, run.ObservationsCreated);
        Assert.Equal(0, run.ReviewCount);
        Assert.Equal(3, await fixture.Db.CourtExternalListingObservations.CountAsync());
        var oldSource = new CourtExternalSourceDocument { SourceUrl = "https://delhihighcourt.nic.in/files/old.pdf",
            SourceTitle = "Old unrelated publication", Status = CourtExternalSourceStatus.NeedsReview };
        fixture.Db.CourtExternalSourceDocuments.Add(oldSource);
        fixture.Db.CourtExternalListingObservations.Add(new CourtExternalListingObservation
        {
            SourceDocument = oldSource, NormalizedCaseIdentity = "delhihighcourt|wpc|9999|2026",
            ListingDate = new DateOnly(2026, 9, 30), Status = CourtExternalListingStatus.NeedsReview
        });
        await fixture.Db.SaveChangesAsync();
        Assert.Empty(await fixture.Sync.ObservationsAsync(null, true, fixture.User.Id, default));
        Assert.Empty(await fixture.Sync.SourceReviewsAsync(fixture.User.Id, default));
    }

    [Fact]
    public async Task LiveWindow_RetainsOnlyTargetRelevantPdfAcrossLargePublications()
    {
        using var fixture = new Fixture();
        fixture.Case("W.P.(C) 7003/2026");
        fixture.Case("W.P.(C) 7004/2026");
        fixture.Case("W.P.(C) 9000/2026");
        fixture.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "unrelated-large.pdf",
            Enumerable.Range(1, 2500).Select(n => $"{n} W.P.(C)-{n + 10000}/2026 OTHER PARTY").ToArray());
        fixture.Publish("ADVANCE CAUSE LIST OF CASES FOR 01.10.2026", "01-10-2026", "matched-large.pdf",
            Enumerable.Range(1, 2500).Select(n => $"{n} W.P.(C)-{n + 6000}/2026 TEST PARTY").ToArray());

        var first = await fixture.Sync.RunAsync(null, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, first.Status);
        Assert.Equal(2, first.SourceDocumentsProcessed);
        Assert.Equal(2, first.ObservationsCreated);
        var sources = await fixture.Db.CourtExternalSourceDocuments.OrderBy(x => x.SourceUrl).ToListAsync();
        Assert.Equal(2, sources.Count);
        Assert.All(sources, source => { Assert.Equal(CourtExternalSourceStatus.Processed, source.Status); Assert.NotNull(source.Sha256Hash); });
        Assert.Null(sources.Single(x => x.SourceUrl.Contains("unrelated-large.pdf")).DocumentId);
        Assert.NotNull(sources.Single(x => x.SourceUrl.Contains("matched-large.pdf")).DocumentId);
        Assert.Single(fixture.Db.Documents);
        Assert.Single(fixture.Storage.Files);
        Assert.Equal(2, await fixture.Db.CourtExternalListingObservations.CountAsync());
        Assert.All(fixture.Db.CourtExternalListingObservations,
            observation => Assert.Contains(observation.NormalizedCaseIdentity, new[] { "delhihighcourt|wpc|7003|2026", "delhihighcourt|wpc|7004|2026" }));

        var second = await fixture.Sync.RunAsync(null, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, second.Status);
        Assert.Equal(0, second.SourceDocumentsProcessed);
        Assert.Equal(2, fixture.Source.PdfRequests);
        Assert.Single(fixture.Storage.Files);
    }

    [Fact]
    public async Task UnrelatedDeletionNote_LeavesOnlyProcessedSourceMetadata()
    {
        using var fixture = new Fixture();
        fixture.Case("W.P.(C) 7003/2026");
        fixture.Publish("Deletion Note for 30.09.2026", "30-09-2026", "unrelated-deletion.pdf",
            "1 W.P.(C)-9999/2026");

        var run = await fixture.Sync.RunAsync(null, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, run.Status);
        Assert.Equal(1, run.SourceDocumentsProcessed);
        var source = Assert.Single(fixture.Db.CourtExternalSourceDocuments);
        Assert.Equal(CourtExternalSourceStatus.Processed, source.Status);
        Assert.NotNull(source.Sha256Hash);
        Assert.Null(source.DocumentId);
        Assert.Empty(fixture.Db.Documents);
        Assert.Empty(fixture.Storage.Files);
        Assert.Empty(fixture.Db.CourtExternalListingObservations);
    }

    [Fact]
    public async Task UnchangedTargets_DoNotRedownloadEphemeralUnmatchedPdf()
    {
        using var f = new Fixture();
        f.Case("W.P.(C) 7003/2026");
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "unmatched.pdf",
            "1 W.P.(C)-8004/2026");
        await f.Sync.RunAsync(null, default);
        var source = Assert.Single(f.Db.CourtExternalSourceDocuments);
        Assert.NotNull(source.LiveTargetSetFingerprint);
        Assert.Null(source.DocumentId);
        await f.Sync.RunAsync(null, default);
        Assert.Equal(1, f.Source.PdfRequests);
        Assert.Empty(f.Storage.Files);
        Assert.Empty(f.Db.CourtExternalListingObservations);
    }

    [Fact]
    public async Task NewTarget_RechecksEphemeralPdfAndRetainsNewEvidence()
    {
        using var f = new Fixture();
        f.Case("W.P.(C) 7003/2026");
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "later-target.pdf",
            "1 W.P.(C)-8004/2026");
        await f.Sync.RunAsync(null, default);
        var source = Assert.Single(f.Db.CourtExternalSourceDocuments);
        var firstFingerprint = source.LiveTargetSetFingerprint;
        Assert.Null(source.DocumentId);
        f.Case("W.P.(C) 8004/2026");

        var second = await f.Sync.RunAsync(null, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, second.Status);
        Assert.NotEqual(firstFingerprint, source.LiveTargetSetFingerprint);
        Assert.Equal(2, f.Source.PdfRequests);
        Assert.NotNull(source.DocumentId);
        Assert.Single(f.Db.Documents);
        Assert.Single(f.Storage.Files);
        Assert.Equal("delhihighcourt|wpc|8004|2026", Assert.Single(f.Db.CourtExternalListingObservations).NormalizedCaseIdentity);
    }

    [Fact]
    public async Task NewTarget_ReusesStoredPdfWithoutNetworkOrDuplicateObservation()
    {
        using var f = new Fixture();
        f.Case("W.P.(C) 7003/2026");
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "stored-targets.pdf",
            "1 W.P.(C)-7003/2026", "2 W.P.(C)-8004/2026");
        await f.Sync.RunAsync(null, default);
        var source = Assert.Single(f.Db.CourtExternalSourceDocuments);
        var documentId = source.DocumentId;
        Assert.NotNull(documentId);
        Assert.Single(f.Db.CourtExternalListingObservations);
        f.Case("W.P.(C) 8004/2026");

        var second = await f.Sync.RunAsync(null, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, second.Status);
        Assert.Equal(1, second.ObservationsCreated);
        Assert.Equal(1, f.Source.PdfRequests);
        Assert.Equal(documentId, source.DocumentId);
        Assert.Single(f.Db.Documents);
        Assert.Single(f.Storage.Files);
        Assert.Equal(2, f.Db.CourtExternalListingObservations.Count());
        Assert.Equal(2, f.Db.CourtExternalListingObservations.Select(x => x.NormalizedCaseIdentity).Distinct().Count());
    }

    [Fact]
    public async Task TargetFingerprint_IsIndependentOfCaseInsertionOrder()
    {
        using var first = new Fixture();
        using var second = new Fixture();
        first.Case("W.P.(C) 7003/2026"); first.Case("W.P.(C) 8004/2026");
        second.Case("W.P.(C) 8004/2026"); second.Case("W.P.(C) 7003/2026");
        first.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "order.pdf", "1 W.P.(C)-9999/2026");
        second.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "order.pdf", "1 W.P.(C)-9999/2026");
        await first.Sync.RunAsync(null, default);
        await second.Sync.RunAsync(null, default);
        Assert.Equal(Assert.Single(first.Db.CourtExternalSourceDocuments).LiveTargetSetFingerprint,
            Assert.Single(second.Db.CourtExternalSourceDocuments).LiveTargetSetFingerprint);
    }

    [Fact]
    public async Task CorrectedCanonicalIdentity_ChangesFingerprintAndRechecksSource()
    {
        using var f = new Fixture();
        var target = f.Case("W.P.(C) 7003/2026");
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "correction.pdf",
            "1 W.P.(C)-8004/2026");
        await f.Sync.RunAsync(null, default);
        var source = Assert.Single(f.Db.CourtExternalSourceDocuments);
        var firstFingerprint = source.LiveTargetSetFingerprint;
        target.CaseNumber = "W.P.(C) 8004/2026";
        await f.Db.SaveChangesAsync();

        await f.Sync.RunAsync(null, default);
        Assert.NotEqual(firstFingerprint, source.LiveTargetSetFingerprint);
        Assert.Equal(2, f.Source.PdfRequests);
        Assert.Single(f.Db.CourtExternalListingObservations);
    }

    [Fact]
    public async Task OfficialGet_RetriesAtMostTwiceWithoutDuplicatingEvidence()
    {
        using var fixture = new Fixture();
        fixture.Case();
        fixture.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "retry.pdf",
            "1 W.P.(C)-7003/2026");
        fixture.Source.TransientFailuresRemaining = 2;
        var run = await fixture.Sync.RunAsync(null, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, run.Status);
        Assert.Equal(2, fixture.Source.TransientAttempts);
        Assert.Single(fixture.Db.CourtExternalListingObservations);
        Assert.Single(fixture.Db.Documents);
    }

    private readonly ITestOutputHelper output;
    public DelhiHighCourtSyncTests(ITestOutputHelper output) => this.output = output;
    private sealed class Clock : IOfficeClock
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public DateOnly Today = new(2026, 9, 28);
        public DateOnly GetCurrentDate() => Today;
        public DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Storage : IDocumentStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct) =>
            throw new NotSupportedException();
        public async Task<DocumentStorageWriteResult> SaveAndHashAsync(Stream content, string fileName, CancellationToken ct)
        {
            using var output = new MemoryStream();
            await content.CopyToAsync(output, ct);
            var path = Guid.NewGuid().ToString("N") + ".pdf";
            var bytes = output.ToArray();
            Files.Add(path, bytes);
            return new(path, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
        }
        public Task DeleteAsync(string storagePath, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null);
        public StorageHealth GetHealth() => new("memory", true, null, null);
    }

    private sealed class SourceHandler : HttpMessageHandler
    {
        public string Html = "";
        public Dictionary<string, byte[]> Pdfs { get; } = [];
        public Dictionary<string, string> Pages { get; } = [];
        public int PdfRequests;
        public TaskCompletionSource<bool>? HoldPdf;
        public TaskCompletionSource<bool> PdfStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Fail;
        public bool Redirect;
        public int TransientFailuresRemaining;
        public int TransientAttempts;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (TransientFailuresRemaining > 0)
            {
                TransientFailuresRemaining--;
                TransientAttempts++;
                throw new IOException("Temporary official connection ended.");
            }
            if (Fail) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (Redirect) return new HttpResponseMessage(HttpStatusCode.Redirect)
            { Headers = { Location = new Uri("https://evil.example/redirect") } };
            if (Pages.TryGetValue(request.RequestUri!.AbsoluteUri, out var page))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(page) };
            if (request.RequestUri.AbsolutePath == "/web/cause-lists/cause-list")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Html) };
            PdfRequests++;
            PdfStarted.TrySetResult(true);
            if (HoldPdf != null) await HoldPdf.Task.WaitAsync(ct);
            return Pdfs.TryGetValue(request.RequestUri.AbsoluteUri, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
    private sealed class Factory(SourceHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class TestLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
    private sealed class LiveFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            { Timeout = TimeSpan.FromSeconds(90) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LAC-Platform-CourtCauseListSync/1.0 (public-source smoke)");
            return client;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string DatabaseName { get; } = Guid.NewGuid().ToString();
        public InMemoryDatabaseRoot DatabaseRoot { get; } = new();
        public LacDbContext Db { get; }
        public Clock Clock { get; } = new();
        public Storage Storage { get; } = new();
        public SourceHandler Source { get; } = new();
        public DelhiHighCourtSyncGate Gate { get; } = new();
        public AppUser User { get; } = new() { Username = "dhc-officer", NormalizedUsername = "DHC-OFFICER", DisplayName = "DHC Officer" };
        public CourtAuthorizationService Auth { get; }
        public DelhiHighCourtSyncService Sync { get; }
        public CourtProjectionService Projection { get; }

        public Fixture()
        {
            Db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>()
                .UseInMemoryDatabase(DatabaseName, DatabaseRoot).Options);
            Db.AppUsers.Add(User);
            var role = new Role { Code = "DHC_OFFICER", Name = "DHC Officer" };
            Db.Roles.Add(role);
            Db.UserRoles.Add(new UserRole { UserId = User.Id, RoleId = role.Id });
            foreach (var code in new[] { PermissionCodes.CourtView, PermissionCodes.CourtEdit, PermissionCodes.ScheduleView })
            {
                var permission = new Permission { Code = code, Name = code, Category = "Court" };
                Db.Permissions.Add(permission);
                Db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = ScopeMode.All });
            }
            Db.SaveChanges();
            Auth = new CourtAuthorizationService(Db, null!, null!, null!);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CourtSync:DelhiHighCourt:BaseUrl"] = DelhiHighCourtSyncService.OfficialPage,
                ["CourtSync:DelhiHighCourt:ForwardDays"] = "7"
            }).Build();
            Sync = new DelhiHighCourtSyncService(Db, Storage, Clock, new Factory(Source), config, Auth, Gate);
            Projection = new CourtProjectionService(Db, Auth, new ScheduleAuthorizationService(Db, Auth), Clock);
        }

        public CourtCase Case(string number = "W.P.(C) 7003/2026", string court = "Delhi High Court", string status = "Pending")
        {
            var item = new CourtCase { CaseNumber = number, CourtName = court, CurrentStatus = status };
            Db.CourtCases.Add(item);
            Db.SaveChanges();
            return item;
        }

        public void Publish(string title, string date, string file, params string[] lines)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            var bytes = QuestPDF.Fluent.Document.Create(document => document.Page(page =>
            {
                page.Margin(36);
                page.Content().Column(column => {
                    column.Item().Text(title);
                    column.Item().Text(date);
                    foreach (var line in lines) column.Item().Text(line);
                });
            })).GeneratePdf();
            var url = "https://delhihighcourt.nic.in/files/2026-09/cause-list/" + file;
            Source.Pdfs[url] = bytes;
            Source.Html += $"<tr><td headers='view-title-table-column'>{title}</td>" +
                $"<td headers='view-field-date-table-column'>{date}</td>" +
                $"<td><a href='/files/2026-09/cause-list/{file}'>Download</a></td></tr>";
        }

        public async Task<string?> OperationalDateSourceAsync(Guid id)
        {
            var page = await Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(CaseNumber: "7003/2026"), User.Id);
            return page.Items.Single(x => x.Id == id).OperationalNdohSource;
        }
        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public void Discovery_RejectsUnapprovedHostsAndClassifiesConservatively()
    {
        var page = new Uri(DelhiHighCourtSyncService.OfficialPage);
        var html = "<tr><td headers='view-title-table-column'>ADVANCE CAUSE LIST OF CASES FOR 30.09.2026</td>" +
            "<td headers='view-field-date-table-column'>30-09-2026</td><td><a href='/files/list.pdf'>Download</a></td></tr>" +
            "<tr><td headers='view-title-table-column'>Supplementary Pronouncement of Judgment on 29.09.2026</td>" +
            "<td headers='view-field-date-table-column'>29-09-2026</td><td><a href='https://evil.example/list.pdf'>Download</a></td></tr>";
        var publication = Assert.Single(DelhiHighCourtCauseListParser.Discover(html, page));
        Assert.Equal(new DateOnly(2026, 9, 30), publication.ListingDate);
        Assert.Equal(CourtExternalSourceKind.OrdinaryListing, publication.Kind);
        Assert.False(DelhiHighCourtCauseListParser.IsApprovedUri(new Uri("http://delhihighcourt.nic.in/a.pdf")));
        Assert.False(DelhiHighCourtCauseListParser.IsApprovedUri(new Uri("https://evil.example/a.pdf")));
        Assert.Equal(CourtExternalSourceKind.Unsupported, DelhiHighCourtCauseListParser.Classify("Pre Lok Adalat Cause List"));
        Assert.Equal(CourtExternalSourceKind.Unsupported, DelhiHighCourtCauseListParser.Classify("Pronouncement of Judgment"));
        Assert.Equal(CourtExternalSourceKind.Unsupported, DelhiHighCourtCauseListParser.Classify("Unknown publication"));
        Assert.Equal(CourtExternalSourceKind.DeletionOrCorrigendum, DelhiHighCourtCauseListParser.Classify("Deletion Note for 30.09.2026"));
    }

    [Fact]
    public async Task FutureExactListing_IsEvidenceBackedAndIdempotent_WithoutProceedingOrCalendar()
    {
        using var f = new Fixture();
        var item = f.Case();
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "advance.pdf",
            "30.09.2026 ADVANCE CAUSE LIST", "1 W.P.(C)-7003/2026 TEST PARTY", "CM APPL. 1234/2026");
        var first = await f.Sync.RunAsync(null, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, first.Status);
        Assert.Equal(1, first.ObservationsAccepted);
        var observation = Assert.Single(f.Db.CourtExternalListingObservations);
        Assert.Equal(CourtExternalListingStatus.Accepted, observation.Status);
        Assert.Equal(item.Id, observation.CourtCaseId);
        Assert.Equal(1, observation.SourcePageNumber);
        Assert.Contains("7003/2026", observation.RawMatchedText);
        var source = Assert.Single(f.Db.CourtExternalSourceDocuments);
        Assert.Equal(source.Sha256Hash, Assert.Single(f.Db.Documents).Sha256Hash);
        Assert.Single(f.Storage.Files);
        Assert.Empty(f.Db.CourtProceedings);
        Assert.Empty(f.Db.ScheduledEvents);
        var row = Assert.Single((await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id)).Items);
        Assert.Equal(new DateOnly(2026, 9, 30), row.OperationalNdoh);
        Assert.Equal("DHC Cause List", row.OperationalNdohSource);
        Assert.Null(row.LastHearingDate);
        var second = await f.Sync.RunAsync(null, default);
        Assert.Equal(0, second.ObservationsCreated);
        Assert.Single(f.Db.CourtExternalListingObservations);
        Assert.Single(f.Db.Documents);
        Assert.Single(f.Db.CourtExternalListingDecisions);
    }

    [Fact]
    public async Task AmbiguousDisposedAndNonDhcCases_NeverAutoApply()
    {
        using var f = new Fixture();
        f.Case("W.P.(C) 7003/2026", "Delhi High Court", "Disposed");
        f.Case("W.P.(C) 7004/2026");
        f.Case("W.P.(C) 7004/2026");
        f.Case("W.P.(C) 7005/2026", "Supreme Court");
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "ambiguous.pdf",
            "1 W.P.(C)-7003/2026", "2 W.P.(C)-7004/2026", "3 W.P.(C)-7005/2026", "4 W.P.(C)-7006/2026");
        var run = await f.Sync.RunAsync(null, default);
        Assert.Equal(0, run.ObservationsAccepted);
        Assert.Equal(2, run.ReviewCount);
        Assert.Equal(2, await f.Db.CourtExternalListingObservations.CountAsync());
        Assert.All(f.Db.CourtExternalListingObservations, x => Assert.Equal(CourtExternalListingStatus.NeedsReview, x.Status));
        Assert.Empty(f.Db.CourtProceedings);
    }

    [Fact]
    public async Task SameDateConfirmsWithoutSecondBusinessState_PastAndUnsupportedSourcesDoNotApply()
    {
        using var f = new Fixture();
        var item = f.Case();
        f.Db.CourtProceedings.Add(new CourtProceeding { CourtCaseId = item.Id,
            ProceedingDate = new DateOnly(2026, 9, 28), NextDate = new DateOnly(2026, 9, 30) });
        await f.Db.SaveChangesAsync();
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "confirm.pdf", "1 W.P.(C)-7003/2026");
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 27.09.2026", "27-09-2026", "past.pdf", "1 W.P.(C)-7003/2026");
        f.Publish("Pronouncement of Judgment on 01.10.2026", "01-10-2026", "judgment.pdf", "1 W.P.(C)-7003/2026");
        var run = await f.Sync.RunAsync(null, default);
        Assert.Equal(1, run.ObservationsAccepted);
        Assert.Equal(2, run.SourceDocumentsDiscovered);
        Assert.Single(f.Db.CourtProceedings);
        Assert.Single(f.Db.CourtExternalListingObservations);
        Assert.Equal(new DateOnly(2026, 9, 30), Assert.Single((await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id)).Items).OperationalNdoh);
        Assert.Single(f.Db.Documents);
    }

    [Fact]
    public async Task RedirectOrSourceOutageCannotCreateAnObservation()
    {
        using var f = new Fixture();
        f.Case();
        f.Source.Redirect = true;
        var blocked = await f.Sync.RunAsync(null, default);
        Assert.Equal(CourtExternalSyncRunStatus.Failed, blocked.Status);
        Assert.Empty(f.Db.CourtExternalListingObservations);
        Assert.Empty(f.Db.CourtExternalSourceDocuments);
    }

    [Fact]
    public async Task ChangedHtmlLayoutFailsClosedAndPreservesAcceptedListing()
    {
        using var f = new Fixture();
        var item = f.Case();
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "before-layout-change.pdf",
            "1 W.P.(C)-7003/2026");
        Assert.Equal(CourtExternalSyncRunStatus.Completed, (await f.Sync.RunAsync(null, default)).Status);
        f.Source.Html = "<html><body>Layout unavailable</body></html>";
        var failed = await f.Sync.RunAsync(null, default);
        Assert.Equal(CourtExternalSyncRunStatus.Failed, failed.Status);
        Assert.Contains("publication rows", failed.FailureMessage);
        Assert.Equal("DHC Cause List", await f.OperationalDateSourceAsync(item.Id));
        Assert.Equal(CourtExternalListingStatus.Accepted, Assert.Single(f.Db.CourtExternalListingObservations).Status);
    }

    [Fact]
    public async Task NewProceedingWinsUntilNewerOfficialObservation_AndConflictStopsAutoDate()
    {
        using var f = new Fixture();
        var item = f.Case();
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "first.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        f.Clock.Now = f.Clock.Now.AddHours(1);
        f.Db.CourtProceedings.Add(new CourtProceeding { CourtCaseId = item.Id, ProceedingDate = new DateOnly(2026, 9, 28),
            NextDate = new DateOnly(2026, 10, 2), CreatedAt = f.Clock.Now });
        await f.Db.SaveChangesAsync();
        var afterOfficer = Assert.Single((await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id)).Items);
        Assert.Equal(new DateOnly(2026, 10, 2), afterOfficer.OperationalNdoh);
        Assert.Equal("Court proceeding", afterOfficer.OperationalNdohSource);
        f.Clock.Now = f.Clock.Now.AddHours(1);
        f.Publish("SUPPLEMENTARY CAUSE LIST OF SITTING OF BENCHES FOR 30.09.2026", "30-09-2026", "second.pdf", "1 W.P.(C)-7003/2026");
        var secondRun = await f.Sync.RunAsync(null, default);
        Assert.Equal(1, secondRun.ObservationsAccepted);
        var newestObservation = f.Db.CourtExternalListingObservations.OrderByDescending(x => x.ObservedAt).First();
        Assert.True(newestObservation.ObservedAt > f.Db.CourtProceedings.Single().CreatedAt);
        var resolved = await CourtOperationalNdohQuery.Resolve(f.Db.CourtCases, f.Db, f.Clock.GetCurrentDate()).SingleAsync();
        Assert.True(resolved.UsesExternalListing);
        var newListing = Assert.Single((await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id)).Items);
        Assert.Equal(new DateOnly(2026, 9, 30), newListing.OperationalNdoh);
        Assert.Equal("DHC Cause List", newListing.OperationalNdohSource);
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 01.10.2026", "01-10-2026", "conflict.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        Assert.Equal(3, f.Db.CourtExternalListingObservations.Count(x => x.Status == CourtExternalListingStatus.NeedsReview));
        var afterConflict = Assert.Single((await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id)).Items);
        Assert.Equal(new DateOnly(2026, 10, 2), afterConflict.OperationalNdoh);
    }

    [Fact]
    public async Task ThirdPublicationCannotBypassUnresolvedDifferentDate_ManualResolutionIsAudited()
    {
        using var f = new Fixture();
        var item = f.Case();
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "first-date.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        var first = Assert.Single(f.Db.CourtExternalListingObservations);
        Assert.Equal(CourtExternalListingStatus.Accepted, first.Status);

        f.Publish("SUPPLEMENTARY CAUSE LIST FOR 01.10.2026", "01-10-2026", "conflicting-date.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        var conflicting = f.Db.CourtExternalListingObservations.Single(x => x.ListingDate == new DateOnly(2026, 10, 1));
        Assert.Equal(CourtExternalListingStatus.NeedsReview, first.Status);
        Assert.Equal(CourtExternalListingStatus.NeedsReview, conflicting.Status);

        f.Publish("SUPPLEMENTARY CAUSE LIST FOR 30.09.2026", "30-09-2026", "third-date.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        var third = f.Db.CourtExternalListingObservations.Single(x => x.SourceDocument.SourceUrl.EndsWith("third-date.pdf"));
        Assert.Equal(CourtExternalListingStatus.NeedsReview, third.Status);
        Assert.Null((await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id)).Items.Single().OperationalNdoh);
        await Assert.ThrowsAsync<CourtWorkflowException>(() => f.Sync.ReviewAsync(third.Id,
            new(true, item.Id, new DateOnly(2026, 9, 30), "Checked source."), f.User.Id, default));

        await f.Sync.ReviewAsync(conflicting.Id, new(false, null, null, "Officer rejected conflicting source."), f.User.Id, default);
        await f.Sync.ReviewAsync(third.Id,
            new(true, item.Id, new DateOnly(2026, 9, 30), "Officer accepted verified date."), f.User.Id, default);
        Assert.Equal(CourtExternalListingStatus.Accepted, third.Status);
        Assert.Equal(new DateOnly(2026, 9, 30),
            Assert.Single((await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id)).Items).OperationalNdoh);
        Assert.Equal(6, f.Db.CourtExternalListingDecisions.Count());
        Assert.Equal(2, f.Db.CourtExternalListingDecisions.Count(x => x.ActorUserId == f.User.Id));
    }

    [Fact]
    public async Task CaseDetailOperationalDateSwitchesFromProceedingToDhcAndBack()
    {
        using var f = new Fixture();
        var item = f.Case();
        f.Db.CourtProceedings.Add(new CourtProceeding { CourtCaseId = item.Id,
            ProceedingDate = new DateOnly(2026, 9, 28), NextDate = new DateOnly(2026, 10, 2), CreatedAt = f.Clock.Now });
        await f.Db.SaveChangesAsync();
        // OfficialRecord creation uses the real save time; set historical test evidence
        // before observing the list so a later newly saved proceeding can win again.
        f.Db.CourtProceedings.Single().CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        await f.Db.SaveChangesAsync();
        f.Clock.Now = DateTimeOffset.UtcNow.AddMinutes(-1);
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "detail-list.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        var withDhc = await f.Projection.GetCourtCaseDetailAsync(item.Id, f.User.Id);
        Assert.NotNull(withDhc);
        Assert.Equal(new DateOnly(2026, 9, 30), withDhc.OperationalNdoh);
        Assert.Equal("DHC Cause List", withDhc.OperationalNdohSource);
        Assert.Equal(new DateOnly(2026, 10, 2), withDhc.AuthoritativeNextDate);

        f.Clock.Now = DateTimeOffset.UtcNow;
        f.Db.CourtProceedings.Add(new CourtProceeding { CourtCaseId = item.Id,
            ProceedingDate = new DateOnly(2026, 9, 29), NextDate = new DateOnly(2026, 10, 3), CreatedAt = f.Clock.Now });
        await f.Db.SaveChangesAsync();
        var withProceeding = await f.Projection.GetCourtCaseDetailAsync(item.Id, f.User.Id);
        Assert.NotNull(withProceeding);
        Assert.Equal(new DateOnly(2026, 10, 3), withProceeding.OperationalNdoh);
        Assert.Equal("Court proceeding", withProceeding.OperationalNdohSource);
    }

    [Fact]
    public async Task SamePdfUnderSecondUrl_ReusesPhysicalDocumentAndEquivalentObservation()
    {
        using var f = new Fixture();
        f.Case();
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "sha-a.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "sha-b.pdf", "1 W.P.(C)-7003/2026");
        f.Source.Pdfs["https://delhihighcourt.nic.in/files/2026-09/cause-list/sha-b.pdf"] =
            f.Source.Pdfs["https://delhihighcourt.nic.in/files/2026-09/cause-list/sha-a.pdf"];
        var second = await f.Sync.RunAsync(null, default);
        Assert.Equal(0, second.ObservationsCreated);
        Assert.Single(f.Db.Documents);
        Assert.Single(f.Storage.Files);
        Assert.Single(f.Db.CourtExternalListingObservations);
        Assert.Equal(2, f.Db.CourtExternalSourceDocuments.Count());
        Assert.Single(f.Db.CourtExternalSourceDocuments.Select(x => x.DocumentId).Distinct());
        var repeated = await f.Sync.RunAsync(null, default);
        Assert.Equal(0, repeated.ObservationsCreated);
        Assert.Single(f.Storage.Files);
    }

    [Theory]
    [InlineData("ADVANCE CAUSE LIST OF CASES FOR 01.10.2026", "01-10-2026")]
    [InlineData("Deletion Note for 30.09.2026", "30-09-2026")]
    public async Task SamePdfWithConflictingMetadata_IsReviewOnly(string title, string date)
    {
        using var f = new Fixture();
        var item = f.Case();
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "metadata-a.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        f.Publish(title, date, "metadata-b.pdf", "1 W.P.(C)-7003/2026");
        f.Source.Pdfs["https://delhihighcourt.nic.in/files/2026-09/cause-list/metadata-b.pdf"] =
            f.Source.Pdfs["https://delhihighcourt.nic.in/files/2026-09/cause-list/metadata-a.pdf"];
        var second = await f.Sync.RunAsync(null, default);
        Assert.Equal(0, second.ObservationsCreated);
        Assert.Equal(CourtExternalSourceStatus.NeedsReview,
            f.Db.CourtExternalSourceDocuments.Single(x => x.SourceUrl.EndsWith("metadata-b.pdf")).Status);
        Assert.Equal(CourtExternalListingStatus.NeedsReview, Assert.Single(f.Db.CourtExternalListingObservations).Status);
        Assert.Null((await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id)).Items.Single(x => x.Id == item.Id).OperationalNdoh);
        Assert.Single(f.Db.Documents);
        Assert.Single(f.Storage.Files);
    }

    [Fact]
    public async Task ListingDecisionHistoryCannotBeModifiedOrDeleted()
    {
        using var f = new Fixture();
        f.Case();
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "immutable.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        var decision = Assert.Single(f.Db.CourtExternalListingDecisions);
        decision.Reason = "overwrite";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        Assert.Throws<InvalidOperationException>(() => f.Db.SaveChanges());
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync(true, default));
        f.Db.Entry(decision).State = EntityState.Unchanged;
        f.Db.CourtExternalListingDecisions.Remove(decision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        Assert.Throws<InvalidOperationException>(() => f.Db.SaveChanges(true));
    }

    [Fact]
    public async Task IsolatedPostgres_SharedDhcDocumentIndex_WhenConfigured()
    {
        var connection = Environment.GetEnvironmentVariable("DHC_DEDUPE_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection).Options);
        await db.Database.MigrateAsync();
        var document = new LAC.Domain.Document { DocumentType = "CourtCauseList", OriginalFileName = "shared.pdf",
            StoragePath = "isolated-smoke/shared.pdf", Sha256Hash = new string('a', 64), MimeType = "application/pdf" };
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        var date = new DateOnly(2026, 9, 30);
        db.CourtExternalSourceDocuments.AddRange(
            new CourtExternalSourceDocument { SourceUrl = "https://delhihighcourt.nic.in/files/isolated-a.pdf",
                SourceTitle = "ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", Kind = CourtExternalSourceKind.OrdinaryListing,
                ListingDate = date, DocumentId = document.Id, Sha256Hash = document.Sha256Hash },
            new CourtExternalSourceDocument { SourceUrl = "https://delhihighcourt.nic.in/files/isolated-b.pdf",
                SourceTitle = "ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", Kind = CourtExternalSourceKind.OrdinaryListing,
                ListingDate = date, DocumentId = document.Id, Sha256Hash = document.Sha256Hash });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.CourtExternalSourceDocuments.CountAsync(x => x.DocumentId == document.Id));
        Assert.Equal(1, await db.Documents.CountAsync(x => x.Id == document.Id));
    }

    [Fact]
    public async Task DeletionSupersedesWithoutErasingHistory_AndOutagePreservesTruth()
    {
        using var f = new Fixture();
        var item = f.Case();
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "first.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        f.Source.Fail = true;
        var failed = await f.Sync.RunAsync(null, default);
        Assert.Equal(CourtExternalSyncRunStatus.Failed, failed.Status);
        Assert.Equal("DHC Cause List", await f.OperationalDateSourceAsync(item.Id));
        f.Source.Fail = false;
        f.Publish("Deletion Note for 30.09.2026", "30-09-2026", "deletion.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        Assert.Equal(CourtExternalListingStatus.Superseded,
            Assert.Single(f.Db.CourtExternalListingObservations.Where(x => x.SourceDocument.Kind == CourtExternalSourceKind.OrdinaryListing)).Status);
        Assert.Equal(CourtExternalListingStatus.NeedsReview,
            Assert.Single(f.Db.CourtExternalListingObservations.Where(x => x.SourceDocument.Kind == CourtExternalSourceKind.DeletionOrCorrigendum)).Status);
        Assert.Null((await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id)).Items.Single().OperationalNdoh);
        Assert.Equal(3, f.Db.CourtExternalListingDecisions.Count());
    }

    [Fact]
    public async Task DeletionBeforeOrdinaryList_BlocksLaterAutoAndManualAcceptance()
    {
        using var f = new Fixture();
        var item = f.Case();
        f.Publish("Deletion Note for 30.09.2026", "30-09-2026", "delete-first.pdf", "1 W.P.(C)-7003/2026");
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "ordinary-after.pdf", "1 W.P.(C)-7003/2026");
        var run = await f.Sync.RunAsync(null, default);
        Assert.Equal(0, f.Db.CourtExternalListingObservations.Count(x => x.Status == CourtExternalListingStatus.Accepted));
        Assert.Equal(2, run.ObservationsCreated);
        Assert.Null((await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id)).Items.Single().OperationalNdoh);

        f.Publish("SUPPLEMENTARY CAUSE LIST FOR 30.09.2026", "30-09-2026", "late-ordinary.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        var later = f.Db.CourtExternalListingObservations.Single(x => x.SourceDocument.SourceUrl.EndsWith("late-ordinary.pdf"));
        Assert.Equal(CourtExternalListingStatus.NeedsReview, later.Status);
        await Assert.ThrowsAsync<CourtWorkflowException>(() => f.Sync.ReviewAsync(later.Id,
            new(true, item.Id, new DateOnly(2026, 9, 30), "Checked case."), f.User.Id, default));
        Assert.Equal(0, f.Db.CourtExternalListingObservations.Count(x => x.Status == CourtExternalListingStatus.Accepted));
    }

    [Fact]
    public async Task ManualReviewIsAuditedAndAssignedScopeCannotTriggerGlobalSync()
    {
        using var f = new Fixture();
        var item = f.Case();
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 30.09.2026", "30-09-2026", "review.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        var observation = Assert.Single(f.Db.CourtExternalListingObservations);
        observation.Status = CourtExternalListingStatus.NeedsReview;
        await f.Db.SaveChangesAsync();
        await f.Sync.ReviewAsync(observation.Id, new(true, item.Id, new DateOnly(2026, 9, 30), "Officer checked exact source."), f.User.Id, default);
        Assert.Equal(2, f.Db.CourtExternalListingDecisions.Count());
        Assert.Equal(f.User.Id, f.Db.CourtExternalListingDecisions.Last().ActorUserId);
        f.Publish("SUPPLEMENTARY CAUSE LIST FOR 30.09.2026", "30-09-2026", "reject.pdf", "1 W.P.(C)-7003/2026");
        await f.Sync.RunAsync(null, default);
        var supplemental = f.Db.CourtExternalListingObservations.Single(x => x.SourceDocument.SourceUrl.EndsWith("reject.pdf"));
        supplemental.Status = CourtExternalListingStatus.NeedsReview;
        await f.Db.SaveChangesAsync();
        await f.Sync.ReviewAsync(supplemental.Id, new(false, null, null, "Conflicting supplementary listing."), f.User.Id, default);
        Assert.Equal(CourtExternalListingStatus.Rejected, supplemental.Status);
        Assert.Equal(f.User.Id, f.Db.CourtExternalListingDecisions.Last().ActorUserId);
        var assigned = new AppUser { Username = "assigned-dhc", NormalizedUsername = "ASSIGNED-DHC", DisplayName = "Assigned" };
        var role = new Role { Code = "ASSIGNED_DHC", Name = "Assigned DHC" };
        f.Db.AddRange(assigned, role);
        f.Db.UserRoles.Add(new UserRole { UserId = assigned.Id, RoleId = role.Id });
        foreach (var code in new[] { PermissionCodes.CourtView, PermissionCodes.CourtEdit })
        {
            var permission = f.Db.Permissions.Local.Single(x => x.Code == code);
            f.Db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = ScopeMode.Assigned });
        }
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<CourtWorkflowException>(() => f.Sync.RunAsync(assigned.Id, default));
        await Assert.ThrowsAsync<CourtWorkflowException>(() => f.Sync.RunHistoricalAsync(assigned.Id, default));
        await f.Gate.Semaphore.WaitAsync();
        try { await Assert.ThrowsAsync<CourtWorkflowException>(() => f.Sync.RunAsync(f.User.Id, default)); }
        finally { f.Gate.Semaphore.Release(); }
    }

    [Fact]
    public async Task IsolatedLiveOfficialSourceAndWorkbookSmoke_WhenExplicitlyConfigured()
    {
        var connection = Environment.GetEnvironmentVariable("DHC_LIVE_SMOKE_POSTGRES_CONNECTION");
        var workbook = Environment.GetEnvironmentVariable("DHC_LIVE_SMOKE_WORKBOOK");
        if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(workbook)) return;
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection).Options);
        await db.Database.MigrateAsync();
        var clock = new OfficeClock();
        var storage = new Storage();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new AppUser { Username = "dhc_smoke_" + suffix, NormalizedUsername = "DHC_SMOKE_" + suffix.ToUpperInvariant(), DisplayName = "DHC Smoke Officer" };
        var role = new Role { Code = "DHC_SMOKE_" + suffix, Name = "DHC Smoke" };
        db.AddRange(user, role);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        foreach (var code in new[] { PermissionCodes.CourtView, PermissionCodes.CourtCreate, PermissionCodes.CourtEdit })
        {
            var permission = await db.Permissions.SingleAsync(x => x.Code == code);
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = ScopeMode.All });
        }
        await db.SaveChangesAsync();
        var auth = new CourtAuthorizationService(db, null!, null!, null!);
        var imports = new CourtImportService(db, storage);
        await using var source = File.Open(workbook, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var batch = await imports.StageAsync(source, Path.GetFileName(workbook),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", user.Id);
        var review = new CourtImportReviewService(db, auth, new CourtWorkflowService(db, auth, storage));
        var approved = await review.ApproveSafeAsync(batch.Id, user.Id);
        var committed = await review.CommitAsync(batch.Id, user.Id);
        output.WriteLine($"Workbook read-only stage: {batch.TotalRows} rows; safe approved {approved.Ready}; committed {committed.CommittedThisRun}; failures {committed.Failures.Count}");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CourtSync:DelhiHighCourt:BaseUrl"] = DelhiHighCourtSyncService.OfficialPage,
            ["CourtSync:DelhiHighCourt:ForwardDays"] = "7"
        }).Build();
        var sync = new DelhiHighCourtSyncService(db, storage, clock, new LiveFactory(), config, auth, new DelhiHighCourtSyncGate());
        var first = await sync.RunAsync(null, default);
        output.WriteLine($"Public discovery: {first.Status}; discovered {first.SourceDocumentsDiscovered}; processed {first.SourceDocumentsProcessed}; accepted {first.ObservationsAccepted}; review {first.ReviewCount}; failure {first.FailureMessage}");
        var sources = await db.CourtExternalSourceDocuments.AsNoTracking().OrderBy(x => x.SourceTitle).ToListAsync();
        foreach (var entry in sources)
            output.WriteLine($"{entry.ListingDate}: {entry.SourceTitle} | {entry.Status} | SHA {entry.Sha256Hash ?? "none"} | {entry.FailureMessage}");
        var accepted = await db.CourtExternalListingObservations.AsNoTracking()
            .Where(x => x.Status == CourtExternalListingStatus.Accepted)
            .Join(db.CourtCases, x => x.CourtCaseId, c => c.Id, (x, c) => new { c.CaseNumber, x.ListingDate })
            .OrderBy(x => x.CaseNumber).ToListAsync();
        foreach (var item in accepted) output.WriteLine($"MATCH {item.CaseNumber} {item.ListingDate}");
        output.WriteLine($"Parseable identities {await db.CourtExternalListingObservations.CountAsync()}; unmatched/review {await db.CourtExternalListingObservations.CountAsync(x => x.Status == CourtExternalListingStatus.NeedsReview)}");
        var second = await sync.RunAsync(null, default);
        output.WriteLine($"Repeated sync: created {second.ObservationsCreated}; processed {second.SourceDocumentsProcessed}");
        Assert.Equal(0, second.ObservationsCreated);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, first.Status);
    }

    private static CourtProceeding Legacy(Fixture f, CourtCase item, DateOnly? date)
    {
        var proceeding = new CourtProceeding { CourtCaseId = item.Id,
            SourceKind = "LegacyRegisterNDOH", NextDate = date, ProceedingDate = null };
        f.Db.CourtProceedings.Add(proceeding);
        f.Db.SaveChanges();
        return proceeding;
    }

    private static void HistoricalPages(Fixture f, params (string Date, string File, string[] Lines)[] entries)
    {
        var rows = new List<string>();
        foreach (var (date, file, lines) in entries)
        {
            f.Source.Html = "";
            f.Publish($"CAUSE LIST OF SITTING OF BENCHES FOR {date}", date, file, lines);
            rows.Add(f.Source.Html);
        }
        f.Source.Html = rows[0];
        var root = DelhiHighCourtSyncService.OfficialArchive + "?title=2026";
        for (var i = 0; i < rows.Count; i++)
        {
            var next = i + 1 < rows.Count ? $"<a href='?title=2026&amp;page={i + 1}' rel='next'>Next</a>" : "";
            f.Source.Pages[i == 0 ? root : root + "&page=" + i] = rows[i] + next;
        }
    }

    [Fact]
    public async Task HistoricalBackfill_TargetedChronologicalAndOneTime()
    {
        using var f = new Fixture();
        f.Clock.Today = new DateOnly(2026, 9, 29);
        var item = f.Case();
        var legacy = Legacy(f, item, new DateOnly(2026, 3, 15));
        var july = f.Case("W.P.(C) 8004/2026");
        Legacy(f, july, new DateOnly(2026, 7, 1));
        var noBaseline = f.Case("W.P.(C) 9005/2026");
        var disposed = f.Case("W.P.(C) 9006/2026", status: "Disposed");
        Legacy(f, disposed, new DateOnly(2026, 3, 15));
        var otherCourt = f.Case("W.P.(C) 9007/2026", court: "Dwarka Court");
        Legacy(f, otherCourt, new DateOnly(2026, 3, 15));
        var real = f.Case("W.P.(C) 9008/2026");
        Legacy(f, real, new DateOnly(2026, 3, 15));
        f.Db.CourtProceedings.Add(new CourtProceeding { CourtCaseId = real.Id,
            ProceedingDate = new DateOnly(2026, 9, 20), NextDate = new DateOnly(2026, 10, 10) });
        await f.Db.SaveChangesAsync();
        HistoricalPages(f,
            ("05-09-2026", "sep.pdf", ["1 W.P.(C)-7003/2026", "2 W.P.(C)-8004/2026", "3 W.P.(C)-9999/2026"]),
            ("22-07-2026", "jul.pdf", ["1 W.P.(C)-7003/2026", "2 W.P.(C)-8004/2026"]),
            ("15-03-2026", "mar.pdf", ["1 W.P.(C)-7003/2026", "2 W.P.(C)-8004/2026"]),
            ("15-02-2026", "feb.pdf", ["1 W.P.(C)-7003/2026"]));
        var before = await f.Sync.HistoricalStatusAsync(f.User.Id, default);
        Assert.Equal(2, before.EligibleCaseCount);
        Assert.Equal(1, before.NoBaselineCount);
        Assert.Equal(1, before.RealProceedingExclusionCount);
        Assert.Equal(new DateOnly(2026, 3, 15), before.EarliestBaseline);
        var run = await f.Sync.RunHistoricalAsync(f.User.Id, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, run.Status);
        Assert.Equal(CourtExternalSyncMode.HistoricalBackfill, run.Mode);
        Assert.Equal(5, run.ArchivePagesDiscovered);
        Assert.Equal(3, run.SourceDocumentsProcessed);
        Assert.Equal(5, run.ObservationsAccepted);
        Assert.Equal(2, run.CasesAdvanced);
        Assert.Equal(3, f.Source.PdfRequests);
        Assert.Equal(5, f.Db.CourtExternalListingObservations.Count());
        Assert.DoesNotContain(f.Db.CourtExternalListingObservations, x => x.NormalizedCaseIdentity.Contains("9999"));
        Assert.Equal(new DateOnly(2026, 3, 15), legacy.NextDate);
        Assert.Equal(2, f.Db.CourtProceedings.Count(x => x.CourtCaseId == item.Id || x.CourtCaseId == july.Id));
        Assert.Empty(f.Db.CourtCaseEvents);
        var page = await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id);
        var row = page.Items.Single(x => x.Id == item.Id);
        Assert.Equal(new DateOnly(2026, 9, 5), row.OperationalNdoh);
        Assert.Equal("DHC historical cause list", row.OperationalNdohSource);
        Assert.Equal("Overdue", row.QueueState);
        Assert.Equal(new DateOnly(2026, 9, 5), page.Items.Single(x => x.Id == july.Id).OperationalNdoh);
        Assert.Equal(new DateOnly(2026, 10, 10), page.Items.Single(x => x.Id == real.Id).OperationalNdoh);
        Assert.False((await f.Sync.HistoricalStatusAsync(f.User.Id, default)).CanStart);
        await Assert.ThrowsAsync<CourtWorkflowException>(() => f.Sync.RunHistoricalAsync(f.User.Id, default));
        f.Clock.Now = legacy.CreatedAt.AddTicks(1);
        f.Publish("ADVANCE CAUSE LIST OF CASES FOR 03.10.2026", "03-10-2026", "future.pdf", "1 W.P.(C)-7003/2026");
        Assert.Equal(CourtExternalSyncRunStatus.Completed, (await f.Sync.RunAsync(null, default)).Status);
        row = (await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id)).Items.Single(x => x.Id == item.Id);
        Assert.Equal(new DateOnly(2026, 10, 3), row.OperationalNdoh);
        Assert.Equal("DHC Cause List", row.OperationalNdohSource);
        f.Clock.Now = f.Clock.Now.AddHours(1);
        f.Db.CourtProceedings.Add(new CourtProceeding { CourtCaseId = item.Id,
            ProceedingDate = new DateOnly(2026, 9, 29), NextDate = new DateOnly(2026, 10, 10),
            CreatedAt = f.Clock.Now });
        await f.Db.SaveChangesAsync();
        row = (await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id)).Items.Single(x => x.Id == item.Id);
        Assert.Equal(new DateOnly(2026, 10, 10), row.OperationalNdoh);
        Assert.Equal("Court proceeding", row.OperationalNdohSource);
    }

    [Fact]
    public async Task HistoricalDeletionFallsBackAndFailedRunRetriesWithoutDuplicateEvidence()
    {
        using var f = new Fixture();
        f.Clock.Today = new DateOnly(2026, 9, 29);
        var item = f.Case();
        Legacy(f, item, new DateOnly(2026, 3, 15));
        HistoricalPages(f,
            ("05-09-2026", "sep.pdf", ["1 W.P.(C)-7003/2026"]),
            ("22-07-2026", "jul.pdf", ["1 W.P.(C)-7003/2026"]),
            ("15-03-2026", "mar.pdf", ["1 W.P.(C)-7003/2026"]),
            ("15-02-2026", "feb.pdf", ["1 W.P.(C)-7003/2026"]));
        var current = f.Source.Html;
        f.Source.Html = "";
        f.Publish("Deletion Note for 05.09.2026", "05-09-2026", "deletion.pdf", "1 W.P.(C)-7003/2026");
        var deletionRow = f.Source.Html;
        f.Source.Html = current + deletionRow;
        var root = DelhiHighCourtSyncService.OfficialArchive + "?title=2026";
        f.Source.Pages[root] = current + deletionRow + "<a href='?title=2026&amp;page=1' rel='next'>Next</a>";
        f.Source.Fail = true;
        Assert.Equal(CourtExternalSyncRunStatus.Failed, (await f.Sync.RunHistoricalAsync(f.User.Id, default)).Status);
        f.Source.Fail = false;
        var run = await f.Sync.RunHistoricalAsync(f.User.Id, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, run.Status);
        Assert.Equal(2, f.Db.CourtExternalListingObservations.Count(x => x.Status == CourtExternalListingStatus.Accepted));
        var september = f.Db.CourtExternalListingObservations.Single(x => x.ListingDate == new DateOnly(2026, 9, 5) &&
            x.SourceDocument.Kind == CourtExternalSourceKind.OrdinaryListing);
        Assert.Equal(CourtExternalListingStatus.Superseded, september.Status);
        var row = (await f.Projection.GetCourtCasesAsync(new CourtCaseFilterQuery(), f.User.Id)).Items.Single(x => x.Id == item.Id);
        Assert.Equal(new DateOnly(2026, 7, 22), row.OperationalNdoh);
    }

    [Fact]
    public async Task InterruptedHistoricalRunCanResumeAndMarksOldAttemptFailed()
    {
        using var f = new Fixture();
        f.Clock.Today = new DateOnly(2026, 9, 29);
        f.Db.CourtExternalSyncRuns.Add(new CourtExternalSyncRun
        {
            Mode = CourtExternalSyncMode.HistoricalBackfill, StartedAt = f.Clock.Now.AddHours(-1),
            Status = CourtExternalSyncRunStatus.Running
        });
        await f.Db.SaveChangesAsync();
        Assert.True((await f.Sync.HistoricalStatusAsync(f.User.Id, default)).CanStart);
        var resumed = await f.Sync.RunHistoricalAsync(f.User.Id, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, resumed.Status);
        var old = f.Db.CourtExternalSyncRuns.Single(x => x.Id != resumed.Id);
        Assert.Equal(CourtExternalSyncRunStatus.Failed, old.Status);
        Assert.Contains("Interrupted", old.FailureMessage);
    }

    [Fact]
    public async Task HistoricalModeReusesLiveDocumentAndSameShaAcrossOfficialUrls()
    {
        using var f = new Fixture();
        f.Clock.Today = new DateOnly(2026, 9, 28);
        var item = f.Case();
        Legacy(f, item, new DateOnly(2026, 9, 25));
        f.Publish("CAUSE LIST OF SITTING OF BENCHES FOR 28.09.2026", "28-09-2026",
            "first.pdf", "1 W.P.(C)-7003/2026");
        Assert.Equal(CourtExternalSyncRunStatus.Completed, (await f.Sync.RunAsync(null, default)).Status);
        var firstUrl = f.Source.Pdfs.Keys.Single();
        var bytes = f.Source.Pdfs[firstUrl];
        var secondUrl = firstUrl.Replace("first.pdf", "second.pdf");
        f.Source.Pdfs[secondUrl] = bytes;
        var firstRow = f.Source.Html;
        var secondRow = firstRow.Replace("first.pdf", "second.pdf");
        f.Clock.Today = new DateOnly(2026, 9, 29);
        f.Source.Html = firstRow + secondRow;
        f.Source.Pages[DelhiHighCourtSyncService.OfficialArchive + "?title=2026"] =
            firstRow + secondRow;
        var run = await f.Sync.RunHistoricalAsync(f.User.Id, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, run.Status);
        Assert.Equal(1, f.Db.Documents.Count(x => x.DocumentType == "CourtCauseList"));
        Assert.Single(f.Storage.Files);
        Assert.Equal(2, f.Source.PdfRequests); // live first URL, historical second URL only
        Assert.Equal(1, f.Db.CourtExternalListingObservations.Count(x =>
            x.Mode == CourtExternalSyncMode.HistoricalBackfill));
        Assert.Equal(1, f.Db.CourtExternalListingDecisions.Count(x =>
            x.Observation.Mode == CourtExternalSyncMode.HistoricalBackfill));
        Assert.All(f.Db.CourtExternalSourceDocuments, x => Assert.Equal(
            f.Db.CourtExternalSourceDocuments.First().DocumentId, x.DocumentId));
    }

    [Fact]
    public async Task UnmatchedHistoricalPdfsRemainEphemeral_OnlyTwoOfFivePersist()
    {
        using var f = new Fixture();
        f.Clock.Today = new DateOnly(2026, 9, 29);
        var item = f.Case();
        Legacy(f, item, new DateOnly(2026, 3, 1));
        HistoricalPages(f,
            ("05-09-2026", "one.pdf", ["1 W.P.(C)-7003/2026", "2 W.P.(C)-9999/2026"]),
            ("20-08-2026", "unmatched-a.pdf", ["1 W.P.(C)-9999/2026"]),
            ("22-07-2026", "two.pdf", ["1 W.P.(C)-7003/2026"]),
            ("11-06-2026", "unmatched-b.pdf", ["1 W.P.(C)-8888/2026"]),
            ("15-03-2026", "unmatched-c.pdf", ["1 W.P.(C)-7777/2026"]));
        var run = await f.Sync.RunHistoricalAsync(f.User.Id, default);
        Assert.Equal(CourtExternalSyncRunStatus.Completed, run.Status);
        Assert.Equal(5, f.Source.PdfRequests);
        Assert.Equal(5, run.SourceDocumentsProcessed);
        Assert.Equal(5, f.Db.CourtExternalSourceDocuments.Count());
        Assert.Equal(2, f.Db.CourtExternalSourceDocuments.Count(x => x.DocumentId != null));
        Assert.Equal(2, f.Db.Documents.Count(x => x.DocumentType == "CourtCauseList"));
        Assert.Equal(2, f.Storage.Files.Count);
        Assert.Equal(2, f.Db.CourtExternalListingObservations.Count());
        Assert.Equal(2, f.Db.CourtExternalListingDecisions.Count());
    }

    [Fact]
    public async Task ManualLauncherReturnsBeforeJobFinishes_AndIgnoresRequestCancellation()
    {
        using var f = new Fixture();
        f.Clock.Today = new DateOnly(2026, 9, 29);
        var item = f.Case();
        Legacy(f, item, new DateOnly(2026, 9, 25));
        HistoricalPages(f, ("28-09-2026", "held.pdf", ["1 W.P.(C)-7003/2026"]));
        f.Source.HoldPdf = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LacDbContext>(o => o.UseInMemoryDatabase(f.DatabaseName, f.DatabaseRoot));
        services.AddSingleton<IDocumentStorage>(f.Storage);
        services.AddSingleton<IOfficeClock>(f.Clock);
        services.AddSingleton<IHttpClientFactory>(new Factory(f.Source));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(f.Gate);
        services.AddSingleton<IHostApplicationLifetime>(new TestLifetime());
        services.AddScoped<ICourtAuthorizationService>(sp =>
            new CourtAuthorizationService(sp.GetRequiredService<LacDbContext>(), null!, null!, null!));
        services.AddScoped<DelhiHighCourtSyncService>();
        services.AddSingleton<DelhiHighCourtHistoricalLauncher>();
        using var provider = services.BuildServiceProvider();
        var launcher = provider.GetRequiredService<DelhiHighCourtHistoricalLauncher>();
        using var request = new CancellationTokenSource();
        try
        {
            await launcher.StartAsync(f.User.Id, request.Token).WaitAsync(TimeSpan.FromSeconds(2));
            await f.Source.PdfStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            request.Cancel(); // The job must be using ApplicationStopping, not RequestAborted.
            using (var scope = provider.CreateScope())
            {
                var status = await scope.ServiceProvider.GetRequiredService<DelhiHighCourtSyncService>()
                    .HistoricalStatusAsync(f.User.Id, default);
                Assert.Equal(CourtExternalSyncRunStatus.Running, status.LastAttempt?.Status);
                Assert.False(status.CanStart);
            }
            await Assert.ThrowsAsync<CourtWorkflowException>(() => launcher.StartAsync(f.User.Id, default));
            f.Source.HoldPdf.SetResult(true);
            DhcHistoricalStatusDto? finished = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                using var scope = provider.CreateScope();
                finished = await scope.ServiceProvider.GetRequiredService<DelhiHighCourtSyncService>()
                    .HistoricalStatusAsync(f.User.Id, default);
                if (finished.LastAttempt?.Status != CourtExternalSyncRunStatus.Running) break;
                await Task.Delay(100);
            }
            Assert.Equal(CourtExternalSyncRunStatus.Completed, finished?.LastAttempt?.Status);
            Assert.False(finished!.CanStart);
            await Assert.ThrowsAsync<CourtWorkflowException>(() => launcher.StartAsync(f.User.Id, default));
        }
        finally { f.Source.HoldPdf.TrySetResult(true); }
    }

    [Fact]
    public void HistoricalArchiveChangedLayoutAndUnsafePaginationFailClosed()
    {
        var root = new Uri(DelhiHighCourtSyncService.OfficialArchive + "?title=2026");
        Assert.Throws<InvalidDataException>(() =>
            DelhiHighCourtCauseListParser.DiscoverArchivePage("<html>layout changed</html>", root));
        var row = "<tr><td headers='view-title-table-column'>CAUSE LIST OF SITTING OF BENCHES FOR 05.09.2026</td>" +
            "<td headers='view-field-date-table-column'>05-09-2026</td>" +
            "<td><a href='/files/2026-09/cause-list/sep.pdf'>Download</a></td></tr>";
        Assert.Throws<InvalidDataException>(() => DelhiHighCourtCauseListParser.DiscoverArchivePage(
            row + "<a href='https://evil.example/?page=1' rel='next'>Next</a>", root));
    }

    [Fact]
    public async Task IsolatedHistoricalWorkbookAudit_WhenExplicitlyConfigured()
    {
        var connection = Environment.GetEnvironmentVariable("DHC_HISTORICAL_SMOKE_POSTGRES_CONNECTION");
        var workbook = Environment.GetEnvironmentVariable("DHC_HISTORICAL_SMOKE_WORKBOOK");
        if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(workbook)) return;
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection).Options);
        await db.Database.MigrateAsync();
        var storage = new Storage();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new AppUser { Username = "dhc_historical_" + suffix,
            NormalizedUsername = "DHC_HISTORICAL_" + suffix.ToUpperInvariant(), DisplayName = "Historical Smoke Officer" };
        var role = new Role { Code = "DHC_HIST_" + suffix, Name = "DHC historical smoke" };
        db.AddRange(user, role);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        foreach (var code in new[] { PermissionCodes.CourtView, PermissionCodes.CourtCreate, PermissionCodes.CourtEdit })
        {
            var permission = await db.Permissions.SingleAsync(x => x.Code == code);
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = ScopeMode.All });
        }
        await db.SaveChangesAsync();
        var auth = new CourtAuthorizationService(db, null!, null!, null!);
        var imports = new CourtImportService(db, storage);
        await using var source = File.Open(workbook, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var batch = await imports.StageAsync(source, Path.GetFileName(workbook),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", user.Id);
        var review = new CourtImportReviewService(db, auth, new CourtWorkflowService(db, auth, storage));
        var approved = await review.ApproveSafeAsync(batch.Id, user.Id);
        var committed = await review.CommitAsync(batch.Id, user.Id);
        var sync = new DelhiHighCourtSyncService(db, storage, new OfficeClock(), new Factory(new SourceHandler()),
            new ConfigurationBuilder().Build(), auth, new DelhiHighCourtSyncGate());
        var audit = await sync.HistoricalStatusAsync(user.Id, default);
        output.WriteLine($"Workbook rows={batch.TotalRows}, safe approved={approved.Ready}, committed={committed.CommittedThisRun}, failures={committed.Failures.Count}");
        output.WriteLine($"Canonical={await db.CourtCases.CountAsync(x => x.RecordStatus == RecordStatus.Active)}, Pending DHC={await db.CourtCases.CountAsync(x => x.RecordStatus == RecordStatus.Active && x.CourtName == "Delhi High Court" && x.CurrentStatus != null && x.CurrentStatus.Trim().ToLower() == "pending")}");
        output.WriteLine($"Eligible stale legacy={audit.EligibleCaseCount}, no baseline={audit.NoBaselineCount}, real proceeding exclusions={audit.RealProceedingExclusionCount}, earliest baseline={audit.EarliestBaseline}");
        var assisted = await new DelhiHighCourtAssistedService(db, auth, new OfficeClock())
            .PreviewAsync(user.Id, default);
        output.WriteLine($"Assisted queue: recommended={assisted.RecommendedCount}, no NDOH={assisted.NoNdohCount}, overdue={assisted.OverdueCount}, cause-list review={assisted.ReviewCount}, identity needs review={assisted.SkippedIdentityCount}");
        var resolved = await CourtOperationalNdohQuery.Resolve(db.CourtCases.AsNoTracking(), db,
            new OfficeClock().GetCurrentDate())
            .Select(x => new { x.OperationalNdoh, x.UsesHistoricalListing }).ToListAsync();
        Assert.Equal(await db.CourtCases.CountAsync(), resolved.Count);
        Assert.Contains("20260928193923_AddDhcHistoricalBackfillMode", await db.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20260929060336_AddDhcAssistedSync", await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task BoundedPublicHistoricalSourceSmoke_WhenExplicitlyConfigured()
    {
        var connection = Environment.GetEnvironmentVariable("DHC_HISTORICAL_SMOKE_POSTGRES_CONNECTION");
        var enabled = Environment.GetEnvironmentVariable("DHC_HISTORICAL_LIVE_SMOKE");
        if (string.IsNullOrWhiteSpace(connection) || enabled != "1") return;
        using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection).Options);
        var today = new OfficeClock().GetCurrentDate();
        using var http = new LiveFactory().CreateClient("DelhiHighCourtCauseList");
        var pageUrls = Enumerable.Range(0, 8).Select(i => DelhiHighCourtSyncService.OfficialPage +
            (i == 0 ? "" : "?page=" + i));
        DhcPublication? chosen = null;
        foreach (var url in pageUrls)
        {
            var uri = new Uri(url);
            using var response = await http.GetAsync(uri);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = DelhiHighCourtCauseListParser.DiscoverArchivePage(await response.Content.ReadAsStringAsync(), uri);
            var candidate = page.Publications.Where(x => x.Kind == CourtExternalSourceKind.OrdinaryListing &&
                x.DateConflict == null && x.ListingDate >= today.AddDays(-7) && x.ListingDate < today)
                .OrderBy(x => x.Title.StartsWith("ADVANCE CAUSE LIST OF CASES FOR", StringComparison.OrdinalIgnoreCase) ? 0 :
                    x.Title.StartsWith("Cause List of Sitting of Benches", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .FirstOrDefault();
            chosen ??= candidate;
            if (candidate?.Title.StartsWith("ADVANCE CAUSE LIST OF CASES FOR", StringComparison.OrdinalIgnoreCase) == true)
            { chosen = candidate; break; }
            await Task.Delay(750);
        }
        Assert.NotNull(chosen);
        Assert.True(DelhiHighCourtCauseListParser.IsApprovedUri(chosen.PdfUrl));
        await Task.Delay(750);
        using var pdfResponse = await http.GetAsync(chosen.PdfUrl, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, pdfResponse.StatusCode);
        Assert.True(pdfResponse.Content.Headers.ContentLength is null or <= 30 * 1024 * 1024,
            $"{chosen.Title}: {pdfResponse.Content.Headers.ContentLength} bytes");
        var bytes = await pdfResponse.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length <= 30 * 1024 * 1024);
        Assert.True(bytes.AsSpan().StartsWith("%PDF"u8));
        IReadOnlyList<DhcCaseLine> lines;
        using (var pdf = new MemoryStream(bytes, writable: false))
            lines = DelhiHighCourtCauseListParser.ExtractCases(pdf);
        var storage = new Storage();
        using var upload = new MemoryStream(bytes, writable: false);
        var stored = await storage.SaveAndHashAsync(upload, Path.GetFileName(chosen.PdfUrl.LocalPath), default);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), stored.Sha256Hash);
        var doc = new LAC.Domain.Document
        {
            DocumentType = "CourtCauseList", OriginalFileName = Path.GetFileName(chosen.PdfUrl.LocalPath),
            StoragePath = stored.StoragePath, Sha256Hash = stored.Sha256Hash, FileSize = stored.FileSize,
            MimeType = "application/pdf", UploadedAt = DateTimeOffset.UtcNow,
            UploadedBy = DelhiHighCourtSyncService.Provider
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        var candidates = await db.CourtCases.AsNoTracking().Include(x => x.Proceedings)
            .Where(x => x.RecordStatus == RecordStatus.Active && x.CourtName == "Delhi High Court")
            .ToListAsync();
        var targets = candidates.Where(x => string.Equals(x.CurrentStatus?.Trim(), "Pending", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Proceedings.Where(p => p.RecordStatus == RecordStatus.Active)
                .OrderByDescending(p => p.ProceedingDate.HasValue).ThenByDescending(p => p.ProceedingDate)
                .ThenByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).FirstOrDefault() is
                { ProceedingDate: null, SourceKind: "LegacyRegisterNDOH", NextDate: not null } selected &&
                selected.NextDate < today)
            .Select(x => DelhiHighCourtCauseListParser.NormalizeIdentity(x.CaseNumber)).ToHashSet();
        var matches = lines.Count(x => targets.Contains(x.Identity));
        output.WriteLine($"Bounded public smoke: date={chosen.ListingDate}, source={chosen.Title}, PDF bytes={bytes.Length}, SHA={stored.Sha256Hash}, parsed identities={lines.Count}, exact stale-register target matches={matches}, stored Document={doc.Id}");
        Assert.True(lines.Count > 0);
    }
}
