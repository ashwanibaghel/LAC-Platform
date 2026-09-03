using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LAC.Infrastructure;

public sealed class LrWorkflowException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed record LrRowInput(
    int? RowNumber,
    string RawKhasraText,
    Guid? KhasraId,
    string? RawAreaText,
    decimal? ParsedArea,
    string? AreaUnit,
    Guid? Section4NotificationId,
    Guid? Section6NotificationId,
    Guid? AwardId,
    string? RawRemarks,
    VerificationStatus VerificationStatus);

public sealed record LrRowSaveResult(Guid Id, int Revision, bool PossibleDuplicate, string? DuplicateWarning);
public sealed record LrCommitResult(Guid Id, int Revision, int CreatedNotificationLinks, bool CreatedAwardLink);

public sealed class LrWorkflowService(LacDbContext db)
{
    public async Task<LrRowSaveResult> CreateAsync(Guid villageLrId, LrRowInput input, CancellationToken ct)
    {
        var register = await RegisterAsync(villageLrId, ct);
        await ValidateInputAsync(register.VillageId, input, ct);
        var duplicate = await DuplicateWarningAsync(villageLrId, input.RawKhasraText, input.AwardId, null, ct);
        var row = Apply(new LREntry { VillageLRId = villageLrId }, input);
        db.LREntries.Add(row);
        await db.SaveChangesAsync(ct);
        return new LrRowSaveResult(row.Id, row.Revision, duplicate is not null, duplicate);
    }

