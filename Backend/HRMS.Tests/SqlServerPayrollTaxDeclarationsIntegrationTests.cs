using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class SqlServerPayrollTaxDeclarationsIntegrationTests(SqlServerIntegrationTestHarness fixture) : IClassFixture<SqlServerIntegrationTestHarness>
{
    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_tax_declarations_provider_acceptance_is_repeatable()
    {
        var tenant = new TestTenantContext();
        await using var db = fixture.CreateContext(tenant);
        await PayrollTaxDeclarationsProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer);
        await PayrollTaxDeclarationsProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer);
    }
}
