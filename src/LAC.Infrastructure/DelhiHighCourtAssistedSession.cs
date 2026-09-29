using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Configuration;

namespace LAC.Infrastructure;

public sealed record DhcOfficialChallengeImage(byte[] Bytes, string ContentType);

// A fresh instance is created only for a human-started run. The cookie jar and
// form tokens never leave this object and are destroyed on completion/cancel.
public sealed class DelhiHighCourtAssistedSession : IAsyncDisposable
{
    private readonly HttpClient client;
    private readonly int delayMs;
    private DateTimeOffset lastRequest;
    private string? pendingHumanOrderAnswer;
    private string? lastResponseContentType;
    public string? LastOrderResponseContentType { get; private set; }
    public IReadOnlyList<string> LastOrderPostFieldNames { get; private set; } = [];
    public CookieContainer Cookies { get; } = new();
    public DelhiHighCourtAssistedForms.FormState? StatusForm { get; private set; }
    public DelhiHighCourtAssistedForms.FormState? OrderForm { get; private set; }
    public bool Verified { get; private set; }
    public bool IsDisposed { get; private set; }

    public DelhiHighCourtAssistedSession(IConfiguration configuration,
        Func<CookieContainer, HttpMessageHandler>? handlerFactory = null)
    {
        delayMs = Math.Max(1000, configuration.GetValue<int?>("CourtSync:DelhiHighCourt:AssistedDelayMs") ?? 1000);
        var handler = handlerFactory?.Invoke(Cookies) ?? new HttpClientHandler
        {
            CookieContainer = Cookies, UseCookies = true, AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LAC-Platform-DHC-AssistedSync/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
    }

    private async Task<byte[]> SendAsync(HttpRequestMessage request, int maxBytes, CancellationToken ct,
        bool officialImage = false)
    {
        if (request.RequestUri is not { } uri || !(officialImage
                ? DelhiHighCourtAssistedForms.IsApprovedOfficialUri(uri)
                : DelhiHighCourtAssistedForms.IsApprovedFormUri(uri)))
            throw new InvalidDataException("DHC request URL is not an approved official HTTPS URL.");
        var remaining = TimeSpan.FromMilliseconds(delayMs) - (DateTimeOffset.UtcNow - lastRequest);
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct);
        lastRequest = DateTimeOffset.UtcNow;
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new InvalidDataException("DHC redirected the assisted request; no case data was updated.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"DHC returned HTTP {(int)response.StatusCode}.");
        lastResponseContentType = response.Content.Headers.ContentType?.MediaType;
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException("DHC response exceeds the assisted safety limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + count > maxBytes)
                throw new InvalidDataException("DHC response exceeds the assisted safety limit.");
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    private async Task<string> TextAsync(HttpRequestMessage request, CancellationToken ct) =>
        Encoding.UTF8.GetString(await SendAsync(request, 4 * 1024 * 1024, ct));

    public async Task<DelhiHighCourtAssistedForms.FormState> LoadFormAsync(bool orders, CancellationToken ct)
    {
        var url = orders ? DelhiHighCourtAssistedForms.OrderUrl : DelhiHighCourtAssistedForms.StatusUrl;
        string html;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            html = await TextAsync(request, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && (ex is HttpRequestException or IOException or TaskCanceledException))
        {
            // Only the initial read-only official form GET is retried. Never replay
            // a human answer or a case-search POST after a transport failure.
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            using var retry = new HttpRequestMessage(HttpMethod.Get, url);
            html = await TextAsync(retry, ct);
        }
        var parsed = DelhiHighCourtAssistedForms.ParseForm(html, orders);
        if (orders) OrderForm = parsed; else StatusForm = parsed;
        Verified = false;
        pendingHumanOrderAnswer = null;
        if (orders) { LastOrderResponseContentType = null; LastOrderPostFieldNames = []; }
        return parsed;
    }

    public async Task<DhcOfficialChallengeImage> GetChallengeImageAsync(bool orders, CancellationToken ct)
    {
        var uri = (orders ? OrderForm : StatusForm)?.ImageChallenge ??
            throw new InvalidOperationException("No active DHC image challenge.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var bytes = await SendAsync(request, 512 * 1024, ct, officialImage: true);
        var contentType = lastResponseContentType;
        if (contentType is not ("image/png" or "image/jpeg" or "image/gif" or "image/webp"))
            throw new InvalidDataException("Official challenge image has an unsupported content type.");
        return new DhcOfficialChallengeImage(bytes, contentType);
    }

    public async Task<bool> ValidateHumanAnswerAsync(string answer, bool orders, CancellationToken ct)
    {
        var form = orders ? OrderForm : StatusForm;
        if (form == null || string.IsNullOrWhiteSpace(form.Csrf))
            throw new InvalidOperationException("No active official DHC challenge.");
        if (string.IsNullOrWhiteSpace(answer) || answer.Length > 64)
            throw new CourtWorkflowException("Enter the official DHC verification code.", 400);
        using var request = new HttpRequestMessage(HttpMethod.Post, DelhiHighCourtAssistedForms.ValidateUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_token"] = form.Csrf, ["captchaInput"] = answer.Trim()
            })
        };
        using var result = JsonDocument.Parse(await TextAsync(request, ct));
        if (!result.RootElement.TryGetProperty("success", out var success) ||
            success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("DHC verification response layout changed.");
        Verified = success.GetBoolean();
        // The normal order-search POST still requires the code that the officer
        // supplied. Keep it only in this in-memory session until that one POST.
        pendingHumanOrderAnswer = Verified && orders ? answer.Trim() : null;
        return Verified;
    }

    public async Task<string> SearchStatusAsync(string officialType, string number, string year, CancellationToken ct)
    {
        if (!Verified || StatusForm == null) throw new InvalidOperationException("Human verification is required.");
        var query = new Dictionary<string, string>
        {
            ["draw"] = "1", ["start"] = "0", ["length"] = "50",
            ["case_type"] = officialType, ["case_number"] = number, ["case_year"] = year
        };
        var uri = new Uri(DelhiHighCourtAssistedForms.StatusUrl + "?" +
            string.Join('&', query.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        // The official DataTables endpoint returns the CAPTCHA HTML page for a
        // regular navigation GET. Its own AJAX call sends this header and gets
        // the JSON case result after the officer's manual verification.
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        return await TextAsync(request, ct);
    }

    public async Task<string> SearchOrdersAsync(string officialType, string number, string year, CancellationToken ct)
    {
        if (!Verified || OrderForm == null) throw new InvalidOperationException("Human verification is required.");
        var manualAnswer = pendingHumanOrderAnswer ??
            throw new InvalidOperationException("A fresh human-entered order verification code is required.");
        pendingHumanOrderAnswer = null;
        Verified = false;
        var fields = new Dictionary<string, string>(OrderForm.HiddenFields, StringComparer.Ordinal)
        {
            ["case_type"] = officialType, ["case_number"] = number, ["year"] = year,
            ["captchaInput"] = manualAnswer
        };
        LastOrderPostFieldNames = fields.Keys.Order(StringComparer.Ordinal).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Post, DelhiHighCourtAssistedForms.OrderUrl)
        { Content = new FormUrlEncodedContent(fields) };
        // Never replay this POST automatically after a transport/protocol failure.
        var response = await TextAsync(request, ct);
        LastOrderResponseContentType = lastResponseContentType;
        return response;
    }

    public ValueTask DisposeAsync()
    {
        client.Dispose();
        StatusForm = null;
        OrderForm = null;
        pendingHumanOrderAnswer = null;
        Verified = false;
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}
