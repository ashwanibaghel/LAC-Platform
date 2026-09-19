using LAC.Api;
using LAC.Domain;
using LAC.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);
var pdfMaxFileSizeMb = Math.Clamp(builder.Configuration.GetValue<int?>("PdfImport:MaxFileSizeMb") ?? 250, 1, 1024);
var pdfMaxRequestBytes = pdfMaxFileSizeMb * 1024L * 1024L;
var matterDocumentExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".png", ".jpg", ".jpeg", ".tif", ".tiff" };
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = pdfMaxRequestBytes);
builder.Services.Configure<Microsoft.AspNetCore.Builder.IISServerOptions>(options => options.MaxRequestBodySize = pdfMaxRequestBytes);
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
if (!builder.Environment.IsEnvironment("Testing"))
{
    var configuredConnection = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");
    var connection = new NpgsqlConnectionStringBuilder(configuredConnection)
    {
        Pooling = true,
        Timeout = 6,
        CommandTimeout = 15,
        KeepAlive = 30
    };
    if (connection.Host?.Contains("supabase", StringComparison.OrdinalIgnoreCase) == true) throw new InvalidOperationException("Supabase is not a local-first runtime database. Configure ConnectionStrings__DefaultConnection for local PostgreSQL at 127.0.0.1.");
    builder.Services.AddDbContextPool<LacDbContext>(options => options.UseNpgsql(connection.ConnectionString, npgsql => { npgsql.EnableRetryOnFailure(2); npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery); }));
}
builder.Services.AddSingleton<LocalStoragePaths>();
builder.Services.AddScoped<IDocumentStorage, LocalDocumentStorage>();
builder.Services.AddScoped<LrWorkflowService>();
builder.Services.AddScoped<OwnershipService>();
builder.Services.AddScoped<KhasraWorkspaceService>();
builder.Services.AddScoped<AwardWorkflowService>();
builder.Services.AddScoped<AwardIngestionService>();
builder.Services.AddScoped<NmWorkflowService>();
builder.Services.AddScoped<DocumentSourceCropService>();
builder.Services.AddScoped<DocumentPageImageService>();
builder.Services.AddSingleton<IAwardPdfJobQueue, AwardPdfJobQueue>();
builder.Services.Configure<DocumentIntelligenceOptions>(builder.Configuration.GetSection("DocumentIntelligence"));
builder.Services.AddScoped<ILocalDocumentIntelligenceClient, LocalDocumentIntelligenceClient>();
builder.Services.AddScoped<IOcrEngine, TesseractOcrEngine>();
builder.Services.AddSingleton<IAwardSectionClassifier, AwardSectionClassifier>();
builder.Services.AddSingleton<TextConceptMatcher>();
builder.Services.AddSingleton<StrictKhasraParser>();
builder.Services.AddSingleton<StrictDateParser>();
builder.Services.AddSingleton<StrictAreaParser>();
builder.Services.AddSingleton<AwardExtractionRuleEngine>();
builder.Services.AddScoped<AwardPdfExtractionService>();
builder.Services.AddScoped<AwardPdfJobRunner>();
builder.Services.AddScoped<IDakAuthorizationService, DakAuthorizationService>();
builder.Services.AddScoped<DakWorkflowService>();
builder.Services.AddScoped<IOutwardAuthorizationService, OutwardAuthorizationService>();
builder.Services.AddScoped<OutwardWorkflowService>();
builder.Services.AddScoped<IMatterAuthorizationService, MatterAuthorizationService>();
builder.Services.AddScoped<MatterWorkflowService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IOfficeClock, OfficeClock>();
builder.Services.AddHostedService<AwardPdfExtractionWorker>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
builder.Services.AddScoped<IAccessControlService, AccessControlService>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "lac_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
if (app.Environment.IsDevelopment()) app.UseDeveloperExceptionPage(); else app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
// The published React build is staged in wwwroot by scripts/publish-office.ps1.
// Development still uses Vite; IIS serves this same-site build in production.
app.UseDefaultFiles();
app.UseStaticFiles();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LacDbContext>();
    if (db.Database.IsRelational()) await db.Database.MigrateAsync();
    else await db.Database.EnsureCreatedAsync();
    await SeedData.SeedAsync(db, app.Configuration, app.Logger, CancellationToken.None);
}

var api = app.MapGroup("/api");
api.MapRbacEndpoints();
api.MapDakEndpoints();
api.MapOutwardEndpoints();
api.MapMatterEndpoints();
api.MapMatterDraftEndpoints();
api.AddEndpointFilter(async (context, next) =>
{
    var path = context.HttpContext.Request.Path.Value ?? "";
    if (path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(path, "/api/auth/login", StringComparison.OrdinalIgnoreCase))
    {
        return await next(context);
    }

    var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
    if (!currentUser.IsAuthenticated)
    {
        return Results.Unauthorized();
    }

    return await next(context);
});

api.MapGet("/home", async (LacDbContext db, IMemoryCache cache, CancellationToken ct) =>
    await cache.GetOrCreateAsync("administrative-home", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
        return await db.Districts.AsNoTracking().OrderBy(x => x.Name).Select(x => new DistrictDetail(x.Id, x.Name,
            x.SubDivisions.OrderBy(s => s.Name).Select(s => new SubDivisionListItem(s.Id, s.Name, s.Villages.Count)).ToList())).FirstOrDefaultAsync(ct);
    })).RequirePermission(PermissionCodes.VillageView);

api.MapGet("/health", async (LacDbContext db, IDocumentStorage storage, CancellationToken ct) =>
{
    var databaseReachable = await db.Database.CanConnectAsync(ct); var documentStorage = storage.GetHealth();
    return (databaseReachable && documentStorage.Writable)
        ? Results.Ok(new { status = "Healthy", database = "Reachable", documentStorage = new { writable = true, freeBytes = documentStorage.FreeBytes, totalBytes = documentStorage.TotalBytes } })
        : Results.Json(new { status = "Degraded", database = databaseReachable ? "Reachable" : "Unavailable", documentStorage = new { writable = documentStorage.Writable, freeBytes = documentStorage.FreeBytes, totalBytes = documentStorage.TotalBytes } }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

api.MapGet("/health/document-intelligence", (ILocalDocumentIntelligenceClient intelligence) =>
{
    var result = intelligence.GetPreflight();
    return result.Status == "Misconfigured"
        ? Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(result);
});

api.MapGet("/districts", async (LacDbContext db, IMemoryCache cache, CancellationToken ct) =>
    await cache.GetOrCreateAsync("administrative-districts", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
        return await db.Districts.AsNoTracking().OrderBy(x => x.Name).Select(x => new DistrictListItem(x.Id, x.Name, x.SubDivisions.Count)).ToListAsync(ct);
    })).RequirePermission(PermissionCodes.VillageView);

api.MapGet("/districts/{id:guid}", async (Guid id, LacDbContext db, IMemoryCache cache, CancellationToken ct) =>
{
    var district = await cache.GetOrCreateAsync($"administrative-district-{id}", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
        return await db.Districts.AsNoTracking().Where(x => x.Id == id).Select(x => new DistrictDetail(x.Id, x.Name,
            x.SubDivisions.OrderBy(s => s.Name).Select(s => new SubDivisionListItem(s.Id, s.Name, s.Villages.Count)).ToList())).FirstOrDefaultAsync(ct);
    });
    return district is null ? NotFound("District", id) : Results.Ok(district);
}).RequirePermission(PermissionCodes.VillageView);

api.MapGet("/subdivisions/{id:guid}", async (Guid id, int page, int pageSize, string? q, LacDbContext db, CancellationToken ct) =>
{
    var subdivision = await db.SubDivisions.AsNoTracking().Where(x => x.Id == id).Select(x => new SubDivisionSummary(x.Id, x.Name, new DistrictReference(x.District.Id, x.District.Name), x.Villages.Count)).FirstOrDefaultAsync(ct);
    if (subdivision is null) return NotFound("Sub-division", id);
    var villages = db.Villages.AsNoTracking().Where(x => x.SubDivisionId == id);
    if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim().ToUpperInvariant(); villages = villages.Where(x => x.Name.ToUpper().Contains(term)); }
    var result = await ToPageAsync(villages.OrderBy(x => x.Name).Select(x => new VillageListItem(x.Id, x.Name, x.Khasras.Count)), page, pageSize, ct);
    return Results.Ok(new SubDivisionDetail(subdivision.Id, subdivision.Name, subdivision.District, subdivision.VillageCount, result));
}).RequirePermission(PermissionCodes.VillageView);

api.MapGet("/villages", async (Guid? subDivisionId, int page, int pageSize, string? q, LacDbContext db, CancellationToken ct) =>
{
    var villages = db.Villages.AsNoTracking().AsQueryable();
    if (subDivisionId is not null) villages = villages.Where(x => x.SubDivisionId == subDivisionId);
    if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim().ToUpperInvariant(); villages = villages.Where(x => x.Name.ToUpper().Contains(term)); }
    return Results.Ok(await ToPageAsync(villages.OrderBy(x => x.Name).Select(x => new VillageListItem(x.Id, x.Name, x.Khasras.Count)), page, pageSize, ct));
}).RequirePermission(PermissionCodes.VillageView);

api.MapGet("/villages/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var village = await db.Villages.AsNoTracking().Where(x => x.Id == id).Select(x => new VillageDetail(x.Id, x.Name,
        new SubDivisionReference(x.SubDivision.Id, x.SubDivision.Name, new DistrictReference(x.SubDivision.District.Id, x.SubDivision.District.Name)),
        x.Khasras.Count, x.Khasras.SelectMany(k => k.AwardLinks).Select(link => link.AwardId).Distinct().Count(),
        x.DocumentRelationships.Count(link => link.Document.AwardLinks.Any() || db.NmDocuments.Any(nm => nm.DocumentId == link.DocumentId) || db.DocumentNotifications.Any(dn => dn.DocumentId == link.DocumentId)),
        db.VillageLRs.Any(lr => lr.VillageId == x.Id))).FirstOrDefaultAsync(ct);
    return village is null ? NotFound("Village", id) : Results.Ok(village);
}).RequirePermission(PermissionCodes.VillageView);

// This is deliberately a three-lane read model.  Canonical records, document-review
// work and missing source categories are returned separately so an OCR suggestion can
// never be rendered as an official village fact.
api.MapGet("/villages/{id:guid}/overview", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var village = await db.Villages.AsNoTracking().Where(x => x.Id == id).Select(x => new VillageDetail(x.Id, x.Name,
        new SubDivisionReference(x.SubDivision.Id, x.SubDivision.Name, new DistrictReference(x.SubDivision.District.Id, x.SubDivision.District.Name)),
        x.Khasras.Count, x.Khasras.SelectMany(k => k.AwardLinks).Select(link => link.AwardId).Distinct().Count(),
        x.DocumentRelationships.Count(link => link.Document.AwardLinks.Any() || db.NmDocuments.Any(nm => nm.DocumentId == link.DocumentId) || db.DocumentNotifications.Any(dn => dn.DocumentId == link.DocumentId)),
        db.VillageLRs.Any(lr => lr.VillageId == x.Id))).FirstOrDefaultAsync(ct);
    if (village is null) return NotFound("Village", id);

    var awardIds = db.Awards.Where(a => a.VillageLinks.Any(link => link.VillageId == id) || a.KhasraLinks.Any(link => link.Khasra.VillageId == id)).Select(a => a.Id);
    var awards = await db.Awards.AsNoTracking().Where(a => awardIds.Contains(a.Id)).OrderByDescending(a => a.AwardDate).ThenBy(a => a.AwardNumber)
        .Select(a => new VillageOfficialAwardItem(a.Id, a.AwardNumber, a.AwardDate, a.AwardType, a.Status, a.KhasraLinks.Count(k => k.Khasra.VillageId == id), a.DocumentRelationships.Count)).ToListAsync(ct);
    var notifications = await db.Notifications.AsNoTracking().Where(n => n.KhasraLinks.Any(link => link.Khasra.VillageId == id) || db.AwardNotifications.Any(link => awardIds.Contains(link.AwardId) && link.NotificationId == n.Id)).OrderByDescending(n => n.NotificationDate)
        .Select(n => new VillageOfficialNotificationItem(n.Id, n.NotificationNumber, n.SectionType, n.NotificationDate)).ToListAsync(ct);
    var awardIdList = awards.Select(a => a.Id).ToList();
    var official = new VillageOfficialSummary(village.TotalKhasras, awards.Count, notifications.Count,
        await db.PossessionEvents.CountAsync(p => awardIdList.Contains(p.AwardId), ct),
        await db.Set<CourtCaseAward>().CountAsync(link => awardIdList.Contains(link.AwardId), ct),
        await db.Set<AwardValuationRule>().CountAsync(rule => awardIdList.Contains(rule.AwardId), ct),
        await db.Set<AwardCompensationRule>().CountAsync(rule => awardIdList.Contains(rule.AwardId), ct),
        await db.Claims.CountAsync(claim => awardIdList.Contains(claim.AwardId), ct));

    var pendingStatuses = new[] { AwardIngestionCandidateStatus.New, AwardIngestionCandidateStatus.NeedsReview, AwardIngestionCandidateStatus.Conflict, AwardIngestionCandidateStatus.Ambiguous, AwardIngestionCandidateStatus.Invalid, AwardIngestionCandidateStatus.DuplicateInBatch, AwardIngestionCandidateStatus.Ready };
    var pendingSessionRows = await db.AwardIngestionSessions.AsNoTracking()
        .Where(s => s.SelectedVillageId == id && s.SourceDocumentId != null && s.Candidates.Any(c => pendingStatuses.Contains(c.Status)))
        .OrderByDescending(s => s.UpdatedAt)
        .Select(s => new { s.Id, s.TargetAwardId, s.SourceDocumentId, AwardNumber = s.TargetAward == null ? null : s.TargetAward.AwardNumber, SourceDocumentName = s.SourceDocument!.OriginalFileName, s.Status, PendingCandidateCount = s.Candidates.Count(c => pendingStatuses.Contains(c.Status)) })
        .ToListAsync(ct);
    // Re-analysis creates a new session for the same original document.  Present only
    // its latest unresolved review, never a stack of superseded OCR attempts.
    var currentPendingSessionRows = pendingSessionRows.GroupBy(s => new { s.TargetAwardId, s.SourceDocumentId }).Select(group => group.First()).ToList();
    var pendingSessionIds = currentPendingSessionRows.Select(s => s.Id).ToList();
    var pendingTypeRows = await db.AwardIngestionCandidates.AsNoTracking().Where(c => pendingSessionIds.Contains(c.SessionId) && pendingStatuses.Contains(c.Status))
        .GroupBy(c => new { c.SessionId, c.CandidateType }).Select(g => new { g.Key.SessionId, g.Key.CandidateType, Count = g.Count() }).ToListAsync(ct);
    var pendingSessions = currentPendingSessionRows.Select(s => new VillagePendingReviewItem(s.Id, s.TargetAwardId, s.AwardNumber, s.SourceDocumentName, s.Status.ToString(), s.PendingCandidateCount,
        pendingTypeRows.Where(c => c.SessionId == s.Id).OrderBy(c => c.CandidateType).Select(c => new PendingCandidateTypeCount(c.CandidateType.ToString(), c.Count)).ToList())).ToList();

    var associatedDocuments = await db.Documents.AsNoTracking()
        .Where(d => d.AwardLinks.Any(link => awardIdList.Contains(link.AwardId)) || d.VillageLinks.Any(link => link.VillageId == id) || d.VillageLRLinks.Any(link => link.VillageLR.VillageId == id) || d.KhatauniRecordLinks.Any(link => link.KhatauniRecord.VillageId == id))
        .Select(d => new { d.OriginalFileName, d.DocumentType }).ToListAsync(ct);
    bool HasDocument(string keyword) => associatedDocuments.Any(d => d.DocumentType.Contains(keyword, StringComparison.OrdinalIgnoreCase) || d.OriginalFileName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    var sources = new[]
    {
        new VillageSourceStatusItem("Award PDF", awards.Sum(a => a.DocumentCount) > 0 ? "Loaded" : "Source not loaded", awards.Sum(a => a.DocumentCount) > 0 ? "Award document is stored locally and linked." : "No Award PDF is linked to this village's Awards."),
        new VillageSourceStatusItem("NM", HasDocument("NM") ? "Loaded" : "Source not loaded", HasDocument("NM") ? "A locally stored NM document is linked." : "No NM document is linked locally."),
        new VillageSourceStatusItem("Statement A", HasDocument("Statement A") ? "Loaded" : "Source not loaded", HasDocument("Statement A") ? "A locally stored Statement A document is linked." : "No Statement A document is linked locally."),
        new VillageSourceStatusItem("Possession proceedings", HasDocument("Possession") ? "Loaded" : "Source not loaded", HasDocument("Possession") ? "A locally stored possession document is linked." : "No possession-proceedings document is linked locally."),
        new VillageSourceStatusItem("Court orders", HasDocument("Court") || HasDocument("CWP") ? "Loaded" : "Source not loaded", HasDocument("Court") || HasDocument("CWP") ? "A locally stored court document is linked." : "No standalone court order is linked locally."),
        new VillageSourceStatusItem("LR / Khatauni", village.LrAvailable ? "Loaded" : "Source not loaded", village.LrAvailable ? "A local revenue record is available." : "No LR or Khatauni source is loaded.")
    };
    return Results.Ok(new VillageOverviewResponse(village, official, awards, notifications, pendingSessions, sources));
}).RequirePermission(PermissionCodes.VillageView);

