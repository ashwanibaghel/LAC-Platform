using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed class NmWorkflowException(string message, int statusCode = 400) : Exception(message) { public int StatusCode { get; } = statusCode; }
public sealed record NmRowInput(int SourcePage, string SourceRow, string SourceRegionJson, string? RecordedPersonText, string? FatherOrSpouseText, string? RawShareText, string? RawAreaText, decimal? EntitlementAmount, string? EntitlementBasisText, IReadOnlyList<NmKhasraInput> Khasras);
public sealed record NmKhasraInput(string RawKhasraText, string? Qualifier, string? RawAreaText = null, string? RawShareText = null, string? SourceRegionJson = null);

public sealed class NmWorkflowService(LacDbContext db)
{
    public async Task<NmDocument> CreateReviewAsync(Guid documentId, Guid villageId, Guid? awardId, string? reference, DateOnly? date, CancellationToken ct)
    {
        if (!await db.Documents.AnyAsync(x => x.Id == documentId, ct)) throw new NmWorkflowException("NM source document was not found.", 404);
        if (!await db.Villages.AnyAsync(x => x.Id == villageId, ct)) throw new NmWorkflowException("Village was not found.", 404);
        if (awardId is not null && !await db.AwardVillages.AnyAsync(x => x.AwardId == awardId && x.VillageId == villageId, ct)) throw new NmWorkflowException("Award is not linked to the selected Village.");
        if (await db.NmDocuments.AnyAsync(x => x.DocumentId == documentId, ct)) throw new NmWorkflowException("This document already has an NM review.");
        var item = new NmDocument { DocumentId = documentId, VillageId = villageId, AwardId = awardId, ReferenceNumber = Clean(reference), RecordDate = date, Status = NmReviewStatus.Draft };
        db.Add(item); await db.SaveChangesAsync(ct); return item;
    }

    public async Task<NmReviewRow> AddReviewRowAsync(Guid nmDocumentId, NmRowInput input, CancellationToken ct)
    {
        var nm = await db.NmDocuments.SingleOrDefaultAsync(x => x.Id == nmDocumentId, ct) ?? throw new NmWorkflowException("NM review was not found.", 404);
        if (nm.Status == NmReviewStatus.Committed) throw new NmWorkflowException("Committed NM reviews cannot be changed.");
        if (input.SourcePage < 1 || string.IsNullOrWhiteSpace(input.SourceRow)) throw new NmWorkflowException("Source page and row are required.");
        var row = new NmReviewRow { NmDocumentId = nmDocumentId, SourcePage = input.SourcePage, SourceRow = input.SourceRow.Trim(), SourceRegionJson = input.SourceRegionJson ?? "{}", RecordedPersonText = Clean(input.RecordedPersonText), FatherOrSpouseText = Clean(input.FatherOrSpouseText), RawShareText = Clean(input.RawShareText), RawAreaText = Clean(input.RawAreaText), EntitlementAmount = input.EntitlementAmount, EntitlementBasisText = Clean(input.EntitlementBasisText) };
        foreach (var item in input.Khasras)
        {
            var raw = item.RawKhasraText?.Trim() ?? ""; if (raw.Length == 0) throw new NmWorkflowException("Each NM Khasra needs its raw source value.");
            var qualifier = Clean(item.Qualifier); var number = KhasraNumber.Normalize(RemoveQualifier(raw, qualifier));
            var matches = await db.Khasras.Where(x => x.VillageId == nm.VillageId && x.NormalizedNumber == number && x.Qualifier == qualifier).Select(x => x.Id).Take(2).ToListAsync(ct);
            row.Khasras.Add(new NmReviewKhasra { RawKhasraText = raw, NormalizedNumber = number, Qualifier = qualifier, SuggestedKhasraId = matches.Count == 1 ? matches[0] : null, Status = matches.Count == 1 ? NmReviewStatus.Draft : NmReviewStatus.NeedsReview, RawAreaText = Clean(item.RawAreaText), RawShareText = Clean(item.RawShareText), SourceRegionJson = item.SourceRegionJson });
        }
        db.Add(row); nm.Status = NmReviewStatus.NeedsReview; await db.SaveChangesAsync(ct); return row;
    }

