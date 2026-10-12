using System.Text.Json;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LAC.Infrastructure;

public sealed partial class LandRecordsStaffWorkflowService(LacDbContext db, IDocumentStorage storage,
    IAccessControlService access, ICurrentUserContext user, IConfiguration configuration)
{
    private readonly LandRecordsMutationStore mutations = new(db);
    private string Actor => user.UserId?.ToString() ?? throw new StaffWorkflowException("Unauthenticated", "Sign in before changing official records.", 401);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Reason(string? reason) { if (Clean(reason) is null || reason!.Length > 2000) throw new StaffWorkflowException("ReasonRequired", "A correction reason (up to 2000 characters) is required."); }
    private static void Revision(int actual, int expected) { if (actual != expected) throw new StaffWorkflowException("RevisionConflict", "Record changed. Refresh before saving.", 409); }
    internal static AccessResourceContext Context(string? owner, string workstream = WorkstreamCodes.Award) => new(WorkstreamCode: workstream, OwnerUserId: Guid.TryParse(owner, out var id) ? id : null);
    private async Task AuthorizeAsync(string permission, string? owner = null, string workstream = WorkstreamCodes.Award, CancellationToken ct = default)
    {
        _ = Actor;
        if (!await access.CanAsync(permission, Context(owner, workstream), ct)) throw new StaffWorkflowException("Forbidden", "You do not have live permission for this record.", 403);
    }
    private async Task<Award> AwardAsync(Guid id, string permission, bool active, CancellationToken ct)
    {
        var award = await db.Awards.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new StaffWorkflowException("AwardNotFound", "Award was not found.", 404);
        // Authorization reads can precede an advisory-lock wait. Reload inside
        // the mutation as well, so a concurrent archive is never missed by tracking.
        if (db.Entry(award).State == EntityState.Unchanged) await db.Entry(award).ReloadAsync(ct);
        await AuthorizeAsync(permission, award.CreatedBy, ct: ct);
        if (active && award.RecordStatus != RecordStatus.Active) throw new StaffWorkflowException("AwardArchived", "Restore the Award before changing it.", 409);
        return award;
    }
    public async Task AuthorizeAwardReadAsync(Guid id, CancellationToken ct)
    {
        var award = await AwardAsync(id, PermissionCodes.AwardView, false, ct);
        if (award.RecordStatus != RecordStatus.Active) await AuthorizeAsync(PermissionCodes.AuditView, award.CreatedBy, ct: ct);
    }
    public async Task AuthorizeUnassignedSourceReadAsync(Guid documentId, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId, ct) ?? throw new StaffWorkflowException("DocumentNotFound", "Source document was not found.", 404);
        await AuthorizeAsync(PermissionCodes.AwardView, document.CreatedBy ?? document.UploadedBy, ct: ct);
        if (document.RecordStatus != RecordStatus.Active) await AuthorizeAsync(PermissionCodes.AuditView, document.CreatedBy, ct: ct);
    }
    private void Audit(string entityType, Guid id, string action, object? before, object? after, string? reason = null)
        => db.AuditLogs.Add(new AuditLog { EntityType = entityType, EntityId = id, Action = action, ChangedBy = Actor,
            ChangedAt = DateTimeOffset.UtcNow, OldValues = before is null ? null : JsonSerializer.Serialize(before),
            NewValues = JsonSerializer.Serialize(new { values = after, reason }) });

    internal static async Task<List<AwardIdentityConflict>> ConflictsAsync(LacDbContext db, Guid? excluding, IReadOnlyList<Guid> villages,
        string number, string? regime, Guid? project, CancellationToken ct)
    {
        var candidates = await db.Awards.AsNoTracking().Where(x => x.Id != excluding && x.RecordStatus == RecordStatus.Active &&
            x.VillageLinks.Any(v => villages.Contains(v.VillageId))).Select(x => new { x.Id, x.AwardNumber, x.AwardDate, x.ActRegime, x.AcquisitionProjectId }).ToListAsync(ct);
        return candidates.Where(x => AwardNumberIdentity.Equivalent(x.AwardNumber, number) &&
            string.Equals(Clean(x.ActRegime), Clean(regime), StringComparison.OrdinalIgnoreCase) &&
            (project is null || x.AcquisitionProjectId is null || x.AcquisitionProjectId == project))
            .Select(x => new AwardIdentityConflict(x.Id, x.AwardNumber, x.AwardDate, x.ActRegime)).ToList();
    }
    private static void ValidateMetadata(AwardMetadataInput input)
    {
        if (Clean(input.AwardNumber) is null || input.AwardNumber.Length > 200) throw new StaffWorkflowException("AwardNumberRequired", "Award number is required (up to 200 characters).");
        if (new[] { input.AwardType, input.ActRegime, input.Purpose, input.Remarks }.Any(x => x?.Length > 4000)) throw new StaffWorkflowException("MetadataTooLong", "Metadata exceeds 4000 characters.");
        Reason(input.Reason);
    }
    public async Task<IReadOnlyList<AwardIdentityConflict>> PreviewMetadataAsync(Guid id, AwardMetadataInput input, CancellationToken ct)
    {
        await AwardAsync(id, PermissionCodes.AwardEdit, true, ct); ValidateMetadata(input);
        return await ConflictsAsync(db, id, await db.AwardVillages.Where(x => x.AwardId == id).Select(x => x.VillageId).ToListAsync(ct), input.AwardNumber, input.ActRegime, input.AcquisitionProjectId, ct);
    }
    public async Task<AwardMutationResult> UpdateMetadataAsync(Guid id, AwardMetadataInput input, CancellationToken ct)
    {
        await AwardAsync(id, PermissionCodes.AwardEdit, true, ct);
        return await mutations.ExecuteAsync(["award-metadata-identities", $"award:{id}"], $"award-metadata:{id}", input.MutationId, input, async () =>
        {
            var award = await AwardAsync(id, PermissionCodes.AwardEdit, true, ct); Revision(award.Revision, input.ExpectedRevision); ValidateMetadata(input);
            if (award.Status is not ("Draft" or "NeedsReview")) throw new StaffWorkflowException("AwardNotEditable", "Only Draft or NeedsReview Awards permit generic metadata corrections.", 409);
            var verified = await db.SourceEvidence.Where(x => x.AwardId == id).Select(x => x.FactName).Distinct().ToListAsync(ct);
            if (verified.Contains("AwardCore") ||
                verified.Contains("awardNumber") && !AwardNumberIdentity.Equivalent(award.AwardNumber, input.AwardNumber) ||
                verified.Contains("awardDate") && award.AwardDate != input.AwardDate ||
                verified.Contains("awardType") && award.AwardType != Clean(input.AwardType) ||
                verified.Contains("purpose") && award.Purpose != Clean(input.Purpose))
                throw new StaffWorkflowException("VerifiedMetadataProtected", "Reviewed fields differ from this correction. Preserve them or use a reviewed source correction.", 409);
            if (input.AcquisitionProjectId is Guid project && !await db.AcquisitionProjects.AnyAsync(x => x.Id == project && x.RecordStatus == RecordStatus.Active, ct)) throw new StaffWorkflowException("ProjectNotFound", "Project was not found.", 404);
            var conflicts = await PreviewMetadataAsync(id, input, ct);
            if (conflicts.Count > 0) throw new StaffWorkflowException("DuplicateAward", "Possible duplicate in the same Village/acquisition context.", 409, conflicts);
            var before = Metadata(award);
            award.AwardNumber = input.AwardNumber.Trim(); award.AwardDate = input.AwardDate; award.AwardType = Clean(input.AwardType);
            award.ActRegime = Clean(input.ActRegime); award.Purpose = Clean(input.Purpose); award.AcquisitionProjectId = input.AcquisitionProjectId; award.Remarks = Clean(input.Remarks);
            // A no-op correction still advances the revision and records its explicit reason.
            db.Entry(award).Property(x => x.Revision).IsModified = true;
            Audit(nameof(Award), id, "StaffMetadataCorrected", before, Metadata(award), input.Reason);
            await db.SaveChangesAsync(ct); return Result(award);
        }, ct);
    }
    public async Task<AwardMutationResult> LifecycleAsync(Guid id, AwardLifecycleInput input, CancellationToken ct)
    {
        var authorized = await AwardAsync(id, PermissionCodes.AwardEdit, false, ct); await AuthorizeAsync(PermissionCodes.AuditView, authorized.CreatedBy, ct: ct);
        return await mutations.ExecuteAsync([$"award:{id}"], $"award-lifecycle:{id}", input.MutationId, input, async () =>
        {
            var award = await AwardAsync(id, PermissionCodes.AwardEdit, false, ct); Revision(award.Revision, input.ExpectedRevision); Reason(input.Reason);
            if (input.Action is not ("Archive" or "Void" or "Restore")) throw new StaffWorkflowException("InvalidAction", "Choose Archive, Void or Restore.");
            if (input.Action == "Restore" && award.RecordStatus != RecordStatus.Archived || input.Action != "Restore" && award.RecordStatus != RecordStatus.Active) throw new StaffWorkflowException("InvalidTransition", "This lifecycle transition is not eligible.", 409);
            if (input.Action == "Restore")
            {
                var villages = await db.AwardVillages.Where(x => x.AwardId == id).Select(x => x.VillageId).ToListAsync(ct);
                var conflicts = await ConflictsAsync(db, id, villages, award.AwardNumber, award.ActRegime, award.AcquisitionProjectId, ct);
                if (conflicts.Count > 0) throw new StaffWorkflowException("DuplicateAward", "Restoration conflicts with an active Award.", 409, conflicts);
            }
            var before = Result(award);
            award.RecordStatus = input.Action == "Restore" ? RecordStatus.Active : RecordStatus.Archived;
            award.ArchiveKind = input.Action == "Restore" ? null : input.Action;
            Audit(nameof(Award), id, "StaffAward" + input.Action, before, Result(award), input.Reason);
            await db.SaveChangesAsync(ct); return Result(award);
        }, ct);
    }
    private static AwardMutationResult Result(Award award) => new(award.Id, award.Revision, award.RecordStatus, award.ArchiveKind);
    private static object Metadata(Award a) => new { a.AwardNumber, a.AwardDate, a.AwardType, a.ActRegime, a.Purpose, a.AcquisitionProjectId, a.Remarks };
}
