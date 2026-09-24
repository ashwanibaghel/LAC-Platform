namespace LAC.Infrastructure.Configurations;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public static class MatterModelConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<MatterDraft>().HasOne(x => x.OfficeDocument).WithMany()
            .HasForeignKey(x => x.OfficeDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterDraft>().Property(x => x.OfficeKeyGeneration).HasDefaultValue(0);
        b.Entity<MatterDraft>().Property(x => x.LastOfficeSaveTokenHash).HasMaxLength(64);
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

        // 3. MatterDocumentExtract (Derived Document Lineage Provenance)
        b.Entity<MatterDocumentExtract>(entity =>
        {
            entity.HasOne(x => x.MatterDocument)
                .WithOne(x => x.ExtractProvenance)
                .HasForeignKey<MatterDocumentExtract>(x => x.MatterDocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.SourceDocument)
                .WithMany()
                .HasForeignKey(x => x.SourceDocumentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ExtractedByUser)
                .WithMany()
                .HasForeignKey(x => x.ExtractedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 4. MatterDocument (Matter <-> Document Relationship)
        b.Entity<MatterDocument>(entity =>
        {
            entity.HasIndex(x => new { x.MatterId, x.DocumentId });
            entity.Property(x => x.RecordStatus).HasConversion<string>().HasMaxLength(32);

            entity.HasOne(x => x.Matter)
                .WithMany(x => x.DocumentLinks)
                .HasForeignKey(x => x.MatterId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Document)
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
