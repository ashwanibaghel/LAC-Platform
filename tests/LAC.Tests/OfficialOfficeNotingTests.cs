namespace LAC.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
using Xunit;

public sealed class OfficialOfficeNotingTests
{
    private sealed class MutableClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset now = initial;
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan duration) => now += duration;
    }
    private sealed class DocsStub : HttpMessageHandler, IHttpClientFactory
    {
        internal byte[] Bytes = [];
        internal int Error;
        public HttpClient CreateClient(string name) => new(this,false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = request.Method == HttpMethod.Post ? new StringContent("{\"error\":"+Error+"}",Encoding.UTF8,"application/json") : new ByteArrayContent(Bytes) });
    }
    private sealed record Fixture(OfficeNotingSchema Schema,Guid Actor,Guid Other,Guid Matter,Guid Dak,NotingTestStorage Storage,DocsStub Docs)
    {
        internal OnlyOfficeNotingService Service(LacDbContext db, TimeProvider? clock = null)
        {
            var options=Options.Create(new OnlyOfficeOptions { Enabled=true,BrowserUrl="http://localhost:8082",AppExternalUrl="http://localhost:5922",JwtSecret=new string('x',32) });
            var tokens=new OnlyOfficeTokens(options,clock ?? TimeProvider.System);
            var workflow=new MatterNotingWorkflow(db,new MatterAuthorizationService(db),new DakWorkflowService(db,Storage),Storage,NotingFixture.VillageAuth(db));
            return new(db,workflow,Storage,tokens,new(Docs,tokens,options,Options.Create(new OnlyOfficeNotingOptions { Enabled=true })),clock ?? TimeProvider.System);
        }
        internal MatterNotingWorkflow Workflow(LacDbContext db) => new(db,new MatterAuthorizationService(db),new DakWorkflowService(db,Storage),Storage,NotingFixture.VillageAuth(db));
        internal async Task<int> Callback(LacDbContext db,OfficeSessionInfo session,OfficeSaveInfo request,string text)
        {
            using var doc=OfficeNoteDocx.Create(MatterNotingTextTests.Text(text)); Docs.Bytes=doc.ToArray();
            return await Service(db).CallbackAsync(session.Id,JsonSerializer.SerializeToElement(new { key="lac-note-"+session.Id.ToString("N"),status=6,users=new[]{Actor.ToString()},userdata=request.Id.ToString(),url="http://localhost:8082/cache/test.docx" }),default);
        }
    }
    private static async Task<Fixture> Setup()
    {
        var schema=await OfficeNotingSchema.CreateAsync(); await using var db=schema.Context();
        var actor=await db.AppUsers.Where(x=>x.Username=="office.feasibility.editor").Select(x=>x.Id).SingleAsync();
        var other=await db.AppUsers.Where(x=>x.Username=="office.feasibility.viewer").Select(x=>x.Id).SingleAsync();
        var matter=await db.Matters.Where(x=>x.Title.Contains("MANUAL")).Select(x=>x.Id).SingleAsync();
        var dak=await db.DakMatterLinks.Where(x=>x.MatterId==matter).Select(x=>x.DakId).SingleAsync();
        var storage=new NotingTestStorage();
        var documentRoot=Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("LAC_OFFICE_TEST_BASELINE"))!,"documents");
        foreach(var doc in await db.MatterDocuments.Where(x=>x.MatterId==matter && x.Document.MimeType=="application/pdf").Select(x=>x.Document).ToListAsync())
        {
            if(Path.GetFileName(doc.StoragePath)!=doc.StoragePath) throw new InvalidOperationException("Unexpected synthetic storage path.");
            storage.Files[doc.StoragePath]=await File.ReadAllBytesAsync(Path.Combine(documentRoot,doc.StoragePath));
        }
        return new(schema,actor,other,matter,dak,storage,new());
    }
    [OfficeSchemaFact]
    public async Task Correlated_snapshot_review_atomic_numbered_submit_and_database_immutability()
    {
        var f=await Setup(); await using var db=f.Schema.Context(); var office=f.Service(db);
        var session=await office.OpenAsync(f.Matter,new(f.Dak),0,f.Actor,Guid.NewGuid(),default);
        Assert.Equal(403,(await Assert.ThrowsAsync<MatterWorkflowException>(()=>office.OpenAsync(f.Matter,new(f.Dak),0,f.Other,Guid.NewGuid(),default))).StatusCode);
        Assert.Equal(409,(await Assert.ThrowsAsync<MatterWorkflowException>(()=>f.Workflow(db).SaveAsync(f.Matter,new(MatterNotingTextTests.Text("bypass"),f.Dak),0,f.Actor,Guid.NewGuid(),default))).StatusCode);
        var save=await office.SaveAsync(session.Id,new(1),1,f.Actor,Guid.NewGuid(),default); Assert.Equal("Pending",save.State);
        Assert.Equal(409,(await Assert.ThrowsAsync<MatterWorkflowException>(()=>f.Workflow(db).SubmitAsync(f.Matter,new(f.Dak),0,f.Actor,Guid.NewGuid(),default))).StatusCode);
        Assert.Equal(0,await f.Callback(db,session,save,"Same 😀 Same\nReviewed note"));
        Assert.Equal(0,await f.Callback(db,session,save,"ignored duplicate replacement"));
        var receipt=await office.StatusAsync(session.Id,save.Id,f.Actor,default); Assert.Equal("Committed",receipt.State); Assert.Equal(1,receipt.Version);
        var preview=await office.PreviewAsync(session.Id,new(save.Id),receipt.SessionRevision,f.Actor,Guid.NewGuid(),default);
        Assert.Equal("Same 😀 Same\nReviewed note\n",preview.CanonicalText);
        Assert.Equal(409,(await Assert.ThrowsAsync<MatterWorkflowException>(()=>f.Callback(db,session,save,"late overwrite"))).StatusCode);
        var draft=await office.ConfirmAsync(f.Matter,new(preview.Id,preview.CanonicalHash),0,f.Actor,Guid.NewGuid(),default);
        var badSend=new SendWorkingNoteCommand(f.Dak,4,Guid.NewGuid(),f.Other,"Forwarded",false,null,null,preview.Id,preview.CanonicalHash);
        await Assert.ThrowsAsync<DakWorkflowException>(()=>f.Workflow(db).SendAsync(f.Matter,badSend,draft.Revision,f.Actor,Guid.NewGuid(),default));
        db.ChangeTracker.Clear(); Assert.Empty(await db.MatterOfficialNotes.ToListAsync()); Assert.Empty(await db.MatterOfficeNoteEvidence.ToListAsync());
        Assert.Equal("Reviewed",(await db.MatterOfficeSessions.SingleAsync()).State);
        var role=await db.UserRoles.Where(x=>x.UserId==f.Other).Select(x=>x.RoleId).SingleAsync();
        foreach(var permission in await db.Permissions.Where(x=>x.Code==PermissionCodes.DakView || x.Code==PermissionCodes.DakReceive).ToListAsync())
            db.RolePermissions.Add(new RolePermission {RoleId=role,PermissionId=permission.Id,ScopeMode=ScopeMode.All});
        await db.SaveChangesAsync();
        var desk=await db.UserDeskMemberships.Where(x=>x.UserId==f.Other && x.IsActive).Select(x=>x.OfficeDeskId).SingleAsync();
        var sendCommand=badSend with {ToDeskId=desk}; var key=Guid.NewGuid();
        var sent=await f.Workflow(db).SendAsync(f.Matter,sendCommand,draft.Revision,f.Actor,key,default);
        var replay=await f.Workflow(db).SendAsync(f.Matter,sendCommand,draft.Revision,f.Actor,key,default);
        Assert.Equal(sent.Note.Id,replay.Note.Id); Assert.Equal(sent.Dak.TransferId,replay.Dak.TransferId);
        Assert.Equal(1,sent.Note.Number); Assert.Equal(preview.CanonicalText,sent.Note.CanonicalText); Assert.Single(await db.MatterOfficeNoteEvidence.ToListAsync());
        Assert.Equal("Submitted",(await db.MatterOfficeSessions.SingleAsync()).State);
        Assert.Single(await db.DakTransfers.Where(x=>x.State==DakTransferState.Pending).ToListAsync());
        // Database-only physical evidence fixtures; no mocked renderer success is claimed.
        var source=new MatterGreenSheetSource {MatterId=f.Matter,ActorUserId=f.Actor,StoragePath="synthetic-source.docx",Sha256=new string('0',64),ManifestJson="{}",CreatedAt=DateTimeOffset.UtcNow};
        db.MatterGreenSheetSources.Add(source);db.MatterGreenSheets.Add(new MatterGreenSheet {SourceId=source.Id,PdfStoragePath="synthetic-output.pdf",PdfSha256=new string('0',64),FontManifestSha256=new string('0',64),RendererVersion="test",PageCount=1,CreatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();
        await using var conn=new NpgsqlConnection(f.Schema.Connection); await conn.OpenAsync();
        foreach(var sql in new[]{"UPDATE \"MatterOfficeSnapshots\" SET \"Version\"=99","DELETE FROM \"MatterOfficePreviews\"","UPDATE \"MatterOfficeNoteEvidence\" SET \"Sha256\"='bad'","DELETE FROM \"MatterOfficeSaveRequests\"","TRUNCATE \"MatterOfficeSnapshots\" CASCADE","UPDATE \"MatterOfficialNotes\" SET \"CanonicalText\"='bad'","UPDATE \"MatterGreenSheetSources\" SET \"Sha256\"='bad'","DELETE FROM \"MatterGreenSheets\"","TRUNCATE \"MatterGreenSheets\"","UPDATE \"MatterOfficeSessions\" SET \"State\"='Active',\"Revision\"=\"Revision\"+1"})
        { await using var command=new NpgsqlCommand(sql,conn); await Assert.ThrowsAsync<PostgresException>(()=>command.ExecuteNonQueryAsync()); }
    }
    [OfficeSchemaFact]
    public async Task Failure_retry_same_UUID_retains_snapshot_and_changed_payload_conflicts()
    {
        var f=await Setup(); await using var db=f.Schema.Context(); var office=f.Service(db);
        var s=await office.OpenAsync(f.Matter,new(f.Dak),0,f.Actor,Guid.NewGuid(),default);
        f.Docs.Error=4; var key=Guid.NewGuid(); var failed=await office.SaveAsync(s.Id,new(1),1,f.Actor,key,default); Assert.Equal("Failed",failed.State);
        Assert.Single(await db.MatterOfficeSnapshots.ToListAsync());
        Assert.Equal(409,(await Assert.ThrowsAsync<MatterWorkflowException>(()=>office.PreviewAsync(s.Id,new(failed.Id),2,f.Actor,Guid.NewGuid(),default))).StatusCode);
        Assert.Equal(409,(await Assert.ThrowsAsync<MatterWorkflowException>(()=>office.SaveAsync(s.Id,new(2),1,f.Actor,key,default))).StatusCode);
        f.Docs.Error=0; var retry=await office.SaveAsync(s.Id,new(1),1,f.Actor,key,default); Assert.Equal(failed.Id,retry.Id);
        Assert.Equal(0,await f.Callback(db,s,retry,"Retained retry text"));
        Assert.Equal("Committed",(await office.StatusAsync(s.Id,retry.Id,f.Actor,default)).State);
        Assert.Equal(2,await db.MatterOfficeSnapshots.CountAsync()); Assert.Single(await db.MatterOfficeSaveRequests.ToListAsync());
    }
    [OfficeSchemaFact]
    public async Task Concurrent_same_save_UUID_has_one_request_and_stale_revisions_fail()
    {
        var f=await Setup(); OfficeSessionInfo s;
        await using(var db=f.Schema.Context()) s=await f.Service(db).OpenAsync(f.Matter,new(f.Dak),0,f.Actor,Guid.NewGuid(),default);
        var key=Guid.NewGuid(); var results=await Task.WhenAll(Enumerable.Range(0,5).Select(async _=>{await using var db=f.Schema.Context();return await f.Service(db).SaveAsync(s.Id,new(1),1,f.Actor,key,default);}));
        Assert.Single(results.Select(x=>x.Id).Distinct()); await using var verify=f.Schema.Context(); Assert.Single(await verify.MatterOfficeSaveRequests.ToListAsync());
        Assert.Equal(409,(await Assert.ThrowsAsync<MatterWorkflowException>(()=>f.Service(verify).SaveAsync(s.Id,new(2),1,f.Actor,Guid.NewGuid(),default))).StatusCode);
    }
    [OfficeSchemaFact]
    public async Task Live_permission_and_login_revocation_reject_callbacks_without_losing_committed_bytes()
    {
        var f=await Setup(); await using var db=f.Schema.Context(); var office=f.Service(db); var s=await office.OpenAsync(f.Matter,new(f.Dak),0,f.Actor,Guid.NewGuid(),default);
        var save=await office.SaveAsync(s.Id,new(1),1,f.Actor,Guid.NewGuid(),default); var count=f.Storage.Files.Count;
        var user=await db.AppUsers.SingleAsync(x=>x.Id==f.Actor); user.SessionVersion=Guid.NewGuid(); await db.SaveChangesAsync();
        Assert.Equal(409,(await Assert.ThrowsAsync<MatterWorkflowException>(()=>f.Callback(db,s,save,"revoked"))).StatusCode);
        Assert.Equal(count,f.Storage.Files.Count); Assert.Empty(await db.MatterOfficialNotes.ToListAsync());
        var role=await db.UserRoles.Where(x=>x.UserId==f.Actor).Select(x=>x.RoleId).SingleAsync(); var grant=await db.RolePermissions.Include(x=>x.Permission).SingleAsync(x=>x.RoleId==role && x.Permission.Code==PermissionCodes.DraftEdit);
        db.RolePermissions.Remove(grant); await db.SaveChangesAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<MatterWorkflowException>(()=>f.Callback(db,s,save,"permission revoked"))).StatusCode);
        Assert.Equal(count,f.Storage.Files.Count);
    }
    [Fact] public void JWT_and_download_origin_fail_closed()
    {
        var options=Options.Create(new OnlyOfficeOptions { Enabled=true,BrowserUrl="http://localhost:8082",JwtSecret=new string('x',32) }); var tokens=new OnlyOfficeTokens(options,TimeProvider.System);
        Assert.Throws<UnauthorizedAccessException>(()=>tokens.Verify(tokens.Sign(new { exp=1,key="old" })));
        var token=tokens.Sign(new { key="valid" }); Assert.Throws<UnauthorizedAccessException>(()=>tokens.Verify(token[..^1]+(token[^1]=='a'?'b':'a')));
        var office=new NotingOfficeClient(new DocsStub(),tokens,options,Options.Create(new OnlyOfficeNotingOptions{Enabled=true}));
        foreach(var url in new[]{"http://evil.invalid/cache.docx","http://localhost:8083/cache.docx","http://user@localhost:8082/cache.docx","http://localhost:8082/cache.docx#fragment","file:///C:/secret"}) Assert.Throws<MatterWorkflowException>(()=>office.ValidateOrigin(url));
    }
    [OfficeSchemaFact]
    public async Task V2_tamper_callback_never_commits_and_manifest_is_database_immutable()
    {
        var f = await Setup(); await using var db = f.Schema.Context(); var service = f.Service(db);
        var historicalJson = MatterNotingTextTests.Text("Historical same same"); var historicalText = NotingText.Canonicalize(historicalJson);
        db.MatterOfficialNotes.Add(new MatterOfficialNote { MatterId = f.Matter, Number = 1, AuthorUserId = f.Actor, AuthorName = "Synthetic historical officer",
            Designation = "Kanungo", ContentJson = historicalJson, CanonicalText = historicalText, TextHash = NotingText.Hash(historicalText), SubmittedAt = DateTimeOffset.UnixEpoch });
        await db.SaveChangesAsync();
        var session = await service.OpenAsync(f.Matter, new(f.Dak, null, ContinuousGreenEnvelope.Version), 0, f.Actor, Guid.NewGuid(), default);
        var row = await db.MatterOfficeSessions.SingleAsync(x => x.Id == session.Id);
        var manifest = ContinuousGreenEnvelope.Read(row.HistoryManifestJson!, row.HistoryManifestHash!);
        Assert.Equal(ContinuousGreenEnvelope.Version, session.CompositionVersion); Assert.Single(manifest.Notes);
        Assert.NotNull(row.HistoryManifestSeal);
        using var bytes = ContinuousGreenEnvelope.Compose(manifest, MatterNotingTextTests.Text("Current 😀 same same"));
        f.Docs.Bytes = bytes.ToArray();
        Assert.Equal(428, (await Assert.ThrowsAsync<MatterWorkflowException>(() => service.SaveAsync(session.Id, new(1), 1, f.Actor, Guid.NewGuid(), default))).StatusCode);
        var nativeUrl = "http://localhost:8082/cache/files/lac-note-"+session.Id.ToString("N")+"_1/output.docx/Continuous%20Green%20Noting.docx";
        Assert.Equal(422,(await Assert.ThrowsAsync<MatterWorkflowException>(()=>service.SaveAsync(session.Id,new(1,"http://localhost:8082/healthcheck"),1,f.Actor,Guid.NewGuid(),default))).StatusCode);
        var save = await service.SaveAsync(session.Id, new(1, nativeUrl), 1, f.Actor, Guid.NewGuid(), default);
        using (var corrupt = new MemoryStream())
        {
            bytes.Position = 0; bytes.CopyTo(corrupt); corrupt.Position = 0;
            using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(corrupt, true))
            { package.MainDocumentPart!.Document!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().First().Text = "Tampered officer"; package.MainDocumentPart.Document.Save(); }
            f.Docs.Bytes = corrupt.ToArray();
        }
        var callback = JsonSerializer.SerializeToElement(new { key = "lac-note-" + session.Id.ToString("N"), status = 6, users = new[] { f.Actor.ToString() }, userdata = save.Id.ToString(), url = "http://localhost:8082/cache/test.docx" });
        var stored = f.Storage.Files.Count;
        Assert.Equal(422, (await Assert.ThrowsAsync<MatterWorkflowException>(() => service.CallbackAsync(session.Id, callback, default))).StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(stored, f.Storage.Files.Count); Assert.Single(await db.MatterOfficeSnapshots.ToListAsync());
        Assert.Equal("Pending", (await service.StatusAsync(session.Id, save.Id, f.Actor, default)).State);
        using (var stale = ContinuousGreenEnvelope.Compose(manifest, MatterNotingTextTests.Text("Missing final marker")))
        { f.Docs.Bytes = stale.ToArray(); Assert.Equal(422, (await Assert.ThrowsAsync<MatterWorkflowException>(() => service.CallbackAsync(session.Id, callback, default))).StatusCode); }
        const string changedMarks = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Current 😀 same same","marks":[{"type":"bold"}]}]}]}""";
        using (var stale = ContinuousGreenEnvelope.Compose(manifest, changedMarks))
        { f.Docs.Bytes = stale.ToArray(); Assert.Equal(422, (await Assert.ThrowsAsync<MatterWorkflowException>(() => service.CallbackAsync(session.Id, callback, default))).StatusCode); }
        db.ChangeTracker.Clear(); Assert.Single(await db.MatterOfficeSnapshots.ToListAsync());
        f.Docs.Bytes = bytes.ToArray(); Assert.Equal(0, await service.CallbackAsync(session.Id, callback, default));
        var receipt = await service.StatusAsync(session.Id, save.Id, f.Actor, default);
        var preview = await service.PreviewAsync(session.Id, new(save.Id), receipt.SessionRevision, f.Actor, Guid.NewGuid(), default);
        Assert.Equal("Current 😀 same same\n", preview.CanonicalText); Assert.Equal(ContinuousGreenEnvelope.Version, preview.ConverterVersion);
        Assert.Single(await db.MatterOfficialNotes.ToListAsync());
        await using var conn = new NpgsqlConnection(f.Schema.Connection); await conn.OpenAsync();
        await using var tamper = new NpgsqlCommand("UPDATE \"MatterOfficeSessions\" SET \"HistoryManifestJson\"='{}', \"Revision\"=\"Revision\"+1", conn);
        await Assert.ThrowsAsync<PostgresException>(() => tamper.ExecuteNonQueryAsync());
        await using var changeCapture = new NpgsqlCommand("UPDATE \"MatterOfficeSaveRequests\" SET \"NativeContentHash\"=repeat('A',64)", conn);
        await Assert.ThrowsAsync<PostgresException>(() => changeCapture.ExecuteNonQueryAsync());
    }
    [OfficeSchemaFact]
    public async Task V2_live_chain_change_blocks_writes_and_recovery_rewraps_only_current_content()
    {
        var f = await Setup(); await using var db = f.Schema.Context(); var service = f.Service(db);
        var session = await service.OpenAsync(f.Matter, new(f.Dak, null, ContinuousGreenEnvelope.Version), 0, f.Actor, Guid.NewGuid(), default);
        var lease = await db.MatterOfficeSessions.SingleAsync(x => x.Id == session.Id);
        var manifest = ContinuousGreenEnvelope.Read(lease.HistoryManifestJson!, lease.HistoryManifestHash!);
        using var bytes = ContinuousGreenEnvelope.Compose(manifest, MatterNotingTextTests.Text("Committed current survives")); f.Docs.Bytes = bytes.ToArray();
        var save = await service.SaveAsync(session.Id, new(1, "http://localhost:8082/cache/files/lac-note-"+session.Id.ToString("N")+"_1/output.docx/Continuous%20Green%20Noting.docx"), 1, f.Actor, Guid.NewGuid(), default);
        var callback = JsonSerializer.SerializeToElement(new { key = "lac-note-" + session.Id.ToString("N"), status = 6, users = new[] { f.Actor.ToString() }, userdata = save.Id.ToString(), url = "http://localhost:8082/cache/test.docx" });
        await service.CallbackAsync(session.Id, callback, default);
        var receipt = await service.StatusAsync(session.Id, save.Id, f.Actor, default);
        var json = MatterNotingTextTests.Text("New synthetic fixture chain entry"); var text = NotingText.Canonicalize(json);
        db.MatterOfficialNotes.Add(new MatterOfficialNote { MatterId = f.Matter, Number = 1, AuthorUserId = f.Actor, AuthorName = "Synthetic fixture",
            Designation = "Kanungo", ContentJson = json, CanonicalText = text, TextHash = NotingText.Hash(text), SubmittedAt = DateTimeOffset.UnixEpoch }); await db.SaveChangesAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<MatterWorkflowException>(() => service.SaveAsync(session.Id, new(2), receipt.SessionRevision, f.Actor, Guid.NewGuid(), default))).StatusCode);
        var fresh = await service.OpenAsync(f.Matter, new(f.Dak, null, ContinuousGreenEnvelope.Version), 0, f.Actor, Guid.NewGuid(), default);
        var freshRow = await db.MatterOfficeSessions.SingleAsync(x => x.Id == fresh.Id); var snap = await db.MatterOfficeSnapshots.SingleAsync(x => x.Id == fresh.LatestSnapshotId);
        var freshManifest = ContinuousGreenEnvelope.Read(freshRow.HistoryManifestJson!, freshRow.HistoryManifestHash!); Assert.Single(freshManifest.Notes);
        using var recovered = await f.Storage.OpenReadAsync(snap.StoragePath, default);
        Assert.Equal("Committed current survives\n", ContinuousGreenEnvelope.Convert(recovered!, freshManifest).CanonicalText);
        Assert.Equal("Revoked", (await db.MatterOfficeSessions.SingleAsync(x => x.Id == session.Id)).State);
    }
    [OfficeSchemaFact]
    public async Task V1_open_fingerprint_and_default_replay_remain_compatible()
    {
        var f = await Setup(); await using var db = f.Schema.Context(); var key = Guid.NewGuid(); var service = f.Service(db);
        var session = await service.OpenAsync(f.Matter, new(f.Dak), 0, f.Actor, key, default);
        var originalShape = new { action = "OfficeOpen", matter = f.Matter, cmd = new { SourceDakId = (Guid?)f.Dak, ResumeSnapshotId = (Guid?)null }, workingRevision = 0 };
        var expected = NotingText.Hash(JsonSerializer.Serialize(originalShape, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(expected, (await db.MatterWorkflowReceipts.SingleAsync(x => x.RequestId == key)).PayloadHash);
        Assert.Equal(session.Id, (await service.OpenAsync(f.Matter, new(f.Dak, null, OfficeNoteDocx.ConverterVersion), 0, f.Actor, key, default)).Id);
        Assert.Equal(422, (await Assert.ThrowsAsync<MatterWorkflowException>(() => service.OpenAsync(f.Matter, new(f.Dak, null, "Unknown"), 0, f.Actor, Guid.NewGuid(), default))).StatusCode);
        var saveKey = Guid.NewGuid(); await service.SaveAsync(session.Id,new(1),1,f.Actor,saveKey,default);
        var oldSave = new { action="OfficeSave", id=session.Id, cmd=new { ClientGeneration=1 }, revision=1 };
        Assert.Equal(NotingText.Hash(JsonSerializer.Serialize(oldSave,new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            (await db.MatterWorkflowReceipts.SingleAsync(x=>x.RequestId==saveKey)).PayloadHash);
    }
    private sealed class V4PreparationFactAttribute : FactAttribute
    {
        public V4PreparationFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("LAC_V4_PREPARE_ROOT") is null || Environment.GetEnvironmentVariable("LAC_OFFICE_TEST_CONNECTION") is null || Environment.GetEnvironmentVariable("LAC_OFFICE_TEST_BASELINE") is null)
                Skip = "Requires explicit isolated synthetic preparation; never creates/replaces operational accounts.";
        }
    }
    [V4PreparationFact]
    public async Task Prepare_fresh_synthetic_Matter_for_the_real_authenticated_native_checkpoint()
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("LAC_V4_PREPARE_ROOT")!);
        var allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex","artifacts") + Path.DirectorySeparatorChar;
        if (!root.StartsWith(allowed,StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(root,"fresh-host-manifest.json"))) throw new InvalidOperationException("Use a fresh private task root; preserve prior acceptance evidence.");
        var f=await Setup(); await using var db=f.Schema.Context();
        var template=await db.Matters.AsNoTracking().SingleAsync(x=>x.Id==f.Matter);
        var deskA=await db.UserDeskMemberships.Where(x=>x.UserId==f.Actor && x.IsActive).Select(x=>x.OfficeDeskId).SingleAsync();
        var deskB=await db.UserDeskMemberships.Where(x=>x.UserId==f.Other && x.IsActive).Select(x=>x.OfficeDeskId).SingleAsync();
        var matter=new Matter {VillageId=template.VillageId,WorkstreamId=template.WorkstreamId,NativeOwnerUserId=f.Actor,Title="SYNTHETIC V4 CONTINUOUS ACCEPTANCE " + Guid.NewGuid().ToString("N"),MatterType="General"};
        var document=await db.MatterDocuments.Where(x=>x.MatterId==f.Matter && x.Document.MimeType=="application/pdf").Select(x=>x.Document).FirstAsync();
        var role=await db.UserRoles.Where(x=>x.UserId==f.Other).Select(x=>x.RoleId).SingleAsync();
        string[] codes=[PermissionCodes.MatterView,PermissionCodes.MatterEdit,PermissionCodes.MatterDocumentManage,PermissionCodes.DraftView,PermissionCodes.DraftCreate,PermissionCodes.DraftEdit,PermissionCodes.DakView,PermissionCodes.DakReceive,PermissionCodes.DakMove];
        foreach(var p in await db.Permissions.Where(x=>codes.Contains(x.Code)).ToListAsync())
            if(!await db.RolePermissions.AnyAsync(x=>x.RoleId==role && x.PermissionId==p.Id)) db.RolePermissions.Add(new RolePermission {RoleId=role,PermissionId=p.Id,ScopeMode=ScopeMode.All});
        await db.SaveChangesAsync();
        var roleA=await db.UserRoles.Where(x=>x.UserId==f.Actor).Select(x=>x.RoleId).SingleAsync();
        string[] fixtureCodes=[PermissionCodes.DakRegister,PermissionCodes.DakMark,PermissionCodes.DakReceive,PermissionCodes.DakEdit];
        foreach(var p in await db.Permissions.Where(x=>fixtureCodes.Contains(x.Code)).ToListAsync())
            if(!await db.RolePermissions.AnyAsync(x=>x.RoleId==roleA && x.PermissionId==p.Id)) db.RolePermissions.Add(new RolePermission {RoleId=roleA,PermissionId=p.Id,ScopeMode=ScopeMode.All});
        await db.SaveChangesAsync();
        // Use real custody transitions; the database forbids fabricated confirmed assignments.
        var custody=new DakWorkflowService(db,f.Storage);
        using var input=new MemoryStream(f.Storage.Files[document.StoragePath]);
        var dak=await custody.RegisterAsync(new("SYN-V4-"+Guid.NewGuid().ToString("N"),DateOnly.FromDateTime(DateTime.UtcNow),matter.Title,"Synthetic office sample",null,null,null,null,null,"Email",DakPriority.Routine,null,null,template.WorkstreamId,input,"synthetic-receipt.pdf","application/pdf",Guid.NewGuid()),f.Actor);
        var marked=await custody.SendAsync(dak.Id,new(DakMovementAction.Marked,deskA,f.Actor,DakDestinationKind.Officer,false,null,null,dak.Revision,Guid.NewGuid()),f.Actor);
        var received=await custody.ReceiveAsync(dak.Id,marked.TransferId!.Value,new(marked.Revision,Guid.NewGuid()),f.Actor);
        await f.Workflow(db).ClassifyAsync(dak.Id,new("VillageSpecific",[template.VillageId],received.Revision),f.Actor,Guid.NewGuid(),default);
        db.Matters.Add(matter);db.DakMatterLinks.Add(new DakMatterLink {DakId=dak.Id,MatterId=matter.Id});
        db.MatterDocuments.Add(new MatterDocument {MatterId=matter.Id,DocumentId=document.Id,DocumentRole="Evidence",DisplayName="Original synthetic receipt PDF"});
        await db.SaveChangesAsync();
        Directory.CreateDirectory(Path.Combine(root,"api-host","documents"));
        foreach(var file in f.Storage.Files) File.WriteAllBytes(Path.Combine(root,"api-host","documents",file.Key),file.Value);
        var dakRevision=await db.Daks.Where(x=>x.Id==dak.Id).Select(x=>x.Revision).SingleAsync();
        File.WriteAllText(Path.Combine(root,"fresh-host-manifest.json"),JsonSerializer.Serialize(new {Schema=f.Schema.Name,Database="lac_onlyoffice_real_9b0459_20261012",DatabasePort=56683,MatterId=matter.Id,DakId=dak.Id,DakRevision=dakRevision,ActorA=f.Actor,ActorB=f.Other,DeskA=deskA,DeskB=deskB,DocumentId=document.Id,DocumentSha256=document.Sha256Hash,PreparedAt=DateTimeOffset.UtcNow,ExistingIdentitiesCloned=true,PublicSchemaUnchanged=true,ConfirmedViaRealReceipt=true},new JsonSerializerOptions {WriteIndented=true}));
        Assert.Empty(await db.MatterOfficialNotes.Where(x=>x.MatterId==matter.Id).ToListAsync());
        Assert.Equal(3,dakRevision);
    }
    [OfficeSchemaFact]
    public async Task Expired_lease_rejects_writes_review_download_and_config_and_recovers_only_committed_bytes()
    {
        var f = await Setup(); await using var db = f.Schema.Context();
        var clock = new MutableClock(DateTimeOffset.UtcNow); var service = f.Service(db, clock);
        var session = await service.OpenAsync(f.Matter, new(f.Dak), 0, f.Actor, Guid.NewGuid(), default);
        var config = JsonSerializer.SerializeToElement(await service.ConfigAsync(session.Id, f.Actor, default));
        var url = new Uri(config.GetProperty("config").GetProperty("document").GetProperty("url").GetString()!);
        var token = Uri.UnescapeDataString(url.Query["?token=".Length..]);
        var save = await service.SaveAsync(session.Id, new(1), 1, f.Actor, Guid.NewGuid(), default);
        using var doc = OfficeNoteDocx.Create(MatterNotingTextTests.Text("Synthetic committed expiry recovery")); f.Docs.Bytes = doc.ToArray();
        var callback = JsonSerializer.SerializeToElement(new { key = "lac-note-" + session.Id.ToString("N"), status = 6,
            users = new[] { f.Actor.ToString() }, userdata = save.Id.ToString(), url = "http://localhost:8082/cache/test.docx" });
        Assert.Equal(0, await service.CallbackAsync(session.Id, callback, default));
        var receipt = await service.StatusAsync(session.Id, save.Id, f.Actor, default);
        config = JsonSerializer.SerializeToElement(await service.ConfigAsync(session.Id, f.Actor, default));
        url = new Uri(config.GetProperty("config").GetProperty("document").GetProperty("url").GetString()!);
        token = Uri.UnescapeDataString(url.Query["?token=".Length..]);
        await using (var authorizedDownload = await service.FileAsync(session.Id, token, default))
            Assert.Equal(doc.Length, authorizedDownload.Length);
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(409, (await Assert.ThrowsAsync<MatterWorkflowException>(() => service.ConfigAsync(session.Id, f.Actor, default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<MatterWorkflowException>(() => service.SaveAsync(session.Id, new(2), receipt.SessionRevision, f.Actor, Guid.NewGuid(), default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<MatterWorkflowException>(() => service.StatusAsync(session.Id, save.Id, f.Actor, default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<MatterWorkflowException>(() => service.PreviewAsync(session.Id, new(save.Id), receipt.SessionRevision, f.Actor, Guid.NewGuid(), default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<MatterWorkflowException>(() => service.CallbackAsync(session.Id, callback, default))).StatusCode);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.FileAsync(session.Id, token, default));
        Assert.Equal(2, await db.MatterOfficeSnapshots.CountAsync()); Assert.Empty(await db.MatterOfficialNotes.ToListAsync());
        var recovery = await service.OpenAsync(f.Matter, new(f.Dak), 0, f.Actor, Guid.NewGuid(), default);
        Assert.NotEqual(session.Id, recovery.Id);
        Assert.Equal(receipt.Sha256, (await db.MatterOfficeSnapshots.SingleAsync(x => x.Id == recovery.LatestSnapshotId)).Sha256);
        Assert.Equal("Revoked", (await db.MatterOfficeSessions.SingleAsync(x => x.Id == session.Id)).State);
    }
    [OfficeSchemaFact]
    public async Task Exact_repeated_text_citations_survive_unchanged_conversion_and_changed_text_requires_explicit_reset()
    {
        foreach(var changed in new[]{false,true})
        {
            var f=await Setup(); await using var db=f.Schema.Context(); var workflow=f.Workflow(db); const string text="sample evidence. sample evidence.";
            await workflow.SaveAsync(f.Matter,new(MatterNotingTextTests.Text(text),f.Dak),0,f.Actor,Guid.NewGuid(),default);
            var document=await db.MatterDocuments.Where(x=>x.MatterId==f.Matter && x.RecordStatus==RecordStatus.Active && x.Document.MimeType=="application/pdf").Select(x=>x.DocumentId).FirstAsync();
            var draft=await workflow.CiteAsync(f.Matter,new(new(1,17,33,"sample evidence.",NotingText.Hash("sample evidence.")),document,1,null,1),f.Actor,Guid.NewGuid(),default);
            var citations=draft.CitationsJson; var office=f.Service(db); var session=await office.OpenAsync(f.Matter,new(f.Dak),2,f.Actor,Guid.NewGuid(),default);
            var save=await office.SaveAsync(session.Id,new(1),1,f.Actor,Guid.NewGuid(),default); await f.Callback(db,session,save,changed?"prefix "+text:text);
            var receipt=await office.StatusAsync(session.Id,save.Id,f.Actor,default); var preview=await office.PreviewAsync(session.Id,new(save.Id),receipt.SessionRevision,f.Actor,Guid.NewGuid(),default);
            if(changed)
            {
                Assert.Equal(409,(await Assert.ThrowsAsync<MatterWorkflowException>(()=>office.ConfirmAsync(f.Matter,new(preview.Id,preview.CanonicalHash),2,f.Actor,Guid.NewGuid(),default))).StatusCode);
                var reset=await office.ConfirmAsync(f.Matter,new(preview.Id,preview.CanonicalHash,true),2,f.Actor,Guid.NewGuid(),default); Assert.Equal("[]",reset.CitationsJson);
            }
            else
            {
                var confirmed=await office.ConfirmAsync(f.Matter,new(preview.Id,preview.CanonicalHash),2,f.Actor,Guid.NewGuid(),default); Assert.Equal(citations,confirmed.CitationsJson);
                var note=await workflow.SubmitAsync(f.Matter,new(f.Dak,preview.Id,preview.CanonicalHash),confirmed.Revision,f.Actor,Guid.NewGuid(),default); Assert.Equal(citations,note.CitationsJson);
                var remark=await workflow.RemarkAsync(f.Matter,note.Id,new(new(1,17,33,"sample evidence.",NotingText.Hash("sample evidence.")),"Second occurrence only"),f.Actor,Guid.NewGuid(),default);
                Assert.Equal(17,JsonSerializer.Deserialize<NoteAnchor>(remark.AnchorJson,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Start);
            }
        }
    }
    [OfficeSchemaFact]
    public async Task PostgreSQL_HTTP_headers_actor_binding_and_signed_callback_body()
    {
        var f=await Setup();
        await using var baseFactory=new MatterNotingApiTests.Factory(f.Schema.Connection);
        await using var factory=baseFactory.WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["OnlyOffice:Enabled"]="true",["OnlyOffice:BrowserUrl"]="http://localhost:8082",["OnlyOffice:AppExternalUrl"]="http://localhost:5922",
            ["OnlyOffice:JwtSecret"]=new string('x',32),["OnlyOfficeNoting:Enabled"]="true"
        })));
        using var client=factory.CreateClient(); var path=$"/api/matters/{f.Matter}/noting/office/sessions";
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync(path,new { sourceDakId=f.Dak })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Noting-Actor",f.Actor.ToString());
        Assert.Equal((HttpStatusCode)428,(await client.PostAsJsonAsync(path,new { sourceDakId=f.Dak })).StatusCode);
        client.DefaultRequestHeaders.Add("Idempotency-Key",Guid.NewGuid().ToString()); client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match","\"0\"");
        var opened=await client.PostAsJsonAsync(path,new { sourceDakId=f.Dak }); opened.EnsureSuccessStatusCode();
        var session=await opened.Content.ReadFromJsonAsync<OfficeSessionInfo>();
        var config=await client.GetFromJsonAsync<JsonElement>($"/api/noting-office/sessions/{session!.Id}/config"); Assert.True(config.GetProperty("config").GetProperty("document").GetProperty("permissions").GetProperty("edit").GetBoolean());
        var callback=$"/api/noting-office/sessions/{session.Id}/callback";
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync(callback,new { status=1,key="forged" })).StatusCode);
        var tokens=new OnlyOfficeTokens(Options.Create(new OnlyOfficeOptions{JwtSecret=new string('x',32)}),TimeProvider.System);
        using var request=new HttpRequestMessage(HttpMethod.Post,callback);
        request.Headers.Authorization=new("Bearer",tokens.Sign(new { payload=new { status=1,key="lac-note-"+session.Id.ToString("N") } }));
        request.Content=JsonContent.Create(new { status=6,key="forged",url="http://evil.invalid/" });
        var response=await client.SendAsync(request); response.EnsureSuccessStatusCode(); Assert.Equal(0,(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetInt32());
        using var oversized=new HttpRequestMessage(HttpMethod.Post,callback) { Content=JsonContent.Create(new { oversized=new string('x',140000) }) };
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await client.SendAsync(oversized)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Noting-Actor"); client.DefaultRequestHeaders.Add("X-Noting-Actor",f.Other.ToString());
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/noting-office/sessions/{session.Id}/config")).StatusCode);
        await using var db=f.Schema.Context(); Assert.Single(await db.MatterOfficeSnapshots.ToListAsync()); Assert.Empty(await db.MatterOfficialNotes.ToListAsync());
    }
}
