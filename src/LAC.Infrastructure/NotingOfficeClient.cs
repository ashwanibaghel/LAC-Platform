namespace LAC.Infrastructure;

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

public sealed class NotingOfficeClient(IHttpClientFactory clients, OnlyOfficeTokens tokens, IOptions<OnlyOfficeOptions> options,
    IOptions<OnlyOfficeNotingOptions> notingOptions)
{
    public OnlyOfficeOptions Options => options.Value;
    public OnlyOfficeNotingOptions NotingOptions => notingOptions.Value;
    public void Enabled()
    {
        if (!Options.Enabled || !NotingOptions.Enabled) throw new MatterWorkflowException("Official ONLYOFFICE authoring is disabled.", 503);
    }
    public Uri ValidateOrigin(string? url)
    {
        var origin = new Uri(Options.SaveOrigin);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != origin.Scheme || uri.IdnHost != origin.IdnHost || uri.Port != origin.Port ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) throw new MatterWorkflowException("Unexpected Document Server file origin.", 422);
        return uri;
    }
    public async Task<JsonElement> CommandAsync(Dictionary<string, object?> command, CancellationToken ct)
    {
        Enabled();
        var body = new Dictionary<string, object?>(command) { ["token"] = tokens.Sign(command) };
        using var response = await clients.CreateClient("OnlyOffice").PostAsJsonAsync(Options.SaveOrigin.TrimEnd('/') + "/coauthoring/CommandService.ashx", body, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).Clone();
    }
    public async Task<byte[]> DownloadAsync(string url, int limit, CancellationToken ct)
    {
        using var response = await clients.CreateClient("OnlyOffice").GetAsync(ValidateOrigin(url), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new MatterWorkflowException("Document Server output exceeds its limit.", 422);
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var result = new MemoryStream();
        var buffer = new byte[81920]; int n;
        while ((n = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (result.Length + n > limit) throw new MatterWorkflowException("Document Server output exceeds its limit.", 422);
            await result.WriteAsync(buffer.AsMemory(0, n), ct);
        }
        return result.ToArray();
    }
    public async Task<string> VerifyRendererAsync(CancellationToken ct)
    {
        Enabled();
        var version = await CommandAsync(new() { ["c"] = "version" }, ct);
        if (version.GetProperty("error").GetInt32() != 0 || version.GetProperty("version").GetString() != NotingOptions.RendererVersion)
            throw new MatterWorkflowException("Document Server version differs from the frozen GreenLegalV2 renderer.", 503);
        var manifest = await DownloadAsync(Options.SaveOrigin.TrimEnd('/') + "/sdkjs/common/AllFonts.js", 5 * 1024 * 1024, ct);
        var hash = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
        if (manifest.Length > 5 * 1024 * 1024 || hash != NotingOptions.FontsManifestSha256.ToLowerInvariant())
            throw new MatterWorkflowException("Document Server font manifest differs from the frozen print policy.", 503);
        return hash;
    }
    public async Task<byte[]> ConvertPdfAsync(string documentUrl, string key, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?> { ["async"] = false, ["filetype"] = "docx", ["outputtype"] = "pdf", ["key"] = key, ["url"] = documentUrl };
        var body = new Dictionary<string, object?>(payload) { ["token"] = tokens.Sign(payload) };
        using var response = await clients.CreateClient("OnlyOffice").PostAsJsonAsync(Options.SaveOrigin.TrimEnd('/') + "/converter?shardkey=" + Uri.EscapeDataString(key), body, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!result.TryGetProperty("endConvert", out var done) || !done.GetBoolean()) throw new MatterWorkflowException("Green Sheet PDF conversion did not finish; no printable output was published.", 503);
        return await DownloadAsync(result.GetProperty("fileUrl").GetString()!, 20 * 1024 * 1024, ct);
    }
}
