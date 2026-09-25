using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class OvertimePolicyConfiguration : IEntityTypeConfiguration<OvertimePolicy>
{
    public void Configure(EntityTypeBuilder<OvertimePolicy> b)
    {
        b.ToTable("OvertimePolicies"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.Code).HasMaxLength(50).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.EligibilityMode).HasMaxLength(50).IsRequired(); b.Property(x => x.RoundingMode).HasConversion<int>();
        b.Property(x => x.NormalDayMultiplier).HasPrecision(10, 4); b.Property(x => x.WeekOffMultiplier).HasPrecision(10, 4); b.Property(x => x.HolidayMultiplier).HasPrecision(10, 4); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EffectiveFrom, x.EffectiveTo }); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OvertimeRequestConfiguration : IEntityTypeConfiguration<OvertimeRequest>
{
    public void Configure(EntityTypeBuilder<OvertimeRequest> b)
    {
        b.ToTable("OvertimeRequests"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.TenantId, x.Id }); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.Category).HasConversion<int>(); b.Property(x => x.Reason).HasMaxLength(2000); b.Property(x => x.RejectionReason).HasMaxLength(2000); b.Property(x => x.CorrectionReason).HasMaxLength(2000); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.WorkDate, x.Status }); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Policy).WithMany().HasForeignKey(x => new { x.TenantId, x.PolicyId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EmployeeMonthlyOvertimeConfiguration : IEntityTypeConfiguration<EmployeeMonthlyOvertime>
{
    public void Configure(EntityTypeBuilder<EmployeeMonthlyOvertime> b)
    {
        b.ToTable("EmployeeMonthlyOvertimes"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.TenantId, x.Id }); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken(); b.HasIndex(x => new { x.TenantId, x.AttendancePeriodId, x.EmployeeId, x.Version }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.AttendancePeriodId, x.EmployeeId, x.IsCurrent }).IsUnique(); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.AttendanceSnapshot).WithMany().HasForeignKey(x => new { x.TenantId, x.AttendanceSnapshotId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollOvertimeSnapshotConfiguration : IEntityTypeConfiguration<PayrollOvertimeSnapshot>
{
    public void Configure(EntityTypeBuilder<PayrollOvertimeSnapshot> b)
    {
        b.ToTable("PayrollOvertimeSnapshots"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.TenantId, x.Id }); b.Property(x => x.NormalDayMultiplier).HasPrecision(10, 4); b.Property(x => x.WeekOffMultiplier).HasPrecision(10, 4); b.Property(x => x.HolidayMultiplier).HasPrecision(10, 4); b.Property(x => x.ReopenReason).HasMaxLength(2000); b.HasIndex(x => new { x.TenantId, x.AttendancePeriodId, x.EmployeeId, x.Version }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.AttendancePeriodId, x.EmployeeId, x.IsCurrent }).IsUnique(); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.AttendancePeriod).WithMany().HasForeignKey(x => new { x.TenantId, x.AttendancePeriodId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne<PayrollAttendanceSnapshot>().WithMany().HasForeignKey(x => new { x.TenantId, x.AttendanceSnapshotId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
