using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddDbContext<LacDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IDocumentStorage, LocalDocumentStorage>();
builder.Services.AddScoped<LrWorkflowService>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
    if (db.Database.IsRelational()) await db.Database.MigrateAsync();
    else await db.Database.EnsureCreatedAsync();
    await SeedData.SeedAsync(db, CancellationToken.None);
}

var api = app.MapGroup("/api");

api.MapGet("/districts", async (LacDbContext db, CancellationToken ct) =>
    await db.Districts.AsNoTracking().OrderBy(x => x.Name).Select(x => new DistrictListItem(x.Id, x.Name, x.SubDivisions.Count)).ToListAsync(ct));

api.MapGet("/districts/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var district = await db.Districts.AsNoTracking().Where(x => x.Id == id).Select(x => new DistrictDetail(x.Id, x.Name,
        x.SubDivisions.OrderBy(s => s.Name).Select(s => new SubDivisionListItem(s.Id, s.Name, s.Villages.Count)).ToList())).FirstOrDefaultAsync(ct);
    return district is null ? NotFound("District", id) : Results.Ok(district);
});

api.MapGet("/subdivisions/{id:guid}", async (Guid id, int page, int pageSize, string? q, LacDbContext db, CancellationToken ct) =>
{
    var subdivision = await db.SubDivisions.AsNoTracking().Where(x => x.Id == id).Select(x => new SubDivisionSummary(x.Id, x.Name, new DistrictReference(x.District.Id, x.District.Name), x.Villages.Count)).FirstOrDefaultAsync(ct);
    if (subdivision is null) return NotFound("Sub-division", id);
    var villages = db.Villages.AsNoTracking().Where(x => x.SubDivisionId == id);
    if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim().ToUpperInvariant(); villages = villages.Where(x => x.Name.ToUpper().Contains(term)); }
    var result = await ToPageAsync(villages.OrderBy(x => x.Name).Select(x => new VillageListItem(x.Id, x.Name, x.Khasras.Count)), page, pageSize, ct);
    return Results.Ok(new SubDivisionDetail(subdivision.Id, subdivision.Name, subdivision.District, subdivision.VillageCount, result));
});

api.MapGet("/villages", async (Guid? subDivisionId, int page, int pageSize, string? q, LacDbContext db, CancellationToken ct) =>
{
    var villages = db.Villages.AsNoTracking().AsQueryable();
    if (subDivisionId is not null) villages = villages.Where(x => x.SubDivisionId == subDivisionId);
    if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim().ToUpperInvariant(); villages = villages.Where(x => x.Name.ToUpper().Contains(term)); }
    return Results.Ok(await ToPageAsync(villages.OrderBy(x => x.Name).Select(x => new VillageListItem(x.Id, x.Name, x.Khasras.Count)), page, pageSize, ct));
});

api.MapGet("/villages/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var village = await db.Villages.AsNoTracking().Where(x => x.Id == id).Select(x => new VillageDetail(x.Id, x.Name,
        new SubDivisionReference(x.SubDivision.Id, x.SubDivision.Name, new DistrictReference(x.SubDivision.District.Id, x.SubDivision.District.Name)),
        x.Khasras.Count, x.Khasras.SelectMany(k => k.AwardLinks).Select(link => link.AwardId).Distinct().Count(), x.DocumentRelationships.Count, db.VillageLRs.Any(lr => lr.VillageId == x.Id))).FirstOrDefaultAsync(ct);
    return village is null ? NotFound("Village", id) : Results.Ok(village);
});

api.MapGet("/villages/{id:guid}/khasras", async (Guid id, int page, int pageSize, string? q, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Villages.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village", id);
    var khasras = db.Khasras.AsNoTracking().Where(x => x.VillageId == id);
    if (!string.IsNullOrWhiteSpace(q))
    {
        var term = KhasraNumber.Normalize(q);
        khasras = khasras.Where(x => x.NormalizedNumber.Contains(term) || x.DisplayNumber.ToUpper().Contains(q.Trim().ToUpperInvariant()));
    }
    return Results.Ok(await ToPageAsync(khasras.OrderBy(x => x.DisplayNumber).Select(KhasraListItem.Selector), page, pageSize, ct));
});

