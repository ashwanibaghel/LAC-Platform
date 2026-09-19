namespace LAC.Domain;

public sealed record AccessResourceContext(
    Guid? WorkstreamId = null,
    string? WorkstreamCode = null,
    Guid? OwnerUserId = null,
    Guid? AssignedUserId = null,
    Guid? AssignedDeskId = null
);

public interface IAccessControlService
{
    Task<bool> CanAsync(string permissionCode, AccessResourceContext? resourceContext = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, ScopeMode>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
}
