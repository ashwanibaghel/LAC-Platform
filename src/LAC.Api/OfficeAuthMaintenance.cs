namespace LAC.Api;

using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

public static class OfficeAuthMaintenance
{
    public static void ReportMissingConnection(IConfiguration configuration, IHostEnvironment environment)
    {
        PrintConfiguration(configuration, environment, officePreflight: true);
        Console.Error.WriteLine("ConnectionStrings:DefaultConnection is blank. No database operation was attempted.");
    }

    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment, string[] args)
    {
        var username = Option(args, "--username") ?? configuration["BootstrapAdmin:Username"];
        if (args[0] == "reset-admin-password" && string.IsNullOrWhiteSpace(Option(args, "--username")))
        {
            Console.Error.WriteLine("Reset requires --username. No account was changed.");
            return 2;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        try
        {
            if (args[0] == "auth-doctor")
            {
                var officePreflight = args.Contains("--office-preflight", StringComparer.Ordinal);
                var preflightOk = PrintConfiguration(configuration, environment, officePreflight);
                var reachable = await db.Database.CanConnectAsync();
                Console.WriteLine($"Database reachable: {reachable}");
                var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>().GetHealth();
                Console.WriteLine($"Document storage writable: {storage.Writable}");
                Console.WriteLine($"App dependency health: {(reachable && storage.Writable ? "Healthy" : "Degraded")}");
                if (!reachable) return 1;
                var userCount = await db.AppUsers.CountAsync();
                Console.WriteLine($"AppUsers count: {userCount}");
                if (userCount == 0 && (string.IsNullOrWhiteSpace(configuration["BootstrapAdmin:Username"]) || string.IsNullOrWhiteSpace(configuration["BootstrapAdmin:Password"])))
                {
                    Console.WriteLine("No users exist and bootstrap credentials are incomplete.");
                    preflightOk = false;
                }
                if (!string.IsNullOrWhiteSpace(username))
                {
                    var user = await FindUserAsync(db, username);
                    Console.WriteLine($"Target normalized username: {username.Trim().ToUpperInvariant()}");
                    Console.WriteLine($"Target user exists: {user is not null}");
                    if (user is not null)
                    {
                        Console.WriteLine($"Target active: {user.IsActive}");
                        Console.WriteLine($"Target record status: {user.RecordStatus}");
                        Console.WriteLine($"Target roles count: {await db.UserRoles.CountAsync(x => x.UserId == user.Id)}");
                        Console.WriteLine($"Password hash exists: {!string.IsNullOrWhiteSpace(user.PasswordHash)}");
                    }
                    if (args.Contains("--verify-password", StringComparer.Ordinal))
                    {
                        var password = ReadSecret("LAC_AUTH_DOCTOR_PASSWORD", "Password to verify: ");
                        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
                        var result = user is not null && user.IsActive && user.RecordStatus == RecordStatus.Active &&
                                     !string.IsNullOrWhiteSpace(user.PasswordHash) &&
                                     hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
                        Console.WriteLine($"Password verification: {(result ? "SUCCESS" : "FAILED")}");
                        if (!result) return 1;
                    }
                }
                else Console.WriteLine("Target user: unspecified (pass --username).");

                var healthUrl = Option(args, "--health-url");
                if (healthUrl is not null)
                {
                    if (!Uri.TryCreate(healthUrl, UriKind.Absolute, out var uri) ||
                        uri.Scheme is not ("http" or "https") || uri.AbsolutePath != "/api/health")
                    {
                        Console.Error.WriteLine("--health-url must be an absolute HTTP(S) /api/health URL.");
                        return 2;
                    }
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    var response = await client.GetAsync(uri);
                    Console.WriteLine($"App /api/health HTTP status: {(int)response.StatusCode}");
                    if (!response.IsSuccessStatusCode) return 1;
                }
                return preflightOk ? 0 : 1;
            }

            if (!await db.Database.CanConnectAsync())
            {
                Console.Error.WriteLine("Database unavailable. No account was changed.");
                return 1;
            }
            var existing = await FindUserAsync(db, username!);
            if (existing is null)
            {
                Console.Error.WriteLine("Target user does not exist. No account was changed.");
                return 1;
            }
            if (!await db.UserRoles.AnyAsync(x => x.UserId == existing.Id && x.Role.Code == "SYSTEM_ADMIN"))
            {
                Console.Error.WriteLine("Target user is not a system administrator. No account was changed.");
                return 1;
            }
            var newPassword = ReadSecret("LAC_MAINTENANCE_NEW_PASSWORD", "New password: ");
            if (string.IsNullOrWhiteSpace(newPassword))
            {
                Console.Error.WriteLine("Blank password rejected. No account was changed.");
                return 2;
            }
            var hasherForReset = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
            await ResetExistingPasswordAsync(db, hasherForReset, existing, newPassword);
            Console.WriteLine($"Password reset for {existing.NormalizedUsername} at {DateTimeOffset.UtcNow:O}. No password was logged.");
            return 0;
        }
        catch (Exception ex)
        {
            // Exception messages can contain connection strings or credentials.
            Console.Error.WriteLine($"Auth maintenance failed ({ex.GetType().Name}). Check host configuration and database availability.");
            return 1;
        }
    }

    public static async Task ResetExistingPasswordAsync(LacDbContext db, IPasswordHasher<AppUser> hasher, AppUser user, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword)) throw new ArgumentException("Password must not be blank.", nameof(newPassword));
        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.PasswordChangedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    private static Task<AppUser?> FindUserAsync(LacDbContext db, string username) =>
        db.AppUsers.SingleOrDefaultAsync(x => x.NormalizedUsername == username.Trim().ToUpperInvariant());

    private static bool PrintConfiguration(IConfiguration configuration, IHostEnvironment environment, bool officePreflight)
    {
        var preflightOk = true;
        Console.WriteLine($"Environment: {environment.EnvironmentName}");
        Console.WriteLine($"Content root: {environment.ContentRootPath}");
        Console.WriteLine($"ASPNETCORE_ENVIRONMENT: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "(unset)"}");
        foreach (var key in new[] { "ConnectionStrings:DefaultConnection", "BootstrapAdmin:Username", "BootstrapAdmin:Password", "Storage:DocumentRoot", "Storage:ExtractionRoot", "Storage:BackupRoot", "OnlyOffice:Enabled", "OnlyOffice:BrowserUrl", "OnlyOffice:AppExternalUrl", "OnlyOffice:DocumentServerUrl", "OnlyOffice:JwtSecret" })
        {
            var envKey = key.Replace(":", "__");
            Console.WriteLine($"{key} source: {Source(configuration, key)}; configured: {!string.IsNullOrWhiteSpace(configuration[key])}; environment scopes: process={Present(envKey, EnvironmentVariableTarget.Process)}, user={Present(envKey, EnvironmentVariableTarget.User)}, machine={Present(envKey, EnvironmentVariableTarget.Machine)}");
        }
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine($"DB fingerprint: {DatabaseFingerprint(connectionString)}");
        }
        Console.WriteLine($"Bootstrap username: {configuration["BootstrapAdmin:Username"]?.Trim() ?? "(unset)"}");
        Console.WriteLine($"Bootstrap password present: {!string.IsNullOrWhiteSpace(configuration["BootstrapAdmin:Password"])}");
        foreach (var key in new[] { "Storage:DocumentRoot", "Storage:ExtractionRoot", "Storage:BackupRoot" })
        {
            var path = configuration[key];
            if (string.IsNullOrWhiteSpace(path))
            {
                if (officePreflight) preflightOk = false;
                continue;
            }
            var writable = false;
            try
            {
                Directory.CreateDirectory(path);
                var probe = Path.Combine(path, $".lac-auth-doctor-{Guid.NewGuid():N}");
                using (File.Create(probe)) { }
                File.Delete(probe);
                writable = true;
            }
            catch { /* Diagnostic only. */ }
            Console.WriteLine($"{key} writable: {writable}");
            if (officePreflight && !writable) preflightOk = false;
        }
        if (officePreflight)
        {
            foreach (var key in new[] { "ConnectionStrings:DefaultConnection", "OnlyOffice:BrowserUrl", "OnlyOffice:AppExternalUrl", "OnlyOffice:DocumentServerUrl", "OnlyOffice:JwtSecret" })
                if (string.IsNullOrWhiteSpace(configuration[key])) preflightOk = false;
            if (!bool.TryParse(configuration["OnlyOffice:Enabled"], out var enabled) || !enabled) preflightOk = false;
            Console.WriteLine($"Office preflight: {(preflightOk ? "PASS" : "FAIL (review missing or unwritable settings above)")}");
        }
        return preflightOk;
    }

    private static bool Present(string key, EnvironmentVariableTarget target) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key, target));

    public static string DatabaseFingerprint(string connectionString)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        return $"host={connection.Host}; port={connection.Port}; database={connection.Database}; username={connection.Username}; password present={!string.IsNullOrWhiteSpace(connection.Password)}";
    }

    private static string Source(IConfiguration configuration, string key)
    {
        if (configuration is not IConfigurationRoot root) return "unknown";
        foreach (var provider in root.Providers.Reverse())
        {
            if (!provider.TryGet(key, out _)) continue;
            if (provider is FileConfigurationProvider file) return file.Source.Path ?? "configuration file";
            return provider.GetType().Name;
        }
        return "unset";
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string ReadSecret(string environmentName, string prompt)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(environmentName);
        if (fromEnvironment is not null)
        {
            Environment.SetEnvironmentVariable(environmentName, null);
            return fromEnvironment;
        }
        if (Console.IsInputRedirected) throw new InvalidOperationException("Secure interactive input is unavailable.");
        Console.Write(prompt);
        var characters = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (characters.Count > 0) characters.RemoveAt(characters.Count - 1);
            }
            else if (!char.IsControl(key.KeyChar)) characters.Add(key.KeyChar);
        }
        Console.WriteLine();
        return new string(characters.ToArray());
    }
}
