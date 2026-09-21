using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed record PayrollResultComponentDto(Guid Id, Guid? SalaryComponentId, Guid? SalaryStructureComponentId, Guid CalculationAttemptId, string ComponentCode, string ComponentName, SalaryComponentType ComponentType, SalaryStructureCalculationType CalculationType, decimal? BaseAmount, decimal? Rate, decimal UnproratedAmount, decimal ProrationFactor, decimal CalculatedAmount, bool IsEarning, bool IsDeduction, bool IsProrated, int CalculationSequence, string CalculationSource, string? FormulaSnapshot, string? CalculationMetadata);
public sealed record PayrollResultDto(Guid Id, Guid PayrollRunId, Guid EmployeeId, string EmployeeCode, string EmployeeName, Guid EmployeeSalaryAssignmentId, Guid SalaryStructureId, Guid SalaryStructureVersionId, Guid CalculationAttemptId, DateOnly PeriodStartDate, DateOnly PeriodEndDate, DateOnly EmploymentSnapshotDate, int CalendarDays, int EligibleDays, decimal ProrationFactor, string CurrencyCode, decimal GrossEarnings, decimal TotalDeductions, decimal EmployerContributions, decimal NetPay, PayrollResultStatus Status, int CalculationVersion, bool IsCurrent, IReadOnlyList<PayrollResultComponentDto> Components);
public sealed record PayrollCalculationErrorDto(Guid Id, Guid PayrollRunId, Guid PayrollRunEmployeeId, Guid EmployeeId, Guid CalculationAttemptId, string EmployeeCode, PayrollCalculationErrorCode ErrorCode, string Message, Guid? SalaryComponentId, DateTime CreatedAtUtc, bool IsCurrent);
public sealed record PayrollCalculationSummaryDto(Guid PayrollRunId, PayrollRunStatus Status, int EmployeeCount, int CalculatedCount, int FailedCount, decimal GrossEarnings, decimal TotalDeductions, decimal NetPay);
public sealed class PayrollResultQuery : PagedQuery { public PayrollResultStatus? Status { get; set; } }
