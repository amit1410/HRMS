using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class SqlServerPayrollAdjustmentsIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerPayrollAdjustmentsIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;
    [SqlServerPayrollAdjustmentsFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_adjustments_provider_acceptance_is_repeatable_and_tenant_scoped()
    {
        var tenant = new TestTenantContext(); await using var db = fixture.CreateContext(tenant); await PayrollAdjustmentsProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer); await PayrollAdjustmentsProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer);
    }
}

public sealed class SqlServerPayrollAdjustmentsFactAttribute : FactAttribute
{
    public SqlServerPayrollAdjustmentsFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Payroll Adjustments tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
