using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LAC.Domain;

namespace LAC.Infrastructure;

public static class AwardExtractionRuleSet
{
    public const string Version = "AwardRules/1.1";
}

public enum AwardDocumentConcept { AwardNumber, AwardDate, Village, Khasra, TotalArea, AreaAwarded, Notification, Possession, CourtCase, Claim, MarketValue, Compensation, Supplementary }
public enum TextMatchKind { None, Exact, Alias, Fuzzy }
public sealed record TextConceptMatch(AwardDocumentConcept Concept, TextMatchKind Kind, decimal Confidence, string MatchedAlias);
public sealed record ParsedKhasra(string NormalizedNumber, string? Qualifier);
public sealed record ParsedArea(decimal? Bigha, int? Biswa, int? Biswansi);
public sealed record CandidateEvidence(int Page, string RuleVersion, string Rule, string Reason, IReadOnlyList<string> Gates, IReadOnlyList<string> Warnings, string? PossibleCanonicalMatch = null);
public sealed record ExtractionCanonicalKhasra(string NormalizedNumber, string? Qualifier, string DisplayNumber);
public sealed record ExtractionCanonicalVillage(string Name);
public sealed record AwardExtractionContext(Guid? TargetAwardId, Guid? VillageId, string? VillageName, IReadOnlyList<ExtractionCanonicalKhasra> CanonicalKhasras, IReadOnlyList<ExtractionCanonicalVillage>? CanonicalVillages = null);

/// <summary>Central vocabulary. Aliases are semantic labels only; this is never used to repair identifiers or numbers.</summary>
public static class AwardLexicon
{
    public static readonly IReadOnlyDictionary<AwardDocumentConcept, string[]> Aliases = new Dictionary<AwardDocumentConcept, string[]>
    {
        [AwardDocumentConcept.AwardNumber] = ["award no", "award number", "award nos"],
        [AwardDocumentConcept.AwardDate] = ["award date", "date of award"],
        [AwardDocumentConcept.Village] = ["village", "village name", "mauza", "revenue estate"],
        [AwardDocumentConcept.Khasra] = ["khasra", "khasra no", "khasra number"],
        [AwardDocumentConcept.TotalArea] = ["total area", "total area of khasra"],
        [AwardDocumentConcept.AreaAwarded] = ["area awarded", "awarded area", "area acquired", "land awarded"],
        [AwardDocumentConcept.Notification] = ["section 4", "sec 4", "u s 4", "under section 4", "section 6", "sec 6", "u s 6", "section 17", "section 17 1", "u s 17 1"],
        [AwardDocumentConcept.Possession] = ["possession", "physical possession", "possession taken", "taken over"],
        [AwardDocumentConcept.CourtCase] = ["cwp", "w p", "writ petition", "civil writ petition", "case no"],
        [AwardDocumentConcept.Claim] = ["claimant", "claim", "claims", "compensation claimed"],
        [AwardDocumentConcept.MarketValue] = ["market value", "market rate", "rate per acre", "rate per bigha"],
        [AwardDocumentConcept.Compensation] = ["solatium", "additional amount", "interest"],
        [AwardDocumentConcept.Supplementary] = ["structure", "structures", "trees", "tubewell", "supplementary award"]
    };
}

public sealed class TextConceptMatcher
{
    public TextConceptMatch MatchHeading(string text, AwardDocumentConcept concept)
    {
        var normalized = NormalizeLabel(text);
        if (normalized.Length == 0) return new(concept, TextMatchKind.None, 0, "");
        foreach (var alias in AwardLexicon.Aliases[concept])
        {
            var normalizedAlias = NormalizeLabel(alias);
            if (normalized.Contains(normalizedAlias, StringComparison.Ordinal)) return new(concept, normalized == normalizedAlias ? TextMatchKind.Exact : TextMatchKind.Alias, 1m, alias);
        }
        // Fuzzy matching is intentionally confined to short heading-like text.
        if (normalized.Length > 48 || Regex.IsMatch(normalized, @"\d")) return new(concept, TextMatchKind.None, 0, "");
        var best = AwardLexicon.Aliases[concept].Select(alias => new { Alias = alias, Score = Similarity(normalized, NormalizeLabel(alias)) }).OrderByDescending(x => x.Score).First();
        return best.Score >= .84m ? new(concept, TextMatchKind.Fuzzy, best.Score, best.Alias) : new(concept, TextMatchKind.None, 0, "");
    }

