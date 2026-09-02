using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class SqlServerIntegrationSafetyTests
{
    [Theory]
    [InlineData("HRMS_IntegrationTests")]
    [InlineData("HRMS_Test_20260901")]
    public void Accepts_dedicated_database_names(string database) => SqlServerIntegrationTestHarness.ValidateIntegrationDatabase(database);

    [Theory]
    [InlineData("HRMS")]
    [InlineData("master")]
    [InlineData("model")]
    [InlineData("msdb")]
    [InlineData("tempdb")]
    [InlineData("HRMSDev")]
    public void Rejects_protected_or_non_test_database_names(string database) => Assert.Throws<InvalidOperationException>(() => SqlServerIntegrationTestHarness.ValidateIntegrationDatabase(database));
}
