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

public sealed class MySqlPayrollProductionControlsIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_payroll_controls_provider_acceptance_is_tenant_scoped_and_repeatable()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION"); if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Payroll Controls tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var databaseName = $"HRMS_Phase7L_Controls_{Guid.NewGuid():N}"; var normalized = MySqlApiFactory.NormalizeConnectionString(configured); var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty }; var connection = new MySqlConnectionStringBuilder(normalized) { Database = databaseName }; var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid(); var user = Guid.NewGuid();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(timeout.Token); await using var command = server.CreateCommand(); command.CommandTimeout = 90; command.CommandText = $"CREATE DATABASE `{databaseName}`"; await command.ExecuteNonQueryAsync(timeout.Token); }
            await using (var setup = Context(connection.ConnectionString, new TestTenantContext())) { await setup.Database.MigrateAsync(timeout.Token); setup.Tenants.AddRange(Tenant(tenantA, "MYSQL7LA"), Tenant(tenantB, "MYSQL7LB")); await setup.SaveChangesAsync(timeout.Token); }
            await using var db = Context(connection.ConnectionString, new TestTenantContext(tenantA));
            var controls = new PayrollControlService(db, new TestTenantContext(tenantA, user), TimeProvider.System); Assert.True((await controls.UpdateAsync(new PayrollControlConfigurationRequest())).Succeeded);
            var guard = new PayrollApprovalGuard(db, new TestTenantContext(tenantA, user)); Assert.Equal(ResultStatus.ValidationFailed, (await guard.ValidateAsync(user, "approve")).Status); Assert.True((await guard.ValidateAsync(Guid.NewGuid(), "approve")).Succeeded);
            var periods = new PayrollPeriodService(db, new TestTenantContext(tenantA), TimeProvider.System); var period = await periods.CreateAsync(Period("MYSQL7L")); Assert.True(period.Succeeded, period.Message); var opened = await periods.TransitionAsync(period.Value!.Id, "open", period.Value.ConcurrencyVersion); var closed = await periods.TransitionAsync(period.Value.Id, "close", opened.Value!.ConcurrencyVersion); var locked = await periods.TransitionAsync(period.Value.Id, "lock", closed.Value!.ConcurrencyVersion, "production controls acceptance"); Assert.True(locked.Succeeded, locked.Message);
            await using var other = Context(connection.ConnectionString, new TestTenantContext(tenantB)); Assert.Equal(ResultStatus.NotFound, (await new PayrollPeriodService(other, new TestTenantContext(tenantB), TimeProvider.System).GetByIdAsync(period.Value.Id)).Status);
        }
        finally
        {
            await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandTimeout = 90; drop.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`"; await drop.ExecuteNonQueryAsync();
        }
    }

    private static HrmsDbContext Context(string connection, TestTenantContext tenant) => new(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection, options => options.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant);
    private static PayrollPeriodRequest Period(string code) => new() { Code = code, Name = code, PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9 };
    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = $"{code.ToLowerInvariant()}.test", ShardKey = $"{code.ToLowerInvariant()}-shard", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.MySql };
}
