using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

[Collection("Attendance MySQL")]
public sealed class MySqlAttendanceDeviceSyncRecoveryProviderTests
{
    [Fact]
    public async Task MySql_phase6g_attendance_device_sync_recovery_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("Phase 6G MySQL provider acceptance requires HRMS_MYSQL_TEST_CONNECTION.");
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var databaseName = $"HRMS_Phase6G_Integration_{Guid.NewGuid():N}";
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var target = new MySqlConnectionStringBuilder(normalized) { Database = databaseName };
        var created = false;
        try
        {
            await using (var catalog = new MySqlConnection(admin.ConnectionString))
            {
                await catalog.OpenAsync();
                await using var create = catalog.CreateCommand();
                create.CommandText = "CREATE DATABASE " + "\u0060" + databaseName + "\u0060";
                await create.ExecuteNonQueryAsync();
                created = true;
            }
            await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(target.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, new TestTenantContext());
            await db.Database.MigrateAsync();
            var applied = await db.Database.GetAppliedMigrationsAsync();
            Assert.Equal(db.Database.GetMigrations(), applied);
            Assert.Contains(applied, x => x.EndsWith("_AddAttendanceDeviceIntegrationPhase6F", StringComparison.Ordinal));
            Assert.Contains(applied, x => x.EndsWith("_AddAttendanceDeviceAdministrationAuditPhase6F", StringComparison.Ordinal));
            Assert.Contains(applied, x => x.EndsWith("_AddAttendanceDeviceSyncLeasePhase6G", StringComparison.Ordinal));
            Assert.Contains(applied, x => x.EndsWith("_AddAttendanceDeviceSyncRecoveryPhase6G", StringComparison.Ordinal));
            await AttendanceDeviceSyncRecoveryProviderAcceptance.RunAsync(tenant => new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(target.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant), "MySQL");
        }
        finally
        {
            if (created)
            {
                await using var catalog = new MySqlConnection(admin.ConnectionString);
                await catalog.OpenAsync();
                await using var drop = catalog.CreateCommand();
                drop.CommandText = "DROP DATABASE IF EXISTS " + "\u0060" + databaseName + "\u0060";
                await drop.ExecuteNonQueryAsync();
                await using var verify = catalog.CreateCommand();
                verify.CommandText = "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @name";
                verify.Parameters.AddWithValue("@name", databaseName);
                Assert.Equal(0L, Convert.ToInt64(await verify.ExecuteScalarAsync()));
            }
        }
    }
}
