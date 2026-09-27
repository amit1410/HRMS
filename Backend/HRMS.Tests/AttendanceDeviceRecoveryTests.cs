using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests;

public sealed class AttendanceDeviceRecoveryTests
{
    [Fact]
    public async Task Transient_failure_retries_and_succeeds_without_duplicate_punch()
    {
        using var f = await FixtureAsync("Retry", "NoProvider");
        var source = new FlakySource("NoProvider", 1, Punch("retry-1"));
        var service = Recovery(f, source, new() { MaxRetries = 1, RetryBaseDelaySeconds = 1, RetryMaxDelaySeconds = 1 });

        var result = await service.ExecuteAsync(f.DeviceId);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, source.Calls);
        Assert.Equal(1, await f.Context.AttendancePunches.CountAsync());
        Assert.Equal(0, await f.Context.AttendancePunches.GroupBy(x => x.ExternalPunchId).Where(x => x.Count() > 1).CountAsync());
        Assert.Equal(0, await f.Context.AttendanceDevices.Where(x => x.Id == f.DeviceId).Select(x => x.ConsecutiveFailureCount).SingleAsync());
    }

    [Fact]
    public async Task Retry_exhaustion_is_failed_and_does_not_advance_checkpoint()
    {
        using var f = await FixtureAsync("Exhaust", "NoProvider");
        var service = Recovery(f, new FlakySource("NoProvider", 10, Punch("never")), new() { MaxRetries = 1, RetryBaseDelaySeconds = 1, RetryMaxDelaySeconds = 1 });

        var result = await service.ExecuteAsync(f.DeviceId);
        var device = await f.Context.AttendanceDevices.SingleAsync(x => x.Id == f.DeviceId);

        Assert.False(result.Succeeded);
        Assert.Equal("DeviceSyncRetryExhausted", result.Message);
        Assert.Null(device.LastSuccessfulCheckpoint);
        Assert.Equal(2, device.ConsecutiveFailureCount);
        Assert.Equal(0, await f.Context.AttendancePunches.CountAsync());
    }

    [Fact]
    public async Task Permanent_unsupported_provider_is_not_retried()
    {
        using var f = await FixtureAsync("Permanent", "Unsupported");
        var service = Recovery(f, Array.Empty<IAttendancePunchSource>(), new() { MaxRetries = 3, RetryBaseDelaySeconds = 1, RetryMaxDelaySeconds = 1 });

        var result = await service.ExecuteAsync(f.DeviceId);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("UnsupportedProvider", result.Message);
        Assert.Equal(1, await f.Context.AttendanceDeviceSyncRuns.CountAsync(x => x.FailureCode == "DeviceConfiguration"));
    }

    [Fact]
    public async Task Cancellation_stops_retry_backoff()
    {
        using var f = await FixtureAsync("Cancel", "NoProvider");
        var service = Recovery(f, new FlakySource("NoProvider", 10, Punch("cancel")), new() { MaxRetries = 2, RetryBaseDelaySeconds = 30, RetryMaxDelaySeconds = 30 });
        using var cts = new CancellationTokenSource();

        var task = service.ExecuteAsync(f.DeviceId, cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Stale_running_run_and_expired_lease_are_reconciled_without_losing_history()
    {
        using var f = await FixtureAsync("Recover", "NoProvider");
        var now = new DateTime(2026, 10, 10, 20, 0, 0, DateTimeKind.Utc);
        var oldRun = new AttendanceDeviceSyncRun
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, AttendanceDeviceId = f.DeviceId,
            Source = "worker", Status = AttendanceDeviceSyncStatus.Running,
            StartedAtUtc = now.AddMinutes(-20)
        };
        var lease = new AttendanceDeviceSyncLease
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, AttendanceDeviceId = f.DeviceId,
            LeaseOwner = "crashed-worker", LeaseToken = Guid.NewGuid(),
            ClaimedAtUtc = now.AddMinutes(-20), LeaseExpiresAtUtc = now.AddMinutes(-10), LastHeartbeatAtUtc = now.AddMinutes(-10)
        };
        f.Context.AttendanceDeviceSyncRuns.Add(oldRun);
        f.Context.AttendanceDeviceSyncLeases.Add(lease);
        await f.Context.SaveChangesAsync();

        var recovered = new AttendanceDeviceSyncRecoveryService(
            new AttendanceDeviceOperationsService(f.Context, f.TenantContext, Ingestion(f), []), f.Context, f.TenantContext,
            new AttendanceDeviceWorkerOptions { StaleRunThresholdSeconds = 300 }, new FixedTimeProvider(now), NullLogger<AttendanceDeviceSyncRecoveryService>.Instance);

        Assert.Equal(1, await recovered.ReconcileStaleRunsAsync());
        var savedRun = await f.Context.AttendanceDeviceSyncRuns.SingleAsync(x => x.Id == oldRun.Id);
        var savedLease = await f.Context.AttendanceDeviceSyncLeases.SingleAsync(x => x.Id == lease.Id);
        Assert.Equal(AttendanceDeviceSyncStatus.Failed, savedRun.Status);
        Assert.Equal("WorkerLeaseExpired", savedRun.FailureCode);
        Assert.Null(savedLease.LeaseToken);
        Assert.Equal(1, await f.Context.AttendanceDeviceSyncRuns.CountAsync());
    }

    [Fact]
    public async Task Stale_owner_cannot_complete_ingestion_or_move_checkpoint()
    {
        using var f = await FixtureAsync("LeaseGuard", "NoProvider");
        var clock = new MutableTimeProvider();
        var leaseService = new AttendanceDeviceLeaseService(f.Context, f.TenantContext, clock,
            new AttendanceDeviceWorkerOptions { LeaseDurationSeconds = 10, HeartbeatIntervalSeconds = 2 });
        var old = await leaseService.TryAcquireAsync(f.DeviceId, "old-worker");
        clock.Advance(TimeSpan.FromSeconds(11));
        var current = await leaseService.TryAcquireAsync(f.DeviceId, "new-worker");
        Assert.True(old.Acquired && current.Acquired);

        var result = await Ingestion(f).IngestAsync(new(f.DeviceId, "lease-guard", [Punch("stale")], "stale-checkpoint", old.Lease!.LeaseToken));

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("DeviceSyncLeaseLost", result.Message);
        Assert.Equal(0, await f.Context.AttendancePunches.CountAsync());
        Assert.Null(await f.Context.AttendanceDevices.Where(x => x.Id == f.DeviceId).Select(x => x.LastSuccessfulCheckpoint).SingleAsync());
    }

    [Fact]
    public async Task Poison_device_does_not_block_healthy_device()
    {
        using var f = await FixtureAsync("Poison", "PoisonProvider", twoDevices: true);
        var healthy = await f.Context.AttendanceDevices.SingleAsync(x => x.Code == "Healthy");
        f.Context.AttendanceDeviceEmployeeMappings.Add(new AttendanceDeviceEmployeeMapping
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, AttendanceDeviceId = healthy.Id,
            ExternalEmployeeIdentifier = "EXT-1", EmployeeId = f.EmployeeId, EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active
        });
        await f.Context.SaveChangesAsync();

        var poison = Recovery(f, new FlakySource("PoisonProvider", 10, Punch("poison")), new() { MaxRetries = 0 });
        var good = Recovery(f, new FlakySource("HealthyProvider", 0, Punch("healthy")), new() { MaxRetries = 0 });
        var poisonResult = await poison.ExecuteAsync(f.DeviceId);
        var goodResult = await good.ExecuteAsync(healthy.Id);

        Assert.False(poisonResult.Succeeded);
        Assert.True(goodResult.Succeeded, goodResult.Message);
        Assert.Equal(1, await f.Context.AttendancePunches.CountAsync());
    }

    [Fact]
    public void Invalid_retry_and_recovery_options_are_rejected()
    {
        Assert.NotNull(new AttendanceDeviceWorkerOptions { MaxRetries = -1 }.Validate());
        Assert.NotNull(new AttendanceDeviceWorkerOptions { MaxRetries = 1, RetryBaseDelaySeconds = 0 }.Validate());
        Assert.NotNull(new AttendanceDeviceWorkerOptions { RetryBaseDelaySeconds = 10, RetryMaxDelaySeconds = 5 }.Validate());
        Assert.NotNull(new AttendanceDeviceWorkerOptions { StaleRunThresholdSeconds = 0 }.Validate());
        Assert.Null(new AttendanceDeviceWorkerOptions { Enabled = false }.Validate());
    }

    [Fact]
    public async Task Health_reports_disabled_worker_stale_state_and_recent_failures()
    {
        using var f = await FixtureAsync("Health", "NoProvider");
        var now = new DateTime(2026, 10, 10, 20, 0, 0, DateTimeKind.Utc);
        f.Context.AttendanceDeviceSyncLeases.Add(new AttendanceDeviceSyncLease
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, AttendanceDeviceId = f.DeviceId,
            LeaseOwner = "dead", LeaseToken = Guid.NewGuid(), LeaseExpiresAtUtc = now.AddMinutes(-1)
        });
        f.Context.AttendanceDeviceSyncRuns.Add(new AttendanceDeviceSyncRun
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, AttendanceDeviceId = f.DeviceId,
            Source = "worker", Status = AttendanceDeviceSyncStatus.Running, StartedAtUtc = now.AddMinutes(-10)
        });
        await f.Context.SaveChangesAsync();

        var health = new AttendanceDeviceHealthService(f.Context, f.TenantContext,
            new AttendanceDeviceWorkerOptions { Enabled = false, StaleRunThresholdSeconds = 300 }, new FixedTimeProvider(now));
        var result = await health.GetAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.WorkerEnabled);
        Assert.Equal(1, result.Value.StaleLeases);
        Assert.Equal(1, result.Value.StaleRunningSyncRuns);
        Assert.Equal(0, result.Value.RecentFailures);
    }

    [Fact]
    public async Task Retry_state_persistence_failure_does_not_report_success_or_leave_a_claim()
    {
        using var f = await FixtureAsync("RetryPersist", "NoProvider");
        await using var context = f.CreateIsolatedContext(new TestTenantContext(f.TenantId), new FailOnceSaveChangesInterceptor(
            db => db.ChangeTracker.Entries<AttendanceDevice>().Any(x => x.State == EntityState.Modified)));
        var options = new AttendanceDeviceWorkerOptions { MaxRetries = 0 };
        var operations = new AttendanceDeviceOperationsService(context, new TestTenantContext(f.TenantId), Ingestion(f),
            [new FlakySource("NoProvider", 1, Punch("persistence-failure"))],
            new AttendanceDeviceLeaseService(context, new TestTenantContext(f.TenantId), new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)), options), options);
        var recovery = new AttendanceDeviceSyncRecoveryService(operations, context, new TestTenantContext(f.TenantId), options,
            TimeProvider.System, NullLogger<AttendanceDeviceSyncRecoveryService>.Instance);

        await Assert.ThrowsAsync<DbUpdateException>(() => recovery.ExecuteAsync(f.DeviceId));
        Assert.Equal(0, await f.Context.AttendanceDeviceSyncLeases.CountAsync(x => x.LeaseToken != null));
        Assert.Null(await f.Context.AttendanceDevices.Where(x => x.Id == f.DeviceId).Select(x => x.LastSuccessfulCheckpoint).SingleAsync());
        Assert.Equal(0, await f.Context.AttendancePunches.CountAsync());
    }

    private static AttendanceDeviceSyncRecoveryService Recovery(AttendanceTestFixture f, IAttendancePunchSource source, AttendanceDeviceWorkerOptions options) =>
        Recovery(f, [source], options);

    private static AttendanceDeviceSyncRecoveryService Recovery(AttendanceTestFixture f, IEnumerable<IAttendancePunchSource> sources, AttendanceDeviceWorkerOptions options) =>
        new(new AttendanceDeviceOperationsService(f.Context, f.TenantContext, Ingestion(f), sources,
            new AttendanceDeviceLeaseService(f.Context, f.TenantContext, new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)), options), options), f.Context, f.TenantContext,
            options, TimeProvider.System, NullLogger<AttendanceDeviceSyncRecoveryService>.Instance);

    private static async Task<AttendanceTestFixture> FixtureAsync(string code, string vendor, bool twoDevices = false)
    {
        var f = await AttendanceTestFixture.CreateAsync();
        await f.AddShiftAsync($"{code}-SHIFT", isDefault: true);
        f.Context.AttendanceDevices.Add(new AttendanceDevice
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, Code = code, Name = code, DeviceType = "Terminal", Vendor = vendor,
            TimeZoneId = "UTC", ConnectionMode = AttendanceDeviceConnectionMode.Pull, Status = AttendanceDeviceStatus.Active
        });
        if (twoDevices)
            f.Context.AttendanceDevices.Add(new AttendanceDevice
            {
                Id = Guid.NewGuid(), TenantId = f.TenantId, Code = "Healthy", Name = "Healthy", DeviceType = "Terminal", Vendor = "HealthyProvider",
                TimeZoneId = "UTC", ConnectionMode = AttendanceDeviceConnectionMode.Pull, Status = AttendanceDeviceStatus.Active
            });
        await f.Context.SaveChangesAsync();
        f.DeviceId = await f.Context.AttendanceDevices.Where(x => x.Code == code).Select(x => x.Id).SingleAsync();
        f.Context.AttendanceDeviceEmployeeMappings.Add(new AttendanceDeviceEmployeeMapping
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, AttendanceDeviceId = f.DeviceId,
            ExternalEmployeeIdentifier = "EXT-1", EmployeeId = f.EmployeeId, EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active
        });
        await f.Context.SaveChangesAsync();
        return f;
    }

    private static NormalizedAttendancePunch Punch(string id) =>
        new(id, "EXT-1", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In);

    private static AttendanceDeviceIntegrationService Ingestion(AttendanceTestFixture f)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero));
        var punch = new AttendancePunchIngestionService(f.Context, f.TenantContext, f.CalendarService, new AttendanceBusinessDateResolver(), new AttendanceBusinessTimeZoneProvider(), clock);
        return new(f.Context, f.TenantContext, punch, new AttendanceDayProcessor(f.Context, f.TenantContext, f.CalendarService, clock), clock);
    }

    private sealed class FlakySource(string key, int failures, NormalizedAttendancePunch punch) : IAttendancePunchSource
    {
        private int _calls;
        public string ProviderKey => key;
        public int Calls => _calls;
        public Task<AttendanceDevicePunchPage> FetchAsync(string? checkpoint, int maximumItems, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) <= failures) throw new TimeoutException("test transient failure");
            return Task.FromResult(new AttendanceDevicePunchPage([punch], $"checkpoint-{punch.ExternalEventId}"));
        }
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 10, 20, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class FailOnceSaveChangesInterceptor(Func<DbContext, bool> predicate) : SaveChangesInterceptor
    {
        private int _failed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null && predicate(eventData.Context) && Interlocked.Exchange(ref _failed, 1) == 0)
                throw new DbUpdateException("Injected Phase 6G retry-state persistence failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
