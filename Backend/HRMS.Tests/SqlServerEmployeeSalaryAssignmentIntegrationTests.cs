using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SqlServerEmployeeSalaryAssignmentIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerEmployeeSalaryAssignmentIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerEmployeeSalaryFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_employee_salary_assignment_preserves_provider_neutral_semantics()
    {
        var run = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var component = Guid.NewGuid(); var structure = Guid.NewGuid(); var versionId = Guid.NewGuid(); var structureComponent = Guid.NewGuid();
        try
        {
            await using (var setup = fixture.CreateContext(new TestTenantContext())) { setup.Tenants.Add(Tenant(tenant, run)); setup.Employees.Add(Employee(tenant, employee, run)); setup.SalaryComponents.Add(Component(tenant, component, run)); setup.SalaryStructures.Add(new SalaryStructure { Id = structure, TenantId = tenant, Code = $"STAFF{run}", Name = "Staff", IsActive = true }); setup.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = versionId, TenantId = tenant, SalaryStructureId = structure, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true, Components = { new SalaryStructureComponent { Id = structureComponent, TenantId = tenant, SalaryStructureVersionId = versionId, SalaryComponentId = component, Sequence = 1, CalculationType = SalaryStructureCalculationType.FixedAmount, Value = 1000, IsEditableAtEmployeeLevel = true, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true } } }); await setup.SaveChangesAsync(); }
            await using var db = fixture.CreateContext(new TestTenantContext(tenant)); var service = new EmployeeSalaryAssignmentService(db, new TestTenantContext(tenant), TimeProvider.System); var created = await service.CreateAsync(new EmployeeSalaryAssignmentRequest { EmployeeId = employee, SalaryStructureId = structure, EffectiveFrom = new DateOnly(2026, 1, 1), AnnualCtc = 12000, MonthlyCtc = 1000, Components = [] }); Assert.True(created.Succeeded, created.Message); Assert.True((await service.AddComponentAsync(created.Value!.Id, new EmployeeSalaryComponentRequest { SalaryStructureComponentId = structureComponent, EffectiveFrom = new DateOnly(2026, 1, 1), OverrideValue = 1200 })).Succeeded); Assert.Equal(created.Value.Id, (await service.GetEffectiveAsync(employee, new DateOnly(2026, 5, 1))).Value!.Id); Assert.NotEmpty((await service.GetHistoryAsync(created.Value.Id)).Value!);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext());
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [EmployeeSalaryAssignmentHistories] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [EmployeeSalaryComponents] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [EmployeeSalaryAssignments] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [SalaryStructureComponents] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [SalaryStructureVersions] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [SalaryStructures] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [SalaryComponents] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Employees] WHERE [TenantId] = {tenant}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Tenants] WHERE [Id] = {tenant}");
        }
    }
    private static Tenant Tenant(Guid id, string run) => new() { Id = id, TenantCode = $"SQL7C{run}", TenantName = "SQL7C", Host = $"sql7c-{run}.test", ShardKey = $"sql7c-{run}", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.SqlServer };
    private static Employee Employee(Guid tenant, Guid id, string run) => new() { Id = id, TenantId = tenant, EmployeeCode = $"E{run}", FirstName = "Sql", LastName = "Server", Email = id + "@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active };
    private static SalaryComponent Component(Guid tenant, Guid id, string run) => new() { Id = id, TenantId = tenant, Code = $"BASIC{run}", Name = "Basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true };
}

public sealed class SqlServerEmployeeSalaryFactAttribute : FactAttribute
{
    public SqlServerEmployeeSalaryFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Employee Salary tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
