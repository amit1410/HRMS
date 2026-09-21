using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class PayrollGLAccountConfiguration : IEntityTypeConfiguration<PayrollGLAccount>
{
    public void Configure(EntityTypeBuilder<PayrollGLAccount> b)
    {
        b.ToTable("PayrollGLAccounts"); b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(50).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.ExternalCode).HasMaxLength(100);
        b.Property(x => x.AccountType).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollAccountingConfigurationConfiguration : IEntityTypeConfiguration<PayrollAccountingConfiguration>
{
    public void Configure(EntityTypeBuilder<PayrollAccountingConfiguration> b)
    {
        b.ToTable("PayrollAccountingConfigurations"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(100).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollAccountingConfigurationVersionConfiguration : IEntityTypeConfiguration<PayrollAccountingConfigurationVersion>
{
    public void Configure(EntityTypeBuilder<PayrollAccountingConfigurationVersion> b)
    {
        b.ToTable("PayrollAccountingConfigurationVersions"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.AggregationMode).HasConversion<int>();
        b.HasIndex(x => new { x.TenantId, x.PayrollAccountingConfigurationId, x.EffectiveFrom });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Configuration).WithMany(x => x.Versions).HasForeignKey(x => new { x.TenantId, x.PayrollAccountingConfigurationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollGLMappingConfiguration : IEntityTypeConfiguration<PayrollGLMapping>
{
    public void Configure(EntityTypeBuilder<PayrollGLMapping> b)
    {
        b.ToTable("PayrollGLMappings"); b.HasKey(x => x.Id); b.Property(x => x.MappingType).HasConversion<int>(); b.Property(x => x.StatutoryType).HasConversion<int>();
        b.HasIndex(x => new { x.TenantId, x.ConfigurationVersionId, x.SalaryComponentId, x.StatutoryType, x.Priority });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ConfigurationVersion).WithMany(x => x.Mappings).HasForeignKey(x => new { x.TenantId, x.ConfigurationVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.DebitAccount).WithMany().HasForeignKey(x => new { x.TenantId, x.DebitAccountId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreditAccount).WithMany().HasForeignKey(x => new { x.TenantId, x.CreditAccountId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.EmployerContributionAccount).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployerContributionAccountId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SalaryComponent).WithMany().HasForeignKey(x => new { x.TenantId, x.SalaryComponentId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollJournalBatchConfiguration : IEntityTypeConfiguration<PayrollJournalBatch>
{
    public void Configure(EntityTypeBuilder<PayrollJournalBatch> b)
    {
        b.ToTable("PayrollJournalBatches"); b.HasKey(x => x.Id); b.Property(x => x.JournalNumber).HasMaxLength(120).IsRequired(); b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.TotalDebit).HasPrecision(18, 2); b.Property(x => x.TotalCredit).HasPrecision(18, 2); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.JournalNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.PayrollRunId }); b.HasIndex(x => new { x.TenantId, x.FinalSettlementCaseId }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(x => new { x.TenantId, x.PayrollRunId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.PayrollPeriod).WithMany().HasForeignKey(x => new { x.TenantId, x.PayrollPeriodId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.FinalSettlementCase).WithMany().HasForeignKey(x => new { x.TenantId, x.FinalSettlementCaseId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.ConfigurationVersion).WithMany().HasForeignKey(x => new { x.TenantId, x.AccountingConfigurationVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollJournalLineConfiguration : IEntityTypeConfiguration<PayrollJournalLine>
{
    public void Configure(EntityTypeBuilder<PayrollJournalLine> b)
    {
        b.ToTable("PayrollJournalLines"); b.HasKey(x => x.Id); b.Property(x => x.AccountCodeSnapshot).HasMaxLength(50).IsRequired(); b.Property(x => x.AccountNameSnapshot).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(500).IsRequired(); b.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired(); b.Property(x => x.Debit).HasPrecision(18, 2); b.Property(x => x.Credit).HasPrecision(18, 2); b.Property(x => x.SourceType).HasMaxLength(100).IsRequired(); b.Property(x => x.StatutoryType).HasConversion<int>();
        b.HasIndex(x => new { x.TenantId, x.PayrollJournalBatchId }); b.HasIndex(x => x.PayrollGLAccountId);
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Batch).WithMany(x => x.Lines).HasForeignKey(x => new { x.TenantId, x.PayrollJournalBatchId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Account).WithMany().HasForeignKey(x => new { x.TenantId, x.PayrollGLAccountId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollJournalLineSourceConfiguration : IEntityTypeConfiguration<PayrollJournalLineSource>
{
    public void Configure(EntityTypeBuilder<PayrollJournalLineSource> b)
    {
        b.ToTable("PayrollJournalLineSources"); b.HasKey(x => x.Id); b.Property(x => x.SourceType).HasMaxLength(100).IsRequired(); b.Property(x => x.Amount).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.PayrollJournalLineId }); b.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.JournalLine).WithMany(x => x.Sources).HasForeignKey(x => new { x.TenantId, x.PayrollJournalLineId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PayrollJournalHistoryConfiguration : IEntityTypeConfiguration<PayrollJournalHistory>
{
    public void Configure(EntityTypeBuilder<PayrollJournalHistory> b)
    {
        b.ToTable("PayrollJournalHistories"); b.HasKey(x => x.Id); b.Property(x => x.ChangeType).HasConversion<int>(); b.Property(x => x.Message).HasMaxLength(1000); b.HasIndex(x => new { x.TenantId, x.PayrollJournalBatchId, x.ChangedAtUtc }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Batch).WithMany(x => x.History).HasForeignKey(x => new { x.TenantId, x.PayrollJournalBatchId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
