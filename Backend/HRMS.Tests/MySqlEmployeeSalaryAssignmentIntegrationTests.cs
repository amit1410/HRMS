using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlEmployeeSalaryAssignmentIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_employee_salary_assignment_preserves_tenant_and_effective_version_semantics()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION"); if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Employee Salary tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var name = $"HRMS_Phase7C_EmployeeSalary_{Guid.NewGuid():N}"; var normalized = MySqlApiFactory.NormalizeConnectionString(configured); var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty }; var connection = new MySqlConnectionStringBuilder(normalized) { Database = name };
        try
        {
            await using (var db = new MySqlConnection(admin.ConnectionString)) { await db.OpenAsync(); await using var command = db.CreateCommand(); command.CommandText = $"CREATE DATABASE `{name}`"; await command.ExecuteNonQueryAsync(); }
            var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid(); var employee = Guid.NewGuid(); var component = Guid.NewGuid(); var structure = Guid.NewGuid();
            await using (var setup = Context(connection.ConnectionString, new TestTenantContext())) { await setup.Database.MigrateAsync(); setup.Tenants.AddRange(Tenant(tenantA, "MYSQL7CA"), Tenant(tenantB, "MYSQL7CB")); setup.Employees.Add(Employee(tenantA, employee)); setup.SalaryComponents.Add(Component(tenantA, component)); setup.SalaryStructures.Add(new SalaryStructure { Id = structure, TenantId = tenantA, Code = "STAFF", Name = "Staff", IsActive = true }); var version = new SalaryStructureVersion { Id = Guid.NewGuid(), TenantId = tenantA, SalaryStructureId = structure, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true }; version.Components.Add(new SalaryStructureComponent { Id = Guid.NewGuid(), TenantId = tenantA, SalaryStructureVersionId = version.Id, SalaryComponentId = component, Sequence = 1, CalculationType = SalaryStructureCalculationType.FixedAmount, Value = 1000, IsEditableAtEmployeeLevel = true, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true }); setup.SalaryStructureVersions.Add(version); await setup.SaveChangesAsync(); }
            await using var dbA = Context(connection.ConnectionString, new TestTenantContext(tenantA)); var service = new EmployeeSalaryAssignmentService(dbA, new TestTenantContext(tenantA), TimeProvider.System); var versionComponent = await dbA.SalaryStructureComponents.Select(x => x.Id).SingleAsync(); var created = await service.CreateAsync(new EmployeeSalaryAssignmentRequest { EmployeeId = employee, SalaryStructureId = structure, EffectiveFrom = new DateOnly(2026, 1, 1), AnnualCtc = 12000, MonthlyCtc = 1000, Components = [] }); Assert.True(created.Succeeded, created.Message); Assert.True((await service.AddComponentAsync(created.Value!.Id, new EmployeeSalaryComponentRequest { SalaryStructureComponentId = versionComponent, EffectiveFrom = new DateOnly(2026, 1, 1), OverrideValue = 1200 })).Succeeded); Assert.Equal(created.Value.Id, (await service.GetEffectiveAsync(employee, new DateOnly(2026, 5, 1))).Value!.Id); Assert.Equal(ResultStatus.NotFound, (await service.GetByIdAsync(Guid.NewGuid())).Status);
        }
        finally { await using var db = new MySqlConnection(admin.ConnectionString); await db.OpenAsync(); await using var command = db.CreateCommand(); command.CommandText = $"DROP DATABASE IF EXISTS `{name}`"; await command.ExecuteNonQueryAsync(); }
    }
    private static HrmsDbContext Context(string connection, TestTenantContext tenant) => new(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant);
    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = code.ToLowerInvariant() + ".test", ShardKey = code.ToLowerInvariant(), Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.MySql };
    private static Employee Employee(Guid tenant, Guid id) => new() { Id = id, TenantId = tenant, EmployeeCode = "E001", FirstName = "My", LastName = "Sql", Email = id + "@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active };
    private static SalaryComponent Component(Guid tenant, Guid id) => new() { Id = id, TenantId = tenant, Code = "BASIC", Name = "Basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true };
}
