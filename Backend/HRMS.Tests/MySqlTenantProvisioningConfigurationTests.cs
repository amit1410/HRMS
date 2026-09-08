using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Sharding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace HRMS.Tests;

public sealed class MySqlTenantProvisioningConfigurationTests
{
    [Fact]
    public void Shard_key_resolves_to_the_expected_tenant_database_name()
    {
        var connection = "Server=mysql;Port=3306;Database=HRMS_anevratechnologies;User ID=operator;Password=secret;";

        Assert.Equal("HRMS_anevratechnologies", TenantProvisioningService.GetMySqlDatabaseName(connection));
    }

    [Fact]
    public void Server_connection_removes_only_the_tenant_database_name()
    {
        var connection = "Server=mysql;Port=3306;Database=HRMS_anevratechnologies;User ID=operator;Password=secret;";

        var serverConnection = TenantProvisioningService.CreateMySqlServerConnectionString(connection);
        var builder = new MySqlConnectionStringBuilder(serverConnection);

        Assert.Equal(string.Empty, builder.Database);
        Assert.Equal("mysql", builder.Server);
        Assert.Equal("operator", builder.UserID);
    }

    [Fact]
    public void MySql_template_is_provider_specific_and_does_not_need_sql_server_template()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "SqlServer",
                ["Sharding:MySqlConnectionStringTemplate"] = "Server=mysql;Database=HRMS_{shardKey};User ID=operator;"
            })
            .Build();
        var options = Options.Create(new ShardingOptions
        {
            MySqlConnectionStringTemplate = configuration["Sharding:MySqlConnectionStringTemplate"]
        });
        var factory = new ShardConnectionStringFactory(configuration, options, NullLogger<ShardConnectionStringFactory>.Instance);

        Assert.Contains("HRMS_anevratechnologies", factory.For(new HRMS.Application.Abstractions.ShardDescriptor(
            Guid.NewGuid(), "ANEVRATECHNOLOGIES", "anevratechnologies.localhost", "anevratechnologies",
            TenantStatus.Inactive, DatabaseProviderType.MySql)));
    }

    [Fact]
    public void Recognized_history_prefix_is_allowed_to_have_pending_migrations()
    {
        var known = new[]
        {
            "20260905172008_InitialMySqlTenantSchema",
            "20260907182803_AddUserInvitations",
            "20260908163000_AddPasswordResetOtps"
        };

        var exception = Record.Exception(() => TenantProvisioningService.ValidateMySqlMigrationHistory(
            [known[0]], known, "hrms_anevra01"));

        Assert.Null(exception);
    }

    [Fact]
    public void Unknown_skipped_and_empty_history_fail_closed()
    {
        var known = new[]
        {
            "20260905172008_InitialMySqlTenantSchema",
            "20260907182803_AddUserInvitations",
            "20260908163000_AddPasswordResetOtps"
        };

        Assert.Throws<TenantProvisioningException>(() => TenantProvisioningService.ValidateMySqlMigrationHistory(
            ["foreign_migration"], known, "hrms_anevra01"));
        Assert.Throws<TenantProvisioningException>(() => TenantProvisioningService.ValidateMySqlMigrationHistory(
            [known[1]], known, "hrms_anevratechnologies"));
        Assert.Throws<TenantProvisioningException>(() => TenantProvisioningService.ValidateMySqlMigrationHistory(
            [], known, "hrms_empty"));
    }

    [Fact]
    public void Current_history_is_idempotently_accepted()
    {
        var known = new[]
        {
            "20260905172008_InitialMySqlTenantSchema",
            "20260907182803_AddUserInvitations",
            "20260908163000_AddPasswordResetOtps"
        };

        TenantProvisioningService.ValidateMySqlMigrationHistory(known, known, "hrms_anevratechnologies");
    }
}
