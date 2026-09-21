using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed record PayrollJournalLineDto(Guid Id, int Sequence, string AccountCode, string AccountName, string Description, decimal Debit, decimal Credit, string CurrencyCode, string SourceType, Guid? SourceId);
public sealed record PayrollJournalDto(Guid Id, Guid? PayrollRunId, Guid? PayrollPeriodId, string JournalNumber, DateOnly JournalDate, string CurrencyCode, PayrollJournalStatus Status, decimal TotalDebit, decimal TotalCredit, IReadOnlyList<PayrollJournalLineDto> Lines);
public sealed class PayrollGLAccountRequest { public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public PayrollGLAccountType AccountType { get; set; } public string? ExternalCode { get; set; } public bool IsActive { get; set; } = true; }
public sealed record PayrollGLAccountDto(Guid Id, string Code, string Name, PayrollGLAccountType AccountType, string? ExternalCode, bool IsActive);
public sealed class PayrollAccountingConfigurationRequest { public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public bool IsActive { get; set; } = true; }
public sealed record PayrollAccountingConfigurationDto(Guid Id, string Code, string Name, bool IsActive, IReadOnlyList<PayrollAccountingConfigurationVersionDto> Versions);
public sealed class PayrollAccountingConfigurationVersionRequest { public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public PayrollJournalAggregationMode AggregationMode { get; set; } }
public sealed record PayrollAccountingConfigurationVersionDto(Guid Id, Guid ConfigurationId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, PayrollAccountingConfigurationVersionStatus Status, PayrollJournalAggregationMode AggregationMode, IReadOnlyList<PayrollGLMappingDto> Mappings);
public sealed class PayrollGLMappingRequest { public Guid? SalaryComponentId { get; set; } public StatutoryType? StatutoryType { get; set; } public PayrollGLMappingType MappingType { get; set; } public Guid? DebitAccountId { get; set; } public Guid? CreditAccountId { get; set; } public Guid? EmployerContributionAccountId { get; set; } public int Priority { get; set; } public bool IsActive { get; set; } = true; }
public sealed record PayrollGLMappingDto(Guid Id, Guid ConfigurationVersionId, Guid? SalaryComponentId, StatutoryType? StatutoryType, PayrollGLMappingType MappingType, Guid? DebitAccountId, Guid? CreditAccountId, Guid? EmployerContributionAccountId, int Priority, bool IsActive);
