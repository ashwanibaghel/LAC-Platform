using System.Text.Json;
using System.Text.RegularExpressions;

namespace LAC.Infrastructure;

public sealed record TableColumnLabel(int Column, string RawText, JsonElement SourceRegion);
public sealed record KhasraSourceArea(int SourceColumn, string RawText, string Role, JsonElement SourceRegion);
public sealed record DocumentTableObservation(string TableId, int PageStart, int PageEnd, int? SectionObservationIndex,
    string? RawHeading, IReadOnlyList<TableColumnLabel> RawColumnLabels, string Layout, string Semantic,
    decimal Confidence, JsonElement SourceRegion, IReadOnlyList<DocumentGenreEvidence> Evidence,
    IReadOnlyList<string> Warnings, string ClassifierVersion);
public sealed record KhasraSourceObservation(string OccurrenceId, string TableId, int Page, int SourceRow,
    int SourceColumn, int MentionIndex, string RawKhasraText, string? NormalizedKhasraNumber, string? Qualifier,
    bool ExplicitPart, string Semantic, string RawRowContext, JsonElement SourceRegion,
    IReadOnlyList<KhasraSourceArea> AreaFields, decimal Confidence, IReadOnlyList<DocumentGenreEvidence> Evidence,
    IReadOnlyList<string> Warnings, string ClassifierVersion);
public sealed record DocumentTableObservations(IReadOnlyList<DocumentTableObservation> Tables,
    IReadOnlyList<KhasraSourceObservation> Occurrences);

public static class DocumentTableContract
{
    private static readonly HashSet<string> Semantics = ["AWARDED_LAND", "CLAIM_LINKED_LAND", "NOTIFIED_LAND", "OTHER",
        "OWNER_LINKED_LAND", "POSSESSION_LAND", "STAY_AFFECTED_LAND", "TRUE_CORRECT_AREA", "UNKNOWN_TABLE_SEMANTIC"];
    private static readonly HashSet<string> Layouts = ["SIMPLE_TWO_COLUMN", "EXPANDED_GRID", "INLINE_PARAGRAPH_LIST",
        "REPEATED_SIDE_BY_SIDE_GRID", "COMPARATIVE_MULTI_COLUMN", "OTHER", "UNKNOWN"];
    private static readonly HashSet<string> AreaRoles = ["NOTIFIED", "RECORDED", "CORRECTED", "ACQUIRED", "AWARDED",
        "CLAIMED", "STAY_AFFECTED", "POSSESSED", "OTHER", "UNKNOWN"];
    private static readonly Regex TableId = new(@"^p(?<page>[1-9]\d*)-t(?<table>[1-9]\d*)$", RegexOptions.Compiled);

