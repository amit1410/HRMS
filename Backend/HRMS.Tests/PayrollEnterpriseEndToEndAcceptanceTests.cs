using HRMS.Application.Services;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// Composes the already-provider-neutral payroll acceptance flows into one closure gate.
/// Each composed flow uses the canonical application service for its own authoritative
/// artifact; this test does not create a second payroll engine or bypass those services.
/// </summary>
public sealed class PayrollEnterpriseEndToEndAcceptanceTests
{
    private static async Task RunAcceptanceAsync(
        Func<HRMS.Infrastructure.Persistence.HrmsDbContext, TestTenantContext, Task> acceptance)
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext();
        await using var db = database.CreateContext(tenant);
        await acceptance(db, tenant);
    }

    [Fact]
    public async Task Enterprise_closure_acceptance_composes_authoritative_payroll_artifacts()
    {
        // Each canonical acceptance flow owns its transaction and cleanup. Isolating
        // the flows keeps this closure composition deterministic while still routing
        // every assertion through the real provider-neutral services.
        await RunAcceptanceAsync(PayrollOutputProviderAcceptance.RunAsync);
        await RunAcceptanceAsync(PayrollAccountingProviderAcceptance.RunAsync);
        await RunAcceptanceAsync(PayrollStatutoryComplianceProviderAcceptance.RunAsync);
        await RunAcceptanceAsync(PayrollYearEndProviderAcceptance.RunAsync);
        await RunAcceptanceAsync(StatutoryFilingProviderAcceptance.RunAsync);

        using var healthDatabase = new SqliteInMemoryDatabase();
        var healthTenant = new TestTenantContext(Guid.NewGuid());
        await using var healthDb = healthDatabase.CreateContext(healthTenant);
        var operations = new PayrollOperationsService(healthDb, healthTenant);
        var health = await operations.GetProductionHealthAsync();
        var integrity = await operations.GetIntegrityAsync();

        Assert.True(health.Succeeded, health.Message);
        Assert.True(integrity.Succeeded, integrity.Message);
        Assert.DoesNotContain(integrity.Value!.Checks, x => x.Status == "Critical");
    }
}
