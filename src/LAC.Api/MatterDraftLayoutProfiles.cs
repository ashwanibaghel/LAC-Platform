using System.Text.Json;
using LAC.Domain;

public readonly record struct MatterDraftLayout(string PageSize, string Orientation, decimal MarginTopMm, decimal MarginRightMm, decimal MarginBottomMm, decimal MarginLeftMm);

public static class MatterDraftLayoutProfiles
{
    public static MatterDraftLayoutProfile DelhiLacNotingV1 => MatterDraftOfficeProfiles.DelhiLacNotingV1;

    // The persisted left/right fields predate mirror margins. For Noting, left is inside and right is outside.
    public static MatterDraftLayout NotingLayout => ToStoredLayout(DelhiLacNotingV1);

    public static MatterDraftLayout For(MatterDraft draft) => draft.DraftType == MatterDraftType.Noting
        ? NotingLayout
        : new MatterDraftLayout(draft.PageSize, draft.Orientation, draft.MarginTopMm, draft.MarginRightMm, draft.MarginBottomMm, draft.MarginLeftMm);

    public static void ApplyDraftLayout(MatterDraft draft, MatterDraftLayout layout)
    {
        draft.PageSize = layout.PageSize;
        draft.Orientation = layout.Orientation;
        draft.MarginTopMm = layout.MarginTopMm;
        draft.MarginRightMm = layout.MarginRightMm;
        draft.MarginBottomMm = layout.MarginBottomMm;
        draft.MarginLeftMm = layout.MarginLeftMm;
    }

    public static void ApplyNotingLayout(MatterDraft draft) => ApplyDraftLayout(draft, NotingLayout);

    private static MatterDraftLayout ToStoredLayout(MatterDraftLayoutProfile profile) => new(
        profile.PageSize, profile.Orientation, profile.MarginTopMm, profile.MarginOutsideMm,
        profile.MarginBottomMm, profile.MarginInsideMm);

    public static bool TryDraftTitle(string? value, out string title, out string problem)
    {
        title = value?.Trim() ?? "";
        problem = title.Length switch
        {
            0 => "Draft title is required.",
            > 300 => "Draft title must be 300 characters or fewer.",
            _ => ""
        };
        return problem.Length == 0;
    }

    public static bool TryValidateDraftLayout(UpdateMatterDraftRequest request, MatterDraftType draftType, out MatterDraftLayout layout, out string problem)
    {
        problem = "";
        layout = default;
        if (draftType == MatterDraftType.Noting)
        {
            layout = NotingLayout;
            if (request.PageSize != layout.PageSize || request.Orientation != layout.Orientation || request.MarginTopMm != layout.MarginTopMm || request.MarginRightMm != layout.MarginRightMm || request.MarginBottomMm != layout.MarginBottomMm || request.MarginLeftMm != layout.MarginLeftMm)
            {
                problem = "Noting layout is fixed by the DelhiLacNotingV1 office profile.";
                return false;
            }
            return true;
        }
        if (request.PageSize is not ("A4" or "Legal")) problem = "Page size must be A4 or Legal.";
        else if (request.Orientation is not ("Portrait" or "Landscape")) problem = "Orientation must be Portrait or Landscape.";
        else if (new[] { request.MarginTopMm, request.MarginRightMm, request.MarginBottomMm, request.MarginLeftMm }.Any(x => x < 0 || x > 50)) problem = "Margins must be between 0 and 50 mm.";
        if (problem.Length > 0) return false;
        layout = new MatterDraftLayout(request.PageSize, request.Orientation, request.MarginTopMm, request.MarginRightMm, request.MarginBottomMm, request.MarginLeftMm);
        return true;
    }

    public static bool TryValidateDraftContent(string? contentJson, out string problem)
    {
        problem = "";
        if (string.IsNullOrWhiteSpace(contentJson)) { problem = "Structured editor content is required."; return false; }
        if (contentJson.Length > 1_000_000) { problem = "Draft content must be 1 MB or smaller."; return false; }
        try
        {
            using var document = JsonDocument.Parse(contentJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("type", out var rootType) || rootType.GetString() != "doc")
            {
                problem = "Content must be a structured editor document.";
                return false;
            }
            return TryValidateDraftNode(document.RootElement, isRoot: true, out problem);
        }
        catch (JsonException)
        {
            problem = "Content must be valid structured editor JSON.";
            return false;
        }
    }