api.MapGet("/villages/{id:guid}/awards", async (Guid id, int page, int pageSize, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Villages.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village", id);
    return Results.Ok(await ToPageAsync(db.Awards.AsNoTracking().Where(a => a.KhasraLinks.Any(link => link.Khasra.VillageId == id)).OrderByDescending(x => x.AwardDate).Select(AwardListItem.Selector), page, pageSize, ct));
});

api.MapGet("/villages/{id:guid}/notifications", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Villages.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village", id);
    return Results.Ok(await db.Notifications.AsNoTracking().Where(n => n.KhasraLinks.Any(link => link.Khasra.VillageId == id)).OrderByDescending(n => n.NotificationDate).Select(NotificationListItem.Selector).ToListAsync(ct));
});

api.MapGet("/villages/{id:guid}/lrs", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Villages.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village", id);
    return Results.Ok(await db.VillageLRs.AsNoTracking().Where(lr => lr.VillageId == id).OrderBy(lr => lr.RegisterReference).Select(lr => new VillageLrListItem(lr.Id, lr.RegisterReference, lr.Entries.Count)).ToListAsync(ct));
});

api.MapGet("/villages/{id:guid}/documents", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Villages.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village", id);
    return Results.Ok(await db.DocumentVillages.AsNoTracking().Where(link => link.VillageId == id).OrderByDescending(link => link.Document.UploadedAt).Select(link => new DocumentListItem(link.Document.Id, link.Document.OriginalFileName, link.Document.DocumentType, link.Document.UploadedAt, link.Document.Status)).ToListAsync(ct));
});

api.MapGet("/khasras/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var khasra = await db.Khasras.AsNoTracking().Where(x => x.Id == id).Select(x => new KhasraDetail(x.Id, x.DisplayNumber, x.NormalizedNumber, x.RectangleNumber, x.KillaNumber, x.SubdivisionNumber, x.TotalArea, x.AreaUnit, x.Remarks,
        new VillageReference(x.Village.Id, x.Village.Name, new SubDivisionReference(x.Village.SubDivision.Id, x.Village.SubDivision.Name, new DistrictReference(x.Village.SubDivision.District.Id, x.Village.SubDivision.District.Name))),
        x.NotificationLinks.OrderBy(n => n.Notification.NotificationDate).Select(n => new NotificationLinkItem(n.Notification.Id, n.Notification.NotificationNumber, n.Notification.SectionType, n.Notification.NotificationDate, n.NotifiedArea, n.AreaUnit)).ToList(),
        x.AwardLinks.OrderBy(a => a.Award.AwardNumber).Select(a => new AwardLinkItem(a.Award.Id, a.Award.AwardNumber, a.AcquiredArea, a.AreaUnit, a.AcquisitionStatus)).ToList(),
        db.LREntries.Where(lr => lr.KhasraId == x.Id).OrderByDescending(lr => lr.UpdatedAt).Select(lr => new LrEntryItem(lr.Id, lr.VillageLRId, lr.RawKhasraText, lr.RawAreaText, lr.RawRemarks, lr.VerificationStatus.ToString())).ToList())).FirstOrDefaultAsync(ct);
    return khasra is null ? NotFound("Khasra", id) : Results.Ok(khasra);
});

api.MapGet("/awards", async (int page, int pageSize, string? q, LacDbContext db, CancellationToken ct) =>
{
    var awards = db.Awards.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim().ToUpperInvariant(); awards = awards.Where(x => x.AwardNumber.ToUpper().Contains(term)); }
    return Results.Ok(await ToPageAsync(awards.OrderByDescending(x => x.AwardDate).ThenBy(x => x.AwardNumber).Select(AwardListItem.Selector), page, pageSize, ct));
});

