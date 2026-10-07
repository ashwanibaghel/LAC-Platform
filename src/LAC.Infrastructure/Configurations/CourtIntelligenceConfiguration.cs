using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure.Configurations;

public static class CourtIntelligenceConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<CourtOrderIntelligence>(e =>
        {
            e.Property(x => x.OfficialUrl).HasMaxLength(2048);
            e.Property(x => x.SourceKind).HasMaxLength(32);
            e.HasIndex(x => new { x.CourtCaseId, x.OrderDate, x.OfficialUrl }).IsUnique();
            e.HasOne(x => x.CourtCase).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CorrectsOrder).WithMany().HasForeignKey(x => x.CorrectsOrderId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CourtOrderIntelligenceRevision>(e =>
        {
            e.Property(x => x.StructuredFactsJson).HasColumnType("jsonb");
            e.Property(x => x.PdfSha256).HasMaxLength(64);
            e.Property(x => x.PayloadSha256).HasMaxLength(64);
            e.Property(x => x.Contract).HasMaxLength(64);
            e.Property(x => x.ExtractionState).HasMaxLength(32);
            e.Property(x => x.LacRelevanceState).HasMaxLength(32);
            e.Property(x => x.LacAuthorityScope).HasMaxLength(32);
            e.HasIndex(x => new { x.CourtOrderIntelligenceId, x.SourceObservationId, x.PayloadSha256 }).IsUnique();
            e.HasIndex(x => new { x.CourtOrderIntelligenceId, x.CreatedAt });
            e.HasOne(x => x.Order).WithMany(x => x.Revisions).HasForeignKey(x => x.CourtOrderIntelligenceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SourceObservation).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.ToTable("CourtOrderIntelligenceRevisions", t => t.HasCheckConstraint("CK_CourtScope_Actionable", "NOT \"LacActionable\" OR (\"LacRelevant\" AND \"LacAuthorityScope\" = 'ThisOffice' AND \"LacRelevanceState\" = 'Relevant')"));
        });
        b.Entity<CourtOrderRecordLink>(e =>
        {
            e.Property(x => x.EntityType).HasMaxLength(16);
            e.Property(x => x.ExtractedEntityId).HasMaxLength(128);
            e.Property(x => x.Origin).HasMaxLength(32);
            e.Property(x => x.MatchState).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne(x => x.Revision).WithMany(x => x.Links).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Village).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Award).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Khasra).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReviewedByUser).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.RevisionId, x.ExtractedEntityId }).IsUnique().HasFilter("\"MatchState\" = 'Confirmed'");
            e.HasIndex(x => new { x.VillageId, x.MatchState });
            e.HasIndex(x => new { x.AwardId, x.MatchState });
            e.HasIndex(x => new { x.KhasraId, x.MatchState });
            e.ToTable("CourtOrderRecordLinks", t =>
            {
                t.HasCheckConstraint("CK_CourtLink_Target", "(\"EntityType\" IN ('Village','Award','Khasra') AND \"MatchState\" IN ('NotMatched','NeedsReview') AND \"VillageId\" IS NULL AND \"AwardId\" IS NULL AND \"KhasraId\" IS NULL) OR (\"EntityType\" = 'Village' AND \"VillageId\" IS NOT NULL AND \"AwardId\" IS NULL AND \"KhasraId\" IS NULL) OR (\"EntityType\" = 'Award' AND \"AwardId\" IS NOT NULL AND \"VillageId\" IS NULL AND \"KhasraId\" IS NULL) OR (\"EntityType\" = 'Khasra' AND \"KhasraId\" IS NOT NULL AND \"VillageId\" IS NULL AND \"AwardId\" IS NULL)");
                t.HasCheckConstraint("CK_CourtLink_Review", "\"MatchState\" <> 'Confirmed' OR (\"ReviewedByUserId\" IS NOT NULL AND \"ReviewedAt\" IS NOT NULL AND \"ReviewReason\" IS NOT NULL AND length(trim(\"ReviewReason\")) > 0)");
            });
        });
    }
}
