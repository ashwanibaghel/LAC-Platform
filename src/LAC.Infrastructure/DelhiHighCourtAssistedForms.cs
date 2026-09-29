using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace LAC.Infrastructure;

// Audited against the official public HTML on 29 September 2026. The status
// page uses DataTables GET to its own URL after POST /validateCaptcha. The
// order page POSTs its #search1 form to its own URL after the same validator.
// Both currently render #captcha-code as visible text; the older image element
// is commented out. We never use that text as an answer. The two option-value
// maps are intentionally independent: W.P.(C) is W.P.(C) for status and CW for
// orders. Missing/changed official options fail closed.
public static class DelhiHighCourtAssistedForms
{
    public const string StatusUrl = "https://delhihighcourt.nic.in/app/get-case-type-status";
    public const string OrderUrl = "https://delhihighcourt.nic.in/app/case-number";
    public const string ValidateUrl = "https://delhihighcourt.nic.in/app/validateCaptcha";
    public const string MappingVersion = "dhc-options-2026-09-29-v1";

    private static readonly IReadOnlyDictionary<string, string> StatusTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["wpc"] = "W.P.(C)", ["wpcrl"] = "W.P.(CRL)", ["lpa"] = "LPA",
            ["cmm"] = "CM(M)", ["rfa"] = "RFA", ["arba"] = "ARB.A.",
            ["arbp"] = "ARB.P.", ["fao"] = "FAO", ["crlmc"] = "CRL.M.C.",
            ["contcasc"] = "CONT.CAS(C)", ["laapp"] = "LA.APP.", ["csos"] = "CS(OS)"
        };
    private static readonly IReadOnlyDictionary<string, string> OrderTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["wpc"] = "CW", ["lpa"] = "LPA", ["cmm"] = "CMM",
            ["rfa"] = "RFA", ["arba"] = "AAP", ["contcasc"] = "CCP",
            ["laapp"] = "LAA", ["csos"] = "S"
        };

    public sealed record FormState(string SourceUrl, string? Csrf, string? VisibleChallenge,
        Uri? ImageChallenge, IReadOnlyDictionary<string, string> CaseTypeOptions,
        IReadOnlyDictionary<string, string> HiddenFields);

    public enum OrderResponseKind { Result, CaptchaRequired, FormValidationError, UnconfirmedForm, Unrecognized }

    public sealed record OrderResponseAssessment(OrderResponseKind Kind, bool HasResultRows,
        bool HasCaptchaForm, bool HasCaptchaError, bool HasFormError);

    public static FormState ParseForm(string html, bool orders)
    {
        var doc = new HtmlParser().ParseDocument(html);
        var options = doc.QuerySelectorAll("select#case_type option")
            .Where(x => !string.IsNullOrWhiteSpace(x.GetAttribute("value")))
            .ToDictionary(x => x.GetAttribute("value")!, x => x.TextContent.Trim(), StringComparer.Ordinal);
        if (options.Count == 0 || doc.QuerySelector("#captchaInput") == null)
            throw new InvalidDataException("DHC form layout changed; no case data was updated.");
        var orderForm = orders ? doc.QuerySelector("form#search1[method=post]") : null;
        if (orders && (orderForm == null ||
            !Uri.TryCreate(new Uri(OrderUrl), orderForm.GetAttribute("action"), out var orderAction) ||
            orderAction.AbsoluteUri != OrderUrl))
            throw new InvalidDataException("DHC order form action changed; no case data was updated.");
        var hidden = (orders ? doc.QuerySelector("form#search1")! : doc.DocumentElement!)
            .QuerySelectorAll("input[type=hidden][name]")
            .ToDictionary(x => x.GetAttribute("name")!, x => x.GetAttribute("value") ?? "", StringComparer.Ordinal);
        var csrf = hidden.GetValueOrDefault("_token");
        if (!orders && string.IsNullOrWhiteSpace(csrf))
            csrf = Regex.Match(html, "\\\"_token\\\"\\s*:\\s*\\\"(?<token>[^\\\"]+)\\\"")
                .Groups["token"].Value;
        // Read the challenge only from what the human sees. The order form's
        // hidden fields are retained only as opaque normal-form submission
        // state, never compared with or copied into captchaInput.
        var visible = doc.QuerySelector("#captcha-code")?.TextContent.Trim();
        var imageSrc = doc.QuerySelector("img#captcha-image")?.GetAttribute("src");
        Uri? image = imageSrc == null ? null : new Uri(new Uri(orders ? OrderUrl : StatusUrl), imageSrc);
        if (string.IsNullOrWhiteSpace(visible) && image == null)
            throw new InvalidDataException("DHC verification challenge is unavailable.");
        if (string.IsNullOrWhiteSpace(csrf))
            throw new InvalidDataException("DHC form token is unavailable.");
        return new FormState(orders ? OrderUrl : StatusUrl, csrf, visible, image, options, hidden);
    }

    public static string? ExactCaseTypeValue(string normalizedIdentity, FormState form, bool orders)
    {
        var pieces = normalizedIdentity.Split('|');
        if (pieces.Length != 4) return null;
        var map = orders ? OrderTypes : StatusTypes;
        if (!map.TryGetValue(pieces[1], out var official)) return null;
        return form.CaseTypeOptions.ContainsKey(official) ? official : null;
    }

    public static (string Number, string Year)? NumberAndYear(string normalizedIdentity)
    {
        var pieces = normalizedIdentity.Split('|');
        return pieces.Length == 4 && pieces[2].All(char.IsAsciiDigit) &&
            pieces[3].Length == 4 && pieces[3].All(char.IsAsciiDigit)
            ? (pieces[2], pieces[3]) : null;
    }

    public static bool IsApprovedOfficialUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("delhihighcourt.nic.in", StringComparison.OrdinalIgnoreCase) &&
        uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo);

    public static bool IsApprovedFormUri(Uri uri) => IsApprovedOfficialUri(uri) &&
        uri.AbsolutePath.StartsWith("/app/", StringComparison.Ordinal);

    public static string Plain(string html) => string.Join(' ', new HtmlParser().ParseDocument(html)
        .Body?.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? []);

    public static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = Regex.Match(value, @"\b(?<d>\d{1,2}[./-]\d{1,2}[./-]\d{4})\b");
        return match.Success && DateOnly.TryParseExact(match.Groups["d"].Value,
            ["dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    public static string Hash(string fragment) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fragment)))
        .ToLowerInvariant();

    public static bool CaptchaRequired(string body)
    {
        if (body.Contains("captcha", StringComparison.OrdinalIgnoreCase) &&
            (body.Contains("incorrect", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("required", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("expired", StringComparison.OrdinalIgnoreCase))) return true;
        if (!body.Contains("<", StringComparison.Ordinal)) return false;
        var doc = new HtmlParser().ParseDocument(body);
        return doc.QuerySelector("#captcha-code, #captcha-image") != null;
    }

    public static OrderResponseAssessment AssessOrderResponse(string body)
    {
        var doc = new HtmlParser().ParseDocument(body);
        // The official page embeds fixed CAPTCHA-error strings in JavaScript,
        // even on a successful result. Only visible response text is evidence.
        var visible = Regex.Replace(body, @"(?is)<(?:script|style)\b[^>]*>.*?</(?:script|style)>", " ");
        visible = Regex.Replace(Regex.Replace(visible, "<[^>]+>", " "), @"\s+", " ");
        var captchaError = Regex.IsMatch(visible,
            @"\bcaptcha\b.{0,80}\b(required|incorrect|invalid|expired|failed)\b|\b(required|incorrect|invalid|expired|failed)\b.{0,80}\bcaptcha\b",
            RegexOptions.IgnoreCase);
        var formError = Regex.IsMatch(visible,
            @"\b(case type|case number|year|form field)\b.{0,80}\b(required|invalid|missing)\b",
            RegexOptions.IgnoreCase);
        var table = doc.QuerySelector("#s_judgeTable");
        var hasRows = table?.QuerySelector("tbody tr td:not([colspan])") != null;
        var hasCaptchaForm = doc.QuerySelector("form#search1 #captchaInput") != null;
        var kind = captchaError ? OrderResponseKind.CaptchaRequired :
            formError ? OrderResponseKind.FormValidationError :
            hasCaptchaForm && !hasRows ? OrderResponseKind.UnconfirmedForm :
            hasRows ? OrderResponseKind.Result : OrderResponseKind.Unrecognized;
        return new OrderResponseAssessment(kind, hasRows, hasCaptchaForm, captchaError, formError);
    }

    public sealed record StatusRow(string RawCaseNumber, string? RawDiaryNumber,
        string? RawStatus, string? RawParties, string? RawListingDate,
        DateOnly? ListingDate, string? RawCourtNumber, string RawEvidenceText, string EvidenceSha256);

    public static IReadOnlyList<StatusRow> ParseStatusRows(string json)
    {
        using var root = JsonDocument.Parse(json);
        if (!root.RootElement.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("DHC status result layout changed; no case data was updated.");
        var result = new List<StatusRow>();
        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("ctype", out var number) ||
                !row.TryGetProperty("pet", out var parties) ||
                !row.TryGetProperty("orderdate", out var listing))
                throw new InvalidDataException("DHC status columns changed; no case data was updated.");
            var rawNumber = Plain(number.GetString() ?? "");
            var rawParties = Plain(parties.GetString() ?? "");
            var rawListing = Plain(listing.GetString() ?? "");
            var status = Regex.Match(rawNumber, @"\[([^\]]+)\]").Groups[1].Value;
            var diary = Regex.Match(rawNumber, @"Diary\s*(?:No\.?\s*)?[:#]?\s*([A-Za-z0-9/-]+)", RegexOptions.IgnoreCase)
                .Groups[1].Value;
            var court = Regex.Match(rawListing, @"Court\s*(?:No\.?)?\s*[:#]?\s*([A-Za-z0-9-]+)", RegexOptions.IgnoreCase)
                .Groups[1].Value;
            var fragment = $"{rawNumber} | {rawParties} | {rawListing}";
            result.Add(new StatusRow(rawNumber, diary.Length == 0 ? null : diary,
                status.Length == 0 ? null : status, rawParties, rawListing,
                ParseDate(rawListing), court.Length == 0 ? null : court, fragment, Hash(fragment)));
        }
        return result;
    }

    public sealed record OrderRow(string RawCaseNumber, string? RawOrderDate, DateOnly? OrderDate,
        string? OfficialUrl, string? CorrigendumUrl, string? RawUploadDate, DateOnly? UploadDate,
        string? RawRemark, string RawEvidenceText, string EvidenceSha256);

    public static IReadOnlyList<OrderRow> ParseOrderRows(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);
        var table = doc.QuerySelector("#s_judgeTable") ??
            throw new InvalidDataException("DHC order result table is unavailable; no order link was applied.");
        var headers = table.QuerySelectorAll("thead th").Select(x => x.TextContent.Trim()).ToList();
        if (headers.Count != 7 || !headers[1].Contains("Case No", StringComparison.OrdinalIgnoreCase) ||
            !headers[2].Contains("Judgment/Order", StringComparison.OrdinalIgnoreCase) ||
            !headers[4].Contains("Corrigendum", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DHC order result columns changed; no order link was applied.");
        var results = new List<OrderRow>();
        foreach (var tr in table.QuerySelectorAll("tbody tr"))
        {
            var cells = tr.QuerySelectorAll("td");
            if (cells.Length == 1 && cells[0].GetAttribute("colspan") != null) continue;
            if (cells.Length != 7)
                throw new InvalidDataException("DHC order row layout changed; no order link was applied.");
            var rawCase = cells[1].TextContent.Trim();
            var rawOrder = cells[2].TextContent.Trim();
            var rawUpload = cells[5].TextContent.Trim();
            var link = ApprovedLink(cells[2].QuerySelector("a[href]")?.GetAttribute("href"), OrderUrl);
            var corrigendum = ApprovedLink(cells[4].QuerySelector("a[href]")?.GetAttribute("href"), OrderUrl);
            var fragment = string.Join(" | ", cells.Select(x => x.TextContent.Trim()));
            results.Add(new OrderRow(rawCase, rawOrder, ParseDate(rawOrder), link, corrigendum,
                rawUpload, ParseDate(rawUpload), cells[6].TextContent.Trim(), fragment, Hash(fragment)));
        }
        return results;
    }

    private static string? ApprovedLink(string? href, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        if (!Uri.TryCreate(new Uri(baseUrl), href, out var uri) || !IsApprovedOfficialUri(uri))
            throw new InvalidDataException("DHC order result contains a non-official URL.");
        return uri.AbsoluteUri;
    }
}
