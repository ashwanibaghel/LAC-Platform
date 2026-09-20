namespace LAC.Infrastructure.Configurations;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public static class ScheduledEventModelConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        // 1. ScheduledEvent
        b.Entity<ScheduledEvent>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Title).HasMaxLength(500);
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property(x => x.CancellationReason).HasMaxLength(1000);
            entity.Property(x => x.CreatedByDisplayNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.CreatedByDesignationSnapshot).HasMaxLength(256);

            entity.Property(x => x.EventKind).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.Priority).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Origin).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Revision).IsConcurrencyToken().HasDefaultValue(1);

            entity.HasIndex(x => x.ScheduledDate);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.WorkstreamId);
            entity.HasIndex(x => x.ResponsibleOfficeDeskId);
            entity.HasIndex(x => x.AssignedUserId);
            entity.HasIndex(x => x.WorkItemId);
            entity.HasIndex(x => x.CourtCaseId);
            entity.HasIndex(x => x.CourtProceedingId);
            entity.HasIndex(x => x.LastActivityAt);
            entity.HasIndex(x => new { x.ScheduledDate, x.Status });

            // Prevent duplicate active CourtProceeding-derived schedule rows
            entity.HasIndex(x => x.CourtProceedingId)
                .IsUnique()
                .HasFilter("\"CourtProceedingId\" IS NOT NULL AND \"Status\" != 'Cancelled' AND \"RecordStatus\" = 'Active'");

            entity.HasOne(x => x.Workstream)
                .WithMany()
                .HasForeignKey(x => x.WorkstreamId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ResponsibleOfficeDesk)
                .WithMany()
                .HasForeignKey(x => x.ResponsibleOfficeDeskId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.AssignedUser)
                .WithMany()
                .HasForeignKey(x => x.AssignedUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Matter)
                .WithMany()
                .HasForeignKey(x => x.MatterId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Dak)
                .WithMany()
                .HasForeignKey(x => x.DakId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Outward)
                .WithMany()
                .HasForeignKey(x => x.OutwardId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.WorkItem)
                .WithMany()
                .HasForeignKey(x => x.WorkItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CourtCase)
                .WithMany()
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CourtProceeding)
                .WithMany()
                .HasForeignKey(x => x.CourtProceedingId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Reminders)
                .WithOne(x => x.ScheduledEvent)
                .HasForeignKey(x => x.ScheduledEventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.Events)
                .WithOne(x => x.ScheduledEvent)
                .HasForeignKey(x => x.ScheduledEventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 2. ScheduledReminder
        b.Entity<ScheduledReminder>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.CreatedByDisplayNameSnapshot).HasMaxLength(256);

            entity.HasIndex(x => x.ScheduledEventId);
            entity.HasIndex(x => new { x.ScheduledEventId, x.IsActive });

            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 3. ScheduledEventEvent (Append-only official history)
        b.Entity<ScheduledEventEvent>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Action).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.ActorDisplayNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.ActorDesignationSnapshot).HasMaxLength(256);
            entity.Property(x => x.WorkstreamNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.SourceDeskNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.TargetDeskNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.SourceUserDisplayNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.TargetUserDisplayNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.Notes).HasMaxLength(2000);

            entity.HasIndex(x => x.ScheduledEventId);
            entity.HasIndex(x => x.ActionAt);
            entity.HasIndex(x => x.ActorUserId);
            entity.HasIndex(x => x.WorkstreamIdSnapshot);
            entity.HasIndex(x => x.SourceDeskId);
            entity.HasIndex(x => x.TargetDeskId);
            entity.HasIndex(x => new { x.ScheduledEventId, x.SequenceNumber });

            entity.HasOne(x => x.ActorUser)
                .WithMany()
                .HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