    public static DocumentTableObservations ReadAll(LocalDocumentIntelligenceResult result, DocumentGenreObservation? genre,
        IReadOnlyList<DocumentSectionObservation> sections, bool enabled)
    {
        if (!enabled) return new([], []);
        if (result.ContractVersion != 2 || genre is null || result.PageCount is null or < 1 ||
            result.Observations is not { ValueKind: JsonValueKind.Array } nodes) throw Invalid();
        var tables = new List<DocumentTableObservation>();
        var occurrences = new List<KhasraSourceObservation>();
        var remaining = nodes.EnumerateArray().Skip(1 + sections.Count).ToArray();
        if (genre.Genre is not ("AWARD" or "SUPPLEMENTARY_AWARD"))
        {
            if (remaining.Length != 0) throw Invalid();
            return new(tables, occurrences);
        }
        var occurrenceStarted = false;
        foreach (var node in remaining)
        {
            var type = Text(node, "observationType");
            if (type == "DocumentTableSemantic" && !occurrenceStarted) tables.Add(ReadTable(node, result.PageCount.Value, sections));
            else if (type == "KhasraOccurrence")
            { occurrenceStarted = true; occurrences.Add(ReadOccurrence(node, result.PageCount.Value)); }
            else throw Invalid();
        }
        var byId = new Dictionary<string, DocumentTableObservation>();
        foreach (var table in tables)
            if (!byId.TryAdd(table.TableId, table)) throw Invalid();
        var occurrenceIds = new HashSet<string>();
        foreach (var item in occurrences)
        {
            if (!occurrenceIds.Add(item.OccurrenceId) || !byId.TryGetValue(item.TableId, out var table) ||
                item.Page != table.PageStart || item.Semantic != table.Semantic && item.Semantic != "UNKNOWN_TABLE_SEMANTIC" ||
                item.Semantic == "STAY_AFFECTED_LAND" && !Regex.IsMatch(item.RawRowContext,
                    @"status\s*quo\s*dispossession|stay\s+(?:order|granted|operates)", RegexOptions.IgnoreCase) ||
                !table.RawColumnLabels.Any(x => x.Column == item.SourceColumn &&
                    (x.RawText.Contains("khasra", StringComparison.OrdinalIgnoreCase) ||
                     x.RawText.Contains("killa", StringComparison.OrdinalIgnoreCase) ||
                     x.RawText.Contains("field no", StringComparison.OrdinalIgnoreCase))) ||
                item.AreaFields.Any(x => !table.RawColumnLabels.Any(c => c.Column == x.SourceColumn &&
                    c.RawText.Contains("area", StringComparison.OrdinalIgnoreCase)) ||
                    x.Role != AreaRole(table.RawColumnLabels.Single(c => c.Column == x.SourceColumn).RawText, item.Semantic) ||
                    x.SourceColumn <= item.SourceColumn ||
                    table.RawColumnLabels.Any(c => c.Column > item.SourceColumn && c.Column <= x.SourceColumn &&
                        (c.RawText.Contains("khasra", StringComparison.OrdinalIgnoreCase) ||
                         c.RawText.Contains("killa", StringComparison.OrdinalIgnoreCase) ||
                         c.RawText.Contains("field no", StringComparison.OrdinalIgnoreCase))) ||
                    !VerticalOverlap(item.SourceRegion, x.SourceRegion))) throw Invalid();
        }
        return new(tables, occurrences);
    }

    private static DocumentTableObservation ReadTable(JsonElement node, int pageCount,
        IReadOnlyList<DocumentSectionObservation> sections)
    {
        if (node.ValueKind != JsonValueKind.Object || !Int(node, "pageStart", out var page) ||
            !Int(node, "pageEnd", out var end) || page < 1 || page > pageCount || end != page ||
            !Confidence(node, out var confidence) || !Review(node) ||
            !node.TryGetProperty("sourceRegion", out var region) || !Region(region)) throw Invalid();
        var id = Text(node, "tableId");
        var match = TableId.Match(id ?? "");
        if (!match.Success || !int.TryParse(match.Groups["page"].Value, out var idPage) || idPage != page) throw Invalid();
        var semantic = Text(node, "semantic");
        var layout = Text(node, "layout");
        var heading = Text(node, "rawHeading");
        if (semantic is null || !Semantics.Contains(semantic) || layout is null || !Layouts.Contains(layout) ||
            heading?.Length > 220) throw Invalid();
        int? sectionIndex = null;
        if (node.TryGetProperty("sectionObservationIndex", out var sectionNode) && sectionNode.ValueKind == JsonValueKind.Number &&
            sectionNode.TryGetInt32(out var parsed)) sectionIndex = parsed;
        else if (!node.TryGetProperty("sectionObservationIndex", out sectionNode) || sectionNode.ValueKind != JsonValueKind.Null) throw Invalid();
        DocumentSectionObservation? section = null;
        if (sectionIndex is not null)
        {
            if (sectionIndex < 1 || sectionIndex > sections.Count) throw Invalid();
            section = sections[sectionIndex.Value - 1];
            if (section.PageStart > page || section.PageEnd < page || section.RawHeading != heading) throw Invalid();
        }
        if (semantic != "UNKNOWN_TABLE_SEMANTIC" &&
            (section is null || section.Semantic == "UNKNOWN" || string.IsNullOrWhiteSpace(heading) ||
             !Compatible(section.Semantic, semantic))) throw Invalid();
        if (!node.TryGetProperty("rawColumnLabels", out var columnsNode) || columnsNode.ValueKind != JsonValueKind.Array ||
            columnsNode.GetArrayLength() is < 1 or > 30) throw Invalid();
        var columns = new List<TableColumnLabel>();
        foreach (var cell in columnsNode.EnumerateArray())
        {
            if (!Int(cell, "column", out var index) || index < 0 || index > 100 ||
                string.IsNullOrWhiteSpace(Text(cell, "rawText")) || Text(cell, "rawText")!.Length > 120 ||
                !cell.TryGetProperty("sourceRegion", out var columnRegion) || !Region(columnRegion) ||
                columns.Any(x => x.Column == index)) throw Invalid();
            columns.Add(new(index, Text(cell, "rawText")!, columnRegion));
        }
        if (!columns.Any(x => Regex.IsMatch(x.RawText, @"\bkhasra\b|\bkilla\b|\bfield\s*(?:nos?|numbers?)\b", RegexOptions.IgnoreCase))) throw Invalid();
        var evidence = Evidence(node, section?.PageStart ?? page, page, 12);
        if (semantic != "UNKNOWN_TABLE_SEMANTIC" &&
            (!evidence.Any(x => x.RawText == heading) || !columns.Any(c => evidence.Any(x => x.RawText == c.RawText)) ||
             !HeadingSupports(heading!, semantic, columns, evidence) ||
             !evidence.Any(x => x.Page == page && x.RawText == heading) &&
             !(section is not null && section.PageStart < page &&
               evidence.Any(x => x.Page == page && Regex.IsMatch(x.RawText, @"\bcont(?:inued|d\.?)\b", RegexOptions.IgnoreCase)) &&
               section.Evidence.Any(x => x.Page == page && Regex.IsMatch(x.RawText, @"\bcont(?:inued|d\.?)\b", RegexOptions.IgnoreCase))))) throw Invalid();
        if (semantic != "UNKNOWN_TABLE_SEMANTIC" &&
            (section is null || !HasSourceOrderAnchor(section, evidence, page, region))) throw Invalid();
        return new(id!, page, end, sectionIndex, heading, columns, layout, semantic, confidence, region,
            evidence, Warnings(node), Version(node));
    }

