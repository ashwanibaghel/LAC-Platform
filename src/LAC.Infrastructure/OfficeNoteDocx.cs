namespace LAC.Infrastructure;

using System.IO.Compression;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LAC.Domain;
using WDocument = DocumentFormat.OpenXml.Wordprocessing.Document;

public sealed record ConvertedOfficeNote(string ContentJson, string CanonicalText, string CanonicalHash);

public static class OfficeNoteDocx
{
    public const string ConverterVersion = "OfficeNoteV1";
    public const string FontPolicy = "LiberationSerif12-v1";
    public const int MaxBytes = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static MatterWorkflowException Unsupported(string name) => new($"OfficeNoteV1 does not support {name}. Remove it and save again; no content was imported.", 422);

    public static ConvertedOfficeNote Convert(Stream stream)
    {
        if (stream.Length > MaxBytes || stream.Length == 0) throw Unsupported("an empty or oversized DOCX");
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
            if (zip.Entries.Count > 1000 || zip.Entries.Sum(x => x.Length) > 20 * 1024 * 1024) throw Unsupported("an oversized package");
        stream.Position = 0;
        try
        {
            using var package = WordprocessingDocument.Open(stream, false, new OpenSettings { MaxCharactersInPart = 1_000_000 });
            var main = package.MainDocumentPart ?? throw Unsupported("a missing document part");
            if (package.DocumentType != WordprocessingDocumentType.Document || main.VbaProjectPart is not null ||
                main.HeaderParts.Any() || main.FooterParts.Any() || HasActualNotes(main.FootnotesPart?.Footnotes) || HasActualNotes(main.EndnotesPart?.Endnotes) ||
                main.WordprocessingCommentsPart is not null || main.CustomXmlParts.Any())
                throw Unsupported("macros, headers, footers, footnotes or endnotes");
            if (main.Document?.GetFirstChild<DocumentBackground>() is not null) throw Unsupported("document backgrounds");
            CheckRelations(package);
            var normal = main.StyleDefinitionsPart?.Styles?.Elements<Style>().FirstOrDefault(x => x.Type?.Value == StyleValues.Paragraph && x.Default?.Value == true);
            CheckInherited(normal?.StyleRunProperties);
            CheckInherited(main.StyleDefinitionsPart?.Styles?.GetFirstChild<DocDefaults>()?.GetFirstChild<RunPropertiesDefault>()?.GetFirstChild<RunPropertiesBaseStyle>());
            var body = main.Document?.Body ?? throw Unsupported("a missing body");
            var section = body.Elements<SectionProperties>().SingleOrDefault();
            var page = section?.GetFirstChild<PageSize>(); var margins = section?.GetFirstChild<PageMargin>();
            var mirror = main.DocumentSettingsPart?.Settings?.GetFirstChild<MirrorMargins>();
            foreach (var property in section?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
                if (property is not (PageSize or PageMargin or Columns or SectionType) &&
                    !(property is FootnoteProperties or EndnoteProperties && !property.HasChildren)) throw Unsupported(property.LocalName);
            if (page?.Width?.Value != 12240U || page.Height?.Value != 20160U || page.Orient?.Value != PageOrientationValues.Portrait ||
                margins?.Left?.Value != 2551U || margins.Right?.Value != 850U || margins.Top?.Value != 1417 || margins.Bottom?.Value != 1417 ||
                margins.Gutter?.Value is not (null or 0U) || mirror is null || mirror.Val?.Value == false ||
                section?.GetFirstChild<Columns>()?.ColumnCount?.Value is > 1)
                throw Unsupported("layout outside GreenLegalV2");
            var paragraphs = new List<object>();
            foreach (var block in body.ChildElements)
            {
                if (block is SectionProperties) continue;
                if (block is not Paragraph p) throw Unsupported(block.LocalName);
                ValidateParagraph(p, main);
                var inline = new List<object>();
                foreach (var child in p.ChildElements)
                {
                    if (child is ParagraphProperties or BookmarkStart or BookmarkEnd or ProofError) continue;
                    if (child is not Run run) throw Unsupported(child.LocalName);
                    var marks = Marks(run.RunProperties);
                    foreach (var item in run.ChildElements)
                    {
                        if (item is RunProperties) continue;
                        if (item is Text t) inline.Add(new { type = "text", text = t.Text, marks });
                        else if (item is TabChar) inline.Add(new { type = "text", text = "\t", marks });
                        else if (item is Break br && (br.Type is null || br.Type.Value == BreakValues.TextWrapping)) inline.Add(new { type = "hardBreak" });
                        else if (item is LastRenderedPageBreak) { /* Non-semantic cached layout hint. */ }
                        else throw Unsupported(item.LocalName);
                    }
                }
                paragraphs.Add(new { type = "paragraph", content = inline });
            }
            var content = JsonSerializer.Serialize(new { type = "doc", content = paragraphs }, Json);
            var text = NotingText.Canonicalize(content);
            return new(content, text, NotingText.Hash(text));
        }
        catch (MatterWorkflowException) { throw; }
        catch (Exception ex) when (ex is InvalidDataException or OpenXmlPackageException or System.Xml.XmlException or ArgumentException)
        { throw Unsupported("an invalid DOCX package"); }
    }

    private static void CheckRelations(OpenXmlPartContainer container)
    {
        if (container.ExternalRelationships.Any() || container.HyperlinkRelationships.Any(x => x.IsExternal)) throw Unsupported("external relationships");
        foreach (var part in container.Parts) CheckRelations(part.OpenXmlPart);
    }

    // Docs 9.4 emits reserved separator parts even when the document has no notes.
    // Permit ONLY those non-text reserved records; any real note/reference remains unsupported.
    private static bool HasActualNotes(OpenXmlElement? root) => root?.ChildElements.Any(n =>
        n.GetAttribute("id", "http://schemas.openxmlformats.org/wordprocessingml/2006/main").Value is not ("-1" or "0") ||
        n.GetAttribute("type", "http://schemas.openxmlformats.org/wordprocessingml/2006/main").Value is not ("separator" or "continuationSeparator") ||
        n.Descendants<Text>().Any() || n.Descendants().Any(x => x is not (Paragraph or ParagraphProperties or ParagraphBorders or SpacingBetweenLines or Indentation or ParagraphMarkRunProperties or DocumentFormat.OpenXml.Wordprocessing.Run or RunProperties or SeparatorMark or ContinuationSeparatorMark))) == true;

    private static void ValidateParagraph(Paragraph p, MainDocumentPart main)
    {
        foreach (var property in p.ParagraphProperties?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
        {
            if (property is ParagraphStyleId style)
            {
                var definition = main.StyleDefinitionsPart?.Styles?.Elements<Style>().FirstOrDefault(x => x.StyleId == style.Val);
                if (style.Val?.Value is not ("Normal" or "normal") && definition?.StyleName?.Val?.Value != "Normal") throw Unsupported("paragraph styles other than Normal");
                if (definition?.StyleRunProperties?.Elements().Any(x => x is Bold or Italic or Underline or Strike) == true)
                    throw Unsupported("inherited text marks");
                if (definition?.BasedOn is not null) throw Unsupported("inherited paragraph styles");
                CheckInherited(definition?.StyleRunProperties);
            }
            else if (property is Justification j && j.Val?.Value != JustificationValues.Left) throw Unsupported("paragraph alignment other than left");
            else if (property is Indentation i && i.GetAttributes().Any(a => a.Value is not ("0" or ""))) throw Unsupported("nonzero paragraph indentation");
            else if (property is ParagraphBorders borders && !borders.HasChildren) { }
            else if (property is not (SpacingBetweenLines or ParagraphMarkRunProperties or KeepNext or KeepLines or WidowControl or ContextualSpacing or Justification or Indentation))
                throw Unsupported(property.LocalName);
        }
    }

    private static void CheckInherited(OpenXmlElement? properties)
    {
        if (properties is null) return;
        var inherited = new RunProperties(properties.ChildElements.Select(x => x.CloneNode(true)));
        if (Marks(inherited).Length > 0) throw Unsupported("inherited text marks");
    }

    private static object[] Marks(RunProperties? properties)
    {
        var marks = new List<object>();
        foreach (var p in properties?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
        {
            if (p is Bold b) { if (b.Val is null || b.Val.Value) marks.Add(new { type = "bold" }); }
            else if (p is Italic i) { if (i.Val is null || i.Val.Value) marks.Add(new { type = "italic" }); }
            else if (p is Underline u) { if (u.Val is null || u.Val.Value == UnderlineValues.Single) marks.Add(new { type = "underline" }); else if (u.Val.Value != UnderlineValues.None) throw Unsupported("underline styles other than single"); }
            else if (p is Strike s) { if (s.Val is null || s.Val.Value) marks.Add(new { type = "strike" }); }
            else if (p is RunFonts f && (f.Ascii?.Value is { } a && a != "Liberation Serif" || f.HighAnsi?.Value is { } h && h != "Liberation Serif" ||
                f.EastAsia?.Value is { } e && e != "Liberation Serif" || f.ComplexScript?.Value is { } c && c != "Liberation Serif" ||
                f.AsciiTheme is not null || f.HighAnsiTheme is not null || f.EastAsiaTheme is not null || f.ComplexScriptTheme is not null)) throw Unsupported("unapproved fonts");
            else if (p is FontSize size && size.Val?.Value != "24") throw Unsupported("font sizes other than 12 pt");
            else if (p is FontSizeComplexScript cs && cs.Val?.Value != "24") throw Unsupported("complex-script font sizes other than 12 pt");
            else if (p is Color color && color.Val?.Value is not ("000000" or "auto")) throw Unsupported("colored text");
            else if (p is Highlight highlight && highlight.Val?.Value == HighlightColorValues.None) { }
            else if (p is not (RunFonts or FontSize or FontSizeComplexScript or Languages or NoProof or Color)) throw Unsupported(p.LocalName);
        }
        return marks.ToArray();
    }

    public static MemoryStream Create(string contentJson) => Compose([new GreenNoteInput(null, null, null, null, contentJson)]);

    public static MemoryStream Compose(IReadOnlyList<GreenNoteInput> notes)
    {
        var output = new MemoryStream();
        using (var package = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document, true))
        {
            var main = package.AddMainDocumentPart();
            package.ChangeIdOfPart(main, "rId1");
            var body = new Body();
            foreach (var note in notes)
            {
                NotingText.Canonicalize(note.ContentJson);
                if (note.Number is not null)
                    body.Append(new Paragraph(new ParagraphProperties(new KeepNext(), Spacing()),
                        Run($"{(note.Working ? "UNSUBMITTED working note" : "Note " + note.Number)} — {note.AuthorName} ({note.Designation}) — {note.SubmittedAt:yyyy-MM-dd HH:mm} UTC", ["bold"])));
                using var json = JsonDocument.Parse(note.ContentJson);
                Append(json.RootElement, body);
                if (note.Number is not null)
                {
                    // One unsplit reservation; no KeepNext/page break at an officer boundary.
                    body.Append(new Paragraph(new ParagraphProperties(new KeepLines(), new SpacingBetweenLines { Before = "720", After = "720", Line = "360", LineRule = LineSpacingRuleValues.Auto }),
                        Run($"Handwritten signature / {note.Designation} / date / marking", []), new Run(new Break()), new Run(new Break())));
                }
            }
            if (!body.HasChildren) body.Append(new Paragraph(Run("", [])));
            body.Append(new SectionProperties(new PageSize { Width = 12240U, Height = 20160U, Orient = PageOrientationValues.Portrait },
                new PageMargin { Left = 2551U, Right = 850U, Top = 1417, Bottom = 1417, Gutter = 0U, Header = 0U, Footer = 0U }));
            main.Document = new WDocument(body);
            var settings = main.AddNewPart<DocumentSettingsPart>("rId2");
            settings.Settings = new Settings(new MirrorMargins()); settings.Settings.Save();
            var styles = main.AddNewPart<StyleDefinitionsPart>("rId3");
            styles.Styles = new Styles(new DocDefaults(new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { Ascii = "Liberation Serif", HighAnsi = "Liberation Serif", EastAsia = "Liberation Serif", ComplexScript = "Liberation Serif" },
                new FontSize { Val = "24" }, new FontSizeComplexScript { Val = "24" }))));
            styles.Styles.Save();
            main.Document.Save();
        }
        output.Position = 0;
        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, true))
            foreach (var entry in archive.Entries) entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        output.Position = 0;
        return output;
    }

    private static SpacingBetweenLines Spacing() => new() { After = "120", Line = "360", LineRule = LineSpacingRuleValues.Auto };
    private static Run Run(string text, IReadOnlyList<string> marks)
    {
        var properties = new RunProperties(new RunFonts { Ascii = "Liberation Serif", HighAnsi = "Liberation Serif" }, new FontSize { Val = "24" });
        foreach (var mark in marks) properties.Append(mark switch { "bold" => new Bold(), "italic" => new Italic(), "underline" => new Underline { Val = UnderlineValues.Single }, "strike" => new Strike(), _ => throw Unsupported(mark) });
        var result = new Run(properties);
        foreach (var part in System.Text.RegularExpressions.Regex.Split(text, "([\\n\\t])"))
            result.Append(part == "\n" ? new Break() : part == "\t" ? new TabChar() : new Text(part) { Space = SpaceProcessingModeValues.Preserve });
        return result;
    }
    private static void Append(JsonElement node, Body body, string? prefix = null, bool quote = false)
    {
        var type = node.GetProperty("type").GetString();
        if (type is "paragraph" or "heading")
        {
            var p = new Paragraph(new ParagraphProperties(Spacing()));
            if (quote) p.ParagraphProperties!.Append(new Indentation { Left = "360", Right = "360" });
            if (prefix is not null) p.Append(Run(prefix, []));
            if (node.TryGetProperty("content", out var inline)) foreach (var item in inline.EnumerateArray())
            {
                var kind = item.GetProperty("type").GetString();
                if (kind == "hardBreak") p.Append(new Run(new Break()));
                else
                {
                    var marks = item.TryGetProperty("marks", out var ms) ? ms.EnumerateArray().Select(m => m.GetProperty("type").GetString()!).ToList() : [];
                    if (type == "heading" && !marks.Contains("bold")) marks.Add("bold");
                    p.Append(Run(item.GetProperty("text").GetString()!, marks));
                }
            }
            body.Append(p);
        }
        else if (node.TryGetProperty("content", out var children))
        {
            var index = 0;
            foreach (var child in children.EnumerateArray())
                Append(child, body, type == "bulletList" ? "• " : type == "orderedList" ? (++index) + ". " : prefix, quote || type == "blockquote");
        }
    }
}

public sealed record GreenNoteInput(int? Number, string? AuthorName, string? Designation, DateTimeOffset? SubmittedAt, string ContentJson, bool Working = false);
