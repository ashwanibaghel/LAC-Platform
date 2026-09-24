using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LAC.Domain;
using WordDocument = DocumentFormat.OpenXml.Wordprocessing.Document;

namespace LAC.Infrastructure;

public static class MatterDraftDocx
{
    public const string MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public static MemoryStream Create(MatterDraft draft)
    {
        var output = new MemoryStream();
        using (var package = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document, true))
        {
            var main = package.AddMainDocumentPart();
            var body = new Body();
            using var json = JsonDocument.Parse(draft.ContentJson);
            AppendParagraphs(json.RootElement, body);
            if (!body.HasChildren) body.Append(new Paragraph());
            var noting = draft.DraftType == MatterDraftType.Noting;
            uint width = !noting && draft.PageSize == "Legal" ? 12240U : 11906U;
            uint height = !noting && draft.PageSize == "Legal" ? 20160U : 16838U;
            var landscape = !noting && draft.Orientation == "Landscape";
            body.Append(new SectionProperties(
                new PageSize { Width = landscape ? height : width, Height = landscape ? width : height,
                    Orient = landscape ? PageOrientationValues.Landscape : PageOrientationValues.Portrait },
                new PageMargin { Top = Twips(noting ? 25 : draft.MarginTopMm),
                    Right = (uint)Twips(noting ? 20 : draft.MarginRightMm),
                    Bottom = Twips(noting ? 20 : draft.MarginBottomMm),
                    Left = (uint)Twips(noting ? 25 : draft.MarginLeftMm), Header = 720U, Footer = 720U, Gutter = 0U }));
            main.Document = new WordDocument(body);
            main.Document.Save();
        }
        output.Position = 0;
        return output;
    }

    private static int Twips(decimal mm) => (int)Math.Round(mm * 1440m / 25.4m);

    // Flatten legacy lists/tables into their paragraphs; preserve text, never synthesize it.
    private static void AppendParagraphs(JsonElement node, Body body)
    {
        var type = node.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type is "paragraph" or "heading" || type == "text")
        {
            var paragraph = new Paragraph();
            AppendText(node, paragraph);
            body.Append(paragraph);
        }
        else if (node.TryGetProperty("content", out var children))
            foreach (var child in children.EnumerateArray()) AppendParagraphs(child, body);
    }

    private static void AppendText(JsonElement node, Paragraph paragraph)
    {
        if (node.TryGetProperty("text", out var text))
            paragraph.Append(new Run(new Text(text.GetString() ?? "") { Space = SpaceProcessingModeValues.Preserve }));
        else if (node.TryGetProperty("type", out var type) && type.GetString() == "hardBreak")
            paragraph.Append(new Run(new Break()));
        if (node.TryGetProperty("content", out var children))
            foreach (var child in children.EnumerateArray()) AppendText(child, paragraph);
    }
}
