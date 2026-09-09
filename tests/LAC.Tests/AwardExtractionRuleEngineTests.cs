using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Xunit;

namespace LAC.Tests;

public sealed class AwardExtractionRuleEngineTests
{
    public static IEnumerable<object[]> GoldenCorpus()
    {
        yield return ["clean-born-digital", Page("Award No: FIC-2026/01", "Award Date: 01/01/2026"), new[] { AwardIngestionCandidateType.AwardCore }];
        yield return ["clean-scanned", Page("Award Number: FIC-2026/02", "Award Date: 02-01-2026"), new[] { AwardIngestionCandidateType.AwardCore }];
        yield return ["noisy-scanned", Page("Awrd No: FIC-2026/03"), new[] { AwardIngestionCandidateType.AwardCore }];
        yield return ["skewed-page", Page("Award No: FIC-2026/04", y: 713), new[] { AwardIngestionCandidateType.AwardCore }];
        yield return ["alternate-award-header", Page("Award Nos: FIC-2026/05"), new[] { AwardIngestionCandidateType.AwardCore }];
        yield return ["khasra-table", TablePage("Khasra No", "Total Area", "Area Awarded", ["22//2/1", "1-2-3", "0-10-0"]), new[] { AwardIngestionCandidateType.AwardKhasra }];
        yield return ["reordered-table", ReorderedTablePage(), new[] { AwardIngestionCandidateType.AwardKhasra }];
        yield return ["multipage-table", TablePage("Khasra No", "Total Area", "Area Awarded", ["22//2/1", "1-2-3", "0-10-0"], 1).Concat(TablePage("Khasra No", "Total Area", "Area Awarded", ["22//2/2", "1-3-0", "1-0-0"], 2)).ToArray(), new[] { AwardIngestionCandidateType.AwardKhasra, AwardIngestionCandidateType.AwardKhasra }];
        yield return ["notification-heavy", Page("Section 4 Notification N-FIC/12 dated 01/01/2026", "Section 6 Notice S-FIC/13"), new[] { AwardIngestionCandidateType.Notification, AwardIngestionCandidateType.Notification }];
        yield return ["court-narrative", Page("CWP 123/2026 is listed. No Khasra table is present."), new[] { AwardIngestionCandidateType.UnmappedAwardFinding }];
        yield return ["claims-narrative", Page("Claimant names are listed in narrative text without a claim table."), new[] { AwardIngestionCandidateType.UnmappedAwardFinding }];
        yield return ["numeric-noise", Page("File 1627//154, note 02//26100 and ref 10106//213 are not Khasra table rows."), new[] { AwardIngestionCandidateType.UnmappedAwardFinding }];
    }

    [Theory]
    [MemberData(nameof(GoldenCorpus))]
    public void Golden_corpus_produces_exact_supported_candidate_types(string _, NormalizedDocumentPage[] pages, AwardIngestionCandidateType[] expected)
    {
        var candidates = Engine().Extract(pages, Context()).Select(x => x.Input.CandidateType).Order().ToArray();
        Assert.Equal(expected.Order(), candidates);
    }

    [Fact]
    public void Strict_khasra_grammar_accepts_supported_values_and_preserves_qualifier()
    {
        var parser = new StrictKhasraParser();
        Assert.True(parser.TryParse("22//2/1 min", out var parsed, out _));
        Assert.Equal("22//2/1", parsed.NormalizedNumber); Assert.Equal("min", parsed.Qualifier);
    }

    [Theory]
    [InlineData("1627//154")]
    [InlineData("02//26100")]
    [InlineData("10106//213")]
    public void Malformed_khasra_lookalikes_are_never_accepted(string value)
    {
        Assert.False(new StrictKhasraParser().TryParse(value, out _, out _));
    }

    [Fact]
    public void Narrative_khasra_value_without_table_context_is_not_extracted()
    {
        var candidates = Engine().Extract(Page("The court record merely mentions 22//2/1."), Context());
        Assert.DoesNotContain(candidates, x => x.Input.CandidateType == AwardIngestionCandidateType.AwardKhasra);
    }

    [Fact]
    public void Unreadable_award_label_becomes_a_review_item_without_guessed_digits()
    {
        var candidate=Assert.Single(Engine().Extract(Page("Award No: 30/20?2-2003"), Context()));
        var payload=JsonSerializer.Deserialize<AwardCoreCandidate>(candidate.Input.PayloadJson)!;
        Assert.Equal(AwardIngestionCandidateType.AwardCore,candidate.Input.CandidateType);
        Assert.Equal("",payload.AwardNumber);
        Assert.Contains(candidate.Evidence.Warnings,warning=>warning.Contains("no value was guessed",StringComparison.Ordinal));
    }