api.MapGet("/awards/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var award = await db.Awards.AsNoTracking().Where(x => x.Id == id).Select(x => new AwardDetail(x.Id, x.AwardNumber, x.AwardDate, x.AwardType, x.Status, x.ActRegime, x.Remarks,
        x.AcquisitionProject == null ? null : new ProjectReference(x.AcquisitionProject.Id, x.AcquisitionProject.Name, x.AcquisitionProject.RequiringAgency, x.AcquisitionProject.ActRegime),
        x.KhasraLinks.Count, x.KhasraLinks.Sum(link => link.AcquiredArea),
        x.KhasraLinks.OrderBy(link => link.Khasra.DisplayNumber).Select(link => new AwardKhasraItem(link.Khasra.Id, link.Khasra.DisplayNumber, link.Khasra.Village.Name, link.AcquiredArea, link.AreaUnit, link.AcquisitionStatus)).ToList(),
        x.KhasraLinks.SelectMany(link => link.Khasra.NotificationLinks).Select(link => new NotificationLinkItem(link.Notification.Id, link.Notification.NotificationNumber, link.Notification.SectionType, link.Notification.NotificationDate, link.NotifiedArea, link.AreaUnit)).Distinct().ToList(),
        x.DocumentRelationships.OrderByDescending(link => link.Document.UploadedAt).Select(link => new DocumentListItem(link.Document.Id, link.Document.OriginalFileName, link.Document.DocumentType, link.Document.UploadedAt, link.Document.Status)).ToList())).FirstOrDefaultAsync(ct);
    return award is null ? NotFound("Award", id) : Results.Ok(award);
});

api.MapGet("/notifications", async (int page, int pageSize, string? q, LacDbContext db, CancellationToken ct) =>
{
    var notifications = db.Notifications.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim().ToUpperInvariant(); notifications = notifications.Where(x => x.NotificationNumber.ToUpper().Contains(term)); }
    return Results.Ok(await ToPageAsync(notifications.OrderByDescending(x => x.NotificationDate).Select(NotificationListItem.Selector), page, pageSize, ct));
});

api.MapGet("/notifications/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var notification = await db.Notifications.AsNoTracking().Where(x => x.Id == id).Select(x => new NotificationDetail(x.Id, x.SectionType, x.NotificationNumber, x.NotificationDate, x.GazetteDetails, x.Remarks,
        x.AcquisitionProject == null ? null : new ProjectReference(x.AcquisitionProject.Id, x.AcquisitionProject.Name, x.AcquisitionProject.RequiringAgency, x.AcquisitionProject.ActRegime),
        x.KhasraLinks.OrderBy(link => link.Khasra.DisplayNumber).Select(link => new NotificationKhasraItem(link.Khasra.Id, link.Khasra.DisplayNumber, link.Khasra.Village.Name, link.NotifiedArea, link.AreaUnit)).ToList(),
        x.DocumentRelationships.OrderByDescending(link => link.Document.UploadedAt).Select(link => new DocumentListItem(link.Document.Id, link.Document.OriginalFileName, link.Document.DocumentType, link.Document.UploadedAt, link.Document.Status)).ToList())).FirstOrDefaultAsync(ct);
    return notification is null ? NotFound("Notification", id) : Results.Ok(notification);
});

api.MapGet("/documents", async (int page, int pageSize, LacDbContext db, CancellationToken ct) => Results.Ok(await ToPageAsync(db.Documents.AsNoTracking().OrderByDescending(x => x.UploadedAt).Select(x => new DocumentListItem(x.Id, x.OriginalFileName, x.DocumentType, x.UploadedAt, x.Status)), page, pageSize, ct)));

api.MapGet("/search", async (string? q, LacDbContext db, CancellationToken ct) =>
{
    var input = q?.Trim() ?? string.Empty;
    if (input.Length < 2) return Results.Ok(Array.Empty<SearchResultItem>());
    var normalizedKhasra = KhasraNumber.Normalize(input);
    var term = input.ToUpperInvariant();
    var villages = await db.Villages.AsNoTracking().Where(x => x.Name.ToUpper().Contains(term)).Take(8).Select(x => new SearchResultItem("Village", x.Id, x.Name, x.SubDivision.Name, $"/villages/{x.Id}")).ToListAsync(ct);
    var khasras = await db.Khasras.AsNoTracking().Where(x => x.NormalizedNumber.Contains(normalizedKhasra) || x.DisplayNumber.ToUpper().Contains(term)).Take(12).Select(x => new SearchResultItem("Khasra", x.Id, x.DisplayNumber, x.Village.Name, $"/khasras/{x.Id}")).ToListAsync(ct);
    var awards = await db.Awards.AsNoTracking().Where(x => x.AwardNumber.ToUpper().Contains(term)).Take(8).Select(x => new SearchResultItem("Award", x.Id, x.AwardNumber, x.AcquisitionProject == null ? null : x.AcquisitionProject.Name, $"/awards/{x.Id}")).ToListAsync(ct);
    return Results.Ok(villages.Concat(khasras).Concat(awards));
});

