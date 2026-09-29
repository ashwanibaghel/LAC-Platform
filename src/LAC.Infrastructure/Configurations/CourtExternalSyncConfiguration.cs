using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure.Configurations;

public static class CourtExternalSyncConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<CourtExternalSyncRun>().Property(x => x.Status).HasConversion<string>();
        b.Entity<CourtExternalSyncRun>().Property(x => x.Mode).HasConversion<string>();
        b.Entity<CourtExternalSyncRun>().Property(x => x.ProviderCode).HasMaxLength(80);
        b.Entity<CourtExternalSyncRun>().HasIndex(x => new { x.ProviderCode, x.StartedAt });

        b.Entity<CourtExternalSourceDocument>().Property(x => x.Kind).HasConversion<string>();
        b.Entity<CourtExternalSourceDocument>().Property(x => x.Status).HasConversion<string>();
        b.Entity<CourtExternalSourceDocument>().Property(x => x.ProviderCode).HasMaxLength(80);
        b.Entity<CourtExternalSourceDocument>().Property(x => x.SourceUrl).HasMaxLength(2048);
        b.Entity<CourtExternalSourceDocument>().Property(x => x.Sha256Hash).HasMaxLength(64);
        b.Entity<CourtExternalSourceDocument>().Property(x => x.LiveTargetSetFingerprint).HasMaxLength(64);
        b.Entity<CourtExternalSourceDocument>().HasIndex(x => new { x.ProviderCode, x.SourceUrl }).IsUnique();
        b.Entity<CourtExternalSourceDocument>().HasIndex(x => x.DocumentId);
        b.Entity<CourtExternalSourceDocument>().HasOne(x => x.Document).WithMany()
            .HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<CourtExternalListingObservation>().Property(x => x.Status).HasConversion<string>();
        b.Entity<CourtExternalListingObservation>().Property(x => x.Mode).HasConversion<string>();
        b.Entity<CourtExternalListingObservation>().Property(x => x.ProviderCode).HasMaxLength(80);
        b.Entity<CourtExternalListingObservation>().Property(x => x.NormalizedCaseIdentity).HasMaxLength(512);
        b.Entity<CourtExternalListingObservation>().HasIndex(x => new { x.SourceDocumentId, x.NormalizedCaseIdentity, x.ListingDate, x.Mode }).IsUnique();
        b.Entity<CourtExternalListingObservation>().HasIndex(x => new { x.CourtCaseId, x.Status, x.ObservedAt });
        b.Entity<CourtExternalListingObservation>().HasOne(x => x.SourceDocument).WithMany()
            .HasForeignKey(x => x.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CourtExternalListingObservation>().HasOne(x => x.CourtCase).WithMany()
            .HasForeignKey(x => x.CourtCaseId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<CourtExternalListingDecision>().Property(x => x.FromStatus).HasConversion<string>();
        b.Entity<CourtExternalListingDecision>().Property(x => x.ToStatus).HasConversion<string>();
        b.Entity<CourtExternalListingDecision>().HasIndex(x => new { x.ObservationId, x.DecidedAt });
        b.Entity<CourtExternalListingDecision>().HasOne(x => x.Observation).WithMany()
            .HasForeignKey(x => x.ObservationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CourtExternalListingDecision>().HasOne(x => x.ActorUser).WithMany()
            .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