api.MapGet("/villages/{id:guid}/khasras", async (Guid id, int page, int pageSize, string? q, LacDbContext db, OwnershipService ownership, CancellationToken ct) =>
{
    if (!await db.Villages.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village", id);
    var khasras = db.Khasras.AsNoTracking().Where(x => x.VillageId == id);
    if (!string.IsNullOrWhiteSpace(q))
    {
        var term = KhasraNumber.Normalize(q);
        khasras = khasras.Where(x => x.NormalizedNumber.Contains(term) || x.DisplayNumber.ToUpper().Contains(q.Trim().ToUpperInvariant()));
    }
    // Rectangle numbers are stored as text to preserve the official source value. Sort numeric-looking
    // values by digit length then text so 1, 2, …, 10 are ordered naturally before pagination.
    var result = await ToPageAsync(khasras
        .OrderBy(x => x.RectangleNumber == null)
        .ThenBy(x => x.RectangleNumber == null ? 0 : x.RectangleNumber.Length)
        .ThenBy(x => x.RectangleNumber)
        .ThenBy(x => x.DisplayNumber.Length)
        .ThenBy(x => x.DisplayNumber)
        .Select(KhasraListBaseItem.Selector), page, pageSize, ct);
    var ownershipByKhasra = await ownership.GetRecordedOwnershipBatchAsync(result.Items.Select(x => x.Id), ct);
    var items = new List<KhasraListItem>();
    foreach (var item in result.Items)
    {
        var recorded = ownershipByKhasra[item.Id];
        items.Add(new(item.Id, item.RectangleNumber, item.DisplayNumber, item.AreaBigha, item.AreaBiswa, item.AreaBiswansi, recorded.Found ? string.Join(", ", recorded.Owners.Select(x => x.DisplayName)) : recorded.IsAmbiguous ? "Ambiguous — review ownership" : "—", item.AcquisitionStatus, item.Awards));
    }
    return Results.Ok(new PageResponse<KhasraListItem>(items, result.Page, result.PageSize, result.TotalCount));
}).RequirePermission(PermissionCodes.KhasraView);

api.MapGet("/villages/{id:guid}/awards", async (Guid id, int page, int pageSize, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Villages.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village", id);
    return Results.Ok(await ToPageAsync(db.Awards.AsNoTracking().Where(a => a.KhasraLinks.Any(link => link.Khasra.VillageId == id)).OrderByDescending(x => x.AwardDate).Select(AwardListItem.Selector), page, pageSize, ct));
}).RequirePermission(PermissionCodes.AwardView);

api.MapGet("/villages/{id:guid}/notifications", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Villages.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village", id);
    return Results.Ok(await db.Notifications.AsNoTracking().Where(n => n.KhasraLinks.Any(link => link.Khasra.VillageId == id)).OrderByDescending(n => n.NotificationDate).Select(NotificationListItem.Selector).ToListAsync(ct));
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);

api.MapGet("/villages/{id:guid}/lrs", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Villages.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village", id);
    return Results.Ok(await db.VillageLRs.AsNoTracking().Where(lr => lr.VillageId == id).OrderBy(lr => lr.RegisterReference).Select(lr => new VillageLrListItem(lr.Id, lr.RegisterReference, lr.Entries.Count)).ToListAsync(ct));
}).RequirePermission(PermissionCodes.LrView, WorkstreamCodes.LandRecords);

api.MapGet("/villages/{id:guid}/documents", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Villages.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village", id);
    return Results.Ok(await db.DocumentVillages.AsNoTracking()
        .Where(link => link.VillageId == id && link.Document.RecordStatus == RecordStatus.Active && link.Document.Status == "Active" && (link.Document.AwardLinks.Any() || db.NmDocuments.Any(nm => nm.DocumentId == link.DocumentId) || db.DocumentNotifications.Any(dn => dn.DocumentId == link.DocumentId)))
        .OrderByDescending(link => link.Document.UploadedAt)
        .Select(link => new DocumentListItem(link.Document.Id, link.Document.OriginalFileName, link.Document.DocumentType, link.Document.UploadedAt, link.Document.Status)).ToListAsync(ct));
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);

api.MapGet("/khasras/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var khasra = await db.Khasras.AsNoTracking().Where(x => x.Id == id).Select(x => new KhasraDetail(x.Id, x.DisplayNumber, x.NormalizedNumber, x.RectangleNumber, x.KillaNumber, x.SubdivisionNumber, x.TotalArea, x.AreaUnit, x.AreaBigha, x.AreaBiswa, x.AreaBiswansi, x.Remarks,
        new VillageReference(x.Village.Id, x.Village.Name, new SubDivisionReference(x.Village.SubDivision.Id, x.Village.SubDivision.Name, new DistrictReference(x.Village.SubDivision.District.Id, x.Village.SubDivision.District.Name))),
        x.NotificationLinks.OrderBy(n => n.Notification.NotificationDate).Select(n => new NotificationLinkItem(n.Notification.Id, n.Notification.NotificationNumber, n.Notification.SectionType, n.Notification.NotificationDate, n.NotifiedArea, n.AreaUnit)).ToList(),
        x.AwardLinks.OrderBy(a => a.Award.AwardNumber).Select(a => new AwardLinkItem(a.Award.Id, a.Award.AwardNumber, a.AcquiredArea, a.AreaUnit, a.AcquisitionStatus)).ToList(),
        db.LREntries.Where(lr => lr.KhasraId == x.Id).OrderByDescending(lr => lr.UpdatedAt).Select(lr => new LrEntryItem(lr.Id, lr.VillageLRId, lr.RawKhasraText, lr.RawAreaText, lr.RawRemarks, lr.VerificationStatus.ToString())).ToList())).FirstOrDefaultAsync(ct);
    return khasra is null ? NotFound("Khasra", id) : Results.Ok(khasra);
}).RequirePermission(PermissionCodes.KhasraView);

api.MapGet("/khasras/{id:guid}/history", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Khasras.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Khasra", id);
    var awards = await db.Set<AwardKhasra>().AsNoTracking().Where(link => link.KhasraId == id).OrderBy(link => link.Award.AwardNumber)
        .Select(link => new KhasraOfficialAwardHistoryItem(link.AwardId, link.Award.AwardNumber, link.Award.AwardDate, link.RecordedTotalAreaBigha, link.RecordedTotalAreaBiswa, link.RecordedTotalAreaBiswansi, link.AwardedAreaBigha, link.AwardedAreaBiswa, link.AwardedAreaBiswansi, link.AcquisitionStatus)).ToListAsync(ct);
    var possession = await db.Set<PossessionKhasra>().AsNoTracking().Where(link => link.KhasraId == id).OrderByDescending(link => link.PossessionEvent.PossessionDate)
        .Select(link => new KhasraOfficialPossessionItem(link.PossessionEventId, link.PossessionEvent.AwardId, link.PossessionEvent.PossessionDate, link.PossessionEvent.EventType, link.PossessionEvent.Status)).ToListAsync(ct);
    var court = await db.Set<CourtCaseKhasra>().AsNoTracking().Where(link => link.KhasraId == id).OrderBy(link => link.CourtCase.CaseNumber)
        .Select(link => new KhasraOfficialCourtItem(link.CourtCaseId, link.CourtCase.CaseNumber, link.CourtCase.CourtName, link.CourtCase.CurrentStatus)).ToListAsync(ct);
    var linkedAwardIds = awards.Select(a => a.AwardId).ToList();
    var pendingRows = await db.AwardIngestionSessions.AsNoTracking()
        .Where(s => linkedAwardIds.Contains(s.TargetAwardId ?? Guid.Empty) && s.SourceDocumentId != null && s.Candidates.Any(c => c.Status == AwardIngestionCandidateStatus.NeedsReview || c.Status == AwardIngestionCandidateStatus.Conflict || c.Status == AwardIngestionCandidateStatus.Ambiguous || c.Status == AwardIngestionCandidateStatus.Invalid))
        .OrderByDescending(s => s.UpdatedAt)
        .Select(s => new { s.Id, s.TargetAwardId, s.SourceDocumentId, AwardNumber = s.TargetAward == null ? null : s.TargetAward.AwardNumber, SourceDocumentName = s.SourceDocument!.OriginalFileName, s.Status, PendingCandidateCount = s.Candidates.Count(c => c.Status == AwardIngestionCandidateStatus.NeedsReview || c.Status == AwardIngestionCandidateStatus.Conflict || c.Status == AwardIngestionCandidateStatus.Ambiguous || c.Status == AwardIngestionCandidateStatus.Invalid) }).ToListAsync(ct);
    var pending = pendingRows.GroupBy(s => new { s.TargetAwardId, s.SourceDocumentId }).Select(group => group.First())
        .Select(s => new KhasraPendingDocumentReviewItem(s.Id, s.TargetAwardId, s.AwardNumber, s.SourceDocumentName, s.Status.ToString(), s.PendingCandidateCount)).ToList();
    return Results.Ok(new KhasraHistoryResponse(awards, possession, court, pending));
}).RequirePermission(PermissionCodes.KhasraView);

api.MapGet("/awards", async (int page, int pageSize, string? q, LacDbContext db, CancellationToken ct) =>
{
    var awards = db.Awards.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim().ToUpperInvariant(); awards = awards.Where(x => x.AwardNumber.ToUpper().Contains(term)); }
    return Results.Ok(await ToPageAsync(awards.OrderByDescending(x => x.AwardDate).ThenBy(x => x.AwardNumber).Select(AwardListItem.Selector), page, pageSize, ct));
}).RequirePermission(PermissionCodes.AwardView);

api.MapGet("/awards/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var award = await db.Awards.AsNoTracking().Where(x => x.Id == id).Select(x => new AwardDetail(x.Id, x.AwardNumber, x.AwardDate, x.AwardType, x.Status, x.ActRegime, x.Remarks,
        x.AcquisitionProject == null ? null : new ProjectReference(x.AcquisitionProject.Id, x.AcquisitionProject.Name, x.AcquisitionProject.RequiringAgency, x.AcquisitionProject.ActRegime),
        x.KhasraLinks.Count, x.KhasraLinks.Sum(link => link.AcquiredArea),
        x.KhasraLinks.OrderBy(link => link.Khasra.DisplayNumber).Select(link => new AwardKhasraItem(link.Khasra.Id, link.Khasra.DisplayNumber, link.Khasra.Village.Name, link.AcquiredArea, link.AreaUnit, link.AcquisitionStatus)).ToList(),
        x.KhasraLinks.SelectMany(link => link.Khasra.NotificationLinks).Select(link => new NotificationLinkItem(link.Notification.Id, link.Notification.NotificationNumber, link.Notification.SectionType, link.Notification.NotificationDate, link.NotifiedArea, link.AreaUnit)).Distinct().ToList(),
        x.DocumentRelationships.OrderByDescending(link => link.Document.UploadedAt).Select(link => new DocumentListItem(link.Document.Id, link.Document.OriginalFileName, link.Document.DocumentType, link.Document.UploadedAt, link.Document.Status)).ToList())).FirstOrDefaultAsync(ct);
    return award is null ? NotFound("Award", id) : Results.Ok(award);
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);

api.MapGet("/awards/{id:guid}/workspace", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var item = await db.Awards.AsNoTracking().Where(x => x.Id == id).Select(x => new AwardWorkspaceOverview(
        x.Id, x.AwardNumber, x.AwardDate, x.AwardType, x.ParentAwardId, x.ParentAward == null ? null : x.ParentAward.AwardNumber, x.ParentAwardReference, x.Purpose, x.ActRegime, x.Status, x.Remarks,
        x.AcquisitionProject == null ? null : new ProjectReference(x.AcquisitionProject.Id, x.AcquisitionProject.Name, x.AcquisitionProject.RequiringAgency, x.AcquisitionProject.ActRegime),
        x.VillageLinks.Select(v => new VillageReference(v.Village.Id, v.Village.Name, new SubDivisionReference(v.Village.SubDivision.Id, v.Village.SubDivision.Name, new DistrictReference(v.Village.SubDivision.District.Id, v.Village.SubDivision.District.Name)))).ToList(),
        x.KhasraLinks.Count, x.NotificationLinks.Count, db.PossessionEvents.Count(p => p.AwardId == x.Id), db.Set<CourtCaseAward>().Count(c => c.AwardId == x.Id), db.Claims.Count(c => c.AwardId == x.Id), db.Set<AwardAreaIssue>().Count(i => i.AwardId == x.Id && i.Status != "Resolved"), x.DocumentRelationships.Count,
        x.KhasraLinks.Any() ? "Available" : "Not Added", x.NotificationLinks.Any() ? "Available" : "Not Added", db.PossessionEvents.Any(p => p.AwardId == x.Id) ? "Partial" : "Not Added", db.Set<CourtCaseAward>().Any(c => c.AwardId == x.Id) ? "Available" : "Not Added", db.Claims.Any(c => c.AwardId == x.Id) ? "Available" : "Not Added"
    )).FirstOrDefaultAsync(ct);
    if (item is null) return NotFound("Award", id);
    return Results.Ok(item);
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);

// This deliberately exposes only the direct AwardVillage relationship.  The workspace
// overview may display Khasra-derived villages for navigation, but those are not valid
// targets for a transactional Award ingestion commit.
api.MapGet("/awards/{id:guid}/ingestion-villages", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Awards.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Award", id);
    return Results.Ok(await db.AwardVillages.AsNoTracking().Where(x => x.AwardId == id)
        .OrderBy(x => x.Village.Name).Select(x => new { x.VillageId, x.Village.Name }).ToListAsync(ct));
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);

api.MapGet("/notifications", async (int page, int pageSize, string? q, LacDbContext db, CancellationToken ct) =>
{
    var notifications = db.Notifications.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim().ToUpperInvariant(); notifications = notifications.Where(x => x.NotificationNumber.ToUpper().Contains(term)); }
    return Results.Ok(await ToPageAsync(notifications.OrderByDescending(x => x.NotificationDate).Select(NotificationListItem.Selector), page, pageSize, ct));
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);

