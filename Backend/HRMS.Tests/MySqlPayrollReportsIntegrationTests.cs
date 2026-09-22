using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlPayrollReportsIntegrationTests
{
    [Fact]
    [Trait("Category", "MySqlIntegration")]
    public async Task MySql_payroll_reports_provider_acceptance_is_repeatable()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("MySQL Payroll Reports tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var databaseName = $"HRMS_Phase7T_Reports_{Guid.NewGuid():N}";
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var connection = new MySqlConnectionStringBuilder(normalized) { Database = databaseName };

        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString))
            {
                await server.OpenAsync();
                await using var command = server.CreateCommand();
                command.CommandText = $"CREATE DATABASE `{databaseName}`";
                await command.ExecuteNonQueryAsync();
            }

            var tenant = new TestTenantContext();
            await using var db = new HrmsDbContext(
                new DbContextOptionsBuilder<HrmsDbContext>()
                    .UseMySQL(connection.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations"))
                    .Options,
                tenant);

            await db.Database.MigrateAsync();
            await PayrollReportsProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.MySql);
            await PayrollReportsProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.MySql);
        }
        finally
        {
            await using var server = new MySqlConnection(admin.ConnectionString);
            await server.OpenAsync();
            await using var drop = server.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
