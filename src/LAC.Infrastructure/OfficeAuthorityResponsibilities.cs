using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

// Explicit assignment-side responsibility maintenance; runtime designation checks are never used.
public static class OfficeAuthorityResponsibilities
{
    public static async Task StageAsync(LacDbContext db, AppUser user, IReadOnlyList<Guid> roleIds, Guid? actor, CancellationToken ct)
    {
        const string order = "OFFICE-AUTHORITY-V3";
        var full = await db.Roles.AnyAsync(r => roleIds.Contains(r.Id) && r.IsActive && r.RecordStatus == RecordStatus.Active
            && (r.Code == "SYSTEM_ADMIN" || r.Code == "OFFICE_ADMIN" || r.Code == "OFFICE_SUPERVISOR"), ct);
        var existing = await db.WorkAllocations.Where(a => a.UserId == user.Id && a.WorkOrderReference == order && a.RevokedAt == null).ToListAsync(ct);
        if (!full)
        {
            foreach (var allocation in existing) { allocation.RevokedAt = DateTimeOffset.UtcNow; allocation.RevokedByUserId = actor; allocation.Revision++; }
            return;
        }
        foreach (var work in await db.WorkDefinitions.Where(w => w.IsActive && w.RecordStatus == RecordStatus.Active).ToListAsync(ct))
            if (!existing.Any(a => a.WorkDefinitionId == work.Id)) db.WorkAllocations.Add(new WorkAllocation { UserId = user.Id,
                WorkDefinitionId = work.Id, ValidFrom = DateTimeOffset.UtcNow, WorkOrderReference = order,
                Scopes = [new WorkAllocationScope { Kind = AllocationScopeKind.Global }] });
        var streams = await db.Workstreams.Where(w => w.IsActive && w.RecordStatus == RecordStatus.Active).Select(w => w.Id).ToListAsync(ct);
        var memberships = await db.UserWorkstreamMemberships.Where(m => m.UserId == user.Id && m.IsActive).Select(m => m.WorkstreamId).ToListAsync(ct);
        var removed = db.ChangeTracker.Entries<UserWorkstreamMembership>().Where(e => e.State == EntityState.Deleted && e.Entity.UserId == user.Id).Select(e => e.Entity.WorkstreamId).ToList();
        memberships = memberships.Except(removed).ToList();
        var pending = db.ChangeTracker.Entries<UserWorkstreamMembership>().Where(e => e.State == EntityState.Added && e.Entity.UserId == user.Id).Select(e => e.Entity.WorkstreamId).ToList();
        foreach (var stream in streams.Except(memberships.Concat(pending))) db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership { UserId = user.Id, WorkstreamId = stream });
    }
}
