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

public sealed class MySqlSalaryStructureIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_salary_structure_master_preserves_provider_neutral_semantics()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Salary Structure tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var databaseName = $"HRMS_Phase7B_SalaryStructure_{Guid.NewGuid():N}";
        var connection = MySqlApiFactory.NormalizeConnectionString(configured); var builder = new MySqlConnectionStringBuilder(connection) { Database = databaseName }; var adminBuilder = new MySqlConnectionStringBuilder(connection) { Database = string.Empty };
        try
        {
            await using (var admin = new MySqlConnection(adminBuilder.ConnectionString)) { await admin.OpenAsync(); await using var create = admin.CreateCommand(); create.CommandText = $"CREATE DATABASE `{databaseName}`"; await create.ExecuteNonQueryAsync(); }
            var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid(); var basic = Guid.NewGuid(); var hra = Guid.NewGuid();
            await using (var setup = CreateContext(builder.ConnectionString, new TestTenantContext()))
            {
                await setup.Database.MigrateAsync(); setup.Tenants.AddRange(Tenant(tenantA, "MYSQL7BA"), Tenant(tenantB, "MYSQL7BB")); setup.SalaryComponents.AddRange(Component(tenantA, basic, "BASIC"), Component(tenantA, hra, "HRA"), Component(tenantB, Guid.NewGuid(), "BASIC")); await setup.SaveChangesAsync();
            }
            await using var dbA = CreateContext(builder.ConnectionString, new TestTenantContext(tenantA)); await using var dbB = CreateContext(builder.ConnectionString, new TestTenantContext(tenantB));
            var serviceA = new SalaryStructureService(dbA, new TestTenantContext(tenantA), TimeProvider.System); var serviceB = new SalaryStructureService(dbB, new TestTenantContext(tenantB), TimeProvider.System);
            var created = await serviceA.CreateAsync(Request("STAFF_MONTHLY", "Staff Monthly", basic, hra)); Assert.True(created.Succeeded, created.Message);
            Assert.Equal(ResultStatus.Conflict, (await serviceA.CreateAsync(Request("STAFF_MONTHLY", "Duplicate", basic))).Status);
            Assert.True((await serviceB.CreateAsync(Request("STAFF_MONTHLY", "Staff Monthly", await dbB.SalaryComponents.Select(x => x.Id).SingleAsync()))).Succeeded);
            var page = await serviceA.GetAsync(new SalaryStructureQuery { Page = 1, PageSize = 1 }); Assert.Equal(1, page.Value!.Items.Count);
            var updated = await serviceA.UpdateAsync(created.Value!.Id, Request("STAFF_MONTHLY", "Updated", basic, hra)); Assert.True(updated.Succeeded, updated.Message);
            Assert.True((await serviceA.SetActiveAsync(created.Value.Id, false, updated.Value!.ConcurrencyVersion)).Succeeded); Assert.True((await serviceA.GetHistoryAsync(created.Value.Id)).Value!.Count >= 3); Assert.Equal(ResultStatus.NotFound, (await serviceB.GetByIdAsync(created.Value.Id)).Status);
        }
        finally { await using var admin = new MySqlConnection(adminBuilder.ConnectionString); await admin.OpenAsync(); await using var drop = admin.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`"; await drop.ExecuteNonQueryAsync(); }
    }
    private static HrmsDbContext CreateContext(string connection, TestTenantContext tenant) => new(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection, mysql => mysql.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant);
    private static SalaryStructureRequest Request(string code, string name, Guid basic, Guid? hra = null) => new() { Code = code, Name = name, EffectiveFrom = new DateOnly(2026, 1, 1), Components = Rows(basic, hra) };
    private static List<SalaryStructureComponentRequest> Rows(Guid basic, Guid? hra) { var rows = new List<SalaryStructureComponentRequest> { new() { SalaryComponentId = basic, Sequence = 1, CalculationType = SalaryStructureCalculationType.FixedAmount, Value = 10000 } }; if (hra is Guid id) rows.Add(new() { SalaryComponentId = id, Sequence = 2, CalculationType = SalaryStructureCalculationType.Percentage, Value = 40, PercentageOfComponentId = basic }); return rows; }
    private static SalaryComponent Component(Guid tenant, Guid id, string code) => new() { Id = id, TenantId = tenant, Code = code, Name = code, ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true };
    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = $"{code.ToLowerInvariant()}.test", ShardKey = $"{code.ToLowerInvariant()}-shard", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.MySql };
}
