using LAC.Domain;
using Microsoft.EntityFrameworkCore;
namespace LAC.Infrastructure;
public static class SeedData
{
 public static async Task SeedAsync(LacDbContext db, CancellationToken ct)
 {
  if (!await db.Districts.AnyAsync(ct))
  {
   var d=new District{Name="South West Delhi"}; var s=new SubDivision{District=d,Name="Demo Sub-Division"}; var v=new Village{SubDivision=s,Name="Galibpur"}; var p=new AcquisitionProject{Name="Demo Corridor Acquisition",RequiringAgency="Demo Requiring Agency",ActRegime="Land Acquisition Act (demo)"};
   var k1=new Khasra{Village=v,DisplayNumber="22//1",NormalizedNumber=KhasraNumber.Normalize("22//1"),TotalArea=4m,AreaUnit="Bigha"}; var k2=new Khasra{Village=v,DisplayNumber="22//2",NormalizedNumber=KhasraNumber.Normalize("22//2"),TotalArea=3.5m,AreaUnit="Bigha"}; var k3=new Khasra{Village=v,DisplayNumber="22//1/6",NormalizedNumber=KhasraNumber.Normalize("22//1/6"),TotalArea=1.25m,AreaUnit="Bigha"};
   var n4=new Notification{AcquisitionProject=p,SectionType="4",NotificationNumber="DEMO-S4-2026-001",NotificationDate=new DateOnly(2026,1,12)}; var n6=new Notification{AcquisitionProject=p,SectionType="6",NotificationNumber="DEMO-S6-2026-001",NotificationDate=new DateOnly(2026,3,4)}; var a=new Award{AcquisitionProject=p,AwardNumber="DEMO-AWARD-01",AwardDate=new DateOnly(2026,6,1),AwardType="Demo",Status="Published"}; var lr=new VillageLR{Village=v,RegisterReference="DEMO-LR-GAL-01"};
   db.AddRange(d,s,v,p,k1,k2,k3,n4,n6,a,lr,new NotificationKhasra{Notification=n4,Khasra=k2,NotifiedArea=2m,AreaUnit="Bigha"},new NotificationKhasra{Notification=n6,Khasra=k2,NotifiedArea=2m,AreaUnit="Bigha"},new AwardKhasra{Award=a,Khasra=k2,AcquiredArea=2m,AreaUnit="Bigha",AcquisitionStatus="Acquired"},new AwardKhasra{Award=a,Khasra=k3,AcquiredArea=1.25m,AreaUnit="Bigha",AcquisitionStatus="Acquired"},new LREntry{VillageLR=lr,RowNumber=1,RawKhasraText="22//2 min",Khasra=k2,RawAreaText="2 bigha",ParsedArea=2m,AreaUnit="Bigha",Section4NotificationId=n4.Id,Section6NotificationId=n6.Id,AwardId=a.Id,RawRemarks="Dummy training data",VerificationStatus=VerificationStatus.Verified}); await db.SaveChangesAsync(ct);
  }
  if(await db.KhatauniRecords.AnyAsync(x=>x.ReferenceNumber=="DEMO-KHATAUNI-2024",ct)) return;
  var village=await db.Villages.SingleAsync(x=>x.Name=="Galibpur",ct); var k2Existing=await db.Khasras.SingleAsync(x=>x.VillageId==village.Id&&x.NormalizedNumber==KhasraNumber.Normalize("22//2"),ct); var k3Existing=await db.Khasras.SingleAsync(x=>x.VillageId==village.Id&&x.NormalizedNumber==KhasraNumber.Normalize("22//1/6"),ct);
  var record=new KhatauniRecord{Village=village,ReferenceNumber="DEMO-KHATAUNI-2024",RecordYearText="2024",AsOfDate=new DateOnly(2024,1,1),EffectiveFrom=new DateOnly(2024,1,1),VerificationStatus=RevenueRecordVerificationStatus.Verified,Remarks="Fictional demonstration revenue record only."}; var khata=new Khata{KhatauniRecord=record,KhataNumber="DEMO-KHATA-145",RawKhataNumber="145"}; var ram=new Party{DisplayName="Demo Ram Singh",PartyType=PartyType.Individual,FatherOrSpouseName="Demo Parent"}; var shyam=new Party{DisplayName="Demo Shyam Singh",PartyType=PartyType.Individual,FatherOrSpouseName="Demo Parent"};
  db.AddRange(record,khata,ram,shyam,new KhataKhasra{Khata=khata,Khasra=k2Existing,RawKhasraText="22//2",RecordedArea=3.5m,RawAreaText="3.5 bigha",AreaUnit="Bigha"},new KhataKhasra{Khata=khata,Khasra=k3Existing,RawKhasraText="22//1/6",RecordedArea=1.25m,RawAreaText="1.25 bigha",AreaUnit="Bigha"},new KhataPartyShare{Khata=khata,Party=ram,RawShareText="1/2",ShareNumerator=1,ShareDenominator=2,VerificationStatus=RevenueRecordVerificationStatus.Verified},new KhataPartyShare{Khata=khata,Party=shyam,RawShareText="1/2",ShareNumerator=1,ShareDenominator=2,VerificationStatus=RevenueRecordVerificationStatus.Verified}); await db.SaveChangesAsync(ct);
 }
}
