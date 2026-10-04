using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using LAC.Domain;

namespace LAC.Infrastructure;

public sealed record CoreSourcePage(int PageNumber, string Text);
public sealed record CoreIntakeEvidence(string Field, int PageNumber, string SourceText);
public sealed record CoreIntakeAlternative(string Kind, string Value, Guid? AwardId = null, string? Reason = null);
public sealed record CoreClassification(string DetectedRole, string? DetectedAwardNumber, DateOnly? DetectedAwardDate,
    string? DetectedVillage, string? DetectedAwardType, decimal Confidence, bool RequiresReview,
    IReadOnlyList<CoreIntakeEvidence> Evidence, IReadOnlyList<CoreIntakeAlternative> Alternatives);
public sealed record CoreRoleRule(string Role, string HeadingPattern);

public static class CoreDocumentRoles
{
    // Extend the vocabulary here; specialized headings take precedence over their Award references.
    public static readonly IReadOnlyList<CoreRoleRule> Rules = [
        new("NM", @"(?:n\s*\.?\s*m\.?\b|na[kq]sha\s+(?:muntazamin|muntazimin|mutazamin|mutabiq)\b)"),
        new("StatementA", @"statement\s*[-:]?\s*[\""']?a\b"),
        new("PossessionProceeding", @"possession\s+(?:proceedings?|memo)\b")
    ];
    public static readonly string[] All = ["Award", .. Rules.Select(x => x.Role)];
}

public interface ICoreDocumentTextReader
{
    Task<IReadOnlyList<CoreSourcePage>> ReadAsync(Stream pdf, CancellationToken ct);
}

/// <summary>Uses the same local PdfPig library as Award extraction; no OCR or remote inference.</summary>
public sealed class CoreDocumentNativeTextReader : ICoreDocumentTextReader
{
    public const int MaxPages = 6;
    public const int MaxCharactersPerPage = 16000;
    public Task<IReadOnlyList<CoreSourcePage>> ReadAsync(Stream source, CancellationToken ct)
    {
        using var pdf = PdfDocument.Open(source);
        var pages = new List<CoreSourcePage>();
        for (var i = 1; i <= Math.Min(pdf.NumberOfPages, MaxPages); i++)
        {
            ct.ThrowIfCancellationRequested();
            var page = pdf.GetPage(i);
            var tokens = AwardPdfJobRunner.BuildEmbeddedWordTokens(page.Letters);
            var normalized = new NormalizedDocumentPage(i, (decimal)page.Width, (decimal)page.Height,
                AwardDocumentExtractionMethod.EmbeddedText, page.Text, tokens);
            var text = string.Join('\n', DocumentLayout.Lines(normalized).Select(x => x.Text));
            pages.Add(new(i, text[..Math.Min(text.Length, MaxCharactersPerPage)]));
        }
        return Task.FromResult<IReadOnlyList<CoreSourcePage>>(pages);
    }
}

public interface ICoreDocumentClassifier
{
    CoreClassification Classify(IReadOnlyList<CoreSourcePage> pages);
}

public sealed class CoreDocumentClassifier : ICoreDocumentClassifier
{
    private const string Number = @"[0-9]{1,6}(?:\s*/\s*(?:[0-9]{4}|[0-9]{2})(?:\s*[-–—]\s*(?:[0-9]{4}|[0-9]{2}))?)?(?![\w/–—-])";
    private static readonly Regex References = Pattern(@"\baward\s+(?:no\.?|number|nos\.?)\s*[:#-]?\s*(?<value>" + Number + @")(?<more>(?:\s*(?:,|&|and)\s*" + Number + @")*)");
    private static readonly Regex Dates = Pattern(@"(?:\baward\s+date|\bdate\s+of\s+award|\baward\s+(?:no\.?|number)\s*[:#-]?\s*" + Number + @"\s*[,]?\s*dated)\s*[:=-]?\s*(?<value>\d{4}-\d{2}-\d{2}|\d{1,2}\s+[a-z]{3,9}\s+\d{4}|\d{1,2}[./-]\d{1,2}[./-]\d{4})\b");
    private static readonly Regex Villages = Pattern(@"\b(?:village(?:\s+name)?|mauza|revenue\s+estate)(?:\s*[:=-]\s*|[ \t]+)(?<value>[\p{L}][\p{L} .'-]{0,100})");
    private static Regex Pattern(string value) => new(value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public CoreClassification Classify(IReadOnlyList<CoreSourcePage> pages)
    {
        var evidence = new List<CoreIntakeEvidence>();
        var alternatives = new List<CoreIntakeAlternative>();
        var numbers = new List<string>(); var dates = new List<DateOnly>(); var villages = new List<string>();
        var roles = new HashSet<string>();
        // Role headings must be in the opening text. Later references cannot turn an Award into its annexure.
        var opening = pages.FirstOrDefault();
        if (opening is not null)
        {
            var heading = opening.Text[..Math.Min(opening.Text.Length, 1600)];
            var firstSpecializedHeading = int.MaxValue;
            foreach (var rule in CoreDocumentRoles.Rules)
            {
                var match = Pattern(@"(?:^|[\r\n])\s*" + rule.HeadingPattern).Match(heading);
                if (!match.Success) continue;
                firstSpecializedHeading = Math.Min(firstSpecializedHeading, match.Index);
                roles.Add(rule.Role); evidence.Add(new("documentRole", opening.PageNumber, match.Value.Trim()));
            }
            var awardHeading = Pattern(@"(?:^|[\r\n])\s*(?:(?:main|supplementary)\s+)?award\b(?:\s+(?:no\.?|number)\b|\s*[:\r\n]|$)").Match(heading);
            if (awardHeading.Success && awardHeading.Index < firstSpecializedHeading)
            {
                roles.Clear(); roles.Add("Award");
                evidence.Add(new("documentRole", opening.PageNumber, awardHeading.Value.Trim()));
            }
        }
        foreach (var page in pages)
        {
            foreach (Match match in References.Matches(page.Text))
            {
                numbers.Add(CleanNumber(match.Groups["value"].Value));
                foreach (Match extra in Pattern(Number).Matches(match.Groups["more"].Value)) numbers.Add(CleanNumber(extra.Value));
                evidence.Add(new("awardNumber", page.PageNumber, match.Value));
            }
            foreach (Match match in Dates.Matches(page.Text))
            {
                evidence.Add(new("awardDate", page.PageNumber, match.Value));
                if (new StrictDateParser().TryParse(match.Groups["value"].Value, out var date)
                    || DateOnly.TryParseExact(match.Groups["value"].Value, ["d MMM yyyy", "d MMMM yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) dates.Add(date);
                else alternatives.Add(new("awardDate", match.Groups["value"].Value, Reason: "Invalid explicit date."));
            }
            foreach (Match match in Villages.Matches(page.Text))
            {
                var value = match.Groups["value"].Value.Trim().TrimEnd('.');
                villages.Add(value); evidence.Add(new("village", page.PageNumber, match.Value));
            }
        }
        var uniqueNumbers = new List<string>();
        foreach (var number in numbers) if (!uniqueNumbers.Any(x => AwardNumberIdentity.Equivalent(x, number))) uniqueNumbers.Add(number);
        var uniqueDates = dates.Distinct().ToList(); var uniqueVillages = villages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        alternatives.AddRange(uniqueNumbers.Select(x => new CoreIntakeAlternative("awardNumber", x)));
        alternatives.AddRange(uniqueDates.Select(x => new CoreIntakeAlternative("awardDate", x.ToString("yyyy-MM-dd"))));
        alternatives.AddRange(uniqueVillages.Select(x => new CoreIntakeAlternative("village", x)));
        alternatives.AddRange(roles.Select(x => new CoreIntakeAlternative("documentRole", x)));
        var role = roles.Count == 1 ? roles.Single() : "Unknown";
        var awardNumber = uniqueNumbers.Count == 1 ? uniqueNumbers[0] : null;
        var review = role == "Unknown" || uniqueNumbers.Count != 1 || uniqueDates.Count > 1 || uniqueVillages.Count > 1 || alternatives.Any(x => x.Reason is not null);
        var confidence = review ? .3m : awardNumber!.Contains('/') ? .95m : .65m;
        var awardType = role == "Award"
            ? Pattern(@"(?:^|[\r\n])\s*supplementary\s+award\b").IsMatch(opening!.Text) ? "Supplementary"
                : Pattern(@"(?:^|[\r\n])\s*main\s+award\b").IsMatch(opening!.Text) ? "Main" : null
            : null;
        return new(role, awardNumber, uniqueDates.Count == 1 ? uniqueDates[0] : null, uniqueVillages.Count == 1 ? uniqueVillages[0] : null,
            awardType, confidence, review, evidence, alternatives);
    }

    private static string CleanNumber(string number) => Regex.Replace(number, @"\s+", "").Replace('–', '-').Replace('—', '-');
}