api.MapGet("/notifications/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var notification = await db.Notifications.AsNoTracking().Where(x => x.Id == id).Select(x => new NotificationDetail(x.Id, x.SectionType, x.NotificationNumber, x.NotificationDate, x.GazetteDetails, x.Remarks,
        x.AcquisitionProject == null ? null : new ProjectReference(x.AcquisitionProject.Id, x.AcquisitionProject.Name, x.AcquisitionProject.RequiringAgency, x.AcquisitionProject.ActRegime),
        x.KhasraLinks.OrderBy(link => link.Khasra.DisplayNumber).Select(link => new NotificationKhasraItem(link.Khasra.Id, link.Khasra.DisplayNumber, link.Khasra.Village.Name, link.NotifiedArea, link.AreaUnit)).ToList(),
        x.DocumentRelationships.OrderByDescending(link => link.Document.UploadedAt).Select(link => new DocumentListItem(link.Document.Id, link.Document.OriginalFileName, link.Document.DocumentType, link.Document.UploadedAt, link.Document.Status)).ToList())).FirstOrDefaultAsync(ct);
    return notification is null ? NotFound("Notification", id) : Results.Ok(notification);
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);

api.MapGet("/documents", async (int page, int pageSize, LacDbContext db, CancellationToken ct) =>
{
    var query = db.Documents.AsNoTracking()
        .Where(x => x.RecordStatus == RecordStatus.Active && x.Status == "Active" && (x.AwardLinks.Any() || db.NmDocuments.Any(nm => nm.DocumentId == x.Id) || db.DocumentNotifications.Any(dn => dn.DocumentId == x.Id)))
        .OrderByDescending(x => x.UploadedAt)
        .Select(x => new DocumentListItem(x.Id, x.OriginalFileName, x.DocumentType, x.UploadedAt, x.Status));
    return Results.Ok(await ToPageAsync(query, page, pageSize, ct));
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapGet("/documents/{id:guid}/content", async (Guid id, LacDbContext db, IDocumentStorage storage, CancellationToken ct) =>
{
    var document = await db.Documents.AsNoTracking()
        .Where(x => x.Id == id && x.RecordStatus == RecordStatus.Active && x.Status == "Active" && (x.AwardLinks.Any() || db.NmDocuments.Any(nm => nm.DocumentId == x.Id) || db.DocumentNotifications.Any(dn => dn.DocumentId == x.Id)))
        .SingleOrDefaultAsync(ct);
    // The NM review workspace stores the NM-document identifier, whereas this
    // generic viewer is given a stored-document identifier elsewhere. Resolve
    // that stable NM-to-document relationship so the original source remains
    // viewable in the semantic review without duplicating a PDF or its storage.
    if (document is null)
    {
        var sourceDocumentId = await db.NmDocuments.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => (Guid?)x.DocumentId)
            .SingleOrDefaultAsync(ct);
        if (sourceDocumentId is not null)
            document = await db.Documents.AsNoTracking()
                .Where(x => x.Id == sourceDocumentId.Value && x.RecordStatus == RecordStatus.Active && x.Status == "Active" && (x.AwardLinks.Any() || db.NmDocuments.Any(nm => nm.DocumentId == x.Id) || db.DocumentNotifications.Any(dn => dn.DocumentId == x.Id)))
                .SingleOrDefaultAsync(ct);
    }
    if (document is null) return Results.NotFound();
    var stream = await storage.OpenReadAsync(document.StoragePath, ct);
    return stream is null ? Results.NotFound() : Results.File(stream, document.MimeType ?? "application/octet-stream", enableRangeProcessing: true);
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapGet("/villages/{id:guid}/core-records", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Villages.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village", id);
    var awards = await db.Awards.AsNoTracking().Where(a => a.VillageLinks.Any(v => v.VillageId == id)).OrderByDescending(a => a.AwardDate).ThenBy(a => a.AwardNumber)
        .Select(a => new { a.Id, a.AwardNumber, a.AwardDate, a.AwardType, documents = a.DocumentRelationships.Select(d => new { d.DocumentId, d.CoreDocumentRole, d.Document.OriginalFileName, d.Document.UploadedAt }).ToList() }).ToListAsync(ct);
    return Results.Ok(awards.Select(a => new { a.Id, a.AwardNumber, a.AwardDate, a.AwardType, roles = new[] { "Award", "NM", "StatementA", "PossessionProceeding" }.Select(role => new { role, count = a.documents.Count(d => d.CoreDocumentRole == role), available = a.documents.Any(d => d.CoreDocumentRole == role) }), documents = a.documents }));
}).RequirePermission(PermissionCodes.VillageView);
api.MapPost("/villages/{id:guid}/awards", async (Guid id, CreateVillageAwardRequest request, LacDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.AwardNumber)) return Validation("awardNumber", "Award number is required.");
    if (!await db.Villages.AnyAsync(v => v.Id == id, ct)) return NotFound("Village", id);
    var award = new Award { AwardNumber = request.AwardNumber.Trim(), AwardDate = request.AwardDate, AwardType = request.AwardType?.Trim(), Status = "Draft", Remarks = request.Remarks?.Trim() };
    db.Add(award); db.Add(new AwardVillage { Award = award, VillageId = id }); await db.SaveChangesAsync(ct);
    return Results.Created($"/api/awards/{award.Id}", new IdResponse(award.Id));
}).RequirePermission(PermissionCodes.AwardCreate);
api.MapGet("/awards/{id:guid}/core-documents", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Awards.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Award", id);
    return Results.Ok(await db.DocumentAwards.AsNoTracking().Where(x => x.AwardId == id && x.CoreDocumentRole != null).OrderByDescending(x => x.Document.UploadedAt)
        .Select(x => new { x.DocumentId, role = x.CoreDocumentRole, x.Document.OriginalFileName, x.Document.MimeType, x.Document.UploadedAt }).ToListAsync(ct));
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapPost("/awards/{id:guid}/core-documents", async (Guid id, string role, IFormFile file, LacDbContext db, IDocumentStorage storage, CancellationToken ct) =>
{
    var allowed = new[] { "Award", "NM", "StatementA", "PossessionProceeding" };
    if (!allowed.Contains(role, StringComparer.Ordinal)) return Validation("role", "Choose Award, NM, StatementA, or PossessionProceeding.");
    if (file.Length == 0) return Validation("file", "Choose a non-empty document.");
    var villageIds = await db.AwardVillages.Where(x => x.AwardId == id).Select(x => x.VillageId).Distinct().ToListAsync(ct);
    if (villageIds.Count == 0) return NotFound("Award", id);
    await using var source = file.OpenReadStream(); var stored = await storage.SaveAndHashAsync(source, file.FileName, ct);
    var document = new Document { DocumentType = role, OriginalFileName = file.FileName, StoragePath = stored.StoragePath, Sha256Hash = stored.Sha256Hash, FileSize = stored.FileSize, MimeType = file.ContentType, UploadedAt = DateTimeOffset.UtcNow };
    db.Add(document); db.Add(new DocumentAward { AwardId = id, Document = document, CoreDocumentRole = role });
    foreach (var villageId in villageIds) db.Add(new DocumentVillage { VillageId = villageId, Document = document });
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/documents/{document.Id}", new { documentId = document.Id, role });
}).DisableAntiforgery().RequirePermission(PermissionCodes.AwardCoreDocumentUpload, WorkstreamCodes.Award);
api.MapGet("/villages/{id:guid}/matters", async (
    Guid id,
    LacDbContext db,
    IMatterAuthorizationService matterAuth,
    ICurrentUserContext currentUser,
    CancellationToken ct) =>
{
    if (!currentUser.UserId.HasValue) return Results.Unauthorized();
    var userId = currentUser.UserId.Value;

    if (!await db.Villages.AsNoTracking().AnyAsync(v => v.Id == id, ct))
        return NotFound("Village", id);

    var baseQuery = db.Matters.AsNoTracking().Where(x => x.VillageId == id && x.RecordStatus == RecordStatus.Active);
    var auth = await matterAuth.AuthorizeListQueryAsync(baseQuery, PermissionCodes.MatterView, userId, false, ct);
    if (!auth.HasPermission)
        return Results.Forbid();

    var query = auth.Query;

    var items = await query.OrderByDescending(x => x.CreatedAt).Select(x => new
    {
        x.Id,
        x.VillageId,
        villageName = x.Village.Name,
        x.WorkstreamId,
        workstreamName = x.Workstream != null ? x.Workstream.Name : null,
        workstreamCode = x.Workstream != null ? x.Workstream.Code : null,
        isUnclassified = x.WorkstreamId == null,
        x.Title,
        x.MatterType,
        x.Status,
        x.ReferenceNumber,
        x.Remarks,
        x.KhasraReferenceText,
        x.Revision,
        award = x.AwardLinks.Where(a => a.IsPrimary).Select(a => new { a.AwardId, a.Award.AwardNumber }).FirstOrDefault()
    }).ToListAsync(ct);

    return Results.Ok(items);
});
api.MapPost("/villages/{id:guid}/matters", async (
    Guid id,
    CreateMatterRequest request,
    LacDbContext db,
    MatterWorkflowService workflow,
    ICurrentUserContext currentUser,
    CancellationToken ct) =>
{
    if (!currentUser.UserId.HasValue) return Results.Unauthorized();
    var userId = currentUser.UserId.Value;

    if (string.IsNullOrWhiteSpace(request.Title)) return Validation("title", "Matter title is required.");
    if (!await db.Villages.AnyAsync(v => v.Id == id, ct)) return NotFound("Village", id);

    if (!request.WorkstreamId.HasValue || request.WorkstreamId.Value == Guid.Empty)
        return Validation("workstreamId", "Workstream is mandatory for new matters.");

    var cmd = new CreateMatterCommand(
        VillageId: id,
        Title: request.Title,
        MatterType: !string.IsNullOrWhiteSpace(request.MatterType) ? request.MatterType.Trim() : "Other",
        WorkstreamId: request.WorkstreamId.Value,
        ReferenceNumber: request.ReferenceNumber,
        Remarks: request.Remarks,
        KhasraReferenceText: request.KhasraReferenceText,
        AwardId: request.AwardId
    );

    try
    {
        var matter = await workflow.CreateMatterAsync(cmd, userId, ct);
        return Results.Created($"/api/matters/{matter.Id}", new IdResponse(matter.Id));
    }
    catch (MatterWorkflowException ex)
    {
        return Results.Json(new { message = ex.Message }, statusCode: ex.StatusCode);
    }
});
api.MapGet("/documents/{id:guid}/page-image", async (Guid id, int page, LacDbContext db, DocumentPageImageService renderer, CancellationToken ct) =>
{
    var document = await db.Documents.AsNoTracking()
        .Where(x => x.Id == id && x.RecordStatus == RecordStatus.Active && x.Status == "Active" && (x.AwardLinks.Any() || db.NmDocuments.Any(nm => nm.DocumentId == x.Id) || db.DocumentNotifications.Any(dn => dn.DocumentId == x.Id)))
        .SingleOrDefaultAsync(ct);
    if (document is null)
    {
        var sourceDocumentId = await db.NmDocuments.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.DocumentId).SingleOrDefaultAsync(ct);
        if (sourceDocumentId is not null)
            document = await db.Documents.AsNoTracking()
                .Where(x => x.Id == sourceDocumentId.Value && x.RecordStatus == RecordStatus.Active && x.Status == "Active" && (x.AwardLinks.Any() || db.NmDocuments.Any(nm => nm.DocumentId == x.Id) || db.DocumentNotifications.Any(dn => dn.DocumentId == x.Id)))
                .SingleOrDefaultAsync(ct);
    }
    if (document is null) return Results.NotFound();
    try { return Results.File(await renderer.RenderAsync(document.StoragePath, page, rotateClockwise: true, ct), "image/png"); }
    catch (AwardIngestionException ex) { return IngestionProblem(ex); }
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);

api.MapGet("/search", async (string? q, LacDbContext db, CancellationToken ct) =>
{
    var input = q?.Trim() ?? string.Empty;
    if (input.Length < 2) return Results.Ok(Array.Empty<SearchResultItem>());
    var normalizedKhasra = KhasraNumber.Normalize(input);
    var term = input.ToUpperInvariant();
    var villages = await db.Villages.AsNoTracking().Where(x => x.Name.ToUpper().Contains(term)).Take(8).Select(x => new SearchResultItem("Village", x.Id, x.Name, x.SubDivision.Name, $"/villages/{x.Id}")).ToListAsync(ct);
    var khasras = await db.Khasras.AsNoTracking().Where(x => x.NormalizedNumber.Contains(normalizedKhasra) || x.DisplayNumber.ToUpper().Contains(term)).Take(12).Select(x => new SearchResultItem("Khasra", x.Id, x.DisplayNumber, x.Village.Name, $"/khasras/{x.Id}")).ToListAsync(ct);
    var awards = await db.Awards.AsNoTracking().Where(x => x.AwardNumber.ToUpper().Contains(term)).Take(8).Select(x => new SearchResultItem("Award", x.Id, x.AwardNumber, x.AcquisitionProject == null ? null : x.AcquisitionProject.Name, $"/awards/{x.Id}")).ToListAsync(ct);
    var parties = await db.Parties.AsNoTracking().Where(x => x.DisplayName.ToUpper().Contains(term)).Take(12).Select(x => new SearchResultItem("Recorded Party", x.Id, x.DisplayName, x.PartyType.ToString() + " · " + (x.KhataShares.Select(s => s.Khata.KhatauniRecord.Village.Name + " / " + s.Khata.KhataNumber).FirstOrDefault() ?? "No recorded holding"), $"/parties/{x.Id}")).ToListAsync(ct);
    return Results.Ok(villages.Concat(khasras).Concat(awards).Concat(parties));
}).RequirePermission(PermissionCodes.VillageView);

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
}).RequirePermission(PermissionCodes.LrView, WorkstreamCodes.LandRecords);

api.MapGet("/village-lrs/{id:guid}/entries", async (Guid id, int page, int pageSize, string? status, string? q, Guid? khasraId, Guid? awardId, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.VillageLRs.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound("Village LR register", id);
    var rows = db.LREntries.AsNoTracking().Where(x => x.VillageLRId == id);
    if (Enum.TryParse<VerificationStatus>(status, true, out var parsedStatus)) rows = rows.Where(x => x.VerificationStatus == parsedStatus);
    if (khasraId is not null) rows = rows.Where(x => x.KhasraId == khasraId);
    if (awardId is not null) rows = rows.Where(x => x.AwardId == awardId);
    if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim().ToUpperInvariant(); rows = rows.Where(x => x.RawKhasraText.ToUpper().Contains(term)); }
    return Results.Ok(await ToPageAsync(rows.OrderBy(x => x.RowNumber).ThenBy(x => x.CreatedAt).Select(LrEntryDetailItem.Selector), page, pageSize, ct));
}).RequirePermission(PermissionCodes.LrView, WorkstreamCodes.LandRecords);

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
}).RequirePermission(PermissionCodes.LrView, WorkstreamCodes.LandRecords);

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
}).RequirePermission(PermissionCodes.LrView, WorkstreamCodes.LandRecords);

api.MapPost("/village-lrs", async (CreateVillageLrRequest request, LacDbContext db, CancellationToken ct) =>
{
    if (request.VillageId == Guid.Empty) return Validation("villageId", "A village must be selected.");
    if (!await db.Villages.AnyAsync(x => x.Id == request.VillageId, ct)) return NotFound("Village", request.VillageId);
    var register = new VillageLR { VillageId = request.VillageId, RegisterReference = request.RegisterReference?.Trim(), Remarks = request.Remarks?.Trim() };
    db.VillageLRs.Add(register); await db.SaveChangesAsync(ct);
    return Results.Created($"/api/village-lrs/{register.Id}", new IdResponse(register.Id));
}).RequirePermission(PermissionCodes.LrEdit, WorkstreamCodes.LandRecords);

api.MapPost("/villages/{id:guid}/khasras", async (Guid id, CreateKhasraRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try { var khasra = await workflow.CreateKhasraAsync(id, request.DisplayNumber, request.TotalArea, request.AreaUnit, request.RectangleNumber, request.KillaNumber, request.SubdivisionNumber, ct); return Results.Created($"/api/khasras/{khasra.Id}", new IdResponse(khasra.Id)); }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
}).RequirePermission(PermissionCodes.KhasraEdit, WorkstreamCodes.LandRecords);