    public bool HasContext(string text, AwardDocumentConcept concept) => MatchHeading(text, concept).Kind != TextMatchKind.None;
    public static string NormalizeLabel(string value) => Regex.Replace(value.ToLowerInvariant().Replace("0", "o"), "[^a-z]+", " ").Trim();
    private static decimal Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        var distance = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) distance[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) distance[0, j] = j;
        for (var i = 1; i <= a.Length; i++) for (var j = 1; j <= b.Length; j++) distance[i, j] = Math.Min(Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1), distance[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return 1m - ((decimal)distance[a.Length, b.Length] / Math.Max(a.Length, b.Length));
    }
}

/// <summary>Strict grammar: no OCR digit substitution and deliberately bounded rectangle/killa segments.</summary>
public sealed class StrictKhasraParser
{
    private static readonly Regex Pattern = new(@"^(?<number>[1-9]\d{0,2}//[1-9]\d{0,2}(?:/[1-9]\d{0,2})*)(?:\s*(?<qualifier>min))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public bool TryParse(string value, out ParsedKhasra parsed, out string reason)
    {
        parsed = default!; reason = "";
        var compact = Regex.Replace(value.Trim(), @"\s+", " ");
        var match = Pattern.Match(compact);
        if (!match.Success) { reason = "Khasra does not match the supported rectangle//killa[/subdivision] grammar."; return false; }
        // Values such as 1627//154 and 02//26100 are intentionally rejected before matching any master record.
        var normalized = match.Groups["number"].Value;
        parsed = new ParsedKhasra(normalized, match.Groups["qualifier"].Success ? "min" : null);
        return true;
    }
}

public sealed class StrictDateParser
{
    private static readonly string[] Formats = ["d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy"];
    public bool TryParse(string value, out DateOnly parsed) => DateOnly.TryParseExact(value.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);
}

public sealed class StrictAreaParser
{
    private static readonly Regex Triple = new(@"^(?<b>\d{1,4}(?:\.\d{1,4})?)\s*[-,\s]\s*(?<w>\d{1,2})\s*[-,\s]\s*(?<bw>\d{1,2})$", RegexOptions.Compiled);
    public bool TryParse(string value, out ParsedArea area)
    {
        area = default!;
        var pair = Regex.Match(value.Trim(), @"^(?<b>\d{1,4})\s*(?:--?|–|—|\s)\s*(?<w>\d{1,2})$");
        if (pair.Success && int.TryParse(pair.Groups["w"].Value, out var pairBiswa) && pairBiswa <= 19)
        {
            area = new ParsedArea(decimal.Parse(pair.Groups["b"].Value, CultureInfo.InvariantCulture), pairBiswa, null);
            return true;
        }
        var match = Triple.Match(Regex.Replace(value.Trim(), @"\s+", " "));
        if (!match.Success || !decimal.TryParse(match.Groups["b"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var bigha) || !int.TryParse(match.Groups["w"].Value, out var biswa) || !int.TryParse(match.Groups["bw"].Value, out var biswansi) || biswa > 19 || biswansi > 19) return false;
        area = new ParsedArea(bigha, biswa, biswansi); return true;
    }
}

public sealed record DocumentLine(int PageNumber, decimal Baseline, IReadOnlyList<DocumentToken> Tokens)
{
    public string Text => string.Join(" ", Tokens.OrderBy(x => x.X).Select(x => x.Text));
}

public static class DocumentLayout
{
    public static IReadOnlyList<DocumentLine> Lines(NormalizedDocumentPage page)
    {
        // Preserve native coordinates for source evidence, but read top-to-bottom in both systems.
        var lines = new List<List<DocumentToken>>();
        foreach (var token in page.Tokens.Where(x => !string.IsNullOrWhiteSpace(x.Text)).OrderByDescending(x => x.Y).ThenBy(x => x.X))
        {
            var line = lines.FirstOrDefault(existing => Math.Abs(existing.Average(x => x.Y) - token.Y) <= Math.Max(4m, token.Height * .65m));
            if (line is null) lines.Add([token]); else line.Add(token);
        }
        var result = lines.Select(line => new DocumentLine(page.PageNumber, line.Average(x => x.Y), line.OrderBy(x => x.X).ToList()));
        return (page.Method == AwardDocumentExtractionMethod.LocalOcr ? result.OrderBy(x => x.Baseline) : result.OrderByDescending(x => x.Baseline)).ToList();
    }
}

public sealed record ExtractionCandidate(IngestionCandidateInput Input, CandidateEvidence Evidence);
public sealed record TableColumn(decimal Start, decimal End);
public sealed record KhasraTableColumns(TableColumn Khasra, TableColumn? TotalArea, TableColumn? AwardedArea);

/// <summary>Two-pass, deterministic document rule engine. It never attempts to correct numeric content.</summary>
public sealed class AwardExtractionRuleEngine(TextConceptMatcher matcher, StrictKhasraParser khasraParser, StrictDateParser dateParser, StrictAreaParser areaParser)
{
    public IReadOnlyList<ExtractionCandidate> Extract(IReadOnlyList<NormalizedDocumentPage> pages, AwardExtractionContext context)
    {
        var allLines = pages.SelectMany(DocumentLayout.Lines).ToList();
        var result = new List<ExtractionCandidate>();
        ExtractAwardIdentity(allLines, result);
        ExtractVillage(allLines, context, result);
        ExtractNotifications(allLines, result);
        ExtractKhasraTables(pages, context, result);
        ExtractNarrativeEvidence(pages, result);
        AddUnmappedPages(pages, result);
        return result;
    }

