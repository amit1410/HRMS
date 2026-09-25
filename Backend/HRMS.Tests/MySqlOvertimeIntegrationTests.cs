using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlOvertimeIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_overtime_provider_acceptance_preserves_versioned_snapshot_semantics()
    {
        static void Checkpoint(string name) => Console.WriteLine($"[MYSQL-OT] {name} {DateTimeOffset.UtcNow:O}");
        Checkpoint("fixture-start");
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Overtime tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var databaseName = $"HRMS_Phase6C_Overtime_{Guid.NewGuid():N}";
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var connection = new MySqlConnectionStringBuilder(normalized) { Database = databaseName };
        try
        {
            Checkpoint("database-create-start");
            await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(); Checkpoint("database-server-opened"); await using var command = server.CreateCommand(); command.CommandText = $"CREATE DATABASE `{databaseName}`"; await command.ExecuteNonQueryAsync(); }
            Checkpoint("database-created");
            var tenantContext = new TestTenantContext();
            await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, options => options.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenantContext);
            Checkpoint("migrations-start");
            await db.Database.MigrateAsync();
            Checkpoint("migrations-complete");
            Checkpoint("acceptance-start");
            await OvertimeProviderAcceptance.RunAsync(db, Guid.NewGuid(), tenantContext);
            Checkpoint("acceptance-complete");
        }
        finally
        {
            Checkpoint("cleanup-start");
            await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`"; await drop.ExecuteNonQueryAsync();
            Checkpoint("cleanup-complete");
        }
    }
}
