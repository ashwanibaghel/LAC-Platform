using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed class AwardWorkflowException(string message, int statusCode = 400) : Exception(message) { public int StatusCode { get; } = statusCode; }
public sealed record AwardCreateInput(string AwardNumber, Guid VillageId, DateOnly? AwardDate, string? AwardType, string? ActRegime, string? Purpose, Guid? AcquisitionProjectId, string? Remarks);
public sealed record AwardKhasraInput(Guid VillageId, string KhasraNumber, string? Qualifier, decimal? RecordedTotalAreaBigha, int? RecordedTotalAreaBiswa, int? RecordedTotalAreaBiswansi, decimal? AwardedAreaBigha, int? AwardedAreaBiswa, int? AwardedAreaBiswansi, string? RelationshipStatus, string? Remarks, decimal? CanonicalAreaBigha = null, int? CanonicalAreaBiswa = null, int? CanonicalAreaBiswansi = null);
public sealed record AwardKhasraLinkResult(Guid KhasraId, bool CreatedKhasra, bool CreatedReviewFlag, bool CreatedAwardLink);
public sealed record AwardKhasraMatchResult(bool IsExisting, string? DisplayNumber, decimal? CanonicalAreaBigha, int? CanonicalAreaBiswa, int? CanonicalAreaBiswansi);

