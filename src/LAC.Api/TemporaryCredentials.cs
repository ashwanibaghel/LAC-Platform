using System.Security.Cryptography;
using LAC.Domain;
using Microsoft.AspNetCore.Identity;

namespace LAC.Api;

public static class TemporaryCredentials
{
    public static string Issue(AppUser user, IPasswordHasher<AppUser> hasher, string? supplied = null)
    {
        var credential = string.IsNullOrWhiteSpace(supplied) ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "aA9!" : supplied;
        user.PasswordHash = hasher.HashPassword(user, credential);
        user.SessionVersion = Guid.NewGuid();
        user.PasswordChangedAt = DateTimeOffset.UtcNow;
        user.MustChangePassword = true;
        user.TemporaryCredentialExpiresAt = DateTimeOffset.UtcNow.AddHours(24);
        return credential;
    }
}

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