    private void ExtractAwardIdentity(IReadOnlyList<DocumentLine> lines, ICollection<ExtractionCandidate> output)
    {
        DocumentLine? unreadableLabel = null;
        foreach (var line in lines)
        {
            var labelText = line.Text.Split([':', '#'], 2)[0];
            var label = matcher.MatchHeading(labelText, AwardDocumentConcept.AwardNumber);
            if (label.Kind == TextMatchKind.None || ContainsNegativeAwardContext(line.Text)) continue;
            var value = ValueAfterLabel(line.Text, label.MatchedAlias);
            if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value, @"^[A-Za-z0-9][A-Za-z0-9/.-]{1,49}$")) { unreadableLabel ??= line; continue; }
            var pageLines = lines.Where(other => other.PageNumber == line.PageNumber).ToList();
            var nearbyDate = pageLines.Where(other => Math.Abs(other.Baseline - line.Baseline) < 80m).Select(other => ExtractDate(other.Text)).FirstOrDefault(date => date is not null);
            var fullPage = string.Join("\n", pageLines.Select(x => x.Text));
            var supplementary = Regex.IsMatch(fullPage, @"\bsupplementary\s+award\b", RegexOptions.IgnoreCase);
            var main = !supplementary && Regex.IsMatch(fullPage, @"\bmain\s+award\b", RegexOptions.IgnoreCase);
            var parent = supplementary ? MatchExplicitValue(fullPage, @"\b(?:parent|main)\s+award\s*(?:no\.?|number)?\s*[:.-]?\s*(?<value>[A-Za-z0-9][A-Za-z0-9/.-]{1,49})") : null;
            var nature = FindLabelledValue(pageLines, AwardDocumentConcept.Supplementary, @"\bnature\s+of\s+acquisition\b");
            var purpose = FindLabelledValue(pageLines, AwardDocumentConcept.Supplementary, @"\bpurpose\s+of\s+acquisition\b");
            var area = FindLabelledValue(pageLines, AwardDocumentConcept.Supplementary, @"\b(?:awarded|acquisition)\s+area\b");
            Add(output, new AwardCoreCandidate(value, nearbyDate, supplementary ? "Supplementary" : main ? "Main" : null, purpose, nature, area, parent), line.PageNumber, "AwardIdentity", $"{label.Kind} Award Number label", ["Award Number label", "strict identifier syntax"], [] , line.Text);
            return;
        }
        // When an Award label exists but its value is unreadable, preserve the
        // explicit review task rather than inventing digits.  Documents with no
        // Award identity label remain neutral evidence rather than false findings.
        if (unreadableLabel is null) return;
        Add(output, new AwardCoreCandidate("", null, null, null), unreadableLabel.PageNumber,
            "AwardIdentity", "Award number could not be read with strict syntax", [],
            ["Award number needs confirmation from the source document; no value was guessed."], unreadableLabel.Text);
    }

