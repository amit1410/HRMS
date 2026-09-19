using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class PayrollPeriodConfiguration : IEntityTypeConfiguration<PayrollPeriod>
{
    public void Configure(EntityTypeBuilder<PayrollPeriod> b)
    {
        b.ToTable("PayrollPeriods"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(50).IsRequired(); b.Property(x => x.Name).HasMaxLength(150).IsRequired(); b.Property(x => x.PeriodType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.StartDate, x.EndDate }); b.HasIndex(x => new { x.TenantId, x.Status }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1);
    }
}

public sealed class PayrollPeriodHistoryConfiguration : IEntityTypeConfiguration<PayrollPeriodHistory>
{
    public void Configure(EntityTypeBuilder<PayrollPeriodHistory> b)
    {
        b.ToTable("PayrollPeriodHistories"); b.HasKey(x => x.Id); b.Property(x => x.ChangeType).HasMaxLength(40).IsRequired(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.SnapshotJson).HasMaxLength(4000).IsRequired(); b.HasIndex(x => new { x.TenantId, x.PayrollPeriodId, x.ChangedAtUtc }); b.HasOne(x => x.PayrollPeriod).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "PayrollPeriodId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ActorUser).WithMany().HasForeignKey(new[] { "TenantId", "ActorUserId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> b)
    {
        b.ToTable("PayrollRuns"); b.HasKey(x => x.Id); b.Property(x => x.RunNumber).HasMaxLength(60).IsRequired(); b.Property(x => x.RunType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.Notes).HasMaxLength(1000); b.HasIndex(x => new { x.TenantId, x.RunNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.PayrollPeriodId }); b.HasIndex(x => new { x.TenantId, x.Status }); b.HasOne(x => x.PayrollPeriod).WithMany(x => x.Runs).HasForeignKey(new[] { "TenantId", "PayrollPeriodId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1);
    }
}

public sealed class PayrollRunEmployeeConfiguration : IEntityTypeConfiguration<PayrollRunEmployee>
{
    public void Configure(EntityTypeBuilder<PayrollRunEmployee> b)
    {
        b.ToTable("PayrollRunEmployees"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ExclusionReason).HasMaxLength(250); b.HasIndex(x => new { x.TenantId, x.PayrollRunId, x.EmployeeId }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EmployeeId }); b.HasIndex(x => x.EmployeeSalaryAssignmentId); b.HasOne(x => x.PayrollRun).WithMany(x => x.Employees).HasForeignKey(new[] { "TenantId", "PayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.EmployeeSalaryAssignment).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeSalaryAssignmentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollRunHistoryConfiguration : IEntityTypeConfiguration<PayrollRunHistory>
{
    public void Configure(EntityTypeBuilder<PayrollRunHistory> b)
    {
        b.ToTable("PayrollRunHistories"); b.HasKey(x => x.Id); b.Property(x => x.ChangeType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.Reason).HasMaxLength(500); b.HasIndex(x => new { x.TenantId, x.PayrollRunId, x.ChangedAtUtc }); b.HasOne(x => x.PayrollRun).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "PayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ActorUser).WithMany().HasForeignKey(new[] { "TenantId", "ActorUserId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
