using HRMS.Infrastructure.Persistence;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Xunit;

namespace HRMS.Tests;

internal static class SeparationApprovalProviderAcceptance
{
    public static Task RunAsync(HrmsDbContext db, TestTenantContext tenant)
        => SeparationFoundationProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer);
}

public sealed class SqlServerSeparationApprovalIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;

    public SqlServerSeparationApprovalIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerSeparationApprovalFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_separation_approval_provider_acceptance()
    {
        var tenant = new TestTenantContext(Guid.NewGuid());
        await using var db = fixture.CreateContext(tenant);
        await SeparationApprovalProviderAcceptance.RunAsync(db, tenant);
    }
}

public sealed class SqlServerSeparationApprovalFactAttribute : FactAttribute
{
    public SqlServerSeparationApprovalFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable))
        ? $"SQL Server Phase 8B tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent."
        : null;
}
