using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class LeaveAccrualOccurrenceConfiguration : IEntityTypeConfiguration<LeaveAccrualOccurrence>
{
    public void Configure(EntityTypeBuilder<LeaveAccrualOccurrence> b)
    {
        b.ToTable("LeaveAccrualOccurrences");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.AccrualFrequency).HasConversion<int>().IsRequired();
        b.Property(x => x.OccurrenceDate).HasColumnType("date").IsRequired();
        b.Property(x => x.OccurrenceKey).HasMaxLength(220).IsRequired();
        b.Property(x => x.CalculatedQuantity).HasPrecision(9, 3).IsRequired();
        b.Property(x => x.CreditedQuantity).HasPrecision(9, 3).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.FailureCode).HasMaxLength(200);
        b.Property(x => x.ClaimToken).HasMaxLength(64);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.OccurrenceKey }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status, x.OccurrenceDate });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeaveType).WithMany().HasForeignKey(x => new { x.TenantId, x.LeaveTypeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeavePeriod).WithMany().HasForeignKey(x => new { x.TenantId, x.LeavePeriodId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeavePolicyVersion).WithMany().HasForeignKey(x => new { x.TenantId, x.LeavePolicyVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeavePolicyRule).WithMany().HasForeignKey(x => new { x.TenantId, x.LeavePolicyRuleId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
