using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed class PayrollPeriodQuery : PagedQuery
{
    public PayrollPeriodStatus? Status { get; set; }
    public PayrollPeriodType? PeriodType { get; set; }
}

public sealed class PayrollPeriodRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PayrollPeriodType PeriodType { get; set; } = PayrollPeriodType.Monthly;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly PayDate { get; set; }
    public int FiscalYear { get; set; }
    public int PeriodNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public int? ExpectedConcurrencyVersion { get; set; }
}

public sealed record PayrollPeriodDto(Guid Id, string Code, string Name, PayrollPeriodType PeriodType, DateOnly StartDate, DateOnly EndDate, DateOnly PayDate, int FiscalYear, int PeriodNumber, PayrollPeriodStatus Status, bool IsActive, int RunCount, int ConcurrencyVersion);
public sealed record PayrollPeriodHistoryDto(Guid Id, Guid PayrollPeriodId, string ChangeType, PayrollPeriodStatus Status, string SnapshotJson, Guid? ActorUserId, DateTime ChangedAtUtc);

public sealed class PayrollRunQuery : PagedQuery
{
    public Guid? PayrollPeriodId { get; set; }
    public PayrollRunStatus? Status { get; set; }
    public PayrollRunType? RunType { get; set; }
}

public sealed class PayrollRunRequest
{
    public Guid PayrollPeriodId { get; set; }
    public PayrollRunType RunType { get; set; } = PayrollRunType.Regular;
    public string? Notes { get; set; }
}

public sealed record PayrollRunEmployeeDto(Guid Id, Guid EmployeeId, string EmployeeCode, string EmployeeName, Guid? EmployeeSalaryAssignmentId, Guid? SalaryStructureId, Guid? SalaryStructureVersionId, DateOnly EmploymentSnapshotDate, bool IsEligible, string? ExclusionReason, PayrollRunEmployeeStatus Status);
public sealed record PayrollRunDto(Guid Id, Guid PayrollPeriodId, string PayrollPeriodCode, string RunNumber, PayrollRunType RunType, PayrollRunStatus Status, DateTime? StartedAtUtc, Guid? StartedByUserId, int EmployeeCount, int EligibleCount, int ExcludedCount, string? Notes, int ConcurrencyVersion, IReadOnlyList<PayrollRunEmployeeDto> Employees);
public sealed record PayrollRunHistoryDto(Guid Id, Guid PayrollRunId, PayrollRunHistoryChangeType ChangeType, PayrollRunStatus Status, string? Reason, Guid? ActorUserId, DateTime ChangedAtUtc);
