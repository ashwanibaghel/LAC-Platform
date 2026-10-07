using System.Text.Json.Nodes;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LAC.Tests;

public sealed class CourtScopePersistenceTests
{
    internal const string Url = "https://delhihighcourt.nic.in/app/showlogo/scope-fixture.pdf/2026";
    internal static JsonObject Scope() => new()
    {
        ["contract"] = CourtScopeContract.Version,
        ["source"] = new JsonObject { ["pdfSha256"] = new string('a', 64), ["officialUrl"] = Url, ["orderDate"] = "2026-10-07" },
        ["extraction"] = new JsonObject { ["state"] = "Validated", ["fullRelevantTextChecked"] = true },
        ["lacRelevant"] = false, ["lacRelevanceState"] = "NotRelevant", ["lacAuthorityScope"] = "UnknownLAC", ["lacActionable"] = false,
        ["missingState"] = "NotStated",
        ["purposes"] = new JsonArray(), ["villages"] = new JsonArray(), ["awards"] = new JsonArray(), ["parcels"] = new JsonArray(),
        ["parcelGroups"] = new JsonArray(), ["possession"] = new JsonArray(), ["compensation"] = new JsonArray(),
        ["directions"] = new JsonArray(), ["directionChanges"] = new JsonArray(), ["evidence"] = new JsonArray(), ["relevanceBasis"] = new JsonArray()
    };
    internal static (CourtCase Case, CourtExternalOrderObservation Observation) Source(LacDbContext db)
    {
        var actor = new AppUser { Username = "scope-officer", NormalizedUsername = "SCOPE-OFFICER", DisplayName = "Scope officer" };
        var c = new CourtCase { CaseNumber = "W.P.(C) 42/2026", CourtName = "Delhi High Court" };
        var run = new DhcAssistedSyncRun { StartedByUser = actor };
        var item = new DhcAssistedSyncItem { Run = run, CourtCase = c };
        var o = new CourtExternalOrderObservation { CourtCase = c, RunItem = item, RawCaseNumber = c.CaseNumber,
            NormalizedCaseIdentity = "delhihighcourt|wpc|42|2026", OrderDate = new DateOnly(2026, 10, 7), OfficialUrl = Url };
        db.Add(o); return (c, o);
    }
    [Fact]
    public async Task Repeated_observation_keeps_one_order_and_immutable_provenance_reloads()
    {
        var options = new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new LacDbContext(options); var (c, o) = Source(db); await db.SaveChangesAsync();
        var service = new CourtIntelligencePersistence(db);
        var first = await service.PersistAsync(c.Id, o.Id, o.OrderDate!.Value, Url, Scope());
        Assert.Equal(first.Id, (await service.PersistAsync(c.Id, o.Id, o.OrderDate.Value, Url, Scope())).Id);
        var another = new CourtExternalOrderObservation { CourtCaseId = c.Id, RunItemId = o.RunItemId,
            RawCaseNumber = c.CaseNumber, NormalizedCaseIdentity = o.NormalizedCaseIdentity, OfficialUrl = Url, OrderDate = o.OrderDate };
        db.Add(another); await db.SaveChangesAsync();
        var next = await service.PersistAsync(c.Id, another.Id, o.OrderDate.Value, Url, Scope());
        Assert.Equal(first.CourtOrderIntelligenceId, next.CourtOrderIntelligenceId);
        Assert.Single(await db.CourtOrderIntelligence.ToListAsync());
        db.ChangeTracker.Clear(); var reloaded = await db.CourtOrderIntelligenceRevisions.SingleAsync(x => x.Id == first.Id);
        Assert.Equal(first.PdfSha256, reloaded.PdfSha256); Assert.Equal(Scope().ToJsonString(), reloaded.StructuredFactsJson);
        reloaded.StructuredFactsJson = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Empty(db.CourtProceedings); Assert.Empty(db.Awards); Assert.Empty(db.Khasras);
    }
    [Fact]
    public void Incomplete_scope_cannot_claim_absence_or_model_office_authority()
    {
        var scope = Scope(); scope["extraction"]!["fullRelevantTextChecked"] = false;
        Assert.Throws<InvalidDataException>(() => CourtScopeContract.Validate(scope, new string('a',64)));
        scope = Scope(); scope["lacActionable"] = true;
        Assert.Throws<InvalidDataException>(() => CourtScopeContract.Validate(scope, new string('a',64)));
    }
    [Fact]
    public async Task Cross_case_observation_is_rejected()
    {
        await using var db = new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var (_, o) = Source(db); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => new CourtIntelligencePersistence(db).PersistAsync(Guid.NewGuid(), o.Id, o.OrderDate!.Value, Url, Scope()));
        Assert.Empty(db.CourtOrderIntelligence);
    }
    [Fact]
    public async Task Corrigendum_is_independent_and_links_only_to_the_observed_original()
    {
        await using var db=new LacDbContext(new DbContextOptionsBuilder<LacDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var (c,o)=Source(db);o.CorrigendumUrl=Url.Replace("scope-fixture","scope-correction");await db.SaveChangesAsync();
        var service=new CourtIntelligencePersistence(db);var original=await service.PersistAsync(c.Id,o.Id,o.OrderDate!.Value,Url,Scope());
        var corrected=Scope();corrected["source"]!["officialUrl"]=o.CorrigendumUrl;
        var revision=await service.PersistAsync(c.Id,o.Id,o.OrderDate.Value,o.CorrigendumUrl,corrected,"Corrigendum");
        var correction=await db.CourtOrderIntelligence.SingleAsync(x=>x.Id==revision.CourtOrderIntelligenceId);
        Assert.NotEqual(original.CourtOrderIntelligenceId,correction.Id);Assert.Equal(original.CourtOrderIntelligenceId,correction.CorrectsOrderId);
        Assert.Equal("Corrigendum",correction.SourceKind);Assert.Equal(2,await db.CourtOrderIntelligence.CountAsync());Assert.Empty(db.CourtProceedings);
    }
    [DakPostgresFact]
    public async Task Postgres_additive_migration_uniqueness_jsonb_and_revision_reload()
    {
        await using var database = await DisposableDakDatabase.CreateAsync();
        await using var db = database.Context(); var (c,o) = Source(db); await db.SaveChangesAsync();
        var first = await new CourtIntelligencePersistence(db).PersistAsync(c.Id,o.Id,o.OrderDate!.Value,Url,Scope());
        db.ChangeTracker.Clear();
        Assert.True(JsonNode.DeepEquals(Scope(), JsonNode.Parse((await db.CourtOrderIntelligenceRevisions.SingleAsync()).StructuredFactsJson)));
        db.Add(new CourtOrderIntelligence { CourtCaseId=c.Id,OrderDate=o.OrderDate.Value,OfficialUrl=Url });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.NotEqual(Guid.Empty,first.Id);
    }
}
