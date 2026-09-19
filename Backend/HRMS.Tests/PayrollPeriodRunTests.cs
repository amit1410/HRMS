using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollPeriodRunTests
{
    [Fact]
    public async Task Period_validates_overlap_and_lifecycle_and_is_tenant_scoped()
    {
        using var database = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var other = Guid.NewGuid(); await SeedTenant(database, tenant); await SeedTenant(database, other);
        await using var db = database.CreateContext(new TestTenantContext(tenant)); var service = new PayrollPeriodService(db, new TestTenantContext(tenant), TimeProvider.System);
        var request = Period("SEP-26", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)); var created = await service.CreateAsync(request); Assert.True(created.Succeeded, created.Message);
        Assert.Equal(ResultStatus.Conflict, (await service.CreateAsync(Period("OVERLAP", new DateOnly(2026, 9, 15), new DateOnly(2026, 10, 15)))).Status);
        var opened = await service.TransitionAsync(created.Value!.Id, "open", created.Value.ConcurrencyVersion); Assert.True(opened.Succeeded);
        var closed = await service.TransitionAsync(created.Value.Id, "close", opened.Value!.ConcurrencyVersion); Assert.True(closed.Succeeded);
        var locked = await service.TransitionAsync(created.Value.Id, "lock", closed.Value!.ConcurrencyVersion); Assert.True(locked.Succeeded);
        Assert.Equal(ResultStatus.Conflict, (await service.UpdateAsync(created.Value.Id, request)).Status);
        await using var otherDb = database.CreateContext(new TestTenantContext(other)); var otherService = new PayrollPeriodService(otherDb, new TestTenantContext(other), TimeProvider.System); Assert.Equal(ResultStatus.NotFound, (await otherService.GetByIdAsync(created.Value.Id)).Status);
    }

    [Fact]
    public async Task Run_preparation_snapshots_eligible_and_excluded_employees_and_lifecycle_is_explicit()
    {
        using var database = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); await SeedTenant(database, tenant); var employee = Guid.NewGuid(); var inactive = Guid.NewGuid(); var structure = Guid.NewGuid(); var version = Guid.NewGuid(); await SeedPayrollData(database, tenant, employee, inactive, structure, version);
        await using var db = database.CreateContext(new TestTenantContext(tenant)); var periods = new PayrollPeriodService(db, new TestTenantContext(tenant), TimeProvider.System); var period = await periods.CreateAsync(Period("SEP", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30))); Assert.True(period.Succeeded, period.Message); var runs = new PayrollRunService(db, new TestTenantContext(tenant), TimeProvider.System); var run = await runs.CreateAsync(new PayrollRunRequest { PayrollPeriodId = period.Value!.Id }); Assert.True(run.Succeeded, run.Message);
        var prepared = await runs.PrepareAsync(run.Value!.Id, false); Assert.True(prepared.Succeeded, prepared.Message); Assert.Equal(2, prepared.Value!.EmployeeCount); Assert.Equal(1, prepared.Value.EligibleCount); Assert.Equal(1, prepared.Value.ExcludedCount); var rows = await runs.GetEmployeesAsync(run.Value.Id, new PayrollRunQuery { Page = 1, PageSize = 10 }); Assert.True(rows.Succeeded); Assert.Contains(rows.Value!.Items, x => x.EmployeeId == employee && x.SalaryStructureVersionId == version && x.IsEligible); Assert.Contains(rows.Value.Items, x => x.EmployeeId == inactive && !x.IsEligible);
        Assert.Equal(ResultStatus.Conflict, (await runs.TransitionAsync(run.Value.Id, PayrollRunStatus.Approved)).Status); Assert.True((await runs.TransitionAsync(run.Value.Id, PayrollRunStatus.Processing)).Succeeded); Assert.Equal(ResultStatus.Conflict, (await runs.PrepareAsync(run.Value.Id, true)).Status); Assert.NotEmpty((await runs.GetHistoryAsync(run.Value.Id)).Value!);
    }

    private static PayrollPeriodRequest Period(string code, DateOnly from, DateOnly to) => new() { Code = code, Name = code, PeriodType = PayrollPeriodType.Monthly, StartDate = from, EndDate = to, PayDate = to.AddDays(5), FiscalYear = from.Year, PeriodNumber = from.Month };
    private static async Task SeedTenant(SqliteInMemoryDatabase database, Guid id) { await using var db = database.CreateContext(new TestTenantContext()); db.Tenants.Add(new Tenant { Id = id, TenantCode = $"T{id:N}"[..12], TenantName = "Test", Host = $"{id:N}.test", ShardKey = id.ToString("N") }); await db.SaveChangesAsync(); }
    private static async Task SeedPayrollData(SqliteInMemoryDatabase database, Guid tenant, Guid employeeId, Guid inactiveId, Guid structure, Guid version)
    {
        await using var db = database.CreateContext(new TestTenantContext(tenant)); var component = new SalaryComponent { Id = Guid.NewGuid(), TenantId = tenant, Code = "BASIC" + Guid.NewGuid().ToString("N")[..6], Name = "Basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true }; db.SalaryComponents.Add(component); db.Employees.AddRange(new Employee { Id = employeeId, TenantId = tenant, EmployeeCode = "E1" + employeeId.ToString("N")[..5], FirstName = "Eligible", Email = employeeId + "@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active }, new Employee { Id = inactiveId, TenantId = tenant, EmployeeCode = "E2" + inactiveId.ToString("N")[..5], FirstName = "Inactive", Email = inactiveId + "@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Resigned }); db.SalaryStructures.Add(new SalaryStructure { Id = structure, TenantId = tenant, Code = "STAFF" + structure.ToString("N")[..5], Name = "Staff", IsActive = true }); db.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = version, TenantId = tenant, SalaryStructureId = structure, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true, Components = { new SalaryStructureComponent { Id = Guid.NewGuid(), TenantId = tenant, SalaryStructureVersionId = version, SalaryComponentId = component.Id, Sequence = 1, CalculationType = SalaryStructureCalculationType.FixedAmount, Value = 1000, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true } } }); db.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employeeId, SalaryStructureId = structure, SalaryStructureVersionId = version, EffectiveFrom = new DateOnly(2026, 1, 1), MonthlyCtc = 1000, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active }); await db.SaveChangesAsync();
    }
}
