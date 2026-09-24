using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class PayrollResult : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid PayrollRunEmployeeId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid EmployeeSalaryAssignmentId { get; set; }
    public Guid SalaryStructureId { get; set; }
    public Guid SalaryStructureVersionId { get; set; }
    public Guid CalculationAttemptId { get; set; }
    public DateOnly PeriodStartDate { get; set; }
    public DateOnly PeriodEndDate { get; set; }
    public DateOnly EmploymentSnapshotDate { get; set; }
    public int CalendarDays { get; set; }
    public int EligibleDays { get; set; }
    public Guid? AttendanceSnapshotId { get; set; }
    public int? AttendanceVersion { get; set; }
    public decimal? AttendanceEligibleDays { get; set; }
    public decimal? AttendancePayableDays { get; set; }
    public decimal? AttendanceLopDays { get; set; }
    public decimal ProrationFactor { get; set; } = 1m;
    public DateTime CalculationDateUtc { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public decimal GrossEarnings { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetPay { get; set; }
    public decimal EmployerContributions { get; set; }
    public PayrollResultStatus Status { get; set; } = PayrollResultStatus.Calculated;
    public int CalculationVersion { get; set; } = 1;
    public bool IsCurrent { get; set; } = true;
    public DateTime CalculatedAtUtc { get; set; }
    public Guid? CalculatedByUserId { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public PayrollRun? PayrollRun { get; set; }
    public PayrollRunEmployee? PayrollRunEmployee { get; set; }
    public Employee? Employee { get; set; }
    public EmployeeSalaryAssignment? EmployeeSalaryAssignment { get; set; }
    public ICollection<PayrollResultComponent> Components { get; set; } = new List<PayrollResultComponent>();
}

public sealed class PayrollResultComponent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollResultId { get; set; }
    public Guid? SalaryComponentId { get; set; }
    public Guid? SalaryStructureComponentId { get; set; }
    public Guid CalculationAttemptId { get; set; }
    public string ComponentCode { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;
    public SalaryComponentType ComponentType { get; set; }
    public SalaryStructureCalculationType CalculationType { get; set; }
    public decimal? BaseAmount { get; set; }
    public decimal? Rate { get; set; }
    public decimal? Quantity { get; set; }
    public decimal UnproratedAmount { get; set; }
    public decimal ProrationFactor { get; set; } = 1m;
    public decimal CalculatedAmount { get; set; }
    public bool IsEarning { get; set; }
    public bool IsDeduction { get; set; }
    public bool IsEmployerContribution { get; set; }
    public bool IsTaxable { get; set; }
    public bool IsProrated { get; set; }
    public int CalculationSequence { get; set; }
    public string CalculationSource { get; set; } = string.Empty;
    public string? FormulaSnapshot { get; set; }
    public string? CalculationMetadata { get; set; }
    public Guid? EmployeeLoanId { get; set; }
    public Guid? LoanInstallmentId { get; set; }
    public Guid? ReimbursementClaimId { get; set; }
    public Guid? ReimbursementClaimLineId { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollResult? PayrollResult { get; set; }
    public SalaryComponent? SalaryComponent { get; set; }
    public EmployeeLoan? EmployeeLoan { get; set; }
    public LoanInstallment? LoanInstallment { get; set; }
}

public sealed class PayrollCalculationError : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid PayrollRunEmployeeId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid CalculationAttemptId { get; set; }
    public bool IsCurrent { get; set; } = true;
    public PayrollCalculationErrorCode ErrorCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? SalaryComponentId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollRun? PayrollRun { get; set; }
    public PayrollRunEmployee? PayrollRunEmployee { get; set; }
    public Employee? Employee { get; set; }
    public SalaryComponent? SalaryComponent { get; set; }
}

public sealed class PayrollCalculationHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid CalculationAttemptId { get; set; }
    public PayrollCalculationHistoryChangeType ChangeType { get; set; }
    public Guid? PayrollRunEmployeeId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? Message { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public Guid? ActorUserId { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollRun? PayrollRun { get; set; }
}

public sealed class StatutoryConfiguration : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string JurisdictionCode { get; set; } = "IN";
    public string? StateCode { get; set; }
    public StatutoryType StatutoryType { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public ICollection<StatutoryConfigurationVersion> Versions { get; set; } = new List<StatutoryConfigurationVersion>();
    public ICollection<StatutoryConfigurationHistory> History { get; set; } = new List<StatutoryConfigurationHistory>();
}

public sealed class StatutoryConfigurationVersion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid StatutoryConfigurationId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public StatutoryConfigurationStatus Status { get; set; } = StatutoryConfigurationStatus.Active;
    public int Priority { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public Guid? CreatedByUserId { get; set; }
    public Tenant? Tenant { get; set; }
    public StatutoryConfiguration? Configuration { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<StatutoryComponentBasis> BasisMappings { get; set; } = new List<StatutoryComponentBasis>();
    public ICollection<StatutorySlab> Slabs { get; set; } = new List<StatutorySlab>();
}

public sealed class StatutoryComponentBasis : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid StatutoryConfigurationVersionId { get; set; }
    public Guid SalaryComponentId { get; set; }
    public bool Include { get; set; } = true;
    public decimal Weight { get; set; } = 100m;
    public Tenant? Tenant { get; set; }
    public StatutoryConfigurationVersion? ConfigurationVersion { get; set; }
    public SalaryComponent? SalaryComponent { get; set; }
}

public sealed class StatutorySlab : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid StatutoryConfigurationVersionId { get; set; }
    public decimal FromAmount { get; set; }
    public decimal? ToAmount { get; set; }
    public decimal Rate { get; set; }
    public decimal FixedAmount { get; set; }
    public int Sequence { get; set; }
    public int? OptionalMonth { get; set; }
    public Tenant? Tenant { get; set; }
    public StatutoryConfigurationVersion? ConfigurationVersion { get; set; }
}

public sealed class EmployeeStatutoryProfile : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string JurisdictionCode { get; set; } = "IN";
    public string? StateCode { get; set; }
    public bool PfApplicable { get; set; }
    public string? Uan { get; set; }
    public bool EsiApplicable { get; set; }
    public string? EsiNumber { get; set; }
    public bool ProfessionalTaxApplicable { get; set; }
    public bool IncomeTaxApplicable { get; set; }
    public string? TaxRegime { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public ICollection<EmployeeStatutoryProfileHistory> History { get; set; } = new List<EmployeeStatutoryProfileHistory>();
}

public sealed class EmployeeStatutoryProfileHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeStatutoryProfileId { get; set; }
    public Guid EmployeeId { get; set; }
    public StatutoryProfileChangeType ChangeType { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public Tenant? Tenant { get; set; }
    public EmployeeStatutoryProfile? Profile { get; set; }
}

public sealed class StatutoryConfigurationHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid StatutoryConfigurationId { get; set; }
    public StatutoryConfigurationChangeType ChangeType { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public Tenant? Tenant { get; set; }
    public StatutoryConfiguration? Configuration { get; set; }
}

public sealed class PayrollStatutoryResult : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollResultId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public StatutoryType StatutoryType { get; set; }
    public string JurisdictionCode { get; set; } = "IN";
    public Guid StatutoryConfigurationId { get; set; }
    public Guid StatutoryConfigurationVersionId { get; set; }
    public decimal CalculationBasis { get; set; }
    public decimal EmployeeAmount { get; set; }
    public decimal EmployerAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal? AppliedRate { get; set; }
    public decimal? AppliedCeiling { get; set; }
    public string CalculationMetadata { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollResult? PayrollResult { get; set; }
    public PayrollRun? PayrollRun { get; set; }
    public Employee? Employee { get; set; }
    public StatutoryConfiguration? Configuration { get; set; }
    public StatutoryConfigurationVersion? ConfigurationVersion { get; set; }
}
