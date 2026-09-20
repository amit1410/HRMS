using HRMS.Tests.TestSupport;
using Xunit;

namespace HRMS.Tests;

public sealed class SqlServerBankAdviceIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerBankAdviceIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerBankAdviceFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_bank_advice_provider_behavior_preserves_provider_neutral_semantics()
    {
        var tenant = new TestTenantContext(); await using var db = fixture.CreateContext(tenant); await BankAdviceProviderAcceptance.RunAsync(db, tenant);
    }
}

public sealed class SqlServerBankAdviceFactAttribute : FactAttribute
{
    public SqlServerBankAdviceFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Bank Advice tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
