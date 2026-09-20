namespace LAC.Infrastructure.Configurations;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public static class RecordAccessConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<RecordAccessEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ActorDisplayNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.ContextEntityType).HasMaxLength(64);
            entity.Property(x => x.DocumentTitleSnapshot).HasMaxLength(512);
            entity.Property(x => x.DeduplicationKey).HasMaxLength(256);

            entity.HasIndex(x => x.ActorUserId);
            entity.HasIndex(x => x.OccurredAt);
            entity.HasIndex(x => x.DocumentId);
            entity.HasIndex(x => x.DeduplicationKey)
                .IsUnique()
                .HasFilter("\"DeduplicationKey\" IS NOT NULL");

            entity.HasOne(x => x.ActorUser)
                .WithMany()
                .HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Document)
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Workstream)
                .WithMany()
                .HasForeignKey(x => x.WorkstreamId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.OfficeDesk)
                .WithMany()
                .HasForeignKey(x => x.OfficeDeskId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
