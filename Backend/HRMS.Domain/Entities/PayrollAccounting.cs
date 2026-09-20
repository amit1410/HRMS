using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class PayrollGLAccount : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PayrollGLAccountType AccountType { get; set; }
    public string? ExternalCode { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
}

public sealed class PayrollAccountingConfiguration : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
    public ICollection<PayrollAccountingConfigurationVersion> Versions { get; set; } = new List<PayrollAccountingConfigurationVersion>();
}

public sealed class PayrollAccountingConfigurationVersion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollAccountingConfigurationId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public PayrollAccountingConfigurationVersionStatus Status { get; set; } = PayrollAccountingConfigurationVersionStatus.Active;
    public PayrollJournalAggregationMode AggregationMode { get; set; } = PayrollJournalAggregationMode.Account;
    public Tenant? Tenant { get; set; }
    public PayrollAccountingConfiguration? Configuration { get; set; }
    public ICollection<PayrollGLMapping> Mappings { get; set; } = new List<PayrollGLMapping>();
}

public sealed class PayrollGLMapping : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfigurationVersionId { get; set; }
    public Guid? SalaryComponentId { get; set; }
    public StatutoryType? StatutoryType { get; set; }
    public PayrollGLMappingType MappingType { get; set; }
    public Guid? DebitAccountId { get; set; }
    public Guid? CreditAccountId { get; set; }
    public Guid? EmployerContributionAccountId { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
    public PayrollAccountingConfigurationVersion? ConfigurationVersion { get; set; }
    public PayrollGLAccount? DebitAccount { get; set; }
    public PayrollGLAccount? CreditAccount { get; set; }
    public PayrollGLAccount? EmployerContributionAccount { get; set; }
    public SalaryComponent? SalaryComponent { get; set; }
}

public sealed class PayrollJournalBatch : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid PayrollPeriodId { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public DateOnly JournalDate { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public PayrollJournalStatus Status { get; set; } = PayrollJournalStatus.Draft;
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public int LineCount { get; set; }
    public Guid AccountingConfigurationVersionId { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
    public Guid? GeneratedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public Guid? PostedByUserId { get; set; }
    public DateTime? ExportedAtUtc { get; set; }
    public Guid? ExportedByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public PayrollRun? PayrollRun { get; set; }
    public PayrollPeriod? PayrollPeriod { get; set; }
    public PayrollAccountingConfigurationVersion? ConfigurationVersion { get; set; }
    public ICollection<PayrollJournalLine> Lines { get; set; } = new List<PayrollJournalLine>();
    public ICollection<PayrollJournalHistory> History { get; set; } = new List<PayrollJournalHistory>();
}

public sealed class PayrollJournalLine : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollJournalBatchId { get; set; }
    public int Sequence { get; set; }
    public Guid PayrollGLAccountId { get; set; }
    public string AccountCodeSnapshot { get; set; } = string.Empty;
    public string AccountNameSnapshot { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public string SourceType { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? SalaryComponentId { get; set; }
    public StatutoryType? StatutoryType { get; set; }
    public Guid? CostCenterId { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollJournalBatch? Batch { get; set; }
    public PayrollGLAccount? Account { get; set; }
    public ICollection<PayrollJournalLineSource> Sources { get; set; } = new List<PayrollJournalLineSource>();
}

public sealed class PayrollJournalLineSource : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollJournalLineId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public decimal Amount { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollJournalLine? JournalLine { get; set; }
}

public sealed class PayrollJournalHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollJournalBatchId { get; set; }
    public PayrollJournalHistoryChangeType ChangeType { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? Message { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollJournalBatch? Batch { get; set; }
}
