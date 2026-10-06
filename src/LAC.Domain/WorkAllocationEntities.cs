namespace LAC.Domain;

public enum OperationalWorkKind { LandAcquisition, Award, LandRecords, Nm, Enm, Possession, Accounts, Compensation, StatementA, Court, Rti, Correspondence, General }
public enum AllocationScopeKind { Global, District, Subdivision, Village }

public sealed class WorkDefinition : OfficialRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public OperationalWorkKind Kind { get; set; }
    public Guid WorkstreamId { get; set; }
    public Workstream Workstream { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public int Revision { get; set; }
}

// Responsibility only. RolePermission remains the sole positive permission source.
public sealed class WorkAllocation : OfficialRecord
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public Guid WorkDefinitionId { get; set; }
    public WorkDefinition WorkDefinition { get; set; } = null!;
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public string WorkOrderReference { get; set; } = "";
    public string? Reason { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public Guid? DelegatedFromAllocationId { get; set; }
    public WorkAllocation? DelegatedFromAllocation { get; set; }
    public int Revision { get; set; }
    public ICollection<WorkAllocationScope> Scopes { get; set; } = new List<WorkAllocationScope>();
}

public sealed class WorkAllocationScope
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkAllocationId { get; set; }
    public WorkAllocation WorkAllocation { get; set; } = null!;
    public AllocationScopeKind Kind { get; set; }
    public Guid? DistrictId { get; set; }
    public District? District { get; set; }
    public Guid? SubDivisionId { get; set; }
    public SubDivision? SubDivision { get; set; }
    public Guid? VillageId { get; set; }
    public Village? Village { get; set; }
}

// A restrictive delegation ceiling, never a positive permission grant.
public sealed class AssistantPermissionLimit
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}

public sealed record AllocationScopeInput([property: System.Text.Json.Serialization.JsonRequired] AllocationScopeKind Kind, Guid? DistrictId = null, Guid? SubDivisionId = null, Guid? VillageId = null);
public sealed record WorkAllocationInput(Guid WorkDefinitionId, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo,
    string WorkOrderReference, string? Reason, IReadOnlyList<AllocationScopeInput> Scopes,
    Guid? DelegatedFromAllocationId = null);
