namespace LAC.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

public sealed class DakDirectoryTests(DakTestFactory factory) : IClassFixture<DakTestFactory>
{
    [Theory]
    [InlineData("receivedFrom")][InlineData("receivedTo")][InlineData("dateRange")]
    [InlineData("inwardMode")][InlineData("categoryId")][InlineData("workstreamId")]
    [InlineData("deskId")][InlineData("handlerId")][InlineData("sender")]
    [InlineData("senderDepartment")][InlineData("hasDocument")][InlineData("withoutDocument")]
    [InlineData("status")][InlineData("priority")][InlineData("diary")]
    [InlineData("subject")][InlineData("senderSearch")][InlineData("reference")][InlineData("combined")]
    public async Task Filters_are_applied_before_count_and_pagination(string filter)
    {
        var scenario = await DakDirectoryScenario.CreateAsync(factory);
        await scenario.VerifyFilterAsync(filter);
    }

    [Theory]
    [InlineData("Dak.Register", 0, false)][InlineData("Dak.View", 0, true)]
    [InlineData("Dak.Move", 0, false)][InlineData("Dak.Receive", 0, false)]
    [InlineData("Dak.View", 1, true)][InlineData("Dak.View", 2, true)]
    public async Task Filters_never_expand_permission_or_desk_scope(string permission, int desks, bool allowed)
    {
        var scenario = await DakDirectoryScenario.CreateAsync(factory);
        await scenario.VerifyPermissionAsync(permission, desks, allowed);
    }

    [Fact]
    public async Task Date_validation_and_active_lookups_use_canonical_records()
    {
        var scenario = await DakDirectoryScenario.CreateAsync(factory);
        await scenario.VerifyLookupsAsync();
        foreach (var query in new[] { "receivedFrom=2026-10-09&receivedTo=2026-10-08", "receivedFrom=invalid", "hasDocument=invalid", "handlerId=invalid" })
            Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.GetAsync("/api/dak?" + query)).StatusCode);
    }
}

public sealed class DakDirectoryPostgresTests
{
    [DakPostgresFact]
    public async Task Real_Postgres_directory_filters_counts_scopes_and_active_lookups()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        using var factory = new DakDirectoryPgFactory(database.ConnectionString);
        var scenario = await DakDirectoryScenario.CreateAsync(factory);
        foreach (var filter in DakDirectoryScenario.FilterNames) await scenario.VerifyFilterAsync(filter);
        foreach (var permission in new[] { PermissionCodes.DakRegister, PermissionCodes.DakMove, PermissionCodes.DakReceive })
            await scenario.VerifyPermissionAsync(permission, 0, false);
        foreach (var desks in new[] { 0, 1, 2 }) await scenario.VerifyPermissionAsync(PermissionCodes.DakView, desks, true);
        await scenario.VerifyLookupsAsync();
    }
}

internal sealed class DakDirectoryPgFactory(string connection) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BootstrapAdmin:Username"] = DakTestFactory.TestAdminUser,
            ["BootstrapAdmin:Password"] = DakTestFactory.TestAdminPass
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LacDbContext>>(); services.RemoveAll<LacDbContext>();
            services.AddDbContext<LacDbContext>(o => o.UseNpgsql(connection));
        });
    }
}

