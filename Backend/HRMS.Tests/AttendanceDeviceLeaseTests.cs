using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceDeviceLeaseTests
{
    [Fact]
    public async Task First_claim_succeeds()
    {
        using var f = await FixtureAsync();
        var result = await Lease(f).TryAcquireAsync(f.DeviceId, "worker-a");
        Assert.True(result.Acquired);
        Assert.NotNull(result.Lease);
    }

    [Fact]
    public async Task Second_same_device_claim_is_denied_while_valid()
    {
        using var f = await FixtureAsync();
        var service = Lease(f);
        Assert.True((await service.TryAcquireAsync(f.DeviceId, "worker-a")).Acquired);
        var second = await service.TryAcquireAsync(f.DeviceId, "worker-b");
        Assert.False(second.Acquired);
        Assert.Equal("DeviceSyncAlreadyRunning", second.Message);
    }

    [Fact]
    public async Task Concurrent_claim_race_has_at_most_one_owner()
    {
        using var f = await FixtureAsync();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<AttendanceDeviceLeaseResult> AttemptAsync(string owner)
        {
            await using var context = f.CreateIsolatedContext(new TestTenantContext(f.TenantId));
            await gate.Task;
            return await new AttendanceDeviceLeaseService(context, new TestTenantContext(f.TenantId), TimeProvider.System,
                new AttendanceDeviceWorkerOptions()).TryAcquireAsync(f.DeviceId, owner);
        }
        var first = AttemptAsync("worker-a");
        var second = AttemptAsync("worker-b");
        gate.SetResult(true);
        var outcomes = await Task.WhenAll(first, second);
        Assert.Equal(1, outcomes.Count(x => x.Acquired));
    }

    [Fact]
    public async Task Different_device_claim_succeeds()
    {
        using var f = await FixtureAsync(twoDevices: true);
        var service = Lease(f);
        Assert.True((await service.TryAcquireAsync(f.DeviceId, "worker-a")).Acquired);
        Assert.True((await service.TryAcquireAsync(f.SecondDeviceId, "worker-b")).Acquired);
    }

    [Fact]
    public async Task Same_device_id_in_different_tenant_is_independent()
    {
        using var f = await FixtureAsync();
        var service = Lease(f);
        Assert.True((await service.TryAcquireAsync(f.DeviceId, "tenant-a")).Acquired);
        var otherTenant = Guid.NewGuid();
        f.Context.Tenants.Add(new Tenant { Id = otherTenant, TenantCode = $"T-{otherTenant:N}"[..20], Host = $"{otherTenant:N}.test", ShardKey = otherTenant.ToString("N"), TenantName = "Other" });
        await f.Context.SaveChangesAsync();
        f.SwitchTenant(otherTenant);
        f.Context.ChangeTracker.Clear();
        var otherDeviceId = Guid.NewGuid();
        f.Context.AttendanceDevices.Add(new AttendanceDevice { Id = otherDeviceId, TenantId = otherTenant, Code = "LEASE-1", Name = "Other", DeviceType = "Terminal", TimeZoneId = "UTC", ConnectionMode = AttendanceDeviceConnectionMode.Pull, Status = AttendanceDeviceStatus.Active });
        await f.Context.SaveChangesAsync();
        Assert.True((await Lease(f).TryAcquireAsync(otherDeviceId, "tenant-b")).Acquired);
    }

    [Fact]
    public async Task Expired_claim_can_be_reclaimed()
    {
        using var f = await FixtureAsync();
        var clock = new MutableTimeProvider();
        var service = Lease(f, clock);
        var first = await service.TryAcquireAsync(f.DeviceId, "worker-a");
        clock.Advance(TimeSpan.FromSeconds(11));
        var second = await service.TryAcquireAsync(f.DeviceId, "worker-b");
        Assert.True(first.Acquired && second.Acquired);
        Assert.NotEqual(first.Lease!.LeaseToken, second.Lease!.LeaseToken);
    }

    [Fact]
    public async Task Stale_token_cannot_release_new_claim()
    {
        using var f = await FixtureAsync();
        var clock = new MutableTimeProvider();
        var service = Lease(f, clock);
        var first = await service.TryAcquireAsync(f.DeviceId, "worker-a");
        clock.Advance(TimeSpan.FromSeconds(11));
        var second = await service.TryAcquireAsync(f.DeviceId, "worker-b");
        Assert.False(await service.ReleaseAsync(f.DeviceId, first.Lease!.LeaseToken));
        Assert.True(await service.ReleaseAsync(f.DeviceId, second.Lease!.LeaseToken));
    }

    [Fact]
    public async Task Stale_token_cannot_heartbeat_new_claim()
    {
        using var f = await FixtureAsync();
        var clock = new MutableTimeProvider();
        var service = Lease(f, clock);
        var first = await service.TryAcquireAsync(f.DeviceId, "worker-a");
        clock.Advance(TimeSpan.FromSeconds(11));
        await service.TryAcquireAsync(f.DeviceId, "worker-b");
        Assert.False(await service.HeartbeatAsync(f.DeviceId, first.Lease!.LeaseToken));
    }

    [Fact]
    public async Task Valid_owner_heartbeat_extends_claim()
    {
        using var f = await FixtureAsync();
        var clock = new MutableTimeProvider();
        var service = Lease(f, clock);
        var claim = await service.TryAcquireAsync(f.DeviceId, "worker-a");
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(await service.HeartbeatAsync(f.DeviceId, claim.Lease!.LeaseToken));
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.False((await service.TryAcquireAsync(f.DeviceId, "worker-b")).Acquired);
    }

    [Fact]
    public async Task Valid_owner_release_succeeds_and_allows_next_claim()
    {
        using var f = await FixtureAsync();
        var service = Lease(f);
        var claim = await service.TryAcquireAsync(f.DeviceId, "worker-a");
        Assert.True(await service.ReleaseAsync(f.DeviceId, claim.Lease!.LeaseToken));
        Assert.True((await service.TryAcquireAsync(f.DeviceId, "worker-b")).Acquired);
    }

    [Fact]
    public async Task Manual_sync_respects_worker_lease()
    {
        using var f = await FixtureAsync(pull: true);
        var claim = await Lease(f).TryAcquireAsync(f.DeviceId, "worker-a");
        var operations = new AttendanceDeviceOperationsService(f.Context, f.TenantContext, Ingestion(f), []);
        var result = await operations.SyncNowAsync(f.DeviceId);
        Assert.True(claim.Acquired);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("DeviceSyncAlreadyRunning", result.Message);
    }

    [Fact]
    public async Task Device_query_selects_only_active_pull_devices()
    {
        using var f = await FixtureAsync(pull: true, twoDevices: true);
        f.Context.AttendanceDevices.Add(new AttendanceDevice { Id = Guid.NewGuid(), TenantId = f.TenantId, Code = "PUSH", Name = "Push", DeviceType = "Terminal", TimeZoneId = "UTC", ConnectionMode = AttendanceDeviceConnectionMode.Push, Status = AttendanceDeviceStatus.Active });
        f.Context.AttendanceDevices.Add(new AttendanceDevice { Id = Guid.NewGuid(), TenantId = f.TenantId, Code = "INACTIVE", Name = "Inactive", DeviceType = "Terminal", TimeZoneId = "UTC", ConnectionMode = AttendanceDeviceConnectionMode.Pull, Status = AttendanceDeviceStatus.Inactive });
        await f.Context.SaveChangesAsync();
        var result = await Operations(f).GetDevicesAsync(new(Status: AttendanceDeviceStatus.Active, ConnectionMode: AttendanceDeviceConnectionMode.Pull));
        Assert.True(result.Succeeded);
        Assert.All(result.Value!.Items, x => Assert.Equal(AttendanceDeviceConnectionMode.Pull, x.ConnectionMode));
        Assert.DoesNotContain(result.Value.Items, x => x.Code is "PUSH" or "INACTIVE");
    }

    [Fact]
    public void Disabled_worker_options_are_explicit_and_valid()
    {
        var options = new AttendanceDeviceWorkerOptions();
        Assert.False(options.Enabled);
        Assert.Null(options.Validate());
    }

    [Fact]
    public void Invalid_worker_options_are_rejected()
    {
        Assert.NotNull(new AttendanceDeviceWorkerOptions { PollingIntervalSeconds = 0 }.Validate());
        Assert.NotNull(new AttendanceDeviceWorkerOptions { HeartbeatIntervalSeconds = 120 }.Validate());
    }

    [Fact]
    public void Worker_cycle_is_bounded_by_page_and_cycle_limits()
    {
        var options = new AttendanceDeviceWorkerOptions { DevicePageSize = 25, MaxDevicesPerCycle = 75 };
        Assert.Null(options.Validate());
        Assert.InRange(options.DevicePageSize, 1, options.MaxDevicesPerCycle);
        Assert.True(options.MaxDevicesPerCycle > 0);
    }

    [Fact]
    public void Worker_ingestion_batch_size_is_explicitly_configured()
    {
        var options = new AttendanceDeviceWorkerOptions { BatchSize = 250 };
        Assert.Null(options.Validate());
        Assert.Equal(250, options.BatchSize);
    }

    [Fact]
    public async Task Canceled_claim_honors_cancellation()
    {
        using var f = await FixtureAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Lease(f).TryAcquireAsync(f.DeviceId, "worker-a", cts.Token));
    }

    [Fact]
    public async Task Unsupported_provider_isolated_to_one_device_run()
    {
        using var f = await FixtureAsync(pull: true);
        var operations = new AttendanceDeviceOperationsService(f.Context, f.TenantContext, Ingestion(f), []);
        var result = await operations.SyncNowAsync(f.DeviceId);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("UnsupportedProvider", result.Message);
        Assert.Equal(0, await f.Context.AttendanceDeviceSyncLeases.CountAsync(x => x.LeaseToken != null));
    }

    [Fact]
    public async Task Sync_now_reuses_existing_ingestion_pipeline()
    {
        using var f = await FixtureAsync(pull: true);
        f.Context.AttendanceDeviceEmployeeMappings.Add(new AttendanceDeviceEmployeeMapping { Id = Guid.NewGuid(), TenantId = f.TenantId, AttendanceDeviceId = f.DeviceId, ExternalEmployeeIdentifier = "EXT-1", EmployeeId = f.EmployeeId, EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active });
        await f.Context.SaveChangesAsync();
        var source = new TestPunchSource("NoProvider", new NormalizedAttendancePunch("evt-1", "EXT-1", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In));
        var options = new AttendanceDeviceWorkerOptions();
        var operations = new AttendanceDeviceOperationsService(f.Context, f.TenantContext, Ingestion(f), [source],
            new AttendanceDeviceLeaseService(f.Context, f.TenantContext, new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)), options), options);
        var result = await operations.SyncNowAsync(f.DeviceId);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, await f.Context.AttendancePunches.CountAsync());
        Assert.Empty(await f.Context.AttendanceDeviceSyncLeases.Where(x => x.LeaseToken != null).ToListAsync());
    }

    private static AttendanceDeviceLeaseService Lease(AttendanceTestFixture f, TimeProvider? clock = null) =>
        new(f.Context, f.TenantContext, clock ?? TimeProvider.System, new AttendanceDeviceWorkerOptions { LeaseDurationSeconds = 10, HeartbeatIntervalSeconds = 2 });

    private static AttendanceDeviceOperationsService Operations(AttendanceTestFixture f) =>
        new(f.Context, f.TenantContext, Ingestion(f), []);

    private static AttendanceDeviceIntegrationService Ingestion(AttendanceTestFixture f)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero));
        var punch = new AttendancePunchIngestionService(f.Context, f.TenantContext, f.CalendarService, new AttendanceBusinessDateResolver(), new AttendanceBusinessTimeZoneProvider(), clock);
        return new(f.Context, f.TenantContext, punch, new AttendanceDayProcessor(f.Context, f.TenantContext, f.CalendarService, clock), clock);
    }

    private static async Task<AttendanceTestFixture> FixtureAsync(bool pull = false, bool twoDevices = false)
    {
        var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("DEVICE-LEASE", isDefault: true);
        fixture.Context.AttendanceDevices.Add(new AttendanceDevice { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Code = "LEASE-1", Name = "Lease 1", DeviceType = "Terminal", Vendor = pull ? "NoProvider" : null, TimeZoneId = "UTC", ConnectionMode = pull ? AttendanceDeviceConnectionMode.Pull : AttendanceDeviceConnectionMode.FileImport, Status = AttendanceDeviceStatus.Active });
        if (twoDevices) fixture.Context.AttendanceDevices.Add(new AttendanceDevice { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Code = "LEASE-2", Name = "Lease 2", DeviceType = "Terminal", TimeZoneId = "UTC", ConnectionMode = AttendanceDeviceConnectionMode.Pull, Status = AttendanceDeviceStatus.Active });
        await fixture.Context.SaveChangesAsync();
        fixture.DeviceId = await fixture.Context.AttendanceDevices.Where(x => x.Code == "LEASE-1").Select(x => x.Id).SingleAsync();
        fixture.SecondDeviceId = await fixture.Context.AttendanceDevices.Where(x => x.Code == "LEASE-2").Select(x => x.Id).SingleOrDefaultAsync();
        return fixture;
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class TestPunchSource(string key, NormalizedAttendancePunch punch) : IAttendancePunchSource
    {
        public string ProviderKey => key;
        public Task<AttendanceDevicePunchPage> FetchAsync(string? checkpoint, int maximumItems, CancellationToken cancellationToken = default) => Task.FromResult(new AttendanceDevicePunchPage([punch], "checkpoint-1"));
    }
}
