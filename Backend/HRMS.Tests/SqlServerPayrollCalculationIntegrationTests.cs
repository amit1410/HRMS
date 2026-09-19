using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class SqlServerPayrollCalculationIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerPayrollCalculationIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerPayrollCalculationFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_calculation_persists_provider_neutral_run_state()
    {
        var tenant = Guid.NewGuid(); var run = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        try
        {
            await using (var setup = fixture.CreateContext(new TestTenantContext())) { setup.Tenants.Add(Tenant(tenant, $"SQL7E{run}")); await setup.SaveChangesAsync(); }
            await using var db = fixture.CreateContext(new TestTenantContext(tenant)); var periods = new PayrollPeriodService(db, new TestTenantContext(tenant), TimeProvider.System); var period = await periods.CreateAsync(new PayrollPeriodRequest { Code = $"SEP{run}", Name = "September", PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9 }); Assert.True(period.Succeeded, period.Message); var runs = new PayrollRunService(db, new TestTenantContext(tenant), TimeProvider.System); var created = await runs.CreateAsync(new PayrollRunRequest { PayrollPeriodId = period.Value!.Id }); Assert.True(created.Succeeded, created.Message); var prepared = await runs.PrepareAsync(created.Value!.Id, false); Assert.True(prepared.Succeeded, prepared.Message); var calculated = await new PayrollCalculationEngine(db, new TestTenantContext(tenant), TimeProvider.System).CalculateAsync(created.Value.Id, false); Assert.True(calculated.Succeeded, calculated.Message); Assert.Equal(PayrollRunStatus.Calculated, calculated.Value!.Status); Assert.Empty(await db.PayrollResults.ToListAsync());
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext()); await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollCalculationHistories] WHERE [TenantId] = {tenant}"); await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollCalculationErrors] WHERE [TenantId] = {tenant}"); await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollResultComponents] WHERE [TenantId] = {tenant}"); await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollResults] WHERE [TenantId] = {tenant}"); await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollRunHistories] WHERE [TenantId] = {tenant}"); await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollRunEmployees] WHERE [TenantId] = {tenant}"); await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollRuns] WHERE [TenantId] = {tenant}"); await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollPeriodHistories] WHERE [TenantId] = {tenant}"); await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollPeriods] WHERE [TenantId] = {tenant}"); await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Tenants] WHERE [Id] = {tenant}");
        }
    }
    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = $"{code.ToLowerInvariant()}.test", ShardKey = $"{code.ToLowerInvariant()}-shard", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.SqlServer };
}

public sealed class SqlServerPayrollCalculationFactAttribute : FactAttribute
{
    public SqlServerPayrollCalculationFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Payroll Calculation tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