public sealed class AwardWorkflowService(LacDbContext db)
{
    public async Task<Award> CreateAsync(AwardCreateInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.AwardNumber)) throw new AwardWorkflowException("Award number is required.");
        if (!await db.Villages.AnyAsync(x => x.Id == input.VillageId, ct)) throw new AwardWorkflowException("Village was not found.", 404);
        if (input.AcquisitionProjectId is not null && !await db.AcquisitionProjects.AnyAsync(x => x.Id == input.AcquisitionProjectId, ct)) throw new AwardWorkflowException("Acquisition project was not found.", 404);
        var award = new Award { AwardNumber = input.AwardNumber.Trim(), AwardDate = input.AwardDate, AwardType = Clean(input.AwardType), ActRegime = Clean(input.ActRegime), Purpose = Clean(input.Purpose), AcquisitionProjectId = input.AcquisitionProjectId, Remarks = Clean(input.Remarks) };
        db.Add(award); db.Add(new AwardVillage { Award = award, VillageId = input.VillageId });
        db.AuditLogs.Add(new AuditLog { EntityType = nameof(Award), EntityId = award.Id, Action = "AwardCreatedWithVillage", ChangedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct); return award;
    }

    public async Task<AwardKhasraLinkResult> LinkKhasraAsync(Guid awardId, AwardKhasraInput input, CancellationToken ct)
    {
        var award = await db.Awards.SingleOrDefaultAsync(x => x.Id == awardId, ct) ?? throw new AwardWorkflowException("Award was not found.", 404);
        if (!await db.AwardVillages.AnyAsync(x => x.AwardId == awardId && x.VillageId == input.VillageId, ct)) throw new AwardWorkflowException("The selected Village is not linked to this Award.");
        if (string.IsNullOrWhiteSpace(input.KhasraNumber)) throw new AwardWorkflowException("Khasra number is required.");
        var qualifier = Clean(input.Qualifier);
        var baseNumber = KhasraNumber.Normalize(RemoveQualifier(input.KhasraNumber, qualifier));
        var display = qualifier is null || input.KhasraNumber.Contains(qualifier, StringComparison.OrdinalIgnoreCase) ? input.KhasraNumber.Trim() : $"{input.KhasraNumber.Trim()} {qualifier}";
        var khasra = await db.Khasras.SingleOrDefaultAsync(x => x.VillageId == input.VillageId && x.NormalizedNumber == baseNumber && x.Qualifier == qualifier, ct);
        var created = khasra is null;
        if (created)
        {
            khasra = new Khasra { VillageId = input.VillageId, DisplayNumber = display, NormalizedNumber = baseNumber, Qualifier = qualifier, RectangleNumber = Rectangle(baseNumber), AreaBigha = input.CanonicalAreaBigha, AreaBiswa = input.CanonicalAreaBiswa, AreaBiswansi = input.CanonicalAreaBiswansi };
            db.Add(khasra);
        }
        var link = await db.Set<AwardKhasra>().SingleOrDefaultAsync(x => x.AwardId == awardId && x.KhasraId == khasra!.Id, ct);
        var createdLink = link is null;
        if (createdLink) { link = new AwardKhasra { AwardId = awardId, Khasra = khasra! }; db.Add(link); }
        link!.RecordedTotalAreaBigha = input.RecordedTotalAreaBigha; link.RecordedTotalAreaBiswa = input.RecordedTotalAreaBiswa; link.RecordedTotalAreaBiswansi = input.RecordedTotalAreaBiswansi; link.AwardedAreaBigha = input.AwardedAreaBigha; link.AwardedAreaBiswa = input.AwardedAreaBiswa; link.AwardedAreaBiswansi = input.AwardedAreaBiswansi; link.RelationshipStatus = Clean(input.RelationshipStatus); link.Remarks = Clean(input.Remarks);
        var flagCreated = false;
        if (created)
        {
            db.Add(new KhasraReviewFlag { Khasra = khasra!, RelatedAward = award, ReasonCode = "DiscoveredFromAwardNotInVillageMaster", Status = "Open", Message = $"This Khasra was discovered while entering Award {award.AwardNumber} and was not present in the Village master data." });
            flagCreated = true;
            db.AuditLogs.Add(new AuditLog { EntityType = nameof(Khasra), EntityId = khasra!.Id, Action = "KhasraCreatedFromAwardNeedsMasterReview", ChangedAt = DateTimeOffset.UtcNow });
        }
        db.AuditLogs.Add(new AuditLog { EntityType = nameof(AwardKhasra), EntityId = link.Id, Action = createdLink ? "AwardKhasraLinked" : "AwardKhasraUpdated", ChangedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct); return new(khasra!.Id, created, flagCreated, createdLink);
    }

    public async Task<AwardKhasraMatchResult> MatchKhasraAsync(Guid awardId, Guid villageId, string khasraNumber, string? qualifier, CancellationToken ct)
    {
        if (!await db.AwardVillages.AnyAsync(x => x.AwardId == awardId && x.VillageId == villageId, ct)) throw new AwardWorkflowException("The selected Village is not linked to this Award.");
        if (string.IsNullOrWhiteSpace(khasraNumber)) throw new AwardWorkflowException("Khasra number is required.");
        var cleanQualifier = Clean(qualifier);
        var normalized = KhasraNumber.Normalize(RemoveQualifier(khasraNumber, cleanQualifier));
        var khasra = await db.Khasras.AsNoTracking().SingleOrDefaultAsync(x => x.VillageId == villageId && x.NormalizedNumber == normalized && x.Qualifier == cleanQualifier, ct);
        return khasra is null ? new(false, null, null, null, null) : new(true, khasra.DisplayNumber, khasra.AreaBigha, khasra.AreaBiswa, khasra.AreaBiswansi);
    }

    public async Task ResolveReviewFlagAsync(Guid flagId, string? resolvedBy, CancellationToken ct)
    {
        var flag = await db.KhasraReviewFlags.SingleOrDefaultAsync(x => x.Id == flagId, ct) ?? throw new AwardWorkflowException("Khasra review flag was not found.", 404);
        if (flag.Status == "Resolved") return;
        flag.Status = "Resolved"; flag.ResolvedAt = DateTimeOffset.UtcNow; flag.ResolvedBy = Clean(resolvedBy);
        db.AuditLogs.Add(new AuditLog { EntityType = nameof(KhasraReviewFlag), EntityId = flag.Id, Action = "KhasraMasterReviewResolved", ChangedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    public async Task LinkNotificationAsync(Guid awardId, Guid notificationId, CancellationToken ct)
    {
        if (!await db.Awards.AnyAsync(x => x.Id == awardId, ct)) throw new AwardWorkflowException("Award was not found.", 404);
        if (!await db.Notifications.AnyAsync(x => x.Id == notificationId, ct)) throw new AwardWorkflowException("Notification was not found.", 404);
        if (await db.AwardNotifications.AnyAsync(x => x.AwardId == awardId && x.NotificationId == notificationId, ct)) return;
        db.Add(new AwardNotification { AwardId = awardId, NotificationId = notificationId });
        db.AuditLogs.Add(new AuditLog { EntityType = nameof(AwardNotification), EntityId = awardId, Action = "AwardNotificationLinked", ChangedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    public async Task<PossessionEvent> AddPossessionAsync(Guid awardId, DateOnly? possessionDate, string? eventType, string? status, string? remarks, IReadOnlyList<Guid> khasraIds, CancellationToken ct)
    {
        if (!await db.Awards.AnyAsync(x => x.Id == awardId, ct)) throw new AwardWorkflowException("Award was not found.", 404);
        var valid = await db.Set<AwardKhasra>().Where(x => x.AwardId == awardId && khasraIds.Contains(x.KhasraId)).Select(x => x.KhasraId).ToListAsync(ct);
        if (valid.Count != khasraIds.Distinct().Count()) throw new AwardWorkflowException("Every possession Khasra must already be linked to this Award.");
        var item = new PossessionEvent { AwardId = awardId, PossessionDate = possessionDate, EventType = Clean(eventType), Status = Clean(status), Remarks = Clean(remarks) };
        db.Add(item); foreach (var khasraId in valid) db.Add(new PossessionKhasra { PossessionEvent = item, KhasraId = khasraId });
        db.AuditLogs.Add(new AuditLog { EntityType = nameof(PossessionEvent), EntityId = item.Id, Action = "PossessionEventAdded", ChangedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct); return item;
    }

    public async Task<CourtCase> CreateCourtCaseAsync(Guid awardId, string caseNumber, string courtName, string? caseType, DateOnly? filedDate, string? currentStatus, string? remarks, IReadOnlyList<Guid> khasraIds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(caseNumber) || string.IsNullOrWhiteSpace(courtName)) throw new AwardWorkflowException("Case number and court name are required.");
        if (!await db.Awards.AnyAsync(x => x.Id == awardId, ct)) throw new AwardWorkflowException("Award was not found.", 404);
        var valid = await db.Set<AwardKhasra>().Where(x => x.AwardId == awardId && khasraIds.Contains(x.KhasraId)).Select(x => x.KhasraId).ToListAsync(ct);
        if (valid.Count != khasraIds.Distinct().Count()) throw new AwardWorkflowException("Every affected Khasra must already be linked to this Award.");
        var item = new CourtCase { CaseNumber = caseNumber.Trim(), CourtName = courtName.Trim(), CaseType = Clean(caseType), FiledDate = filedDate, CurrentStatus = Clean(currentStatus), Remarks = Clean(remarks) };
        db.Add(item); db.Add(new CourtCaseAward { CourtCase = item, AwardId = awardId }); foreach (var khasraId in valid) db.Add(new CourtCaseKhasra { CourtCase = item, KhasraId = khasraId });
        db.AuditLogs.Add(new AuditLog { EntityType = nameof(CourtCase), EntityId = item.Id, Action = "CourtCaseCreatedAndLinkedToAward", ChangedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(ct); return item;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string RemoveQualifier(string value, string? qualifier) => qualifier is null ? value : value.EndsWith($" {qualifier}", StringComparison.OrdinalIgnoreCase) ? value[..^(qualifier.Length + 1)] : value;
    private static string? Rectangle(string value) { var i = value.IndexOf("//", StringComparison.Ordinal); return i > 0 ? value[..i] : null; }
}
