using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class LeaveEntitlementGrantConfiguration : IEntityTypeConfiguration<LeaveEntitlementGrant>
{
    public void Configure(EntityTypeBuilder<LeaveEntitlementGrant> b)
    {
        b.ToTable("LeaveEntitlementGrants"); b.HasKey(x => x.Id); b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.SourceType).HasConversion<int>().IsRequired(); b.Property(x => x.SourceReference).HasMaxLength(220).IsRequired();
        b.Property(x => x.GrantedQuantity).HasPrecision(9, 3).IsRequired(); b.Property(x => x.ReservedQuantity).HasPrecision(9, 3).IsRequired(); b.Property(x => x.ConsumedQuantity).HasPrecision(9, 3).IsRequired(); b.Property(x => x.ExpiredQuantity).HasPrecision(9, 3).IsRequired();
        b.Property(x => x.GrantedOn).HasColumnType("date").IsRequired(); b.Property(x => x.ExpiresOn).HasColumnType("date"); b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id }); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.LeaveTypeId, x.LeavePeriodId, x.ExpiresOn, x.GrantedOn }); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.LeaveTypeId, x.LeavePeriodId, x.SourceType, x.SourceReference }).IsUnique();
        b.HasOne<LeaveBalanceTransaction>().WithMany().HasForeignKey(x => new { x.TenantId, x.LeaveBalanceTransactionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EmployeeLeaveBalance>().WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeLeaveBalanceId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