internal sealed class DakDirectoryScenario(WebApplicationFactory<Program> factory, HttpClient admin, string prefix,
    Guid first, Guid second, Guid category, Guid workstream, Guid desk, Guid otherDesk, Guid holder,
    Guid inactiveCategory, Guid inactiveWorkstream, Guid inactiveDesk, Guid inactiveHolder)
{
    public HttpClient Admin => admin;
    public static readonly string[] FilterNames = ["receivedFrom", "receivedTo", "dateRange", "inwardMode", "categoryId", "workstreamId", "deskId", "handlerId", "sender", "senderDepartment", "hasDocument", "withoutDocument", "status", "priority", "diary", "subject", "senderSearch", "reference", "combined"];

    public static async Task<DakDirectoryScenario> CreateAsync(WebApplicationFactory<Program> factory)
    {
        var admin = await LoginAsync(factory, DakTestFactory.TestAdminUser);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var prefix = Guid.NewGuid().ToString("N");
        var actor = await db.AppUsers.SingleAsync(u => u.Username == DakTestFactory.TestAdminUser);
        var category = new DakCategory { Code = prefix, Name = "Directory category" };
        var inactiveCategory = new DakCategory { Code = prefix + "X", Name = "Inactive category", IsActive = false };
        var workstream = new Workstream { Code = prefix, Name = "Directory workstream" };
        var inactiveWorkstream = new Workstream { Code = prefix + "X", Name = "Inactive workstream", IsActive = false };
        var desk = new OfficeDesk { Code = prefix, Name = "Directory desk" };
        var otherDesk = new OfficeDesk { Code = prefix + "B", Name = "Other desk" };
        var inactiveDesk = new OfficeDesk { Code = prefix + "X", Name = "Inactive desk", IsActive = false };
        var holder = new AppUser { Username = prefix, NormalizedUsername = prefix.ToUpperInvariant(), DisplayName = "Directory holder" };
        var inactiveHolder = new AppUser { Username = prefix + "X", NormalizedUsername = prefix.ToUpperInvariant() + "X", DisplayName = "Inactive holder", IsActive = false };
        var role = new Role { Code = prefix, Name = "Directory receiver" };
        db.AddRange(category, inactiveCategory, workstream, inactiveWorkstream, desk, otherDesk, inactiveDesk, holder, inactiveHolder, role);
        await db.SaveChangesAsync();
        var receive = await db.Permissions.SingleAsync(p => p.Code == PermissionCodes.DakReceive);
        db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = receive.Id, ScopeMode = ScopeMode.Assigned });
        foreach (var u in new[] { holder, inactiveHolder })
        {
            db.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = role.Id });
            db.UserDeskMemberships.Add(new UserDeskMembership { UserId = u.Id, OfficeDeskId = desk.Id, IsActive = true });
        }
        var doc = new Document { OriginalFileName = "directory.pdf", StoragePath = prefix, MimeType = "application/pdf" }; db.Documents.Add(doc);
        var first = new Dak { DiaryNumber = prefix + "-MATCH", Subject = "Subjectneedle", SenderName = "Senderneedle", SenderDepartment = "Departmentneedle", SenderReferenceNumber = "Refneedle", ReceivedDate = new(2026, 10, 8), InwardMode = "Email", Priority = DakPriority.Immediate, CategoryId = category.Id, WorkstreamId = workstream.Id };
        var second = new Dak { DiaryNumber = prefix + "-OTHER", Subject = "Other", SenderName = "Other", ReceivedDate = new(2026, 10, 1), InwardMode = "Physical", Status = DakStatus.Registered };
        db.Daks.AddRange(first, second); await db.SaveChangesAsync();
        db.DakAttachments.Add(new DakAttachment { DakId = first.Id, DocumentId = doc.Id, Title = "Active attachment only" });
        db.DakAttachments.Add(new DakAttachment { DakId = second.Id, DocumentId = doc.Id, Title = "Archived attachment", RecordStatus = RecordStatus.Archived });
        db.DakAssignments.Add(new DakAssignment { DakId = second.Id, OfficeDeskId = otherDesk.Id, AssignedByUserId = actor.Id });
        await db.SaveChangesAsync();
        var workflow = new DakWorkflowService(db, new TestInMemoryDocumentStorage());
        await TestWorkAllocations.GrantGlobalAsync(db, holder.Id);
        var sent = await workflow.SendAsync(first.Id, new SendDakCommand(DakMovementAction.Marked, desk.Id, holder.Id,
            DakDestinationKind.Officer, false, "Directory fixture", null, 0, Guid.NewGuid()), actor.Id);
        await workflow.ReceiveAsync(first.Id, sent.TransferId!.Value, new ReceiveDakCommand(sent.Revision, Guid.NewGuid()), holder.Id);
        return new(factory, admin, prefix, first.Id, second.Id, category.Id, workstream.Id, desk.Id, otherDesk.Id, holder.Id,
            inactiveCategory.Id, inactiveWorkstream.Id, inactiveDesk.Id, inactiveHolder.Id);
    }

    private string Query(string filter) => filter switch
    {
        "receivedFrom" => "receivedFrom=2026-10-08", "receivedTo" => "receivedTo=2026-10-01",
        "dateRange" => "receivedFrom=2026-10-08&receivedTo=2026-10-08", "inwardMode" => "inwardMode=email",
        "categoryId" => $"categoryId={category}", "workstreamId" => $"workstreamId={workstream}",
        "deskId" => $"deskId={desk}", "handlerId" => $"handlerId={holder}",
        "sender" => "sender=senderNEEDLE", "senderDepartment" => "sender=departmentNEEDLE",
        "hasDocument" => "hasDocument=true", "withoutDocument" => "hasDocument=false",
        "status" => "status=InProcess", "priority" => "priority=Immediate", "diary" => $"q={prefix}-MATCH",
        "subject" => "q=subjectneedle", "senderSearch" => "q=senderneedle", "reference" => "q=refneedle",
        "combined" => $"receivedFrom=2026-10-08&receivedTo=2026-10-08&categoryId={category}&workstreamId={workstream}&handlerId={holder}&deskId={desk}&inwardMode=Email&sender=departmentneedle&hasDocument=true&status=InProcess&priority=Immediate",
        _ => throw new ArgumentException(filter)
    };

    public async Task VerifyFilterAsync(string filter)
    {
        // Unique fixture restriction is independent of the tested search field.
        var fixture = filter is "diary" or "subject" or "senderSearch" or "reference" ? $"&deskId={desk}" : $"&q={prefix}";
        var query = Query(filter) + fixture + "&pageSize=1";
        var result = await ReadAsync(admin, query);
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        var row = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal(filter is "receivedTo" or "withoutDocument" ? second : first, row.GetProperty("id").GetGuid());
        Assert.Equal(filter is not ("receivedTo" or "withoutDocument"), row.GetProperty("hasDocument").GetBoolean());
        var emptyPage = await ReadAsync(admin, query + "&page=1");
        Assert.Equal(1, emptyPage.GetProperty("totalCount").GetInt32()); Assert.Empty(emptyPage.GetProperty("items").EnumerateArray());
    }

    public async Task VerifyPermissionAsync(string permission, int deskCount, bool allowed)
    {
        var name = Guid.NewGuid().ToString("N");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
            var role = new Role { Code = name, Name = name }; var user = new AppUser { Username = name, NormalizedUsername = name.ToUpperInvariant(), DisplayName = name };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, DakTestFactory.TestAdminPass);
            db.AddRange(role, user); await db.SaveChangesAsync();
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = (await db.Permissions.SingleAsync(p => p.Code == permission)).Id, ScopeMode = deskCount == 0 ? ScopeMode.All : ScopeMode.Assigned });
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            foreach (var id in new[] { desk, otherDesk }.Take(deskCount)) db.UserDeskMemberships.Add(new UserDeskMembership { UserId = user.Id, OfficeDeskId = id, IsActive = true });
            await db.SaveChangesAsync();
        }
        using var client = await LoginAsync(factory, name);
        var response = await client.GetAsync($"/api/dak?q={prefix}&pageSize=1");
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/api/dak/lookups/directory")).StatusCode);
        if (!allowed) return;
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(deskCount == 1 ? 1 : 2, data.GetProperty("totalCount").GetInt32());
        var restricted = await ReadAsync(client, $"q={prefix}&deskId={otherDesk}&hasDocument=false");
        Assert.Equal(deskCount == 1 ? 0 : 1, restricted.GetProperty("totalCount").GetInt32());
        var combination = await ReadAsync(client, Query("combined") + $"&q={prefix}");
        Assert.Equal(1, combination.GetProperty("totalCount").GetInt32());
    }

    public async Task VerifyLookupsAsync()
    {
        using var setup = factory.Services.CreateScope();
        var setupDb = setup.ServiceProvider.GetRequiredService<LacDbContext>();
        var name = "lookup-manager-" + Guid.NewGuid().ToString("N");
        var manager = new AppUser { Username = name, NormalizedUsername = name.ToUpperInvariant(), DisplayName = "Lookup administrator", IsActive = true };
        manager.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<AppUser>().HashPassword(manager, DakTestFactory.TestAdminPass);
        setupDb.AppUsers.Add(manager);
        setupDb.UserRoles.Add(new UserRole { UserId = manager.Id, RoleId = (await setupDb.Roles.SingleAsync(r => r.Code == "OFFICE_ADMIN")).Id });
        await setupDb.SaveChangesAsync();
        var response = await admin.GetAsync("/api/dak/lookups/directory"); response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(holder, data.GetProperty("handlers").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()));
        using var office = await LoginAsync(factory, name);
        response = await office.GetAsync("/api/dak/lookups/directory"); response.EnsureSuccessStatusCode();
        data = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var (key, active, inactive) in new[] { ("categories", category, inactiveCategory), ("workstreams", workstream, inactiveWorkstream), ("desks", desk, inactiveDesk), ("handlers", holder, inactiveHolder) })
        {
            var ids = data.GetProperty(key).EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList();
            Assert.Contains(active, ids); Assert.DoesNotContain(inactive, ids);
        }
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
        var membership = await db.UserDeskMemberships.SingleAsync(m => m.UserId == holder && m.OfficeDeskId == desk);
        membership.RemovedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
        var removed = await (await office.GetAsync("/api/dak/lookups/directory")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(holder, removed.GetProperty("handlers").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()));
        membership.RemovedAt = null;
        var role = await db.UserRoles.Where(r => r.UserId == holder).Select(r => r.Role).SingleAsync();
        role.IsActive = false; await db.SaveChangesAsync();
        var revoked = await (await office.GetAsync("/api/dak/lookups/directory")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(holder, revoked.GetProperty("handlers").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()));
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/dak?" + query); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory, string name)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, DakTestFactory.TestAdminPass))).StatusCode);
        return client;
    }
}