    private void ExtractNotifications(IReadOnlyList<DocumentLine> lines, ICollection<ExtractionCandidate> output)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var section = Regex.Match(line.Text, @"(?:^|\b)(?:section|sec\.?|u\s*/?\s*s\.?|under\s+section)\s*(?<section>3\s*[AD]|17\s*\(\s*1\s*\)|17|4|6)(?!\d)", RegexOptions.IgnoreCase);
            if (!section.Success) continue;
            var after = line.Text[(section.Index + section.Length)..];
            var number = Regex.Match(after, @"^\s*(?:vide\s+)?(?:(?:notification|notice)\s*)?(?:no\.?\s*)?(?<number>[A-Za-z][A-Za-z0-9()./& -]*?)(?:\s*dated\b.*|$)", RegexOptions.IgnoreCase);
            var identifier = number.Groups["number"].Value.Trim().TrimEnd('.');
            if (!number.Success || !Regex.IsMatch(identifier, @"\d") || !Regex.IsMatch(identifier, @"[/.-]") || identifier.Length > 150 || Regex.IsMatch(identifier, @"\b(notification|vide|land|act)\b", RegexOptions.IgnoreCase))
            {
                Add(output, new UnmappedAwardFindingCandidate("Statutory reference", "A legal provision was detected without a safe notification identifier.", line.Text), line.PageNumber, "StatutoryReference", "legal provision without a safe notification identifier", ["section context"], ["Legal reference retained; no Notification was fabricated."], line.Text);
                continue;
            }
            var key = Regex.Replace(section.Groups["section"].Value + "|" + identifier, @"\s+", "");
            if (!seen.Add(key)) continue;
            var pageText = string.Join(" ", lines.Where(x => x.PageNumber == line.PageNumber).Select(x => x.Text));
            var framework = Regex.IsMatch(pageText, @"national\s+highways?|\bnh\s+act\b", RegexOptions.IgnoreCase) ? "National Highways Act" : Regex.IsMatch(pageText, @"land\s+acquisition\s+act|\bla\s+act\b", RegexOptions.IgnoreCase) ? "Land Acquisition Act" : null;
            var sectionText = "Section " + Regex.Replace(section.Groups["section"].Value, @"\s+", "").ToUpperInvariant();
            Add(output, new NotificationCandidate(framework is null ? sectionText : $"{framework} · {sectionText}", identifier, ExtractDate(after)), line.PageNumber, "Notification", "section label with adjacent notification identifier", ["section context", "identifier adjacent to section"], ["Verify the complete notification reference and date against the source."], line.Text);
        }
    }

    private static string? FindLabelledValue(IReadOnlyList<DocumentLine> lines, AwardDocumentConcept _, string label)
        => lines.Select(line => MatchExplicitValue(line.Text, label + @"\s*[:.-]\s*(?<value>.{1,180})")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static string? MatchExplicitValue(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Trim().TrimEnd('.') : null;
    }

    private void ExtractVillage(IReadOnlyList<DocumentLine> lines, AwardExtractionContext context, ICollection<ExtractionCandidate> output)
    {
        foreach (var line in lines)
        {
            var labelText = line.Text.Split([':', '#'], 2)[0]; var label = matcher.MatchHeading(labelText, AwardDocumentConcept.Village);
            if (label.Kind == TextMatchKind.None) continue;
            var value = ValueAfterLabel(line.Text, label.MatchedAlias);
            if (!Regex.IsMatch(value, @"^[A-Za-z][A-Za-z .'-]{1,99}$")) continue;
            var exact = context.CanonicalVillages?.SingleOrDefault(village => TextConceptMatcher.NormalizeLabel(village.Name) == TextConceptMatcher.NormalizeLabel(value));
            Add(output, new AwardVillageCandidate(value, exact?.Name), line.PageNumber, "VillageIdentity", $"{label.Kind} Village label", ["Village label", "strict text syntax"], exact is null ? ["No exact official Village master match."] : [], line.Text, exact?.Name);
            break;
        }
    }

    private void ExtractKhasraTables(IReadOnlyList<NormalizedDocumentPage> pages, AwardExtractionContext context, ICollection<ExtractionCandidate> output)
    {
        foreach (var page in pages)
        {
            var lines = DocumentLayout.Lines(page);
            for (var headerIndex = 0; headerIndex < lines.Count; headerIndex++)
            {
                var header = lines[headerIndex];
                var khasraHeader = matcher.MatchHeading(header.Text, AwardDocumentConcept.Khasra);
                if (khasraHeader.Kind == TextMatchKind.None) continue;
                if (headerIndex + 1 < lines.Count)
                {
                    var next = lines[headerIndex + 1];
                    if (Math.Abs(next.Baseline - header.Baseline) < header.Tokens.Average(t => t.Height) * 2 &&
                        (matcher.HasContext(next.Text, AwardDocumentConcept.TotalArea) || matcher.HasContext(next.Text, AwardDocumentConcept.AreaAwarded)))
                    {
                        header = new DocumentLine(page.PageNumber, header.Baseline, header.Tokens.Concat(next.Tokens).OrderBy(t => t.X).ToList());
                        headerIndex++;
                    }
                }
                var totalHeader = matcher.MatchHeading(header.Text, AwardDocumentConcept.TotalArea);
                var awardedHeader = matcher.MatchHeading(header.Text, AwardDocumentConcept.AreaAwarded);
                if (totalHeader.Kind == TextMatchKind.None && awardedHeader.Kind == TextMatchKind.None) continue;
                var tables = RepeatedColumnBoundaries(header);
                foreach (var boundaries in tables)
                {
                var blockEnd = new[] { boundaries.Khasra.End, boundaries.TotalArea?.End ?? 0, boundaries.AwardedArea?.End ?? 0 }.Max();
                var headerEdge = page.Method == AwardDocumentExtractionMethod.LocalOcr ? header.Tokens.Max(t => t.Y + t.Height) : header.Tokens.Min(t => t.Y);
                var blockTokens = page.Tokens.Where(t => t.X >= boundaries.Khasra.Start && t.X < blockEnd &&
                    (page.Method == AwardDocumentExtractionMethod.LocalOcr ? t.Y > headerEdge : t.Y + t.Height < headerEdge)).ToList();
                foreach (var row in DocumentLayout.Lines(page with { Tokens = blockTokens }))
                {
                    if (matcher.HasContext(row.Text, AwardDocumentConcept.Khasra)) break;
                    var cells = SplitCells(row, boundaries);
                    if (cells.Khasra.Length == 0) continue;
                    if (!khasraParser.TryParse(cells.Khasra, out var parsed, out _)) continue;
                    var hasRecorded = areaParser.TryParse(cells.TotalArea, out var recorded);
                    var hasAwarded = areaParser.TryParse(cells.AwardedArea, out var awarded);
                    var warnings = new List<string>();
                    if (!hasRecorded && !hasAwarded) warnings.Add("Area columns could not be safely interpreted.");
                    var exact = context.CanonicalKhasras.SingleOrDefault(x => x.NormalizedNumber == parsed.NormalizedNumber && x.Qualifier == parsed.Qualifier);
                    var possibleMatches = context.CanonicalKhasras.Where(x => KhasraStem(x.NormalizedNumber) == KhasraStem(parsed.NormalizedNumber)).Select(x => x.DisplayNumber).Distinct().Take(2).ToList();
                    var possible = exact?.DisplayNumber ?? (possibleMatches.Count == 1 ? possibleMatches[0] : null);
                    if (possibleMatches.Count > 1 && exact is null) warnings.Add("Multiple possible master records; no match selected.");
                    var numericConfidence = page.Method == AwardDocumentExtractionMethod.EmbeddedText ? 1m : row.Tokens.Min(t => t.Confidence ?? 0m);
                    if (numericConfidence < .98m || exact is null || !hasRecorded || !hasAwarded)
                        warnings.Add("Verify the Khasra identifier, qualifier and both area columns against the source page.");
                    var gates = new List<string> { "Khasra table header", "strict Khasra grammar" };
                    if (context.VillageId is not null) gates.Add("known Award Village"); else warnings.Add("Village context is unresolved.");
                    if (exact is not null) gates.Add("exact Village master match");
                    else if (possible is not null) warnings.Add("Detected Khasra differs from a possible Village master record; no digits were substituted.");
                    var payload = new AwardKhasraCandidate(parsed.NormalizedNumber, parsed.Qualifier, null, null, null, hasRecorded ? recorded.Bigha : null, hasRecorded ? recorded.Biswa : null, hasRecorded ? recorded.Biswansi : null, hasAwarded ? awarded.Bigha : null, hasAwarded ? awarded.Biswa : null, hasAwarded ? awarded.Biswansi : null);
                    Add(output, payload, page.PageNumber, "KhasraTable", "geometry-bound Khasra table row", gates, warnings, row.Text, possible, numericConfidence);
                }
                }
            }
        }
    }

    private void AddUnmappedPages(IReadOnlyList<NormalizedDocumentPage> pages, ICollection<ExtractionCandidate> output)
    {
        foreach (var page in pages)
        {
            var mapped = output.Where(x => x.Evidence.Page == page.PageNumber).ToList();
            var remainder = DocumentLayout.Lines(page).Where(l => !mapped.Any(c => c.Input.RawSourceText?.Contains(l.Text, StringComparison.Ordinal) == true)).ToList();
            if (mapped.Count > 0 && string.Join(" ", remainder.Select(l => l.Text)).Length < 120) continue;
            if (page.Text.Length == 0 && page.Method != AwardDocumentExtractionMethod.Unavailable) continue;
            Add(output, new UnmappedAwardFindingCandidate(page.Method == AwardDocumentExtractionMethod.Unavailable ? "Unreadable page" : "Other / Unmapped Finding", mapped.Count == 0 ? "No safe structured rule applied to this page." : "This page is only partially mapped; review the remaining evidence as well.", page.Text), page.PageNumber, "Unmapped", "Uninterpreted content is retained even on partially mapped pages", [], ["Human review required."], page.Text);
        }
    }

    private void ExtractNarrativeEvidence(IReadOnlyList<NormalizedDocumentPage> pages, ICollection<ExtractionCandidate> output)
    {
        foreach (var page in pages)
        {
            var lines = DocumentLayout.Lines(page);
            foreach (var line in lines)
            {
                if (Regex.IsMatch(line.Text, @"\bpossession\b.*\b(?:has been taken|was taken|taken over)\b", RegexOptions.IgnoreCase) && ExtractDate(line.Text) is DateOnly date)
                    Add(output, new PossessionEventCandidate(date, null, null), page.PageNumber, "PossessionNarrative", "Explicit dated possession statement", ["possession statement", "strict date"], ["Confirm event scope and affected Khasras; this does not imply possession of all Award land or change Award status."], line.Text);
            }
            // Evidence buckets are deliberately not canonical legal records. Numeric rates, claimant
            // identities, affected parcels and stay/possession scope are never inferred from keywords.
            var categories = new Dictionary<string, string>
            {
                ["Purpose / Nature"] = @"\b(?:purpose|nature)\s+of\s+acquisition\b",
                ["Court Cases / CWPs"] = @"\b(?:cwp|c\.?\s*w\.?\s*p\.?|writ petition)\b",
                ["Claims"] = @"\b(?:claimant|claims|compensation claimed)\b",
                ["Area Issues / Corrigendum"] = @"\b(?:corrigendum|clerical mistakes|totaling mistake|totalling mistake)\b",
                ["Valuation Rules"] = @"\b(?:market value|market rate|rate per acre|rate per bigha)\b",
                ["Compensation Rules"] = @"\b(?:solatium|additional amount|interest)\b",
                ["Land Classification"] = @"\b(?:land classification|category of land|class of land)\b",
                ["Supplementary Matters"] = @"\b(?:supplementary|tubewell|structures|trees)\b"
            };
            foreach (var category in categories)
            {
                var matching = lines.Where(l => Regex.IsMatch(l.Text, category.Value, RegexOptions.IgnoreCase)).ToList();
                if (matching.Count == 0) continue;
                var raw = string.Join("\n", matching.Select(l => l.Text));
                Add(output, new UnmappedAwardFindingCandidate(category.Key, "Source mentions this subject; structured fields and legal scope still require review.", raw), page.PageNumber, "ClassifiedNarrativeEvidence", category.Key + " evidence", ["literal subject mention"], ["Evidence only, not a verified structured record. Do not infer stay, merge Parties, assign rates or link all Khasras."], raw);
            }
        }
    }

    private void Add(ICollection<ExtractionCandidate> target, IAwardIngestionCandidatePayload payload, int page, string rule, string reason, IReadOnlyList<string> gates, IReadOnlyList<string> warnings, string raw, string? possibleCanonicalMatch = null, decimal? confidence = null)
    {
        var evidence = new CandidateEvidence(page, AwardExtractionRuleSet.Version, rule, reason, gates, warnings, possibleCanonicalMatch);
        target.Add(new(new(payload.CandidateType, JsonSerializer.Serialize(payload, payload.GetType()), JsonSerializer.Serialize(evidence), raw.Length > 1200 ? raw[..1200] : raw, confidence), evidence));
    }

    private static IReadOnlyList<KhasraTableColumns> RepeatedColumnBoundaries(DocumentLine header)
    {
        var tokens = header.Tokens.SelectMany(t =>
        {
            var match = Regex.Match(t.Text, @"^(?<serial>recno)(?<label>khasra)$", RegexOptions.IgnoreCase);
            if (!match.Success) return new[] { t };
            var fraction = (decimal)match.Groups["serial"].Length / t.Text.Length;
            return new[] { t with { Text = "RecNo", Width = t.Width * fraction }, t with { Text = "Khasra", X = t.X + t.Width * fraction, Width = t.Width * (1 - fraction) } };
        }).OrderBy(x => x.X).ToList();
        var starts = new List<(AwardDocumentConcept Concept, decimal X)>();
        for (var i = 0; i < tokens.Count; i++)
        {
            foreach (var concept in new[] { AwardDocumentConcept.Khasra, AwardDocumentConcept.TotalArea, AwardDocumentConcept.AreaAwarded })
            {
                foreach (var alias in AwardLexicon.Aliases[concept].OrderByDescending(x => x.Length))
                {
                    var words = alias.Split(' ');
                    if (i + words.Length <= tokens.Count && words.SequenceEqual(tokens.Skip(i).Take(words.Length).Select(x => TextConceptMatcher.NormalizeLabel(x.Text))))
                    { starts.Add((concept, tokens[i].X)); break; }
                }
            }
        }
        starts = starts.Distinct().OrderBy(x => x.X).ToList();
        var gutter = tokens.Count == 0 ? 0 : tokens.Average(t => t.Height) * .6m;
        var result = new List<KhasraTableColumns>();
        // Each repeated Khasra heading starts a new block; preceding serial columns are excluded.
        var khasraStarts = starts.Where(x => x.Concept == AwardDocumentConcept.Khasra).ToList();
        if (khasraStarts.Count <= 1)
        {
            var single = ColumnBoundaries(header);
            if (single is not null) result.Add(single);
            return result;
        }
        for (var index = 0; index < khasraStarts.Count; index++)
        {
            var start = khasraStarts[index].X;
            var precedingSerial = tokens.LastOrDefault(t => t.X < start && (index == 0 || t.X > khasraStarts[index - 1].X) && Regex.IsMatch(t.Text, @"^rec[e]?no\.?$", RegexOptions.IgnoreCase));
            var end = index + 1 < khasraStarts.Count ? khasraStarts[index + 1].X : decimal.MaxValue;
            var serial = tokens.FirstOrDefault(t => t.X > start && t.X < end && Regex.IsMatch(t.Text, @"^rec\s*no\.?$", RegexOptions.IgnoreCase));
            if (serial is not null) end = serial.X;
            var group = starts.Where(x => x.X >= start && x.X < end).ToList();
            if (group.Count < 2) continue;
            TableColumn? Column(AwardDocumentConcept concept)
            {
                var position = group.FindIndex(x => x.Concept == concept);
                return position < 0 ? null : new((concept == AwardDocumentConcept.Khasra && precedingSerial is not null ? precedingSerial.X : group[position].X) - gutter, (position + 1 < group.Count ? group[position + 1].X : end) - gutter);
            }
            result.Add(new(Column(AwardDocumentConcept.Khasra)!, Column(AwardDocumentConcept.TotalArea), Column(AwardDocumentConcept.AreaAwarded)));
        }
        return result;
    }

    private static KhasraTableColumns? ColumnBoundaries(DocumentLine header)
    {
        decimal Start(AwardDocumentConcept concept)
        {
            var words = header.Tokens.OrderBy(token => token.X).Select(token => TextConceptMatcher.NormalizeLabel(token.Text)).ToList();
            foreach (var alias in AwardLexicon.Aliases[concept])
            {
                var aliasWords = TextConceptMatcher.NormalizeLabel(alias).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (var index = 0; index <= words.Count - aliasWords.Length; index++) if (aliasWords.SequenceEqual(words.Skip(index).Take(aliasWords.Length))) return header.Tokens.OrderBy(token => token.X).ElementAt(index).X;
            }
            return decimal.MaxValue;
        }
        var starts = new Dictionary<AwardDocumentConcept, decimal>
        {
            [AwardDocumentConcept.Khasra] = Start(AwardDocumentConcept.Khasra),
            [AwardDocumentConcept.TotalArea] = Start(AwardDocumentConcept.TotalArea),
            [AwardDocumentConcept.AreaAwarded] = Start(AwardDocumentConcept.AreaAwarded)
        }.Where(pair => pair.Value != decimal.MaxValue).OrderBy(pair => pair.Value).ToList();
        if (!starts.Any(pair => pair.Key == AwardDocumentConcept.Khasra) || starts.Count < 2) return null;
        TableColumn Column(AwardDocumentConcept concept)
        {
            var index = starts.FindIndex(pair => pair.Key == concept);
            return new(starts[index].Value, index + 1 < starts.Count ? starts[index + 1].Value : decimal.MaxValue);
        }
        return new(Column(AwardDocumentConcept.Khasra), starts.Any(pair => pair.Key == AwardDocumentConcept.TotalArea) ? Column(AwardDocumentConcept.TotalArea) : null, starts.Any(pair => pair.Key == AwardDocumentConcept.AreaAwarded) ? Column(AwardDocumentConcept.AreaAwarded) : null);
    }

    private static (string Khasra, string TotalArea, string AwardedArea) SplitCells(DocumentLine row, KhasraTableColumns columns)
    {
        IEnumerable<string> Cell(TableColumn? column) => column is null ? [] : row.Tokens.Where(token => token.X >= column.Start && token.X < column.End).Select(token => token.Text);
        var khasra = Cell(columns.Khasra);
        var total = Cell(columns.TotalArea);
        var awarded = Cell(columns.AwardedArea);
        return (string.Join(" ", khasra), string.Join(" ", total), string.Join(" ", awarded));
    }

    private DateOnly? ExtractDate(string value) => Regex.Matches(value, @"\b\d{1,2}[./-]\d{1,2}[./-]\d{4}\b").Select(x => dateParser.TryParse(x.Value, out var date) ? date : (DateOnly?)null).FirstOrDefault(x => x is not null);
    private static bool ContainsNegativeAwardContext(string text) => Regex.IsMatch(text, @"\b(previous|earlier|superseded|reference)\s+award\b", RegexOptions.IgnoreCase);
    private static string ValueAfterLabel(string text, string label)
    {
        // A fuzzy semantic label may not be byte-for-byte present. A delimiter is required in that case.
        var delimiter = text.IndexOfAny([':', '#']);
        if (delimiter >= 0) return text[(delimiter + 1)..].Trim(' ', ':', '-', '#');
        var index = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? "" : text[(index + label.Length)..].Trim(' ', ':', '-', '#');
    }
    private static string KhasraStem(string number) { if (number.Count(character => character == '/') <= 2) return number; var index = number.LastIndexOf('/'); return index <= 0 ? number : number[..index]; }
}
