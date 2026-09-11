using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed class NmWorkflowException(string message, int statusCode = 400) : Exception(message) { public int StatusCode { get; } = statusCode; }
public sealed record NmRowInput(int SourcePage, string SourceRow, string SourceRegionJson, string? RecordedPersonText, string? FatherOrSpouseText, string? RawShareText, string? RawAreaText, decimal? EntitlementAmount, string? EntitlementBasisText, IReadOnlyList<NmKhasraInput> Khasras);
public sealed record NmKhasraInput(string RawKhasraText, string? Qualifier, string? RawAreaText = null, string? RawShareText = null, string? SourceRegionJson = null);
public sealed record NmKhasraSelection(Guid ReviewKhasraId, Guid? KhasraId, bool MarkUnreadable = false);

public sealed class NmWorkflowService(LacDbContext db, IDocumentStorage? storage = null)
{
    public async Task<NmDocument> UploadAsync(Stream content, string fileName, string? contentType, Guid awardId, Guid villageId, CancellationToken ct)
    {
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new NmWorkflowException("Choose an NM PDF.");
        if (!await db.AwardVillages.AnyAsync(x=>x.AwardId==awardId&&x.VillageId==villageId,ct)) throw new NmWorkflowException("Award and Village context is invalid.");
        var signature=new byte[4]; if(await content.ReadAsync(signature,ct)<4||signature[0]!='%'||signature[1]!='P'||signature[2]!='D'||signature[3]!='F') throw new NmWorkflowException("The uploaded file is not a valid PDF."); if(!content.CanSeek) throw new NmWorkflowException("The NM PDF stream cannot be stored safely."); content.Position=0;
        var documentStorage=storage??throw new NmWorkflowException("Local document storage is not configured.",500); var saved=await documentStorage.SaveAndHashAsync(content,fileName,ct); var document=await db.Documents.SingleOrDefaultAsync(x=>x.Sha256Hash==saved.Sha256Hash&&x.Status=="Active",ct);
        if(document is null){document=new Document{DocumentType="NM",OriginalFileName=Path.GetFileName(fileName),StoragePath=saved.StoragePath,Sha256Hash=saved.Sha256Hash,MimeType=string.IsNullOrWhiteSpace(contentType)?"application/pdf":contentType,FileSize=saved.FileSize};db.Add(document);} else await documentStorage.DeleteAsync(saved.StoragePath,ct);
        if(!await db.DocumentAwards.AnyAsync(x=>x.DocumentId==document.Id&&x.AwardId==awardId,ct))db.Add(new DocumentAward{Document=document,AwardId=awardId}); if(!await db.DocumentVillages.AnyAsync(x=>x.DocumentId==document.Id&&x.VillageId==villageId,ct))db.Add(new DocumentVillage{Document=document,VillageId=villageId}); await db.SaveChangesAsync(ct);
        return await CreateReviewAsync(document.Id,villageId,awardId,null,null,ct);
    }
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
            // OCR/source text is never a canonical decision. Even an exact-looking
            // value remains unresolved until the reviewer selects a Village Khasra.
            row.Khasras.Add(new NmReviewKhasra { RawKhasraText = raw, NormalizedNumber = number, Qualifier = qualifier, SuggestedKhasraId = null, Status = NmReviewStatus.NeedsReview, RawAreaText = Clean(item.RawAreaText), RawShareText = Clean(item.RawShareText), SourceRegionJson = item.SourceRegionJson });
        }
        db.Add(row); nm.Status = NmReviewStatus.NeedsReview; await db.SaveChangesAsync(ct); return row;
    }

    public async Task SelectKhasrasAsync(Guid rowId, string selectedBy, IReadOnlyList<NmKhasraSelection> selections, CancellationToken ct)
    {
        var row = await db.NmReviewRows.Include(x => x.NmDocument).Include(x => x.Khasras).SingleOrDefaultAsync(x => x.Id == rowId, ct) ?? throw new NmWorkflowException("NM review row was not found.", 404);
        if (row.NmDocument.Status == NmReviewStatus.Committed) throw new NmWorkflowException("Committed NM reviews cannot be changed.");
        if (string.IsNullOrWhiteSpace(selectedBy)) throw new NmWorkflowException("Reviewer name is required.");
        if (selections.Count != row.Khasras.Count || selections.Select(x => x.ReviewKhasraId).Distinct().Count() != row.Khasras.Count || selections.Any(x => row.Khasras.All(k => k.Id != x.ReviewKhasraId))) throw new NmWorkflowException("Select, clear, or mark every source Khasra fragment explicitly.");
        foreach (var selection in selections)
        {
            var link = row.Khasras.Single(x => x.Id == selection.ReviewKhasraId);
            if (selection.MarkUnreadable) { link.SuggestedKhasraId = null; link.Status = NmReviewStatus.Unreadable; continue; }
            if (selection.KhasraId is null) { link.SuggestedKhasraId = null; link.Status = NmReviewStatus.NeedsReview; continue; }
            if (!await db.Khasras.AnyAsync(x => x.Id == selection.KhasraId && x.VillageId == row.NmDocument.VillageId, ct)) throw new NmWorkflowException("Selected Khasra must belong to this NM Village.");
            link.SuggestedKhasraId = selection.KhasraId;
            link.Status = NmReviewStatus.Verified;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task VerifyRowAsync(Guid rowId, string verifiedBy, NmRowInput corrected, CancellationToken ct)
    {
        var old = await db.NmReviewRows.Include(x => x.NmDocument).Include(x => x.Khasras).SingleOrDefaultAsync(x => x.Id == rowId, ct) ?? throw new NmWorkflowException("NM review row was not found.", 404);
        if (old.NmDocument.Status == NmReviewStatus.Committed) throw new NmWorkflowException("Committed NM reviews cannot be changed.");
        if (old.Khasras.Count != corrected.Khasras.Count) throw new NmWorkflowException("Changing the number of Khasras requires a new source-row review; existing evidence is retained.");
        old.RecordedPersonText = Clean(corrected.RecordedPersonText); old.FatherOrSpouseText = Clean(corrected.FatherOrSpouseText); old.RawShareText = Clean(corrected.RawShareText); old.RawAreaText = Clean(corrected.RawAreaText); old.EntitlementAmount = corrected.EntitlementAmount; old.EntitlementBasisText = Clean(corrected.EntitlementBasisText);
        for(var i=0;i<corrected.Khasras.Count;i++) { var input=corrected.Khasras[i]; var raw=input.RawKhasraText.Trim(); var q=Clean(input.Qualifier); var n=KhasraNumber.Normalize(RemoveQualifier(raw,q)); var link=old.Khasras.ElementAt(i); if(link.SuggestedKhasraId is null || link.Status!=NmReviewStatus.Verified) throw new NmWorkflowException("Explicit human Khasra selection is required before a row can be verified."); link.RawKhasraText=raw;link.NormalizedNumber=n;link.Qualifier=q;link.RawAreaText=Clean(input.RawAreaText);link.RawShareText=Clean(input.RawShareText);link.SourceRegionJson=input.SourceRegionJson; }
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
