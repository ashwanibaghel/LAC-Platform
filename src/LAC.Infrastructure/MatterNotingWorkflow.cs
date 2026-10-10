namespace LAC.Infrastructure;

using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

public sealed record VillageClassificationCommand(string Classification, IReadOnlyList<Guid> VillageIds, [property: System.Text.Json.Serialization.JsonRequired] int ExpectedRevision);
public sealed record MatterWorkspaceCommand(Guid VillageId, Guid WorkstreamId, Guid? ExistingMatterId, string? Title, string? MatterType, [property: System.Text.Json.Serialization.JsonRequired] int ExpectedRevision);
public sealed record NativeMatterCommand(Guid WorkstreamId, string Title, string? MatterType);
public sealed record MatterWorkspaceResult(Guid MatterId, Guid? DakId, int? DakRevision, int MatterRevision);
public sealed record SaveWorkingNoteCommand(string ContentJson, Guid? SourceDakId);
public sealed record SubmitWorkingNoteCommand(Guid? SourceDakId);
public sealed record SendWorkingNoteCommand(Guid SourceDakId, [property: System.Text.Json.Serialization.JsonRequired] int ExpectedDakRevision, Guid ToDeskId, Guid ToUserId, string Action,
    bool IncludesPhysicalOriginal, string? Instructions, string? Remarks);
public sealed record SendWorkingNoteResult(MatterOfficialNote Note, DakCommandResult Dak);
public sealed record CreateRemarkCommand(NoteAnchor Anchor, string Text);
public sealed record ChangeRemarkCommand(string State, [property: System.Text.Json.Serialization.JsonRequired] int ExpectedRevision);
public sealed record CreateAnnotationCommand(int Page, PageRegion Region, string? Quote, string? Text);
public sealed record CreateCitationCommand(NoteAnchor Anchor, Guid DocumentId, int? Page, PageRegion? Region, [property: System.Text.Json.Serialization.JsonRequired] int ExpectedRevision);

