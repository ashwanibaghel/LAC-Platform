using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed class AllocationException(int status, string message) : Exception(message)
{
    public int StatusCode { get; } = status;
}

public sealed class WorkAllocationService(LacDbContext db, TimeProvider clock)
{
    public async Task<WorkAllocation> StageAsync(Guid userId, WorkAllocationInput input, Guid? supervisorId, CancellationToken ct, bool stage = true)
    {
        if (!await db.WorkDefinitions.AnyAsync(w => w.Id == input.WorkDefinitionId && w.IsActive && w.RecordStatus == RecordStatus.Active, ct))
            throw new AllocationException(400, "Work is missing or inactive.");
        if (input.ValidFrom == default || input.ValidTo <= input.ValidFrom || string.IsNullOrWhiteSpace(input.WorkOrderReference)
            || input.Scopes is null || input.Scopes.Count is < 1 or > 100)
            throw new AllocationException(400, "A work-order reference, valid interval and explicit scopes are required.");
        var scopes = new List<WorkAllocationScope>();
        foreach (var inputScope in input.Scopes.Distinct())
        {
            if (!Enum.IsDefined(inputScope.Kind)) throw new AllocationException(400, "Unknown scope kind.");
            var count = (inputScope.DistrictId.HasValue ? 1 : 0) + (inputScope.SubDivisionId.HasValue ? 1 : 0) + (inputScope.VillageId.HasValue ? 1 : 0);
            var valid = inputScope.Kind switch
            {
                AllocationScopeKind.Global => count == 0,
                AllocationScopeKind.District => count == 1 && inputScope.DistrictId.HasValue && await db.Districts.AnyAsync(x => x.Id == inputScope.DistrictId && x.RecordStatus == RecordStatus.Active, ct),
                AllocationScopeKind.Subdivision => count == 1 && inputScope.SubDivisionId.HasValue && await db.SubDivisions.AnyAsync(x => x.Id == inputScope.SubDivisionId && x.RecordStatus == RecordStatus.Active, ct),
                AllocationScopeKind.Village => count == 1 && inputScope.VillageId.HasValue && await db.Villages.AnyAsync(x => x.Id == inputScope.VillageId && x.RecordStatus == RecordStatus.Active, ct),
                _ => false
            };
            if (!valid) throw new AllocationException(400, "Scope kind must have exactly its matching active target.");
            scopes.Add(new WorkAllocationScope { Kind = inputScope.Kind, DistrictId = inputScope.DistrictId, SubDivisionId = inputScope.SubDivisionId, VillageId = inputScope.VillageId });
        }
        if (supervisorId.HasValue)
        {
            var parent = await db.WorkAllocations.Include(x => x.Scopes).Include(x => x.WorkDefinition)
                .SingleOrDefaultAsync(x => x.Id == input.DelegatedFromAllocationId, ct);
            if (parent is null || parent.UserId != supervisorId || parent.DelegatedFromAllocationId.HasValue
                || parent.WorkDefinitionId != input.WorkDefinitionId || !Active(parent)
                || input.ValidFrom < parent.ValidFrom || (parent.ValidTo.HasValue && (!input.ValidTo.HasValue || input.ValidTo > parent.ValidTo)))
                throw new AllocationException(403, "Delegation must reference a current supervising officer allocation and fit its interval.");
            foreach (var child in scopes)
                if (!await ScopeContainedAsync(child, parent.Scopes, ct))
                    throw new AllocationException(403, "Delegated scopes must be a subset of the officer's current allocation.");
        }
        else if (input.DelegatedFromAllocationId.HasValue)
            throw new AllocationException(400, "Only assistants can receive delegated allocations.");
        var allocation = new WorkAllocation
        {
            UserId = userId, WorkDefinitionId = input.WorkDefinitionId, ValidFrom = input.ValidFrom.ToUniversalTime(),
            ValidTo = input.ValidTo?.ToUniversalTime(), WorkOrderReference = input.WorkOrderReference.Trim(), Reason = input.Reason?.Trim(),
            DelegatedFromAllocationId = input.DelegatedFromAllocationId, Scopes = scopes
        };
        if (stage) db.WorkAllocations.Add(allocation);
        return allocation;
    }

    public bool Active(WorkAllocation a) => a.RecordStatus == RecordStatus.Active && a.RevokedAt is null
        && a.ValidFrom <= clock.GetUtcNow() && (a.ValidTo is null || a.ValidTo > clock.GetUtcNow())
        && a.WorkDefinition.IsActive && a.WorkDefinition.RecordStatus == RecordStatus.Active;

