using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class StatutoryConfigurationConfiguration : IEntityTypeConfiguration<StatutoryConfiguration>
{
    public void Configure(EntityTypeBuilder<StatutoryConfiguration> b)
    {
        b.ToTable("StatutoryConfigurations"); b.HasKey(x => x.Id); b.Property(x => x.JurisdictionCode).HasMaxLength(10).IsRequired(); b.Property(x => x.StateCode).HasMaxLength(10); b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.StatutoryType).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.JurisdictionCode, x.StateCode, x.StatutoryType, x.IsActive }); b.HasAlternateKey(x => new { x.TenantId, x.Id }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutoryConfigurationVersionConfiguration : IEntityTypeConfiguration<StatutoryConfigurationVersion>
{
    public void Configure(EntityTypeBuilder<StatutoryConfigurationVersion> b)
    {
        b.ToTable("StatutoryConfigurationVersions"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConfigurationJson).HasMaxLength(20000).IsRequired(); b.HasIndex(x => new { x.TenantId, x.StatutoryConfigurationId, x.EffectiveFrom, x.EffectiveTo }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Configuration).WithMany(x => x.Versions).HasForeignKey(new[] { "TenantId", "StatutoryConfigurationId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(new[] { "TenantId", "CreatedByUserId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutoryComponentBasisConfiguration : IEntityTypeConfiguration<StatutoryComponentBasis>
{
    public void Configure(EntityTypeBuilder<StatutoryComponentBasis> b)
    {
        b.ToTable("StatutoryComponentBasis"); b.HasKey(x => x.Id); b.Property(x => x.Weight).HasPrecision(18, 6); b.HasIndex(x => new { x.TenantId, x.StatutoryConfigurationVersionId, x.SalaryComponentId }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ConfigurationVersion).WithMany(x => x.BasisMappings).HasForeignKey(new[] { "TenantId", "StatutoryConfigurationVersionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.SalaryComponent).WithMany().HasForeignKey(new[] { "TenantId", "SalaryComponentId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutorySlabConfiguration : IEntityTypeConfiguration<StatutorySlab>
{
    public void Configure(EntityTypeBuilder<StatutorySlab> b)
    {
        b.ToTable("StatutorySlabs"); b.HasKey(x => x.Id); b.Property(x => x.FromAmount).HasPrecision(18, 6); b.Property(x => x.ToAmount).HasPrecision(18, 6); b.Property(x => x.Rate).HasPrecision(18, 6); b.Property(x => x.FixedAmount).HasPrecision(18, 6); b.HasIndex(x => new { x.TenantId, x.StatutoryConfigurationVersionId, x.Sequence }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ConfigurationVersion).WithMany(x => x.Slabs).HasForeignKey(new[] { "TenantId", "StatutoryConfigurationVersionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EmployeeStatutoryProfileConfiguration : IEntityTypeConfiguration<EmployeeStatutoryProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeStatutoryProfile> b)
    {
        b.ToTable("EmployeeStatutoryProfiles"); b.HasKey(x => x.Id); b.Property(x => x.JurisdictionCode).HasMaxLength(10).IsRequired(); b.Property(x => x.StateCode).HasMaxLength(10); b.Property(x => x.Uan).HasMaxLength(30); b.Property(x => x.EsiNumber).HasMaxLength(50); b.Property(x => x.TaxRegime).HasMaxLength(40); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.EffectiveFrom, x.EffectiveTo }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EmployeeStatutoryProfileHistoryConfiguration : IEntityTypeConfiguration<EmployeeStatutoryProfileHistory>
{
    public void Configure(EntityTypeBuilder<EmployeeStatutoryProfileHistory> b)
    {
        b.ToTable("EmployeeStatutoryProfileHistories"); b.HasKey(x => x.Id); b.Property(x => x.ChangeType).HasConversion<int>(); b.Property(x => x.SnapshotJson).HasMaxLength(20000).IsRequired(); b.HasIndex(x => new { x.TenantId, x.EmployeeStatutoryProfileId, x.ChangedAtUtc }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Profile).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "EmployeeStatutoryProfileId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StatutoryConfigurationHistoryConfiguration : IEntityTypeConfiguration<StatutoryConfigurationHistory>
{
    public void Configure(EntityTypeBuilder<StatutoryConfigurationHistory> b)
    {
        b.ToTable("StatutoryConfigurationHistories"); b.HasKey(x => x.Id); b.Property(x => x.ChangeType).HasConversion<int>(); b.Property(x => x.SnapshotJson).HasMaxLength(20000).IsRequired(); b.HasIndex(x => new { x.TenantId, x.StatutoryConfigurationId, x.ChangedAtUtc }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Configuration).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "StatutoryConfigurationId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PayrollStatutoryResultConfiguration : IEntityTypeConfiguration<PayrollStatutoryResult>
{
    public void Configure(EntityTypeBuilder<PayrollStatutoryResult> b)
    {
        b.ToTable("PayrollStatutoryResults"); b.HasKey(x => x.Id); b.Property(x => x.JurisdictionCode).HasMaxLength(10).IsRequired(); b.Property(x => x.StatutoryType).HasConversion<int>(); b.Property(x => x.CalculationBasis).HasPrecision(18, 6); b.Property(x => x.EmployeeAmount).HasPrecision(18, 6); b.Property(x => x.EmployerAmount).HasPrecision(18, 6); b.Property(x => x.TotalAmount).HasPrecision(18, 6); b.Property(x => x.AppliedRate).HasPrecision(18, 6); b.Property(x => x.AppliedCeiling).HasPrecision(18, 6); b.Property(x => x.CalculationMetadata).HasMaxLength(20000).IsRequired(); b.HasIndex(x => new { x.TenantId, x.PayrollResultId, x.StatutoryType }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.PayrollRunId, x.EmployeeId }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.PayrollResult).WithMany().HasForeignKey(new[] { "TenantId", "PayrollResultId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(new[] { "TenantId", "PayrollRunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.NoAction); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Configuration).WithMany().HasForeignKey(new[] { "TenantId", "StatutoryConfigurationId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ConfigurationVersion).WithMany().HasForeignKey(new[] { "TenantId", "StatutoryConfigurationVersionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
