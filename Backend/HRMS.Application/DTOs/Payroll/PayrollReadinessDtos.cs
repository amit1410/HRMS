namespace HRMS.Application.DTOs.Payroll;

public sealed record PayrollReadinessCheckDto(string Code, string Severity, Guid? EmployeeId, string? EntityType, Guid? EntityId, string Message, bool Blocking);

public sealed record PayrollReadinessDto(Guid PayrollRunId, bool Ready, int ErrorCount, int WarningCount, IReadOnlyList<PayrollReadinessCheckDto> Checks);
