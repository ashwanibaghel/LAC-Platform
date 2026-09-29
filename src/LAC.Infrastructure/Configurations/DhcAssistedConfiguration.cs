using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure.Configurations;

public static class DhcAssistedConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<DhcAssistedSyncRun>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Phase).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.FailureMessage).HasMaxLength(500);
            e.HasIndex(x => new { x.Status, x.StartedAt });
            e.HasOne(x => x.StartedByUser).WithMany().HasForeignKey(x => x.StartedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<DhcAssistedSyncItem>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Reason).HasMaxLength(80);
            e.Property(x => x.FailureCode).HasMaxLength(80);
            e.Property(x => x.FailureMessage).HasMaxLength(500);
            e.HasIndex(x => new { x.RunId, x.QueueOrder }).IsUnique();
            e.HasOne(x => x.Run).WithMany(x => x.Items).HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CourtCase).WithMany().HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CourtExternalCaseStatusObservation>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ProviderCode).HasMaxLength(80);
            e.Property(x => x.NormalizedCaseIdentity).HasMaxLength(512);
            e.Property(x => x.RawCaseNumber).HasMaxLength(200);
            e.Property(x => x.RawDiaryNumber).HasMaxLength(200);
            e.Property(x => x.RawStatus).HasMaxLength(100);
            e.Property(x => x.RawListingDate).HasMaxLength(100);
            e.Property(x => x.RawCourtNumber).HasMaxLength(100);
            e.Property(x => x.SourceUrl).HasMaxLength(2048);
            e.Property(x => x.EvidenceSha256).HasMaxLength(64);
            e.Property(x => x.ParserVersion).HasMaxLength(80);
            e.Property(x => x.ReviewReason).HasMaxLength(500);
            e.HasIndex(x => new { x.CourtCaseId, x.ObservedAt });
            e.HasIndex(x => new { x.RunItemId, x.EvidenceSha256 }).IsUnique();
            e.HasOne(x => x.CourtCase).WithMany().HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RunItem).WithMany().HasForeignKey(x => x.RunItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CourtExternalOrderObservation>(e =>
        {
            e.Property(x => x.NormalizedCaseIdentity).HasMaxLength(512);
            e.Property(x => x.RawCaseNumber).HasMaxLength(200);
            e.Property(x => x.RawOrderDate).HasMaxLength(100);
            e.Property(x => x.RawUploadDate).HasMaxLength(100);
            e.Property(x => x.OfficialUrl).HasMaxLength(2048);
            e.Property(x => x.CorrigendumUrl).HasMaxLength(2048);
            e.Property(x => x.SourceUrl).HasMaxLength(2048);
            e.Property(x => x.EvidenceSha256).HasMaxLength(64);
            e.Property(x => x.ParserVersion).HasMaxLength(80);
            e.HasIndex(x => new { x.CourtCaseId, x.ObservedAt });
            e.HasIndex(x => new { x.RunItemId, x.EvidenceSha256 }).IsUnique();
            e.HasOne(x => x.CourtCase).WithMany().HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RunItem).WithMany().HasForeignKey(x => x.RunItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CourtExternalAssistedDecision>(e =>
        {
            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasIndex(x => new { x.ObservationId, x.DecidedAt });
            e.HasOne(x => x.Observation).WithMany().HasForeignKey(x => x.ObservationId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