    [Fact]
    public void Exact_master_match_is_evidence_not_digit_correction()
    {
        var pages = TablePage("Khasra No", "Total Area", "Area Awarded", ["22//2/7", "1-2-3", "0-10-0"]);
        var context = Context([new("22//2/1", null, "22//2/1")]);
        var candidate = Assert.Single(Engine().Extract(pages, context));
        var payload = JsonSerializer.Deserialize<AwardKhasraCandidate>(candidate.Input.PayloadJson)!;
        Assert.Equal("22//2/7", payload.KhasraNumber);
        Assert.Equal("22//2/1", candidate.Evidence.PossibleCanonicalMatch);
        Assert.Contains(candidate.Evidence.Warnings, warning => warning.Contains("no digits were substituted", StringComparison.Ordinal));
    }

    [Fact]
    public void Village_is_extracted_before_dependent_records_with_exact_master_evidence()
    {
        var context = new AwardExtractionContext(null, null, null, [], [new("Fictional Village")]);
        var candidate = Assert.Single(Engine().Extract(Page("Village: Fictional Village"), context));
        Assert.Equal(AwardIngestionCandidateType.AwardVillage, candidate.Input.CandidateType);
        Assert.Equal("Fictional Village", candidate.Evidence.PossibleCanonicalMatch);
    }

    [Fact]
    public void Table_area_columns_remain_separate()
    {
        var candidate = Assert.Single(Engine().Extract(TablePage("Khasra No", "Total Area", "Area Awarded", ["22//2/1 min", "1-2-3", "0-10-0"]), Context()));
        var payload = JsonSerializer.Deserialize<AwardKhasraCandidate>(candidate.Input.PayloadJson)!;
        Assert.Equal(1m, payload.RecordedAreaBigha); Assert.Equal(2, payload.RecordedAreaBiswa); Assert.Equal(3, payload.RecordedAreaBiswansi);
        Assert.Equal(0m, payload.AwardedAreaBigha); Assert.Equal(10, payload.AwardedAreaBiswa); Assert.Equal(0, payload.AwardedAreaBiswansi);
        Assert.Equal("min", payload.Qualifier);
    }

    [Fact]
    public void Fictional_golden_benchmark_measures_zero_false_positive_supported_fields()
    {
        var pages = Page("Award No: FIC-2026/99", "Section 4 Notification N-FIC/12").Concat(TablePage("Khasra No", "Total Area", "Area Awarded", ["22//2/1", "1-2-3", "0-10-0"])).ToArray();
        var result = ExtractionBenchmark.Measure(Engine().Extract(pages, Context()), [
            new ExtractionTruth(AwardIngestionCandidateType.AwardCore, "FIC-2026/99"),
            new ExtractionTruth(AwardIngestionCandidateType.Notification, "Section 4|N-FIC/12"),
            new ExtractionTruth(AwardIngestionCandidateType.AwardKhasra, "22//2/1|")
        ]);
        Assert.Equal(1m, result.AutoReadyPrecision);
        Assert.All(result.ByCandidateType.Values, metric => Assert.Equal(1m, metric.Precision));
    }

    private static AwardExtractionRuleEngine Engine() => new(new TextConceptMatcher(), new StrictKhasraParser(), new StrictDateParser(), new StrictAreaParser());

    [Fact]
    public void Partially_mapped_page_retains_other_evidence_without_inventing_legal_records()
    {
        var candidates=Engine().Extract(Page("Village: Fictional Village", "CWP 123/2026 is mentioned but the order and affected parcels require reading.", "Claims received are discussed in the following paragraphs; names must not automatically merge Parties.", "There is a clerical mistake; the corrigendum will be considered separately."), Context());
        Assert.Contains(candidates,c=>c.Input.CandidateType==AwardIngestionCandidateType.AwardVillage);
        Assert.Contains(candidates,c=>c.Input.PayloadJson.Contains("Court Cases / CWPs"));
        Assert.Contains(candidates,c=>c.Input.PayloadJson.Contains("Area Issues / Corrigendum"));
        Assert.DoesNotContain(candidates,c=>c.Input.CandidateType is AwardIngestionCandidateType.CourtCase or AwardIngestionCandidateType.Party);
        Assert.All(candidates.Where(c=>c.Evidence.Rule=="ClassifiedNarrativeEvidence"),c=>Assert.NotEmpty(c.Evidence.Warnings));
    }

    [Fact]
    public void Explicit_possession_date_does_not_set_status_or_link_parcels()
    {
        var candidates=Engine().Extract(Page("The possession of part of the land has been taken over on 11-9-2002."), Context());
        var payload=JsonSerializer.Deserialize<PossessionEventCandidate>(Assert.Single(candidates).Input.PayloadJson)!;
        Assert.Equal(new DateOnly(2002,9,11),payload.PossessionDate);
        Assert.Null(payload.Status);
        Assert.Null(payload.EventType);
    }