    public async Task<bool> CanWorkAsync(Guid userId, OperationalWorkKind? kind, Guid? workstreamId,
        IReadOnlyList<Guid> villageIds, CancellationToken ct)
    {
        var user = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive && u.RecordStatus == RecordStatus.Active, ct);
        if (user is null) return false;
        if (user.SupervisingOfficerId.HasValue && !await db.AppUsers.AnyAsync(u => u.Id == user.SupervisingOfficerId
                && u.IsActive && u.RecordStatus == RecordStatus.Active && u.SupervisingOfficerId == null, ct)) return false;
        var now = clock.GetUtcNow();
        var query = db.WorkAllocations.AsNoTracking().Include(x => x.WorkDefinition).Include(x => x.Scopes)
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.RecordStatus == RecordStatus.Active
                && x.ValidFrom <= now && (x.ValidTo == null || x.ValidTo > now)
                && x.WorkDefinition.IsActive && x.WorkDefinition.RecordStatus == RecordStatus.Active
                && x.WorkDefinition.Workstream.IsActive && x.WorkDefinition.Workstream.RecordStatus == RecordStatus.Active);
        if (kind.HasValue) query = query.Where(x => x.WorkDefinition.Kind == kind);
        if (workstreamId.HasValue) query = query.Where(x => x.WorkDefinition.WorkstreamId == workstreamId);
        var allocations = await query.ToListAsync(ct);
        // An explicit global responsibility authorizes reaching business validation for unknown
        // or historical geography; bounded allocations still require a resolved matching target.
        if (!user.SupervisingOfficerId.HasValue && allocations.Any(x => x.Scopes.Any(s => s.Kind == AllocationScopeKind.Global))) return true;
        var villageRows = await db.Villages.AsNoTracking().Where(x => villageIds.Contains(x.Id) && x.RecordStatus == RecordStatus.Active)
            .Select(x => new { x.Id, x.SubDivisionId }).ToListAsync(ct);
        var subdivisionIds = villageRows.Select(x => x.SubDivisionId).Distinct().ToList();
        var districts = await db.SubDivisions.Where(x => subdivisionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DistrictId, ct);
        var villages = villageRows.Select(x => new Geo(x.Id, x.SubDivisionId, districts.TryGetValue(x.SubDivisionId, out var district) ? district : null)).ToList();
        if (villages.Count != villageIds.Distinct().Count()) return false;
        var targets = villages.Count == 0 ? new List<Geo?> { null } : villages.Cast<Geo?>().ToList();
        foreach (var target in targets)
        {
            var matched = false;
            foreach (var allocation in allocations)
            {
                if (!Covers(allocation.Scopes, target)) continue;
                if (user.SupervisingOfficerId.HasValue)
                {
                    var parent = await db.WorkAllocations.AsNoTracking().Include(x => x.WorkDefinition).Include(x => x.Scopes)
                        .SingleOrDefaultAsync(x => x.Id == allocation.DelegatedFromAllocationId, ct);
                    if (parent is null || parent.UserId != user.SupervisingOfficerId || parent.DelegatedFromAllocationId.HasValue
                        || parent.WorkDefinitionId != allocation.WorkDefinitionId || !Active(parent) || !Covers(parent.Scopes, target)) continue;
                }
                matched = true;
                break;
            }
            if (!matched) return false;
        }
        return true;
    }

    private sealed record Geo(Guid Village, Guid Subdivision, Guid? District);
    private static bool Covers(IEnumerable<WorkAllocationScope> scopes, Geo? geo) => scopes.Any(x => x.Kind == AllocationScopeKind.Global
        || (geo is not null && (x.Kind == AllocationScopeKind.Village && x.VillageId == geo.Village
            || x.Kind == AllocationScopeKind.Subdivision && x.SubDivisionId == geo.Subdivision
            || x.Kind == AllocationScopeKind.District && x.DistrictId == geo.District)));

    private async Task<bool> ScopeContainedAsync(WorkAllocationScope child, IEnumerable<WorkAllocationScope> parents, CancellationToken ct)
    {
        if (parents.Any(x => x.Kind == AllocationScopeKind.Global)) return true;
        if (child.Kind == AllocationScopeKind.Global) return false;
        if (parents.Any(x => x.Kind == child.Kind && x.DistrictId == child.DistrictId && x.SubDivisionId == child.SubDivisionId && x.VillageId == child.VillageId)) return true;
        if (child.Kind == AllocationScopeKind.Village)
        {
            var geo = await db.Villages.Where(x => x.Id == child.VillageId).Select(x => new Geo(x.Id, x.SubDivisionId, x.SubDivision.DistrictId)).SingleAsync(ct);
            return Covers(parents, geo);
        }
        if (child.Kind == AllocationScopeKind.Subdivision)
        {
            var district = await db.SubDivisions.Where(x => x.Id == child.SubDivisionId).Select(x => x.DistrictId).SingleAsync(ct);
            return parents.Any(x => x.Kind == AllocationScopeKind.District && x.DistrictId == district);
        }
        return false;
    }

    public void Revoke(WorkAllocation allocation, Guid actor)
    {
        allocation.RevokedAt ??= clock.GetUtcNow();
        allocation.RevokedByUserId = actor;
        allocation.Revision++;
    }
}
