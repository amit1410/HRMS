using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class EmployeeLeaveBalanceConfiguration : IEntityTypeConfiguration<EmployeeLeaveBalance>
{
    public void Configure(EntityTypeBuilder<EmployeeLeaveBalance> b)
    {
        b.ToTable("EmployeeLeaveBalances", t => t.HasCheckConstraint(
            "CK_EmployeeLeaveBalances_NonNegativeAndAvailable",
            "[GrantedQuantity] >= 0 AND [ReservedQuantity] >= 0 AND [ConsumedQuantity] >= 0 AND [ReservedQuantity] + [ConsumedQuantity] <= [GrantedQuantity]"));
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.GrantedQuantity).HasPrecision(9, 3).IsRequired();
        b.Property(x => x.ReservedQuantity).HasPrecision(9, 3).IsRequired();
        b.Property(x => x.ConsumedQuantity).HasPrecision(9, 3).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken().IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.LeaveTypeId, x.LeavePeriodId }).IsUnique();
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeaveType).WithMany().HasForeignKey(x => new { x.TenantId, x.LeaveTypeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeavePeriod).WithMany().HasForeignKey(x => new { x.TenantId, x.LeavePeriodId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeaveBalanceTransactionConfiguration : IEntityTypeConfiguration<LeaveBalanceTransaction>
{
    public void Configure(EntityTypeBuilder<LeaveBalanceTransaction> b)
    {
        b.ToTable("LeaveBalanceTransactions", t => t.HasCheckConstraint("CK_LeaveBalanceTransactions_PositiveQuantity", "[Quantity] > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.TransactionType).HasConversion<int>().IsRequired();
        b.Property(x => x.Quantity).HasPrecision(9, 3).IsRequired();
        b.Property(x => x.EffectiveDate).HasColumnType("date").IsRequired();
        b.Property(x => x.OccurredAtUtc).IsRequired();
        b.Property(x => x.SourceType).HasConversion<int>().IsRequired();
        b.Property(x => x.SourceReference).HasMaxLength(200);
        b.Property(x => x.ActorType).HasConversion<int>().IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(100);
        b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.PayloadFingerprint).HasMaxLength(64).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.LeaveRequestId, x.TransactionType })
            .IsUnique()
            .HasFilter("[LeaveRequestId] IS NOT NULL");
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.LeaveTypeId, x.LeavePeriodId, x.EffectiveDate });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.EmployeeLeaveBalance).WithMany(x => x.Transactions).HasForeignKey(x => new { x.TenantId, x.EmployeeLeaveBalanceId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeaveType).WithMany().HasForeignKey(x => new { x.TenantId, x.LeaveTypeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeavePeriod).WithMany().HasForeignKey(x => new { x.TenantId, x.LeavePeriodId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeavePolicyVersion).WithMany().HasForeignKey(x => new { x.TenantId, x.LeavePolicyVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeavePolicyRule).WithMany().HasForeignKey(x => new { x.TenantId, x.LeavePolicyRuleId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeaveRequest).WithMany().HasForeignKey(x => new { x.TenantId, x.LeaveRequestId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.ActorUserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.TenantId, x.ActorEmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
