using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LoanPayrollRecoveryResolver(IHrmsDbContext db, ITenantContext tenant) : ILoanPayrollRecoveryResolver
{
    public async Task<IReadOnlyList<LoanRecoveryDue>> ResolveAsync(Guid employeeId, DateOnly recoveryDate, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return [];
        return await db.LoanInstallments.AsNoTracking()
            .Include(x => x.EmployeeLoan).ThenInclude(x => x!.LoanProduct)
            .Where(x => x.TenantId == tenantId && x.EmployeeLoan!.EmployeeId == employeeId && x.EmployeeLoan.Status == LoanStatus.Active && x.DueDate <= recoveryDate && (x.Status == LoanInstallmentStatus.Scheduled || x.Status == LoanInstallmentStatus.PartiallyRecovered) && x.RecoveredAmount < x.InstallmentAmount)
            .OrderBy(x => x.EmployeeLoan!.LoanProduct!.RecoveryPriority).ThenBy(x => x.DueDate).ThenBy(x => x.InstallmentNumber)
            .Select(x => new LoanRecoveryDue(x.EmployeeLoanId, x.Id, x.EmployeeLoan!.LoanNumber, x.EmployeeLoan.LoanProduct!.Code, x.InstallmentNumber, Math.Max(0m, x.PrincipalAmount - Math.Min(x.RecoveredAmount, x.PrincipalAmount)), Math.Max(0m, x.InterestAmount - Math.Max(0m, x.RecoveredAmount - x.PrincipalAmount)), Math.Max(0m, x.InstallmentAmount - x.RecoveredAmount), x.EmployeeLoan.LoanProduct.RecoveryPriority, x.EmployeeLoan.RecoveryPolicy, x.DueDate))
            .ToListAsync(ct);
    }
}
