using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

internal static class LandRecordsStaffModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<Award>().Property(x => x.Revision).IsConcurrencyToken().HasDefaultValue(0);
        b.Entity<Notification>().Property(x => x.Revision).IsConcurrencyToken().HasDefaultValue(0);
        b.Entity<Notification>().Property(x => x.IsFinalized).HasDefaultValue(false);
        b.Entity<Notification>().HasIndex(x => new { x.SectionType, x.NotificationNumber, x.NotificationDate }).IsUnique()
            .HasFilter("\"NotificationNumber\" <> ''");
        b.Entity<DocumentAward>().Property(x => x.Revision).IsConcurrencyToken().HasDefaultValue(0);
        b.Entity<DocumentAward>().HasOne(x => x.Village).WithMany().HasForeignKey(x => x.VillageId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentAward>().HasOne(x => x.PossessionEvent).WithMany().HasForeignKey(x => x.PossessionEventId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DocumentAward>().HasIndex(x => x.PossessionEventId).IsUnique();
        b.Entity<TemporaryAcquisitionProceeding>().ToTable("TemporaryAcquisitionProceedings");
        b.Entity<TemporaryAcquisitionProceeding>().Property(x => x.RecordStatus).HasConversion<string>();
        b.Entity<TemporaryAcquisitionProceeding>().HasIndex(x => new { x.DocumentId, x.VillageId }).IsUnique();
        b.Entity<LandRecordMutationReceipt>().ToTable("LandRecordMutationReceipts");
        b.Entity<LandRecordMutationReceipt>().Property(x => x.Scope).HasMaxLength(200);
        b.Entity<LandRecordMutationReceipt>().Property(x => x.RequestHash).HasMaxLength(64);
        b.Entity<LandRecordMutationReceipt>().HasIndex(x => new { x.Scope, x.MutationId }).IsUnique();
    }

    public static void BeforeSave(LacDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State == EntityState.Modified &&
                     x.Entity is Award or Notification or DocumentAward))
        {
            var revision = entry.Property("Revision");
            revision.CurrentValue = (int)revision.OriginalValue! + 1;
        }
    }
}
