using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed record VerifyExtractedFactRequest(string VerifiedBy, string? CorrectedPayloadJson, string Action = "Confirm");
public sealed record VerifyAwardKhasraFieldRequest(string VerifiedBy, string FieldRole, string? HumanValue, string Decision);
public sealed record ConfirmExactRequest(string VerifiedBy, int ExpectedCount, int? SourcePage = null);
public sealed record CommitVerifiedRequest(string VerifiedBy,int ExpectedCount);
public sealed record ReviewContextRequest(Guid AwardId, Guid VillageId, string VerifiedBy);

public sealed partial class AwardIngestionService
{
    public async Task SetReviewContextAsync(Guid sessionId, ReviewContextRequest request, CancellationToken ct)
    {
        RequireReviewer(request.VerifiedBy);
        var session = await db.AwardIngestionSessions.Include(x=>x.Candidates).SingleOrDefaultAsync(x=>x.Id==sessionId,ct) ?? throw new AwardIngestionException("Review not found.",404);
        if(session.Candidates.Any(x=>x.VerifiedAt!=null || x.Status==AwardIngestionCandidateStatus.Committed)) throw new AwardIngestionException("This review already contains verified records; its context cannot be changed.",409);
        if(!await db.AwardVillages.AnyAsync(x=>x.AwardId==request.AwardId && x.VillageId==request.VillageId,ct)) throw new AwardIngestionException("Choose a Village linked to this Award.");
        if(session.SourceDocumentId is null) throw new AwardIngestionException("This review has no source document.");
        var old=JsonSerializer.Serialize(new {session.TargetAwardId,session.SelectedVillageId},Json);
        session.TargetAwardId=request.AwardId; session.SelectedVillageId=request.VillageId;
        foreach(var row in session.Candidates.Where(x=>x.Status!=AwardIngestionCandidateStatus.Skipped && x.Status!=AwardIngestionCandidateStatus.Rejected))
        {
            var checkedRow=await AnalyzeAsync(session,DeserializeInput(new(row.CandidateType,row.StructuredPayloadJson)),row.Sequence,ct);
            if(row.Status!=AwardIngestionCandidateStatus.Conflict && row.Status!=AwardIngestionCandidateStatus.DuplicateInBatch) row.Status=checkedRow.Status;
            row.CanonicalEntityId=checkedRow.CanonicalEntityId;row.CanonicalEntityType=checkedRow.CanonicalEntityType;
            row.ValidationIssuesJson=checkedRow.ValidationIssuesJson;
            if(row.Status==AwardIngestionCandidateStatus.Ready && EvidenceRequiresReview(row.SourceLocatorJson,out var warning)){row.Status=AwardIngestionCandidateStatus.NeedsReview;row.ValidationIssuesJson=JsonSerializer.Serialize(new[]{warning},Json);}
            await RefreshEvidenceMetadataAsync(row,ct);
        }
        if(!await db.DocumentAwards.AnyAsync(x=>x.DocumentId==session.SourceDocumentId && x.AwardId==request.AwardId,ct)) db.DocumentAwards.Add(new(){DocumentId=session.SourceDocumentId.Value,AwardId=request.AwardId});
        var jobs=await db.AwardDocumentExtractionJobs.Where(x=>x.IngestionSessionId==session.Id).ToListAsync(ct);
        foreach(var job in jobs){job.TargetAwardId=request.AwardId;job.SelectedVillageId=request.VillageId;}
        db.AuditLogs.Add(new(){EntityType=nameof(AwardIngestionSession),EntityId=session.Id,Action="ReviewContextConfirmed",ChangedBy=request.VerifiedBy.Trim(),OldValues=old,NewValues=JsonSerializer.Serialize(new{session.TargetAwardId,session.SelectedVillageId},Json)});
        await db.SaveChangesAsync(ct);
    }
    public async Task<IngestionCommitResult> CommitVerifiedAsync(Guid sessionId,CommitVerifiedRequest request,CancellationToken ct)
    {
        RequireReviewer(request.VerifiedBy);
        var ids=await db.AwardIngestionCandidates.Where(x=>x.SessionId==sessionId && x.VerifiedAt!=null && x.Status==AwardIngestionCandidateStatus.Ready).Select(x=>x.Id).ToListAsync(ct);
        if(ids.Count==0 || ids.Count!=request.ExpectedCount) throw new AwardIngestionException("Verified selection changed. Refresh before committing.",409);
        return await CommitAsync(sessionId,ids,request.VerifiedBy,ct);
    }
    private async Task RefreshEvidenceMetadataAsync(AwardIngestionCandidate row,CancellationToken ct)
    {
        try {using var locator=JsonDocument.Parse(row.SourceLocatorJson??"{}"); if(locator.RootElement.TryGetProperty("Page",out var p) || locator.RootElement.TryGetProperty("page",out p)) row.SourcePage=p.GetInt32();} catch(JsonException){row.SourcePage=null;}
        row.SafeToConfirm=await IsSafeExactAsync(row,ct);
    }
    private static bool AwardAreasDisagree(AwardKhasra link, AwardKhasraCandidate p) =>
        Different(link.RecordedTotalAreaBigha,p.RecordedAreaBigha) || Different(link.RecordedTotalAreaBiswa,p.RecordedAreaBiswa) || Different(link.RecordedTotalAreaBiswansi,p.RecordedAreaBiswansi) ||
        Different(link.AwardedAreaBigha,p.AwardedAreaBigha) || Different(link.AwardedAreaBiswa,p.AwardedAreaBiswa) || Different(link.AwardedAreaBiswansi,p.AwardedAreaBiswansi);
    private static bool Different<T>(T? a,T? b) where T:struct => a.HasValue && b.HasValue && !a.Value.Equals(b.Value);

