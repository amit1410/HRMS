using HRMS.Domain.Entities;
using HRMS.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

internal static class AttendanceConfigurationHelpers
{
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
        b.Property(x => x.StartTime).HasColumnType("time"); b.Property(x => x.EndTime).HasColumnType("time"); b.Property(x => x.CaptureMode).HasConversion<int>(); b.Property(x => x.CreatedBy).HasMaxLength(256); b.Property(x => x.ModifiedBy).HasMaxLength(256);
        b.HasIndex(x => new { x.TenantId, x.ShiftCode }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EffectiveFrom, x.EffectiveTo }); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
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
    public void Configure(EntityTypeBuilder<ShiftApplicabilityRule> b) { b.ToTable("ShiftApplicabilityRules"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b); b.Property(x => x.Gender).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.Priority, x.EffectiveFrom }); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Shift>().WithMany().HasForeignKey(x => new { x.TenantId, x.ShiftId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne<ShiftPattern>().WithMany().HasForeignKey(x => new { x.TenantId, x.ShiftPatternId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class EmployeeRosterDayConfiguration : IEntityTypeConfiguration<EmployeeRosterDay>
{
    public void Configure(EntityTypeBuilder<EmployeeRosterDay> b) { b.ToTable("EmployeeRosterDays"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b); b.Property(x => x.DayType).HasConversion<int>(); b.Property(x => x.AssignmentSource).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.RosterDate }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.RosterDate }); b.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne<Shift>().WithMany().HasForeignKey(x => new { x.TenantId, x.ShiftId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne<ShiftPattern>().WithMany().HasForeignKey(x => new { x.TenantId, x.ShiftPatternId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class EmployeeRosterChangeHistoryConfiguration : IEntityTypeConfiguration<EmployeeRosterChangeHistory>
{
    public void Configure(EntityTypeBuilder<EmployeeRosterChangeHistory> b) { b.ToTable("EmployeeRosterChangeHistories"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b); b.Property(x => x.PreviousDayType).HasConversion<int>(); b.Property(x => x.NewDayType).HasConversion<int>(); b.Property(x => x.Source).HasConversion<int>(); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.ChangedBy).HasMaxLength(256); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.RosterDate }); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class RosterUploadBatchConfiguration : IEntityTypeConfiguration<RosterUploadBatch>
{
    public void Configure(EntityTypeBuilder<RosterUploadBatch> b) { b.ToTable("RosterUploadBatches"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b); b.Property(x => x.FileName).HasMaxLength(260).IsRequired(); b.Property(x => x.Status).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.CreatedDate }); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasMany(x => x.Rows).WithOne(x => x.Batch).HasForeignKey(x => new { x.TenantId, x.RosterUploadBatchId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); }
}

public sealed class RosterUploadRowConfiguration : IEntityTypeConfiguration<RosterUploadRow>
{
    public void Configure(EntityTypeBuilder<RosterUploadRow> b) { b.ToTable("RosterUploadRows"); b.HasKey(x => x.Id); AttendanceConfigurationHelpers.Tenant(b); b.Property(x => x.EmployeeCode).HasMaxLength(100).IsRequired(); b.Property(x => x.ShiftCode).HasMaxLength(40); b.Property(x => x.DayType).HasConversion<int>(); b.Property(x => x.ErrorMessage).HasMaxLength(1000); b.HasIndex(x => new { x.TenantId, x.RosterUploadBatchId, x.RowNumber }).IsUnique(); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); }
}
