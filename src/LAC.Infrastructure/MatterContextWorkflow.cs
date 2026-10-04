namespace LAC.Infrastructure;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public enum MatterContextKind { Award, Khasra, CourtCase, Dak }
public sealed record MatterContextLinkCommand([property: System.Text.Json.Serialization.JsonRequired] int ExpectedRevision, bool IsPrimary = false);
public sealed record MatterContextMutationResult(Guid Id, int Revision, bool Changed);

public sealed partial class MatterWorkflowService
{
    private static HashSet<Guid>? NormalizeContextIds(IReadOnlyList<Guid>? ids)
    {
        if (ids is null) return null;
        if (ids.Count > 250 || ids.Contains(Guid.Empty))
            throw new MatterWorkflowException("Context IDs must be non-empty; at most 250 per type.");
        return ids.ToHashSet();
    }

    private async Task<bool> CanViewContextTargetAsync(MatterContextKind kind, Guid id, Guid userId, CancellationToken ct)
    {
        // Court and Dak use their record-specific live assignment/workstream authorization.
        return kind switch
        {
            MatterContextKind.Award => await accessControl.CanAsync(PermissionCodes.AwardView,
                new AccessResourceContext(WorkstreamCode: WorkstreamCodes.Award), ct)
                && await db.Awards.AnyAsync(x => x.Id == id && x.RecordStatus == RecordStatus.Active, ct),
            MatterContextKind.Khasra => (await accessControl.CanAsync(PermissionCodes.KhasraView,
                    new AccessResourceContext(WorkstreamCode: WorkstreamCodes.LandRecords), ct)
                || await accessControl.CanAsync(PermissionCodes.KhasraView,
                    new AccessResourceContext(WorkstreamCode: WorkstreamCodes.Award), ct))
                && await db.Khasras.AnyAsync(x => x.Id == id && x.RecordStatus == RecordStatus.Active, ct),
            MatterContextKind.CourtCase => courtAuth is not null && await courtAuth.CanViewCourtCaseAsync(id, userId, ct),
            MatterContextKind.Dak => dakAuth is not null && await dakAuth.CanAccessDakAsync(id, PermissionCodes.DakView, userId, ct),
            _ => false
        };
    }

    private async Task ValidateContextTargetAsync(Matter matter, MatterContextKind kind, Guid id, Guid userId, bool linking, CancellationToken ct)
    {
        if (!await CanViewContextTargetAsync(kind, id, userId, ct))
            throw new MatterWorkflowException($"Context target is unavailable or unauthorized ({(kind == MatterContextKind.CourtCase ? PermissionCodes.CourtView : kind + ".View")} required).", 403);
        if (!linking) return;
        if (kind == MatterContextKind.Award && !await db.AwardVillages.AnyAsync(x => x.AwardId == id && x.VillageId == matter.VillageId, ct))
            throw new MatterWorkflowException("Award does not belong to the selected village.");
        if (kind == MatterContextKind.Khasra && !await db.Khasras.AnyAsync(x => x.Id == id && x.VillageId == matter.VillageId, ct))
            throw new MatterWorkflowException("Khasra does not belong to the selected village.");
    }

    private async Task AddContextEventAsync(Matter matter, MatterEventAction action, MatterContextKind kind, Guid? targetId,
        AppUser actor, CancellationToken ct, Guid? eventId = null)
    {
        var stored = await db.MatterEvents.Where(x => x.MatterId == matter.Id).MaxAsync(x => (int?)x.SequenceNumber, ct) ?? 0;
        var pending = db.ChangeTracker.Entries<MatterEvent>().Where(x => x.Entity.MatterId == matter.Id)
            .Select(x => x.Entity.SequenceNumber).DefaultIfEmpty(0).Max();
        db.MatterEvents.Add(new MatterEvent
        {
            Id = eventId ?? Guid.NewGuid(), MatterId = matter.Id, SequenceNumber = Math.Max(stored, pending) + 1,
            Action = action, ActionByUserId = actor.Id, ActionByDisplayNameSnapshot = actor.DisplayName,
            ActionAt = DateTimeOffset.UtcNow, ContextEntityType = kind.ToString(), ContextEntityId = targetId,
            WorkstreamIdSnapshot = matter.WorkstreamId, WorkstreamNameSnapshot = matter.Workstream?.Name
        });
    }

    private static MatterEventAction LinkAction(MatterContextKind kind, bool link) => (kind, link) switch
    {
        (MatterContextKind.Award, true) => MatterEventAction.AwardLinked,
        (MatterContextKind.Award, false) => MatterEventAction.AwardUnlinked,
        (MatterContextKind.Khasra, true) => MatterEventAction.KhasraLinked,
        (MatterContextKind.Khasra, false) => MatterEventAction.KhasraUnlinked,
        (MatterContextKind.CourtCase, true) => MatterEventAction.CourtCaseLinked,
        (MatterContextKind.CourtCase, false) => MatterEventAction.CourtCaseUnlinked,
        (MatterContextKind.Dak, true) => MatterEventAction.DakLinked,
        _ => MatterEventAction.DakUnlinked
    };