    public async Task<LrRowSaveResult> UpdateAsync(Guid id, int expectedRevision, LrRowInput input, CancellationToken ct)
    {
        var row = await db.LREntries.Include(x => x.VillageLR).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new LrWorkflowException("LR row was not found.", 404);
        if (row.VerificationStatus == VerificationStatus.Committed) throw new LrWorkflowException("Committed LR rows cannot be edited. Create a reviewed correction instead.", 409);
        if (row.Revision != expectedRevision) throw new LrWorkflowException("This LR row was changed by another user. Refresh before saving.", 409);
        await ValidateInputAsync(row.VillageLR.VillageId, input, ct);
        var duplicate = await DuplicateWarningAsync(row.VillageLRId, input.RawKhasraText, input.AwardId, row.Id, ct);
        Apply(row, input);
        row.Revision++;
        db.AuditLogs.Add(new AuditLog { EntityType = nameof(LREntry), EntityId = row.Id, Action = "LrInterpretationChanged", ChangedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        return new LrRowSaveResult(row.Id, row.Revision, duplicate is not null, duplicate);
    }

    public async Task<LrCommitResult> CommitAsync(Guid id, int expectedRevision, bool applyParsedAreaToAcquisitionLinks, CancellationToken ct)
    {
        var row = await db.LREntries.Include(x => x.VillageLR).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new LrWorkflowException("LR row was not found.", 404);
        if (row.Revision != expectedRevision) throw new LrWorkflowException("This LR row was changed by another user. Refresh before committing.", 409);
        if (row.VerificationStatus != VerificationStatus.Verified) throw new LrWorkflowException("Only a Verified LR row may be explicitly committed.", 409);
        if (row.KhasraId is null) throw new LrWorkflowException("A verified Khasra link is required before commit.");
        if (applyParsedAreaToAcquisitionLinks && row.ParsedArea is not > 0) throw new LrWorkflowException("A positive parsed area is required before mapping LR area to acquisition relationships.");
        await ValidateReferencesAsync(row.VillageLR.VillageId, row.KhasraId, row.Section4NotificationId, row.Section6NotificationId, row.AwardId, ct);

        IDbContextTransaction? transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var createdNotifications = 0;
            createdNotifications += await EnsureNotificationLinkAsync(row.Section4NotificationId, row.KhasraId.Value, applyParsedAreaToAcquisitionLinks ? row.ParsedArea : null, row.AreaUnit, ct);
            createdNotifications += await EnsureNotificationLinkAsync(row.Section6NotificationId, row.KhasraId.Value, applyParsedAreaToAcquisitionLinks ? row.ParsedArea : null, row.AreaUnit, ct);
            var createdAward = await EnsureAwardLinkAsync(row.AwardId, row.KhasraId.Value, applyParsedAreaToAcquisitionLinks ? row.ParsedArea : null, row.AreaUnit, ct);
            row.VerificationStatus = VerificationStatus.Committed;
            row.Revision++;
            db.AuditLogs.Add(new AuditLog { EntityType = nameof(LREntry), EntityId = row.Id, Action = "LrRowCommitted", ChangedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return new LrCommitResult(row.Id, row.Revision, createdNotifications, createdAward);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            throw;
        }
        finally { if (transaction is not null) await transaction.DisposeAsync(); }
    }

    public async Task<Khasra> CreateKhasraAsync(Guid villageId, string displayNumber, decimal? totalArea, string? areaUnit, string? rectangleNumber, string? killaNumber, string? subdivisionNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(displayNumber)) throw new LrWorkflowException("Display number is required.");
        if (totalArea is not null && totalArea <= 0) throw new LrWorkflowException("Total area must be positive when provided.");
        if (!await db.Villages.AnyAsync(x => x.Id == villageId, ct)) throw new LrWorkflowException("Village was not found.", 404);
        var normalized = KhasraNumber.Normalize(displayNumber);
        var existing = await db.Khasras.SingleOrDefaultAsync(x => x.VillageId == villageId && x.NormalizedNumber == normalized, ct);
        if (existing is not null) return existing;
        var khasra = new Khasra { VillageId = villageId, DisplayNumber = displayNumber.Trim(), NormalizedNumber = normalized, TotalArea = totalArea, AreaUnit = Clean(areaUnit), RectangleNumber = Clean(rectangleNumber), KillaNumber = Clean(killaNumber), SubdivisionNumber = Clean(subdivisionNumber) };
        db.Khasras.Add(khasra);
        try { await db.SaveChangesAsync(ct); return khasra; }
        catch (DbUpdateException)
        {
            var concurrent = await db.Khasras.SingleOrDefaultAsync(x => x.VillageId == villageId && x.NormalizedNumber == normalized, ct);
            if (concurrent is not null) return concurrent;
            throw;
        }
    }

    public async Task<Notification> CreateNotificationAsync(string sectionType, string notificationNumber, DateOnly? notificationDate, string? remarks, CancellationToken ct)
    {
        sectionType = sectionType.Trim(); notificationNumber = notificationNumber.Trim();
        if (sectionType is not ("4" or "6")) throw new LrWorkflowException("Only Section 4 or Section 6 notifications can be created from an LR row.");
        if (string.IsNullOrWhiteSpace(notificationNumber)) throw new LrWorkflowException("Notification number is required.");
        var existing = await db.Notifications.SingleOrDefaultAsync(x => x.SectionType == sectionType && x.NotificationNumber == notificationNumber, ct);
        if (existing is not null) return existing;
        var notification = new Notification { SectionType = sectionType, NotificationNumber = notificationNumber, NotificationDate = notificationDate, Remarks = Clean(remarks) };
        db.Notifications.Add(notification); await db.SaveChangesAsync(ct); return notification;
    }

    public async Task<Award> CreateAwardAsync(string awardNumber, DateOnly? awardDate, string? awardType, string? actRegime, CancellationToken ct)
    {
        awardNumber = awardNumber.Trim(); if (string.IsNullOrWhiteSpace(awardNumber)) throw new LrWorkflowException("Award number is required.");
        var existing = await db.Awards.SingleOrDefaultAsync(x => x.AwardNumber == awardNumber, ct);
        if (existing is not null) return existing;
        var award = new Award { AwardNumber = awardNumber, AwardDate = awardDate, AwardType = Clean(awardType), ActRegime = Clean(actRegime) };
        db.Awards.Add(award); await db.SaveChangesAsync(ct); return award;
    }

    private async Task<VillageLR> RegisterAsync(Guid id, CancellationToken ct) => await db.VillageLRs.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new LrWorkflowException("Village LR register was not found.", 404);
    private async Task ValidateInputAsync(Guid villageId, LrRowInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.RawKhasraText)) throw new LrWorkflowException("Raw Khasra transcription is required.");
        if (input.ParsedArea is not null && input.ParsedArea <= 0) throw new LrWorkflowException("Parsed area must be positive when provided.");
        if (input.VerificationStatus == VerificationStatus.Committed) throw new LrWorkflowException("Use the explicit commit action; rows cannot be saved directly as Committed.");
        if (input.VerificationStatus == VerificationStatus.Verified && input.KhasraId is null) throw new LrWorkflowException("A Khasra link is required before a row can be marked Verified.");
        await ValidateReferencesAsync(villageId, input.KhasraId, input.Section4NotificationId, input.Section6NotificationId, input.AwardId, ct);
    }
    private async Task ValidateReferencesAsync(Guid villageId, Guid? khasraId, Guid? section4Id, Guid? section6Id, Guid? awardId, CancellationToken ct)
    {
        if (khasraId is not null && !await db.Khasras.AnyAsync(x => x.Id == khasraId && x.VillageId == villageId, ct)) throw new LrWorkflowException("The selected Khasra does not belong to this village.");
        if (section4Id is not null && !await db.Notifications.AnyAsync(x => x.Id == section4Id && x.SectionType == "4", ct)) throw new LrWorkflowException("Section 4 link must reference a Section 4 notification.");
        if (section6Id is not null && !await db.Notifications.AnyAsync(x => x.Id == section6Id && x.SectionType == "6", ct)) throw new LrWorkflowException("Section 6 link must reference a Section 6 notification.");
        if (awardId is not null && !await db.Awards.AnyAsync(x => x.Id == awardId, ct)) throw new LrWorkflowException("Selected award was not found.");
    }
    private async Task<string?> DuplicateWarningAsync(Guid registerId, string rawKhasra, Guid? awardId, Guid? excludingId, CancellationToken ct)
    {
        var candidate = db.LREntries.Where(x => x.VillageLRId == registerId && x.RawKhasraText == rawKhasra.Trim() && x.AwardId == awardId);
        if (excludingId is not null) candidate = candidate.Where(x => x.Id != excludingId);
        return await candidate.AnyAsync(ct) ? "Possible duplicate: a row with the same raw Khasra text and award already exists in this LR register." : null;
    }
    private async Task<int> EnsureNotificationLinkAsync(Guid? notificationId, Guid khasraId, decimal? area, string? unit, CancellationToken ct)
    {
        if (notificationId is null) return 0;
        var link = await db.Set<NotificationKhasra>().SingleOrDefaultAsync(x => x.NotificationId == notificationId && x.KhasraId == khasraId, ct);
        if (link is null) { db.Add(new NotificationKhasra { NotificationId = notificationId.Value, KhasraId = khasraId, NotifiedArea = area, AreaUnit = Clean(unit) }); return 1; }
        if (area is not null) { link.NotifiedArea = area; link.AreaUnit = Clean(unit); }
        return 0;
    }
    private async Task<bool> EnsureAwardLinkAsync(Guid? awardId, Guid khasraId, decimal? area, string? unit, CancellationToken ct)
    {
        if (awardId is null) return false;
        var link = await db.Set<AwardKhasra>().SingleOrDefaultAsync(x => x.AwardId == awardId && x.KhasraId == khasraId, ct);
        if (link is null) { db.Add(new AwardKhasra { AwardId = awardId.Value, KhasraId = khasraId, AcquiredArea = area, AreaUnit = Clean(unit), AcquisitionStatus = "Acquired" }); return true; }
        if (area is not null) { link.AcquiredArea = area; link.AreaUnit = Clean(unit); }
        return false;
    }
    private static LREntry Apply(LREntry row, LrRowInput input)
    {
        row.RowNumber = input.RowNumber; row.RawKhasraText = input.RawKhasraText.Trim(); row.KhasraId = input.KhasraId; row.RawAreaText = Clean(input.RawAreaText); row.ParsedArea = input.ParsedArea; row.AreaUnit = Clean(input.AreaUnit); row.Section4NotificationId = input.Section4NotificationId; row.Section6NotificationId = input.Section6NotificationId; row.AwardId = input.AwardId; row.RawRemarks = Clean(input.RawRemarks); row.VerificationStatus = input.VerificationStatus; return row;
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
