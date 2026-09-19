using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed record PayslipLineDto(Guid Id, string ComponentCode, string ComponentName, string ComponentType, string DisplayGroup, decimal Amount, int Sequence, string Source, bool IsStatutory, decimal? EmployerAmount);
public sealed record PayslipDto(Guid Id, Guid PayrollRunId, Guid PayrollResultId, Guid EmployeeId, string PayslipNumber, DateOnly PeriodStartDate, DateOnly PeriodEndDate, DateOnly PayDate, string CurrencyCode, decimal GrossEarnings, decimal TotalDeductions, decimal NetPay, decimal EmployerContributionTotal, string EmployeeCode, string EmployeeName, string? Designation, string? Department, string? WorkLocation, DateOnly? DateOfJoining, string? SalaryStructureReference, PayslipStatus Status, int Version, DateTime GeneratedAtUtc, IReadOnlyList<PayslipLineDto> Lines);
public sealed record PayrollRegisterRowDto(Guid EmployeeId, string EmployeeCode, string EmployeeName, string? Department, string? WorkLocation, decimal GrossEarnings, decimal TotalDeductions, decimal EmployerContributions, decimal NetPay, string CurrencyCode, PayrollResultStatus Status);
public sealed class PayrollOutputQuery : PagedQuery { public PayslipStatus? Status { get; set; } }
public sealed record PayrollOutputFile(string FileName, string ContentType, byte[] Content);
