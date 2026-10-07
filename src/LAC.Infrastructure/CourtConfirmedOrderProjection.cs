using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed record ConfirmedCourtOrder(Guid CourtCaseId,string CaseNumber,Guid CourtOrderIntelligenceId,
    DateOnly OrderDate,string OfficialUrl,string SourceKind,Guid RevisionId,string PdfSha256,string ExtractedEntityId,
    string Origin,Guid? ReviewedByUserId,DateTimeOffset? ReviewedAt,string? ReviewReason);

public sealed class CourtConfirmedOrderProjection(LacDbContext db,ICourtAuthorizationService auth)
{
    public async Task<IReadOnlyList<ConfirmedCourtOrder>> ReadAsync(string type,Guid targetId,Guid actor,CancellationToken ct=default)
    {
        var allowed=type switch {
            "Village" => await auth.CanAccessVillageAsync(targetId,actor,ct),
            "Award" => await auth.CanAccessAwardAsync(targetId,actor,ct),
            "Khasra" => await auth.CanAccessKhasraAsync(targetId,actor,ct), _ => false };
        if(!allowed || !await auth.HasCourtViewPermissionAsync(actor,ct))throw new CourtWorkflowException("Forbidden",403);
        var links=db.CourtOrderRecordLinks.AsNoTracking().Where(l=>l.MatchState==CourtRecordMatchState.Confirmed && l.Revision.Order.CourtCase.RecordStatus==RecordStatus.Active);
        links=type switch {"Village"=>links.Where(l=>l.VillageId==targetId),"Award"=>links.Where(l=>l.AwardId==targetId),_=>links.Where(l=>l.KhasraId==targetId)};
        var rows=await links.OrderByDescending(l=>l.Revision.Order.OrderDate).ThenByDescending(l=>l.ReviewedAt)
            .Select(l=>new ConfirmedCourtOrder(l.Revision.Order.CourtCaseId,l.Revision.Order.CourtCase.CaseNumber,l.Revision.CourtOrderIntelligenceId,
                l.Revision.Order.OrderDate,l.Revision.Order.OfficialUrl,l.Revision.Order.SourceKind,l.RevisionId,l.Revision.PdfSha256,l.ExtractedEntityId,
                l.Origin,l.ReviewedByUserId,l.ReviewedAt,l.ReviewReason)).ToListAsync(ct);
        var result=new List<ConfirmedCourtOrder>();
        foreach(var row in rows.DistinctBy(r=>r.CourtOrderIntelligenceId))
            if(await auth.CanViewCourtCaseAsync(row.CourtCaseId,actor,ct))result.Add(row);
        return result;
    }
}
