using System.Text.Json;
using System.Text.Json.Nodes;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed record CourtLinkReviewRequest(CourtRecordMatchState State, int ExpectedVersion, string Reason);
public sealed class CourtOrderLinkReview(LacDbContext db, ICourtAuthorizationService auth)
{
    public async Task MatchAsync(Guid revisionId, Guid userId, CancellationToken ct = default)
    {
        var revision = await db.CourtOrderIntelligenceRevisions.Include(r => r.Order).SingleAsync(r => r.Id == revisionId, ct);
        if (!await auth.CanEditCourtCaseAsync(revision.Order.CourtCaseId, userId, ct)) throw new CourtWorkflowException("Forbidden",403);
        var scope = JsonNode.Parse(revision.StructuredFactsJson)!.AsObject();
        var existing = await db.CourtOrderRecordLinks.Where(l => l.Revision.CourtOrderIntelligenceId == revision.CourtOrderIntelligenceId && l.Revision.PdfSha256 == revision.PdfSha256).ToListAsync(ct);
        var villages = await db.Villages.AsNoTracking().Where(v => v.RecordStatus == RecordStatus.Active).ToListAsync(ct);
        var awards = await db.Awards.AsNoTracking().Where(a => a.RecordStatus == RecordStatus.Active).Include(a => a.VillageLinks).ToListAsync(ct);
        var khasras = await db.Khasras.AsNoTracking().Where(k => k.RecordStatus == RecordStatus.Active).ToListAsync(ct);
        var villageCandidates = new Dictionary<string,List<Guid>>();
        foreach (var v in scope["villages"]!.AsArray())
        {
            var extractedId = v!["id"]!.GetValue<string>();
            var name = CourtScopeContract.Text(v["name"]);
            var matches = villages.Where(x => string.Equals(x.Name.Trim(),name?.Trim(),StringComparison.OrdinalIgnoreCase)).ToList();
            var reviewed=existing.Where(l=>l.ExtractedEntityId==extractedId && l.EntityType=="Village" && l.MatchState==CourtRecordMatchState.Confirmed)
                .Select(l=>l.VillageId!.Value).Where(id=>matches.Any(v=>v.Id==id)).Distinct().ToList();
            villageCandidates[extractedId] = reviewed.Count==1 ? reviewed : matches.Select(x => x.Id).ToList();
            if(matches.Count==0)Add(extractedId,"Village",null,CourtRecordMatchState.NotMatched,"No exact village name match in active office records.");
            foreach (var candidate in matches)
                if (await auth.CanAccessVillageAsync(candidate.Id,userId,ct)) Add(extractedId,"Village",candidate.Id,matches.Count>1 ? CourtRecordMatchState.Ambiguous : CourtRecordMatchState.Candidate,"Exact village name; officer review required.");
        }
        foreach (var a in scope["awards"]!.AsArray())
        {
            var matches = awards.Where(x => AwardNumberIdentity.Equivalent(x.AwardNumber,CourtScopeContract.Text(a!["number"]))).ToList();
            var geography = a!["villageRefs"]!.AsArray().SelectMany(r => villageCandidates.GetValueOrDefault(r!.GetValue<string>()) ?? []).ToHashSet();
            if(matches.Count==0)Add(a["id"]!.GetValue<string>(),"Award",null,CourtRecordMatchState.NotMatched,"No equivalent active Award number found.");
            foreach (var candidate in matches)
            {
                if (!await auth.CanAccessAwardAsync(candidate.Id,userId,ct)) continue;
                var date = CourtScopeContract.Text(a["date"]);
                var conflict = date is not null && candidate.AwardDate?.ToString("yyyy-MM-dd") is { } officeDate && date != officeDate
                    || geography.Count>0 && candidate.VillageLinks.Count>0 && !candidate.VillageLinks.Any(v => geography.Contains(v.VillageId));
                Add(a["id"]!.GetValue<string>(),"Award",candidate.Id,conflict ? CourtRecordMatchState.Conflict : matches.Count>1 ? CourtRecordMatchState.Ambiguous : CourtRecordMatchState.Candidate,
                    conflict ? "Explicit court date/geography conflicts with office record." : "Award number equivalent; officer review required.");
            }
        }
        foreach (var p in scope["parcels"]!.AsArray())
        {
            if (!new StrictKhasraParser().TryParse(p!["number"]?["rawText"]?.GetValue<string>() ?? "",out var number,out _))
            {Add(p!["id"]!.GetValue<string>(),"Khasra",null,CourtRecordMatchState.NeedsReview,"Khasra source syntax requires review.");continue;}
            var geography=p["villageRefs"]!.AsArray().SelectMany(r=>villageCandidates.GetValueOrDefault(r!.GetValue<string>()) ?? []).ToHashSet();
            // Missing or ambiguous geography cannot create a candidate parcel identity.
            if(geography.Count != 1)
            {Add(p["id"]!.GetValue<string>(),"Khasra",null,CourtRecordMatchState.NeedsReview,"A single source-backed Village identity is required.");continue;}
            var matches=khasras.Where(k=>geography.Contains(k.VillageId) && k.NormalizedNumber==KhasraNumber.Normalize(number.NormalizedNumber) && k.Qualifier==number.Qualifier).ToList();
            if(matches.Count==0)Add(p["id"]!.GetValue<string>(),"Khasra",null,CourtRecordMatchState.NotMatched,"No exact Village/normalized Khasra/qualifier identity found.");
            foreach(var candidate in matches)
                if(await auth.CanAccessKhasraAsync(candidate.Id,userId,ct))
                    Add(p["id"]!.GetValue<string>(),"Khasra",candidate.Id,matches.Count>1 ? CourtRecordMatchState.Ambiguous : CourtRecordMatchState.Candidate,"Exact Village/number/qualifier identity; source area remains separate.");
        }
        void Add(string extractedId,string type,Guid? target,CourtRecordMatchState state,string reason)
        {
            if(existing.Any(l=>l.ExtractedEntityId==extractedId && l.EntityType==type
                && (type=="Village" ? l.VillageId : type=="Award" ? l.AwardId : l.KhasraId)==target))return;
            var link=new CourtOrderRecordLink { RevisionId=revisionId, ExtractedEntityId=extractedId, EntityType=type,
                VillageId=type=="Village" ? target : null,AwardId=type=="Award" ? target : null,KhasraId=type=="Khasra" ? target : null,MatchState=state,MatchReason=reason };
            db.CourtOrderRecordLinks.Add(link);existing.Add(link);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task ReviewAsync(Guid linkId, CourtLinkReviewRequest request, Guid userId, CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length>2000 || request.State is not (CourtRecordMatchState.Confirmed or CourtRecordMatchState.NotMatched or CourtRecordMatchState.NeedsReview))
            throw new CourtWorkflowException("A review decision and reason are required.",400);
        var link=await db.CourtOrderRecordLinks.Include(l=>l.Revision).ThenInclude(r=>r.Order).SingleOrDefaultAsync(l=>l.Id==linkId,ct)
            ?? throw new CourtWorkflowException("Order link not found.",404);
        if(!await auth.CanEditCourtCaseAsync(link.Revision.Order.CourtCaseId,userId,ct)
            || link.VillageId is Guid v && !await auth.CanAccessVillageAsync(v,userId,ct)
            || link.AwardId is Guid a && !await auth.CanAccessAwardAsync(a,userId,ct)
            || link.KhasraId is Guid k && !await auth.CanAccessKhasraAsync(k,userId,ct)) throw new CourtWorkflowException("Forbidden",403);
        if(link.Version!=request.ExpectedVersion)throw new CourtWorkflowException("Link changed. Reload before reviewing.",409);
        if(request.State==CourtRecordMatchState.Confirmed && link.MatchState is CourtRecordMatchState.Conflict)
            throw new CourtWorkflowException("Resolve the source/office conflict before confirmation; do not overwrite canonical facts.",409);
        if(request.State==CourtRecordMatchState.Confirmed && !await CanConfirmAsync(link,ct))
            throw new CourtWorkflowException("Source identity/date/geography conflicts with the current office record. Confirmation is blocked.",409);
        if(request.State==CourtRecordMatchState.Confirmed && await db.CourtOrderRecordLinks.AnyAsync(l=>l.Revision.CourtOrderIntelligenceId==link.Revision.CourtOrderIntelligenceId && l.Revision.PdfSha256==link.Revision.PdfSha256 && l.ExtractedEntityId==link.ExtractedEntityId && l.Id!=link.Id && l.MatchState==CourtRecordMatchState.Confirmed,ct))
            throw new CourtWorkflowException("Another candidate is already confirmed. Revoke that decision first.",409);
        var before=JsonSerializer.Serialize(new {link.MatchState,link.Version});
        link.MatchState=request.State;link.Version++;link.ReviewedByUserId=userId;link.ReviewedAt=DateTimeOffset.UtcNow;link.ReviewReason=request.Reason.Trim();
        db.AuditLogs.Add(new AuditLog {EntityType=nameof(CourtOrderRecordLink),EntityId=link.Id,ActorUserId=userId,ChangedAt=link.ReviewedAt.Value,
            Action="CourtOrderLinkReviewed",OldValues=before,NewValues=JsonSerializer.Serialize(new {link.MatchState,link.Version,link.ReviewReason,link.RevisionId,link.ExtractedEntityId})});
        try {await db.SaveChangesAsync(ct);} catch(DbUpdateConcurrencyException){throw new CourtWorkflowException("Link changed. Reload before reviewing.",409);}
    }

    private async Task<bool> CanConfirmAsync(CourtOrderRecordLink link,CancellationToken ct)
    {
        if(link.VillageId is null && link.AwardId is null && link.KhasraId is null)return false;
        var scope=JsonNode.Parse(link.Revision.StructuredFactsJson)!.AsObject();
        var section=link.EntityType=="Village" ? "villages" : link.EntityType=="Award" ? "awards" : "parcels";
        var entity=scope[section]!.AsArray().SingleOrDefault(x=>x!["id"]!.GetValue<string>()==link.ExtractedEntityId);
        if(entity is null)return false;
        if(link.VillageId is Guid villageId)
        {
            var village=await db.Villages.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==villageId && x.RecordStatus==RecordStatus.Active,ct);
            return village is not null && string.Equals(village.Name.Trim(),CourtScopeContract.Text(entity["name"])?.Trim(),StringComparison.OrdinalIgnoreCase);
        }
        var names=entity["villageRefs"]!.AsArray().Select(id=>scope["villages"]!.AsArray().Single(v=>v!["id"]!.GetValue<string>()==id!.GetValue<string>()))
            .Select(v=>CourtScopeContract.Text(v!["name"])?.Trim().ToLower()).Where(n=>n is not null).ToArray();
        var geography=await db.Villages.AsNoTracking().Where(v=>v.RecordStatus==RecordStatus.Active && names.Contains(v.Name.Trim().ToLower())).Select(v=>v.Id).ToListAsync(ct);
        var referenceIds=entity["villageRefs"]!.AsArray().Select(r=>r!.GetValue<string>()).ToArray();
        var reviewed=await db.CourtOrderRecordLinks.AsNoTracking().Where(l=>l.Revision.CourtOrderIntelligenceId==link.Revision.CourtOrderIntelligenceId
            && l.Revision.PdfSha256==link.Revision.PdfSha256 && l.MatchState==CourtRecordMatchState.Confirmed && l.VillageId!=null && referenceIds.Contains(l.ExtractedEntityId))
            .Select(l=>l.VillageId!.Value).Distinct().ToListAsync(ct);
        if(reviewed.Count==1 && geography.Contains(reviewed[0]))geography=reviewed;
        if(link.AwardId is Guid awardId)
        {
            var award=await db.Awards.AsNoTracking().Include(a=>a.VillageLinks).SingleOrDefaultAsync(a=>a.Id==awardId && a.RecordStatus==RecordStatus.Active,ct);
            return award is not null && AwardNumberIdentity.Equivalent(award.AwardNumber,CourtScopeContract.Text(entity["number"]))
                && (CourtScopeContract.Text(entity["date"]) is not { } date || award.AwardDate is null || award.AwardDate.Value.ToString("yyyy-MM-dd")==date)
                && (names.Length==0 || geography.Count>0 && (award.VillageLinks.Count==0 || award.VillageLinks.Any(v=>geography.Contains(v.VillageId))));
        }
        if(link.KhasraId is Guid khasraId)
        {
            var khasra=await db.Khasras.AsNoTracking().SingleOrDefaultAsync(k=>k.Id==khasraId && k.RecordStatus==RecordStatus.Active,ct);
            return khasra is not null && geography.Count==1 && geography[0]==khasra.VillageId
                && new StrictKhasraParser().TryParse(entity["number"]?["rawText"]?.GetValue<string>() ?? "",out var number,out _)
                && khasra.NormalizedNumber==KhasraNumber.Normalize(number.NormalizedNumber) && khasra.Qualifier==number.Qualifier;
        }
        return false;
    }
}
