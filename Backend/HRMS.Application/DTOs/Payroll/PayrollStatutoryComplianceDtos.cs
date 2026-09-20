using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Payroll;

public sealed class PayrollCompliancePeriodRequest { public string JurisdictionCode { get; set; } = "IN"; public PayrollComplianceType ComplianceType { get; set; } public DateOnly PeriodStart { get; set; } public DateOnly PeriodEnd { get; set; } public DateOnly? DueDate { get; set; } }
public sealed record PayrollCompliancePeriodDto(Guid Id, string JurisdictionCode, PayrollComplianceType ComplianceType, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly? DueDate, PayrollCompliancePeriodStatus Status);
public sealed record PayrollStatutoryReturnEmployeeDto(Guid Id, Guid EmployeeId, string EmployeeCode, string EmployeeName, string? Uan, string? EsicNumber, string? Pan, decimal GrossWages, decimal StatutoryWages, decimal EmployeeContribution, decimal EmployerContribution, decimal DeductionAmount, decimal PayableAmount, PayrollComplianceValidationStatus ValidationStatus, string? ValidationMessage, int Sequence);
public sealed record PayrollStatutoryReturnDto(Guid Id, Guid CompliancePeriodId, PayrollComplianceType ComplianceType, string JurisdictionCode, string BatchNumber, PayrollStatutoryReturnStatus Status, int EmployeeCount, decimal GrossRelevantWages, decimal EmployeeContribution, decimal EmployerContribution, decimal TotalDeduction, decimal TotalPayable, IReadOnlyList<PayrollStatutoryReturnEmployeeDto> Employees);
