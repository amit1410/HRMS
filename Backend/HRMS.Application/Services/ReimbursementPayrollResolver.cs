using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Services;

public sealed class ReimbursementPayrollResolver(IReimbursementService reimbursements) : IReimbursementPayrollResolver
{
    public async Task<IReadOnlyList<ReimbursementPayrollRecovery>> ResolveAsync(Guid employeeId, DateOnly asOf, CancellationToken ct = default)
    {
        var result = await reimbursements.GetPayrollRecoveriesAsync(employeeId, asOf, ct);
        return result.Value ?? Array.Empty<ReimbursementPayrollRecovery>();
    }
}