public sealed class MatterNotingWorkflow(LacDbContext db, IMatterAuthorizationService auth, DakWorkflowService dakWorkflow, IDocumentStorage storage, ICourtAuthorizationService villageAuth)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Serialize(object? value) => JsonSerializer.Serialize(value, Json);

    private async Task ScopeAsync(Guid matterId, Guid actor, string permission, CancellationToken ct)
    {
        if (!await auth.CanAccessMatterAsync(matterId, PermissionCodes.MatterView, actor, ct) ||
            !await auth.CanAccessMatterAsync(matterId, permission, actor, ct))
            throw new MatterWorkflowException("Matter is unavailable or outside your authorized scope.", 403);
        var matter = await db.Matters.AsNoTracking().SingleAsync(x => x.Id == matterId, ct);
        if (matter.WorkstreamId.HasValue && !await db.Workstreams.AnyAsync(x => x.Id == matter.WorkstreamId && x.IsActive && x.RecordStatus == RecordStatus.Active, ct))
            throw new MatterWorkflowException("Matter workstream is inactive.", 403);
        await AllocationAsync(actor, matter.WorkstreamId, matter.VillageId, ct);
    }

    private async Task AllocationAsync(Guid actor, Guid? workstream, Guid village, CancellationToken ct)
    {
        var user = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor && x.IsActive && x.RecordStatus == RecordStatus.Active, ct);
        if (user is null || (user.OfficeAccessManaged || user.SupervisingOfficerId.HasValue) &&
            !await new WorkAllocationService(db, TimeProvider.System).CanWorkAsync(actor, workstream.HasValue ? null : OperationalWorkKind.General, workstream, [village], ct))
            throw new MatterWorkflowException("Current Village work allocation is required.", 403);
    }

    private async Task<Matter> EditorAsync(Guid matterId, Guid actor, Guid? dakId, CancellationToken ct)
    {
        await ScopeAsync(matterId, actor, PermissionCodes.DraftEdit, ct);
        if (!await auth.CanAccessMatterDraftCapabilityAsync(matterId, PermissionCodes.DraftCreate, actor, ct)) throw new MatterWorkflowException("Draft creation permission is required.", 403);
        var matter = await db.Matters.SingleAsync(x => x.Id == matterId, ct);
        if (matter.Status != "Open") throw new MatterWorkflowException("Matter is not open for noting.", 409);
        if (dakId.HasValue)
        {
            if (!await db.DakMatterLinks.AnyAsync(x => x.DakId == dakId && x.MatterId == matterId && x.RecordStatus == RecordStatus.Active, ct))
                throw new MatterWorkflowException("Controlling Dak must be explicitly linked to this Matter.", 403);
            var dak = await DakMatterGuard.ConfirmedHolderAsync(db, dakId.Value, actor, ct);
            await DakMatterGuard.ValidateVillageAsync(db, dak, matter.VillageId, ct);
        }
        else if (matter.NativeOwnerUserId != actor || await db.DakMatterLinks.AnyAsync(x => x.MatterId == matterId && x.RecordStatus == RecordStatus.Active &&
            x.Dak.RecordStatus == RecordStatus.Active && x.Dak.Status != DakStatus.Resolved && x.Dak.Status != DakStatus.Disposed && x.Dak.Status != DakStatus.Cancelled, ct))
            throw new MatterWorkflowException("Select an explicit active controlling Dak, or use a native Matter you own.", 409);
        return matter;
    }

    private async Task DraftPermissionAsync(Guid matter, Guid actor, CancellationToken ct)
    {
        await ScopeAsync(matter, actor, PermissionCodes.DraftEdit, ct);
        if (!await auth.CanAccessMatterDraftCapabilityAsync(matter, PermissionCodes.DraftCreate, actor, ct))
            throw new MatterWorkflowException("Draft creation permission is required.", 403);
    }

    private async Task LockAsync(Guid? matter, Guid? dak, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return;
        // Consistent order: Dak first, then Matter. Recheck permissions after acquiring locks.
        if (dak.HasValue) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Daks\" WHERE \"Id\" = {dak.Value} FOR UPDATE", ct);
        if (matter.HasValue) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Matters\" WHERE \"Id\" = {matter.Value} FOR UPDATE", ct);
    }

    private async Task<T> CommandAsync<T>(Guid actor, Guid key, object payload, Guid? matter, Guid? dak,
        Func<CancellationToken, Task> authorize, Func<CancellationToken, Task<T>> apply, CancellationToken ct)
    {
        if (key == Guid.Empty) throw new MatterWorkflowException("A UUID Idempotency-Key is required.", 428);
        var hash = NotingText.Hash(Serialize(payload));
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
                if (transaction is not null)
                {
                    var lockKey = BitConverter.ToInt64(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(actor.ToString() + key)), 0);
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
                }
                await LockAsync(matter, dak, ct);
                await authorize(ct);
                var receipt = await db.MatterWorkflowReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.ActorUserId == actor && x.RequestId == key, ct);
                if (receipt is not null)
                {
                    if (receipt.PayloadHash != hash) throw new MatterWorkflowException("Idempotency-Key was used with a different command.", 409);
                    return JsonSerializer.Deserialize<T>(receipt.ResultJson, Json)!;
                }
                var result = await apply(ct);
                db.MatterWorkflowReceipts.Add(new MatterWorkflowReceipt { ActorUserId = actor, RequestId = key, PayloadHash = hash, ResultJson = Serialize(result) });
                await db.SaveChangesAsync(ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
                return result;
            });
        }
        catch (DbUpdateConcurrencyException) { throw new MatterWorkflowException("Record changed; refresh before retrying.", 409); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" }) { throw new MatterWorkflowException("A competing command already committed.", 409); }
    }

    private async Task AuditAsync(Guid matter, Guid actor, string action, Guid context, CancellationToken ct)
    {
        var user = await db.AppUsers.AsNoTracking().SingleAsync(x => x.Id == actor, ct);
        var sequence = await db.MatterEvents.Where(x => x.MatterId == matter).MaxAsync(x => (int?)x.SequenceNumber, ct) ?? 0;
        var pending = db.ChangeTracker.Entries<MatterEvent>().Where(x => x.Entity.MatterId == matter).Select(x => x.Entity.SequenceNumber).DefaultIfEmpty(0).Max();
        db.MatterEvents.Add(new MatterEvent { MatterId = matter, SequenceNumber = Math.Max(sequence, pending) + 1, Action = MatterEventAction.NotingWorkflow,
            ActionByUserId = actor, ActionByDisplayNameSnapshot = user.DisplayName, ContextEntityType = action, ContextEntityId = context });
        db.AuditLogs.Add(new AuditLog { ActorUserId = actor, EntityType = "MatterNoting", EntityId = context, Action = action, ChangedBy = actor.ToString(), ChangedAt = DateTimeOffset.UtcNow });
    }

    public Task<object> ClassifyAsync(Guid id, VillageClassificationCommand cmd, Guid actor, Guid key, CancellationToken ct) =>
        CommandAsync<object>(actor, key, new { action = "Classify", id, cmd }, null, id,
            async c => { if (!await new DakAuthorizationService(db).CanAccessDakAsync(id, PermissionCodes.DakEdit, actor, c)) throw new MatterWorkflowException("Dak classification is unauthorized.", 403); },
            async c =>
            {
                if (!Enum.TryParse<DakVillageClassification>(cmd.Classification, false, out var classification) || !Enum.IsDefined(classification) || cmd.VillageIds is null ||
                    cmd.VillageIds.Count > 100 || cmd.VillageIds.Distinct().Count() != cmd.VillageIds.Count || cmd.VillageIds.Contains(Guid.Empty)) throw new MatterWorkflowException("Invalid Village classification.");
                var count = cmd.VillageIds.Count;
                if (classification == DakVillageClassification.VillageSpecific ? count != 1 : classification == DakVillageClassification.MultiVillage ? count < 2 : count != 0)
                    throw new MatterWorkflowException("Classification does not match explicit Village count.");
                var dak = await db.Daks.SingleAsync(x => x.Id == id, c);
                if (dak.Revision != cmd.ExpectedRevision || dak.RecordStatus != RecordStatus.Active || dak.Status is DakStatus.Resolved or DakStatus.Cancelled or DakStatus.Disposed) throw new MatterWorkflowException("Dak is stale or completed.", 409);
                if (await db.Villages.CountAsync(x => cmd.VillageIds.Contains(x.Id) && x.RecordStatus == RecordStatus.Active, c) != count) throw new MatterWorkflowException("Use active Villages only.");
                foreach (var village in cmd.VillageIds) await AllocationAsync(actor, dak.WorkstreamId, village, c);
                if (await db.DakMatterLinks.AnyAsync(x => x.DakId == id && !cmd.VillageIds.Contains(x.Matter.VillageId), c)) throw new MatterWorkflowException("Classification would contradict historical Matter links.", 409);
                var old = await db.DakVillageLinks.Where(x => x.DakId == id && x.RecordStatus == RecordStatus.Active).ToListAsync(c);
                foreach (var row in old.Where(x => !cmd.VillageIds.Contains(x.VillageId))) row.RecordStatus = RecordStatus.Archived;
                foreach (var village in cmd.VillageIds.Where(v => old.All(x => x.VillageId != v))) db.DakVillageLinks.Add(new DakVillageLink { DakId = id, VillageId = village });
                dak.VillageClassification = classification; dak.Revision++;
                db.AuditLogs.Add(new AuditLog { ActorUserId = actor, EntityType = "Dak", EntityId = id, Action = "VillageClassified", NewValues = Serialize(cmd), ChangedAt = DateTimeOffset.UtcNow });
                return new { dak.Id, dak.Revision, classification = classification.ToString(), villageIds = cmd.VillageIds };
            }, ct);

    public async Task<object> OptionsAsync(Guid id, Guid village, string? search, Guid actor, CancellationToken ct)
    {
        var dak = await DakMatterGuard.ConfirmedHolderAsync(db, id, actor, ct);
        await DakMatterGuard.ValidateVillageAsync(db, dak, village, ct);
        var list = await auth.AuthorizeListQueryAsync(db.Matters.AsNoTracking().Where(x => x.VillageId == village), PermissionCodes.MatterView, actor, ct: ct);
        var query = list.Query;
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Title.Contains(search));
        var rows = new List<object>();
        foreach (var m in await query.OrderBy(x => x.Title).Take(100).ToListAsync(ct))
            try { await ScopeAsync(m.Id, actor, PermissionCodes.MatterEdit, ct); rows.Add(new { m.Id, m.Title, m.Revision }); } catch (MatterWorkflowException ex) when (ex.StatusCode == 403) { }
        return new { dak.Subject, villageId = village, dakRevision = dak.Revision, matters = rows };
    }

    private async Task<Matter> NewMatterAsync(Guid village, Guid workstream, string? title, string? type, Guid actor, bool native, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 1000 || type?.Length > 100) throw new MatterWorkflowException("Valid title (up to 1000 characters) is required.");
        if (!await db.Villages.AnyAsync(x => x.Id == village && x.RecordStatus == RecordStatus.Active, ct)) throw new MatterWorkflowException("Village is unavailable.", 403);
        if (!await villageAuth.CanAccessVillageAsync(village, actor, ct)) throw new MatterWorkflowException("Village is outside your authorized scope.", 403);
        if (!await auth.CanCreateMatterInWorkstreamAsync(workstream, actor, ct)) throw new MatterWorkflowException("Matter creation is unauthorized.", 403);
        await AllocationAsync(actor, workstream, village, ct);
        var matter = new Matter { VillageId = village, WorkstreamId = workstream, Title = title.Trim(), MatterType = string.IsNullOrWhiteSpace(type) ? "General" : type.Trim(), NativeOwnerUserId = native ? actor : null };
        db.Matters.Add(matter);
        await AuditAsync(matter.Id, actor, "Created", matter.Id, ct);
        return matter;
    }

    public Task<MatterWorkspaceResult> NativeAsync(Guid village, NativeMatterCommand cmd, Guid actor, Guid key, CancellationToken ct) =>
        CommandAsync(actor, key, new { action = "NativeCreate", village, cmd }, null, null,
            async c => { if (!await auth.CanCreateMatterInWorkstreamAsync(cmd.WorkstreamId, actor, c) || !await villageAuth.CanAccessVillageAsync(village, actor, c)) throw new MatterWorkflowException("Matter creation or Village access is unauthorized.", 403); await AllocationAsync(actor, cmd.WorkstreamId, village, c); },
            async c => { var m = await NewMatterAsync(village, cmd.WorkstreamId, cmd.Title, cmd.MatterType, actor, true, c); return new MatterWorkspaceResult(m.Id, null, null, m.Revision); }, ct);

    public Task<MatterWorkspaceResult> WorkspaceAsync(Guid id, MatterWorkspaceCommand cmd, Guid actor, Guid key, CancellationToken ct) =>
        CommandAsync(actor, key, new { action = "DakWorkspace", id, cmd }, cmd.ExistingMatterId, id,
            async c =>
            {
                if (!await new DakAuthorizationService(db).CanAccessDakAsync(id, PermissionCodes.DakView, actor, c)) throw new MatterWorkflowException("Dak is unavailable.", 403);
                if (cmd.ExistingMatterId.HasValue) await ScopeAsync(cmd.ExistingMatterId.Value, actor, PermissionCodes.MatterEdit, c);
                else if (!await auth.CanCreateMatterInWorkstreamAsync(cmd.WorkstreamId, actor, c) || !await villageAuth.CanAccessVillageAsync(cmd.VillageId, actor, c)) throw new MatterWorkflowException("Matter creation or Village access is unauthorized.", 403);
                if (!cmd.ExistingMatterId.HasValue) await AllocationAsync(actor, cmd.WorkstreamId, cmd.VillageId, c);
            }, async c =>
            {
                var dak = await DakMatterGuard.ConfirmedHolderAsync(db, id, actor, c);
                await DakMatterGuard.ValidateVillageAsync(db, dak, cmd.VillageId, c);
                if (dak.Revision != cmd.ExpectedRevision) throw new MatterWorkflowException("Dak changed; refresh.", 409);
                var m = cmd.ExistingMatterId.HasValue ? await db.Matters.SingleAsync(x => x.Id == cmd.ExistingMatterId, c)
                    : await NewMatterAsync(cmd.VillageId, cmd.WorkstreamId, cmd.Title ?? dak.Subject, cmd.MatterType, actor, false, c);
                if (m.VillageId != cmd.VillageId) throw new MatterWorkflowException("Matter belongs to a different Village.", 409);
                if (!await db.DakMatterLinks.AnyAsync(x => x.DakId == id && x.MatterId == m.Id && x.RecordStatus == RecordStatus.Active, c))
                {
                    db.DakMatterLinks.Add(new DakMatterLink { DakId = id, MatterId = m.Id });
                    m.Revision++; dak.Revision++; await AuditAsync(m.Id, actor, "DakLinked", id, c);
                }
                return new MatterWorkspaceResult(m.Id, id, dak.Revision, m.Revision);
            }, ct);

    public async Task<MatterWorkingNote?> DraftAsync(Guid id, Guid actor, CancellationToken ct)
    {
        await ScopeAsync(id, actor, PermissionCodes.DraftView, ct);
        return await db.MatterWorkingNotes.AsNoTracking().SingleOrDefaultAsync(x => x.MatterId == id && x.AuthorUserId == actor, ct);
    }

    private async Task<MatterWorkingNote> RequireDraftAsync(Guid id, Guid actor, int revision, Guid? source, CancellationToken ct)
    {
        var draft = await db.MatterWorkingNotes.SingleOrDefaultAsync(x => x.MatterId == id && x.AuthorUserId == actor, ct);
        if (draft is null || draft.Revision != revision || draft.SourceDakId != source || string.IsNullOrWhiteSpace(draft.CanonicalText)) throw new MatterWorkflowException("Flush the current draft and use its saved revision/source before submitting.", 409);
        return draft;
    }

    public Task<MatterWorkingNote> SaveAsync(Guid id, SaveWorkingNoteCommand cmd, int revision, Guid actor, Guid key, CancellationToken ct) =>
        CommandAsync(actor, key, new { action = "Autosave", id, cmd, revision }, id, cmd.SourceDakId,
            c => DraftPermissionAsync(id, actor, c), async c =>
            {
                await EditorAsync(id, actor, cmd.SourceDakId, c);
                var text = NotingText.Canonicalize(cmd.ContentJson);
                var draft = await db.MatterWorkingNotes.SingleOrDefaultAsync(x => x.MatterId == id && x.AuthorUserId == actor, c);
                if ((draft?.Revision ?? 0) != revision || revision < 0) throw new MatterWorkflowException("Draft changed; recover saved text before retrying.", 409);
                if (draft is null) { draft = new MatterWorkingNote { MatterId = id, AuthorUserId = actor }; db.MatterWorkingNotes.Add(draft); }
                if (draft.CanonicalText != text) draft.CitationsJson = "[]"; // Never carry stale anchors through edits.
                draft.ContentJson = cmd.ContentJson; draft.CanonicalText = text; draft.SourceDakId = cmd.SourceDakId; draft.Revision++; draft.UpdatedAt = DateTimeOffset.UtcNow;
                await AuditAsync(id, actor, "DraftSaved", draft.Id, c);
                return draft;
            }, ct);

    private async Task<MatterOfficialNote> FinalizeAsync(Guid id, int revision, Guid? source, Guid actor, CancellationToken ct)
    {
        var matter = await EditorAsync(id, actor, source, ct);
        var draft = await RequireDraftAsync(id, actor, revision, source, ct);
        var citations = JsonSerializer.Deserialize<List<NoteCitation>>(draft.CitationsJson, Json)!;
        foreach (var citation in citations)
        {
            NotingText.ValidateAnchor(draft.CanonicalText, citation.Anchor);
            var document = await DocumentAsync(id, citation.DocumentId, actor, ct);
            if (document.Version != citation.DocumentVersion || document.Sha256Hash != citation.DocumentHash) throw new MatterWorkflowException("Cited document changed; review citation.", 409);
        }
        var user = await db.AppUsers.AsNoTracking().Include(x => x.Designation).SingleAsync(x => x.Id == actor, ct);
        Guid? desk = source.HasValue ? (await db.DakAssignments.AsNoTracking().SingleAsync(x => x.DakId == source, ct)).OfficeDeskId :
            await db.UserDeskMemberships.Where(x => x.UserId == actor && x.IsPrimary && x.IsActive && x.RemovedAt == null && x.OfficeDesk.IsActive).Select(x => (Guid?)x.OfficeDeskId).SingleOrDefaultAsync(ct);
        var docs = await db.MatterDocuments.AsNoTracking().Where(x => x.MatterId == id && x.RecordStatus == RecordStatus.Active && x.Document.RecordStatus == RecordStatus.Active && x.Document.Status == "Active")
            .Select(x => new { x.DocumentId, x.Document.Version, x.Document.Sha256Hash, x.DisplayName, x.DocumentRole }).ToListAsync(ct);
        var note = new MatterOfficialNote { MatterId = id, Number = (await db.MatterOfficialNotes.Where(x => x.MatterId == id).MaxAsync(x => (int?)x.Number, ct) ?? 0) + 1,
            ContentJson = draft.ContentJson, CanonicalText = draft.CanonicalText, TextHash = NotingText.Hash(draft.CanonicalText), AuthorUserId = actor, AuthorName = user.DisplayName,
            Designation = user.CustomDesignation ?? user.Designation?.Name ?? "", DeskId = desk, DeskName = desk.HasValue ? await db.OfficeDesks.Where(x => x.Id == desk).Select(x => x.Name).SingleAsync(ct) : null,
            SubmittedAt = DateTimeOffset.UtcNow, SourceDakId = source, CitationsJson = draft.CitationsJson, DocumentManifestJson = Serialize(docs) };
        db.MatterOfficialNotes.Add(note); draft.ContentJson = ""; draft.CanonicalText = ""; draft.CitationsJson = "[]"; draft.Revision++; draft.UpdatedAt = DateTimeOffset.UtcNow; matter.Revision++;
        await AuditAsync(id, actor, "NoteSubmitted", note.Id, ct);
        return note;
    }

    public Task<MatterOfficialNote> SubmitAsync(Guid id, SubmitWorkingNoteCommand cmd, int revision, Guid actor, Guid key, CancellationToken ct) =>
        CommandAsync(actor, key, new { action = "Submit", id, cmd, revision }, id, cmd.SourceDakId,
            c => DraftPermissionAsync(id, actor, c), c => FinalizeAsync(id, revision, cmd.SourceDakId, actor, c), ct);

    public Task<SendWorkingNoteResult> SendAsync(Guid id, SendWorkingNoteCommand cmd, int revision, Guid actor, Guid key, CancellationToken ct) =>
        CommandAsync(actor, key, new { action = "Send", id, cmd, revision }, id, cmd.SourceDakId,
            async c => { await DraftPermissionAsync(id, actor, c); if (!await new DakAuthorizationService(db).CanAccessDakAsync(cmd.SourceDakId, PermissionCodes.DakMove, actor, c)) throw new MatterWorkflowException("Dak movement is unauthorized.", 403); },
            async c =>
            {
                if (cmd.Action is not ("Forwarded" or "Returned")) throw new MatterWorkflowException("Select Forwarded or Returned.");
                if (!await db.Daks.AsNoTracking().AnyAsync(x => x.Id == cmd.SourceDakId && x.Revision == cmd.ExpectedDakRevision, c))
                    throw new MatterWorkflowException("Controlling Dak changed; refresh before sending.", 409);
                var note = await FinalizeAsync(id, revision, cmd.SourceDakId, actor, c);
                var result = await dakWorkflow.SendAsync(cmd.SourceDakId, new SendDakCommand(Enum.Parse<DakMovementAction>(cmd.Action), cmd.ToDeskId, cmd.ToUserId,
                    DakDestinationKind.Officer, cmd.IncludesPhysicalOriginal, cmd.Remarks, cmd.Instructions, cmd.ExpectedDakRevision, key), actor, c);
                return new SendWorkingNoteResult(note, result);
            }, ct);

    private async Task<MatterOfficialNote> NoteAsync(Guid matter, Guid note, Guid actor, CancellationToken ct)
    {
        await ScopeAsync(matter, actor, PermissionCodes.MatterView, ct);
        return await db.MatterOfficialNotes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == note && x.MatterId == matter, ct) ?? throw new MatterWorkflowException("Note not found.", 404);
    }

    public async Task<List<MatterNoteRemark>> RemarksAsync(Guid matter, Guid note, Guid actor, CancellationToken ct)
    {
        await NoteAsync(matter, note, actor, ct);
        return await db.MatterNoteRemarks.AsNoTracking().Where(x => x.MatterId == matter && x.NoteId == note).OrderBy(x => x.CreatedAt).ToListAsync(ct);
    }
    public Task<MatterNoteRemark> RemarkAsync(Guid matter, Guid note, CreateRemarkCommand cmd, Guid actor, Guid key, CancellationToken ct) =>
        CommandAsync(actor, key, new { action = "Remark", matter, note, cmd }, matter, null, c => ScopeAsync(matter, actor, PermissionCodes.MatterEdit, c), async c =>
        {
            var n = await NoteAsync(matter, note, actor, c); NotingText.ValidateAnchor(n.CanonicalText, cmd.Anchor);
            if (string.IsNullOrWhiteSpace(cmd.Text) || cmd.Text.Length > 4000) throw new MatterWorkflowException("Remark text is required, up to 4000 characters.");
            var user = await db.AppUsers.Include(x => x.Designation).SingleAsync(x => x.Id == actor, c);
            var row = new MatterNoteRemark { MatterId = matter, NoteId = note, AnchorJson = Serialize(cmd.Anchor), Text = cmd.Text.Trim(), AuthorUserId = actor, AuthorName = user.DisplayName, Designation = user.CustomDesignation ?? user.Designation?.Name ?? "", CreatedAt = DateTimeOffset.UtcNow };
            db.MatterNoteRemarks.Add(row); await AuditAsync(matter, actor, "RemarkCreated", row.Id, c); return row;
        }, ct);
    public Task<MatterNoteRemark> ChangeRemarkAsync(Guid matter, Guid note, Guid remark, ChangeRemarkCommand cmd, Guid actor, Guid key, CancellationToken ct) =>
        CommandAsync(actor, key, new { action = "RemarkState", matter, note, remark, cmd }, matter, null, c => ScopeAsync(matter, actor, PermissionCodes.MatterEdit, c), async c =>
        {
            await NoteAsync(matter, note, actor, c);
            if (cmd.State is not ("Open" or "Addressed")) throw new MatterWorkflowException("Invalid remark state.");
            var row = await db.MatterNoteRemarks.SingleOrDefaultAsync(x => x.Id == remark && x.MatterId == matter && x.NoteId == note, c) ?? throw new MatterWorkflowException("Remark not found.", 404);
            if (row.Revision != cmd.ExpectedRevision) throw new MatterWorkflowException("Remark changed; refresh.", 409);
            row.State = cmd.State; row.Revision++; row.AddressedAt = cmd.State == "Addressed" ? DateTimeOffset.UtcNow : null; row.AddressedByUserId = cmd.State == "Addressed" ? actor : null;
            await AuditAsync(matter, actor, "RemarkStateChanged", row.Id, c); return row;
        }, ct);

    private async Task<Document> DocumentAsync(Guid matter, Guid document, Guid actor, CancellationToken ct)
    {
        await ScopeAsync(matter, actor, PermissionCodes.MatterView, ct);
        if (!await auth.CanAccessMatterDocumentAsync(matter, document, actor, ct)) throw new MatterWorkflowException("Document is unavailable or access has been revoked.", 403);
        return await db.Documents.AsNoTracking().SingleAsync(x => x.Id == document, ct);
    }
    public async Task<List<MatterPdfAnnotation>> AnnotationsAsync(Guid matter, Guid document, Guid actor, CancellationToken ct)
    {
        var doc = await DocumentAsync(matter, document, actor, ct);
        return await db.MatterPdfAnnotations.AsNoTracking().Where(x => x.MatterId == matter && x.DocumentId == document && x.DocumentVersion == doc.Version && x.DocumentHash == doc.Sha256Hash).OrderBy(x => x.CreatedAt).ToListAsync(ct);
    }
    public Task<MatterPdfAnnotation> AnnotateAsync(Guid matter, Guid document, CreateAnnotationCommand cmd, Guid actor, Guid key, CancellationToken ct) =>
        CommandAsync(actor, key, new { action = "Highlight", matter, document, cmd }, matter, null,
            async c => { await ScopeAsync(matter, actor, PermissionCodes.MatterDocumentManage, c); await DocumentAsync(matter, document, actor, c); }, async c =>
        {
            var doc = await DocumentAsync(matter, document, actor, c);
            if (!string.Equals(Path.GetExtension(doc.OriginalFileName), ".pdf", StringComparison.OrdinalIgnoreCase)) throw new MatterWorkflowException("Shared highlights require a PDF.");
            if (cmd.Region is null || cmd.Text?.Length > 4000 || cmd.Quote?.Length > 4000) throw new MatterWorkflowException("Valid region and bounded annotation text are required.");
            NotingText.ValidateRegion(cmd.Page, cmd.Region);
            await ValidatePageAsync(doc, cmd.Page, c);
            var row = new MatterPdfAnnotation { MatterId = matter, DocumentId = document, DocumentVersion = doc.Version, DocumentHash = doc.Sha256Hash,
                Page = cmd.Page, RegionJson = Serialize(cmd.Region), Quote = cmd.Quote, Text = cmd.Text, AuthorUserId = actor, AuthorName = (await db.AppUsers.SingleAsync(x => x.Id == actor, c)).DisplayName, CreatedAt = DateTimeOffset.UtcNow };
            db.MatterPdfAnnotations.Add(row); await AuditAsync(matter, actor, "PdfHighlighted", row.Id, c); return row;
        }, ct);
    public async Task<MatterWorkingNote> CiteAsync(Guid matter, CreateCitationCommand cmd, Guid actor, Guid key, CancellationToken ct)
    {
        var source = await db.MatterWorkingNotes.AsNoTracking().Where(x => x.MatterId == matter && x.AuthorUserId == actor).Select(x => x.SourceDakId).SingleOrDefaultAsync(ct);
        return await CommandAsync(actor, key, new { action = "Citation", matter, cmd }, matter, source,
            async c => { await DraftPermissionAsync(matter, actor, c); await DocumentAsync(matter, cmd.DocumentId, actor, c); }, async c =>
        {
            var draft = await db.MatterWorkingNotes.SingleOrDefaultAsync(x => x.MatterId == matter && x.AuthorUserId == actor, c) ?? throw new MatterWorkflowException("Save a draft first.", 409);
            if (draft.SourceDakId != source) throw new MatterWorkflowException("Draft source changed; refresh.", 409);
            await EditorAsync(matter, actor, draft.SourceDakId, c);
            if (draft.Revision != cmd.ExpectedRevision) throw new MatterWorkflowException("Draft changed; refresh.", 409);
            NotingText.ValidateAnchor(draft.CanonicalText, cmd.Anchor); NotingText.ValidateRegion(cmd.Page, cmd.Region);
            var doc = await DocumentAsync(matter, cmd.DocumentId, actor, c);
            if (cmd.Page.HasValue) await ValidatePageAsync(doc, cmd.Page.Value, c);
            var citations = JsonSerializer.Deserialize<List<NoteCitation>>(draft.CitationsJson, Json)!;
            if (citations.Count >= 100) throw new MatterWorkflowException("At most 100 citations per note.");
            citations.Add(new(cmd.Anchor, doc.Id, doc.Version, doc.Sha256Hash, cmd.Page, cmd.Region)); draft.CitationsJson = Serialize(citations); draft.Revision++; draft.UpdatedAt = DateTimeOffset.UtcNow;
            await AuditAsync(matter, actor, "CitationAdded", doc.Id, c); return draft;
        }, ct);
    }

    private async Task ValidatePageAsync(Document doc, int page, CancellationToken ct)
    {
        if (!string.Equals(Path.GetExtension(doc.OriginalFileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new MatterWorkflowException("Page/region anchors currently require PDF; cite Word without a page.");
        await using var stream = await storage.OpenReadAsync(doc.StoragePath, ct) ?? throw new MatterWorkflowException("Source document is unavailable.", 404);
        try
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(stream);
            if (page > pdf.NumberOfPages) throw new MatterWorkflowException("Page is outside the source PDF.");
        }
        catch (MatterWorkflowException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new MatterWorkflowException("The source PDF cannot be safely parsed for page anchors."); }
    }

    public async Task<object> WorkspaceReadAsync(Guid id, Guid? source, Guid actor, CancellationToken ct)
    {
        await ScopeAsync(id, actor, PermissionCodes.MatterView, ct);
        var matter = await db.Matters.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var editor = false; try { await EditorAsync(id, actor, source, ct); editor = true; } catch (MatterWorkflowException) { }
        var notes = await db.MatterOfficialNotes.AsNoTracking().Where(x => x.MatterId == id).OrderBy(x => x.Number).ToListAsync(ct);
        var docs = new List<object>();
        foreach (var link in await db.MatterDocuments.AsNoTracking().Include(x => x.Document).Where(x => x.MatterId == id && x.RecordStatus == RecordStatus.Active).ToListAsync(ct))
            if (await auth.CanAccessMatterDocumentAsync(id, link.DocumentId, actor, ct)) docs.Add(new { id = link.DocumentId, filename = link.Document.OriginalFileName, mime = link.Document.MimeType,
                version = link.Document.Version, hash = link.Document.Sha256Hash, link.DisplayName, link.DocumentRole, contentUrl = $"/api/matters/{id}/documents/{link.DocumentId}/content" });
        return new { matterId = id, matter.Revision, matter.VillageId, sourceDakId = source,
            capabilities = new { canEditDraft = editor, canSubmit = editor, canRemark = await auth.CanAccessMatterAsync(id, PermissionCodes.MatterEdit, actor, ct),
                canAnnotate = await auth.CanAccessMatterAsync(id, PermissionCodes.MatterDocumentManage, actor, ct), canSend = editor && source.HasValue && await new DakAuthorizationService(db).CanAccessDakAsync(source.Value, PermissionCodes.DakMove, actor, ct), nativeRoutingPolicyPending = !source.HasValue },
            notes = notes.Select(n => new { n.Id, n.MatterId, n.Number, n.Version, n.ContentJson, n.CanonicalText, n.TextHash, n.AuthorUserId, n.AuthorName, n.Designation, n.DeskId, n.DeskName, n.SubmittedAt, n.SourceDakId,
                citations = JsonSerializer.Deserialize<List<NoteCitation>>(n.CitationsJson, Json), n.DocumentManifestJson }), documents = docs };
    }
}
