using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests;

internal static class AttendanceDeviceSyncRecoveryProviderAcceptance
{
    public const int IntendedScenarioCount = 18;

    public static async Task RunAsync(Func<TestTenantContext, HrmsDbContext> createContext, string provider)
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var deviceA = Guid.NewGuid();
        var deviceA2 = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        var clock = new MutableTimeProvider();

        await using (var setup = createContext(new TestTenantContext()))
        {
            setup.Tenants.AddRange(Tenant(tenantA, "P6G-A"), Tenant(tenantB, "P6G-B"));
            setup.Employees.AddRange(Employee(tenantA, employeeA, "P6G-A-001"), Employee(tenantB, employeeB, "P6G-B-001"));
            setup.EmployeeEmploymentHistory.AddRange(Employment(tenantA, employeeA), Employment(tenantB, employeeB));
            setup.Shifts.AddRange(Shift(tenantA), Shift(tenantB));
            setup.AttendanceDevices.AddRange(
                Device(tenantA, deviceA, "P6G-A-DEVICE", "P6G"),
                Device(tenantA, deviceA2, "P6G-A-DEVICE-2", "Unsupported"),
                Device(tenantB, deviceB, "P6G-B-DEVICE", "P6G"));
            setup.AttendanceDeviceEmployeeMappings.AddRange(
                Mapping(tenantA, deviceA, employeeA, "EXT-A"),
                Mapping(tenantA, deviceA2, employeeA, "EXT-A2"),
                Mapping(tenantB, deviceB, employeeB, "EXT-B"));
            await setup.SaveChangesAsync();
        }

        await using var db = createContext(new TestTenantContext(tenantA, Guid.NewGuid()));
        var owner = new AttendanceDeviceLeaseService(db, new TestTenantContext(tenantA), clock, Options());

