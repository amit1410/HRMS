using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class PayrollAttendanceSnapshotConfiguration : IEntityTypeConfiguration<PayrollAttendanceSnapshot>
{
    public void Configure(EntityTypeBuilder<PayrollAttendanceSnapshot> b)
    {
        b.ToTable("PayrollAttendanceSnapshots");
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.EligibleDays).HasPrecision(18, 4);
        b.Property(x => x.PayableDays).HasPrecision(18, 4);
        b.Property(x => x.LopDays).HasPrecision(18, 4);
        b.Property(x => x.PresentDays).HasPrecision(18, 4);
        b.Property(x => x.AbsentDays).HasPrecision(18, 4);
        b.Property(x => x.PaidLeaveDays).HasPrecision(18, 4);
        b.Property(x => x.UnpaidLeaveDays).HasPrecision(18, 4);
        b.Property(x => x.HolidayDays).HasPrecision(18, 4);
        b.Property(x => x.WeekOffDays).HasPrecision(18, 4);
        b.Property(x => x.OnDutyDays).HasPrecision(18, 4);
        b.Property(x => x.SourceHash).HasMaxLength(128);
        b.HasIndex(x => new { x.TenantId, x.AttendancePeriodId, x.EmployeeId, x.Version }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.AttendancePeriodId, x.EmployeeId, x.IsCurrent }).IsUnique();
        b.HasOne(x => x.AttendancePeriod).WithMany().HasForeignKey(x => new { x.TenantId, x.AttendancePeriodId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
