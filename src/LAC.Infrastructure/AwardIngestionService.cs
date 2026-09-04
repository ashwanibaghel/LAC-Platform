using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed class AwardIngestionException(string message, int statusCode = 400) : Exception(message) { public int StatusCode { get; } = statusCode; }

public interface IAwardDocumentExtractor { Task<AwardIngestionCandidateSet> ExtractAsync(Document sourceDocument, CancellationToken ct); }
public sealed record AwardIngestionCandidateSet(IReadOnlyList<IAwardIngestionCandidatePayload> Candidates);
public interface IAwardIngestionCandidatePayload { AwardIngestionCandidateType CandidateType { get; } }
public sealed record AwardCoreCandidate(string AwardNumber, DateOnly? AwardDate, string? AwardType, string? Purpose) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardCore; }
public sealed record NotificationCandidate(string SectionType, string NotificationNumber, DateOnly? NotificationDate) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.Notification; }
public sealed record KhasraCandidate(string KhasraNumber, string? Qualifier, decimal? CanonicalAreaBigha, int? CanonicalAreaBiswa, int? CanonicalAreaBiswansi) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.Khasra; }
public sealed record AwardKhasraCandidate(string KhasraNumber, string? Qualifier, decimal? CanonicalAreaBigha, int? CanonicalAreaBiswa, int? CanonicalAreaBiswansi, decimal? RecordedAreaBigha, int? RecordedAreaBiswa, int? RecordedAreaBiswansi, decimal? AwardedAreaBigha, int? AwardedAreaBiswa, int? AwardedAreaBiswansi) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardKhasra; }
public sealed record PossessionEventCandidate(DateOnly? PossessionDate, string? EventType, string? Status) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.PossessionEvent; }
public sealed record CourtCaseCandidate(string CaseNumber, string CourtName, string? CaseType) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.CourtCase; }
public sealed record ClaimCandidate(string? ClaimReference, DateOnly? ClaimDate, string? ClaimText) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.Claim; }
public sealed record LandClassCandidate(string Code, string? Description) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardLandClass; }
public sealed record ValuationRuleCandidate(string RuleType, decimal? RateAmount, string? LegalSection) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardValuationRule; }
public sealed record CompensationRuleCandidate(string RuleType, decimal? RatePercent, decimal? RateAmount, string? LegalSection) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardCompensationRule; }
public sealed record AreaIssueCandidate(string IssueType, decimal? NotificationAreaBigha, decimal? FieldBookAreaBigha, decimal? DifferenceBigha) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardAreaIssue; }
public sealed record SupplementaryMatterCandidate(string MatterType, string? Description) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardSupplementaryMatter; }
public sealed record IngestionSessionInput(AwardIngestionSourceType SourceType, Guid? TargetAwardId, Guid? SelectedVillageId, Guid? SourceDocumentId, string? CreatedBy, string? Remarks, IReadOnlyList<IAwardIngestionCandidatePayload> Candidates);
public sealed record IngestionCandidateInput(AwardIngestionCandidateType CandidateType, string PayloadJson, string? SourceLocatorJson = null, string? RawSourceText = null, decimal? Confidence = null);
public sealed record IngestionSessionSummary(Guid Id, AwardIngestionSourceType SourceType, AwardIngestionSessionStatus Status, Guid? TargetAwardId, Guid? SelectedVillageId, DateTimeOffset CreatedAt, DateTimeOffset? CommittedAt, IReadOnlyDictionary<string, int> Counts);
public sealed record IngestionCandidateReview(Guid Id, AwardIngestionCandidateType CandidateType, int Sequence, AwardIngestionCandidateStatus Status, string PayloadJson, Guid? CanonicalEntityId, string? CanonicalEntityType, string? ResolutionAction, string? ValidationIssuesJson, string? ConflictDetailsJson, string? SourceLocatorJson, string? RawSourceText, decimal? Confidence);
public sealed record IngestionCommitResult(int Created, int Reused, int ReviewFlagsCreated, int Skipped, int Remaining);
public sealed record IngestionPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed class AwardIngestionService(LacDbContext db, AwardWorkflowService awards)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AwardIngestionSession> CreatePreviewAsync(IngestionSessionInput input, CancellationToken ct)
    {
        if (input.TargetAwardId is not null && !await db.Awards.AnyAsync(x => x.Id == input.TargetAwardId, ct)) throw new AwardIngestionException("Target Award was not found.", 404);
        if (input.SelectedVillageId is not null && !await db.Villages.AnyAsync(x => x.Id == input.SelectedVillageId, ct)) throw new AwardIngestionException("Selected Village was not found.", 404);
        if (input.SourceDocumentId is not null && !await db.Documents.AnyAsync(x => x.Id == input.SourceDocumentId, ct)) throw new AwardIngestionException("Source Document was not found.", 404);
        if (input.TargetAwardId is not null && input.SelectedVillageId is not null && !await db.AwardVillages.AnyAsync(x => x.AwardId == input.TargetAwardId && x.VillageId == input.SelectedVillageId, ct))
            throw new AwardIngestionException("The selected Village must be directly linked to the target Award before ingestion can be staged.");
        var session = new AwardIngestionSession { SourceType = input.SourceType, TargetAwardId = input.TargetAwardId, SelectedVillageId = input.SelectedVillageId, SourceDocumentId = input.SourceDocumentId, CreatedBy = Clean(input.CreatedBy), Remarks = Clean(input.Remarks), Status = AwardIngestionSessionStatus.Parsed };
        db.AwardIngestionSessions.Add(session);
        var seen = new Dictionary<string, AwardIngestionCandidate>();
        for (var index = 0; index < input.Candidates.Count; index++)
        {
            var payload = input.Candidates[index];
            var candidate = await AnalyzeAsync(session, payload, index + 1, ct);
            var identity = BatchIdentity(payload);
            if (identity is not null && seen.TryGetValue(identity, out var prior))
            {
                candidate.Status = JsonSerializer.Serialize(payload, Json) == prior.StructuredPayloadJson ? AwardIngestionCandidateStatus.DuplicateInBatch : AwardIngestionCandidateStatus.Conflict;
                candidate.ValidationIssuesJson = "[\"Duplicate candidate in this session\"]";
            }
            else if (identity is not null) seen[identity] = candidate;
            session.Candidates.Add(candidate);
        }
        session.Status = SessionStatus(session.Candidates); await db.SaveChangesAsync(ct); return session;
    }

    public async Task<AwardIngestionSession> CreatePreviewFromJsonAsync(AwardIngestionSourceType sourceType, Guid? targetAwardId, Guid? selectedVillageId, Guid? sourceDocumentId, string? createdBy, string? remarks, IReadOnlyList<IngestionCandidateInput> inputs, CancellationToken ct)
    {
        var typed = new List<IAwardIngestionCandidatePayload>();
        foreach (var input in inputs)
        {
            try { typed.Add(DeserializeInput(input)); }
            catch (AwardIngestionException) { typed.Add(new UnsupportedCandidate(input.CandidateType)); }
        }
        var session = await CreatePreviewAsync(new(sourceType, targetAwardId, selectedVillageId, sourceDocumentId, createdBy, remarks, typed), ct);
        var saved = await db.AwardIngestionCandidates.Where(x => x.SessionId == session.Id).OrderBy(x => x.Sequence).ToListAsync(ct);
        for (var i = 0; i < saved.Count; i++) { saved[i].SourceLocatorJson = inputs[i].SourceLocatorJson; saved[i].RawSourceText = inputs[i].RawSourceText; saved[i].Confidence = inputs[i].Confidence; if (typed[i] is UnsupportedCandidate) { saved[i].Status = AwardIngestionCandidateStatus.Invalid; saved[i].ValidationIssuesJson = "[\"Unknown or malformed candidate contract.\"]"; } }
        session.Status = SessionStatus(saved); await db.SaveChangesAsync(ct); return session;
    }

    public async Task<IngestionSessionSummary> GetSummaryAsync(Guid id, CancellationToken ct)
    {
        var session = await db.AwardIngestionSessions.AsNoTracking().Include(x => x.Candidates).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AwardIngestionException("Ingestion session was not found.", 404);
        return Summary(session);
    }

    public async Task<IngestionPage<IngestionSessionSummary>> GetHistoryAsync(Guid awardId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 0); pageSize = Math.Clamp(pageSize == 0 ? 25 : pageSize, 1, 100);
        var query = db.AwardIngestionSessions.AsNoTracking().Where(x => x.TargetAwardId == awardId).OrderByDescending(x => x.CreatedAt);
        var total = await query.CountAsync(ct);
        var sessions = await query.Skip(page * pageSize).Take(pageSize).Include(x => x.Candidates).ToListAsync(ct);
        return new(sessions.Select(Summary).ToList(), page, pageSize, total);
    }

    public async Task<IngestionPage<IngestionCandidateReview>> GetCandidatesAsync(Guid id, AwardIngestionCandidateType? type, AwardIngestionCandidateStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.AwardIngestionCandidates.AsNoTracking().Where(x => x.SessionId == id);
        if (type is not null) query = query.Where(x => x.CandidateType == type);
        if (status is not null) query = query.Where(x => x.Status == status);
        return await ToPageAsync(query.OrderBy(x => x.CandidateType).ThenBy(x => x.Sequence).Select(x => new IngestionCandidateReview(x.Id, x.CandidateType, x.Sequence, x.Status, x.StructuredPayloadJson, x.CanonicalEntityId, x.CanonicalEntityType, x.ResolutionAction, x.ValidationIssuesJson, x.ConflictDetailsJson, x.SourceLocatorJson, x.RawSourceText, x.Confidence)), page, pageSize, ct);
    }

    public async Task ResolveAsync(Guid candidateId, string action, CancellationToken ct)
    {
        var candidate = await db.AwardIngestionCandidates.Include(x => x.Session).SingleOrDefaultAsync(x => x.Id == candidateId, ct) ?? throw new AwardIngestionException("Ingestion candidate was not found.", 404);
        if (candidate.Status is AwardIngestionCandidateStatus.Committed or AwardIngestionCandidateStatus.Rejected) throw new AwardIngestionException("A finalised candidate cannot be changed.");
        if (!new[] { "KeepExisting", "SkipField", "SkipCandidate", "LinkExisting", "CreateNew" }.Contains(action, StringComparer.Ordinal)) throw new AwardIngestionException("This review action is not supported.");
        candidate.ResolutionAction = action; candidate.UpdatedAt = DateTimeOffset.UtcNow;
        candidate.Status = action == "SkipCandidate" ? AwardIngestionCandidateStatus.Skipped : AwardIngestionCandidateStatus.Ready;
        candidate.Session.Status = SessionStatus(await db.AwardIngestionCandidates.Where(x => x.SessionId == candidate.SessionId).ToListAsync(ct));
        db.AuditLogs.Add(new AuditLog { EntityType = nameof(AwardIngestionCandidate), EntityId = candidate.Id, Action = $"IngestionCandidate{action}", ChangedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IngestionCommitResult> CommitAsync(Guid sessionId, IReadOnlyList<Guid> candidateIds, string? committedBy, CancellationToken ct)
    {
        // Npgsql is configured with a retrying execution strategy.  A manually-started
        // transaction must live inside that strategy or EF correctly rejects it.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
            await CommitCoreAsync(sessionId, candidateIds, committedBy, ct));
    }

    private async Task<IngestionCommitResult> CommitCoreAsync(Guid sessionId, IReadOnlyList<Guid> candidateIds, string? committedBy, CancellationToken ct)
    {
        var session = await db.AwardIngestionSessions.Include(x => x.Candidates).SingleOrDefaultAsync(x => x.Id == sessionId, ct) ?? throw new AwardIngestionException("Ingestion session was not found.", 404);
        if (session.TargetAwardId is null) throw new AwardIngestionException("Select a target Award before committing Award ingestion candidates.");
        var selected = session.Candidates.Where(x => candidateIds.Contains(x.Id) && x.Status == AwardIngestionCandidateStatus.Ready).OrderBy(x => x.CandidateType).ThenBy(x => x.Sequence).ToList();
        if (selected.Count == 0) throw new AwardIngestionException("Select one or more Ready candidates.");
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var created = 0; var reused = 0; var flags = 0; var skipped = 0;
        try
        {
            if (session.SourceDocumentId is not null && !await db.DocumentAwards.AnyAsync(x => x.DocumentId == session.SourceDocumentId && x.AwardId == session.TargetAwardId, ct))
                db.DocumentAwards.Add(new DocumentAward { DocumentId = session.SourceDocumentId.Value, AwardId = session.TargetAwardId.Value });
            foreach (var candidate in selected)
            {
                if (candidate.CandidateType == AwardIngestionCandidateType.AwardKhasra)
                {
                    if (session.SelectedVillageId is null) { candidate.Status = AwardIngestionCandidateStatus.Invalid; continue; }
                    var payload = Deserialize<AwardKhasraCandidate>(candidate);
                    var revalidated = await AnalyzeAsync(session, payload, candidate.Sequence, ct);
                    if (revalidated.Status == AwardIngestionCandidateStatus.Conflict && candidate.ResolutionAction is not "KeepExisting" and not "SkipField") { candidate.Status = AwardIngestionCandidateStatus.Conflict; candidate.ConflictDetailsJson = revalidated.ConflictDetailsJson; continue; }
                    var result = await awards.LinkKhasraAsync(session.TargetAwardId.Value, new(session.SelectedVillageId.Value, payload.KhasraNumber, payload.Qualifier, payload.RecordedAreaBigha, payload.RecordedAreaBiswa, payload.RecordedAreaBiswansi, payload.AwardedAreaBigha, payload.AwardedAreaBiswa, payload.AwardedAreaBiswansi, "Recorded", null), ct);
                    candidate.CanonicalEntityId = result.KhasraId; candidate.CanonicalEntityType = nameof(Khasra); candidate.Status = AwardIngestionCandidateStatus.Committed; candidate.UpdatedAt = DateTimeOffset.UtcNow;
                    if (result.CreatedKhasra) created++; else reused++; if (result.CreatedReviewFlag) flags++;
                }
                else if (candidate.CandidateType == AwardIngestionCandidateType.Notification)
                {
                    var payload = Deserialize<NotificationCandidate>(candidate);
                    var revalidated = await AnalyzeAsync(session, payload, candidate.Sequence, ct);
                    if (revalidated.Status is AwardIngestionCandidateStatus.Ambiguous or AwardIngestionCandidateStatus.Invalid) { candidate.Status = revalidated.Status; candidate.ValidationIssuesJson = revalidated.ValidationIssuesJson; continue; }
                    var notification = revalidated.CanonicalEntityId is Guid existingId
                        ? await db.Notifications.SingleAsync(x => x.Id == existingId, ct)
                        : new Notification { SectionType = payload.SectionType.Trim(), NotificationNumber = payload.NotificationNumber.Trim(), NotificationDate = payload.NotificationDate };
                    if (revalidated.CanonicalEntityId is null) { db.Notifications.Add(notification); created++; } else reused++;
                    if (!await db.AwardNotifications.AnyAsync(x => x.AwardId == session.TargetAwardId && x.NotificationId == notification.Id, ct)) db.AwardNotifications.Add(new AwardNotification { AwardId = session.TargetAwardId.Value, Notification = notification });
                    candidate.CanonicalEntityId = notification.Id; candidate.CanonicalEntityType = nameof(Notification); candidate.Status = AwardIngestionCandidateStatus.Committed; candidate.UpdatedAt = DateTimeOffset.UtcNow;
                }
                else { candidate.Status = AwardIngestionCandidateStatus.Skipped; skipped++; continue; }
                db.AuditLogs.Add(new AuditLog { EntityType = nameof(AwardIngestionCandidate), EntityId = candidate.Id, Action = "IngestionCandidateCommitted", ChangedAt = DateTimeOffset.UtcNow, ChangedBy = Clean(committedBy) });
            }
            var remaining = session.Candidates.Count(x => x.Status is not AwardIngestionCandidateStatus.Committed and not AwardIngestionCandidateStatus.Skipped and not AwardIngestionCandidateStatus.Rejected);
            session.Status = remaining == 0 ? AwardIngestionSessionStatus.Committed : AwardIngestionSessionStatus.PartiallyCommitted; session.CommittedAt = DateTimeOffset.UtcNow; session.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct);
            return new(created, reused, flags, skipped, remaining);
        }
        catch { if (transaction is not null) await transaction.RollbackAsync(ct); throw; }
    }

    private async Task<AwardIngestionCandidate> AnalyzeAsync(AwardIngestionSession session, IAwardIngestionCandidatePayload payload, int sequence, CancellationToken ct)
    {
        var item = new AwardIngestionCandidate { CandidateType = payload.CandidateType, Sequence = sequence, StructuredPayloadJson = JsonSerializer.Serialize(payload, payload.GetType(), Json), Status = AwardIngestionCandidateStatus.NeedsReview };
        if (payload is NotificationCandidate notification)
        {
            if (session.TargetAwardId is null) { item.Status = AwardIngestionCandidateStatus.Invalid; item.ValidationIssuesJson = "[\"Notification requires a target Award.\"]"; return item; }
            if (string.IsNullOrWhiteSpace(notification.SectionType) || string.IsNullOrWhiteSpace(notification.NotificationNumber)) { item.Status = AwardIngestionCandidateStatus.Invalid; item.ValidationIssuesJson = "[\"Notification section and number are required.\"]"; return item; }
            var matches = await db.Notifications.AsNoTracking().Where(x => x.SectionType == notification.SectionType.Trim() && x.NotificationNumber == notification.NotificationNumber.Trim() && x.NotificationDate == notification.NotificationDate).Take(2).ToListAsync(ct);
            if (matches.Count > 1) { item.Status = AwardIngestionCandidateStatus.Ambiguous; item.ValidationIssuesJson = "[\"More than one exact Notification match exists; select a canonical record manually.\"]"; return item; }
            if (matches.Count == 1) { item.CanonicalEntityId = matches[0].Id; item.CanonicalEntityType = nameof(Notification); item.ResolutionAction = await db.AwardNotifications.AnyAsync(x => x.AwardId == session.TargetAwardId && x.NotificationId == matches[0].Id, ct) ? "AlreadyLinked" : "LinkExisting"; item.Status = AwardIngestionCandidateStatus.Ready; return item; }
            item.Status = AwardIngestionCandidateStatus.Ready; item.ResolutionAction = "CreateNew"; item.ValidationIssuesJson = "[\"New canonical Notification will be linked to this Award on commit.\"]"; return item;
        }
        if (payload is not AwardKhasraCandidate khasra) { item.ValidationIssuesJson = "[\"Candidate contract is stored for future review; canonical commit is not implemented for this candidate type yet.\"]"; return item; }
        if (session.SelectedVillageId is null || session.TargetAwardId is null) { item.Status = AwardIngestionCandidateStatus.Invalid; item.ValidationIssuesJson = "[\"Award Khasra requires a selected Award and Village.\"]"; return item; }
        if (string.IsNullOrWhiteSpace(khasra.KhasraNumber)) { item.Status = AwardIngestionCandidateStatus.Invalid; item.ValidationIssuesJson = "[\"Khasra number is required.\"]"; return item; }
        var normalized = KhasraNumber.Normalize(RemoveQualifier(khasra.KhasraNumber, khasra.Qualifier));
        var existing = await db.Khasras.AsNoTracking().SingleOrDefaultAsync(x => x.VillageId == session.SelectedVillageId && x.NormalizedNumber == normalized && x.Qualifier == Clean(khasra.Qualifier), ct);
        if (existing is null) { item.Status = AwardIngestionCandidateStatus.Ready; item.ResolutionAction = "CreateNew"; item.ValidationIssuesJson = "[\"New canonical Village Khasra; review flag will be created on commit.\"]"; return item; }
        item.CanonicalEntityId = existing.Id; item.CanonicalEntityType = nameof(Khasra);
        if (khasra.CanonicalAreaBigha is not null && existing.AreaBigha is not null && khasra.CanonicalAreaBigha != existing.AreaBigha) { item.Status = AwardIngestionCandidateStatus.Conflict; item.ConflictDetailsJson = JsonSerializer.Serialize(new[] { new { field = "CanonicalAreaBigha", existingValue = existing.AreaBigha, incomingValue = khasra.CanonicalAreaBigha, conflictType = "MasterAreaConflict" } }, Json); return item; }
        var linked = await db.Set<AwardKhasra>().AnyAsync(x => x.AwardId == session.TargetAwardId && x.KhasraId == existing.Id, ct);
        item.Status = AwardIngestionCandidateStatus.Ready; item.ResolutionAction = linked ? "AlreadyLinked" : "LinkExisting"; return item;
    }

    private static T Deserialize<T>(AwardIngestionCandidate candidate) => JsonSerializer.Deserialize<T>(candidate.StructuredPayloadJson, Json) ?? throw new AwardIngestionException("Candidate payload is invalid.");
    private static IAwardIngestionCandidatePayload DeserializeInput(IngestionCandidateInput input) => (input.CandidateType switch { AwardIngestionCandidateType.AwardCore => (IAwardIngestionCandidatePayload?)JsonSerializer.Deserialize<AwardCoreCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.Notification => JsonSerializer.Deserialize<NotificationCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.Khasra => JsonSerializer.Deserialize<KhasraCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.AwardKhasra => JsonSerializer.Deserialize<AwardKhasraCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.PossessionEvent => JsonSerializer.Deserialize<PossessionEventCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.CourtCase => JsonSerializer.Deserialize<CourtCaseCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.Claim => JsonSerializer.Deserialize<ClaimCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.AwardLandClass => JsonSerializer.Deserialize<LandClassCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.AwardValuationRule => JsonSerializer.Deserialize<ValuationRuleCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.AwardCompensationRule => JsonSerializer.Deserialize<CompensationRuleCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.AwardAreaIssue => JsonSerializer.Deserialize<AreaIssueCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.AwardSupplementaryMatter => JsonSerializer.Deserialize<SupplementaryMatterCandidate>(input.PayloadJson, Json), _ => throw new AwardIngestionException("Unknown candidate contract.") }) ?? throw new AwardIngestionException("Candidate payload is invalid.");
    private static string? BatchIdentity(IAwardIngestionCandidatePayload payload) => payload switch { AwardKhasraCandidate k => $"K:{KhasraNumber.Normalize(RemoveQualifier(k.KhasraNumber, k.Qualifier))}:{Clean(k.Qualifier)}", NotificationCandidate n => $"N:{Clean(n.SectionType)}:{Clean(n.NotificationNumber)}:{n.NotificationDate:O}", _ => null };
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string RemoveQualifier(string value, string? qualifier) => qualifier is null ? value : value.EndsWith($" {qualifier}", StringComparison.OrdinalIgnoreCase) ? value[..^(qualifier.Length + 1)] : value;
    private static AwardIngestionSessionStatus SessionStatus(IEnumerable<AwardIngestionCandidate> items) => items.Any(x => x.Status is AwardIngestionCandidateStatus.Conflict or AwardIngestionCandidateStatus.Ambiguous or AwardIngestionCandidateStatus.Invalid or AwardIngestionCandidateStatus.NeedsReview or AwardIngestionCandidateStatus.DuplicateInBatch) ? AwardIngestionSessionStatus.NeedsReview : AwardIngestionSessionStatus.ReadyToCommit;
    private static IngestionSessionSummary Summary(AwardIngestionSession session) => new(session.Id, session.SourceType, session.Status, session.TargetAwardId, session.SelectedVillageId, session.CreatedAt, session.CommittedAt, session.Candidates.GroupBy(x => x.Status.ToString()).ToDictionary(x => x.Key, x => x.Count()));
    private static async Task<IngestionPage<T>> ToPageAsync<T>(IQueryable<T> query, int page, int pageSize, CancellationToken ct) { page = Math.Max(page, 0); pageSize = Math.Clamp(pageSize == 0 ? 25 : pageSize, 1, 100); var total = await query.CountAsync(ct); return new(await query.Skip(page * pageSize).Take(pageSize).ToListAsync(ct), page, pageSize, total); }
}
file sealed record UnsupportedCandidate(AwardIngestionCandidateType CandidateType) : IAwardIngestionCandidatePayload;
