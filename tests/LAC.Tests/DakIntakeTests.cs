namespace LAC.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class DakIntakeTests(DakTestFactory factory) : IClassFixture<DakTestFactory>
{
    private static MultipartFormDataContent Form(string diary, string subject = "Incoming receipt", bool file = false)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(diary), "diaryNumber" },
            { new StringContent("2026-10-06"), "receivedDate" },
            { new StringContent(subject), "subject" },
            { new StringContent("Sender"), "senderName" }
        };
        if (file) form.Add(new ByteArrayContent("%PDF-1.4\n"u8.ToArray()), "file", "scan.pdf");
        return form;
    }

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(DakTestFactory.TestAdminUser, DakTestFactory.TestAdminPass));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private static async Task<Guid> RegisterAsync(HttpClient client)
    {
        using var form = Form($"DAK-{Guid.NewGuid():N}");
        var response = await client.PostAsync("/api/dak", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public async Task Diary_is_mandatory(string diary)
    {
        using var client = await AdminAsync();
        using var form = Form(diary);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/dak", form)).StatusCode);
    }

    [Fact]
    public async Task Registration_activity_does_not_claim_marking()
    {
        using var client = await AdminAsync();
        var id = await RegisterAsync(client);
        var activity = await client.GetFromJsonAsync<JsonElement>("/api/activity/my-history?entityType=Dak&pageSize=100");
        var registered = Assert.Single(activity.GetProperty("items").EnumerateArray(), item => item.GetProperty("entityId").GetGuid() == id);
        Assert.Equal("Registered in inward correspondence", registered.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Duplicate_is_conflict_and_unknown_context_is_valid()
    {
        using var client = await AdminAsync();
        var diary = $"Stamped/{Guid.NewGuid():N}";
        using var first = Form(diary);
        var response = await client.PostAsync("/api/dak", first);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var second = Form($"  {diary.ToUpperInvariant()}  ");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/dak", second)).StatusCode);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/dak/{id}");
        Assert.Equal(0, detail.GetProperty("matterLinks").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("currentAssignment").ValueKind);
    }

    [Theory]
    [InlineData(RecordStatus.Archived, DakStatus.Registered)]
    [InlineData(RecordStatus.Active, DakStatus.Disposed)]
    [InlineData(RecordStatus.Active, DakStatus.Cancelled)]
    [InlineData(RecordStatus.Archived, DakStatus.Disposed)]
    [InlineData(RecordStatus.Archived, DakStatus.Cancelled)]
    [InlineData(RecordStatus.Inactive, DakStatus.Registered)]
    public async Task Permanent_diary_rejects_reuse_but_same_request_replays_original(RecordStatus recordStatus, DakStatus status)
    {
        using var client = await AdminAsync();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var diary = $"Replay-{Guid.NewGuid():N}";
        using var first = Form(diary, file: true);
        var initial = await client.PostAsync("/api/dak", first);
        Assert.Equal(HttpStatusCode.Created, initial.StatusCode);
        var id = (await initial.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var archiveScope = factory.Services.CreateScope())
        {
            var archiveDb = archiveScope.ServiceProvider.GetRequiredService<LacDbContext>();
            var dak = await archiveDb.Daks.SingleAsync(d => d.Id == id);
            dak.RecordStatus = recordStatus;
            dak.Status = status;
            await archiveDb.SaveChangesAsync();
        }
        // A new intake cannot reuse the stamped identity after archival or closure.
        var originalKey = client.DefaultRequestHeaders.GetValues("Idempotency-Key").Single();
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        using var duplicate = Form($" \t{diary.ToUpperInvariant()}\r\n", file: true);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/dak", duplicate)).StatusCode);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var newRequest = Form(diary, file: true);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/dak", newRequest)).StatusCode);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", originalKey);
        // Matching replays remain bound to the original receipt in every lifecycle state.
        using var again = Form(diary, file: true);
        var replay = await client.PostAsync("/api/dak", again);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        using var different = Form(diary, "Changed content", file: true);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/dak", different)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        Assert.Equal(1, await db.DakMovements.CountAsync(m => m.DakId == id));
        Assert.Equal(1, await db.Daks.CountAsync(d => d.Id == id));
    }

    [Fact]
    public async Task Linked_context_requires_its_own_permission_and_redacts_identity()
    {
        using var admin = await AdminAsync();
        var id = await RegisterAsync(admin);
        Guid roleId;
        Guid matterId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var village = new Village { Name = "Restricted village" };
            var award = new Award { AwardNumber = "Restricted award" };
            var khasra = new Khasra { DisplayNumber = "Restricted khasra", Village = village };
            var matter = new Matter { Title = "Restricted Matter title", Village = village };
            matterId = matter.Id;
            db.DakVillageLinks.Add(new DakVillageLink { DakId = id, Village = village });
            db.DakAwardLinks.Add(new DakAwardLink { DakId = id, Award = award });
            db.DakKhasraLinks.Add(new DakKhasraLink { DakId = id, Khasra = khasra });
            db.DakMatterLinks.Add(new DakMatterLink { DakId = id, Matter = matter });
            var role = new Role { Code = $"dak-only-{Guid.NewGuid():N}", Name = "Dak only" };
            roleId = role.Id;
            db.Roles.Add(role);
            foreach (var code in new[] { PermissionCodes.DakView, PermissionCodes.DakEdit })
                db.RolePermissions.Add(new RolePermission { Role = role, PermissionId = await db.Permissions.Where(p => p.Code == code).Select(p => p.Id).SingleAsync(), ScopeMode = ScopeMode.All });
            await db.SaveChangesAsync();
        }
        var username = $"dak-reader-{Guid.NewGuid():N}";
        var create = await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(username, "Dak reader", "Reader!123abc", null, [roleId], null, null));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var reader = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await reader.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "Reader!123abc"))).StatusCode);
        var detail = await reader.GetFromJsonAsync<JsonElement>($"/api/dak/{id}");
        foreach (var name in new[] { "matterLinks", "villageLinks", "awardLinks", "khasraLinks" })
        {
            var link = detail.GetProperty(name)[0];
            Assert.False(link.GetProperty("canOpen").GetBoolean());
            Assert.Equal(JsonValueKind.Null, link.GetProperty("entityId").ValueKind);
            Assert.Equal("Restricted record", link.GetProperty("displayName").GetString());
        }
        var allowed = await admin.GetFromJsonAsync<JsonElement>($"/api/dak/{id}");
        Assert.Equal(matterId, allowed.GetProperty("matterLinks")[0].GetProperty("entityId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync($"/api/dak/{id}/links/matters", new { entityId = matterId })).StatusCode);
    }

    [Theory]
    [InlineData(DakStatus.Disposed)]
    [InlineData(DakStatus.Cancelled)]
    public async Task Terminal_records_reject_all_intake_mutations(DakStatus terminal)
    {
        using var client = await AdminAsync();
        var id = await RegisterAsync(client);
        Guid attachmentId;
        Guid villageId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var attachment = new DakAttachment { DakId = id, Document = new Document { OriginalFileName = "scan.pdf", StoragePath = "test" } };
            db.DakAttachments.Add(attachment); attachmentId = attachment.Id;
            var village = new Village { Name = "Village" }; villageId = village.Id;
            db.DakVillageLinks.Add(new DakVillageLink { DakId = id, Village = village });
            (await db.Daks.SingleAsync(d => d.Id == id)).Status = terminal;
            await db.SaveChangesAsync();
        }
        using var file = Form("unused", file: true);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/dak/{id}/attachments", file)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/dak/{id}/attachments/{attachmentId}")).StatusCode);
        foreach (var kind in new[] { "villages", "awards", "matters", "khasras" })
        {
            Assert.False((await client.PostAsJsonAsync($"/api/dak/{id}/links/{kind}", new { entityId = villageId })).IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/dak/{id}/links/{kind}/{villageId}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/dak/{id}/physical-original", new PhysicalOriginalRequest(true, null, null, "Record room", "Observed", 0))).StatusCode);
        var metadata = new UpdateDakMetadataRequest("Edited", "Sender", null, null, null, null, null, "Physical",
            DakPriority.Routine, null, null, null, 0);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/dak/{id}", metadata)).StatusCode);
        using var verify = factory.Services.CreateScope();
        Assert.Equal(0, await verify.ServiceProvider.GetRequiredService<LacDbContext>().Daks.Where(d => d.Id == id).Select(d => d.Revision).SingleAsync());
    }

    [Fact]
    public async Task Archived_records_are_historical_read_only_and_not_in_active_register()
    {
        using var client = await AdminAsync();
        var id = await RegisterAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            (await db.Daks.SingleAsync(d => d.Id == id)).RecordStatus = RecordStatus.Archived;
            await db.SaveChangesAsync();
        }
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/dak/{id}");
        Assert.Equal("Archived", detail.GetProperty("recordStatus").GetString());
        var active = await client.GetFromJsonAsync<JsonElement>($"/api/dak?q={detail.GetProperty("diaryNumber").GetString()}");
        Assert.Equal(0, active.GetProperty("totalCount").GetInt32());
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/dak?includeArchived=true&q={detail.GetProperty("diaryNumber").GetString()}");
        Assert.Equal(1, history.GetProperty("totalCount").GetInt32());
        using var file = Form("unused", file: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/dak/{id}/attachments", file)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/dak/{id}/move", new MoveDakRequest("Marked", Guid.NewGuid(), null, null, null, 0))).StatusCode);
        var desk = await client.GetFromJsonAsync<JsonElement>("/api/dak/my-desk");
        Assert.DoesNotContain(desk.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Physical_original_does_not_mirror_routing_and_intake_revision_is_checked()
    {
        using var client = await AdminAsync();
        var id = await RegisterAsync(client);
        var deskResponse = await client.PostAsJsonAsync("/api/admin/desks", new CreateDeskRequest($"D{Guid.NewGuid():N}"[..12], "Routing desk", null, null));
        var desk = (await deskResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/dak/{id}/move", new MoveDakRequest("Marked", desk, null, null, null, 0))).StatusCode);
        var original = await client.GetFromJsonAsync<JsonElement>($"/api/dak/{id}/physical-original");
        Assert.Equal(JsonValueKind.Null, original.GetProperty("hasPhysicalOriginal").ValueKind);
        Assert.Equal(JsonValueKind.Null, original.GetProperty("deskId").ValueKind);
        var location = new PhysicalOriginalRequest(true, null, null, "Legacy record-room shelf", "Checked original against stamp", 1);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/dak/{id}/physical-original", location)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/dak/{id}/physical-original", location)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/dak/{id}/move", new MoveDakRequest("Returned", desk, null, null, null, 2))).StatusCode);
        original = await client.GetFromJsonAsync<JsonElement>($"/api/dak/{id}/physical-original");
        Assert.Equal("Legacy record-room shelf", original.GetProperty("locationNote").GetString());
        Assert.Equal(JsonValueKind.Null, original.GetProperty("deskId").ValueKind);
        using var scope = factory.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<LacDbContext>().AuditLogs.AnyAsync(a => a.EntityId == id && a.Action == "PhysicalOriginalUpdated"));
        client.DefaultRequestHeaders.Add("If-Match", "\"0\"");
        using var file = Form("unused", file: true);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/dak/{id}/attachments", file)).StatusCode);
    }
}
