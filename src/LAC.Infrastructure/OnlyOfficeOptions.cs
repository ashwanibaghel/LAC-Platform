namespace LAC.Infrastructure;

public sealed class OnlyOfficeOptions
{
    public bool Enabled { get; set; }
    public string BrowserUrl { get; set; } = "";
    public string AppExternalUrl { get; set; } = "";
    // Browser-facing LAC origin for ONLYOFFICE's Back to Matter navigation.
    // Defaults to AppExternalUrl when the UI and API share an origin.
    public string? AppBrowserUrl { get; set; }
    public string JwtSecret { get; set; } = "";
    // Optional API-to-Docs origin when the browser origin is unreachable from LAC.
    public string? DocumentServerUrl { get; set; }
    public string SaveOrigin => string.IsNullOrWhiteSpace(DocumentServerUrl) ? BrowserUrl : DocumentServerUrl;
    public string BackOrigin => string.IsNullOrWhiteSpace(AppBrowserUrl) ? AppExternalUrl : AppBrowserUrl;

    public bool IsValid() => !Enabled || (IsOrigin(BrowserUrl) && IsOrigin(AppExternalUrl)
        && IsOrigin(SaveOrigin) && IsOrigin(BackOrigin)
        && System.Text.Encoding.UTF8.GetByteCount(JwtSecret) >= 32);

    private static bool IsOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.AbsolutePath == "/"
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
}
