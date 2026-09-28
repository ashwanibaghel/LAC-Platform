using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using LAC.Domain;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace LAC.Infrastructure;

public sealed record DhcPublication(string Title, DateOnly? ListingDate, Uri PdfUrl,
    CourtExternalSourceKind Kind, string? DateConflict);
public sealed record DhcCaseLine(string Identity, int PageNumber, string Text);
public sealed record DhcArchivePage(IReadOnlyList<DhcPublication> Publications, Uri? NextPage);

// Public cause-list PDFs only. The CAPTCHA-protected Order Information/case-status
// workflow is deliberately not queried, replayed or automated.
public static class DelhiHighCourtCauseListParser
{
    private static readonly Regex Row = new(@"<tr\b[^>]*>(?<body>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Title = new(@"<td\b[^>]*headers\s*=\s*['""]view-title-table-column['""][^>]*>(?<text>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Date = new(@"<td\b[^>]*headers\s*=\s*['""]view-field-date-table-column['""][^>]*>(?<text>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Link = new(@"<a\b[^>]*href\s*=\s*['""](?<url>[^'""]+\.pdf(?:\?[^'""]*)?)['""]", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex TitleDate = new(@"\b(?:FOR|ON)\s+(?<date>\d{1,2}[./-]\d{1,2}[./-]\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // An item number at the start of a PDF line is required. References buried
    // in notes, party text and application numbers are not auto-matched.
    private static readonly Regex NumberedCase = new(@"^\s*\d+\s*(?<case>[A-Za-z][A-Za-z.() ]*?\s*[-/]?\s*\d+\s*/\s*(?:19|20)\d{2})(?=\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NextPage = new(@"<a\b(?=[^>]*\brel\s*=\s*['""]next['""])[^>]*\bhref\s*=\s*['""](?<url>[^'""]+)['""]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsApprovedUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("delhihighcourt.nic.in", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("www.delhihighcourt.nic.in", StringComparison.OrdinalIgnoreCase)) &&
        uri.UserInfo.Length == 0 && uri.Port == 443;

    public static string? NormalizeIdentity(string caseNumber) =>
        CourtImportService.Identity("Delhi High Court", caseNumber);

    public static CourtExternalSourceKind Classify(string title)
    {
        var upper = title.ToUpperInvariant();
        if (upper.Contains("DELETION") || upper.Contains("CORRIGENDUM")) return CourtExternalSourceKind.DeletionOrCorrigendum;
        if (upper.Contains("PRONOUNC") || upper.Contains("JUDG") || upper.Contains("MEDIATION") ||
            upper.Contains("LOK ADALAT") || upper.Contains("ADMINISTRATIVE")) return CourtExternalSourceKind.Unsupported;
        if (upper.StartsWith("ADVANCE CAUSE LIST OF CASES", StringComparison.Ordinal) ||
            upper.StartsWith("CAUSE LIST OF SITTING OF BENCHES", StringComparison.Ordinal) ||
            upper.StartsWith("SUPPLEMENTARY CAUSE LIST", StringComparison.Ordinal)) return CourtExternalSourceKind.OrdinaryListing;
        return CourtExternalSourceKind.Unsupported;
    }

    public static IReadOnlyList<DhcPublication> Discover(string html, Uri pageUrl)
    {
        if (!IsApprovedUri(pageUrl)) throw new InvalidOperationException("Unapproved Delhi High Court page URL.");
        var result = new List<DhcPublication>();
        foreach (Match row in Row.Matches(html))
        {
            var title = Clean(Title.Match(row.Groups["body"].Value).Groups["text"].Value);
            var dateText = Clean(Date.Match(row.Groups["body"].Value).Groups["text"].Value);
            var href = WebUtility.HtmlDecode(Link.Match(row.Groups["body"].Value).Groups["url"].Value);
            if (title.Length == 0 || href.Length == 0 || !Uri.TryCreate(pageUrl, href, out var uri) || !IsApprovedUri(uri)) continue;
            var metadataDate = ParseDate(dateText);
            var titleMatch = TitleDate.Match(title);
            var titleDate = titleMatch.Success ? ParseDate(titleMatch.Groups["date"].Value) : null;
            var conflict = titleMatch.Success && titleDate != metadataDate ? "Title and publication date disagree or are invalid." : null;
            result.Add(new DhcPublication(title, metadataDate, uri, Classify(title), conflict));
        }
        return result;
    }

    public static DhcArchivePage DiscoverArchivePage(string html, Uri pageUrl)
    {
        if (!IsApprovedUri(pageUrl) || (pageUrl.AbsolutePath != "/web/cause-lists/archive-cause-list" &&
            pageUrl.AbsolutePath != "/web/cause-lists/cause-list"))
            throw new InvalidDataException("Unapproved public cause-list archive page.");
        var publications = Discover(html, pageUrl);
        if (publications.Count == 0 || publications.Any(x => x.ListingDate == null))
            throw new InvalidDataException("Public cause-list archive layout changed or contains undated rows.");
        var nextMatch = NextPage.Match(html);
        if (!nextMatch.Success) return new(publications, null);
        var href = WebUtility.HtmlDecode(nextMatch.Groups["url"].Value);
        if (!Uri.TryCreate(pageUrl, href, out var next) || !IsApprovedUri(next) ||
            next.AbsolutePath != pageUrl.AbsolutePath || next == pageUrl)
            throw new InvalidDataException("Unsafe public archive pagination link.");
        return new(publications, next);
    }

    public static IReadOnlyList<DhcCaseLine> ExtractCases(Stream pdfStream)
    {
        using var pdf = PdfDocument.Open(pdfStream);
        var cases = new List<DhcCaseLine>();
        foreach (var page in pdf.GetPages())
        {
            var lines = ExtractLines(page.Letters);
            if (lines.Count == 0) throw new InvalidDataException($"PDF page {page.Number} has no reliable embedded text.");
            foreach (var line in lines)
            {
                var clean = Clean(line);
                var match = NumberedCase.Match(clean);
                if (!match.Success) continue;
                var identity = CourtImportService.Identity("Delhi High Court", match.Groups["case"].Value);
                if (identity != null) cases.Add(new DhcCaseLine(identity, page.Number, clean));
            }
        }
        return cases.GroupBy(x => x.Identity).Select(g => g.First()).ToList();
    }

    private static IReadOnlyList<string> ExtractLines(IEnumerable<Letter> letters)
    {
        var rows = new List<List<Letter>>();
        foreach (var letter in letters.Where(x => !string.IsNullOrEmpty(x.Value))
                     .OrderByDescending(x => x.GlyphRectangle.Bottom).ThenBy(x => x.GlyphRectangle.Left))
        {
            // Glyph baselines vary within one printed line (parentheses, slash,
            // hyphen). A flat two-point tolerance split real DHC case numbers.
            var tolerance = Math.Max(2, letter.GlyphRectangle.Height * .6);
            var row = rows.FirstOrDefault(x => Math.Abs(x.Average(item => item.GlyphRectangle.Bottom) - letter.GlyphRectangle.Bottom) <= tolerance);
            if (row == null) rows.Add([letter]); else row.Add(letter);
        }
        var output = new List<string>();
        foreach (var row in rows)
        {
            var text = new StringBuilder();
            double previousRight = double.NaN;
            foreach (var letter in row.OrderBy(x => x.GlyphRectangle.Left))
            {
                if (!double.IsNaN(previousRight) && letter.GlyphRectangle.Left - previousRight > 2)
                    text.Append(' ');
                text.Append(letter.Value);
                previousRight = letter.GlyphRectangle.Right;
            }
            if (text.Length > 0) output.Add(text.ToString());
        }
        return output;
    }

    private static DateOnly? ParseDate(string input) => DateOnly.TryParseExact(input,
        ["dd.MM.yyyy", "d.M.yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy"],
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    private static string Clean(string raw) => string.Join(' ', WebUtility.HtmlDecode(Tag.Replace(raw, " "))
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
