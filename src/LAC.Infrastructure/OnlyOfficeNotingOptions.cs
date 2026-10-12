namespace LAC.Infrastructure;

public sealed class OnlyOfficeNotingOptions
{
    public bool Enabled { get; set; }
    public int LeaseMinutes { get; set; } = 30;
    public int SaveTimeoutSeconds { get; set; } = 45;
    public string RendererVersion { get; set; } = "9.4.0.129";
    public string FontsManifestSha256 { get; set; } = "";
    public string PdfVerifierPython { get; set; } = "";
    public string PdfVerifierScript { get; set; } = "";
    public string PdfVerifierSha256 { get; set; } = "";
}
