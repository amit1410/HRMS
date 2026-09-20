using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class SqlServerPayrollProductionControlsIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerPayrollProductionControlsIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerPayrollProductionControlsFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_controls_provider_acceptance_is_tenant_scoped_and_repeatable()
    {
        var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid(); var user = Guid.NewGuid();
        try
        {
            await using (var setup = fixture.CreateContext(new TestTenantContext())) { setup.Tenants.AddRange(Tenant(tenantA, "SQL7LA"), Tenant(tenantB, "SQL7LB")); await setup.SaveChangesAsync(); }
            await using var db = fixture.CreateContext(new TestTenantContext(tenantA));
            var controls = new PayrollControlService(db, new TestTenantContext(tenantA, user), TimeProvider.System);
            var policy = await controls.UpdateAsync(new PayrollControlConfigurationRequest()); Assert.True(policy.Succeeded, policy.Message);
            var guard = new PayrollApprovalGuard(db, new TestTenantContext(tenantA, user)); Assert.Equal(ResultStatus.ValidationFailed, (await guard.ValidateAsync(user, "approve")).Status); Assert.True((await guard.ValidateAsync(Guid.NewGuid(), "approve")).Succeeded);
            var periods = new PayrollPeriodService(db, new TestTenantContext(tenantA), TimeProvider.System);
            var period = await periods.CreateAsync(Period("SQL7L")); Assert.True(period.Succeeded, period.Message);
            var opened = await periods.TransitionAsync(period.Value!.Id, "open", period.Value.ConcurrencyVersion); var closed = await periods.TransitionAsync(period.Value.Id, "close", opened.Value!.ConcurrencyVersion); var locked = await periods.TransitionAsync(period.Value.Id, "lock", closed.Value!.ConcurrencyVersion, "production controls acceptance");
            Assert.True(locked.Succeeded, locked.Message); Assert.NotNull(locked.Value!.Id);
            await using var other = fixture.CreateContext(new TestTenantContext(tenantB)); Assert.Equal(ResultStatus.NotFound, (await new PayrollPeriodService(other, new TestTenantContext(tenantB), TimeProvider.System).GetByIdAsync(period.Value.Id)).Status);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext());
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollPeriodHistories] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollControlConfigurations] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollPeriods] WHERE [TenantId] IN ({tenantA}, {tenantB})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Tenants] WHERE [Id] IN ({tenantA}, {tenantB})");
        }
    }

    private static PayrollPeriodRequest Period(string code) => new() { Code = code, Name = code, PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9 };
    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = $"{code.ToLowerInvariant()}.test", ShardKey = $"{code.ToLowerInvariant()}-shard", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.SqlServer };
}

public sealed class SqlServerPayrollProductionControlsFactAttribute : FactAttribute
{
    public SqlServerPayrollProductionControlsFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Payroll Controls tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
