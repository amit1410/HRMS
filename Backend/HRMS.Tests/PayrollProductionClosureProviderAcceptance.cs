using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

public static class PayrollProductionClosureProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant)
    {
        var tenantId = tenant.TenantId ?? Guid.NewGuid();
        if (!await db.Tenants.AnyAsync(x => x.Id == tenantId))
        {
            db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"C{tenantId:N}"[..12], TenantName = "Closure acceptance", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
            await db.SaveChangesAsync();
        }

        var scoped = tenant.TenantId == tenantId ? db : new HrmsDbContext(db.Database.GetDbConnection() is SqlConnection
            ? new DbContextOptionsBuilder<HrmsDbContext>().UseSqlServer(db.Database.GetConnectionString()!).Options
            : new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(db.Database.GetConnectionString()!, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options,
            new TestTenantContext(tenantId));
        await using (scoped)
        {
            var service = new PayrollOperationsService(scoped, new TestTenantContext(tenantId));
            var health = await service.GetProductionHealthAsync();
            var integrity = await service.GetIntegrityAsync();
            Assert.True(health.Succeeded, health.Message);
            Assert.True(integrity.Succeeded, integrity.Message);
            Assert.Equal(tenantId, (await scoped.Tenants.SingleAsync(x => x.Id == tenantId)).Id);
            Assert.All(integrity.Value!.Checks, check => Assert.Equal(0, check.Count));
        }
    }
}

public sealed class MySqlPayrollProductionClosureIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_payroll_production_closure_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Payroll Production Closure tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var name = $"HRMS_Phase7Y_Closure_{Guid.NewGuid():N}";
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var connection = new MySqlConnectionStringBuilder(normalized) { Database = name };
        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(); await using var create = server.CreateCommand(); create.CommandText = $"CREATE DATABASE `{name}`"; await create.ExecuteNonQueryAsync(); }
            var tenant = new TestTenantContext(Guid.NewGuid());
            await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant);
            await db.Database.MigrateAsync();
            await PayrollProductionClosureProviderAcceptance.RunAsync(db, tenant);
        }
        finally
        {
            await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS `{name}`"; await drop.ExecuteNonQueryAsync();
        }
    }
}

public sealed class SqlServerPayrollProductionClosureIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerPayrollProductionClosureIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerPayrollProductionClosureFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_production_closure_provider_acceptance()
    {
        var tenant = new TestTenantContext(Guid.NewGuid());
        await using var db = fixture.CreateContext(tenant);
        await PayrollProductionClosureProviderAcceptance.RunAsync(db, tenant);
    }
}

public sealed class SqlServerPayrollProductionClosureFactAttribute : FactAttribute
{
    public SqlServerPayrollProductionClosureFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable))
        ? $"SQL Server Payroll Production Closure tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent."
        : null;
}
