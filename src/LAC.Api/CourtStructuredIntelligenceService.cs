using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

public sealed class CourtStructuredIntelligenceService(LacDbContext db, LocalStoragePaths paths,
    CourtIntelligencePersistence persistence, CourtOfficeAuthority office, ICourtAuthorizationService auth)
{
    public async Task<JsonElement?> OfficeActionAnswerAsync(CourtIntelligenceCaseIndex index,Guid actor,string question,CancellationToken ct=default)
    {
        if(question.Length>2000 || !Regex.IsMatch(question,@"\bwhat\b.{0,60}\b(?:LAC|our office)\b.{0,40}\bdo\b|\bpending\b.{0,30}\bLAC\b.{0,30}\b(?:actions?|tasks?)\b",RegexOptions.IgnoreCase))return null;
        var artifact=await CourtIntelligenceArtifactReader.ReadAsync(paths.ExtractionRoot,index.CaseId,ct);
        if(artifact is null)return null;
        var view=JsonNode.Parse((await ViewAsync(index,actor,ct)).GetRawText())!.AsObject();
        var actions=view["beforeNextHearing"]!.AsArray();
        var coverage=view["orders"]!.AsArray().Count>=index.Orders.Count(o=>CourtIntelligenceCaseData.Eligible(index,o))
            && view["orders"]!.AsArray().All(o=>o?["lacOrderScope"]?["extraction"]?["fullRelevantTextChecked"]?.GetValue<bool>()==true
                && o["lacOrderScope"]?["lacRelevanceState"]?.GetValue<string>()!="NeedsReview");
        var claims=new JsonArray(actions.Select(a=>new JsonObject { ["text"]=a!["text"]!.DeepClone(),["attribution"]="COURT_DIRECTION",["source"]=a["source"]!.DeepClone() }).Cast<JsonNode?>().ToArray());
        var conclusion=actions.Count>0 ? "The processed Court evidence establishes these operative directions to this office. Completion is not confirmed."
            : coverage ? "No operative LAC direction to this office is established in the checked orders." : "No operative direction to this office is established in the usable evidence. Office authority or source coverage still needs review.";
        return JsonSerializer.SerializeToElement(new JsonObject { ["caseId"]=index.CaseId.ToString(),["answer"]=conclusion,["actionConclusion"]=conclusion,
            ["coverageNote"]=coverage ? "Known relevant native sources checked." : "This conclusion is limited to checked evidence; unresolved sources or authority may contain further directions.",
            ["claims"]=claims,["insufficientEvidence"]=!coverage && actions.Count==0,["mode"]="CourtGrounded" });
    }
    public async Task<int> IngestAsync(CourtIntelligenceCaseIndex index, CancellationToken ct=default)
    {
        var artifact=await CourtIntelligenceArtifactReader.ReadAsync(paths.ExtractionRoot,index.CaseId,ct);
        if(artifact is null)return 0;
        CourtIntelligenceCaseData.ValidateArtifact(artifact.Value,index);
        var count=0;
        foreach(var order in artifact.Value.GetProperty("orders").EnumerateArray())
        {
            if(!order.TryGetProperty("lacOrderScope",out var structured))continue;
            var source=index.Orders.First(x=>CourtIntelligenceCaseData.Eligible(index,x)
                && x.OfficialUrl==order.GetProperty("officialUrl").GetString() && x.OrderDate!.Value.ToString("yyyy-MM-dd")==order.GetProperty("orderDate").GetString());
            var raw=JsonNode.Parse(structured.GetRawText())!.AsObject();
            // Retrieved fragment provenance is stamped from the trusted DB index, not inferred by the model.
            raw["source"]!["sourceObservationId"]=source.SourceObservationId.ToString();
            raw["source"]!["sourceEvidenceSha256"]=source.SourceEvidenceSha256;
            await persistence.PersistAsync(index.CaseId,source.SourceObservationId,source.OrderDate!.Value,source.OfficialUrl!,raw,source.SourceKind ?? "Order",ct);
            count++;
        }
        return count;
    }

    public async Task<JsonElement> ViewAsync(CourtIntelligenceCaseIndex index, Guid userId, CancellationToken ct=default)
    {
        var raw=await CourtIntelligenceCaseData.ViewAsync(paths.ExtractionRoot,index,ct);
        var view=JsonNode.Parse(raw.GetRawText())!.AsObject();
        var persisted=await db.CourtOrderIntelligence.AsNoTracking().Where(o=>o.CourtCaseId==index.CaseId)
            .Include(o=>o.Revisions).ThenInclude(r=>r.Links).ToListAsync(ct);
        foreach(var order in view["orders"]!.AsArray())
        {
            var stored=persisted.SingleOrDefault(o=>o.OfficialUrl==order!["officialUrl"]!.GetValue<string>() && o.OrderDate.ToString("yyyy-MM-dd")==order["orderDate"]!.GetValue<string>());
            if(order!["lacOrderScope"] is JsonObject scope)
            {
                order["lacOrderScope"]=await office.ResolveAsync(scope,ct);
                if(stored is not null)
                {
                    order["courtOrderIntelligenceId"]=stored.Id.ToString();
                    var hash=scope["source"]?["pdfSha256"]?.GetValue<string>() ?? "";
                    var revision=stored.Revisions.Where(r=>r.PdfSha256==hash).OrderByDescending(r=>r.CreatedAt).FirstOrDefault();
                    order["scopeRevisionId"]=revision?.Id.ToString();
                    var links=new JsonArray();
                    foreach(var link in stored.Revisions.Where(r=>r.PdfSha256==hash).SelectMany(r=>r.Links).OrderByDescending(l=>l.ReviewedAt))
                    {
                        if(link.VillageId is Guid v && !await auth.CanAccessVillageAsync(v,userId,ct)
                            || link.AwardId is Guid a && !await auth.CanAccessAwardAsync(a,userId,ct)
                            || link.KhasraId is Guid k && !await auth.CanAccessKhasraAsync(k,userId,ct))continue;
                        links.Add(JsonSerializer.SerializeToNode(new {link.Id,link.ExtractedEntityId,link.EntityType,link.VillageId,link.AwardId,link.KhasraId,
                            MatchState=link.MatchState.ToString(),link.MatchReason,link.Version,link.ReviewedByUserId,link.ReviewedAt,link.ReviewReason,link.Origin,
                            officeRecord=await OfficeRecordAsync(link,ct)},JsonSerializerOptions.Web));
                    }
                    order["recordLinks"]=links;
                }
            }
            else order["structuredScopeState"]="Structured order scope not yet extracted.";
        }
        foreach (var key in new[]{"latestOrder","finalOrder","latestMeaningfulOrder"})
            if (view[key] is JsonObject displayed)
            {
                var resolved=view["orders"]!.AsArray().FirstOrDefault(o=>o!["officialUrl"]!.GetValue<string>()==displayed["officialUrl"]!.GetValue<string>() && o["orderDate"]!.ToString()==displayed["orderDate"]!.ToString());
                if(resolved is not null)view[key]=resolved.DeepClone();
            }
        BuildCaseContext(view);
        view["canReviewOrderLinks"]=await auth.CanEditCourtCaseAsync(index.CaseId,userId,ct);
        return JsonSerializer.SerializeToElement(view);
    }

    private async Task<object?> OfficeRecordAsync(CourtOrderRecordLink link,CancellationToken ct)
    {
        if(link.AwardId is Guid a)return await db.Awards.AsNoTracking().Where(x=>x.Id==a).Select(x=>new {x.AwardNumber,x.AwardDate}).SingleOrDefaultAsync(ct);
        if(link.KhasraId is Guid k)return await db.Khasras.AsNoTracking().Where(x=>x.Id==k).Select(x=>new {x.NormalizedNumber,x.Qualifier,x.TotalArea,x.AreaUnit,Village=x.Village.Name}).SingleOrDefaultAsync(ct);
        if(link.VillageId is Guid v)return await db.Villages.AsNoTracking().Where(x=>x.Id==v).Select(x=>new {x.Name,Subdivision=x.SubDivision.Name}).SingleOrDefaultAsync(ct);
        return null;
    }

    internal static void BuildCaseContext(JsonObject view)
    {
        var context=new JsonObject();var actions=new JsonArray();
        foreach(var section in new[]{"villages","awards","parcels","possession","compensation"})context[section]=new JsonArray();
        foreach(var order in view["orders"]!.AsArray().OrderBy(o=>o!["orderDate"]!.GetValue<string>()))
        {
            if(order!["lacOrderScope"] is not JsonObject scope)continue;
            foreach(var section in new[]{"villages","awards","parcels","possession","compensation"})
                foreach(var item in scope[section]!.AsArray())
                    context[section]!.AsArray().Add(new JsonObject { ["orderDate"]=order["orderDate"]!.DeepClone(),["officialUrl"]=order["officialUrl"]!.DeepClone(),["fact"]=item!.DeepClone(),["evidence"]=scope["evidence"]!.DeepClone() });
            foreach(var change in scope["directionChanges"]!.AsArray())
            {
                var day=change?["targetOrderDate"]?.GetValue<string>();
                var text=change?["targetActionText"]?.GetValue<string>();
                var evidence=CourtScopeContract.Text(change?["language"]);
                if(day is null || text is null || evidence is null || !evidence.Contains(text,StringComparison.Ordinal)
                    || change?["language"]?["attribution"]?.GetValue<string>() is not ("COURT_FINDING" or "COURT_DIRECTION")
                    || change?["language"]?["scope"]?.GetValue<string>()!="Current"
                    || !CourtDirectionDeadline.SourceDates(evidence).Any(d=>d.ToString("yyyy-MM-dd")==day))continue;
                foreach(var action in actions.Where(a=>a!["orderDate"]!.GetValue<string>()==day && a["text"]!.GetValue<string>()==text))
                { action!["state"]=change!["kind"]!.GetValue<string>()=="compliance" ? "Completed" : "Superseded";
                  action["resolutionSource"]=new JsonObject { ["orderDate"]=order["orderDate"]!.DeepClone(),["officialUrl"]=order["officialUrl"]!.DeepClone(),["evidence"]=scope["evidence"]!.DeepClone() }; }
            }
            foreach(var direction in scope["directions"]!.AsArray().Where(d=>d?["lacActionable"]?.GetValue<bool>()==true))
            {
                var eid=direction!["action"]!["evidenceIds"]![0]!.GetValue<string>();
                var e=scope["evidence"]!.AsArray().Single(e=>e!["id"]!.GetValue<string>()==eid)!;
                var deadline=CourtScopeContract.Text(direction["deadline"]);
                var nextHearing=DateOnly.TryParse(CourtScopeContract.Text(direction["nextHearingDate"]),out var hearing) ? (DateOnly?)hearing : null;
                actions.Add(new JsonObject { ["id"]=direction["id"]!.DeepClone(),["text"]=CourtScopeContract.Text(direction["action"]),["actor"]=CourtScopeContract.Text(direction["directedTo"]),
                    ["deadlineText"]=deadline,["dueDate"]=CourtDirectionDeadline.Resolve(DateOnly.Parse(order["orderDate"]!.GetValue<string>()),deadline,nextHearing)?.ToString("yyyy-MM-dd"),
                    ["deadlineBasis"]=deadline is null ? null : "Source deadline; calculated only with an explicit date or order-date anchor",["state"]="Not confirmed complete",["orderDate"]=order["orderDate"]!.DeepClone(),
                    ["source"]=new JsonObject{["orderDate"]=order["orderDate"]!.DeepClone(),["officialUrl"]=order["officialUrl"]!.DeepClone(),["page"]=e["page"]!.DeepClone(),["evidence"]=e["text"]!.DeepClone()} });
            }
        }
        context["directions"]=actions.DeepClone();view["caseLandLacContext"]=context;
        // Legacy evidence remains usable, but unstructured jurisdiction does not authorize this office.
        view["beforeNextHearing"]=new JsonArray(actions.Where(a=>a!["state"]!.GetValue<string>()=="Not confirmed complete").Select(a=>a!.DeepClone()).ToArray());
    }
}
