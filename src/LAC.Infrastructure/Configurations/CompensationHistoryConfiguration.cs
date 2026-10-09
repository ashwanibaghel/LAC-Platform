using LAC.Domain;
using Microsoft.EntityFrameworkCore;
namespace LAC.Infrastructure.Configurations;
public static class CompensationHistoryConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        var entity = model.Entity<CompensationHistory>();
        entity.HasKey(x => x.Id);
        entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.OwnerUserId, x.IdempotencyKey }).IsUnique();
        entity.HasIndex(x => new { x.OwnerUserId, x.CreatedAt, x.Id }).IsDescending(false, true, true);
        entity.Property(x => x.Title).HasMaxLength(200);
        entity.Property(x => x.SubmissionHash).HasMaxLength(64);
        entity.Property(x => x.OriginalAreaNotation).HasMaxLength(100);
        entity.Property(x => x.MarketRate).HasMaxLength(100);
        entity.Property(x => x.FinalAmount).HasMaxLength(100);
        entity.Property(x => x.CalculatorVersion).HasMaxLength(64);
        entity.Property(x => x.ConversionVersion).HasMaxLength(64);
        foreach (var name in new[] { "InputsJson", "RequestJson", "ResponseJson" }) entity.Property<string>(name).HasColumnType("jsonb");
    }
}
