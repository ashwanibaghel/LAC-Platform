using System.Security.Cryptography;

namespace LAC.Tests;

// Test factories share an ephemeral password for their login requests. Nothing
// here is a committed credential or a fallback for an application account.
internal static class TestCredentials
{
    public static string SharedPassword { get; } = NewPassword();
    public static string NewPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "aA9!";
}
