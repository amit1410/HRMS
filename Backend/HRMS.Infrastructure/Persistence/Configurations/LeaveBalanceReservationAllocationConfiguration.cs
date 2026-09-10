using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class LeaveBalanceReservationAllocationConfiguration : IEntityTypeConfiguration<LeaveBalanceReservationAllocation>
{
    public void Configure(EntityTypeBuilder<LeaveBalanceReservationAllocation> b)
    {
        b.ToTable("LeaveBalanceReservationAllocations"); b.HasKey(x => x.Id); b.Property(x => x.TenantId).IsRequired(); b.Property(x => x.ReservedQuantity).HasPrecision(9, 3).IsRequired(); b.Property(x => x.ConsumedQuantity).HasPrecision(9, 3).IsRequired(); b.Property(x => x.ReleasedQuantity).HasPrecision(9, 3).IsRequired(); b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id }); b.HasIndex(x => new { x.TenantId, x.LeaveRequestId, x.Status });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.LeaveRequest).WithMany().HasForeignKey(x => new { x.TenantId, x.LeaveRequestId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.LeaveEntitlementGrant).WithMany().HasForeignKey(x => new { x.TenantId, x.LeaveEntitlementGrantId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