    public async Task VerifyRowAsync(Guid rowId, string verifiedBy, NmRowInput corrected, CancellationToken ct)
    {
        var old = await db.NmReviewRows.Include(x => x.NmDocument).Include(x => x.Khasras).SingleOrDefaultAsync(x => x.Id == rowId, ct) ?? throw new NmWorkflowException("NM review row was not found.", 404);
        if (old.NmDocument.Status == NmReviewStatus.Committed) throw new NmWorkflowException("Committed NM reviews cannot be changed.");
        if (old.Khasras.Count != corrected.Khasras.Count) throw new NmWorkflowException("Changing the number of Khasras requires a new source-row review; existing evidence is retained.");
        old.RecordedPersonText = Clean(corrected.RecordedPersonText); old.FatherOrSpouseText = Clean(corrected.FatherOrSpouseText); old.RawShareText = Clean(corrected.RawShareText); old.RawAreaText = Clean(corrected.RawAreaText); old.EntitlementAmount = corrected.EntitlementAmount; old.EntitlementBasisText = Clean(corrected.EntitlementBasisText);
        for(var i=0;i<corrected.Khasras.Count;i++) { var input=corrected.Khasras[i]; var raw=input.RawKhasraText.Trim(); var q=Clean(input.Qualifier); var n=KhasraNumber.Normalize(RemoveQualifier(raw,q)); var match=await db.Khasras.SingleOrDefaultAsync(x=>x.VillageId==old.NmDocument.VillageId&&x.NormalizedNumber==n&&x.Qualifier==q,ct); if(match is null) throw new NmWorkflowException("Every confirmed NM Khasra must be an exact Village master match; digits and qualifiers are never repaired."); var link=old.Khasras.ElementAt(i); link.RawKhasraText=raw;link.NormalizedNumber=n;link.Qualifier=q;link.SuggestedKhasraId=match.Id;link.Status=NmReviewStatus.Verified;link.RawAreaText=Clean(input.RawAreaText);link.RawShareText=Clean(input.RawShareText);link.SourceRegionJson=input.SourceRegionJson; }
        if(string.IsNullOrWhiteSpace(old.RecordedPersonText)) throw new NmWorkflowException("Recorded person as per NM is required to confirm a row."); old.Status=NmReviewStatus.Verified; old.VerifiedBy=verifiedBy.Trim(); old.VerifiedAt=DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
    }

    public async Task<int> CommitAsync(Guid nmDocumentId, string committedBy, CancellationToken ct)
    {
        var nm=await db.NmDocuments.Include(x=>x.ReviewRows).ThenInclude(x=>x.Khasras).SingleOrDefaultAsync(x=>x.Id==nmDocumentId,ct) ?? throw new NmWorkflowException("NM review was not found.",404);
        var rows=nm.ReviewRows.Where(x=>x.Status==NmReviewStatus.Verified).ToList(); if(rows.Count==0) throw new NmWorkflowException("Verify at least one NM row before committing.");
        foreach(var row in rows) { var person=new NmRecordedPerson {NmDocumentId=nm.Id,DisplayNameAsRecorded=row.RecordedPersonText!,FatherOrSpouseAsRecorded=row.FatherOrSpouseText,SourceRow=row.SourceRow}; var entitlement=new NmEntitlement {NmDocumentId=nm.Id,NmRecordedPerson=person,AwardId=nm.AwardId,SourceRow=row.SourceRow,RawShareText=row.RawShareText,RawAreaText=row.RawAreaText,EntitlementAmount=row.EntitlementAmount,EntitlementBasisText=row.EntitlementBasisText}; db.Add(entitlement); foreach(var k in row.Khasras) { var link=new NmEntitlementKhasra {NmEntitlement=entitlement,KhasraId=k.SuggestedKhasraId!.Value,RawKhasraText=k.RawKhasraText,RawQualifier=k.Qualifier,RawAreaText=k.RawAreaText,RawShareText=k.RawShareText}; db.Add(link); db.Add(new SourceEvidence{DocumentId=nm.DocumentId,PageNumber=row.SourcePage,SourceRegionJson=k.SourceRegionJson??row.SourceRegionJson,FactName="NM recorded Khasra",ConfirmedValueJson=$"\"{k.RawKhasraText}\"",VerifiedAt=DateTimeOffset.UtcNow,VerifiedBy=committedBy,NmEntitlementKhasra=link}); } db.Add(new SourceEvidence{DocumentId=nm.DocumentId,PageNumber=row.SourcePage,SourceRegionJson=row.SourceRegionJson,FactName="NM recorded person",ConfirmedValueJson=$"\"{row.RecordedPersonText}\"",VerifiedAt=DateTimeOffset.UtcNow,VerifiedBy=committedBy,NmRecordedPerson=person}); db.Add(new SourceEvidence{DocumentId=nm.DocumentId,PageNumber=row.SourcePage,SourceRegionJson=row.SourceRegionJson,FactName="NM entitlement",ConfirmedValueJson=row.EntitlementAmount?.ToString()??"null",VerifiedAt=DateTimeOffset.UtcNow,VerifiedBy=committedBy,NmEntitlement=entitlement}); row.Status=NmReviewStatus.Committed; }
        nm.Status=NmReviewStatus.Committed; await db.SaveChangesAsync(ct); return rows.Count;
    }
    private static string? Clean(string? x)=>string.IsNullOrWhiteSpace(x)?null:x.Trim(); private static string RemoveQualifier(string x,string? q)=>q is null?x:x.EndsWith($" {q}",StringComparison.OrdinalIgnoreCase)?x[..^(q.Length+1)]:x;
}
