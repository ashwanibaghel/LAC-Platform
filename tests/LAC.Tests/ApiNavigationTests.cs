using System.Net.Http.Json;
using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.IO.Compression;
using Xunit;

namespace LAC.Tests;

public sealed class ApiNavigationTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    private readonly ApiFactory _factory;

    public ApiNavigationTests(ApiFactory factory) { _factory = factory; _client = factory.CreateClient(); }

    [Fact]
    public async Task Unknown_api_route_is_not_served_as_the_spa()
    {
        using var response = await _client.GetAsync("/api/does-not-exist");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Document_intelligence_health_is_read_only_and_reports_configuration()
    {
        using var response = await _client.GetAsync("/api/health/document-intelligence");
        Assert.True(response.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("enabled", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pythonExists", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Matters_inherit_award_core_documents_without_creating_document_copies()
    {
        var village = await _client.GetFromJsonAsync<PageResponse<VillageListItem>>("/api/villages?page=0&pageSize=1");
        var villageId = Assert.Single(village!.Items).Id;
        var award = new Award { AwardNumber = "CORE-TEST" };
        var document = new Document { DocumentType = "NM", OriginalFileName = "nm.pdf", StoragePath = "nm.pdf" };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Add(award); db.Add(new AwardVillage { Award = award, VillageId = villageId });
            db.Add(document); db.Add(new DocumentAward { Award = award, Document = document, CoreDocumentRole = "NM" });
            await db.SaveChangesAsync();
        }
        async Task<Guid> Create(string title)
        {
            using var response = await _client.PostAsJsonAsync($"/api/villages/{villageId}/matters", new { title, matterType = "Court Case", status = "Open", awardId = award.Id });
            response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        }
        var first = await Create("Dharambir"); var second = await Create("Second matter");
        var firstDetail = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{first}");
        var secondDetail = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{second}");
        Assert.Equal(document.Id.ToString(), firstDetail.GetProperty("award").GetProperty("documents")[0].GetProperty("documentId").GetString());
        Assert.Equal(document.Id.ToString(), secondDetail.GetProperty("award").GetProperty("documents")[0].GetProperty("documentId").GetString());
        using var verify = _factory.Services.CreateScope(); Assert.Equal(1, await verify.ServiceProvider.GetRequiredService<LacDbContext>().Documents.CountAsync(x => x.Id == document.Id));
    }

    [Fact]
    public async Task Core_document_upload_links_the_single_document_to_every_award_village()
    {
        Guid awardId; Guid firstVillageId; Guid secondVillageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var first = await db.Villages.OrderBy(x => x.Name).FirstAsync();
            var second = new Village { Name = "Core document second village", SubDivisionId = first.SubDivisionId };
            var award = new Award { AwardNumber = "MULTI-VILLAGE-CORE" };
            db.Add(second); db.Add(award); db.AddRange(new AwardVillage { Award = award, VillageId = first.Id }, new AwardVillage { Award = award, Village = second });
            await db.SaveChangesAsync(); awardId = award.Id; firstVillageId = first.Id; secondVillageId = second.Id;
        }
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(new MemoryStream("%PDF-test"u8.ToArray())), "file", "source.pdf");
        using var response = await _client.PostAsync($"/api/awards/{awardId}/core-documents?role=NM", content);
        response.EnsureSuccessStatusCode();
        var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("documentId").GetGuid();
        using var verify = _factory.Services.CreateScope(); var check = verify.ServiceProvider.GetRequiredService<LacDbContext>();
        var links = await check.DocumentVillages.Where(x => x.DocumentId == created).Select(x => x.VillageId).ToListAsync();
        Assert.Equal(new[] { firstVillageId, secondVillageId }.Order(), links.Order());
        Assert.Equal(1, await check.Documents.CountAsync(x => x.Id == created));
        await verify.ServiceProvider.GetRequiredService<IDocumentStorage>().DeleteAsync((await check.Documents.SingleAsync(x => x.Id == created)).StoragePath, CancellationToken.None);
    }

    [Fact]
    public async Task Matter_can_link_existing_village_document_but_rejects_unrelated_document()
    {
        Guid matterId; Guid allowedId; Guid unrelatedId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var village = await db.Villages.FirstAsync();
            var matter = new Matter { VillageId = village.Id, Title = "Matter document test" }; var allowed = new Document { OriginalFileName = "allowed.pdf", StoragePath = "allowed.pdf" }; var unrelated = new Document { OriginalFileName = "other.pdf", StoragePath = "other.pdf" };
            db.AddRange(matter, allowed, unrelated); db.Add(new DocumentVillage { Document = allowed, VillageId = village.Id }); await db.SaveChangesAsync(); matterId = matter.Id; allowedId = allowed.Id; unrelatedId = unrelated.Id;
        }
        using var allowedResponse = await _client.PostAsJsonAsync($"/api/matters/{matterId}/documents/link", new { documentId = allowedId, role = "Application" });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, allowedResponse.StatusCode);
        using var rejectedResponse = await _client.PostAsJsonAsync($"/api/matters/{matterId}/documents/link", new { documentId = unrelatedId, role = "Other" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, rejectedResponse.StatusCode);
        var linked = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{matterId}/documents");
        Assert.Equal(allowedId, linked[0].GetProperty("documentId").GetGuid());
    }

    [Fact]
    public async Task Matter_upload_rejects_unsupported_files_before_storage()
    {
        Guid matterId; using (var scope = _factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var village = await db.Villages.FirstAsync(); var matter = new Matter { VillageId = village.Id, Title = "Upload validation" }; db.Add(matter); await db.SaveChangesAsync(); matterId = matter.Id; }
        using var content = new MultipartFormDataContent(); content.Add(new StreamContent(new MemoryStream([1, 2, 3])), "file", "unsafe.exe");
        using var response = await _client.PostAsync($"/api/matters/{matterId}/documents?role=Other", content);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        using var verify = _factory.Services.CreateScope(); Assert.Empty(await verify.ServiceProvider.GetRequiredService<LacDbContext>().MatterDocuments.Where(x => x.MatterId == matterId).ToListAsync());
    }

    [Fact]
    public async Task Matter_drafts_are_isolated_structured_and_revision_safe()
    {
        Guid villageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            villageId = (await db.Villages.FirstAsync()).Id;
        }
        async Task<Guid> CreateMatter(string title)
        {
            using var response = await _client.PostAsJsonAsync($"/api/villages/{villageId}/matters", new { title, matterType = "Court Case", status = "Open" });
            response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        }
        var firstMatter = await CreateMatter("Draft matter A"); var secondMatter = await CreateMatter("Draft matter B");
        using var invalidType = await _client.PostAsJsonAsync($"/api/matters/{firstMatter}/drafts", new { title = "Bad", draftType = "Memo" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalidType.StatusCode);
        using var created = await _client.PostAsJsonAsync($"/api/matters/{firstMatter}/drafts", new { title = "Reply to Court", draftType = "Letter" });
        created.EnsureSuccessStatusCode(); var draftId = (await created.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var firstList = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{firstMatter}/drafts"); var secondList = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{secondMatter}/drafts");
        Assert.Single(firstList.EnumerateArray()); Assert.Empty(secondList.EnumerateArray());
        const string content = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Reply to Court\",\"marks\":[{\"type\":\"bold\"}]}]}]}";
        var update = new { title = "Updated reply", contentJson = content, pageSize = "A4", orientation = "Landscape", marginTopMm = 18, marginRightMm = 19, marginBottomMm = 20, marginLeftMm = 21, expectedRevision = 0 };
        using var saved = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", update); saved.EnsureSuccessStatusCode();
        var savedBody = await saved.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(1, savedBody.GetProperty("revision").GetInt32());
        var reloaded = await _client.GetFromJsonAsync<JsonElement>($"/api/matter-drafts/{draftId}"); Assert.Equal(content, reloaded.GetProperty("contentJson").GetString()); Assert.Equal("Updated reply", reloaded.GetProperty("title").GetString());
        using var stale = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", update); Assert.Equal(System.Net.HttpStatusCode.Conflict, stale.StatusCode);
        using var unsafeContent = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new { title = "Unsafe", contentJson = "{\"type\":\"doc\",\"content\":[{\"type\":\"image\",\"attrs\":{\"src\":\"data:image/png;base64,x\"}}]}", pageSize = "A4", orientation = "Portrait", marginTopMm = 20, marginRightMm = 20, marginBottomMm = 20, marginLeftMm = 20, expectedRevision = 1 });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, unsafeContent.StatusCode);
        const string literalText = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Literal <script> and data:image text are not executable.\"}]}]}";
        using var ordinaryText = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new { title = "Plain text", contentJson = literalText, pageSize = "A4", orientation = "Portrait", marginTopMm = 20, marginRightMm = 20, marginBottomMm = 20, marginLeftMm = 20, expectedRevision = 1 });
        ordinaryText.EnsureSuccessStatusCode();
        using var unknownNode = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new { title = "Unknown", contentJson = "{\"type\":\"doc\",\"content\":[{\"type\":\"customWidget\"}]}", pageSize = "A4", orientation = "Portrait", marginTopMm = 20, marginRightMm = 20, marginBottomMm = 20, marginLeftMm = 20, expectedRevision = 2 });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, unknownNode.StatusCode);
        using var oversized = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new { title = "Too large", contentJson = new string('x', 1_000_001), pageSize = "A4", orientation = "Portrait", marginTopMm = 20, marginRightMm = 20, marginBottomMm = 20, marginLeftMm = 20, expectedRevision = 2 });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, oversized.StatusCode);
        using var legal = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new { title = "Legal letter", contentJson = literalText, pageSize = "Legal", orientation = "Landscape", marginTopMm = 15, marginRightMm = 16, marginBottomMm = 17, marginLeftMm = 18, expectedRevision = 2 });
        legal.EnsureSuccessStatusCode();
        const string pagedContent = "{\"type\":\"doc\",\"content\":[{\"type\":\"draftPage\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Paged editor content\"}]}]}]}";
        using var paged = await _client.PutAsJsonAsync($"/api/matter-drafts/{draftId}", new { title = "Paged letter", contentJson = pagedContent, pageSize = "Legal", orientation = "Landscape", marginTopMm = 15, marginRightMm = 16, marginBottomMm = 17, marginLeftMm = 18, expectedRevision = 3 });
        paged.EnsureSuccessStatusCode();
        using var notingCreated = await _client.PostAsJsonAsync($"/api/matters/{firstMatter}/drafts", new { title = "Office noting", draftType = "Noting" });
        notingCreated.EnsureSuccessStatusCode(); var notingId = (await notingCreated.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var noting = await _client.GetFromJsonAsync<JsonElement>($"/api/matter-drafts/{notingId}"); Assert.Equal(25m, noting.GetProperty("marginTopMm").GetDecimal()); Assert.Equal(0m, noting.GetProperty("marginLeftMm").GetDecimal());
        using var alteredNoting = await _client.PutAsJsonAsync($"/api/matter-drafts/{notingId}", new { title = "Office noting", contentJson = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\"}]}", pageSize = "Legal", orientation = "Landscape", marginTopMm = 1, marginRightMm = 1, marginBottomMm = 1, marginLeftMm = 1, expectedRevision = 0 });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, alteredNoting.StatusCode);
    }

    [Fact]
    public async Task Nm_owner_review_projects_summary_money_states_and_keeps_running_total_separate()
    {
        var document = new Document { DocumentType = "NM", OriginalFileName = "review.pdf", StoragePath = "review.pdf" };
        var village = new Village { Name = "Review village" };
        var nm = new NmDocument { Document = document, Village = village, Status = NmReviewStatus.NeedsReview };
        var session = new NmSemanticAnalysisSession { NmDocument = nm, ParserVersion = "test", SourcePagesJson = "[1]", Status = NmSemanticSessionStatus.Completed };
        var owner = new NmSemanticOwnerBlock { AnalysisSession = session, SourceSequence = 1, PageStart = 1, PageEnd = 1, RecordedNameRaw = "Review owner", ShareRaw = "1/3" };
        var group = new NmSemanticParcelGroup { OwnerBlock = owner, SourceSequence = 1, ParcelCountAsRecorded = "3", TotalAreaAsRecorded = "6-3" };
        var parcel = new NmSemanticParcelEntry { ParcelGroup = group, SourceSequence = 1, RawKhasraText = "26/21/3", RawAreaText = "6-3", LandClassRaw = "A", SourcePage = 1 };
        var land = new NmSemanticCompensationComponent { OwnerBlock = owner, ComponentType = "land_compensation", RawAmountText = "Rs590,229.17", Amount = 590229.17m, SourcePage = 1, SemanticState = "MappedByColumn" };
        var structure = new NmSemanticCompensationComponent { OwnerBlock = owner, ComponentType = "structure_compensation", RawAmountText = "Rs0.00", Amount = 0m, SourcePage = 1, SemanticState = "MappedByColumn" };
        var running = new NmSemanticCompensationComponent { OwnerBlock = owner, ComponentType = "running_total", RawAmountText = "Rs999.00", Amount = 999m, SourcePage = 1, SemanticState = "RunningTotal" };
        using (var scope = _factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); db.AddRange(session, owner, group, parcel, land, structure, running); await db.SaveChangesAsync(); }

        using var response = await _client.GetAsync($"/api/nm-semantic-sessions/{session.Id}/review-workspace");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var card = json.RootElement.GetProperty("owners")[0];
        Assert.Equal("3", card.GetProperty("summary").GetProperty("parcelCountAsRecorded").GetString());
        Assert.Equal("6-3", card.GetProperty("summary").GetProperty("totalAreaAsRecorded").GetString());
        var compensation = card.GetProperty("compensation");
        var projectedLand = compensation.EnumerateArray().Single(item => item.GetProperty("componentType").GetString() == "land_compensation");
        var projectedStructure = compensation.EnumerateArray().Single(item => item.GetProperty("componentType").GetString() == "structure_compensation");
        var projectedFinal = compensation.EnumerateArray().Single(item => item.GetProperty("componentType").GetString() == "grand_total");
        Assert.Equal("Rs590,229.17", projectedLand.GetProperty("rawAmountText").GetString()); Assert.Equal(590229.17m, projectedLand.GetProperty("amount").GetDecimal());
        Assert.Equal(0m, projectedStructure.GetProperty("amount").GetDecimal()); Assert.True(projectedStructure.GetProperty("isResolved").GetBoolean());
        Assert.Equal("NeedsReview", projectedFinal.GetProperty("semanticState").GetString()); Assert.Equal(JsonValueKind.Null, projectedFinal.GetProperty("amount").ValueKind);
        Assert.DoesNotContain(compensation.EnumerateArray(), item => item.GetProperty("componentType").GetString() == "running_total");
        Assert.Equal("Rs999.00", card.GetProperty("runningTotals")[0].GetProperty("rawAmountText").GetString());
    }

    [Fact]
    public async Task Village_khasra_award_and_back_to_khasra_use_canonical_detail_endpoints()
    {
        var districts = await _client.GetFromJsonAsync<List<DistrictListItem>>("/api/districts");
        var district = Assert.Single(districts!);
        var districtDetail = await _client.GetFromJsonAsync<DistrictDetail>($"/api/districts/{district.Id}");
        var subdivision = Assert.Single(districtDetail!.SubDivisions, item => item.Name == "Matiala");
        var subdivisionDetail = await _client.GetFromJsonAsync<SubDivisionDetail>($"/api/subdivisions/{subdivision.Id}?page=0&pageSize=25");
        var village = Assert.Single(subdivisionDetail!.Villages.Items, item => item.Name == "GALIB PUR");

        var khasras = await _client.GetFromJsonAsync<PageResponse<KhasraListItem>>($"/api/villages/{village.Id}/khasras?page=0&pageSize=25&q=22%2F%2F2");
        var khasra = Assert.Single(khasras!.Items);
        var khasraDetail = await _client.GetFromJsonAsync<KhasraDetail>($"/api/khasras/{khasra.Id}");
        var awardLink = Assert.Single(khasraDetail!.Awards);
        var awardDetail = await _client.GetFromJsonAsync<AwardDetail>($"/api/awards/{awardLink.Id}");

        Assert.Contains(awardDetail!.Khasras, item => item.Id == khasra.Id);
        Assert.Equal("22//2", khasraDetail.DisplayNumber);
    }

    [Fact]
    public async Task Khasra_search_includes_village_context_and_award_filter_is_paged()
    {
        var search = await _client.GetFromJsonAsync<List<SearchResultItem>>("/api/search?q=22%2F%2F2");
        var khasra = Assert.Single(search!, item => item.Type == "Khasra");
        Assert.Equal("GALIB PUR", khasra.Context);
        Assert.StartsWith("/khasras/", khasra.Route, StringComparison.Ordinal);

        var awards = await _client.GetFromJsonAsync<PageResponse<AwardListItem>>("/api/awards?page=0&pageSize=1&q=DEMO-AWARD");
        Assert.NotNull(awards);
        Assert.Equal(1, awards!.PageSize);
        Assert.Equal(1, awards.TotalCount);
        Assert.Single(awards.Items);
        Assert.Equal("DEMO-AWARD-01", awards.Items[0].AwardNumber);
    }

    [Fact]
    public async Task Lr_register_exposes_preserved_source_rows_progress_and_review_queue()
    {
        var villages = await _client.GetFromJsonAsync<PageResponse<VillageListItem>>("/api/villages?page=0&pageSize=100&q=GALIB");
        var village = Assert.Single(villages!.Items, item => item.Name == "GALIB PUR");
        var registers = await _client.GetFromJsonAsync<List<VillageLrListItem>>($"/api/villages/{village.Id}/lrs");
        var register = Assert.Single(registers!);

        var detail = await _client.GetFromJsonAsync<VillageLrDetail>($"/api/village-lrs/{register.Id}");
        var rows = await _client.GetFromJsonAsync<PageResponse<LrEntryDetailItem>>($"/api/village-lrs/{register.Id}/entries?page=0&pageSize=25");
        var progress = await _client.GetFromJsonAsync<LrProgress>($"/api/villages/{village.Id}/lr-progress");
        var review = await _client.GetFromJsonAsync<PageResponse<LrReviewItem>>("/api/lr-review?status=Verified&page=0&pageSize=25");

        Assert.Equal(1, detail!.TotalRows);
        Assert.Equal("22//2 min", Assert.Single(rows!.Items).RawKhasraText);
        Assert.Equal(1, progress!.TotalRows);
        Assert.Single(review!.Items);
    }

    [Fact]
    public async Task Enhanced_documents_endpoint_returns_context_and_filters_correctly()
    {
        Guid villageId;
        Guid awardId;
        Guid matterId;
        Guid docId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();

            var village = await db.Villages.OrderBy(x => x.Name).FirstAsync();
            villageId = village.Id;

            var award = new Award { AwardNumber = "DOC-CTX-AWARD" };
            db.Add(award);
            db.Add(new AwardVillage { Award = award, VillageId = villageId });

            var storagePath = await storage.SaveAsync(new MemoryStream("document content"u8.ToArray()), "acquisition_plan.pdf", CancellationToken.None);
            var document = new Document
            {
                OriginalFileName = "Acquisition_Plan_2026.pdf",
                DocumentType = "Award",
                StoragePath = storagePath,
                MimeType = "application/pdf",
                Status = "Active",
                Remarks = "Draft acquisition layout"
            };
            db.Add(document);
            db.Add(new DocumentVillage { Document = document, VillageId = villageId });
            db.Add(new DocumentAward { Document = document, Award = award, CoreDocumentRole = "Plan" });

            var matter = new Matter
            {
                VillageId = villageId,
                Title = "Acquisition Land Dispute",
                ReferenceNumber = "REF-2026-001",
                Status = "Open",
                MatterType = "Court Case"
            };
            db.Add(matter);
            db.Add(new MatterDocument
            {
                Matter = matter,
                Document = document,
                DocumentRole = "Evidence",
                DisplayName = "Key Acquisition Evidence"
            });

            await db.SaveChangesAsync();
            awardId = award.Id;
            matterId = matter.Id;
            docId = document.Id;
        }

        // Test GET /api/documents without filter
        var list = await _client.GetFromJsonAsync<PageResponse<DocumentDetailItem>>("/api/documents?page=0&pageSize=10");
        Assert.NotNull(list);
        var item = Assert.Single(list!.Items, d => d.Id == docId);
        Assert.Equal("Acquisition_Plan_2026.pdf", item.OriginalFileName);
        Assert.Equal("Award", item.DocumentType);
        Assert.Equal("Active", item.Status);

        var vCtx = Assert.Single(item.Villages);
        Assert.Equal(villageId, vCtx.Id);

        var aCtx = Assert.Single(item.Awards);
        Assert.Equal(awardId, aCtx.Id);
        Assert.Equal("DOC-CTX-AWARD", aCtx.AwardNumber);
        Assert.Equal("Plan", aCtx.CoreDocumentRole);

        var mCtx = Assert.Single(item.Matters);
        Assert.Equal(matterId, mCtx.Id);
        Assert.Equal("Acquisition Land Dispute", mCtx.Title);
        Assert.Equal("REF-2026-001", mCtx.ReferenceNumber);
        Assert.Equal("Evidence", mCtx.DocumentRole);
        Assert.Equal("Key Acquisition Evidence", mCtx.DisplayName);

        // Filter by q
        var filterQ = await _client.GetFromJsonAsync<PageResponse<DocumentDetailItem>>("/api/documents?q=Acquisition_Plan");
        Assert.Contains(filterQ!.Items, d => d.Id == docId);

        var filterQNone = await _client.GetFromJsonAsync<PageResponse<DocumentDetailItem>>("/api/documents?q=NonExistentFilename999");
        Assert.DoesNotContain(filterQNone!.Items, d => d.Id == docId);

        // Filter by documentType
        var filterType = await _client.GetFromJsonAsync<PageResponse<DocumentDetailItem>>("/api/documents?documentType=award");
        Assert.Contains(filterType!.Items, d => d.Id == docId);

        var filterTypeMismatch = await _client.GetFromJsonAsync<PageResponse<DocumentDetailItem>>("/api/documents?documentType=Khasra");
        Assert.DoesNotContain(filterTypeMismatch!.Items, d => d.Id == docId);

        // Filter by villageId
        var filterVillage = await _client.GetFromJsonAsync<PageResponse<DocumentDetailItem>>($"/api/documents?villageId={villageId}");
        Assert.Contains(filterVillage!.Items, d => d.Id == docId);

        var filterVillageMismatch = await _client.GetFromJsonAsync<PageResponse<DocumentDetailItem>>($"/api/documents?villageId={Guid.NewGuid()}");
        Assert.DoesNotContain(filterVillageMismatch!.Items, d => d.Id == docId);
    }

    [Fact]
    public async Task Search_returns_matters_and_documents_with_rich_context_and_routes()
    {
        Guid villageId;
        Guid matterId;
        Guid docIdWithMatter;
        Guid standaloneDocId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();

            var village = await db.Villages.OrderBy(x => x.Name).FirstAsync();
            villageId = village.Id;

            var matter = new Matter
            {
                VillageId = villageId,
                Title = "UniqueSearchMatterTitle",
                ReferenceNumber = "SEARCH-REF-777",
                Status = "Open",
                MatterType = "Court Case"
            };
            db.Add(matter);

            var path1 = await storage.SaveAsync(new MemoryStream("doc1"u8.ToArray()), "search_evidence.pdf", CancellationToken.None);
            var doc1 = new Document
            {
                OriginalFileName = "UniqueSearchEvidenceDoc.pdf",
                DocumentType = "Order",
                StoragePath = path1,
                MimeType = "application/pdf",
                Status = "Active"
            };
            db.Add(doc1);
            db.Add(new MatterDocument { Matter = matter, Document = doc1, DocumentRole = "Order" });

            var path2 = await storage.SaveAsync(new MemoryStream("doc2"u8.ToArray()), "unlinked_record.pdf", CancellationToken.None);
            var doc2 = new Document
            {
                OriginalFileName = "StandaloneSearchDoc.pdf",
                DocumentType = "Notice",
                StoragePath = path2,
                MimeType = "application/pdf",
                Status = "Active",
                Remarks = "StandaloneNoticeRemarks"
            };
            db.Add(doc2);

            await db.SaveChangesAsync();
            matterId = matter.Id;
            docIdWithMatter = doc1.Id;
            standaloneDocId = doc2.Id;
        }

        // Search for Matter by title
        var searchMatter = await _client.GetFromJsonAsync<List<SearchResultItem>>("/api/search?q=UniqueSearchMatter");
        Assert.NotNull(searchMatter);
        var mItem = Assert.Single(searchMatter!, item => item.Type == "Matter" && item.Id == matterId);
        Assert.Equal("UniqueSearchMatterTitle (SEARCH-REF-777)", mItem.Label);
        Assert.Equal($"/matters/{matterId}", mItem.Route);

        // Search for Matter by reference
        var searchMatterRef = await _client.GetFromJsonAsync<List<SearchResultItem>>("/api/search?q=SEARCH-REF-777");
        Assert.Contains(searchMatterRef!, item => item.Type == "Matter" && item.Id == matterId);

        // Search for Document by filename (linked to matter -> route goes to matter)
        var searchDoc1 = await _client.GetFromJsonAsync<List<SearchResultItem>>("/api/search?q=UniqueSearchEvidence");
        var d1Item = Assert.Single(searchDoc1!, item => item.Type == "Document" && item.Id == docIdWithMatter);
        Assert.Equal("UniqueSearchEvidenceDoc.pdf", d1Item.Label);
        Assert.Equal($"/matters/{matterId}", d1Item.Route);
        Assert.Contains("UniqueSearchMatterTitle", d1Item.Context);

        // Search for Document by remarks (unlinked -> route goes to /api/documents/{id}/content)
        var searchDoc2 = await _client.GetFromJsonAsync<List<SearchResultItem>>("/api/search?q=StandaloneNoticeRemarks");
        var d2Item = Assert.Single(searchDoc2!, item => item.Type == "Document" && item.Id == standaloneDocId);
        Assert.Equal("StandaloneSearchDoc.pdf", d2Item.Label);
        Assert.Equal($"/api/documents/{standaloneDocId}/content", d2Item.Route);

        // Confirm existing searches (Village) still work
        var searchVillage = await _client.GetFromJsonAsync<List<SearchResultItem>>("/api/search?q=GALIB");
        Assert.Contains(searchVillage!, item => item.Type == "Village");
    }

    [Fact]
    public async Task Document_content_download_flag_sets_attachment_disposition_and_preserves_inline()
    {
        Guid docId;
        string originalFileName = "Special_Document_Download_Test.pdf";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();

            var path = await storage.SaveAsync(new MemoryStream("%PDF-test-bytes"u8.ToArray()), "download_test.pdf", CancellationToken.None);
            var doc = new Document
            {
                OriginalFileName = originalFileName,
                DocumentType = "Award",
                StoragePath = path,
                MimeType = "application/pdf",
                Status = "Active"
            };
            db.Add(doc);
            await db.SaveChangesAsync();
            docId = doc.Id;
        }

        // Test with ?download=true -> Content-Disposition: attachment; filename="Special_Document_Download_Test.pdf"
        using var downloadResponse = await _client.GetAsync($"/api/documents/{docId}/content?download=true");
        downloadResponse.EnsureSuccessStatusCode();
        var disposition = downloadResponse.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition!.DispositionType);
        Assert.Equal(originalFileName, disposition.FileName?.Trim('"'));

        // Test with download absent -> inline streaming (enableRangeProcessing: true)
        using var inlineResponse = await _client.GetAsync($"/api/documents/{docId}/content");
        inlineResponse.EnsureSuccessStatusCode();
        var inlineDisposition = inlineResponse.Content.Headers.ContentDisposition;
        Assert.True(inlineDisposition == null || inlineDisposition.DispositionType != "attachment");
        Assert.Contains("bytes", inlineResponse.Headers.AcceptRanges);
    }

    [Fact]
    public async Task Award_core_upload_and_retrieval_supports_all_canonical_roles_and_allows_multiple_records_without_overwrite()
    {
        Guid awardId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var village = await db.Villages.FirstAsync();
            var award = new Award { AwardNumber = "CORE-ROLE-TEST" };
            db.Add(award);
            db.Add(new AwardVillage { Award = award, VillageId = village.Id });
            await db.SaveChangesAsync();
            awardId = award.Id;
        }

        // 1. Invalid file extension is rejected
        using (var badContent = new MultipartFormDataContent())
        {
            badContent.Add(new StreamContent(new MemoryStream([1, 2, 3])), "file", "malicious.exe");
            using var badResponse = await _client.PostAsync($"/api/awards/{awardId}/core-documents?role=Award", badContent);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, badResponse.StatusCode);
        }

        // 2. Upload one for each canonical role
        async Task<Guid> UploadCore(string role, string filename)
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StreamContent(new MemoryStream("%PDF-dummy-content"u8.ToArray())), "file", filename);
            using var response = await _client.PostAsync($"/api/awards/{awardId}/core-documents?role={role}", content);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("documentId").GetGuid();
        }

        var awardDocId = await UploadCore("Award", "Award_Signed.pdf");
        var nmDocId = await UploadCore("NM", "NM_Register.pdf");
        var stmtA1Id = await UploadCore("StatementA", "Statement_A_Part1.pdf");
        // Upload a second StatementA to prove multiple documents for a role are preserved without silent overwrite
        var stmtA2Id = await UploadCore("StatementA", "Statement_A_Part2.pdf");
        var possId = await UploadCore("PossessionProceeding", "Possession_Report.pdf");

        // 3. Retrieve core documents via GET /api/awards/{id}/core-documents
        var coreList = await _client.GetFromJsonAsync<JsonElement>($"/api/awards/{awardId}/core-documents");
        Assert.Equal(5, coreList.GetArrayLength());

        var items = coreList.EnumerateArray().ToList();
        Assert.Contains(items, x => x.GetProperty("documentId").GetGuid() == awardDocId && x.GetProperty("role").GetString() == "Award" && x.GetProperty("originalFileName").GetString() == "Award_Signed.pdf");
        Assert.Contains(items, x => x.GetProperty("documentId").GetGuid() == nmDocId && x.GetProperty("role").GetString() == "NM" && x.GetProperty("originalFileName").GetString() == "NM_Register.pdf");
        Assert.Contains(items, x => x.GetProperty("documentId").GetGuid() == stmtA1Id && x.GetProperty("role").GetString() == "StatementA" && x.GetProperty("originalFileName").GetString() == "Statement_A_Part1.pdf");
        Assert.Contains(items, x => x.GetProperty("documentId").GetGuid() == stmtA2Id && x.GetProperty("role").GetString() == "StatementA" && x.GetProperty("originalFileName").GetString() == "Statement_A_Part2.pdf");
        Assert.Contains(items, x => x.GetProperty("documentId").GetGuid() == possId && x.GetProperty("role").GetString() == "PossessionProceeding" && x.GetProperty("originalFileName").GetString() == "Possession_Report.pdf");
    }

    [Fact]
    public async Task Matter_upload_and_download_preserves_role_display_name_and_original_filename()
    {
        Guid matterId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var village = await db.Villages.FirstAsync();
            var matter = new Matter { VillageId = village.Id, Title = "Court Stay Matter" };
            db.Add(matter);
            await db.SaveChangesAsync();
            matterId = matter.Id;
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(new MemoryStream("%PDF-stay-order"u8.ToArray())), "file", "hc-stay-2024.pdf");
        using var response = await _client.PostAsync($"/api/matters/{matterId}/documents?role=Court%20Order&displayName=High%20Court%20Interim%20Stay%20Order", content);
        response.EnsureSuccessStatusCode();
        var uploadJson = await response.Content.ReadFromJsonAsync<JsonElement>();
        var docId = uploadJson.GetProperty("documentId").GetGuid();

        // Check Matter documents endpoint
        var matterDocs = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{matterId}/documents");
        var docItem = matterDocs.EnumerateArray().Single(x => x.GetProperty("documentId").GetGuid() == docId);
        Assert.Equal("Court Order", docItem.GetProperty("documentRole").GetString());
        Assert.Equal("High Court Interim Stay Order", docItem.GetProperty("displayName").GetString());
        Assert.Equal("hc-stay-2024.pdf", docItem.GetProperty("originalFileName").GetString());

        // Check download header
        using var dlResponse = await _client.GetAsync($"/api/documents/{docId}/content?download=true");
        dlResponse.EnsureSuccessStatusCode();
        Assert.Equal("attachment", dlResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("hc-stay-2024.pdf", dlResponse.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    [Fact]
    public async Task Matter_link_existing_reuses_document_and_creates_no_new_binary_or_document_row()
    {
        Guid matterId;
        Guid villageDocId;
        int initialDocCount;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var village = await db.Villages.FirstAsync();
            var matter = new Matter { VillageId = village.Id, Title = "Linking Reusability Matter" };
            var doc = new Document { OriginalFileName = "reusable_khatoni.pdf", StoragePath = "reusable_khatoni.pdf", DocumentType = "Khatoni" };
            db.Add(matter);
            db.Add(doc);
            db.Add(new DocumentVillage { Document = doc, VillageId = village.Id });
            await db.SaveChangesAsync();

            matterId = matter.Id;
            villageDocId = doc.Id;
            initialDocCount = await db.Documents.CountAsync();
        }

        // 1. Verify it is initially eligible
        var eligibleBefore = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{matterId}/eligible-documents");
        Assert.Contains(eligibleBefore.EnumerateArray(), x => x.GetProperty("id").GetGuid() == villageDocId && x.GetProperty("source").GetString() == "Village");

        // 2. Link existing document
        using var linkResponse = await _client.PostAsJsonAsync($"/api/matters/{matterId}/documents/link", new { documentId = villageDocId, role = "Khatoni", displayName = "Village Master Khatoni" });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, linkResponse.StatusCode);

        // 3. PROOF: Documents count has NOT increased; storage path has NOT changed
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var finalDocCount = await db.Documents.CountAsync();
            Assert.Equal(initialDocCount, finalDocCount); // No new Document row!

            var matterDoc = await db.MatterDocuments.SingleOrDefaultAsync(x => x.MatterId == matterId && x.DocumentId == villageDocId);
            Assert.NotNull(matterDoc);
            Assert.Equal("Khatoni", matterDoc.DocumentRole);
            Assert.Equal("Village Master Khatoni", matterDoc.DisplayName);
        }

        // 4. Verify it appears in matter documents
        var matterDocs = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{matterId}/documents");
        var linkedItem = matterDocs.EnumerateArray().Single(x => x.GetProperty("documentId").GetGuid() == villageDocId);
        Assert.Equal("reusable_khatoni.pdf", linkedItem.GetProperty("originalFileName").GetString());
        Assert.Equal("Village Master Khatoni", linkedItem.GetProperty("displayName").GetString());

        // 5. Verify it is no longer in eligible documents
        var eligibleAfter = await _client.GetFromJsonAsync<JsonElement>($"/api/matters/{matterId}/eligible-documents");
        Assert.DoesNotContain(eligibleAfter.EnumerateArray(), x => x.GetProperty("id").GetGuid() == villageDocId);
    }

    [Fact]
    public async Task Generic_document_export_validates_empty_nonexistent_and_inactive_documents()
    {
        // 1. Empty documentIds
        using var emptyResponse = await _client.PostAsJsonAsync("/api/documents/export", new { documentIds = Array.Empty<Guid>() });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, emptyResponse.StatusCode);

        // 2. Nonexistent ID
        using var nonExistentResponse = await _client.PostAsJsonAsync("/api/documents/export", new { documentIds = new[] { Guid.NewGuid() } });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, nonExistentResponse.StatusCode);

        // 3. Inactive/Archived document
        var archivedDoc = new Document { DocumentType = "Other", OriginalFileName = "archived.pdf", StoragePath = "archived.pdf", Status = "Archived" };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Add(archivedDoc);
            await db.SaveChangesAsync();
        }

        using var archivedResponse = await _client.PostAsJsonAsync("/api/documents/export", new { documentIds = new[] { archivedDoc.Id } });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, archivedResponse.StatusCode);
    }

    [Fact]
    public async Task Generic_document_export_streams_zip_with_byte_matching_and_deduplicated_names_and_ids()
    {
        var doc1Bytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 sample pdf content 1");
        var doc2Bytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 sample pdf content 2 - duplicate name");
        var doc3Bytes = System.Text.Encoding.UTF8.GetBytes("DOCX dummy zip binary content 3");
        var doc4Bytes = System.Text.Encoding.UTF8.GetBytes("PNG dummy image data 4");

        string path1, path2, path3, path4;
        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            path1 = await storage.SaveAsync(new MemoryStream(doc1Bytes), "report.pdf", CancellationToken.None);
            path2 = await storage.SaveAsync(new MemoryStream(doc2Bytes), "report.pdf", CancellationToken.None);
            path3 = await storage.SaveAsync(new MemoryStream(doc3Bytes), "order.docx", CancellationToken.None);
            path4 = await storage.SaveAsync(new MemoryStream(doc4Bytes), "survey.png", CancellationToken.None);
        }

        var doc1 = new Document { DocumentType = "Award", OriginalFileName = "report.pdf", StoragePath = path1, Status = "Active" };
        var doc2 = new Document { DocumentType = "NM", OriginalFileName = "report.pdf", StoragePath = path2, Status = "Active" };
        var doc3 = new Document { DocumentType = "Court Order", OriginalFileName = "order.docx", StoragePath = path3, Status = "Active" };
        var doc4 = new Document { DocumentType = "Demarcation", OriginalFileName = "survey.png", StoragePath = path4, Status = "Active" };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.AddRange(doc1, doc2, doc3, doc4);
            await db.SaveChangesAsync();
        }

        // Request with duplicate doc1 ID to verify deduplication
        var requestedIds = new[] { doc1.Id, doc2.Id, doc3.Id, doc4.Id, doc1.Id };
        using var response = await _client.PostAsJsonAsync("/api/documents/export", new { documentIds = requestedIds });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

        var disposition = response.Content.Headers.ContentDisposition?.FileName;
        Assert.NotNull(disposition);
        Assert.StartsWith("lac-documents-", disposition);
        Assert.EndsWith(".zip", disposition);

        var zipBytes = await response.Content.ReadAsByteArrayAsync();
        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        // 4 unique entries expected (doc1 deduplicated)
        Assert.Equal(4, archive.Entries.Count);

        var entryNames = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("report.pdf", entryNames);
        Assert.Contains("report (2).pdf", entryNames);
        Assert.Contains("order.docx", entryNames);
        Assert.Contains("survey.png", entryNames);

        // Verify byte contents
        var entry1 = archive.GetEntry("report.pdf")!;
        using (var s = entry1.Open())
        using (var ms = new MemoryStream())
        {
            await s.CopyToAsync(ms);
            Assert.Equal(doc1Bytes, ms.ToArray());
        }

        var entry2 = archive.GetEntry("report (2).pdf")!;
        using (var s = entry2.Open())
        using (var ms = new MemoryStream())
        {
            await s.CopyToAsync(ms);
            Assert.Equal(doc2Bytes, ms.ToArray());
        }

        var entry3 = archive.GetEntry("order.docx")!;
        using (var s = entry3.Open())
        using (var ms = new MemoryStream())
        {
            await s.CopyToAsync(ms);
            Assert.Equal(doc3Bytes, ms.ToArray());
        }

        var entry4 = archive.GetEntry("survey.png")!;
        using (var s = entry4.Open())
        using (var ms = new MemoryStream())
        {
            await s.CopyToAsync(ms);
            Assert.Equal(doc4Bytes, ms.ToArray());
        }

        // Verify no absolute paths or storage GUIDs in entry names
        foreach (var entry in archive.Entries)
        {
            Assert.DoesNotContain("/", entry.FullName);
            Assert.DoesNotContain("\\", entry.FullName);
            Assert.DoesNotContain(":", entry.FullName);
        }
    }

    [Fact]
    public async Task Generic_document_export_cleans_up_and_returns_controlled_error_when_storage_file_missing()
    {
        var doc = new Document { DocumentType = "Award", OriginalFileName = "missing.pdf", StoragePath = $"nonexistent-{Guid.NewGuid():N}.pdf", Status = "Active" };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Add(doc);
            await db.SaveChangesAsync();
        }

        using var response = await _client.PostAsJsonAsync("/api/documents/export", new { documentIds = new[] { doc.Id } });
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("unavailable", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Matter_export_preserves_scope_authorization_and_exports_inherited_plus_matter_documents()
    {
        var village = await _client.GetFromJsonAsync<PageResponse<VillageListItem>>("/api/villages?page=0&pageSize=1");
        var villageId = Assert.Single(village!.Items).Id;
        var award = new Award { AwardNumber = $"MATTER-EXPORT-{Guid.NewGuid():N}" };

        var coreBytes = System.Text.Encoding.UTF8.GetBytes("CORE_AWARD_DOC_DATA");
        var matterBytes = System.Text.Encoding.UTF8.GetBytes("MATTER_SPECIFIC_DOC_DATA");
        var unrelatedBytes = System.Text.Encoding.UTF8.GetBytes("UNRELATED_DOC_DATA");

        string corePath, matterPath, unrelatedPath;
        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            corePath = await storage.SaveAsync(new MemoryStream(coreBytes), "core-award.pdf", CancellationToken.None);
            matterPath = await storage.SaveAsync(new MemoryStream(matterBytes), "matter-app.pdf", CancellationToken.None);
            unrelatedPath = await storage.SaveAsync(new MemoryStream(unrelatedBytes), "unrelated.pdf", CancellationToken.None);
        }

        var coreDoc = new Document { DocumentType = "Award", OriginalFileName = "core-award.pdf", StoragePath = corePath, Status = "Active" };
        var matterDoc = new Document { DocumentType = "Application", OriginalFileName = "matter-app.pdf", StoragePath = matterPath, Status = "Active" };
        var unrelatedDoc = new Document { DocumentType = "Other", OriginalFileName = "unrelated.pdf", StoragePath = unrelatedPath, Status = "Active" };

        var matter = new Matter { Title = "Export Test Matter", MatterType = "Court Case", Status = "Open", VillageId = villageId };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Add(award);
            db.Add(new AwardVillage { Award = award, VillageId = villageId });
            db.Add(coreDoc);
            db.Add(new DocumentAward { Award = award, Document = coreDoc, CoreDocumentRole = "Award" });

            db.Add(matter);
            db.Add(new MatterAward { Matter = matter, Award = award, IsPrimary = true });
            db.Add(matterDoc);
            db.Add(new MatterDocument { Matter = matter, Document = matterDoc, DocumentRole = "Application" });

            db.Add(unrelatedDoc);
            await db.SaveChangesAsync();
        }

        // 1. Unrelated document must be rejected by matter export validation
        using var badResponse = await _client.PostAsJsonAsync($"/api/matters/{matter.Id}/export", new { documentIds = new[] { coreDoc.Id, unrelatedDoc.Id } });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, badResponse.StatusCode);

        // 2. Exporting inherited core doc + matter doc together works
        using var goodResponse = await _client.PostAsJsonAsync($"/api/matters/{matter.Id}/export", new { documentIds = new[] { coreDoc.Id, matterDoc.Id } });
        Assert.Equal(System.Net.HttpStatusCode.OK, goodResponse.StatusCode);
        Assert.Equal("application/zip", goodResponse.Content.Headers.ContentType?.MediaType);

        var zipBytes = await goodResponse.Content.ReadAsByteArrayAsync();
        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count);

        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("core-award.pdf", names);
        Assert.Contains("matter-app.pdf", names);

        var coreEntry = archive.GetEntry("core-award.pdf")!;
        using (var s = coreEntry.Open())
        using (var ms = new MemoryStream())
        {
            await s.CopyToAsync(ms);
            Assert.Equal(coreBytes, ms.ToArray());
        }

        var matterEntry = archive.GetEntry("matter-app.pdf")!;
        using (var s = matterEntry.Open())
        using (var ms = new MemoryStream())
        {
            await s.CopyToAsync(ms);
            Assert.Equal(matterBytes, ms.ToArray());
        }
    }

    [Fact]
    public async Task Matter_unlink_removes_only_MatterDocument_and_preserves_canonical_document_and_binary_and_other_matter_links()
    {
        var village = await _client.GetFromJsonAsync<PageResponse<VillageListItem>>("/api/villages?page=0&pageSize=1");
        var villageId = Assert.Single(village!.Items).Id;

        var bytes = System.Text.Encoding.UTF8.GetBytes("UNLINK_TEST_FILE_CONTENT");
        string storagePath;
        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            storagePath = await storage.SaveAsync(new MemoryStream(bytes), "unlink-test.pdf", CancellationToken.None);
        }

        var doc = new Document
        {
            OriginalFileName = "unlink-test.pdf",
            DocumentType = "Matter",
            StoragePath = storagePath,
            FileSize = bytes.Length,
            Sha256Hash = "dummyhash123",
            Status = "Active"
        };

        var matter1 = new Matter { Title = "Matter 1 for Unlink", VillageId = villageId, Status = "Open", MatterType = "Court Case" };
        var matter2 = new Matter { Title = "Matter 2 for Unlink", VillageId = villageId, Status = "Open", MatterType = "Court Case" };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Add(doc);
            db.Add(new DocumentVillage { Document = doc, VillageId = villageId });
            db.Add(matter1);
            db.Add(matter2);
            db.Add(new MatterDocument { Matter = matter1, Document = doc, DocumentRole = "Petition" });
            db.Add(new MatterDocument { Matter = matter2, Document = doc, DocumentRole = "Evidence" });
            await db.SaveChangesAsync();
        }

        // Verify initial links
        var m1DocsBefore = await _client.GetFromJsonAsync<List<MatterDocumentDto>>($"/api/matters/{matter1.Id}/documents");
        Assert.Contains(m1DocsBefore!, d => d.DocumentId == doc.Id);
        var m2DocsBefore = await _client.GetFromJsonAsync<List<MatterDocumentDto>>($"/api/matters/{matter2.Id}/documents");
        Assert.Contains(m2DocsBefore!, d => d.DocumentId == doc.Id);

        // Execute Unlink on matter1
        using var unlinkResponse = await _client.DeleteAsync($"/api/matters/{matter1.Id}/documents/{doc.Id}/link");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, unlinkResponse.StatusCode);

        // Verification invariants:
        // 1. Disappears from matter1
        var m1DocsAfter = await _client.GetFromJsonAsync<List<MatterDocumentDto>>($"/api/matters/{matter1.Id}/documents");
        Assert.DoesNotContain(m1DocsAfter!, d => d.DocumentId == doc.Id);

        // 2. Still remains in matter2
        var m2DocsAfter = await _client.GetFromJsonAsync<List<MatterDocumentDto>>($"/api/matters/{matter2.Id}/documents");
        Assert.Contains(m2DocsAfter!, d => d.DocumentId == doc.Id);

        // 3. Canonical Document row in DB untouched
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();

            var inDb = await db.Documents.SingleOrDefaultAsync(x => x.Id == doc.Id);
            Assert.NotNull(inDb);
            Assert.Equal("Active", inDb!.Status);
            Assert.Equal(storagePath, inDb.StoragePath);
            Assert.Equal("unlink-test.pdf", inDb.OriginalFileName);
            Assert.Equal(bytes.Length, inDb.FileSize);

            // DocumentVillage still exists
            Assert.True(await db.DocumentVillages.AnyAsync(dv => dv.DocumentId == doc.Id && dv.VillageId == villageId));

            // Binary on storage still readable and intact
            await using var stream = await storage.OpenReadAsync(inDb.StoragePath, CancellationToken.None);
            Assert.NotNull(stream);
            using var ms = new MemoryStream();
            await stream!.CopyToAsync(ms);
            Assert.Equal(bytes, ms.ToArray());
        }

        // 4. Repeated unlink returns 404
        using var repeatResponse = await _client.DeleteAsync($"/api/matters/{matter1.Id}/documents/{doc.Id}/link");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, repeatResponse.StatusCode);
    }

    [Fact]
    public async Task Document_archive_and_restore_cycle_preserves_metadata_and_binary_and_relations()
    {
        var village = await _client.GetFromJsonAsync<PageResponse<VillageListItem>>("/api/villages?page=0&pageSize=1");
        var villageId = Assert.Single(village!.Items).Id;
        var award = new Award { AwardNumber = $"AWARD-ARCH-{Guid.NewGuid():N}" };

        var bytes = System.Text.Encoding.UTF8.GetBytes("ARCHIVE_RESTORE_CYCLE_DATA");
        string storagePath;
        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            storagePath = await storage.SaveAsync(new MemoryStream(bytes), "cycle.pdf", CancellationToken.None);
        }

        var uploadedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var doc = new Document
        {
            OriginalFileName = "cycle.pdf",
            DocumentType = "Award",
            StoragePath = storagePath,
            Sha256Hash = "cycle_sha256_hash",
            FileSize = bytes.Length,
            UploadedAt = uploadedAt,
            Status = "Active"
        };
        var matter = new Matter { Title = "Cycle Matter", VillageId = villageId, Status = "Open", MatterType = "Court Case" };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Add(award);
            db.Add(new AwardVillage { Award = award, VillageId = villageId });
            db.Add(doc);
            db.Add(new DocumentVillage { Document = doc, VillageId = villageId });
            db.Add(new DocumentAward { Award = award, Document = doc, CoreDocumentRole = "Award" });
            db.Add(matter);
            db.Add(new MatterDocument { Matter = matter, Document = doc, DocumentRole = "Award" });
            await db.SaveChangesAsync();
        }

        // 1. Archive
        using var archiveRes = await _client.PostAsync($"/api/documents/{doc.Id}/archive", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, archiveRes.StatusCode);

        // Repeated archive is idempotent
        using var repeatArchiveRes = await _client.PostAsync($"/api/documents/{doc.Id}/archive", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, repeatArchiveRes.StatusCode);

        // Verify state while Archived
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();

            var inDb = await db.Documents.SingleAsync(x => x.Id == doc.Id);
            Assert.Equal("Archived", inDb.Status);
            Assert.Equal(RecordStatus.Archived, inDb.RecordStatus);
            Assert.Equal(storagePath, inDb.StoragePath);
            Assert.Equal("cycle_sha256_hash", inDb.Sha256Hash);
            Assert.Equal(bytes.Length, inDb.FileSize);
            Assert.Equal(uploadedAt, inDb.UploadedAt);

            // Relationships preserved
            Assert.True(await db.DocumentVillages.AnyAsync(dv => dv.DocumentId == doc.Id));
            Assert.True(await db.DocumentAwards.AnyAsync(da => da.DocumentId == doc.Id));
            Assert.True(await db.MatterDocuments.AnyAsync(md => md.DocumentId == doc.Id));

            // Binary physically preserved
            await using var stream = await storage.OpenReadAsync(inDb.StoragePath, CancellationToken.None);
            Assert.NotNull(stream);
        }

        // 2. Restore
        using var restoreRes = await _client.PostAsync($"/api/documents/{doc.Id}/restore", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, restoreRes.StatusCode);

        // Repeated restore is idempotent
        using var repeatRestoreRes = await _client.PostAsync($"/api/documents/{doc.Id}/restore", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, repeatRestoreRes.StatusCode);

        // Verify state after Restore
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();

            var inDb = await db.Documents.SingleAsync(x => x.Id == doc.Id);
            Assert.Equal("Active", inDb.Status);
            Assert.Equal(RecordStatus.Active, inDb.RecordStatus);
            Assert.Equal(doc.Id, inDb.Id);
            Assert.Equal(storagePath, inDb.StoragePath);

            await using var stream = await storage.OpenReadAsync(inDb.StoragePath, CancellationToken.None);
            Assert.NotNull(stream);
            using var ms = new MemoryStream();
            await stream!.CopyToAsync(ms);
            Assert.Equal(bytes, ms.ToArray());
        }
    }

    [Fact]
    public async Task Archive_returns_409_conflict_when_active_workflow_session_in_progress()
    {
        var docExtraction = new Document { OriginalFileName = "extract.pdf", StoragePath = "p1", Status = "Active" };
        var docNm = new Document { OriginalFileName = "nm.pdf", StoragePath = "p2", Status = "Active" };
        var docIngestion = new Document { OriginalFileName = "ingest.pdf", StoragePath = "p3", Status = "Active" };

        var village = await _client.GetFromJsonAsync<PageResponse<VillageListItem>>("/api/villages?page=0&pageSize=1");
        var villageId = Assert.Single(village!.Items).Id;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Add(docExtraction);
            db.Add(docNm);
            db.Add(docIngestion);

            // Active extraction job
            db.Add(new AwardDocumentExtractionJob
            {
                Document = docExtraction,
                Status = AwardDocumentExtractionJobStatus.Extracting
            });

            // Active NM document
            db.Add(new NmDocument
            {
                Document = docNm,
                VillageId = villageId,
                Status = NmReviewStatus.NeedsReview
            });

            // Active Ingestion Session
            db.Add(new AwardIngestionSession
            {
                SourceDocument = docIngestion,
                Status = AwardIngestionSessionStatus.NeedsReview
            });

            await db.SaveChangesAsync();
        }

        // Case A: Extraction conflict
        using var resA = await _client.PostAsync($"/api/documents/{docExtraction.Id}/archive", null);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, resA.StatusCode);
        var bodyA = await resA.Content.ReadAsStringAsync();
        Assert.Contains("extraction", bodyA, StringComparison.OrdinalIgnoreCase);

        // Case B: NM review conflict
        using var resB = await _client.PostAsync($"/api/documents/{docNm.Id}/archive", null);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, resB.StatusCode);
        var bodyB = await resB.Content.ReadAsStringAsync();
        Assert.Contains("Naksha Muntazmin", bodyB, StringComparison.OrdinalIgnoreCase);

        // Case C: Ingestion review conflict
        using var resC = await _client.PostAsync($"/api/documents/{docIngestion.Id}/archive", null);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, resC.StatusCode);
        var bodyC = await resC.Content.ReadAsStringAsync();
        Assert.Contains("award ingestion", bodyC, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Active_query_exclusions_filter_out_archived_documents_across_operational_surfaces()
    {
        var village = await _client.GetFromJsonAsync<PageResponse<VillageListItem>>("/api/villages?page=0&pageSize=1");
        var villageId = Assert.Single(village!.Items).Id;
        var award = new Award { AwardNumber = $"EXCLUSION-AWARD-{Guid.NewGuid():N}" };

        var bytes = System.Text.Encoding.UTF8.GetBytes("EXCLUSION_TEST_CONTENT");
        string pathCore, pathMatter;
        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            pathCore = await storage.SaveAsync(new MemoryStream(bytes), "excl-core.pdf", CancellationToken.None);
            pathMatter = await storage.SaveAsync(new MemoryStream(bytes), "excl-matter.pdf", CancellationToken.None);
        }

        var uniquePrefix = $"ExclSearch{Guid.NewGuid():N}".Substring(0, 16);
        var coreDoc = new Document
        {
            OriginalFileName = $"{uniquePrefix}-Core.pdf",
            DocumentType = "Award",
            StoragePath = pathCore,
            Status = "Active"
        };
        var matterDoc = new Document
        {
            OriginalFileName = $"{uniquePrefix}-Matter.pdf",
            DocumentType = "Order",
            StoragePath = pathMatter,
            Status = "Active"
        };
        var matter = new Matter { Title = $"{uniquePrefix}-MatterTitle", VillageId = villageId, Status = "Open", MatterType = "Court Case" };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            db.Add(award);
            db.Add(new AwardVillage { Award = award, VillageId = villageId });
            db.Add(coreDoc);
            db.Add(new DocumentAward { Award = award, Document = coreDoc, CoreDocumentRole = "Award" });
            db.Add(new DocumentVillage { Document = coreDoc, VillageId = villageId });

            db.Add(matter);
            db.Add(new MatterAward { Matter = matter, Award = award, IsPrimary = true });
            db.Add(matterDoc);
            db.Add(new MatterDocument { Matter = matter, Document = matterDoc, DocumentRole = "Order" });
            db.Add(new DocumentVillage { Document = matterDoc, VillageId = villageId });

            await db.SaveChangesAsync();
        }

        // Initially: all active surfaces include them
        var awardCoreDocs = await _client.GetFromJsonAsync<List<CoreDocumentItem>>($"/api/awards/{award.Id}/core-documents");
        Assert.Contains(awardCoreDocs!, d => d.DocumentId == coreDoc.Id);

        var villageCore = await _client.GetFromJsonAsync<List<VillageAwardCoreItem>>($"/api/villages/{villageId}/core-records");
        var awardInVillage = Assert.Single(villageCore!, a => a.Id == award.Id);
        Assert.Contains(awardInVillage.Documents, d => d.DocumentId == coreDoc.Id);

        var matterDetail = await _client.GetFromJsonAsync<MatterDetailItem>($"/api/matters/{matter.Id}");
        Assert.NotNull(matterDetail?.Award?.Documents);
        Assert.Contains(matterDetail!.Award!.Documents, d => d.DocumentId == coreDoc.Id);

        var matterDocs = await _client.GetFromJsonAsync<List<MatterDocumentDto>>($"/api/matters/{matter.Id}/documents");
        Assert.Contains(matterDocs!, d => d.DocumentId == matterDoc.Id);

        var searchBefore = await _client.GetFromJsonAsync<List<SearchResultItem>>($"/api/search?q={uniquePrefix}");
        Assert.Contains(searchBefore!, s => s.Type == "Document" && s.Id == coreDoc.Id);
        Assert.Contains(searchBefore!, s => s.Type == "Document" && s.Id == matterDoc.Id);

        // Archive coreDoc and matterDoc
        using var arch1 = await _client.PostAsync($"/api/documents/{coreDoc.Id}/archive", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, arch1.StatusCode);
        using var arch2 = await _client.PostAsync($"/api/documents/{matterDoc.Id}/archive", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, arch2.StatusCode);

        // Verification after archive:
        // 1. Award core query excludes coreDoc
        var awardCoreAfter = await _client.GetFromJsonAsync<List<CoreDocumentItem>>($"/api/awards/{award.Id}/core-documents");
        Assert.DoesNotContain(awardCoreAfter!, d => d.DocumentId == coreDoc.Id);

        // 2. Village core query excludes coreDoc
        var villageCoreAfter = await _client.GetFromJsonAsync<List<VillageAwardCoreItem>>($"/api/villages/{villageId}/core-records");
        var awardInVillageAfter = Assert.Single(villageCoreAfter!, a => a.Id == award.Id);
        Assert.DoesNotContain(awardInVillageAfter.Documents, d => d.DocumentId == coreDoc.Id);

        // 3. Matter inherited core docs excludes coreDoc
        var matterDetailAfter = await _client.GetFromJsonAsync<MatterDetailItem>($"/api/matters/{matter.Id}");
        Assert.DoesNotContain(matterDetailAfter!.Award!.Documents, d => d.DocumentId == coreDoc.Id);

        // 4. Matter documents query excludes matterDoc
        var matterDocsAfter = await _client.GetFromJsonAsync<List<MatterDocumentDto>>($"/api/matters/{matter.Id}/documents");
        Assert.DoesNotContain(matterDocsAfter!, d => d.DocumentId == matterDoc.Id);

        // 5. Eligible documents query excludes matterDoc
        var eligibleAfter = await _client.GetFromJsonAsync<List<EligibleDocumentItem>>($"/api/matters/{matter.Id}/eligible-documents");
        Assert.DoesNotContain(eligibleAfter!, d => d.Id == matterDoc.Id);
        Assert.DoesNotContain(eligibleAfter!, d => d.Id == coreDoc.Id);

        // 6. Search excludes both archived documents
        var searchAfter = await _client.GetFromJsonAsync<List<SearchResultItem>>($"/api/search?q={uniquePrefix}");
        Assert.DoesNotContain(searchAfter!, s => s.Type == "Document" && (s.Id == coreDoc.Id || s.Id == matterDoc.Id));

        // 7. Generic ZIP rejects archived documents
        using var zipGenericRes = await _client.PostAsJsonAsync("/api/documents/export", new { documentIds = new[] { coreDoc.Id } });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, zipGenericRes.StatusCode);

        // 8. Matter ZIP rejects archived documents
        using var zipMatterRes = await _client.PostAsJsonAsync($"/api/matters/{matter.Id}/export", new { documentIds = new[] { matterDoc.Id } });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, zipMatterRes.StatusCode);

        // 9. Vault Status filter works:
        // status=Active does NOT contain them
        var vaultActive = await _client.GetFromJsonAsync<PageResponse<DocumentDetailItem>>($"/api/documents?q={uniquePrefix}&status=Active");
        Assert.DoesNotContain(vaultActive!.Items, d => d.Id == coreDoc.Id || d.Id == matterDoc.Id);

        // status=Archived CONTAINS them
        var vaultArchived = await _client.GetFromJsonAsync<PageResponse<DocumentDetailItem>>($"/api/documents?q={uniquePrefix}&status=Archived");
        Assert.Contains(vaultArchived!.Items, d => d.Id == coreDoc.Id);
        Assert.Contains(vaultArchived!.Items, d => d.Id == matterDoc.Id);

        // status=All contains them
        var vaultAll = await _client.GetFromJsonAsync<PageResponse<DocumentDetailItem>>($"/api/documents?q={uniquePrefix}&status=All");
        Assert.Contains(vaultAll!.Items, d => d.Id == coreDoc.Id);
        Assert.Contains(vaultAll!.Items, d => d.Id == matterDoc.Id);

        // Restore coreDoc
        using var restoreRes = await _client.PostAsync($"/api/documents/{coreDoc.Id}/restore", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, restoreRes.StatusCode);

        // Now coreDoc appears in Award core query again
        var awardCoreRestored = await _client.GetFromJsonAsync<List<CoreDocumentItem>>($"/api/awards/{award.Id}/core-documents");
        Assert.Contains(awardCoreRestored!, d => d.DocumentId == coreDoc.Id);

        // Search finds coreDoc again
        var searchRestored = await _client.GetFromJsonAsync<List<SearchResultItem>>($"/api/search?q={uniquePrefix}");
        Assert.Contains(searchRestored!, s => s.Type == "Document" && s.Id == coreDoc.Id);
    }

    [Fact]
    public async Task Archived_documents_content_and_page_image_inaccessible_until_restored_preserving_storage_and_nm_fallback()
    {
        Guid docId;
        Guid nmDocId;
        string storagePath;
        string originalFileName = "Archived_Access_Test.pdf";
        byte[] pdfBytes = "%PDF-test-bytes-archived-correction"u8.ToArray();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();

            storagePath = await storage.SaveAsync(new MemoryStream(pdfBytes), "archived_access_test.pdf", CancellationToken.None);
            var doc = new Document
            {
                OriginalFileName = originalFileName,
                DocumentType = "Award",
                StoragePath = storagePath,
                MimeType = "application/pdf",
                Status = "Active"
            };
            db.Add(doc);

            var village = new Village { Name = "Archived Test Village" };
            db.Add(village);

            var nmDoc = new NmDocument
            {
                Document = doc,
                Village = village,
                Status = NmReviewStatus.Committed
            };
            db.Add(nmDoc);

            await db.SaveChangesAsync();
            docId = doc.Id;
            nmDocId = nmDoc.Id;
        }

        // A. Active document: GET /content -> 200
        using (var activeRes = await _client.GetAsync($"/api/documents/{docId}/content"))
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, activeRes.StatusCode);
        }

        // Active document via NM fallback: GET /content -> 200
        using (var activeNmRes = await _client.GetAsync($"/api/documents/{nmDocId}/content"))
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, activeNmRes.StatusCode);
        }

        // Archive document
        using (var archRes = await _client.PostAsync($"/api/documents/{docId}/archive", null))
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, archRes.StatusCode);
        }

        // B. Archived document: GET /content -> 404
        using (var archContentRes = await _client.GetAsync($"/api/documents/{docId}/content"))
        {
            Assert.Equal(System.Net.HttpStatusCode.NotFound, archContentRes.StatusCode);
        }

        // C. Archived document: GET /content?download=true -> 404
        using (var archDownloadRes = await _client.GetAsync($"/api/documents/{docId}/content?download=true"))
        {
            Assert.Equal(System.Net.HttpStatusCode.NotFound, archDownloadRes.StatusCode);
        }

        // Archived document: GET /page-image -> 404
        using (var archPageRes = await _client.GetAsync($"/api/documents/{docId}/page-image?page=1"))
        {
            Assert.Equal(System.Net.HttpStatusCode.NotFound, archPageRes.StatusCode);
        }

        // F. NM fallback: Archived source document cannot be streamed through NmDocument ID (404)
        using (var archNmContentRes = await _client.GetAsync($"/api/documents/{nmDocId}/content"))
        {
            Assert.Equal(System.Net.HttpStatusCode.NotFound, archNmContentRes.StatusCode);
        }
        using (var archNmDownloadRes = await _client.GetAsync($"/api/documents/{nmDocId}/content?download=true"))
        {
            Assert.Equal(System.Net.HttpStatusCode.NotFound, archNmDownloadRes.StatusCode);
        }
        using (var archNmPageRes = await _client.GetAsync($"/api/documents/{nmDocId}/page-image?page=1"))
        {
            Assert.Equal(System.Net.HttpStatusCode.NotFound, archNmPageRes.StatusCode);
        }

        // G. No physical file deletion during archive remains true
        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            using var fileStream = await storage.OpenReadAsync(storagePath, CancellationToken.None);
            Assert.NotNull(fileStream);
            using var ms = new MemoryStream();
            await fileStream.CopyToAsync(ms);
            Assert.Equal(pdfBytes, ms.ToArray());
        }

        // D. Restore same document: GET /content -> 200
        using (var restoreRes = await _client.PostAsync($"/api/documents/{docId}/restore", null))
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, restoreRes.StatusCode);
        }

        using (var restoredContentRes = await _client.GetAsync($"/api/documents/{docId}/content"))
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, restoredContentRes.StatusCode);
        }

        // E. Restore: GET /content?download=true -> 200 and original filename preserved
        using (var restoredDlRes = await _client.GetAsync($"/api/documents/{docId}/content?download=true"))
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, restoredDlRes.StatusCode);
            var disposition = restoredDlRes.Content.Headers.ContentDisposition;
            Assert.NotNull(disposition);
            Assert.Equal("attachment", disposition!.DispositionType);
            Assert.Equal(originalFileName, disposition.FileName?.Trim('"'));
        }

        // F (cont). Restored source can be streamed again through NM fallback (200)
        using (var restoredNmRes = await _client.GetAsync($"/api/documents/{nmDocId}/content"))
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, restoredNmRes.StatusCode);
        }
        using (var restoredNmDlRes = await _client.GetAsync($"/api/documents/{nmDocId}/content?download=true"))
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, restoredNmDlRes.StatusCode);
            var disposition = restoredNmDlRes.Content.Headers.ContentDisposition;
            Assert.NotNull(disposition);
            Assert.Equal("attachment", disposition!.DispositionType);
            Assert.Equal(originalFileName, disposition.FileName?.Trim('"'));
        }
    }

    [Fact]
    public async Task Core_document_upload_cleans_up_storage_binary_when_db_save_fails()
    {
        var inMemoryDbName = $"failing-test-{Guid.NewGuid():N}";
        var interceptor = new FailingDocumentSaveInterceptor();

        using var failingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<LacDbContext>>();
                services.RemoveAll<LacDbContext>();
                services.AddDbContext<LacDbContext>(options =>
                {
                    options.UseInMemoryDatabase(inMemoryDbName)
                           .AddInterceptors(interceptor);
                });
            });
        });

        var paths = failingFactory.Services.GetRequiredService<LocalStoragePaths>();
        var filesBefore = Directory.GetFiles(paths.DocumentRoot).ToHashSet();

        Guid awardId;
        using (var scope = failingFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var village = new Village { Name = "Fail Village" };
            var award = new Award { AwardNumber = "FAIL-CLEANUP-TEST" };
            db.AddRange(village, award, new AwardVillage { Award = award, Village = village });
            await db.SaveChangesAsync();
            awardId = award.Id;
        }

        interceptor.ShouldFail = true;

        var client = failingFactory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(new MemoryStream("%PDF-fail-test"u8.ToArray())), "file", "should-be-cleaned.pdf");

        var response = await client.PostAsync($"/api/awards/{awardId}/core-documents?role=NM", content);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

        var filesAfter = Directory.GetFiles(paths.DocumentRoot);
        var newlyCreated = filesAfter.Where(f => !filesBefore.Contains(f)).ToList();
        Assert.Empty(newlyCreated);
    }

    [Fact]
    public async Task Core_document_upload_preserves_original_persistence_exception_when_cleanup_fails()
    {
        var inMemoryDbName = $"failing-test-{Guid.NewGuid():N}";
        var interceptor = new FailingDocumentSaveInterceptor();
        Exception? capturedException = null;

        using var failingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<LacDbContext>>();
                services.RemoveAll<LacDbContext>();
                services.AddDbContext<LacDbContext>(options =>
                {
                    options.UseInMemoryDatabase(inMemoryDbName)
                           .AddInterceptors(interceptor);
                });

                var existingStorage = services.Single(d => d.ServiceType == typeof(IDocumentStorage));
                services.Remove(existingStorage);
                services.AddScoped<IDocumentStorage>(sp =>
                {
                    var inner = ActivatorUtilities.CreateInstance<LocalDocumentStorage>(sp);
                    return new FailingDeleteStorage(inner);
                });

                services.AddSingleton<Microsoft.AspNetCore.Diagnostics.IExceptionHandler>(new TestExceptionHandler(ex => capturedException = ex));
            });
        });

        Guid awardId;
        using (var scope = failingFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var village = new Village { Name = "Fail Village 2" };
            var award = new Award { AwardNumber = "FAIL-CLEANUP-MASK-TEST" };
            db.AddRange(village, award, new AwardVillage { Award = award, Village = village });
            await db.SaveChangesAsync();
            awardId = award.Id;
        }

        interceptor.ShouldFail = true;

        var client = failingFactory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(new MemoryStream("%PDF-fail-test-mask"u8.ToArray())), "file", "cleanup-fail.pdf");

        var response = await client.PostAsync($"/api/awards/{awardId}/core-documents?role=NM", content);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

        Assert.NotNull(capturedException);
        Assert.Equal("Simulated DB failure during document save", capturedException.Message);
        Assert.DoesNotContain("Storage cleanup failed", capturedException.Message);
    }

    private sealed class FailingDeleteStorage(IDocumentStorage inner) : IDocumentStorage
    {
        public Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct) => inner.SaveAsync(content, fileName, ct);
        public Task<DocumentStorageWriteResult> SaveAndHashAsync(Stream content, string fileName, CancellationToken ct) => inner.SaveAndHashAsync(content, fileName, ct);
        public Task DeleteAsync(string storagePath, CancellationToken ct) => throw new IOException("Storage cleanup failed: disk error during delete");
        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct) => inner.OpenReadAsync(storagePath, ct);
        public StorageHealth GetHealth() => inner.GetHealth();
    }

    private sealed class TestExceptionHandler(Action<Exception> onException) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
    {
        public ValueTask<bool> TryHandleAsync(Microsoft.AspNetCore.Http.HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            onException(exception);
            return ValueTask.FromResult(false);
        }
    }

    private sealed class FailingDocumentSaveInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool ShouldFail { get; set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (ShouldFail && eventData.Context?.ChangeTracker.Entries<Document>().Any() == true)
            {
                throw new InvalidOperationException("Simulated DB failure during document save");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed record MatterDocumentDto(Guid DocumentId, string? DocumentRole, string? DisplayName, string OriginalFileName, DateTimeOffset UploadedAt);
    private sealed record CoreDocumentItem(Guid DocumentId, string Role, string OriginalFileName, string? MimeType, DateTimeOffset UploadedAt);
    private sealed record VillageAwardCoreItem(Guid Id, string AwardNumber, DateOnly? AwardDate, string? AwardType, List<VillageCoreDocDto> Documents);
    private sealed record VillageCoreDocDto(Guid DocumentId, string? CoreDocumentRole, string OriginalFileName, DateTimeOffset UploadedAt);
    private sealed record MatterDetailItem(Guid Id, Guid VillageId, string VillageName, string Title, string MatterType, string Status, MatterAwardDetailDto? Award);
    private sealed record MatterAwardDetailDto(Guid AwardId, string AwardNumber, List<VillageCoreDocDto> Documents);
    private sealed record EligibleDocumentItem(Guid Id, string OriginalFileName, string DocumentType, DateTimeOffset UploadedAt, string Source);
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"api-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LacDbContext>>();
            services.RemoveAll<LacDbContext>();
            services.AddDbContext<LacDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}