    public async Task<object> GetReviewOverviewAsync(Guid sessionId,CancellationToken ct)
    {
        var session = await db.AwardIngestionSessions.AsNoTracking().Where(x=>x.Id==sessionId).Select(x=>new {x.Id,x.TargetAwardId,x.SelectedVillageId,x.SourceDocumentId,DocumentName=x.SourceDocument==null?null:x.SourceDocument.OriginalFileName,AwardNumber=x.TargetAward==null?null:x.TargetAward.AwardNumber,VillageName=x.SelectedVillage==null?null:x.SelectedVillage.Name}).SingleOrDefaultAsync(ct) ?? throw new AwardIngestionException("Review not found.",404);
        var q=db.AwardIngestionCandidates.AsNoTracking().Where(x=>x.SessionId==sessionId);
        var sections=await q.GroupBy(x=>new{x.CandidateType,x.Status,x.SafeToConfirm,Verified=x.VerifiedAt!=null}).Select(g=>new{g.Key.CandidateType,g.Key.Status,g.Key.SafeToConfirm,g.Key.Verified,Count=g.Count()}).ToListAsync(ct);
        var pages=await q.Where(x=>x.CandidateType==AwardIngestionCandidateType.AwardKhasra).GroupBy(x=>x.SourcePage).Select(g=>new{Page=g.Key,Count=g.Count(),Exact=g.Count(x=>x.SafeToConfirm && x.VerifiedAt==null && x.Status==AwardIngestionCandidateStatus.Ready)}).ToListAsync(ct);
        var job=await db.AwardDocumentExtractionJobs.AsNoTracking().Where(x=>x.IngestionSessionId==sessionId).OrderByDescending(x=>x.CreatedAt).Select(x=>new{x.Status,x.TotalPages,x.ProcessedPages}).FirstOrDefaultAsync(ct);
        return new {session.Id,session.TargetAwardId,session.SelectedVillageId,session.SourceDocumentId,session.DocumentName,session.AwardNumber,session.VillageName,AnalysisStatus=job?.Status,TotalPages=job?.TotalPages,ProcessedPages=job?.ProcessedPages,Sections=sections,Pages=pages};
    }

    public async Task<int> ConfirmExactAsync(Guid sessionId,ConfirmExactRequest request,CancellationToken ct)
    {
        RequireReviewer(request.VerifiedBy);
        var rows=await db.AwardIngestionCandidates.Include(x=>x.Session).Where(x=>x.SessionId==sessionId && x.SafeToConfirm && x.VerifiedAt==null && x.Status==AwardIngestionCandidateStatus.Ready && (request.SourcePage==null || x.SourcePage==request.SourcePage)).ToListAsync(ct);
        if(rows.Count==0 || rows.Count!=request.ExpectedCount) throw new AwardIngestionException("The exact-match group changed. Refresh its summary before confirming.",409);
        // Validate the complete selection before changing any candidate. No partial safe batch.
        foreach(var row in rows) if(!await IsSafeExactAsync(row,ct)) throw new AwardIngestionException("A row no longer qualifies as a safe exact match. Nothing was confirmed.",409);
        foreach(var row in rows) MarkVerified(row,request.VerifiedBy,row.StructuredPayloadJson,"ConfirmExactGroup");
        await db.SaveChangesAsync(ct); return rows.Count;
    }

