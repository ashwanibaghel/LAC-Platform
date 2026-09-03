using LAC.Domain;
using Microsoft.EntityFrameworkCore;
namespace LAC.Infrastructure;
public static class SeedData
{
 public static async Task SeedAsync(LacDbContext db, CancellationToken ct)
 {
  if (!await db.Districts.AnyAsync(ct))
  {
   var d=new District{Name="South West Delhi"}; var s=new SubDivision{District=d,Name="Matiala"}; var v=new Village{SubDivision=s,Name="GALIB PUR"}; var p=new AcquisitionProject{Name="Demo Corridor Acquisition",RequiringAgency="Demo Requiring Agency",ActRegime="Land Acquisition Act (demo)"};
   var k1=new Khasra{Village=v,DisplayNumber="22//1",NormalizedNumber=KhasraNumber.Normalize("22//1"),TotalArea=4m,AreaUnit="Bigha"}; var k2=new Khasra{Village=v,DisplayNumber="22//2",NormalizedNumber=KhasraNumber.Normalize("22//2"),TotalArea=3.5m,AreaUnit="Bigha"}; var k3=new Khasra{Village=v,DisplayNumber="22//1/6",NormalizedNumber=KhasraNumber.Normalize("22//1/6"),TotalArea=1.25m,AreaUnit="Bigha"};
   var n4=new Notification{AcquisitionProject=p,SectionType="4",NotificationNumber="DEMO-S4-2026-001",NotificationDate=new DateOnly(2026,1,12)}; var n6=new Notification{AcquisitionProject=p,SectionType="6",NotificationNumber="DEMO-S6-2026-001",NotificationDate=new DateOnly(2026,3,4)}; var a=new Award{AcquisitionProject=p,AwardNumber="DEMO-AWARD-01",AwardDate=new DateOnly(2026,6,1),AwardType="Demo",Status="Published"}; var lr=new VillageLR{Village=v,RegisterReference="DEMO-LR-GAL-01"};
   db.AddRange(d,s,v,p,k1,k2,k3,n4,n6,a,lr,new NotificationKhasra{Notification=n4,Khasra=k2,NotifiedArea=2m,AreaUnit="Bigha"},new NotificationKhasra{Notification=n6,Khasra=k2,NotifiedArea=2m,AreaUnit="Bigha"},new AwardKhasra{Award=a,Khasra=k2,AcquiredArea=2m,AreaUnit="Bigha",AcquisitionStatus="Acquired"},new AwardKhasra{Award=a,Khasra=k3,AcquiredArea=1.25m,AreaUnit="Bigha",AcquisitionStatus="Acquired"},new LREntry{VillageLR=lr,RowNumber=1,RawKhasraText="22//2 min",Khasra=k2,RawAreaText="2 bigha",ParsedArea=2m,AreaUnit="Bigha",Section4NotificationId=n4.Id,Section6NotificationId=n6.Id,AwardId=a.Id,RawRemarks="Dummy training data",VerificationStatus=VerificationStatus.Verified}); await db.SaveChangesAsync(ct);
  }
  await EnsureSouthWestVillageDirectoryAsync(db,ct);
  if(await db.KhatauniRecords.AnyAsync(x=>x.ReferenceNumber=="DEMO-KHATAUNI-2024",ct)) return;
  var village=await db.Villages.SingleAsync(x=>x.Name=="GALIB PUR",ct); var k2Existing=await db.Khasras.SingleAsync(x=>x.VillageId==village.Id&&x.NormalizedNumber==KhasraNumber.Normalize("22//2"),ct); var k3Existing=await db.Khasras.SingleAsync(x=>x.VillageId==village.Id&&x.NormalizedNumber==KhasraNumber.Normalize("22//1/6"),ct);
  var record=new KhatauniRecord{Village=village,ReferenceNumber="DEMO-KHATAUNI-2024",RecordYearText="2024",AsOfDate=new DateOnly(2024,1,1),EffectiveFrom=new DateOnly(2024,1,1),VerificationStatus=RevenueRecordVerificationStatus.Verified,Remarks="Fictional demonstration revenue record only."}; var khata=new Khata{KhatauniRecord=record,KhataNumber="DEMO-KHATA-145",RawKhataNumber="145"}; var ram=new Party{DisplayName="Demo Ram Singh",PartyType=PartyType.Individual,FatherOrSpouseName="Demo Parent"}; var shyam=new Party{DisplayName="Demo Shyam Singh",PartyType=PartyType.Individual,FatherOrSpouseName="Demo Parent"};
  db.AddRange(record,khata,ram,shyam,new KhataKhasra{Khata=khata,Khasra=k2Existing,RawKhasraText="22//2",RecordedArea=3.5m,RawAreaText="3.5 bigha",AreaUnit="Bigha"},new KhataKhasra{Khata=khata,Khasra=k3Existing,RawKhasraText="22//1/6",RecordedArea=1.25m,RawAreaText="1.25 bigha",AreaUnit="Bigha"},new KhataPartyShare{Khata=khata,Party=ram,RawShareText="1/2",ShareNumerator=1,ShareDenominator=2,VerificationStatus=RevenueRecordVerificationStatus.Verified},new KhataPartyShare{Khata=khata,Party=shyam,RawShareText="1/2",ShareNumerator=1,ShareDenominator=2,VerificationStatus=RevenueRecordVerificationStatus.Verified}); await db.SaveChangesAsync(ct);
 }
 private static async Task EnsureSouthWestVillageDirectoryAsync(LacDbContext db, CancellationToken ct)
 {
  var district=await db.Districts.SingleAsync(x=>x.Name=="South West Delhi",ct); var legacy=await db.SubDivisions.SingleOrDefaultAsync(x=>x.DistrictId==district.Id&&x.Name=="Demo Sub-Division",ct); var matiala=await db.SubDivisions.SingleOrDefaultAsync(x=>x.DistrictId==district.Id&&x.Name=="Matiala",ct);
  if(legacy is not null&&matiala is not null){await db.Villages.Where(x=>x.SubDivisionId==legacy.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(x=>x.SubDivisionId,matiala.Id),ct);await db.SubDivisions.Where(x=>x.Id==legacy.Id).ExecuteDeleteAsync(ct);}
  else if(legacy is not null){legacy.Name="Matiala";await db.SaveChangesAsync(ct);}
  var legacyGalib=await db.Villages.SingleOrDefaultAsync(x=>x.Name=="Galibpur"||x.Name=="Galib Pur",ct); if(legacyGalib is not null){legacyGalib.Name="GALIB PUR";await db.SaveChangesAsync(ct);}
  var villagesBySubdivision=new Dictionary<string,string[]> {
   ["Matiala"]=["Ambar Hai","Sarangpur","Guman Hera","Darya Pur Khurd","Rawta","Devrala","Pindwala Kalan","Pindwala Khurd","Khar Khari Jatmal","Khar Khari Rond","Jhatikra","Ragho Pur","Nanak Heri","Badu Sarai","Shikar Pur","Asalat Pur Khawad","Jain Pur","Hasanpur","Daulat Pur","Rewla Khan Pur","Paprawat","Goela Khurd","Taj Pur Khurd","Quiba Pur","Chhawla","Kangan Heri","Jhul Jhuli","Kakrola","Khera Dabar","Dindar Pur","Luhar Heri","Sahupura","Sher Pur Dairy","Galib Pur","Khar Khari Nahar","Matiala","Nangli Sakrawati","Pochan Pur"],
   ["Bijwasan"]=["Bharthal","Bijwasan","Dhool Siras","Rangpuri","Salah Pur","Bagdola","Bamnoli","Kapas Hera","Mahipalpur","Nangal Dewat","Sahbad Mohd","Samalka","Toganpur"],
   ["Najafgarh"]=["Roshan Pura","Dichaon Kalan","Jharoda Kalan","Surakh Pur","Mitraun","Khaira","Surhera","Kair","Ujwa","Jafar Pur Kalan","Malik Pur Zer N Garh","Mundhela Kalan","Mundhela Khurd","Samas Pur Khalsalssa Pur","Issa Pur","Qazi Pur","Baqar Garh","Dhansa","Najafgarh","Haibat Pur","Masuda Bad"],
   ["Dwarka"]=["Sagarpur","Palam","Mirza Pur","Nasir Pur"] };
  var subdivisions=await db.SubDivisions.Where(x=>x.DistrictId==district.Id).ToListAsync(ct);
  foreach(var name in villagesBySubdivision.Keys) if(subdivisions.All(x=>x.Name!=name)){var subdivision=new SubDivision{DistrictId=district.Id,Name=name};db.SubDivisions.Add(subdivision);subdivisions.Add(subdivision);}
  await db.SaveChangesAsync(ct);
  var subdivisionIds=subdivisions.Select(x=>x.Id).ToList(); var existingVillageKeys=(await db.Villages.Where(x=>subdivisionIds.Contains(x.SubDivisionId)).Select(x=>new{x.SubDivisionId,x.Name}).ToListAsync(ct)).Select(x=>$"{x.SubDivisionId}:{x.Name}").ToHashSet();
  foreach(var (name,villages) in villagesBySubdivision){var subdivision=subdivisions.Single(x=>x.Name==name);foreach(var villageName in villages){var officialName=villageName.ToUpperInvariant();if(existingVillageKeys.Add($"{subdivision.Id}:{officialName}"))db.Villages.Add(new Village{SubDivisionId=subdivision.Id,Name=officialName});}}
  await db.SaveChangesAsync(ct);
 }
}
