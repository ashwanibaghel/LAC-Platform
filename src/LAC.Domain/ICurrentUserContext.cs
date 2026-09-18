namespace LAC.Domain;

public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? Username { get; }
    string? DisplayName { get; }
    Guid? DesignationId { get; }
    string? DesignationCode { get; }
    string? DesignationName { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    IReadOnlyList<Guid> WorkstreamIds { get; }
    IReadOnlyList<string> WorkstreamCodes { get; }
}
