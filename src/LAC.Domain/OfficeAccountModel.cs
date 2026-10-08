namespace LAC.Domain;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<OfficeAuthority>))]
public enum OfficeAuthority { HELPER, STANDARD_OFFICER, OFFICE_SUPERVISOR, OFFICE_ADMIN, SYSTEM_ADMIN }
public enum OfficeModule { DakMatters, Court, Rti, Accounts, RecordRoom }
public enum LandAccessLevel { None, ViewOnly, ViewWrite }
public enum HelperAccessLevel { None, ReadOnly, ReadWrite }

// Selections are persisted for future modules. Runtime permissions still come from roles.
public sealed class OfficeModuleMembership : OfficialRecord
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public OfficeModule Module { get; set; }
}
