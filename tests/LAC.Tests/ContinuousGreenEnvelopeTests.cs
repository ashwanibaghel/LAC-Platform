namespace LAC.Tests;

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LAC.Domain;
using LAC.Infrastructure;
using Xunit;

public sealed class ContinuousGreenEnvelopeTests
{
    [Fact]
    public void Actual_Docs94_continuous_export_validates_and_extracts_only_the_native_current_control()
    {
        var root = Path.Combine(AppContext.BaseDirectory,"fixtures");
        var json = File.ReadAllText(Path.Combine(root,"office94-continuous-green-manifest.json"));
        var manifest = ContinuousGreenEnvelope.Read(json,NotingText.Hash(json));
        using var bytes = File.OpenRead(Path.Combine(root,"office94-continuous-green-synthetic.docx"));
        var current = ContinuousGreenEnvelope.Convert(bytes,manifest);
        Assert.Contains("FINAL-KEYSTROKE-V2-20261013-1858.",current.CanonicalText);
        Assert.DoesNotContain("Historical",current.CanonicalText); Assert.DoesNotContain("Handwritten",current.CanonicalText);
        Assert.DoesNotContain("Synthetic current officer",current.CanonicalText);
    }
    [Fact]
    public void Native_content_fingerprint_covers_marks_and_normalizes_only_run_splitting()
    {
        const string split = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Sa","marks":[{"type":"bold"}]},{"type":"text","text":"me","marks":[{"type":"bold"}]}]}]}""";
        const string joined = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Same","marks":[{"type":"bold"}]}]}]}""";
        var plain = MatterNotingTextTests.Text("Same");
        ConvertedOfficeNote Note(string json) => new(json,NotingText.Canonicalize(json),NotingText.Hash(NotingText.Canonicalize(json)));
        Assert.Equal(ContinuousGreenEnvelope.ContentFingerprint(Note(split)),ContinuousGreenEnvelope.ContentFingerprint(Note(joined)));
        Assert.Equal(Note(plain).CanonicalHash,Note(joined).CanonicalHash);
        Assert.NotEqual(ContinuousGreenEnvelope.ContentFingerprint(Note(plain)),ContinuousGreenEnvelope.ContentFingerprint(Note(joined)));
    }
    internal static GreenEnvelopeManifest Manifest()
    {
        var json = MatterNotingTextTests.Text("Historical Same 😀 Same");
        var text = NotingText.Canonicalize(json);
        GreenEnvelopeHistory Note(int number) => new(Guid.NewGuid(), number, 1, json, text, NotingText.Hash(text), Guid.NewGuid(),
            "Synthetic Officer " + number, "Kanungo", null, null, DateTimeOffset.UnixEpoch, null, "[]", "[]");
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Synthetic current officer", "Tehsildar", [Note(1), Note(2)]);
    }
    [Fact]
    public void Current_only_UTF16_and_repeated_quote_marks_are_extracted_without_history()
    {
        var m = Manifest();
        const string json = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Same 😀 Same","marks":[{"type":"bold"}]},{"type":"hardBreak"},{"type":"text","text":"Next\tline"}]}]}""";
        using var bytes = ContinuousGreenEnvelope.Compose(m, json);
        var current = ContinuousGreenEnvelope.Convert(bytes, m);
        Assert.Equal(NotingText.Canonicalize(json), current.CanonicalText);
        Assert.Equal(NotingText.Hash(current.CanonicalText), current.CanonicalHash);
        Assert.DoesNotContain("Historical", current.CanonicalText); Assert.DoesNotContain("Synthetic", current.CanonicalText);
        NotingText.ValidateAnchor(current.CanonicalText, new(1, 8, 12, "Same", NotingText.Hash("Same")));
        bytes.Position = 0; Assert.Equal(422, Assert.Throws<MatterWorkflowException>(() => OfficeNoteDocx.Convert(bytes)).StatusCode);
    }
    [Theory]
    [InlineData("historyText")][InlineData("historyMark")][InlineData("order")][InlineData("missing")]
    [InlineData("lock")][InlineData("tag")][InlineData("metadata")][InlineData("signature")]
    [InlineData("extra")][InlineData("outside")][InlineData("nested")][InlineData("field")]
    [InlineData("relationship")][InlineData("layout")][InlineData("indent")][InlineData("spacing")]
    [InlineData("font")][InlineData("header")][InlineData("currentLock")][InlineData("pageBreak")]
    [InlineData("settings")][InlineData("customXml")][InlineData("currentSpacing")]
    [InlineData("themeColor")]
    [InlineData("paragraphMarkFont")][InlineData("paragraphMarkBold")]
    [InlineData("bibliographyDtd")]
    [InlineData("inheritedThemeColor")][InlineData("settingsNamespace")]
    [InlineData("inheritedParagraphShading")][InlineData("inheritedParagraphSpacing")]
    [InlineData("controlAttributes")][InlineData("duplicateControlId")]
    public void Tampering_fails_closed_before_current_text_is_imported(string attack)
    {
        var m = Manifest(); using var bytes = ContinuousGreenEnvelope.Compose(m, MatterNotingTextTests.Text("Current private note"));
        using (var package = WordprocessingDocument.Open(bytes, true))
        {
            var main = package.MainDocumentPart!; var body = main.Document!.Body!;
            var controls = body.Elements<SdtBlock>().ToArray(); var history = controls[0]; var current = controls[^1];
            switch (attack)
            {
                case "historyText": history.Descendants<Text>().First(t => t.Text.StartsWith("Historical")).Text = "Forged"; break;
                case "historyMark": history.Descendants<Run>().First(r => r.InnerText.StartsWith("Historical")).RunProperties!.Append(new Bold()); break;
                case "order": history.Remove(); body.InsertAfter(history, controls[1]); break;
                case "missing": history.Remove(); break;
                case "lock": history.SdtProperties!.GetFirstChild<Lock>()!.Val = LockingValues.SdtLocked; break;
                case "currentLock": current.SdtProperties!.GetFirstChild<Lock>()!.Remove(); break;
                case "tag": history.SdtProperties!.GetFirstChild<Tag>()!.Val = "someone-else"; break;
                case "metadata": history.Descendants<Text>().First().Text = "Wrong officer"; break;
                case "signature": history.SdtContentBlock!.LastChild!.Remove(); break;
                case "extra": body.InsertBefore(history.CloneNode(true), body.LastChild); break;
                case "outside": body.InsertBefore(new Paragraph(new Run(new Text("Unauthorized"))), body.LastChild); break;
                case "nested": current.SdtContentBlock!.Append(new SdtBlock(new SdtContentBlock(new Paragraph()))); break;
                case "field": current.Descendants<Paragraph>().First().Append(new SimpleField { Instruction = "DATE" }); break;
                case "relationship": main.AddHyperlinkRelationship(new Uri("https://example.invalid"), true); break;
                case "layout": body.GetFirstChild<SectionProperties>()!.GetFirstChild<PageMargin>()!.Left = 2500; break;
                case "indent": history.Descendants<Indentation>().First().Left = "0"; break;
                case "spacing": history.Descendants<SpacingBetweenLines>().Last().Before = "0"; break;
                case "font": current.Descendants<Run>().First().RunProperties!.GetFirstChild<FontSize>()!.Val = "28"; break;
                case "header": main.AddNewPart<HeaderPart>().Header = new Header(); break;
                case "pageBreak": current.Descendants<Paragraph>().First().Append(new Run(new Break { Type = BreakValues.Page })); break;
                case "settings": main.DocumentSettingsPart!.Settings!.Append(new BookFoldPrinting()); main.DocumentSettingsPart.Settings.Save(); break;
                case "customXml": using (var writer = new StreamWriter(main.AddCustomXmlPart(CustomXmlPartType.CustomXml).GetStream(FileMode.Create))) writer.Write("<malicious>data</malicious>"); break;
                case "bibliographyDtd": using (var writer = new StreamWriter(main.AddCustomXmlPart(CustomXmlPartType.CustomXml).GetStream(FileMode.Create))) writer.Write("<!DOCTYPE Sources [<!ENTITY x 'expanded'>]><Sources xmlns='http://schemas.openxmlformats.org/officeDocument/2006/bibliography'>&x;</Sources>"); break;
                case "currentSpacing": current.Descendants<SpacingBetweenLines>().First().Before = "720"; break;
                case "themeColor": current.Descendants<Run>().First().RunProperties!.Append(new Color { Val="000000",ThemeColor=ThemeColorValues.Accent1 }); break;
                case "inheritedThemeColor": main.StyleDefinitionsPart!.Styles!.Descendants<RunPropertiesBaseStyle>().First().Append(new Color { Val="000000",ThemeColor=ThemeColorValues.Accent1 }); main.StyleDefinitionsPart.Styles.Save(); break;
                case "settingsNamespace": main.DocumentSettingsPart!.Settings!.Append(new OpenXmlUnknownElement("x", "zoom", "urn:unapproved")); main.DocumentSettingsPart.Settings.Save(); break;
                case "inheritedParagraphShading": main.StyleDefinitionsPart!.Styles!.Append(new Style(new StyleParagraphProperties(new Shading {Val=ShadingPatternValues.Clear,Fill="00FF00"})) {Type=StyleValues.Paragraph,Default=true,StyleId="Normal"});main.StyleDefinitionsPart.Styles.Save();break;
                case "inheritedParagraphSpacing": main.StyleDefinitionsPart!.Styles!.Append(new Style(new StyleParagraphProperties(new SpacingBetweenLines {Before="720"})) {Type=StyleValues.Paragraph,Default=true,StyleId="Normal"});main.StyleDefinitionsPart.Styles.Save();break;
                case "controlAttributes": current.SetAttribute(new OpenXmlAttribute("w","unsupported","http://schemas.openxmlformats.org/wordprocessingml/2006/main","true"));break;
                case "duplicateControlId": current.SdtProperties!.GetFirstChild<SdtId>()!.Val=history.SdtProperties!.GetFirstChild<SdtId>()!.Val;break;
                case "paragraphMarkFont": current.Descendants<Paragraph>().First().ParagraphProperties!.Append(new ParagraphMarkRunProperties(new FontSize {Val="32"})); break;
                case "paragraphMarkBold": current.Descendants<Paragraph>().First().ParagraphProperties!.Append(new ParagraphMarkRunProperties(new Bold())); break;
            }
            main.Document.Save();
        }
        bytes.Position = 0;
        Assert.Equal(422, Assert.Throws<MatterWorkflowException>(() => ContinuousGreenEnvelope.Convert(bytes, m)).StatusCode);
    }
    [Fact]
    public void Run_splitting_and_cached_page_breaks_do_not_change_historical_authority()
    {
        var m = Manifest(); using var bytes = ContinuousGreenEnvelope.Compose(m, MatterNotingTextTests.Text("Current"));
        using (var package = WordprocessingDocument.Open(bytes, true))
        {
            var run = package.MainDocumentPart!.Document!.Body!.Elements<SdtBlock>().First().Descendants<Run>().First();
            var text = run.GetFirstChild<Text>()!; var suffix = text.Text[3..]; text.Text = text.Text[..3];
            run.Append(new LastRenderedPageBreak());
            run.InsertAfterSelf(new Run(run.RunProperties!.CloneNode(true), new Text(suffix) { Space = SpaceProcessingModeValues.Preserve }));
            package.MainDocumentPart.Document.Save();
        }
        bytes.Position = 0; Assert.Equal("Current\n", ContinuousGreenEnvelope.Convert(bytes, m).CanonicalText);
    }
    [Fact]
    public void Server_manifest_hash_metadata_and_order_are_not_inferred_from_document()
    {
        var m = Manifest(); var json = ContinuousGreenEnvelope.Serialize(m);
        Assert.Equal(m.SessionId, ContinuousGreenEnvelope.Read(json, NotingText.Hash(json)).SessionId);
        Assert.Equal(409, Assert.Throws<MatterWorkflowException>(() => ContinuousGreenEnvelope.Read(json + " ", NotingText.Hash(json))).StatusCode);
        using var bytes = ContinuousGreenEnvelope.Compose(m, MatterNotingTextTests.Text("Current"));
        var reordered = m with { Notes = m.Notes.Reverse().ToArray() };
        Assert.Throws<MatterWorkflowException>(() => ContinuousGreenEnvelope.Convert(bytes, reordered));
    }
    [Fact]
    public void Duplicate_internal_part_relationships_are_rejected_before_recursive_traversal()
    {
        var m=Manifest();using var bytes=ContinuousGreenEnvelope.Compose(m,MatterNotingTextTests.Text("Current"));
        using(var zip=new System.IO.Compression.ZipArchive(bytes,System.IO.Compression.ZipArchiveMode.Update,true))
        {
            var entry=zip.GetEntry("word/_rels/document.xml.rels")!;System.Xml.Linq.XElement xml;
            using(var read=entry.Open())xml=System.Xml.Linq.XElement.Load(read);
            var original=xml.Elements().Single(x=>x.Attribute("Type")!.Value.EndsWith("/styles",StringComparison.Ordinal));
            var duplicate=new System.Xml.Linq.XElement(original);duplicate.SetAttributeValue("Id","rId_duplicate_styles");xml.Add(duplicate);
            using var write=entry.Open();write.SetLength(0);xml.Save(write);
        }
        bytes.Position=0;Assert.Equal(422,Assert.Throws<MatterWorkflowException>(()=>ContinuousGreenEnvelope.Convert(bytes,m)).StatusCode);
    }
    [Fact]
    public void Preview_wire_response_does_not_expose_internal_native_capture_storage()
    {
        var preview=new MatterOfficePreview {NativeCaptureJson="private/storage/evidence",CanonicalText="Reviewed text"};
        var json=System.Text.Json.JsonSerializer.Serialize(preview);
        Assert.DoesNotContain("NativeCapture",json);Assert.DoesNotContain("private/storage",json);
        Assert.Contains("Reviewed text",json);Assert.Equal("private/storage/evidence",preview.NativeCaptureJson);
    }
}
