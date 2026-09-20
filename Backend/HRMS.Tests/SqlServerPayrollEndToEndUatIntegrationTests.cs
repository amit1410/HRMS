using HRMS.Tests.TestSupport;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class SqlServerPayrollEndToEndUatIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerPayrollEndToEndUatIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerPayrollEndToEndUatFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_end_to_end_uat_acceptance_preserves_provider_neutral_semantics()
    {
        var tenant = new TestTenantContext();
        await using var db = fixture.CreateContext(tenant);
        await PayrollEndToEndUatAcceptance.RunAsync(db, tenant);
    }
}

public sealed class SqlServerPayrollEndToEndUatFactAttribute : FactAttribute
{
    public SqlServerPayrollEndToEndUatFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Payroll UAT tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
