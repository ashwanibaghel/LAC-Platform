namespace LAC.Infrastructure;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed record GreenEnvelopeManifest(Guid SessionId, Guid MatterId, Guid ActorId, Guid? SourceDakId,
    string AuthorName, string Designation, IReadOnlyList<GreenEnvelopeHistory> Notes);
public sealed record GreenEnvelopeHistory(Guid Id, int Number, int Version, string ContentJson, string CanonicalText,
    string TextHash, Guid AuthorUserId, string AuthorName, string Designation, Guid? DeskId, string? DeskName,
    DateTimeOffset SubmittedAt, Guid? SourceDakId, string CitationsJson, string DocumentManifestJson);

// Experimental closed-format adapter. The captured server manifest, never uploaded XML,
// defines history. V1 is still the default; no second official chain exists.
public static class ContinuousGreenEnvelope
{
    public const string Version = "ContinuousGreenEnvelopeV2";
    public const string GlyphPolicy = "LiberationSerif12-inset57twips-v1";
    public const int GlyphInsetTwips = 57; // 1.0054 mm ink safety, inside unchanged official margins.
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static MatterWorkflowException Invalid(string reason) => new($"{Version}: {reason}; no returned content was accepted.", 422);
    public static string ContentFingerprint(ConvertedOfficeNote note)
    {
        using var json = JsonDocument.Parse(note.ContentJson); var value = new StringBuilder();
        foreach (var paragraph in json.RootElement.GetProperty("content").EnumerateArray())
        {
            value.Append("P|");
            foreach (var inline in paragraph.GetProperty("content").EnumerateArray())
            {
                var hardBreak = inline.GetProperty("type").GetString() == "hardBreak";
                var text = hardBreak ? "\n" : inline.GetProperty("text").GetString()!;
                var marks = inline.TryGetProperty("marks", out var m) ? string.Join(",", m.EnumerateArray().Select(x => x.GetProperty("type").GetString()).Order(StringComparer.Ordinal)) : "";
                foreach (var c in text) value.Append((int)c).Append(':').Append(marks).Append(';');
            }
        }
        return NotingText.Hash(value.ToString());
    }
    public static string Serialize(GreenEnvelopeManifest manifest) => JsonSerializer.Serialize(manifest, Json);
    public static GreenEnvelopeManifest Read(string json, string hash)
    {
        if (NotingText.Hash(json) != hash) throw new MatterWorkflowException("Captured historical manifest integrity failed.", 409);
        var manifest = JsonSerializer.Deserialize<GreenEnvelopeManifest>(json, Json) ?? throw Invalid("missing manifest");
        CheckManifest(manifest); return manifest;
    }
    private static GreenEnvelopeHistory History(MatterOfficialNote n) => new(n.Id, n.Number, n.Version, n.ContentJson,
        n.CanonicalText, n.TextHash, n.AuthorUserId, n.AuthorName, n.Designation, n.DeskId, n.DeskName,
        n.SubmittedAt, n.SourceDakId, n.CitationsJson, n.DocumentManifestJson);
    public static async Task<GreenEnvelopeManifest> CaptureAsync(LacDbContext db, MatterOfficeSession s, CancellationToken ct)
    {
        var user = await db.AppUsers.AsNoTracking().Include(x => x.Designation).SingleAsync(x => x.Id == s.ActorUserId, ct);
        var notes = await db.MatterOfficialNotes.AsNoTracking().Where(x => x.MatterId == s.MatterId).OrderBy(x => x.Number).ToListAsync(ct);
        var result = new GreenEnvelopeManifest(s.Id, s.MatterId, s.ActorUserId, s.SourceDakId, user.DisplayName,
            user.CustomDesignation ?? user.Designation?.Name ?? "", notes.Select(History).ToArray());
        CheckManifest(result); return result;
    }
    private static void CheckManifest(GreenEnvelopeManifest m)
    {
        if (m.Notes.Count > 500 || m.Notes.Sum(x => x.CanonicalText.Length) > 1_000_000 ||
            m.Notes.Select(x => x.Id).Distinct().Count() != m.Notes.Count) throw Invalid("invalid or oversized history");
        var previous = 0;
        foreach (var n in m.Notes)
        {
            if (n.Number <= previous || n.Version != 1 || NotingText.Canonicalize(n.ContentJson) != n.CanonicalText || NotingText.Hash(n.CanonicalText) != n.TextHash)
                throw new MatterWorkflowException("Historical canonical note integrity or order failed.", 409);
            previous = n.Number;
        }
    }
    public static async Task<GreenEnvelopeManifest> AuthoritativeAsync(LacDbContext db, MatterOfficeSession s, CancellationToken ct)
    {
        var m = Read(s.HistoryManifestJson!, s.HistoryManifestHash!);
        if (m.SessionId != s.Id || m.MatterId != s.MatterId || m.ActorId != s.ActorUserId || m.SourceDakId != s.SourceDakId)
            throw new MatterWorkflowException("Historical manifest lease identity mismatch.", 409);
        var live = await db.MatterOfficialNotes.AsNoTracking().Where(x => x.MatterId == s.MatterId).OrderBy(x => x.Number).ToListAsync(ct);
        if (JsonSerializer.Serialize(live.Select(History), Json) != JsonSerializer.Serialize(m.Notes, Json))
            throw new MatterWorkflowException("Official chain changed; recover current content into a new authorized lease.", 409);
        return m;
    }
    private static string HistoryTag(GreenEnvelopeHistory n) => "lac:v2:note:" + n.Id.ToString("N");
    private static string ContextTag(GreenEnvelopeManifest m) => "lac:v2:context:" + m.SessionId.ToString("N");
    private static string CurrentTag(GreenEnvelopeManifest m) => "lac:v2:current:" + m.SessionId.ToString("N");
    private static string Context(GreenEnvelopeManifest m) => $"UNSUBMITTED working note — {m.AuthorName} ({m.Designation})";
    private static string TextJson(string text) => JsonSerializer.Serialize(new { type = "doc", content = new[] {
        new { type = "paragraph", content = new[] { new { type = "text", text } } } } }, Json);
    private static Paragraph[] Paragraphs(GreenNoteInput input)
    {
        using var bytes = OfficeNoteDocx.Compose([input]);
        using var package = WordprocessingDocument.Open(bytes, false);
        return package.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Select(p =>
        {
            var result = (Paragraph)p.CloneNode(true);
            result.ParagraphProperties ??= new ParagraphProperties();
            if (result.ParagraphProperties.GetFirstChild<Indentation>() is not null) throw Invalid("indented source content");
            result.ParagraphProperties.Append(new Indentation { Left = GlyphInsetTwips.ToString(), Right = GlyphInsetTwips.ToString() });
            return result;
        }).ToArray();
    }
    private static Paragraph[] Historical(GreenEnvelopeHistory n) => Paragraphs(new(n.Number, n.AuthorName, n.Designation, n.SubmittedAt, n.ContentJson));
    private static SdtBlock Control(string tag, LockingValues locking, IEnumerable<Paragraph> paragraphs, int id) => new(
        new SdtProperties(new SdtAlias { Val = tag }, new Tag { Val = tag }, new SdtId { Val = id }, new Lock { Val = locking }),
        new SdtContentBlock(paragraphs));
    public static MemoryStream Compose(GreenEnvelopeManifest manifest, string currentJson)
    {
        CheckManifest(manifest);
        var result = OfficeNoteDocx.Create(TextJson(""));
        using (var package = WordprocessingDocument.Open(result, true))
        {
            var body = package.MainDocumentPart!.Document!.Body!;
            var section = body.GetFirstChild<SectionProperties>()!; section.Remove(); body.RemoveAllChildren();
            var id = 1;
            foreach (var n in manifest.Notes) body.Append(Control(HistoryTag(n), LockingValues.SdtContentLocked, Historical(n), id++));
            body.Append(Control(ContextTag(manifest), LockingValues.SdtContentLocked, Paragraphs(new(null, null, null, null, TextJson(Context(manifest)))), id++));
            body.Append(Control(CurrentTag(manifest), LockingValues.SdtLocked, Paragraphs(new(null, null, null, null, currentJson)), id));
            body.Append(section); package.MainDocumentPart.Document.Save();
            var settings = package.MainDocumentPart.DocumentSettingsPart!.Settings!;
            settings.Append(new DefaultTabStop { Val = 720 }, new Compatibility(new CompatibilitySetting {
                Name = CompatSettingNameValues.CompatibilityMode, Uri = "http://schemas.microsoft.com/office/word", Val = "14" }));
            settings.Save();
        }
        result.Position = 0;
        // Exercise the same closed-format validation on generated input; unsupported
        // historical formatting prevents opening rather than disappearing silently.
        Convert(result, manifest); result.Position = 0; return result;
    }
    public static ConvertedOfficeNote Convert(Stream stream, GreenEnvelopeManifest manifest)
    {
        CheckManifest(manifest);
        try
        {
            ValidateZip(stream);
            stream.Position = 0;
            using var copy = new MemoryStream(); stream.CopyTo(copy); copy.Position = 0;
            using (var package = WordprocessingDocument.Open(copy, true, new OpenSettings { MaxCharactersInPart = 1_000_000 }))
            {
                var main = package.MainDocumentPart ?? throw Invalid("missing document");
                CheckParts(package);
                var body = main.Document?.Body ?? throw Invalid("missing body");
                CheckLayoutDefaults(main);
                if (main.Document.ChildElements.Any(x => x is not Body)) throw Invalid("document background or unknown document child");
                var blocks = body.ChildElements.ToArray();
                if (blocks.Length != manifest.Notes.Count + 3 || blocks[^1] is not SectionProperties || blocks.Take(blocks.Length - 1).Any(x => x is not SdtBlock))
                    throw Invalid("changed envelope structure or text outside controls");
                var controls = blocks.OfType<SdtBlock>().ToArray();
                for (var i = 0; i < manifest.Notes.Count; i++)
                {
                    var n = manifest.Notes[i];
                    CheckControl(controls[i], HistoryTag(n), LockingValues.SdtContentLocked);
                    if (Semantic(controls[i].SdtContentBlock!.Elements<Paragraph>()) != Semantic(Historical(n))) throw Invalid("historical text, marks or signature reservation changed");
                }
                var context = controls[^2]; CheckControl(context, ContextTag(manifest), LockingValues.SdtContentLocked);
                if (Semantic(context.SdtContentBlock!.Elements<Paragraph>()) != Semantic(Paragraphs(new(null, null, null, null, TextJson(Context(manifest))))))
                    throw Invalid("unsubmitted context metadata changed");
                var current = controls[^1]; CheckControl(current, CurrentTag(manifest), LockingValues.SdtLocked);
                foreach (var p in current.SdtContentBlock!.Elements<Paragraph>())
                {
                    var spacing = p.ParagraphProperties?.GetFirstChild<SpacingBetweenLines>();
                    if (spacing?.Before?.Value is not (null or "0") || spacing?.After?.Value != "120" ||
                        p.ParagraphProperties?.ChildElements.Any(x => x is KeepNext or KeepLines) == true)
                        throw Invalid("current paragraph layout tampering");
                }
                foreach (var p in body.Descendants<Paragraph>()) CheckParagraph(p);
                // V1 independently checks fonts, marks, layout and paragraph/run structure
                // for ALL blocks, before any history is removed from the clone.
                var flattened = controls.SelectMany(c => c.SdtContentBlock!.Elements<Paragraph>()).Select(p => (Paragraph)p.CloneNode(true)).ToArray();
                var section = blocks[^1].CloneNode(true); body.RemoveAllChildren();
                foreach (var p in flattened) { p.ParagraphProperties?.RemoveAllChildren<Indentation>(); body.Append(p); }
                body.Append(section); main.Document.Save();
            }
            copy.Position = 0; OfficeNoteDocx.Convert(copy);
            copy.Position = 0;
            using (var package = WordprocessingDocument.Open(copy, true))
            {
                var body = package.MainDocumentPart!.Document!.Body!;
                var prefixCount = manifest.Notes.Sum(n => Historical(n).Length) + 1;
                foreach (var p in body.Elements<Paragraph>().Take(prefixCount).ToArray()) p.Remove();
                package.MainDocumentPart.Document.Save();
            }
            copy.Position = 0; return OfficeNoteDocx.Convert(copy);
        }
        catch (MatterWorkflowException) { throw; }
        catch (Exception ex) when (ex is InvalidDataException or OpenXmlPackageException or System.Xml.XmlException or ArgumentException or InvalidOperationException)
        { throw Invalid("invalid DOCX envelope"); }
    }
    private static void ValidateZip(Stream stream)
    {
        if (stream.Length is 0 or > OfficeNoteDocx.MaxBytes) throw Invalid("empty or oversized DOCX");
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, true);
        if (zip.Entries.Count > 1000 || zip.Entries.Sum(x => x.Length) > 20 * 1024 * 1024 || zip.Entries.Select(x => x.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count)
            throw Invalid("oversized or duplicate package entries");
        string[] names = ["[Content_Types].xml", "_rels/.rels", "word/document.xml", "word/styles.xml", "word/settings.xml", "word/webSettings.xml", "word/fontTable.xml", "word/numbering.xml", "word/footnotes.xml", "word/endnotes.xml", "word/theme/theme1.xml", "word/_rels/document.xml.rels", "word/_rels/footnotes.xml.rels", "word/_rels/endnotes.xml.rels", "word/_rels/comments.xml.rels", "docProps/app.xml", "docProps/core.xml", "customXml/item1.xml", "customXml/itemProps1.xml", "customXml/_rels/item1.xml.rels"];
        if (zip.Entries.Any(e => !names.Contains(e.FullName, StringComparer.Ordinal))) throw Invalid("unexpected package part");
    }
    private static void CheckParts(OpenXmlPartContainer root)
    {
        if (root.ExternalRelationships.Any() || root.HyperlinkRelationships.Any() || root.DataPartReferenceRelationships.Any()) throw Invalid("unexpected relationship");
        foreach (var pair in root.Parts.ToArray())
        {
            var part = pair.OpenXmlPart;
            if (part is CustomXmlPart custom)
            {
                // Docs 9.4 emits this empty bibliography record. No bindings/data allowed.
                using var stream = custom.GetStream();
                using var reader = XmlReader.Create(stream, new XmlReaderSettings {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = 1_000_000, MaxCharactersFromEntities = 1_000 });
                var xml = XElement.Load(reader);
                XNamespace b = "http://schemas.openxmlformats.org/officeDocument/2006/bibliography";
                if (xml.Name != b + "Sources" || xml.HasElements || !string.IsNullOrWhiteSpace(xml.Value) ||
                    xml.Attributes().Where(a => !a.IsNamespaceDeclaration).Any(a => a.Name.NamespaceName != "" || (a.Name.LocalName, a.Value) is not (("SelectedStyle", "/APA.XSL") or ("StyleName", "APA"))))
                    throw Invalid("custom XML other than empty native bibliography");
                if (custom.Parts.Any(p => p.OpenXmlPart is not CustomXmlPropertiesPart)) throw Invalid("custom XML relationship");
                CheckParts(custom); root.DeletePart(custom); continue;
            }
            if (part is not (MainDocumentPart or StyleDefinitionsPart or DocumentSettingsPart or WebSettingsPart or FontTablePart or NumberingDefinitionsPart or FootnotesPart or EndnotesPart or ThemePart or CoreFilePropertiesPart or ExtendedFilePropertiesPart or CustomXmlPropertiesPart))
                throw Invalid("unexpected relationship part " + part.ContentType);
            CheckParts(part);
        }
    }
    private static void CheckControl(SdtBlock control, string tag, LockingValues locking)
    {
        if (control.ChildElements.Any(x => x is not (SdtProperties or SdtContentBlock)) || control.Elements<SdtProperties>().Count() != 1 || control.Elements<SdtContentBlock>().Count() != 1)
            throw Invalid("control structure");
        var properties = control.SdtProperties!;
        if (properties.HasAttributes || properties.ChildElements.GroupBy(x => x.LocalName).Any(g => g.Count() > 1)) throw Invalid("duplicate or unknown control metadata");
        if (properties.Elements<Tag>().Count() != 1 || properties.GetFirstChild<Tag>()?.Val?.Value != tag || properties.Elements<Lock>().Count() != 1 || properties.GetFirstChild<Lock>()?.Val?.Value != locking)
            throw Invalid("missing, reordered, retagged or unlocked control");
        foreach (var p in properties.ChildElements)
            if (p is SdtAlias alias && alias.Val?.Value != tag || p is RunProperties rp && rp.HasChildren ||
                p is not (Tag or Lock or SdtAlias or SdtId or RunProperties) && !(p.LocalName == "appearance" && p.NamespaceUri == "http://schemas.microsoft.com/office/word/2012/wordml" && p.GetAttribute("val", "http://schemas.microsoft.com/office/word/2012/wordml").Value == "boundingBox"))
                throw Invalid("unexpected control properties");
        if (!control.SdtContentBlock!.HasChildren || control.SdtContentBlock.ChildElements.Any(x => x is not Paragraph)) throw Invalid("nested control or non-paragraph content");
    }
    private static void CheckLayoutDefaults(MainDocumentPart main)
    {
        var section = main.Document!.Body!.Elements<SectionProperties>().SingleOrDefault() ?? throw Invalid("missing global section");
        var margins = section.GetFirstChild<PageMargin>();
        if (margins?.Header?.Value is not (null or 0U or 720U) || margins?.Footer?.Value is not (null or 0U or 720U) ||
            section.GetAttributes().Any()) throw Invalid("section metadata or header/footer layout");
        foreach (var p in main.StyleDefinitionsPart?.Styles?.Descendants<ParagraphPropertiesBaseStyle>() ?? Enumerable.Empty<ParagraphPropertiesBaseStyle>())
            if (p.ChildElements.Any(x => x is not (SpacingBetweenLines or Indentation or ParagraphBorders)) ||
                p.Elements<Indentation>().Any(i => i.GetAttributes().Any(a => a.Value != "0")) || p.Elements<ParagraphBorders>().Any(b => b.HasChildren))
                throw Invalid("inherited paragraph layout");
        // Only native 9.4 inert defaults are allowed. Print/layout options such as
        // gutterAtTop, printTwoOnOne, bookFoldPrinting or revision tracking fail closed.
        var settings = main.DocumentSettingsPart?.Settings ?? throw Invalid("missing settings");
        var defaults = main.StyleDefinitionsPart?.Styles?.GetFirstChild<DocDefaults>()?.GetFirstChild<RunPropertiesDefault>()?.GetFirstChild<RunPropertiesBaseStyle>();
        var normal = main.StyleDefinitionsPart?.Styles?.Elements<Style>().FirstOrDefault(x => x.Type?.Value == StyleValues.Paragraph && x.Default?.Value == true);
        CheckTextAttributes(defaults); CheckTextAttributes(normal?.StyleRunProperties);
        foreach (var property in settings.ChildElements)
        {
            if (property.NamespaceUri != W && !(property.LocalName == "mathPr" && property.NamespaceUri == "http://schemas.openxmlformats.org/officeDocument/2006/math" && !property.HasChildren && !property.HasAttributes))
                throw Invalid("unknown settings namespace or math defaults");
            if (property.LocalName is not ("mirrorMargins" or "zoom" or "trackRevisions" or "documentProtection" or "defaultTabStop" or "characterSpacingControl" or "footnotePr" or "endnotePr" or "compat" or "mathPr" or "themeFontLang" or "clrSchemeMapping" or "shapeDefaults" or "decimalSymbol" or "listSeparator")) throw Invalid("unknown native settings " + property.LocalName);
            if (property is TrackRevisions tracking && tracking.Val?.Value != false || property is DocumentProtection protection && (protection.HasChildren || protection.HasAttributes) ||
                property is DefaultTabStop tabs && tabs.Val?.Value != 720 || property is CharacterSpacingControl spacing && spacing.Val?.Value != CharacterSpacingValues.DoNotCompress)
                throw Invalid("unsafe native settings");
            if (property is Compatibility compatibility)
                foreach (var c in compatibility.ChildElements)
                {
                    if (c.LocalName == "useFELayout" && c.GetAttribute("val", W).Value == "true") continue;
                    if (c is CompatibilitySetting cs && cs.Uri?.Value == "http://schemas.microsoft.com/office/word" &&
                        ((cs.GetAttribute("name", W).Value == "compatibilityMode" && cs.Val?.Value == "14") || (cs.GetAttribute("name", W).Value is "overrideTableStyleFontSizeAndJustification" or "enableOpenTypeFeatures" or "doNotFlipMirrorIndents" && cs.Val?.Value == "1"))) continue;
                    throw Invalid("unsupported compatibility layout");
                }
        }
    }
    private static void CheckParagraph(Paragraph p)
    {
        var properties = p.ParagraphProperties;
        if (properties is null || properties.Elements<Indentation>().Count() != 1) throw Invalid("missing glyph-clearance indentation");
        var indent = properties.GetFirstChild<Indentation>()!;
        if (indent.Left?.Value != GlyphInsetTwips.ToString() || indent.Right?.Value != GlyphInsetTwips.ToString() || indent.GetAttributes().Any(a => a.LocalName is not ("left" or "right"))) throw Invalid("paragraph indentation policy");
        foreach (var property in properties.ChildElements)
        {
            if (properties.ChildElements.Count(x => x.LocalName == property.LocalName) != 1) throw Invalid("duplicate paragraph properties");
            if (property is ParagraphMarkRunProperties mark) OfficeNoteDocx.CheckInherited(mark);
            if (property is SpacingBetweenLines spacing && (spacing.Before?.Value is not (null or "0" or "720") || spacing.After?.Value is not ("120" or "720") || spacing.Line?.Value != "360" || spacing.LineRule?.Value != LineSpacingRuleValues.Auto || spacing.GetAttributes().Any(a => a.LocalName is not ("before" or "after" or "line" or "lineRule")))) throw Invalid("paragraph spacing policy");
            if (property is not (SpacingBetweenLines or Indentation or KeepNext or KeepLines or ParagraphBorders or ParagraphMarkRunProperties or ParagraphStyleId or Justification)) throw Invalid("unsupported paragraph property");
        }
        if (properties.Elements<SpacingBetweenLines>().Count() != 1) throw Invalid("missing spacing policy");
        foreach (var run in p.Elements<Run>())
        {
            if (run.RunProperties?.ChildElements.GroupBy(x => x.LocalName).Any(g => g.Count() != 1) == true) throw Invalid("duplicate run properties");
            CheckTextAttributes(run.RunProperties);
        }
        foreach (var mark in properties.Elements<ParagraphMarkRunProperties>()) CheckTextAttributes(mark);
    }
    private static void CheckTextAttributes(OpenXmlElement? properties)
    {
        foreach (var property in properties?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
        {
            string[] allowed = property switch { RunFonts => ["ascii","hAnsi","eastAsia","cs","hint"], Languages => ["val","eastAsia","bidi"], _ => ["val"] };
            if (property.GetAttributes().Any(a => a.NamespaceUri != W || !allowed.Contains(a.LocalName))) throw Invalid("unknown text-format attributes");
        }
    }
    private static string Semantic(IEnumerable<Paragraph> paragraphs)
    {
        // Normalize nonsemantic run splitting and false/absent marks. UTF-16 units,
        // paragraph boundaries and exact formatting remain distinct.
        var result = new StringBuilder();
        foreach (var p in paragraphs)
        {
            CheckParagraph(p);
            var props = p.ParagraphProperties!; var spacing = props.GetFirstChild<SpacingBetweenLines>()!;
            result.Append(JsonSerializer.Serialize(new { before = spacing.Before?.Value ?? "0", after = spacing.After?.Value, line = spacing.Line?.Value,
                keepNext = props.GetFirstChild<KeepNext>() is { } kn && kn.Val?.Value != false,
                keepLines = props.GetFirstChild<KeepLines>() is { } kl && kl.Val?.Value != false }));
            foreach (var run in p.Elements<Run>())
            {
                var rp = run.RunProperties;
                var marks = (rp?.GetFirstChild<Bold>() is { } b && b.Val?.Value != false ? "b" : "") +
                    (rp?.GetFirstChild<Italic>() is { } i && i.Val?.Value != false ? "i" : "") +
                    (rp?.GetFirstChild<Underline>() is { } u && u.Val?.Value != UnderlineValues.None ? "u" : "") +
                    (rp?.GetFirstChild<Strike>() is { } s && s.Val?.Value != false ? "s" : "");
                foreach (var child in run.ChildElements)
                {
                    var text = child switch { Text t => t.Text, TabChar => "\t", Break br when br.Type is null || br.Type.Value == BreakValues.TextWrapping => "\n", _ => "" };
                    foreach (var c in text) result.Append((int)c).Append(':').Append(marks).Append(';');
                }
            }
            result.Append('|');
        }
        return result.ToString();
    }
}
