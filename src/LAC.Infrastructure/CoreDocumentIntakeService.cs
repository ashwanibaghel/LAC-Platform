using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed class CoreIntakeException(string message, int statusCode = 400, string code = "InvalidIntake", object? conflicts = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public object? Conflicts { get; } = conflicts;
}
public sealed record CoreAwardCreation(string AwardNumber, DateOnly? AwardDate = null, string? AwardType = null, bool Confirmed = false, string? ActRegime = null, Guid? AcquisitionProjectId = null);
public sealed record CoreIntakeConfirmation(string DocumentRole, Guid? AwardId = null, CoreAwardCreation? CreateAward = null,
    DateOnly? LegalDate = null, string? ReferenceNumber = null, string? PartNumber = null, string? Title = null, string? Remarks = null,
    IReadOnlyList<Guid>? KhasraIds = null, int? ExpectedRevision = null);
public sealed record CoreIntakeProposal(CoreClassification Classification, string MatchState, Guid? MatchedAwardId,
    string? MatchedAwardNumber, IReadOnlyList<CoreIntakeAlternative> Alternatives);
public sealed record CoreIntakeView(Guid IntakeId, Guid VillageId, Guid DocumentId, string FileName, string Status,
    string DetectedRole, string? DetectedAwardNumber, DateOnly? DetectedAwardDate, string? DetectedVillage,
    string? DetectedAwardType, decimal Confidence, string MatchState, Guid? MatchedAwardId, string? MatchedAwardNumber,
    IReadOnlyList<CoreIntakeEvidence> Evidence, IReadOnlyList<CoreIntakeAlternative> Alternatives,
    DateTimeOffset CreatedAt, Guid? ConfirmedAwardId, string? ConfirmedRole, Guid? ConfirmedDocumentId,
    DateTimeOffset? ConfirmedAt, string ViewRoute, string DownloadRoute, bool IsDuplicate = false, int Revision = 0, string? AttachmentStatus = null);

/// <summary>Bounded stripes serialize local mutations without retaining one semaphore per Village forever.</summary>
public sealed class CoreIntakeGate
{
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    public SemaphoreSlim For(Guid villageId) => gates[(villageId.GetHashCode() & int.MaxValue) % gates.Length];
}

