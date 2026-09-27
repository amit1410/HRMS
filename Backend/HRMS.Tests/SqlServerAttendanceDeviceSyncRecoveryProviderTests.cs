using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class SqlServerAttendanceDeviceSyncRecoveryProviderTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;

    public SqlServerAttendanceDeviceSyncRecoveryProviderTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [Fact]
    public async Task SqlServer_phase6g_attendance_device_sync_recovery_provider_acceptance()
    {
        if (!fixture.IsConfigured) throw SkipException.ForSkip("Phase 6G SQL Server provider acceptance requires HRMS_SQLSERVER_TEST_CONNECTION.");
        await using var disposable = await fixture.CreateDisposableDatabaseAsync("HRMS_Phase6G_Integration_");
        await using var probe = disposable.CreateContext(new TestTenantContext());
        var applied = await probe.Database.GetAppliedMigrationsAsync();
        Assert.Equal(probe.Database.GetMigrations(), applied);
        Assert.Contains(applied, x => x.EndsWith("_AddAttendanceDeviceIntegrationPhase6F", StringComparison.Ordinal));
        Assert.Contains(applied, x => x.EndsWith("_AddAttendanceDeviceAdministrationAuditPhase6F", StringComparison.Ordinal));
        Assert.Contains(applied, x => x.EndsWith("_AddAttendanceDeviceSyncLeasePhase6G", StringComparison.Ordinal));
        Assert.Contains(applied, x => x.EndsWith("_AddAttendanceDeviceSyncRecoveryPhase6G", StringComparison.Ordinal));
        await AttendanceDeviceSyncRecoveryProviderAcceptance.RunAsync(disposable.CreateContext, "SQL Server");
    }
}
