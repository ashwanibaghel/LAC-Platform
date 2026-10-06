using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure.Configurations;

public static class WorkAllocationConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<AppUser>().HasOne(x => x.SupervisingOfficer).WithMany().HasForeignKey(x => x.SupervisingOfficerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AppUser>().Property(x => x.AssistantRevision).IsConcurrencyToken();
        b.Entity<AppUser>().Property(x => x.SessionVersion).IsConcurrencyToken();
        b.Entity<AppUser>().ToTable(t => t.HasCheckConstraint("CK_AppUser_Supervisor", "\"SupervisingOfficerId\" IS NULL OR \"SupervisingOfficerId\" <> \"Id\""));
        b.Entity<WorkDefinition>().HasIndex(x => x.Code).IsUnique();
        b.Entity<WorkDefinition>().Property(x => x.Code).HasMaxLength(64);
        b.Entity<WorkDefinition>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<WorkDefinition>().Property(x => x.Kind).HasConversion<string>();
        b.Entity<WorkDefinition>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<WorkDefinition>().HasOne(x => x.Workstream).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkAllocation>().HasOne(x => x.User).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkAllocation>().HasOne(x => x.WorkDefinition).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkAllocation>().HasOne(x => x.DelegatedFromAllocation).WithMany().HasForeignKey(x => x.DelegatedFromAllocationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkAllocation>().HasIndex(x => new { x.UserId, x.WorkDefinitionId, x.ValidFrom });
        b.Entity<WorkAllocation>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<WorkAllocation>().ToTable(t => t.HasCheckConstraint("CK_WorkAllocation_Interval", "\"ValidTo\" IS NULL OR \"ValidTo\" > \"ValidFrom\""));
        b.Entity<WorkAllocationScope>().Property(x => x.Kind).HasConversion<string>();
        b.Entity<WorkAllocationScope>().HasOne(x => x.WorkAllocation).WithMany(x => x.Scopes).HasForeignKey(x => x.WorkAllocationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkAllocationScope>().HasOne(x => x.District).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkAllocationScope>().HasOne(x => x.SubDivision).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkAllocationScope>().HasOne(x => x.Village).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkAllocationScope>().ToTable(t => t.HasCheckConstraint("CK_WorkAllocationScope_Target",
            "(\"Kind\" = 'Global' AND \"DistrictId\" IS NULL AND \"SubDivisionId\" IS NULL AND \"VillageId\" IS NULL) OR " +
            "(\"Kind\" = 'District' AND \"DistrictId\" IS NOT NULL AND \"SubDivisionId\" IS NULL AND \"VillageId\" IS NULL) OR " +
            "(\"Kind\" = 'Subdivision' AND \"DistrictId\" IS NULL AND \"SubDivisionId\" IS NOT NULL AND \"VillageId\" IS NULL) OR " +
            "(\"Kind\" = 'Village' AND \"DistrictId\" IS NULL AND \"SubDivisionId\" IS NULL AND \"VillageId\" IS NOT NULL)"));
        b.Entity<AssistantPermissionLimit>().HasKey(x => new { x.UserId, x.PermissionId });
        b.Entity<AssistantPermissionLimit>().HasOne(x => x.User).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<AssistantPermissionLimit>().HasOne(x => x.Permission).WithMany().OnDelete(DeleteBehavior.Restrict);
    }
}
