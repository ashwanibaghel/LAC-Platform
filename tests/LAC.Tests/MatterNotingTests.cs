namespace LAC.Tests;

using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

public sealed class MatterNotingTextTests
{
    internal static string Text(string text) => JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text } } } } });
    [Fact] public void Canonical_multiline_UTF16_anchor_is_exact_and_surrogate_safe()
    {
        var text = NotingText.Canonicalize(Text("First\nSecond 😀")); Assert.Equal("First\nSecond 😀\n", text);
        NotingText.ValidateAnchor(text, new(1, 6, 15, "Second 😀", NotingText.Hash("Second 😀")));
        Assert.Throws<MatterWorkflowException>(() => NotingText.ValidateAnchor(text, new(1, 13, 14, "\ud83d", NotingText.Hash("\ud83d"))));
        Assert.Throws<MatterWorkflowException>(() => NotingText.ValidateAnchor(text, new(2, 0, 5, "First", NotingText.Hash("First"))));
        Assert.Throws<MatterWorkflowException>(() => NotingText.ValidateAnchor(text, new(1, 0, 5, "Other", NotingText.Hash("Other"))));
    }
    [Theory]
    [InlineData("{\"type\":\"script\"}")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"image\",\"attrs\":{\"src\":\"https://evil\"}}]}")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"x\",\"marks\":[{\"type\":\"link\",\"attrs\":{\"href\":\"javascript:alert(1)\"}}]}]}]}")]
    [InlineData("{\"type\":\"doc\",\"attrs\":{\"onclick\":\"alert(1)\"}}")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"heading\",\"attrs\":{\"level\":\"invalid\"}}]}")]
    public void Active_content_is_rejected(string json) => Assert.Throws<MatterWorkflowException>(() => NotingText.Canonicalize(json));
    [Fact] public void Coordinates_reject_outside_page_negative_and_nonfinite()
    {
        NotingText.ValidateRegion(1, new(.1, .1, .2, .2));
        foreach (var region in new[] { new PageRegion(.9, 0, .2, .1), new PageRegion(0, 0, 0, 1), new PageRegion(double.NaN, 0, .1, .1) })
            Assert.Throws<MatterWorkflowException>(() => NotingText.ValidateRegion(1, region));
        Assert.Throws<MatterWorkflowException>(() => NotingText.ValidateRegion(0, new(0, 0, 1, 1)));
    }
}

internal sealed record NotingFixture(CustodyActors Actors, Guid Village, Guid OtherVillage, Guid Stream, Guid Dak, Guid Matter)
{
    internal static readonly NotingTestStorage Storage = new();
    internal static ICourtAuthorizationService VillageAuth(LacDbContext db) => new CourtAuthorizationService(db, new WorkItemAuthorizationService(db), new MatterAuthorizationService(db), new EmptyServices());
    private sealed class EmptyServices : IServiceProvider { public object? GetService(Type type) => null; }
    internal static MatterNotingWorkflow Workflow(LacDbContext db) => new(db, new MatterAuthorizationService(db), new DakWorkflowService(db, Storage), Storage, VillageAuth(db));
    internal static async Task<NotingFixture> CreateAsync(LacDbContext db, bool paper = false)
    {
        var actors = await CustodyActors.CreateAsync(db);
        var role = await db.UserRoles.Where(x => x.UserId == actors.Receiver).Select(x => x.RoleId).SingleAsync();
        foreach (var p in await db.Permissions.Where(x => x.Code.StartsWith("Matter.") || x.Code.StartsWith("Draft.") || x.Code == PermissionCodes.VillageView).ToListAsync())
            db.RolePermissions.Add(new RolePermission { RoleId = role, PermissionId = p.Id, ScopeMode = ScopeMode.All });
        var district = new District { Name = "Test district" }; var sub = new SubDivision { Name = "Test subdivision", District = district };
        var village = new Village { Name = "Test village", SubDivision = sub }; var other = new Village { Name = "Other village", SubDivision = sub };
        db.Villages.AddRange(village, other); await db.SaveChangesAsync();
        var stream = await db.Workstreams.Where(x => x.Code == WorkstreamCodes.DakCorrespondence).Select(x => x.Id).SingleAsync();
        var dak = await actors.RegisterAsync(db, paper);
        var dw = new DakWorkflowService(db, new TestInMemoryDocumentStorage()); var sent = await dw.SendAsync(dak, actors.Mark(paper), actors.Sender);
        await dw.ReceiveAsync(dak, sent.TransferId!.Value, new(1, Guid.NewGuid(), paper), actors.Receiver);
        var workflow = Workflow(db);
        await workflow.ClassifyAsync(dak, new("VillageSpecific", [village.Id], 2), actors.Receiver, Guid.NewGuid(), default);
        var workspace = await workflow.WorkspaceAsync(dak, new(village.Id, stream, null, null, null, 3), actors.Receiver, Guid.NewGuid(), default);
        return new(actors, village.Id, other.Id, stream, dak, workspace.MatterId);
    }
    internal Task<MatterWorkingNote> SaveAsync(LacDbContext db, int revision = 0, string text = "Please review the evidence.") =>
        Workflow(db).SaveAsync(Matter, new(MatterNotingTextTests.Text(text), Dak), revision, Actors.Receiver, Guid.NewGuid(), default);
    internal SendWorkingNoteCommand Send(int revision = 4, bool paper = false) => new(Dak, revision, Actors.SenderDesk, Actors.Sender, "Forwarded", paper, "Review", "For orders");
}

