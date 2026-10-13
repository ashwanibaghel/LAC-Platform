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
        b.Entity<MatterOfficeSession>().Property(x => x.CompositionVersion).HasDefaultValue("OfficeNoteV1");
        b.Entity<MatterOfficeSession>().ToTable("MatterOfficeSessions", t => t.HasCheckConstraint("CK_Office_Composition",
            "(\"CompositionVersion\" = 'OfficeNoteV1' AND \"HistoryManifestJson\" IS NULL AND \"HistoryManifestHash\" IS NULL AND \"HistoryManifestSeal\" IS NULL) OR " +
            "(\"CompositionVersion\" = 'ContinuousGreenEnvelopeV2' AND \"HistoryManifestJson\" IS NOT NULL AND \"HistoryManifestHash\" IS NOT NULL AND \"HistoryManifestHash\" ~ '^[0-9A-F]{64}$' AND \"HistoryManifestSeal\" IS NOT NULL)"));
        b.Entity<MatterOfficeSession>().HasOne<Matter>().WithMany().HasForeignKey(x => x.MatterId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSession>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSession>().HasOne<Dak>().WithMany().HasForeignKey(x => x.SourceDakId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSaveRequest>().HasIndex(x => new { x.ActorUserId, x.RequestId }).IsUnique();
        b.Entity<MatterOfficeSaveRequest>().ToTable("MatterOfficeSaveRequests", t => t.HasCheckConstraint("CK_Office_NativeCapture",
            "(\"NativeCaptureStoragePath\" IS NULL AND \"NativeCaptureSha256\" IS NULL AND \"NativeCaptureSize\" IS NULL AND \"NativeCanonicalHash\" IS NULL AND \"NativeContentHash\" IS NULL) OR " +
            "(\"NativeCaptureStoragePath\" IS NOT NULL AND \"NativeCaptureStoragePath\" <> '' AND \"NativeCaptureSha256\" IS NOT NULL AND \"NativeCaptureSha256\" ~ '^[0-9a-f]{64}$' AND \"NativeCaptureSize\" IS NOT NULL AND \"NativeCaptureSize\" BETWEEN 1 AND 5242880 AND \"NativeCanonicalHash\" IS NOT NULL AND \"NativeCanonicalHash\" ~ '^[0-9A-F]{64}$' AND \"NativeContentHash\" IS NOT NULL AND \"NativeContentHash\" ~ '^[0-9A-F]{64}$')"));
        b.Entity<MatterOfficeSaveRequest>().HasIndex(x => x.SessionId).IsUnique().HasFilter("\"State\" = 'Pending'");
        b.Entity<MatterOfficeSaveRequest>().HasOne<MatterOfficeSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSaveRequest>().HasOne<MatterOfficeSnapshot>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSnapshot>().HasIndex(x => new { x.SessionId, x.Version }).IsUnique();
        b.Entity<MatterOfficeSnapshot>().HasOne<MatterOfficeSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSnapshot>().HasOne<MatterOfficeSaveRequest>().WithMany().HasForeignKey(x => x.SaveRequestId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSnapshot>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficeSaveRequest>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficePreview>().HasOne<MatterOfficeSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MatterOfficePreview>().ToTable("MatterOfficePreviews", t=>t.HasCheckConstraint("CK_Office_PreviewNativeCapture",
            "\"NativeCaptureJson\" IS NULL OR (length(\"NativeCaptureJson\") <= 8192 AND jsonb_typeof(\"NativeCaptureJson\"::jsonb)='object' AND " +
            "\"NativeCaptureJson\"::jsonb ?& ARRAY['Version','StoragePath','Sha256','Size','CanonicalHash','ContentHash'] AND " +
            "jsonb_typeof(\"NativeCaptureJson\"::jsonb->'Version')='number' AND (\"NativeCaptureJson\"::jsonb->>'Version')='1' AND jsonb_typeof(\"NativeCaptureJson\"::jsonb->'StoragePath')='string' AND (\"NativeCaptureJson\"::jsonb->>'StoragePath')<>'' AND " +
            "jsonb_typeof(\"NativeCaptureJson\"::jsonb->'Sha256')='string' AND (\"NativeCaptureJson\"::jsonb->>'Sha256')~'^[0-9a-f]{64}$' AND " +
            "jsonb_typeof(\"NativeCaptureJson\"::jsonb->'Size')='number' AND (\"NativeCaptureJson\"::jsonb->>'Size')::bigint BETWEEN 1 AND 5242880 AND " +
            "jsonb_typeof(\"NativeCaptureJson\"::jsonb->'CanonicalHash')='string' AND (\"NativeCaptureJson\"::jsonb->>'CanonicalHash')=\"CanonicalHash\" AND " +
            "jsonb_typeof(\"NativeCaptureJson\"::jsonb->'ContentHash')='string' AND (\"NativeCaptureJson\"::jsonb->>'ContentHash')~'^[0-9A-F]{64}$')"));
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
