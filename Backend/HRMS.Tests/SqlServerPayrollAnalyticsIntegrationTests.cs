using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using HRMS.Domain.Enums;

namespace HRMS.Tests;

public sealed class SqlServerPayrollAnalyticsIntegrationTests(SqlServerIntegrationTestHarness fixture) : IClassFixture<SqlServerIntegrationTestHarness>
{
    [Fact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_analytics_provider_acceptance_is_repeatable()
    { var tenant = new TestTenantContext(); await using var db = fixture.CreateContext(tenant); await PayrollAnalyticsProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer); await PayrollAnalyticsProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer); }
}
