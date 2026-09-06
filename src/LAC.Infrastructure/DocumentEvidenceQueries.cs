using System.Linq.Expressions;
using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed record EvidenceView(Guid Id,Guid DocumentId,string DocumentName,int PageNumber,int? PageEnd,string FactName,string ConfirmedValueJson,string? Snippet,DateTimeOffset VerifiedAt,string VerifiedBy,string SourceUrl);
public static class DocumentEvidenceQueries
{
    public static async Task<object> AwardDocumentsAsync(LacDbContext db,Guid awardId,CancellationToken ct)
    {
        return await db.DocumentAwards.AsNoTracking().Where(x=>x.AwardId==awardId).OrderByDescending(x=>x.Document.UploadedAt).Select(x=>new {
            x.Document.Id,x.Document.OriginalFileName,x.Document.DocumentType,x.Document.Sha256Hash,x.Document.FileSize,
            SourceUrl="/api/documents/"+x.DocumentId+"/content",
            Job=db.AwardDocumentExtractionJobs.Where(j=>j.DocumentId==x.DocumentId && j.TargetAwardId==awardId).OrderByDescending(j=>j.CreatedAt).Select(j=>new {j.Id,j.Status,j.ProcessedPages,j.TotalPages,j.StartedAt,j.CurrentStage,j.IngestionSessionId,j.ErrorMessage,
                Attention=db.AwardIngestionCandidates.Count(c=>c.SessionId==j.IngestionSessionId && c.VerifiedAt==null && !c.SafeToConfirm && c.Status!=AwardIngestionCandidateStatus.Committed && c.Status!=AwardIngestionCandidateStatus.Skipped),
                Reviewed=db.AwardIngestionCandidates.Any(c=>c.SessionId==j.IngestionSessionId) && !db.AwardIngestionCandidates.Any(c=>c.SessionId==j.IngestionSessionId && c.VerifiedAt==null && c.Status!=AwardIngestionCandidateStatus.Skipped && c.Status!=AwardIngestionCandidateStatus.Committed)
            }).FirstOrDefault(),
            Villages=x.Award.VillageLinks.Select(v=>new {v.VillageId,v.Village.Name}).ToList()
        }).ToListAsync(ct);
    }
    public static async Task<IngestionPage<EvidenceView>> ReadAsync(LacDbContext db,Expression<Func<SourceEvidence,bool>> target,int page,CancellationToken ct)
    {
        var query=db.SourceEvidence.AsNoTracking().Where(target);
        var count=await query.CountAsync(ct);
        var items=await query.OrderByDescending(x=>x.VerifiedAt).ThenBy(x=>x.Id).Skip(Math.Max(0,page)*50).Take(50).Select(x=>new EvidenceView(x.Id,x.DocumentId,x.Document.OriginalFileName,x.PageNumber,x.PageEnd,x.FactName,x.ConfirmedValueJson,x.ExtractedSnippet,x.VerifiedAt,x.VerifiedBy,"/api/documents/"+x.DocumentId+"/content#page="+x.PageNumber)).ToListAsync(ct);
        return new(items,Math.Max(0,page),50,count);
    }
}
