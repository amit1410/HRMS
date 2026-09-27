using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Attendance;
using HRMS.Infrastructure.Sharding;
using HRMS.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;

namespace HRMS.Tests;

/// <summary>
/// Phase 6G worker-scale and production-flow acceptance. The scale case keeps the provider call
/// deterministic and bounded; the E2E case uses the real worker, operations, lease, recovery, and
/// Phase 6F ingestion services.
/// </summary>
public sealed class AttendanceDeviceWorkerScaleAndE2ETests
{
    private static readonly string[] WorkerScaleManifest =
    [
        "1000-device fixture", "bounded discovery", "active Pull filtering", "inactive/non-Pull exclusion",
        "lease contention", "poison isolation", "tenant fairness", "retry eligibility",
        "cancellation under scale", "lease cleanup", "tenant isolation", "query-shape/bounded-memory"
    ];

    private static readonly string[] ProductionE2EManifest =
    [
        "primary worker path", "replay/idempotency", "restart/crash recovery", "stale-owner denial after reclaim",
        "transient retry success", "retry exhaustion", "shared multi-tenant E2E", "finalized-period late event",
        "manual sync vs worker", "operational success status", "operational failure status", "health diagnostics"
    ];

    [Fact]
    public async Task Worker_discovery_excludes_future_retries_server_side_and_pages_before_hydration()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var dueId = Guid.NewGuid();
        var futureId = Guid.NewGuid();
        fixture.Context.AttendanceDevices.AddRange(
            new Domain.Entities.AttendanceDevice { Id = dueId, TenantId = fixture.TenantId, Code = "DUE", Name = "Due", DeviceType = "Terminal", Vendor = "Scale", TimeZoneId = "UTC", ConnectionMode = AttendanceDeviceConnectionMode.Pull, Status = AttendanceDeviceStatus.Active, NextRetryAtUtc = now.AddMinutes(-1) },
            new Domain.Entities.AttendanceDevice { Id = futureId, TenantId = fixture.TenantId, Code = "FUTURE", Name = "Future", DeviceType = "Terminal", Vendor = "Scale", TimeZoneId = "UTC", ConnectionMode = AttendanceDeviceConnectionMode.Pull, Status = AttendanceDeviceStatus.Active, NextRetryAtUtc = now.AddMinutes(5) });
        await fixture.Context.SaveChangesAsync();

