using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class SqlServerPayrollStatutoryComplianceIntegrationTests(SqlServerIntegrationTestHarness fixture) : IClassFixture<SqlServerIntegrationTestHarness>
{
    [SqlServerPayrollStatutoryComplianceFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_statutory_compliance_provider_behavior_preserves_provider_neutral_semantics()
    { var tenant = new TestTenantContext(); await using var db = fixture.CreateContext(tenant); await PayrollStatutoryComplianceProviderAcceptance.RunAsync(db, tenant); }
}

public sealed class SqlServerPayrollStatutoryComplianceFactAttribute : FactAttribute
{ public SqlServerPayrollStatutoryComplianceFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Payroll Statutory Compliance tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null; }
