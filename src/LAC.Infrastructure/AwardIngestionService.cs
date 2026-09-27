using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed class AwardIngestionException(string message, int statusCode = 400) : Exception(message) { public int StatusCode { get; } = statusCode; }

public interface IAwardDocumentExtractor { Task<AwardIngestionCandidateSet> ExtractAsync(Document sourceDocument, CancellationToken ct); }
public sealed record AwardIngestionCandidateSet(IReadOnlyList<IAwardIngestionCandidatePayload> Candidates);
public interface IAwardIngestionCandidatePayload { AwardIngestionCandidateType CandidateType { get; } }
// These document fields are review suggestions.  The existing Award entity only
// receives its supported core columns during the explicit Commit workflow; the
// additional source facts remain evidence for a human to assess.
public sealed record AwardCoreCandidate(string AwardNumber, DateOnly? AwardDate, string? AwardType, string? Purpose, string? NatureOfAcquisition = null, string? AwardedAreaText = null, string? ParentAwardReferenceSuggestion = null) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardCore; }
public sealed record AwardVillageCandidate(string VillageName, string? ExactCanonicalVillageName) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardVillage; }
public sealed record NotificationCandidate(string SectionType, string NotificationNumber, DateOnly? NotificationDate) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.Notification; }
public sealed record KhasraCandidate(string KhasraNumber, string? Qualifier, decimal? CanonicalAreaBigha, int? CanonicalAreaBiswa, int? CanonicalAreaBiswansi) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.Khasra; }
public sealed record AwardKhasraCandidate(string KhasraNumber, string? Qualifier, decimal? CanonicalAreaBigha, int? CanonicalAreaBiswa, int? CanonicalAreaBiswansi, decimal? RecordedAreaBigha, int? RecordedAreaBiswa, int? RecordedAreaBiswansi, decimal? AwardedAreaBigha, int? AwardedAreaBiswa, int? AwardedAreaBiswansi) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardKhasra; }
// Possession is source-event evidence, not an Award-level boolean or date.  The
// raw area and parcel references deliberately remain review text until a human
// chooses any canonical PossessionKhasra relationships.
public sealed record PossessionEventCandidate(DateOnly? PossessionDate, string? EventType, string? Status, string? PossessionAreaText = null, string? PossessionAreaUnit = null, string? KhasraReferences = null) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.PossessionEvent; }
// A court reference is deliberately narrower than its possible legal effect.
// Khasra and area values remain source references until a reviewer explicitly
// resolves any canonical CourtCaseKhasra relationship during a later workflow.
public sealed record CourtCaseCandidate(string CaseNumber, string? CourtName, string? CaseType, string? Status = null, string? KhasraReferences = null, string? RelatedAreaText = null, string? Parties = null) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.CourtCase; }
public sealed record ClaimCandidate(string? ClaimReference, DateOnly? ClaimDate, string? ClaimText) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.Claim; }
public sealed record LandClassCandidate(string Code, string? Description) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardLandClass; }
public sealed record ValuationRuleCandidate(string RuleType, decimal? RateAmount, string? LegalSection, string? RateUnit = null) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardValuationRule; }
public sealed record CompensationRuleCandidate(string RuleType, decimal? RatePercent, decimal? RateAmount, string? LegalSection) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardCompensationRule; }
public sealed record AreaIssueCandidate(string IssueType, decimal? NotificationAreaBigha, decimal? FieldBookAreaBigha, decimal? DifferenceBigha) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardAreaIssue; }
public sealed record SupplementaryMatterCandidate(string MatterType, string? Description) : IAwardIngestionCandidatePayload { public AwardIngestionCandidateType CandidateType => AwardIngestionCandidateType.AwardSupplementaryMatter; }
public sealed record IngestionSessionInput(AwardIngestionSourceType SourceType, Guid? TargetAwardId, Guid? SelectedVillageId, Guid? SourceDocumentId, string? CreatedBy, string? Remarks, IReadOnlyList<IAwardIngestionCandidatePayload> Candidates);
public sealed record IngestionCandidateInput(AwardIngestionCandidateType CandidateType, string PayloadJson, string? SourceLocatorJson = null, string? RawSourceText = null, decimal? Confidence = null);
public sealed record IngestionSessionSummary(Guid Id, AwardIngestionSourceType SourceType, AwardIngestionSessionStatus Status, Guid? SourceDocumentId, Guid? TargetAwardId, Guid? SelectedVillageId, DateTimeOffset CreatedAt, DateTimeOffset? CommittedAt, IReadOnlyDictionary<string, int> Counts);
public sealed record IngestionCandidateReview(Guid Id, AwardIngestionCandidateType CandidateType, int Sequence, AwardIngestionCandidateStatus Status, string PayloadJson, Guid? CanonicalEntityId, string? CanonicalEntityType, string? ResolutionAction, string? ValidationIssuesJson, string? ConflictDetailsJson, string? SourceLocatorJson, string? RawSourceText, decimal? Confidence, bool SafeToConfirm = false, int? SourcePage = null, DateTimeOffset? VerifiedAt = null, string? VerifiedBy = null, string? FieldReviewJson = null);
public sealed record IngestionCommitResult(int Created, int Reused, int ReviewFlagsCreated, int Skipped, int Remaining);
public sealed record IngestionPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed partial class AwardIngestionService(LacDbContext db, AwardWorkflowService awards)
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
            if (identity is not null && seen.TryGetValue(identity, out var prior) && !(input.SourceType == AwardIngestionSourceType.Document && payload is AwardKhasraCandidate))
            {
                candidate.Status = JsonSerializer.Serialize(payload, payload.GetType(), Json) == prior.StructuredPayloadJson ? AwardIngestionCandidateStatus.DuplicateInBatch : AwardIngestionCandidateStatus.Conflict;
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
        for (var i = 0; i < saved.Count; i++) { saved[i].SourceLocatorJson = inputs[i].SourceLocatorJson; saved[i].RawSourceText = inputs[i].RawSourceText; saved[i].Confidence = inputs[i].Confidence; if (typed[i] is UnsupportedCandidate) { saved[i].Status = AwardIngestionCandidateStatus.Invalid; saved[i].ValidationIssuesJson = "[\"Unknown or malformed candidate contract.\"]"; } else if (saved[i].Status == AwardIngestionCandidateStatus.Ready && EvidenceRequiresReview(inputs[i].SourceLocatorJson, out var reason)) { saved[i].Status = AwardIngestionCandidateStatus.NeedsReview; saved[i].ValidationIssuesJson = JsonSerializer.Serialize(new[] { reason }, Json); } }
        foreach(var candidate in saved) await RefreshEvidenceMetadataAsync(candidate,ct);
        if (sourceType == AwardIngestionSourceType.Document) ClassifyAwardSourceOccurrences(saved);
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

    public async Task<IngestionPage<IngestionCandidateReview>> GetCandidatesAsync(Guid id, AwardIngestionCandidateType? type, AwardIngestionCandidateStatus? status, int page, int pageSize, CancellationToken ct, string? bucket = null, int? sourcePage = null)
    {
        var query = db.AwardIngestionCandidates.AsNoTracking().Where(x => x.SessionId == id);
        if (type is not null) query = query.Where(x => x.CandidateType == type);
        if (status is not null) query = query.Where(x => x.Status == status);
        if(sourcePage is not null) query=query.Where(x=>x.SourcePage==sourcePage);
        query=bucket switch {
            "exact"=>query.Where(x=>x.SafeToConfirm && x.VerifiedAt==null && x.Status==AwardIngestionCandidateStatus.Ready),
            "attention"=>query.Attention(),
            "conflict"=>query.Where(x=>x.Status==AwardIngestionCandidateStatus.Conflict || x.Status==AwardIngestionCandidateStatus.Ambiguous || x.Status==AwardIngestionCandidateStatus.DuplicateInBatch),
            "unreadable"=>query.Where(x=>x.Status==AwardIngestionCandidateStatus.Invalid),
            "verified"=>query.VerifiedWaiting(),
            "committed"=>query.Committed(),
            _=>query};
        return await ToPageAsync(query.OrderBy(x => x.SourcePage).ThenBy(x => x.Sequence).Select(x => new IngestionCandidateReview(x.Id, x.CandidateType, x.Sequence, x.Status, x.StructuredPayloadJson, x.CanonicalEntityId, x.CanonicalEntityType, x.ResolutionAction, x.ValidationIssuesJson, x.ConflictDetailsJson, x.SourceLocatorJson, x.RawSourceText, x.Confidence,x.SafeToConfirm,x.SourcePage,x.VerifiedAt,x.VerifiedBy,x.FieldReviewJson)), page, pageSize, ct);
    }

    public async Task ResolveAsync(Guid candidateId, string action, CancellationToken ct)
    {
        var candidate = await db.AwardIngestionCandidates.Include(x => x.Session).SingleOrDefaultAsync(x => x.Id == candidateId, ct) ?? throw new AwardIngestionException("Ingestion candidate was not found.", 404);
        if (candidate.Session.SourceDocumentId is not null && action != "SkipCandidate") throw new AwardIngestionException("Use the human verification workflow to confirm document evidence.");
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
        if (session.SourceDocumentId is not null && selected.Any(c => c.VerifiedAt is null || c.VerifiedPayloadJson != c.StructuredPayloadJson)) throw new AwardIngestionException("Every selected document fact must be human verified before commit.");
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
                    var currentLink = revalidated.CanonicalEntityId is Guid currentId ? await db.Set<AwardKhasra>().SingleOrDefaultAsync(x => x.AwardId == session.TargetAwardId && x.KhasraId == currentId, ct) : null;
                    if (currentLink is not null && AwardAreasDisagree(currentLink, payload)) { candidate.Status = AwardIngestionCandidateStatus.Conflict; candidate.ValidationIssuesJson = "[\"Existing Award areas differ; verified facts were not overwritten.\"]"; continue; }
                    var result = await awards.LinkKhasraAsync(session.TargetAwardId.Value, new(session.SelectedVillageId.Value, payload.KhasraNumber, payload.Qualifier, payload.RecordedAreaBigha ?? currentLink?.RecordedTotalAreaBigha, payload.RecordedAreaBiswa ?? currentLink?.RecordedTotalAreaBiswa, payload.RecordedAreaBiswansi ?? currentLink?.RecordedTotalAreaBiswansi, payload.AwardedAreaBigha ?? currentLink?.AwardedAreaBigha, payload.AwardedAreaBiswa ?? currentLink?.AwardedAreaBiswa, payload.AwardedAreaBiswansi ?? currentLink?.AwardedAreaBiswansi, currentLink?.RelationshipStatus ?? "Recorded", currentLink?.Remarks, payload.CanonicalAreaBigha, payload.CanonicalAreaBiswa, payload.CanonicalAreaBiswansi), ct);
                    candidate.CanonicalEntityId = result.KhasraId; candidate.CanonicalEntityType = nameof(Khasra); candidate.Status = AwardIngestionCandidateStatus.Committed; candidate.UpdatedAt = DateTimeOffset.UtcNow;
                    if (result.CreatedKhasra) created++; else reused++; if (result.CreatedReviewFlag) flags++;
                }
                else if (candidate.CandidateType == AwardIngestionCandidateType.Notification)
                {
                    var payload = Deserialize<NotificationCandidate>(candidate);
                    var revalidated = await AnalyzeAsync(session, payload, candidate.Sequence, ct);
                    if (revalidated.Status is AwardIngestionCandidateStatus.Ambiguous or AwardIngestionCandidateStatus.Invalid or AwardIngestionCandidateStatus.Conflict) { candidate.Status = revalidated.Status; candidate.ValidationIssuesJson = revalidated.ValidationIssuesJson; continue; }
                    var notification = revalidated.CanonicalEntityId is Guid existingId
                        ? await db.Notifications.SingleAsync(x => x.Id == existingId, ct)
                        : new Notification { SectionType = payload.SectionType.Trim(), NotificationNumber = payload.NotificationNumber.Trim(), NotificationDate = payload.NotificationDate };
                    if (revalidated.CanonicalEntityId is null) { db.Notifications.Add(notification); created++; } else reused++;
                    if (!await db.AwardNotifications.AnyAsync(x => x.AwardId == session.TargetAwardId && x.NotificationId == notification.Id, ct)) db.AwardNotifications.Add(new AwardNotification { AwardId = session.TargetAwardId.Value, Notification = notification });
                    candidate.CanonicalEntityId = notification.Id; candidate.CanonicalEntityType = nameof(Notification); candidate.Status = AwardIngestionCandidateStatus.Committed; candidate.UpdatedAt = DateTimeOffset.UtcNow;
                }
                else if (session.SourceDocumentId is not null && await CommitVerifiedRelatedAsync(session, candidate, ct)) { created++; }
                else { candidate.Status = AwardIngestionCandidateStatus.Skipped; skipped++; continue; }
                if (session.SourceDocumentId is not null) await SavePermanentEvidenceAsync(session, candidate, ct);
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
        if (payload is AwardCoreCandidate core && session.TargetAwardId is Guid targetAwardId)
        {
            var target = await db.Awards.AsNoTracking().SingleAsync(x => x.Id == targetAwardId, ct);
            if (!string.IsNullOrWhiteSpace(core.AwardNumber) && !string.Equals(target.AwardNumber?.Trim(), core.AwardNumber.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                item.Status = AwardIngestionCandidateStatus.Conflict;
                item.ValidationIssuesJson = "[\"Document Award number differs from the current Award context. It was not changed automatically.\"]";
                item.ConflictDetailsJson = JsonSerializer.Serialize(new { field = "AwardNumber", currentAward = target.AwardNumber, documentSuggestion = core.AwardNumber }, Json);
                return item;
            }
        }
        if (payload is NotificationCandidate notification)
        {
            if (session.TargetAwardId is null) { item.Status = AwardIngestionCandidateStatus.NeedsReview; item.ValidationIssuesJson = "[\"Pending context: select a target Award before reviewing this Notification.\"]"; return item; }
            if (string.IsNullOrWhiteSpace(notification.SectionType) || string.IsNullOrWhiteSpace(notification.NotificationNumber)) { item.Status = AwardIngestionCandidateStatus.Invalid; item.ValidationIssuesJson = "[\"Notification section and number are required.\"]"; return item; }
            if (await db.Notifications.AnyAsync(x=>x.SectionType==notification.SectionType.Trim() && x.NotificationNumber==notification.NotificationNumber.Trim() && x.NotificationDate!=notification.NotificationDate && db.SourceEvidence.Any(e=>e.NotificationId==x.Id),ct)) {item.Status=AwardIngestionCandidateStatus.Conflict;item.ValidationIssuesJson="[\"A verified Notification with this reference has a different date. Existing evidence is retained.\"]";return item;}
            var matches = await db.Notifications.AsNoTracking().Where(x => x.SectionType == notification.SectionType.Trim() && x.NotificationNumber == notification.NotificationNumber.Trim() && x.NotificationDate == notification.NotificationDate).Take(2).ToListAsync(ct);
            if (matches.Count > 1) { item.Status = AwardIngestionCandidateStatus.Ambiguous; item.ValidationIssuesJson = "[\"More than one exact Notification match exists; select a canonical record manually.\"]"; return item; }
            if (matches.Count == 1) { item.CanonicalEntityId = matches[0].Id; item.CanonicalEntityType = nameof(Notification); item.ResolutionAction = await db.AwardNotifications.AnyAsync(x => x.AwardId == session.TargetAwardId && x.NotificationId == matches[0].Id, ct) ? "AlreadyLinked" : "LinkExisting"; item.Status = AwardIngestionCandidateStatus.Ready; return item; }
            item.Status = AwardIngestionCandidateStatus.Ready; item.ResolutionAction = "CreateNew"; item.ValidationIssuesJson = "[\"New canonical Notification will be linked to this Award on commit.\"]"; return item;
        }
        if (payload is AwardVillageCandidate village) { item.Status = AwardIngestionCandidateStatus.NeedsReview; item.ValidationIssuesJson = JsonSerializer.Serialize(new[] { village.ExactCanonicalVillageName is null ? "Village requires human confirmation; no exact official master match was found." : "Exact official Village master match found; confirm before linking it to the Award." }, Json); return item; }
        if (payload is not AwardKhasraCandidate khasra) { item.ValidationIssuesJson = "[\"Candidate contract is stored for future review; canonical commit is not implemented for this candidate type yet.\"]"; return item; }
        if (session.SelectedVillageId is null || session.TargetAwardId is null) { item.Status = AwardIngestionCandidateStatus.NeedsReview; item.ValidationIssuesJson = "[\"Pending context: select an Award and Village before reviewing this Khasra.\"]"; return item; }
        if (string.IsNullOrWhiteSpace(khasra.KhasraNumber)) { item.Status = AwardIngestionCandidateStatus.Invalid; item.ValidationIssuesJson = "[\"Khasra number is required.\"]"; return item; }
        var normalized = KhasraNumber.Normalize(RemoveQualifier(khasra.KhasraNumber, khasra.Qualifier));
        var existing = await db.Khasras.AsNoTracking().SingleOrDefaultAsync(x => x.VillageId == session.SelectedVillageId && x.NormalizedNumber == normalized && x.Qualifier == Clean(khasra.Qualifier), ct);
        if (existing is null) { item.Status = AwardIngestionCandidateStatus.Ready; item.ResolutionAction = "CreateNew"; item.ValidationIssuesJson = "[\"New canonical Village Khasra; review flag will be created on commit.\"]"; return item; }
        item.CanonicalEntityId = existing.Id; item.CanonicalEntityType = nameof(Khasra);
        if (khasra.CanonicalAreaBigha is not null && existing.AreaBigha is not null && khasra.CanonicalAreaBigha != existing.AreaBigha) { item.Status = AwardIngestionCandidateStatus.Conflict; item.ConflictDetailsJson = JsonSerializer.Serialize(new[] { new { field = "CanonicalAreaBigha", existingValue = existing.AreaBigha, incomingValue = khasra.CanonicalAreaBigha, conflictType = "MasterAreaConflict" } }, Json); return item; }
        var link = await db.Set<AwardKhasra>().AsNoTracking().SingleOrDefaultAsync(x => x.AwardId == session.TargetAwardId && x.KhasraId == existing.Id, ct);
        if (link is not null && AwardAreasDisagree(link, khasra))
        {
            item.Status = AwardIngestionCandidateStatus.Conflict;
            item.ValidationIssuesJson = "[\"Existing Award areas differ. Compare the existing and new sources; verified values have not changed.\"]";
            item.ConflictDetailsJson = JsonSerializer.Serialize(new { existing = new { link.RecordedTotalAreaBigha, link.RecordedTotalAreaBiswa, link.RecordedTotalAreaBiswansi, link.AwardedAreaBigha, link.AwardedAreaBiswa, link.AwardedAreaBiswansi }, incoming = khasra }, Json);
            return item;
        }
        item.Status = AwardIngestionCandidateStatus.Ready; item.ResolutionAction = link is not null ? "AlreadyLinked" : "LinkExisting"; return item;
    }

    private static T Deserialize<T>(AwardIngestionCandidate candidate) => JsonSerializer.Deserialize<T>(candidate.StructuredPayloadJson, Json) ?? throw new AwardIngestionException("Candidate payload is invalid.");
    private static IAwardIngestionCandidatePayload DeserializeInput(IngestionCandidateInput input) => (input.CandidateType switch { AwardIngestionCandidateType.AwardCore => (IAwardIngestionCandidatePayload?)JsonSerializer.Deserialize<AwardCoreCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.AwardVillage => JsonSerializer.Deserialize<AwardVillageCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.Notification => JsonSerializer.Deserialize<NotificationCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.Khasra => JsonSerializer.Deserialize<KhasraCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.AwardKhasra => JsonSerializer.Deserialize<AwardKhasraCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.PossessionEvent => JsonSerializer.Deserialize<PossessionEventCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.CourtCase => JsonSerializer.Deserialize<CourtCaseCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.Claim => JsonSerializer.Deserialize<ClaimCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.AwardLandClass => JsonSerializer.Deserialize<LandClassCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.AwardValuationRule => JsonSerializer.Deserialize<ValuationRuleCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.AwardCompensationRule => JsonSerializer.Deserialize<CompensationRuleCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.AwardAreaIssue => JsonSerializer.Deserialize<AreaIssueCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.AwardSupplementaryMatter => JsonSerializer.Deserialize<SupplementaryMatterCandidate>(input.PayloadJson, Json), AwardIngestionCandidateType.UnmappedAwardFinding => JsonSerializer.Deserialize<UnmappedAwardFindingCandidate>(input.PayloadJson, Json), _ => throw new AwardIngestionException("Unknown candidate contract.") }) ?? throw new AwardIngestionException("Candidate payload is invalid.");
    private static string? BatchIdentity(IAwardIngestionCandidatePayload payload) => payload switch { AwardKhasraCandidate k => $"K:{KhasraNumber.Normalize(RemoveQualifier(k.KhasraNumber, k.Qualifier))}:{Clean(k.Qualifier)}", NotificationCandidate n => $"N:{Clean(n.SectionType)}:{Clean(n.NotificationNumber)}:{n.NotificationDate:O}", _ => null };
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void ClassifyAwardSourceOccurrences(IReadOnlyList<AwardIngestionCandidate> candidates)
    {
        foreach (var group in candidates.Where(x => x.CandidateType == AwardIngestionCandidateType.AwardKhasra).GroupBy(x => BatchIdentity(Deserialize<AwardKhasraCandidate>(x))))
        {
            var occurrences = group.ToList();
            if (occurrences.Count < 2) continue;
            var sameValues = occurrences.Select(x => x.StructuredPayloadJson).Distinct().Count() == 1;
            var code = sameValues ? "RepeatedSameValues" : "RepeatedDifferentAreas";
            var message = sameValues
                ? "Repeated in source. This Award contains the same Khasra and areas more than once; each source occurrence remains separately reviewable."
                : "Same Khasra has multiple area entries in the source. Each occurrence remains separate; do not sum or choose an area automatically.";
            var summary = JsonSerializer.Serialize(new
            {
                code,
                occurrences = occurrences.Select(row =>
                {
                    var value = Deserialize<AwardKhasraCandidate>(row);
                    return new { row.Id, row.SourcePage, recordedArea = FormatArea(value.RecordedAreaBigha, value.RecordedAreaBiswa, value.RecordedAreaBiswansi), awardedArea = FormatArea(value.AwardedAreaBigha, value.AwardedAreaBiswa, value.AwardedAreaBiswansi) };
                })
            }, Json);
            foreach (var candidate in occurrences)
            {
                // Master/Award conflicts were produced by domain validation and
                // remain distinct from repeated PDF source evidence.
                if (candidate.ConflictDetailsJson is not null) continue;
                candidate.Status = AwardIngestionCandidateStatus.NeedsReview;
                candidate.SafeToConfirm = false;
                candidate.ValidationIssuesJson = JsonSerializer.Serialize(new[] { message }, Json);
                candidate.ConflictDetailsJson = summary;
            }
        }
    }
    private static bool EvidenceRequiresReview(string? locator, out string reason)
    {
        reason = ""; if (string.IsNullOrWhiteSpace(locator)) return false;
        try
        {
            using var document = JsonDocument.Parse(locator); var root = document.RootElement;
            // A worker which supplies field-cell identities must prove that an
            // Award Khasra's fields came from one physical table/group/row.
            // Older non-geometry evidence remains reviewable, but malformed
            // new geometry is never silently treated as a coherent row.
            if ((root.TryGetProperty("StructuredPayload", out var payload) || root.TryGetProperty("structuredPayload", out payload)) &&
                payload.TryGetProperty("sourceCells", out var cells) && !SameSourceRow(cells))
            {
                reason = "Award Khasra source cells do not prove one table, logical group, and row; review is required.";
                return true;
            }
            if (!root.TryGetProperty("Warnings", out var warnings) && !root.TryGetProperty("warnings", out warnings)) return false;
            var values = warnings.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (values.Count == 0) return false; reason = string.Join(" ", values!); return true;
        }
        catch (JsonException) { return false; }
    }
    private static bool SameSourceRow(JsonElement cells)
    {
        var expected = new int?[3]; var hasIdentity = false;
        foreach (var name in new[] { "khasra", "recordedArea", "awardedArea" })
        {
            if (!cells.TryGetProperty(name, out var cell)) continue;
            var identity = new[] { "tableId", "logicalGroupId", "rowId" }
                .Select(property => cell.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : (int?)null).ToArray();
            if (identity.All(value => value is null)) continue; // pre-v1 geometry evidence
            hasIdentity = true;
            if (identity.Any(value => value is null)) return false;
            for (var index = 0; index < identity.Length; index++)
            {
                if (expected[index] is null) expected[index] = identity[index];
                else if (expected[index] != identity[index]) return false;
            }
        }
        return !hasIdentity || expected.All(value => value is not null);
    }
    private static string RemoveQualifier(string value, string? qualifier) => qualifier is null ? value : value.EndsWith($" {qualifier}", StringComparison.OrdinalIgnoreCase) ? value[..^(qualifier.Length + 1)] : value;
    private static AwardIngestionSessionStatus SessionStatus(IEnumerable<AwardIngestionCandidate> items) => items.Any(x => x.Status is AwardIngestionCandidateStatus.Conflict or AwardIngestionCandidateStatus.Ambiguous or AwardIngestionCandidateStatus.Invalid or AwardIngestionCandidateStatus.NeedsReview or AwardIngestionCandidateStatus.DuplicateInBatch) ? AwardIngestionSessionStatus.NeedsReview : AwardIngestionSessionStatus.ReadyToCommit;
    private static IngestionSessionSummary Summary(AwardIngestionSession session) => new(session.Id, session.SourceType, session.Status, session.SourceDocumentId, session.TargetAwardId, session.SelectedVillageId, session.CreatedAt, session.CommittedAt, session.Candidates.GroupBy(x => x.Status.ToString()).ToDictionary(x => x.Key, x => x.Count()));
    private static async Task<IngestionPage<T>> ToPageAsync<T>(IQueryable<T> query, int page, int pageSize, CancellationToken ct) { page = Math.Max(page, 0); pageSize = Math.Clamp(pageSize == 0 ? 25 : pageSize, 1, 100); var total = await query.CountAsync(ct); return new(await query.Skip(page * pageSize).Take(pageSize).ToListAsync(ct), page, pageSize, total); }
}
file sealed record UnsupportedCandidate(AwardIngestionCandidateType CandidateType) : IAwardIngestionCandidatePayload;
