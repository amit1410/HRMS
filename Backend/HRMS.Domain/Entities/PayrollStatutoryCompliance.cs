using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class PayrollCompliancePeriod : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string JurisdictionCode { get; set; } = "IN";
    public PayrollComplianceType ComplianceType { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public DateOnly? DueDate { get; set; }
    public PayrollCompliancePeriodStatus Status { get; set; } = PayrollCompliancePeriodStatus.Open;
    public Tenant? Tenant { get; set; }
    public ICollection<PayrollStatutoryReturnBatch> ReturnBatches { get; set; } = new List<PayrollStatutoryReturnBatch>();
}

public sealed class PayrollStatutoryReturnBatch : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollCompliancePeriodId { get; set; }
    public PayrollComplianceType ComplianceType { get; set; }
    public string JurisdictionCode { get; set; } = "IN";
    public string BatchNumber { get; set; } = string.Empty;
    public PayrollStatutoryReturnStatus Status { get; set; } = PayrollStatutoryReturnStatus.Draft;
    public int EmployeeCount { get; set; }
    public decimal GrossRelevantWages { get; set; }
    public decimal EmployeeContribution { get; set; }
    public decimal EmployerContribution { get; set; }
    public decimal TotalDeduction { get; set; }
    public decimal TotalPayable { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
    public Guid? GeneratedByUserId { get; set; }
    public DateTime? ValidatedAtUtc { get; set; }
    public Guid? ValidatedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ExportedAtUtc { get; set; }
    public Guid? ExportedByUserId { get; set; }
    public DateTime? FiledAtUtc { get; set; }
    public Guid? FiledByUserId { get; set; }
    public string? ExternalReference { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public Tenant? Tenant { get; set; }
    public PayrollCompliancePeriod? CompliancePeriod { get; set; }
    public ICollection<PayrollStatutoryReturnEmployee> Employees { get; set; } = new List<PayrollStatutoryReturnEmployee>();
    public ICollection<PayrollStatutoryReturnSource> Sources { get; set; } = new List<PayrollStatutoryReturnSource>();
    public ICollection<PayrollStatutoryComplianceHistory> History { get; set; } = new List<PayrollStatutoryComplianceHistory>();
    public ICollection<PayrollStatutoryChallan> Challans { get; set; } = new List<PayrollStatutoryChallan>();
}

public sealed class PayrollStatutoryReturnEmployee : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollStatutoryReturnBatchId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCodeSnapshot { get; set; } = string.Empty;
    public string EmployeeNameSnapshot { get; set; } = string.Empty;
    public string? Uan { get; set; }
    public string? EsicNumber { get; set; }
    public string? Pan { get; set; }
    public string? PtRegistrationReference { get; set; }
    public decimal GrossWages { get; set; }
    public decimal StatutoryWages { get; set; }
    public decimal EmployeeContribution { get; set; }
    public decimal EmployerContribution { get; set; }
    public decimal DeductionAmount { get; set; }
    public decimal PayableAmount { get; set; }
    public PayrollComplianceValidationStatus ValidationStatus { get; set; } = PayrollComplianceValidationStatus.Valid;
    public string? ValidationMessage { get; set; }
    public int Sequence { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollStatutoryReturnBatch? ReturnBatch { get; set; }
    public Employee? Employee { get; set; }
}

public sealed class PayrollStatutoryReturnSource : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollStatutoryReturnBatchId { get; set; }
    public Guid PayrollStatutoryReturnEmployeeId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid PayrollResultId { get; set; }
    public Guid PayrollStatutoryResultId { get; set; }
    public decimal Amount { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }
    public PayrollStatutoryReturnBatch? ReturnBatch { get; set; }
    public PayrollStatutoryReturnEmployee? ReturnEmployee { get; set; }
}

public sealed class PayrollStatutoryComplianceHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollStatutoryReturnBatchId { get; set; }
    public PayrollStatutoryComplianceHistoryChangeType ChangeType { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? Message { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollStatutoryReturnBatch? ReturnBatch { get; set; }
}

public sealed class PayrollStatutoryChallan : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollStatutoryReturnBatchId { get; set; }
    public string? ChallanNumber { get; set; }
    public DateOnly? PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PayrollStatutoryChallanStatus Status { get; set; } = PayrollStatutoryChallanStatus.Pending;
    public string? BankReference { get; set; }
    public string? ExternalReference { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollStatutoryReturnBatch? ReturnBatch { get; set; }
}
