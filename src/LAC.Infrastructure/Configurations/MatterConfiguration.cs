namespace LAC.Infrastructure.Configurations;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public static class MatterModelConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        // 1. Matter (Aggregate Root)
        b.Entity<Matter>(entity =>
        {
            entity.HasIndex(x => x.WorkstreamId);
            entity.Property(x => x.Revision).IsConcurrencyToken().HasDefaultValue(0);

            entity.HasOne(x => x.Workstream)
                .WithMany()
                .HasForeignKey(x => x.WorkstreamId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 2. MatterEvent (Append-Only Event Journal)
        b.Entity<MatterEvent>(entity =>
        {
            entity.HasIndex(x => new { x.MatterId, x.SequenceNumber }).IsUnique();
            entity.Property(x => x.Action).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.ActionByDisplayNameSnapshot).HasMaxLength(256);

            entity.HasOne(x => x.Matter)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.MatterId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ActionByUser)
                .WithMany()
                .HasForeignKey(x => x.ActionByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
