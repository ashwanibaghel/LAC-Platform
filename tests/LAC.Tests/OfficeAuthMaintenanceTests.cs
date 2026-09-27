namespace LAC.Tests;

using System.Net;
using System.Net.Http.Json;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

public sealed class OfficeAuthMaintenanceTests
{
    [Fact]
    public async Task Bootstrap_is_initial_only_and_explicit_reset_changes_existing_password()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = $"office-auth-{Guid.NewGuid():N}";
        const string username = "office-admin@example.test";
        const string initialPassword = "Initial-Pass-987!";
        const string changedBootstrapPassword = "Changed-Bootstrap-987!";
        const string resetPassword = "Explicit-Reset-987!";

        string initialHash;
        await using (var first = new OfficeAuthFactory(databaseName, root, username, initialPassword))
        {
            using var client = first.CreateClient();
            Assert.Equal(HttpStatusCode.OK, await LoginAsync(client, username, initialPassword));
            initialHash = await GetHashAsync(first.Services, username);
        }

        await using (var sameBootstrap = new OfficeAuthFactory(databaseName, root, username, initialPassword))
        {
            using var client = sameBootstrap.CreateClient();
            Assert.Equal(HttpStatusCode.OK, await LoginAsync(client, username, initialPassword));
            Assert.Equal(initialHash, await GetHashAsync(sameBootstrap.Services, username));
        }

        await using (var changedBootstrap = new OfficeAuthFactory(databaseName, root, username, changedBootstrapPassword))
        {
            using var client = changedBootstrap.CreateClient();
            Assert.Equal(initialHash, await GetHashAsync(changedBootstrap.Services, username));
            Assert.Equal(HttpStatusCode.OK, await LoginAsync(client, username, initialPassword));
            Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(client, username, changedBootstrapPassword));

            using (var scope = changedBootstrap.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
                var user = await db.AppUsers.SingleAsync(x => x.NormalizedUsername == username.ToUpperInvariant());
                await OfficeAuthMaintenance.ResetExistingPasswordAsync(db, hasher, user, resetPassword);
                Assert.NotEqual(initialHash, user.PasswordHash);
                Assert.NotNull(user.PasswordChangedAt);
            }

            Assert.Equal(HttpStatusCode.OK, await LoginAsync(client, username, resetPassword));
            Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(client, username, initialPassword));
            using (var scope = changedBootstrap.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
                var user = await db.AppUsers.SingleAsync(x => x.NormalizedUsername == username.ToUpperInvariant());
                user.IsActive = false;
                await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(client, username, resetPassword));
        }
    }

    [Fact]
    public void Database_fingerprint_exposes_database_difference_without_password()
    {
        var laptop = OfficeAuthMaintenance.DatabaseFingerprint("Host=127.0.0.1;Port=5432;Database=lac_laptop;Username=lac_app;Password=first-secret");
        var office = OfficeAuthMaintenance.DatabaseFingerprint("Host=127.0.0.1;Port=5432;Database=lac_office;Username=lac_app;Password=second-secret");
        Assert.NotEqual(laptop, office);
        Assert.Contains("lac_laptop", laptop);
        Assert.Contains("lac_office", office);
        Assert.DoesNotContain("first-secret", laptop);
        Assert.DoesNotContain("second-secret", office);
    }

    private static async Task<string> GetHashAsync(IServiceProvider services, string username)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        return (await db.AppUsers.SingleAsync(x => x.NormalizedUsername == username.ToUpperInvariant())).PasswordHash;
    }

    private static async Task<HttpStatusCode> LoginAsync(HttpClient client, string username, string password) =>
        (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password))).StatusCode;

    private sealed class OfficeAuthFactory(string databaseName, InMemoryDatabaseRoot root, string username, string password) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = username,
                ["BootstrapAdmin:Password"] = password
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<LacDbContext>>();
                services.RemoveAll<LacDbContext>();
                services.AddDbContext<LacDbContext>(options => options.UseInMemoryDatabase(databaseName, root));
            });
        }
    }
}
