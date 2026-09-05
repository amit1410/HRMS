using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> b)
    {
        b.ToTable("LeaveRequests", t => t.HasCheckConstraint(
            "CK_LeaveRequests_DateAndQuantity",
            "[StartDate] <= [EndDate] AND [RequestedQuantity] >= 0 AND [ChargeableQuantity] >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.StartDate).HasColumnType("date").IsRequired();
        b.Property(x => x.EndDate).HasColumnType("date").IsRequired();
        b.Property(x => x.RequestedQuantity).HasPrecision(9, 3).IsRequired();
        b.Property(x => x.ChargeableQuantity).HasPrecision(9, 3).IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.PolicyGenderSnapshot).HasConversion<int>().IsRequired();
        b.Property(x => x.SubmittedAtUtc);
        b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.PayloadFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken().IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.StartDate, x.EndDate, x.Status });

        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeaveType).WithMany().HasForeignKey(x => new { x.TenantId, x.LeaveTypeId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeavePeriod).WithMany().HasForeignKey(x => new { x.TenantId, x.LeavePeriodId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeavePolicyVersion).WithMany().HasForeignKey(x => new { x.TenantId, x.LeavePolicyVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeavePolicyRule).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.LeavePolicyVersionId, x.LeavePolicyRuleId })
            .HasPrincipalKey(x => new { x.TenantId, x.LeavePolicyVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.EmployeeEmploymentHistory).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EmployeeId, x.EmployeeEmploymentHistoryId })
            .HasPrincipalKey(x => new { x.TenantId, x.EmployeeId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeaveRequestDayConfiguration : IEntityTypeConfiguration<LeaveRequestDay>
{
    public void Configure(EntityTypeBuilder<LeaveRequestDay> b)
    {
        b.ToTable("LeaveRequestDays", t => t.HasCheckConstraint(
            "CK_LeaveRequestDays_NonNegativeQuantity",
            "[RequestedQuantity] >= 0 AND [ChargeableQuantity] >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.Date).HasColumnType("date").IsRequired();
        b.Property(x => x.RequestedQuantity).HasPrecision(9, 3).IsRequired();
        b.Property(x => x.ChargeableQuantity).HasPrecision(9, 3).IsRequired();
        b.Property(x => x.DayClassification).HasMaxLength(50);
        b.Property(x => x.CalculationReason).HasMaxLength(200);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.LeaveRequestId, x.Date }).IsUnique();
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeaveRequest).WithMany(x => x.Days)
            .HasForeignKey(x => new { x.TenantId, x.LeaveRequestId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeaveRequestEventConfiguration : IEntityTypeConfiguration<LeaveRequestEvent>
{
    public void Configure(EntityTypeBuilder<LeaveRequestEvent> b)
    {
        b.ToTable("LeaveRequestEvents");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.EventType).HasConversion<int>().IsRequired();
        b.Property(x => x.OccurredAtUtc).IsRequired();
        b.Property(x => x.ActorType).HasConversion<int>().IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(100);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.LeaveRequestId, x.OccurredAtUtc, x.Id });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeaveRequest).WithMany(x => x.Events)
            .HasForeignKey(x => new { x.TenantId, x.LeaveRequestId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => new { x.TenantId, x.ActorUserId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ActorEmployee).WithMany().HasForeignKey(x => new { x.TenantId, x.ActorEmployeeId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
