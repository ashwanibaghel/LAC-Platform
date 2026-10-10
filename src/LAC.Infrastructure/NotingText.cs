namespace LAC.Infrastructure;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed record NoteAnchor(int Version, int Start, int End, string Quote, string QuoteHash);
public sealed record PageRegion(double X, double Y, double Width, double Height);
public sealed record NoteCitation(NoteAnchor Anchor, Guid DocumentId, int DocumentVersion, string? DocumentHash, int? Page, PageRegion? Region);

public static class NotingText
{
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string Canonicalize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 250_000) throw new MatterWorkflowException("Note content is required; maximum 250000 characters.");
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
            var result = new StringBuilder();
            Visit(doc.RootElement, result, true, null);
            return result.ToString();
        }
        catch (JsonException) { throw new MatterWorkflowException("Invalid rich text JSON."); }
        catch (InvalidOperationException) { throw new MatterWorkflowException("Invalid rich text field type or Unicode text."); }
    }

    private static void Visit(JsonElement node, StringBuilder output, bool root, string? parent)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("type", out var value) || value.ValueKind != JsonValueKind.String)
            throw new MatterWorkflowException("Invalid rich text node.");
        var type = value.GetString();
        if (root ? type != "doc" : type is not ("paragraph" or "heading" or "bulletList" or "orderedList" or "listItem" or "blockquote" or "text" or "hardBreak"))
            throw new MatterWorkflowException("Unsupported rich text node.");
        if (type is "text" or "hardBreak" && parent is not ("paragraph" or "heading")) throw new MatterWorkflowException("Inline text needs a paragraph or heading.");
        foreach (var property in node.EnumerateObject())
            if (property.Name is not ("type" or "content" or "text" or "marks" or "attrs")) throw new MatterWorkflowException("Unsupported rich text field.");
        if (node.TryGetProperty("attrs", out var attrs))
        {
            if (type != "heading" || attrs.ValueKind != JsonValueKind.Object || attrs.EnumerateObject().Count() != 1 ||
                !attrs.TryGetProperty("level", out var level) || !level.TryGetInt32(out var n) || n is < 1 or > 6)
                throw new MatterWorkflowException("Only heading level attributes are allowed.");
        }
        if (node.TryGetProperty("marks", out var marks))
        {
            if (type != "text" || marks.ValueKind != JsonValueKind.Array) throw new MatterWorkflowException("Invalid marks.");
            foreach (var mark in marks.EnumerateArray())
                if (mark.ValueKind != JsonValueKind.Object || mark.EnumerateObject().Count() != 1 || !mark.TryGetProperty("type", out var mt) ||
                    mt.ValueKind != JsonValueKind.String || mt.GetString() is not ("bold" or "italic" or "underline" or "strike")) throw new MatterWorkflowException("Unsupported text mark.");
        }
        if (type == "text")
        {
            if (!node.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String || node.TryGetProperty("content", out _)) throw new MatterWorkflowException("Invalid text node.");
            var s = text.GetString()!;
            if (s.Any(c => char.IsControl(c) && c is not ('\n' or '\t'))) throw new MatterWorkflowException("Unsupported control character.");
            output.Append(s);
        }
        else
        {
            if (node.TryGetProperty("text", out _)) throw new MatterWorkflowException("Text belongs to inline nodes.");
            if (type == "hardBreak") output.Append('\n');
            if (node.TryGetProperty("content", out var children))
            {
                if (children.ValueKind != JsonValueKind.Array || type == "hardBreak") throw new MatterWorkflowException("Invalid child nodes.");
                foreach (var child in children.EnumerateArray()) Visit(child, output, false, type);
            }
            if (type is "paragraph" or "heading") output.Append('\n');
        }
    }

    public static void ValidateAnchor(string text, NoteAnchor anchor)
    {
        if (anchor is null || anchor.Version != 1 || anchor.Start < 0 || anchor.End <= anchor.Start || anchor.End > text.Length ||
            SplitSurrogate(text, anchor.Start) || SplitSurrogate(text, anchor.End) || text[anchor.Start..anchor.End] != anchor.Quote || Hash(anchor.Quote) != anchor.QuoteHash)
            throw new MatterWorkflowException("Anchor does not match the immutable canonical text/version/hash.");
    }
    private static bool SplitSurrogate(string text, int index) => index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]);
    public static void ValidateRegion(int? page, PageRegion? region)
    {
        if (page is <= 0 || region is not null && (page is null || !double.IsFinite(region.X) || !double.IsFinite(region.Y) ||
            !double.IsFinite(region.Width) || !double.IsFinite(region.Height) || region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 ||
            region.X + region.Width > 1 || region.Y + region.Height > 1)) throw new MatterWorkflowException("Invalid page or normalized page region.");
    }
}