    public async Task VerifyFactAsync(Guid candidateId,VerifyExtractedFactRequest request,CancellationToken ct)
    {
        RequireReviewer(request.VerifiedBy);
        var row=await db.AwardIngestionCandidates.Include(x=>x.Session).SingleOrDefaultAsync(x=>x.Id==candidateId,ct) ?? throw new AwardIngestionException("Review item not found.",404);
        if(row.Status is AwardIngestionCandidateStatus.Committed or AwardIngestionCandidateStatus.Skipped or AwardIngestionCandidateStatus.Rejected) throw new AwardIngestionException("This item is finalised.");
        if(row.CandidateType==AwardIngestionCandidateType.AwardKhasra && row.Session.SourceDocumentId is not null && HasGeometryFieldEvidence(row.SourceLocatorJson)) throw new AwardIngestionException("Review geometry-backed Award Khasra fields individually; one field cannot verify the entire row.");
        if(request.Action=="Skip") {row.Status=AwardIngestionCandidateStatus.Skipped; row.SafeToConfirm=false; db.AuditLogs.Add(new(){EntityType=nameof(AwardIngestionCandidate),EntityId=row.Id,Action="HumanSkipped",ChangedBy=request.VerifiedBy}); await db.SaveChangesAsync(ct);return;}
        if(request.Action=="Uncertain") {row.Status=AwardIngestionCandidateStatus.NeedsReview; row.SafeToConfirm=false; db.AuditLogs.Add(new(){EntityType=nameof(AwardIngestionCandidate),EntityId=row.Id,Action="HumanMarkedUncertain",ChangedBy=request.VerifiedBy}); await db.SaveChangesAsync(ct);return;}
        if(request.Action is not "Confirm" and not "LinkExisting" and not "KeepDetected") throw new AwardIngestionException("Unsupported verification action.");
        if(row.Session.TargetAwardId is null || row.Session.SourceDocumentId is null) throw new AwardIngestionException("An Award and source document must be selected first.");
        var json=request.CorrectedPayloadJson ?? row.StructuredPayloadJson;
        IAwardIngestionCandidatePayload payload;
        try { payload=DeserializeInput(new(row.CandidateType,json)); }
        catch(JsonException) { throw new AwardIngestionException("The corrected values are not valid structured data."); }
        ValidateReviewPayload(payload);
        if(payload is AwardVillageCandidate village && !await db.Villages.AnyAsync(x=>x.Id==row.Session.SelectedVillageId && x.Name==village.VillageName,ct)) throw new AwardIngestionException("Select the official Village spelling confirmed for this review.");
        var checkedRow=await AnalyzeAsync(row.Session,payload,row.Sequence,ct);
        if(checkedRow.Status is AwardIngestionCandidateStatus.Invalid or AwardIngestionCandidateStatus.Ambiguous or AwardIngestionCandidateStatus.Conflict) throw new AwardIngestionException("This value conflicts with existing records. Correct it or retain the existing value before confirmation.");
        if(payload is AwardKhasraCandidate k)
        {
            var identity=BatchIdentity(k);
            var otherVerified=await db.AwardIngestionCandidates.Where(x=>x.SessionId==row.SessionId && x.Id!=row.Id && x.CandidateType==row.CandidateType && x.VerifiedAt!=null && x.Status!=AwardIngestionCandidateStatus.Skipped).ToListAsync(ct);
            if(otherVerified.Any(x=>BatchIdentity(Deserialize<AwardKhasraCandidate>(x))==identity && JsonSerializer.Serialize(Deserialize<AwardKhasraCandidate>(x),Json)!=JsonSerializer.Serialize(k,Json)))
                throw new AwardIngestionException("Another human-verified row has this Khasra identity with different values. Resolve that conflict before confirming.");
            if(row.Session.SelectedVillageId is null) throw new AwardIngestionException("Confirm the Award Village first.");
            var match=await db.Khasras.SingleOrDefaultAsync(x=>x.VillageId==row.Session.SelectedVillageId && x.NormalizedNumber==k.KhasraNumber && x.Qualifier==Clean(k.Qualifier),ct);
            if(request.Action=="LinkExisting" && match is null) throw new AwardIngestionException("The corrected identity does not exactly match a Village master record.");
            if(match is not null)
            {
                var link=await db.Set<AwardKhasra>().SingleOrDefaultAsync(x=>x.AwardId==row.Session.TargetAwardId && x.KhasraId==match.Id,ct);
                if(link is not null && AwardAreasDisagree(link,k)) throw new AwardIngestionException("Existing Award areas differ. Existing values and their sources remain unchanged.");
            }
        }
        var sourcePage=await VerifiedSourcePageAsync(row,ct);
        row.SourcePage=sourcePage;
        var old=row.StructuredPayloadJson;
        row.StructuredPayloadJson=JsonSerializer.Serialize(payload,payload.GetType(),Json);
        row.CanonicalEntityId=checkedRow.CanonicalEntityId; row.CanonicalEntityType=checkedRow.CanonicalEntityType;
        MarkVerified(row,request.VerifiedBy,old,request.Action);
        // This records a local training label only after an explicit human
        // confirmation/correction.  It is not canonical data and commit is
        // still a separate operation.
        CaptureHumanGold(row, payload, old, request.Action);
        await db.SaveChangesAsync(ct);
    }

