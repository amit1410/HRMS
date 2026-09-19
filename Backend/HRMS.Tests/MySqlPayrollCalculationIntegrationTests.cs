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

public sealed class MySqlPayrollCalculationIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_payroll_calculation_persists_provider_neutral_run_state()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION"); if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Payroll Calculation tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent."); var databaseName = $"HRMS_Phase7E_Calculation_{Guid.NewGuid():N}"; var normalized = MySqlApiFactory.NormalizeConnectionString(configured); var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty }; var connection = new MySqlConnectionStringBuilder(normalized) { Database = databaseName }; var tenant = Guid.NewGuid();
        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(); await using var command = server.CreateCommand(); command.CommandText = $"CREATE DATABASE `{databaseName}`"; await command.ExecuteNonQueryAsync(); }
            await using (var setup = Context(connection.ConnectionString, new TestTenantContext())) { await setup.Database.MigrateAsync(); setup.Tenants.Add(Tenant(tenant, $"MYSQL7E{databaseName[^8..]}")); await setup.SaveChangesAsync(); }
            await using var db = Context(connection.ConnectionString, new TestTenantContext(tenant)); var periods = new PayrollPeriodService(db, new TestTenantContext(tenant), TimeProvider.System); var period = await periods.CreateAsync(new PayrollPeriodRequest { Code = "SEP", Name = "September", PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9 }); Assert.True(period.Succeeded, period.Message); var runs = new PayrollRunService(db, new TestTenantContext(tenant), TimeProvider.System); var created = await runs.CreateAsync(new PayrollRunRequest { PayrollPeriodId = period.Value!.Id }); Assert.True(created.Succeeded, created.Message); Assert.True((await runs.PrepareAsync(created.Value!.Id, false)).Succeeded); var calculated = await new PayrollCalculationEngine(db, new TestTenantContext(tenant), TimeProvider.System).CalculateAsync(created.Value.Id, false); Assert.True(calculated.Succeeded, calculated.Message); Assert.Equal(PayrollRunStatus.Calculated, calculated.Value!.Status);
        }
        finally { await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`"; await drop.ExecuteNonQueryAsync(); }
    }
    private static HrmsDbContext Context(string connection, TestTenantContext tenant) => new(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection, options => options.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant);
    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = $"{code.ToLowerInvariant()}.test", ShardKey = $"{code.ToLowerInvariant()}-shard", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.MySql };
}