        var persisted = await owner.TryAcquireAsync(deviceA, "provider-a");
        Assert.True(persisted.Acquired, $"{provider}: initial claim failed.");
        Assert.NotEqual(Guid.Empty, await db.AttendanceDeviceSyncLeases.Where(x => x.AttendanceDeviceId == deviceA).Select(x => x.LeaseToken).SingleAsync());
        var contenders = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            await using var contenderDb = createContext(new TestTenantContext(tenantA));
            return await new AttendanceDeviceLeaseService(contenderDb, new TestTenantContext(tenantA), clock, Options()).TryAcquireAsync(deviceA2, $"contender-{i}");
        }));
        Assert.Equal(1, contenders.Count(x => x.Acquired));
        await owner.ReleaseAsync(deviceA, persisted.Lease!.LeaseToken);

        var first = await owner.TryAcquireAsync(deviceA, "old-owner");
        clock.Advance(TimeSpan.FromSeconds(121));
        var reclaimed = await owner.TryAcquireAsync(deviceA, "new-owner");
        Assert.True(first.Acquired && reclaimed.Acquired);
        Assert.NotEqual(first.Lease!.LeaseToken, reclaimed.Lease!.LeaseToken);
        Assert.False(await owner.HeartbeatAsync(deviceA, first.Lease.LeaseToken));
        Assert.False(await owner.ReleaseAsync(deviceA, first.Lease.LeaseToken));
        Assert.True(await owner.HeartbeatAsync(deviceA, reclaimed.Lease!.LeaseToken));
        await owner.ReleaseAsync(deviceA, reclaimed.Lease.LeaseToken);
        await using (var tenantBContext = createContext(new TestTenantContext(tenantB)))
        {
            var tenantBLease = new AttendanceDeviceLeaseService(tenantBContext, new TestTenantContext(tenantB), clock, Options());
            Assert.True((await tenantBLease.TryAcquireAsync(deviceB, "tenant-b")).Acquired);
        }
        Assert.False((await owner.TryAcquireAsync(deviceB, "cross-tenant")).Acquired);

        var oldClaim = await owner.TryAcquireAsync(deviceA, "stale-owner");
        clock.Advance(TimeSpan.FromSeconds(121));
        var currentClaim = await owner.TryAcquireAsync(deviceA, "current-owner");
        Assert.True(oldClaim.Acquired && currentClaim.Acquired);
        var ingestion = CreateIngestion(db, tenantA, clock);
        var stale = await ingestion.IngestAsync(new(deviceA, "provider-recovery", [Punch("evt-stale", "EXT-A")], "stale-checkpoint", oldClaim.Lease!.LeaseToken));
        Assert.Equal(ResultStatus.Conflict, stale.Status);
        Assert.Equal(0, await db.AttendancePunches.CountAsync(x => x.TenantId == tenantA && x.ExternalPunchId == $"{deviceA:N}:evt-stale"));
        Assert.Null(await db.AttendanceDevices.Where(x => x.Id == deviceA).Select(x => x.LastSuccessfulCheckpoint).SingleAsync());
        var current = await ingestion.IngestAsync(new(deviceA, "provider-recovery", [Punch("evt-stale", "EXT-A")], "current-checkpoint", currentClaim.Lease!.LeaseToken));
        Assert.True(current.Succeeded, current.Message);
        Assert.Equal(1, await db.AttendancePunches.CountAsync(x => x.TenantId == tenantA && x.ExternalPunchId == $"{deviceA:N}:evt-stale"));
        await owner.ReleaseAsync(deviceA, currentClaim.Lease!.LeaseToken);

        var staleRun = new AttendanceDeviceSyncRun
        {
            Id = Guid.NewGuid(), TenantId = tenantA, AttendanceDeviceId = deviceA, Source = "worker",
            Status = AttendanceDeviceSyncStatus.Running, StartedAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-20)
        };
        db.AttendanceDeviceSyncRuns.Add(staleRun);
        var expired = await db.AttendanceDeviceSyncLeases.SingleAsync(x => x.TenantId == tenantA && x.AttendanceDeviceId == deviceA);
        expired.LeaseOwner = "crashed";
        expired.LeaseToken = Guid.NewGuid();
        expired.ClaimedAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-20);
        expired.LeaseExpiresAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-10);
        await db.SaveChangesAsync();
        var recovery = new AttendanceDeviceSyncRecoveryService(CreateOperations(db, tenantA, clock, []), db, new TestTenantContext(tenantA), Options(), clock, NullLogger<AttendanceDeviceSyncRecoveryService>.Instance);
        Assert.Equal(1, await recovery.ReconcileStaleRunsAsync());
        Assert.Equal(AttendanceDeviceSyncStatus.Failed, await db.AttendanceDeviceSyncRuns.Where(x => x.Id == staleRun.Id).Select(x => x.Status).SingleAsync());

        var replayFirst = await ingestion.IngestAsync(new(deviceA, "provider-replay", [Punch("evt-replay", "EXT-A")]));
        var replaySecond = await ingestion.IngestAsync(new(deviceA, "provider-replay", [Punch("evt-replay", "EXT-A")]));
        Assert.True(replayFirst.Succeeded && replaySecond.Succeeded);
        Assert.Equal(1, replaySecond.Value!.Duplicate);
        Assert.Equal(1, await db.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{deviceA:N}:evt-replay"));

        var workerClaim = await owner.TryAcquireAsync(deviceA, "worker-claim");
        Assert.True(workerClaim.Acquired);
        Assert.Equal(ResultStatus.Conflict, (await CreateOperations(db, tenantA, clock, []).SyncNowAsync(deviceA)).Status);
        await owner.ReleaseAsync(deviceA, workerClaim.Lease!.LeaseToken);
        var manualClaim = await owner.TryAcquireAsync(deviceA, "manual-claim");
        Assert.True(manualClaim.Acquired);
        Assert.False((await owner.TryAcquireAsync(deviceA, "worker-after-manual")).Acquired);
        await owner.ReleaseAsync(deviceA, manualClaim.Lease!.LeaseToken);

        var unmapped = await ingestion.IngestAsync(new(deviceA, "provider-unmapped", [Punch("evt-unmapped", "EXT-MISSING")], "unmapped-checkpoint"));
        Assert.True(unmapped.Succeeded);
        Assert.Equal(1, unmapped.Value!.Unmapped);
        Assert.Null(unmapped.Value.Checkpoint);
        Assert.Equal(0, await db.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{deviceA:N}:evt-unmapped"));
        var finalizedIssue = await ingestion.IngestAsync(new(deviceA, "provider-finalized", [new("evt-finalized", "EXT-FINALIZED", new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)], null));
        Assert.True(finalizedIssue.Succeeded);
        db.EmployeeAttendanceDays.AddRange(Enumerable.Range(1, 30).Select(day => new EmployeeAttendanceDay
        {
            Id = Guid.NewGuid(), TenantId = tenantA, EmployeeId = employeeA, BusinessDate = new DateOnly(2026, 11, day),
            Status = EmployeeAttendanceDayStatus.Present, ExpectedWorkMinutes = 480, WorkedMinutes = 480,
            ProcessedAtUtc = clock.GetUtcNow().UtcDateTime, ProcessingOutcome = "Phase 6G provider finalization control"
        }));
        await db.SaveChangesAsync();
        var monthly = new AttendanceMonthlyProcessor(db, new TestTenantContext(tenantA));
        var period = await monthly.CreatePeriodAsync(new(2026, 11));
        Assert.True(period.Succeeded, period.Message);
        Assert.True((await monthly.ProcessAsync(period.Value!.Id)).Succeeded);
        Assert.True((await monthly.CloseAsync(period.Value.Id)).Succeeded);
        var snapshotBefore = await db.PayrollAttendanceSnapshots.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Value.Id && x.IsCurrent && x.EmployeeId == employeeA);
        db.AttendanceDeviceEmployeeMappings.Add(Mapping(tenantA, deviceA, employeeA, "EXT-FINALIZED"));
        await db.SaveChangesAsync();
        var finalizedId = await db.AttendanceDeviceIngestionEvents.Where(x => x.ExternalEventId == "evt-finalized").Select(x => x.Id).SingleAsync();
        var finalizedResult = await CreateOperations(db, tenantA, clock, []).ReprocessIssueAsync(finalizedId);
        Assert.True(finalizedResult.Succeeded, finalizedResult.Message);
        Assert.Equal(AttendanceDeviceIngestionStatus.RequiresPeriodReopen, await db.AttendanceDeviceIngestionEvents.Where(x => x.Id == finalizedId).Select(x => x.Status).SingleAsync());
        Assert.Equal(AttendancePeriodStatus.Closed, await db.AttendancePeriods.Where(x => x.Id == period.Value.Id).Select(x => x.Status).SingleAsync());
        var snapshotAfter = await db.PayrollAttendanceSnapshots.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Value.Id && x.IsCurrent && x.EmployeeId == employeeA);
        Assert.Equal(snapshotBefore.Id, snapshotAfter.Id);
        Assert.Equal(snapshotBefore.Version, snapshotAfter.Version);
        Assert.Equal(0, await db.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == period.Value.Id && x.IsCurrent && x.Id != snapshotBefore.Id));
        var unsupported = await CreateOperations(db, tenantA, clock, []).SyncNowAsync(deviceA2);
        Assert.Equal(ResultStatus.Conflict, unsupported.Status);
        Assert.Contains("UnsupportedProvider", unsupported.Message);
        await using var tenantBCheck = createContext(new TestTenantContext(tenantB));
        Assert.Equal(0, await tenantBCheck.AttendancePunches.CountAsync(x => x.TenantId == tenantA));
    }

    private static AttendanceDeviceWorkerOptions Options() => new() { LeaseDurationSeconds = 120, HeartbeatIntervalSeconds = 30, MaxRetries = 0 };

    private static AttendanceDeviceOperationsService CreateOperations(HrmsDbContext db, Guid tenantId, MutableTimeProvider clock, IEnumerable<IAttendancePunchSource> sources)
    {
        var tenant = new TestTenantContext(tenantId);
        var options = Options();
        return new(db, tenant, CreateIngestion(db, tenantId, clock), sources, new AttendanceDeviceLeaseService(db, tenant, clock, options), options);
    }

    private static AttendanceDeviceIntegrationService CreateIngestion(HrmsDbContext db, Guid tenantId, MutableTimeProvider clock)
    {
        var tenant = new TestTenantContext(tenantId);
        var roster = new AttendanceFoundationService(db, tenant, new EffectiveEmploymentResolver(db, tenant));
        var calendar = new WorkingDayCalendarResolver(db, tenant, new EffectiveEmploymentResolver(db, tenant));
        var punch = new AttendancePunchIngestionService(db, tenant, roster, new AttendanceBusinessDateResolver(), new AttendanceBusinessTimeZoneProvider(), clock);
        return new(db, tenant, punch, new AttendanceDayProcessor(db, tenant, roster, clock), clock);
    }

    private static NormalizedAttendancePunch Punch(string id, string external) => new(id, external, new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In);
    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = $"{code}.test", ShardKey = id.ToString("N"), Status = TenantStatus.Active };
    private static Employee Employee(Guid tenantId, Guid id, string code) => new() { Id = id, TenantId = tenantId, EmployeeCode = code, FirstName = "Provider", LastName = "Acceptance", Email = $"{id:N}@provider.test", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active };
    private static EmployeeEmploymentHistory Employment(Guid tenantId, Guid id) => new() { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = id, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active };
    private static Shift Shift(Guid tenantId) => new() { Id = Guid.NewGuid(), TenantId = tenantId, ShiftCode = "P6G-DEFAULT", ShiftName = "P6G-DEFAULT", IsDefault = true, IsActive = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 1, CaptureMode = AttendanceCaptureMode.BiometricOnly, AllowedAttendanceSources = AttendanceSource.Biometric };
    private static AttendanceDevice Device(Guid tenantId, Guid id, string code, string vendor) => new() { Id = id, TenantId = tenantId, Code = code, Name = code, DeviceType = "Terminal", Vendor = vendor, TimeZoneId = "UTC", ConnectionMode = AttendanceDeviceConnectionMode.Pull, Status = AttendanceDeviceStatus.Active };
    private static AttendanceDeviceEmployeeMapping Mapping(Guid tenantId, Guid deviceId, Guid employeeId, string external) => new() { Id = Guid.NewGuid(), TenantId = tenantId, AttendanceDeviceId = deviceId, EmployeeId = employeeId, ExternalEmployeeIdentifier = external, EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active };

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 10, 20, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
