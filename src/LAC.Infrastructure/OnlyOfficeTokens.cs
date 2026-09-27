using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace LAC.Infrastructure;

// ONLYOFFICE uses HS256, with the editor configuration itself as JWT payload.
public sealed class OnlyOfficeTokens(IOptions<OnlyOfficeOptions> options, TimeProvider clock)
{
    private byte[] Key => Encoding.UTF8.GetBytes(options.Value.JwtSecret);

    public string Sign(object payload)
    {
        var data = WebEncoders.Base64UrlEncode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"u8.ToArray())
            + "." + WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        return data + "." + WebEncoders.Base64UrlEncode(HMACSHA256.HashData(Key, Encoding.ASCII.GetBytes(data)));
    }

    public JsonElement Verify(string token)
    {
        try
        {
            if (token.Length > 65536) throw new FormatException();
            var parts = token.Split('.');
            if (parts.Length != 3) throw new FormatException();
            using var header = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[0]));
            if (header.RootElement.GetProperty("alg").GetString() != "HS256") throw new FormatException();
            var signature = HMACSHA256.HashData(Key, Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]));
            if (!CryptographicOperations.FixedTimeEquals(signature, WebEncoders.Base64UrlDecode(parts[2])))
                throw new FormatException();
            using var body = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[1]));
            var payload = body.RootElement;
            if (payload.TryGetProperty("exp", out var exp) && exp.GetInt64() <= clock.GetUtcNow().ToUnixTimeSeconds())
                throw new FormatException();
            if (payload.TryGetProperty("nbf", out var nbf) && nbf.GetInt64() > clock.GetUtcNow().ToUnixTimeSeconds())
                throw new FormatException();
            return payload.Clone();
        }
        catch (Exception ex) when (ex is FormatException or JsonException or KeyNotFoundException or InvalidOperationException or OverflowException)
        {
            throw new UnauthorizedAccessException("Invalid office token.");
        }
    }

    public string DownloadToken(Guid draftId, Guid documentId) => Sign(new
    {
        purpose = "onlyoffice-download", draftId, documentId,
        exp = clock.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds()
    });

    public bool CanDownload(string token, Guid draftId, Guid documentId)
    {
        try
        {
            var p = Verify(token);
            return p.GetProperty("purpose").GetString() == "onlyoffice-download"
                && p.GetProperty("draftId").GetGuid() == draftId && p.GetProperty("documentId").GetGuid() == documentId
                && p.GetProperty("exp").GetInt64() > clock.GetUtcNow().ToUnixTimeSeconds();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException or FormatException) { return false; }
    }
}
