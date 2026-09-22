using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollAnalyticsLargeDataTests
{
    [Fact]
    public async Task Five_hundred_employee_results_support_paging_variance_and_tenant_isolation()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var previousRunId = Guid.NewGuid();
        var currentRunId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext()))
        {
            seed.Tenants.AddRange(
                new Tenant { Id = tenantId, TenantCode = $"PL{tenantId:N}"[..10], TenantName = "Analytics large", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active },
                new Tenant { Id = otherTenantId, TenantCode = $"PO{otherTenantId:N}"[..10], TenantName = "Analytics other", Host = $"{otherTenantId:N}.test", ShardKey = otherTenantId.ToString("N"), Status = TenantStatus.Active });
            seed.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "AN-LARGE", Name = "Analytics large", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 8, 1), EndDate = new(2026, 8, 31), PayDate = new(2026, 9, 5), FiscalYear = 2026, PeriodNumber = 8, Status = PayrollPeriodStatus.Closed });
            seed.PayrollRuns.AddRange(
                new PayrollRun { Id = previousRunId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "AN-LARGE-PREV", Status = PayrollRunStatus.Calculated },
                new PayrollRun { Id = currentRunId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "AN-LARGE-CURR", Status = PayrollRunStatus.Calculated });
            var structureId = Guid.NewGuid();
            var structureVersionId = Guid.NewGuid();
            var departmentA = Guid.NewGuid();
            var departmentB = Guid.NewGuid();
            var costCenterA = Guid.NewGuid();
            var costCenterB = Guid.NewGuid();
            seed.Departments.AddRange(
                new Department { Id = departmentA, TenantId = tenantId, Code = "AN-D-A", Name = "Analytics Department A" },
                new Department { Id = departmentB, TenantId = tenantId, Code = "AN-D-B", Name = "Analytics Department B" });
            seed.CostCenters.AddRange(
                new CostCenter { Id = costCenterA, TenantId = tenantId, Code = "AN-CC-A", Name = "Analytics Cost Center A" },
                new CostCenter { Id = costCenterB, TenantId = tenantId, Code = "AN-CC-B", Name = "Analytics Cost Center B" });
            seed.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "AN-LARGE-STRUCT", Name = "Analytics large structure" });
            seed.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = structureVersionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new(2026, 1, 1) });
            var employees = Enumerable.Range(1, 500).Select(i => new Employee { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCode = $"AN-{i:0000}", FirstName = "Analytics", LastName = i.ToString(), Email = $"analytics{i}@test.local", DateOfJoining = new(2022, 1, 1), Status = EmployeeStatus.Active }).ToList();
            seed.Employees.AddRange(employees);
            foreach (var (employee, index) in employees.Select((employee, index) => (employee, index)))
            {
                var assignmentId = Guid.NewGuid();
                seed.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employee.Id, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, EffectiveFrom = new(2026, 1, 1) });
                var previousRunEmployeeId = Guid.NewGuid();
                var currentRunEmployeeId = Guid.NewGuid();
                var departmentId = index % 2 == 0 ? departmentA : departmentB;
                var costCenterId = index % 2 == 0 ? costCenterA : costCenterB;
                seed.PayrollRunEmployees.AddRange(new PayrollRunEmployee { Id = previousRunEmployeeId, TenantId = tenantId, PayrollRunId = previousRunId, EmployeeId = employee.Id, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, EmploymentSnapshotDate = new(2026, 8, 31), DepartmentId = departmentId, CostCenterId = costCenterId, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible }, new PayrollRunEmployee { Id = currentRunEmployeeId, TenantId = tenantId, PayrollRunId = currentRunId, EmployeeId = employee.Id, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, EmploymentSnapshotDate = new(2026, 8, 31), DepartmentId = departmentId, CostCenterId = costCenterId, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible });
                var previousResult = new PayrollResult { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = previousRunId, PayrollRunEmployeeId = previousRunEmployeeId, EmployeeId = employee.Id, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = new(2026, 8, 1), PeriodEndDate = new(2026, 8, 31), EmploymentSnapshotDate = new(2026, 8, 31), CalendarDays = 31, EligibleDays = 31, CalculationDateUtc = DateTime.UtcNow, GrossEarnings = 1000, TotalDeductions = 100, NetPay = 900 };
                var currentResult = new PayrollResult { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = currentRunId, PayrollRunEmployeeId = currentRunEmployeeId, EmployeeId = employee.Id, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = new(2026, 8, 1), PeriodEndDate = new(2026, 8, 31), EmploymentSnapshotDate = new(2026, 8, 31), CalendarDays = 31, EligibleDays = 31, CalculationDateUtc = DateTime.UtcNow, GrossEarnings = 1100, TotalDeductions = 100, NetPay = 1000 };
                seed.PayrollResults.AddRange(previousResult, currentResult);
                seed.PayrollResultComponents.AddRange(new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, PayrollResultId = previousResult.Id, CalculationAttemptId = previousResult.CalculationAttemptId, ComponentCode = "ANALYTICS", ComponentName = "Analytics component", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryStructureCalculationType.FixedAmount, CalculatedAmount = 100m, IsEarning = true, CalculationSource = "Salary" }, new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, PayrollResultId = currentResult.Id, CalculationAttemptId = currentResult.CalculationAttemptId, ComponentCode = "ANALYTICS", ComponentName = "Analytics component", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryStructureCalculationType.FixedAmount, CalculatedAmount = 200m, IsEarning = true, CalculationSource = "Salary" });
            }
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var service = new PayrollAnalyticsService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var control = await service.CreateControlAsync(new PayrollAnalyticsControlRequest { Code = "AN-LARGE-GROSS", Name = "Large gross threshold", Scope = PayrollControlScope.PayrollRun, Metric = PayrollControlMetric.GrossPay, AbsoluteThreshold = 1m, EffectiveFrom = new(2026, 1, 1) });
        Assert.True(control.Succeeded, control.Message);
        var pageOne = await service.GetVarianceAsync(currentRunId, previousRunId, new TestPageQuery { Page = 1, PageSize = 50 });
        var pageTen = await service.GetVarianceAsync(currentRunId, previousRunId, new TestPageQuery { Page = 10, PageSize = 50 });
        Assert.True(pageOne.Succeeded, pageOne.Message);
        Assert.True(pageTen.Succeeded, pageTen.Message);
        Assert.Equal(500, pageOne.Value!.TotalCount);
        Assert.Equal(50, pageOne.Value.Items.Count);
        Assert.Equal(50, pageTen.Value!.Items.Count);
        Assert.All(pageOne.Value.Items, row => Assert.Equal(100m, row.GrossDelta));
        var components = await service.GetComponentVarianceAsync(currentRunId, previousRunId);
        Assert.True(components.Succeeded, components.Message); Assert.Contains(components.Value!, x => x.ComponentCode == "ANALYTICS" && x.CurrentAmount == 100000m);
        var summary = await service.GetRunSummaryAsync(currentRunId);
        var controlTotals = await service.GetControlTotalsAsync(currentRunId);
        var departments = await service.GetDepartmentSummaryAsync(currentRunId);
        var costCenters = await service.GetCostCenterSummaryAsync(currentRunId);
        Assert.True(summary.Succeeded, summary.Message);
        Assert.True(controlTotals.Succeeded, controlTotals.Message);
        Assert.True(departments.Succeeded, departments.Message);
        Assert.True(costCenters.Succeeded, costCenters.Message);
        Assert.Equal(500, summary.Value!.Totals.EmployeeCount);
        Assert.Equal(500, summary.Value.Totals.ResultCount);
        Assert.Equal(summary.Value.Totals.GrossTotal, departments.Value!.Sum(x => x.GrossTotal));
        Assert.Equal(summary.Value.Totals.DeductionTotal, departments.Value.Sum(x => x.DeductionTotal));
        Assert.Equal(summary.Value.Totals.NetPayTotal, departments.Value.Sum(x => x.NetPayTotal));
        Assert.Equal(summary.Value.Totals.GrossTotal, costCenters.Value!.Sum(x => x.GrossTotal));
        Assert.Equal(summary.Value.Totals.DeductionTotal, costCenters.Value.Sum(x => x.DeductionTotal));
        Assert.Equal(summary.Value.Totals.NetPayTotal, costCenters.Value.Sum(x => x.NetPayTotal));
        Assert.Equal(summary.Value.Totals.GrossTotal, controlTotals.Value!.GrossTotal);
        var reconciliation = await service.GenerateReconciliationAsync(currentRunId, PayrollReconciliationType.PostPayroll);
        Assert.True(reconciliation.Succeeded, reconciliation.Message); Assert.Contains(reconciliation.Value!.Findings, x => x.ControlCode == "AN-LARGE-GROSS");
        await using var other = database.CreateContext(new TestTenantContext(otherTenantId));
        Assert.False((await new PayrollAnalyticsService(other, new TestTenantContext(otherTenantId), TimeProvider.System).GetVarianceAsync(currentRunId, previousRunId, new TestPageQuery())).Succeeded);
    }

    private sealed class TestPageQuery : PagedQuery { }
}
