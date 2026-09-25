using HRMS.Domain.Entities;
using HRMS.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HRMS.Infrastructure.Persistence.Configurations;

internal static class AttendanceConfigurationHelpers
{
    private static readonly ValueConverter<TimeOnly, TimeSpan> TimeOnlyConverter = new(value => value.ToTimeSpan(), value => TimeOnly.FromTimeSpan(value));
    private static readonly ValueConverter<TimeOnly?, TimeSpan?> NullableTimeOnlyConverter = new(value => value.HasValue ? value.Value.ToTimeSpan() : null, value => value.HasValue ? TimeOnly.FromTimeSpan(value.Value) : null);

    public static PropertyBuilder<TimeOnly> Time(EntityTypeBuilder builder, string propertyName) =>
        builder.Property<TimeOnly>(propertyName).HasConversion(TimeOnlyConverter).HasColumnType("time");

    public static PropertyBuilder<TimeOnly?> NullableTime(EntityTypeBuilder builder, string propertyName) =>
        builder.Property<TimeOnly?>(propertyName).HasConversion(NullableTimeOnlyConverter).HasColumnType("time");

    public static void Tenant<T>(EntityTypeBuilder<T> b) where T : BaseEntity, ITenantEntity
    {
        b.Property(x => x.TenantId).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
    }
}

public sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> b)
    {
        b.ToTable("Shifts"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b);
        b.Property(x => x.ShiftCode).HasMaxLength(40).IsRequired(); b.Property(x => x.ShiftName).HasMaxLength(150).IsRequired(); b.Property(x => x.Description).HasMaxLength(1000);
        AttendanceConfigurationHelpers.Time(b, nameof(Shift.StartTime)); AttendanceConfigurationHelpers.Time(b, nameof(Shift.EndTime));
        AttendanceConfigurationHelpers.NullableTime(b, nameof(Shift.MandatoryStartTime)); AttendanceConfigurationHelpers.NullableTime(b, nameof(Shift.MandatoryEndTime));
        AttendanceConfigurationHelpers.NullableTime(b, nameof(Shift.StretchedStartTime)); AttendanceConfigurationHelpers.NullableTime(b, nameof(Shift.StretchedEndTime));
        b.Property(x => x.ShiftType).HasConversion<int>(); b.Property(x => x.CaptureMode).HasConversion<int>(); b.Property(x => x.AllowedAttendanceSources).HasConversion<int>(); b.Property(x => x.PrimaryAttendanceSource).HasConversion<int>(); b.Property(x => x.PostShiftMarkOutMode).HasConversion<int>(); b.Property(x => x.CreatedBy).HasMaxLength(256); b.Property(x => x.ModifiedBy).HasMaxLength(256);
        b.HasIndex(x => new { x.TenantId, x.ShiftCode }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EffectiveFrom, x.EffectiveTo }); b.HasIndex(x => new { x.TenantId, x.IsDefault, x.EffectiveFrom }); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Breaks).WithOne(x => x.Shift).HasForeignKey(x => new { x.TenantId, x.ShiftId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ShiftBreakConfiguration : IEntityTypeConfiguration<ShiftBreak>
{
    public void Configure(EntityTypeBuilder<ShiftBreak> b) { b.ToTable("ShiftBreaks"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b); b.Property(x => x.Name).HasMaxLength(100).IsRequired(); b.Property(x => x.Description).HasMaxLength(500); AttendanceConfigurationHelpers.Time(b, nameof(ShiftBreak.StartTime)); AttendanceConfigurationHelpers.Time(b, nameof(ShiftBreak.EndTime)); b.HasIndex(x => new { x.TenantId, x.ShiftId, x.Sequence }).IsUnique(); b.HasOne<Shift>().WithMany().HasForeignKey(x => new { x.TenantId, x.ShiftId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); }
}

public sealed class ShiftPatternConfiguration : IEntityTypeConfiguration<ShiftPattern>
{
    public void Configure(EntityTypeBuilder<ShiftPattern> b) { b.ToTable("ShiftPatterns"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b); b.Property(x => x.Code).HasMaxLength(40).IsRequired(); b.Property(x => x.Name).HasMaxLength(150).IsRequired(); b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasMany(x => x.Days).WithOne(x => x.ShiftPattern).HasForeignKey(x => new { x.TenantId, x.ShiftPatternId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); }
}

public sealed class ShiftPatternDayConfiguration : IEntityTypeConfiguration<ShiftPatternDay>
{
    public void Configure(EntityTypeBuilder<ShiftPatternDay> b) { b.ToTable("ShiftPatternDays"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b); b.Property(x => x.DayType).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.ShiftPatternId, x.SequenceDay }).IsUnique(); b.HasOne(x => x.Shift).WithMany().HasForeignKey(x => new { x.TenantId, x.ShiftId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class ShiftApplicabilityRuleConfiguration : IEntityTypeConfiguration<ShiftApplicabilityRule>
{
    public void Configure(EntityTypeBuilder<ShiftApplicabilityRule> b) { b.ToTable("ShiftApplicabilityRules"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b); b.Property(x => x.RuleName).HasMaxLength(200).IsRequired(); b.Property(x => x.Gender).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.Priority, x.EffectiveFrom }); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.EffectiveFrom }); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Shift>().WithMany().HasForeignKey(x => new { x.TenantId, x.ShiftId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne<ShiftPattern>().WithMany().HasForeignKey(x => new { x.TenantId, x.ShiftPatternId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class EmployeeRosterDayConfiguration : IEntityTypeConfiguration<EmployeeRosterDay>
{
    public void Configure(EntityTypeBuilder<EmployeeRosterDay> b) { b.ToTable("EmployeeRosterDays"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b); b.Property(x => x.DayType).HasConversion<int>(); b.Property(x => x.AssignmentSource).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.RosterDate }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.RosterDate }); b.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne<Shift>().WithMany().HasForeignKey(x => new { x.TenantId, x.ShiftId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne<ShiftPattern>().WithMany().HasForeignKey(x => new { x.TenantId, x.ShiftPatternId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class EmployeeRosterChangeHistoryConfiguration : IEntityTypeConfiguration<EmployeeRosterChangeHistory>
{
    public void Configure(EntityTypeBuilder<EmployeeRosterChangeHistory> b) { b.ToTable("EmployeeRosterChangeHistories"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b); b.Property(x => x.PreviousDayType).HasConversion<int>(); b.Property(x => x.NewDayType).HasConversion<int>(); b.Property(x => x.Source).HasConversion<int>(); b.Property(x => x.PreviousSource).HasConversion<int>(); b.Property(x => x.NewSource).HasConversion<int>(); b.Property(x => x.ChangeType).HasConversion<int>(); b.Property(x => x.OriginalCalendarDayType).HasConversion<int>(); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.ChangedBy).HasMaxLength(256); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.RosterDate }); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class RosterUploadBatchConfiguration : IEntityTypeConfiguration<RosterUploadBatch>
{
    public void Configure(EntityTypeBuilder<RosterUploadBatch> b) { b.ToTable("RosterUploadBatches"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b); b.Property(x => x.FileName).HasMaxLength(260).IsRequired(); b.Property(x => x.Status).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.CreatedDate }); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasMany(x => x.Rows).WithOne(x => x.Batch).HasForeignKey(x => new { x.TenantId, x.RosterUploadBatchId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); }
}

public sealed class RosterUploadRowConfiguration : IEntityTypeConfiguration<RosterUploadRow>
{
    public void Configure(EntityTypeBuilder<RosterUploadRow> b) { b.ToTable("RosterUploadRows"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b); b.Property(x => x.EmployeeCode).HasMaxLength(100).IsRequired(); b.Property(x => x.ShiftCode).HasMaxLength(40); b.Property(x => x.DayType).HasConversion<int>(); b.Property(x => x.ErrorMessage).HasMaxLength(1000); b.HasIndex(x => new { x.TenantId, x.RosterUploadBatchId, x.RowNumber }).IsUnique(); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class AttendancePunchConfiguration : IEntityTypeConfiguration<AttendancePunch>
{
    public void Configure(EntityTypeBuilder<AttendancePunch> b)
    {
        b.ToTable("AttendancePunches"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b);
        b.Property(x => x.Direction).HasConversion<int>(); b.Property(x => x.Source).HasConversion<int>();
        b.Property(x => x.ExternalPunchId).HasMaxLength(200); b.Property(x => x.DeviceId).HasMaxLength(200); b.Property(x => x.RawReference).HasMaxLength(1000);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.BusinessDate }); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.PunchAtUtc });
        b.HasIndex(x => new { x.TenantId, x.Source, x.ExternalPunchId }).IsUnique().HasFilter("ExternalPunchId IS NOT NULL");
        b.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EmployeeAttendanceDayConfiguration : IEntityTypeConfiguration<EmployeeAttendanceDay>
{
    public void Configure(EntityTypeBuilder<EmployeeAttendanceDay> b)
    {
        b.ToTable("EmployeeAttendanceDays"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b);
        b.Property(x => x.ShiftCode).HasMaxLength(40); b.Property(x => x.RosterAssignmentSource).HasConversion<int>(); b.Property(x => x.RosterDayType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ProcessingOutcome).HasMaxLength(1000);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.BusinessDate }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.BusinessDate });
        b.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Shift>().WithMany().HasForeignKey(x => new { x.TenantId, x.ShiftId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AttendanceExceptionResolutionConfiguration : IEntityTypeConfiguration<AttendanceExceptionResolution>
{
    public void Configure(EntityTypeBuilder<AttendanceExceptionResolution> b)
    {
        b.ToTable("AttendanceExceptionResolutions"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b);
        b.Property(x => x.ExceptionType).HasConversion<int>(); b.Property(x => x.Action).HasConversion<int>();
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.AttendanceDayId, x.AttendanceVersion, x.ExceptionType }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.ExceptionType, x.AttendanceVersion });
        b.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EmployeeAttendanceDay>().WithMany().HasForeignKey(x => new { x.TenantId, x.AttendanceDayId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
