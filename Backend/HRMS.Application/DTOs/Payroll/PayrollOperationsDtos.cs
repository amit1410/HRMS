namespace HRMS.Application.DTOs.Payroll;

public sealed record PayrollHealthIssueDto(string Code, string Severity, string Message, Guid? EmployeeId = null, string? EntityType = null, Guid? EntityId = null, string? NavigationHint = null);

public sealed record PayrollHealthCategoryDto(string Category, string Status, int IssueCount, int BlockingCount, IReadOnlyList<PayrollHealthIssueDto> Issues);

public sealed record PayrollConfigurationHealthDto(IReadOnlyList<PayrollHealthCategoryDto> Categories);

public sealed record PayrollOperationsDashboardDto(
    int OpenPeriods,
    int LockedPeriods,
    int RunsWithReadinessErrors,
    int RunsAwaitingCalculation,
    int RunsAwaitingApproval,
    int UnpublishedPayslips,
    int BankAdviceAwaitingApproval,
    int BankAdviceAwaitingExport,
    int AccountingAwaitingApproval,
    int AccountingAwaitingPosting,
    int StatutoryReturnsAwaitingValidation,
    int StatutoryReturnsAwaitingFiling,
    int RetroCasesPending,
    int FinalSettlementsPending);
