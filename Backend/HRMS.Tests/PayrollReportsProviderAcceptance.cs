using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

internal static class PayrollReportsProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant, DatabaseProviderType provider)
    {
        var tenantId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var costCenterId = Guid.NewGuid();
        var structureId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var runEmployeeId = Guid.NewGuid();
        tenant.TenantId = tenantId;

        try
        {
            db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"RPT{tenantId:N}"[..12], TenantName = $"Reports {provider}", Host = $"{tenantId:N}.reports.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
            db.Departments.Add(new Department { Id = departmentId, TenantId = tenantId, Code = "RPT-DEPT", Name = "Reports Department", IsActive = true });
            db.CostCenters.Add(new CostCenter { Id = costCenterId, TenantId = tenantId, Code = "RPT-CC", Name = "Reports Cost Center", IsActive = true });
            db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "RPT-EMP", FirstName = "Reports", LastName = "Employee", Email = $"{employeeId:N}@reports.test", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active });
            db.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "RPT-STRUCT", Name = "Reports structure", IsActive = true });
            db.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = versionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new(2026, 1, 1), IsActive = true });
            db.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = new(2026, 1, 1), MonthlyCtc = 10000m, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
            db.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "RPT-2026-09", Name = "Reports period", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Open });
            db.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "RPT-RUN", Status = PayrollRunStatus.Calculated });
            db.PayrollRunEmployees.Add(new PayrollRunEmployee { Id = runEmployeeId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EmploymentSnapshotDate = new(2026, 9, 30), DepartmentId = departmentId, CostCenterId = costCenterId, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible });
            db.PayrollResults.Add(new PayrollResult
            {
                Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = runId, PayrollRunEmployeeId = runEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = new(2026, 9, 1), PeriodEndDate = new(2026, 9, 30), EmploymentSnapshotDate = new(2026, 9, 30), CalendarDays = 30, EligibleDays = 30, CurrencyCode = "INR", GrossEarnings = 10000m, TotalDeductions = 1000m, NetPay = 9000m, EmployerContributions = 500m, Status = PayrollResultStatus.Calculated, IsCurrent = true,
                Components = { new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, ComponentCode = "RPT-BASIC", ComponentName = "Reports basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryStructureCalculationType.FixedAmount, CalculatedAmount = 10000m, IsEarning = true, CalculationSource = "Persisted", CalculationSequence = 1 } }
            });
            await db.SaveChangesAsync();

            var service = new PayrollReportsService(db, tenant);
            var register = await service.GetRegisterAsync(new PayrollReportQuery { PayrollRunId = runId, PageSize = 50 });
            Assert.True(register.Succeeded, register.Message);
            Assert.Single(register.Value!.Items);
            Assert.Equal("RPT-DEPT", register.Value.Items[0].Department);
            Assert.Equal("RPT-CC", register.Value.Items[0].CostCenter);
            Assert.Equal(10000m, register.Value.Items[0].GrossTotal);

            var reportCalls = new (string Name, Func<Task<Result<object>>> Call)[]
            {
                ("earnings", () => Box(service.GetEarningsAsync(new PayrollReportQuery { PayrollRunId = runId, PageSize = 50 }))),
                ("deductions", () => Box(service.GetDeductionsAsync(new PayrollReportQuery { PayrollRunId = runId, PageSize = 50 }))),
                ("employer-contributions", () => Box(service.GetEmployerContributionsAsync(new PayrollReportQuery { PayrollRunId = runId, PageSize = 50 }))),
                ("departments", () => Box(service.GetDepartmentsAsync(new PayrollReportQuery { PayrollRunId = runId }))),
                ("cost-centers", () => Box(service.GetCostCentersAsync(new PayrollReportQuery { PayrollRunId = runId }))),
                ("components", () => Box(service.GetComponentsAsync(new PayrollReportQuery { PayrollRunId = runId }))),
                ("statutory-summary", () => Box(service.GetStatutorySummaryAsync(new PayrollReportQuery { PayrollRunId = runId }))),
                ("bank-payments", () => Box(service.GetBankPaymentsAsync(new PayrollReportQuery { PayrollRunId = runId, PageSize = 50 }))),
                ("accounting-summary", () => Box(service.GetAccountingSummaryAsync(new PayrollReportQuery { PayrollRunId = runId }))),
                ("loan-recoveries", () => Box(service.GetLoanRecoveriesAsync(new PayrollReportQuery { PayrollRunId = runId, PageSize = 50 }))),
                ("reimbursements", () => Box(service.GetReimbursementsAsync(new PayrollReportQuery { PayrollRunId = runId, PageSize = 50 }))),
                ("variable-pay", () => Box(service.GetVariablePayAsync(new PayrollReportQuery { PayrollRunId = runId, PageSize = 50 }))),
                ("adjustments", () => Box(service.GetAdjustmentsAsync(new PayrollReportQuery { PayrollRunId = runId, PageSize = 50 }))),
                ("final-settlements", () => Box(service.GetFinalSettlementsAsync(new PayrollReportQuery { PayrollRunId = runId, PageSize = 50 }))),
                ("off-cycle", () => Box(service.GetOffCycleAsync(new PayrollReportQuery { PayrollRunId = runId, PageSize = 50 }))),
                ("dashboard", () => Box(service.GetDashboardAsync(new PayrollReportQuery { PayrollRunId = runId }))),
                ("trends", () => Box(service.GetTrendsAsync(new PayrollReportQuery { PayrollRunId = runId })))
            };
            foreach (var report in reportCalls)
            {
                var result = await report.Call();
                Assert.True(result.Succeeded, $"{report.Name}: {result.Message}");
            }
            foreach (var report in new[] { "earnings", "deductions", "employer-contributions", "departments", "cost-centers", "components", "statutory-summary", "bank-payments", "accounting-summary", "loan-recoveries", "reimbursements", "variable-pay", "adjustments", "final-settlements", "off-cycle" })
            {
                var export = await service.ExportAsync(report, new PayrollReportQuery { PayrollRunId = runId, PageSize = 50 });
                Assert.True(export.Succeeded, $"{report}: {export.Message}");
            }
            var isolated = await new PayrollReportsService(db, new TestTenantContext(Guid.NewGuid())).GetRegisterAsync(new PayrollReportQuery { PayrollRunId = runId });
            Assert.True(isolated.Succeeded, isolated.Message);
            Assert.Empty(isolated.Value!.Items);
        }
        finally
        {
            db.ClearChangeTracker();
        }
    }

    private static async Task<Result<object>> Box<T>(Task<Result<T>> task)
    {
        var result = await task;
        return result.Succeeded ? Result<object>.Success(result.Value!) : Result<object>.Failure(result.Status, result.Message);
    }
}
