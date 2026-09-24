using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class AttendancePeriodConfiguration : IEntityTypeConfiguration<AttendancePeriod>
{
    public void Configure(EntityTypeBuilder<AttendancePeriod> b)
    {
        b.ToTable("AttendancePeriods");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1).IsConcurrencyToken();
        b.Property(x => x.DataVersion).HasDefaultValue(1).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Year, x.Month }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.CreatedByUserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.LastProcessedByUserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AttendancePeriodEventConfiguration : IEntityTypeConfiguration<AttendancePeriodEvent>
{
    public void Configure(EntityTypeBuilder<AttendancePeriodEvent> b)
    {
        b.ToTable("AttendancePeriodEvents");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.EventType).HasConversion<int>().IsRequired();
        b.Property(x => x.Details).HasMaxLength(2000);
        b.HasIndex(x => new { x.TenantId, x.AttendancePeriodId, x.OccurredAtUtc, x.Id });
        b.HasOne(x => x.Period).WithMany(x => x.Events).HasForeignKey(x => new { x.TenantId, x.AttendancePeriodId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.ActorUserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EmployeeAttendanceMonthlySummaryConfiguration : IEntityTypeConfiguration<EmployeeAttendanceMonthlySummary>
{
    public void Configure(EntityTypeBuilder<EmployeeAttendanceMonthlySummary> b)
    {
        b.ToTable("EmployeeAttendanceMonthlySummaries");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.EmployeeCode).HasMaxLength(100);
        b.Property(x => x.EmployeeName).HasMaxLength(300).IsRequired();
        b.Property(x => x.SourceDataVersion).IsRequired();
        b.Property(x => x.PresentDayQuantity).HasPrecision(18, 4);
        b.Property(x => x.PaidLeaveDays).HasPrecision(18, 4);
        b.Property(x => x.UnpaidLeaveDays).HasPrecision(18, 4);
        b.Property(x => x.PayableDays).HasPrecision(18, 4);
        b.Property(x => x.LopDays).HasPrecision(18, 4);
        b.HasIndex(x => new { x.TenantId, x.AttendancePeriodId, x.EmployeeId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.AttendancePeriodId, x.ExceptionCount });
        b.HasOne<AttendancePeriod>().WithMany().HasForeignKey(x => new { x.TenantId, x.AttendancePeriodId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
