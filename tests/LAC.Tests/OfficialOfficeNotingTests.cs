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
        internal OnlyOfficeNotingService Service(LacDbContext db)
        {
            var options=Options.Create(new OnlyOfficeOptions { Enabled=true,BrowserUrl="http://localhost:8082",AppExternalUrl="http://localhost:5922",JwtSecret=new string('x',32) });
            var tokens=new OnlyOfficeTokens(options,TimeProvider.System);
            var workflow=new MatterNotingWorkflow(db,new MatterAuthorizationService(db),new DakWorkflowService(db,Storage),Storage,NotingFixture.VillageAuth(db));
            return new(db,workflow,Storage,tokens,new(Docs,tokens,options,Options.Create(new OnlyOfficeNotingOptions { Enabled=true })),TimeProvider.System);
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
        await Assert.ThrowsAnyAsync<Exception>(()=>f.Workflow(db).SendAsync(f.Matter,badSend,draft.Revision,f.Actor,Guid.NewGuid(),default));
        db.ChangeTracker.Clear(); Assert.Empty(await db.MatterOfficialNotes.ToListAsync()); Assert.Empty(await db.MatterOfficeNoteEvidence.ToListAsync());
        Assert.Equal("Reviewed",(await db.MatterOfficeSessions.SingleAsync()).State);
        var key=Guid.NewGuid(); var note=await f.Workflow(db).SubmitAsync(f.Matter,new(f.Dak,preview.Id,preview.CanonicalHash),draft.Revision,f.Actor,key,default);
        var replay=await f.Workflow(db).SubmitAsync(f.Matter,new(f.Dak,preview.Id,preview.CanonicalHash),draft.Revision,f.Actor,key,default);
        Assert.Equal(note.Id,replay.Id); Assert.Equal(1,note.Number); Assert.Equal(preview.CanonicalText,note.CanonicalText); Assert.Single(await db.MatterOfficeNoteEvidence.ToListAsync());
        await using var conn=new NpgsqlConnection(f.Schema.Connection); await conn.OpenAsync();
        foreach(var sql in new[]{"UPDATE \"MatterOfficeSnapshots\" SET \"Version\"=99","DELETE FROM \"MatterOfficePreviews\"","UPDATE \"MatterOfficeNoteEvidence\" SET \"Sha256\"='bad'","DELETE FROM \"MatterOfficeSaveRequests\"","TRUNCATE \"MatterOfficeSnapshots\" CASCADE","UPDATE \"MatterOfficialNotes\" SET \"CanonicalText\"='bad'"})
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
