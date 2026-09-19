using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class EmployeeSalaryAssignmentConfiguration : IEntityTypeConfiguration<EmployeeSalaryAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeeSalaryAssignment> b)
    {
        b.ToTable("EmployeeSalaryAssignments"); b.HasKey(x => x.Id);
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        b.Property(x => x.PayFrequency).HasConversion<int>().IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.ChangeReason).HasConversion<int>().IsRequired();
        b.Property(x => x.AnnualCtc).HasPrecision(18, 6); b.Property(x => x.MonthlyCtc).HasPrecision(18, 6);
        b.Property(x => x.Remarks).HasMaxLength(2000);
        // The service validates the expected version and owns the increment. Keeping the token out of
        // provider-generated update predicates avoids SQLite/MySQL row-count differences.
        b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.EffectiveFrom });
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status, x.EffectiveFrom });
        b.HasIndex(x => new { x.TenantId, x.SalaryStructureId });
        b.HasIndex(x => new { x.TenantId, x.SalaryStructureVersionId });
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SalaryStructure).WithMany().HasForeignKey(new[] { "TenantId", "SalaryStructureId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SalaryStructureVersion).WithMany().HasForeignKey(new[] { "TenantId", "SalaryStructureVersionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EmployeeSalaryComponentConfiguration : IEntityTypeConfiguration<EmployeeSalaryComponent>
{
    public void Configure(EntityTypeBuilder<EmployeeSalaryComponent> b)
    {
        b.ToTable("EmployeeSalaryComponents"); b.HasKey(x => x.Id);
        b.Property(x => x.OverrideValue).HasPrecision(18, 6); b.Property(x => x.OverridePercentage).HasPrecision(9, 6);
        b.Property(x => x.OverrideFormula).HasMaxLength(2000); b.Property(x => x.Remarks).HasMaxLength(2000);
        b.HasIndex(x => new { x.TenantId, x.EmployeeSalaryAssignmentId, x.SalaryStructureComponentId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.EmployeeSalaryAssignmentId, x.EffectiveFrom });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Assignment).WithMany(x => x.Components).HasForeignKey(new[] { "TenantId", "EmployeeSalaryAssignmentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.SalaryStructureComponent).WithMany().HasForeignKey(new[] { "TenantId", "SalaryStructureComponentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SalaryComponent).WithMany().HasForeignKey(new[] { "TenantId", "SalaryComponentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EmployeeSalaryAssignmentHistoryConfiguration : IEntityTypeConfiguration<EmployeeSalaryAssignmentHistory>
{
    public void Configure(EntityTypeBuilder<EmployeeSalaryAssignmentHistory> b)
    {
        b.ToTable("EmployeeSalaryAssignmentHistories"); b.HasKey(x => x.Id);
        b.Property(x => x.ChangeType).HasConversion<int>().IsRequired(); b.Property(x => x.PayFrequency).HasConversion<int>().IsRequired(); b.Property(x => x.Status).HasConversion<int>().IsRequired(); b.Property(x => x.ChangeReason).HasConversion<int>().IsRequired();
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired(); b.Property(x => x.AnnualCtc).HasPrecision(18, 6); b.Property(x => x.MonthlyCtc).HasPrecision(18, 6); b.Property(x => x.Remarks).HasMaxLength(2000); b.Property(x => x.ComponentsJson).HasMaxLength(20000).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.EmployeeSalaryAssignmentId, x.ChangedAtUtc });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Assignment).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "EmployeeSalaryAssignmentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ActorUser).WithMany().HasForeignKey(new[] { "TenantId", "ActorUserId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
