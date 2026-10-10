namespace LAC.Tests;

using Npgsql;

// Keep the legacy default, but allow a caller to opt into its own disposable cluster.
// Never use an occupied acceptance server to satisfy a hard-coded test port.
internal static class RbacPostgresServer
{
    internal static void RequireDedicatedServer(NpgsqlConnectionStringBuilder cs)
    {
        var configuredPort = Environment.GetEnvironmentVariable("LAC_RBAC_TEST_PORT");
        var port = 55442;
        if (!string.IsNullOrWhiteSpace(configuredPort) && (!int.TryParse(configuredPort, out port) || port is < 1024 or > 65535))
            throw new InvalidOperationException("LAC_RBAC_TEST_PORT must name the caller's dedicated disposable server port.");
        if (cs.Host != "127.0.0.1" || cs.Port != port || cs.Database != "postgres")
            throw new InvalidOperationException($"RBAC integration tests require the explicitly selected dedicated loopback server on port {port}, database postgres.");
    }
}
