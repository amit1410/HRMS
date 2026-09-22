using HRMS.Application.Services;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using HRMS.Application.DTOs.Payroll;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

internal static class PayrollAnalyticsProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant, DatabaseProviderType provider)
    {
        var tenantId = Guid.NewGuid(); tenant.TenantId = tenantId; tenant.UserId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var runId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var departmentId = Guid.NewGuid(); var costCenterId = Guid.NewGuid(); var structureId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var assignmentId = Guid.NewGuid(); var runEmployeeId = Guid.NewGuid(); var resultId = Guid.NewGuid();
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"PAS{tenantId:N}"[..12], TenantName = $"Analytics {provider}", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
            db.Departments.Add(new Department { Id = departmentId, TenantId = tenantId, Code = "PAS-DEPT", Name = "Provider Department", IsActive = true });
            db.CostCenters.Add(new CostCenter { Id = costCenterId, TenantId = tenantId, Code = "PAS-CC", Name = "Provider Cost Center", IsActive = true });
            db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "PAS-EMP", FirstName = "Provider", LastName = "Employee", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active });
            db.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "PAS-STRUCT", Name = "Provider structure", IsActive = true });
            db.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = versionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new(2026, 1, 1), IsActive = true });
            db.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = new(2026, 1, 1), MonthlyCtc = 10000m, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
            db.PayrollRunEmployees.Add(new PayrollRunEmployee { Id = runEmployeeId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EmploymentSnapshotDate = new(2026, 9, 30), DepartmentId = departmentId, CostCenterId = costCenterId, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible });
            db.PayrollResults.Add(new PayrollResult { Id = resultId, TenantId = tenantId, PayrollRunId = runId, PayrollRunEmployeeId = runEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = new(2026, 9, 1), PeriodEndDate = new(2026, 9, 30), EmploymentSnapshotDate = new(2026, 9, 30), CalendarDays = 30, EligibleDays = 30, CurrencyCode = "INR", GrossEarnings = 10000m, TotalDeductions = 1000m, NetPay = 9000m, Status = PayrollResultStatus.Calculated, IsCurrent = true, Components = { new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, ComponentCode = "PAS-BASIC", ComponentName = "Provider basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryStructureCalculationType.FixedAmount, CalculatedAmount = 10000m, IsEarning = true, CalculationSource = "Salary", CalculationSequence = 1 } } });
            db.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = $"PAS-{provider}", Name = "Provider period", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Open });
            db.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = $"PAS-{provider}-RUN", Status = PayrollRunStatus.Calculated }); await db.SaveChangesAsync();
            var service = new PayrollAnalyticsService(db, tenant, TimeProvider.System); var control = await service.CreateControlAsync(new PayrollAnalyticsControlRequest { Code = "PAS_GROSS", Name = "Provider gross control", Scope = PayrollControlScope.PayrollRun, Metric = PayrollControlMetric.GrossPay, AbsoluteThreshold = 1, EffectiveFrom = new(2026, 1, 1) }); Assert.True(control.Succeeded, control.Message);
            var pre = await service.GenerateReconciliationAsync(runId, PayrollReconciliationType.PrePayroll); Assert.True(pre.Succeeded, pre.Message); var post = await service.GenerateReconciliationAsync(runId, PayrollReconciliationType.PostPayroll); Assert.True(post.Succeeded, post.Message); var repeat = await service.GenerateReconciliationAsync(runId, PayrollReconciliationType.PostPayroll); Assert.True(repeat.Succeeded, repeat.Message); Assert.Equal(2, repeat.Value!.Version); Assert.Equal(3, await db.PayrollAnalyticsSnapshots.CountAsync());
            var findings = await service.GetFindingsAsync(runId, new ProviderQuery()); Assert.True(findings.Succeeded, findings.Message); Assert.NotEmpty(findings.Value!.Items);
            var acknowledged = await service.ActOnFindingAsync(findings.Value.Items[0].Id, "acknowledge", new PayrollFindingActionRequest("Provider review", null)); Assert.True(acknowledged.Succeeded, acknowledged.Message);
            var resolved = await service.ActOnFindingAsync(findings.Value.Items[0].Id, "resolve", new PayrollFindingActionRequest("Provider resolved", "PAS-RESOLUTION")); Assert.True(resolved.Succeeded, resolved.Message);
            var summary = await service.GetRunSummaryAsync(runId); Assert.True(summary.Succeeded, summary.Message); Assert.Equal(1, summary.Value!.Totals.EmployeeCount); Assert.Equal(10000m, summary.Value.Totals.GrossTotal);
            var totals = await service.GetControlTotalsAsync(runId); Assert.True(totals.Succeeded, totals.Message); Assert.Equal(9000m, totals.Value!.NetPayTotal);
            var departments = await service.GetDepartmentSummaryAsync(runId); Assert.True(departments.Succeeded, departments.Message); Assert.Contains(departments.Value!, x => x.DimensionId == departmentId && x.GrossTotal == 10000m);
            var costCenters = await service.GetCostCenterSummaryAsync(runId); Assert.True(costCenters.Succeeded, costCenters.Message); Assert.Contains(costCenters.Value!, x => x.DimensionId == costCenterId && x.NetPayTotal == 9000m);
            var summaryCsv = await service.ExportRunSummaryAsync(runId); Assert.True(summaryCsv.Succeeded, summaryCsv.Message);
            var totalsCsv = await service.ExportControlTotalsAsync(runId); Assert.True(totalsCsv.Succeeded, totalsCsv.Message);
            var exceptionsCsv = await service.ExportExceptionsAsync(new ProviderQuery()); Assert.True(exceptionsCsv.Succeeded, exceptionsCsv.Message);
            var varianceCsv = await service.ExportVarianceAsync(runId, null); Assert.True(varianceCsv.Succeeded, varianceCsv.Message); Assert.StartsWith("payroll-variance-", varianceCsv.Value!.FileName);
            var findingsCsv = await service.ExportFindingsAsync(runId); Assert.True(findingsCsv.Succeeded, findingsCsv.Message); Assert.StartsWith("payroll-findings-", findingsCsv.Value!.FileName);
            Assert.False((await new PayrollAnalyticsService(db, new TestTenantContext(Guid.NewGuid()), TimeProvider.System).GetOverviewAsync(runId)).Succeeded);
        }
        finally { await tx.RollbackAsync(); db.ClearChangeTracker(); }
    }

    private sealed class ProviderQuery : PagedQuery { public ProviderQuery() { PageSize = 100; } }
}
