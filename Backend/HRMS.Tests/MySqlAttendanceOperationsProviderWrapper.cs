using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

[Collection("Attendance MySQL")]
public sealed class MySqlAttendanceOperationsIntegrationTests
{
    [Phase6BMySqlFact]
    public async Task MySql_phase6e_attendance_operations_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Phase 6E MySQL provider acceptance requires HRMS_MYSQL_TEST_CONNECTION.");

        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var databaseName = $"HRMS_Phase6E_AttendanceOps_Test_{Guid.NewGuid():N}";
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var target = new MySqlConnectionStringBuilder(normalized) { Database = databaseName };
        var databaseCreated = false;
        try
        {
            await using (var catalog = new MySqlConnection(admin.ConnectionString))
            {
                await catalog.OpenAsync();
                await using var create = catalog.CreateCommand();
                create.CommandText = $"CREATE DATABASE `{databaseName}`";
                await create.ExecuteNonQueryAsync();
                databaseCreated = true;
            }

            var seed = new MySqlLeaveLifecycleIntegrationTests.Fixture(target.ConnectionString);
            await seed.SeedAsync();
            await using var db = seed.CreateContext(seed.ManagerTenant);
            var applied = await db.Database.GetAppliedMigrationsAsync();
            var all = db.Database.GetMigrations();
            Assert.NotEmpty(all);
            Assert.Equal(all, applied);
            Assert.Contains(applied, x => x.EndsWith("_AddAttendanceExceptionResolutionStatePhase6E", StringComparison.Ordinal));
            Assert.True(await db.Database.CanConnectAsync());
            Assert.False(await db.AttendanceExceptionResolutions.AnyAsync());
            await AttendanceOperationsProviderAcceptance.RunAsync(db, new MySqlFixtureAdapter(seed));
        }
        finally
        {
            if (databaseCreated)
            {
                await using var catalog = new MySqlConnection(admin.ConnectionString);
                await catalog.OpenAsync();
                await using var drop = catalog.CreateCommand();
                drop.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`";
                await drop.ExecuteNonQueryAsync();
                await using var verify = catalog.CreateCommand();
                verify.CommandText = "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @databaseName";
                verify.Parameters.AddWithValue("@databaseName", databaseName);
                Assert.Equal(0L, Convert.ToInt64(await verify.ExecuteScalarAsync()));
            }
        }
    }

    private sealed class MySqlFixtureAdapter(MySqlLeaveLifecycleIntegrationTests.Fixture fixture) : IAttendanceOperationsProviderFixture
    {
        public string ProviderName => "MySQL";
        public Guid TenantId => fixture.TenantId;
        public Guid OtherTenantId => fixture.OtherTenantId;
        public Guid EmployeeId => fixture.EmployeeId;
        public Guid ManagerId => fixture.ManagerId;
        public Guid ManagerUserId => fixture.ManagerUserId;
        public Guid LeaveTypeId => fixture.LeaveTypeId;
        public TestTenantContext EmployeeTenant => fixture.EmployeeTenant;
        public TestTenantContext ManagerTenant => fixture.ManagerTenant;
        public HRMS.Application.Abstractions.ILeaveRequestSubmissionLock CreateLeaveSubmissionLock(HrmsDbContext db) => new MySqlLeaveRequestSubmissionLock(db);
    }
}
