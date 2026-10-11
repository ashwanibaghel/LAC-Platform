namespace LAC.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class MatterFunctionalAuditTests
{
    [DakPostgresFact]
    public async Task Village_and_paginated_directory_preserve_General_custom_types_and_identical_scope()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        NotingFixture f; Guid customId; Guid hiddenId; Guid role;
        await using (var db = database.Context())
        {
            f = await NotingFixture.CreateAsync(db);
            var custom = new Matter { VillageId = f.Village, WorkstreamId = f.Stream, Title = "Custom", MatterType = "Future custom type" };
            var excludedStream = new Workstream { Code = "EXCLUDED_TEST", Name = "Other scope" };
            var hidden = new Matter { VillageId = f.Village, Workstream = excludedStream, Title = "Hidden", MatterType = "General" };
            db.Matters.AddRange(custom, hidden,
                new Matter { VillageId = f.OtherVillage, WorkstreamId = f.Stream, Title = "Other Village", MatterType = "General" },
                new Matter { VillageId = f.Village, WorkstreamId = f.Stream, Title = "Archived", MatterType = "General", RecordStatus = RecordStatus.Archived });
            await db.SaveChangesAsync(); customId = custom.Id; hiddenId = hidden.Id;
            role = await db.UserRoles.Where(x => x.UserId == f.Actors.Receiver).Select(x => x.RoleId).SingleAsync();
        }
        await using var factory = new MatterNotingApiTests.Factory(database.ConnectionString);
        using var client = factory.CreateClient(); Actor(client, f.Actors.Receiver);
        var all = await Consistent(client, f.Village);
        Assert.Equal(new[] { f.Matter, customId, hiddenId }.Order(), all.Select(x => x.GetProperty("id").GetGuid()).Order());
        Assert.Equal(2, all.Count(x => x.GetProperty("matterType").GetString() == "General"));
        Assert.Contains(all, x => x.GetProperty("matterType").GetString() == "Future custom type");
        await using (var db = database.Context())
        {
            (await db.RolePermissions.SingleAsync(x => x.RoleId == role && x.Permission.Code == PermissionCodes.MatterView)).ScopeMode = ScopeMode.Workstream;
            db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership { UserId = f.Actors.Receiver, WorkstreamId = f.Stream, IsActive = true });
            await db.SaveChangesAsync();
        }
        var scoped = await Consistent(client, f.Village);
        Assert.Equal(new[] { f.Matter, customId }.Order(), scoped.Select(x => x.GetProperty("id").GetGuid()).Order());
        await using (var db = database.Context())
        {
            (await db.UserWorkstreamMemberships.SingleAsync(x => x.UserId == f.Actors.Receiver && x.WorkstreamId == f.Stream)).IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Empty(await Consistent(client, f.Village));
        await using (var db = database.Context())
        {
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == role && x.Permission.Code == PermissionCodes.MatterView));
            await db.SaveChangesAsync();
        }
        foreach (var route in new[] { $"/api/matters/?villageId={f.Village}", $"/api/villages/{f.Village}/matters" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
            client.DefaultRequestHeaders.Remove("X-Noting-Actor");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
            Actor(client, f.Actors.Receiver);
        }
    }

    private static async Task<JsonElement[]> Consistent(HttpClient client, Guid village)
    {
        var villageRows = (await client.GetFromJsonAsync<JsonElement[]>($"/api/villages/{village}/matters"))!;
        var directory = new List<JsonElement>();
        var page = 1;
        while (true)
        {
            var body = await client.GetFromJsonAsync<JsonElement>($"/api/matters/?villageId={village}&status=all&page={page}&pageSize=1");
            directory.AddRange(body.GetProperty("items").EnumerateArray());
            if (directory.Count >= body.GetProperty("totalCount").GetInt32()) break;
            page++; Assert.True(page <= 10, "Unexpected pagination loop");
        }
        Assert.Equal(villageRows!.Select(x => x.GetProperty("id").GetGuid()).Order(), directory.Select(x => x.GetProperty("id").GetGuid()).Order());
        foreach (var row in directory)
        {
            var peer = Assert.Single(villageRows, x => x.GetProperty("id").GetGuid() == row.GetProperty("id").GetGuid());
            foreach (var property in new[] { "villageId", "matterType", "revision", "workstreamId", "status", "title" })
                Assert.Equal(peer.GetProperty(property).GetRawText(), row.GetProperty(property).GetRawText());
        }
        return directory.ToArray();
    }

    [DakPostgresFact]
    public async Task Exact_remark_HTTP_contract_handles_repeated_multiline_whitespace_and_surrogates()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); NotingFixture f; MatterOfficialNote note;
        const string richJson = """
            {"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Repeat"}]},{"type":"paragraph","content":[{"type":"text","text":"Repeat 😀"}]}]}
            """;
        await using (var db = database.Context())
        {
            f = await NotingFixture.CreateAsync(db); var w = NotingFixture.Workflow(db);
            await w.SaveAsync(f.Matter, new(richJson, f.Dak), 0, f.Actors.Receiver, Guid.NewGuid(), default);
            note = await w.SubmitAsync(f.Matter, new(f.Dak), 1, f.Actors.Receiver, Guid.NewGuid(), default);
        }
        Assert.Equal("Repeat\nRepeat 😀\n", note.CanonicalText);
        await using var factory = new MatterNotingApiTests.Factory(database.ConnectionString);
        using var client = factory.CreateClient(); Actor(client, f.Actors.Receiver);
        var route = $"/api/matters/{f.Matter}/notes/{note.Id}/remarks";
        var repeated = new NoteAnchor(1, 7, 13, "Repeat", "B6B7A0065808A62E7D5781B356C5DDBA4E000433C34B5D561D200158A4443EB1");
        Key(client, Guid.NewGuid()); var body = new CreateRemarkCommand(repeated, "Review second occurrence");
        var first = await client.PostAsJsonAsync(route, body); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var remark = (await first.Content.ReadFromJsonAsync<MatterNoteRemark>())!;
        var replay = await client.PostAsJsonAsync(route, body); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(remark.Id, (await replay.Content.ReadFromJsonAsync<MatterNoteRemark>())!.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(route, body with { Text = "Changed" })).StatusCode);
        foreach (var (start, end) in new[] { (0, 6), (0, 13), (13, 14), (14, 16), (5, 8), (0, 1) })
        {
            Key(client, Guid.NewGuid()); var quote = note.CanonicalText[start..end];
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(route, new CreateRemarkCommand(new(1, start, end, quote, NotingText.Hash(quote)), "Exact span"))).StatusCode);
        }
        var invalid = new[] {
            repeated with { Start = -1 }, repeated with { End = 7 }, repeated with { End = 99 },
            repeated with { Version = 2 }, repeated with { Quote = "Other" }, repeated with { QuoteHash = new string('0',64) },
            repeated with { QuoteHash = repeated.QuoteHash.ToLowerInvariant() },
            new NoteAnchor(1, 14, 15, "?", NotingText.Hash("?")), new NoteAnchor(1, 15, 16, "?", NotingText.Hash("?")),
        };
        foreach (var anchor in invalid)
        {
            Key(client, Guid.NewGuid()); var result = await client.PostAsJsonAsync(route, new CreateRemarkCommand(anchor, "Invalid"));
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            Assert.Equal("Anchor does not match the immutable canonical text/version/hash.", (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        }
        Key(client, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, body with { Text = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, body with { Text = new string('x',4001) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/matters/{f.Matter}/notes/{Guid.NewGuid()}/remarks", body)).StatusCode);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        Assert.Equal((HttpStatusCode)428, (await client.PostAsJsonAsync(route, body)).StatusCode);
        Actor(client, f.Actors.Supervisor); Key(client, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        await using var verify = database.Context();
        Assert.Equal(7, await verify.MatterNoteRemarks.CountAsync());
        Assert.Equal(note.CanonicalText, (await verify.MatterOfficialNotes.SingleAsync()).CanonicalText);
        Assert.Equal(repeated, JsonSerializer.Deserialize<NoteAnchor>((await verify.MatterNoteRemarks.SingleAsync(x => x.Id == remark.Id)).AnchorJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [DakPostgresFact]
    public async Task Address_reopen_revisions_keep_immutable_note_anchor_text_and_transition_receipts()
    {
        await using var database = await DisposableDakDatabase.CreateAsync(); NotingFixture f; MatterOfficialNote note;
        await using (var db = database.Context())
        {
            f = await NotingFixture.CreateAsync(db); await f.SaveAsync(db);
            note = await NotingFixture.Workflow(db).SubmitAsync(f.Matter, new(f.Dak), 1, f.Actors.Receiver, Guid.NewGuid(), default);
        }
        await using var factory = new MatterNotingApiTests.Factory(database.ConnectionString);
        using var client = factory.CreateClient(); Actor(client, f.Actors.Receiver); Key(client, Guid.NewGuid());
        var route = $"/api/matters/{f.Matter}/notes/{note.Id}/remarks"; var quote = note.CanonicalText[..6];
        var created = await client.PostAsJsonAsync(route, new CreateRemarkCommand(new(1,0,6,quote,NotingText.Hash(quote)), "Review"));
        created.EnsureSuccessStatusCode(); var remark = (await created.Content.ReadFromJsonAsync<MatterNoteRemark>())!;
        var addressedKey = Guid.NewGuid(); Key(client, addressedKey);
        var addressedResponse = await client.PutAsJsonAsync($"{route}/{remark.Id}", new ChangeRemarkCommand("Addressed",0));
        Assert.Equal(HttpStatusCode.OK, addressedResponse.StatusCode);
        var addressed = (await addressedResponse.Content.ReadFromJsonAsync<MatterNoteRemark>())!;
        Assert.NotNull(addressed.AddressedAt); Assert.Equal(f.Actors.Receiver,addressed.AddressedByUserId);
        Key(client, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{route}/{remark.Id}", new ChangeRemarkCommand("Open",0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{route}/{remark.Id}", new ChangeRemarkCommand("Resolved",1))).StatusCode);
        var reopenedKey = Guid.NewGuid(); Key(client, reopenedKey);
        var openResponse = await client.PutAsJsonAsync($"{route}/{remark.Id}", new ChangeRemarkCommand("Open",1));
        Assert.Equal(HttpStatusCode.OK,openResponse.StatusCode);
        var reopened = (await openResponse.Content.ReadFromJsonAsync<MatterNoteRemark>())!;
        Assert.Null(reopened.AddressedAt); Assert.Null(reopened.AddressedByUserId); Assert.Equal(2,reopened.Revision);
        Assert.Equal(remark.AnchorJson,reopened.AnchorJson); Assert.Equal(remark.Text,reopened.Text);
        // PostgreSQL timestamps persist microseconds; command responses can have 100 ns ticks.
        Assert.Equal(remark.CreatedAt.UtcTicks / 10,reopened.CreatedAt.UtcTicks / 10);
        Assert.Equal(remark.AuthorUserId,reopened.AuthorUserId);
        await using var verify = database.Context();
        var snapshots = await verify.MatterWorkflowReceipts.Where(x => x.RequestId == addressedKey || x.RequestId == reopenedKey).ToListAsync();
        Assert.Equal(2,snapshots.Count);
        var historic = JsonSerializer.Deserialize<MatterNoteRemark>(snapshots.Single(x => x.RequestId == addressedKey).ResultJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("Addressed",historic.State); Assert.Equal(addressed.AddressedAt,historic.AddressedAt);
        Assert.Equal(f.Actors.Receiver,historic.AddressedByUserId);
        Assert.Equal(note.TextHash,(await verify.MatterOfficialNotes.SingleAsync()).TextHash);
        Assert.Equal(2,await verify.MatterEvents.CountAsync(x => x.ContextEntityId == remark.Id && x.ContextEntityType == "RemarkStateChanged"));
    }

    private static void Actor(HttpClient client, Guid actor) { client.DefaultRequestHeaders.Remove("X-Noting-Actor"); client.DefaultRequestHeaders.Add("X-Noting-Actor",actor.ToString()); }
    private static void Key(HttpClient client, Guid key) { client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key",key.ToString()); }
}
