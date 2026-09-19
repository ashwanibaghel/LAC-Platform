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

    /// <summary>
    /// Note: DeskIds, DeskCodes, and PrimaryDeskId originate from authentication-cookie claims as session/UI snapshots.
    /// In accordance with Phase 2B architectural rules, cookie claims MUST NOT become authoritative proof of current Desk
    /// membership for Dak assignment or movement authorization; future Phase 2B operations must query live database state.
    /// </summary>
    IReadOnlyList<Guid> DeskIds { get; }
    IReadOnlyList<string> DeskCodes { get; }

    /// <summary>
    /// Default/home organizational desk for the user. Does not confer permissions or automatic Dak routing.
    /// A user may have zero primary desks.
    /// </summary>
    Guid? PrimaryDeskId { get; }
}

