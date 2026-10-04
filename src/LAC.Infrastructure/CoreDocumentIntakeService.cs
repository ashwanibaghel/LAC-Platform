using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed class CoreIntakeException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
public sealed record CoreAwardCreation(string AwardNumber, DateOnly? AwardDate = null, string? AwardType = null, bool Confirmed = false);
public sealed record CoreIntakeConfirmation(string DocumentRole, Guid? AwardId = null, CoreAwardCreation? CreateAward = null);
public sealed record CoreIntakeProposal(CoreClassification Classification, string MatchState, Guid? MatchedAwardId,
    string? MatchedAwardNumber, IReadOnlyList<CoreIntakeAlternative> Alternatives);
public sealed record CoreIntakeView(Guid IntakeId, Guid VillageId, Guid DocumentId, string FileName, string Status,
    string DetectedRole, string? DetectedAwardNumber, DateOnly? DetectedAwardDate, string? DetectedVillage,
    string? DetectedAwardType, decimal Confidence, string MatchState, Guid? MatchedAwardId, string? MatchedAwardNumber,
    IReadOnlyList<CoreIntakeEvidence> Evidence, IReadOnlyList<CoreIntakeAlternative> Alternatives,
    DateTimeOffset CreatedAt, Guid? ConfirmedAwardId, string? ConfirmedRole, Guid? ConfirmedDocumentId,
    DateTimeOffset? ConfirmedAt, string ViewRoute, string DownloadRoute, bool IsDuplicate = false);

/// <summary>Bounded stripes serialize local mutations without retaining one semaphore per Village forever.</summary>
public sealed class CoreIntakeGate
{
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    public SemaphoreSlim For(Guid villageId) => gates[(villageId.GetHashCode() & int.MaxValue) % gates.Length];
}

