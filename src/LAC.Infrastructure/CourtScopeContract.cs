using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Globalization;

namespace LAC.Infrastructure;

public static class CourtScopeContract
{
    public const string Version = "court-lac-order-scope/v1";
    public static readonly string[] Sections = ["purposes", "villages", "awards", "parcels", "parcelGroups", "possession", "compensation", "directions", "directionChanges", "evidence", "relevanceBasis"];
    public static JsonObject Validate(JsonObject scope, string pdfSha256)
    {
        try{return ValidateCore(scope,pdfSha256);}
        catch(Exception e) when(e is InvalidOperationException or NullReferenceException or KeyNotFoundException or ArgumentException or FormatException)
        {throw new InvalidDataException("Malformed structured Court scope.",e);}
    }
    private static JsonObject ValidateCore(JsonObject scope,string pdfSha256)
    {
        var noBytes = string.IsNullOrEmpty(pdfSha256) && scope["extraction"]?["fullRelevantTextChecked"]?.GetValue<bool>() == false
            && Sections.All(s => scope[s] is JsonArray a && a.Count == 0);
        if (scope["contract"]?.GetValue<string>() != Version || !noBytes && (!Regex.IsMatch(pdfSha256, "^[a-fA-F0-9]{64}$")
            || scope["source"]?["pdfSha256"]?.GetValue<string>() != pdfSha256))
            throw new InvalidDataException("Structured Court scope source/version mismatch.");
        foreach (var key in Sections)
            if (scope[key] is not JsonArray) throw new InvalidDataException("Missing structured Court section: " + key);
        var complete = scope["extraction"]?["fullRelevantTextChecked"]?.GetValue<bool>() == true;
        if (scope["extraction"]?["state"]?.GetValue<string>() is not ("Validated" or "NeedsReview"))
            throw new InvalidDataException("Invalid Court scope extraction state.");
        var evidence = scope["evidence"]!.AsArray().ToDictionary(x => x!["id"]!.GetValue<string>());
        foreach (var item in evidence.Values)
            if (item?["page"]?.GetValue<int>() is not (>= 1 and <= 200) || string.IsNullOrWhiteSpace(item["text"]?.GetValue<string>()))
                throw new InvalidDataException("Court scope evidence requires a page and passage.");
        var ids = new HashSet<string>();
        var required = new Dictionary<string,string[]> {
            ["purposes"]=["subject"], ["villages"]=["name"], ["awards"]=["number","date"],
            ["parcels"]=["number","area","areaUnit"], ["parcelGroups"]=["area","areaUnit"],
            ["possession"]=["status","language","date"], ["compensation"]=["status","language","amount","currency","rate","rateUnit","beneficiary","depositReference"],
            ["directions"]=["directedTo","action","object","deadline","nextHearingDate"], ["directionChanges"]=["language"] };
        foreach (var section in Sections.Where(x => x is not ("evidence" or "relevanceBasis")))
            foreach (var item in scope[section]!.AsArray())
                if (item is not JsonObject || item["id"] is null || !ids.Add(item["id"]!.GetValue<string>()))
                    throw new InvalidDataException("Invalid/duplicate extracted entity identity.");
                else foreach(var field in required[section])
                    if(item[field] is not JsonObject fact || !fact.ContainsKey("rawText") || !fact.ContainsKey("value"))
                        throw new InvalidDataException("Missing structured Court field: "+section+"."+field);
                    else if(fact["state"]?.ToString()=="Explicit")
                    {
                        var raw=fact["rawText"]?.ToString();var value=fact["value"]?.ToString();
                        if(field is "name" or "number" or "subject" or "language" or "directedTo" or "action" or "object" or "deadline" or "areaUnit" or "rateUnit" or "beneficiary" or "depositReference")
                        {if(raw!=value)throw new InvalidDataException("Source text field cannot be rewritten: "+field);}
                        if(field is "date" or "nextHearingDate")
                        {
                            if(!DateOnly.TryParseExact(raw,["d.M.yyyy","d/M/yyyy","d-M-yyyy","d MMMM yyyy","yyyy-MM-dd"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)
                                || date.ToString("yyyy-MM-dd")!=value)throw new InvalidDataException("Unsupported source date normalization.");
                        }
                        if(field is "area" or "amount" or "rate")
                            if(!decimal.TryParse(raw?.Replace(",",""),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number)
                                || !decimal.TryParse(value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var typed) || number!=typed)
                                throw new InvalidDataException("Unsupported source numeric normalization.");
                        if(field=="status" && section=="compensation" && fact["attribution"]?.ToString() is "PETITIONER_SUBMISSION" or "RESPONDENT_SUBMISSION" or "HISTORICAL_QUOTATION")
                            throw new InvalidDataException("A party/quoted payment claim cannot establish payment status.");
                    }
        void Walk(JsonNode? node)
        {
            if (node is JsonArray a) { foreach (var value in a) Walk(value); return; }
            if (node is not JsonObject o) return;
            foreach (var forbidden in new[] { "canonicalId", "villageId", "awardId", "khasraId", "confirmedMatch" })
                if (o.ContainsKey(forbidden)) throw new InvalidDataException("Extractor cannot provide canonical matches.");
            if (o.ContainsKey("rawText"))
            {
                var state = o["state"]?.GetValue<string>();
                if (state is not ("Explicit" or "NotStated" or "NeedsReview")) throw new InvalidDataException("Invalid Court fact state.");
                if (o["evidenceIds"] is not JsonArray refs) throw new InvalidDataException("Missing fact evidence references.");
                if (state == "NotStated" && (!complete || o["rawText"] is not null || o["value"] is not null || refs.Count != 0))
                    throw new InvalidDataException("NotStated requires complete checked evidence and no value.");
                if (state == "Explicit" && (string.IsNullOrWhiteSpace(o["rawText"]?.GetValue<string>()) || refs.Count == 0 || o["value"] is null))
                    throw new InvalidDataException("Explicit Court fact requires source evidence and value.");
                foreach (var reference in refs)
                    if (!evidence.TryGetValue(reference!.GetValue<string>(), out var passage)
                        || o["rawText"] is not null && !passage!["text"]!.GetValue<string>().Contains(o["rawText"]!.GetValue<string>(), StringComparison.Ordinal))
                        throw new InvalidDataException("Fact value is absent from its evidence.");
                    else if(o["rawText"] is not null && (passage!["attribution"]?.ToString()!=o["attribution"]?.ToString()
                        || passage["scope"]?.ToString()!=o["scope"]?.ToString()))
                        throw new InvalidDataException("Fact attribution does not match its evidence.");
                if (state == "Explicit" && o["attribution"]?.GetValue<string>() is not
                    ("COURT_DIRECTION" or "COURT_FINDING" or "COURT_OBSERVATION" or "PETITIONER_SUBMISSION" or "RESPONDENT_SUBMISSION" or "HISTORICAL_QUOTATION" or "OTHER"))
                    throw new InvalidDataException("Missing source attribution.");
            }
            foreach(var field in new[]{"villageRefs","awardRefs","parcelRefs"})
                if(o[field] is JsonArray refs)
                {
                    var targetSection=field=="villageRefs" ? "villages" : field=="awardRefs" ? "awards" : "parcels";
                    var targetIds=scope[targetSection]!.AsArray().Select(x=>x!["id"]!.GetValue<string>()).ToHashSet();
                    if(refs.Any(r=>!targetIds.Contains(r!.GetValue<string>())))throw new InvalidDataException("Dangling source entity reference.");
                }
            foreach (var value in o) Walk(value.Value);
        }
        Walk(scope);
        var relevant = scope["lacRelevant"]!.GetValue<bool>();
        var relevance = scope["lacRelevanceState"]!.GetValue<string>();
        if (relevance is not ("Relevant" or "NotRelevant" or "NeedsReview")
            || relevance == "NotRelevant" && (!complete || relevant)
            || relevant && scope["relevanceBasis"]!.AsArray().Count == 0)
            throw new InvalidDataException("Invalid LAC relevance/coverage.");
        foreach (var basis in scope["relevanceBasis"]!.AsArray())
            if (!evidence.TryGetValue(basis!["evidenceId"]!.GetValue<string>(), out var passage)
                || !Regex.IsMatch(basis["term"]!.GetValue<string>(), @"^(?:LAC|Land Acquisition Collector)$", RegexOptions.IgnoreCase)
                || !Regex.IsMatch(passage!["text"]!.GetValue<string>(), @"\b(?:LAC|Land Acquisition Collector)\b", RegexOptions.IgnoreCase))
                throw new InvalidDataException("LAC relevance needs an exact authority reference.");
            else if(basis["jurisdiction"] is not null && !passage!["text"]!.GetValue<string>().Contains(basis["jurisdiction"]!.GetValue<string>(),StringComparison.Ordinal))
                throw new InvalidDataException("LAC jurisdiction is absent from authority evidence.");
        if(relevant != (scope["relevanceBasis"]!.AsArray().Count>0))throw new InvalidDataException("LAC relevance contradicts its evidence.");
        foreach(var direction in scope["directions"]!.AsArray())
            if(direction!["lacActionable"]?.GetValue<bool>()!=false || direction["lacAuthorityScope"]?.GetValue<string>()!="UnknownLAC")
                throw new InvalidDataException("Extractor cannot authorize direction targets.");
            else if(direction["jurisdiction"] is not null && !direction["action"]!["rawText"]!.GetValue<string>().Contains(direction["jurisdiction"]!.GetValue<string>(),StringComparison.Ordinal))
                throw new InvalidDataException("Directed authority jurisdiction is absent from the source direction.");
        // Office identity is server-owned. No extractor may authorize this office.
        if (scope["lacActionable"]!.GetValue<bool>() || scope["lacAuthorityScope"]!.GetValue<string>() != "UnknownLAC")
            throw new InvalidDataException("Extractor cannot resolve this office or authorize an action.");
        foreach (var parcel in scope["parcels"]!.AsArray())
        {
            var raw = parcel!["number"]?["rawText"]?.GetValue<string>();
            if (parcel["normalizedNumber"] is not null &&
                (!new StrictKhasraParser().TryParse(raw ?? "", out var parsed, out _)
                 || parsed.NormalizedNumber != parcel["normalizedNumber"]!.GetValue<string>()
                 || parsed.Qualifier != parcel["qualifier"]?.GetValue<string>()))
                throw new InvalidDataException("Unsafe Khasra normalization.");
        }
        return scope;
    }

    public static string? Text(JsonNode? fact) => fact?["state"]?.GetValue<string>() == "Explicit"
        ? fact["value"]?.ToString() : null;
}