    private async Task<HashSet<Guid>> GetContextIdsAsync(Guid matterId, MatterContextKind kind, CancellationToken ct) => kind switch
    {
        MatterContextKind.Award => (await db.MatterAwards.Where(x => x.MatterId == matterId).Select(x => x.AwardId).ToListAsync(ct)).ToHashSet(),
        MatterContextKind.Khasra => (await db.MatterKhasras.Where(x => x.MatterId == matterId).Select(x => x.KhasraId).ToListAsync(ct)).ToHashSet(),
        MatterContextKind.CourtCase => (await db.CourtCaseMatters.Where(x => x.MatterId == matterId && x.RecordStatus == RecordStatus.Active).Select(x => x.CourtCaseId).ToListAsync(ct)).ToHashSet(),
        _ => (await db.DakMatterLinks.Where(x => x.MatterId == matterId && x.RecordStatus == RecordStatus.Active).Select(x => x.DakId).ToListAsync(ct)).ToHashSet()
    };

    private async Task ChangeContextPairAsync(Matter matter, MatterContextKind kind, Guid id, bool link, AppUser actor, CancellationToken ct, Guid? receiptId = null)
    {
        switch (kind)
        {
            case MatterContextKind.Award:
                if (link) db.MatterAwards.Add(new MatterAward { MatterId = matter.Id, AwardId = id, IsPrimary = false });
                else db.MatterAwards.Remove(await db.MatterAwards.SingleAsync(x => x.MatterId == matter.Id && x.AwardId == id, ct));
                break;
            case MatterContextKind.Khasra:
                if (link) db.MatterKhasras.Add(new MatterKhasra { MatterId = matter.Id, KhasraId = id });
                else db.MatterKhasras.Remove(await db.MatterKhasras.SingleAsync(x => x.MatterId == matter.Id && x.KhasraId == id, ct));
                break;
            case MatterContextKind.CourtCase:
                // Match the Court-side workflow: reuse the canonical pair on relink.
                if (link)
                {
                    var pair = await db.CourtCaseMatters.FirstOrDefaultAsync(x => x.MatterId == matter.Id && x.CourtCaseId == id, ct);
                    if (pair is null) db.CourtCaseMatters.Add(new CourtCaseMatter { MatterId = matter.Id, CourtCaseId = id });
                    else pair.RecordStatus = RecordStatus.Active;
                }
                else (await db.CourtCaseMatters.SingleAsync(x => x.MatterId == matter.Id && x.CourtCaseId == id && x.RecordStatus == RecordStatus.Active, ct)).RecordStatus = RecordStatus.Archived;
                break;
            case MatterContextKind.Dak:
                if (link) db.DakMatterLinks.Add(new DakMatterLink { MatterId = matter.Id, DakId = id });
                else (await db.DakMatterLinks.SingleAsync(x => x.MatterId == matter.Id && x.DakId == id && x.RecordStatus == RecordStatus.Active, ct)).RecordStatus = RecordStatus.Archived;
                break;
        }
        await AddContextEventAsync(matter, LinkAction(kind, link), kind, id, actor, ct, receiptId);
    }

