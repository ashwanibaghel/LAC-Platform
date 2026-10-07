using System.Text.Json.Nodes;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace LAC.Tests;

public sealed class CourtStructuredReviewTests
{
    private static LacDbContext Memory() => new(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    internal static JsonObject Fact(string? raw,string? value=null,string role="COURT_FINDING") => new() {
        ["rawText"]=raw,["value"]=value ?? raw,["state"]=raw is null ? "NotStated" : "Explicit",["attribution"]=raw is null ? null : role,
        ["scope"]=raw is null ? null : "Current",["evidenceIds"]=raw is null ? new JsonArray() : new JsonArray("e") };
    private static JsonObject LandScope(bool awardDate=false)
    {
        var s=CourtScopePersistenceTests.Scope();
        s["evidence"]=new JsonArray(new JsonObject { ["id"]="e",["page"]=1,["attribution"]="COURT_FINDING",["scope"]="Current",
            ["text"]="Village Pochanpur Khasra 29//8/4 29//18/4 29//19/4 acquired under Award 63/86-87 dated 19.09.1986." });
        s["villages"]=new JsonArray(new JsonObject { ["id"]="v",["name"]=Fact("Pochanpur") });
        s["awards"]=new JsonArray(new JsonObject { ["id"]="a",["number"]=Fact("63/86-87"),["date"]=awardDate ? Fact("19.09.1986","1986-09-19") : Fact(null),["villageRefs"]=new JsonArray("v"),["parcelRefs"]=new JsonArray("p0","p1","p2") });
        var parcels=new JsonArray();
        foreach(var (number,index) in new[]{"29//8/4","29//18/4","29//19/4"}.Select((n,i)=>(n,i)))
            parcels.Add(new JsonObject { ["id"]=$"p{index}",["number"]=Fact(number),["normalizedNumber"]=number,["qualifier"]=null,
                ["area"]=Fact(null),["areaUnit"]=Fact(null),["villageRefs"]=new JsonArray("v"),["awardRefs"]=new JsonArray("a") });
        s["parcels"]=parcels;return s;
    }
    internal static JsonObject AuthorityScope(string? jurisdiction,bool direction=true,string role="COURT_DIRECTION")
    {
        var s=CourtScopePersistenceTests.Scope();var designation="Land Acquisition Collector";
        var text=$"{designation}{(jurisdiction is null ? "" : " ("+jurisdiction+")")} shall file a report.";
        s["evidence"]=new JsonArray(new JsonObject { ["id"]="e",["page"]=1,["text"]=text,["attribution"]=role,["scope"]="Current",["location"]=direction ? "Body" : "Caption" });
        s["relevanceBasis"]=new JsonArray(new JsonObject { ["term"]=designation,["authorityText"]=designation,["jurisdiction"]=jurisdiction,["evidenceId"]="e",["location"]=direction ? "Body" : "Caption" });
        s["lacRelevant"]=true;s["lacRelevanceState"]="Relevant";
        if(direction)s["directions"]=new JsonArray(new JsonObject { ["id"]="d",["directedTo"]=Fact(designation,role:role),["action"]=Fact(text,role:role),
            ["object"]=Fact(text,role:role),["deadline"]=Fact(null),["nextHearingDate"]=Fact(null),["modality"]="Mandatory",["jurisdiction"]=jurisdiction,["lacAuthorityScope"]="UnknownLAC",["lacActionable"]=false,["parcelRefs"]=new JsonArray() });
        return s;
    }
    [Theory]
    [InlineData("District Alpha",true,"COURT_DIRECTION","ThisOffice",true)]
    [InlineData("District Beta",true,"COURT_DIRECTION","OtherLAC",false)]
    [InlineData(null,true,"COURT_DIRECTION","UnknownLAC",false)]
    [InlineData("District Alpha",false,"OTHER","ThisOffice",false)]
    [InlineData("District Alpha",true,"COURT_FINDING","ThisOffice",false)]
    [InlineData("District Alpha",true,"PETITIONER_SUBMISSION","ThisOffice",false)]
    public async Task Relevance_authority_and_actionability_are_independent(string? jurisdiction,bool direction,string role,string expected,bool action)
    {
        await using var db=Memory();var raw=AuthorityScope(jurisdiction,direction,role);CourtScopeContract.Validate(raw,new string('a',64));
        var resolved=await new CourtOfficeAuthority(db,Options.Create(new CourtIntelligenceOfficeOptions {JurisdictionAliases=["District Alpha"],OtherJurisdictions=["District Beta"]})).ResolveAsync(raw);
        Assert.True(resolved["lacRelevant"]!.GetValue<bool>());Assert.Equal(expected,resolved["lacAuthorityScope"]!.GetValue<string>());
        Assert.Equal(action,resolved["lacActionable"]!.GetValue<bool>());
        if(expected=="UnknownLAC")Assert.Equal("NeedsReview",resolved["lacRelevanceState"]!.GetValue<string>());
        Assert.False(raw["lacActionable"]!.GetValue<bool>());
    }
    private static async Task<(CourtCase Case,CourtOrderIntelligenceRevision Revision,AppUser Actor,Village Village,Award Award,List<Khasra> Parcels,CourtOrderLinkReview Review,ICourtAuthorizationService Auth)> Setup(LacDbContext db,bool conflict=false)
    {
        var (c,o)=CourtScopePersistenceTests.Source(db);var actor=o.RunItem.Run.StartedByUser;
        var role=new Role {Code="SCOPE_TEST",Name="Scope test"};db.Add(role);db.Add(new UserRole {User=actor,Role=role});
        foreach(var code in new[]{PermissionCodes.CourtView,PermissionCodes.CourtEdit,PermissionCodes.VillageView,PermissionCodes.AwardView,PermissionCodes.KhasraView})
        {
            var permission=await db.Permissions.SingleOrDefaultAsync(p=>p.Code==code);
            if(permission is null){permission=new Permission {Code=code,Name=code,Category="Test"};db.Add(permission);}
            db.Add(new RolePermission {Role=role,Permission=permission,ScopeMode=ScopeMode.All});
        }
        var v=new Village {Name="Pochanpur",SubDivision=new SubDivision {Name="Test Subdivision",District=new District {Name="District Alpha"}}};db.Add(v);
        var a=new Award {AwardNumber="63/86-87",AwardDate=new DateOnly(conflict ? 1990 : 1986,9,19)};db.Add(a);db.Add(new AwardVillage {Award=a,Village=v});
        var parcels=new[]{"29//8/4","29//18/4","29//19/4"}.Select(number=>new Khasra {Village=v,DisplayNumber=number,NormalizedNumber=number,TotalArea=10,AreaUnit="bigha"}).ToList();db.AddRange(parcels);
        db.Add(new CourtCaseAward {CourtCase=c,Award=a});db.Add(new CourtCaseKhasra {CourtCase=c,Khasra=parcels[0]});await db.SaveChangesAsync();
        var revision=await new CourtIntelligencePersistence(db).PersistAsync(c.Id,o.Id,o.OrderDate!.Value,CourtScopePersistenceTests.Url,LandScope(conflict));
        var auth=new CourtAuthorizationService(db,null!,null!,null!);return(c,revision,actor,v,a,parcels,new CourtOrderLinkReview(db,auth),auth);
    }
    private static async Task ExerciseLinks(LacDbContext db)
    {
        var s=await Setup(db);await s.Review.MatchAsync(s.Revision.Id,s.Actor.Id);await s.Review.MatchAsync(s.Revision.Id,s.Actor.Id);
        var links=await db.CourtOrderRecordLinks.ToListAsync();Assert.Equal(5,links.Count);Assert.All(links,l=>Assert.Equal(CourtRecordMatchState.Candidate,l.MatchState));
        var projection=new CourtConfirmedOrderProjection(db,s.Auth);
        Assert.Empty(await projection.ReadAsync("Village",s.Village.Id,s.Actor.Id));Assert.Empty(await projection.ReadAsync("Award",s.Award.Id,s.Actor.Id));
        foreach(var link in links)await s.Review.ReviewAsync(link.Id,new(CourtRecordMatchState.Confirmed,link.Version,"Verified against Court source and office identity."),s.Actor.Id);
        foreach(var (type,id) in new[]{("Village",s.Village.Id),("Award",s.Award.Id)}.Concat(s.Parcels.Select(k=>("Khasra",k.Id))))
        {var order=Assert.Single(await projection.ReadAsync(type,id,s.Actor.Id));Assert.Equal(s.Case.Id,order.CourtCaseId);Assert.Equal(s.Revision.CourtOrderIntelligenceId,order.CourtOrderIntelligenceId);}
        Assert.Equal(5,await db.AuditLogs.CountAsync(x=>x.Action=="CourtOrderLinkReviewed"));
        Assert.Single(db.Set<CourtCaseAward>());Assert.Single(db.Set<CourtCaseKhasra>());Assert.Empty(db.CourtProceedings);
        Assert.All(s.Parcels,k=>Assert.Equal(10,k.TotalArea));Assert.Equal(new DateOnly(1986,9,19),s.Award.AwardDate);
        Assert.Equal("NotStated",JsonNode.Parse(s.Revision.StructuredFactsJson)!["awards"]![0]!["date"]!["state"]!.GetValue<string>());
        var villageLink=links.Single(l=>l.EntityType=="Village");await s.Review.ReviewAsync(villageLink.Id,new(CourtRecordMatchState.NotMatched,villageLink.Version,"Revoked wrong identity."),s.Actor.Id);
        Assert.Empty(await projection.ReadAsync("Village",s.Village.Id,s.Actor.Id));Assert.Single(await projection.ReadAsync("Award",s.Award.Id,s.Actor.Id));
    }
    [Fact] public async Task Confirmed_links_share_one_order_and_preserve_manual_and_office_records() {await using var db=Memory();await ExerciseLinks(db);}
    [DakPostgresFact] public async Task Postgres_confirmed_links_audit_and_projections_are_relational() {await using var database=await DisposableDakDatabase.CreateAsync();await using var db=database.Context();await ExerciseLinks(db);}
    [DakPostgresFact]
    public async Task Postgres_stale_review_rolls_back_decision_and_audit_together()
    {
        await using var database=await DisposableDakDatabase.CreateAsync();await using var db=database.Context();var s=await Setup(db);
        await s.Review.MatchAsync(s.Revision.Id,s.Actor.Id);var link=await db.CourtOrderRecordLinks.SingleAsync(l=>l.EntityType=="Village");
        await using var second=database.Context();var stale=await second.CourtOrderRecordLinks.SingleAsync(l=>l.Id==link.Id);
        await s.Review.ReviewAsync(link.Id,new(CourtRecordMatchState.Confirmed,link.Version,"First officer decision"),s.Actor.Id);
        var review=new CourtOrderLinkReview(second,new CourtAuthorizationService(second,null!,null!,null!));
        var error=await Assert.ThrowsAsync<CourtWorkflowException>(()=>review.ReviewAsync(stale.Id,new(CourtRecordMatchState.NotMatched,stale.Version,"Concurrent stale decision"),s.Actor.Id));
        Assert.Equal(409,error.StatusCode);await using var check=database.Context();
        Assert.Equal(CourtRecordMatchState.Confirmed,(await check.CourtOrderRecordLinks.SingleAsync(l=>l.Id==link.Id)).MatchState);
        Assert.Equal(1,await check.AuditLogs.CountAsync(l=>l.Action=="CourtOrderLinkReviewed"));
    }
    [DakPostgresFact]
    public async Task Postgres_confirmation_requires_review_metadata_and_unique_source_identity()
    {
        await using var database=await DisposableDakDatabase.CreateAsync();await using var db=database.Context();var s=await Setup(db);
        db.Add(new CourtOrderRecordLink {RevisionId=s.Revision.Id,ExtractedEntityId="v",EntityType="Village",VillageId=s.Village.Id,
            MatchState=CourtRecordMatchState.Confirmed,ReviewedByUserId=s.Actor.Id,ReviewedAt=DateTimeOffset.UtcNow,ReviewReason=null});
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        var first=new CourtOrderRecordLink {RevisionId=s.Revision.Id,ExtractedEntityId="v",EntityType="Village",VillageId=s.Village.Id,
            MatchState=CourtRecordMatchState.Confirmed,ReviewedByUserId=s.Actor.Id,ReviewedAt=DateTimeOffset.UtcNow,ReviewReason="Verified identity"};
        db.Add(first);await db.SaveChangesAsync();
        db.Add(new CourtOrderRecordLink {RevisionId=s.Revision.Id,ExtractedEntityId="v",EntityType="Village",VillageId=s.Village.Id,
            MatchState=CourtRecordMatchState.Confirmed,ReviewedByUserId=s.Actor.Id,ReviewedAt=DateTimeOffset.UtcNow,ReviewReason="Duplicate confirmation"});
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task Review_requires_permission_reason_and_current_version()
    {
        await using var db=Memory();var s=await Setup(db);await s.Review.MatchAsync(s.Revision.Id,s.Actor.Id);var link=await db.CourtOrderRecordLinks.FirstAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<CourtWorkflowException>(()=>s.Review.ReviewAsync(link.Id,new(CourtRecordMatchState.Confirmed,link.Version,"Reviewed"),Guid.NewGuid()))).StatusCode);
        Assert.Equal(400,(await Assert.ThrowsAsync<CourtWorkflowException>(()=>s.Review.ReviewAsync(link.Id,new(CourtRecordMatchState.Confirmed,link.Version," "),s.Actor.Id))).StatusCode);
        Assert.Equal(409,(await Assert.ThrowsAsync<CourtWorkflowException>(()=>s.Review.ReviewAsync(link.Id,new(CourtRecordMatchState.Confirmed,link.Version+1,"Reviewed"),s.Actor.Id))).StatusCode);
        Assert.Empty(db.AuditLogs.Where(x=>x.Action=="CourtOrderLinkReviewed"));
    }
    [Fact]
    public async Task Conflict_cannot_be_laundered_through_needs_review()
    {
        await using var db=Memory();var s=await Setup(db,true);await s.Review.MatchAsync(s.Revision.Id,s.Actor.Id);var link=await db.CourtOrderRecordLinks.SingleAsync(l=>l.EntityType=="Award");
        Assert.Equal(CourtRecordMatchState.Conflict,link.MatchState);await s.Review.ReviewAsync(link.Id,new(CourtRecordMatchState.NeedsReview,link.Version,"Inspect date conflict"),s.Actor.Id);
        Assert.Equal(409,(await Assert.ThrowsAsync<CourtWorkflowException>(()=>s.Review.ReviewAsync(link.Id,new(CourtRecordMatchState.Confirmed,link.Version,"Attempt confirmation"),s.Actor.Id))).StatusCode);
        Assert.Single(db.AuditLogs,x=>x.Action=="CourtOrderLinkReviewed");Assert.Equal(new DateOnly(1990,9,19),s.Award.AwardDate);
    }
    [Fact]
    public async Task Changed_canonical_identity_is_rechecked_at_confirmation()
    {
        await using var db=Memory();var s=await Setup(db);await s.Review.MatchAsync(s.Revision.Id,s.Actor.Id);var link=await db.CourtOrderRecordLinks.SingleAsync(l=>l.EntityType=="Village");
        s.Village.Name="Another village";await db.SaveChangesAsync();
        Assert.Equal(409,(await Assert.ThrowsAsync<CourtWorkflowException>(()=>s.Review.ReviewAsync(link.Id,new(CourtRecordMatchState.Confirmed,link.Version,"Reviewed"),s.Actor.Id))).StatusCode);
    }
    [Fact]
    public async Task Unknown_source_identity_retains_review_state_without_inventing_targets()
    {
        await using var db=Memory();var s=await Setup(db);s.Village.Name="Other name";await db.SaveChangesAsync();await s.Review.MatchAsync(s.Revision.Id,s.Actor.Id);
        var village=await db.CourtOrderRecordLinks.SingleAsync(l=>l.EntityType=="Village");Assert.Equal(CourtRecordMatchState.NotMatched,village.MatchState);Assert.Null(village.VillageId);
        Assert.All(await db.CourtOrderRecordLinks.Where(l=>l.EntityType=="Khasra").ToListAsync(),l=>{Assert.Equal(CourtRecordMatchState.NeedsReview,l.MatchState);Assert.Null(l.KhasraId);});
        Assert.Empty(await new CourtConfirmedOrderProjection(db,s.Auth).ReadAsync("Village",s.Village.Id,s.Actor.Id));
        Assert.Equal(409,(await Assert.ThrowsAsync<CourtWorkflowException>(()=>s.Review.ReviewAsync(village.Id,new(CourtRecordMatchState.Confirmed,village.Version,"Cannot invent identity"),s.Actor.Id))).StatusCode);
    }
    [Fact]
    public async Task Historical_authority_does_not_authorize_a_generic_current_LAC()
    {
        await using var db=Memory();var s=AuthorityScope("District Alpha",direction:false,role:"HISTORICAL_QUOTATION");s["evidence"]![0]!["scope"]="Quoted";
        var resolved=await new CourtOfficeAuthority(db,Options.Create(new CourtIntelligenceOfficeOptions {JurisdictionAliases=["District Alpha"]})).ResolveAsync(s);
        Assert.Equal("UnknownLAC",resolved["lacAuthorityScope"]!.GetValue<string>());Assert.False(resolved["lacActionable"]!.GetValue<bool>());
    }
    [Fact]
    public async Task Officer_confirmed_village_resolves_parcel_geography_without_changing_source()
    {
        await using var db=Memory();var s=await Setup(db);db.Add(new Village {Name=s.Village.Name,SubDivision=new SubDivision {Name="Other subdivision",District=new District {Name="Other district"}}});await db.SaveChangesAsync();
        await s.Review.MatchAsync(s.Revision.Id,s.Actor.Id);var village=await db.CourtOrderRecordLinks.SingleAsync(l=>l.EntityType=="Village" && l.VillageId==s.Village.Id);Assert.Equal(CourtRecordMatchState.Ambiguous,village.MatchState);
        await s.Review.ReviewAsync(village.Id,new(CourtRecordMatchState.Confirmed,village.Version,"Verified the source Village jurisdiction."),s.Actor.Id);
        await s.Review.MatchAsync(s.Revision.Id,s.Actor.Id);var parcel=await db.CourtOrderRecordLinks.SingleAsync(l=>l.KhasraId==s.Parcels[0].Id);
        await s.Review.ReviewAsync(parcel.Id,new(CourtRecordMatchState.Confirmed,parcel.Version,"Exact number/qualifier in reviewed Village."),s.Actor.Id);
        Assert.Single(await new CourtConfirmedOrderProjection(db,s.Auth).ReadAsync("Khasra",s.Parcels[0].Id,s.Actor.Id));
    }
    [Fact]
    public void Model_ids_false_attribution_and_dangling_relationships_are_rejected()
    {
        var s=LandScope();s["villages"]![0]!["villageId"]=Guid.NewGuid().ToString();Assert.Throws<InvalidDataException>(()=>CourtScopeContract.Validate(s,new string('a',64)));
        s=LandScope();s["parcels"]![0]!["villageRefs"]=new JsonArray("invented");Assert.Throws<InvalidDataException>(()=>CourtScopeContract.Validate(s,new string('a',64)));
        s=LandScope();s["villages"]![0]!["name"]!["attribution"]="PETITIONER_SUBMISSION";Assert.Throws<InvalidDataException>(()=>CourtScopeContract.Validate(s,new string('a',64)));
        s=AuthorityScope("District Alpha");s["directions"]![0]!["action"]!["value"]="Land Acquisition Collector shall pay compensation.";Assert.Throws<InvalidDataException>(()=>CourtScopeContract.Validate(s,new string('a',64)));
        s=AuthorityScope(null);s["relevanceBasis"]![0]!["jurisdiction"]="District Alpha";Assert.Throws<InvalidDataException>(()=>CourtScopeContract.Validate(s,new string('a',64)));
    }
    [Theory]
    [InlineData("within four weeks from today","2026-11-04")]
    [InlineData("within four weeks from receipt of this order",null)]
    [InlineData("within four weeks",null)]
    [InlineData("by 15.10.2026","2026-10-15")]
    [InlineData("before next hearing",null)]
    public void Deadline_requires_an_explicit_anchor(string deadline,string? expected) => Assert.Equal(expected,CourtDirectionDeadline.Resolve(new DateOnly(2026,10,7),deadline)?.ToString("yyyy-MM-dd"));
}
