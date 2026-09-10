using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class LeavePeriodCloseOccurrenceConfiguration : IEntityTypeConfiguration<LeavePeriodCloseOccurrence>
{
    public void Configure(EntityTypeBuilder<LeavePeriodCloseOccurrence> b)
    {
        b.ToTable("LeavePeriodCloseOccurrences"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired(); b.Property(x => x.OccurrenceKey).HasMaxLength(220).IsRequired();
        b.Property(x => x.ClosingQuantity).HasPrecision(9, 3).IsRequired(); b.Property(x => x.CarriedQuantity).HasPrecision(9, 3).IsRequired(); b.Property(x => x.LapsedQuantity).HasPrecision(9, 3).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired(); b.Property(x => x.FailureCode).HasMaxLength(200); b.Property(x => x.ClaimToken).HasMaxLength(64);
        b.HasAlternateKey(x => new { x.TenantId, x.Id }); b.HasIndex(x => new { x.TenantId, x.OccurrenceKey }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.Status, x.SourceLeavePeriodId });
    }
}
