using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class SqlServerPayrollPeriodRunIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;

    public SqlServerPayrollPeriodRunIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerPayrollPeriodRunFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_period_and_run_preserve_provider_neutral_semantics()
    {
        var run = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var periodId = Guid.NewGuid();

        try
        {
            await using (var setup = fixture.CreateContext(new TestTenantContext()))
            {
                setup.Tenants.AddRange(Tenant(tenantA, $"SQL7D{run}A"), Tenant(tenantB, $"SQL7D{run}B"));
                await setup.SaveChangesAsync();
            }

            await using var dbA = fixture.CreateContext(new TestTenantContext(tenantA));
            var periodsA = new PayrollPeriodService(dbA, new TestTenantContext(tenantA), TimeProvider.System);
            var period = await periodsA.CreateAsync(Period(periodId, $"SEP-{run}"));
            Assert.True(period.Succeeded, period.Message);
            Assert.Equal(ResultStatus.Conflict, (await periodsA.CreateAsync(Period(Guid.NewGuid(), $"SEP-{run}"))).Status);

            await using var dbB = fixture.CreateContext(new TestTenantContext(tenantB));
            var periodsB = new PayrollPeriodService(dbB, new TestTenantContext(tenantB), TimeProvider.System);
            Assert.True((await periodsB.CreateAsync(Period(Guid.NewGuid(), $"SEP-{run}"))).Succeeded);
            Assert.Equal(ResultStatus.NotFound, (await periodsB.GetByIdAsync(periodId)).Status);

            var runs = new PayrollRunService(dbA, new TestTenantContext(tenantA), TimeProvider.System);
            var created = await runs.CreateAsync(new PayrollRunRequest { PayrollPeriodId = period.Value!.Id });
            Assert.True(created.Succeeded, created.Message);
            var prepared = await runs.PrepareAsync(created.Value!.Id, false);
            Assert.True(prepared.Succeeded, prepared.Message);
            Assert.Equal(PayrollRunStatus.Prepared, prepared.Value!.Status);
            Assert.Empty((await runs.GetEmployeesAsync(created.Value.Id, new PayrollRunQuery { Page = 1, PageSize = 10 })).Value!.Items);
            Assert.NotEmpty((await runs.GetHistoryAsync(created.Value.Id)).Value!);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext());
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollRunHistories] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollRunEmployees] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollRuns] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollPeriodHistories] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollPeriods] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Tenants] WHERE [Id] IN ({tenantA}, {tenantB})");
        }
    }

    private static PayrollPeriodRequest Period(Guid id, string code) => new() { Code = code, Name = code, PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = id.GetHashCode() & 0x7fff };
    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = $"{code.ToLowerInvariant()}.test", ShardKey = $"{code.ToLowerInvariant()}-shard", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.SqlServer };
}

public sealed class SqlServerPayrollPeriodRunFactAttribute : FactAttribute
{
    public SqlServerPayrollPeriodRunFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Payroll Period/Run tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
