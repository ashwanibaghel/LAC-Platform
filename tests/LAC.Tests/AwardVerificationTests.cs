using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LAC.Tests;

public sealed class AwardVerificationTests
{
    [Fact]
    public async Task Geometry_backed_award_khasra_requires_every_source_cell_and_creates_gold_per_field()
    {
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var award = new Award { AwardNumber = "FICTIONAL-FIELD-REVIEW" }; var village = new Village { Name = "Fictional Village" }; var document = new LAC.Domain.Document { OriginalFileName = "fictional.pdf", StoragePath = "fictional.pdf" };
        db.AddRange(award, village, document, new AwardVillage { Award = award, Village = village }, new DocumentAward { Award = award, Document = document }); await db.SaveChangesAsync();
        var locator = JsonSerializer.Serialize(new { page = 1, sourceRegion = new { x = 1, y = 1, width = 5, height = 5 }, structuredPayload = new { sourceCells = new { khasra = new { rawOcr = "6//10", normalizedSuggestion = "6//10", sourceRegion = new { x = 1, y = 1, width = 5, height = 5 } }, recordedArea = new { rawOcr = "22 -- 3", normalizedSuggestion = "22-3", sourceRegion = new { x = 8, y = 1, width = 5, height = 5 } }, awardedArea = new { rawOcr = "22 -- 3", normalizedSuggestion = "22-3", sourceRegion = new { x = 15, y = 1, width = 5, height = 5 } } } } });
        var input = new IngestionCandidateInput(AwardIngestionCandidateType.AwardKhasra, JsonSerializer.Serialize(new AwardKhasraCandidate("6//10", null, null, null, null, 22, 3, null, 22, 3, null)), locator, "fictional", .9m);
        var service = new AwardIngestionService(db, new AwardWorkflowService(db)); var session = await service.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Document, award.Id, village.Id, document.Id, "tester", null, [input], default);
        db.Add(new AwardDocumentExtractionJob { DocumentId = document.Id, IngestionSessionId = session.Id, TargetAwardId = award.Id, SelectedVillageId = village.Id, TotalPages = 1, ProcessedPages = 1, Status = AwardDocumentExtractionJobStatus.NeedsReview }); await db.SaveChangesAsync();
        var candidate = await db.AwardIngestionCandidates.SingleAsync();
        await service.VerifyAwardKhasraFieldAsync(candidate.Id, new("Officer", "Khasra", "6//10", "Confirm"), default);
        candidate = await db.AwardIngestionCandidates.SingleAsync(); Assert.Null(candidate.VerifiedAt); Assert.Equal(AwardIngestionCandidateStatus.NeedsReview, candidate.Status); Assert.Single(await db.DocumentTrainingExamples.ToListAsync());
        await service.VerifyAwardKhasraFieldAsync(candidate.Id, new("Officer", "RecordedArea", "22-3", "Confirm"), default);
        candidate = await db.AwardIngestionCandidates.SingleAsync(); Assert.Null(candidate.VerifiedAt); Assert.Equal(2, await db.DocumentTrainingExamples.CountAsync()); Assert.All(await db.DocumentTrainingExamples.ToListAsync(), x => Assert.False(x.WasCorrected));
        await service.VerifyAwardKhasraFieldAsync(candidate.Id, new("Officer", "AwardedArea", "22-3", "Confirm"), default);
        candidate = await db.AwardIngestionCandidates.SingleAsync(); Assert.NotNull(candidate.VerifiedAt); Assert.Equal(3, await db.DocumentTrainingExamples.CountAsync()); Assert.Empty(await db.Khasras.ToListAsync()); Assert.Empty(await db.Set<AwardKhasra>().ToListAsync());
        await service.VerifyAwardKhasraFieldAsync(candidate.Id, new("Officer", "Khasra", "6//10", "Confirm"), default);
        Assert.Equal(3, await db.DocumentTrainingExamples.CountAsync());
    }
    [Fact]
    public async Task Older_upload_can_select_context_without_reupload_or_canonical_writes()
    {
        await using var f=await Fixture.Create();
        var s=await f.Service.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Document,null,null,f.Document.Id,null,null,[f.KhasraInput()],default);
        await f.Service.SetReviewContextAsync(s.Id,new(f.Award.Id,f.Village.Id,"Officer"),default);
        Assert.Equal(f.Award.Id,s.TargetAwardId);Assert.Equal(f.Village.Id,s.SelectedVillageId);
        var c=await f.Db.AwardIngestionCandidates.SingleAsync();Assert.Equal(1,c.SourcePage);Assert.True(c.SafeToConfirm);
        Assert.Single(await f.Db.Documents.ToListAsync());Assert.Single(await f.Db.DocumentAwards.ToListAsync());
        Assert.Empty(await f.Db.Set<AwardKhasra>().ToListAsync());Assert.Null(c.VerifiedAt);
    }

    [Fact]
    public async Task Village_confirmation_checks_official_spelling_and_preserves_evidence()
    {
        await using var f=await Fixture.Create();var s=await f.Preview(f.Input(new AwardVillageCandidate("OCR spelling",null)));
        var c=await f.Db.AwardIngestionCandidates.SingleAsync();
        await Assert.ThrowsAsync<AwardIngestionException>(()=>f.Service.VerifyFactAsync(c.Id,new("Officer",null),default));
        await f.Service.VerifyFactAsync(c.Id,new("Officer",JsonSerializer.Serialize(new AwardVillageCandidate(f.Village.Name,f.Village.Name))),default);
        Assert.NotNull(c.VerifiedAt);Assert.Empty(await f.Db.SourceEvidence.ToListAsync());
        await Assert.ThrowsAsync<AwardIngestionException>(()=>f.Service.SetReviewContextAsync(s.Id,new(f.Award.Id,f.Village.Id,"Officer"),default));
        await f.Service.CommitVerifiedAsync(s.Id,new("Officer",1),default);
        Assert.Contains(await f.Db.SourceEvidence.ToListAsync(),e=>e.AwardId==f.Award.Id && e.DocumentId==f.Document.Id && e.PageNumber==1);
    }

    [Fact]
    public async Task Award_core_verification_allows_same_number_on_an_unrelated_award()
    {
        await using var f=await Fixture.Create();
        var other=new Award { AwardNumber=f.Award.AwardNumber, AwardDate=new DateOnly(2025,1,1) };
        f.Db.Add(other); await f.Db.SaveChangesAsync();
        var session=await f.Preview(f.Input(new AwardCoreCandidate("FIC-VERIFY",new DateOnly(2026,2,3),"Permanent","Verified purpose")));
        var candidate=await f.Db.AwardIngestionCandidates.SingleAsync();
        await f.Service.VerifyFactAsync(candidate.Id,new("Officer",null),default);
        await f.Service.CommitVerifiedAsync(session.Id,new("Officer",1),default);
        Assert.Equal(new DateOnly(2026,2,3),f.Award.AwardDate);
        Assert.Equal("Verified purpose",f.Award.Purpose);
        Assert.Equal(2,await f.Db.Awards.CountAsync(x=>x.AwardNumber=="FIC-VERIFY"));
        Assert.Contains(await f.Db.SourceEvidence.ToListAsync(),e=>e.AwardId==f.Award.Id && e.FactName=="awardNumber");
    }

    [Fact]
    public async Task Exact_group_confirmation_is_human_verification_not_canonical_commit()
    {
        await using var f=await Fixture.Create();
        var session=await f.Preview(f.KhasraInput());
        Assert.True((await f.Db.AwardIngestionCandidates.SingleAsync()).SafeToConfirm);
        Assert.Equal(1,await f.Service.ConfirmExactAsync(session.Id,new("Test officer",1),default));
        var c=await f.Db.AwardIngestionCandidates.SingleAsync();Assert.NotNull(c.VerifiedAt);Assert.Equal("Test officer",c.VerifiedBy);
        Assert.Empty(await f.Db.Set<AwardKhasra>().ToListAsync());Assert.Empty(await f.Db.SourceEvidence.ToListAsync());
    }

    [Theory]
    [InlineData(.97,false)]
    [InlineData(.99,true)]
    public async Task Numeric_uncertainty_never_enters_safe_group(decimal confidence,bool safe)
    {
        await using var f=await Fixture.Create();var session=await f.Preview(f.KhasraInput() with {Confidence=confidence});
        Assert.Equal(safe,(await f.Db.AwardIngestionCandidates.SingleAsync()).SafeToConfirm);
        if(!safe) await Assert.ThrowsAsync<AwardIngestionException>(()=>f.Service.ConfirmExactAsync(session.Id,new("Officer",1),default));
    }

    [Fact]
    public async Task Evidence_warning_and_repeated_source_rows_block_every_affected_exact_match_without_generic_conflict()
    {
        await using var f=await Fixture.Create();var warning=f.KhasraInput() with {SourceLocatorJson=JsonSerializer.Serialize(new CandidateEvidence(1,"test","KhasraTable","Test",[],["Last digit unclear"]))};
        await f.Preview(warning);Assert.False((await f.Db.AwardIngestionCandidates.SingleAsync()).SafeToConfirm);
        var session=await f.Preview(f.KhasraInput(),f.KhasraInput(3));
        var rows=await f.Db.AwardIngestionCandidates.Where(x=>x.SessionId==session.Id).ToListAsync();
        Assert.All(rows,x=>{Assert.False(x.SafeToConfirm);Assert.Equal(AwardIngestionCandidateStatus.NeedsReview,x.Status);Assert.Contains("RepeatedDifferentAreas",x.ConflictDetailsJson);});
    }

    [Fact]
    public async Task Same_value_source_occurrences_are_retained_with_a_precise_reason_and_qualifier_is_part_of_identity()
    {
        await using var f=await Fixture.Create();
        var same=await f.Preview(f.KhasraInput(),f.KhasraInput());
        var repeated=await f.Db.AwardIngestionCandidates.Where(x=>x.SessionId==same.Id).ToListAsync();
        Assert.Equal(2,repeated.Count);Assert.All(repeated,x=>{Assert.Equal(AwardIngestionCandidateStatus.NeedsReview,x.Status);Assert.Contains("RepeatedSameValues",x.ConflictDetailsJson);});
        var distinct=await f.Preview(f.KhasraInput(),f.Input(new AwardKhasraCandidate("4//12",null,null,null,null,2,2,null,1,1,null)));
        Assert.All(await f.Db.AwardIngestionCandidates.Where(x=>x.SessionId==distinct.Id).ToListAsync(),x=>Assert.Null(x.ConflictDetailsJson));
    }

    [Fact]
    public async Task Document_commit_requires_human_verification_and_cannot_use_legacy_resolve_bypass()
    {
        await using var f=await Fixture.Create();var session=await f.Preview(f.KhasraInput());var row=await f.Db.AwardIngestionCandidates.SingleAsync();
        await Assert.ThrowsAsync<AwardIngestionException>(()=>f.Service.CommitAsync(session.Id,[row.Id],"Officer",default));
        await Assert.ThrowsAsync<AwardIngestionException>(()=>f.Service.ResolveAsync(row.Id,"KeepExisting",default));
        Assert.Empty(await f.Db.SourceEvidence.ToListAsync());
    }

    [Fact]
    public async Task Verified_khasra_fact_retains_document_page_values_after_staging_purge()
    {
        await using var f=await Fixture.Create();var session=await f.Preview(f.KhasraInput());
        await f.Service.ConfirmExactAsync(session.Id,new("Officer",1),default);await f.Service.CommitVerifiedAsync(session.Id,new("Officer",1),default);
        var link=await f.Db.Set<AwardKhasra>().SingleAsync();Assert.Equal(f.Khasra.Id,link.KhasraId);Assert.Equal(2,link.RecordedTotalAreaBigha);Assert.Equal(1,link.AwardedAreaBigha);Assert.Equal(4,f.Khasra.AreaBigha);
        var evidence=await f.Db.SourceEvidence.Where(x=>x.FactName=="awardedAreaBigha").SingleAsync();Assert.Equal(link.Id,evidence.AwardKhasraId);Assert.Equal(f.Document.Id,evidence.DocumentId);Assert.Equal(1,evidence.PageNumber);Assert.Equal("1",evidence.ConfirmedValueJson);
        f.Db.AwardIngestionCandidates.RemoveRange(await f.Db.AwardIngestionCandidates.ToListAsync());f.Db.AwardIngestionSessions.Remove(session);await f.Db.SaveChangesAsync();
        Assert.NotEmpty(await f.Db.SourceEvidence.ToListAsync());Assert.Single(await f.Db.Documents.ToListAsync());
        var view=await DocumentEvidenceQueries.ReadAsync(f.Db,x=>x.AwardKhasraId==link.Id,0,default);Assert.All(view.Items,x=>Assert.Equal($"/api/documents/{f.Document.Id}/content#page=1",x.SourceUrl));
    }

    [Fact]
    public async Task Committed_candidate_cannot_be_recommitted_or_duplicate_evidence()
    {
        await using var f=await Fixture.Create();var session=await f.Preview(f.KhasraInput());
        await f.Service.ConfirmExactAsync(session.Id,new("Officer",1),default);
        await f.Service.CommitVerifiedAsync(session.Id,new("Officer",1),default);
        var evidenceCount=await f.Db.SourceEvidence.CountAsync();
        await Assert.ThrowsAsync<AwardIngestionException>(()=>f.Service.CommitVerifiedAsync(session.Id,new("Officer",1),default));
        Assert.Equal(evidenceCount,await f.Db.SourceEvidence.CountAsync());
        Assert.Single(await f.Db.Set<AwardKhasra>().ToListAsync());
    }

    [Fact]
    public async Task Verified_notification_and_possession_have_permanent_typed_evidence()
    {
        await using var f=await Fixture.Create();var inputs=new[]{f.Input(new NotificationCandidate("Section 4","FIC/20",new(2026,1,1))),f.Input(new PossessionEventCandidate(new(2026,2,1),"Partial",null))};
        var session=await f.Preview(inputs);
        foreach(var c in await f.Db.AwardIngestionCandidates.ToListAsync())await f.Service.VerifyFactAsync(c.Id,new("Officer",null),default);
        await f.Service.CommitVerifiedAsync(session.Id,new("Officer",2),default);
        var notification=await f.Db.Notifications.SingleAsync();var possession=await f.Db.PossessionEvents.SingleAsync();
        Assert.Contains(await f.Db.SourceEvidence.ToListAsync(),e=>e.NotificationId==notification.Id && e.DocumentId==f.Document.Id && e.PageNumber==1);
        Assert.Contains(await f.Db.SourceEvidence.ToListAsync(),e=>e.PossessionEventId==possession.Id && e.DocumentId==f.Document.Id && e.PageNumber==1);
        Assert.Empty(await f.Db.Set<PossessionKhasra>().ToListAsync());Assert.Equal("Draft",f.Award.Status);
    }

    [Fact]
    public async Task Reanalysis_differences_do_not_overwrite_verified_award_areas()
    {
        await using var f=await Fixture.Create();var first=await f.Preview(f.KhasraInput());await f.Service.ConfirmExactAsync(first.Id,new("Officer",1),default);await f.Service.CommitVerifiedAsync(first.Id,new("Officer",1),default);
        var second=await f.Preview(f.KhasraInput(9));var row=await f.Db.AwardIngestionCandidates.SingleAsync(x=>x.SessionId==second.Id);
        Assert.False(row.SafeToConfirm);await Assert.ThrowsAsync<AwardIngestionException>(()=>f.Service.VerifyFactAsync(row.Id,new("Officer",null),default));
        Assert.Equal(2,(await f.Db.Set<AwardKhasra>().SingleAsync()).RecordedTotalAreaBigha);Assert.NotEmpty(await f.Db.SourceEvidence.ToListAsync());Assert.Equal(9,JsonSerializer.Deserialize<AwardKhasraCandidate>(row.StructuredPayloadJson,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.RecordedAreaBigha);
    }

    [Fact]
    public async Task Changed_verified_payload_cannot_be_committed()
    {
        await using var f=await Fixture.Create();var s=await f.Preview(f.KhasraInput());await f.Service.ConfirmExactAsync(s.Id,new("Officer",1),default);
        var c=await f.Db.AwardIngestionCandidates.SingleAsync();c.StructuredPayloadJson=f.KhasraInput(8).PayloadJson;await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<AwardIngestionException>(()=>f.Service.CommitVerifiedAsync(s.Id,new("Officer",1),default));
    }

    [Fact]
    public async Task Multiple_documents_support_same_canonical_fact_without_duplicate_khasra()
    {
        await using var f=await Fixture.Create();var first=await f.Preview(f.KhasraInput());await f.Service.ConfirmExactAsync(first.Id,new("Officer",1),default);await f.Service.CommitVerifiedAsync(first.Id,new("Officer",1),default);
        var other=new Document{OriginalFileName="fictional-second.pdf",StoragePath="fictional-second.pdf"};f.Db.Documents.Add(other);var job=new AwardDocumentExtractionJob{Document=other,TargetAward=f.Award,SelectedVillage=f.Village};f.Db.Add(job);f.Db.Add(new AwardDocumentPageExtraction{Job=job,PageNumber=1});await f.Db.SaveChangesAsync();
        var s=await f.Service.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Document,f.Award.Id,f.Village.Id,other.Id,null,null,[f.KhasraInput()],default);await f.Service.ConfirmExactAsync(s.Id,new("Second officer",1),default);await f.Service.CommitVerifiedAsync(s.Id,new("Second officer",1),default);
        Assert.Single(await f.Db.Khasras.ToListAsync());Assert.Single(await f.Db.Set<AwardKhasra>().ToListAsync());Assert.Equal(2,await f.Db.SourceEvidence.Select(x=>x.DocumentId).Distinct().CountAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public LacDbContext Db {get;}=new(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Village Village {get;}=new(){Name="Fictional"}; public Award Award {get;}=new(){AwardNumber="FIC-VERIFY"}; public Document Document {get;}=new(){OriginalFileName="fictional.pdf",StoragePath="fictional.pdf"}; public Khasra Khasra {get;private set;}=null!;
        public AwardIngestionService Service=>new(Db,new AwardWorkflowService(Db));
        public static async Task<Fixture> Create(){var f=new Fixture();f.Khasra=new(){Village=f.Village,NormalizedNumber="4//12",DisplayNumber="4//12 min",Qualifier="min",AreaBigha=4};var job=new AwardDocumentExtractionJob{Document=f.Document,TargetAward=f.Award,SelectedVillage=f.Village};f.Db.AddRange(f.Village,f.Award,f.Document,f.Khasra,new AwardVillage{Award=f.Award,Village=f.Village},job,new AwardDocumentPageExtraction{Job=job,PageNumber=1});await f.Db.SaveChangesAsync();return f;}
        public IngestionCandidateInput Input(IAwardIngestionCandidatePayload p)=>new(p.CandidateType,JsonSerializer.Serialize(p,p.GetType()),JsonSerializer.Serialize(new CandidateEvidence(1,"test","KhasraTable","Verified geometry",["known Award Village","strict Khasra grammar"],[])),"Fictional source evidence",1m);
        public IngestionCandidateInput KhasraInput(decimal recorded=2)=>Input(new AwardKhasraCandidate("4//12","min",null,null,null,recorded,2,null,1,1,null));
        public Task<AwardIngestionSession> Preview(params IngestionCandidateInput[] inputs)=>Service.CreatePreviewFromJsonAsync(AwardIngestionSourceType.Document,Award.Id,Village.Id,Document.Id,null,null,inputs,default);
        public ValueTask DisposeAsync()=>Db.DisposeAsync();
    }
}
