namespace LAC.Api;

using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

public static partial class DakEndpoints
{
    private static int? ReadRevision(HttpRequest request)
    {
        var value = request.Headers.IfMatch.ToString();
        if (string.IsNullOrEmpty(value)) return null; // Existing callers remain compatible; writes still serialize.
        if (!int.TryParse(value.Trim('"'), out var revision) || revision < 0)
            throw new DakWorkflowException("If-Match must contain the Dak revision as a nonnegative integer.");
        return revision;
    }

    private static async Task<List<DakLinkItemDto>> ProjectLinksAsync(IEnumerable<DakLinkItemDto> links, Func<Task<bool>> canRead)
    {
        var items = links.ToList();
        if (items.Count == 0) return items;
        var allowed = await canRead();
        return allowed ? items : items.Select(l => l with { EntityId = null, DisplayName = "Restricted record", CanOpen = false }).ToList();
    }

    private static async Task<IResult> LinkContextAsync(Guid id, Guid targetId, string type, bool remove,
        HttpRequest http, LacDbContext db, DakWorkflowService workflow, IDakAuthorizationService dakAuth,
        IMatterAuthorizationService matterAuth, IAccessControlService access, ICurrentUserContext user, CancellationToken ct)
    {
        if (!user.UserId.HasValue) return Results.Unauthorized();
        var userId = user.UserId.Value;
        if (!await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakEdit, userId, ct)) return Results.Forbid();
        if (!remove)
        {
            var allowed = type switch
            {
                "Matter" => await matterAuth.CanAccessMatterAsync(targetId, PermissionCodes.MatterView, userId, ct),
                "Award" => await access.CanAsync(PermissionCodes.AwardView, new AccessResourceContext(WorkstreamCode: WorkstreamCodes.Award), ct),
                "Village" => await access.CanAsync(PermissionCodes.VillageView, cancellationToken: ct),
                "Khasra" => await access.CanAsync(PermissionCodes.KhasraView, cancellationToken: ct),
                _ => false
            };
            if (!allowed) return Results.Forbid();
        }
        var linkId = Guid.NewGuid();
        var changedId = await workflow.MutateIntakeAsync(id, ReadRevision(http), userId,
            remove ? "ContextUnlinked" : "ContextLinked", async (lockedDak, c) =>
            {
                if (type == "Village" && lockedDak.VillageClassification != DakVillageClassification.Unclassified) throw new DakWorkflowException("Use the revision-guarded Village classification endpoint.", 409);
                if (type == "Matter" && !remove)
                {
                    var matter = await db.Matters.SingleOrDefaultAsync(x => x.Id == targetId && x.RecordStatus == RecordStatus.Active, c) ?? throw new DakWorkflowException("Matter is unavailable.", 403);
                    try { await DakMatterGuard.ValidateLinkAsync(db, id, matter, userId, c); } catch (MatterWorkflowException ex) { throw new DakWorkflowException(ex.Message, ex.StatusCode); }
                }
                return type switch
            {
                "Village" => await ChangeLinkAsync(db, db.DakVillageLinks.Where(l => l.DakId == id && (l.VillageId == targetId || remove && l.Id == targetId)),
                    () => new DakVillageLink { Id = linkId, DakId = id, VillageId = targetId },
                    () => db.Villages.AnyAsync(v => v.Id == targetId && v.RecordStatus == RecordStatus.Active, c), remove, c),
                "Award" => await ChangeLinkAsync(db, db.DakAwardLinks.Where(l => l.DakId == id && (l.AwardId == targetId || remove && l.Id == targetId)),
                    () => new DakAwardLink { Id = linkId, DakId = id, AwardId = targetId },
                    () => db.Awards.AnyAsync(v => v.Id == targetId && v.RecordStatus == RecordStatus.Active, c), remove, c),
                "Matter" => await ChangeLinkAsync(db, db.DakMatterLinks.Where(l => l.DakId == id && (l.MatterId == targetId || remove && l.Id == targetId)),
                    () => new DakMatterLink { Id = linkId, DakId = id, MatterId = targetId },
                    () => db.Matters.AnyAsync(v => v.Id == targetId && v.RecordStatus == RecordStatus.Active, c), remove, c),
                "Khasra" => await ChangeLinkAsync(db, db.DakKhasraLinks.Where(l => l.DakId == id && (l.KhasraId == targetId || remove && l.Id == targetId)),
                    () => new DakKhasraLink { Id = linkId, DakId = id, KhasraId = targetId },
                    () => db.Khasras.AnyAsync(v => v.Id == targetId && v.RecordStatus == RecordStatus.Active, c), remove, c),
                _ => throw new DakWorkflowException("Unknown context type.")
            };
            }, ct);
        if (remove) return Results.NoContent();
        return Results.Created($"/api/dak/{id}/links/{type.ToLowerInvariant()}s/{changedId}", new Dictionary<string, object>
        {
            ["id"] = changedId, ["linkId"] = changedId, [$"{type.ToLowerInvariant()}Id"] = targetId
        });
    }

    private static async Task<Guid> ChangeLinkAsync<T>(LacDbContext db, IQueryable<T> query, Func<T> create,
        Func<Task<bool>> targetExists, bool remove, CancellationToken ct) where T : OfficialRecord
    {
        var existing = await query.SingleOrDefaultAsync(l => l.RecordStatus == RecordStatus.Active, ct);
        if (remove)
        {
            if (existing is null) throw new DakWorkflowException("Link not found.", 404);
            existing.RecordStatus = RecordStatus.Archived;
            return existing.Id;
        }
        if (!await targetExists()) throw new DakWorkflowException("Target record does not exist or is inactive.");
        if (existing is not null) throw new DakWorkflowException("Target record is already actively linked.", 409);
        var link = create();
        db.Set<T>().Add(link);
        return link.Id;
    }
}