public sealed class MatterNotingPostgresTests
{
    [DakPostgresFact]
    public async Task Explicit_source_Dak_file_sharing_reuses_binary_and_checks_live_Dak_access()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); await using var db = database.Context(); var f = await NotingFixture.CreateAsync(db);
        var storage = new NotingTestStorage(); var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder(); builder.AddPage(100, 100);
        var bytes = builder.Build(); using var stream = new MemoryStream(bytes); var saved = await storage.SaveAndHashAsync(stream, "receipt.pdf", default);
        var doc = new Document { OriginalFileName = "receipt.pdf", StoragePath = saved.StoragePath, Sha256Hash = saved.Sha256Hash, MimeType = "application/pdf", Status = "Active" };
        db.Documents.Add(doc); (await db.Daks.SingleAsync(x => x.Id == f.Dak)).MainDocumentId = doc.Id;
        await db.SaveChangesAsync();
        var unrelated = await f.Actors.RegisterAsync(db); var otherDoc = new Document { OriginalFileName = "unrelated.pdf", Status = "Active" };
        db.Documents.Add(otherDoc); (await db.Daks.SingleAsync(x => x.Id == unrelated)).MainDocumentId = otherDoc.Id; await db.SaveChangesAsync();
        var matter = await db.Matters.SingleAsync(x => x.Id == f.Matter);
        var candidates = await MatterDocumentProvenanceHelper.GetEligibleDocumentCandidateMapAsync(db, matter, new DeniedAccess(), default, f.Actors.Receiver);
        Assert.Contains(doc.Id, candidates.Keys); Assert.DoesNotContain(otherDoc.Id, candidates.Keys);
        var role = await db.UserRoles.Where(x => x.UserId == f.Actors.Receiver).Select(x => x.RoleId).SingleAsync();
        var grant = await db.RolePermissions.Include(x => x.Permission).SingleAsync(x => x.RoleId == role && x.Permission.Code == PermissionCodes.DakView);
        db.RolePermissions.Remove(grant); await db.SaveChangesAsync();
        var auth = new MatterAuthorizationService(db); var mw = new MatterWorkflowService(db, storage, auth, new DeniedAccess());
        Assert.Equal(400, (await Assert.ThrowsAsync<MatterWorkflowException>(() => mw.LinkExistingDocumentAsync(f.Matter, new(doc.Id, null, null, 1), f.Actors.Receiver))).StatusCode);
        db.ChangeTracker.Clear(); db.RolePermissions.Add(new RolePermission { RoleId = role, PermissionId = grant.PermissionId, ScopeMode = grant.ScopeMode }); await db.SaveChangesAsync();
        var linked = await mw.LinkExistingDocumentAsync(f.Matter, new(doc.Id, "Receipt", null, 1), f.Actors.Receiver);
        Assert.Equal(doc.Id, linked.DocumentId); Assert.Equal(2, await db.Documents.CountAsync()); Assert.Single(storage.Files);
        using var repeated = new MemoryStream(bytes);
        var reused = await mw.UploadDocumentAsync(f.Matter, new(repeated, "receipt-copy.pdf", "application/pdf", null, null, 2), f.Actors.Receiver);
        Assert.Equal(doc.Id, reused.DocumentId); Assert.Single(storage.Files);
        await f.SaveAsync(db, text: "Receipt evidence");
        var noting = new MatterNotingWorkflow(db, auth, new DakWorkflowService(db, storage), storage, NotingFixture.VillageAuth(db));
        await noting.CiteAsync(f.Matter, new(new(1, 0, 7, "Receipt", NotingText.Hash("Receipt")), doc.Id, 1, null, 1), f.Actors.Receiver, Guid.NewGuid(), default);
        var note = await noting.SubmitAsync(f.Matter, new(f.Dak), 2, f.Actors.Receiver, Guid.NewGuid(), default); Assert.Contains(doc.Id.ToString(), note.CitationsJson);
        db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == role && x.PermissionId == grant.PermissionId)); await db.SaveChangesAsync();
        Assert.True(await auth.CanAccessMatterDocumentAsync(f.Matter, doc.Id, f.Actors.Receiver));
        Assert.Contains(doc.Id.ToString(), (await db.MatterOfficialNotes.SingleAsync()).DocumentManifestJson);
    }

    [DakPostgresFact]
    public async Task Permission_revoked_while_waiting_for_Matter_lock_denies_autosave()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); NotingFixture f;
        await using (var setup = database.Context()) { f = await NotingFixture.CreateAsync(setup); }
        await using var blocker = database.Context(); await using var tx = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Matters\" WHERE \"Id\" = {f.Matter} FOR UPDATE");
        await using var editor = database.Context(); var save = f.SaveAsync(editor); Assert.False(save.IsCompleted);
        await blocker.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"RolePermissions\" WHERE \"RoleId\" IN (SELECT \"RoleId\" FROM \"UserRoles\" WHERE \"UserId\" = {f.Actors.Receiver}) AND \"PermissionId\" IN (SELECT \"Id\" FROM \"Permissions\" WHERE \"Code\" = 'Draft.Edit')");
        await tx.CommitAsync(); Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => save)).StatusCode);
        Assert.Empty(await editor.MatterWorkingNotes.ToListAsync());
    }

    [DakPostgresFact]
    public async Task Managed_Village_allocation_and_active_workstream_are_live_on_every_noting_request()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); await using var db = database.Context(); var f = await NotingFixture.CreateAsync(db); var w = NotingFixture.Workflow(db);
        var user = await db.AppUsers.SingleAsync(x => x.Id == f.Actors.Receiver); user.OfficeAccessManaged = true; await db.SaveChangesAsync();
        await f.SaveAsync(db); var allocations = await db.WorkAllocations.Where(x => x.UserId == user.Id).ToListAsync();
        foreach (var allocation in allocations) allocation.RevokedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => w.DraftAsync(f.Matter, user.Id, default))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => w.NativeAsync(f.OtherVillage, new(f.Stream, "Outside allocation", null), user.Id, Guid.NewGuid(), default))).StatusCode);
        db.ChangeTracker.Clear(); user = await db.AppUsers.SingleAsync(x => x.Id == f.Actors.Receiver); user.OfficeAccessManaged = false;
        var stream = await db.Workstreams.SingleAsync(x => x.Id == f.Stream); stream.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => w.WorkspaceReadAsync(f.Matter, f.Dak, user.Id, default))).StatusCode);
    }

    [DakPostgresFact]
    public async Task Explicit_multiVillage_choice_and_create_retry_never_infer_or_duplicate_Matter()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); await using var db = database.Context(); var f = await NotingFixture.CreateAsync(db); var w = NotingFixture.Workflow(db);
        var fresh = await f.Actors.RegisterAsync(db); var dw = new DakWorkflowService(db, NotingFixture.Storage);
        await w.ClassifyAsync(fresh, new("MultiVillage", [f.Village, f.OtherVillage], 0), f.Actors.Sender, Guid.NewGuid(), default);
        var mark = await dw.SendAsync(fresh, f.Actors.Mark() with { ExpectedRevision = 1 }, f.Actors.Sender);
        Assert.Single(await db.Matters.ToListAsync());
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => w.WorkspaceAsync(fresh, new(f.OtherVillage, f.Stream, null, null, null, 2), f.Actors.Receiver, Guid.NewGuid(), default))).StatusCode);
        await dw.ReceiveAsync(fresh, mark.TransferId!.Value, new(2, Guid.NewGuid()), f.Actors.Receiver);
        var key = Guid.NewGuid(); var cmd = new MatterWorkspaceCommand(f.OtherVillage, f.Stream, null, null, null, 3);
        var created = await w.WorkspaceAsync(fresh, cmd, f.Actors.Receiver, key, default);
        Assert.Equal(created, await w.WorkspaceAsync(fresh, cmd, f.Actors.Receiver, key, default)); Assert.Equal(2, await db.Matters.CountAsync());
        Assert.Equal(f.OtherVillage, (await db.Matters.SingleAsync(x => x.Id == created.MatterId)).VillageId);
        var options = System.Text.Json.JsonSerializer.Serialize(await w.OptionsAsync(fresh, f.OtherVillage, null, f.Actors.Receiver, default));
        Assert.Contains(created.MatterId.ToString(), options); Assert.DoesNotContain(f.Matter.ToString(), options);
        await Assert.ThrowsAsync<MatterWorkflowException>(() => w.WorkspaceAsync(fresh, cmd with { ExistingMatterId = f.Matter, ExpectedRevision = 4 }, f.Actors.Receiver, Guid.NewGuid(), default));
        Assert.Equal(2, await db.Matters.CountAsync());
    }

    [DakPostgresFact]
    public async Task PDF_Word_upload_dedupe_and_source_family_permission_revocation()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); await using var db = database.Context(); var f = await NotingFixture.CreateAsync(db);
        var storage = new NotingTestStorage(); var auth = new MatterAuthorizationService(db); var mw = new MatterWorkflowService(db, storage, auth);
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder(); builder.AddPage(100, 100); var bytes = builder.Build();
        using var pdf1 = new MemoryStream(bytes); var first = await mw.UploadDocumentAsync(f.Matter, new(pdf1, "one.pdf", "application/pdf", "Evidence", null, 1), f.Actors.Receiver);
        using var pdf2 = new MemoryStream(bytes); var duplicate = await mw.UploadDocumentAsync(f.Matter, new(pdf2, "same.pdf", "application/pdf", "Evidence", null, 2), f.Actors.Receiver);
        Assert.Equal(first.DocumentId, duplicate.DocumentId); Assert.Single(await db.Documents.ToListAsync());
        using var wordStream = new MemoryStream(); using (var word = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(wordStream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        { var part = word.AddMainDocumentPart(); part.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new DocumentFormat.OpenXml.Wordprocessing.Body(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("Letter"))))); part.Document.Save(); }
        wordStream.Position = 0;
        var docx = await mw.UploadDocumentAsync(f.Matter, new(wordStream, "letter.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "Letter", null, 2), f.Actors.Receiver);
        Assert.True(await auth.CanAccessMatterDocumentAsync(f.Matter, docx.DocumentId, f.Actors.Receiver));
        using var forged = new MemoryStream("MZ executable"u8.ToArray()); await Assert.ThrowsAsync<MatterWorkflowException>(() => mw.UploadDocumentAsync(f.Matter, new(forged, "fake.pdf", "application/pdf", null, null, 3), f.Actors.Receiver));
        var award = new Award { AwardNumber = "Test/Award" }; db.Awards.Add(award); db.AwardVillages.Add(new AwardVillage { Award = award, VillageId = f.Village });
        db.MatterAwards.Add(new MatterAward { MatterId = f.Matter, Award = award }); db.DocumentAwards.Add(new DocumentAward { DocumentId = first.DocumentId, Award = award });
        var role = await db.UserRoles.Where(x => x.UserId == f.Actors.Receiver).Select(x => x.RoleId).SingleAsync(); var awardView = await db.Permissions.Where(x => x.Code == PermissionCodes.AwardView).Select(x => x.Id).SingleAsync();
        db.RolePermissions.Add(new RolePermission { RoleId = role, PermissionId = awardView, ScopeMode = ScopeMode.All }); await db.SaveChangesAsync();
        Assert.True(await auth.CanAccessMatterDocumentAsync(f.Matter, first.DocumentId, f.Actors.Receiver));
        db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == role && x.PermissionId == awardView)); await db.SaveChangesAsync();
        Assert.False(await auth.CanAccessMatterDocumentAsync(f.Matter, first.DocumentId, f.Actors.Receiver));
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => mw.ExtractAndAttachMatterDocumentPagesAsync(f.Matter,
            new(first.DocumentId, "1", null, null, null, null, null, 3), f.Actors.Receiver))).StatusCode);
        using var revokedCopy = new MemoryStream(bytes);
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => mw.UploadDocumentAsync(f.Matter, new(revokedCopy, "copy.pdf", "application/pdf", null, null, 3), f.Actors.Receiver))).StatusCode);
        var noting = new MatterNotingWorkflow(db, auth, new DakWorkflowService(db, storage), storage, NotingFixture.VillageAuth(db));
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => noting.AnnotationsAsync(f.Matter, first.DocumentId, f.Actors.Receiver, default))).StatusCode);
    }

    [DakPostgresFact]
    public async Task Durable_autosave_recovery_conflict_private_draft_and_immutable_numbered_chain()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); NotingFixture f; Guid key = Guid.NewGuid(); SaveWorkingNoteCommand save;
        await using (var db = database.Context())
        {
            f = await NotingFixture.CreateAsync(db); save = new(MatterNotingTextTests.Text("Note one"), f.Dak);
            var draft = await NotingFixture.Workflow(db).SaveAsync(f.Matter, save, 0, f.Actors.Receiver, key, default); Assert.Equal(1, draft.Revision);
        }
        await using var recovered = database.Context(); var w = NotingFixture.Workflow(recovered);
        var durable = await w.DraftAsync(f.Matter, f.Actors.Receiver, default); Assert.Equal("Note one\n", durable!.CanonicalText);
        Assert.Null(await w.DraftAsync(f.Matter, f.Actors.Sender, default));
        Assert.Equal(durable.Id, (await w.SaveAsync(f.Matter, save, 0, f.Actors.Receiver, key, default)).Id);
        Assert.Equal(409, (await Assert.ThrowsAsync<MatterWorkflowException>(() => w.SaveAsync(f.Matter, save with { ContentJson = MatterNotingTextTests.Text("Changed") }, 0, f.Actors.Receiver, key, default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<MatterWorkflowException>(() => f.SaveAsync(recovered, 0))).StatusCode);
        var n1 = await w.SubmitAsync(f.Matter, new(f.Dak), 1, f.Actors.Receiver, Guid.NewGuid(), default); Assert.Equal(1, n1.Number);
        Assert.Equal("Receiver", n1.AuthorName); Assert.Equal(f.Actors.ReceiverDesk, n1.DeskId);
        await f.SaveAsync(recovered, 2, "Note two"); var n2 = await w.SubmitAsync(f.Matter, new(f.Dak), 3, f.Actors.Receiver, Guid.NewGuid(), default); Assert.Equal(2, n2.Number);
        recovered.ChangeTracker.Clear(); var official = await recovered.MatterOfficialNotes.FirstAsync(); official.ContentJson = "forged";
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovered.SaveChangesAsync()); recovered.ChangeTracker.Clear();
        await Assert.ThrowsAsync<PostgresException>(() => recovered.Database.ExecuteSqlRawAsync("UPDATE \"MatterOfficialNotes\" SET \"CanonicalText\" = 'forged'"));
        await Assert.ThrowsAsync<PostgresException>(() => recovered.Database.ExecuteSqlRawAsync("DELETE FROM \"MatterOfficialNotes\""));
        Assert.Equal(2, await recovered.MatterOfficialNotes.CountAsync());
    }

    [DakPostgresFact]
    public async Task Concurrent_submit_assigns_one_number_and_idempotent_retries_return_same_note()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); NotingFixture f;
        await using (var db = database.Context()) { f = await NotingFixture.CreateAsync(db); await f.SaveAsync(db); }
        var key = Guid.NewGuid();
        var notes = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => { await using var db = database.Context(); return await NotingFixture.Workflow(db).SubmitAsync(f.Matter, new(f.Dak), 1, f.Actors.Receiver, key, default); }));
        Assert.All(notes, n => Assert.Equal(notes[0].Id, n.Id));
        await using var verify = database.Context(); Assert.Single(await verify.MatterOfficialNotes.ToListAsync()); await f.SaveAsync(verify, 2);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => { await using var db = database.Context(); try { await NotingFixture.Workflow(db).SubmitAsync(f.Matter, new(f.Dak), 3, f.Actors.Receiver, Guid.NewGuid(), default); return 200; } catch (MatterWorkflowException ex) { return ex.StatusCode; } }));
        Assert.Equal(1, attempts.Count(x => x == 200)); Assert.Equal(3, attempts.Count(x => x == 409));
        Assert.Equal(new[] { 1, 2 }, await verify.MatterOfficialNotes.OrderBy(x => x.Number).Select(x => x.Number).ToArrayAsync());
    }

    [DakPostgresFact]
    public async Task Send_is_atomic_preserves_paper_receipt_and_only_routes_selected_Dak()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); await using var db = database.Context(); var f = await NotingFixture.CreateAsync(db, true);
        var second = await f.Actors.RegisterAsync(db); var dw = new DakWorkflowService(db, new TestInMemoryDocumentStorage()); var transfer = await dw.SendAsync(second, f.Actors.Mark(), f.Actors.Sender);
        await dw.ReceiveAsync(second, transfer.TransferId!.Value, new(1, Guid.NewGuid()), f.Actors.Receiver);
        var w = NotingFixture.Workflow(db); await w.ClassifyAsync(second, new("VillageSpecific", [f.Village], 2), f.Actors.Receiver, Guid.NewGuid(), default);
        await w.WorkspaceAsync(second, new(f.Village, f.Stream, f.Matter, null, null, 3), f.Actors.Receiver, Guid.NewGuid(), default);
        await f.SaveAsync(db);
        var bad = f.Send() with { ToUserId = f.Actors.Supervisor };
        await Assert.ThrowsAsync<DakWorkflowException>(() => w.SendAsync(f.Matter, bad, 1, f.Actors.Receiver, Guid.NewGuid(), default));
        db.ChangeTracker.Clear(); Assert.Empty(await db.MatterOfficialNotes.ToListAsync()); Assert.Equal(1, (await w.DraftAsync(f.Matter, f.Actors.Receiver, default))!.Revision);
        Assert.Equal(DakRoutingState.WithHolder, (await db.Daks.SingleAsync(x => x.Id == f.Dak)).RoutingState);
        await Assert.ThrowsAsync<MatterWorkflowException>(() => w.SendAsync(f.Matter, f.Send(revision: 3), 1, f.Actors.Receiver, Guid.NewGuid(), default));
        db.ChangeTracker.Clear(); Assert.Empty(await db.MatterOfficialNotes.ToListAsync());
        var key = Guid.NewGuid(); var sent = await w.SendAsync(f.Matter, f.Send(paper: true), 1, f.Actors.Receiver, key, default);
        Assert.Equal(sent.Note.Id, (await w.SendAsync(f.Matter, f.Send(paper: true), 1, f.Actors.Receiver, key, default)).Note.Id);
        db.ChangeTracker.Clear(); Assert.Equal(DakRoutingState.WithHolder, (await db.Daks.SingleAsync(x => x.Id == second)).RoutingState);
        Assert.Equal(f.Actors.Receiver, (await db.Daks.SingleAsync(x => x.Id == f.Dak)).PhysicalOriginalUserId);
        await Assert.ThrowsAsync<DakWorkflowException>(() => dw.ReceiveAsync(f.Dak, sent.Dak.TransferId!.Value, new(5, Guid.NewGuid()), f.Actors.Sender));
        await dw.ReceiveAsync(f.Dak, sent.Dak.TransferId!.Value, new(5, Guid.NewGuid(), true), f.Actors.Sender);
        await w.SaveAsync(f.Matter, new(MatterNotingTextTests.Text("Next officer"), f.Dak), 0, f.Actors.Sender, Guid.NewGuid(), default);
        var note2 = await w.SubmitAsync(f.Matter, new(f.Dak), 1, f.Actors.Sender, Guid.NewGuid(), default); Assert.Equal(2, note2.Number);
        await dw.ResolveAsync(f.Dak, new(true, "Finished", 6, Guid.NewGuid()), f.Actors.Sender);
        Assert.Equal(2, await db.MatterOfficialNotes.CountAsync()); Assert.Single(await db.Matters.ToListAsync());
        await dw.ReopenAsync(f.Dak, new("More work", 7, Guid.NewGuid()), f.Actors.Supervisor);
        Assert.Equal(2, await db.MatterOfficialNotes.CountAsync());
    }

    [DakPostgresFact]
    public async Task Different_concurrent_send_keys_and_submit_vs_send_have_one_winner()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); NotingFixture f;
        await using (var db = database.Context()) { f = await NotingFixture.CreateAsync(db); await f.SaveAsync(db); }
        var attempts = await Task.WhenAll(Enumerable.Range(0, 3).Select(async i => { await using var db = database.Context(); var w = NotingFixture.Workflow(db); try {
            if (i == 0) await w.SubmitAsync(f.Matter, new(f.Dak), 1, f.Actors.Receiver, Guid.NewGuid(), default);
            else await w.SendAsync(f.Matter, f.Send(), 1, f.Actors.Receiver, Guid.NewGuid(), default); return 200;
        } catch (MatterWorkflowException ex) { return ex.StatusCode; } catch (DakWorkflowException ex) { return ex.StatusCode; } }));
        Assert.Equal(1, attempts.Count(x => x == 200)); Assert.All(attempts.Where(x => x != 200), x => Assert.True(x is 403 or 409));
        await using var verify = database.Context(); Assert.Single(await verify.MatterOfficialNotes.ToListAsync());
        Assert.InRange(await verify.DakTransfers.CountAsync(), 1, 2);
    }

    [DakPostgresFact]
    public async Task Village_validation_holder_RBAC_and_legacy_link_bypass_are_denied()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); await using var db = database.Context(); var f = await NotingFixture.CreateAsync(db); var w = NotingFixture.Workflow(db);
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => w.SaveAsync(f.Matter, new(MatterNotingTextTests.Text("forged"), f.Dak), 0, f.Actors.Supervisor, Guid.NewGuid(), default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<MatterWorkflowException>(() => w.WorkspaceAsync(f.Dak, new(f.OtherVillage, f.Stream, null, null, null, 4), f.Actors.Receiver, Guid.NewGuid(), default))).StatusCode);
        await Assert.ThrowsAsync<MatterWorkflowException>(() => w.ClassifyAsync(f.Dak, new("General", [], 4), f.Actors.Receiver, Guid.NewGuid(), default));
        var fresh = await f.Actors.RegisterAsync(db); Assert.Single(await db.Matters.ToListAsync());
        var existing = await db.Matters.SingleAsync(); await Assert.ThrowsAsync<MatterWorkflowException>(() => DakMatterGuard.ValidateLinkAsync(db, fresh, existing, f.Actors.Receiver, default));
        var ws = new MatterWorkflowService(db, new TestInMemoryDocumentStorage(), new MatterAuthorizationService(db), new DeniedAccess(), dakAuth: new DakAuthorizationService(db));
        await Assert.ThrowsAsync<MatterWorkflowException>(() => ws.ChangeContextLinkAsync(f.Matter, MatterContextKind.Dak, fresh, true, new(1), f.Actors.Receiver));
        var key = Guid.NewGuid(); var cmd = new NativeMatterCommand(f.Stream, "Village native", null);
        var villageRole = await db.UserRoles.Where(x => x.UserId == f.Actors.Receiver).Select(x => x.RoleId).SingleAsync();
        var villageGrant = await db.RolePermissions.SingleAsync(x => x.RoleId == villageRole && x.Permission.Code == PermissionCodes.VillageView);
        db.RolePermissions.Remove(villageGrant); await db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => w.NativeAsync(f.Village, cmd, f.Actors.Receiver, key, default))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => w.WorkspaceAsync(f.Dak, new(f.Village, f.Stream, null, null, null, 4), f.Actors.Receiver, Guid.NewGuid(), default))).StatusCode);
        Assert.Single(await db.Matters.ToListAsync());
        db.RolePermissions.Add(new RolePermission { RoleId = villageRole, PermissionId = villageGrant.PermissionId, ScopeMode = villageGrant.ScopeMode }); await db.SaveChangesAsync();
        var native = await w.NativeAsync(f.Village, cmd, f.Actors.Receiver, key, default);
        Assert.Equal(native, await w.NativeAsync(f.Village, cmd, f.Actors.Receiver, key, default));
        await w.SaveAsync(native.MatterId, new(MatterNotingTextTests.Text("No synthetic Dak"), null), 0, f.Actors.Receiver, Guid.NewGuid(), default);
        await w.SubmitAsync(native.MatterId, new(null), 1, f.Actors.Receiver, Guid.NewGuid(), default);
        Assert.Equal(2, await db.Daks.CountAsync());
        await Assert.ThrowsAsync<MatterWorkflowException>(() => w.SaveAsync(native.MatterId, new(MatterNotingTextTests.Text("Someone else"), null), 0, f.Actors.Sender, Guid.NewGuid(), default));
        var role = await db.UserRoles.Where(x => x.UserId == f.Actors.Receiver).Select(x => x.RoleId).SingleAsync();
        var permission = await db.RolePermissions.SingleAsync(x => x.RoleId == role && x.Permission.Code == PermissionCodes.DraftEdit); db.RolePermissions.Remove(permission); await db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => f.SaveAsync(db))).StatusCode);
    }

    [DakPostgresFact]
    public async Task Anchored_remarks_citations_and_highlights_survive_additions_and_revoke_on_unlink()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); await using var db = database.Context(); var f = await NotingFixture.CreateAsync(db); var w = NotingFixture.Workflow(db);
        var doc = new Document { OriginalFileName = "evidence.pdf", DocumentType = "MatterDocument", Sha256Hash = "ABC", MimeType = "application/pdf" };
        var pdfBuilder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder(); pdfBuilder.AddPage(100, 100);
        using var pdfStream = new MemoryStream(pdfBuilder.Build()); doc.StoragePath = await NotingFixture.Storage.SaveAsync(pdfStream, "evidence.pdf", default);
        var word = new Document { OriginalFileName = "letter.docx", DocumentType = "MatterDocument" };
        db.Documents.AddRange(doc, word); db.MatterDocuments.AddRange(new MatterDocument { MatterId = f.Matter, Document = doc }, new MatterDocument { MatterId = f.Matter, Document = word }); await db.SaveChangesAsync();
        await f.SaveAsync(db, text: "Evidence available"); var anchor = new NoteAnchor(1, 0, 8, "Evidence", NotingText.Hash("Evidence"));
        var draft = await w.CiteAsync(f.Matter, new(anchor, doc.Id, 1, new(.1, .1, .2, .2), 1), f.Actors.Receiver, Guid.NewGuid(), default); Assert.Equal(2, draft.Revision);
        var note = await w.SubmitAsync(f.Matter, new(f.Dak), 2, f.Actors.Receiver, Guid.NewGuid(), default); Assert.Contains(doc.Id.ToString(), note.CitationsJson); Assert.Contains(word.Id.ToString(), note.DocumentManifestJson);
        var remark = await w.RemarkAsync(f.Matter, note.Id, new(anchor, "Check this evidence"), f.Actors.Sender, Guid.NewGuid(), default);
        await Assert.ThrowsAsync<MatterWorkflowException>(() => w.RemarkAsync(f.Matter, note.Id, new(anchor with { End = 9 }, "Bad anchor"), f.Actors.Sender, Guid.NewGuid(), default));
        var addressed = await w.ChangeRemarkAsync(f.Matter, note.Id, remark.Id, new("Addressed", 0), f.Actors.Receiver, Guid.NewGuid(), default); Assert.Equal(f.Actors.Receiver, addressed.AddressedByUserId);
        await w.ChangeRemarkAsync(f.Matter, note.Id, remark.Id, new("Open", 1), f.Actors.Sender, Guid.NewGuid(), default);
        await f.SaveAsync(db, 3, "Next note"); await w.SubmitAsync(f.Matter, new(f.Dak), 4, f.Actors.Receiver, Guid.NewGuid(), default);
        Assert.Equal(remark.AnchorJson, (await w.RemarksAsync(f.Matter, note.Id, f.Actors.Receiver, default)).Single().AnchorJson);
        await w.AnnotateAsync(f.Matter, doc.Id, new(1, new(.1, .1, .2, .2), "Evidence", "Check"), f.Actors.Receiver, Guid.NewGuid(), default);
        Assert.Single(await w.AnnotationsAsync(f.Matter, doc.Id, f.Actors.Sender, default));
        await Assert.ThrowsAsync<MatterWorkflowException>(() => w.AnnotateAsync(f.Matter, word.Id, new(1, new(0, 0, .1, .1), null, null), f.Actors.Receiver, Guid.NewGuid(), default));
        db.ChangeTracker.Clear(); var join = await db.MatterDocuments.SingleAsync(x => x.DocumentId == doc.Id); join.RecordStatus = RecordStatus.Archived; await db.SaveChangesAsync();
        Assert.False(await new MatterAuthorizationService(db).CanAccessMatterDocumentAsync(f.Matter, doc.Id, f.Actors.Receiver));
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => w.AnnotationsAsync(f.Matter, doc.Id, f.Actors.Receiver, default))).StatusCode);
        Assert.Contains(doc.Id.ToString(), (await db.MatterOfficialNotes.SingleAsync(x => x.Id == note.Id)).CitationsJson);
    }

    private sealed class DeniedAccess : IAccessControlService
    {
        public Task<bool> CanAsync(string p, AccessResourceContext? context = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyDictionary<string, ScopeMode>> GetEffectivePermissionsAsync(Guid u, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, ScopeMode>>(new Dictionary<string, ScopeMode>());
    }
}

internal sealed class NotingTestStorage : IDocumentStorage
{
    public readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Files = new();
    public async Task<string> SaveAsync(Stream content, string filename, CancellationToken ct) => (await SaveAndHashAsync(content, filename, ct)).StoragePath;
    public async Task<DocumentStorageWriteResult> SaveAndHashAsync(Stream content, string filename, CancellationToken ct)
    {
        using var stream = new MemoryStream(); await content.CopyToAsync(stream, ct); var bytes = stream.ToArray(); var path = $"{Guid.NewGuid():N}-{filename}";
        Files[path] = bytes; return new(path, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes.Length);
    }
    public Task DeleteAsync(string path, CancellationToken ct) { Files.TryRemove(path, out _); return Task.CompletedTask; }
    public Task<Stream?> OpenReadAsync(string path, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes) : null);
    public StorageHealth GetHealth() => new("Synthetic noting storage", true, 1_000_000, 1_000_000);
}
