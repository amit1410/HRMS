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
