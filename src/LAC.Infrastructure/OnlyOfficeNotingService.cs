namespace LAC.Infrastructure;

using System.Security.Cryptography;
using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public sealed record OpenOfficeNoteCommand(Guid? SourceDakId, Guid? ResumeSnapshotId = null, string? CompositionVersion = null);
public sealed record SaveOfficeNoteCommand([property: System.Text.Json.Serialization.JsonRequired] int ClientGeneration,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? NativeExportUrl = null);
public sealed record PreviewOfficeNoteCommand(Guid? SaveRequestId,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? NativeExportUrl = null);
public sealed record CheckOfficeSaveCommand(string NativeExportUrl);
public sealed record OfficeSaveResolution(bool NoChanges, Guid SnapshotId, string Sha256, int SnapshotVersion, int SessionRevision, OfficeSaveInfo? CommittedReceipt);
public sealed record ConfirmOfficeNoteCommand(Guid PreviewId, string CanonicalHash, bool ResetCitations = false);
public sealed record OfficeSessionInfo(Guid Id, Guid MatterId, Guid ActorUserId, Guid? SourceDakId, int WorkingRevision, int Revision, string State, DateTimeOffset ExpiresAt, Guid? LatestSnapshotId, string CompositionVersion = "OfficeNoteV1");
public sealed record OfficeSaveInfo(Guid Id, Guid SessionId, int ClientGeneration, int SessionRevision, string State, DateTimeOffset Deadline, Guid? SnapshotId, string? Sha256, int? Version, string? FailureCode);

