namespace LAC.Infrastructure.Configurations;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public static class CourtCaseModelConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        // 1. CourtCase
        b.Entity<CourtCase>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.CaseNumber).HasMaxLength(128);
            entity.Property(x => x.CourtName).HasMaxLength(256);
            entity.Property(x => x.CaseTitle).HasMaxLength(500);
            entity.Property(x => x.CaseType).HasMaxLength(128);
            entity.Property(x => x.CurrentStatus).HasMaxLength(64);
            entity.Property(x => x.Remarks).HasMaxLength(2000);
            entity.Property(x => x.Revision).IsConcurrencyToken().HasDefaultValue(1);

            entity.HasIndex(x => x.CaseNumber);
            entity.HasIndex(x => x.CourtName);
            entity.HasIndex(x => x.CurrentStatus);
            entity.HasIndex(x => x.FiledDate);
            entity.HasIndex(x => x.DisposedDate);
            entity.HasIndex(x => x.ResponsibleOfficeDeskId);
            entity.HasIndex(x => x.AssignedUserId);

            entity.HasOne(x => x.ResponsibleOfficeDesk)
                .WithMany()
                .HasForeignKey(x => x.ResponsibleOfficeDeskId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.AssignedUser)
                .WithMany()
                .HasForeignKey(x => x.AssignedUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Awards)
                .WithOne(x => x.CourtCase)
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Khasras)
                .WithOne(x => x.CourtCase)
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Matters)
                .WithOne(x => x.CourtCase)
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Parties)
                .WithOne(x => x.CourtCase)
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Representatives)
                .WithOne(x => x.CourtCase)
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Documents)
                .WithOne(x => x.CourtCase)
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Proceedings)
                .WithOne(x => x.CourtCase)
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Events)
                .WithOne(x => x.CourtCase)
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 2. CourtProceeding
        b.Entity<CourtProceeding>(entity =>
        {
            entity.HasIndex(x => x.CourtCaseId);
            entity.HasIndex(x => new { x.CourtCaseId, x.ProceedingDate, x.CreatedAt });
            entity.Property(x => x.OrderType).HasMaxLength(128);
            entity.Property(x => x.RestraintNature).HasMaxLength(128);
            entity.Property(x => x.Summary).HasMaxLength(4000);
        });

        // 3. CourtCaseMatter
        b.Entity<CourtCaseMatter>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.CourtCaseId);
            entity.HasIndex(x => x.MatterId);
            entity.HasIndex(x => new { x.CourtCaseId, x.MatterId })
                .IsUnique()
                .HasFilter("\"RecordStatus\" = 'Active'");

            entity.HasOne(x => x.CourtCase)
                .WithMany(x => x.Matters)
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Matter)
                .WithMany(x => x.CourtCaseLinks)
                .HasForeignKey(x => x.MatterId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 4. CourtCaseParty
        b.Entity<CourtCaseParty>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.DisplayName).HasMaxLength(256);
            entity.Property(x => x.Role).HasMaxLength(100);
            entity.Property(x => x.FatherOrSpouseName).HasMaxLength(256);
            entity.Property(x => x.AddressText).HasMaxLength(1000);
            entity.Property(x => x.Remarks).HasMaxLength(1000);

            entity.HasIndex(x => x.CourtCaseId);
            entity.HasIndex(x => x.PartyId);

            entity.HasOne(x => x.CourtCase)
                .WithMany(x => x.Parties)
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Party)
                .WithMany()
                .HasForeignKey(x => x.PartyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 5. CourtCaseRepresentative
        b.Entity<CourtCaseRepresentative>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.DisplayName).HasMaxLength(256);
            entity.Property(x => x.RepresentativeType).HasMaxLength(64);
            entity.Property(x => x.RepresentsRole).HasMaxLength(100);
            entity.Property(x => x.ContactText).HasMaxLength(500);
            entity.Property(x => x.Remarks).HasMaxLength(1000);

            entity.HasIndex(x => x.CourtCaseId);
            entity.HasIndex(x => x.CourtCasePartyId);

            entity.HasOne(x => x.CourtCase)
                .WithMany(x => x.Representatives)
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CourtCaseParty)
                .WithMany()
                .HasForeignKey(x => x.CourtCasePartyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 6. CourtCaseDocument
        b.Entity<CourtCaseDocument>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.DocumentRole).HasMaxLength(100);
            entity.Property(x => x.DisplayName).HasMaxLength(256);

            entity.HasIndex(x => x.CourtCaseId);
            entity.HasIndex(x => x.DocumentId);
            entity.HasIndex(x => x.CourtProceedingId);
            entity.HasIndex(x => new { x.CourtCaseId, x.DocumentId })
                .IsUnique()
                .HasFilter("\"RecordStatus\" = 'Active'");

            entity.HasOne(x => x.CourtCase)
                .WithMany(x => x.Documents)
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Document)
                .WithMany(x => x.CourtCaseLinks)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CourtProceeding)
                .WithMany()
                .HasForeignKey(x => x.CourtProceedingId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 7. CourtCaseEvent (Append-only immutable official ledger)
        b.Entity<CourtCaseEvent>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Action).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.ActorDisplayNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.ActorDesignationSnapshot).HasMaxLength(256);
            entity.Property(x => x.CaseNumberSnapshot).HasMaxLength(128);
            entity.Property(x => x.CaseTitleSnapshot).HasMaxLength(500);
            entity.Property(x => x.WorkstreamNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.SourceDeskNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.SourceUserDisplayNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.TargetDeskNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.TargetUserDisplayNameSnapshot).HasMaxLength(256);
            entity.Property(x => x.OldStatus).HasMaxLength(64);
            entity.Property(x => x.NewStatus).HasMaxLength(64);
            entity.Property(x => x.Reason).HasMaxLength(1000);
            entity.Property(x => x.Notes).HasMaxLength(2000);

            entity.HasIndex(x => x.CourtCaseId);
            entity.HasIndex(x => x.ActionAt);
            entity.HasIndex(x => x.ActorUserId);
            entity.HasIndex(x => new { x.CourtCaseId, x.SequenceNumber }).IsUnique();

            entity.HasOne(x => x.CourtCase)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.CourtCaseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ActorUser)
                .WithMany()
                .HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
