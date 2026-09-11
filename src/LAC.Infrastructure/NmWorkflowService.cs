using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LAC.Infrastructure;

public sealed class NmWorkflowException(string message, int statusCode = 400) : Exception(message) { public int StatusCode { get; } = statusCode; }
public sealed record NmRowInput(int SourcePage, string SourceRow, string SourceRegionJson, string? RecordedPersonText, string? FatherOrSpouseText, string? RawShareText, string? RawAreaText, decimal? EntitlementAmount, string? EntitlementBasisText, IReadOnlyList<NmKhasraInput> Khasras);
public sealed record NmKhasraInput(string RawKhasraText, string? Qualifier, string? RawAreaText = null, string? RawShareText = null, string? SourceRegionJson = null);
public sealed record NmKhasraSelection(Guid ReviewKhasraId, Guid? KhasraId, bool MarkUnreadable = false);

public sealed class NmWorkflowService(LacDbContext db, IDocumentStorage? storage = null, ILocalDocumentIntelligenceClient? intelligence = null)
{
    public async Task<NmSemanticAnalysisSession> AnalyzeSemanticAsync(Guid nmDocumentId, CancellationToken ct)
    {
        var nm = await db.NmDocuments.Include(x => x.Document).SingleOrDefaultAsync(x => x.Id == nmDocumentId, ct) ?? throw new NmWorkflowException("NM review was not found.", 404);
        var pages = new[] { 1, 4, 10, 16, 20, 26, 30, 40, 50, 60, 70, 75 };
        var session = new NmSemanticAnalysisSession { NmDocumentId = nm.Id, ParserVersion = "nm-semantic-v1", SourcePagesJson = JsonSerializer.Serialize(pages) };
        db.NmSemanticAnalysisSessions.Add(session); await db.SaveChangesAsync(ct);
        var documentStorage = storage ?? throw new NmWorkflowException("Local document storage is not configured.", 500);
        var client = intelligence ?? throw new NmWorkflowException("Local NM intelligence is not configured.", 503);
        var localPdf = Path.Combine(Path.GetTempPath(), "lac-nm-semantic-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            await using (var source = await documentStorage.OpenReadAsync(nm.Document.StoragePath, ct) ?? throw new NmWorkflowException("The locally stored NM PDF is unavailable.", 404))
            await using (var target = File.Create(localPdf)) await source.CopyToAsync(target, ct);
            var result = await client.RunAsync(new(1, nm.DocumentId, localPdf, nm.AwardId ?? Guid.Empty, nm.VillageId, pages, false, true), ct);
            if (result.Status != "Completed") throw new NmWorkflowException("Local NM semantic analysis did not complete.", 503);
            foreach (var candidate in result.Candidates.Where(x => x.CandidateType == "NmSemanticOwnerBlock")) { await PersistSemanticOwnerAsync(session, nm, candidate, ct); await db.SaveChangesAsync(ct); }
            foreach (var candidate in result.Candidates.Where(x => x.CandidateType == "NmSemanticException"))
            {
                var owner = new NmSemanticOwnerBlock { AnalysisSessionId = session.Id, SourceSequence = session.OwnerBlocks.Count + 1, PageStart = candidate.Page, PageEnd = candidate.Page, Status = NmSemanticBlockStatus.Exception, SourceRegionJson = candidate.SourceRegion?.GetRawText() ?? "{}", ValidationSummaryJson = "[\"semantic page exception\"]" };
                var payload = candidate.StructuredPayload;
                owner.Exceptions.Add(new NmSemanticException { Reason = payload.GetProperty("reason").GetString() ?? "MissingRequiredSourceEvidence", SourcePage = candidate.Page, SourceRegionJson = candidate.SourceRegion?.GetRawText(), Detail = payload.GetProperty("detail").GetString() ?? "Worker could not safely form an owner block." });
                db.NmSemanticOwnerBlocks.Add(owner); session.OwnerBlocks.Add(owner);
            }
            session.AutoStructuredCount = session.OwnerBlocks.Count(x => x.Status == NmSemanticBlockStatus.AutoStructured);
            session.ExceptionCount = session.OwnerBlocks.Count(x => x.Status == NmSemanticBlockStatus.Exception);
            session.DiagnosticsJson = JsonSerializer.Serialize(new { result.PagesProcessed, result.Warnings, candidateCount = result.Candidates.Count });
            session.Status = NmSemanticSessionStatus.Completed; session.CompletedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); return session;
        }
        catch { session.Status = NmSemanticSessionStatus.Failed; session.CompletedAt = DateTimeOffset.UtcNow; try { await db.SaveChangesAsync(CancellationToken.None); } catch { } throw; }
        finally { try { File.Delete(localPdf); } catch { } }
    }

    private async Task PersistSemanticOwnerAsync(NmSemanticAnalysisSession session, NmDocument nm, LocalDocumentIntelligenceCandidate candidate, CancellationToken ct)
    {
        var payload = candidate.StructuredPayload;
        var fields = payload.TryGetProperty("fieldSources", out var sources) ? sources : default;
        var nameSource = Source(fields, "recordedName");
        var owner = new NmSemanticOwnerBlock { AnalysisSessionId = session.Id, SourceSequence = session.OwnerBlocks.Count + 1, PageStart = candidate.Page, PageEnd = candidate.Page, RecordedNameRaw = StringValue(payload, "recordedNameRaw"), FatherOrSpouseRaw = StringValue(payload, "fatherOrSpouseRaw"), ResidenceRaw = StringValue(payload, "residenceRaw"), ShareRaw = StringValue(payload, "shareRaw"), Status = NmSemanticBlockStatus.Exception, SourceRegionJson = Region(nameSource) ?? "{}", FieldSourcesJson = fields.ValueKind == JsonValueKind.Undefined ? "{}" : fields.GetRawText() };
        var group = new NmSemanticParcelGroup { SourceSequence = 1 };
        var sequence = 0;
        foreach (var parcel in payload.GetProperty("parcels").EnumerateArray())
        {
            var raw = StringValue(parcel, "rawKhasraText"); var qualifier = raw?.Trim().EndsWith(" min", StringComparison.OrdinalIgnoreCase) == true ? "min" : null; var normalized = raw is null ? null : KhasraNumber.Normalize(RemoveQualifier(raw, qualifier));
            var khasraSource = Source(parcel, "khasraSource"); var areaSource = Source(parcel, "areaSource"); var classSource = Source(parcel, "landClassSource");
            var entry = new NmSemanticParcelEntry { SourceSequence = ++sequence, RawKhasraText = raw, NormalizedKhasraText = normalized, Qualifier = qualifier, RawAreaText = StringValue(parcel, "rawAreaText"), LandClassRaw = StringValue(parcel, "landClassRaw"), SourcePage = Page(khasraSource, candidate.Page), SourceRegionJson = Region(khasraSource) ?? "{}", KhasraSourceRegionJson = Region(khasraSource), AreaSourceRegionJson = Region(areaSource), LandClassSourceRegionJson = Region(classSource), IsInherited = parcel.TryGetProperty("isInherited", out var inherited) && inherited.GetBoolean(), ValidationState = "Exception" };
            if (string.IsNullOrWhiteSpace(raw) || khasraSource.ValueKind == JsonValueKind.Undefined) AddException(owner, "MissingRequiredSourceEvidence", "Khasra", candidate, "Khasra value or its individual source region is missing.");
            else if (string.IsNullOrWhiteSpace(normalized)) AddException(owner, "IncompleteKhasra", "Khasra", candidate, "Source did not contain a complete Khasra identity.");
            else { var matches = await db.Khasras.Where(x => x.VillageId == nm.VillageId && x.NormalizedNumber == normalized && x.Qualifier == qualifier).Select(x => x.Id).ToListAsync(ct); if (matches.Count == 1) { entry.ExactKhasraCandidateId = matches[0]; entry.ValidationState = "ExactSourceMasterMatch"; } else { var numberExists = await db.Khasras.AnyAsync(x => x.VillageId == nm.VillageId && x.NormalizedNumber == normalized, ct); AddException(owner, numberExists ? "QualifierConflict" : "KhasraNotInVillageMaster", "Khasra", candidate, "No exact Village-scoped source match was found."); } }
            if (!entry.IsInherited && (string.IsNullOrWhiteSpace(entry.RawAreaText) || areaSource.ValueKind == JsonValueKind.Undefined)) AddException(owner, "MissingRequiredSourceEvidence", "Area", candidate, "Area value or its individual source region is missing.");
            group.Entries.Add(entry);
        }
        owner.ParcelGroups.Add(group);
        // Explicitly mark the staging graph as new. The session was saved before
        // worker invocation, so relationship fixup alone must not imply Update.
        db.NmSemanticOwnerBlocks.Add(owner); session.OwnerBlocks.Add(owner);
        if (nameSource.ValueKind == JsonValueKind.Undefined || Source(fields, "share").ValueKind == JsonValueKind.Undefined) AddException(owner, "MissingRequiredSourceEvidence", "OwnerOrShare", candidate, "Owner name and share must each retain individual source provenance.");
        foreach (var component in payload.GetProperty("components").EnumerateObject()) { var value = component.Value; var source = Source(value, "source"); if (source.ValueKind == JsonValueKind.Undefined) AddException(owner, "MissingRequiredSourceEvidence", component.Name, candidate, "Compensation component lacks an individual source region."); owner.CompensationComponents.Add(new NmSemanticCompensationComponent { ComponentType = component.Name, RawAmountText = StringValue(value, "rawAmountText") ?? "", SourceSequence = owner.CompensationComponents.Count + 1, SourcePage = Page(source, candidate.Page), SourceRegionJson = Region(source) ?? "{}", SemanticState = source.ValueKind == JsonValueKind.Undefined ? "Exception" : "MappedByColumn" }); }
        var relations = payload.TryGetProperty("relations", out var relationItems) && relationItems.ValueKind == JsonValueKind.Array ? relationItems.EnumerateArray().ToArray() : [];
        foreach (var relation in relations)
        {
            var marker = Source(relation, "marker"); var previous = session.OwnerBlocks.LastOrDefault(x => x.PageStart == candidate.Page && x.ParcelGroups.Any());
            if (marker.ValueKind == JsonValueKind.Undefined || previous?.ParcelGroups.SingleOrDefault() is not { } original) { AddException(owner, "AmbiguousDittoScope", "ParcelRelation", candidate, "The ditto marker could not be tied to a prior explicit parcel group."); continue; }
            db.NmSemanticSourceRelations.Add(new NmSemanticSourceRelation { OwnerBlock = owner, RelationType = StringValue(relation, "relationType") ?? "ExplicitSourceReference", RelatedParcelGroup = original, SourcePage = Page(marker, candidate.Page), SourceRegionJson = Region(marker)!, RawSourceText = StringValue(marker, "rawSourceText") ?? "", OriginalSourcePage = original.Entries.FirstOrDefault()?.SourcePage, OriginalSourceRegionJson = original.Entries.FirstOrDefault()?.KhasraSourceRegionJson, OriginalSourceSequence = previous.SourceSequence });
        }
        foreach (var reason in payload.GetProperty("exceptions").EnumerateArray()) AddException(owner, reason.GetString() ?? "MissingRequiredSourceEvidence", null, candidate, "Worker semantic validation exception.");
        var requested = StringValue(payload, "status");
        owner.Status = requested == "AutoStructured" && owner.Exceptions.Count == 0 && group.Entries.Count > 0 && group.Entries.All(x => x.ValidationState == "ExactSourceMasterMatch" && !string.IsNullOrWhiteSpace(x.KhasraSourceRegionJson) && !string.IsNullOrWhiteSpace(x.AreaSourceRegionJson)) && (!group.Entries.Any(x => x.IsInherited) || db.ChangeTracker.Entries<NmSemanticSourceRelation>().Any(x => x.Entity.OwnerBlock == owner && (x.Entity.RelatedParcelGroupId != null || x.Entity.RelatedParcelGroup != null))) ? NmSemanticBlockStatus.AutoStructured : NmSemanticBlockStatus.Exception;
        owner.ValidationSummaryJson = JsonSerializer.Serialize(new { requested, owner.Status, exceptions = owner.Exceptions.Select(x => x.Reason) });
    }
    private static JsonElement Source(JsonElement value, string name) => value.ValueKind != JsonValueKind.Undefined && value.TryGetProperty(name, out var item) && item.ValueKind != JsonValueKind.Null ? item : default;
    private static string? Region(JsonElement source) => source.ValueKind != JsonValueKind.Undefined && source.TryGetProperty("sourceRegion", out var region) ? region.GetRawText() : null;
    private static int Page(JsonElement source, int fallback) => source.ValueKind != JsonValueKind.Undefined && source.TryGetProperty("page", out var page) ? page.GetInt32() : fallback;
    private static string? StringValue(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind != JsonValueKind.Null ? item.GetString() : null;
    private static void AddException(NmSemanticOwnerBlock owner, string reason, string? field, LocalDocumentIntelligenceCandidate candidate, string detail) => owner.Exceptions.Add(new NmSemanticException { Reason = reason, FieldName = field, SourcePage = candidate.Page, SourceRegionJson = candidate.SourceRegion?.GetRawText(), Detail = detail });
    public async Task<int> AnalyzePilotAsync(Guid nmDocumentId, CancellationToken ct)
    {
        var nm = await db.NmDocuments.Include(x => x.Document).SingleOrDefaultAsync(x => x.Id == nmDocumentId, ct)
            ?? throw new NmWorkflowException("NM review was not found.", 404);
        if (nm.Status == NmReviewStatus.Committed) throw new NmWorkflowException("Committed NM reviews cannot be analyzed.");
        if (await db.NmReviewRows.AnyAsync(x => x.NmDocumentId == nm.Id, ct)) throw new NmWorkflowException("This NM pilot already has review rows. Review those rows instead of duplicating source evidence.");
        var documentStorage = storage ?? throw new NmWorkflowException("Local document storage is not configured.", 500);
        var client = intelligence ?? throw new NmWorkflowException("Local NM intelligence is not configured.", 503);
        await using var content = await documentStorage.OpenReadAsync(nm.Document.StoragePath, ct) ?? throw new NmWorkflowException("The locally stored NM PDF is unavailable.", 404);
        var localPdf = Path.Combine(Path.GetTempPath(), "lac-nm-pilot-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            await using (var target = File.Create(localPdf)) await content.CopyToAsync(target, ct);
            // Representative spread across the register; never a full-document run.
            var pages = new[] { 1,4,7,10,13,16,20,23,26,30,33,36,40,43,46,50,53,56,60,63,66,70,73,75 };
            var result = await client.RunAsync(new(1, nm.DocumentId, localPdf, nm.AwardId ?? Guid.Empty, nm.VillageId, pages, true), ct);
            if (result.Status != "Completed") throw new NmWorkflowException("Local NM analysis did not complete.", 503);
            var rows = result.Candidates.Where(x => x.CandidateType == "NmReviewRow").Take(30).ToList();
            foreach (var candidate in rows)
            {
                var p = candidate.StructuredPayload;
                var sourceRow = p.GetProperty("sourceRow").GetString() ?? $"pilot-{candidate.Page}";
                var person = p.GetProperty("recordedPersonText").GetString();
                var area = p.TryGetProperty("rawAreaText", out var a) && a.ValueKind != System.Text.Json.JsonValueKind.Null ? a.GetString() : null;
                var share = p.TryGetProperty("rawShareText", out var s) && s.ValueKind != System.Text.Json.JsonValueKind.Null ? s.GetString() : null;
                decimal? amount = p.TryGetProperty("entitlementAmount", out var amountElement) && decimal.TryParse(amountElement.GetString(), out var parsedAmount) ? parsedAmount : null;
                var khasras = p.GetProperty("khasras").EnumerateArray().Select(k => new NmKhasraInput(k.GetProperty("rawKhasraText").GetString() ?? "OCR fragment", k.TryGetProperty("qualifier", out var q) && q.ValueKind != System.Text.Json.JsonValueKind.Null ? q.GetString() : null, area, share, candidate.SourceRegion?.GetRawText())).ToList();
                await AddReviewRowAsync(nm.Id, new(candidate.Page, sourceRow, candidate.SourceRegion?.GetRawText() ?? "{}", person, null, share, area, amount, null, khasras), ct);
            }
            foreach (var fragment in result.Candidates.Where(x => x.CandidateType == "UnassignedSourceFragment"))
                db.NmReviewFragments.Add(new NmReviewFragment { NmDocumentId = nm.Id, SourcePage = fragment.Page, SourceRegionJson = fragment.SourceRegion?.GetRawText() ?? "{}", RawOcrText = fragment.RawOcr ?? fragment.RawSourceText ?? "" });
            await db.SaveChangesAsync(ct);
            return rows.Count;
        }
        finally { try { File.Delete(localPdf); } catch { } }
    }
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