api.MapPost("/villages/{id:guid}/khasras/batch", async (Guid id, KhasraBatchRequest request, KhasraWorkspaceService workspace, CancellationToken ct) =>
{
    try { return Results.Ok(await workspace.ImportAsync(id, request.Rows, ct)); }
    catch (KhasraWorkspaceException ex) { return KhasraProblem(ex); }
}).RequirePermission(PermissionCodes.KhasraEdit, WorkstreamCodes.LandRecords);
api.MapPost("/villages/{id:guid}/khasras/import-preview", async (Guid id, IFormFile file, KhasraWorkspaceService workspace, CancellationToken ct) =>
{
    try { if (file.Length == 0) return Validation("file", "Select a non-empty .xlsx workbook."); await using var stream = file.OpenReadStream(); return Results.Ok(await workspace.PreviewAsync(id, stream, ct)); }
    catch (KhasraWorkspaceException ex) { return KhasraProblem(ex); }
    catch (Exception) { return Validation("file", "The workbook could not be read. Use a valid .xlsx file with the expected headers."); }
}).DisableAntiforgery().RequirePermission(PermissionCodes.KhasraEdit, WorkstreamCodes.LandRecords);
api.MapPost("/villages/{id:guid}/khasras/import", async (Guid id, KhasraBatchRequest request, KhasraWorkspaceService workspace, CancellationToken ct) =>
{
    try { return Results.Ok(await workspace.ImportAsync(id, request.Rows, ct)); }
    catch (KhasraWorkspaceException ex) { return KhasraProblem(ex); }
}).RequirePermission(PermissionCodes.KhasraEdit, WorkstreamCodes.LandRecords);
api.MapGet("/villages/{id:guid}/khasras/import-template", (Guid id) => Results.File(KhasraWorkspaceService.ImportTemplate(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "village-khasra-import-template.xlsx")).RequirePermission(PermissionCodes.KhasraView);
api.MapPut("/khasras/{id:guid}", async (Guid id, KhasraWorkspaceRow request, KhasraWorkspaceService workspace, CancellationToken ct) =>
{
    try { await workspace.UpdateAsync(id, request, ct); return Results.NoContent(); }
    catch (KhasraWorkspaceException ex) { return KhasraProblem(ex); }
}).RequirePermission(PermissionCodes.KhasraEdit, WorkstreamCodes.LandRecords);
api.MapDelete("/khasras/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var khasra = await db.Khasras.SingleOrDefaultAsync(x => x.Id == id, ct); if (khasra is null) return NotFound("Khasra", id); db.Remove(khasra); await db.SaveChangesAsync(ct); return Results.NoContent();
}).RequirePermission(PermissionCodes.KhasraEdit, WorkstreamCodes.LandRecords);
api.MapGet("/villages/{id:guid}/khasras/export/{format}", async (Guid id, string format, string? q, KhasraWorkspaceService workspace, CancellationToken ct) =>
{
    try
    {
        var rows = await workspace.ExportRowsAsync(id, q, ct); var name = "village-khasras";
        return format.ToLowerInvariant() switch { "xlsx" => Results.File(KhasraWorkspaceService.Excel(rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{name}.xlsx"), "csv" => Results.File(KhasraWorkspaceService.Csv(rows), "text/csv", $"{name}.csv"), "pdf" => Results.File(KhasraWorkspaceService.Pdf(rows), "application/pdf", $"{name}.pdf"), "docx" => Results.File(KhasraWorkspaceService.Docx(rows), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{name}.docx"), _ => Validation("format", "Choose xlsx, csv, pdf, or docx.") };
    }
    catch (KhasraWorkspaceException ex) { return KhasraProblem(ex); }
}).RequirePermission(PermissionCodes.KhasraView);

api.MapPost("/notifications", async (CreateNotificationRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try { var notification = await workflow.CreateNotificationAsync(request.SectionType, request.NotificationNumber, request.NotificationDate, request.Remarks, ct); return Results.Created($"/api/notifications/{notification.Id}", new IdResponse(notification.Id)); }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
}).RequirePermission(PermissionCodes.AwardCreate, WorkstreamCodes.Award);

api.MapPost("/awards", async (CreateAwardRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try { var award = await workflow.CreateAwardAsync(request.AwardNumber, request.AwardDate, request.AwardType, request.ActRegime, ct); return Results.Created($"/api/awards/{award.Id}", new IdResponse(award.Id)); }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
}).RequirePermission(PermissionCodes.AwardCreate);

api.MapPost("/awards/foundation", async (AwardFoundationCreateRequest request, AwardWorkflowService workflow, CancellationToken ct) =>
{
    try { var award = await workflow.CreateAsync(new(request.AwardNumber, request.VillageId, request.AwardDate, request.AwardType, request.ActRegime, request.Purpose, request.AcquisitionProjectId, request.Remarks), ct); return Results.Created($"/api/awards/{award.Id}", new IdResponse(award.Id)); }
    catch (AwardWorkflowException ex) { return AwardWorkflowProblem(ex); }
}).RequirePermission(PermissionCodes.AwardCreate, WorkstreamCodes.Award);
api.MapPost("/awards/supplementary", async (CreateSupplementaryAwardRequest request, AwardWorkflowService workflow, CancellationToken ct) =>
{
    try { var award = await workflow.CreateSupplementaryAsync(new(request.AwardNumber, request.AwardDate, request.ParentAwardReference, request.ParentAwardId, request.Remarks), ct); return Results.Created($"/api/awards/{award.Id}", new IdResponse(award.Id)); }
    catch (AwardWorkflowException ex) { return AwardWorkflowProblem(ex); }
}).RequirePermission(PermissionCodes.AwardCreate, WorkstreamCodes.Award);

api.MapPost("/award-ingestion-sessions", async (CreateAwardIngestionSessionRequest request, AwardIngestionService ingestion, CancellationToken ct) =>
{
    try { var session = await ingestion.CreatePreviewFromJsonAsync(request.SourceType, request.TargetAwardId, request.SelectedVillageId, request.SourceDocumentId, request.CreatedBy, request.Remarks, request.Candidates, ct); return Results.Created($"/api/award-ingestion-sessions/{session.Id}", new IdResponse(session.Id)); }
    catch (AwardIngestionException ex) { return IngestionProblem(ex); }
}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapPost("/nm-documents", async (CreateNmDocumentRequest request, NmWorkflowService workflow, CancellationToken ct) => { try { var item=await workflow.CreateReviewAsync(request.DocumentId,request.VillageId,request.AwardId,request.ReferenceNumber,request.RecordDate,ct); return Results.Created($"/api/nm-documents/{item.Id}",new IdResponse(item.Id)); } catch(NmWorkflowException ex){ return Results.Problem(ex.Message,statusCode:ex.StatusCode); } }).RequirePermission(PermissionCodes.AwardCoreDocumentUpload, WorkstreamCodes.Award);
api.MapPost("/awards/{awardId:guid}/nm-documents", async(Guid awardId,Guid? villageId,IFormFile file,LacDbContext db,NmWorkflowService workflow,CancellationToken ct)=>{ if(file.Length==0)return Validation("file","Choose a non-empty NM PDF."); var resolved=villageId??await db.AwardVillages.Where(x=>x.AwardId==awardId).Select(x=>(Guid?)x.VillageId).SingleOrDefaultAsync(ct); if(resolved is null)return Validation("villageId","This Award needs one selected Village before NM upload."); try{await using var stream=file.OpenReadStream();var nm=await workflow.UploadAsync(stream,file.FileName,file.ContentType,awardId,resolved.Value,ct);return Results.Created($"/api/nm-documents/{nm.Id}",new {nmDocumentId=nm.Id,documentId=nm.DocumentId,status="Uploaded"});}catch(NmWorkflowException ex){return Results.Problem(ex.Message,statusCode:ex.StatusCode);}}).DisableAntiforgery().RequirePermission(PermissionCodes.AwardCoreDocumentUpload, WorkstreamCodes.Award);
api.MapGet("/awards/{awardId:guid}/nm-documents", async(Guid awardId,LacDbContext db,CancellationToken ct)=>Results.Ok(await db.NmDocuments.AsNoTracking().Where(x=>x.AwardId==awardId).OrderByDescending(x=>x.CreatedAt).Select(x=>new{x.Id,x.DocumentId,x.Document.OriginalFileName,x.VillageId,VillageName=x.Village.Name,x.Status,Rows=x.ReviewRows.Count}).ToListAsync(ct))).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapPost("/nm-documents/{id:guid}/analyze-pilot", async(Guid id,NmWorkflowService workflow,CancellationToken ct)=> { try { return Results.Ok(new { rowsCreated = await workflow.AnalyzePilotAsync(id,ct) }); } catch(NmWorkflowException ex) { return Results.Problem(ex.Message,statusCode:ex.StatusCode); } }).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapPost("/nm-documents/{id:guid}/analyze-semantic", async(Guid id,HttpRequest request,NmWorkflowService workflow,CancellationToken ct)=> { try { var rawPages=request.Query["pages"].SelectMany(value=>(value ?? "").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)).ToArray(); if(rawPages.Any(value=>!int.TryParse(value,out _))) return Results.Problem("Semantic analysis pages must be integers.",statusCode:400); var pages=rawPages.Length==0?null:rawPages.Select(int.Parse).ToArray(); var session = await workflow.AnalyzeSemanticAsync(id,ct,pages); return Results.Ok(new { session.Id, session.Status, session.AutoStructuredCount, session.ExceptionCount }); } catch(NmWorkflowException ex) { return Results.Problem(ex.Message,statusCode:ex.StatusCode); } }).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapGet("/nm-documents/{id:guid}/semantic-sessions/latest", async(Guid id,LacDbContext db,CancellationToken ct) => {
    var session = await db.NmSemanticAnalysisSessions.AsNoTracking().Where(x=>x.NmDocumentId==id).OrderByDescending(x=>x.StartedAt).Select(x=>new {x.Id,x.NmDocumentId,x.ParserVersion,x.Status,x.SourcePagesJson,x.AutoStructuredCount,x.ExceptionCount,x.StartedAt,x.CompletedAt,ExceptionReasons=x.OwnerBlocks.SelectMany(b=>b.Exceptions).GroupBy(e=>e.Reason).Select(g=>new {Reason=g.Key,Count=g.Count()}).ToList()}).FirstOrDefaultAsync(ct); return session is null ? Results.NotFound() : Results.Ok(session);
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapGet("/nm-semantic-sessions/{id:guid}/owner-blocks", async(Guid id,string? status,LacDbContext db,CancellationToken ct) => Results.Ok(await db.NmSemanticOwnerBlocks.AsNoTracking().Where(x=>x.AnalysisSessionId==id && (status==null || x.Status.ToString()==status)).OrderBy(x=>x.SourceSequence).Select(x=>new{x.Id,x.SourceSequence,x.PageStart,x.PageEnd,x.RecordedNameRaw,x.FatherOrSpouseRaw,x.ResidenceRaw,x.ShareRaw,x.Status,x.SourceRegionJson,x.FieldSourcesJson,Exceptions=x.Exceptions.Select(e=>new{e.Reason,e.FieldName,e.Detail,e.SourcePage,e.SourceRegionJson}).ToList(),Parcels=x.ParcelGroups.SelectMany(g=>g.Entries).Select(p=>new{p.RawKhasraText,p.RawAreaText,p.LandClassRaw,p.IsInherited,p.ValidationState,p.SourcePage,p.SourceRegionJson,p.KhasraSourceRegionJson,p.AreaSourceRegionJson,p.LandClassSourceRegionJson}).ToList(),Components=x.CompensationComponents.Select(c=>new{c.ComponentType,c.RawAmountText,c.Amount,c.SemanticState,c.SourcePage,c.SourceRegionJson}).ToList(),Relations=db.NmSemanticSourceRelations.Where(r=>r.OwnerBlockId==x.Id).Select(r=>new{r.RelationType,r.SourcePage,r.SourceRegionJson,r.RawSourceText,r.OriginalSourcePage,r.OriginalSourceRegionJson,r.OriginalSourceSequence}).ToList()}).ToListAsync(ct))).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapGet("/nm-semantic-sessions/{id:guid}/exceptions", async(Guid id,LacDbContext db,CancellationToken ct) => Results.Ok(await db.NmSemanticOwnerBlocks.AsNoTracking().Where(x=>x.AnalysisSessionId==id).SelectMany(x=>x.Exceptions.Select(e=>new { e.Id, OwnerBlockId=x.Id, x.SourceSequence, x.RecordedNameRaw, e.Reason, e.FieldName, e.Detail, e.SourcePage, e.SourceRegionJson })).ToListAsync(ct))).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapPut("/nm-semantic-parcels/{id:guid}/area", async(Guid id, ReviewNmSemanticAreaRequest request, NmWorkflowService workflow, CancellationToken ct) => { try { var entry=await workflow.ReviewSemanticAreaAsync(id,request.RawReviewerValue,request.ReviewedBy,ct); return Results.Ok(new {entry.Id,entry.AreaReviewerValueRaw,entry.AreaReviewerValueNormalized,entry.AreaFieldState,entry.AreaReviewedAt,entry.AreaReviewedBy,Status=entry.ParcelGroup.OwnerBlock.Status}); } catch(NmWorkflowException ex) { return Results.Problem(ex.Message,statusCode:ex.StatusCode); } }).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapPost("/nm-semantic-parcels/{id:guid}/area/unreadable", async(Guid id, ReviewNmSemanticUnreadableRequest request, NmWorkflowService workflow, CancellationToken ct) => { try { var entry=await workflow.MarkSemanticAreaUnreadableAsync(id,request.ReviewedBy,ct); return Results.Ok(new {entry.Id,entry.AreaFieldState,entry.AreaReviewedAt,entry.AreaReviewedBy,Status=entry.ParcelGroup.OwnerBlock.Status}); } catch(NmWorkflowException ex) { return Results.Problem(ex.Message,statusCode:ex.StatusCode); } }).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapPut("/nm-semantic-owners/{id:guid}/review", async(Guid id, ReviewNmSemanticOwnerRequest request, LacDbContext db, CancellationToken ct) => { var owner=await db.NmSemanticOwnerBlocks.Include(x=>x.ParcelGroups).ThenInclude(x=>x.Entries).SingleOrDefaultAsync(x=>x.Id==id,ct); if(owner is null)return Results.NotFound(); owner.ReviewerName=request.Name?.Trim();owner.ReviewerFatherOrSpouse=request.FatherOrSpouse?.Trim();owner.ReviewerResidence=request.Residence?.Trim();owner.ReviewerShare=request.Share?.Trim();owner.ReviewedBy=request.ReviewedBy.Trim();owner.ReviewedAt=DateTimeOffset.UtcNow;owner.ReviewState=request.SourceUnclear?"SourceUnclear":"Reviewed"; foreach(var item in request.Parcels){var entry=owner.ParcelGroups.SelectMany(x=>x.Entries).SingleOrDefault(x=>x.Id==item.Id);if(entry is null)continue;entry.ReviewerKhasra=item.Khasra?.Trim();entry.ReviewerKhasraNormalized=string.IsNullOrWhiteSpace(item.Khasra)?null:KhasraNumber.Normalize(item.Khasra);entry.AreaReviewerValueRaw=item.Area?.Trim();entry.ReviewerLandClass=item.LandClass?.Trim();}await db.SaveChangesAsync(ct);return Results.NoContent();}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapGet("/nm-semantic-sessions/{id:guid}/review-workspace", async(Guid id,LacDbContext db,CancellationToken ct) =>
{
    var session = await db.NmSemanticAnalysisSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? await db.NmSemanticAnalysisSessions.AsNoTracking().Where(x => x.NmDocumentId == id).OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(ct);
    if (session is null) return Results.NotFound();
    var owners = await db.NmSemanticOwnerBlocks.AsNoTracking().Where(x => x.AnalysisSessionId == session.Id).OrderBy(x => x.SourceSequence)
        .Include(x => x.Exceptions).Include(x => x.ParcelGroups).ThenInclude(x => x.Entries).ThenInclude(x => x.ExactKhasraCandidate)
        .Include(x => x.CompensationComponents).ToListAsync(ct);
    var compensationDefinitions = new[] {
        ("land_compensation", "Land Compensation"),
        ("structure_compensation", "Structure Compensation"),
        ("base_total", "Total"),
        ("solatium", "Solatium @30%"),
        ("additional_compensation", "Addl Compensation u/s23(1A) @12%"),
        ("interest", "Addl Interest u/s34 @9%"),
        ("grand_total", "Final Total")
    };
    NmCompensationReviewItem ProjectComponent(NmSemanticCompensationComponent component, string label) => new(
        component.ComponentType, label, component.RawAmountText, component.Amount, component.SourcePage,
        component.SemanticState, component.Amount is not null);
    var ownerCards = owners.Select(owner => new {
        owner.Id, owner.SourceSequence, owner.PageStart, owner.PageEnd, owner.RecordedNameRaw, owner.FatherOrSpouseRaw, owner.ResidenceRaw, owner.ShareRaw, owner.ReviewState, owner.ReviewerName, owner.ReviewerFatherOrSpouse, owner.ReviewerResidence, owner.ReviewerShare, owner.Status, owner.SourceRegionJson, owner.FieldSourcesJson,
        Summary = owner.ParcelGroups.OrderBy(group => group.SourceSequence).Select(group => new { group.ParcelCountAsRecorded, group.TotalAreaAsRecorded }).FirstOrDefault(),
        Parcels = owner.ParcelGroups.SelectMany(group => group.Entries).OrderBy(parcel => parcel.SourceSequence).Select(parcel => new {
            parcel.Id, parcel.SourceSequence, parcel.RawKhasraText, parcel.NormalizedKhasraText, parcel.Qualifier, parcel.ReviewerKhasra, parcel.ReviewerKhasraNormalized, parcel.RawAreaText, parcel.NormalizedAreaText, parcel.AreaReviewerValueRaw, parcel.AreaReviewerValueNormalized, parcel.ReviewerLandClass, parcel.AreaFieldState, parcel.AreaReviewedAt, parcel.AreaReviewedBy, parcel.LandClassRaw, parcel.IsInherited, parcel.ValidationState, parcel.SourcePage, parcel.SourceRegionJson, parcel.KhasraSourceRegionJson, parcel.AreaSourceRegionJson, parcel.LandClassSourceRegionJson,
            Master = parcel.ExactKhasraCandidate == null ? null : new { parcel.ExactKhasraCandidate.DisplayNumber, parcel.ExactKhasraCandidate.AreaBigha, parcel.ExactKhasraCandidate.AreaBiswa, parcel.ExactKhasraCandidate.AreaBiswansi, parcel.ExactKhasraCandidate.TotalArea, parcel.ExactKhasraCandidate.AreaUnit }
        }).ToList(),
        Compensation = compensationDefinitions.Select(definition => {
            var component = owner.CompensationComponents.SingleOrDefault(item => item.ComponentType == definition.Item1);
            return component is null
                ? new NmCompensationReviewItem(definition.Item1, definition.Item2, null, null, null, "NeedsReview", false)
                : ProjectComponent(component, definition.Item2);
        }).ToList(),
        RunningTotals = owner.CompensationComponents.Where(component => component.ComponentType == "running_total")
            .Select(component => ProjectComponent(component, "Running Total")).ToList(),
        Exceptions = owner.Exceptions.Select(exception => new { exception.Id, exception.Reason, exception.FieldName, exception.Detail, exception.SourcePage, exception.SourceRegionJson }).ToList()
    }).ToList();
    var queue = ownerCards.SelectMany(owner => owner.Exceptions.Select(exception => new {
        exception.Id, OwnerBlockId = owner.Id, owner.SourceSequence, owner.RecordedNameRaw, owner.FatherOrSpouseRaw, owner.ResidenceRaw, owner.ShareRaw, owner.PageStart, exception.Reason, exception.FieldName, exception.Detail,
        SourcePage = exception.SourcePage ?? owner.PageStart, SourceRegionJson = exception.SourceRegionJson ?? owner.SourceRegionJson,
        Parcel = owner.Parcels.FirstOrDefault(parcel => (exception.FieldName == "Area" && (parcel.SourcePage == (exception.SourcePage ?? parcel.SourcePage))) || (exception.FieldName == "Khasra" && parcel.SourcePage == (exception.SourcePage ?? parcel.SourcePage))) ?? owner.Parcels.FirstOrDefault()
    })).OrderBy(item => item.SourcePage).ThenBy(item => item.SourceSequence).ThenBy(item => item.FieldName).ToList();
    var resolved = ownerCards.SelectMany(owner => owner.Parcels.Where(parcel => parcel.AreaFieldState == "ResolvedByReviewer" || parcel.AreaFieldState == "SourceUnreadable").Select(parcel => new { OwnerBlockId = owner.Id, owner.SourceSequence, owner.RecordedNameRaw, SourcePage = parcel.SourcePage, FieldName = "Area", Parcel = parcel })).OrderBy(item => item.SourcePage).ThenBy(item => item.SourceSequence).ToList();
    return Results.Ok(new { session.Id, session.NmDocumentId, session.ParserVersion, session.Status, session.SourcePagesJson, session.StartedAt, session.CompletedAt, OwnerCount = ownerCards.Count, TotalExceptions = queue.Count, ResolvedCount = resolved.Count, Owners = ownerCards, Queue = queue, Resolved = resolved });
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapGet("/nm-documents/{id:guid}/review-rows", async(Guid id,LacDbContext db,CancellationToken ct)=> {
    var exists=await db.NmDocuments.AnyAsync(x=>x.Id==id,ct); if(!exists)return Results.NotFound();
    var rows=await db.NmReviewRows.AsNoTracking().Where(x=>x.NmDocumentId==id).OrderBy(x=>x.SourcePage).ThenBy(x=>x.SourceRow).Select(x=>new {x.Id,x.SourcePage,x.SourceRow,x.SourceRegionJson,x.RecordedPersonText,x.FatherOrSpouseText,x.RawKhasrasText,x.RawShareText,x.RawAreaText,x.EntitlementAmount,x.EntitlementBasisText,x.RawSuggestionText,x.Status,x.VerifiedBy,x.VerifiedAt,Khasras=x.Khasras.Select(k=>new{k.Id,k.RawKhasraText,k.NormalizedNumber,k.Qualifier,k.SuggestedKhasraId,SuggestedKhasraDisplay=k.SuggestedKhasra==null?null:k.SuggestedKhasra.DisplayNumber,k.Status,k.RawAreaText,k.RawShareText,k.SourceRegionJson}).ToList()}).ToListAsync(ct);
    var context=await db.NmDocuments.AsNoTracking().Where(x=>x.Id==id).Select(x=>new{x.DocumentId,x.VillageId}).SingleAsync(ct);
    var fragments=await db.NmReviewFragments.AsNoTracking().Where(x=>x.NmDocumentId==id).OrderBy(x=>x.SourcePage).Select(x=>new{x.Id,x.SourcePage,x.SourceRegionJson,x.RawOcrText,x.Status}).ToListAsync(ct);
    return Results.Ok(new { context.DocumentId, context.VillageId, rows, fragments });
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapPost("/nm-documents/{id:guid}/rows", async(Guid id, CreateNmRowRequest request,NmWorkflowService workflow,CancellationToken ct)=> {try { var row=await workflow.AddReviewRowAsync(id,new(request.SourcePage,request.SourceRow,request.SourceRegionJson??"{}",request.RecordedPersonText,request.FatherOrSpouseText,request.RawShareText,request.RawAreaText,request.EntitlementAmount,request.EntitlementBasisText,request.Khasras.Select(x=>new NmKhasraInput(x.RawKhasraText,x.Qualifier,x.RawAreaText,x.RawShareText,x.SourceRegionJson)).ToList()),ct);return Results.Created($"/api/nm-review-rows/{row.Id}",new IdResponse(row.Id));}catch(NmWorkflowException ex){return Results.Problem(ex.Message,statusCode:ex.StatusCode);}}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapPost("/nm-review-rows/{id:guid}/verify", async(Guid id, VerifyNmRowRequest request,NmWorkflowService workflow,CancellationToken ct)=> {try {await workflow.VerifyRowAsync(id,request.VerifiedBy,new(request.SourcePage,request.SourceRow,request.SourceRegionJson??"{}",request.RecordedPersonText,request.FatherOrSpouseText,request.RawShareText,request.RawAreaText,request.EntitlementAmount,request.EntitlementBasisText,request.Khasras.Select(x=>new NmKhasraInput(x.RawKhasraText,x.Qualifier,x.RawAreaText,x.RawShareText,x.SourceRegionJson)).ToList()),ct);return Results.NoContent();}catch(NmWorkflowException ex){return Results.Problem(ex.Message,statusCode:ex.StatusCode);}}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapPut("/nm-review-rows/{id:guid}/khasra-selections", async(Guid id, SelectNmKhasrasRequest request,NmWorkflowService workflow,CancellationToken ct)=> { try { await workflow.SelectKhasrasAsync(id,request.Reviewer,request.Selections.Select(x=>new NmKhasraSelection(x.ReviewKhasraId,x.KhasraId,x.MarkUnreadable)).ToList(),ct); return Results.NoContent(); } catch(NmWorkflowException ex) { return Results.Problem(ex.Message,statusCode:ex.StatusCode); } }).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapPost("/nm-documents/{id:guid}/commit", async(Guid id, CommitNmRequest request,NmWorkflowService workflow,CancellationToken ct)=> {try{return Results.Ok(new{committed=await workflow.CommitAsync(id,request.VerifiedBy,ct)});}catch(NmWorkflowException ex){return Results.Problem(ex.Message,statusCode:ex.StatusCode);}}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapGet("/awards/{id:guid}/documents", async (Guid id,LacDbContext db,CancellationToken ct) => Results.Ok(await DocumentEvidenceQueries.AwardDocumentsAsync(db,id,ct))).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapGet("/khasras/{id:guid}/evidence", async (Guid id,int? page,LacDbContext db,CancellationToken ct) => Results.Ok(await DocumentEvidenceQueries.ReadAsync(db,x=>x.AwardKhasra!=null && x.AwardKhasra.KhasraId==id,page??0,ct))).RequirePermission(PermissionCodes.AwardView);
api.MapGet("/notifications/{id:guid}/evidence", async (Guid id,int? page,LacDbContext db,CancellationToken ct) => Results.Ok(await DocumentEvidenceQueries.ReadAsync(db,x=>x.NotificationId==id,page??0,ct))).RequirePermission(PermissionCodes.AwardView);
api.MapGet("/awards/{id:guid}/evidence", async (Guid id,int? page,LacDbContext db,CancellationToken ct) => Results.Ok(await DocumentEvidenceQueries.ReadAsync(db,x=>x.AwardId==id || (x.AwardKhasra!=null && x.AwardKhasra.AwardId==id) || (x.PossessionEvent!=null && x.PossessionEvent.AwardId==id) || (x.NotificationId!=null && db.AwardNotifications.Any(n=>n.AwardId==id && n.NotificationId==x.NotificationId)),page??0,ct))).RequirePermission(PermissionCodes.AwardView);
api.MapGet("/award-ingestion-sessions/{id:guid}/overview", async (Guid id,AwardIngestionService ingestion,CancellationToken ct) => {try{return Results.Ok(await ingestion.GetReviewOverviewAsync(id,ct));}catch(AwardIngestionException ex){return IngestionProblem(ex);}}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapPost("/award-ingestion-sessions/{id:guid}/context", async (Guid id,ReviewContextRequest request,AwardIngestionService ingestion,CancellationToken ct) => {try{await ingestion.SetReviewContextAsync(id,request,ct);return Results.NoContent();}catch(AwardIngestionException ex){return IngestionProblem(ex);}}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapPost("/award-ingestion-sessions/{id:guid}/confirm-exact", async (Guid id,ConfirmExactRequest request,AwardIngestionService ingestion,CancellationToken ct) => {try{return Results.Ok(new {confirmed=await ingestion.ConfirmExactAsync(id,request,ct)});}catch(AwardIngestionException ex){return IngestionProblem(ex);}}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapPost("/award-ingestion-sessions/{id:guid}/commit-verified", async (Guid id,CommitVerifiedRequest request,AwardIngestionService ingestion,CancellationToken ct) => {try{return Results.Ok(await ingestion.CommitVerifiedAsync(id,request,ct));}catch(AwardIngestionException ex){return IngestionProblem(ex);}}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapPost("/award-ingestion-candidates/{id:guid}/verify", async (Guid id,VerifyExtractedFactRequest request,AwardIngestionService ingestion,CancellationToken ct) => {try{await ingestion.VerifyFactAsync(id,request,ct);return Results.NoContent();}catch(AwardIngestionException ex){return IngestionProblem(ex);}}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapPost("/award-ingestion-candidates/{id:guid}/verify-award-khasra-field", async (Guid id, VerifyAwardKhasraFieldRequest request, AwardIngestionService ingestion, CancellationToken ct) => { try { await ingestion.VerifyAwardKhasraFieldAsync(id, request, ct); return Results.NoContent(); } catch (AwardIngestionException ex) { return IngestionProblem(ex); } }).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapGet("/award-ingestion-candidates/{id:guid}/source-crop", async (Guid id, string? fieldRole, DocumentSourceCropService crops, CancellationToken ct) => { try { return Results.File(await crops.CreateAsync(id, fieldRole, ct), "image/png"); } catch (AwardIngestionException ex) { return IngestionProblem(ex); } }).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapPost("/award-pdf-extractions", async (IFormFile file, Guid? targetAwardId, Guid? selectedVillageId, AwardPdfExtractionService extraction, CancellationToken ct) =>
{
    if (file.Length == 0) return Validation("file", "Choose a non-empty PDF.");
    if (targetAwardId is null) return Validation("targetAwardId", "Choose an Award before uploading its PDF.");
    if (file.Length > pdfMaxRequestBytes) return Validation("file", $"PDF exceeds the configured {pdfMaxFileSizeMb} MB upload limit.");
    try { await using var stream = file.OpenReadStream(); var result = await extraction.QueueUploadAsync(stream, file.FileName, file.ContentType, targetAwardId, selectedVillageId, null, ct); return Results.Created($"/api/documents/{result.DocumentId}", result); }
    catch (AwardIngestionException ex) { return IngestionProblem(ex); }
}).DisableAntiforgery().RequirePermission(PermissionCodes.AwardCoreDocumentUpload, WorkstreamCodes.Award);
api.MapPost("/awards/{awardId:guid}/documents/{documentId:guid}/link", async (Guid awardId, Guid documentId, Guid? villageId, AwardPdfExtractionService extraction, CancellationToken ct) =>
{
    try { return Results.Ok(await extraction.LinkStoredDocumentAsync(documentId, awardId, villageId, ct)); }
    catch (AwardIngestionException ex) { return IngestionProblem(ex); }
}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapPost("/awards/{awardId:guid}/documents/{documentId:guid}/analyze", async (Guid awardId,Guid documentId,Guid? villageId,AwardPdfExtractionService extraction,CancellationToken ct) =>
{
    try { var result=await extraction.AnalyzeAsync(documentId,awardId,villageId,ct);return Results.Accepted($"/api/award-pdf-extractions/{result.JobId}",result); }
    catch(AwardIngestionException ex){return IngestionProblem(ex);}
}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapGet("/award-pdf-extractions/{id:guid}", async (Guid id, AwardPdfExtractionService extraction, CancellationToken ct) => { try { return Results.Ok(await extraction.GetAsync(id, ct)); } catch (AwardIngestionException ex) { return IngestionProblem(ex); } }).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapGet("/award-pdf-extractions/recent", async (AwardPdfExtractionService extraction, CancellationToken ct) => Results.Ok(await extraction.GetRecentUnassignedAsync(ct))).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapGet("/award-pdf-uploads/unlinked", async (AwardPdfExtractionService extraction, CancellationToken ct) => Results.Ok(await extraction.GetUnlinkedStoredDocumentsAsync(ct))).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapPost("/award-pdf-extractions/{id:guid}/reanalyze", async (Guid id, AwardPdfJobRunner runner, CancellationToken ct) => { try { return Results.Ok(await runner.ReanalyzeAsync(id, ct)); } catch (AwardIngestionException ex) { return IngestionProblem(ex); } }).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapGet("/awards/{id:guid}/pdf-extractions", async (Guid id, AwardPdfExtractionService extraction, CancellationToken ct) => Results.Ok(await extraction.GetForAwardAsync(id, ct))).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapGet("/award-ingestion-sessions/{id:guid}", async (Guid id, AwardIngestionService ingestion, CancellationToken ct) => { try { return Results.Ok(await ingestion.GetSummaryAsync(id, ct)); } catch (AwardIngestionException ex) { return IngestionProblem(ex); } }).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapGet("/awards/{id:guid}/ingestion-sessions", async (Guid id, int page, int pageSize, AwardIngestionService ingestion, CancellationToken ct) => Results.Ok(await ingestion.GetHistoryAsync(id, page, pageSize, ct))).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapGet("/award-ingestion-sessions/{id:guid}/candidates", async (Guid id, AwardIngestionCandidateType? type, AwardIngestionCandidateStatus? status, int page, int pageSize, string? bucket, int? sourcePage, AwardIngestionService ingestion, CancellationToken ct) => { try { return Results.Ok(await ingestion.GetCandidatesAsync(id, type, status, page, pageSize, ct, bucket, sourcePage)); } catch (AwardIngestionException ex) { return IngestionProblem(ex); } }).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapPost("/award-ingestion-candidates/{id:guid}/resolve", async (Guid id, ResolveAwardIngestionCandidateRequest request, AwardIngestionService ingestion, CancellationToken ct) => { try { await ingestion.ResolveAsync(id, request.Action, ct); return Results.NoContent(); } catch (AwardIngestionException ex) { return IngestionProblem(ex); } }).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapPost("/award-ingestion-sessions/{id:guid}/commit", async (Guid id, CommitAwardIngestionSessionRequest request, AwardIngestionService ingestion, CancellationToken ct) => { try { return Results.Ok(await ingestion.CommitAsync(id, request.CandidateIds, request.CommittedBy, ct)); } catch (AwardIngestionException ex) { return IngestionProblem(ex); } }).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);

api.MapPost("/awards/{id:guid}/khasras", async (Guid id, AwardFoundationKhasraRequest request, AwardWorkflowService workflow, CancellationToken ct) =>
{
    try { return Results.Ok(await workflow.LinkKhasraAsync(id, new(request.VillageId, request.KhasraNumber, request.Qualifier, request.RecordedTotalAreaBigha, request.RecordedTotalAreaBiswa, request.RecordedTotalAreaBiswansi, request.AwardedAreaBigha, request.AwardedAreaBiswa, request.AwardedAreaBiswansi, request.RelationshipStatus, request.Remarks, request.CanonicalAreaBigha, request.CanonicalAreaBiswa, request.CanonicalAreaBiswansi), ct)); }
    catch (AwardWorkflowException ex) { return AwardWorkflowProblem(ex); }
}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapGet("/awards/{id:guid}/khasras/match", async (Guid id, Guid villageId, string khasraNumber, string? qualifier, AwardWorkflowService workflow, CancellationToken ct) =>
{
    try { return Results.Ok(await workflow.MatchKhasraAsync(id, villageId, khasraNumber, qualifier, ct)); }
    catch (AwardWorkflowException ex) { return AwardWorkflowProblem(ex); }
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapPost("/awards/{id:guid}/khasras/import-preview", async (Guid id, Guid villageId, IFormFile file, LacDbContext db, KhasraWorkspaceService workspace, CancellationToken ct) =>
{
    if (!await db.AwardVillages.AnyAsync(x => x.AwardId == id && x.VillageId == villageId, ct)) return Validation("villageId", "Select a Village linked to this Award.");
    if (file.Length == 0) return Validation("file", "Choose a non-empty Excel workbook.");
    try { await using var stream = file.OpenReadStream(); return Results.Ok(await workspace.PreviewAsync(villageId, stream, ct)); }
    catch (KhasraWorkspaceException ex) { return KhasraProblem(ex); }
}).DisableAntiforgery().RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);

api.MapPost("/awards/{id:guid}/notifications/{notificationId:guid}", async (Guid id, Guid notificationId, AwardWorkflowService workflow, CancellationToken ct) => { try { await workflow.LinkNotificationAsync(id, notificationId, ct); return Results.NoContent(); } catch (AwardWorkflowException ex) { return AwardWorkflowProblem(ex); } }).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Award);
api.MapGet("/awards/{id:guid}/notifications", async (Guid id, LacDbContext db, CancellationToken ct) => Results.Ok(await db.AwardNotifications.AsNoTracking().Where(x => x.AwardId == id).OrderByDescending(x => x.Notification.NotificationDate).Select(x => new AwardNotificationWorkspaceItem(x.NotificationId, x.Notification.NotificationNumber, x.Notification.SectionType, x.Notification.NotificationDate)).ToListAsync(ct))).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);
api.MapPost("/awards/{id:guid}/possession-events", async (Guid id, CreatePossessionEventRequest request, AwardWorkflowService workflow, CancellationToken ct) => { try { var item = await workflow.AddPossessionAsync(id, request.PossessionDate, request.EventType, request.Status, request.Remarks, request.KhasraIds, ct); return Results.Created($"/api/possession-events/{item.Id}", new IdResponse(item.Id)); } catch (AwardWorkflowException ex) { return AwardWorkflowProblem(ex); } }).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.Possession);
api.MapGet("/awards/{id:guid}/possession-events", async (Guid id, LacDbContext db, CancellationToken ct) => Results.Ok(await db.PossessionEvents.AsNoTracking().Where(x => x.AwardId == id).OrderByDescending(x => x.PossessionDate).Select(x => new AwardPossessionWorkspaceItem(x.Id, x.PossessionDate, x.EventType, x.Status, x.KhasraLinks.Count)).ToListAsync(ct))).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Possession);
api.MapPost("/awards/{id:guid}/court-cases", async (Guid id, CreateAwardCourtCaseRequest request, AwardWorkflowService workflow, CancellationToken ct) => { try { var item = await workflow.CreateCourtCaseAsync(id, request.CaseNumber, request.CourtName, request.CaseType, request.FiledDate, request.CurrentStatus, request.Remarks, request.KhasraIds, ct); return Results.Created($"/api/court-cases/{item.Id}", new IdResponse(item.Id)); } catch (AwardWorkflowException ex) { return AwardWorkflowProblem(ex); } }).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.CourtReferences);
api.MapGet("/awards/{id:guid}/court-cases", async (Guid id, LacDbContext db, CancellationToken ct) => Results.Ok(await db.Set<CourtCaseAward>().AsNoTracking().Where(x => x.AwardId == id).OrderByDescending(x => x.CourtCase.FiledDate).Select(x => new AwardCourtCaseWorkspaceItem(x.CourtCaseId, x.CourtCase.CaseNumber, x.CourtCase.CourtName, x.CourtCase.CurrentStatus, db.Set<CourtCaseKhasra>().Count(k => k.CourtCaseId == x.CourtCaseId))).ToListAsync(ct))).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.CourtReferences);
api.MapGet("/awards/{id:guid}/claims", async (Guid id, int page, int pageSize, LacDbContext db, CancellationToken ct) => Results.Ok(await ToPageAsync(db.Claims.AsNoTracking().Where(x => x.AwardId == id).OrderByDescending(x => x.ClaimDate).Select(x => new AwardClaimItem(x.Id, x.ClaimReference, x.ClaimDate, x.ClaimantParty == null ? null : x.ClaimantParty.DisplayName, x.ClaimedRateAmount, x.ClaimedAmount, x.Status, db.Set<ClaimKhasra>().Count(k => k.ClaimId == x.Id))), page, pageSize, ct))).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.AccountsCompensation);

api.MapPost("/awards/{id:guid}/claims", async (Guid id, CreateAwardClaimRequest request, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Awards.AnyAsync(x => x.Id == id, ct)) return NotFound("Award", id);
    var khasraIds = request.KhasraIds?.Distinct().ToArray() ?? [];
    var valid = await db.Set<AwardKhasra>().Where(x => x.AwardId == id && khasraIds.Contains(x.KhasraId)).Select(x => x.KhasraId).ToListAsync(ct);
    if (valid.Count != khasraIds.Length) return Validation("khasraIds", "Every claim Khasra must already be linked to this Award.");
    var claim = new Claim { AwardId = id, ClaimReference = Clean(request.ClaimReference), ClaimDate = request.ClaimDate, ClaimText = Clean(request.ClaimText), ClaimedRateAmount = request.ClaimedRateAmount, ClaimedRateUnit = Clean(request.ClaimedRateUnit), ClaimedAmount = request.ClaimedAmount, Status = Clean(request.Status), Remarks = Clean(request.Remarks) };
    db.Claims.Add(claim); foreach (var khasraId in valid) db.Add(new ClaimKhasra { Claim = claim, KhasraId = khasraId });
    await db.SaveChangesAsync(ct); return Results.Created($"/api/claims/{claim.Id}", new IdResponse(claim.Id));
}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.AccountsCompensation);
api.MapPost("/awards/{id:guid}/land-classes", async (Guid id, CreateAwardLandClassRequest request, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Awards.AnyAsync(x => x.Id == id, ct)) return NotFound("Award", id);
    if (string.IsNullOrWhiteSpace(request.Code)) return Validation("code", "Land classification code is required.");
    if (await db.Set<AwardLandClass>().AnyAsync(x => x.AwardId == id && x.Code == request.Code.Trim(), ct)) return Validation("code", "This land classification already exists for the Award.");
    var item = new AwardLandClass { AwardId = id, Code = request.Code.Trim(), Description = Clean(request.Description) }; db.Add(item); await db.SaveChangesAsync(ct); return Results.Created($"/api/award-land-classes/{item.Id}", new IdResponse(item.Id));
}).RequirePermission(PermissionCodes.AwardEdit);
api.MapPost("/awards/{id:guid}/valuation-rules", async (Guid id, CreateAwardValuationRuleRequest request, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Awards.AnyAsync(x => x.Id == id, ct)) return NotFound("Award", id);
    if (request.AwardLandClassId is not null && !await db.Set<AwardLandClass>().AnyAsync(x => x.Id == request.AwardLandClassId && x.AwardId == id, ct)) return Validation("awardLandClassId", "Land class must belong to this Award.");
    var item = new AwardValuationRule { AwardId = id, AwardLandClassId = request.AwardLandClassId, RuleType = Clean(request.RuleType) ?? "Other", RateAmount = request.RateAmount, RateUnit = Clean(request.RateUnit), ReferenceDate = request.ReferenceDate, LegalSection = Clean(request.LegalSection), Description = Clean(request.Description) }; db.Add(item); await db.SaveChangesAsync(ct); return Results.Created($"/api/award-valuation-rules/{item.Id}", new IdResponse(item.Id));
}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.AccountsCompensation);
api.MapPost("/awards/{id:guid}/compensation-rules", async (Guid id, CreateAwardCompensationRuleRequest request, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Awards.AnyAsync(x => x.Id == id, ct)) return NotFound("Award", id);
    var item = new AwardCompensationRule { AwardId = id, RuleType = Clean(request.RuleType) ?? "Other", RatePercent = request.RatePercent, RateAmount = request.RateAmount, LegalSection = Clean(request.LegalSection), BasisDescription = Clean(request.BasisDescription), StartEvent = Clean(request.StartEvent), EndEvent = Clean(request.EndEvent), Remarks = Clean(request.Remarks) }; db.Add(item); await db.SaveChangesAsync(ct); return Results.Created($"/api/award-compensation-rules/{item.Id}", new IdResponse(item.Id));
}).RequirePermission(PermissionCodes.AwardEdit, WorkstreamCodes.AccountsCompensation);
api.MapPost("/awards/{id:guid}/area-issues", async (Guid id, CreateAwardAreaIssueRequest request, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Awards.AnyAsync(x => x.Id == id, ct)) return NotFound("Award", id);
    if (request.KhasraId is not null && !await db.Set<AwardKhasra>().AnyAsync(x => x.AwardId == id && x.KhasraId == request.KhasraId, ct)) return Validation("khasraId", "Khasra must already be linked to this Award.");
    var item = new AwardAreaIssue { AwardId = id, KhasraId = request.KhasraId, IssueType = Clean(request.IssueType) ?? "Other", NotificationAreaBigha = request.NotificationAreaBigha, FieldBookAreaBigha = request.FieldBookAreaBigha, DifferenceBigha = request.DifferenceBigha, Status = Clean(request.Status) ?? "Open", CorrigendumReference = Clean(request.CorrigendumReference), CorrigendumDate = request.CorrigendumDate, Remarks = Clean(request.Remarks) }; db.Add(item); await db.SaveChangesAsync(ct); return Results.Created($"/api/award-area-issues/{item.Id}", new IdResponse(item.Id));
}).RequirePermission(PermissionCodes.AwardEdit);
api.MapPost("/awards/{id:guid}/supplementary-matters", async (Guid id, CreateAwardSupplementaryMatterRequest request, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Awards.AnyAsync(x => x.Id == id, ct)) return NotFound("Award", id);
    var item = new AwardSupplementaryMatter { AwardId = id, MatterType = Clean(request.MatterType) ?? "Other", Status = Clean(request.Status) ?? "Pending", Description = Clean(request.Description), SupplementaryAwardId = request.SupplementaryAwardId }; db.Add(item); await db.SaveChangesAsync(ct); return Results.Created($"/api/award-supplementary-matters/{item.Id}", new IdResponse(item.Id));
}).RequirePermission(PermissionCodes.AwardEdit);
api.MapGet("/awards/{id:guid}/export/{format}", async (Guid id, string format, LacDbContext db, CancellationToken ct) =>
{
    var award = await db.Awards.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (award is null) return NotFound("Award", id);
    var rows = await db.Set<AwardKhasra>().AsNoTracking().Where(x => x.AwardId == id).OrderBy(x => x.Khasra.RectangleNumber).ThenBy(x => x.Khasra.DisplayNumber).Select(x => new KhasraExportRow(x.Khasra.DisplayNumber, x.AwardedAreaBigha, x.AwardedAreaBiswa, x.AwardedAreaBiswansi, "", x.RelationshipStatus ?? "Recorded", award.AwardNumber)).ToListAsync(ct);
    var name = $"award-{award.AwardNumber}";
    return format.ToLowerInvariant() switch { "xlsx" => Results.File(KhasraWorkspaceService.Excel(rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{name}.xlsx"), "csv" => Results.File(KhasraWorkspaceService.Csv(rows), "text/csv", $"{name}.csv"), "pdf" => Results.File(KhasraWorkspaceService.Pdf(rows), "application/pdf", $"{name}.pdf"), "docx" => Results.File(KhasraWorkspaceService.Docx(rows), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{name}.docx"), _ => Validation("format", "Choose xlsx, csv, pdf, or docx.") };
}).RequirePermission(PermissionCodes.AwardView);

api.MapPost("/khasra-review-flags/{id:guid}/resolve", async (Guid id, ResolveKhasraReviewRequest request, AwardWorkflowService workflow, CancellationToken ct) =>
{
    try { await workflow.ResolveReviewFlagAsync(id, request.ResolvedBy, ct); return Results.NoContent(); }
    catch (AwardWorkflowException ex) { return AwardWorkflowProblem(ex); }
}).RequirePermission(PermissionCodes.KhasraEdit, WorkstreamCodes.LandRecords);
api.MapGet("/khasras/{id:guid}/review-flags", async (Guid id, LacDbContext db, CancellationToken ct) => Results.Ok(await db.KhasraReviewFlags.AsNoTracking().Where(x => x.KhasraId == id && x.Status == "Open").OrderByDescending(x => x.CreatedAt).Select(x => new KhasraReviewFlagItem(x.Id, x.Status, x.ReasonCode, x.Message, x.RelatedAwardId, x.RelatedAward == null ? null : x.RelatedAward.AwardNumber)).ToListAsync(ct))).RequirePermission(PermissionCodes.KhasraView);

api.MapGet("/awards/{id:guid}/khasras", async (Guid id, int page, int pageSize, LacDbContext db, CancellationToken ct) =>
{
    var rows = db.Set<AwardKhasra>().AsNoTracking().Where(x => x.AwardId == id).OrderBy(x => x.Khasra.Village.Name).ThenBy(x => x.Khasra.RectangleNumber == null).ThenBy(x => x.Khasra.RectangleNumber!.Length).ThenBy(x => x.Khasra.RectangleNumber).ThenBy(x => x.Khasra.DisplayNumber)
        .Select(x => new AwardWorkspaceKhasraItem(x.Id, x.Khasra.Id, x.Khasra.DisplayNumber, x.Khasra.Village.Name, x.Khasra.RectangleNumber, x.Khasra.AreaBigha, x.Khasra.AreaBiswa, x.Khasra.AreaBiswansi, x.RecordedTotalAreaBigha, x.RecordedTotalAreaBiswa, x.RecordedTotalAreaBiswansi, x.AwardedAreaBigha, x.AwardedAreaBiswa, x.AwardedAreaBiswansi, x.RelationshipStatus, db.KhasraReviewFlags.Where(f => f.KhasraId == x.KhasraId && f.Status == "Open").Select(f => (Guid?)f.Id).FirstOrDefault()));
    return Results.Ok(await ToPageAsync(rows, page, pageSize, ct));
}).RequirePermission(PermissionCodes.AwardView, WorkstreamCodes.Award);

api.MapPost("/village-lrs/{id:guid}/entries", async (Guid id, LrEntryRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try { var saved = await workflow.CreateAsync(id, request.ToInput(), ct); return Results.Created($"/api/lr-entries/{saved.Id}", saved); }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
}).RequirePermission(PermissionCodes.LrEdit, WorkstreamCodes.LandRecords);

api.MapPost("/village-lrs/{id:guid}/entries/batch", async (Guid id, BatchLrEntryRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try
    {
        var saved = new List<LrRowSaveResult>();
        foreach (var row in request.Rows) saved.Add(await workflow.CreateAsync(id, row.ToInput(), ct));
        return Results.Created($"/api/village-lrs/{id}/entries", saved);
    }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
}).RequirePermission(PermissionCodes.LrEdit, WorkstreamCodes.LandRecords);

api.MapPut("/lr-entries/{id:guid}", async (Guid id, UpdateLrEntryRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try { return Results.Ok(await workflow.UpdateAsync(id, request.ExpectedRevision, request.Row.ToInput(), ct)); }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
}).RequirePermission(PermissionCodes.LrEdit, WorkstreamCodes.LandRecords);

api.MapPost("/lr-entries/{id:guid}/commit", async (Guid id, CommitLrEntryRequest request, LrWorkflowService workflow, CancellationToken ct) =>
{
    try { return Results.Ok(await workflow.CommitAsync(id, request.ExpectedRevision, request.ApplyParsedAreaToAcquisitionLinks, ct)); }
    catch (LrWorkflowException ex) { return WorkflowProblem(ex); }
}).RequirePermission(PermissionCodes.LrCommit, WorkstreamCodes.LandRecords);

api.MapGet("/villages/{id:guid}/khatauni", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    if (!await db.Villages.AnyAsync(x => x.Id == id, ct)) return NotFound("Village", id);
    return Results.Ok(await db.KhatauniRecords.AsNoTracking().Where(x => x.VillageId == id).OrderByDescending(x => x.AsOfDate).ThenByDescending(x => x.RecordYearText).Select(KhatauniListItem.Selector).ToListAsync(ct));
}).RequirePermission(PermissionCodes.LrView, WorkstreamCodes.LandRecords);
api.MapGet("/khatauni/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var record = await db.KhatauniRecords.AsNoTracking().Include(x => x.Village).Include(x => x.SourceDocument).Include(x => x.Khatas).ThenInclude(x => x.KhasraLinks).Include(x => x.Khatas).ThenInclude(x => x.PartyShares).SingleOrDefaultAsync(x => x.Id == id, ct);
    if (record is null) return NotFound("Khatauni record", id);
    return Results.Ok(new KhatauniDetail(record.Id, record.VillageId, record.Village.Name, record.ReferenceNumber, record.RecordYearText, record.AsOfDate, record.EffectiveFrom, record.EffectiveTo, record.Remarks, record.VerificationStatus.ToString(), record.Version, record.SourceDocumentId, record.SourceDocument?.OriginalFileName, record.Khatas.Count, record.Khatas.Sum(k => k.KhasraLinks.Count), record.Khatas.SelectMany(k => k.PartyShares).Select(s => s.PartyId).Distinct().Count(), record.Khatas.OrderBy(k => k.KhataNumber).Select(k => new KhataSummary(k.Id, k.KhataNumber, k.KhasraLinks.Count, k.PartyShares.Count, OwnershipService.ValidateShares(k.PartyShares).Message, k.PartyShares.All(s => s.VerificationStatus == RevenueRecordVerificationStatus.Verified))).ToList()));
}).RequirePermission(PermissionCodes.LrView, WorkstreamCodes.LandRecords);
api.MapGet("/khatas/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
    var khata = await db.Khatas.AsNoTracking().Include(x => x.KhatauniRecord).ThenInclude(x => x.Village).Include(x => x.KhasraLinks).ThenInclude(x => x.Khasra).Include(x => x.PartyShares).ThenInclude(x => x.Party).SingleOrDefaultAsync(x => x.Id == id, ct);
    if (khata is null) return NotFound("Khata", id);
    var validation = OwnershipService.ValidateShares(khata.PartyShares);
    return Results.Ok(new KhataDetail(khata.Id, khata.KhataNumber, khata.RawKhataNumber, khata.Remarks, khata.KhatauniRecordId, khata.KhatauniRecord.ReferenceNumber, khata.KhatauniRecord.VillageId, khata.KhatauniRecord.Village.Name, khata.KhasraLinks.OrderBy(x => x.Khasra.DisplayNumber).Select(x => new KhataKhasraItem(x.KhasraId, x.Khasra.DisplayNumber, x.RecordedArea, x.RawAreaText, x.AreaUnit)).ToList(), khata.PartyShares.OrderBy(x => x.Party.DisplayName).Select(x => new PartyShareItem(x.Id, x.PartyId, x.Party.DisplayName, x.RawShareText, x.ShareNumerator, x.ShareDenominator, x.VerificationStatus.ToString(), x.Version)).ToList(), validation.Message));
}).RequirePermission(PermissionCodes.LrView, WorkstreamCodes.LandRecords);
api.MapGet("/khasras/{id:guid}/ownership", async (Guid id, DateOnly? asOfDate, OwnershipService ownership, CancellationToken ct) => Results.Ok(await ownership.GetRecordedOwnershipAsync(id, asOfDate, ct))).RequirePermission(PermissionCodes.LrView, WorkstreamCodes.LandRecords);
api.MapGet("/khasras/{id:guid}/ownership-history", async (Guid id, LacDbContext db, CancellationToken ct) => Results.Ok(await db.KhataKhasras.AsNoTracking().Where(x => x.KhasraId == id).OrderByDescending(x => x.Khata.KhatauniRecord.AsOfDate).Select(x => new OwnershipHistoryItem(x.Khata.KhatauniRecordId, x.KhataId, x.Khata.KhatauniRecord.ReferenceNumber, x.Khata.KhatauniRecord.RecordYearText, x.Khata.KhatauniRecord.AsOfDate, x.Khata.KhataNumber, x.Khata.KhatauniRecord.VerificationStatus.ToString())).ToListAsync(ct))).RequirePermission(PermissionCodes.LrView, WorkstreamCodes.LandRecords);
api.MapGet("/parties/{id:guid}", async (Guid id, LacDbContext db, CancellationToken ct) =>
{
 var p = await db.Parties.AsNoTracking().Where(x => x.Id == id).Select(x => new PartyDetail(x.Id, x.PartyType.ToString(), x.DisplayName, x.FatherOrSpouseName, x.AddressText, x.Remarks, x.Version, x.KhataShares.Select(s => new PartyHoldingItem(s.Khata.KhatauniRecord.VillageId, s.Khata.KhatauniRecord.Village.Name, s.Khata.KhatauniRecordId, s.Khata.KhatauniRecord.ReferenceNumber, s.KhataId, s.Khata.KhataNumber, s.Khata.KhasraLinks.Select(k => new KhasraReference(k.KhasraId, k.Khasra.DisplayNumber)).ToList(), s.RawShareText, s.ShareNumerator, s.ShareDenominator)).ToList())).FirstOrDefaultAsync(ct); return p is null ? NotFound("Party", id) : Results.Ok(p);
}).RequirePermission(PermissionCodes.LrView, WorkstreamCodes.LandRecords);
api.MapPost("/khatauni", async (CreateKhatauniRequest request, LacDbContext db, CancellationToken ct) => { if (request.VillageId == Guid.Empty || !await db.Villages.AnyAsync(x => x.Id == request.VillageId, ct)) return NotFound("Village", request.VillageId); var record = new KhatauniRecord { VillageId = request.VillageId, ReferenceNumber = request.ReferenceNumber?.Trim(), RecordYearText = request.RecordYearText?.Trim(), AsOfDate = request.AsOfDate, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, Remarks = request.Remarks?.Trim(), VerificationStatus = request.VerificationStatus }; db.KhatauniRecords.Add(record); await db.SaveChangesAsync(ct); return Results.Created($"/api/khatauni/{record.Id}", new IdResponse(record.Id)); }).RequirePermission(PermissionCodes.LrEdit, WorkstreamCodes.LandRecords);
api.MapPost("/khatauni/{id:guid}/khatas", async (Guid id, CreateKhataRequest request, OwnershipService ownership, CancellationToken ct) => { try { var khata = await ownership.CreateKhataAsync(id, request.KhataNumber, request.RawKhataNumber, request.Remarks, ct); return Results.Created($"/api/khatas/{khata.Id}", new IdResponse(khata.Id)); } catch (OwnershipWorkflowException ex) { return OwnershipProblem(ex); } }).RequirePermission(PermissionCodes.LrEdit, WorkstreamCodes.LandRecords);
api.MapPost("/khatas/{id:guid}/khasras", async (Guid id, LinkKhataKhasraRequest request, OwnershipService ownership, CancellationToken ct) => { try { var link = await ownership.LinkKhasraAsync(id, request.KhasraId, request.RawKhasraText, request.RecordedArea, request.RawAreaText, request.AreaUnit, request.Remarks, ct); return Results.Created($"/api/khatas/{id}/khasras/{link.Id}", new IdResponse(link.Id)); } catch (OwnershipWorkflowException ex) { return OwnershipProblem(ex); } }).RequirePermission(PermissionCodes.LrEdit, WorkstreamCodes.LandRecords);
api.MapPost("/parties", async (CreatePartyRequest request, OwnershipService ownership, CancellationToken ct) => { try { var party = await ownership.CreatePartyAsync(request.PartyType, request.DisplayName, request.FatherOrSpouseName, request.AddressText, request.Remarks, ct); return Results.Created($"/api/parties/{party.Id}", new IdResponse(party.Id)); } catch (OwnershipWorkflowException ex) { return OwnershipProblem(ex); } }).RequirePermission(PermissionCodes.LrEdit, WorkstreamCodes.LandRecords);
api.MapPost("/khatas/{id:guid}/shares", async (Guid id, AddShareRequest request, OwnershipService ownership, CancellationToken ct) => { try { var share = await ownership.AddShareAsync(id, request.PartyId, request.RawShareText, request.ShareNumerator, request.ShareDenominator, request.Remarks, request.VerificationStatus, ct); return Results.Created($"/api/khatas/{id}/shares/{share.Id}", new IdResponse(share.Id)); } catch (OwnershipWorkflowException ex) { return OwnershipProblem(ex); } }).RequirePermission(PermissionCodes.LrEdit, WorkstreamCodes.LandRecords);
api.MapPost("/khatauni/{id:guid}/verify", async (Guid id, VerifyKhatauniRequest request, OwnershipService ownership, CancellationToken ct) => { try { await ownership.VerifyKhatauniAsync(id, request.ExpectedVersion, ct); return Results.NoContent(); } catch (OwnershipWorkflowException ex) { return OwnershipProblem(ex); } }).RequirePermission(PermissionCodes.LrVerify, WorkstreamCodes.LandRecords);

// Keep unmatched API paths as real 404s; only browser routes receive index.html.
api.MapFallback(() => Results.NotFound());
app.MapFallbackToFile("index.html");
app.Run();

static IResult NotFound(string entityName, Guid id) => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: $"{entityName} not found", detail: $"No {entityName.ToLowerInvariant()} exists for id {id}.");
static IResult Validation(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

static IResult WorkflowProblem(LrWorkflowException exception) => Results.Problem(statusCode: exception.StatusCode, title: "LR workflow validation", detail: exception.Message);
static IResult OwnershipProblem(OwnershipWorkflowException exception) => Results.Problem(statusCode: exception.StatusCode, title: "Recorded ownership validation", detail: exception.Message);
static IResult KhasraProblem(KhasraWorkspaceException exception) => Results.Problem(statusCode: exception.StatusCode, title: "Khasra workspace validation", detail: exception.Message);
static IResult AwardWorkflowProblem(AwardWorkflowException exception) => Results.Problem(statusCode: exception.StatusCode, title: "Award workflow validation", detail: exception.Message);
static IResult IngestionProblem(AwardIngestionException exception) => Results.Problem(statusCode: exception.StatusCode, title: "Award ingestion validation", detail: exception.Message);
static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
static async Task<PageResponse<T>> ToPageAsync<T>(IQueryable<T> query, int page, int pageSize, CancellationToken ct)
{
    page = Math.Max(page, 0); pageSize = Math.Clamp(pageSize == 0 ? 25 : pageSize, 1, 100);
    var totalCount = await query.CountAsync(ct); var items = await query.Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
    return new PageResponse<T>(items, page, pageSize, totalCount);
}

public partial class Program { }
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
public sealed record NmCompensationReviewItem(string ComponentType, string Label, string? RawAmountText, decimal? Amount, int? SourcePage, string SemanticState, bool IsResolved);
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
public sealed record VillageOfficialSummary(int KhasraCount, int AwardCount, int NotificationCount, int PossessionEventCount, int CourtCaseCount, int ValuationRuleCount, int CompensationRuleCount, int ClaimCount);
public sealed record VillageOfficialAwardItem(Guid Id, string AwardNumber, DateOnly? AwardDate, string? AwardType, string Status, int KhasraCount, int DocumentCount);
public sealed record VillageOfficialNotificationItem(Guid Id, string NotificationNumber, string SectionType, DateOnly? NotificationDate);
public sealed record PendingCandidateTypeCount(string CandidateType, int Count);
public sealed record VillagePendingReviewItem(Guid SessionId, Guid? AwardId, string? AwardNumber, string SourceDocumentName, string Status, int PendingCandidateCount, IReadOnlyList<PendingCandidateTypeCount> CandidateCounts);
public sealed record VillageSourceStatusItem(string SourceType, string Status, string Detail);
public sealed record VillageOverviewResponse(VillageDetail Village, VillageOfficialSummary Official, IReadOnlyList<VillageOfficialAwardItem> Awards, IReadOnlyList<VillageOfficialNotificationItem> Notifications, IReadOnlyList<VillagePendingReviewItem> PendingReview, IReadOnlyList<VillageSourceStatusItem> Sources);
public sealed record AwardLinkItem(Guid Id, string AwardNumber, decimal? AcquiredArea, string? AreaUnit, string? AcquisitionStatus);
public sealed record KhasraListBaseItem(Guid Id, string? RectangleNumber, string DisplayNumber, decimal? AreaBigha, int? AreaBiswa, int? AreaBiswansi, string AcquisitionStatus, IReadOnlyList<AwardLinkItem> Awards) { public static readonly System.Linq.Expressions.Expression<Func<Khasra, KhasraListBaseItem>> Selector = x => new KhasraListBaseItem(x.Id, x.RectangleNumber, x.DisplayNumber, x.AreaBigha, x.AreaBiswa, x.AreaBiswansi, x.AwardLinks.Select(a => a.AcquisitionStatus).FirstOrDefault(s => s != null) ?? "Not recorded", x.AwardLinks.OrderBy(a => a.Award.AwardNumber).Select(a => new AwardLinkItem(a.Award.Id, a.Award.AwardNumber, a.AcquiredArea, a.AreaUnit, a.AcquisitionStatus)).ToList()); }
public sealed record KhasraListItem(Guid Id, string? RectangleNumber, string DisplayNumber, decimal? AreaBigha, int? AreaBiswa, int? AreaBiswansi, string OwnerSummary, string AcquisitionStatus, IReadOnlyList<AwardLinkItem> Awards);
public sealed record NotificationLinkItem(Guid Id, string NotificationNumber, string SectionType, DateOnly? NotificationDate, decimal? Area, string? AreaUnit);
public sealed record LrEntryItem(Guid Id, Guid VillageLrId, string RawKhasraText, string? RawAreaText, string? RawRemarks, string VerificationStatus);
public sealed record KhasraDetail(Guid Id, string DisplayNumber, string NormalizedNumber, string? RectangleNumber, string? KillaNumber, string? SubdivisionNumber, decimal? TotalArea, string? AreaUnit, decimal? AreaBigha, int? AreaBiswa, int? AreaBiswansi, string? Remarks, VillageReference Village, IReadOnlyList<NotificationLinkItem> Notifications, IReadOnlyList<AwardLinkItem> Awards, IReadOnlyList<LrEntryItem> LrEntries);
public sealed record KhasraOfficialAwardHistoryItem(Guid AwardId, string AwardNumber, DateOnly? AwardDate, decimal? RecordedAreaBigha, int? RecordedAreaBiswa, int? RecordedAreaBiswansi, decimal? AwardedAreaBigha, int? AwardedAreaBiswa, int? AwardedAreaBiswansi, string? AcquisitionStatus);
public sealed record KhasraOfficialPossessionItem(Guid PossessionEventId, Guid AwardId, DateOnly? PossessionDate, string? EventType, string? Status);
public sealed record KhasraOfficialCourtItem(Guid CourtCaseId, string CaseNumber, string CourtName, string? Status);
public sealed record KhasraPendingDocumentReviewItem(Guid SessionId, Guid? AwardId, string? AwardNumber, string SourceDocumentName, string Status, int PendingCandidateCount);
public sealed record KhasraHistoryResponse(IReadOnlyList<KhasraOfficialAwardHistoryItem> Awards, IReadOnlyList<KhasraOfficialPossessionItem> Possession, IReadOnlyList<KhasraOfficialCourtItem> CourtCases, IReadOnlyList<KhasraPendingDocumentReviewItem> PendingDocumentReviews);
public sealed record ProjectReference(Guid Id, string Name, string? RequiringAgency, string? ActRegime);
public sealed record AwardListItem(Guid Id, string AwardNumber, DateOnly? AwardDate, string? AwardType, string Status, string? ActRegime, string? ProjectName, string? RequiringAgency, string? VillageNames, int LinkedKhasraCount) { public static readonly System.Linq.Expressions.Expression<Func<Award, AwardListItem>> Selector = x => new AwardListItem(x.Id, x.AwardNumber, x.AwardDate, x.AwardType, x.Status, x.ActRegime, x.AcquisitionProject == null ? null : x.AcquisitionProject.Name, x.AcquisitionProject == null ? null : x.AcquisitionProject.RequiringAgency, string.Join(", ", x.KhasraLinks.Select(link => link.Khasra.Village.Name).Distinct()), x.KhasraLinks.Count); }
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
public sealed record KhasraBatchRequest(IReadOnlyList<KhasraWorkspaceRow> Rows);
public sealed record CreateNotificationRequest(string SectionType, string NotificationNumber, DateOnly? NotificationDate, string? Remarks);
public sealed record CreateAwardRequest(string AwardNumber, DateOnly? AwardDate, string? AwardType, string? ActRegime);
public sealed record CreateSupplementaryAwardRequest(string AwardNumber, DateOnly? AwardDate, string ParentAwardReference, Guid? ParentAwardId = null, string? Remarks = null);
public sealed record AwardFoundationCreateRequest(string AwardNumber, Guid VillageId, DateOnly? AwardDate, string? AwardType, string? ActRegime, string? Purpose, Guid? AcquisitionProjectId, string? Remarks);
public sealed record CreateAwardIngestionSessionRequest(AwardIngestionSourceType SourceType, Guid? TargetAwardId, Guid? SelectedVillageId, Guid? SourceDocumentId, string? CreatedBy, string? Remarks, IReadOnlyList<IngestionCandidateInput> Candidates);
public sealed record ResolveAwardIngestionCandidateRequest(string Action);
public sealed record CommitAwardIngestionSessionRequest(IReadOnlyList<Guid> CandidateIds, string? CommittedBy);
public sealed record AwardFoundationKhasraRequest(Guid VillageId, string KhasraNumber, string? Qualifier, decimal? RecordedTotalAreaBigha, int? RecordedTotalAreaBiswa, int? RecordedTotalAreaBiswansi, decimal? AwardedAreaBigha, int? AwardedAreaBiswa, int? AwardedAreaBiswansi, string? RelationshipStatus, string? Remarks, decimal? CanonicalAreaBigha = null, int? CanonicalAreaBiswa = null, int? CanonicalAreaBiswansi = null);
public sealed record ResolveKhasraReviewRequest(string? ResolvedBy);
public sealed record AwardWorkspaceKhasraItem(Guid AwardKhasraId, Guid KhasraId, string DisplayNumber, string VillageName, string? RectangleNumber, decimal? CanonicalAreaBigha, int? CanonicalAreaBiswa, int? CanonicalAreaBiswansi, decimal? RecordedTotalAreaBigha, int? RecordedTotalAreaBiswa, int? RecordedTotalAreaBiswansi, decimal? AwardedAreaBigha, int? AwardedAreaBiswa, int? AwardedAreaBiswansi, string? RelationshipStatus, Guid? ReviewFlagId);
public sealed record KhasraReviewFlagItem(Guid Id, string Status, string ReasonCode, string? Message, Guid? RelatedAwardId, string? RelatedAwardNumber);
public sealed record AwardWorkspaceOverview(Guid Id, string AwardNumber, DateOnly? AwardDate, string? AwardType, Guid? ParentAwardId, string? ParentAwardNumber, string? ParentAwardReference, string? Purpose, string? ActRegime, string Status, string? Remarks, ProjectReference? Project, IReadOnlyList<VillageReference> Villages, int KhasraCount, int NotificationCount, int PossessionEventCount, int CourtCaseCount, int ClaimCount, int OpenAreaIssueCount, int DocumentCount, string KhasrasData, string NotificationsData, string PossessionData, string LitigationData, string ClaimsData);
public sealed record CreatePossessionEventRequest(DateOnly? PossessionDate, string? EventType, string? Status, string? Remarks, IReadOnlyList<Guid> KhasraIds);
public sealed record CreateAwardCourtCaseRequest(string CaseNumber, string CourtName, string? CaseType, DateOnly? FiledDate, string? CurrentStatus, string? Remarks, IReadOnlyList<Guid> KhasraIds);
public sealed record AwardNotificationWorkspaceItem(Guid Id, string NotificationNumber, string SectionType, DateOnly? NotificationDate);
public sealed record AwardPossessionWorkspaceItem(Guid Id, DateOnly? PossessionDate, string? EventType, string? Status, int KhasraCount);
public sealed record AwardCourtCaseWorkspaceItem(Guid Id, string CaseNumber, string CourtName, string? Status, int KhasraCount);
public sealed record AwardClaimItem(Guid Id, string? ClaimReference, DateOnly? ClaimDate, string? ClaimantName, decimal? ClaimedRateAmount, decimal? ClaimedAmount, string? Status, int KhasraCount);
public sealed record CreateAwardClaimRequest(string? ClaimReference, DateOnly? ClaimDate, string? ClaimText, decimal? ClaimedRateAmount, string? ClaimedRateUnit, decimal? ClaimedAmount, string? Status, string? Remarks, IReadOnlyList<Guid>? KhasraIds);
public sealed record CreateAwardLandClassRequest(string Code, string? Description);
public sealed record CreateAwardValuationRuleRequest(Guid? AwardLandClassId, string? RuleType, decimal? RateAmount, string? RateUnit, DateOnly? ReferenceDate, string? LegalSection, string? Description);
public sealed record CreateAwardCompensationRuleRequest(string? RuleType, decimal? RatePercent, decimal? RateAmount, string? LegalSection, string? BasisDescription, string? StartEvent, string? EndEvent, string? Remarks);
public sealed record CreateAwardAreaIssueRequest(Guid? KhasraId, string? IssueType, decimal? NotificationAreaBigha, decimal? FieldBookAreaBigha, decimal? DifferenceBigha, string? Status, string? CorrigendumReference, DateOnly? CorrigendumDate, string? Remarks);
public sealed record CreateAwardSupplementaryMatterRequest(string? MatterType, string? Status, string? Description, Guid? SupplementaryAwardId);
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
public sealed record CreateVillageAwardRequest(string AwardNumber, DateOnly? AwardDate, string? AwardType, string? Remarks);
public sealed record CreateMatterRequest(string Title, string? MatterType, string? Status, string? ReferenceNumber, string? Remarks, string? KhasraReferenceText, Guid? AwardId, Guid? WorkstreamId = null);
public sealed record CreateMatterDraftRequest(string Title, string DraftType);
public sealed record UpdateMatterDraftRequest(string Title, string ContentJson, string PageSize, string Orientation, decimal MarginTopMm, decimal MarginRightMm, decimal MarginBottomMm, decimal MarginLeftMm, int ExpectedRevision);
public sealed record LinkMatterDocumentRequest(Guid DocumentId, string? Role, string? DisplayName);
public sealed record ExportMatterDocumentsRequest(IReadOnlyList<Guid> DocumentIds);
public sealed record KhatauniListItem(Guid Id, string? ReferenceNumber, string? RecordYearText, DateOnly? AsOfDate, string VerificationStatus, int KhataCount, int RecordedKhasraCount) { public static readonly System.Linq.Expressions.Expression<Func<KhatauniRecord, KhatauniListItem>> Selector = x => new(x.Id, x.ReferenceNumber, x.RecordYearText, x.AsOfDate, x.VerificationStatus.ToString(), x.Khatas.Count, x.Khatas.SelectMany(k => k.KhasraLinks).Count()); }
public sealed record KhataSummary(Guid Id, string KhataNumber, int KhasraCount, int OwnerCount, string ShareValidation, bool IsVerified);
public sealed record KhatauniDetail(Guid Id, Guid VillageId, string VillageName, string? ReferenceNumber, string? RecordYearText, DateOnly? AsOfDate, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, string? Remarks, string VerificationStatus, int Version, Guid? SourceDocumentId, string? SourceDocumentName, int TotalKhatas, int TotalLinkedKhasras, int TotalRecordedParties, IReadOnlyList<KhataSummary> Khatas);
public sealed record KhataKhasraItem(Guid KhasraId, string DisplayNumber, decimal? RecordedArea, string? RawAreaText, string? AreaUnit);
public sealed record PartyShareItem(Guid Id, Guid PartyId, string DisplayName, string? RawShareText, int? ShareNumerator, int? ShareDenominator, string VerificationStatus, int Version);
public sealed record KhataDetail(Guid Id, string KhataNumber, string? RawKhataNumber, string? Remarks, Guid KhatauniRecordId, string? KhatauniReference, Guid VillageId, string VillageName, IReadOnlyList<KhataKhasraItem> Khasras, IReadOnlyList<PartyShareItem> Owners, string ShareValidation);
public sealed record OwnershipHistoryItem(Guid KhatauniRecordId, Guid KhataId, string? ReferenceNumber, string? RecordYearText, DateOnly? AsOfDate, string KhataNumber, string VerificationStatus);
public sealed record KhasraReference(Guid Id, string DisplayNumber);
public sealed record PartyHoldingItem(Guid VillageId, string VillageName, Guid KhatauniRecordId, string? KhatauniReference, Guid KhataId, string KhataNumber, IReadOnlyList<KhasraReference> Khasras, string? RawShareText, int? ShareNumerator, int? ShareDenominator);
public sealed record PartyDetail(Guid Id, string PartyType, string DisplayName, string? FatherOrSpouseName, string? AddressText, string? Remarks, int Version, IReadOnlyList<PartyHoldingItem> Holdings);
public sealed record CreateKhatauniRequest(Guid VillageId, string? ReferenceNumber, string? RecordYearText, DateOnly? AsOfDate, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, string? Remarks, RevenueRecordVerificationStatus VerificationStatus = RevenueRecordVerificationStatus.Draft);
public sealed record CreateKhataRequest(string KhataNumber, string? RawKhataNumber, string? Remarks);
public sealed record LinkKhataKhasraRequest(Guid KhasraId, string? RawKhasraText, decimal? RecordedArea, string? RawAreaText, string? AreaUnit, string? Remarks);
public sealed record CreatePartyRequest(PartyType PartyType, string DisplayName, string? FatherOrSpouseName, string? AddressText, string? Remarks);
public sealed record AddShareRequest(Guid PartyId, string? RawShareText, int? ShareNumerator, int? ShareDenominator, string? Remarks, RevenueRecordVerificationStatus VerificationStatus = RevenueRecordVerificationStatus.Draft);
public sealed record VerifyKhatauniRequest(int ExpectedVersion);
public sealed record CreateNmDocumentRequest(Guid DocumentId, Guid VillageId, Guid? AwardId, string? ReferenceNumber, DateOnly? RecordDate);
public sealed record NmKhasraRequest(string RawKhasraText, string? Qualifier, string? RawAreaText, string? RawShareText, string? SourceRegionJson);
public sealed record CreateNmRowRequest(int SourcePage, string SourceRow, string? SourceRegionJson, string? RecordedPersonText, string? FatherOrSpouseText, string? RawShareText, string? RawAreaText, decimal? EntitlementAmount, string? EntitlementBasisText, IReadOnlyList<NmKhasraRequest> Khasras);
public sealed record VerifyNmRowRequest(string VerifiedBy, int SourcePage, string SourceRow, string? SourceRegionJson, string? RecordedPersonText, string? FatherOrSpouseText, string? RawShareText, string? RawAreaText, decimal? EntitlementAmount, string? EntitlementBasisText, IReadOnlyList<NmKhasraRequest> Khasras);
public sealed record SelectNmKhasrasRequest(string Reviewer, IReadOnlyList<NmKhasraSelectionRequest> Selections);
public sealed record NmKhasraSelectionRequest(Guid ReviewKhasraId, Guid? KhasraId, bool MarkUnreadable = false);
public sealed record CommitNmRequest(string VerifiedBy);
public sealed record ReviewNmSemanticAreaRequest(string RawReviewerValue, string ReviewedBy);
public sealed record ReviewNmSemanticUnreadableRequest(string ReviewedBy);
public sealed record ReviewNmSemanticOwnerRequest(string ReviewedBy,string? Name,string? FatherOrSpouse,string? Residence,string? Share,bool SourceUnclear,IReadOnlyList<ReviewNmSemanticParcelRequest> Parcels);
public sealed record ReviewNmSemanticParcelRequest(Guid Id,string? Khasra,string? Area,string? LandClass);