public sealed class CoreDocumentIntakeService(LacDbContext db, IDocumentStorage storage, ICoreDocumentTextReader reader,
    ICoreDocumentClassifier classifier, CoreIntakeGate gate, IAccessControlService access, ICurrentUserContext user, LandRecordsStaffWorkflowService staff)
{
    public const long MaxFileBytes = 50 * 1024 * 1024;
    public const int MaxBatchFiles = 20;


    private readonly LandRecordsMutationStore mutations = new(db);
    private string Actor => user.UserId?.ToString() ?? throw new CoreIntakeException("Authentication is required.", 401, "Unauthenticated");
    private async Task AuthorizeAsync(string permission, CancellationToken ct, string? owner = null, bool record = false)
    {
        _ = Actor;
        Guid? ownerId = Guid.TryParse(owner, out var id) ? id : null;
        if (ownerId is null && owner is not null)
            ownerId = await db.AppUsers.Where(x => x.Username == owner).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (!await access.CanAsync(permission, new(WorkstreamCode: WorkstreamCodes.Award, OwnerUserId: record ? ownerId : ownerId ?? user.UserId), ct))
            throw new CoreIntakeException("You do not have live permission for this action.", 403, "Forbidden");
    }
    private async Task AuthorizeReadAsync(CoreDocumentIntake intake, CancellationToken ct)
    {
        if (await db.DocumentAwards.AnyAsync(x => x.DocumentId == intake.DocumentId, ct) ||
            await db.DocumentNotifications.AnyAsync(x => x.DocumentId == intake.DocumentId, ct) ||
            await db.Set<TemporaryAcquisitionProceeding>().AnyAsync(x => x.DocumentId == intake.DocumentId, ct))
            await staff.AuthorizeDocumentReadAsync(intake.DocumentId, ct);
        else await AuthorizeAsync(PermissionCodes.AwardView, ct, intake.CreatedBy, record: true);
    }
    private static CoreIntakeException Convert(StaffWorkflowException ex) => new(ex.Message, ex.StatusCode, ex.Code, ex.Conflicts);

    public async Task<CoreIntakeView> StageAsync(Guid villageId, Stream source, string fileName, CancellationToken ct)
    {
        await AuthorizeAsync(PermissionCodes.AwardCoreDocumentUpload, ct);
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 180 || safeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new CoreIntakeException("Provide a valid PDF filename of at most 180 characters.");
        var semaphore = gate.For(villageId); await semaphore.WaitAsync(ct);
        try
        {
            await using var pdf = await PreparedLandRecordPdf.ReadAsync(source, fileName, Math.Min(MaxFileBytes, staff.MaxPdfBytes), ct);
            var files = new LandRecordPdfStore(db, storage);
            var staged = await mutations.ExecuteAsync([$"pdf:{pdf.Hash}", $"intake-village:{villageId}"], null, null, new { villageId, pdf.Hash }, async () =>
            {
                var village = await db.Villages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == villageId && x.RecordStatus == RecordStatus.Active, ct)
                    ?? throw new CoreIntakeException("Village was not found.", 404);
                var duplicate = await db.CoreDocumentIntakes.Include(x => x.Document).SingleOrDefaultAsync(x => x.VillageId == villageId && x.Sha256Hash.ToLower() == pdf.Hash, ct);
                if (duplicate is not null) { await AuthorizeReadAsync(duplicate, ct); return (Intake: duplicate, Duplicate: true, Village: village); }
                var document = await files.GetOrStoreAsync(pdf, safeName, "Unclassified Core PDF", Actor, ct);
                await staff.EnsureIntakeSourceReadAsync(document.Id, ct);
                if (files.NewStoragePath is not null) document.Status = "Staged";
                var intake = new CoreDocumentIntake { VillageId = villageId, Document = document, Sha256Hash = pdf.Hash, CreatedBy = Actor, ClassifierVersion = CoreDocumentClassifier.Version };
                db.Add(intake); await db.SaveChangesAsync(ct);
                return (Intake: intake, Duplicate: false, Village: village);
            }, ct, files.CleanupAsync);
            var pending = staged.Intake;
            if (pending.Status != "Staged") return View(pending, staged.Duplicate);
            IReadOnlyList<CoreSourcePage> pages = []; CoreClassification classification;
            try
            {
                await using var stored = await storage.OpenReadAsync(pending.Document.StoragePath, ct) ?? throw new IOException("Stored PDF is unavailable.");
                pages = await reader.ReadAsync(stored, ct); classification = classifier.Classify(pages);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                classification = new("Unknown", null, null, null, null, 0m, true,
                    [new("extraction", 0, "Native PDF text could not be read; inspect the original PDF.")],
                    [new("extraction", ex.GetType().Name, Reason: "Local extraction failed. Officer review is required.")]);
            }
            var proposal = await MatchAsync(staged.Village, classification, ct);
            return await mutations.ExecuteAsync([$"intake:{pending.Id}"], null, null, proposal, async () =>
            {
                await db.Entry(pending).ReloadAsync(ct);
                if (pending.Status == "Staged")
                {
                    pending.SourcePagesJson = JsonSerializer.Serialize(pages); pending.ProposalJson = JsonSerializer.Serialize(proposal);
                    pending.ClassifierVersion = CoreDocumentClassifier.Version;
                    pending.Status = "NeedsConfirmation"; pending.Revision++; await db.SaveChangesAsync(ct);
                }
                return View(pending, staged.Duplicate);
            }, ct);
        }
        catch (StaffWorkflowException ex) { throw Convert(ex); }
        finally { semaphore.Release(); }
    }

    public async Task<CoreIntakeView> GetAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var intake = await db.CoreDocumentIntakes.AsNoTracking().Include(x => x.Document).SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new CoreIntakeException("Intake was not found.", 404);
            await AuthorizeReadAsync(intake, ct); return View(intake);
        }
        catch (StaffWorkflowException ex) { throw Convert(ex); }
    }
    public async Task<IReadOnlyList<CoreIntakeView>> ListAsync(Guid villageId, int skip, int take, CancellationToken ct)
    {
        await AuthorizeAsync(PermissionCodes.AwardView, ct);
        if (!await db.Villages.AnyAsync(x => x.Id == villageId, ct)) throw new CoreIntakeException("Village was not found.", 404);
        var items = await db.CoreDocumentIntakes.AsNoTracking().Include(x => x.Document).Where(x => x.VillageId == villageId)
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        var results = new List<CoreIntakeView>();
        foreach (var item in items)
        {
            try { await AuthorizeReadAsync(item, ct); results.Add(View(item)); }
            catch (CoreIntakeException ex) when (ex.StatusCode == 403) { }
            catch (StaffWorkflowException ex) when (ex.StatusCode == 403) { }
        }
        return results;
    }
    public async Task<CoreIntakeSourceEvidence> SourceEvidenceAsync(Guid id, CancellationToken ct)
    {
        await GetAsync(id, ct);
        var intake = await db.CoreDocumentIntakes.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        return new(id, intake.DocumentId, intake.Sha256Hash, intake.ClassifierVersion, JsonSerializer.Deserialize<List<CoreSourcePage>>(intake.SourcePagesJson) ?? []);
    }

    public async Task<CoreIntakeView> ConfirmAsync(Guid id, CoreIntakeConfirmation request, CancellationToken ct)
    {
        await AuthorizeAsync(PermissionCodes.AwardCoreDocumentUpload, ct);
        if (!CoreDocumentRoles.All.Contains(request.DocumentRole, StringComparer.Ordinal)) throw new CoreIntakeException("Choose an explicit supported core role.");
        if ((request.AwardId is null) == (request.CreateAward is null)) throw new CoreIntakeException("Select exactly one Award or explicitly confirm creation.");
        var initial = await db.CoreDocumentIntakes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CoreIntakeException("Intake was not found.", 404);
        try
        {
            return await mutations.ExecuteAsync(["award-metadata-identities", $"award:{request.AwardId}", $"pdf:{initial.Sha256Hash.ToLowerInvariant()}", $"intake:{id}", $"intake-village:{initial.VillageId}"],
                null, null, request, async () =>
            {
                var intake = await db.CoreDocumentIntakes.Include(x => x.Document).SingleAsync(x => x.Id == id, ct);
                await db.Entry(intake).ReloadAsync(ct);
                var confirmationJson = JsonSerializer.Serialize(request);
                if (intake.Status == "Confirmed")
                {
                    await AuthorizeReadAsync(intake, ct);
                    if (intake.ConfirmedAwardId is Guid owner)
                    {
                        var existingAward = await db.Awards.SingleAsync(x => x.Id == owner, ct);
                        await AuthorizeAsync(PermissionCodes.AwardCoreDocumentUpload, ct, existingAward.CreatedBy, record: true);
                    }
                    if (SameDecision(intake.ConfirmationJson, request) || request.AwardId == intake.ConfirmedAwardId && request.DocumentRole == intake.ConfirmedRole &&
                        request.LegalDate is null && request.ReferenceNumber is null && request.PartNumber is null && request.Title is null && request.Remarks is null && request.KhasraIds is null)
                        return View(intake, true);
                    throw new CoreIntakeException("This intake has already been confirmed with a different decision.", 409, "AlreadyConfirmedDifferentDecision");
                }
                if (request.ExpectedRevision is int revision && revision != intake.Revision) throw new CoreIntakeException("Intake changed. Refresh before confirming.", 409, "RevisionConflict");
                if (intake.Status != "NeedsConfirmation") throw new CoreIntakeException("The PDF is still being inspected. Reload or re-upload to resume.", 409, "IntakePending");
                await AuthorizeReadAsync(intake, ct);
                if (!await db.Villages.AnyAsync(x => x.Id == intake.VillageId && x.RecordStatus == RecordStatus.Active, ct)) throw new CoreIntakeException("Village was not found.", 404);
                if (intake.Document.RecordStatus != RecordStatus.Active || intake.Document.Status is not ("Staged" or "Active")) throw new CoreIntakeException("Source is unavailable for confirmation.", 409, "SourceArchived");
                var savedProposal = JsonSerializer.Deserialize<CoreIntakeProposal>(intake.ProposalJson);
                if (request.DocumentRole == "Award" && savedProposal?.Classification.Alternatives.Any(x => x.Kind == "temporaryInstrument") == true)
                    throw new CoreIntakeException("Register Offer/Section 35 evidence as a temporary proceeding.", 409, "TemporaryInstrumentRequiresProceeding");
                Award award; var created = false;
                if (request.AwardId is Guid awardId)
                {
                    award = await db.Awards.SingleOrDefaultAsync(x => x.Id == awardId && x.RecordStatus == RecordStatus.Active && x.VillageLinks.Any(v => v.VillageId == intake.VillageId), ct)
                        ?? throw new CoreIntakeException("The selected Award must belong to this Village.");
                }
                else
                {
                    await AuthorizeAsync(PermissionCodes.AwardCreate, ct);
                    var input = request.CreateAward!;
                    if (!input.Confirmed || request.DocumentRole != "Award") throw new CoreIntakeException("Only an explicitly confirmed final Award document can create an Award.");
                    if (string.IsNullOrWhiteSpace(input.AwardNumber) || input.AwardNumber.Length > 128 || input.AwardType?.Length > 100) throw new CoreIntakeException("Provide a valid Award number and type.");
                    var proposal = JsonSerializer.Deserialize<CoreIntakeProposal>(intake.ProposalJson);
                    if (proposal?.Classification.Alternatives.Any(x => x.Kind == "temporaryInstrument") == true)
                        throw new CoreIntakeException("Register Section 35 Offer/proceedings as a temporary instrument; it cannot create a final Award.", 409, "TemporaryInstrumentRequiresProceeding");
                    var conflicts = await LandRecordsStaffWorkflowService.ConflictsAsync(db, null, [intake.VillageId], input.AwardNumber, input.ActRegime, input.AcquisitionProjectId, ct);
                    if (conflicts.Count > 1) throw new CoreIntakeException("Multiple Awards have this identity. Select explicitly.", 409, "DuplicateAward");
                    if (conflicts.Count == 1)
                    {
                        award = await db.Awards.SingleAsync(x => x.Id == conflicts[0].AwardId, ct);
                        if (input.AwardDate is not null && award.AwardDate is not null && input.AwardDate != award.AwardDate ||
                            !string.IsNullOrWhiteSpace(input.AwardType) && !string.IsNullOrWhiteSpace(award.AwardType) && !string.Equals(input.AwardType.Trim(), award.AwardType, StringComparison.OrdinalIgnoreCase))
                            throw new CoreIntakeException("Proposed identity conflicts with an existing Award. Select explicitly.", 409, "DuplicateAward");
                    }
                    else
                    {
                        if (await db.DocumentAwards.AnyAsync(x => !x.IsDetached && x.Document.Sha256Hash != null && x.Document.Sha256Hash.ToLower() == intake.Sha256Hash.ToLower(), ct))
                            throw await staff.OwnershipConflictAsync(intake.DocumentId, Guid.Empty, ct);
                        award = await new AwardWorkflowService(db).CreateAsync(new(input.AwardNumber, intake.VillageId, input.AwardDate, input.AwardType, input.ActRegime, null, input.AcquisitionProjectId, null), ct);
                        award.CreatedBy = Actor; created = true;
                    }
                }
                await using var source = await storage.OpenReadAsync(intake.Document.StoragePath, ct) ?? throw new CoreIntakeException("Stored source unavailable.", 409, "SourceUnavailable");
                var result = await staff.UploadCoreAsync(award.Id, new(request.DocumentRole, intake.VillageId, request.LegalDate,
                    request.ReferenceNumber, request.PartNumber, request.Title, request.Remarks, request.KhasraIds), source, intake.Document.OriginalFileName, ct);
                intake.ConfirmedDocumentId = result.DocumentId; intake.ConfirmedAwardId = award.Id; intake.ConfirmedRole = request.DocumentRole;
                intake.ConfirmedBy = Actor; intake.ConfirmedAt = DateTimeOffset.UtcNow; intake.ConfirmationJson = confirmationJson; intake.Status = "Confirmed"; intake.Revision++;
                db.Add(new AuditLog { EntityType = nameof(CoreDocumentIntake), EntityId = id, Action = "CoreDocumentIntakeConfirmed", ChangedBy = Actor,
                    OldValues = JsonSerializer.Serialize(new { status = "NeedsConfirmation", intake.DocumentId }),
                    NewValues = JsonSerializer.Serialize(new { awardId = award.Id, documentId = result.DocumentId, request.DocumentRole, createdAward = created, request.LegalDate }) });
                await db.SaveChangesAsync(ct); return View(intake, result.Duplicate);
            }, ct);
        }
        catch (StaffWorkflowException ex) { throw Convert(ex); }
        catch (AwardWorkflowException ex) { throw new CoreIntakeException(ex.Message, ex.StatusCode, "DuplicateAward"); }
    }

    private async Task<CoreIntakeProposal> MatchAsync(Village village, CoreClassification classification, CancellationToken ct)
    {
        var alternatives = classification.Alternatives.ToList();
        var references = classification.Alternatives.Where(x => x.Kind == "awardNumber").Select(x => x.Value).ToList();
        var matching = (await VillageAwardsAsync(village.Id, ct)).Where(x => references.Any(number => AwardNumberIdentity.Equivalent(x.AwardNumber, number))).ToList();
        alternatives.AddRange(matching.Select(x => new CoreIntakeAlternative("existingAward", x.AwardNumber, x.Id)));
        var villageMismatch = classification.DetectedVillage is not null && !string.Equals(classification.DetectedVillage.Trim(), village.Name.Trim(), StringComparison.OrdinalIgnoreCase);
        if (villageMismatch) alternatives.Add(new("villageConflict", classification.DetectedVillage!, Reason: "The explicit Village differs from the selected Village."));
        var dateConflict = matching.Any(x => x.AwardDate is not null && classification.DetectedAwardDate is not null && x.AwardDate != classification.DetectedAwardDate);
        if (dateConflict) alternatives.Add(new("awardDateConflict", classification.DetectedAwardDate!.Value.ToString("yyyy-MM-dd"), Reason: "The explicit date differs from an existing Award."));
        if (!classification.RequiresReview && !villageMismatch && !dateConflict && matching.Count == 1)
            return new(classification, "MatchedExistingAward", matching[0].Id, matching[0].AwardNumber, alternatives);
        if (!classification.RequiresReview && !villageMismatch && matching.Count == 0 && classification.DetectedRole == "Award" && classification.Confidence >= .9m)
            return new(classification, "ProposedNewAward", null, null, alternatives);
        return new(classification, "NeedsOfficerReview", null, null, alternatives);
    }

    private async Task<List<Award>> VillageAwardsAsync(Guid villageId, CancellationToken ct)
    {
        var candidates = await db.Awards.Where(x => x.RecordStatus == RecordStatus.Active && x.VillageLinks.Any(v => v.VillageId == villageId)).ToListAsync(ct);
        var visible = new List<Award>();
        foreach (var award in candidates)
            if (await access.CanAsync(PermissionCodes.AwardView, LandRecordsStaffWorkflowService.Context(award.CreatedBy), ct)) visible.Add(award);
        return visible;
    }
    private static bool SameDecision(string? previous, CoreIntakeConfirmation request)
    {
        if (string.IsNullOrWhiteSpace(previous)) return false;
        try { return JsonSerializer.Serialize(JsonSerializer.Deserialize<CoreIntakeConfirmation>(previous, new JsonSerializerOptions(JsonSerializerDefaults.Web))) == JsonSerializer.Serialize(request); }
        catch (JsonException) { return false; }
    }

    private static CoreIntakeView View(CoreDocumentIntake intake, bool duplicate = false)
    {
        var proposal = intake.ProposalJson == "{}" ? null : JsonSerializer.Deserialize<CoreIntakeProposal>(intake.ProposalJson);
        var c = proposal?.Classification;
        return new(intake.Id, intake.VillageId, intake.DocumentId, intake.Document.OriginalFileName, intake.Status,
            c?.DetectedRole ?? "Unknown", c?.DetectedAwardNumber, c?.DetectedAwardDate, c?.DetectedVillage, c?.DetectedAwardType,
            c?.Confidence ?? 0, proposal?.MatchState ?? "NeedsOfficerReview", proposal?.MatchedAwardId, proposal?.MatchedAwardNumber,
            c?.Evidence ?? [], proposal?.Alternatives ?? [], intake.CreatedAt, intake.ConfirmedAwardId, intake.ConfirmedRole, intake.ConfirmedDocumentId,
            intake.ConfirmedAt, $"/api/core-document-intakes/{intake.Id}/file", $"/api/core-document-intakes/{intake.Id}/file?download=true", duplicate, intake.Revision, intake.Status == "Confirmed" ? duplicate ? "AlreadyAttached" : "Attached" : null);
    }
}

public sealed record CoreIntakeSourceEvidence(Guid IntakeId, Guid DocumentId, string Sha256Hash, string ClassifierVersion,
    IReadOnlyList<CoreSourcePage> Pages);
