namespace LAC.Infrastructure.Configurations;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public static class OutwardModelConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        // 1. Outward (Aggregate Root)
        b.Entity<Outward>(entity =>
        {
            // Unconditional unique index on canonical normalized number across all records (no RecordStatus filter)
            entity.HasIndex(x => x.NormalizedOutwardNumber).IsUnique();
            entity.HasIndex(x => x.OutwardNumber);
            entity.HasIndex(x => new { x.Status, x.OutwardDate });
            entity.HasIndex(x => x.IssuingDeskId);
            entity.HasIndex(x => x.WorkstreamId);
            entity.HasIndex(x => x.MatterId);

            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.Revision).IsConcurrencyToken().HasDefaultValue(0);

            entity.HasOne(x => x.IssuingDesk)
                .WithMany()
                .HasForeignKey(x => x.IssuingDeskId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Workstream)
                .WithMany()
                .HasForeignKey(x => x.WorkstreamId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.MainDocument)
                .WithMany()
                .HasForeignKey(x => x.MainDocumentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.DispatchedByUser)
                .WithMany()
                .HasForeignKey(x => x.DispatchedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CancelledByUser)
                .WithMany()
                .HasForeignKey(x => x.CancelledByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Matter)
                .WithMany()
                .HasForeignKey(x => x.MatterId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 2. OutwardEvent (Append-Only Event Journal)
        b.Entity<OutwardEvent>(entity =>
        {
            entity.HasIndex(x => new { x.OutwardId, x.SequenceNumber }).IsUnique();
            entity.Property(x => x.Action).HasConversion<string>();

            entity.HasOne(x => x.Outward)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.OutwardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ActionByUser)
                .WithMany()
                .HasForeignKey(x => x.ActionByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 3. OutwardDakLink (Sole Inward Dak Cross-Reference Entity)
        b.Entity<OutwardDakLink>(entity =>
        {
            entity.HasIndex(x => new { x.OutwardId, x.DakId })
                .IsUnique()
                .HasFilter("\"RecordStatus\" = 'Active'");

            // Partial unique index enforcing at most one active primary Dak link per Outward
            entity.HasIndex(x => x.OutwardId)
                .IsUnique()
                .HasFilter("\"IsPrimary\" = true AND \"RecordStatus\" = 'Active'");

            entity.HasIndex(x => x.DakId);

            entity.HasOne(x => x.Outward)
                .WithMany(x => x.DakLinks)
                .HasForeignKey(x => x.OutwardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Dak)
                .WithMany()
                .HasForeignKey(x => x.DakId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 4. OutwardAttachment (Supporting Enclosures)
        b.Entity<OutwardAttachment>(entity =>
        {
            entity.HasIndex(x => new { x.OutwardId, x.DocumentId })
                .IsUnique()
                .HasFilter("\"RecordStatus\" = 'Active'");

            entity.HasOne(x => x.Outward)
                .WithMany(x => x.Attachments)
                .HasForeignKey(x => x.OutwardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Document)
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
