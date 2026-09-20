using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class SqlServerPayrollRetroSettlementIntegrationTests(SqlServerIntegrationTestHarness fixture) : IClassFixture<SqlServerIntegrationTestHarness>
{
    [SqlServerPayrollRetroSettlementFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_retro_settlement_provider_behavior_preserves_provider_neutral_semantics()
    { var tenant = new TestTenantContext(); await using var db = fixture.CreateContext(tenant); await PayrollRetroSettlementProviderAcceptance.RunAsync(db, tenant); }
}

public sealed class SqlServerPayrollRetroSettlementFactAttribute : FactAttribute
{ public SqlServerPayrollRetroSettlementFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Payroll Retro/Settlement tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null; }