    public static bool TryValidateDraftNode(JsonElement node, bool isRoot, out string problem)
    {
        problem = "";
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("type", out var typeValue) || typeValue.ValueKind != JsonValueKind.String)
        {
            problem = "Every editor node must have a type.";
            return false;
        }
        var type = typeValue.GetString()!;
        var allowedNodes = new HashSet<string>(StringComparer.Ordinal) { "doc", "draftPage", "paragraph", "text", "heading", "bulletList", "orderedList", "listItem", "hardBreak", "blockquote", "horizontalRule", "table", "tableRow", "tableHeader", "tableCell" };
        if (!allowedNodes.Contains(type))
        {
            problem = $"Editor node type '{type}' is not supported.";
            return false;
        }
        if (isRoot != (type == "doc"))
        {
            problem = isRoot ? "The root editor node must be a document." : "A document node is only allowed at the root.";
            return false;
        }
        foreach (var property in node.EnumerateObject())
        {
            if (property.Name is "html" or "src" or "href")
            {
                problem = "Draft content cannot include HTML or external resources.";
                return false;
            }
            if (property.Name == "content")
            {
                if (property.Value.ValueKind != JsonValueKind.Array) { problem = "Node content must be an array."; return false; }
                foreach (var child in property.Value.EnumerateArray()) if (!TryValidateDraftNode(child, false, out problem)) return false;
            }
            else if (property.Name == "marks")
            {
                if (property.Value.ValueKind != JsonValueKind.Array) { problem = "Node marks must be an array."; return false; }
                foreach (var mark in property.Value.EnumerateArray()) if (!TryValidateDraftMark(mark, out problem)) return false;
            }
            else if (property.Name == "attrs")
            {
                if (!TryValidateDraftAttributes(property.Value, type, out problem)) return false;
            }
            else if (property.Name == "text")
            {
                if (type != "text" || property.Value.ValueKind != JsonValueKind.String) { problem = "Only text nodes may contain plain text."; return false; }
            }
            else if (property.Name != "type")
            {
                problem = $"Unsupported editor node property '{property.Name}'.";
                return false;
            }
        }
        return true;
    }

    public static bool TryValidateDraftMark(JsonElement mark, out string problem)
    {
        problem = "";
        if (mark.ValueKind != JsonValueKind.Object || !mark.TryGetProperty("type", out var typeValue) || typeValue.ValueKind != JsonValueKind.String)
        {
            problem = "Every text mark must have a type.";
            return false;
        }
        if (!new HashSet<string>(StringComparer.Ordinal) { "bold", "italic", "underline", "textStyle" }.Contains(typeValue.GetString()!))
        {
            problem = $"Text mark type '{typeValue.GetString()}' is not supported.";
            return false;
        }
        foreach (var property in mark.EnumerateObject())
        {
            if (property.Name == "type") continue;
            if (property.Name == "attrs" && TryValidateDraftAttributes(property.Value, "textStyle", out problem)) continue;
            problem = $"Unsupported text mark property '{property.Name}'.";
            return false;
        }
        return true;
    }

    public static bool TryValidateDraftAttributes(JsonElement attributes, string nodeType, out string problem)
    {
        problem = "";
        if (attributes.ValueKind != JsonValueKind.Object) { problem = "Editor attributes must be an object."; return false; }
        var allowed = nodeType switch
        {
            "paragraph" or "heading" => new HashSet<string>(StringComparer.Ordinal) { "textAlign", "level" },
            "orderedList" => new HashSet<string>(StringComparer.Ordinal) { "start" },
            "textStyle" => new HashSet<string>(StringComparer.Ordinal) { "color", "fontFamily", "fontSize" },
            "tableCell" or "tableHeader" => new HashSet<string>(StringComparer.Ordinal) { "colspan", "rowspan", "colwidth", "align" },
            _ => new HashSet<string>(StringComparer.Ordinal)
        };
        foreach (var property in attributes.EnumerateObject())
        {
            if (property.Name is "src" or "href" or "html" || !allowed.Contains(property.Name))
            {
                problem = $"Editor attribute '{property.Name}' is not supported.";
                return false;
            }
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array && property.Name != "colwidth")
            {
                problem = $"Editor attribute '{property.Name}' has an invalid value.";
                return false;
            }
        }
        return true;
    }
}
