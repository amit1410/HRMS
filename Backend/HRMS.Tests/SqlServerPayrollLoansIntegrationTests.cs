using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class SqlServerPayrollLoansIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerPayrollLoansIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerPayrollLoansFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_loans_provider_acceptance_is_tenant_scoped()
    {
        var tenant = new TestTenantContext();
        await using var db = fixture.CreateContext(tenant);
        await PayrollLoansProviderAcceptance.RunAsync(db, tenant);
    }
}

public sealed class SqlServerPayrollLoansFactAttribute : Xunit.FactAttribute
{
    public SqlServerPayrollLoansFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Payroll Loans tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
