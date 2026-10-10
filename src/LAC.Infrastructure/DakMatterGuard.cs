namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public static class DakMatterGuard
{
    public static async Task<Dak> ConfirmedHolderAsync(LacDbContext db, Guid dakId, Guid actor, CancellationToken ct)
    {
        var dak = await db.Daks.Include(x => x.CurrentAssignment).SingleOrDefaultAsync(x => x.Id == dakId, ct);
        if (dak is null || dak.RecordStatus != RecordStatus.Active || dak.Status is DakStatus.Resolved or DakStatus.Disposed or DakStatus.Cancelled ||
            dak.RoutingState != DakRoutingState.WithHolder || dak.CurrentAssignment is not { IsActive: true, RecordStatus: RecordStatus.Active, ReceivedAt: not null } a || a.AssignedUserId != actor ||
            !await db.UserDeskMemberships.AnyAsync(x => x.UserId == actor && x.OfficeDeskId == a.OfficeDeskId && x.IsActive && x.RemovedAt == null &&
                x.RecordStatus == RecordStatus.Active && x.OfficeDesk.IsActive && x.OfficeDesk.RecordStatus == RecordStatus.Active, ct) ||
            !await new DakAuthorizationService(db).CanAccessDakAsync(dakId, PermissionCodes.DakView, actor, ct))
            throw new MatterWorkflowException("Only the authorized confirmed received holder can work from this active Dak.", 403);
        return dak;
    }

    public static async Task ValidateVillageAsync(LacDbContext db, Dak dak, Guid village, CancellationToken ct)
    {
        if (dak.VillageClassification is not (DakVillageClassification.VillageSpecific or DakVillageClassification.MultiVillage) ||
            !await db.DakVillageLinks.AnyAsync(x => x.DakId == dak.Id && x.VillageId == village && x.RecordStatus == RecordStatus.Active, ct) ||
            !await db.Villages.AnyAsync(x => x.Id == village && x.RecordStatus == RecordStatus.Active, ct))
            throw new MatterWorkflowException("Select an explicitly validated Village of this Dak before creating/linking a Matter.", 409);
    }

    public static async Task ValidateLinkAsync(LacDbContext db, Guid dakId, Matter matter, Guid actor, CancellationToken ct)
    {
        var dak = await ConfirmedHolderAsync(db, dakId, actor, ct);
        await ValidateVillageAsync(db, dak, matter.VillageId, ct);
        if (!await new MatterAuthorizationService(db).CanAccessMatterAsync(matter.Id, PermissionCodes.MatterEdit, actor, ct))
            throw new MatterWorkflowException("Matter link is outside your authorized working scope.", 403);
    }
}
