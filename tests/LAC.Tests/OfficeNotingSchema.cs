namespace LAC.Tests;

using System.Text.RegularExpressions;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

public sealed class OfficeSchemaFactAttribute : FactAttribute
{
    public OfficeSchemaFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LAC_OFFICE_TEST_CONNECTION")) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LAC_OFFICE_TEST_BASELINE")))
            Skip = "Requires an explicitly supplied synthetic local database and private INSERT-format baseline dump; creates schemas only.";
    }
}

// No database creation/deletion or public-schema writes. Existing synthetic identities are copied, never recreated in public.
internal sealed class OfficeNotingSchema(string connection, string schema)
{
    internal string Connection => connection;
    internal string Name => schema;
    internal LacDbContext Context() => new(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection, o => o.EnableRetryOnFailure(2)).Options);
    internal static async Task<OfficeNotingSchema> CreateAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("LAC_OFFICE_TEST_CONNECTION"));
        if (builder.Host != "127.0.0.1" || builder.Database?.StartsWith("lac_onlyoffice_real_",StringComparison.Ordinal) != true || builder.SearchPath is not (null or ""))
            throw new InvalidOperationException("Office tests require an explicitly isolated local synthetic database without a supplied search path.");
        var name = "lac_office_test_" + Guid.NewGuid().ToString("N");
        var sql = await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("LAC_OFFICE_TEST_BASELINE")!);
        if (sql.Contains("DROP DATABASE",StringComparison.OrdinalIgnoreCase) || !sql.Contains("CREATE SCHEMA public;") || sql.Contains("CREATE SCHEMA ",StringComparison.Ordinal) && Regex.Matches(sql,"CREATE SCHEMA ").Count != 1)
            throw new InvalidOperationException("Expected only a public-schema synthetic pg_dump baseline.");
        sql = Regex.Replace(sql, @"^\\.*$", "", RegexOptions.Multiline).Replace("CREATE SCHEMA public;",$"CREATE SCHEMA {name};").Replace("public.",name+".").Replace("SCHEMA public", "SCHEMA " + name);
        await using (var conn = new NpgsqlConnection(builder.ConnectionString))
        {
            await conn.OpenAsync(); await using var command = new NpgsqlCommand(sql,conn) { CommandTimeout = 120 }; await command.ExecuteNonQueryAsync();
        }
        builder.SearchPath=name; builder.Pooling=false;
        var result = new OfficeNotingSchema(builder.ConnectionString,name);
        await using var db=result.Context(); await db.Database.MigrateAsync();
        return result; // Retained for acceptance audit; never DROP DATABASE/schema automatically.
    }
}
