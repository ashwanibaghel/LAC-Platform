using System.Net;
using System.Net.Http.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace LAC.Tests;

public sealed partial class LandRecordsStaffPersistenceTests
{
    [StaffPostgresFact]
    public async Task Integrated_intake_live_Own_scope_redacts_owners_and_never_claims_unattributed_legacy_sources()
    {
        using var admin = await ClientAsync(); var hidden = await ContextAsync(); var own = await ContextAsync(); var bytes = Pdf("Restricted integrated source");
        await Body(await Upload(admin, Core(hidden), bytes));
        var username = "synthetic-intake-own-" + Guid.NewGuid().ToString("N"); Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var user = new AppUser { Username = username, NormalizedUsername = username.ToUpperInvariant(), DisplayName = username, IsActive = true };
            user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>().HashPassword(user, "SyntheticOwn!791");
            var role = new Role { Code = username, Name = username, IsActive = true }; db.AddRange(user, role, new UserRole { User = user, Role = role });
            foreach (var code in new[] { PermissionCodes.AwardView, PermissionCodes.AwardCoreDocumentUpload, PermissionCodes.AwardEdit })
                db.Add(new RolePermission { Role = role, PermissionId = await db.Permissions.Where(x => x.Code == code).Select(x => x.Id).SingleAsync(), ScopeMode = ScopeMode.Own });
            (await db.Awards.SingleAsync(x => x.Id == own.Award)).CreatedBy = user.Id.ToString(); await db.SaveChangesAsync(); userId = user.Id;
        }
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
        await Body(await client.PostAsJsonAsync("/api/auth/login", new { username, password = "SyntheticOwn!791" }), HttpStatusCode.OK);
        var conflict = await Body(await Upload(client, $"/api/villages/{own.Village}/core-document-intake", bytes), HttpStatusCode.Conflict);
        Assert.Equal("PDF_OWNERSHIP_CONFLICT", conflict.GetProperty("code").GetString()); Assert.Empty(conflict.GetProperty("conflicts").GetProperty("existingLinks").EnumerateArray());
        Assert.DoesNotContain(hidden.Award.ToString(), conflict.ToString());
        var staged = await Body(await Upload(client, $"/api/villages/{own.Village}/core-document-intake", Pdf("Owned synthetic intake source")));
        var id = staged.GetProperty("intakeId").GetGuid();
        await Body(await client.PostAsJsonAsync($"/api/core-document-intakes/{id}/confirm", new CoreIntakeConfirmation("Award", own.Award)), HttpStatusCode.OK);
        await Body(await client.GetAsync($"/api/core-document-intakes/{id}"), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/core-document-intakes/{id}/file")).StatusCode);
        var unattributed = await Body(await Upload(admin, $"/api/villages/{hidden.Village}/core-document-intake", Pdf("Unattributed historical intake source")));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            (await db.CoreDocumentIntakes.SingleAsync(x => x.Id == unattributed.GetProperty("intakeId").GetGuid())).CreatedBy = "unresolved-historical-officer";
            await db.SaveChangesAsync();
        }
        await Body(await client.GetAsync($"/api/core-document-intakes/{unattributed.GetProperty("intakeId").GetGuid()}"), HttpStatusCode.Forbidden);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); (await db.AppUsers.SingleAsync(x => x.Id == userId)).IsActive = false; await db.SaveChangesAsync();
        }
        await Body(await client.GetAsync($"/api/core-document-intakes/{id}"), HttpStatusCode.Forbidden);
        await Body(await Upload(client, $"/api/villages/{own.Village}/core-document-intake", Pdf("Revoked intake user")), HttpStatusCode.Forbidden);
    }

    [StaffPostgresFact]
    public async Task Integrated_legacy_confirmation_payload_replays_and_temporary_intake_never_creates_a_final_Award()
    {
        using var client = await ClientAsync(); var context = await ContextAsync(); var number = "SYN-LEGACY-" + Guid.NewGuid();
        var staged = await Body(await Upload(client, $"/api/villages/{context.Village}/core-document-intake", Pdf("Synthetic new Award " + number)));
        var id = staged.GetProperty("intakeId").GetGuid(); var input = new CoreIntakeConfirmation("Award", CreateAward: new(number, Confirmed: true));
        var first = await Body(await client.PostAsJsonAsync($"/api/core-document-intakes/{id}/confirm", input), HttpStatusCode.OK);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>(); var intake = await db.CoreDocumentIntakes.SingleAsync(x => x.Id == id);
            intake.ConfirmationJson = System.Text.Json.JsonSerializer.Serialize(new { DocumentRole = "Award", AwardId = (Guid?)null, CreateAward = new { AwardNumber = number, AwardDate = (DateOnly?)null, AwardType = (string?)null, Confirmed = true } });
            await db.SaveChangesAsync();
        }
        var retry = await Body(await client.PostAsJsonAsync($"/api/core-document-intakes/{id}/confirm", input), HttpStatusCode.OK);
        Assert.Equal(first.GetProperty("confirmedAwardId").GetGuid(), retry.GetProperty("confirmedAwardId").GetGuid());
        var temporary = await Body(await Upload(client, $"/api/villages/{context.Village}/core-document-intake", Pdf("Offer No. 123\nSection 35(1) temporary occupation for Drain\nPreviously referring to Award No. 44/2001-02")));
        Assert.Equal("Unknown", temporary.GetProperty("detectedRole").GetString());
        var blocked = await Body(await client.PostAsJsonAsync($"/api/core-document-intakes/{temporary.GetProperty("intakeId").GetGuid()}/confirm", new CoreIntakeConfirmation("Award", context.Award)), HttpStatusCode.Conflict);
        Assert.Equal("TemporaryInstrumentRequiresProceeding", blocked.GetProperty("code").GetString());
        Assert.Equal(0, await Read(db => db.DocumentAwards.CountAsync(x => x.AwardId == context.Award)));
    }
    [StaffPostgresFact]
    public async Task Integrated_intake_then_direct_upload_and_confirmation_reuse_one_source_and_preserve_proposal()
    {
        using var client = await ClientAsync(); var context = await ContextAsync(); var bytes = Pdf("Smart source with explicit officer-selected Award");
        var staged = await Body(await Upload(client, $"/api/villages/{context.Village}/core-document-intake", bytes));
        var id = staged.GetProperty("documentId").GetGuid(); var intakeId = staged.GetProperty("intakeId").GetGuid();
        Assert.Equal("NeedsConfirmation", staged.GetProperty("status").GetString());
        var uploaded = await Body(await Upload(client, Core(context), bytes)); Assert.Equal(id, uploaded.GetProperty("documentId").GetGuid());
        var decision = new CoreIntakeConfirmation("Award", context.Award, ExpectedRevision: staged.GetProperty("revision").GetInt32());
        var confirmed = await Body(await client.PostAsJsonAsync($"/api/core-document-intakes/{intakeId}/confirm", decision), HttpStatusCode.OK);
        Assert.Equal(id, confirmed.GetProperty("confirmedDocumentId").GetGuid()); Assert.Equal("AlreadyAttached", confirmed.GetProperty("attachmentStatus").GetString());
        await Body(await client.PostAsJsonAsync($"/api/core-document-intakes/{intakeId}/confirm", decision), HttpStatusCode.OK);
        Assert.Equal(1, await Read(db => db.DocumentAwards.CountAsync(x => x.AwardId == context.Award)));
        Assert.Equal(0, await Read(db => db.AwardDocumentExtractionJobs.CountAsync(x => x.TargetAwardId == context.Award)));
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/core-document-intakes/{intakeId}/file"));
        await Body(await client.GetAsync($"/api/core-document-intakes/{intakeId}/source-evidence"), HttpStatusCode.OK);
        var inventory = await client.GetFromJsonAsync<List<CoreAwardInventory>>($"/api/villages/{context.Village}/core-records");
        var entry = Assert.Single(Assert.Single(inventory!).Roles.Single(x => x.Role == "Award").Documents);
        Assert.Equal(id, entry.DocumentId); Assert.Equal(context.Village, entry.VillageId);
        await Body(await client.PostAsync($"/api/awards/{context.Award}/documents/{id}/analyze?villageId={context.Village}", null), HttpStatusCode.Accepted);
    }

    [StaffPostgresFact]
    public async Task Integrated_direct_then_smart_conflict_uses_same_contract_and_confirmed_sharing_retains_source()
    {
        using var client = await ClientAsync(); var a = await ContextAsync(); var b = await ContextAsync(); var bytes = Pdf("Common synthetic intake and direct source");
        var upload = await Body(await Upload(client, Core(a), bytes)); var id = upload.GetProperty("documentId").GetGuid();
        var staged = await Body(await Upload(client, $"/api/villages/{b.Village}/core-document-intake", bytes));
        Assert.Equal(id, staged.GetProperty("documentId").GetGuid()); var intake = staged.GetProperty("intakeId").GetGuid();
        var decision = new CoreIntakeConfirmation("Award", b.Award, ExpectedRevision: staged.GetProperty("revision").GetInt32());
        var conflict = await Body(await client.PostAsJsonAsync($"/api/core-document-intakes/{intake}/confirm", decision), HttpStatusCode.Conflict);
        Assert.Equal("PDF_OWNERSHIP_CONFLICT", conflict.GetProperty("code").GetString());
        Assert.False(conflict.TryGetProperty("confidence", out _));
        Assert.Equal(a.Award, conflict.GetProperty("conflicts").GetProperty("existingLinks")[0].GetProperty("awardId").GetGuid());
        var hash = conflict.GetProperty("conflicts").GetProperty("sha256Hash").GetString()!;
        var resolution = new AwardPdfLinkResolutionInput(Guid.NewGuid(), "ShareSource", a.Award, b.Award, b.Village, 0, 0, 0, hash, true, "Confirmed common source", [1], "Synthetic page 1 covers both references");
        await Body(await client.PostAsJsonAsync($"/api/documents/{id}/award-link-resolutions", resolution), HttpStatusCode.OK);
        var confirmed = await Body(await client.PostAsJsonAsync($"/api/core-document-intakes/{intake}/confirm", decision), HttpStatusCode.OK);
        Assert.Equal(id, confirmed.GetProperty("confirmedDocumentId").GetGuid()); Assert.True(confirmed.GetProperty("isDuplicate").GetBoolean());
        Assert.Equal(2, await Read(db => db.DocumentAwards.CountAsync(x => x.DocumentId == id && !x.IsDetached)));
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/documents/{id}/content"));
        Assert.Equal(1, await Read(db => db.Documents.CountAsync(x => x.Sha256Hash == hash)));
    }

    [StaffPostgresFact]
    public async Task Integrated_parallel_staging_and_smart_confirmation_vs_direct_upload_have_one_source_and_owner()
    {
        using var client = await ClientAsync(); var a = await ContextAsync(); var b = await ContextAsync(); var bytes = Pdf("Parallel integrated intake ownership " + Guid.NewGuid());
        var staged = await Task.WhenAll(Upload(client, $"/api/villages/{a.Village}/core-document-intake", bytes), Upload(client, $"/api/villages/{b.Village}/core-document-intake", bytes));
        var first = await Body(staged[0]); var second = await Body(staged[1]); var id = first.GetProperty("documentId").GetGuid();
        Assert.Equal(id, second.GetProperty("documentId").GetGuid()); Assert.NotEqual(first.GetProperty("intakeId").GetGuid(), second.GetProperty("intakeId").GetGuid());
        var decision = new CoreIntakeConfirmation("Award", a.Award);
        var outcomes = await Task.WhenAll(client.PostAsJsonAsync($"/api/core-document-intakes/{first.GetProperty("intakeId").GetGuid()}/confirm", decision), Upload(client, Core(b), bytes));
        Assert.Single(outcomes, x => x.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created); Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.Conflict);
        var conflict = await Body(outcomes.Single(x => x.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict);
        Assert.Equal("PDF_OWNERSHIP_CONFLICT", conflict.GetProperty("code").GetString());
        Assert.Equal(1, await Read(db => db.DocumentAwards.CountAsync(x => x.DocumentId == id && !x.IsDetached)));
        Assert.Equal(2, await Read(db => db.CoreDocumentIntakes.CountAsync(x => x.DocumentId == id)));
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/documents/{id}/content"));
    }

    [StaffPostgresFact]
    public async Task Integrated_new_Award_confirmation_failure_rolls_back_registration_link_audit_and_intake_state()
    {
        using var client = await ClientAsync(); var context = await ContextAsync(); var number = "SYN-INTEGRATED-" + Guid.NewGuid();
        var bytes = Pdf("Synthetic proposed Award " + number);
        var staged = await Body(await Upload(client, $"/api/villages/{context.Village}/core-document-intake", bytes)); var id = staged.GetProperty("intakeId").GetGuid();
        var decision = new CoreIntakeConfirmation("Award", CreateAward: new(number, Confirmed: true));
        factory.Failure.FailNextCoreAttachment = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync($"/api/core-document-intakes/{id}/confirm", decision)).StatusCode);
        Assert.Equal(0, await Read(db => db.Awards.CountAsync(x => x.AwardNumber == number)));
        Assert.Equal("NeedsConfirmation", await Read(db => db.CoreDocumentIntakes.Where(x => x.Id == id).Select(x => x.Status).SingleAsync()));
        Assert.Equal(0, await Read(db => db.AuditLogs.CountAsync(x => x.EntityId == id && x.Action == "CoreDocumentIntakeConfirmed")));
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/core-document-intakes/{id}/file"));
        var confirmed = await Body(await client.PostAsJsonAsync($"/api/core-document-intakes/{id}/confirm", decision), HttpStatusCode.OK);
        Assert.Equal(staged.GetProperty("documentId").GetGuid(), confirmed.GetProperty("confirmedDocumentId").GetGuid());
        Assert.Equal(1, await Read(db => db.Awards.CountAsync(x => x.AwardNumber == number)));
    }

    [StaffPostgresFact]
    public async Task Integrated_PP_requires_actual_date_and_NM_keeps_review_and_grouped_history_uses_canonical_sources()
    {
        using var client = await ClientAsync(); var context = await ContextAsync(date: new(2000, 1, 1));
        var staged = await Body(await Upload(client, $"/api/villages/{context.Village}/core-document-intake", Pdf("Possession Proceedings relating to synthetic Award")));
        var id = staged.GetProperty("intakeId").GetGuid();
        var invalid = await Body(await client.PostAsJsonAsync($"/api/core-document-intakes/{id}/confirm", new CoreIntakeConfirmation("PossessionProceeding", context.Award)), HttpStatusCode.BadRequest);
        Assert.Equal("PossessionDateRequired", invalid.GetProperty("code").GetString());
        var date = new DateOnly(2000, 2, 2);
        await Body(await client.PostAsJsonAsync($"/api/core-document-intakes/{id}/confirm", new CoreIntakeConfirmation("PossessionProceeding", context.Award, LegalDate: date)), HttpStatusCode.OK);
        var nm = await Body(await Upload(client, $"/api/villages/{context.Village}/core-document-intake", Pdf("NM of synthetic Award")));
        await Body(await client.PostAsJsonAsync($"/api/core-document-intakes/{nm.GetProperty("intakeId").GetGuid()}/confirm", new CoreIntakeConfirmation("NM", context.Award)), HttpStatusCode.OK);
        Assert.Equal(1, await Read(db => db.NmDocuments.CountAsync(x => x.AwardId == context.Award)));
        var history = await client.GetFromJsonAsync<VillageAcquisitionHistory>($"/api/villages/{context.Village}/acquisition-history");
        var possession = Assert.Single(Assert.Single(history!.Awards).Events, x => x.Type == "Possession"); Assert.Equal(date, possession.Date); Assert.Single(possession.Documents!);
        var source = Assert.Single(await client.GetFromJsonAsync<List<CoreDocumentEntry>>($"/api/awards/{context.Award}/core-documents") ?? [] , x => x.Role == "PossessionProceeding");
        // The V2 attachment correction detaches the same canonical PP/event from both projections.
        await Body(await client.PutAsJsonAsync($"/api/awards/{context.Award}/core-document-links/{source.AttachmentId}", new AttachmentCorrectionInput(Guid.NewGuid(), source.Revision, "Detach", null, null, null, null, null, "Reviewed wrong PP attachment")), HttpStatusCode.OK);
        history = await client.GetFromJsonAsync<VillageAcquisitionHistory>($"/api/villages/{context.Village}/acquisition-history"); Assert.DoesNotContain(Assert.Single(history!.Awards).Events, x => x.Type == "Possession");
    }
}
