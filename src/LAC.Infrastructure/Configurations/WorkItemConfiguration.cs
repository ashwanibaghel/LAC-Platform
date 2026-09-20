namespace LAC.Infrastructure.Configurations;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public static class WorkItemModelConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        // 1. WorkItem (Aggregate Root)
        b.Entity<WorkItem>(entity =>
        {
            entity.HasIndex(x => x.WorkstreamId);
            entity.HasIndex(x => new { x.Status, x.DueAt });
            entity.HasIndex(x => x.RequestedByUserId);
            entity.HasIndex(x => x.LastActivityAt);
            entity.HasIndex(x => x.CreatedAt);

            entity.Property(x => x.Priority).HasConversion<string>();
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.Origin).HasConversion<string>();
            entity.Property(x => x.Revision).IsConcurrencyToken().HasDefaultValue(0);

            entity.HasOne(x => x.Workstream)
                .WithMany()
                .HasForeignKey(x => x.WorkstreamId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.RequestedByUser)
                .WithMany()
                .HasForeignKey(x => x.RequestedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CurrentAssignment)
                .WithOne(x => x.WorkItem)
                .HasForeignKey<WorkItemAssignment>(x => x.WorkItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 2. WorkItemAssignment (Single Current Responsibility Projection Row per WorkItem)
        b.Entity<WorkItemAssignment>(entity =>
        {
            entity.HasIndex(x => x.WorkItemId).IsUnique();
            entity.HasIndex(x => x.OfficeDeskId);
            entity.HasIndex(x => x.AssignedUserId);
            entity.HasIndex(x => new { x.OfficeDeskId, x.AssignedUserId, x.IsActive });

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

            entity.HasOne(x => x.FirstSeenByUser)
                .WithMany()
                .HasForeignKey(x => x.FirstSeenByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.FirstActionByUser)
                .WithMany()
                .HasForeignKey(x => x.FirstActionByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 3. WorkItemContributor
        b.Entity<WorkItemContributor>(entity =>
        {
            entity.HasIndex(x => new { x.WorkItemId, x.UserId })
                .IsUnique()
                .HasFilter("\"IsActive\" = true AND \"RecordStatus\" = 'Active'");
            entity.HasIndex(x => x.UserId);
            entity.Property(x => x.Status).HasConversion<string>();

            entity.HasOne(x => x.WorkItem)
                .WithMany(x => x.Contributors)
                .HasForeignKey(x => x.WorkItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.AddedByUser)
                .WithMany()
                .HasForeignKey(x => x.AddedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ReviewedByUser)
                .WithMany()
                .HasForeignKey(x => x.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 4. WorkItemUpdate (Append-only Progress Journal)
        b.Entity<WorkItemUpdate>(entity =>
        {
            entity.HasIndex(x => new { x.WorkItemId, x.AddedAt });
            entity.HasIndex(x => x.AddedByUserId);

            entity.HasOne(x => x.WorkItem)
                .WithMany(x => x.Updates)
                .HasForeignKey(x => x.WorkItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.AddedByUser)
                .WithMany()
                .HasForeignKey(x => x.AddedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 5. WorkItemAttachment
        b.Entity<WorkItemAttachment>(entity =>
        {
            entity.HasIndex(x => new { x.WorkItemId, x.DocumentId })
                .IsUnique()
                .HasFilter("\"RecordStatus\" = 'Active'");
            entity.HasIndex(x => x.DocumentId);

            entity.HasOne(x => x.WorkItem)
                .WithMany(x => x.Attachments)
                .HasForeignKey(x => x.WorkItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Document)
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.WorkItemUpdate)
                .WithMany()
                .HasForeignKey(x => x.WorkItemUpdateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 6. WorkItemMatterLink
        b.Entity<WorkItemMatterLink>(entity =>
        {
            entity.HasIndex(x => new { x.WorkItemId, x.MatterId })
                .IsUnique()
                .HasFilter("\"RecordStatus\" = 'Active'");
            entity.HasIndex(x => x.MatterId);

            entity.HasOne(x => x.WorkItem)
                .WithMany(x => x.MatterLinks)
                .HasForeignKey(x => x.WorkItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Matter)
                .WithMany()
                .HasForeignKey(x => x.MatterId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 7. WorkItemDakLink
        b.Entity<WorkItemDakLink>(entity =>
        {
            entity.HasIndex(x => new { x.WorkItemId, x.DakId })
                .IsUnique()
                .HasFilter("\"RecordStatus\" = 'Active'");
            entity.HasIndex(x => x.DakId);

            entity.HasOne(x => x.WorkItem)
                .WithMany(x => x.DakLinks)
                .HasForeignKey(x => x.WorkItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Dak)
                .WithMany()
                .HasForeignKey(x => x.DakId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 8. WorkItemEvent (Immutable Event Journal)
        b.Entity<WorkItemEvent>(entity =>
        {
            entity.HasIndex(x => new { x.WorkItemId, x.SequenceNumber }).IsUnique();
            entity.HasIndex(x => new { x.WorkItemId, x.ActionAt });
            entity.Property(x => x.Action).HasConversion<string>();
            entity.Property(x => x.FromStatus).HasConversion<string>();
            entity.Property(x => x.ToStatus).HasConversion<string>();

            entity.HasOne(x => x.WorkItem)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.WorkItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ActionByUser)
                .WithMany()
                .HasForeignKey(x => x.ActionByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