        var capture = new SqliteQueryCapture();
        await using var db = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId), capture);
        var operations = new AttendanceDeviceOperationsService(db, new TestTenantContext(fixture.TenantId), new NoopIngestion(), []);
        var result = await operations.GetDevicesAsync(new AttendanceDeviceQuery(Page: 1, PageSize: 1, Status: AttendanceDeviceStatus.Active, ConnectionMode: AttendanceDeviceConnectionMode.Pull, EligibleAtUtc: now));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal(dueId, result.Value.Items.Single().Id);
        var sql = string.Join("\n", capture.Commands);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(futureId.ToString("N"), sql, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Production_worker_cycle_is_deterministic_and_does_not_starve_small_tenants()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenants = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await using (var catalog = database.CreateCatalogContext())
        {
            catalog.Tenants.AddRange(
                new Domain.Entities.Tenant { Id = tenants[0], TenantCode = "A-LARGE", TenantName = "A", Host = "a.test", ShardKey = "a" },
                new Domain.Entities.Tenant { Id = tenants[1], TenantCode = "B-SMALL", TenantName = "B", Host = "b.test", ShardKey = "b" },
                new Domain.Entities.Tenant { Id = tenants[2], TenantCode = "C-SMALL", TenantName = "C", Host = "c.test", ShardKey = "c" });
            await catalog.SaveChangesAsync();
        }

        var context = new TestExecutionContext(null);
        var operations = new FairOperations(context, tenants[0], tenants[1], tenants[2]);
        var execution = new FairExecution(context, operations);
        var serviceProvider = new CycleProvider(database.CreateCatalogContext(), new TestShardContext(), context, operations, execution);
        var worker = new AttendanceDevicePullWorker(new CycleScopeFactory(serviceProvider),
            Options.Create(new AttendanceDeviceWorkerOptions { Enabled = true, DevicePageSize = 10, MaxDevicesPerCycle = 10 }),
            NullLogger<AttendanceDevicePullWorker>.Instance);

        var process = typeof(AttendanceDevicePullWorker).GetMethod("ProcessTenantsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)process.Invoke(worker, [CancellationToken.None])!;

        var firstB = execution.Processed.IndexOf(tenants[1]);
        var firstC = execution.Processed.IndexOf(tenants[2]);
        var firstA = execution.Processed.IndexOf(tenants[0]);
        Assert.True(firstA >= 0 && firstB > firstA && firstC > firstA);
        Assert.True(execution.Processed.Count(x => x == tenants[0]) < 1000);
        Assert.All(operations.Queries, q => Assert.Equal(10, q.PageSize));
    }

    [Fact]
    public async Task Production_worker_cancellation_stops_before_starting_more_devices()
    {
        var devices = Enumerable.Range(0, 100).Select(i => new AttendanceDeviceDto(Guid.NewGuid(), $"C-{i:D3}", "Cancel", "Terminal", "Healthy", null, null, "UTC", AttendanceDeviceConnectionMode.Pull, AttendanceDeviceStatus.Active, null, null)).ToArray();
        var operations = new ScaleOperations(devices, 10);
        var execution = new CancellationExecution();
        var worker = CreateWorker(operations, execution, new AttendanceDeviceWorkerOptions { Enabled = true, DevicePageSize = 10, MaxDevicesPerCycle = 100 });
        using var cancellation = new CancellationTokenSource();
        var cycle = worker.ProcessTenantAsync(new ShardDescriptor(Guid.NewGuid(), "C", "c.test", "c", TenantStatus.Active), cancellation.Token);
        await execution.Started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cycle);
        Assert.Equal(1, execution.StartedCount);
        Assert.Equal(0, execution.Active);
        Assert.Equal(0, execution.FalseSuccesses);
    }

    [Fact]
    public async Task Production_worker_transient_retry_succeeds_and_health_reflects_success()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var source = new FlakySource("RETRY", 1, Punch("retry-worker"));
        var options = new AttendanceDeviceWorkerOptions { Enabled = true, DevicePageSize = 1, MaxDevicesPerCycle = 1, MaxRetries = 1, RetryBaseDelaySeconds = 1, RetryMaxDelaySeconds = 1 };
        var setup = await CreateRealWorkerAsync(fixture, "RETRY", source, options);
        await setup.Worker.ProcessTenantAsync(setup.Descriptor);
        Assert.Equal(2, source.Calls);
        Assert.True(setup.Recording.Results.Last().Succeeded);
        Assert.Equal(1, await fixture.Context.AttendancePunches.CountAsync());
        Assert.Equal(0, await fixture.Context.AttendanceDeviceSyncLeases.CountAsync(x => x.LeaseToken != null));
        var health = await new AttendanceDeviceHealthService(fixture.Context, fixture.TenantContext, options, setup.Clock).GetAsync();
        Assert.True(health.Succeeded && health.Value!.WorkerEnabled);
        Assert.Equal(0, health.Value.ActiveLeases);
    }

    [Fact]
    public async Task Production_worker_retry_exhaustion_fails_without_checkpoint_or_lock()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var source = new AlwaysFailSource("EXHAUST");
        var options = new AttendanceDeviceWorkerOptions { Enabled = true, DevicePageSize = 1, MaxDevicesPerCycle = 1, MaxRetries = 1, RetryBaseDelaySeconds = 1, RetryMaxDelaySeconds = 1 };
        var setup = await CreateRealWorkerAsync(fixture, "EXHAUST", source, options);
        await setup.Worker.ProcessTenantAsync(setup.Descriptor);
        Assert.Equal(2, source.Calls);
        Assert.False(setup.Recording.Results.Last().Succeeded);
        Assert.Equal(0, await fixture.Context.AttendancePunches.CountAsync());
        Assert.Null(await fixture.Context.AttendanceDevices.Where(x => x.Id == setup.DeviceId).Select(x => x.LastSuccessfulCheckpoint).SingleAsync());
        Assert.Equal(0, await fixture.Context.AttendanceDeviceSyncLeases.CountAsync(x => x.LeaseToken != null));
        Assert.True(await fixture.Context.AttendanceDeviceSyncRuns.AnyAsync(x => x.Status == AttendanceDeviceSyncStatus.Failed));
        var health = await new AttendanceDeviceHealthService(fixture.Context, fixture.TenantContext, options, setup.Clock).GetAsync();
        Assert.True(health.Value!.RecentFailures > 0);
    }

    [Fact]
    public async Task Production_worker_restart_reconciles_stale_run_and_replays_safely()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var source = new ReplaySource("RESTART", Punch("restart-worker"));
        var options = new AttendanceDeviceWorkerOptions { Enabled = true, DevicePageSize = 1, MaxDevicesPerCycle = 1, StaleRunThresholdSeconds = 300 };
        var setup = await CreateRealWorkerAsync(fixture, "RESTART", source, options);
        fixture.Context.AttendanceDeviceSyncRuns.Add(new Domain.Entities.AttendanceDeviceSyncRun { Id = Guid.NewGuid(), TenantId = fixture.TenantId, AttendanceDeviceId = setup.DeviceId, Source = "worker", Status = AttendanceDeviceSyncStatus.Running, StartedAtUtc = setup.Clock.GetUtcNow().UtcDateTime.AddMinutes(-20) });
        fixture.Context.AttendanceDeviceSyncLeases.Add(new Domain.Entities.AttendanceDeviceSyncLease { Id = Guid.NewGuid(), TenantId = fixture.TenantId, AttendanceDeviceId = setup.DeviceId, LeaseOwner = "crashed", LeaseToken = Guid.NewGuid(), ClaimedAtUtc = setup.Clock.GetUtcNow().UtcDateTime.AddMinutes(-20), LeaseExpiresAtUtc = setup.Clock.GetUtcNow().UtcDateTime.AddMinutes(-10) });
        await fixture.Context.SaveChangesAsync();
        await setup.Worker.ProcessTenantAsync(setup.Descriptor);
        Assert.True(await fixture.Context.AttendanceDeviceSyncRuns.AnyAsync(x => x.Status == AttendanceDeviceSyncStatus.Failed && x.FailureCode == "WorkerLeaseExpired"));
        Assert.True(await fixture.Context.AttendanceDeviceSyncRuns.AnyAsync(x => x.Status == AttendanceDeviceSyncStatus.Succeeded));
        Assert.Equal(1, await fixture.Context.AttendancePunches.CountAsync());
        Assert.Equal(0, await fixture.Context.AttendanceDeviceSyncLeases.CountAsync(x => x.LeaseToken != null));
    }

    [Fact]
    public async Task Production_worker_multi_tenant_same_external_event_is_independent()
    {
        using var first = await AttendanceTestFixture.CreateAsync();
        using var second = await AttendanceTestFixture.CreateAsync();
        var options = new AttendanceDeviceWorkerOptions { Enabled = true, DevicePageSize = 1, MaxDevicesPerCycle = 1 };
        var a = await CreateRealWorkerAsync(first, "TENANT-A", new ReplaySource("TENANT-A", Punch("same-event")), options);
        var b = await CreateRealWorkerAsync(second, "TENANT-B", new ReplaySource("TENANT-B", Punch("same-event")), options);
        await a.Worker.ProcessTenantAsync(a.Descriptor);
        await b.Worker.ProcessTenantAsync(b.Descriptor);
        Assert.Equal(1, await first.Context.AttendancePunches.CountAsync());
        Assert.Equal(1, await second.Context.AttendancePunches.CountAsync());
        Assert.Equal(0, await first.Context.AttendanceDeviceSyncLeases.CountAsync(x => x.LeaseToken != null));
        Assert.Equal(0, await second.Context.AttendanceDeviceSyncLeases.CountAsync(x => x.LeaseToken != null));
    }

    [Fact]
    public async Task Production_worker_shared_database_multi_tenant_events_and_leases_are_isolated()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var a = await SeedSharedTenantAsync(database, tenantA, "A", "shared-event");
        var b = await SeedSharedTenantAsync(database, tenantB, "B", "shared-event");

        await a.Worker.ProcessTenantAsync(a.Descriptor);
        await b.Worker.ProcessTenantAsync(b.Descriptor);

        var runsA = await a.Operations.GetSyncRunsAsync(new AttendanceDeviceSyncRunQuery());
        var runsB = await b.Operations.GetSyncRunsAsync(new AttendanceDeviceSyncRunQuery());
        var punchCountA = await a.Context.AttendancePunches.CountAsync(x => x.TenantId == tenantA);
        Assert.True(punchCountA == 1, $"A punches={punchCountA}; runs={runsA.Message}");
        Assert.True(runsA.Succeeded && runsA.Value!.TotalCount == 1 && runsA.Value.Items.Single().Status == AttendanceDeviceSyncStatus.Succeeded, runsA.Message);
        Assert.Equal(1, await b.Context.AttendancePunches.CountAsync(x => x.TenantId == tenantB));
        Assert.True(runsB.Succeeded && runsB.Value!.TotalCount == 1 && runsB.Value.Items.Single().Status == AttendanceDeviceSyncStatus.Succeeded, runsB.Message);
        Assert.Equal(0, await a.Context.AttendancePunches.CountAsync(x => x.TenantId == tenantB));
        Assert.Equal(0, await b.Context.AttendancePunches.CountAsync(x => x.TenantId == tenantA));
        Assert.Equal(0, await a.Context.AttendanceDeviceSyncLeases.CountAsync(x => x.LeaseToken != null));
        Assert.Equal(0, await b.Context.AttendanceDeviceSyncLeases.CountAsync(x => x.LeaseToken != null));
    }

    [Fact]
    public async Task Production_worker_manual_sync_conflicts_with_existing_lease_then_recovers_after_release()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var source = new ReplaySource("MANUAL", Punch("manual-worker"));
        var options = new AttendanceDeviceWorkerOptions { Enabled = true, DevicePageSize = 1, MaxDevicesPerCycle = 1 };
        var setup = await CreateRealWorkerAsync(fixture, "MANUAL", source, options);
        var lease = new AttendanceDeviceLeaseService(fixture.Context, fixture.TenantContext, setup.Clock, options);
        var claimed = await lease.TryAcquireAsync(setup.DeviceId, "manual");
        Assert.True(claimed.Acquired);
        await setup.Worker.ProcessTenantAsync(setup.Descriptor);
        Assert.False(setup.Recording.Results.Last().Succeeded);
        Assert.Equal(0, await fixture.Context.AttendancePunches.CountAsync());
        Assert.True(await lease.ReleaseAsync(setup.DeviceId, claimed.Lease!.LeaseToken));
        await setup.Worker.ProcessTenantAsync(setup.Descriptor);
        Assert.Equal(1, await fixture.Context.AttendancePunches.CountAsync());
    }

    [Fact]
    public async Task Production_worker_finalized_period_retains_late_issue_without_reopen()
    {
        await using var finalized = await Phase6BAcceptanceData.FinalizedAsync();
        var periodId = finalized.PeriodId;
        var deviceId = Guid.NewGuid();
        finalized.Db.AttendanceDeviceEmployeeMappings.Add(new Domain.Entities.AttendanceDeviceEmployeeMapping
        {
            Id = Guid.NewGuid(), TenantId = finalized.TenantId, AttendanceDeviceId = deviceId,
            ExternalEmployeeIdentifier = "EXT-1", EmployeeId = finalized.EmployeeId,
            EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active
        });
        finalized.Db.AttendanceDevices.Add(new Domain.Entities.AttendanceDevice
        {
            Id = deviceId, TenantId = finalized.TenantId, Code = "6G-FINAL", Name = "Finalized", DeviceType = "Terminal",
            Vendor = "FINAL", TimeZoneId = "UTC", ConnectionMode = AttendanceDeviceConnectionMode.Pull, Status = AttendanceDeviceStatus.Active
        });
        await finalized.Db.SaveChangesAsync();
        var source = new ReplaySource("FINAL", new NormalizedAttendancePunch("late-final", "EXT-1", new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In));
        var options = new AttendanceDeviceWorkerOptions { Enabled = true, DevicePageSize = 1, MaxDevicesPerCycle = 1 };
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero));
        var ingestion = CreateIngestion(finalized.Db, finalized.Scope, clock);
        var lease = new AttendanceDeviceLeaseService(finalized.Db, finalized.Scope, clock, options);
        var operations = new AttendanceDeviceOperationsService(finalized.Db, finalized.Scope, ingestion, [source], lease, options);
        var recovery = new AttendanceDeviceSyncRecoveryService(operations, finalized.Db, finalized.Scope, options, clock, NullLogger<AttendanceDeviceSyncRecoveryService>.Instance);
        var recording = new RecordingRecovery(recovery);
        var worker = CreateWorker(operations, recording, options, finalized.TenantId, finalized.Db, new TestExecutionContext(finalized.TenantId));
        var descriptor = new ShardDescriptor(finalized.TenantId, "FINAL", "final.test", "final", TenantStatus.Active);
        await worker.ProcessTenantAsync(descriptor);
        Assert.Equal(0, await finalized.Db.AttendancePunches.CountAsync());
        Assert.Equal(1, await finalized.Db.AttendanceDeviceIngestionEvents.CountAsync(x => x.Status == AttendanceDeviceIngestionStatus.RequiresPeriodReopen));
        Assert.Equal(0, await finalized.Db.AttendanceDeviceSyncLeases.CountAsync(x => x.LeaseToken != null));
        Assert.Equal(AttendancePeriodStatus.Closed, await finalized.Db.AttendancePeriods.Where(x => x.Id == periodId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Worker_scale_pages_active_pull_devices_and_isolates_poison_devices()
    {
        const int tenantCount = 10;
        const int deviceCount = 1000;
        const int pageSize = 50;
        var devices = Enumerable.Range(0, deviceCount).Select(i => new AttendanceDeviceDto(
            Guid.NewGuid(), $"S-{i:D4}", $"Scale {i:D4}", "Terminal",
            i % 17 == 0 ? "Poison" : "Healthy", null, null, "UTC",
            i < 800 ? AttendanceDeviceConnectionMode.Pull : i < 900 ? AttendanceDeviceConnectionMode.Push :
                AttendanceDeviceConnectionMode.FileImport,
            i is >= 700 and < 800 ? AttendanceDeviceStatus.Inactive : AttendanceDeviceStatus.Active,
            null, null)).ToArray();
        var poison = devices.Where((x, i) => x.Vendor == "Poison").Select(x => x.Id).ToHashSet();
        var contended = devices.Where((x, i) => x.ConnectionMode == AttendanceDeviceConnectionMode.Pull && i % 23 == 0).Select(x => x.Id).ToHashSet();
        var operations = new ScaleOperations(devices, pageSize);
        var execution = new ScaleExecution(poison, contended);
        var options = new AttendanceDeviceWorkerOptions { Enabled = true, DevicePageSize = pageSize, MaxDevicesPerCycle = 1000 };
        var worker = CreateWorker(operations, execution, options);

        var tenantsProcessed = 0;
        for (var tenant = 0; tenant < tenantCount; tenant++)
        {
            await worker.ProcessTenantAsync(new ShardDescriptor(Guid.NewGuid(), $"T-{tenant}", $"t{tenant}.test", $"t{tenant}", TenantStatus.Active), CancellationToken.None);
            tenantsProcessed++;
        }

        Assert.Equal(tenantCount, tenantsProcessed);
        Assert.Equal(700, execution.Processed.Count);
        Assert.Equal(tenantCount * ((700 + pageSize - 1) / pageSize), operations.Queries.Count);
        Assert.All(operations.Queries, q => Assert.Equal(pageSize, q.PageSize));
        Assert.Equal(0, execution.Processed.Intersect(devices.Where(x => x.Status != AttendanceDeviceStatus.Active || x.ConnectionMode != AttendanceDeviceConnectionMode.Pull).Select(x => x.Id)).Count());
        Assert.True(execution.MaxConcurrent == 1, $"Observed concurrency was {execution.MaxConcurrent}.");
        Assert.Equal(0, execution.UnboundedTasks);
        Assert.Equal(42 * tenantCount, execution.FailedDevices);
        Assert.Equal(29 * tenantCount, execution.ContendedDevices);
        Assert.Equal(0, execution.ActiveLeasesAfterCycle);
    }

    [Fact]
    public async Task Production_worker_e2e_replay_is_idempotent_and_releases_lease()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("6G-WORKER", isDefault: true);
        var deviceId = Guid.NewGuid();
        fixture.Context.AttendanceDevices.Add(new Domain.Entities.AttendanceDevice
        {
            Id = deviceId, TenantId = fixture.TenantId, Code = "6G-WORKER-1", Name = "6G Worker",
            DeviceType = "Terminal", Vendor = "E2E", TimeZoneId = "UTC",
            ConnectionMode = AttendanceDeviceConnectionMode.Pull, Status = AttendanceDeviceStatus.Active
        });
        fixture.Context.AttendanceDeviceEmployeeMappings.Add(new Domain.Entities.AttendanceDeviceEmployeeMapping
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, AttendanceDeviceId = deviceId,
            ExternalEmployeeIdentifier = "EXT-1", EmployeeId = fixture.EmployeeId,
            EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active
        });
        await fixture.Context.SaveChangesAsync();

        var options = new AttendanceDeviceWorkerOptions
        {
            Enabled = true, DevicePageSize = 1, MaxDevicesPerCycle = 1, MaxRetries = 0,
            LeaseDurationSeconds = 120, HeartbeatIntervalSeconds = 30
        };
        var source = new ReplaySource("E2E", new NormalizedAttendancePunch(
            "e2e-event-1", "EXT-1", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero),
            AttendanceDevicePunchDirection.In));
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero));
        var ingestion = CreateIngestion(fixture, clock);
        var lease = new AttendanceDeviceLeaseService(fixture.Context, fixture.TenantContext, clock, options);
        var operations = new AttendanceDeviceOperationsService(fixture.Context, fixture.TenantContext, ingestion, [source], lease, options);
        var recovery = new AttendanceDeviceSyncRecoveryService(operations, fixture.Context, fixture.TenantContext, options,
            clock, NullLogger<AttendanceDeviceSyncRecoveryService>.Instance);
        var recording = new RecordingRecovery(recovery);
        var worker = CreateWorker(operations, recording, options, fixture.TenantId, fixture.Context, new TestExecutionContext(fixture.TenantId));
        var descriptor = new ShardDescriptor(fixture.TenantId, "E2E", "e2e.test", "e2e", TenantStatus.Active);

        await worker.ProcessTenantAsync(descriptor);
        await worker.ProcessTenantAsync(descriptor);

        Assert.Equal(2, source.Calls);
        Assert.All(recording.Results, result => Assert.True(result.Succeeded, result.Message));
        Assert.Equal(1, await fixture.Context.AttendancePunches.CountAsync());
        Assert.Equal(0, await fixture.Context.AttendancePunches.GroupBy(x => x.ExternalPunchId).CountAsync(x => x.Count() > 1));
        Assert.Equal(2, await fixture.Context.AttendanceDeviceSyncRuns.CountAsync(x => x.Status == AttendanceDeviceSyncStatus.Succeeded));
        Assert.Equal(0, await fixture.Context.AttendanceDeviceSyncLeases.CountAsync(x => x.LeaseToken != null));
        Assert.Equal("checkpoint-e2e-event-1", await fixture.Context.AttendanceDevices.Where(x => x.Id == deviceId).Select(x => x.LastSuccessfulCheckpoint).SingleAsync());
    }

    private static AttendanceDevicePullWorker CreateWorker(
        IAttendanceDeviceOperationsService operations,
        IAttendanceDeviceSyncExecutionService execution,
        AttendanceDeviceWorkerOptions options,
        Guid? tenantId = null,
        IHrmsDbContext? db = null,
        ITenantExecutionContext? tenant = null)
    {
        var shard = new TestShardContext();
        var executionContext = tenant ?? new TestExecutionContext(tenantId);
        var recovery = execution as IAttendanceDeviceRecoveryService ?? new NoopRecovery();
        var provider = new TestServiceProvider(shard, executionContext, operations, execution, recovery, db);
        return new AttendanceDevicePullWorker(new TestScopeFactory(provider), Options.Create(options), NullLogger<AttendanceDevicePullWorker>.Instance);
    }

    private static AttendanceDeviceIntegrationService CreateIngestion(AttendanceTestFixture f, TimeProvider clock)
    {
        var punch = new AttendancePunchIngestionService(f.Context, f.TenantContext, f.CalendarService,
            new AttendanceBusinessDateResolver(), new AttendanceBusinessTimeZoneProvider(), clock);
        return new AttendanceDeviceIntegrationService(f.Context, f.TenantContext, punch,
            new AttendanceDayProcessor(f.Context, f.TenantContext, f.CalendarService, clock), clock);
    }

    private static AttendanceDeviceIntegrationService CreateIngestion(HRMS.Infrastructure.Persistence.HrmsDbContext db, TestTenantContext tenant, TimeProvider clock)
    {
        var calendar = new AttendanceFoundationService(db, tenant, new EffectiveEmploymentResolver(db, tenant));
        var punch = new AttendancePunchIngestionService(db, tenant, calendar,
            new AttendanceBusinessDateResolver(), new AttendanceBusinessTimeZoneProvider(), clock);
        return new AttendanceDeviceIntegrationService(db, tenant, punch,
            new AttendanceDayProcessor(db, tenant, calendar, clock), clock);
    }

    private static async Task<RealWorker> CreateRealWorkerAsync(AttendanceTestFixture fixture, string vendor, IAttendancePunchSource source, AttendanceDeviceWorkerOptions options)
    {
        await fixture.AddShiftAsync($"6G-{vendor}", isDefault: true);
        var deviceId = Guid.NewGuid();
        fixture.Context.AttendanceDevices.Add(new Domain.Entities.AttendanceDevice { Id = deviceId, TenantId = fixture.TenantId, Code = $"6G-{vendor}", Name = vendor, DeviceType = "Terminal", Vendor = vendor, TimeZoneId = "UTC", ConnectionMode = AttendanceDeviceConnectionMode.Pull, Status = AttendanceDeviceStatus.Active });
        fixture.Context.AttendanceDeviceEmployeeMappings.Add(new Domain.Entities.AttendanceDeviceEmployeeMapping { Id = Guid.NewGuid(), TenantId = fixture.TenantId, AttendanceDeviceId = deviceId, ExternalEmployeeIdentifier = "EXT-1", EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active });
        await fixture.Context.SaveChangesAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero));
        var ingestion = CreateIngestion(fixture, clock);
        var lease = new AttendanceDeviceLeaseService(fixture.Context, fixture.TenantContext, clock, options);
        var operations = new AttendanceDeviceOperationsService(fixture.Context, fixture.TenantContext, ingestion, [source], lease, options);
        var recovery = new AttendanceDeviceSyncRecoveryService(operations, fixture.Context, fixture.TenantContext, options, clock, NullLogger<AttendanceDeviceSyncRecoveryService>.Instance);
        var recording = new RecordingRecovery(recovery);
        return new(deviceId, new AttendanceDevicePullWorker(new TestScopeFactory(new TestServiceProvider(new TestShardContext(), new TestExecutionContext(fixture.TenantId), operations, recording, recording, fixture.Context)), Options.Create(options), NullLogger<AttendanceDevicePullWorker>.Instance), recording, clock, new ShardDescriptor(fixture.TenantId, "E2E", "e2e.test", "e2e", TenantStatus.Active));
    }

    private static async Task<SharedTenantWorker> SeedSharedTenantAsync(SqliteInMemoryDatabase database, Guid tenantId, string suffix, string eventId)
    {
        var tenant = new TestTenantContext(tenantId, Guid.NewGuid());
        var context = database.CreateContext(tenant);
        var employeeId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        context.Tenants.Add(new Domain.Entities.Tenant { Id = tenantId, TenantCode = $"SHARED-{suffix}", TenantName = suffix, Host = $"{suffix}.test", ShardKey = suffix });
        context.Employees.Add(new Domain.Entities.Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"SH-{suffix}", FirstName = suffix, LastName = "Worker", Email = $"{suffix}@test.local", DateOfJoining = new(2026, 1, 1) });
        context.EmployeeEmploymentHistory.Add(new Domain.Entities.EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active });
        context.Shifts.Add(new Domain.Entities.Shift { Id = Guid.NewGuid(), TenantId = tenantId, ShiftCode = $"SH-{suffix}", ShiftName = suffix, IsDefault = true, IsActive = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 });
        context.AttendanceDevices.Add(new Domain.Entities.AttendanceDevice { Id = deviceId, TenantId = tenantId, Code = $"SH-{suffix}", Name = suffix, DeviceType = "Terminal", Vendor = $"SHARED-{suffix}", TimeZoneId = "UTC", ConnectionMode = AttendanceDeviceConnectionMode.Pull, Status = AttendanceDeviceStatus.Active });
        context.AttendanceDeviceEmployeeMappings.Add(new Domain.Entities.AttendanceDeviceEmployeeMapping { Id = Guid.NewGuid(), TenantId = tenantId, AttendanceDeviceId = deviceId, ExternalEmployeeIdentifier = $"EXT-{suffix}", EmployeeId = employeeId, EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active });
        await context.SaveChangesAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero));
        var source = new ReplaySource($"SHARED-{suffix}", new NormalizedAttendancePunch(eventId, $"EXT-{suffix}", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In));
        var options = new AttendanceDeviceWorkerOptions { Enabled = true, DevicePageSize = 1, MaxDevicesPerCycle = 1 };
        var ingestion = CreateIngestion(context, tenant, clock);
        var lease = new AttendanceDeviceLeaseService(context, tenant, clock, options);
        var operations = new AttendanceDeviceOperationsService(context, tenant, ingestion, [source], lease, options);
        var recovery = new AttendanceDeviceSyncRecoveryService(operations, context, tenant, options, clock, NullLogger<AttendanceDeviceSyncRecoveryService>.Instance);
        var recording = new RecordingRecovery(recovery);
        var worker = CreateWorker(operations, recording, options, tenantId, context, new TestExecutionContext(tenantId));
        return new(context, operations, worker, new ShardDescriptor(tenantId, $"SHARED-{suffix}", $"{suffix}.test", suffix, TenantStatus.Active));
    }

    private sealed record SharedTenantWorker(HRMS.Infrastructure.Persistence.HrmsDbContext Context, IAttendanceDeviceOperationsService Operations, AttendanceDevicePullWorker Worker, ShardDescriptor Descriptor);

    private sealed record RealWorker(Guid DeviceId, AttendanceDevicePullWorker Worker, RecordingRecovery Recording, FixedClock Clock, ShardDescriptor Descriptor);

    private sealed class ScaleOperations(AttendanceDeviceDto[] all, int pageSize) : IAttendanceDeviceOperationsService
    {
        public List<AttendanceDeviceQuery> Queries { get; } = [];
        public Task<Result<PagedResult<AttendanceDeviceDto>>> GetDevicesAsync(AttendanceDeviceQuery query, CancellationToken ct = default)
        {
            Queries.Add(query);
            var eligible = all.Where(x => x.Status == AttendanceDeviceStatus.Active && x.ConnectionMode == AttendanceDeviceConnectionMode.Pull).OrderBy(x => x.Code).ToArray();
            var items = eligible.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray();
            return Task.FromResult(Result<PagedResult<AttendanceDeviceDto>>.Success(new(items, query.Page, pageSize, eligible.Length)));
        }
        public Task<Result<AttendanceDeviceBatchResult>> SyncNowAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Result<AttendanceDeviceBatchResult>.Conflict("not used"));
        public Task<Result<AttendanceDeviceDto>> GetDeviceAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttendanceDeviceDto>> CreateDeviceAsync(AttendanceDeviceRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttendanceDeviceDto>> UpdateDeviceAsync(Guid id, AttendanceDeviceRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttendanceDeviceDto>> SetDeviceStatusAsync(Guid id, AttendanceDeviceStatus status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PagedResult<AttendanceDeviceMappingDto>>> GetMappingsAsync(AttendanceDeviceMappingQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttendanceDeviceMappingDto>> CreateMappingAsync(AttendanceDeviceMappingRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttendanceDeviceMappingDto>> UpdateMappingAsync(Guid id, AttendanceDeviceMappingRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> DeactivateMappingAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PagedResult<AttendanceDeviceIssueDto>>> GetIssuesAsync(AttendanceDeviceIssueQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttendanceDeviceBatchResult>> ReprocessIssueAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PagedResult<AttendanceDeviceSyncRunDto>>> GetSyncRunsAsync(AttendanceDeviceSyncRunQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PagedResult<AttendanceDeviceAuditDto>>> GetAuditHistoryAsync(AttendanceDeviceAuditQuery query, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ScaleExecution(HashSet<Guid> poison, HashSet<Guid> contended) : IAttendanceDeviceSyncExecutionService, IAttendanceDeviceRecoveryService
    {
        private int _active;
        public HashSet<Guid> Processed { get; } = [];
        public int MaxConcurrent { get; private set; }
        public int UnboundedTasks { get; private set; }
        public int FailedDevices { get; private set; }
        public int ContendedDevices { get; private set; }
        public int ActiveLeasesAfterCycle => _active;
        public Task<int> ReconcileStaleRunsAsync(CancellationToken ct = default) => Task.FromResult(0);
        public async Task<Result<AttendanceDeviceBatchResult>> ExecuteAsync(Guid deviceId, CancellationToken ct = default)
        {
            var active = Interlocked.Increment(ref _active);
            MaxConcurrent = Math.Max(MaxConcurrent, active);
            if (active > 1) UnboundedTasks++;
            await Task.Yield();
            Processed.Add(deviceId);
            Interlocked.Decrement(ref _active);
            if (poison.Contains(deviceId)) { FailedDevices++; return Result<AttendanceDeviceBatchResult>.Failure(ResultStatus.Conflict, "DeviceSyncFailure"); }
            if (contended.Contains(deviceId)) { ContendedDevices++; return Result<AttendanceDeviceBatchResult>.Conflict("DeviceSyncAlreadyRunning"); }
            return Result<AttendanceDeviceBatchResult>.Success(new(Guid.NewGuid(), 1, 1, 0, 0, 0, [], "checkpoint"));
        }
    }

    private sealed class CancellationExecution : IAttendanceDeviceSyncExecutionService, IAttendanceDeviceRecoveryService
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StartedCount { get; private set; }
        public int Active { get; private set; }
        public int FalseSuccesses { get; private set; }
        public Task<int> ReconcileStaleRunsAsync(CancellationToken ct = default) => Task.FromResult(0);
        public async Task<Result<AttendanceDeviceBatchResult>> ExecuteAsync(Guid deviceId, CancellationToken ct = default)
        {
            StartedCount++; Active++; Started.TrySetResult(true);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Result<AttendanceDeviceBatchResult>.Success(new(Guid.NewGuid(), 0, 0, 0, 0, 0, [], null)); }
            finally { Active--; }
        }
    }

    private sealed class FairOperations(TestExecutionContext tenant, Guid large, Guid smallB, Guid smallC) : IAttendanceDeviceOperationsService
    {
        public int CatalogQueries { get; set; }
        public List<AttendanceDeviceQuery> Queries { get; } = [];
        public Task<Result<PagedResult<AttendanceDeviceDto>>> GetDevicesAsync(AttendanceDeviceQuery query, CancellationToken ct = default)
        {
            Queries.Add(query);
            var count = tenant.TenantId == large ? 1000 : 1;
            var items = Enumerable.Range(0, Math.Min(query.PageSize, Math.Max(0, count - ((query.Page - 1) * query.PageSize))))
                .Select(i => new AttendanceDeviceDto(Guid.NewGuid(), $"{tenant.TenantId:N}-{query.Page}-{i}", "Fair", "Terminal", "Healthy", null, null, "UTC", AttendanceDeviceConnectionMode.Pull, AttendanceDeviceStatus.Active, null, null)).ToArray();
            return Task.FromResult(Result<PagedResult<AttendanceDeviceDto>>.Success(new(items, query.Page, query.PageSize, count)));
        }
        public Task<Result<AttendanceDeviceBatchResult>> SyncNowAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttendanceDeviceDto>> GetDeviceAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttendanceDeviceDto>> CreateDeviceAsync(AttendanceDeviceRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttendanceDeviceDto>> UpdateDeviceAsync(Guid id, AttendanceDeviceRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttendanceDeviceDto>> SetDeviceStatusAsync(Guid id, AttendanceDeviceStatus status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PagedResult<AttendanceDeviceMappingDto>>> GetMappingsAsync(AttendanceDeviceMappingQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttendanceDeviceMappingDto>> CreateMappingAsync(AttendanceDeviceMappingRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttendanceDeviceMappingDto>> UpdateMappingAsync(Guid id, AttendanceDeviceMappingRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> DeactivateMappingAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PagedResult<AttendanceDeviceIssueDto>>> GetIssuesAsync(AttendanceDeviceIssueQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttendanceDeviceBatchResult>> ReprocessIssueAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PagedResult<AttendanceDeviceSyncRunDto>>> GetSyncRunsAsync(AttendanceDeviceSyncRunQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PagedResult<AttendanceDeviceAuditDto>>> GetAuditHistoryAsync(AttendanceDeviceAuditQuery query, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FairExecution(TestExecutionContext tenant, FairOperations operations) : IAttendanceDeviceSyncExecutionService, IAttendanceDeviceRecoveryService
    {
        public List<Guid> Processed { get; } = [];
        public Task<int> ReconcileStaleRunsAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<Result<AttendanceDeviceBatchResult>> ExecuteAsync(Guid deviceId, CancellationToken ct = default)
        {
            Processed.Add(tenant.TenantId!.Value);
            return Task.FromResult(Result<AttendanceDeviceBatchResult>.Success(new(Guid.NewGuid(), 1, 1, 0, 0, 0, [], "p")));
        }
    }

    private sealed class CycleScopeFactory(IServiceProvider provider) : IServiceScopeFactory { public IServiceScope CreateScope() => new TestScope(provider); }
    private sealed class CycleProvider(HRMS.Infrastructure.Persistence.Catalog.HrmsCatalogDbContext catalog, IShardContext shard, TestExecutionContext tenant, FairOperations operations, FairExecution execution) : IServiceProvider
    {
        public object? GetService(Type t) => t == typeof(IHrmsCatalogDbContext) ? catalog : t == typeof(IShardContext) ? shard : t == typeof(ITenantExecutionContext) ? tenant : t == typeof(IAttendanceDeviceOperationsService) ? operations : t == typeof(IAttendanceDeviceSyncExecutionService) ? execution : t == typeof(IAttendanceDeviceRecoveryService) ? execution : null;
    }

    private sealed class ReplaySource(string key, NormalizedAttendancePunch punch) : IAttendancePunchSource
    {
        public int Calls { get; private set; }
        public string ProviderKey => key;
        public Task<AttendanceDevicePunchPage> FetchAsync(string? checkpoint, int maximumItems, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new AttendanceDevicePunchPage([punch], $"checkpoint-{punch.ExternalEventId}"));
        }
    }

    private sealed class FlakySource(string key, int failures, NormalizedAttendancePunch punch) : IAttendancePunchSource
    {
        private int _calls;
        public int Calls => _calls;
        public string ProviderKey => key;
        public Task<AttendanceDevicePunchPage> FetchAsync(string? checkpoint, int maximumItems, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) <= failures) throw new TimeoutException("transient provider failure");
            return Task.FromResult(new AttendanceDevicePunchPage([punch], $"checkpoint-{punch.ExternalEventId}"));
        }
    }

    private sealed class AlwaysFailSource(string key) : IAttendancePunchSource
    {
        private int _calls;
        public int Calls => _calls;
        public string ProviderKey => key;
        public Task<AttendanceDevicePunchPage> FetchAsync(string? checkpoint, int maximumItems, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            throw new TimeoutException("transient provider failure");
        }
    }

    private static NormalizedAttendancePunch Punch(string id) => new(id, "EXT-1", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In);

    private sealed class NoopRecovery : IAttendanceDeviceRecoveryService { public Task<int> ReconcileStaleRunsAsync(CancellationToken ct = default) => Task.FromResult(0); }
    private sealed class NoopIngestion : IAttendanceDeviceIntegrationService
    {
        public Task<Result<AttendanceDeviceBatchResult>> IngestAsync(AttendanceDeviceBatchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<AttendanceDeviceBatchResult>.Conflict("not used"));
    }
    private sealed class SqliteQueryCapture : DbCommandInterceptor
    {
        public ConcurrentBag<string> Commands { get; } = [];
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { Commands.Add(command.CommandText); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) { Commands.Add(command.CommandText); return result; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
    private sealed class RecordingRecovery(AttendanceDeviceSyncRecoveryService inner) : IAttendanceDeviceSyncExecutionService, IAttendanceDeviceRecoveryService
    {
        public List<Result<AttendanceDeviceBatchResult>> Results { get; } = [];
        public async Task<Result<AttendanceDeviceBatchResult>> ExecuteAsync(Guid deviceId, CancellationToken ct = default)
        {
            var result = await inner.ExecuteAsync(deviceId, ct);
            Results.Add(result);
            return result;
        }
        public Task<int> ReconcileStaleRunsAsync(CancellationToken ct = default) => inner.ReconcileStaleRunsAsync(ct);
    }
    private sealed class TestScopeFactory(IServiceProvider provider) : IServiceScopeFactory { public IServiceScope CreateScope() => new TestScope(provider); }
    private sealed class TestScope(IServiceProvider provider) : IServiceScope, IAsyncDisposable { public IServiceProvider ServiceProvider => provider; public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private sealed class TestServiceProvider(IShardContext shard, ITenantExecutionContext tenant, IAttendanceDeviceOperationsService operations, IAttendanceDeviceSyncExecutionService execution, IAttendanceDeviceRecoveryService recovery, IHrmsDbContext? db) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IShardContext) ? shard : serviceType == typeof(ITenantExecutionContext) ? tenant : serviceType == typeof(IAttendanceDeviceOperationsService) ? operations : serviceType == typeof(IAttendanceDeviceSyncExecutionService) ? execution : serviceType == typeof(IAttendanceDeviceRecoveryService) ? recovery : serviceType == typeof(IHrmsDbContext) ? db : null;
    }
    private sealed class TestShardContext : IShardContext { public ShardDescriptor? Current { get; private set; } public bool HasShard => Current is not null; public void Use(ShardDescriptor descriptor) => Current = descriptor; }
    private sealed class TestExecutionContext(Guid? tenantId) : ITenantExecutionContext { public Guid? TenantId { get; private set; } = tenantId; public Guid? UserId => null; public bool HasTenant => TenantId.HasValue; public void Use(Guid id) => TenantId = id; }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