api.MapGet("/village-lrs/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var register = await db.VillageLRs.AsNoTracking().Where(x => x.Id == id).Select(x => new VillageLrDetail(
        x.Id, x.VillageId, x.RegisterReference, x.Remarks, x.Village.Name,
        x.Entries.Count,
        x.Entries.Count(e => e.VerificationStatus == VerificationStatus.Draft),
        x.Entries.Count(e => e.VerificationStatus == VerificationStatus.NeedsReview),
        x.Entries.Count(e => e.VerificationStatus == VerificationStatus.Verified),
        x.Entries.Count(e => e.VerificationStatus == VerificationStatus.Committed),
        x.DocumentRelationships.OrderByDescending(link => link.Document.UploadedAt).Select(link => new DocumentListItem(link.Document.Id, link.Document.OriginalFileName, link.Document.DocumentType, link.Document.UploadedAt, link.Document.Status)).FirstOrDefault())).FirstOrDefaultAsync(ct);
    return register is null ? NotFound("Village LR register", id) : Results.Ok(register);
});

api.MapGet("/village-lrs/{id:guid}/entries", async (Guid id, int page, int pageSize, string? status, string? q, Guid? khasraId, Guid? awardId, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.VillageLRs.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village LR register", id);
    var rows = db.LREntries.AsNoTracking().Where(x => x.VillageLRId == id);
    if (Enum.TryParse<VerificationStatus>(status, true, out var parsedStatus)) rows = rows.Where(x => x.VerificationStatus == parsedStatus);
    if (khasraId is not null) rows = rows.Where(x => x.KhasraId == khasraId);
    if (awardId is not null) rows = rows.Where(x => x.AwardId == awardId);
    if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim().ToUpperInvariant(); rows = rows.Where(x => x.RawKhasraText.ToUpper().Contains(term)); }
    return Results.Ok(await ToPageAsync(rows.OrderBy(x => x.RowNumber).ThenBy(x => x.CreatedAt).Select(LrEntryDetailItem.Selector), page, pageSize, ct));
});

api.MapGet("/villages/{id:guid}/lr-progress", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Villages.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village", id);
    var rows = db.LREntries.AsNoTracking().Where(x => x.VillageLR.VillageId == id);
    return Results.Ok(new LrProgress(
        await rows.CountAsync(ct),
        await rows.CountAsync(x => x.VerificationStatus == VerificationStatus.Draft, ct),
        await rows.CountAsync(x => x.VerificationStatus == VerificationStatus.NeedsReview, ct),
        await rows.CountAsync(x => x.VerificationStatus == VerificationStatus.Verified, ct),
        await rows.CountAsync(x => x.VerificationStatus == VerificationStatus.Committed, ct)));
});

api.MapGet("/lr-review", async (Guid? villageId, Guid? registerId, string? status, Guid? khasraId, Guid? awardId, int page, int pageSize, LacDbContext db, CancellationToken ct) =>
{
    var rows = db.LREntries.AsNoTracking().AsQueryable();
    if (villageId is not null) rows = rows.Where(x => x.VillageLR.VillageId == villageId);
    if (registerId is not null) rows = rows.Where(x => x.VillageLRId == registerId);
    if (khasraId is not null) rows = rows.Where(x => x.KhasraId == khasraId);
    if (awardId is not null) rows = rows.Where(x => x.AwardId == awardId);
    if (Enum.TryParse<VerificationStatus>(status, true, out var parsedStatus)) rows = rows.Where(x => x.VerificationStatus == parsedStatus);
    else rows = rows.Where(x => x.VerificationStatus == VerificationStatus.NeedsReview || x.VerificationStatus == VerificationStatus.Draft);
    return Results.Ok(await ToPageAsync(rows.OrderBy(x => x.VerificationStatus).ThenBy(x => x.VillageLR.Village.Name).ThenBy(x => x.RowNumber).Select(LrReviewItem.Selector), page, pageSize, ct));
});

