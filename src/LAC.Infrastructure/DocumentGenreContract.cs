using System.Text.Json;

namespace LAC.Infrastructure;

public sealed record DocumentGenreEvidence(int Page, string RawText, JsonElement? SourceRegion);
public sealed record DocumentGenreObservation(string Genre, decimal Confidence, bool RequiresHumanReview,
    int Page, IReadOnlyList<DocumentGenreEvidence> Evidence, IReadOnlyList<string> Warnings, string ClassifierVersion);

public static class DocumentGenreContract
{
    private static readonly HashSet<string> Genres = ["AWARD", "SUPPLEMENTARY_AWARD", "POSSESSION_PROCEEDINGS",
        "CLAIMANT_REGISTER_OR_CONTINUATION", "RENTAL_OR_REQUISITION_OFFER", "OTHER", "UNKNOWN"];

    public static DocumentGenreObservation? Read(LocalDocumentIntelligenceResult result, bool genreRouting, bool sectionObservations = false)
    {
        if (sectionObservations && !genreRouting) throw Invalid();
        if (result.ContractVersion != 2)
        {
            if (genreRouting) throw Invalid();
            return null;
        }
        if (result.Observations is not { ValueKind: JsonValueKind.Array } observations)
            throw Invalid();
        if (!genreRouting)
        {
            if (observations.GetArrayLength() != 0) throw Invalid();
            return null;
        }
        if ((sectionObservations ? observations.GetArrayLength() < 1 : observations.GetArrayLength() != 1) || result.PageCount is null or < 1) throw Invalid();
        var node = observations[0];
        if (node.ValueKind != JsonValueKind.Object || Text(node, "observationType") != "DocumentGenre") throw Invalid();
        var genre = Text(node, "genre");
        if (genre is null || !Genres.Contains(genre) || !node.TryGetProperty("confidence", out var confidenceNode) ||
            confidenceNode.ValueKind != JsonValueKind.Number || !confidenceNode.TryGetDecimal(out var confidence) ||
            confidence is < 0 or > 1 || !node.TryGetProperty("requiresHumanReview", out var reviewNode) ||
            reviewNode.ValueKind != JsonValueKind.True || !Page(node, "page", result.PageCount.Value, out var page) ||
            !node.TryGetProperty("evidence", out var evidenceNode) || evidenceNode.ValueKind != JsonValueKind.Array ||
            evidenceNode.GetArrayLength() > 8 || !node.TryGetProperty("warnings", out var warningsNode) ||
            warningsNode.ValueKind != JsonValueKind.Array || warningsNode.GetArrayLength() is < 1 or > 8)
            throw Invalid();
        var version = Text(node, "classifierVersion");
        if (string.IsNullOrWhiteSpace(version) || version.Length > 100) throw Invalid();
        var warnings = new List<string>();
        foreach (var warning in warningsNode.EnumerateArray())
        {
            if (warning.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(warning.GetString()) ||
                warning.GetString()!.Length > 500) throw Invalid();
            warnings.Add(warning.GetString()!);
        }
        var evidence = new List<DocumentGenreEvidence>();
        foreach (var item in evidenceNode.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !Page(item, "page", result.PageCount.Value, out var evidencePage))
                throw Invalid();
            var raw = Text(item, "rawText");
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > 220 || !item.TryGetProperty("sourceRegion", out var region) ||
                region.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) throw Invalid();
            if (region.ValueKind == JsonValueKind.Object)
            {
                if (!Coordinate(region, "x", false) || !Coordinate(region, "y", false) ||
                    !Coordinate(region, "width", true) || !Coordinate(region, "height", true)) throw Invalid();
            }
            evidence.Add(new(evidencePage, raw, region.ValueKind == JsonValueKind.Object ? region : null));
        }
        if (genre != "UNKNOWN" && evidence.Count == 0 || evidence.Count != 0 && evidence.All(x => x.Page != page))
            throw Invalid();
        if (genre is not ("AWARD" or "SUPPLEMENTARY_AWARD") && result.Candidates.Count != 0)
            throw new InvalidOperationException("A non-Award or unknown genre cannot contain Award extraction candidates.");
        return new(genre, confidence, true, page, evidence, warnings, version);
    }

    private static bool Coordinate(JsonElement node, string name, bool positive) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number) && double.IsFinite(number) &&
        (positive ? number > 0 : number >= 0) && number <= 20000;

    private static bool Page(JsonElement node, string name, int count, out int page)
    {
        page = 0;
        return node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out page) && page >= 1 && page <= count;
    }

    private static string? Text(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static InvalidOperationException Invalid() => new("Version 2 document genre observation is missing, malformed, or unsupported.");
}
