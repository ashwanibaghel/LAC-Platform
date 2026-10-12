namespace LAC.Infrastructure.Configurations;

using LAC.Domain;
using Microsoft.EntityFrameworkCore;

public static class MatterOfficeNotingConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<MatterOfficeSession>().HasIndex(x => x.DocumentKey).IsUnique();
        b.Entity<MatterOfficeSession>().HasIndex(x => new { x.MatterId, x.ActorUserId }).IsUnique().HasFilter("\"State\" IN ('Active','Frozen','Reviewed')");
        b.Entity<MatterOfficeSession>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<MatterOfficeSession>().HasOne<Matter>().WithMany().HasForeignKey(x => x.MatterId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSession>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSession>().HasOne<Dak>().WithMany().HasForeignKey(x => x.SourceDakId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSaveRequest>().HasIndex(x => new { x.ActorUserId, x.RequestId }).IsUnique();
        b.Entity<MatterOfficeSaveRequest>().HasIndex(x => x.SessionId).IsUnique().HasFilter("\"State\" = 'Pending'");
        b.Entity<MatterOfficeSaveRequest>().HasOne<MatterOfficeSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSaveRequest>().HasOne<MatterOfficeSnapshot>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSnapshot>().HasIndex(x => new { x.SessionId, x.Version }).IsUnique();
        b.Entity<MatterOfficeSnapshot>().HasOne<MatterOfficeSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSnapshot>().HasOne<MatterOfficeSaveRequest>().WithMany().HasForeignKey(x => x.SaveRequestId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSnapshot>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSaveRequest>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficePreview>().HasOne<MatterOfficeSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficePreview>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficePreview>().HasOne<Matter>().WithMany().HasForeignKey(x => x.MatterId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficePreview>().HasOne<Dak>().WithMany().HasForeignKey(x => x.SourceDakId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficePreview>().HasOne<MatterOfficeSnapshot>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficePreview>().HasIndex(x => new { x.SessionId, x.SnapshotId }).IsUnique();
        b.Entity<MatterOfficeNoteEvidence>().HasOne<MatterOfficialNote>().WithMany().HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeNoteEvidence>().HasOne<MatterOfficePreview>().WithMany().HasForeignKey(x => x.PreviewId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeNoteEvidence>().HasOne<MatterOfficeSnapshot>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeNoteEvidence>().HasIndex(x => x.NoteId).IsUnique();
        b.Entity<MatterWorkingNote>().HasOne<MatterOfficeSession>().WithMany().HasForeignKey(x => x.OfficeSessionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterWorkingNote>().HasOne<MatterOfficePreview>().WithMany().HasForeignKey(x => x.OfficePreviewId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterGreenSheetSource>().HasOne<Matter>().WithMany().HasForeignKey(x => x.MatterId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterGreenSheetSource>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterGreenSheetSource>().HasOne<MatterOfficePreview>().WithMany().HasForeignKey(x => x.WorkingPreviewId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterGreenSheet>().HasOne<MatterGreenSheetSource>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterGreenSheet>().HasIndex(x => x.SourceId).IsUnique();
        foreach (var type in new[] { typeof(MatterOfficeSession), typeof(MatterOfficeSaveRequest) })
            b.Entity(type).ToTable(type.Name + "s", t => t.HasCheckConstraint("CK_" + type.Name + "_State",
                type == typeof(MatterOfficeSession) ? "\"State\" IN ('Active','Frozen','Reviewed','Revoked','Submitted')" : "\"State\" IN ('Pending','Failed','Committed')"));
    }
}