    private async Task ApplyContextSetsAsync(Matter matter, IReadOnlyList<Guid>? awards, IReadOnlyList<Guid>? khasras,
        IReadOnlyList<Guid>? courts, Guid? primaryAwardId, Guid? legacyAwardId, AppUser actor, CancellationToken ct, Guid? receiptId = null)
    {
        var desired = new Dictionary<MatterContextKind, HashSet<Guid>?>
        {
            [MatterContextKind.Award] = NormalizeContextIds(awards),
            [MatterContextKind.Khasra] = NormalizeContextIds(khasras),
            [MatterContextKind.CourtCase] = NormalizeContextIds(courts)
        };
        var awardLinks = await db.MatterAwards.Where(x => x.MatterId == matter.Id).ToListAsync(ct);
        var oldPrimary = awardLinks.FirstOrDefault(x => x.IsPrimary)?.AwardId;
        if (legacyAwardId.HasValue)
        {
            if (awards is not null || primaryAwardId.HasValue)
                throw new MatterWorkflowException("Use either legacy awardId or awardIds/primaryAwardId.");
            desired[MatterContextKind.Award] = awardLinks.Select(x => x.AwardId).ToHashSet();
            if (oldPrimary.HasValue) desired[MatterContextKind.Award]!.Remove(oldPrimary.Value);
            if (legacyAwardId.Value != Guid.Empty) desired[MatterContextKind.Award]!.Add(legacyAwardId.Value);
            primaryAwardId = legacyAwardId;
        }
        var newPrimary = primaryAwardId.HasValue ? (primaryAwardId == Guid.Empty ? null : primaryAwardId) : oldPrimary;
        var finalAwards = desired[MatterContextKind.Award] ?? awardLinks.Select(x => x.AwardId).ToHashSet();
        if (primaryAwardId.HasValue && newPrimary.HasValue && !finalAwards.Contains(newPrimary.Value))
            throw new MatterWorkflowException("Primary Award must be explicitly linked in awardIds.");
        if (newPrimary.HasValue && !finalAwards.Contains(newPrimary.Value)) newPrimary = null;

        // Validate the complete delta before writing anything, including removal of hidden targets.
        var deltas = new List<(MatterContextKind Kind, Guid Id, bool Link)>();
        foreach (var (kind, ids) in desired)
        {
            if (ids is null) continue;
            var existing = await GetContextIdsAsync(matter.Id, kind, ct);
            foreach (var id in ids) await ValidateContextTargetAsync(matter, kind, id, actor.Id, true, ct);
            foreach (var id in existing.Except(ids)) await ValidateContextTargetAsync(matter, kind, id, actor.Id, false, ct);
            deltas.AddRange(existing.Except(ids).Order().Select(id => (kind, id, false)));
            deltas.AddRange(ids.Except(existing).Order().Select(id => (kind, id, true)));
        }
        if (newPrimary != oldPrimary)
        {
            if (oldPrimary.HasValue) await ValidateContextTargetAsync(matter, MatterContextKind.Award, oldPrimary.Value, actor.Id, false, ct);
            if (newPrimary.HasValue) await ValidateContextTargetAsync(matter, MatterContextKind.Award, newPrimary.Value, actor.Id, true, ct);
            foreach (var row in awardLinks) row.IsPrimary = false;
            // Flush demotion inside the enclosing transaction before promotion for the unique index.
            await db.SaveChangesAsync(ct);
        }
        for (var i = 0; i < deltas.Count; i++)
        {
            var (kind, id, link) = deltas[i];
            await ChangeContextPairAsync(matter, kind, id, link, actor, ct, i == 0 ? receiptId : null);
        }
        if (newPrimary != oldPrimary)
        {
            if (newPrimary.HasValue)
            {
                var row = db.ChangeTracker.Entries<MatterAward>()
                    .FirstOrDefault(x => x.State != EntityState.Deleted && x.Entity.MatterId == matter.Id && x.Entity.AwardId == newPrimary)?.Entity
                    ?? await db.MatterAwards.SingleAsync(x => x.MatterId == matter.Id && x.AwardId == newPrimary, ct);
                row.IsPrimary = true;
            }
            await AddContextEventAsync(matter, MatterEventAction.PrimaryAwardChanged, MatterContextKind.Award, newPrimary, actor, ct, deltas.Count == 0 ? receiptId : null);
        }
    }

    public async Task<MatterContextMutationResult> ChangeContextLinkAsync(Guid matterId, MatterContextKind kind, Guid targetId,
        bool linking, MatterContextLinkCommand cmd, Guid userId, CancellationToken ct = default)
    {
        if (targetId == Guid.Empty || cmd.ExpectedRevision < 0 || (kind != MatterContextKind.Award && cmd.IsPrimary))
            throw new MatterWorkflowException("Invalid context link request.");
        if (!await matterAuth.CanAccessMatterAsync(matterId, PermissionCodes.MatterEdit, userId, ct))
            throw new MatterWorkflowException("Matter is unavailable or unauthorized.", 403);
        var actor = await db.AppUsers.AsNoTracking().SingleAsync(x => x.Id == userId, ct);
        var receiptId = Guid.NewGuid();
        try
        {
            return await ExecuteWorkflowTransactionAsync(async opCt =>
            {
                db.ChangeTracker.Clear();
                var matter = await LockMatterAsync(matterId, opCt);
                if (matter.RecordStatus != RecordStatus.Active || matter.Revision != cmd.ExpectedRevision)
                    throw new MatterWorkflowException("Matter changed; reload before updating context.", 409);
                await ValidateContextTargetAsync(matter, kind, targetId, userId, linking, opCt);
                var existing = await GetContextIdsAsync(matterId, kind, opCt);
                var primaryChange = kind == MatterContextKind.Award && linking && cmd.IsPrimary
                    && !await db.MatterAwards.AnyAsync(x => x.MatterId == matterId && x.AwardId == targetId && x.IsPrimary, opCt);
                var changed = existing.Contains(targetId) != linking || primaryChange;
                if (!changed) return new MatterContextMutationResult(matterId, matter.Revision, false);
                if (kind == MatterContextKind.Award)
                {
                    if (linking) existing.Add(targetId); else existing.Remove(targetId);
                    await ApplyContextSetsAsync(matter, existing.ToList(), null, null,
                        primaryChange ? targetId : null, null, actor, opCt, receiptId);
                }
                else
                {
                    await ChangeContextPairAsync(matter, kind, targetId, linking, actor, opCt, receiptId);
                }
                matter.Revision++;
                matter.UpdatedAt = DateTimeOffset.UtcNow;
                matter.UpdatedBy = actor.DisplayName;
                await db.SaveChangesAsync(opCt);
                return new MatterContextMutationResult(matterId, matter.Revision, true);
            }, verifyCt => db.MatterEvents.AsNoTracking().AnyAsync(x => x.Id == receiptId, verifyCt), ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new MatterWorkflowException("Matter changed; reload before updating context.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw new MatterWorkflowException("Context link changed concurrently; reload before updating.", 409);
        }
    }
}
