using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace LAC.Tests;

// For existing isolated unit fixtures. Persistence/API acceptance uses the real
// AccessControlService, active test roles, cookie login and local PostgreSQL.
internal sealed class StaffUnitUser : ICurrentUserContext
{
    public bool IsAuthenticated => true;
    public Guid? UserId => SeedData.BootstrapAdminId;
    public string? Username => "synthetic-test-admin";
    public string? DisplayName => Username;
    public Guid? DesignationId => null;
    public string? DesignationCode => null;
    public string? DesignationName => null;
    public IReadOnlyList<string> Roles => [];
    public IReadOnlyList<string> Permissions => [];
    public IReadOnlyList<Guid> WorkstreamIds => [];
    public IReadOnlyList<string> WorkstreamCodes => [];
    public IReadOnlyList<Guid> DeskIds => [];
    public IReadOnlyList<string> DeskCodes => [];
    public Guid? PrimaryDeskId => null;
}
internal sealed class StaffUnitAccess : IAccessControlService
{
    public Task<bool> CanAsync(string code, AccessResourceContext? context = null, CancellationToken ct = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<string, ScopeMode>> GetEffectivePermissionsAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<string, ScopeMode>>(new Dictionary<string, ScopeMode>());
}
internal static class StaffUnitServices
{
    public static AwardPdfExtractionService Extraction(LacDbContext db, IDocumentStorage storage, IAwardPdfJobQueue queue) =>
        new(db, queue, new(db, storage, new StaffUnitAccess(), new StaffUnitUser(), new ConfigurationBuilder().Build()));
}
