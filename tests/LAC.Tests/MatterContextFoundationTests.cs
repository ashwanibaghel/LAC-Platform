using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace LAC.Tests;

public sealed class MatterContextFoundationTests
{
    [Fact]
    public async Task Create_UsesCanonicalIds_MultipleCourtsAndKhasras_NoCopiedCourtFactsOrInferredAwards()
    {
        await using var f = await Fixture.Create();
        var matter = await f.Workflow.CreateMatterAsync(new(f.Village.Id, "Office litigation file", "Court Case", f.Workstream.Id,
            KhasraReferenceText: "9999", KhasraIds: [f.Khasra.Id], CourtCaseIds: [f.Court.Id, f.OtherCourt.Id]), f.User.Id);
        var context = await f.Query.GetAsync(matter.Id, f.User.Id);
        Assert.Equal(2, context.CourtCases.Count);
        Assert.Single(context.Khasras);
        Assert.Empty(context.Awards);
        Assert.Null(matter.ReferenceNumber);
        Assert.Equal("Office litigation file", matter.Title);
        Assert.Equal("9999", matter.KhasraReferenceText);
        f.Db.ChangeTracker.Clear();
        var court = await f.Db.CourtCases.SingleAsync(x => x.Id == f.Court.Id);
        court.CaseTitle = "Canonical title corrected";
        court.CurrentStatus = "Disposed";
        f.Db.CourtProceedings.Add(new CourtProceeding { CourtCaseId = court.Id, ProceedingDate = new(2026, 10, 1), NextDate = new(2026, 11, 1) });
        await f.Db.SaveChangesAsync();
        context = await f.Query.GetAsync(matter.Id, f.User.Id);
        var projected = Assert.Single(context.CourtCases, x => x.CourtCaseId == court.Id);
        Assert.Equal("Canonical title corrected", projected.CaseTitle);
        Assert.Equal("Disposed", projected.CurrentStatus);
        Assert.Equal(new DateOnly(2026, 11, 1), projected.OperationalNdoh);
        Assert.Equal("Office litigation file", (await f.Db.Matters.AsNoTracking().SingleAsync()).Title);
        Assert.Equal(1, court.Revision); // Matter links never revise Court metadata.
    }

    [Fact]
    public async Task LegacyMatter_Loads_WithoutInferringCourtOrKhasra()
    {
        await using var f = await Fixture.Create();
        var matter = new Matter { VillageId = f.Village.Id, Title = "W.P.(C) 223/2026 - Ramesh Kumar", MatterType = "Court Case",
            ReferenceNumber = f.Court.CaseNumber, KhasraReferenceText = f.Khasra.DisplayNumber };
        f.Db.Add(matter); await f.Db.SaveChangesAsync();
        var context = await f.Query.GetAsync(matter.Id, f.User.Id);
        Assert.Equal(matter.Title, context.Matter.Title);
        Assert.Empty(context.CourtCases); Assert.Empty(context.Khasras);
        Assert.Equal("Court context not linked", context.CourtContextState);
        Assert.Empty(await f.Db.CourtCaseMatters.ToListAsync()); Assert.Empty(await f.Db.MatterKhasras.ToListAsync());
    }

    [Fact]
    public async Task ConstructedMultiAwardMatter_DefaultsToNoPrimary_AndReloadPreservesExplicitPrimary()
    {
        await using var f = await Fixture.Create();
        var matter = new Matter
        {
            VillageId = f.Village.Id, WorkstreamId = f.Workstream.Id, Title = "Constructed multi-Award file",
            AwardLinks = [new MatterAward { AwardId = f.Award.Id }, new MatterAward { AwardId = f.OtherAward.Id }]
        };
        Assert.All(matter.AwardLinks, link => Assert.False(link.IsPrimary));
        f.Db.Add(matter); await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var links = await f.Db.MatterAwards.Where(x => x.MatterId == matter.Id).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.All(links, link => Assert.False(link.IsPrimary));

        links.Single(x => x.AwardId == f.OtherAward.Id).IsPrimary = true;
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        links = await f.Db.MatterAwards.AsNoTracking().Where(x => x.MatterId == matter.Id).ToListAsync();
        Assert.Equal(f.OtherAward.Id, Assert.Single(links, x => x.IsPrimary).AwardId);
        Assert.False(links.Single(x => x.AwardId == f.Award.Id).IsPrimary);
    }

