using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed record OfficePreset(string Code, IReadOnlyList<(string Code, ScopeMode Scope)> Grants, IReadOnlyList<string> Streams);

public static class OfficeAccessPresets
{
    public const string Prefix = "OFFICE_PRESET_";
    public const string AllocationOrder = "OFFICE-ACCESS-V3";
    private static (string, ScopeMode)[] Grants(ScopeMode scope, params string[] codes) => codes.Select(c => (c, scope)).ToArray();
    public static readonly string[] LandView = [PermissionCodes.VillageView, PermissionCodes.KhasraView, PermissionCodes.LrView, PermissionCodes.AwardView];
    public static readonly string[] LandWrite = [.. LandView, PermissionCodes.KhasraEdit, PermissionCodes.LrEdit, PermissionCodes.LrVerify,
        PermissionCodes.LrCommit, PermissionCodes.AwardCreate, PermissionCodes.AwardEdit, PermissionCodes.AwardCoreDocumentUpload];
    public static readonly string[] Operations = PermissionCodes.All.Where(p => p.Category != "Administration").Select(p => p.Code).ToArray();
    public static OfficePreset Module(OfficeModule module) => module switch
    {
        OfficeModule.DakMatters => new(Prefix + "DAK_MATTERS", Grants(ScopeMode.Assigned, PermissionCodes.DakView, PermissionCodes.DakEdit,
            PermissionCodes.DakMove, PermissionCodes.DakReceive, PermissionCodes.DakPullBack, PermissionCodes.DakResolve,
            PermissionCodes.MatterView, PermissionCodes.MatterCreate, PermissionCodes.MatterEdit, PermissionCodes.MatterDocumentManage,
            PermissionCodes.MatterArchive, PermissionCodes.DraftView, PermissionCodes.DraftCreate, PermissionCodes.DraftEdit,
            PermissionCodes.OutwardView, PermissionCodes.OutwardCreate, PermissionCodes.OutwardEdit, PermissionCodes.OutwardDispatch,
            PermissionCodes.OutwardCancel, PermissionCodes.WorkItemView, PermissionCodes.WorkItemCreate, PermissionCodes.WorkItemUpdate,
            PermissionCodes.WorkItemContribute, PermissionCodes.WorkItemComplete, PermissionCodes.ScheduleView, PermissionCodes.ScheduleCreate,
            PermissionCodes.ScheduleUpdate, PermissionCodes.ScheduleComplete), [WorkstreamCodes.DakCorrespondence, "DRAFTING_NOTING"]),
        OfficeModule.Court => new(Prefix + "COURT", Grants(ScopeMode.Workstream, Operations.Where(c => c.StartsWith("Court.")).ToArray()), [WorkstreamCodes.CourtReferences]),
        OfficeModule.Rti => new(Prefix + "RTI", [], [WorkstreamCodes.Rti]),
        OfficeModule.Accounts => new(Prefix + "ACCOUNTS", [], [WorkstreamCodes.AccountsCompensation]),
        OfficeModule.RecordRoom => new(Prefix + "RECORD_ROOM", Grants(ScopeMode.Assigned, PermissionCodes.DakView, PermissionCodes.DakReceive,
            PermissionCodes.DakMove, PermissionCodes.DakPullBack, PermissionCodes.DakResolve), [WorkstreamCodes.RecordRoom, WorkstreamCodes.DakCorrespondence]),
        _ => throw new ArgumentOutOfRangeException(nameof(module))
    };
    public static readonly OfficePreset Registry = new(Prefix + "INWARD_REGISTRY", Grants(ScopeMode.Workstream,
        PermissionCodes.DakView, PermissionCodes.DakRegister, PermissionCodes.DakMark), [WorkstreamCodes.DakCorrespondence]);
    public static OfficePreset Land(LandAccessLevel level) => new(Prefix + "LAND_" + level.ToString().ToUpperInvariant(),
        Grants(ScopeMode.All, level == LandAccessLevel.ViewWrite ? LandWrite : LandView),
        [WorkstreamCodes.LandRecords, WorkstreamCodes.Award, WorkstreamCodes.LandAcquisition, WorkstreamCodes.Possession, WorkstreamCodes.AccountsCompensation]);
    public static OfficePreset Authority(OfficeAuthority authority) => new(authority.ToString(), Grants(ScopeMode.All,
        authority == OfficeAuthority.SYSTEM_ADMIN ? PermissionCodes.All.Select(p => p.Code).ToArray()
        : [.. Operations, PermissionCodes.UsersManage, PermissionCodes.RolesAssign, PermissionCodes.AllocationsManage,
            PermissionCodes.AssistantsManage, PermissionCodes.AuditView, .. authority == OfficeAuthority.OFFICE_ADMIN
                ? new[] { PermissionCodes.WorkCatalogManage, PermissionCodes.OfficeConfigurationManage } : Array.Empty<string>()]),
        [WorkstreamCodes.DakCorrespondence, WorkstreamCodes.LandAcquisition, WorkstreamCodes.Award, WorkstreamCodes.LandRecords,
            WorkstreamCodes.Possession, WorkstreamCodes.AccountsCompensation, WorkstreamCodes.CourtReferences,
            WorkstreamCodes.Rti, WorkstreamCodes.RecordRoom, "DRAFTING_NOTING"]);
    public static readonly OfficePreset Staff = new(Prefix + "STAFF", Grants(ScopeMode.All, PermissionCodes.AssistantsManage), []);
    // Child authority always intersects parent permission/scope and delegated allocations at runtime.
    public static readonly OfficePreset Helper = new(Prefix + "HELPER", Grants(ScopeMode.All, Operations), []);

    public static async Task SeedAsync(LacDbContext db, CancellationToken ct)
    {
        var presets = Enum.GetValues<OfficeModule>().Select(Module).Concat(new[] { Registry, Land(LandAccessLevel.ViewOnly),
            Land(LandAccessLevel.ViewWrite), Staff, Helper, Authority(OfficeAuthority.OFFICE_ADMIN), Authority(OfficeAuthority.OFFICE_SUPERVISOR) });
        foreach (var preset in presets)
        {
            var role = await db.Roles.SingleOrDefaultAsync(r => r.Code == preset.Code, ct);
            if (role is null) { role = new Role { Code = preset.Code, Name = preset.Code, IsSystemRole = true }; db.Roles.Add(role); }
            // A collision with a pre-existing custom role must not silently turn its users into authorities.
            else if (!role.IsSystemRole) throw new InvalidOperationException($"Reserved office role code collision: {preset.Code}");
            foreach (var grant in preset.Grants)
            {
                var permission = await db.Permissions.SingleAsync(p => p.Code == grant.Code, ct);
                if (!await db.RolePermissions.AnyAsync(p => p.RoleId == role.Id && p.PermissionId == permission.Id, ct))
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id, ScopeMode = grant.Scope });
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
