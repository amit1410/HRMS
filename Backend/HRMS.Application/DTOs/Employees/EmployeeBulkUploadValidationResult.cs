namespace HRMS.Application.DTOs.Employees;

/// <summary>Application result shared by workbook preview and confirmed execution.</summary>
public sealed record EmployeeBulkUploadValidationResult(
    EmployeeBulkUploadPreviewDto Preview,
    IReadOnlyList<ValidatedEmployeeOnboardingRow> ValidatedRows);

/// <summary>Canonical onboarding request resolved from one validated workbook row.</summary>
public sealed record ValidatedEmployeeOnboardingRow(int ExcelRowNumber, EmployeeOnboardingRequest Request);
