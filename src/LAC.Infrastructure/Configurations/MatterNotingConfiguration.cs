namespace LAC.Infrastructure.Configurations;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public static class MatterNotingConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<Dak>().Property(x => x.VillageClassification).HasConversion<string>().HasMaxLength(32)
            .HasDefaultValue(DakVillageClassification.Unclassified);
        b.Entity<Matter>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.NativeOwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterWorkingNote>().HasIndex(x => new { x.MatterId, x.AuthorUserId }).IsUnique();
        b.Entity<MatterWorkingNote>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<MatterOfficialNote>().HasIndex(x => new { x.MatterId, x.Number }).IsUnique();
        b.Entity<MatterOfficialNote>().ToTable("MatterOfficialNotes", t => t.HasCheckConstraint("CK_OfficialNote_Number", "\"Number\" > 0 AND \"Version\" = 1"));
        b.Entity<MatterNoteRemark>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<MatterNoteRemark>().HasIndex(x => new { x.NoteId, x.CreatedAt });
        b.Entity<MatterNoteRemark>().HasOne<MatterOfficialNote>().WithMany().HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterNoteRemark>().ToTable("MatterNoteRemarks", t => t.HasCheckConstraint("CK_Remark_State", "\"State\" IN ('Open','Addressed')"));
        b.Entity<MatterPdfAnnotation>().HasIndex(x => new { x.MatterId, x.DocumentId });
        b.Entity<MatterPdfAnnotation>().HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterPdfAnnotation>().ToTable("MatterPdfAnnotations", t => t.HasCheckConstraint("CK_Annotation_Page", "\"Page\" > 0"));
        b.Entity<MatterWorkflowReceipt>().HasIndex(x => new { x.ActorUserId, x.RequestId }).IsUnique();
        b.Entity<MatterWorkflowReceipt>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        foreach (var type in new[] { typeof(MatterWorkingNote), typeof(MatterOfficialNote), typeof(MatterNoteRemark), typeof(MatterPdfAnnotation) })
        {
            b.Entity(type).HasOne(typeof(Matter)).WithMany().HasForeignKey("MatterId").OnDelete(DeleteBehavior.Restrict);
            b.Entity(type).HasOne(typeof(AppUser)).WithMany().HasForeignKey("AuthorUserId").OnDelete(DeleteBehavior.Restrict);
        }
    }
}