    [Fact]
    public async Task CreateMultiAwardMatter_ExplicitPrimaryAwardIdPromotesOnlyRequestedAward()
    {
        await using var f = await Fixture.Create();
        var matter = await f.Workflow.CreateMatterAsync(new(f.Village.Id, "Explicit primary file", "General", f.Workstream.Id,
            AwardIds: [f.Award.Id, f.OtherAward.Id], PrimaryAwardId: f.OtherAward.Id), f.User.Id);
        f.Db.ChangeTracker.Clear();
        var links = await f.Db.MatterAwards.AsNoTracking().Where(x => x.MatterId == matter.Id).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Equal(f.OtherAward.Id, Assert.Single(links, x => x.IsPrimary).AwardId);
        Assert.False(links.Single(x => x.AwardId == f.Award.Id).IsPrimary);
    }

    [Fact]
    public async Task Awards_Multiple_OptionalPrimary_PromotionAndUnlink_NoAutomaticDocuments()
    {
        await using var f = await Fixture.Create();
        var source = new Document { OriginalFileName = "Award.pdf", StoragePath = "canonical.pdf", Status = "Active", Sha256Hash = new string('a', 64) };
        f.Db.Add(new DocumentAward { AwardId = f.Award.Id, Document = source }); await f.Db.SaveChangesAsync();
        var matter = await f.Workflow.CreateMatterAsync(new(f.Village.Id, "Awards context", "General", f.Workstream.Id,
            AwardIds: [f.Award.Id, f.OtherAward.Id]), f.User.Id);
        Assert.Equal(2, await f.Db.MatterAwards.CountAsync());
        Assert.Empty(await f.Db.MatterAwards.Where(x => x.IsPrimary).ToListAsync());
        await f.Workflow.ChangeContextLinkAsync(matter.Id, MatterContextKind.Award, f.Award.Id, true, new(0, true), f.User.Id);
        await f.Workflow.ChangeContextLinkAsync(matter.Id, MatterContextKind.Award, f.OtherAward.Id, true, new(1, true), f.User.Id);
        Assert.Equal(f.OtherAward.Id, (await f.Db.MatterAwards.AsNoTracking().SingleAsync(x => x.IsPrimary)).AwardId);
        Assert.Equal(2, await f.Db.MatterAwards.CountAsync());
        await f.Workflow.ChangeContextLinkAsync(matter.Id, MatterContextKind.Award, f.OtherAward.Id, false, new(2), f.User.Id);
        Assert.Single(await f.Db.MatterAwards.ToListAsync());
        Assert.Empty(await f.Db.MatterDocuments.ToListAsync()); Assert.Single(await f.Db.Documents.ToListAsync());
        var candidates = await MatterDocumentProvenanceHelper.GetEligibleDocumentCandidateMapAsync(f.Db, matter, f.Access);
        Assert.Contains(source.Id, candidates.Keys);
    }

