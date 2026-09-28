using System.Text.Json;

namespace LAC.Infrastructure;

public sealed record DocumentSectionObservation(string Semantic, string Presentation, int PageStart, int PageEnd,
    string? RawHeading, decimal Confidence, bool RequiresHumanReview, IReadOnlyList<DocumentGenreEvidence> Evidence,
    IReadOnlyList<string> Warnings, string ClassifierVersion);

public static class DocumentSectionContract
{
    private static readonly HashSet<string> Semantics = ["AWARD_IDENTITY", "STATUTORY_NOTIFICATIONS", "CLAIMS",
        "VALUATION", "COMPENSATION_CALCULATION", "LAND_SCHEDULE", "POSSESSION_REFERENCE_OR_SECTION",
        "COURT_OR_DISPUTE_REFERENCE", "APPORTIONMENT_OR_ENTITLEMENT", "SUPPLEMENTARY_MATTER", "OTHER", "UNKNOWN"];
    private static readonly HashSet<string> Presentations = ["NARRATIVE", "TABLE", "SCHEDULE", "ANNEXURE", "MIXED", "UNKNOWN"];

    public static IReadOnlyList<DocumentSectionObservation> ReadAll(LocalDocumentIntelligenceResult result,
        DocumentGenreObservation? genre, bool enabled)
    {
        if (!enabled) return [];
        if (result.ContractVersion != 2 || genre is null || result.PageCount is null or < 1 ||
            result.Observations is not { ValueKind: JsonValueKind.Array } observations) throw Invalid();
        if (genre.Genre is not ("AWARD" or "SUPPLEMENTARY_AWARD"))
        {
            if (observations.GetArrayLength() != 1) throw Invalid();
            return [];
        }
        var sections = new List<DocumentSectionObservation>();
        var covered = new HashSet<int>();
        foreach (var node in observations.EnumerateArray().Skip(1))
        {
            if (node.ValueKind != JsonValueKind.Object || Text(node, "observationType") != "DocumentSection" ||
                !Number(node, "pageStart", out var start) || !Number(node, "pageEnd", out var end) ||
                start < 1 || end < start || end > result.PageCount ||
                !Decimal(node, "confidence", out var confidence) || confidence is < 0 or > 1 ||
                !node.TryGetProperty("requiresHumanReview", out var review) || review.ValueKind != JsonValueKind.True)
                throw Invalid();
            var semantic = Text(node, "semantic");
            var presentation = Text(node, "presentation");
            var heading = Text(node, "rawHeading");
            var version = Text(node, "classifierVersion");
            if (semantic is null || !Semantics.Contains(semantic) || presentation is null || !Presentations.Contains(presentation) ||
                string.IsNullOrWhiteSpace(version) || version.Length > 100 || heading?.Length > 220 ||
                semantic != "UNKNOWN" && string.IsNullOrWhiteSpace(heading) ||
                !node.TryGetProperty("evidence", out var evidenceNode) || evidenceNode.ValueKind != JsonValueKind.Array ||
                evidenceNode.GetArrayLength() > 12 || !node.TryGetProperty("warnings", out var warningsNode) ||
                warningsNode.ValueKind != JsonValueKind.Array || warningsNode.GetArrayLength() is < 1 or > 8) throw Invalid();
            var warnings = new List<string>();
            foreach (var warning in warningsNode.EnumerateArray())
            {
                if (warning.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(warning.GetString()) || warning.GetString()!.Length > 500) throw Invalid();
                warnings.Add(warning.GetString()!);
            }
            var evidence = new List<DocumentGenreEvidence>();
            foreach (var item in evidenceNode.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !Number(item, "page", out var page) ||
                    page < start || page > end || string.IsNullOrWhiteSpace(Text(item, "rawText")) ||
                    Text(item, "rawText")!.Length > 220 || !item.TryGetProperty("sourceRegion", out var region) ||
                    region.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) throw Invalid();
                if (region.ValueKind == JsonValueKind.Object &&
                    (!Coordinate(region, "x", false) || !Coordinate(region, "y", false) ||
                     !Coordinate(region, "width", true) || !Coordinate(region, "height", true))) throw Invalid();
                evidence.Add(new(page, Text(item, "rawText")!, region.ValueKind == JsonValueKind.Object ? region : null));
            }
            if (semantic != "UNKNOWN" && (evidence.Count == 0 || !evidence.Any(x => x.Page == start && x.RawText == heading))) throw Invalid();
            for (var page = start; page <= end; page++) covered.Add(page);
            sections.Add(new(semantic, presentation, start, end, heading, confidence, true, evidence, warnings, version!));
        }
        if (Enumerable.Range(1, result.PageCount.Value).Any(page => !covered.Contains(page))) throw Invalid();
        return sections;
    }

    private static string? Text(JsonElement node, string key) => node.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Number(JsonElement node, string key, out int number)
    { number = 0; return node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number); }
    private static bool Decimal(JsonElement node, string key, out decimal number)
    { number = 0; return node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out number); }
    private static bool Coordinate(JsonElement node, string key, bool positive) => node.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) &&
        (positive ? number > 0 : number >= 0) && number <= 20000;
    private static InvalidOperationException Invalid() => new("Version 2 document section observation is missing, malformed, or unsupported.");
}
