using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class PayrollOperationsService(IHrmsDbContext db, ITenantContext tenant) : IPayrollOperationsService
{
    public async Task<Result<PayrollConfigurationHealthDto>> GetConfigurationHealthAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PayrollConfigurationHealthDto>.Unauthorized("No authenticated tenant.");

        var categories = new List<PayrollHealthCategoryDto>
        {
            await SalaryHealthAsync(tenantId, ct),
            await StatutoryHealthAsync(tenantId, ct),
            await PaymentHealthAsync(tenantId, ct),
            await AccountingHealthAsync(tenantId, ct),
            await ControlsHealthAsync(tenantId, ct),
            await EmployeeReadinessHealthAsync(tenantId, ct)
        };
        return Result<PayrollConfigurationHealthDto>.Success(new PayrollConfigurationHealthDto(categories));
    }

    public async Task<Result<PayrollOperationsDashboardDto>> GetOperationsDashboardAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PayrollOperationsDashboardDto>.Unauthorized("No authenticated tenant.");
        var result = new PayrollOperationsDashboardDto(
            await db.PayrollPeriods.CountAsync(x => x.TenantId == tenantId && x.Status == PayrollPeriodStatus.Open, ct),
            await db.PayrollPeriods.CountAsync(x => x.TenantId == tenantId && x.Status == PayrollPeriodStatus.Locked, ct),
            0,
            await db.PayrollRuns.CountAsync(x => x.TenantId == tenantId && x.Status == PayrollRunStatus.Prepared, ct),
            await db.PayrollRuns.CountAsync(x => x.TenantId == tenantId && x.Status == PayrollRunStatus.Calculated, ct),
            await db.Payslips.CountAsync(x => x.TenantId == tenantId && x.Status == PayslipStatus.Generated, ct),
            await db.BankAdviceBatches.CountAsync(x => x.TenantId == tenantId && x.Status == BankAdviceStatus.Prepared, ct),
            await db.BankAdviceBatches.CountAsync(x => x.TenantId == tenantId && x.Status == BankAdviceStatus.Approved, ct),
            await db.PayrollJournalBatches.CountAsync(x => x.TenantId == tenantId && x.Status == PayrollJournalStatus.Validated, ct),
            await db.PayrollJournalBatches.CountAsync(x => x.TenantId == tenantId && x.Status == PayrollJournalStatus.Approved, ct),
            await db.PayrollStatutoryReturnBatches.CountAsync(x => x.TenantId == tenantId && x.Status == PayrollStatutoryReturnStatus.Generated, ct),
            await db.PayrollStatutoryReturnBatches.CountAsync(x => x.TenantId == tenantId && (x.Status == PayrollStatutoryReturnStatus.Approved || x.Status == PayrollStatutoryReturnStatus.Exported), ct),
            await db.PayrollRetroCases.CountAsync(x => x.TenantId == tenantId && (x.Status == PayrollRetroStatus.Detected || x.Status == PayrollRetroStatus.Evaluated || x.Status == PayrollRetroStatus.Approved), ct),
            await db.FinalSettlementCases.CountAsync(x => x.TenantId == tenantId && (x.Status == FinalSettlementStatus.Draft || x.Status == FinalSettlementStatus.Calculated || x.Status == FinalSettlementStatus.Reviewed || x.Status == FinalSettlementStatus.Approved), ct));
        return Result<PayrollOperationsDashboardDto>.Success(result);
    }

    private async Task<PayrollHealthCategoryDto> SalaryHealthAsync(Guid tenantId, CancellationToken ct)
    {
        var activeEmployees = await db.Employees.CountAsync(x => x.TenantId == tenantId && x.Status == EmployeeStatus.Active, ct);
        var assignedEmployees = await db.EmployeeSalaryAssignments.Where(x => x.TenantId == tenantId && x.Status == EmployeeSalaryAssignmentStatus.Active).Select(x => x.EmployeeId).Distinct().CountAsync(ct);
        var missing = Math.Max(0, activeEmployees - assignedEmployees);
        return Category("Salary", missing == 0 ? "Healthy" : "Error", missing == 0 ? [] : [new("MissingSalaryAssignment", "Error", $"{missing} active employee(s) have no active salary assignment.", EntityType: "Employee", NavigationHint: "/payroll/employee-salary")]);
    }

    private async Task<PayrollHealthCategoryDto> StatutoryHealthAsync(Guid tenantId, CancellationToken ct)
    {
        var count = await db.StatutoryConfigurationVersions.CountAsync(x => x.TenantId == tenantId && x.Status == StatutoryConfigurationStatus.Active, ct);
        return Category("Statutory", count > 0 ? "Healthy" : "Warning", count > 0 ? [] : [new("MissingStatutoryConfiguration", "Warning", "No active statutory configuration version is available.", EntityType: "StatutoryConfiguration", NavigationHint: "/payroll/statutory-configurations")]);
    }

    private async Task<PayrollHealthCategoryDto> PaymentHealthAsync(Guid tenantId, CancellationToken ct)
    {
        var activeEmployees = await db.Employees.CountAsync(x => x.TenantId == tenantId && x.Status == EmployeeStatus.Active, ct);
        var salaryAccounts = await db.EmployeeBankDetails.CountAsync(x => x.TenantId == tenantId && x.IsActive && x.Status == BankAccountStatus.Active && x.AccountPurpose == AccountPurpose.Salary, ct);
        return Category("Payment", salaryAccounts > 0 || activeEmployees == 0 ? "Healthy" : "Warning", salaryAccounts > 0 || activeEmployees == 0 ? [] : [new("MissingSalaryBankAccount", "Warning", "No active salary bank account is available for the tenant.", EntityType: "EmployeeBankDetail", NavigationHint: "/employees")]);
    }

    private async Task<PayrollHealthCategoryDto> AccountingHealthAsync(Guid tenantId, CancellationToken ct)
    {
        var count = await db.PayrollAccountingConfigurationVersions.CountAsync(x => x.TenantId == tenantId && x.Status == PayrollAccountingConfigurationVersionStatus.Active, ct);
        return Category("Accounting", count > 0 ? "Healthy" : "Warning", count > 0 ? [] : [new("MissingAccountingConfiguration", "Warning", "No active payroll accounting configuration version is available.", EntityType: "PayrollAccountingConfiguration", NavigationHint: "/payroll/accounting/configuration")]);
    }

    private async Task<PayrollHealthCategoryDto> ControlsHealthAsync(Guid tenantId, CancellationToken ct)
    {
        var config = await db.PayrollControlConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        return Category("Controls", config is null ? "Warning" : "Healthy", config is null ? [new("MissingPayrollControls", "Warning", "Payroll control configuration has not been explicitly saved.", EntityType: "PayrollControlConfiguration", NavigationHint: "/payroll/controls")] : []);
    }

    private async Task<PayrollHealthCategoryDto> EmployeeReadinessHealthAsync(Guid tenantId, CancellationToken ct)
    {
        var activeEmployees = await db.Employees.CountAsync(x => x.TenantId == tenantId && x.Status == EmployeeStatus.Active, ct);
        var assignedEmployees = await db.EmployeeSalaryAssignments.Where(x => x.TenantId == tenantId && x.Status == EmployeeSalaryAssignmentStatus.Active).Select(x => x.EmployeeId).Distinct().CountAsync(ct);
        var missing = Math.Max(0, activeEmployees - assignedEmployees);
        return Category("EmployeeReadiness", missing == 0 ? "Healthy" : "Error", missing == 0 ? [] : [new("EmployeeReadinessBlockers", "Error", $"{missing} active employee(s) require payroll readiness fixes.", EntityType: "Employee", NavigationHint: "/employees")]);
    }

    private static PayrollHealthCategoryDto Category(string name, string status, IReadOnlyList<PayrollHealthIssueDto> issues) => new(name, status, issues.Count, issues.Count(x => x.Severity == "Error"), issues);
}
