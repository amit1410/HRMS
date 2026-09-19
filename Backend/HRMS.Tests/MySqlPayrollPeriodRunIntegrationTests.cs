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

public sealed class MySqlPayrollPeriodRunIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_payroll_period_and_run_preserve_provider_neutral_semantics()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Payroll Period/Run tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var databaseName = $"HRMS_Phase7D_Payroll_{Guid.NewGuid():N}";
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var connection = new MySqlConnectionStringBuilder(normalized) { Database = databaseName };
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(); await using var command = server.CreateCommand(); command.CommandText = $"CREATE DATABASE `{databaseName}`"; await command.ExecuteNonQueryAsync(); }
            await using (var setup = Context(connection.ConnectionString, new TestTenantContext())) { await setup.Database.MigrateAsync(); setup.Tenants.AddRange(Tenant(tenantA, $"MYSQL7D{databaseName[^8..]}A"), Tenant(tenantB, $"MYSQL7D{databaseName[^8..]}B")); await setup.SaveChangesAsync(); }
            await using var dbA = Context(connection.ConnectionString, new TestTenantContext(tenantA));
            var periodsA = new PayrollPeriodService(dbA, new TestTenantContext(tenantA), TimeProvider.System);
            var period = await periodsA.CreateAsync(Period("SEP"));
            Assert.True(period.Succeeded, period.Message);
            Assert.Equal(ResultStatus.Conflict, (await periodsA.CreateAsync(Period("SEP"))).Status);
            await using var dbB = Context(connection.ConnectionString, new TestTenantContext(tenantB));
            Assert.True((await new PayrollPeriodService(dbB, new TestTenantContext(tenantB), TimeProvider.System).CreateAsync(Period("SEP"))).Succeeded);
            var runs = new PayrollRunService(dbA, new TestTenantContext(tenantA), TimeProvider.System);
            var created = await runs.CreateAsync(new PayrollRunRequest { PayrollPeriodId = period.Value!.Id });
            Assert.True(created.Succeeded, created.Message);
            var prepared = await runs.PrepareAsync(created.Value!.Id, false);
            Assert.True(prepared.Succeeded, prepared.Message);
            Assert.Equal(PayrollRunStatus.Prepared, prepared.Value!.Status);
            Assert.Empty((await runs.GetEmployeesAsync(created.Value.Id, new PayrollRunQuery { Page = 1, PageSize = 10 })).Value!.Items);
        }
        finally
        {
            await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`"; await drop.ExecuteNonQueryAsync();
        }
    }

    private static HrmsDbContext Context(string connection, TestTenantContext tenant) => new(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection, options => options.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant);
    private static PayrollPeriodRequest Period(string code) => new() { Code = code, Name = code, PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9 };
    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = $"{code.ToLowerInvariant()}.test", ShardKey = $"{code.ToLowerInvariant()}-shard", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.MySql };
}
