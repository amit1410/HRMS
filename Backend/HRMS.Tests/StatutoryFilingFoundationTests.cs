using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class StatutoryFilingFoundationTests
{
    [Fact]
    public async Task Manual_download_filing_lifecycle_is_provider_neutral()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext();
        await StatutoryFilingProviderAcceptance.RunAsync(database.CreateContext(tenant), tenant);
    }
}