    [Fact]
    public void Ocr_table_reads_top_to_bottom_and_keeps_deep_subdivision_and_attached_min()
    {
        var page = TablePage("Khasra No", "Total Area", "Area Awarded", ["12//21/2/1/1min", "4--12", "2--7"])[0];
        page = page with { Method = AwardDocumentExtractionMethod.LocalOcr, Tokens = page.Tokens.Select(t => t with { Y = 800 - t.Y, Confidence = .95m }).ToList() };
        var candidate = Assert.Single(Engine().Extract([page], Context()));
        var payload = JsonSerializer.Deserialize<AwardKhasraCandidate>(candidate.Input.PayloadJson)!;
        Assert.Equal("12//21/2/1/1", payload.KhasraNumber);
        Assert.Equal("min", payload.Qualifier);
        Assert.Equal(4m, payload.RecordedAreaBigha);
        Assert.Equal(12, payload.RecordedAreaBiswa);
        Assert.Null(payload.RecordedAreaBiswansi);
        Assert.Equal(2m, payload.AwardedAreaBigha);
        Assert.Equal(7, payload.AwardedAreaBiswa);
    }

    [Fact]
    public void Repeated_table_blocks_do_not_mix_area_cells()
    {
        var tokens = new List<DocumentToken>();
        for (var i = 0; i < 3; i++)
        {
            var x = i * 500;
            Add(tokens, "Khasra", x + 20, 700); Add(tokens, "Total Area", x + 180, 700); Add(tokens, "Area Awarded", x + 340, 700);
            Add(tokens, $"{i + 1}//2", x + 20, 676); Add(tokens, $"{i + 1}-2", x + 180, 676); Add(tokens, "0-1", x + 340, 676);
        }
        var candidates = Engine().Extract([new(1,1500,800,AwardDocumentExtractionMethod.EmbeddedText,"table",tokens)], Context()).Where(c=>c.Input.CandidateType==AwardIngestionCandidateType.AwardKhasra).ToList();
        Assert.Equal(3,candidates.Count);
        Assert.Equal(new decimal?[] {1,2,3}, candidates.Select(c=>JsonSerializer.Deserialize<AwardKhasraCandidate>(c.Input.PayloadJson)!.RecordedAreaBigha));
    }

    [Fact]
    public void Notification_reference_keeps_parentheses_ampersand_and_dotted_date()
    {
        var candidates = Engine().Extract(Page("U/S 17(1) No. F.10(99)/01/L&B/LA/123-456 dated 7.12.2001.", "U/S 17(1) vide notification no.F.10(99)/01/L&B/LA/123-456 dated 7.12.2001."), Context());
        var payload = JsonSerializer.Deserialize<NotificationCandidate>(Assert.Single(candidates).Input.PayloadJson)!;
        Assert.Equal("F.10(99)/01/L&B/LA/123-456",payload.NotificationNumber);
        Assert.Equal(new DateOnly(2001,12,7),payload.NotificationDate);
    }

    [Fact]
    public void Award_core_preserves_explicit_supplementary_parent_and_narrative_fields()
    {
        var candidates=Engine().Extract(Page("Supplementary Award", "Award No: SUP-12/2026", "Main Award No. MAIN-7/2025", "Nature of Acquisition: Permanent.", "Purpose of Acquisition: fictional road widening."),Context());
        var core=JsonSerializer.Deserialize<AwardCoreCandidate>(Assert.Single(candidates,c=>c.Input.CandidateType==AwardIngestionCandidateType.AwardCore).Input.PayloadJson)!;
        Assert.Equal("SUP-12/2026",core.AwardNumber); Assert.Equal("Supplementary",core.AwardType); Assert.Equal("MAIN-7/2025",core.ParentAwardReferenceSuggestion);
        Assert.Equal("Permanent",core.NatureOfAcquisition); Assert.Equal("fictional road widening",core.Purpose);
    }

    [Fact]
    public void Revenue_estate_label_and_award_number_are_strict_values_not_fuzzy_repaired()
    {
        var candidates=Engine().Extract(Page("Award No: A-10/2026", "Revenue Estate: Fictional Estate"),new AwardExtractionContext(Guid.NewGuid(),null,null,[],[new("Fictional Estate")]));
        var village=JsonSerializer.Deserialize<AwardVillageCandidate>(Assert.Single(candidates,c=>c.Input.CandidateType==AwardIngestionCandidateType.AwardVillage).Input.PayloadJson)!;
        Assert.Equal("Fictional Estate",village.VillageName);
        Assert.DoesNotContain(Engine().Extract(Page("Award No: A-1O/2O26"),Context()),c=>c.Input.CandidateType==AwardIngestionCandidateType.AwardCore && c.Input.PayloadJson.Contains("A-10/2026"));
    }

