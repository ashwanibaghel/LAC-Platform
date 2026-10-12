using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LAC.Tests;

public sealed class VillageAcquisitionHistoryTests
{
    [Fact]
    public async Task Two_awards_never_share_a_chain_based_on_chronology_village_or_overlapping_parcels()
    {
        await using var db = Db(); var f = SeedTwoAwards(db);
        var notices = new[]
        {
            Notice("4", new(2000, 12, 1)), Notice("6", new(2001, 12, 1)), Notice("17(1)", new(2002, 9, 1))
        };
        // Both Awards and every notification refer to the same parcel. Only B owns the notification links.
        foreach (var notice in notices) db.AddRange(new AwardNotification { Award = f.B, Notification = notice },
            new NotificationKhasra { Notification = notice, Khasra = f.Parcel });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        var history = (await VillageAcquisitionHistoryQueries.ReadAsync(db, f.Village.Id, true, default))!;
        var a = history.Awards.Single(x => x.AwardId == f.A.Id);
        var b = history.Awards.Single(x => x.AwardId == f.B.Id);
        Assert.Equal(f.A.Id, Assert.Single(a.Events).EventId); Assert.Equal("Award", a.Events[0].Type);
        Assert.Equal(["4", "6", "17(1)"], b.Events.Where(x => x.Type == "Notification").Select(x => x.Section));
        Assert.Equal([new DateOnly(2000, 12, 1), new(2001, 12, 1), new(2002, 9, 1), new(2002, 12, 9)], b.Events.Select(x => x.Date!.Value));
        Assert.All(b.Events.Where(x => x.Type == "Notification"), x =>
        {
            Assert.Equal("ExplicitAwardNotificationLink", x.RelationshipBasis); Assert.Equal([f.B.Id], x.LinkedAwardIds);
        });
        Assert.Empty(history.UnassignedEvents);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(3, await db.AwardNotifications.CountAsync()); Assert.Equal(3, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task Unlinked_village_notifications_remain_unassigned_even_before_award_dates()
    {
        await using var db = Db(); var f = SeedTwoAwards(db);
        var notice = Notice("4", new(1980, 1, 1));
        // Two parcel relationships still represent one canonical notification.
        var secondParcel = new Khasra { Village = f.Village, NormalizedNumber = "2//2", DisplayNumber = "2//2" };
        db.AddRange(new NotificationKhasra { Notification = notice, Khasra = f.Parcel }, new NotificationKhasra { Notification = notice, Khasra = secondParcel });
        await db.SaveChangesAsync();
        var history = (await VillageAcquisitionHistoryQueries.ReadAsync(db, f.Village.Id, true, default))!;
        var unassigned = Assert.Single(history.UnassignedEvents);
        Assert.Equal(notice.Id, unassigned.NotificationId); Assert.Equal("NoExplicitAwardNotificationLink", unassigned.RelationshipBasis);
        Assert.Empty(unassigned.LinkedAwardIds); Assert.False(unassigned.IsShared);
        Assert.All(history.Awards, award => Assert.Equal("Award", Assert.Single(award.Events).Type));
        Assert.Empty(await db.AwardNotifications.ToListAsync());
    }

    [Fact]
    public async Task Shared_notification_appears_only_under_explicitly_linked_awards_and_keeps_one_identity()
    {
        await using var db = Db(); var f = SeedTwoAwards(db);
        var c = new Award { AwardNumber = "OTHER-CHAIN", AwardDate = new(2005, 1, 1) };
        var notice = Notice("4", new(2000, 1, 1));
        db.AddRange(new AwardVillage { Award = c, Village = f.Village }, new AwardNotification { Award = f.A, Notification = notice },
            new AwardNotification { Award = f.B, Notification = notice }); await db.SaveChangesAsync();
        var history = (await VillageAcquisitionHistoryQueries.ReadAsync(db, f.Village.Id, true, default))!;
        foreach (var id in new[] { f.A.Id, f.B.Id })
        {
            var item = Assert.Single(history.Awards.Single(x => x.AwardId == id).Events, x => x.Type == "Notification");
            Assert.Equal(notice.Id, item.EventId); Assert.Equal(notice.Id, item.NotificationId); Assert.True(item.IsShared);
            Assert.Equal(new[] { f.A.Id, f.B.Id }.Order(), item.LinkedAwardIds);
        }
        Assert.Equal("Award", Assert.Single(history.Awards.Single(x => x.AwardId == c.Id).Events).Type);
        Assert.Empty(history.UnassignedEvents); Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal(2, await db.AwardNotifications.CountAsync());
    }

    [Fact]
    public async Task Possession_uses_its_explicit_award_and_sorts_after_award_when_its_date_is_later()
    {
        await using var db = Db(); var f = SeedTwoAwards(db);
        var possession = new PossessionEvent { Award = f.B, PossessionDate = new(2003, 1, 1), EventType = "Memo", Status = "Partial" };
        db.Add(new PossessionKhasra { PossessionEvent = possession, Khasra = f.Parcel }); await db.SaveChangesAsync();
        var history = (await VillageAcquisitionHistoryQueries.ReadAsync(db, f.Village.Id, true, default))!;
        Assert.Equal("Award", Assert.Single(history.Awards.Single(x => x.AwardId == f.A.Id).Events).Type);
        var events = history.Awards.Single(x => x.AwardId == f.B.Id).Events;
        Assert.Equal(["Award", "Possession"], events.Select(x => x.Type));
        Assert.Equal(possession.Id, events[1].PossessionEventId); Assert.Equal("ExplicitPossessionAwardLink", events[1].RelationshipBasis);
        Assert.Equal([f.B.Id], events[1].LinkedAwardIds); Assert.Equal("Partial", events[1].Status);
        var restricted = (await VillageAcquisitionHistoryQueries.ReadAsync(db, f.Village.Id, false, default))!;
        Assert.False(restricted.PossessionEventsVisible); Assert.DoesNotContain(restricted.Awards.SelectMany(x => x.Events), x => x.Type == "Possession");
    }

    [Fact]
    public async Task Undated_events_are_retained_last_and_ties_are_deterministic_without_timestamp_inference()
    {
        await using var db = Db(); var f = SeedTwoAwards(db);
        f.A.AwardDate = null;
        var undated = Notice("6", null); var dated = Notice("4", f.B.AwardDate);
        db.AddRange(new AwardNotification { Award = f.B, Notification = undated }, new AwardNotification { Award = f.B, Notification = dated });
        await db.SaveChangesAsync();
        var first = (await VillageAcquisitionHistoryQueries.ReadAsync(db, f.Village.Id, true, default))!;
        var second = (await VillageAcquisitionHistoryQueries.ReadAsync(db, f.Village.Id, true, default))!;
        Assert.Null(first.Awards.Single(x => x.AwardId == f.A.Id).Events.Single().Date);
        var events = first.Awards.Single(x => x.AwardId == f.B.Id).Events;
        Assert.Equal(undated.Id, events[^1].EventId); Assert.Null(events[^1].Date);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public async Task Unrelated_or_archived_records_and_shared_project_ids_do_not_establish_membership()
    {
        await using var db = Db(); var f = SeedTwoAwards(db);
        var project = new AcquisitionProject { Name = "Shared project" }; f.B.AcquisitionProject = project;
        var unrelated = Notice("4", new(2001, 1, 1)); unrelated.AcquisitionProject = project;
        var archived = Notice("6", new(2001, 2, 1)); archived.RecordStatus = RecordStatus.Archived;
        var hiddenAward = new Award { AwardNumber = "ARCHIVED", RecordStatus = RecordStatus.Archived };
        db.AddRange(unrelated, new AwardNotification { Award = f.B, Notification = archived }, new AwardVillage { Award = hiddenAward, Village = f.Village },
            new PossessionEvent { Award = f.B, RecordStatus = RecordStatus.Inactive }); await db.SaveChangesAsync();
        var history = (await VillageAcquisitionHistoryQueries.ReadAsync(db, f.Village.Id, true, default))!;
        Assert.Equal(2, history.Awards.Count); Assert.Empty(history.UnassignedEvents);
        Assert.All(history.Awards, award => Assert.Equal("Award", Assert.Single(award.Events).Type));
    }

    [Fact]
    public async Task Notification_linked_only_to_an_outside_award_is_not_guessed_into_local_awards_or_labelled_unassigned()
    {
        await using var db = Db(); var f = SeedTwoAwards(db);
        var outside = new Award { AwardNumber = "OUTSIDE" }; var notice = Notice("4", new(2000, 1, 1));
        db.AddRange(new AwardNotification { Award = outside, Notification = notice }, new NotificationKhasra { Notification = notice, Khasra = f.Parcel });
        await db.SaveChangesAsync();
        var history = (await VillageAcquisitionHistoryQueries.ReadAsync(db, f.Village.Id, true, default))!;
        Assert.Empty(history.UnassignedEvents); Assert.Equal(2, history.Awards.Count);
        Assert.All(history.Awards, award => Assert.Equal("Award", Assert.Single(award.Events).Type));
    }

    [Fact]
    public async Task Parcel_linked_award_is_included_once_without_creating_an_award_village_link()
    {
        await using var db = Db(); var f = SeedTwoAwards(db);
        var parcelAward = new Award { AwardNumber = "PARCEL-ONLY", AwardDate = new(2010, 1, 1) };
        db.Add(new AwardKhasra { Award = parcelAward, Khasra = f.Parcel }); await db.SaveChangesAsync();
        var history = (await VillageAcquisitionHistoryQueries.ReadAsync(db, f.Village.Id, true, default))!;
        Assert.Equal(3, history.Awards.Count); Assert.Equal("Award", Assert.Single(history.Awards.Single(x => x.AwardId == parcelAward.Id).Events).Type);
        Assert.False(await db.AwardVillages.AnyAsync(x => x.AwardId == parcelAward.Id));
    }

    [Fact]
    public async Task Unassigned_events_are_chronological_and_an_empty_village_returns_empty_collections()
    {
        await using var db = Db(); var village = new Village { Name = "Empty Estate" }; db.Add(village); await db.SaveChangesAsync();
        var empty = (await VillageAcquisitionHistoryQueries.ReadAsync(db, village.Id, true, default))!;
        Assert.Empty(empty.Awards); Assert.Empty(empty.UnassignedEvents);
        var parcel = new Khasra { Village = village, NormalizedNumber = "3//1", DisplayNumber = "3//1" };
        foreach (var date in new DateOnly?[] { null, new(2001, 1, 1), new(1999, 1, 1) }) db.Add(new NotificationKhasra { Notification = Notice("4", date), Khasra = parcel });
        await db.SaveChangesAsync(); var history = (await VillageAcquisitionHistoryQueries.ReadAsync(db, village.Id, true, default))!;
        Assert.Equal(new DateOnly?[] { new(1999, 1, 1), new(2001, 1, 1), null }, history.UnassignedEvents.Select(x => x.Date));
        Assert.Null(await VillageAcquisitionHistoryQueries.ReadAsync(db, Guid.NewGuid(), true, default));
        village.RecordStatus = RecordStatus.Archived; await db.SaveChangesAsync();
        Assert.Null(await VillageAcquisitionHistoryQueries.ReadAsync(db, village.Id, true, default));
    }

    private static LacDbContext Db() => new(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Notification Notice(string section, DateOnly? date) => new() { SectionType = section, NotificationDate = date, NotificationNumber = "REF-" + Guid.NewGuid().ToString("N") };
    private static (Village Village, Award A, Award B, Khasra Parcel) SeedTwoAwards(LacDbContext db)
    {
        var village = new Village { Name = "Fictional Estate", SubDivision = new() { Name = "Subdivision", District = new() { Name = "District" } } };
        var a = new Award { AwardNumber = "63/86-87", AwardDate = new(1986, 9, 19) };
        var b = new Award { AwardNumber = "30/2002-03", AwardDate = new(2002, 12, 9) };
        var parcel = new Khasra { Village = village, NormalizedNumber = "2//1", DisplayNumber = "2//1" };
        db.AddRange(new AwardVillage { Award = a, Village = village }, new AwardVillage { Award = b, Village = village },
            new AwardKhasra { Award = a, Khasra = parcel }, new AwardKhasra { Award = b, Khasra = parcel });
        return (village, a, b, parcel);
    }
}

public sealed class VillageAcquisitionHistoryApiTests : IClassFixture<RbacFactory>
{
    private readonly RbacFactory factory;
    public VillageAcquisitionHistoryApiTests(RbacFactory factory) => this.factory = factory;

    [Fact]
    public async Task Endpoint_requires_authentication_and_returns_404_for_missing_village()
    {
        using var client = factory.CreateClient(); var route = $"/api/villages/{Guid.NewGuid()}/acquisition-history";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
    }

    [Theory]
    [InlineData(PermissionCodes.VillageView)]
    [InlineData(PermissionCodes.AwardView)]
    public async Task Endpoint_checks_both_permissions(string deniedPermission)
    {
        await using var restricted = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAccessControlService>(); services.AddScoped<IAccessControlService>(_ => new RestrictedAccess(deniedPermission));
        }));
        using var client = restricted.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/villages/{Guid.NewGuid()}/acquisition-history")).StatusCode);
    }

    [Fact]
    public async Task Endpoint_serializes_grouped_contract_and_honours_possession_workstream_visibility()
    {
        Guid villageId; Guid awardId; Guid notificationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var village = new Village { Name = "API History Estate" }; var award = new Award { AwardNumber = "API-HISTORY", AwardDate = new(2002, 12, 9) };
            var notification = new Notification { NotificationNumber = "SEC4-API", SectionType = "4", NotificationDate = new(2000, 1, 1) };
            db.AddRange(new AwardVillage { Award = award, Village = village }, new AwardNotification { Award = award, Notification = notification },
                new PossessionEvent { Award = award, PossessionDate = new(2003, 1, 1) }); await db.SaveChangesAsync();
            villageId = village.Id; awardId = award.Id; notificationId = notification.Id;
        }
        using var client = factory.CreateClient(); await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));
        var response = await client.GetAsync($"/api/villages/{villageId}/acquisition-history"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(villageId, body.RootElement.GetProperty("villageId").GetGuid());
        var awardPayload = Assert.Single(body.RootElement.GetProperty("awards").EnumerateArray()); Assert.Equal(awardId, awardPayload.GetProperty("awardId").GetGuid());
        var events = awardPayload.GetProperty("events"); Assert.Equal(3, events.GetArrayLength());
        Assert.Equal(notificationId, events[0].GetProperty("notificationId").GetGuid());
        Assert.Equal("ExplicitAwardNotificationLink", events[0].GetProperty("relationshipBasis").GetString());
        Assert.Equal("2002-12-09", events[1].GetProperty("date").GetString());
        Assert.True(body.RootElement.GetProperty("possessionEventsVisible").GetBoolean()); Assert.Empty(body.RootElement.GetProperty("unassignedEvents").EnumerateArray());

        await using var restricted = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAccessControlService>(); services.AddScoped<IAccessControlService>(_ => new RestrictedAccess(null, true));
        }));
        using var restrictedClient = restricted.CreateClient(); await restrictedClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(RbacFactory.TestAdminUser, RbacFactory.TestAdminPass));
        var history = await restrictedClient.GetFromJsonAsync<VillageAcquisitionHistory>($"/api/villages/{villageId}/acquisition-history");
        Assert.False(history!.PossessionEventsVisible); Assert.DoesNotContain(history.Awards.SelectMany(x => x.Events), x => x.Type == "Possession");
    }

    private sealed class RestrictedAccess(string? deniedPermission, bool hidePossession = false) : IAccessControlService
    {
        public Task<bool> CanAsync(string permissionCode, AccessResourceContext? resourceContext = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(permissionCode != deniedPermission && (!hidePossession || resourceContext?.WorkstreamCode != WorkstreamCodes.Possession));
        public Task<IReadOnlyDictionary<string, ScopeMode>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, ScopeMode>>(new Dictionary<string, ScopeMode>());
    }
}