public sealed class OnlyOfficeNotingService(LacDbContext db, MatterNotingWorkflow workflow, IDocumentStorage storage,
    OnlyOfficeTokens tokens, NotingOfficeClient office, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateTimeOffset Now => clock.GetUtcNow();
    private static OfficeSessionInfo Info(MatterOfficeSession s) => new(s.Id, s.MatterId, s.ActorUserId, s.SourceDakId, s.WorkingRevision, s.Revision, s.State, s.ExpiresAt, s.LatestSnapshotId, s.CompositionVersion);
    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    public async Task<MatterOfficeSession> AuthorizeAsync(Guid id, Guid actor, bool active, CancellationToken ct)
    {
        office.Enabled();
        var s = await db.MatterOfficeSessions.SingleOrDefaultAsync(x => x.Id == id && x.ActorUserId == actor, ct)
            ?? throw new MatterWorkflowException("Office session is unavailable.", 404);
        await workflow.AuthorizeOfficeEditorAsync(s.MatterId, actor, s.SourceDakId, ct);
        var user = await db.AppUsers.AsNoTracking().SingleAsync(x => x.Id == actor, ct);
        var draft = await db.MatterWorkingNotes.AsNoTracking().SingleOrDefaultAsync(x => x.MatterId == s.MatterId && x.AuthorUserId == actor, ct);
        if (s.ExpiresAt <= Now || s.ActorSessionVersion != user.SessionVersion || draft?.OfficeSessionId != s.Id || draft.Revision != s.WorkingRevision ||
            draft.SourceDakId != s.SourceDakId || (active ? s.State != "Active" : s.State is not ("Active" or "Frozen" or "Reviewed")))
            throw new MatterWorkflowException("Office session is expired, revoked, frozen or stale; recover the last committed note.", 409);
        if (s.CompositionVersion == ContinuousGreenEnvelope.Version)
        {
            VerifyManifestSeal(s);
            await ContinuousGreenEnvelope.AuthoritativeAsync(db, s, ct);
        }
        return s;
    }

    public Task<OfficeSessionInfo> OpenAsync(Guid matter, OpenOfficeNoteCommand cmd, int workingRevision, Guid actor, Guid key, CancellationToken ct)
    {
        office.Enabled();
        var version = cmd.CompositionVersion ?? OfficeNoteDocx.ConverterVersion;
        if (version is not (OfficeNoteDocx.ConverterVersion or ContinuousGreenEnvelope.Version)) throw new MatterWorkflowException("Unknown Office composition version.", 422);
        // Preserve old idempotency fingerprints exactly when the additive field is omitted.
        object commandPayload = version == OfficeNoteDocx.ConverterVersion
            ? new { action = "OfficeOpen", matter, cmd = new { cmd.SourceDakId, cmd.ResumeSnapshotId }, workingRevision }
            : new { action = "OfficeOpen", matter, cmd, workingRevision };
        return workflow.OfficeCommandAsync(matter, cmd.SourceDakId, actor, key, commandPayload, async c =>
        {
            var draft = await db.MatterWorkingNotes.SingleOrDefaultAsync(x => x.MatterId == matter && x.AuthorUserId == actor, c);
            if ((draft?.Revision ?? 0) != workingRevision) throw new MatterWorkflowException("Working note revision changed.", 409);
            foreach (var old in await db.MatterOfficeSessions.Where(x => x.MatterId == matter && x.ActorUserId == actor && (x.State == "Active" || x.State == "Frozen" || x.State == "Reviewed")).ToListAsync(c))
            { old.State = "Revoked"; old.Revision++; }
            await db.SaveChangesAsync(c); // Release the unique live lease before inserting its replacement, in this transaction.
            var user = await db.AppUsers.AsNoTracking().SingleAsync(x => x.Id == actor, c);
            var session = new MatterOfficeSession { MatterId = matter, ActorUserId = actor, SourceDakId = cmd.SourceDakId, WorkingRevision = workingRevision,
                ActorSessionVersion = user.SessionVersion, ExpiresAt = Now.AddMinutes(Math.Clamp(office.NotingOptions.LeaseMinutes, 1, 60)), CompositionVersion = version };
            session.DocumentKey = "lac-note-" + session.Id.ToString("N");
            GreenEnvelopeManifest? manifest = null;
            if (version == ContinuousGreenEnvelope.Version)
            {
                manifest = await ContinuousGreenEnvelope.CaptureAsync(db, session, c);
                session.HistoryManifestJson = ContinuousGreenEnvelope.Serialize(manifest);
                session.HistoryManifestHash = NotingText.Hash(session.HistoryManifestJson);
                session.HistoryManifestSeal = tokens.Sign(new { purpose = "green-envelope-manifest", sessionId = session.Id, actor, matter, hash = session.HistoryManifestHash });
            }
            DocumentStorageWriteResult saved;
            var resumeId = cmd.ResumeSnapshotId;
            if (resumeId is null && draft?.OfficeSessionId is { } priorId)
                resumeId = await db.MatterOfficeSessions.Where(x => x.Id == priorId && x.MatterId == matter && x.ActorUserId == actor && x.SourceDakId == cmd.SourceDakId)
                    .Select(x => x.LatestSnapshotId).SingleOrDefaultAsync(c);
            if (resumeId is { } resume)
            {
                var original = await db.MatterOfficeSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == resume && x.ActorUserId == actor, c)
                    ?? throw new MatterWorkflowException("Recovery snapshot is unavailable.", 404);
                var prior = await db.MatterOfficeSessions.AsNoTracking().SingleAsync(x => x.Id == original.SessionId, c);
                if (prior.MatterId != matter || prior.SourceDakId != cmd.SourceDakId) throw new MatterWorkflowException("Recovery snapshot belongs to a different working context.", 409);
                await OfficeNotingSubmission.VerifyBytesAsync(storage, original, c);
                if (version == OfficeNoteDocx.ConverterVersion && prior.CompositionVersion == OfficeNoteDocx.ConverterVersion)
                    saved = new(original.StoragePath, original.Sha256, original.FileSize);
                else
                {
                    await using var priorBytes = await storage.OpenReadAsync(original.StoragePath, c) ?? throw new MatterWorkflowException("Recovery snapshot unavailable.", 409);
                    // Recovery uses the old captured manifest (not the new live history).
                    // Rewrap only validated current content; historical bytes never leak into it.
                    var current = prior.CompositionVersion == ContinuousGreenEnvelope.Version
                        ? ContinuousGreenEnvelope.Convert(priorBytes, VerifyManifestSeal(prior)) : OfficeNoteDocx.Convert(priorBytes);
                    using var fresh = manifest is null ? OfficeNoteDocx.Create(current.ContentJson) : ContinuousGreenEnvelope.Compose(manifest, current.ContentJson);
                    saved = await storage.SaveAndHashAsync(fresh, "green-working-note.docx", c);
                }
            }
            else
            {
                var current = string.IsNullOrEmpty(draft?.ContentJson) ? "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[]}]}" : draft.ContentJson;
                using var bytes = manifest is null ? OfficeNoteDocx.Create(current) : ContinuousGreenEnvelope.Compose(manifest, current);
                saved = await storage.SaveAndHashAsync(bytes, "green-working-note.docx", c);
            }
            var snapshot = new MatterOfficeSnapshot { SessionId = session.Id, ActorUserId = actor, Version = 0,
                StoragePath = saved.StoragePath, Sha256 = saved.Sha256Hash.ToLowerInvariant(), FileSize = saved.FileSize, CreatedAt = Now };
            session.LatestSnapshotId = snapshot.Id;
            db.MatterOfficeSessions.Add(session); db.MatterOfficeSnapshots.Add(snapshot);
            if (draft is null) { draft = new MatterWorkingNote { MatterId = matter, AuthorUserId = actor, SourceDakId = cmd.SourceDakId, UpdatedAt = Now }; db.MatterWorkingNotes.Add(draft); }
            draft.SourceDakId = cmd.SourceDakId; draft.OfficeSessionId = session.Id; draft.OfficePreviewId = null;
            return Info(session);
        }, ct);
    }

    public async Task<object> ConfigAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var s = await AuthorizeAsync(id, actor, true, ct);
        var snap = await db.MatterOfficeSnapshots.AsNoTracking().SingleAsync(x => x.Id == s.LatestSnapshotId && x.SessionId == s.Id, ct);
        var fileToken = tokens.Sign(new { purpose = "noting-office-file", sessionId = id, actor, snapshotId = snap.Id, exp = s.ExpiresAt.ToUnixTimeSeconds() });
        var user = await db.AppUsers.AsNoTracking().SingleAsync(x => x.Id == actor, ct);
        var customization = new Dictionary<string, object?> { ["autosave"] = true, ["forcesave"] = false,
            ["compactHeader"] = true, ["compactToolbar"] = true, ["zoom"] = 85 };
        if (s.CompositionVersion == ContinuousGreenEnvelope.Version) customization["uiTheme"] = "theme-light";
        var config = new Dictionary<string, object?>
        {
            ["documentType"] = "word",
            ["document"] = new { fileType = "docx", key = s.DocumentKey, title = s.CompositionVersion == ContinuousGreenEnvelope.Version ? "Continuous Green Noting.docx" : "Current private Green Note.docx",
                url = office.Options.AppExternalUrl.TrimEnd('/') + $"/api/noting-office/sessions/{id}/file?token=" + Uri.EscapeDataString(fileToken),
                permissions = new { edit = true, download = s.CompositionVersion == ContinuousGreenEnvelope.Version, print = false, review = false, comment = false, modifyContentControl = false } },
            ["editorConfig"] = new { mode = "edit", user = new { id = actor.ToString(), name = user.DisplayName },
                callbackUrl = office.Options.AppExternalUrl.TrimEnd('/') + $"/api/noting-office/sessions/{id}/callback",
                customization },
            ["exp"] = s.ExpiresAt.ToUnixTimeSeconds()
        };
        config["token"] = tokens.Sign(config);
        return new { session = Info(s), scriptUrl = office.Options.BrowserUrl.TrimEnd('/') + "/web-apps/apps/api/documents/api.js", config };
    }

    public async Task<Stream> FileAsync(Guid id, string token, CancellationToken ct)
    {
        var p = tokens.Verify(token);
        if (p.GetProperty("purpose").GetString() != "noting-office-file" || p.GetProperty("sessionId").GetGuid() != id) throw new UnauthorizedAccessException();
        var actor = p.GetProperty("actor").GetGuid();
        var session = await AuthorizeAsync(id, actor, true, ct);
        var sid = p.GetProperty("snapshotId").GetGuid();
        if (sid != session.LatestSnapshotId) throw new UnauthorizedAccessException();
        var snapshot = await db.MatterOfficeSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sid && x.SessionId == session.Id && x.ActorUserId == actor, ct)
            ?? throw new UnauthorizedAccessException();
        await OfficeNotingSubmission.VerifyBytesAsync(storage, snapshot, ct);
        return await storage.OpenReadAsync(snapshot.StoragePath, ct) ?? throw new MatterWorkflowException("Committed DOCX unavailable.", 404);
    }

    public async Task<OfficeSaveInfo> StatusAsync(Guid id, Guid request, Guid actor, CancellationToken ct)
    {
        await AuthorizeAsync(id, actor, false, ct);
        var r = await db.MatterOfficeSaveRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request && x.SessionId == id && x.ActorUserId == actor, ct)
            ?? throw new MatterWorkflowException("Save request unavailable.", 404);
        var snap = r.SnapshotId is { } sid ? await db.MatterOfficeSnapshots.AsNoTracking().SingleAsync(x => x.Id == sid, ct) : null;
        var currentRevision = await db.MatterOfficeSessions.AsNoTracking().Where(x => x.Id == id).Select(x => x.Revision).SingleAsync(ct);
        return new(r.Id, id, r.ClientGeneration, currentRevision, r.State == "Pending" && r.Deadline <= Now ? "Failed" : r.State,
            r.Deadline, r.SnapshotId, snap?.Sha256, snap?.Version, r.State == "Pending" && r.Deadline <= Now ? "Timeout" : r.FailureCode);
    }

    public async Task<OfficeSaveInfo> SaveAsync(Guid id, SaveOfficeNoteCommand cmd, int revision, Guid actor, Guid key, CancellationToken ct)
    {
        var session = await AuthorizeAsync(id, actor, true, ct);
        if (key == Guid.Empty) throw new MatterWorkflowException("A UUID Idempotency-Key is required.", 428);
        if (cmd.ClientGeneration < 0) throw new MatterWorkflowException("A nonnegative editor generation is required.");
        byte[]? nativeBytes = null;
        if (session.CompositionVersion == ContinuousGreenEnvelope.Version)
        {
            if (string.IsNullOrWhiteSpace(cmd.NativeExportUrl)) throw new MatterWorkflowException("V2 requires a native DOCX export after input quiescence and synchronization; no save was started.", 428);
            ValidateNativeExport(session, cmd.NativeExportUrl);
            if (!await db.MatterOfficeSaveRequests.AnyAsync(x => x.SessionId == id && x.ActorUserId == actor && x.RequestId == key, ct))
                nativeBytes = await office.DownloadAsync(cmd.NativeExportUrl, OfficeNoteDocx.MaxBytes, ct);
        }
        else if (cmd.NativeExportUrl is not null) throw new MatterWorkflowException("Native capture is supported only by ContinuousGreenEnvelopeV2.", 422);
        var requestId = await workflow.OfficeCommandAsync(session.MatterId, session.SourceDakId, actor, key,
            new { action = "OfficeSave", id, cmd, revision }, async c =>
            {
                var s = await AuthorizeAsync(id, actor, true, c);
                if (s.Revision != revision) throw new MatterWorkflowException("Office session revision changed.", 409);
                var lastGeneration = await db.MatterOfficeSaveRequests.Where(x => x.SessionId == id).MaxAsync(x => (int?)x.ClientGeneration, c) ?? 0;
                if (cmd.ClientGeneration < lastGeneration) throw new MatterWorkflowException("Editor generation moved backwards; refresh the current session.", 409);
                var pending = await db.MatterOfficeSaveRequests.Where(x => x.SessionId == id && x.State == "Pending").ToListAsync(c);
                if (pending.Any(x => x.Deadline > Now)) throw new MatterWorkflowException("A correlated save is still pending.", 409);
                foreach (var expired in pending) { expired.State = "Failed"; expired.FailureCode = "Timeout"; }
                await db.SaveChangesAsync(c);
                var r = new MatterOfficeSaveRequest { SessionId = id, ActorUserId = actor, RequestId = key, ClientGeneration = cmd.ClientGeneration,
                    SessionRevision = s.Revision + 1, Deadline = Now.AddSeconds(Math.Clamp(office.NotingOptions.SaveTimeoutSeconds, 5, 120)),
                    PayloadHash = NotingText.Hash(JsonSerializer.Serialize(new { id, cmd, revision }, Json)) };
                if (s.CompositionVersion == ContinuousGreenEnvelope.Version)
                {
                    var manifest = await ContinuousGreenEnvelope.AuthoritativeAsync(db, s, c);
                    using var bytes = new MemoryStream(nativeBytes ?? throw new MatterWorkflowException("Native capture is unavailable.", 409));
                    var target = ContinuousGreenEnvelope.Convert(bytes, manifest); bytes.Position = 0;
                    var capture = await storage.SaveAndHashAsync(bytes, "native-quiesced-capture.docx", c);
                    r.NativeCaptureStoragePath = capture.StoragePath; r.NativeCaptureSha256 = capture.Sha256Hash.ToLowerInvariant(); r.NativeCaptureSize = capture.FileSize;
                    r.NativeCanonicalHash = target.CanonicalHash; r.NativeContentHash = ContinuousGreenEnvelope.ContentFingerprint(target);
                }
                db.MatterOfficeSaveRequests.Add(r); s.Revision++;
                return r.Id;
            }, ct);
        var state = await StatusAsync(id, requestId, actor, ct);
        if (state.State == "Committed") return state;
        if (state.State == "Failed")
        {
            await workflow.OfficeCommandAsync(session.MatterId, session.SourceDakId, actor, Guid.NewGuid(), new { action = "OfficeSaveRetry", requestId }, async c =>
            {
                var s = await AuthorizeAsync(id, actor, true, c);
                var r = await db.MatterOfficeSaveRequests.SingleAsync(x => x.Id == requestId, c);
                if (r.State == "Committed") return r.Id;
                if (r.SessionRevision != s.Revision || await db.MatterOfficeSaveRequests.AnyAsync(x => x.SessionId == id && x.Id != requestId && x.State == "Pending", c))
                    throw new MatterWorkflowException("A newer save supersedes this retry.", 409);
                r.State = "Pending"; r.FailureCode = null; r.Deadline = Now.AddSeconds(Math.Clamp(office.NotingOptions.SaveTimeoutSeconds, 5, 120));
                return r.Id;
            }, ct);
        }
        try
        {
            var response = await office.CommandAsync(new() { ["c"] = "forcesave", ["key"] = session.DocumentKey, ["userdata"] = requestId.ToString() }, ct);
            if (response.GetProperty("error").GetInt32() != 0) await FailAsync(id, requestId, "DocumentServerCommandRejected", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { await FailAsync(id, requestId, "DocumentServerCommandUnavailable", CancellationToken.None); }
        return await StatusAsync(id, requestId, actor, ct);
    }

    private void ValidateNativeExport(MatterOfficeSession session, string url)
    {
        var exportUri=office.ValidateOrigin(url);var path=Uri.UnescapeDataString(exportUri.AbsolutePath);
        var acceptedPath="^/cache/files/(?:data/)?"+System.Text.RegularExpressions.Regex.Escape(session.DocumentKey)+"(?:_[A-Za-z0-9-]+)?/output[.]docx/[A-Za-z0-9._ -]+[.]docx$";
        if(url.Length>8192 || !System.Text.RegularExpressions.Regex.IsMatch(path,acceptedPath) || path.Split('/').Any(x=>x is "." or ".."))
            throw new MatterWorkflowException("Unexpected native DOCX export path.",422);
    }
    private async Task<OfficeSaveResolution> CompareCurrentAsync(MatterOfficeSession s, byte[] native, CancellationToken ct)
    {
        var manifest=VerifyManifestSeal(s);await ContinuousGreenEnvelope.AuthoritativeAsync(db,s,ct);
        using var captured=new MemoryStream(native);var target=ContinuousGreenEnvelope.Convert(captured,manifest);
        var snap=await db.MatterOfficeSnapshots.AsNoTracking().SingleAsync(x=>x.Id==s.LatestSnapshotId && x.SessionId==s.Id && x.ActorUserId==s.ActorUserId,ct);
        await OfficeNotingSubmission.VerifyBytesAsync(storage,snap,ct);
        await using var saved=await storage.OpenReadAsync(snap.StoragePath,ct) ?? throw new MatterWorkflowException("Snapshot unavailable.",409);
        var canonical=ContinuousGreenEnvelope.Convert(saved,manifest);
        var equal=canonical.CanonicalHash==target.CanonicalHash && ContinuousGreenEnvelope.ContentFingerprint(canonical)==ContinuousGreenEnvelope.ContentFingerprint(target);
        OfficeSaveInfo? receipt=null;
        if(equal && snap.SaveRequestId is { } save)
        {
            var r=await db.MatterOfficeSaveRequests.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==save && x.SessionId==s.Id && x.ActorUserId==s.ActorUserId && x.State=="Committed" && x.SnapshotId==snap.Id,ct)
                ?? throw new MatterWorkflowException("Snapshot has no valid committed receipt.",409);
            await OfficeNativeCapture.VerifyAsync(storage,r,manifest,ct);
            if(r.NativeCanonicalHash!=canonical.CanonicalHash || r.NativeContentHash!=ContinuousGreenEnvelope.ContentFingerprint(canonical))
                throw new MatterWorkflowException("Committed receipt differs from snapshot content.",409);
            receipt=new(r.Id,s.Id,r.ClientGeneration,s.Revision,r.State,r.Deadline,snap.Id,snap.Sha256,snap.Version,null);
        }
        else if(equal && snap.Version!=0) throw new MatterWorkflowException("A valid seed or committed receipt is required.",409);
        return new(equal,snap.Id,snap.Sha256,snap.Version,s.Revision,receipt);
    }
    public async Task<OfficeSaveResolution> CheckSaveAsync(Guid id, CheckOfficeSaveCommand cmd, int revision, Guid actor, Guid key, CancellationToken ct)
    {
        var session=await AuthorizeAsync(id,actor,true,ct);
        if(session.CompositionVersion!=ContinuousGreenEnvelope.Version) throw new MatterWorkflowException("Native no-change checks require V2.",422);
        ValidateNativeExport(session,cmd.NativeExportUrl);
        var native=await office.DownloadAsync(cmd.NativeExportUrl,OfficeNoteDocx.MaxBytes,ct);
        var result=await workflow.OfficeCommandAsync(session.MatterId,session.SourceDakId,actor,key,new {action="OfficeCheckSave",id,cmd,revision},async c=>
        {
            var s=await AuthorizeAsync(id,actor,true,c);
            if(s.Revision!=revision || await db.MatterOfficeSaveRequests.AnyAsync(x=>x.SessionId==id && x.State=="Pending" && x.Deadline>Now,c))
                throw new MatterWorkflowException("Session changed or a save is still pending.",409);
            return await CompareCurrentAsync(s,native,c);
        },ct);
        // A replay returns original evidence, but cannot describe a superseded snapshot
        // or a changed lease/content as current. No save request, commit or forcesave is created.
        var live=await AuthorizeAsync(id,actor,true,ct);
        if(live.Revision!=result.SessionRevision || live.LatestSnapshotId!=result.SnapshotId)
            throw new MatterWorkflowException("No-change evidence is stale; capture the current note again.",409);
        var current=await CompareCurrentAsync(live,native,ct);
        if(result.NoChanges!=current.NoChanges || result.Sha256!=current.Sha256)
            throw new MatterWorkflowException("No-change evidence differs from current content.",409);
        return current;
    }

    private async Task FailAsync(Guid sessionId, Guid id, string code, CancellationToken ct)
    {
        var s = await db.MatterOfficeSessions.AsNoTracking().SingleAsync(x => x.Id == sessionId, ct);
        await workflow.OfficeCommandAsync(s.MatterId, s.SourceDakId, s.ActorUserId, Guid.NewGuid(), new { action = "OfficeSaveFailed", id, code }, async c =>
        {
            var r = await db.MatterOfficeSaveRequests.SingleAsync(x => x.Id == id, c);
            if (r.State == "Pending") { r.State = "Failed"; r.FailureCode = code; }
            return r.Id;
        }, ct);
    }

    public async Task<int> CallbackAsync(Guid id, JsonElement signed, CancellationToken ct)
    {
        office.Enabled();
        var body = signed.TryGetProperty("payload", out var wrapped) ? wrapped : signed;
        var session = await db.MatterOfficeSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (session is null) return 1;
        var status = body.GetProperty("status").GetInt32();
        var key = body.GetProperty("key").GetString();
        if (key != session.DocumentKey) return 1;
        await AuthorizeAsync(id, session.ActorUserId, true, ct);
        if (status is 1 or 4) return 0;
        if (status == 7 && body.TryGetProperty("userdata", out var failedData) && Guid.TryParse(failedData.GetString(), out var failedId))
        {
            var failed = await db.MatterOfficeSaveRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == failedId && x.SessionId == id && x.State == "Pending" && x.SessionRevision == session.Revision, ct);
            if (failed is null) return 1;
            await FailAsync(id, failedId, "DocumentServerSaveFailed", ct);
            return 0;
        }
        if (status is not (2 or 6)) return 1;
        if (!body.TryGetProperty("users", out var users) || users.ValueKind != JsonValueKind.Array ||
            users.GetArrayLength() != 1 || users[0].GetString() != session.ActorUserId.ToString()) return 1;
        Guid? request = null;
        if (status == 6)
        {
            if (!body.TryGetProperty("userdata", out var data) || !Guid.TryParse(data.GetString(), out var rid)) return 1;
            request = rid;
            var r = await db.MatterOfficeSaveRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rid && x.SessionId == id, ct);
            if (r is null || r.State == "Failed" || r.Deadline <= Now && r.State != "Committed") return 1;
            if (r.State == "Committed") return 0; // Same private session and request already committed; no late overwrite.
        }
        else if (await db.MatterOfficeSaveRequests.AnyAsync(x => x.SessionId == id && x.State == "Pending", ct)) return 1;
        var bytes = await office.DownloadAsync(body.GetProperty("url").GetString()!, OfficeNoteDocx.MaxBytes, ct);
        using (var input = new MemoryStream(bytes)) OnlyOfficeDraftService.ValidateDocx(input);
        var hash = Hash(bytes);
        // Download occurred outside locks. Revalidate under the same Dak->Matter locks as submission.
        await workflow.OfficeCommandAsync(session.MatterId, session.SourceDakId, session.ActorUserId, Guid.NewGuid(),
            new { action = "OfficeCallback", id, status, request, hash }, async c =>
            {
                var s = await AuthorizeAsync(id, session.ActorUserId, true, c);
                MatterOfficeSaveRequest? r = null;
                if (request.HasValue)
                {
                    r = await db.MatterOfficeSaveRequests.SingleAsync(x => x.Id == request && x.SessionId == id, c);
                    if (r.State == "Committed") return 0;
                    if (r.State != "Pending" || r.Deadline <= Now || r.SessionRevision != s.Revision) throw new MatterWorkflowException("Stale save callback.", 409);
                }
                else if (await db.MatterOfficeSaveRequests.AnyAsync(x => x.SessionId == id && x.State == "Pending", c)) throw new MatterWorkflowException("Final save cannot satisfy a pending force-save request.", 409);
                if (s.CompositionVersion == ContinuousGreenEnvelope.Version)
                {
                    VerifyManifestSeal(s);
                    var captured = await ContinuousGreenEnvelope.AuthoritativeAsync(db, s, c);
                    using var validate = new MemoryStream(bytes);
                    var target = ContinuousGreenEnvelope.Convert(validate, captured);
                    if (r is not null)
                    {
                        await OfficeNativeCapture.VerifyAsync(storage, r, captured, c);
                        if (target.CanonicalHash != r.NativeCanonicalHash || ContinuousGreenEnvelope.ContentFingerprint(target) != r.NativeContentHash)
                            throw new MatterWorkflowException("Native synchronization capture differs from callback content; last committed bytes were preserved.", 422);
                    }
                }
                var previous = await db.MatterOfficeSnapshots.AsNoTracking().SingleAsync(x => x.Id == s.LatestSnapshotId, c);
                if (r is null && previous.Sha256 == hash) return 0;
                using var content = new MemoryStream(bytes);
                var saved = await storage.SaveAndHashAsync(content, "sha-" + hash + ".docx", c);
                var snapshot = new MatterOfficeSnapshot { SessionId = id, ActorUserId = s.ActorUserId, SaveRequestId = request, Version = previous.Version + 1,
                    StoragePath = saved.StoragePath, Sha256 = saved.Sha256Hash.ToLowerInvariant(), FileSize = saved.FileSize, CreatedAt = Now };
                db.MatterOfficeSnapshots.Add(snapshot); s.LatestSnapshotId = snapshot.Id; s.Revision++;
                if (status == 2) s.State = "Revoked"; // A final close retires the private key; recovery opens a new lease from these immutable bytes.
                if (r is not null) { r.State = "Committed"; r.SnapshotId = snapshot.Id; }
                return 0;
            }, ct);
        return 0;
    }

    public async Task<MatterOfficePreview> PreviewAsync(Guid id, PreviewOfficeNoteCommand cmd, int revision, Guid actor, Guid key, CancellationToken ct)
    {
        var session = await AuthorizeAsync(id, actor, false, ct);
        byte[]? native=null;
        if(cmd.NativeExportUrl is not null)
        {
            if(session.CompositionVersion!=ContinuousGreenEnvelope.Version) throw new MatterWorkflowException("Native preview capture requires V2.",422);
            ValidateNativeExport(session,cmd.NativeExportUrl);native=await office.DownloadAsync(cmd.NativeExportUrl,OfficeNoteDocx.MaxBytes,ct);
        }
        return await workflow.OfficeCommandAsync(session.MatterId, session.SourceDakId, actor, key, new { action = "OfficePreview", id, cmd, revision }, async c =>
        {
            var s = await AuthorizeAsync(id, actor, true, c);
            if (s.Revision != revision || await db.MatterOfficeSaveRequests.AnyAsync(x=>x.SessionId==id && x.State=="Pending" && x.Deadline>Now,c)) throw new MatterWorkflowException("Office session changed or a save is still pending.", 409);
            var snap = await db.MatterOfficeSnapshots.AsNoTracking().SingleAsync(x=>x.Id==s.LatestSnapshotId && x.SessionId==id && x.ActorUserId==actor,c);
            var r = cmd.SaveRequestId is { } save ? await db.MatterOfficeSaveRequests.SingleOrDefaultAsync(x => x.Id == save && x.SessionId == id && x.ActorUserId == actor, c) : null;
            if(cmd.SaveRequestId is not null && (r?.State!="Committed" || r.SnapshotId!=snap.Id) ||
                cmd.SaveRequestId is null && (s.CompositionVersion!=ContinuousGreenEnvelope.Version || snap.Version!=0 || snap.SaveRequestId is not null || native is null))
                throw new MatterWorkflowException("An exact committed save or freshly matched server seed is required before preview.",409);
            await OfficeNotingSubmission.VerifyBytesAsync(storage, snap, c);
            await using var stream = await storage.OpenReadAsync(snap.StoragePath, c) ?? throw new MatterWorkflowException("Snapshot unavailable.", 409);
            ConvertedOfficeNote converted;
            if (s.CompositionVersion == ContinuousGreenEnvelope.Version)
            {
                VerifyManifestSeal(s);
                converted = ContinuousGreenEnvelope.Convert(stream, await ContinuousGreenEnvelope.AuthoritativeAsync(db, s, c));
                if(r is not null)
                {
                    await OfficeNativeCapture.VerifyAsync(storage, r, VerifyManifestSeal(s), c);
                    if (converted.CanonicalHash != r.NativeCanonicalHash || ContinuousGreenEnvelope.ContentFingerprint(converted) != r.NativeContentHash)
                        throw new MatterWorkflowException("Preview differs from the quiesced native capture.", 409);
                }
            }
            else converted = OfficeNoteDocx.Convert(stream);
            var p = new MatterOfficePreview { MatterId = s.MatterId, SessionId = id, ActorUserId = actor, SnapshotId = snap.Id, SourceDakId = s.SourceDakId,
                WorkingRevision = s.WorkingRevision, ConverterVersion = s.CompositionVersion, ContentJson = converted.ContentJson, CanonicalText = converted.CanonicalText, CanonicalHash = converted.CanonicalHash, CreatedAt = Now };
            if(native is not null)
            {
                using var captureBytes=new MemoryStream(native);var target=ContinuousGreenEnvelope.Convert(captureBytes,VerifyManifestSeal(s));
                if(target.CanonicalHash!=converted.CanonicalHash || ContinuousGreenEnvelope.ContentFingerprint(target)!=ContinuousGreenEnvelope.ContentFingerprint(converted))
                    throw new MatterWorkflowException("Native content changed before review; save the latest note first.",409);
                captureBytes.Position=0;var capture=await storage.SaveAndHashAsync(captureBytes,"native-review-capture.docx",c);
                p.NativeCaptureJson=JsonSerializer.Serialize(new OfficeCaptureEvidence(1,capture.StoragePath,capture.Sha256Hash.ToLowerInvariant(),capture.FileSize,target.CanonicalHash,ContinuousGreenEnvelope.ContentFingerprint(target)));
            }
            db.MatterOfficePreviews.Add(p); s.State = "Frozen"; s.Revision++;
            return p;
        }, ct);
    }

    public async Task<object> FrozenPreviewAsync(Guid id,Guid actor,CancellationToken ct)
    {
        var s=await AuthorizeAsync(id,actor,false,ct);
        if(s.State is not ("Frozen" or "Reviewed")) throw new MatterWorkflowException("There is no current frozen preview.",409);
        var p=await db.MatterOfficePreviews.AsNoTracking().SingleAsync(x=>x.SessionId==id && x.ActorUserId==actor && x.SnapshotId==s.LatestSnapshotId,ct);
        var snap=await db.MatterOfficeSnapshots.AsNoTracking().SingleAsync(x=>x.Id==p.SnapshotId && x.SessionId==id,ct);
        await OfficeNotingSubmission.VerifyBytesAsync(storage,snap,ct);
        if(s.CompositionVersion==ContinuousGreenEnvelope.Version)
        {
            var manifest=VerifyManifestSeal(s);await ContinuousGreenEnvelope.AuthoritativeAsync(db,s,ct);
            if(p.NativeCaptureJson is not null) await OfficeNativeCapture.VerifyAsync(storage,OfficeNativeCapture.Read(p.NativeCaptureJson),manifest,ct);
        }
        return new {session=Info(s),preview=p};
    }

    public Task<MatterWorkingNote> ConfirmAsync(Guid matter, ConfirmOfficeNoteCommand cmd, int revision, Guid actor, Guid key, CancellationToken ct) =>
        ConfirmCoreAsync(matter, cmd, revision, actor, key, ct);
    private async Task<MatterWorkingNote> ConfirmCoreAsync(Guid matter, ConfirmOfficeNoteCommand cmd, int revision, Guid actor, Guid key, CancellationToken ct)
    {
        var p = await db.MatterOfficePreviews.AsNoTracking().SingleOrDefaultAsync(x => x.Id == cmd.PreviewId && x.MatterId == matter && x.ActorUserId == actor, ct)
            ?? throw new MatterWorkflowException("Canonical preview unavailable.", 404);
        return await workflow.OfficeCommandAsync(matter, p.SourceDakId, actor, key, new { action = "OfficeConfirm", matter, cmd, revision }, async c =>
        {
            var s = await AuthorizeAsync(p.SessionId, actor, false, c);
            if (s.State != "Frozen" || p.WorkingRevision != revision || p.CanonicalHash != cmd.CanonicalHash) throw new MatterWorkflowException("Review the exact frozen canonical preview and current revision.", 409);
            var draft = await db.MatterWorkingNotes.SingleAsync(x => x.MatterId == matter && x.AuthorUserId == actor, c);
            if (draft.Revision != revision) throw new MatterWorkflowException("Working note changed.", 409);
            if (draft.CanonicalText != p.CanonicalText && draft.CitationsJson != "[]")
            {
                if (!cmd.ResetCitations) throw new MatterWorkflowException("Text changed: explicitly reset and re-anchor draft citations before submission.", 409);
                draft.CitationsJson = "[]";
            }
            foreach (var citation in JsonSerializer.Deserialize<List<NoteCitation>>(draft.CitationsJson, Json)!) NotingText.ValidateAnchor(p.CanonicalText, citation.Anchor);
            draft.ContentJson = p.ContentJson; draft.CanonicalText = p.CanonicalText; draft.OfficePreviewId = p.Id; draft.Revision++; draft.UpdatedAt = Now;
            s.State = "Reviewed"; s.WorkingRevision = draft.Revision; s.Revision++;
            return draft;
        }, ct);
    }

    private GreenEnvelopeManifest VerifyManifestSeal(MatterOfficeSession session)
    {
        var seal = tokens.Verify(session.HistoryManifestSeal ?? "");
        if (seal.GetProperty("purpose").GetString() != "green-envelope-manifest" || seal.GetProperty("sessionId").GetGuid() != session.Id ||
            seal.GetProperty("actor").GetGuid() != session.ActorUserId || seal.GetProperty("matter").GetGuid() != session.MatterId || seal.GetProperty("hash").GetString() != session.HistoryManifestHash)
            throw new MatterWorkflowException("Historical manifest seal mismatch.", 409);
        return ContinuousGreenEnvelope.Read(session.HistoryManifestJson!, session.HistoryManifestHash!);
    }

    public async Task<OfficeSessionInfo> RevokeAsync(Guid id, int revision, Guid actor, Guid key, CancellationToken ct)
    {
        var session = await db.MatterOfficeSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ActorUserId == actor, ct) ?? throw new MatterWorkflowException("Session unavailable.", 404);
        return await workflow.OfficeCommandAsync(session.MatterId, session.SourceDakId, actor, key, new { action = "OfficeRevoke", id, revision }, async c =>
        {
            var s = await db.MatterOfficeSessions.SingleAsync(x => x.Id == id, c);
            if (s.Revision != revision || s.State is "Submitted" or "Revoked") throw new MatterWorkflowException("Session changed, revoked or submitted.", 409);
            s.State = "Revoked"; s.Revision++;
            var draft = await db.MatterWorkingNotes.SingleAsync(x => x.MatterId == s.MatterId && x.AuthorUserId == actor, c);
            if (draft.OfficeSessionId == id) { draft.OfficeSessionId = null; draft.OfficePreviewId = null; draft.Revision++; }
            return Info(s);
        }, ct);
    }
}
