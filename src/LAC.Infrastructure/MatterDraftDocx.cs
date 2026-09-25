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
    private const uint A4WidthTwips = 11906;
    private const uint A4HeightTwips = 16838;
    private const uint LegalWidthTwips = 12240;
    private const uint LegalHeightTwips = 20160;

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
            uint width = draft.PageSize == "Legal" ? LegalWidthTwips : A4WidthTwips;
            uint height = draft.PageSize == "Legal" ? LegalHeightTwips : A4HeightTwips;
            var landscape = draft.Orientation == "Landscape";
            body.Append(new SectionProperties(
                new PageSize { Width = landscape ? height : width, Height = landscape ? width : height,
                    Orient = landscape ? PageOrientationValues.Landscape : PageOrientationValues.Portrait },
                new PageMargin { Top = Twips(draft.MarginTopMm), Right = (uint)Twips(draft.MarginRightMm),
                    Bottom = Twips(draft.MarginBottomMm), Left = (uint)Twips(draft.MarginLeftMm), Header = 720U, Footer = 720U, Gutter = 0U }));
            main.Document = new WordDocument(body);
            if (noting) ApplyNotingLayout(package);
            main.Document.Save();
        }
        output.Position = 0;
        return output;
    }

    private static int Twips(decimal mm) => (int)Math.Round(mm * 1440m / 25.4m);

    /// <summary>Restores the official Noting page policy without touching document content or headers/footers.</summary>
    public static void EnforceNotingLayout(Stream stream)
    {
        stream.Position = 0;
        using (var package = WordprocessingDocument.Open(stream, true)) ApplyNotingLayout(package);
        stream.Position = 0;
    }

    private static void ApplyNotingLayout(WordprocessingDocument package)
    {
        var main = package.MainDocumentPart ?? throw new InvalidDataException("DOCX has no main document part.");
        var document = main.Document ?? throw new InvalidDataException("DOCX has no main document.");
        var body = document.Body ?? throw new InvalidDataException("DOCX has no body.");
        var profile = MatterDraftOfficeProfiles.DelhiLacNotingV1;
        var sections = document.Descendants<SectionProperties>().ToList();
        if (sections.Count == 0)
        {
            var section = new SectionProperties();
            body.Append(section);
            sections.Add(section);
        }
        foreach (var section in sections)
        {
            section.GetFirstChild<PageSize>()?.Remove();
            var existingMargin = section.GetFirstChild<PageMargin>();
            var header = existingMargin?.Header?.Value ?? 720U;
            var footer = existingMargin?.Footer?.Value ?? 720U;
            existingMargin?.Remove();
            section.PrependChild(new PageMargin
            {
                Top = Twips(profile.MarginTopMm), Right = (uint)Twips(profile.MarginOutsideMm),
                Bottom = Twips(profile.MarginBottomMm), Left = (uint)Twips(profile.MarginInsideMm),
                Gutter = (uint)Twips(profile.GutterMm), Header = header, Footer = footer
            });
            section.PrependChild(new PageSize { Width = LegalWidthTwips, Height = LegalHeightTwips, Orient = PageOrientationValues.Portrait });
        }
        var settings = main.DocumentSettingsPart ?? main.AddNewPart<DocumentSettingsPart>();
        settings.Settings ??= new Settings();
        var existingMirrorMargins = settings.Settings.GetFirstChild<MirrorMargins>();
        if (existingMirrorMargins is null) settings.Settings.Append(new MirrorMargins());
        else settings.Settings.ReplaceChild(new MirrorMargins(), existingMirrorMargins);
        settings.Settings.Save();
        document.Save();
    }

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
