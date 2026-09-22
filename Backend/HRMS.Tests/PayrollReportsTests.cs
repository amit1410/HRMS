using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using Microsoft.EntityFrameworkCore;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class PayrollReportsTests
{
    [Fact]
    public async Task Register_and_dashboard_are_read_only_and_use_historical_dimensions()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        db.Tenants.Add(new HRMS.Domain.Entities.Tenant { Id = tenantId, TenantCode = $"RPT{tenantId:N}"[..12], TenantName = "Reports", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = HRMS.Domain.Enums.TenantStatus.Active });
        await db.SaveChangesAsync();
        var department = new HRMS.Domain.Entities.Department { Id = Guid.NewGuid(), TenantId = tenantId, Code = "D-A", Name = "Department A" };
        var center = new HRMS.Domain.Entities.CostCenter { Id = Guid.NewGuid(), TenantId = tenantId, Code = "CC-A", Name = "Cost Center A" };
        var employee = new HRMS.Domain.Entities.Employee { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCode = "EMP-7T", FirstName = "Report", LastName = "Employee", Email = "report@example.test", DateOfJoining = new(2020, 1, 1) };
        var structure = new HRMS.Domain.Entities.SalaryStructure { Id = Guid.NewGuid(), TenantId = tenantId, Code = "RPT-STRUCT", Name = "Reports", IsActive = true };
        var version = new HRMS.Domain.Entities.SalaryStructureVersion { Id = Guid.NewGuid(), TenantId = tenantId, SalaryStructureId = structure.Id, EffectiveFrom = new(2020, 1, 1), IsActive = true };
        var assignment = new HRMS.Domain.Entities.EmployeeSalaryAssignment { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, SalaryStructureId = structure.Id, SalaryStructureVersionId = version.Id, EffectiveFrom = new(2020, 1, 1), MonthlyCtc = 1000m, CurrencyCode = "INR", Status = HRMS.Domain.Enums.EmployeeSalaryAssignmentStatus.Active };
        var period = new HRMS.Domain.Entities.PayrollPeriod { Id = Guid.NewGuid(), TenantId = tenantId, Code = "2026-01", Name = "January", StartDate = new(2026, 1, 1), EndDate = new(2026, 1, 31), PayDate = new(2026, 1, 31), FiscalYear = 2026, PeriodNumber = 1 };
        var run = new HRMS.Domain.Entities.PayrollRun { Id = Guid.NewGuid(), TenantId = tenantId, PayrollPeriodId = period.Id, RunNumber = "RUN-7T", PayrollPeriod = period };
        var runEmployee = new HRMS.Domain.Entities.PayrollRunEmployee { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = run.Id, EmployeeId = employee.Id, EmployeeSalaryAssignmentId = assignment.Id, SalaryStructureId = structure.Id, SalaryStructureVersionId = version.Id, DepartmentId = department.Id, CostCenterId = center.Id, EmploymentSnapshotDate = period.EndDate, Status = HRMS.Domain.Enums.PayrollRunEmployeeStatus.Eligible };
        var result = new HRMS.Domain.Entities.PayrollResult { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = run.Id, PayrollRunEmployeeId = runEmployee.Id, EmployeeId = employee.Id, EmployeeSalaryAssignmentId = assignment.Id, SalaryStructureId = structure.Id, SalaryStructureVersionId = version.Id, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = period.StartDate, PeriodEndDate = period.EndDate, EmploymentSnapshotDate = period.EndDate, CalculationDateUtc = DateTime.UtcNow, CalculatedAtUtc = DateTime.UtcNow, GrossEarnings = 100m, TotalDeductions = 20m, NetPay = 80m, EmployerContributions = 10m, Employee = employee, PayrollRunEmployee = runEmployee, PayrollRun = run };
        db.Departments.Add(department); db.CostCenters.Add(center); db.Employees.Add(employee); db.SalaryStructures.Add(structure); db.SalaryStructureVersions.Add(version); db.EmployeeSalaryAssignments.Add(assignment); db.PayrollPeriods.Add(period); db.PayrollRuns.Add(run); db.PayrollRunEmployees.Add(runEmployee); db.PayrollResults.Add(result);
        await db.SaveChangesAsync();
        var service = new PayrollReportsService(db, new TestTenantContext(tenantId));
        var register = await service.GetRegisterAsync(new PayrollReportQuery { PayrollRunId = run.Id });
        var dashboard = await service.GetDashboardAsync(new PayrollReportQuery { PayrollRunId = run.Id });
        Assert.True(register.Succeeded, register.Message); Assert.Single(register.Value!.Items); Assert.Equal("D-A", register.Value.Items[0].Department); Assert.Equal("CC-A", register.Value.Items[0].CostCenter); Assert.Equal(100m, dashboard.Value!.GrossTotal); Assert.Equal(80m, dashboard.Value.NetPayTotal);
        var filtered = await service.GetRegisterAsync(new PayrollReportQuery { PayrollRunId = run.Id, EmployeeId = employee.Id, DepartmentId = department.Id, CostCenterId = center.Id, Page = 1, PageSize = 1 });
        Assert.True(filtered.Succeeded, filtered.Message); Assert.Single(filtered.Value!.Items); Assert.Equal(1, filtered.Value.TotalCount);
        Assert.Empty((await service.GetRegisterAsync(new PayrollReportQuery { PayrollRunId = run.Id, EmployeeId = Guid.NewGuid() })).Value!.Items);
        Assert.True((await service.GetEarningsAsync(new PayrollReportQuery { PayrollRunId = run.Id, PageSize = 50 })).Succeeded);
        Assert.True((await service.GetDeductionsAsync(new PayrollReportQuery { PayrollRunId = run.Id, PageSize = 50 })).Succeeded);
        Assert.True((await service.GetEmployerContributionsAsync(new PayrollReportQuery { PayrollRunId = run.Id, PageSize = 50 })).Succeeded);
        Assert.True((await service.GetDepartmentsAsync(new PayrollReportQuery { PayrollRunId = run.Id })).Succeeded);
        Assert.True((await service.GetCostCentersAsync(new PayrollReportQuery { PayrollRunId = run.Id })).Succeeded);
        Assert.True((await service.GetComponentsAsync(new PayrollReportQuery { PayrollRunId = run.Id })).Succeeded);
        Assert.True((await service.GetStatutorySummaryAsync(new PayrollReportQuery { PayrollRunId = run.Id })).Succeeded);
        Assert.True((await service.GetBankPaymentsAsync(new PayrollReportQuery { PayrollRunId = run.Id, PageSize = 50 })).Succeeded);
        Assert.True((await service.GetAccountingSummaryAsync(new PayrollReportQuery { PayrollRunId = run.Id })).Succeeded);
        Assert.True((await service.GetLoanRecoveriesAsync(new PayrollReportQuery { PayrollRunId = run.Id, PageSize = 50 })).Succeeded);
        Assert.True((await service.GetReimbursementsAsync(new PayrollReportQuery { PayrollRunId = run.Id, PageSize = 50 })).Succeeded);
        Assert.True((await service.GetVariablePayAsync(new PayrollReportQuery { PayrollRunId = run.Id, PageSize = 50 })).Succeeded);
        Assert.True((await service.GetAdjustmentsAsync(new PayrollReportQuery { PayrollRunId = run.Id, PageSize = 50 })).Succeeded);
        Assert.True((await service.GetFinalSettlementsAsync(new PayrollReportQuery { PayrollRunId = run.Id, PageSize = 50 })).Succeeded);
        Assert.True((await service.GetOffCycleAsync(new PayrollReportQuery { PayrollRunId = run.Id, PageSize = 50 })).Succeeded);
        Assert.True((await service.GetTrendsAsync(new PayrollReportQuery { PayrollRunId = run.Id })).Succeeded);
        foreach (var reportName in new[] { "earnings", "deductions", "employer-contributions", "departments", "cost-centers", "components", "statutory-summary", "bank-payments", "accounting-summary", "loan-recoveries", "reimbursements", "variable-pay", "adjustments", "final-settlements", "off-cycle" })
        {
            var export = await service.ExportAsync(reportName, new PayrollReportQuery { PayrollRunId = run.Id, PageSize = 50 });
            Assert.True(export.Succeeded, $"{reportName}: {export.Message}");
        }
        var otherTenantResult = await new PayrollReportsService(db, new TestTenantContext(Guid.NewGuid())).GetRegisterAsync(new PayrollReportQuery { PayrollRunId = run.Id });
        Assert.True(otherTenantResult.Succeeded, otherTenantResult.Message); Assert.Empty(otherTenantResult.Value!.Items);
        Assert.Equal(1, await db.PayrollResults.CountAsync());
    }
}
