using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddDbContext<LacDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IDocumentStorage, LocalDocumentStorage>();
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
        new ProjectReference(x.AcquisitionProject.Id, x.AcquisitionProject.Name, x.AcquisitionProject.RequiringAgency, x.AcquisitionProject.ActRegime),
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
    var register = await db.VillageLRs.AsNoTracking().Where(x => x.Id == id).Select(x => new VillageLrDetail(x.Id, x.VillageId, x.RegisterReference, x.Remarks, x.Entries.OrderBy(e => e.RowNumber).Select(e => new LrEntryItem(e.Id, e.VillageLRId, e.RawKhasraText, e.RawAreaText, e.RawRemarks, e.VerificationStatus.ToString())).ToList())).FirstOrDefaultAsync(ct);
    return register is null ? NotFound("Village LR register", id) : Results.Ok(register);
});

api.MapPost("/village-lrs", async (CreateVillageLrRequest request, LacDbContext db, CancellationToken ct) =>
{
    if (request.VillageId == Guid.Empty) return Validation("villageId", "A village must be selected.");
    if (!await db.Villages.AnyAsync(x => x.Id == request.VillageId, ct)) return NotFound("Village", request.VillageId);
    var register = new VillageLR { VillageId = request.VillageId, RegisterReference = request.RegisterReference?.Trim(), Remarks = request.Remarks?.Trim() };
    db.VillageLRs.Add(register); await db.SaveChangesAsync(ct);
    return Results.Created($"/api/village-lrs/{register.Id}", new IdResponse(register.Id));
});

api.MapPost("/village-lrs/{id:guid}/entries", async (Guid id, CreateLrEntryRequest request, LacDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.RawKhasraText)) return Validation("rawKhasraText", "Khasra transcription is required.");
    if (!await db.VillageLRs.AnyAsync(x => x.Id == id, ct)) return NotFound("Village LR register", id);
    var entry = new LREntry { VillageLRId = id, RowNumber = request.RowNumber, RawKhasraText = request.RawKhasraText.Trim(), RawAreaText = request.RawAreaText?.Trim(), RawRemarks = request.RawRemarks?.Trim(), KhasraId = request.KhasraId, AwardId = request.AwardId, Section4NotificationId = request.Section4NotificationId, Section6NotificationId = request.Section6NotificationId, VerificationStatus = VerificationStatus.Draft };
    db.LREntries.Add(entry); await db.SaveChangesAsync(ct);
    return Results.Created($"/api/lr-entries/{entry.Id}", new IdResponse(entry.Id));
});

app.Run();

static IResult NotFound(string entityName, Guid id) => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: $"{entityName} not found", detail: $"No {entityName.ToLowerInvariant()} exists for id {id}.");
static IResult Validation(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
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
public sealed record NotificationDetail(Guid Id, string SectionType, string NotificationNumber, DateOnly? NotificationDate, string? GazetteDetails, string? Remarks, ProjectReference Project, IReadOnlyList<NotificationKhasraItem> Khasras, IReadOnlyList<DocumentListItem> Documents);
public sealed record SearchResultItem(string Type, Guid Id, string Label, string? Context, string Route);
public sealed record VillageLrListItem(Guid Id, string? RegisterReference, int EntryCount);
public sealed record VillageLrDetail(Guid Id, Guid VillageId, string? RegisterReference, string? Remarks, IReadOnlyList<LrEntryItem> Entries);
public sealed record CreateVillageLrRequest(Guid VillageId, string? RegisterReference, string? Remarks);
public sealed record CreateLrEntryRequest(int? RowNumber, string RawKhasraText, string? RawAreaText, string? RawRemarks, Guid? KhasraId, Guid? AwardId, Guid? Section4NotificationId, Guid? Section6NotificationId);
public sealed record IdResponse(Guid Id);
