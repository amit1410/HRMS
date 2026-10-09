using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class SqlServerOvertimeIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;

    public SqlServerOvertimeIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerIntegrationFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_overtime_provider_acceptance_preserves_versioned_snapshot_semantics()
    {
        var tenantContext = new TestTenantContext();
        await using var db = fixture.CreateContext(tenantContext);
        await db.Database.MigrateAsync();
        await OvertimeProviderAcceptance.RunAsync(db, Guid.NewGuid(), tenantContext);
    }
}