api.MapPost("/village-lrs", async (CreateVillageLrRequest request, LacDbContext db, CancellationToken ct) =>
{
    if (request.VillageId == Guid.Empty) return Validation("villageId", "A village must be selected.");
    if (!await db.Villages.AnyAsync(x => x.Id == request.VillageId, ct)) return NotFound("Village", request.VillageId);
    var register = new VillageLR { VillageId = request.VillageId, RegisterReference = request.RegisterReference?.Trim(), Remarks = request.Remarks?.Trim() };
    db.VillageLRs.Add(register); await db.SaveChangesAsync(ct);
    return Results.Created($"/api/village-lrs/{register.Id}", new IdResponse(register.Id));
});

api.MapPost("/villages/{id:guid}/khasras", async (Guid id, CreateKhasraRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try { var khasra = await workflow.CreateKhasraAsync(id, request.DisplayNumber, request.TotalArea, request.AreaUnit, request.RectangleNumber, request.KillaNumber, request.SubdivisionNumber, ct); return Results.Created($"/api/khasras/{khasra.Id}", new IdResponse(khasra.Id)); }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
});

api.MapPost("/notifications", async (CreateNotificationRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try { var notification = await workflow.CreateNotificationAsync(request.SectionType, request.NotificationNumber, request.NotificationDate, request.Remarks, ct); return Results.Created($"/api/notifications/{notification.Id}", new IdResponse(notification.Id)); }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
});

api.MapPost("/awards", async (CreateAwardRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try { var award = await workflow.CreateAwardAsync(request.AwardNumber, request.AwardDate, request.AwardType, request.ActRegime, ct); return Results.Created($"/api/awards/{award.Id}", new IdResponse(award.Id)); }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
});

api.MapPost("/village-lrs/{id:guid}/entries", async (Guid id, LrEntryRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try { var saved = await workflow.CreateAsync(id, request.ToInput(), ct); return Results.Created($"/api/lr-entries/{saved.Id}", saved); }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
});

api.MapPost("/village-lrs/{id:guid}/entries/batch", async (Guid id, BatchLrEntryRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try
    {
        var saved = new List<LrRowSaveResult>();
        foreach (var row in request.Rows) saved.Add(await workflow.CreateAsync(id, row.ToInput(), ct));
        return Results.Created($"/api/village-lrs/{id}/entries", saved);
    }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
});

api.MapPut("/lr-entries/{id:guid}", async (Guid id, UpdateLrEntryRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try { return Results.Ok(await workflow.UpdateAsync(id, request.ExpectedRevision, request.Row.ToInput(), ct)); }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
});

api.MapPost("/lr-entries/{id:guid}/commit", async (Guid id, CommitLrEntryRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try { return Results.Ok(await workflow.CommitAsync(id, request.ExpectedRevision, request.ApplyParsedAreaToAcquisitionLinks, ct)); }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
});

app.Run();

