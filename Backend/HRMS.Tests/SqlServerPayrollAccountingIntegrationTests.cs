using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class SqlServerPayrollAccountingIntegrationTests(SqlServerIntegrationTestHarness fixture) : IClassFixture<SqlServerIntegrationTestHarness>
{
    [SqlServerPayrollAccountingFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_accounting_provider_behavior_preserves_provider_neutral_semantics()
    { var tenant = new TestTenantContext(); await using var db = fixture.CreateContext(tenant); await PayrollAccountingProviderAcceptance.RunAsync(db, tenant); }
}

public sealed class SqlServerPayrollAccountingFactAttribute : FactAttribute
{ public SqlServerPayrollAccountingFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Payroll Accounting tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null; }
