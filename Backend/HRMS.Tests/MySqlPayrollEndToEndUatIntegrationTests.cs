using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlPayrollEndToEndUatIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_payroll_end_to_end_uat_acceptance_preserves_provider_neutral_semantics()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Payroll UAT tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var databaseName = $"HRMS_Phase7M_Uat_{Guid.NewGuid():N}";
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var connection = new MySqlConnectionStringBuilder(normalized) { Database = databaseName };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString))
            {
                await server.OpenAsync(timeout.Token);
                await using var command = server.CreateCommand(); command.CommandTimeout = 90; command.CommandText = $"CREATE DATABASE `{databaseName}`"; await command.ExecuteNonQueryAsync(timeout.Token);
            }
            var tenant = new TestTenantContext();
            await using (var db = Context(connection.ConnectionString, tenant)) { await db.Database.MigrateAsync(timeout.Token); await PayrollEndToEndUatAcceptance.RunAsync(db, tenant); }
        }
        finally
        {
            await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandTimeout = 90; drop.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`"; await drop.ExecuteNonQueryAsync();
        }
    }

    private static HrmsDbContext Context(string connection, TestTenantContext tenant) => new(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection, options => options.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant);
}
