using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LAC.Tests;

// Existing workflow fixtures historically exercised role/custody rules with system-wide action
// responsibility. Declare that responsibility explicitly; never disable the production action gate.
internal static class TestWorkAllocations
{
    public static async Task GrantGlobalAsync(LacDbContext db, Guid userId)
    {
        var works = await db.WorkDefinitions.Where(x => x.IsActive).ToListAsync();
        foreach (var stream in await db.Workstreams.Where(x => x.IsActive).ToListAsync())
            if (!works.Any(w => w.WorkstreamId == stream.Id))
            {
                var work = new WorkDefinition { Code = $"FIXTURE_{stream.Id:N}", Name = "Fixture responsibility",
                    Kind = OperationalWorkKind.General, WorkstreamId = stream.Id };
                db.WorkDefinitions.Add(work); works.Add(work);
            }
        var existing = await db.WorkAllocations.Where(x => x.UserId == userId && x.RevokedAt == null).Select(x => x.WorkDefinitionId).ToListAsync();
        foreach (var work in works.Where(w => !existing.Contains(w.Id)))
            db.WorkAllocations.Add(new WorkAllocation { UserId = userId, WorkDefinitionId = work.Id, ValidFrom = DateTimeOffset.UnixEpoch,
                WorkOrderReference = "TEST-FIXTURE", Scopes = [new WorkAllocationScope { Kind = AllocationScopeKind.Global }] });
        await db.SaveChangesAsync();
    }
}
