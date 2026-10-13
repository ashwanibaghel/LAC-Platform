namespace LAC.Tests;

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LAC.Domain;
using LAC.Infrastructure;
using Xunit;

public sealed class OfficeNoteDocxTests
{
    [Fact] public void Current_note_converter_rejects_a_shared_content_control_envelope_without_importing_history()
    {
        using var stream = OfficeNoteDocx.Create(MatterNotingTextTests.Text("Synthetic current text"));
        using (var package = WordprocessingDocument.Open(stream, true))
        {
            var body = package.MainDocumentPart!.Document!.Body!;
            var paragraph = body.GetFirstChild<Paragraph>()!;
            paragraph.Remove();
            body.InsertBefore(new SdtBlock(
                new SdtProperties(new Tag { Val = "synthetic-history" }, new Lock { Val = LockingValues.SdtContentLocked }),
                new SdtContentBlock(paragraph)), body.LastChild);
            package.MainDocumentPart.Document.Save();
        }
        stream.Position = 0;
        var error = Assert.Throws<MatterWorkflowException>(() => OfficeNoteDocx.Convert(stream));
        Assert.Equal(422, error.StatusCode);
        Assert.Contains("sdt", error.Message);
    }

    [Fact] public void Actual_Docs94_export_with_reserved_separators_and_empty_borders_converts_exactly()
    {
        using var stream=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"fixtures","office94-synthetic-note.docx"));
        var converted=OfficeNoteDocx.Convert(stream);
        Assert.Contains("ACCEPTANCE-OFFICER-A-20261012.",converted.CanonicalText);
        Assert.Equal(3,converted.CanonicalText.Count(c=>c=='\n'));
        Assert.Equal(2,converted.CanonicalText.Split("sample evidence.").Length-1);
        Assert.Equal(NotingText.Hash(converted.CanonicalText),converted.CanonicalHash);
    }
    [Fact] public void Composition_bytes_are_deterministic_for_the_same_immutable_input()
    {
        GreenNoteInput[] input=[new(1,"A","Kanungo",DateTimeOffset.UnixEpoch,MatterNotingTextTests.Text("Same source"))];
        using var first=OfficeNoteDocx.Compose(input); using var second=OfficeNoteDocx.Compose(input);
        Assert.Equal(first.ToArray(),second.ToArray());
    }
    [Fact] public void Exact_text_marks_repeated_quotes_and_UTF16_survive_conversion()
    {
        const string json = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Same 😀 Same","marks":[{"type":"bold"},{"type":"underline"}]},{"type":"hardBreak"},{"type":"text","text":"Next\tline"}]}]}""";
        using var doc = OfficeNoteDocx.Create(json); var converted = OfficeNoteDocx.Convert(doc);
        Assert.Equal(NotingText.Canonicalize(json), converted.CanonicalText);
        Assert.Equal(NotingText.Hash(converted.CanonicalText), converted.CanonicalHash);
        Assert.Contains("bold", converted.ContentJson); Assert.Contains("underline", converted.ContentJson);
        NotingText.ValidateAnchor(converted.CanonicalText, new(1, 8, 12, "Same", NotingText.Hash("Same")));
        Assert.Throws<MatterWorkflowException>(() => NotingText.ValidateAnchor(converted.CanonicalText, new(1, 0, 3, "Same", NotingText.Hash("Same"))));
    }

    [Theory] [InlineData("table")] [InlineData("field")] [InlineData("image")] [InlineData("tracked")] [InlineData("hyperlink")] [InlineData("header")] [InlineData("layout")] [InlineData("font")] [InlineData("pageBreak")]
    public void Unsupported_content_fails_closed_without_a_partial_import(string kind)
    {
        using var stream = OfficeNoteDocx.Create(MatterNotingTextTests.Text("Keep all text"));
        using (var package = WordprocessingDocument.Open(stream, true))
        {
            var main = package.MainDocumentPart!; var body = main.Document!.Body!; var p = body.GetFirstChild<Paragraph>()!;
            switch (kind)
            {
                case "table": body.InsertBefore(new Table(new TableRow(new TableCell(new Paragraph(new Run(new Text("Lost")))))), body.LastChild); break;
                case "field": p.Append(new SimpleField { Instruction = "DATE" }); break;
                case "image": p.Append(new Run(new Drawing())); break;
                case "tracked": p.Append(new InsertedRun(new Run(new Text("unreviewed"))) { Id = "1", Author = "someone" }); break;
                case "hyperlink": main.AddHyperlinkRelationship(new Uri("https://example.invalid"), true); break;
                case "header": main.AddNewPart<HeaderPart>().Header = new Header(); break;
                case "layout": body.GetFirstChild<SectionProperties>()!.GetFirstChild<PageMargin>()!.Right = 1134U; break;
                case "font": p.GetFirstChild<Run>()!.RunProperties!.GetFirstChild<FontSize>()!.Val = "32"; break;
                case "pageBreak": p.Append(new Run(new Break { Type = BreakValues.Page })); break;
            }
            main.Document.Save();
        }
        stream.Position = 0;
        Assert.Equal(422, Assert.Throws<MatterWorkflowException>(() => OfficeNoteDocx.Convert(stream)).StatusCode);
    }

    [Fact] public void One_global_legal_section_unsplit_signature_and_no_officer_page_breaks()
    {
        using var stream = OfficeNoteDocx.Compose([new(1,"A","Kanungo",DateTimeOffset.UnixEpoch,MatterNotingTextTests.Text("First")),new(2,"B","Tehsildar",DateTimeOffset.UnixEpoch,MatterNotingTextTests.Text("Second"))]);
        using var package = WordprocessingDocument.Open(stream,false); var body = package.MainDocumentPart!.Document!.Body!;
        Assert.Single(body.Elements<SectionProperties>()); Assert.DoesNotContain(body.Descendants<Break>(), b => b.Type?.Value == BreakValues.Page);
        Assert.Equal(2,body.Elements<Paragraph>().Count(p=>p.InnerText.StartsWith("Handwritten signature") && p.ParagraphProperties!.GetFirstChild<KeepLines>() is not null));
        Assert.Equal(12240U,body.GetFirstChild<SectionProperties>()!.GetFirstChild<PageSize>()!.Width!.Value);
        Assert.NotNull(package.MainDocumentPart.DocumentSettingsPart!.Settings!.GetFirstChild<MirrorMargins>());
        Assert.Equal(20m,MatterDraftOfficeProfiles.DelhiLacNotingV1.MarginOutsideMm);
        Assert.Equal(15m,MatterDraftOfficeProfiles.GreenLegalV2.MarginOutsideMm);
    }
    [Fact] public void Unsubmitted_preview_is_visibly_unsubmitted_and_never_claims_an_official_number()
    {
        using var stream=OfficeNoteDocx.Compose([new(2,"A","Kanungo",DateTimeOffset.UnixEpoch,MatterNotingTextTests.Text("private"),true)]);
        using var package=WordprocessingDocument.Open(stream,false);
        Assert.Contains("UNSUBMITTED working note",package.MainDocumentPart!.Document!.InnerText);
        Assert.DoesNotContain("Note 2",package.MainDocumentPart.Document.InnerText);
    }
}
