using Microsoft.Extensions.Configuration;

namespace HRMS.Tests;

public sealed class MigrationHostConfigurationTests
{
    [Fact]
    public void Environment_variables_override_repository_configuration()
    {
        const string catalogKey = "ConnectionStrings__Catalog";
        const string allowlistKey = "MigrationHost__AllowedCatalogs__0";
        var oldCatalog = Environment.GetEnvironmentVariable(catalogKey);
        var oldAllowlist = Environment.GetEnvironmentVariable(allowlistKey);
        try
        {
            Environment.SetEnvironmentVariable(catalogKey, "Server=operator-host;Database=operator-catalog");
            Environment.SetEnvironmentVariable(allowlistKey, "operator-catalog");
            var configuration = MigrationHost.BuildConfiguration([]);

            Assert.Equal("Server=operator-host;Database=operator-catalog", configuration.GetConnectionString("Catalog"));
            Assert.Equal("operator-catalog", configuration["MigrationHost:AllowedCatalogs:0"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(catalogKey, oldCatalog);
            Environment.SetEnvironmentVariable(allowlistKey, oldAllowlist);
        }
    }

    [Fact]
    public void Command_line_arguments_override_repository_and_environment_configuration()
    {
        const string catalogKey = "ConnectionStrings__Catalog";
        var oldCatalog = Environment.GetEnvironmentVariable(catalogKey);
        try
        {
            Environment.SetEnvironmentVariable(catalogKey, "Server=environment-host;Database=environment-catalog");
            var configuration = MigrationHost.BuildConfiguration([
                "validate",
                "--ConnectionStrings:Catalog", "Server=cli-host;Database=cli-catalog",
                "--MigrationHost:AllowedCatalogs:0", "cli-catalog"]);

            Assert.Equal("Server=cli-host;Database=cli-catalog", configuration.GetConnectionString("Catalog"));
            Assert.Equal("cli-catalog", configuration["MigrationHost:AllowedCatalogs:0"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(catalogKey, oldCatalog);
        }
    }
}
