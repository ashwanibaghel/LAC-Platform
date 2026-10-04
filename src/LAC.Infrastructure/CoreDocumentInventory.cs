using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed record CoreDocumentEntry(Guid DocumentId, string? Role, string OriginalFileName, DateTimeOffset UploadedAt,
    string Status, string? MimeType, string ViewRoute, string DownloadRoute)
{
    public string? CoreDocumentRole => Role;
}
public sealed record CoreDocumentRoleInventory(string Role, int Count, bool Available, IReadOnlyList<CoreDocumentEntry> Documents);
public sealed record CoreAwardInventory(Guid Id, string AwardNumber, DateOnly? AwardDate, string? AwardType,
    IReadOnlyList<CoreDocumentRoleInventory> Roles, IReadOnlyList<CoreDocumentEntry> Documents);

public static class CoreDocumentInventory
{
    public static async Task<IReadOnlyList<CoreAwardInventory>> VillageAsync(LacDbContext db, Guid villageId, CancellationToken ct)
    {
        var awards = await db.Awards.AsNoTracking().Where(x => x.RecordStatus == RecordStatus.Active && x.VillageLinks.Any(v => v.VillageId == villageId))
            .OrderByDescending(x => x.AwardDate).ThenBy(x => x.AwardNumber).ToListAsync(ct);
        var awardIds = awards.Select(x => x.Id).ToList();
        var links = await db.DocumentAwards.AsNoTracking().Include(x => x.Document).Where(x => awardIds.Contains(x.AwardId) &&
            x.CoreDocumentRole != null && x.Document.RecordStatus == RecordStatus.Active && x.Document.Status == "Active")
            .OrderByDescending(x => x.Document.UploadedAt).ThenBy(x => x.DocumentId).ToListAsync(ct);
        return awards.Select(award =>
        {
            var documents = links.Where(x => x.AwardId == award.Id).Select(Entry).ToList();
            return new CoreAwardInventory(award.Id, award.AwardNumber, award.AwardDate, award.AwardType,
                CoreDocumentRoles.All.Select(role =>
                {
                    var group = documents.Where(x => x.Role == role).ToList();
                    return new CoreDocumentRoleInventory(role, group.Count, group.Count > 0, group);
                }).ToList(), documents);
        }).ToList();
    }

    public static async Task<IReadOnlyList<CoreDocumentEntry>> AwardAsync(LacDbContext db, Guid awardId, CancellationToken ct) =>
        (await db.DocumentAwards.AsNoTracking().Include(x => x.Document).Where(x => x.AwardId == awardId && x.CoreDocumentRole != null &&
            x.Document.RecordStatus == RecordStatus.Active && x.Document.Status == "Active")
            .OrderByDescending(x => x.Document.UploadedAt).ThenBy(x => x.DocumentId).ToListAsync(ct)).Select(Entry).ToList();

    private static CoreDocumentEntry Entry(DocumentAward link) => new(link.DocumentId, link.CoreDocumentRole,
        link.Document.OriginalFileName, link.Document.UploadedAt, link.Document.Status, link.Document.MimeType,
        $"/api/documents/{link.DocumentId}/content", $"/api/documents/{link.DocumentId}/content?download=true");
}
