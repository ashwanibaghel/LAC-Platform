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

    public sealed record OrderResponseAssessment(OrderResponseKind Kind, bool HasResultTable,
        bool HasValidResultColumns, int ResultDataRowCount, bool HasCaptchaForm,
        bool HasCaptchaError, bool HasFormError, IReadOnlyList<string> ReturnedControlNames,
        bool EchoesCaseType, bool EchoesCaseNumber, bool EchoesYear)
    {
        public bool HasResultRows => ResultDataRowCount > 0;
    }

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

    public static bool IsOfficialStatusOrderListUri(Uri uri) => IsApprovedOfficialUri(uri) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
        uri.AbsoluteUri.Length <= 8192 && Regex.IsMatch(uri.AbsolutePath,
            @"^/app/case-type-status-details/[A-Za-z0-9%+=_-]+/[A-Za-z0-9%+=_-]+/[A-Za-z0-9%+=_-]+$", RegexOptions.CultureInvariant);

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

    public static OrderResponseAssessment AssessOrderResponse(string body,
        string? requestedCaseType = null, string? requestedCaseNumber = null, string? requestedYear = null)
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
        var headers = table?.QuerySelectorAll("thead th").Select(x => x.TextContent.Trim()).ToList();
        var validColumns = headers is { Count: 7 } &&
            headers[1].Contains("Case No", StringComparison.OrdinalIgnoreCase) &&
            headers[2].Contains("Judgment/Order", StringComparison.OrdinalIgnoreCase) &&
            headers[4].Contains("Corrigendum", StringComparison.OrdinalIgnoreCase);
        var dataRows = table?.QuerySelectorAll("tbody tr")
            .Count(x => x.QuerySelector("td:not([colspan])") != null) ?? 0;
        var hasCaptchaForm = doc.QuerySelector("form#search1 #captchaInput") != null;
        var form = doc.QuerySelector("form#search1");
        IEnumerable<IElement> controls = form?.QuerySelectorAll("input[name], select[name], textarea[name]")
            ?? Enumerable.Empty<IElement>();
        var controlNames = controls.Select(x => x.GetAttribute("name") ?? "")
            .Where(x => Regex.IsMatch(x, @"^[A-Za-z_][A-Za-z0-9_.-]{0,63}$"))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(40).ToArray();
        bool Echoes(string name, string? expected)
        {
            if (expected == null) return false;
            var values = controls.Where(x => x.GetAttribute("name") == name)
                .Select(x => x.LocalName == "select"
                    ? x.QuerySelector("option[selected]")?.GetAttribute("value")
                    : x.GetAttribute("value"))
                .Take(2).ToArray();
            return values.Length == 1 && values[0] == expected;
        }
        var kind = captchaError ? OrderResponseKind.CaptchaRequired :
            formError ? OrderResponseKind.FormValidationError :
            validColumns && dataRows == 0 && Echoes("case_type", requestedCaseType) &&
                Echoes("case_number", requestedCaseNumber) && Echoes("year", requestedYear) &&
                Regex.IsMatch(visible, @"no (?:records?|results?|orders?) (?:found|available)", RegexOptions.IgnoreCase)
                ? OrderResponseKind.Result :
            hasCaptchaForm && dataRows == 0 ? OrderResponseKind.UnconfirmedForm :
            dataRows > 0 && validColumns ? OrderResponseKind.Result : OrderResponseKind.Unrecognized;
        return new OrderResponseAssessment(kind, table != null, validColumns, dataRows,
            hasCaptchaForm, captchaError, formError, controlNames,
            Echoes("case_type", requestedCaseType), Echoes("case_number", requestedCaseNumber),
            Echoes("year", requestedYear));
    }

    public sealed record StatusRow(string RawCaseNumber, string? RawDiaryNumber,
        string? RawStatus, string? RawParties, string? RawListingDate,
        DateOnly? ListingDate, string? RawCourtNumber, string RawEvidenceText, string EvidenceSha256,
        string? OfficialOrderListUrl = null);

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
            var numberHtml = number.GetString() ?? "";
            var rawNumber = Plain(numberHtml);
            var orderLinks = new HtmlParser().ParseDocument(numberHtml).QuerySelectorAll("a[href]")
                .Where(x => Regex.IsMatch(x.TextContent, @"click\s+here\s+for\s+orders", RegexOptions.IgnoreCase))
                .Select(x => new Uri(new Uri(StatusUrl), x.GetAttribute("href")!)).Distinct().ToList();
            if (orderLinks.Count > 1 || orderLinks.Any(x => !IsOfficialStatusOrderListUri(x)))
                throw new InvalidDataException("DHC status order-list link changed; no history was confirmed.");
            var orderLink = orderLinks.SingleOrDefault()?.AbsoluteUri;
            var rawParties = Plain(parties.GetString() ?? "");
            var rawListing = Plain(listing.GetString() ?? "");
            var status = Regex.Match(rawNumber, @"\[([^\]]+)\]").Groups[1].Value;
            var diary = Regex.Match(rawNumber, @"Diary\s*(?:No\.?\s*)?[:#]?\s*([A-Za-z0-9/-]+)", RegexOptions.IgnoreCase)
                .Groups[1].Value;
            var court = Regex.Match(rawListing, @"Court\s*(?:No\.?)?\s*[:#]?\s*([A-Za-z0-9-]+)", RegexOptions.IgnoreCase)
                .Groups[1].Value;
            var fragment = $"{rawNumber} | {rawParties} | {rawListing}" +
                (orderLink == null ? "" : $" | Official order list: {orderLink}");
            result.Add(new StatusRow(rawNumber, diary.Length == 0 ? null : diary,
                status.Length == 0 ? null : status, rawParties, rawListing,
                ParseDate(rawListing), court.Length == 0 ? null : court, fragment, Hash(fragment), orderLink));
        }
        return result;
    }

    public sealed record OrderRow(string RawCaseNumber, string? RawOrderDate, DateOnly? OrderDate,
        string? OfficialUrl, string? CorrigendumUrl, string? RawUploadDate, DateOnly? UploadDate,
        string? RawRemark, string RawEvidenceText, string EvidenceSha256, string? HindiOrderUrl = null);

    public sealed record StatusOrderPage(int Total, int Draw, IReadOnlyList<OrderRow> Rows);

    public static StatusOrderPage ParseStatusOrderPage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("recordsTotal", out var total) || !total.TryGetInt32(out var count) ||
            !root.TryGetProperty("recordsFiltered", out var filtered) || !filtered.TryGetInt32(out var matched) ||
            count != matched || count is < 0 or > 1000 || !root.TryGetProperty("draw", out var draw) ||
            !draw.TryGetInt32(out var drawNumber) || !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Official order pagination changed; complete history is not confirmed.");
        var rows = new List<OrderRow>();
        foreach (var row in data.EnumerateArray())
        {
            if (!row.TryGetProperty("case_no_order_link", out var link) || link.ValueKind != JsonValueKind.String ||
                !row.TryGetProperty("order_date", out var date) || !date.TryGetProperty("display", out var display) ||
                display.ValueKind != JsonValueKind.String || !row.TryGetProperty("corrigendum", out var corr))
                throw new InvalidDataException("Official case-order columns changed; no order link was applied.");
            var html = new HtmlParser().ParseDocument(link.GetString()!);
            // Judgment rows append a publication label outside the anchor.
            // Compare the actual linked identity, never that presentation text.
            var anchor = html.QuerySelector("a[href]");
            var rawCase = anchor == null ? Plain(link.GetString()!) : Plain(anchor.TextContent);
            if (row.TryGetProperty("caseno", out var caseNo) && caseNo.GetString()?.Trim() != rawCase)
                throw new InvalidDataException("Official case-order identities disagree.");
            var rawDate = display.GetString();
            var orderDate = ParseDate(rawDate);
            if (row.TryGetProperty("orddate", out var alternate) && ParseDate(alternate.GetString()) != orderDate)
                throw new InvalidDataException("Official case-order dates disagree.");
            var pdf = ApprovedLink(anchor?.GetAttribute("href"), StatusUrl);
            var corrHtml = corr.ValueKind == JsonValueKind.String ? corr.GetString() ?? "" : "";
            var corrDoc = new HtmlParser().ParseDocument(corrHtml);
            var corrUrl = ApprovedLink(corrDoc.QuerySelector("a[href]")?.GetAttribute("href"), StatusUrl);
            var remark = row.TryGetProperty("remarks", out var remarks) && remarks.ValueKind == JsonValueKind.String
                ? Plain(remarks.GetString()!) : null;
            var hindi = row.TryGetProperty("hindi_order", out var hindiValue) && hindiValue.ValueKind == JsonValueKind.String
                ? hindiValue.GetString() ?? "" : "";
            var hindiUrl = ApprovedLink(new HtmlParser().ParseDocument(hindi).QuerySelector("a[href]")?.GetAttribute("href"), StatusUrl);
            var evidence = $"{rawCase} | {rawDate} | {pdf} | {Plain(corrHtml)} | {corrUrl} | {hindiUrl} | {remark}";
            rows.Add(new(rawCase, rawDate, orderDate, pdf, corrUrl, null, null, remark, evidence, Hash(evidence), hindiUrl));
        }
        if (rows.Count > count) throw new InvalidDataException("Official order count does not match returned rows.");
        return new(count, drawNumber, rows);
    }

    public static IReadOnlyList<OrderRow> ParseOrderRows(string html)
    {
        if (html.TrimStart().StartsWith('{')) return ParseStatusOrderPage(html).Rows;
        var doc = new HtmlParser().ParseDocument(html);
        var tables = doc.QuerySelectorAll("table[id='s_judgeTable']");
        if (doc.QuerySelector(".pagination a[rel=next], a[rel=next]") != null)
            throw new InvalidDataException("Official result has unsupported server pagination; complete history is not confirmed.");
        var table = tables.FirstOrDefault() ??
            throw new InvalidDataException("DHC order result table is unavailable; no order link was applied.");
        var results = new List<OrderRow>();
        foreach (var resultTable in tables)
        {
        var headers = resultTable.QuerySelectorAll("thead th").Select(x => x.TextContent.Trim()).ToList();
        if (headers.Count != 7 || !headers[1].Contains("Case No", StringComparison.OrdinalIgnoreCase) ||
            !headers[2].Contains("Judgment/Order", StringComparison.OrdinalIgnoreCase) ||
            !headers[4].Contains("Corrigendum", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DHC order result columns changed; no order link was applied.");
        foreach (var tr in resultTable.QuerySelectorAll("tbody tr"))
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
                rawUpload, ParseDate(rawUpload), cells[6].TextContent.Trim(), fragment, Hash(fragment + " | " + link + " | " + corrigendum)));
        }
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