    [Theory]
    [InlineData(MatterContextKind.Award)]
    [InlineData(MatterContextKind.Khasra)]
    [InlineData(MatterContextKind.CourtCase)]
    [InlineData(MatterContextKind.Dak)]
    public async Task LinkAndUnlink_RequiresMatterAndTargetPermissions_DuplicatesSafe_RevisionsAndImmutableHistory(MatterContextKind kind)
    {
        await using var f = await Fixture.Create();
        var matter = await f.NewMatter(); var id = f.Target(kind);
        var first = await f.Workflow.ChangeContextLinkAsync(matter.Id, kind, id, true, new(0), f.User.Id);
        Assert.True(first.Changed); Assert.Equal(1, first.Revision);
        var duplicate = await f.Workflow.ChangeContextLinkAsync(matter.Id, kind, id, true, new(1), f.User.Id);
        Assert.False(duplicate.Changed); Assert.Equal(1, duplicate.Revision);
        var stale = await Assert.ThrowsAsync<MatterWorkflowException>(() => f.Workflow.ChangeContextLinkAsync(matter.Id, kind, id, false, new(0), f.User.Id));
        Assert.Equal(409, stale.StatusCode);
        await f.Revoke(kind + ".View");
        var denied = await Assert.ThrowsAsync<MatterWorkflowException>(() => f.Workflow.ChangeContextLinkAsync(matter.Id, kind, id, false, new(1), f.User.Id));
        Assert.Equal(403, denied.StatusCode);
        var missing = await Assert.ThrowsAsync<MatterWorkflowException>(() => f.Workflow.ChangeContextLinkAsync(matter.Id, kind, Guid.NewGuid(), true, new(1), f.User.Id));
        Assert.Equal(403, missing.StatusCode); Assert.Equal(denied.Message, missing.Message);
        var hidden = await f.Query.GetAsync(matter.Id, f.User.Id);
        Assert.Empty(hidden.Awards); Assert.Empty(hidden.Khasras); Assert.Empty(hidden.CourtCases); Assert.Empty(hidden.Daks);
        await f.Grant(kind + ".View");
        await f.Revoke(PermissionCodes.MatterEdit);
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => f.Workflow.ChangeContextLinkAsync(matter.Id, kind, id, false, new(1), f.User.Id))).StatusCode);
        await f.Grant(PermissionCodes.MatterEdit);
        await f.Workflow.ChangeContextLinkAsync(matter.Id, kind, id, false, new(1), f.User.Id);
        var unlinkDuplicate = await f.Workflow.ChangeContextLinkAsync(matter.Id, kind, id, false, new(2), f.User.Id);
        Assert.False(unlinkDuplicate.Changed);
        var events = await f.Db.MatterEvents.AsNoTracking().Where(x => x.MatterId == matter.Id).OrderBy(x => x.SequenceNumber).ToListAsync();
        Assert.Equal(3, events.Count); Assert.Equal(new[] { 1, 2, 3 }, events.Select(x => x.SequenceNumber));
        Assert.Equal(id, events[1].ContextEntityId); Assert.Equal(id, events[2].ContextEntityId);
        Assert.EndsWith("Linked", events[1].Action.ToString()); Assert.EndsWith("Unlinked", events[2].Action.ToString());
        await f.Workflow.ChangeContextLinkAsync(matter.Id, kind, id, true, new(2), f.User.Id);
        Assert.Equal(3, (await f.Db.Matters.AsNoTracking().SingleAsync()).Revision);
        Assert.Equal(4, await f.Db.MatterEvents.CountAsync());
        if (kind == MatterContextKind.CourtCase)
            Assert.Equal(RecordStatus.Active, (await f.Db.CourtCaseMatters.SingleAsync()).RecordStatus);
        f.Db.ChangeTracker.Clear(); var ev = await f.Db.MatterEvents.FirstAsync(); ev.ContextEntityId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Context_AllHiddenTargetsAndCountsAreOmitted_AndMatterViewFailsClosed()
    {
        await using var f = await Fixture.Create();
        var matter = await f.Workflow.CreateMatterAsync(new(f.Village.Id, "Context", "General", f.Workstream.Id,
            AwardIds: [f.Award.Id], KhasraIds: [f.Khasra.Id], CourtCaseIds: [f.Court.Id]), f.User.Id);
        await f.Workflow.ChangeContextLinkAsync(matter.Id, MatterContextKind.Dak, f.Dak.Id, true, new(0), f.User.Id);
        var draft = new MatterDraft { MatterId = matter.Id, Title = "Private draft" };
        var outward = new Outward { MatterId = matter.Id, OutwardNumber = "O/1", NormalizedOutwardNumber = "O/1", IssuingDeskId = f.Desk.Id, Subject = "Private Outward" };
        f.Db.AddRange(draft, outward); await f.Db.SaveChangesAsync();
        var allowed = await f.Query.GetAsync(matter.Id, f.User.Id);
        Assert.Single(allowed.Daks); Assert.Equal(1, allowed.DraftCount); Assert.Equal(1, allowed.OutwardCount);
        foreach (var code in new[] { PermissionCodes.AwardView, PermissionCodes.KhasraView, PermissionCodes.CourtView,
                     PermissionCodes.DakView, PermissionCodes.OutwardView, PermissionCodes.DraftView, PermissionCodes.VillageView }) await f.Revoke(code);
        var hidden = await f.Query.GetAsync(matter.Id, f.User.Id);
        Assert.Null(hidden.Village); Assert.Empty(hidden.Awards); Assert.Empty(hidden.Khasras); Assert.Empty(hidden.CourtCases);
        Assert.Empty(hidden.Daks); Assert.Empty(hidden.Outwards); Assert.Equal(0, hidden.OutwardCount); Assert.Equal(0, hidden.DraftCount);
        var json = JsonSerializer.Serialize(hidden);
        foreach (var id in new[] { f.Village.Id, f.Award.Id, f.Khasra.Id, f.Court.Id, f.Dak.Id, outward.Id, draft.Id }) Assert.DoesNotContain(id.ToString(), json);
        await f.Revoke(PermissionCodes.MatterView);
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => f.Query.GetAsync(matter.Id, f.User.Id))).StatusCode);
    }

    [Fact]
    public async Task WorkItems_LiveActiveResponsibilitySummary_NoCopiedMatterAssignments()
    {
        await using var f = await Fixture.Create(); var matter = await f.NewMatter();
        var work = new WorkItem { WorkstreamId = f.Workstream.Id, Title = "Prepare reply", Priority = WorkItemPriority.Urgent,
            DueAt = DateTimeOffset.UtcNow.AddDays(3), RequestedByUserId = f.User.Id };
        f.Db.Add(new WorkItemMatterLink { WorkItem = work, MatterId = matter.Id });
        var assignment = new WorkItemAssignment { WorkItem = work, OfficeDeskId = f.Desk.Id, AssignedUserId = f.User.Id, AssignedByUserId = f.User.Id };
        f.Db.Add(assignment); await f.Db.SaveChangesAsync();
        var context = await f.Query.GetAsync(matter.Id, f.User.Id); var summary = Assert.Single(context.WorkItems);
        Assert.Equal(work.Id, summary.WorkItemId); Assert.Equal("Urgent", summary.Priority); Assert.Equal(work.DueAt, summary.DueAt);
        Assert.Equal(f.Desk.Id, summary.ResponsibleDesk!.DeskId); Assert.Equal(f.User.Id, summary.AssignedUser!.UserId);
        f.Db.ChangeTracker.Clear(); work = await f.Db.WorkItems.SingleAsync(); work.Status = WorkItemStatus.InProgress; work.Title = "Canonical action updated";
        await f.Db.SaveChangesAsync(); context = await f.Query.GetAsync(matter.Id, f.User.Id);
        Assert.Equal("Canonical action updated", Assert.Single(context.WorkItems).Title); Assert.Equal(0, context.Matter.Revision);
        await f.Revoke(PermissionCodes.WorkItemView); Assert.Empty((await f.Query.GetAsync(matter.Id, f.User.Id)).WorkItems);
        await f.Grant(PermissionCodes.WorkItemView); work.Status = WorkItemStatus.Completed; await f.Db.SaveChangesAsync();
        Assert.Empty((await f.Query.GetAsync(matter.Id, f.User.Id)).WorkItems);
    }

    [Fact]
    public async Task UpdateContext_ReplacesOnlyProvidedSets_LegacyPrimaryPreservedAndAuthorized()
    {
        await using var f = await Fixture.Create(); var matter = await f.NewMatter();
        var updated = await f.Workflow.UpdateMetadataAsync(matter.Id, new("Updated office file", "Court Case", null, "Notes", "legacy text", 0,
            AwardIds: [f.Award.Id, f.OtherAward.Id], KhasraIds: [f.Khasra.Id], CourtCaseIds: [f.Court.Id, f.OtherCourt.Id], PrimaryAwardId: f.Award.Id), f.User.Id);
        Assert.Equal(1, updated.Revision);
        Assert.Equal(f.Award.Id, (await f.Db.MatterAwards.SingleAsync(x => x.IsPrimary)).AwardId);
        await f.Workflow.UpdateMetadataAsync(matter.Id, new("Updated office file", "Court Case", null, null, null, 1, AwardId: f.OtherAward.Id), f.User.Id);
        var legacyPrimary = await f.Db.MatterAwards.SingleAsync();
        Assert.Equal(f.OtherAward.Id, legacyPrimary.AwardId);
        Assert.True(legacyPrimary.IsPrimary);
        Assert.Equal(2, (await f.Query.GetAsync(matter.Id, f.User.Id)).CourtCases.Count);
        await f.Revoke(PermissionCodes.AwardView);
        var denied = await Assert.ThrowsAsync<MatterWorkflowException>(() => f.Workflow.UpdateMetadataAsync(matter.Id,
            new("Unauthorized edit", "Court Case", null, null, null, 2, AwardId: f.Award.Id), f.User.Id));
        Assert.Equal(403, denied.StatusCode);
        f.Db.ChangeTracker.Clear(); Assert.Equal(2, (await f.Db.Matters.SingleAsync()).Revision);
        Assert.Equal("Updated office file", (await f.Db.Matters.SingleAsync()).Title);
    }

    [Fact]
    public async Task Create_InvalidOrUnauthorizedContextDoesNotPersistAnyMatter_WorkstreamExplicit()
    {
        await using var f = await Fixture.Create();
        Assert.Equal(400, (await Assert.ThrowsAsync<MatterWorkflowException>(() => f.Workflow.CreateMatterAsync(
            new(f.Village.Id, "Missing Workstream", "General", Guid.Empty), f.User.Id))).StatusCode);
        var ex = await Assert.ThrowsAsync<MatterWorkflowException>(() => f.Workflow.CreateMatterAsync(new(f.Village.Id, "Rejected", "General", f.Workstream.Id,
            AwardIds: [f.Award.Id], PrimaryAwardId: f.Award.Id, CourtCaseIds: [Guid.NewGuid()]), f.User.Id));
        Assert.Equal(403, ex.StatusCode); f.Db.ChangeTracker.Clear(); Assert.Empty(await f.Db.Matters.ToListAsync());
        Assert.Empty(await f.Db.MatterAwards.ToListAsync()); Assert.Empty(await f.Db.MatterEvents.ToListAsync());
    }

    [Fact]
    public async Task Api_CreateContext_OnlyLiveAuthorizedWorkstreams_NoDefault_AndInactiveUserDenied()
    {
        using var factory = new ApiFactory(); var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var userId = SeedData.BootstrapAdminId;
        var roleIds = await db.UserRoles.Where(x => x.UserId == userId).Select(x => x.RoleId).ToListAsync();
        foreach (var rp in await db.RolePermissions.Where(x => roleIds.Contains(x.RoleId) && x.Permission.Code == PermissionCodes.MatterCreate).ToListAsync())
            rp.ScopeMode = ScopeMode.Workstream;
        var ws = await db.Workstreams.FirstAsync(x => x.IsActive);
        var memberships = await db.UserWorkstreamMemberships.Where(x => x.UserId == userId).ToListAsync();
        db.RemoveRange(memberships);
        db.Add(new UserWorkstreamMembership { UserId = userId, WorkstreamId = ws.Id, IsActive = true });
        await db.SaveChangesAsync();
        var context = await client.GetFromJsonAsync<JsonElement>("/api/matters/context");
        Assert.Equal(ws.Id, Assert.Single(context.GetProperty("workstreams").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.False(context.TryGetProperty("defaultWorkstreamId", out _));
        ws.IsActive = false; await db.SaveChangesAsync();
        context = await client.GetFromJsonAsync<JsonElement>("/api/matters/context");
        Assert.Empty(context.GetProperty("workstreams").EnumerateArray());
        var user = await db.AppUsers.SingleAsync(x => x.Id == userId); user.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/matters/context")).StatusCode);
    }

    [Fact]
    public async Task CourtAssignedScope_FiltersPrivateCourtWhileRetainingAuthorizedCourt()
    {
        await using var f = await Fixture.Create();
        var matter = await f.Workflow.CreateMatterAsync(new(f.Village.Id, "Matter", "Court Case", f.Workstream.Id,
            CourtCaseIds: [f.Court.Id, f.OtherCourt.Id]), f.User.Id);
        await f.Revoke(PermissionCodes.CourtView); await f.Grant(PermissionCodes.CourtView, ScopeMode.Assigned);
        f.Db.ChangeTracker.Clear();
        var court = await f.Db.CourtCases.SingleAsync(x => x.Id == f.Court.Id); court.ResponsibleOfficeDeskId = f.Desk.Id;
        f.Db.Add(new UserDeskMembership { UserId = f.User.Id, OfficeDeskId = f.Desk.Id, IsActive = true });
        await f.Db.SaveChangesAsync();
        var visible = await f.Query.GetAsync(matter.Id, f.User.Id);
        Assert.Equal(f.Court.Id, Assert.Single(visible.CourtCases).CourtCaseId);
        Assert.DoesNotContain(f.OtherCourt.Id.ToString(), JsonSerializer.Serialize(visible));
        Assert.Equal(403, (await Assert.ThrowsAsync<MatterWorkflowException>(() => f.Workflow.ChangeContextLinkAsync(
            matter.Id, MatterContextKind.CourtCase, f.OtherCourt.Id, false, new(0), f.User.Id))).StatusCode);
    }

    [Fact]
    public async Task Api_ContextListsDetailsAndJournal_DoNotExposeUnauthorizedAward()
    {
        using var factory = new ApiFactory(); var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var village = new Village { Name = "Safe API Village" }; var award = new Award { AwardNumber = "Private Award" };
        db.Add(new AwardVillage { Award = award, Village = village }); await db.SaveChangesAsync();
        var ws = await db.Workstreams.FirstAsync(x => x.IsActive);
        var create = await client.PostAsJsonAsync("/api/matters", new { villageId = village.Id, title = "Operational file", workstreamId = ws.Id, awardId = award.Id });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var roles = await db.UserRoles.Where(x => x.UserId == SeedData.BootstrapAdminId).Select(x => x.RoleId).ToListAsync();
        db.RemoveRange(await db.RolePermissions.Where(x => roles.Contains(x.RoleId) && x.Permission.Code == PermissionCodes.AwardView).ToListAsync());
        await db.SaveChangesAsync();
        foreach (var url in new[] { $"/api/matters/{id}/context", $"/api/matters/{id}/awards", $"/api/matters/{id}/events",
                     $"/api/matters/{id}", "/api/matters", $"/api/villages/{village.Id}/matters" })
        {
            var response = await client.GetAsync(url); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(award.Id.ToString(), json); Assert.DoesNotContain(award.AwardNumber, json);
        }
    }

    [Fact]
    public async Task Api_CreateUpdateLinkListAndLegacyLoad_Contract()
    {
        using var factory = new ApiFactory(); var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var village = new Village { Name = "Matter API Village" }; var court = new CourtCase { CaseNumber = "W.P.(C) 223/2026", CaseTitle = "Canonical case" };
        var khasra = new Khasra { Village = village, DisplayNumber = "12", NormalizedNumber = "12" };
        db.AddRange(village, court, khasra); await db.SaveChangesAsync();
        var createContext = await client.GetFromJsonAsync<JsonElement>("/api/matters/context");
        var ws = createContext.GetProperty("workstreams")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/matters", new { villageId = village.Id, title = "No Workstream" })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/matters", new { villageId = village.Id, title = "Operational context", matterType = "Court Case", workstreamId = ws,
            courtCaseIds = new[] { court.Id }, khasraIds = new[] { khasra.Id } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var context = await client.GetFromJsonAsync<JsonElement>($"/api/matters/{id}/context");
        Assert.Equal(court.Id, context.GetProperty("courtCases")[0].GetProperty("courtCaseId").GetGuid());
        Assert.Equal("Operational context", context.GetProperty("matter").GetProperty("title").GetString());
        Assert.True(context.GetProperty("matter").GetProperty("referenceNumber").ValueKind == JsonValueKind.Null);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/matters/{id}/court-cases/{court.Id}", new { })).StatusCode);
        var duplicate = await client.PutAsJsonAsync($"/api/matters/{id}/court-cases/{court.Id}", new { expectedRevision = 0 });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode); Assert.False((await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/matters/{id}/court-cases/{court.Id}?expectedRevision=0")).StatusCode);
        var events = await client.GetFromJsonAsync<JsonElement>($"/api/matters/{id}/events");
        Assert.Contains(events.GetProperty("items").EnumerateArray(), x => x.GetProperty("action").GetString() == "CourtCaseUnlinked");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/matters/{id}")).StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/matters/{id}/khasras"); Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/matters/{id}/court-cases/{court.Id}", new { expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/court-cases/{court.Id}/matters", new { matterId = id, expectedRevision = 1 })).StatusCode);
        db.ChangeTracker.Clear(); Assert.Single(await db.CourtCaseMatters.Where(x => x.CourtCaseId == court.Id && x.MatterId == id).ToListAsync());
    }

    [Fact]
    public async Task PostgreSql_Migration_AtomicRollback_ConcurrentLinksAndSinglePrimary()
    {
        if (Environment.GetEnvironmentVariable("MATTER_CONTEXT_POSTGRES_TESTS") != "1") return;
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));
        Assert.Contains(connection.Host, new[] { "localhost", "127.0.0.1", "::1" });
        var schema = "matter_context_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            connection.SearchPath = schema;
            var options = new DbContextOptionsBuilder<LacDbContext>().UseNpgsql(connection.ConnectionString, p =>
            { p.MigrationsHistoryTable("__EFMigrationsHistory", schema); p.EnableRetryOnFailure(2); }).Options;
            await using var migrated = new LacDbContext(options); await migrated.Database.MigrateAsync();
            Assert.Contains(await migrated.Database.GetAppliedMigrationsAsync(), x => x.EndsWith("AddMatterCanonicalContext"));
            await using var f = await Fixture.Create(options); var matter = await f.NewMatter();
            await using var firstDb = new LacDbContext(options); await using var secondDb = new LacDbContext(options);
            async Task<int> Link(LacDbContext db)
            {
                try { await f.CreateWorkflow(db).ChangeContextLinkAsync(matter.Id, MatterContextKind.Khasra, f.Khasra.Id, true, new(0), f.User.Id); return 200; }
                catch (MatterWorkflowException ex) { return ex.StatusCode; }
            }
            var statuses = await Task.WhenAll(Link(firstDb), Link(secondDb)); Assert.Contains(200, statuses); Assert.Contains(409, statuses);
            f.Db.ChangeTracker.Clear(); Assert.Single(await f.Db.MatterKhasras.ToListAsync());
            await f.Workflow.ChangeContextLinkAsync(matter.Id, MatterContextKind.Award, f.Award.Id, true, new(1, true), f.User.Id);
            await f.Workflow.ChangeContextLinkAsync(matter.Id, MatterContextKind.Award, f.OtherAward.Id, true, new(2, true), f.User.Id);
            Assert.Equal(f.OtherAward.Id, (await f.Db.MatterAwards.AsNoTracking().SingleAsync(x => x.IsPrimary)).AwardId);
            Assert.Equal(2, await f.Db.MatterAwards.CountAsync());
            // Canonical Court SQL/NDOH projection is exercised on PostgreSQL too.
            await f.Workflow.ChangeContextLinkAsync(matter.Id, MatterContextKind.CourtCase, f.Court.Id, true, new(3), f.User.Id);
            Assert.Single((await f.Query.GetAsync(matter.Id, f.User.Id)).CourtCases);
            var bad = await Assert.ThrowsAsync<MatterWorkflowException>(() => f.Workflow.UpdateMetadataAsync(matter.Id,
                new("Rejected", "General", null, null, null, 4, AwardIds: [f.Award.Id], PrimaryAwardId: f.Award.Id, CourtCaseIds: [Guid.NewGuid()]), f.User.Id));
            Assert.Equal(403, bad.StatusCode); f.Db.ChangeTracker.Clear();
            Assert.Equal(4, (await f.Db.Matters.SingleAsync()).Revision); Assert.Equal("Office file", (await f.Db.Matters.SingleAsync()).Title);
            Assert.Equal(f.OtherAward.Id, (await f.Db.MatterAwards.SingleAsync(x => x.IsPrimary)).AwardId);
            f.Db.MatterAwards.Single(x => x.AwardId == f.Award.Id).IsPrimary = true;
            var conflict = await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
            Assert.Equal("23505", Assert.IsType<PostgresException>(conflict.InnerException).SqlState);
        }
        finally { await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin); await drop.ExecuteNonQueryAsync(); }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public LacDbContext Db { get; }
        public AppUser User { get; } = new() { Username = Guid.NewGuid().ToString("N"), NormalizedUsername = Guid.NewGuid().ToString("N"), DisplayName = "Officer", PasswordHash = "x", IsActive = true };
        public Role Role { get; } = new() { Code = Guid.NewGuid().ToString("N"), Name = "Test permissions", IsActive = true };
        public Village Village { get; } = new() { Name = "Test Village", SubDivision = new() { Name = "Test subdivision", District = new() { Name = "Test district" } } };
        public Workstream Workstream { get; } = new() { Code = WorkstreamCodes.CourtReferences, Name = "Court workstream", IsActive = true };
        public OfficeDesk Desk { get; } = new() { Code = "D1", Name = "Responsible desk", IsActive = true };
        public Award Award { get; } = new() { AwardNumber = "63/86-87" };
        public Award OtherAward { get; } = new() { AwardNumber = "30/2002-03" };
        public Khasra Khasra { get; private set; } = null!;
        public CourtCase Court { get; } = new() { CaseNumber = "W.P.(C) 223/2026", CourtName = "Delhi High Court", CaseTitle = "Ramesh Kumar", CurrentStatus = "Pending" };
        public CourtCase OtherCourt { get; } = new() { CaseNumber = "W.P.(C) 222/2026", CourtName = "Delhi High Court", CaseTitle = "Another petitioner", CurrentStatus = "Pending" };
        public Dak Dak { get; } = new() { DiaryNumber = "D/1", Subject = "Reply requested", ReceivedDate = new(2026, 10, 1) };
        public AccessControlService Access => new(Db, new TestUser(User));
        public MatterWorkflowService Workflow => CreateWorkflow(Db);
        public MatterContextQuery Query => new(Db, new MatterAuthorizationService(Db), CourtAuthorization(Db), new DakAuthorizationService(Db),
            new WorkItemAuthorizationService(Db), new OutwardAuthorizationService(Db));
        private Fixture(DbContextOptions<LacDbContext> options) { Db = new(options); }
        private CourtAuthorizationService CourtAuthorization(LacDbContext db) => new(db, new WorkItemAuthorizationService(db),
            new MatterAuthorizationService(db), new ServiceCollection().BuildServiceProvider());
        public MatterWorkflowService CreateWorkflow(LacDbContext db) => new(db, new TestInMemoryDocumentStorage(), new MatterAuthorizationService(db),
            new AccessControlService(db, new TestUser(User)), courtAuth: CourtAuthorization(db), dakAuth: new DakAuthorizationService(db));
        public static async Task<Fixture> Create(DbContextOptions<LacDbContext>? options = null)
        {
            var f = new Fixture(options ?? new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
            f.Khasra = new() { Village = f.Village, DisplayNumber = "12", NormalizedNumber = "12" };
            f.Db.AddRange(f.Village, f.Workstream, f.User, f.Role, f.Desk, f.Court, f.OtherCourt, f.Dak, f.Khasra,
                new UserRole { UserId = f.User.Id, RoleId = f.Role.Id }, new AwardVillage { Award = f.Award, Village = f.Village },
                new AwardVillage { Award = f.OtherAward, Village = f.Village });
            await f.Db.SaveChangesAsync();
            foreach (var code in new[] { PermissionCodes.MatterView, PermissionCodes.MatterCreate, PermissionCodes.MatterEdit,
                         PermissionCodes.AwardView, PermissionCodes.KhasraView, PermissionCodes.CourtView, PermissionCodes.DakView,
                         PermissionCodes.VillageView, PermissionCodes.WorkItemView, PermissionCodes.OutwardView, PermissionCodes.DraftView }) await f.Grant(code);
            return f;
        }
        public async Task Grant(string code, ScopeMode scope = ScopeMode.All)
        {
            // CourtCase is the entity name; its canonical permission is Court.View.
            if (code == "CourtCase.View") code = PermissionCodes.CourtView;
            var permission = await Db.Permissions.FirstOrDefaultAsync(x => x.Code == code);
            if (permission is null) { permission = new() { Code = code, Name = code }; Db.Add(permission); }
            Db.Add(new RolePermission { RoleId = Role.Id, PermissionId = permission.Id, ScopeMode = scope }); await Db.SaveChangesAsync();
        }
        public async Task Revoke(string code)
        {
            if (code == "CourtCase.View") code = PermissionCodes.CourtView;
            Db.RolePermissions.RemoveRange(await Db.RolePermissions.Where(x => x.RoleId == Role.Id && x.Permission.Code == code).ToListAsync());
            await Db.SaveChangesAsync();
        }
        public Guid Target(MatterContextKind kind) => kind switch { MatterContextKind.Award => Award.Id, MatterContextKind.Khasra => Khasra.Id,
            MatterContextKind.CourtCase => Court.Id, _ => Dak.Id };
        public Task<Matter> NewMatter() => Workflow.CreateMatterAsync(new(Village.Id, "Office file", "General", Workstream.Id), User.Id);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestUser(AppUser user) : ICurrentUserContext
    {
        public bool IsAuthenticated => true; public Guid? UserId => user.Id; public string? Username => user.Username; public string? DisplayName => user.DisplayName;
        public Guid? DesignationId => null; public string? DesignationCode => null; public string? DesignationName => null;
        public IReadOnlyList<string> Roles => []; public IReadOnlyList<string> Permissions => []; public IReadOnlyList<Guid> WorkstreamIds => [];
        public IReadOnlyList<string> WorkstreamCodes => []; public IReadOnlyList<Guid> DeskIds => []; public IReadOnlyList<string> DeskCodes => []; public Guid? PrimaryDeskId => null;
    }
}
