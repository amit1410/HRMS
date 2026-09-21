using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class SqlServerPayrollSeparationBenefitsIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerPayrollSeparationBenefitsIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerPayrollSeparationBenefitsFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_separation_benefits_provider_acceptance_is_repeatable_and_tenant_scoped()
    {
        var tenant = new TestTenantContext(); await using var db = fixture.CreateContext(tenant); await PayrollSeparationBenefitsProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer); await PayrollSeparationBenefitsProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer);
    }
}

public sealed class SqlServerPayrollSeparationBenefitsFactAttribute : FactAttribute
{
    public SqlServerPayrollSeparationBenefitsFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Payroll Separation Benefits tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
