using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class SqlServerAcceptanceRunSafetyTests
{
    [Theory]
    [InlineData("master")]
    [InlineData("HRMS")]
    [InlineData("HRMS_Catalog")]
    [InlineData("tempdb")]
    public void Protected_databases_are_rejected(string database) =>
        Assert.Throws<InvalidOperationException>(() => SqlServerAcceptanceRun.ValidateDatabaseName(database, "20260903T120000Z_123456"));

    [Theory]
    [InlineData("HRMS_IntegrationTests")]
    [InlineData("HRMS_Phase3B_Integration_20260903T120000Z_123456_Other")]
    public void Non_run_owned_databases_are_rejected(string database) =>
        Assert.Throws<InvalidOperationException>(() => SqlServerAcceptanceRun.ValidateDatabaseName(database, "20260903T120000Z_123456"));

    [Fact]
    public void Generated_names_are_exact_and_manifest_owned()
    {
        var run = SqlServerAcceptanceRun.Create("localhost\\SQLEXPRESS", "20260903T120000Z_123456", "phase3b-manifest.json");
        Assert.Equal("HRMS_Phase3B_Integration_20260903T120000Z_123456_catalog", run.CatalogDatabaseName);
        Assert.All(run.AllDatabaseNames, name => Assert.StartsWith("HRMS_Phase3B_Integration_20260903T120000Z_123456_", name, StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => run.Connection("HRMS_Phase3B_Integration_20260903T120000Z_123456_Unexpected"));
    }

    [Fact]
    public void Full_connection_mode_preserves_sql_auth_and_replaces_only_database()
    {
        const string password = "synthetic-password-that-must-not-be-logged";
        var run = SqlServerAcceptanceRun.CreateFromConnectionString(
            $"Server=127.0.0.1,1433;Database=HRMS_Phase6B_Test;User ID=hrms_phase6b_test;Password={password};Encrypt=True;TrustServerCertificate=True",
            "20260903T120000Z_123456",
            "phase3b-manifest.json");

        var tenant = run.Connection(run.TenantDatabaseNames[0]);
        var master = run.MasterConnection();

        Assert.False(tenant.IntegratedSecurity);
        Assert.Equal("hrms_phase6b_test", tenant.UserID);
        Assert.Equal(password, tenant.Password);
        Assert.Equal(run.TenantDatabaseNames[0], tenant.InitialCatalog);
        Assert.Equal("master", master.InitialCatalog);
        Assert.Equal(password, master.Password);
    }

    [Theory]
    [InlineData(615)]
    [InlineData(3701)]
    public void Cleanup_retries_only_known_absent_or_disappearing_database_errors(int number) =>
        Assert.True(SqlServerAcceptanceRun.IsCleanupStateError(number));

    [Theory]
    [InlineData(262)]
    [InlineData(3726)]
    public void Cleanup_does_not_retry_unclassified_sql_errors(int number) =>
        Assert.False(SqlServerAcceptanceRun.IsCleanupStateError(number));

    [Fact]
    public void Cleanup_retry_budget_is_bounded() => Assert.Equal(3, SqlServerAcceptanceRun.CleanupMaxAttempts);

    [Theory]
    [InlineData("master")]
    [InlineData("HRMS")]
    [InlineData("HRMS_OtherTest_20260903T120000Z_123456")]
    public void Leave_cleanup_rejects_protected_or_unowned_databases(string database) =>
        Assert.Throws<InvalidOperationException>(() => SqlServerLeaveRequestConcurrencyFixture.ValidateOwnedName(database));

    [Fact]
    public void Leave_cleanup_accepts_only_generated_database_prefix()
    {
        var database = "HRMS_LeaveRequestConcurrency_20260903T120000Z_123456";
        SqlServerLeaveRequestConcurrencyFixture.ValidateOwnedName(database);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Server=localhost;Database=HRMS")]
    [InlineData("localhost;Database=HRMS")]
    public void Server_configuration_is_not_a_connection_string(string server) =>
        Assert.Throws<InvalidOperationException>(() => SqlServerAcceptanceRun.ValidateServer(server));

    [Fact]
    public void Cleanup_requires_the_run_ownership_manifest()
    {
        var run = SqlServerAcceptanceRun.Create("localhost\\SQLEXPRESS", "20260903T120000Z_123456", "missing-manifest.json");
        Assert.Throws<InvalidOperationException>(() => run.ValidateManifestOwnership());
    }

    [Fact]
    public void Browser_configuration_validates_all_run_destinations_without_opening_connections()
    {
        var run = SqlServerAcceptanceRun.Create("localhost\\SQLEXPRESS", "20260903T120000Z_123456", "phase3b-manifest.json");
        var configuration = BrowserAcceptanceConfiguration.Create(run, 15080, 15173);
        Assert.Equal(15080, configuration.ApiPort);
        Assert.Equal(15173, configuration.FrontendPort);
        Assert.Equal(2, configuration.WorkspaceOrigins.Count);
    }
}