    [Fact]
    public void La_and_nh_statutory_references_keep_their_own_framework_and_sections()
    {
        var la=JsonSerializer.Deserialize<NotificationCandidate>(Assert.Single(Engine().Extract(Page("Land Acquisition Act Section 4 Notification No. L-4/12 dated 01/01/2026"),Context()).Where(c=>c.Input.CandidateType==AwardIngestionCandidateType.Notification)).Input.PayloadJson)!;
        var nh=JsonSerializer.Deserialize<NotificationCandidate>(Assert.Single(Engine().Extract(Page("National Highways Act Section 3A Notification No. NH-3A/22 dated 02/02/2026"),Context()).Where(c=>c.Input.CandidateType==AwardIngestionCandidateType.Notification)).Input.PayloadJson)!;
        Assert.Equal("Land Acquisition Act · Section 4",la.SectionType); Assert.Equal("National Highways Act · Section 3A",nh.SectionType);
        Assert.Equal("NH-3A/22",nh.NotificationNumber);
    }

    [Fact]
    public void Bare_statutory_reference_is_retained_without_fabricating_notification()
    {
        var candidates=Engine().Extract(Page("National Highways Act Section 3D is referred to in this order."),Context());
        Assert.DoesNotContain(candidates,c=>c.Input.CandidateType==AwardIngestionCandidateType.Notification);
        Assert.Contains(candidates,c=>c.Input.PayloadJson.Contains("Statutory reference"));
    }

    [Fact]
    public void Repeated_khasra_with_different_area_is_retained_for_conflict_review()
    {
        var pages=TablePage("Khasra", "Total Area", "Area Awarded", ["22//2/7", "1-2", "0-1"])
            .Concat(TablePage("Khasra", "Total Area", "Area Awarded", ["22//2/7", "2-2", "1-1"],2)).ToArray();
        var context=Context([new("22//2/1",null,"22//2/1"),new("22//2/2",null,"22//2/2")]);
        var candidates=Engine().Extract(pages,context);
        Assert.Equal(2,candidates.Count);
        Assert.All(candidates,c=>Assert.Null(c.Evidence.PossibleCanonicalMatch));
    }
    private static AwardExtractionContext Context(IReadOnlyList<ExtractionCanonicalKhasra>? khasras = null) => new(Guid.NewGuid(), Guid.NewGuid(), "Fictional Village", khasras ?? [new("22//2/1", null, "22//2/1")]);
    private static NormalizedDocumentPage[] Page(params string[] lines) => Page(lines, 700);
    private static NormalizedDocumentPage[] Page(string line, decimal y) => Page([line], y);
    private static NormalizedDocumentPage[] Page(string[] lines, decimal y)
    {
        var tokens = new List<DocumentToken>(); var currentY = y;
        foreach (var line in lines) { var x = 20m; foreach (var word in line.Split(' ')) { tokens.Add(new(word, x, currentY, word.Length * 7, 12, 95)); x += word.Length * 7 + 5; } currentY -= 24; }
        return [new(1, 600, 800, AwardDocumentExtractionMethod.EmbeddedText, string.Join("\n", lines), tokens)];
    }
    private static NormalizedDocumentPage[] TablePage(string khasraHeader, string totalHeader, string awardedHeader, string[] row, int pageNumber = 1)
    {
        var tokens = new List<DocumentToken>(); Add(tokens, khasraHeader, 20, 700); Add(tokens, totalHeader, 220, 700); Add(tokens, awardedHeader, 420, 700); Add(tokens, row[0], 20, 676); Add(tokens, row[1], 220, 676); Add(tokens, row[2], 420, 676);
        return [new(pageNumber, 600, 800, AwardDocumentExtractionMethod.EmbeddedText, string.Join(" ", tokens.Select(x => x.Text)), tokens)];
    }
    private static NormalizedDocumentPage[] ReorderedTablePage()
    {
        var tokens = new List<DocumentToken>(); Add(tokens, "Area Awarded", 20, 700); Add(tokens, "Khasra No", 220, 700); Add(tokens, "Total Area", 420, 700); Add(tokens, "0-10-0", 20, 676); Add(tokens, "22//2/1", 220, 676); Add(tokens, "1-2-3", 420, 676);
        return [new(1, 600, 800, AwardDocumentExtractionMethod.EmbeddedText, string.Join(" ", tokens.Select(x => x.Text)), tokens)];
    }
    private static void Add(ICollection<DocumentToken> tokens, string text, decimal x, decimal y) { foreach (var word in text.Split(' ')) { tokens.Add(new(word, x, y, word.Length * 7, 12, 95)); x += word.Length * 7 + 5; } }
}
