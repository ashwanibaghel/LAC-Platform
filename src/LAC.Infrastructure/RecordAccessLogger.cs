namespace LAC.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed record RecordAccessCommand(
    Guid ActorUserId,
    RecordAccessAction Action,
    Guid DocumentId,
    string? ContextEntityType = null,
    Guid? ContextEntityId = null,
    Guid? WorkstreamId = null,
    Guid? OfficeDeskId = null,
    string? DocumentTitleSnapshot = null,
    string? ActorDisplayNameSnapshot = null
);

public interface IRecordAccessLogger
{
    Task LogAccessAsync(RecordAccessCommand cmd, CancellationToken ct = default);
}

public sealed class RecordAccessLogger(LacDbContext db) : IRecordAccessLogger
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> KeyLocks = new();

    public async Task LogAccessAsync(RecordAccessCommand cmd, CancellationToken ct = default)
    {
        // 1. Resolve Actor Display Name if missing
        var actorName = cmd.ActorDisplayNameSnapshot;
        if (string.IsNullOrWhiteSpace(actorName))
        {
            actorName = await db.AppUsers.AsNoTracking()
                .Where(u => u.Id == cmd.ActorUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct) ?? "Unknown User";
        }

        // 2. Resolve Document Title if missing
        var docTitle = cmd.DocumentTitleSnapshot;
        if (string.IsNullOrWhiteSpace(docTitle))
        {
            docTitle = await db.Documents.AsNoTracking()
                .Where(d => d.Id == cmd.DocumentId)
                .Select(d => d.OriginalFileName)
                .FirstOrDefaultAsync(ct);
        }

        // 3. Resolve context routing if missing
        var workstreamId = cmd.WorkstreamId;
        var deskId = cmd.OfficeDeskId;

        if (cmd.ContextEntityId.HasValue && !string.IsNullOrWhiteSpace(cmd.ContextEntityType))
        {
            var cType = cmd.ContextEntityType.Trim().ToLowerInvariant();
            if (cType == "dak")
            {
                if (!deskId.HasValue || !workstreamId.HasValue)
                {
                    var dakInfo = await db.Daks.AsNoTracking()
                        .Where(d => d.Id == cmd.ContextEntityId.Value)
                        .Select(d => new { d.WorkstreamId, DeskId = d.CurrentAssignment != null ? (Guid?)d.CurrentAssignment.OfficeDeskId : null })
                        .FirstOrDefaultAsync(ct);
                    if (dakInfo != null)
                    {
                        workstreamId ??= dakInfo.WorkstreamId;
                        deskId ??= dakInfo.DeskId;
                    }
                }
            }
            else if (cType == "matter")
            {
                if (!workstreamId.HasValue)
                {
                    workstreamId = await db.Matters.AsNoTracking()
                        .Where(m => m.Id == cmd.ContextEntityId.Value)
                        .Select(m => m.WorkstreamId)
                        .FirstOrDefaultAsync(ct);
                }
            }
            else if (cType == "outward")
            {
                if (!deskId.HasValue || !workstreamId.HasValue)
                {
                    var outInfo = await db.Outwards.AsNoTracking()
                        .Where(o => o.Id == cmd.ContextEntityId.Value)
                        .Select(o => new { o.IssuingDeskId, o.WorkstreamId })
                        .FirstOrDefaultAsync(ct);
                    if (outInfo != null)
                    {
                        deskId ??= outInfo.IssuingDeskId;
                        workstreamId ??= outInfo.WorkstreamId;
                    }
                }
            }
            else if (cType == "workitem")
            {
                if (!deskId.HasValue || !workstreamId.HasValue)
                {
                    var wiInfo = await db.WorkItems.AsNoTracking()
                        .Where(w => w.Id == cmd.ContextEntityId.Value)
                        .Select(w => new
                        {
                            w.WorkstreamId,
                            DeskId = w.Assignments.Where(a => a.IsActive && a.RecordStatus == RecordStatus.Active).Select(a => (Guid?)a.OfficeDeskId).FirstOrDefault()
                        })
                        .FirstOrDefaultAsync(ct);
                    if (wiInfo != null)
                    {
                        deskId ??= wiInfo.DeskId;
                        workstreamId ??= wiInfo.WorkstreamId;
                    }
                }
            }
        }

        // 4. Compute DeduplicationKey
        string? deduplicationKey = null;
        if (cmd.Action is RecordAccessAction.Opened or RecordAccessAction.Previewed)
        {
            var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 300; // 5-minute bucket
            deduplicationKey = $"{cmd.ActorUserId}:{cmd.DocumentId}:{cmd.ContextEntityType ?? "None"}:{cmd.ContextEntityId?.ToString() ?? "None"}:{bucket}";
        }

        SemaphoreSlim? sem = null;
        if (deduplicationKey != null)
        {
            sem = KeyLocks.GetOrAdd(deduplicationKey, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(ct);
        }

        try
        {
            if (deduplicationKey != null)
            {
                var alreadyLogged = await db.RecordAccessEvents.AsNoTracking()
                    .AnyAsync(e => e.DeduplicationKey == deduplicationKey, ct);
                if (alreadyLogged)
                {
                    return;
                }
            }

            string? workstreamName = null;
            if (workstreamId.HasValue)
            {
                workstreamName = await db.Workstreams.AsNoTracking()
                    .Where(w => w.Id == workstreamId.Value)
                    .Select(w => w.Name)
                    .FirstOrDefaultAsync(ct);
            }

            string? deskName = null;
            if (deskId.HasValue)
            {
                deskName = await db.OfficeDesks.AsNoTracking()
                    .Where(d => d.Id == deskId.Value)
                    .Select(d => d.Name)
                    .FirstOrDefaultAsync(ct);
            }

            var ev = new RecordAccessEvent
            {
                Id = Guid.NewGuid(),
                ActorUserId = cmd.ActorUserId,
                ActorDisplayNameSnapshot = actorName,
                OccurredAt = DateTimeOffset.UtcNow,
                Action = cmd.Action,
                DocumentId = cmd.DocumentId,
                ContextEntityType = cmd.ContextEntityType,
                ContextEntityId = cmd.ContextEntityId,
                WorkstreamId = workstreamId,
                WorkstreamNameSnapshot = workstreamName,
                OfficeDeskId = deskId,
                OfficeDeskNameSnapshot = deskName,
                DocumentTitleSnapshot = docTitle,
                DeduplicationKey = deduplicationKey
            };

            db.RecordAccessEvents.Add(ev);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                if (deduplicationKey != null)
                {
                    db.Entry(ev).State = EntityState.Detached;
                    var exists = await db.RecordAccessEvents.AsNoTracking()
                        .AnyAsync(e => e.DeduplicationKey == deduplicationKey, CancellationToken.None);
                    if (exists)
                    {
                        return;
                    }
                }
                throw;
            }
        }
        finally
        {
            if (sem != null)
            {
                sem.Release();
                if (KeyLocks.Count > 1000)
                {
                    KeyLocks.Clear();
                }
            }
        }
    }
}

