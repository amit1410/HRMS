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
    public Guid SalaryComponentId { get; set; }
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
    public Tenant? Tenant { get; set; }
    public PayrollResult? PayrollResult { get; set; }
    public SalaryComponent? SalaryComponent { get; set; }
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
