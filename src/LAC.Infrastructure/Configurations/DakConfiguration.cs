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
            entity.ToTable(t => {
                t.HasCheckConstraint("CK_Daks_CustodyStates", "\"RoutingState\" IN ('Unassigned','WithHolder','InTransit','LegacyUnconfirmed') AND \"PhysicalState\" IN ('Unknown','NotPresent','AtRecordedLocation','Held','InTransit','ReturnPending') AND \"ProcessingCycle\" > 0");
                t.HasCheckConstraint("CK_Daks_Resolution", "\"Status\" <> 'Resolved' OR (\"ResolvedAt\" IS NOT NULL AND \"ResolvedByUserId\" IS NOT NULL AND \"ResolutionRemarks\" IS NOT NULL AND length(btrim(\"ResolutionRemarks\", E' \\t\\r\\n')) > 0)");
            });
            entity.Property(x => x.RoutingState).HasConversion<string>();
            entity.Property(x => x.PhysicalState).HasConversion<string>();
            entity.Property(x => x.ResolutionRemarks).HasMaxLength(1000);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.DiaryNumber);
            entity.Property(x => x.DiaryNumber).IsRequired();
            entity.Property(x => x.DiaryNumberKey).HasComputedColumnSql(DakDiaryNumber.SqlKey, stored: true).IsRequired();
            entity.HasIndex(x => x.DiaryNumberKey).IsUnique();
            entity.HasIndex(x => new { x.RegisteredByUserId, x.RegistrationRequestId }).IsUnique()
                .HasFilter("\"RegistrationRequestId\" IS NOT NULL");
            entity.ToTable(t => t.HasCheckConstraint("CK_Daks_DiaryNumber", "length(\"DiaryNumberKey\") > 0"));
            entity.HasOne(x => x.RegisteredByUser).WithMany().HasForeignKey(x => x.RegisteredByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PhysicalOriginalDesk).WithMany().HasForeignKey(x => x.PhysicalOriginalDeskId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PhysicalOriginalUser).WithMany().HasForeignKey(x => x.PhysicalOriginalUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PhysicalOriginalUpdatedByUser).WithMany().HasForeignKey(x => x.PhysicalOriginalUpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.PhysicalOriginalLocationNote).HasMaxLength(1000);
            entity.Property(x => x.PhysicalOriginalProvenanceNote).HasMaxLength(1000);
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
            entity.ToTable(t => t.HasCheckConstraint("CK_DakAssignments_ReceivedHolder", "\"ReceivedAt\" IS NULL OR \"AssignedUserId\" IS NOT NULL"));
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
            entity.HasOne<DakTransfer>().WithMany().HasForeignKey(x => new { x.TransferId, x.DakId })
                .HasPrincipalKey(x => new { x.Id, x.DakId }).OnDelete(DeleteBehavior.Restrict);
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

        b.Entity<OfficeDesk>().Property(x => x.Purpose).HasConversion<string>();
        b.Entity<DakTransfer>(e =>
        {
            e.HasAlternateKey(x => new { x.Id, x.DakId });
            e.Property(x => x.State).HasConversion<string>();
            e.Property(x => x.Purpose).HasConversion<string>();
            e.Property(x => x.FromStatus).HasConversion<string>();
            e.Property(x => x.FromRoutingState).HasConversion<string>();
            e.Property(x => x.DestinationKind).HasConversion<string>();
            e.Property(x => x.Remarks).HasMaxLength(1000);
            e.Property(x => x.Instructions).HasMaxLength(1000);
            e.Property(x => x.PullBackReason).HasMaxLength(1000);
            e.Property(x => x.PhysicalReturnProvenance).HasMaxLength(1000);
            e.HasOne(x => x.Dak).WithMany(x => x.Transfers).HasForeignKey(x => x.DakId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.FromHolderUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OfficeDesk>().WithMany().HasForeignKey(x => x.ToDeskId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OfficeDesk>().WithMany().HasForeignKey(x => x.FromDeskId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.DakId).IsUnique().HasFilter("\"State\" = 'Pending'").HasDatabaseName("IX_DakTransfers_OnePending");
            e.HasIndex(x => new { x.ToUserId, x.State });
            e.HasIndex(x => new { x.SenderUserId, x.State });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_DakTransfers_Dispatch", "\"Purpose\" IN ('Marked','Forwarded','Returned') AND \"DestinationKind\" IN ('Officer','RecordRoom') AND ((\"Purpose\" = 'Marked' AND \"FromHolderUserId\" IS NULL) OR (\"Purpose\" <> 'Marked' AND \"FromHolderUserId\" IS NOT NULL AND \"SenderUserId\" = \"FromHolderUserId\"))");
                t.HasCheckConstraint("CK_DakTransfers_State", "(\"State\" = 'Pending' AND \"ReceivedAt\" IS NULL AND \"PulledBackAt\" IS NULL) OR (\"State\" = 'Received' AND \"ReceivedAt\" IS NOT NULL AND \"ReceivedAt\" >= \"SentAt\" AND \"PulledBackAt\" IS NULL) OR (\"State\" = 'PulledBack' AND \"PulledBackAt\" IS NOT NULL AND \"PulledBackAt\" >= \"SentAt\" AND \"ReceivedAt\" IS NULL AND \"PullBackReason\" IS NOT NULL AND length(btrim(\"PullBackReason\", E' \\t\\r\\n')) > 0)");
                t.HasCheckConstraint("CK_DakTransfers_Physical", "(\"PhysicalReceivedAt\" IS NULL OR (\"IncludesPhysicalOriginal\" AND \"State\" = 'Received' AND \"PhysicalReceivedAt\" = \"ReceivedAt\")) AND (\"PhysicalReturnedAt\" IS NULL OR (\"IncludesPhysicalOriginal\" AND \"State\" = 'PulledBack' AND \"PhysicalReturnedAt\" >= \"PulledBackAt\" AND \"PhysicalReturnProvenance\" IS NOT NULL AND length(btrim(\"PhysicalReturnProvenance\", E' \\t\\r\\n')) > 0)) AND (NOT \"IncludesPhysicalOriginal\" OR \"State\" <> 'Received' OR \"PhysicalReceivedAt\" IS NOT NULL)");
            });
        });
        b.Entity<DakWorkflowCommandReceipt>(e =>
        {
            e.ToTable(t => t.HasCheckConstraint("CK_DakWorkflowCommands_RequestId", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
            e.HasIndex(x => new { x.ActorUserId, x.RequestId }).IsUnique();
            e.Property(x => x.PayloadHash).HasMaxLength(64);
            e.HasOne<Dak>().WithMany().HasForeignKey(x => x.DakId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
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