static IResult NotFound(string entityName, Guid id) => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: $"{entityName} not found", detail: $"No {entityName.ToLowerInvariant()} exists for id {id}.");
static IResult Validation(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
static IResult WorkflowProblem(LrWorkflowException exception) => Results.Problem(statusCode: exception.StatusCode, title: "LR workflow validation", detail: exception.Message);
static async Task<PageResponse<T>> ToPageAsync<T>(IQueryable<T> query, int page, int pageSize, CancellationToken ct)
{
    page = Math.Max(page, 0); pageSize = Math.Clamp(pageSize == 0 ? 25 : pageSize, 1, 100);
    var totalCount = await query.CountAsync(ct); var items = await query.Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
    return new PageResponse<T>(items, page, pageSize, totalCount);
}

public partial class Program { }
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
public sealed record DistrictListItem(Guid Id, string Name, int SubDivisionCount);
public sealed record DistrictReference(Guid Id, string Name);
public sealed record DistrictDetail(Guid Id, string Name, IReadOnlyList<SubDivisionListItem> SubDivisions);
public sealed record SubDivisionListItem(Guid Id, string Name, int VillageCount);
public sealed record SubDivisionSummary(Guid Id, string Name, DistrictReference District, int VillageCount);
public sealed record SubDivisionDetail(Guid Id, string Name, DistrictReference District, int VillageCount, PageResponse<VillageListItem> Villages);
public sealed record VillageListItem(Guid Id, string Name, int KhasraCount);
public sealed record SubDivisionReference(Guid Id, string Name, DistrictReference District);
public sealed record VillageReference(Guid Id, string Name, SubDivisionReference SubDivision);
public sealed record VillageDetail(Guid Id, string Name, SubDivisionReference SubDivision, int TotalKhasras, int LinkedAwards, int DocumentCount, bool LrAvailable);
public sealed record AwardLinkItem(Guid Id, string AwardNumber, decimal? AcquiredArea, string? AreaUnit, string? AcquisitionStatus);
public sealed record KhasraListItem(Guid Id, string DisplayNumber, decimal? TotalArea, string? AreaUnit, IReadOnlyList<AwardLinkItem> Awards) { public static readonly System.Linq.Expressions.Expression<Func<Khasra, KhasraListItem>> Selector = x => new KhasraListItem(x.Id, x.DisplayNumber, x.TotalArea, x.AreaUnit, x.AwardLinks.OrderBy(a => a.Award.AwardNumber).Select(a => new AwardLinkItem(a.Award.Id, a.Award.AwardNumber, a.AcquiredArea, a.AreaUnit, a.AcquisitionStatus)).ToList()); }
public sealed record NotificationLinkItem(Guid Id, string NotificationNumber, string SectionType, DateOnly? NotificationDate, decimal? Area, string? AreaUnit);
public sealed record LrEntryItem(Guid Id, Guid VillageLrId, string RawKhasraText, string? RawAreaText, string? RawRemarks, string VerificationStatus);
public sealed record KhasraDetail(Guid Id, string DisplayNumber, string NormalizedNumber, string? RectangleNumber, string? KillaNumber, string? SubdivisionNumber, decimal? TotalArea, string? AreaUnit, string? Remarks, VillageReference Village, IReadOnlyList<NotificationLinkItem> Notifications, IReadOnlyList<AwardLinkItem> Awards, IReadOnlyList<LrEntryItem> LrEntries);
public sealed record ProjectReference(Guid Id, string Name, string? RequiringAgency, string? ActRegime);
public sealed record AwardListItem(Guid Id, string AwardNumber, DateOnly? AwardDate, string? AwardType, string Status, string? ActRegime, string? ProjectName, string? RequiringAgency, int LinkedKhasraCount) { public static readonly System.Linq.Expressions.Expression<Func<Award, AwardListItem>> Selector = x => new AwardListItem(x.Id, x.AwardNumber, x.AwardDate, x.AwardType, x.Status, x.ActRegime, x.AcquisitionProject == null ? null : x.AcquisitionProject.Name, x.AcquisitionProject == null ? null : x.AcquisitionProject.RequiringAgency, x.KhasraLinks.Count); }
public sealed record AwardKhasraItem(Guid Id, string DisplayNumber, string VillageName, decimal? AcquiredArea, string? AreaUnit, string? AcquisitionStatus);
public sealed record DocumentListItem(Guid Id, string OriginalFileName, string DocumentType, DateTimeOffset UploadedAt, string Status);
public sealed record AwardDetail(Guid Id, string AwardNumber, DateOnly? AwardDate, string? AwardType, string Status, string? ActRegime, string? Remarks, ProjectReference? Project, int LinkedKhasraCount, decimal? TotalAcquiredArea, IReadOnlyList<AwardKhasraItem> Khasras, IReadOnlyList<NotificationLinkItem> Notifications, IReadOnlyList<DocumentListItem> Documents);
public sealed record NotificationListItem(Guid Id, string NotificationNumber, string SectionType, DateOnly? NotificationDate) { public static readonly System.Linq.Expressions.Expression<Func<Notification, NotificationListItem>> Selector = x => new NotificationListItem(x.Id, x.NotificationNumber, x.SectionType, x.NotificationDate); }
public sealed record NotificationKhasraItem(Guid Id, string DisplayNumber, string VillageName, decimal? NotifiedArea, string? AreaUnit);
public sealed record NotificationDetail(Guid Id, string SectionType, string NotificationNumber, DateOnly? NotificationDate, string? GazetteDetails, string? Remarks, ProjectReference? Project, IReadOnlyList<NotificationKhasraItem> Khasras, IReadOnlyList<DocumentListItem> Documents);
public sealed record SearchResultItem(string Type, Guid Id, string Label, string? Context, string Route);
public sealed record VillageLrListItem(Guid Id, string? RegisterReference, int EntryCount);
public sealed record VillageLrDetail(Guid Id, Guid VillageId, string? RegisterReference, string? Remarks, string VillageName, int TotalRows, int DraftCount, int NeedsReviewCount, int VerifiedCount, int CommittedCount, DocumentListItem? SourceDocument);
public sealed record CreateVillageLrRequest(Guid VillageId, string? RegisterReference, string? Remarks);
public sealed record CreateKhasraRequest(string DisplayNumber, decimal? TotalArea, string? AreaUnit, string? RectangleNumber, string? KillaNumber, string? SubdivisionNumber);
public sealed record CreateNotificationRequest(string SectionType, string NotificationNumber, DateOnly? NotificationDate, string? Remarks);
public sealed record CreateAwardRequest(string AwardNumber, DateOnly? AwardDate, string? AwardType, string? ActRegime);
public sealed record LrEntryRequest(int? RowNumber, string RawKhasraText, Guid? KhasraId, string? RawAreaText, decimal? ParsedArea, string? AreaUnit, Guid? Section4NotificationId, Guid? Section6NotificationId, Guid? AwardId, string? RawRemarks, VerificationStatus VerificationStatus)
{
    public LrRowInput ToInput() => new(RowNumber, RawKhasraText, KhasraId, RawAreaText, ParsedArea, AreaUnit, Section4NotificationId, Section6NotificationId, AwardId, RawRemarks, VerificationStatus);
}
public sealed record UpdateLrEntryRequest(int ExpectedRevision, LrEntryRequest Row);
public sealed record BatchLrEntryRequest(IReadOnlyList<LrEntryRequest> Rows);
public sealed record CommitLrEntryRequest(int ExpectedRevision, bool ApplyParsedAreaToAcquisitionLinks);
public sealed record LrEntryDetailItem(Guid Id, int Revision, int? RowNumber, string RawKhasraText, Guid? KhasraId, string? KhasraDisplayNumber, string? RawAreaText, decimal? ParsedArea, string? AreaUnit, Guid? AwardId, string? AwardNumber, Guid? Section4NotificationId, string? Section4Number, Guid? Section6NotificationId, string? Section6Number, string? RawRemarks, string VerificationStatus)
{
    public static readonly System.Linq.Expressions.Expression<Func<LREntry, LrEntryDetailItem>> Selector = x => new LrEntryDetailItem(x.Id, x.Revision, x.RowNumber, x.RawKhasraText, x.KhasraId, x.Khasra == null ? null : x.Khasra.DisplayNumber, x.RawAreaText, x.ParsedArea, x.AreaUnit, x.AwardId, x.Award == null ? null : x.Award.AwardNumber, x.Section4NotificationId, x.Section4Notification == null ? null : x.Section4Notification.NotificationNumber, x.Section6NotificationId, x.Section6Notification == null ? null : x.Section6Notification.NotificationNumber, x.RawRemarks, x.VerificationStatus.ToString());
}
public sealed record LrReviewItem(Guid Id, Guid VillageLrId, Guid VillageId, string VillageName, string? RegisterReference, int? RowNumber, string RawKhasraText, Guid? KhasraId, string? KhasraDisplayNumber, Guid? AwardId, string? AwardNumber, string VerificationStatus, int Revision)
{
    public static readonly System.Linq.Expressions.Expression<Func<LREntry, LrReviewItem>> Selector = x => new LrReviewItem(x.Id, x.VillageLRId, x.VillageLR.VillageId, x.VillageLR.Village.Name, x.VillageLR.RegisterReference, x.RowNumber, x.RawKhasraText, x.KhasraId, x.Khasra == null ? null : x.Khasra.DisplayNumber, x.AwardId, x.Award == null ? null : x.Award.AwardNumber, x.VerificationStatus.ToString(), x.Revision);
}
public sealed record LrProgress(int TotalRows, int Draft, int NeedsReview, int Verified, int Committed);
public sealed record IdResponse(Guid Id);
