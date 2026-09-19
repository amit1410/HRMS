using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class Payslip : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid PayrollResultId { get; set; }
    public Guid PayrollRunEmployeeId { get; set; }
    public Guid EmployeeId { get; set; }
    public string PayslipNumber { get; set; } = string.Empty;
    public DateOnly PeriodStartDate { get; set; }
    public DateOnly PeriodEndDate { get; set; }
    public DateOnly PayDate { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public decimal GrossEarnings { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetPay { get; set; }
    public decimal EmployerContributionTotal { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public Guid? GeneratedByUserId { get; set; }
    public PayslipStatus Status { get; set; } = PayslipStatus.Generated;
    public int Version { get; set; } = 1;
    public int ConcurrencyVersion { get; set; } = 1;
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string? Department { get; set; }
    public string? WorkLocation { get; set; }
    public DateOnly? DateOfJoining { get; set; }
    public string? SalaryStructureReference { get; set; }
    public Tenant? Tenant { get; set; }
    public PayrollRun? PayrollRun { get; set; }
    public PayrollResult? PayrollResult { get; set; }
    public PayrollRunEmployee? PayrollRunEmployee { get; set; }
    public Employee? Employee { get; set; }
    public ICollection<PayslipLine> Lines { get; set; } = new List<PayslipLine>();
    public ICollection<PayslipHistory> History { get; set; } = new List<PayslipHistory>();
}

public sealed class PayslipLine : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayslipId { get; set; }
    public string ComponentCode { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public string DisplayGroup { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Sequence { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool IsStatutory { get; set; }
    public decimal? EmployerAmount { get; set; }
    public Tenant? Tenant { get; set; }
    public Payslip? Payslip { get; set; }
}

public sealed class PayslipHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PayslipId { get; set; }
    public PayslipHistoryChangeType ChangeType { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? Message { get; set; }
    public Payslip? Payslip { get; set; }
    public User? ActorUser { get; set; }
    public Tenant? Tenant { get; set; }
}
