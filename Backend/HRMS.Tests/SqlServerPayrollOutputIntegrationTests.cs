using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class SqlServerPayrollOutputIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerPayrollOutputIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerPayrollOutputFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_output_provider_behavior_preserves_provider_neutral_semantics()
    {
        var tenant = new TestTenantContext();
        await using var db = fixture.CreateContext(tenant);
        await PayrollOutputProviderAcceptance.RunAsync(db, tenant);
    }
}

public sealed class SqlServerPayrollOutputFactAttribute : FactAttribute
{
    public SqlServerPayrollOutputFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Payroll Output tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
