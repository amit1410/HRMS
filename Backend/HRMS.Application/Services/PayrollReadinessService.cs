using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class PayrollReadinessService(IHrmsDbContext db, ITenantContext tenant) : IPayrollReadinessService
{
    public async Task<Result<PayrollReadinessDto>> CheckAsync(Guid payrollRunId, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId;
        var run = await db.PayrollRuns.AsNoTracking().Include(x => x.PayrollPeriod).Include(x => x.Employees).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == payrollRunId, ct);
        if (run is null) return Result<PayrollReadinessDto>.NotFound("Payroll run not found.");

        var checks = new List<PayrollReadinessCheckDto>();
        if (run.PayrollPeriod is null)
            checks.Add(new("MissingPayrollPeriod", "Error", null, "PayrollRun", run.Id, "The payroll run has no period.", true));
        else if (run.PayrollPeriod.Status == PayrollPeriodStatus.Locked && run.Status is not (PayrollRunStatus.Approved or PayrollRunStatus.Finalized))
            checks.Add(new("LockedPeriod", "Error", null, "PayrollPeriod", run.PayrollPeriodId, "The payroll period is locked before this run reached an approved state.", true));

        var eligible = run.Employees.Where(x => x.IsEligible).ToList();
        if (eligible.Count == 0)
            checks.Add(new("NoEligibleEmployees", "Error", null, "PayrollRun", run.Id, "The payroll run has no eligible employees.", true));

        var assignmentIds = eligible.Where(x => x.EmployeeSalaryAssignmentId.HasValue).Select(x => x.EmployeeSalaryAssignmentId!.Value).ToHashSet();
        var assignments = await db.EmployeeSalaryAssignments.AsNoTracking().Where(x => x.TenantId == tenantId && assignmentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        foreach (var employee in eligible)
        {
            if (!employee.EmployeeSalaryAssignmentId.HasValue || !assignments.ContainsKey(employee.EmployeeSalaryAssignmentId.Value))
                checks.Add(new("MissingSalaryAssignment", "Error", employee.EmployeeId, "PayrollRunEmployee", employee.Id, "An eligible employee has no resolvable salary assignment.", true));
            else if (!employee.SalaryStructureVersionId.HasValue)
                checks.Add(new("MissingSalaryStructure", "Error", employee.EmployeeId, "PayrollRunEmployee", employee.Id, "An eligible employee has no salary structure version.", true));
        }

        if (run.Status is PayrollRunStatus.Finalized or PayrollRunStatus.Cancelled)
            checks.Add(new("RunNotOpenForCalculation", "Warning", null, "PayrollRun", run.Id, $"The run is already {run.Status}; readiness is informational only.", false));

        var errors = checks.Count(x => x.Blocking);
        var warnings = checks.Count(x => !x.Blocking);
        return Result<PayrollReadinessDto>.Success(new PayrollReadinessDto(run.Id, errors == 0, errors, warnings, checks));
    }
}
