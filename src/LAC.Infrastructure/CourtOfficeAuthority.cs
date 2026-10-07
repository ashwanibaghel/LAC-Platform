using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LAC.Infrastructure;

public sealed class CourtIntelligenceOfficeOptions
{
    // Explicit office selection; never infer the office from case land or officer names.
    public Guid[] DistrictIds { get; set; } = [];
    public string[] JurisdictionAliases { get; set; } = [];
    public string[] OtherJurisdictions { get; set; } = [];
}

public sealed class CourtOfficeAuthority(LacDbContext db, IOptions<CourtIntelligenceOfficeOptions> options)
{
    private static string Normalize(string text) => Regex.Replace(text.ToLowerInvariant(), @"\bdistrict\b|[^a-z0-9]", "");
    public async Task<JsonObject> ResolveAsync(JsonObject extracted, CancellationToken ct = default)
    {
        var scope = (JsonObject)extracted.DeepClone();
        var own = (await db.Districts.AsNoTracking().Where(d => options.Value.DistrictIds.Contains(d.Id))
            .Select(d => d.Name).ToListAsync(ct)).Concat(options.Value.JurisdictionAliases).Select(Normalize).Where(n=>n.Length>0).ToHashSet();
        var others = (await db.Districts.AsNoTracking().Where(d => !options.Value.DistrictIds.Contains(d.Id))
            .Select(d => d.Name).ToListAsync(ct)).Concat(options.Value.OtherJurisdictions).Select(Normalize).Where(n=>n.Length>0).ToHashSet();
        string Resolve(string? jurisdiction) => own.Count==0 || string.IsNullOrWhiteSpace(jurisdiction) ? "UnknownLAC"
            : own.Contains(Normalize(jurisdiction)) ? "ThisOffice" : others.Contains(Normalize(jurisdiction)) ? "OtherLAC" : "UnknownLAC";
        var bases = scope["relevanceBasis"]!.AsArray().Where(b=>scope["evidence"]!.AsArray().Any(e=>e!["id"]!.GetValue<string>()==b!["evidenceId"]!.GetValue<string>()
            && e["scope"]?.GetValue<string>()=="Current" && e["attribution"]?.GetValue<string>()!="HISTORICAL_QUOTATION")).ToArray();
        var named = bases.Select(b => Resolve(b?["jurisdiction"]?.GetValue<string>())).Where(s => s != "UnknownLAC").Distinct().ToArray();
        var explicitUnknown = bases.Any(b => b?["jurisdiction"] is not null && Resolve(b["jurisdiction"]!.GetValue<string>()) == "UnknownLAC");
        var authority = named.Length == 1 && !explicitUnknown ? named[0] : "UnknownLAC";
        scope["lacAuthorityScope"] = authority;
        if (scope["lacRelevant"]!.GetValue<bool>() && authority == "UnknownLAC") scope["lacRelevanceState"] = "NeedsReview";
        var actionable = false;
        foreach (var direction in scope["directions"]!.AsArray())
        {
            var actor = CourtScopeContract.Text(direction?["directedTo"]);
            var text = CourtScopeContract.Text(direction?["action"]);
            var targetLac = actor is not null && Regex.IsMatch(actor, @"\b(?:LAC|Land Acquisition Collector)\b", RegexOptions.IgnoreCase);
            var targetScope = !targetLac ? "UnknownLAC" : direction?["jurisdiction"] is null ? authority : Resolve(direction["jurisdiction"]!.GetValue<string>());
            var operative = text is not null && actor is not null && Regex.IsMatch(text,
                Regex.Escape(actor) + @"\s*(?:\([^)]*\)\s*)?(?:(?:is|are)\s+directed|shall)\b|\blet\s+(?:the\s+)?" + Regex.Escape(actor) + @"\s+(?:file|place|furnish|produce|forward)", RegexOptions.IgnoreCase);
            var permitted = targetScope == "ThisOffice" && authority == "ThisOffice" && operative
                && direction?["modality"]?.GetValue<string>() == "Mandatory"
                && direction?["action"]?["attribution"]?.GetValue<string>() == "COURT_DIRECTION"
                && direction?["action"]?["scope"]?.GetValue<string>() == "Current"
                && scope["extraction"]?["fullRelevantTextChecked"]?.GetValue<bool>() == true;
            direction!["lacAuthorityScope"] = targetScope; direction["lacActionable"] = permitted;
            actionable |= permitted;
        }
        scope["lacActionable"] = actionable;
        return scope;
    }
}
