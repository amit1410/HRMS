using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface ILoanPayrollRecoveryResolver
{
    Task<IReadOnlyList<LoanRecoveryDue>> ResolveAsync(Guid employeeId, DateOnly recoveryDate, CancellationToken ct = default);
}
