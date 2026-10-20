using Microsoft.Extensions.Configuration;

namespace HRMS.Tests;

public sealed class MigrationHostConfigurationTests
{
    [Fact]
    public void Tenant_only_requires_exact_approved_migration_set()
    {
        var approved = MigrationHost.TenantOnlyApprovedMigrations;
        Assert.Equal(2, approved.Count);
        Assert.Contains("20261013132338_AddWelcomeInvitationCenter", approved);
        Assert.Contains("20261013133934_AddWelcomeInvitationOutboxProcessing", approved);
        Assert.DoesNotContain("20261099999999_Unexpected", approved);
    }

    [Fact]
    public async Task Missing_backup_confirmation_fails_before_opening_any_database()
    {
        var result = await MigrationHost.RunAsync(["migrate", "--tenant-only"]);
        Assert.Equal(2, result);
    }

    [Fact]
    public void Tenant_only_and_physical_database_options_are_parsed()
    {
        var args = Arguments.Parse(["migrate", "--tenant-only", "--database", "web1"]);
        Assert.True(args.TenantOnly);
        Assert.Equal("web1", args.Database);
    }

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

    [Fact]
    public void Development_user_secrets_are_loaded_below_environment_and_command_line_values()
    {
        const string environmentKey = "DOTNET_ENVIRONMENT";
        const string catalogKey = "ConnectionStrings__Catalog";
        var oldEnvironment = Environment.GetEnvironmentVariable(environmentKey);
        var oldCatalog = Environment.GetEnvironmentVariable(catalogKey);
        try
        {
            Environment.SetEnvironmentVariable(environmentKey, "Development");
            Environment.SetEnvironmentVariable(catalogKey, "Server=synthetic-env;Database=env-db");
            var fromSecrets = MigrationHost.BuildConfiguration([], configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Catalog"] = "Server=synthetic-secret;Database=secret-db" }));
            Assert.Equal("Server=synthetic-env;Database=env-db", fromSecrets.GetConnectionString("Catalog"));

            var fromCommandLine = MigrationHost.BuildConfiguration([
                "--ConnectionStrings:Catalog", "Server=synthetic-cli;Database=cli-db"], configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Catalog"] = "Server=synthetic-secret;Database=secret-db" }));
            Assert.Equal("Server=synthetic-cli;Database=cli-db", fromCommandLine.GetConnectionString("Catalog"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, oldEnvironment);
            Environment.SetEnvironmentVariable(catalogKey, oldCatalog);
        }
    }
}