    private static KhasraSourceObservation ReadOccurrence(JsonElement node, int pageCount)
    {
        if (node.ValueKind != JsonValueKind.Object || !Int(node, "page", out var page) || page < 1 || page > pageCount ||
            !Int(node, "sourceRow", out var row) || row < 0 || !Int(node, "sourceColumn", out var column) || column < 0 ||
            !Int(node, "mentionIndex", out var mention) || mention < 1 || mention > 100 ||
            !Confidence(node, out var confidence) || !Review(node) ||
            !node.TryGetProperty("sourceRegion", out var region) || !Region(region) ||
            !node.TryGetProperty("explicitPart", out var partNode) || partNode.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid();
        var id = Text(node, "occurrenceId");
        var tableId = Text(node, "tableId");
        var raw = Text(node, "rawKhasraText");
        var normalized = Text(node, "normalizedKhasraNumber");
        var qualifier = Text(node, "qualifier");
        var semantic = Text(node, "semantic");
        var context = Text(node, "rawRowContext");
        if (tableId is null || !TableId.IsMatch(tableId) || id != $"{tableId}-r{row}-c{column}-m{mention}" ||
            string.IsNullOrWhiteSpace(raw) || raw.Length > 100 || string.IsNullOrWhiteSpace(context) ||
            context.Length > 500 || !context.Contains(raw, StringComparison.Ordinal) ||
            semantic is null || !Semantics.Contains(semantic)) throw Invalid();
        var part = partNode.ValueKind == JsonValueKind.True;
        if (part != Regex.IsMatch(raw, @"\bpart\b", RegexOptions.IgnoreCase)) throw Invalid();
        var parser = new StrictKhasraParser();
        var parsed = parser.TryParse(raw, out var exact, out _);
        if (normalized is not null && (!parsed || normalized != exact.NormalizedNumber || qualifier != exact.Qualifier || part) ||
            qualifier is not null && (!parsed || qualifier != exact.Qualifier)) throw Invalid();
        if (!node.TryGetProperty("areaFields", out var areasNode) || areasNode.ValueKind != JsonValueKind.Array ||
            areasNode.GetArrayLength() > 8) throw Invalid();
        var areas = new List<KhasraSourceArea>();
        foreach (var area in areasNode.EnumerateArray())
        {
            if (!Int(area, "sourceColumn", out var areaColumn) || areaColumn < 0 ||
                string.IsNullOrWhiteSpace(Text(area, "rawText")) || Text(area, "rawText")!.Length > 100 ||
                Text(area, "role") is not string role || !AreaRoles.Contains(role) ||
                !area.TryGetProperty("sourceRegion", out var areaRegion) || !Region(areaRegion)) throw Invalid();
            areas.Add(new(areaColumn, Text(area, "rawText")!, role, areaRegion));
        }
        var evidence = Evidence(node, page, page, 4);
        if (!evidence.Any(x => x.RawText == raw)) throw Invalid();
        return new(id!, tableId, page, row, column, mention, raw, normalized, qualifier, part, semantic,
            context, region, areas, confidence, evidence, Warnings(node), Version(node));
    }

    private static bool Compatible(string section, string semantic) => (section, semantic) switch
    {
        ("LAND_SCHEDULE", "AWARDED_LAND" or "TRUE_CORRECT_AREA") => true,
        ("POSSESSION_REFERENCE_OR_SECTION", "POSSESSION_LAND") => true,
        ("STATUTORY_NOTIFICATIONS", "NOTIFIED_LAND") => true,
        ("CLAIMS", "CLAIM_LINKED_LAND") => true,
        ("OTHER", "OWNER_LINKED_LAND") => true,
        ("COURT_OR_DISPUTE_REFERENCE", "STAY_AFFECTED_LAND") => true,
        (_, "OTHER") => true,
        _ => false
    };
    private static bool HasSourceOrderAnchor(DocumentSectionObservation section,
        IReadOnlyList<DocumentGenreEvidence> tableEvidence, int page, JsonElement tableRegion)
    {
        bool IsCue(DocumentGenreEvidence item) => item.Page == page &&
            (section.PageStart == page ? item.RawText == section.RawHeading :
                Regex.IsMatch(item.RawText, @"\bcont(?:inued|d\.?)\b", RegexOptions.IgnoreCase));

        return section.Evidence.Where(IsCue).Any(source =>
            source.SourceRegion is JsonElement sourceRegion && RegionPrecedes(sourceRegion, tableRegion) &&
            tableEvidence.Where(IsCue).Any(copy =>
                copy.RawText == source.RawText && copy.SourceRegion is JsonElement copyRegion &&
                SameRegion(sourceRegion, copyRegion)));
    }
    private static bool RegionPrecedes(JsonElement cue, JsonElement table) =>
        cue.GetProperty("y").GetDouble() + cue.GetProperty("height").GetDouble() <= table.GetProperty("y").GetDouble();
    private static bool SameRegion(JsonElement left, JsonElement right) =>
        new[] { "x", "y", "width", "height" }.All(key =>
            left.GetProperty(key).GetDouble() == right.GetProperty(key).GetDouble());
    private static bool HeadingSupports(string heading, string semantic, IReadOnlyList<TableColumnLabel> columns,
        IReadOnlyList<DocumentGenreEvidence> evidence)
    {
        var h = heading.ToLowerInvariant();
        var labels = string.Join(" ", columns.Select(x => x.RawText)).ToLowerInvariant();
        return semantic switch
        {
            "AWARDED_LAND" => Regex.IsMatch(h.Trim(' ', '.', ':', '-'), @"^(?:annexure\s*[- ]?[a-z]\s+)?land awarded$"),
            "POSSESSION_LAND" => h.Contains("land awarded and taken over") || h.Contains("possession"),
            "TRUE_CORRECT_AREA" => h.Contains("true and correct area") && labels.Contains("notification") && labels.Contains("field book"),
            "NOTIFIED_LAND" => h.Contains("notification") && h.Contains("specification") &&
                               Regex.IsMatch(labels, @"field\s*(?:nos?|numbers?)|boundar"),
            "CLAIM_LINKED_LAND" => h.Contains("claim") && labels.Contains("claimant"),
            "OWNER_LINKED_LAND" => (h.Contains("owner") || h.Contains("ownership")) && labels.Contains("owner") &&
                                   (labels.Contains("occupant") || labels.Contains("soil")),
            "STAY_AFFECTED_LAND" => (h.Contains("cwp") || h.Contains("court")) && labels.Contains("status") &&
                                    (labels.Contains("cwp") || labels.Contains("case")) &&
                                    evidence.Any(x => Regex.IsMatch(x.RawText, @"status\s*quo\s*dispossession|stay\s+(?:order|granted|operates)", RegexOptions.IgnoreCase)),
            "OTHER" => true,
            _ => false
        };
    }
    private static string AreaRole(string header, string semantic)
    {
        var h = header.ToLowerInvariant();
        if (h.Contains("field book") && semantic == "TRUE_CORRECT_AREA") return "CORRECTED";
        if (h.Contains("notification") && (semantic is "TRUE_CORRECT_AREA" or "NOTIFIED_LAND")) return "NOTIFIED";
        if (h.Contains("awarded")) return "AWARDED";
        if (h.Contains("recorded") || h.Contains("total area") && semantic == "AWARDED_LAND") return "RECORDED";
        if (h.Contains("possession") || h.Contains("taken over")) return "POSSESSED";
        if (semantic == "CLAIM_LINKED_LAND" && h.Trim() == "area") return "CLAIMED";
        return "UNKNOWN";
    }
    private static IReadOnlyList<DocumentGenreEvidence> Evidence(JsonElement node, int firstPage, int lastPage, int max)
    {
        if (!node.TryGetProperty("evidence", out var list) || list.ValueKind != JsonValueKind.Array ||
            list.GetArrayLength() is < 1 || list.GetArrayLength() > max) throw Invalid();
        var evidence = new List<DocumentGenreEvidence>();
        foreach (var item in list.EnumerateArray())
        {
            if (!Int(item, "page", out var found) || found < firstPage || found > lastPage ||
                string.IsNullOrWhiteSpace(Text(item, "rawText")) ||
                Text(item, "rawText")!.Length > 220 || !item.TryGetProperty("sourceRegion", out var box) ||
                box.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object) ||
                box.ValueKind == JsonValueKind.Object && !Region(box)) throw Invalid();
            evidence.Add(new(found, Text(item, "rawText")!, box.ValueKind == JsonValueKind.Object ? box : null));
        }
        return evidence;
    }
    private static IReadOnlyList<string> Warnings(JsonElement node)
    {
        if (!node.TryGetProperty("warnings", out var list) || list.ValueKind != JsonValueKind.Array ||
            list.GetArrayLength() is < 1 or > 8) throw Invalid();
        var values = new List<string>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()) ||
                item.GetString()!.Length > 500) throw Invalid();
            values.Add(item.GetString()!);
        }
        return values;
    }
    private static string Version(JsonElement node)
    {
        var value = Text(node, "classifierVersion");
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100) throw Invalid();
        return value;
    }
    private static bool Region(JsonElement node) => node.ValueKind == JsonValueKind.Object &&
        Coord(node, "x", false) && Coord(node, "y", false) && Coord(node, "width", true) && Coord(node, "height", true);
    private static bool VerticalOverlap(JsonElement left, JsonElement right) =>
        left.GetProperty("y").GetDouble() <= right.GetProperty("y").GetDouble() + right.GetProperty("height").GetDouble() + 5 &&
        right.GetProperty("y").GetDouble() <= left.GetProperty("y").GetDouble() + left.GetProperty("height").GetDouble() + 5;
    private static bool Coord(JsonElement node, string key, bool positive) => node.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) &&
        (positive ? number > 0 : number >= 0) && number <= 20000;
    private static bool Int(JsonElement node, string key, out int value)
    { value = 0; return node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var found) &&
        found.ValueKind == JsonValueKind.Number && found.TryGetInt32(out value); }
    private static bool Confidence(JsonElement node, out decimal value)
    { value = 0; return node.TryGetProperty("confidence", out var found) && found.ValueKind == JsonValueKind.Number &&
        found.TryGetDecimal(out value) && value is >= 0 and <= 1; }
    private static bool Review(JsonElement node) => node.TryGetProperty("requiresHumanReview", out var value) &&
        value.ValueKind == JsonValueKind.True;
    private static string? Text(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object &&
        node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static InvalidOperationException Invalid() => new("Version 2 table semantic or Khasra source occurrence is missing, malformed, or unsupported.");
}
