namespace HRMS.Tests.TestSupport;

public sealed class SqlServerIntegrationFactAttribute : FactAttribute
{
    public SqlServerIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)))
            Skip = $"SQL Server integration test not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent.";
    }
}