public sealed class CoreDocumentIntakeService(LacDbContext db, IDocumentStorage storage, ICoreDocumentTextReader reader,
    ICoreDocumentClassifier classifier, CoreIntakeGate gate, IAccessControlService access, ICurrentUserContext user)
{
    public const long MaxFileBytes = 50 * 1024 * 1024;
    public const int MaxBatchFiles = 20;

    public async Task<CoreIntakeView> StageAsync(Guid villageId, Stream source, string fileName, CancellationToken ct)
    {
        await AuthorizeAsync(PermissionCodes.AwardCoreDocumentUpload, ct);
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 180 || safeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new CoreIntakeException("Provide a valid PDF filename of at most 180 characters.");
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new CoreIntakeException("Choose a PDF file.");
        if (!source.CanSeek) throw new CoreIntakeException("The PDF stream must be seekable.");
        if (source.Length is < 5 or > MaxFileBytes) throw new CoreIntakeException("Choose a non-empty PDF of at most 50 MiB.");
        var signature = new byte[5]; source.Position = 0;
        if (await source.ReadAsync(signature, ct) != 5 || !signature.AsSpan().SequenceEqual("%PDF-"u8)) throw new CoreIntakeException("The uploaded file is not a PDF.");
        source.Position = 0;
        var semaphore = gate.For(villageId); await semaphore.WaitAsync(ct);
        try
        {
            var staged = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
                await LockVillageAsync(villageId, ct);
                var village = await db.Villages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == villageId && x.RecordStatus == RecordStatus.Active, ct)
                    ?? throw new CoreIntakeException("Village was not found.", 404);
                source.Position = 0;
                var saved = await storage.SaveAndHashAsync(source, Path.GetFileName(fileName), ct);
                var duplicate = await db.CoreDocumentIntakes.Include(x => x.Document)
                    .SingleOrDefaultAsync(x => x.VillageId == villageId && x.Sha256Hash == saved.Sha256Hash, ct);
                if (duplicate is not null)
                {
                    await storage.DeleteAsync(saved.StoragePath, ct);
                    return (Intake: duplicate, Duplicate: true, Village: village);
                }
                var document = new Document { OriginalFileName = Path.GetFileName(fileName), StoragePath = saved.StoragePath,
                    Sha256Hash = saved.Sha256Hash, FileSize = saved.FileSize, MimeType = "application/pdf", DocumentType = "Unclassified Core PDF", Status = "Staged", UploadedBy = user.Username };
                var intake = new CoreDocumentIntake { VillageId = villageId, Document = document, Sha256Hash = saved.Sha256Hash, CreatedBy = user.Username };
                db.Add(intake);
                try { await db.SaveChangesAsync(ct); }
                catch { await storage.DeleteAsync(saved.StoragePath, CancellationToken.None); throw; }
                if (transaction is not null) await transaction.CommitAsync(ct);

                return (Intake: intake, Duplicate: false, Village: village);
            });

            var pending = staged.Intake;
            if (pending.Status != "Staged") return View(pending, staged.Duplicate);
            // The original is durable before any parser runs. Failures become reviewable proposals.
            IReadOnlyList<CoreSourcePage> pages = [];
            CoreClassification classification;
            try
            {
                await using var stored = await storage.OpenReadAsync(pending.Document.StoragePath, ct)
                    ?? throw new IOException("Stored PDF is unavailable.");
                pages = await reader.ReadAsync(stored, ct);
                classification = classifier.Classify(pages);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                classification = new("Unknown", null, null, null, null, 0m, true,
                    [new("extraction", 0, "Native PDF text could not be read; inspect the original PDF.")],
                    [new("extraction", ex.GetType().Name, Reason: "Local extraction failed. Officer review is required.")]);
            }
            var proposal = await MatchAsync(staged.Village, classification, ct);
            pending.SourcePagesJson = JsonSerializer.Serialize(pages);
            pending.ProposalJson = JsonSerializer.Serialize(proposal);
            pending.Status = "NeedsConfirmation";
            pending.Revision++;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                pending = await db.CoreDocumentIntakes.Include(x => x.Document).SingleAsync(x => x.Id == pending.Id, ct);
            }
            return View(pending, staged.Duplicate);
        }
        finally { semaphore.Release(); }
    }

    public async Task<CoreIntakeView> GetAsync(Guid id, CancellationToken ct)
    {
        await AuthorizeAsync(PermissionCodes.AwardView, ct);
        return View(await db.CoreDocumentIntakes.AsNoTracking().Include(x => x.Document).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CoreIntakeException("Intake was not found.", 404));
    }

    public async Task<IReadOnlyList<CoreIntakeView>> ListAsync(Guid villageId, int skip, int take, CancellationToken ct)
    {
        await AuthorizeAsync(PermissionCodes.AwardView, ct);
        if (!await db.Villages.AnyAsync(x => x.Id == villageId, ct)) throw new CoreIntakeException("Village was not found.", 404);
        return (await db.CoreDocumentIntakes.AsNoTracking().Include(x => x.Document).Where(x => x.VillageId == villageId)
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct)).Select(x => View(x)).ToList();
    }

    public async Task<CoreIntakeSourceEvidence> SourceEvidenceAsync(Guid id, CancellationToken ct)
    {
        await AuthorizeAsync(PermissionCodes.AwardView, ct);
        var intake = await db.CoreDocumentIntakes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CoreIntakeException("Intake was not found.", 404);
        return new(id, intake.DocumentId, intake.Sha256Hash, intake.ClassifierVersion,
            JsonSerializer.Deserialize<List<CoreSourcePage>>(intake.SourcePagesJson) ?? []);
    }

    public async Task<CoreIntakeView> ConfirmAsync(Guid id, CoreIntakeConfirmation request, CancellationToken ct)
    {
        await AuthorizeAsync(PermissionCodes.AwardCoreDocumentUpload, ct);
        if (!CoreDocumentRoles.All.Contains(request.DocumentRole, StringComparer.Ordinal)) throw new CoreIntakeException("Choose Award, NM, StatementA, or PossessionProceeding.");
        if ((request.AwardId is null) == (request.CreateAward is null)) throw new CoreIntakeException("Select exactly one Award or explicitly confirm creation.");
        if (request.CreateAward is not null)
        {
            await AuthorizeAsync(PermissionCodes.AwardCreate, ct);
            if (!request.CreateAward.Confirmed || request.DocumentRole != "Award") throw new CoreIntakeException("Only an officer-confirmed Award document can create an Award.");
            if (string.IsNullOrWhiteSpace(request.CreateAward.AwardNumber) || request.CreateAward.AwardNumber.Length > 128 || request.CreateAward.AwardType?.Length > 100)
                throw new CoreIntakeException("Provide an Award number (up to 128 characters) and an optional type (up to 100 characters).");
        }
        var villageId = await db.CoreDocumentIntakes.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.VillageId).SingleOrDefaultAsync(ct)
            ?? throw new CoreIntakeException("Intake was not found.", 404);
        var semaphore = gate.For(villageId); await semaphore.WaitAsync(ct);
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
                await LockVillageAsync(villageId, ct);
                var intake = await db.CoreDocumentIntakes.Include(x => x.Document).SingleAsync(x => x.Id == id, ct);
                var confirmationJson = JsonSerializer.Serialize(request);
                if (intake.Status == "Confirmed")
                {
                    if (intake.ConfirmationJson == confirmationJson || (request.AwardId == intake.ConfirmedAwardId && request.DocumentRole == intake.ConfirmedRole)) return View(intake);
                    throw new CoreIntakeException("This intake has already been confirmed with a different decision.", 409);
                }
                if (intake.Status != "NeedsConfirmation") throw new CoreIntakeException("The PDF is still being inspected. Reload its status or re-upload to resume an interrupted intake.", 409);
                if (!await db.Villages.AnyAsync(x => x.Id == villageId && x.RecordStatus == RecordStatus.Active, ct))
                    throw new CoreIntakeException("Village was not found.", 404);
                if (intake.Document.RecordStatus != RecordStatus.Active || intake.Document.Status != "Staged") throw new CoreIntakeException("This source PDF is no longer available for confirmation.", 409);
                // No detected field is trusted here. Every identity is selected or explicitly supplied by the officer.
                Award award;
                var created = false;
                if (request.AwardId is Guid awardId)
                {
                    award = await db.Awards.SingleOrDefaultAsync(x => x.Id == awardId && x.RecordStatus == RecordStatus.Active && x.VillageLinks.Any(v => v.VillageId == villageId), ct)
                        ?? throw new CoreIntakeException("The selected Award must belong to this Village.");
                }
                else
                {
                    var input = request.CreateAward!;
                    var matching = (await VillageAwardsAsync(villageId, ct)).Where(x => AwardNumberIdentity.Equivalent(x.AwardNumber, input.AwardNumber)).ToList();
                    if (matching.Count > 1) throw new CoreIntakeException("Multiple existing Awards have this identity. Select the correct Award explicitly.", 409);
                    if (matching.Count == 1)
                    {
                        award = matching[0];
                        if ((input.AwardDate is not null && award.AwardDate is not null && input.AwardDate != award.AwardDate)
                            || (!string.IsNullOrWhiteSpace(input.AwardType) && !string.IsNullOrWhiteSpace(award.AwardType) && !string.Equals(input.AwardType.Trim(), award.AwardType, StringComparison.OrdinalIgnoreCase)))
                            throw new CoreIntakeException("The proposed identity conflicts with an existing Award. Select an Award explicitly.", 409);
                    }
                    else
                    {
                        award = new Award { AwardNumber = input.AwardNumber.Trim(), AwardDate = input.AwardDate, AwardType = input.AwardType?.Trim(), Status = "Draft" };
                        db.Add(award); db.Add(new AwardVillage { Award = award, VillageId = villageId }); created = true;
                    }
                }
                var existingLink = await db.DocumentAwards.Include(x => x.Document).FirstOrDefaultAsync(x => x.AwardId == award.Id &&
                    x.Document.Sha256Hash == intake.Sha256Hash && x.Document.Status == "Active" && x.Document.RecordStatus == RecordStatus.Active, ct);
                if (await db.DocumentAwards.AnyAsync(x => x.AwardId != award.Id && x.Document.Sha256Hash == intake.Sha256Hash &&
                    x.Document.Status == "Active" && x.Document.RecordStatus == RecordStatus.Active, ct))
                    throw new CoreIntakeException("This PDF is already linked to another Award. Review the existing link before adding it here.", 409);
                if (existingLink is not null && existingLink.CoreDocumentRole is not null && existingLink.CoreDocumentRole != request.DocumentRole)
                    throw new CoreIntakeException("This PDF is already linked with another core role. Review that link before changing its role.", 409);
                var linkedDocument = existingLink?.Document ?? intake.Document;
                if (existingLink is null) db.Add(new DocumentAward { Award = award, Document = linkedDocument, CoreDocumentRole = request.DocumentRole });
                else existingLink.CoreDocumentRole = request.DocumentRole;
                if (existingLink is null)
                {
                    linkedDocument.Status = "Active"; linkedDocument.DocumentType = request.DocumentRole;
                    db.Add(new DocumentVillage { VillageId = villageId, Document = linkedDocument });
                }
                else intake.Document.Status = "Duplicate";
                intake.ConfirmedDocumentId = linkedDocument.Id; intake.ConfirmedAwardId = award.Id;
                intake.ConfirmedRole = request.DocumentRole; intake.ConfirmedBy = user.Username; intake.ConfirmedAt = DateTimeOffset.UtcNow;
                intake.ConfirmationJson = confirmationJson; intake.Status = "Confirmed"; intake.Revision++;
                db.Add(new AuditLog { EntityType = nameof(CoreDocumentIntake), EntityId = id, Action = "CoreDocumentIntakeConfirmed", ChangedBy = user.Username,
                    NewValues = JsonSerializer.Serialize(new { awardId = award.Id, documentId = linkedDocument.Id, request.DocumentRole, createdAward = created }) });
                await db.SaveChangesAsync(ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
                return View(intake);
            });
        }
        catch (DbUpdateConcurrencyException) { throw new CoreIntakeException("This intake changed while confirming. Reload its status.", 409); }
        finally { semaphore.Release(); }
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

    private Task<List<Award>> VillageAwardsAsync(Guid villageId, CancellationToken ct) => db.Awards
        .Where(x => x.RecordStatus == RecordStatus.Active && x.VillageLinks.Any(v => v.VillageId == villageId)).ToListAsync(ct);

    private async Task AuthorizeAsync(string permission, CancellationToken ct)
    {
        if (!user.IsAuthenticated) throw new CoreIntakeException("Authentication is required.", 401);
        if (!await access.CanAsync(permission, new AccessResourceContext(WorkstreamCode: WorkstreamCodes.Award), ct))
            throw new CoreIntakeException("You do not have permission for this action.", 403);
    }

    private async Task LockVillageAsync(Guid villageId, CancellationToken ct)
    {
        // The durable lock also serializes concurrent intake processes on the local PostgreSQL server.
        if (db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Villages\" WHERE \"Id\" = {villageId} FOR UPDATE", ct);
    }

    private static CoreIntakeView View(CoreDocumentIntake intake, bool duplicate = false)
    {
        var proposal = intake.ProposalJson == "{}" ? null : JsonSerializer.Deserialize<CoreIntakeProposal>(intake.ProposalJson);
        var c = proposal?.Classification;
        return new(intake.Id, intake.VillageId, intake.DocumentId, intake.Document.OriginalFileName, intake.Status,
            c?.DetectedRole ?? "Unknown", c?.DetectedAwardNumber, c?.DetectedAwardDate, c?.DetectedVillage, c?.DetectedAwardType,
            c?.Confidence ?? 0, proposal?.MatchState ?? "NeedsOfficerReview", proposal?.MatchedAwardId, proposal?.MatchedAwardNumber,
            c?.Evidence ?? [], proposal?.Alternatives ?? [], intake.CreatedAt, intake.ConfirmedAwardId, intake.ConfirmedRole, intake.ConfirmedDocumentId,
            intake.ConfirmedAt, $"/api/core-document-intakes/{intake.Id}/file", $"/api/core-document-intakes/{intake.Id}/file?download=true", duplicate);
    }
}

public sealed record CoreIntakeSourceEvidence(Guid IntakeId, Guid DocumentId, string Sha256Hash, string ClassifierVersion,
    IReadOnlyList<CoreSourcePage> Pages);
