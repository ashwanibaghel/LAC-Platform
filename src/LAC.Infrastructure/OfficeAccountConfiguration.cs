using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public static class OfficeAccountConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<AppUser>().Property(x => x.CustomDesignation).HasMaxLength(200);
        b.Entity<AppUser>().Property(x => x.OfficeRevision).IsConcurrencyToken();
        b.Entity<AppUser>().Property(x => x.LandAccess).HasConversion<string>();
        b.Entity<AppUser>().ToTable(t => t.HasCheckConstraint("CK_AppUser_DesignationChoice",
            "\"CustomDesignation\" IS NULL OR (\"DesignationId\" IS NULL AND length(btrim(\"CustomDesignation\")) BETWEEN 1 AND 200)"));
        b.Entity<OfficeModuleMembership>().Property(x => x.Module).HasConversion<string>();
        b.Entity<OfficeModuleMembership>().HasOne(x => x.User).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<OfficeModuleMembership>().HasIndex(x => new { x.UserId, x.Module }).IsUnique().HasFilter("\"RecordStatus\" = 'Active'");
    }
}
