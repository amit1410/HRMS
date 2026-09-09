using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Sharding;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using System.Text.RegularExpressions;

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

    [Fact]
    public void User_invitation_composite_index_has_a_mysql_safe_name_and_columns()
    {
        using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>()
            .UseMySQL("Server=not-opened;Database=not-opened;", mysql =>
                mysql.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations"))
            .Options, new TestTenantContext());

        var index = db.Model.FindEntityType(typeof(HRMS.Domain.Entities.UserInvitation))!
            .GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(
                ["TenantId", "UserId", "Purpose", "UsedAtUtc", "RevokedAtUtc"]));

        Assert.Equal("IX_UserInvitations_UserPurposeStatus", index.GetDatabaseName());
        Assert.InRange(index.GetDatabaseName()!.Length, 1, 64);
    }

    [Fact]
    public void MySql_fresh_schema_contains_no_overlong_index_or_constraint_identifiers()
    {
        using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>()
            .UseMySQL("Server=not-opened;Database=not-opened;", mysql =>
                mysql.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations"))
            .Options, new TestTenantContext());

        var script = db.Database.GenerateCreateScript();
        var identifiers = Regex.Matches(script, "(?:INDEX|CONSTRAINT|PRIMARY KEY|FOREIGN KEY)\\s+[`\\\"]?([A-Za-z0-9_~]+)", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(identifiers);
        Assert.All(identifiers, identifier => Assert.InRange(identifier.Length, 1, 64));
        Assert.Contains("IX_UserInvitations_UserPurposeStatus", script);
    }

    [Fact]
    public void MySql_invitation_migration_generates_fresh_database_sql_with_the_safe_index()
    {
        using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>()
            .UseMySQL("Server=not-opened;Database=not-opened;", mysql =>
                mysql.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations"))
            .Options, new TestTenantContext());

        var script = db.GetService<IMigrator>().GenerateScript(
            "20260905172008_InitialMySqlTenantSchema",
            "20260907182803_AddUserInvitations");

        Assert.Contains("UserInvitations", script);
        Assert.Contains("IX_UserInvitations_UserPurposeStatus", script);
        Assert.DoesNotContain("IX_UserInvitations_TenantId_UserId_Purpose_UsedAtUtc_RevokedAtUtc", script);
    }

    [Fact]
    public void SqlServer_model_uses_the_same_safe_invitation_index_name()
    {
        using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>()
            .UseSqlServer("Server=not-opened;Database=not-opened;", sql =>
                sql.MigrationsAssembly(typeof(HrmsDbContext).Assembly.FullName))
            .Options, new TestTenantContext());

        var index = db.Model.FindEntityType(typeof(HRMS.Domain.Entities.UserInvitation))!
            .GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(
                ["TenantId", "UserId", "Purpose", "UsedAtUtc", "RevokedAtUtc"]));

        Assert.Equal("IX_UserInvitations_UserPurposeStatus", index.GetDatabaseName());
    }

    [Fact]
    public void Checked_in_mysql_migrations_have_no_overlong_ef_identifiers()
    {
        var repositoryRoot = FindRepositoryRoot();
        var migrationDirectory = Path.Combine(repositoryRoot, "Backend", "HRMS.Infrastructure.MySqlMigrations", "Migrations");
        var identifierPattern = new Regex("\\\"((?:IX_|PK_|FK_|AK_|UX_|CK_)[^\\\"]+)\\\"", RegexOptions.Compiled);

        var identifiers = Directory.EnumerateFiles(migrationDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(path => identifierPattern.Matches(File.ReadAllText(path)).Select(match =>
                (Path: path, Name: match.Groups[1].Value)))
            .ToArray();

        Assert.NotEmpty(identifiers);
        Assert.All(identifiers, identifier =>
            Assert.True(identifier.Name.Length <= 64, $"{identifier.Name} in {identifier.Path} exceeds MySQL's 64-character limit."));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HRMS.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the HRMS repository root.");
    }
}
