namespace LAC.Infrastructure;

public static class AuditRedaction
{
    // Applied to both old and new values. Never duplicate credential/security state into history.
    public static bool Include(string property) =>
        !property.Contains("Password", StringComparison.OrdinalIgnoreCase)
        && !property.Contains("Credential", StringComparison.OrdinalIgnoreCase)
        && !property.Contains("SecurityStamp", StringComparison.OrdinalIgnoreCase)
        && !property.Contains("SessionVersion", StringComparison.OrdinalIgnoreCase)
        && !property.Contains("Secret", StringComparison.OrdinalIgnoreCase)
        && !property.Contains("Token", StringComparison.OrdinalIgnoreCase);
}
