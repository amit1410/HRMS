using HRMS.Tests.TestSupport;
using HRMS.Domain.Enums;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class SqlServerPayrollVariablePayIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerPayrollVariablePayIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;
    [SqlServerPayrollVariablePayFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_variable_pay_provider_acceptance_is_repeatable_and_tenant_scoped()
    {
        var tenant = new TestTenantContext(); await using var db = fixture.CreateContext(tenant); await PayrollVariablePayProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer); await PayrollVariablePayProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer);
    }
}

public sealed class SqlServerPayrollVariablePayFactAttribute : FactAttribute
{
    public SqlServerPayrollVariablePayFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Payroll Variable Pay tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
