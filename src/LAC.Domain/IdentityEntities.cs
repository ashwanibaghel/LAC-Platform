namespace LAC.Domain;

public enum ScopeMode
{
    All = 0,
    Workstream = 1,
    Assigned = 2,
    Own = 3
}

public sealed class Designation : OfficialRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
}

public sealed class AppUser : OfficialRecord
{
    public string Username { get; set; } = "";
    public string NormalizedUsername { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public Guid? DesignationId { get; set; }
    public Designation? Designation { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<UserWorkstreamMembership> WorkstreamMemberships { get; set; } = new List<UserWorkstreamMembership>();
}

public sealed class Role : OfficialRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public sealed class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Category { get; set; } = "";
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public sealed class UserRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RolePermission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
    public ScopeMode ScopeMode { get; set; } = ScopeMode.All;
}

public sealed class Workstream : OfficialRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<UserWorkstreamMembership> UserMemberships { get; set; } = new List<UserWorkstreamMembership>();
}

public sealed class UserWorkstreamMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public Guid WorkstreamId { get; set; }
    public Workstream Workstream { get; set; } = null!;
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
}
