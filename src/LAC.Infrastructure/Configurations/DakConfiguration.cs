namespace LAC.Infrastructure.Configurations;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public static class DakModelConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        // 1. Dak Category
        b.Entity<DakCategory>(entity =>
        {
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.DefaultPriority).HasConversion<string>();
            entity.HasOne(x => x.DefaultWorkstream)
                .WithMany()
                .HasForeignKey(x => x.DefaultWorkstreamId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 2. Dak (Aggregate Root)
        b.Entity<Dak>(entity =>
        {
            entity.HasIndex(x => x.DiaryNumber);
            entity.HasIndex(x => new { x.Status, x.ReceivedDate });
            entity.HasIndex(x => x.Priority);
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.Priority).HasConversion<string>();
            entity.Property(x => x.Revision).IsConcurrencyToken().HasDefaultValue(0);

            entity.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Workstream)
                .WithMany()
                .HasForeignKey(x => x.WorkstreamId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.MainDocument)
                .WithMany()
                .HasForeignKey(x => x.MainDocumentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CurrentAssignment)
                .WithOne(x => x.Dak)
                .HasForeignKey<DakAssignment>(x => x.DakId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 3. Dak Assignment (Single Current Projection Row per Dak)
        b.Entity<DakAssignment>(entity =>
        {
            entity.HasIndex(x => x.DakId).IsUnique();
            entity.HasIndex(x => x.OfficeDeskId);
            entity.HasIndex(x => x.AssignedUserId);

            entity.HasOne(x => x.OfficeDesk)
                .WithMany()
                .HasForeignKey(x => x.OfficeDeskId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.AssignedUser)
                .WithMany()
                .HasForeignKey(x => x.AssignedUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.AssignedByUser)
                .WithMany()
                .HasForeignKey(x => x.AssignedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 4. Dak Movement (Append-Only Event Log)
        b.Entity<DakMovement>(entity =>
        {
            entity.HasIndex(x => new { x.DakId, x.SequenceNumber }).IsUnique();
            entity.HasIndex(x => new { x.DakId, x.ActionAt });
            entity.Property(x => x.Action).HasConversion<string>();

            entity.HasOne(x => x.Dak)
                .WithMany(x => x.Movements)
                .HasForeignKey(x => x.DakId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.FromDesk)
                .WithMany()
                .HasForeignKey(x => x.FromDeskId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.FromUser)
                .WithMany()
                .HasForeignKey(x => x.FromUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ToDesk)
                .WithMany()
                .HasForeignKey(x => x.ToDeskId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ToUser)
                .WithMany()
                .HasForeignKey(x => x.ToUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ActionByUser)
                .WithMany()
                .HasForeignKey(x => x.ActionByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Document)
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 5. Dak Attachment (OfficialRecord with Active Filtered Uniqueness)
        b.Entity<DakAttachment>(entity =>
        {
            entity.HasIndex(x => new { x.DakId, x.DocumentId })
                .IsUnique()
                .HasFilter("\"RecordStatus\" = 'Active'");

            entity.HasOne(x => x.Dak)
                .WithMany(x => x.Attachments)
                .HasForeignKey(x => x.DakId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Document)
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 6. Strongly-Typed Auditable Domain Links
        b.Entity<DakVillageLink>(entity =>
        {
            entity.HasIndex(x => new { x.DakId, x.VillageId })
                .IsUnique()
                .HasFilter("\"RecordStatus\" = 'Active'");

            entity.HasOne(x => x.Dak)
                .WithMany(x => x.VillageLinks)
                .HasForeignKey(x => x.DakId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Village)
                .WithMany()
                .HasForeignKey(x => x.VillageId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DakAwardLink>(entity =>
        {
            entity.HasIndex(x => new { x.DakId, x.AwardId })
                .IsUnique()
                .HasFilter("\"RecordStatus\" = 'Active'");

            entity.HasOne(x => x.Dak)
                .WithMany(x => x.AwardLinks)
                .HasForeignKey(x => x.DakId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Award)
                .WithMany()
                .HasForeignKey(x => x.AwardId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DakMatterLink>(entity =>
        {
            entity.HasIndex(x => new { x.DakId, x.MatterId })
                .IsUnique()
                .HasFilter("\"RecordStatus\" = 'Active'");

            entity.HasOne(x => x.Dak)
                .WithMany(x => x.MatterLinks)
                .HasForeignKey(x => x.DakId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Matter)
                .WithMany()
                .HasForeignKey(x => x.MatterId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DakKhasraLink>(entity =>
        {
            entity.HasIndex(x => new { x.DakId, x.KhasraId })
                .IsUnique()
                .HasFilter("\"RecordStatus\" = 'Active'");

            entity.HasOne(x => x.Dak)
                .WithMany(x => x.KhasraLinks)
                .HasForeignKey(x => x.DakId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Khasra)
                .WithMany()
                .HasForeignKey(x => x.KhasraId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