    private void CaptureHumanGold(AwardIngestionCandidate row, IAwardIngestionCandidatePayload payload, string oldPayload, string action)
    {
        if (row.Session.SourceDocumentId is null || action is "Skip" or "Uncertain") return;
        try
        {
            using var locator = JsonDocument.Parse(row.SourceLocatorJson ?? "{}");
            var root = locator.RootElement;
            var page = root.TryGetProperty("Page", out var pageNode) ? pageNode.GetInt32() : root.TryGetProperty("page", out pageNode) ? pageNode.GetInt32() : 0;
            if (page < 1 || !(root.TryGetProperty("StructuredPayload", out var structured) || root.TryGetProperty("structuredPayload", out structured))) return;
            var current = JsonSerializer.Serialize(payload, payload.GetType(), Json);
            using var prior = JsonDocument.Parse(oldPayload);
            if (payload is AwardKhasraCandidate && structured.TryGetProperty("sourceCells", out var cells))
            {
                AddGold("Khasra", "khasra", k => k.KhasraNumber + (string.IsNullOrWhiteSpace(k.Qualifier) ? "" : " " + k.Qualifier));
                AddGold("RecordedArea", "recordedArea", k => FormatArea(k.RecordedAreaBigha, k.RecordedAreaBiswa, k.RecordedAreaBiswansi));
                AddGold("AwardedArea", "awardedArea", k => FormatArea(k.AwardedAreaBigha, k.AwardedAreaBiswa, k.AwardedAreaBiswansi));
                void AddGold(string role, string sourceKey, Func<AwardKhasraCandidate, string?> value)
                {
                    if (!cells.TryGetProperty(sourceKey, out var source) || !source.TryGetProperty("sourceRegion", out var region)) return;
                    var final = value((AwardKhasraCandidate)payload); if (string.IsNullOrWhiteSpace(final)) return;
                    var raw = Value(source, "rawOcr"); var normalized = Value(source, "normalizedSuggestion");
                    AddGoldRow(role, page, region.GetRawText(), raw, normalized, final, !string.Equals(final, normalized, StringComparison.Ordinal));
                }
            }
            else if (root.TryGetProperty("SourceRegion", out var region) || root.TryGetProperty("sourceRegion", out region))
            {
                AddGoldRow(payload.CandidateType.ToString(), page, region.GetRawText(), Value(root, "RawOcr"), Value(root, "NormalizedSuggestion"), current, !string.Equals(current, oldPayload, StringComparison.Ordinal));
            }
        }
        catch (JsonException) { /* Invalid evidence cannot create training gold. */ }

        void AddGoldRow(string role, int page, string region, string? raw, string? normalized, string final, bool corrected)
        {
            var existing = db.DocumentTrainingExamples.Local.Concat(db.DocumentTrainingExamples.Where(x => x.SourceCandidateId == row.Id && x.CellRole == role && x.HumanFinalValue == final)).Any();
            if (existing) return; // Retry/idempotency: no duplicate label for same human final value.
            var revision = db.DocumentTrainingExamples.Local.Where(x => x.SourceCandidateId == row.Id && x.CellRole == role).Select(x => x.VerificationRevision)
                .Concat(db.DocumentTrainingExamples.Where(x => x.SourceCandidateId == row.Id && x.CellRole == role).Select(x => x.VerificationRevision)).DefaultIfEmpty(0).Max() + 1;
            db.DocumentTrainingExamples.Add(new DocumentTrainingExample { DocumentId = row.Session.SourceDocumentId!.Value, PageNumber = page, SourceRegionJson = region, CellRole = role, RawOcr = raw, NormalizedSuggestion = normalized, HumanFinalValue = final, WasCorrected = corrected, ReviewDecision = "Confirm", VerifiedAt = row.VerifiedAt!.Value, VerifiedBy = row.VerifiedBy!, SourceCandidateId = row.Id, VerificationRevision = revision });
        }
    }
    private static string? Value(JsonElement node, string property)
    {
        if (!node.TryGetProperty(property, out var value)) node.TryGetProperty(char.ToLowerInvariant(property[0]) + property[1..], out value);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
    private static bool HasGeometryFieldEvidence(string? locator)
    {
        try { using var source = JsonDocument.Parse(locator ?? "{}"); return (source.RootElement.TryGetProperty("structuredPayload", out var payload) || source.RootElement.TryGetProperty("StructuredPayload", out payload)) && payload.TryGetProperty("sourceCells", out _); }
        catch (JsonException) { return false; }
    }
    private static string? FormatArea(decimal? bigha, int? biswa, int? biswansi) => bigha is null ? null : biswansi is null ? $"{bigha}-{biswa ?? 0}" : $"{bigha}-{biswa ?? 0}-{biswansi}";

    private void MarkVerified(AwardIngestionCandidate row,string reviewer,string old,string action)
    {
        row.VerifiedAt=DateTimeOffset.UtcNow; row.VerifiedBy=reviewer.Trim(); row.VerifiedPayloadJson=row.StructuredPayloadJson; row.Status=AwardIngestionCandidateStatus.Ready; row.UpdatedAt=DateTimeOffset.UtcNow;
        db.AuditLogs.Add(new(){EntityType=nameof(AwardIngestionCandidate),EntityId=row.Id,Action="HumanVerified:"+action,ChangedBy=row.VerifiedBy,OldValues=old,NewValues=row.StructuredPayloadJson});
    }
    private static void RequireReviewer(string reviewer) { if(string.IsNullOrWhiteSpace(reviewer) || reviewer.Length>200) throw new AwardIngestionException("Enter the name of the verifying officer."); }

    private async Task<int> VerifiedSourcePageAsync(AwardIngestionCandidate row,CancellationToken ct)
    {
        int? page=row.SourcePage;
        try {using var locator=JsonDocument.Parse(row.SourceLocatorJson??"{}"); if(locator.RootElement.TryGetProperty("Page",out var p) || locator.RootElement.TryGetProperty("page",out p)) page=p.GetInt32();} catch(JsonException){throw new AwardIngestionException("Source page evidence is invalid.");}
        var exists = row.Session.SourceDocumentId is not null && (await db.AwardDocumentPageExtractions.AnyAsync(x=>x.Job.DocumentId==row.Session.SourceDocumentId && x.PageNumber==page,ct) || await db.AwardDocumentExtractionJobs.AnyAsync(x=>x.DocumentId==row.Session.SourceDocumentId && x.TotalPages>=page,ct));
        if(page is null or <1 || !exists) throw new AwardIngestionException("A valid stored document page is required for verification.");
        return page.Value;
    }

    private async Task<bool> IsSafeExactAsync(AwardIngestionCandidate row,CancellationToken ct)
    {
        if(row.CandidateType!=AwardIngestionCandidateType.AwardKhasra || row.Status!=AwardIngestionCandidateStatus.Ready || row.Session.SelectedVillageId is null || row.Session.TargetAwardId is null || row.Session.SourceDocumentId is null) return false;
        if(EvidenceRequiresReview(row.SourceLocatorJson,out _)) return false;
        try {await VerifiedSourcePageAsync(row,ct);} catch(AwardIngestionException){return false;}
        var payload=Deserialize<AwardKhasraCandidate>(row);
        try {ValidateReviewPayload(payload);} catch(AwardIngestionException){return false;}
        if(row.Confidence is null or <.98m) return false;
        var matched=await AnalyzeAsync(row.Session,payload,row.Sequence,ct);
        if(matched.CanonicalEntityId is Guid id)
        {
            if(await db.Set<KhasraReviewFlag>().AnyAsync(x=>x.KhasraId==id && x.Status=="Open",ct)) return false;
            var link=await db.Set<AwardKhasra>().SingleOrDefaultAsync(x=>x.AwardId==row.Session.TargetAwardId && x.KhasraId==id,ct);
            if(link!=null && AwardAreasDisagree(link,payload)) return false;
        }
        return matched.Status==AwardIngestionCandidateStatus.Ready && matched.CanonicalEntityId!=null && matched.ResolutionAction is "LinkExisting" or "AlreadyLinked";
    }

    private static void ValidateReviewPayload(IAwardIngestionCandidatePayload payload)
    {
        static void Area(decimal? b,int? w,int? s) {if(b<0 || w is <0 or >19 || s is <0 or >19) throw new AwardIngestionException("Area values are outside the supported range.");}
        switch(payload)
        {
            case AwardCoreCandidate a when !string.IsNullOrWhiteSpace(a.AwardNumber): break;
            case AwardVillageCandidate v when !string.IsNullOrWhiteSpace(v.VillageName): break;
            case AwardKhasraCandidate k:
                if(!new StrictKhasraParser().TryParse(k.KhasraNumber+(Clean(k.Qualifier) is string q?" "+q:""),out _,out _)) throw new AwardIngestionException("Enter a valid Khasra identifier; no digits will be guessed.");
                Area(k.CanonicalAreaBigha,k.CanonicalAreaBiswa,k.CanonicalAreaBiswansi); Area(k.RecordedAreaBigha,k.RecordedAreaBiswa,k.RecordedAreaBiswansi); Area(k.AwardedAreaBigha,k.AwardedAreaBiswa,k.AwardedAreaBiswansi); break;
            case NotificationCandidate n when !string.IsNullOrWhiteSpace(n.SectionType) && !string.IsNullOrWhiteSpace(n.NotificationNumber) && n.NotificationDate!=null: break;
            case PossessionEventCandidate p when p.PossessionDate!=null: break;
            case CourtCaseCandidate c when !string.IsNullOrWhiteSpace(c.CaseNumber) && !string.IsNullOrWhiteSpace(c.CourtName): break;
            case ClaimCandidate c when !string.IsNullOrWhiteSpace(c.ClaimText): break;
            case LandClassCandidate l when !string.IsNullOrWhiteSpace(l.Code): break;
            case ValuationRuleCandidate v when !string.IsNullOrWhiteSpace(v.RuleType) && !string.IsNullOrWhiteSpace(v.RateUnit) && v.RateAmount>=0: break;
            case CompensationRuleCandidate c when !string.IsNullOrWhiteSpace(c.RuleType) && (c.RatePercent>=0 || c.RateAmount>=0): break;
            case AreaIssueCandidate a when !string.IsNullOrWhiteSpace(a.IssueType): break;
            case SupplementaryMatterCandidate s when !string.IsNullOrWhiteSpace(s.MatterType): break;
            default: throw new AwardIngestionException("This section requires complete structured values before verification; narrative findings are evidence, not confirmed records.");
        }
    }

    private async Task<bool> CommitVerifiedRelatedAsync(AwardIngestionSession session,AwardIngestionCandidate candidate,CancellationToken ct)
    {
        var payload=DeserializeInput(new(candidate.CandidateType,candidate.StructuredPayloadJson)); ValidateReviewPayload(payload);
        if(payload is AwardCoreCandidate core)
        {
            var award=await db.Awards.SingleAsync(x=>x.Id==session.TargetAwardId,ct);
            award.AwardNumber=core.AwardNumber.Trim(); award.AwardDate=core.AwardDate; award.AwardType=Clean(core.AwardType); award.Purpose=Clean(core.Purpose);
            candidate.CanonicalEntityId=award.Id;candidate.CanonicalEntityType=nameof(Award);candidate.Status=AwardIngestionCandidateStatus.Committed;return true;
        }
        if(payload is AwardVillageCandidate village)
        {
            if(!await db.AwardVillages.AnyAsync(x=>x.AwardId==session.TargetAwardId && x.VillageId==session.SelectedVillageId && x.Village.Name==village.VillageName,ct)) throw new AwardIngestionException("The confirmed Award Village no longer matches.",409);
            candidate.CanonicalEntityId=session.TargetAwardId; candidate.CanonicalEntityType=nameof(Award);candidate.Status=AwardIngestionCandidateStatus.Committed;return true;
        }
        OfficialRecord? record=payload switch
        {
            PossessionEventCandidate p => new PossessionEvent{AwardId=session.TargetAwardId!.Value,PossessionDate=p.PossessionDate,EventType=p.EventType,Status=p.Status},
            CourtCaseCandidate p => new CourtCase{CaseNumber=p.CaseNumber,CourtName=p.CourtName,CaseType=p.CaseType},
            ClaimCandidate p => new Claim{AwardId=session.TargetAwardId!.Value,ClaimReference=p.ClaimReference,ClaimDate=p.ClaimDate,ClaimText=p.ClaimText},
            LandClassCandidate p => new AwardLandClass{AwardId=session.TargetAwardId!.Value,Code=p.Code,Description=p.Description},
            ValuationRuleCandidate p => new AwardValuationRule{AwardId=session.TargetAwardId!.Value,RuleType=p.RuleType,RateAmount=p.RateAmount,RateUnit=p.RateUnit,LegalSection=p.LegalSection},
            CompensationRuleCandidate p => new AwardCompensationRule{AwardId=session.TargetAwardId!.Value,RuleType=p.RuleType,RateAmount=p.RateAmount,RatePercent=p.RatePercent,LegalSection=p.LegalSection},
            AreaIssueCandidate p => new AwardAreaIssue{AwardId=session.TargetAwardId!.Value,IssueType=p.IssueType,NotificationAreaBigha=p.NotificationAreaBigha,FieldBookAreaBigha=p.FieldBookAreaBigha,DifferenceBigha=p.DifferenceBigha},
            SupplementaryMatterCandidate p => new AwardSupplementaryMatter{AwardId=session.TargetAwardId!.Value,MatterType=p.MatterType,Description=p.Description},
            _=>null
        };
        if(record is null) return false;
        OfficialRecord? existing = payload switch {
            PossessionEventCandidate p => await db.PossessionEvents.SingleOrDefaultAsync(x=>x.AwardId==session.TargetAwardId && x.PossessionDate==p.PossessionDate && x.EventType==p.EventType && x.Status==p.Status,ct),
            CourtCaseCandidate p => await db.CourtCases.SingleOrDefaultAsync(x=>x.CaseNumber==p.CaseNumber && x.CourtName==p.CourtName && x.CaseType==p.CaseType,ct),
            ClaimCandidate p => await db.Claims.SingleOrDefaultAsync(x=>x.AwardId==session.TargetAwardId && x.ClaimReference==p.ClaimReference && x.ClaimDate==p.ClaimDate && x.ClaimText==p.ClaimText,ct),
            LandClassCandidate p => await db.Set<AwardLandClass>().SingleOrDefaultAsync(x=>x.AwardId==session.TargetAwardId && x.Code==p.Code && x.Description==p.Description,ct),
            ValuationRuleCandidate p => await db.Set<AwardValuationRule>().SingleOrDefaultAsync(x=>x.AwardId==session.TargetAwardId && x.RuleType==p.RuleType && x.RateAmount==p.RateAmount && x.RateUnit==p.RateUnit && x.LegalSection==p.LegalSection,ct),
            CompensationRuleCandidate p => await db.Set<AwardCompensationRule>().SingleOrDefaultAsync(x=>x.AwardId==session.TargetAwardId && x.RuleType==p.RuleType && x.RateAmount==p.RateAmount && x.RatePercent==p.RatePercent && x.LegalSection==p.LegalSection,ct),
            AreaIssueCandidate p => await db.Set<AwardAreaIssue>().SingleOrDefaultAsync(x=>x.AwardId==session.TargetAwardId && x.IssueType==p.IssueType && x.NotificationAreaBigha==p.NotificationAreaBigha && x.FieldBookAreaBigha==p.FieldBookAreaBigha && x.DifferenceBigha==p.DifferenceBigha,ct),
            SupplementaryMatterCandidate p => await db.Set<AwardSupplementaryMatter>().SingleOrDefaultAsync(x=>x.AwardId==session.TargetAwardId && x.MatterType==p.MatterType && x.Description==p.Description,ct),
            _=>null
        };
        if(existing is null) db.Add(record); else record=existing;
        if(record is CourtCase court && !await db.Set<CourtCaseAward>().AnyAsync(x=>x.AwardId==session.TargetAwardId && x.CourtCaseId==court.Id,ct)) db.Add(new CourtCaseAward{AwardId=session.TargetAwardId!.Value,CourtCase=court});
        await db.SaveChangesAsync(ct);
        candidate.CanonicalEntityId=record.Id;candidate.CanonicalEntityType=record.GetType().Name;candidate.Status=AwardIngestionCandidateStatus.Committed;
        return true;
    }

    private async Task SavePermanentEvidenceAsync(AwardIngestionSession session,AwardIngestionCandidate candidate,CancellationToken ct)
    {
        var page=await VerifiedSourcePageAsync(candidate,ct);
        var linkId=candidate.CandidateType==AwardIngestionCandidateType.AwardKhasra ? await db.Set<AwardKhasra>().Where(x=>x.AwardId==session.TargetAwardId && x.KhasraId==candidate.CanonicalEntityId).Select(x=>(Guid?)x.Id).SingleAsync(ct):null;
        using var payload=JsonDocument.Parse(candidate.VerifiedPayloadJson!);
        // Award extraction does not verify Village master area. Do not attribute it to the Award link.
        foreach(var field in payload.RootElement.EnumerateObject().Where(p=>p.Name!="candidateType" && !p.Name.StartsWith("canonicalArea",StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind!=JsonValueKind.Null))
        {
            var evidence=new SourceEvidence{DocumentId=session.SourceDocumentId!.Value,PageNumber=page,FactName=field.Name,ConfirmedValueJson=field.Value.GetRawText(),ExtractedSnippet=candidate.RawSourceText,VerifiedAt=candidate.VerifiedAt!.Value,VerifiedBy=candidate.VerifiedBy!};
            switch(candidate.CandidateType)
            {
                case AwardIngestionCandidateType.AwardCore:evidence.AwardId=session.TargetAwardId;break;
                case AwardIngestionCandidateType.AwardVillage:evidence.AwardId=session.TargetAwardId;break;
                case AwardIngestionCandidateType.AwardKhasra:evidence.AwardKhasraId=linkId;break;
                case AwardIngestionCandidateType.Notification:evidence.NotificationId=candidate.CanonicalEntityId;break;
                case AwardIngestionCandidateType.PossessionEvent:evidence.PossessionEventId=candidate.CanonicalEntityId;break;
                case AwardIngestionCandidateType.CourtCase:evidence.CourtCaseId=candidate.CanonicalEntityId;break;
                case AwardIngestionCandidateType.Claim:evidence.ClaimId=candidate.CanonicalEntityId;break;
                case AwardIngestionCandidateType.AwardLandClass:evidence.AwardLandClassId=candidate.CanonicalEntityId;break;
                case AwardIngestionCandidateType.AwardValuationRule:evidence.AwardValuationRuleId=candidate.CanonicalEntityId;break;
                case AwardIngestionCandidateType.AwardCompensationRule:evidence.AwardCompensationRuleId=candidate.CanonicalEntityId;break;
                case AwardIngestionCandidateType.AwardAreaIssue:evidence.AwardAreaIssueId=candidate.CanonicalEntityId;break;
                case AwardIngestionCandidateType.AwardSupplementaryMatter:evidence.AwardSupplementaryMatterId=candidate.CanonicalEntityId;break;
                default:throw new AwardIngestionException("Permanent evidence target is not supported.");
            }
            db.SourceEvidence.Add(evidence);
        }
    }
}
