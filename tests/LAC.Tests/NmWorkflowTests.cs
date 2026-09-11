using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LAC.Tests;

public sealed class NmWorkflowTests
{
 [Fact] public async Task Exact_ocr_khasra_remains_unresolved_until_human_selects_it()
 { await using var db=Db(); var v=new Village{Name="Fictional"};var a=new Award{AwardNumber="A"};var d=new Document{DocumentType="NM",OriginalFileName="x.pdf",StoragePath="x"};var k=new Khasra{Village=v,DisplayNumber="1//2 min",NormalizedNumber="1//2",Qualifier="min"};db.AddRange(v,a,d,k,new AwardVillage{Award=a,Village=v});await db.SaveChangesAsync();var s=new NmWorkflowService(db);var nm=await s.CreateReviewAsync(d.Id,v.Id,a.Id,null,null,default);var row=await s.AddReviewRowAsync(nm.Id,new(1,"1","{}","Recorded Name",null,null,null,100m,null,[new("1//2 min","min")]),default);Assert.Null(row.Khasras.Single().SuggestedKhasraId);Assert.Equal(NmReviewStatus.NeedsReview,row.Khasras.Single().Status);await s.SelectKhasrasAsync(row.Id,"reviewer",[new(row.Khasras.Single().Id,k.Id)],default);Assert.Equal(k.Id,row.Khasras.Single().SuggestedKhasraId);Assert.Empty(await db.NmEntitlements.ToListAsync());Assert.Empty(await db.NmRecordedPeople.ToListAsync()); }
 [Fact] public async Task No_match_and_qualifier_mismatch_remain_needs_review()
 { await using var db=Db();var v=new Village{Name="F"};var d=new Document{OriginalFileName="x",StoragePath="x"};var k=new Khasra{Village=v,DisplayNumber="1//2 min",NormalizedNumber="1//2",Qualifier="min"};db.AddRange(v,d,k);await db.SaveChangesAsync();var s=new NmWorkflowService(db);var nm=await s.CreateReviewAsync(d.Id,v.Id,null,null,null,default);var r=await s.AddReviewRowAsync(nm.Id,new(1,"1","{}","N",null,null,null,null,null,[new("1//2",null),new("9//9",null)]),default);Assert.All(r.Khasras,x=>Assert.Equal(NmReviewStatus.NeedsReview,x.Status)); }
 [Fact] public async Task Commit_preserves_person_and_multiple_human_selected_khasras_without_party_or_payment()
 { await using var db=Db();var v=new Village{Name="F"};var d=new Document{OriginalFileName="x",StoragePath="x"};var ks=new[]{new Khasra{Village=v,DisplayNumber="1//1",NormalizedNumber="1//1"},new Khasra{Village=v,DisplayNumber="1//2",NormalizedNumber="1//2"}};db.AddRange(v,d);db.AddRange(ks);await db.SaveChangesAsync();var s=new NmWorkflowService(db);var nm=await s.CreateReviewAsync(d.Id,v.Id,null,null,null,default);var input=new NmRowInput(1,"1","{}","Same Name",null,"1/2",null,250m,"NM",[new("1//1",null),new("1//2",null)]);var row=await s.AddReviewRowAsync(nm.Id,input,default);await s.SelectKhasrasAsync(row.Id,"reviewer",row.Khasras.Zip(ks,(link,k)=>new NmKhasraSelection(link.Id,k.Id)).ToList(),default);await s.VerifyRowAsync(row.Id,"reviewer",input,default);await s.CommitAsync(nm.Id,"reviewer",default);var e=await db.NmEntitlements.Include(x=>x.NmRecordedPerson).Include(x=>x.KhasraLinks).SingleAsync();Assert.Equal(2,e.KhasraLinks.Count);Assert.Null(e.NmRecordedPerson.PartyId);Assert.Equal(250m,e.EntitlementAmount);Assert.Empty(await db.Set<AwardKhasra>().ToListAsync());Assert.Equal(4,await db.SourceEvidence.CountAsync()); }
 private static LacDbContext Db()=>new(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
