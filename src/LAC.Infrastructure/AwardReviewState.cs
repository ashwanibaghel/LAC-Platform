using LAC.Domain;

namespace LAC.Infrastructure;

/// <summary>Single query source for the durable Award document review states.</summary>
public static class AwardReviewState
{
    public static IQueryable<AwardIngestionCandidate> Pending(this IQueryable<AwardIngestionCandidate> query) =>
        query.Where(x => x.VerifiedAt == null &&
            x.Status != AwardIngestionCandidateStatus.Committed &&
            x.Status != AwardIngestionCandidateStatus.Skipped &&
            x.Status != AwardIngestionCandidateStatus.Rejected);

    public static IQueryable<AwardIngestionCandidate> Attention(this IQueryable<AwardIngestionCandidate> query) =>
        query.Pending()
            .Where(x => !x.SafeToConfirm);

    public static IQueryable<AwardIngestionCandidate> VerifiedWaiting(this IQueryable<AwardIngestionCandidate> query) =>
        query.Where(x => x.VerifiedAt != null && x.Status == AwardIngestionCandidateStatus.Ready);

    public static IQueryable<AwardIngestionCandidate> Committed(this IQueryable<AwardIngestionCandidate> query) =>
        query.Where(x => x.Status == AwardIngestionCandidateStatus.Committed);
}
