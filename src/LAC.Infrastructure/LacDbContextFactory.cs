using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LAC.Infrastructure;

/// <summary>Creates the model for EF tooling without opening a database connection.</summary>
public sealed class LacDbContextFactory : IDesignTimeDbContextFactory<LacDbContext>
{
    public LacDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        // EF only needs a provider and a syntactically valid connection string to generate migrations.
        connection = string.IsNullOrWhiteSpace(connection) ? "Host=localhost;Database=lac_design" : connection;
        return new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection).Options);
    }
}
