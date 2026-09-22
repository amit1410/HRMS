using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollReportsLargeDataTests
{
    [Fact]
    public async Task Reports_support_three_runs_and_one_thousand_employees()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var structureId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var departmentId = Guid.NewGuid(); var costCenterId = Guid.NewGuid(); var runIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await using (var seed = database.CreateContext(new TestTenantContext()))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"RPL{tenantId:N}"[..12], TenantName = "Reports large", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
            seed.Departments.Add(new Department { Id = departmentId, TenantId = tenantId, Code = "RPT-D", Name = "Reports" }); seed.CostCenters.Add(new CostCenter { Id = costCenterId, TenantId = tenantId, Code = "RPT-CC", Name = "Reports" });
            seed.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "RPT-S", Name = "Reports", IsActive = true }); seed.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = versionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new(2026, 1, 1), IsActive = true });
            var periods = Enumerable.Range(0, 3).Select(i => new PayrollPeriod { Id = Guid.NewGuid(), TenantId = tenantId, Code = $"RPT-{i}", Name = $"Report {i}", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, i + 1, 1), EndDate = new(2026, i + 1, DateTime.DaysInMonth(2026, i + 1)), PayDate = new(2026, i + 1, DateTime.DaysInMonth(2026, i + 1)), FiscalYear = 2026, PeriodNumber = i + 1, Status = PayrollPeriodStatus.Closed }).ToList(); seed.PayrollPeriods.AddRange(periods); seed.PayrollRuns.AddRange(runIds.Select((id, i) => new PayrollRun { Id = id, TenantId = tenantId, PayrollPeriodId = periods[i].Id, RunNumber = $"RPT-RUN-{i}", RunType = PayrollRunType.Regular, Status = PayrollRunStatus.Calculated }));
            for (var i = 0; i < 1000; i++) { var employee = new Employee { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCode = $"RPT-{i:0000}", FirstName = "Report", LastName = i.ToString(), Email = $"rpt{i}@test.local", DateOfJoining = new(2020, 1, 1), Status = EmployeeStatus.Active }; seed.Employees.Add(employee); var assignment = new EmployeeSalaryAssignment { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = new(2026, 1, 1), MonthlyCtc = 1000m, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active }; seed.EmployeeSalaryAssignments.Add(assignment); for (var run = 0; run < 3; run++) { var runEmployee = new PayrollRunEmployee { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = runIds[run], EmployeeId = employee.Id, EmployeeSalaryAssignmentId = assignment.Id, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EmploymentSnapshotDate = new(2026, run + 1, 28), DepartmentId = departmentId, CostCenterId = costCenterId, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible }; seed.PayrollRunEmployees.Add(runEmployee); var result = new PayrollResult { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = runIds[run], PayrollRunEmployeeId = runEmployee.Id, EmployeeId = employee.Id, EmployeeSalaryAssignmentId = assignment.Id, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = new(2026, run + 1, 1), PeriodEndDate = new(2026, run + 1, DateTime.DaysInMonth(2026, run + 1)), EmploymentSnapshotDate = new(2026, run + 1, 28), CalculationDateUtc = DateTime.UtcNow, CalculatedAtUtc = DateTime.UtcNow, GrossEarnings = 1000m + run, TotalDeductions = 100m, NetPay = 900m + run, EmployerContributions = 50m }; seed.PayrollResults.Add(result); seed.PayrollResultComponents.Add(new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, PayrollResultId = result.Id, CalculationAttemptId = result.CalculationAttemptId, ComponentCode = "BASIC", ComponentName = "Basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryStructureCalculationType.FixedAmount, CalculatedAmount = 1000m + run, IsEarning = true, CalculationSource = "Salary" }); } }
            await seed.SaveChangesAsync();
        }
        await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new PayrollReportsService(db, new TestTenantContext(tenantId));
        var register = await service.GetRegisterAsync(new PayrollReportQuery { PayrollRunId = runIds[2], Page = 1, PageSize = 50 }); var departments = await service.GetDepartmentsAsync(new PayrollReportQuery { PayrollRunId = runIds[2] }); var components = await service.GetComponentsAsync(new PayrollReportQuery { PayrollRunId = runIds[2] }); var dashboard = await service.GetDashboardAsync(new PayrollReportQuery { PayrollRunId = runIds[2] });
        Assert.True(register.Succeeded, register.Message); Assert.Equal(1000, register.Value!.TotalCount); Assert.Equal(50, register.Value.Items.Count); Assert.Single(departments.Value!); Assert.Equal(1000, departments.Value[0].EmployeeCount); Assert.Equal(1002000m, departments.Value[0].GrossTotal); Assert.Contains(components.Value!, x => x.ComponentCode == "BASIC"); Assert.Equal(1000, dashboard.Value!.EmployeeCount);
    }
}
