using System.Text;
using System.Text.Json.Serialization;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LAC.Api;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OfficeAccountInput(string FullName, Guid? DesignationId, string? CustomDesignation,
    OfficeAuthority Authority, IReadOnlyList<OfficeModule> Modules, bool CanRegisterInwardDak,
    LandAccessLevel LandAccess, IReadOnlyList<Guid> DeskIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOfficeAccountRequest(string Username, OfficeAccountInput Account);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateOfficeAccountRequest(OfficeAccountInput Account, int ExpectedRevision);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OfficeHelperInput(string FullName, Guid? DesignationId, string? CustomDesignation, Guid? DeskId,
    HelperAccessLevel Access, IReadOnlyList<string>? PermissionCodes = null, IReadOnlyList<WorkAllocationInput>? Allocations = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOfficeHelperRequest(string Username, OfficeHelperInput Helper);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateOfficeHelperRequest(OfficeHelperInput Helper, int ExpectedRevision);

public sealed class OfficeAccountService(LacDbContext db, IPasswordHasher<AppUser> hasher, WorkAllocationService allocations)
{
    private static string Clean(string? value, int max, string label, bool required = true)
    {
        if (value?.Any(char.IsControl) == true) throw new AllocationException(400, $"Invalid {label}.");
        var normalized = value?.Normalize(NormalizationForm.FormKC).Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            if (required) throw new AllocationException(400, $"{label} is required.");
            return "";
        }
        if (normalized.Length > max || normalized.Any(char.IsControl)) throw new AllocationException(400, $"Invalid {label}.");
        return normalized;
    }
    public async Task ValidateDesignation(AppUser user, Guid? id, string? custom, bool helper, CancellationToken ct)
    {
        var text = Clean(custom, 200, "custom designation", false);
        if (id.HasValue && text.Length > 0) throw new AllocationException(400, "Choose standard or custom designation, never both.");
        if (id.HasValue && !await db.Designations.AnyAsync(d => d.Id == id && d.IsActive && d.RecordStatus == RecordStatus.Active
                && (!helper || d.Code == "DEO"), ct)) throw new AllocationException(400, "Invalid designation.");
        if (helper && !id.HasValue && text.Length == 0) throw new AllocationException(400, "Helper needs DEO or a custom designation.");
        user.DesignationId = id; user.CustomDesignation = text.Length == 0 ? null : text;
    }
    private async Task<AppUser> NewUser(string username, CancellationToken ct)
    {
        var name = Clean(username, 100, "username");
        if (name.Any(char.IsWhiteSpace)) throw new AllocationException(400, "Username cannot contain whitespace.");
        var normalized = name.ToUpperInvariant();
        if (await db.AppUsers.AnyAsync(u => u.NormalizedUsername == normalized, ct)) throw new AllocationException(409, "Username exists.");
        var user = new AppUser { Username = name, NormalizedUsername = normalized };
        db.AppUsers.Add(user); return user;
    }
    public async Task<(AppUser User, string Credential)> Create(Guid actor, CreateOfficeAccountRequest request, CancellationToken ct)
    {
        var user = await NewUser(request.Username, ct);
        await Apply(actor, user, request.Account, true, ct);
        return (user, TemporaryCredentials.Issue(user, hasher));
    }
    public async Task Apply(Guid actor, AppUser user, OfficeAccountInput input, bool creating, CancellationToken ct)
    {
        if (input is null) throw new AllocationException(400, "Account selection is required.");
        if (!Enum.IsDefined(input.Authority) || !Enum.IsDefined(input.LandAccess) || input.Authority == OfficeAuthority.HELPER
            || input.Modules is null || input.DeskIds is null || input.Modules.Any(m => !Enum.IsDefined(m)) || input.DeskIds.Count > 100)
            throw new AllocationException(400, "Invalid account access selection.");
        var caller = await OfficeAuthorityService.GetAsync(db, actor, ct);
        if (!OfficeAuthorityService.CanGrant(caller, input.Authority) || caller < OfficeAuthority.OFFICE_SUPERVISOR
            || !creating && (!await OfficeAuthorityService.CanManageAsync(db, actor, user.Id, ct) || user.SupervisingOfficerId.HasValue))
            throw new AllocationException(403, "Authority hierarchy does not permit this account change.");
        var level = input.Authority;
        if (creating && caller == OfficeAuthority.SYSTEM_ADMIN && input.DesignationId.HasValue
            && await db.Designations.AnyAsync(d => d.Id == input.DesignationId && d.Code == "ADM", ct))
        {
            if (level == OfficeAuthority.SYSTEM_ADMIN) throw new AllocationException(400, "Technical SYSTEM_ADMIN and official ADM are separate identities.");
            level = OfficeAuthority.OFFICE_ADMIN;
        }
        if (caller != OfficeAuthority.SYSTEM_ADMIN && input.DesignationId.HasValue
            && await db.Designations.AnyAsync(d => d.Id == input.DesignationId && d.Code == "ADM", ct))
            throw new AllocationException(403, "The canonical ADM designation can only be assigned by a System Administrator. Custom titles never change authority.");
        if (!creating && await OfficeAuthorityService.GetAsync(db, user.Id, ct, true) == OfficeAuthority.SYSTEM_ADMIN
            && level != OfficeAuthority.SYSTEM_ADMIN && !await db.UserRoles.AnyAsync(r => r.UserId != user.Id && r.Role.Code == "SYSTEM_ADMIN"
                && r.Role.IsActive && r.Role.RecordStatus == RecordStatus.Active && r.User.IsActive && r.User.RecordStatus == RecordStatus.Active, ct))
            throw new AllocationException(400, "Cannot demote the last active System Administrator.");
        if (!creating && actor == user.Id) throw new AllocationException(403, "Use your own profile and password-change flow; administrative self edits are protected.");
        user.DisplayName = Clean(input.FullName, 200, "full name");
        await ValidateDesignation(user, input.DesignationId, input.CustomDesignation, false, ct);
        var desks = input.DeskIds.Distinct().ToList();
        if (await db.OfficeDesks.CountAsync(d => desks.Contains(d.Id) && d.IsActive && d.RecordStatus == RecordStatus.Active, ct) != desks.Count)
            throw new AllocationException(400, "Desk is missing or inactive.");
        var full = level >= OfficeAuthority.OFFICE_SUPERVISOR;
        var modules = full ? Enum.GetValues<OfficeModule>().ToList() : input.Modules.Distinct().ToList();
        user.OfficeAccessManaged = true; user.LandAccess = full ? LandAccessLevel.ViewWrite : input.LandAccess;
        user.CanRegisterInwardDak = full || input.CanRegisterInwardDak;
        var presets = full ? new List<OfficePreset> { OfficeAccessPresets.Authority(level) }
            : new List<OfficePreset> { OfficeAccessPresets.Staff };
        if (!full)
        {
            presets.AddRange(modules.Select(OfficeAccessPresets.Module));
            if (input.CanRegisterInwardDak) presets.Add(OfficeAccessPresets.Registry);
            if (input.LandAccess != LandAccessLevel.None) presets.Add(OfficeAccessPresets.Land(input.LandAccess));
        }
        var codes = presets.Select(p => p.Code).ToList();
        var roles = await db.Roles.Where(r => codes.Contains(r.Code) && r.IsActive && r.RecordStatus == RecordStatus.Active).ToListAsync(ct);
        if (roles.Count != codes.Count) throw new AllocationException(409, "Required server presets are unavailable.");
        db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == user.Id).ToListAsync(ct));
        foreach (var role in roles) db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        var memberships = await db.OfficeModuleMemberships.Where(m => m.UserId == user.Id).ToListAsync(ct);
        foreach (var m in memberships) m.RecordStatus = modules.Contains(m.Module) ? RecordStatus.Active : RecordStatus.Archived;
        foreach (var module in modules.Where(m => !memberships.Any(x => x.Module == m)))
            db.OfficeModuleMemberships.Add(new OfficeModuleMembership { UserId = user.Id, Module = module });
        var streamCodes = presets.SelectMany(p => p.Streams).Distinct().ToList();
        var streams = await db.Workstreams.Where(s => streamCodes.Contains(s.Code) && s.IsActive && s.RecordStatus == RecordStatus.Active).ToListAsync(ct);
        db.UserWorkstreamMemberships.RemoveRange(await db.UserWorkstreamMemberships.Where(m => m.UserId == user.Id).ToListAsync(ct));
        foreach (var stream in streams) db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership { UserId = user.Id, WorkstreamId = stream.Id });
        await SetDesks(user.Id, desks, ct);
        // Only responsibility records owned by this contract are regenerated. Historical assignments remain.
        foreach (var old in await db.WorkAllocations.Where(a => a.UserId == user.Id && (a.WorkOrderReference == OfficeAccessPresets.AllocationOrder || a.WorkOrderReference == "OFFICE-AUTHORITY-V3") && a.RevokedAt == null).ToListAsync(ct))
            allocations.Revoke(old, actor);
        var streamIds = streams.Select(s => s.Id).ToList();
        foreach (var work in await db.WorkDefinitions.Where(w => streamIds.Contains(w.WorkstreamId) && w.IsActive && w.RecordStatus == RecordStatus.Active).ToListAsync(ct))
            db.WorkAllocations.Add(new WorkAllocation { UserId = user.Id, WorkDefinitionId = work.Id, ValidFrom = DateTimeOffset.UtcNow,
                WorkOrderReference = OfficeAccessPresets.AllocationOrder, Scopes = [new WorkAllocationScope { Kind = AllocationScopeKind.Global }] });
        await OfficeSessionSecurity.InvalidateAsync(db, user, ct);
    }
    private async Task SetDesks(Guid user, IReadOnlyList<Guid> desks, CancellationToken ct)
    {
        var old = await db.UserDeskMemberships.Where(m => m.UserId == user && m.IsActive).ToListAsync(ct);
        foreach (var m in old.Where(m => !desks.Contains(m.OfficeDeskId))) { m.IsActive = false; m.IsPrimary = false; m.RemovedAt = DateTimeOffset.UtcNow; }
        foreach (var m in old.Where(m => desks.Contains(m.OfficeDeskId))) m.IsPrimary = desks.FirstOrDefault() == m.OfficeDeskId;
        foreach (var desk in desks.Where(d => !old.Any(m => m.OfficeDeskId == d)))
            db.UserDeskMemberships.Add(new UserDeskMembership { UserId = user, OfficeDeskId = desk, IsPrimary = desks.FirstOrDefault() == desk });
    }
    public async Task<(AppUser User, string Credential)> CreateHelper(Guid actor, Guid supervisor, CreateOfficeHelperRequest request, CancellationToken ct)
    {
        await CanManageHelper(actor, supervisor, ct);
        var user = await NewUser(request.Username, ct);
        user.SupervisingOfficerId = supervisor;
        user.OfficeAccessManaged = true;
        await ApplyHelper(actor, user, request.Helper, ct);
        return (user, TemporaryCredentials.Issue(user, hasher));
    }
    private async Task CanManageHelper(Guid actor, Guid supervisor, CancellationToken ct)
    {
        var parent = await db.AppUsers.SingleOrDefaultAsync(u => u.Id == supervisor && u.IsActive && u.RecordStatus == RecordStatus.Active && u.SupervisingOfficerId == null, ct);
        if (parent is null || actor != supervisor && !await OfficeAuthorityService.CanManageAsync(db, actor, supervisor, ct))
            throw new AllocationException(403, "Helpers require an active officer whom the caller may manage.");
    }
    public async Task ApplyHelper(Guid actor, AppUser user, OfficeHelperInput input, CancellationToken ct)
    {
        if (input is null) throw new AllocationException(400, "Helper selection is required.");
        if (!user.SupervisingOfficerId.HasValue || !Enum.IsDefined(input.Access)) throw new AllocationException(400, "Invalid helper.");
        var parentId = user.SupervisingOfficerId.Value;
        await CanManageHelper(actor, parentId, ct);
        user.DisplayName = Clean(input.FullName, 200, "full name");
        await ValidateDesignation(user, input.DesignationId, input.CustomDesignation, true, ct);
        if (input.DeskId.HasValue && !await db.UserDeskMemberships.AnyAsync(m => m.UserId == parentId && m.OfficeDeskId == input.DeskId
                && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active && m.OfficeDesk.IsActive && m.OfficeDesk.RecordStatus == RecordStatus.Active, ct))
            throw new AllocationException(403, "Helper desk must belong to the supervising officer.");
        var parentGrants = await db.UserRoles.Where(r => r.UserId == parentId && r.Role.IsActive && r.Role.RecordStatus == RecordStatus.Active)
            .SelectMany(r => r.Role.RolePermissions).Where(p => p.Permission.Category != "Administration").Include(p => p.Permission).ToListAsync(ct);
        var ceiling = parentGrants.Select(p => p.Permission.Code).Distinct().Where(c => input.Access == HelperAccessLevel.ReadWrite
            || input.Access == HelperAccessLevel.ReadOnly && OperationalAuthorizationFilter.IsReadPermission(c)).ToList();
        if (input.PermissionCodes is not null)
        {
            if (input.PermissionCodes.Any(c => !ceiling.Contains(c))) throw new AllocationException(403, "Requested helper permission exceeds the parent's current ceiling.");
            ceiling = input.PermissionCodes.Distinct().ToList();
        }
        var role = await db.Roles.SingleAsync(r => r.Code == OfficeAccessPresets.Helper.Code && r.IsActive && r.RecordStatus == RecordStatus.Active, ct);
        db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == user.Id).ToListAsync(ct));
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        var limits = await db.AssistantPermissionLimits.Include(l => l.Permission).Where(l => l.UserId == user.Id).ToListAsync(ct);
        db.AssistantPermissionLimits.RemoveRange(limits.Where(l => !ceiling.Contains(l.Permission.Code)));
        foreach (var permission in parentGrants.Select(p => p.Permission).DistinctBy(p => p.Id).Where(p => ceiling.Contains(p.Code) && !limits.Any(l => l.PermissionId == p.Id)))
            db.AssistantPermissionLimits.Add(new AssistantPermissionLimit { UserId = user.Id, PermissionId = permission.Id });
        foreach (var old in await db.WorkAllocations.Where(a => a.UserId == user.Id && a.RevokedAt == null).ToListAsync(ct)) allocations.Revoke(old, actor);
        var parents = await db.WorkAllocations.Include(a => a.Scopes).Include(a => a.WorkDefinition).Where(a => a.UserId == parentId && a.RevokedAt == null && a.RecordStatus == RecordStatus.Active).ToListAsync(ct);
        var childInputs = input.Allocations ?? parents.Where(allocations.Active).Select(a => new WorkAllocationInput(a.WorkDefinitionId,
            a.ValidFrom, a.ValidTo, "HELPER-CEILING-V3", null, a.Scopes.Select(s => new AllocationScopeInput(s.Kind, s.DistrictId, s.SubDivisionId, s.VillageId)).ToList(), a.Id)).ToList();
        if (childInputs.Count > 100) throw new AllocationException(400, "Too many helper allocations.");
        foreach (var allocation in childInputs) await allocations.StageAsync(user.Id, allocation, parentId, ct);
        var parentStreams = await db.UserWorkstreamMemberships.Where(m => m.UserId == parentId && m.IsActive && m.Workstream.IsActive && m.Workstream.RecordStatus == RecordStatus.Active).Select(m => m.WorkstreamId).ToListAsync(ct);
        db.UserWorkstreamMemberships.RemoveRange(await db.UserWorkstreamMemberships.Where(m => m.UserId == user.Id).ToListAsync(ct));
        foreach (var stream in parentStreams) db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership { UserId = user.Id, WorkstreamId = stream });
        await SetDesks(user.Id, input.DeskId.HasValue ? [input.DeskId.Value] : [], ct);
        await OfficeSessionSecurity.InvalidateAsync(db, user, ct);
        user.AssistantRevision++;
    }
    public async Task<object> Detail(Guid id, CancellationToken ct)
    {
        var user = await db.AppUsers.AsNoTracking().Include(u => u.Designation).SingleAsync(u => u.Id == id, ct);
        var authority = await OfficeAuthorityService.GetAsync(db, id, ct);
        var full = authority >= OfficeAuthority.OFFICE_SUPERVISOR;
        var actor = db.CurrentUser?.UserId;
        var canManage = actor.HasValue && await OfficeDirectoryPolicy.CanActAsync(db, actor.Value, id, ct);
        return new { user.Id, user.Username, fullName = user.DisplayName, user.DesignationId, user.CustomDesignation,
            effectiveDesignation = user.CustomDesignation ?? user.Designation?.Name, authority,
            user.OfficeAccessManaged, user.IsActive, landAccess = full ? LandAccessLevel.ViewWrite : user.LandAccess,
            canRegisterInwardDak = full || user.CanRegisterInwardDak, user.SupervisingOfficerId,
            capabilities = new { canEdit = canManage && (!user.SupervisingOfficerId.HasValue || user.IsActive), canResetCredential = canManage && user.IsActive, canToggleStatus = canManage,
                canAddHelper = user.IsActive && !user.SupervisingOfficerId.HasValue && canManage },
            user.AssistantRevision, revision = user.OfficeRevision, modules = full ? Enum.GetValues<OfficeModule>().ToList()
                : await db.OfficeModuleMemberships.Where(m => m.UserId == id && m.RecordStatus == RecordStatus.Active).Select(m => m.Module).ToListAsync(ct),
            helperPermissionCodes = user.SupervisingOfficerId.HasValue ? await db.AssistantPermissionLimits.Where(l => l.UserId == id).Select(l => l.Permission.Code).ToListAsync(ct) : null,
            deskIds = await db.UserDeskMemberships.Where(m => m.UserId == id && m.IsActive && m.RemovedAt == null && m.RecordStatus == RecordStatus.Active).Select(m => m.OfficeDeskId).ToListAsync(ct) };
    }
}
