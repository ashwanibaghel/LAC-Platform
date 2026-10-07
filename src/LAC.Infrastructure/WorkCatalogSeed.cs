using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public static class WorkCatalogSeed
{
    public static async Task SeedAsync(LacDbContext db, CancellationToken ct)
    {
        var entries = new (string Code, string Name, OperationalWorkKind Kind, string Stream)[]
        {
            ("LAND_ACQUISITION", "Land Acquisition", OperationalWorkKind.LandAcquisition, WorkstreamCodes.LandAcquisition),
            ("AWARD", "Award", OperationalWorkKind.Award, WorkstreamCodes.Award),
            ("LR", "Land Records / LR", OperationalWorkKind.LandRecords, WorkstreamCodes.LandRecords),
            ("NM", "NM", OperationalWorkKind.Nm, WorkstreamCodes.Award),
            ("ENM", "ENM", OperationalWorkKind.Enm, WorkstreamCodes.Award),
            ("POSSESSION", "Possession", OperationalWorkKind.Possession, WorkstreamCodes.Possession),
            ("ACCOUNTS", "Accounts", OperationalWorkKind.Accounts, WorkstreamCodes.AccountsCompensation),
            ("COMPENSATION", "Compensation", OperationalWorkKind.Compensation, WorkstreamCodes.AccountsCompensation),
            ("STATEMENT_A", "Statement-A", OperationalWorkKind.StatementA, WorkstreamCodes.AccountsCompensation),
            ("COURT", "Court Cases", OperationalWorkKind.Court, WorkstreamCodes.CourtReferences),
            ("RTI", "RTI", OperationalWorkKind.Rti, WorkstreamCodes.Rti),
            ("CORRESPONDENCE", "Correspondence", OperationalWorkKind.Correspondence, WorkstreamCodes.DakCorrespondence),
            ("GENERAL", "General Administration", OperationalWorkKind.General, "DRAFTING_NOTING")
        };
        foreach (var item in entries)
        {
            if (await db.WorkDefinitions.AnyAsync(x => x.Code == item.Code, ct)) continue;
            var stream = await db.Workstreams.SingleAsync(x => x.Code == item.Stream, ct);
            db.WorkDefinitions.Add(new WorkDefinition { Code = item.Code, Name = item.Name, Kind = item.Kind, WorkstreamId = stream.Id });
        }
        await db.SaveChangesAsync(ct);
        // Explicit one-time bootstrap responsibility; never infer assignments from civil designations/names.
        var bootstrap = await db.AppUsers.SingleOrDefaultAsync(x => x.Id == SeedData.BootstrapAdminId, ct);
        if (bootstrap is not null && !await db.WorkAllocations.AnyAsync(x => x.UserId == bootstrap.Id, ct))
        {
            foreach (var work in await db.WorkDefinitions.Where(x => x.IsActive).ToListAsync(ct))
                db.WorkAllocations.Add(new WorkAllocation { UserId = bootstrap.Id, WorkDefinitionId = work.Id,
                    ValidFrom = DateTimeOffset.UnixEpoch, WorkOrderReference = "SYSTEM-BOOTSTRAP",
                    Scopes = [new WorkAllocationScope { Kind = AllocationScopeKind.Global }] });
            await db.SaveChangesAsync(ct);
        }
    }
}
