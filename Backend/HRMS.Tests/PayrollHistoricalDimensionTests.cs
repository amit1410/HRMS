using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollHistoricalDimensionTests
{
    [Fact]
    public async Task Prepare_captures_effective_dimensions_and_preserves_them_after_transfer()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        var employeeId = Guid.NewGuid();
        var departmentA = Guid.NewGuid();
        var departmentB = Guid.NewGuid();
        var costCenterA = Guid.NewGuid();
        var costCenterB = Guid.NewGuid();
        var structureId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();

        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.Departments.AddRange(
                new Department { Id = departmentA, TenantId = tenantId, Code = "DEPT-A", Name = "Department A" },
                new Department { Id = departmentB, TenantId = tenantId, Code = "DEPT-B", Name = "Department B" });
            seed.CostCenters.AddRange(
                new CostCenter { Id = costCenterA, TenantId = tenantId, Code = "CC-A", Name = "Cost Center A" },
                new CostCenter { Id = costCenterB, TenantId = tenantId, Code = "CC-B", Name = "Cost Center B" });
            seed.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "HIST-001", FirstName = "Historical", LastName = "Snapshot", Email = "hist-001@test.local", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active });
            seed.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "HIST-STRUCT", Name = "Historical Structure", IsActive = true });
            seed.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = versionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new(2025, 1, 1), IsActive = true });
            seed.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = new(2025, 1, 1), MonthlyCtc = 10000, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
            seed.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, EffectiveFrom = new(2025, 1, 1), EffectiveTo = new(2026, 9, 30), DepartmentId = departmentA, CostCenterId = costCenterA, EmploymentStatus = EmployeeStatus.Active });
            seed.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, EffectiveFrom = new(2026, 10, 1), DepartmentId = departmentB, CostCenterId = costCenterB, EmploymentStatus = EmployeeStatus.Active });
            seed.PayrollPeriods.AddRange(
                new PayrollPeriod { Id = Guid.NewGuid(), TenantId = tenantId, Code = "HIST-SEP", Name = "Historical September", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Open },
                new PayrollPeriod { Id = Guid.NewGuid(), TenantId = tenantId, Code = "HIST-OCT", Name = "Historical October", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 31), PayDate = new(2026, 11, 5), FiscalYear = 2026, PeriodNumber = 10, Status = PayrollPeriodStatus.Open });
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var periods = await db.PayrollPeriods.OrderBy(x => x.EndDate).ToListAsync();
        var firstRunId = Guid.NewGuid();
        var laterRunId = Guid.NewGuid();
        db.PayrollRuns.AddRange(
            new PayrollRun { Id = firstRunId, TenantId = tenantId, PayrollPeriodId = periods[0].Id, RunNumber = "HIST-SEP-RUN", Status = PayrollRunStatus.Draft },
            new PayrollRun { Id = laterRunId, TenantId = tenantId, PayrollPeriodId = periods[1].Id, RunNumber = "HIST-OCT-RUN", Status = PayrollRunStatus.Draft });
        await db.SaveChangesAsync();
        var service = new PayrollRunService(db, new TestTenantContext(tenantId), TimeProvider.System);

        Assert.True((await service.PrepareAsync(firstRunId, false)).Succeeded);
        var firstSnapshot = await db.PayrollRunEmployees.SingleAsync(x => x.PayrollRunId == firstRunId);
        Assert.Equal(departmentA, firstSnapshot.DepartmentId);
        Assert.Equal(costCenterA, firstSnapshot.CostCenterId);

        Assert.True((await service.PrepareAsync(laterRunId, false)).Succeeded);
        var laterSnapshot = await db.PayrollRunEmployees.SingleAsync(x => x.PayrollRunId == laterRunId);
        Assert.Equal(departmentB, laterSnapshot.DepartmentId);
        Assert.Equal(costCenterB, laterSnapshot.CostCenterId);

        var persistedFirstSnapshot = await db.PayrollRunEmployees.AsNoTracking().SingleAsync(x => x.PayrollRunId == firstRunId);
        Assert.Equal(departmentA, persistedFirstSnapshot.DepartmentId);
        Assert.Equal(costCenterA, persistedFirstSnapshot.CostCenterId);
    }

    [Fact]
    public async Task Dimension_summaries_use_snapshots_and_keep_null_dimensions_unclassified()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        await SeedTenant(database, otherTenantId);
        var runId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var costCenterId = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var structureId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var assignmentA = Guid.NewGuid();
        var assignmentB = Guid.NewGuid();

        await using (var seed = database.CreateContext(new TestTenantContext()))
        {
            seed.Departments.Add(new Department { Id = departmentId, TenantId = tenantId, Code = "DEPT-H", Name = "Historical Department" });
            seed.CostCenters.Add(new CostCenter { Id = costCenterId, TenantId = tenantId, Code = "CC-H", Name = "Historical Cost Center" });
            seed.Employees.AddRange(
                new Employee { Id = employeeA, TenantId = tenantId, EmployeeCode = "DIM-001", FirstName = "Dimension", LastName = "A", Email = "dim-a@test.local", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active },
                new Employee { Id = employeeB, TenantId = tenantId, EmployeeCode = "DIM-002", FirstName = "Dimension", LastName = "B", Email = "dim-b@test.local", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active });
            seed.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "DIM-P", Name = "Dimension Period", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Closed });
            seed.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "DIM-RUN", Status = PayrollRunStatus.Calculated });
            seed.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "DIM-S", Name = "Dimension Structure", IsActive = true });
            seed.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = versionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new(2025, 1, 1), IsActive = true });
            seed.EmployeeSalaryAssignments.AddRange(
                new EmployeeSalaryAssignment { Id = assignmentA, TenantId = tenantId, EmployeeId = employeeA, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = new(2025, 1, 1), MonthlyCtc = 10000, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active },
                new EmployeeSalaryAssignment { Id = assignmentB, TenantId = tenantId, EmployeeId = employeeB, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = new(2025, 1, 1), MonthlyCtc = 5000, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
            var snapshotA = new PayrollRunEmployee { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeA, EmployeeSalaryAssignmentId = assignmentA, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EmploymentSnapshotDate = new(2026, 9, 30), DepartmentId = departmentId, CostCenterId = costCenterId, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible };
            var snapshotB = new PayrollRunEmployee { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeB, EmployeeSalaryAssignmentId = assignmentB, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EmploymentSnapshotDate = new(2026, 9, 30), DepartmentId = null, CostCenterId = null, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible };
            seed.PayrollRunEmployees.AddRange(snapshotA, snapshotB);
            seed.PayrollResults.AddRange(
                Result(tenantId, runId, snapshotA.Id, employeeA, assignmentA, structureId, versionId, 10000, 1000, 9000),
                Result(tenantId, runId, snapshotB.Id, employeeB, assignmentB, structureId, versionId, 5000, 500, 4500));
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var service = new PayrollAnalyticsService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var departments = await service.GetDepartmentSummaryAsync(runId);
        var costCenters = await service.GetCostCenterSummaryAsync(runId);
        Assert.True(departments.Succeeded, departments.Message);
        Assert.True(costCenters.Succeeded, costCenters.Message);
        Assert.Contains(departments.Value!, x => x.DimensionId == departmentId && x.EmployeeCount == 1 && x.GrossTotal == 10000m && x.NetPayTotal == 9000m);
        Assert.Contains(departments.Value!, x => x.DimensionId is null && x.Code == "UNCLASSIFIED" && x.GrossTotal == 5000m);
        Assert.Contains(costCenters.Value!, x => x.DimensionId == costCenterId && x.GrossTotal == 10000m);
        Assert.Contains(costCenters.Value!, x => x.DimensionId is null && x.Code == "UNCLASSIFIED" && x.GrossTotal == 5000m);
        Assert.Equal(departments.Value!.OrderBy(x => x.Code).Select(x => x.Code), departments.Value.Select(x => x.Code));
        Assert.False((await new PayrollAnalyticsService(db, new TestTenantContext(otherTenantId), TimeProvider.System).GetDepartmentSummaryAsync(runId)).Succeeded);
    }

    private static PayrollResult Result(Guid tenantId, Guid runId, Guid runEmployeeId, Guid employeeId, Guid assignmentId, Guid structureId, Guid versionId, decimal gross, decimal deductions, decimal net) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = runId, PayrollRunEmployeeId = runEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId,
        CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = new(2026, 9, 1), PeriodEndDate = new(2026, 9, 30), EmploymentSnapshotDate = new(2026, 9, 30),
        CalendarDays = 30, EligibleDays = 30, CurrencyCode = "INR", GrossEarnings = gross, TotalDeductions = deductions, NetPay = net,
        Status = PayrollResultStatus.Calculated, IsCurrent = true
    };

    private static async Task SeedTenant(SqliteInMemoryDatabase database, Guid tenantId)
    {
        await using var db = database.CreateContext(new TestTenantContext());
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"HD{tenantId:N}"[..10], TenantName = "Historical dimensions", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
        await db.SaveChangesAsync();
    }
}
