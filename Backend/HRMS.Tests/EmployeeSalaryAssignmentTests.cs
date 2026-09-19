using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class EmployeeSalaryAssignmentTests
{
    [Fact]
    public async Task Creates_assignment_binds_exact_structure_version_and_records_history()
    {
        using var database = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); await SeedTenantAsync(database, tenant);
        var employee = await SeedEmployeeAsync(database, tenant, "E001"); var (structure, component) = await SeedStructureAsync(database, tenant, true);
        await using var db = database.CreateContext(new TestTenantContext(tenant)); var service = new EmployeeSalaryAssignmentService(db, new TestTenantContext(tenant), TimeProvider.System);
        var result = await service.CreateAsync(Request(employee, structure, component, SalaryChangeReason.NewHire));
        Assert.True(result.Succeeded, result.Message); Assert.Equal(structure, result.Value!.SalaryStructureId); Assert.Single(db.EmployeeSalaryAssignmentHistories); Assert.Equal(EmployeeSalaryAssignmentChangeType.Created, db.EmployeeSalaryAssignmentHistories.Single().ChangeType);
    }

    [Fact]
    public async Task Rejects_overlap_inactive_structure_and_cross_tenant_references()
    {
        using var database = new SqliteInMemoryDatabase(); var a = Guid.NewGuid(); var b = Guid.NewGuid(); await SeedTenantAsync(database, a); await SeedTenantAsync(database, b);
        var employeeA = await SeedEmployeeAsync(database, a, "E001"); var employeeB = await SeedEmployeeAsync(database, b, "E001"); var (structureA, componentA) = await SeedStructureAsync(database, a, true); var (structureB, componentB) = await SeedStructureAsync(database, b, true);
        await using var db = database.CreateContext(new TestTenantContext(a)); var service = new EmployeeSalaryAssignmentService(db, new TestTenantContext(a), TimeProvider.System);
        Assert.True((await service.CreateAsync(Request(employeeA, structureA, componentA))).Succeeded);
        Assert.Equal(ResultStatus.Conflict, (await service.CreateAsync(Request(employeeA, structureA, componentA))).Status);
        Assert.Equal(ResultStatus.NotFound, (await service.CreateAsync(Request(employeeB, structureA, componentA))).Status);
        Assert.Equal(ResultStatus.NotFound, (await service.CreateAsync(Request(employeeA, structureB, componentB))).Status);
    }

    [Fact]
    public async Task Enforces_override_permissions_and_calculation_validation()
    {
        using var database = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); await SeedTenantAsync(database, tenant); var employee = await SeedEmployeeAsync(database, tenant, "E001"); var employee2 = await SeedEmployeeAsync(database, tenant, "E002"); var (structure, editable) = await SeedStructureAsync(database, tenant, true); var (_, locked) = await SeedStructureAsync(database, tenant, false);
        await using var db = database.CreateContext(new TestTenantContext(tenant)); var service = new EmployeeSalaryAssignmentService(db, new TestTenantContext(tenant), TimeProvider.System);
        var createRequest = Request(employee, structure, editable); createRequest.Components = []; var created = await service.CreateAsync(createRequest); Assert.True(created.Succeeded, created.Message);
        var overrideResult = await service.AddComponentAsync(created.Value!.Id, new EmployeeSalaryComponentRequest { SalaryStructureComponentId = editable, EffectiveFrom = new DateOnly(2026, 1, 1), OverrideValue = 1200 }); Assert.True(overrideResult.Succeeded, overrideResult.Message);
        Assert.Equal(ResultStatus.Conflict, (await service.AddComponentAsync(created.Value.Id, new EmployeeSalaryComponentRequest { SalaryStructureComponentId = editable, EffectiveFrom = new DateOnly(2026, 1, 1), OverrideValue = 1300 })).Status);
        var lockedRequest = Request(employee2, (await SeedStructureIdAsync(db, tenant, locked)), locked); lockedRequest.Components = []; var lockedAssignment = await service.CreateAsync(lockedRequest); Assert.True(lockedAssignment.Succeeded, lockedAssignment.Message); Assert.Equal(ResultStatus.Forbidden, (await service.AddComponentAsync(lockedAssignment.Value!.Id, new EmployeeSalaryComponentRequest { SalaryStructureComponentId = locked, EffectiveFrom = new DateOnly(2026, 1, 1), OverrideValue = 1300 })).Status);
    }

    [Fact]
    public async Task Effective_resolution_history_activation_and_tenant_isolation_work()
    {
        using var database = new SqliteInMemoryDatabase(); var a = Guid.NewGuid(); var b = Guid.NewGuid(); await SeedTenantAsync(database, a); await SeedTenantAsync(database, b); var employee = await SeedEmployeeAsync(database, a, "E001"); var (structure, component) = await SeedStructureAsync(database, a, true);
        await using var db = database.CreateContext(new TestTenantContext(a)); var service = new EmployeeSalaryAssignmentService(db, new TestTenantContext(a), TimeProvider.System); var created = await service.CreateAsync(Request(employee, structure, component)); Assert.True(created.Succeeded);
        Assert.Equal(created.Value!.Id, (await service.GetEffectiveAsync(employee, new DateOnly(2026, 5, 1))).Value!.Id); Assert.True((await service.SetActiveAsync(created.Value.Id, false, created.Value.ConcurrencyVersion)).Succeeded); Assert.Equal(ResultStatus.NotFound, (await service.GetEffectiveAsync(employee, new DateOnly(2026, 5, 1))).Status); Assert.NotEmpty((await service.GetHistoryAsync(created.Value.Id)).Value!);
        await using var other = database.CreateContext(new TestTenantContext(b)); var otherService = new EmployeeSalaryAssignmentService(other, new TestTenantContext(b), TimeProvider.System); Assert.Equal(ResultStatus.NotFound, (await otherService.GetByIdAsync(created.Value.Id)).Status);
    }

    private static EmployeeSalaryAssignmentRequest Request(Guid employee, Guid structure, Guid component, SalaryChangeReason reason = SalaryChangeReason.Increment) => new() { EmployeeId = employee, SalaryStructureId = structure, EffectiveFrom = new DateOnly(2026, 1, 1), AnnualCtc = 120000, MonthlyCtc = 10000, ChangeReason = reason, Components = [new EmployeeSalaryComponentRequest { SalaryStructureComponentId = component, EffectiveFrom = new DateOnly(2026, 1, 1), OverrideValue = 1000 }] };
    private static async Task<(Guid structure, Guid component)> SeedStructureAsync(SqliteInMemoryDatabase database, Guid tenant, bool editable) { await using var db = database.CreateContext(new TestTenantContext(tenant)); var component = new SalaryComponent { Id = Guid.NewGuid(), TenantId = tenant, Code = "BASIC" + Guid.NewGuid().ToString("N")[..5], Name = "Basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true }; db.SalaryComponents.Add(component); await db.SaveChangesAsync(); await using var serviceDb = database.CreateContext(new TestTenantContext(tenant)); var service = new SalaryStructureService(serviceDb, new TestTenantContext(tenant), TimeProvider.System); var result = await service.CreateAsync(new SalaryStructureRequest { Code = "S" + Guid.NewGuid().ToString("N")[..8], Name = "Staff", EffectiveFrom = new DateOnly(2026, 1, 1), Components = [new SalaryStructureComponentRequest { SalaryComponentId = component.Id, Sequence = 1, CalculationType = SalaryStructureCalculationType.FixedAmount, Value = 1000, IsEditableAtEmployeeLevel = editable }] }); return (result.Value!.Id, result.Value.Components[0].Id); }
    private static async Task<Guid> SeedStructureIdAsync(HrmsDbContext db, Guid tenant, Guid component) => await db.SalaryStructures.Where(x => x.TenantId == tenant).OrderByDescending(x => x.CreatedDate).Select(x => x.Id).FirstAsync();
    private static async Task<Guid> SeedEmployeeAsync(SqliteInMemoryDatabase database, Guid tenant, string code) { await using var db = database.CreateContext(new TestTenantContext(tenant)); var employee = new Employee { Id = Guid.NewGuid(), TenantId = tenant, EmployeeCode = code + Guid.NewGuid().ToString("N")[..4], FirstName = "Test", LastName = "Employee", Email = Guid.NewGuid() + "@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active }; db.Employees.Add(employee); await db.SaveChangesAsync(); return employee.Id; }
    private static async Task SeedTenantAsync(SqliteInMemoryDatabase database, Guid tenant) { await using var db = database.CreateContext(new TestTenantContext()); db.Tenants.Add(new Tenant { Id = tenant, TenantCode = "T" + tenant.ToString("N")[..10], TenantName = "Test", Host = tenant + ".test", ShardKey = tenant.ToString() }); await db.SaveChangesAsync(); }
}
