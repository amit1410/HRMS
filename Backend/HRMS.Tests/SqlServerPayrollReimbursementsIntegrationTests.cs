using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class SqlServerPayrollReimbursementsIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerPayrollReimbursementsIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerPayrollReimbursementsFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_reimbursements_provider_acceptance_is_tenant_scoped()
    {
        var tenant = new TestTenantContext(); await using var db = fixture.CreateContext(tenant); await PayrollReimbursementsProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer); await PayrollReimbursementsProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer);
    }
}

public sealed class SqlServerPayrollReimbursementsFactAttribute : Xunit.FactAttribute
{
    public SqlServerPayrollReimbursementsFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Payroll Reimbursements tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
