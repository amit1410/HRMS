using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class PayrollEndToEndUatTests
{
    [Fact]
    public async Task Composed_payroll_uat_acceptance_covers_the_complete_persisted_output_flow()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext();
        await using var db = database.CreateContext(tenant);
        await PayrollEndToEndUatAcceptance.RunAsync(db, tenant);
    }
}
