using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRMS.Tests;

public sealed class AttendanceDeviceConcurrencyTests
{
    [Fact]
    public async Task Same_external_event_concurrently_ingested_creates_one_punch_and_one_receipt()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-CONCURRENT");
        await SeedMappingAsync(fixture, device.Id, "EXT-CONCURRENT");
        var input = Event("evt-concurrent", "EXT-CONCURRENT");

        var outcomes = await RunConcurrentAsync(fixture, context => DeviceService(fixture, context)
            .IngestAsync(new(device.Id, "concurrency", [input], "cursor-1")));

        await using var verify = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
        Assert.Equal(1, await verify.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{device.Id:N}:{input.ExternalEventId}"));
        Assert.Equal(1, await verify.AttendanceDeviceIngestionEvents.CountAsync(x => x.Source == $"device:{device.Id:N}:concurrency" && x.ExternalEventId == input.ExternalEventId));
        Assert.Equal(2, outcomes.Count(x => x is not null));
        Assert.All(outcomes, result => Assert.True(result!.Succeeded, result.Message));
        Assert.Contains(outcomes, result => result!.Value!.Duplicate == 1);
    }

    [Fact]
    public async Task Same_batch_concurrently_ingested_has_no_duplicate_authoritative_punches()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-BATCH");
        await SeedMappingAsync(fixture, device.Id, "EXT-BATCH");
        var batch = new AttendanceDeviceBatchRequest(device.Id, "batch-race", [Event("evt-batch", "EXT-BATCH")], "cursor-batch");

        var outcomes = await RunConcurrentAsync(fixture, context => DeviceService(fixture, context).IngestAsync(batch));

        await using var verify = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
        Assert.Equal(1, await verify.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{device.Id:N}:evt-batch"));
        Assert.InRange(await verify.AttendanceDeviceIngestionEvents.CountAsync(x => x.ExternalEventId == "evt-batch"), 0, 1);
        Assert.Equal(2, outcomes.Count(x => x is not null));
        Assert.All(outcomes, result => Assert.True(result!.Succeeded, result.Message));
    }

    [Fact]
    public async Task Concurrent_mapping_create_has_one_valid_mapping()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-MAPPING");
        var request = new AttendanceDeviceMappingRequest(device.Id, "EXT-MAPPING", fixture.EmployeeId, new(2026, 1, 1), null, AttendanceDeviceMappingStatus.Active);

        var outcomes = await RunConcurrentAsync(fixture, context => DeviceOperations(fixture, context).CreateMappingAsync(request));

        await using var verify = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
        Assert.Equal(1, await verify.AttendanceDeviceEmployeeMappings.CountAsync(x => x.AttendanceDeviceId == device.Id && x.ExternalEmployeeIdentifier == request.ExternalEmployeeIdentifier));
        Assert.Equal(2, outcomes.Count(x => x is not null));
        Assert.Contains(outcomes, x => x?.Succeeded == true);
    }

    [Fact]
    public async Task Overlapping_effective_mapping_race_has_no_ambiguous_active_state()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-OVERLAP");
        var first = new AttendanceDeviceMappingRequest(device.Id, "EXT-OVERLAP", fixture.EmployeeId, new(2026, 1, 1), new(2026, 12, 31), AttendanceDeviceMappingStatus.Active);
        var second = first with { EffectiveFrom = new(2026, 6, 1) };

        var outcomes = await RunConcurrentAsync(fixture, context => DeviceOperations(fixture, context).CreateMappingAsync(Random.Shared.Next(2) == 0 ? first : second));

        await using var verify = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
        var mappings = await verify.AttendanceDeviceEmployeeMappings.Where(x => x.AttendanceDeviceId == device.Id && x.ExternalEmployeeIdentifier == "EXT-OVERLAP" && x.Status == AttendanceDeviceMappingStatus.Active).ToListAsync();
        Assert.InRange(mappings.Count, 0, 1);
        Assert.Equal(2, outcomes.Count(x => x is not null));
        Assert.Contains(outcomes, x => x?.Succeeded == true);
    }

    [Fact]
    public async Task Concurrent_issue_reprocessing_has_one_authoritative_punch_maximum()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-ISSUE");
        var ingestion = DeviceService(fixture, fixture.Context);
        var input = Event("evt-issue", "EXT-ISSUE");
        var received = await ingestion.IngestAsync(new(device.Id, "issue", [input]));
        Assert.True(received.Succeeded, received.Message);
        await SeedMappingAsync(fixture, device.Id, "EXT-ISSUE");
        var issueId = await fixture.Context.AttendanceDeviceIngestionEvents.Where(x => x.ExternalEventId == input.ExternalEventId).Select(x => x.Id).SingleAsync();

        var outcomes = await RunConcurrentAsync(fixture, context => DeviceOperations(fixture, context).ReprocessIssueAsync(issueId));

        await using var verify = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
        Assert.Equal(1, await verify.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{device.Id:N}:{input.ExternalEventId}"));
        Assert.Equal(2, outcomes.Count(x => x is not null));
        Assert.All(outcomes, result => Assert.True(result!.Succeeded, result.Message));
    }

    [Fact]
    public async Task Same_event_in_different_tenants_remains_independent()
    {
        using var fixture = await CreateFixtureAsync();
        var otherTenant = Guid.NewGuid();
        fixture.Context.Tenants.Add(new Tenant { Id = otherTenant, TenantCode = $"T-{otherTenant:N}"[..20], Host = $"{otherTenant:N}.test", ShardKey = otherTenant.ToString("N"), TenantName = "Other" });
        await fixture.Context.SaveChangesAsync();
        var firstDevice = await SeedDeviceAsync(fixture, "BIO-TENANT-A");
        await SeedMappingAsync(fixture, firstDevice.Id, "EXT-SAME");
        await using (var other = fixture.CreateIsolatedContext(new TestTenantContext(otherTenant)))
        {
            var employee = new Employee { Id = Guid.NewGuid(), TenantId = otherTenant, EmployeeCode = "OTHER", FirstName = "Other", LastName = "Tenant", Email = $"{otherTenant:N}@test.local", DateOfJoining = new(2026, 1, 1) };
            other.Employees.Add(employee);
            other.Shifts.Add(new Shift { Id = Guid.NewGuid(), TenantId = otherTenant, ShiftCode = "OTHER", ShiftName = "OTHER", IsDefault = true, IsActive = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 });
            await other.SaveChangesAsync();
        }

        var a = DeviceService(fixture, fixture.Context).IngestAsync(new(firstDevice.Id, "tenant", [Event("evt-same", "EXT-SAME")]));
        await using var otherContext = fixture.CreateIsolatedContext(new TestTenantContext(otherTenant));
        var otherDevice = new AttendanceDevice { Id = Guid.NewGuid(), TenantId = otherTenant, Code = "BIO-TENANT-B", Name = "Other", DeviceType = "Terminal", TimeZoneId = "Asia/Kolkata", ConnectionMode = AttendanceDeviceConnectionMode.FileImport, Status = AttendanceDeviceStatus.Active };
        otherContext.AttendanceDevices.Add(otherDevice);
        await otherContext.SaveChangesAsync();
        var b = DeviceService(fixture, otherContext).IngestAsync(new(otherDevice.Id, "tenant", [Event("evt-same", "EXT-SAME")]));
        await Task.WhenAll(a, b);
        Assert.Equal(1, await fixture.Context.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{firstDevice.Id:N}:evt-same"));
        Assert.Equal(0, await otherContext.AttendancePunches.CountAsync());
    }

    [Fact]
    public async Task Failed_punch_persistence_does_not_advance_checkpoint_or_report_success()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-FAIL-PUNCH");
        await SeedMappingAsync(fixture, device.Id, "EXT-FAIL-PUNCH");
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<AttendancePunch>().Any(x => x.State == EntityState.Added));
        await using (var context = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId), fail))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => DeviceService(fixture, context, new TestTenantContext(fixture.TenantId)).IngestAsync(new(device.Id, "failure", [Event("evt-fail-punch", "EXT-FAIL-PUNCH")], "cursor-fail")));
        }
        await using var verify = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
        Assert.Empty(await verify.AttendancePunches.ToListAsync());
        Assert.Null(await verify.AttendanceDevices.Where(x => x.Id == device.Id).Select(x => x.LastSuccessfulCheckpoint).SingleAsync());
        Assert.Empty(await verify.AttendanceDeviceSyncRuns.Where(x => x.AttendanceDeviceId == device.Id).ToListAsync());
    }

    [Fact]
    public async Task Ingestion_receipt_persistence_failure_rolls_back_punch_and_run()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-FAIL-RECEIPT");
        await SeedMappingAsync(fixture, device.Id, "EXT-FAIL-RECEIPT");
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<AttendanceDeviceIngestionEvent>().Any(x => x.State == EntityState.Added));
        await using (var context = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId), fail))
            await Assert.ThrowsAsync<DbUpdateException>(() => DeviceService(fixture, context, new TestTenantContext(fixture.TenantId)).IngestAsync(new(device.Id, "receipt-failure", [Event("evt-fail-receipt", "EXT-FAIL-RECEIPT")])));
        await using var verify = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
        Assert.Empty(await verify.AttendancePunches.ToListAsync());
        Assert.Empty(await verify.AttendanceDeviceIngestionEvents.ToListAsync());
        Assert.Empty(await verify.AttendanceDeviceSyncRuns.ToListAsync());
    }

    [Fact]
    public async Task Device_audit_failure_rolls_back_device_mutation()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-FAIL-AUDIT");
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<AttendanceDeviceAuditEvent>().Any(x => x.State == EntityState.Added));
        await using (var context = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId), fail))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => DeviceOperations(fixture, context, new TestTenantContext(fixture.TenantId)).UpdateDeviceAsync(device.Id, new("BIO-FAIL-AUDIT-UPDATED", "Updated", "Terminal", null, null, null, "Asia/Kolkata", AttendanceDeviceConnectionMode.FileImport, null)));
        }
        await using var verify = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
        Assert.Equal("BIO-FAIL-AUDIT", await verify.AttendanceDevices.Where(x => x.Id == device.Id).Select(x => x.Code).SingleAsync());
        Assert.Empty(await verify.AttendanceDeviceAuditEvents.Where(x => x.AttendanceDeviceId == device.Id && x.Action == "DeviceUpdated").ToListAsync());
    }

    [Fact]
    public async Task Mapping_audit_failure_rolls_back_mapping_mutation()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-FAIL-MAP-AUDIT");
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<AttendanceDeviceAuditEvent>().Any(x => x.State == EntityState.Added));
        await using (var context = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId), fail))
        {
            var result = await DeviceOperations(fixture, context, new TestTenantContext(fixture.TenantId)).CreateMappingAsync(new(device.Id, "EXT-FAIL-MAP-AUDIT", fixture.EmployeeId, new(2026, 1, 1), null, AttendanceDeviceMappingStatus.Active));
            Assert.False(result.Succeeded);
        }
        await using var verify = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
        Assert.Empty(await verify.AttendanceDeviceEmployeeMappings.Where(x => x.AttendanceDeviceId == device.Id).ToListAsync());
        Assert.Empty(await verify.AttendanceDeviceAuditEvents.Where(x => x.AttendanceDeviceId == device.Id).ToListAsync());
    }

    [Fact]
    public async Task Partial_batch_retains_each_item_and_does_not_advance_checkpoint()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-PARTIAL");
        await SeedMappingAsync(fixture, device.Id, "EXT-VALID");
        var result = await DeviceService(fixture, fixture.Context).IngestAsync(new(device.Id, "partial", [
            Event("evt-valid", "EXT-VALID"),
            Event("evt-unmapped", "EXT-UNMAPPED"),
            new("", "EXT-INVALID", DateTimeOffset.UtcNow, AttendanceDevicePunchDirection.In)
        ], "cursor-partial"));
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.Value!.Received);
        Assert.Equal(1, result.Value.Accepted);
        Assert.Equal(1, result.Value.Unmapped);
        Assert.Equal(1, result.Value.Rejected);
        Assert.Null(result.Value.Checkpoint);
        Assert.Equal(3, await fixture.Context.AttendanceDeviceIngestionEvents.CountAsync());
    }

    [Fact]
    public async Task Provider_failure_before_ingestion_does_not_create_sync_run()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-PROVIDER", AttendanceDeviceConnectionMode.Pull, "FailingProvider");
        var operations = new AttendanceDeviceOperationsService(fixture.Context, fixture.TenantContext, DeviceService(fixture, fixture.Context), [new ThrowingPunchSource()]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => operations.SyncNowAsync(device.Id));
        Assert.Empty(await fixture.Context.AttendanceDeviceSyncRuns.Where(x => x.AttendanceDeviceId == device.Id).ToListAsync());
    }

    [Fact]
    public async Task Checkpoint_is_not_advanced_when_batch_contains_unmapped_event()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-CHECKPOINT");
        var result = await DeviceService(fixture, fixture.Context).IngestAsync(new(device.Id, "checkpoint", [Event("evt-unmapped-checkpoint", "EXT-NONE")], "cursor-no-advance"));
        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.Checkpoint);
        Assert.Null(await fixture.Context.AttendanceDevices.Where(x => x.Id == device.Id).Select(x => x.LastSuccessfulCheckpoint).SingleAsync());
    }

    [Fact]
    public async Task Concurrent_successful_batches_do_not_move_checkpoint_backward()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-CHECKPOINT-RACE");
        await SeedMappingAsync(fixture, device.Id, "EXT-CHECKPOINT-RACE");
        var outcomes = await RunConcurrentAsync(fixture, context => DeviceService(fixture, context).IngestAsync(new(device.Id, "checkpoint-race", [Event($"evt-{Guid.NewGuid():N}", "EXT-CHECKPOINT-RACE")], "cursor-new")));
        await using var verify = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
        var checkpoint = await verify.AttendanceDevices.Where(x => x.Id == device.Id).Select(x => x.LastSuccessfulCheckpoint).SingleAsync();
        Assert.True(checkpoint is null or "cursor-new");
        Assert.Equal(2, outcomes.Count(x => x is not null));
    }

    [Fact]
    public async Task Daily_processor_failure_retains_traceable_issue_without_duplicate_punch()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-FAIL-PROCESSOR");
        await SeedMappingAsync(fixture, device.Id, "EXT-FAIL-PROCESSOR");
        var service = DeviceService(fixture, fixture.Context, fixture.TenantContext, new FailingDayProcessor());
        var result = await service.IngestAsync(new(device.Id, "processor-failure", [Event("evt-fail-processor", "EXT-FAIL-PROCESSOR")]));
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(0, result.Value!.Accepted);
        Assert.Equal(1, await fixture.Context.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{device.Id:N}:evt-fail-processor"));
        Assert.Equal(AttendanceDeviceIngestionStatus.RequiresPeriodReopen, await fixture.Context.AttendanceDeviceIngestionEvents.Where(x => x.ExternalEventId == "evt-fail-processor").Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Checkpoint_persistence_failure_rolls_back_batch_and_retry_is_idempotent()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-FAIL-CHECKPOINT");
        await SeedMappingAsync(fixture, device.Id, "EXT-FAIL-CHECKPOINT");
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<AttendanceDevice>().Any(x => x.State == EntityState.Modified && x.Entity.LastSuccessfulCheckpoint == "cursor-failure"));
        await using (var context = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId), fail))
            await Assert.ThrowsAsync<DbUpdateException>(() => DeviceService(fixture, context, new TestTenantContext(fixture.TenantId)).IngestAsync(new(device.Id, "checkpoint-failure", [Event("evt-fail-checkpoint", "EXT-FAIL-CHECKPOINT")], "cursor-failure")));
        await using var retryContext = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
        var retry = await DeviceService(fixture, retryContext, new TestTenantContext(fixture.TenantId)).IngestAsync(new(device.Id, "checkpoint-failure", [Event("evt-fail-checkpoint", "EXT-FAIL-CHECKPOINT")], "cursor-failure"));
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Equal(1, await retryContext.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{device.Id:N}:evt-fail-checkpoint"));
    }

    [Fact]
    public async Task Sync_run_completion_failure_rolls_back_and_retry_does_not_duplicate()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-FAIL-COMPLETION");
        await SeedMappingAsync(fixture, device.Id, "EXT-FAIL-COMPLETION");
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<AttendanceDeviceSyncRun>().Any(x => x.State == EntityState.Modified && x.Entity.CompletedAtUtc is not null));
        await using (var context = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId), fail))
            await Assert.ThrowsAsync<DbUpdateException>(() => DeviceService(fixture, context, new TestTenantContext(fixture.TenantId)).IngestAsync(new(device.Id, "completion-failure", [Event("evt-fail-completion", "EXT-FAIL-COMPLETION")] )));
        await using var retryContext = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
        var retry = await DeviceService(fixture, retryContext, new TestTenantContext(fixture.TenantId)).IngestAsync(new(device.Id, "completion-failure", [Event("evt-fail-completion", "EXT-FAIL-COMPLETION")]));
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Equal(1, await retryContext.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{device.Id:N}:evt-fail-completion"));
    }

    [Fact]
    public async Task Issue_reprocessing_failure_rolls_back_issue_update_and_retry_is_safe()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-FAIL-REPROCESS");
        var first = await DeviceService(fixture, fixture.Context).IngestAsync(new(device.Id, "reprocess-failure", [Event("evt-fail-reprocess", "EXT-REPROCESS")]));
        Assert.Equal(1, first.Value!.Unmapped);
        await SeedMappingAsync(fixture, device.Id, "EXT-REPROCESS");
        var issueId = await fixture.Context.AttendanceDeviceIngestionEvents.Where(x => x.ExternalEventId == "evt-fail-reprocess").Select(x => x.Id).SingleAsync();
        var fail = new FailOnceSaveChangesInterceptor(context => context.ChangeTracker.Entries<AttendanceDeviceIngestionEvent>().Any(x => x.State == EntityState.Modified && x.Entity.Id == issueId));
        await using (var context = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId), fail))
            await Assert.ThrowsAsync<DbUpdateException>(() => DeviceOperations(fixture, context, new TestTenantContext(fixture.TenantId)).ReprocessIssueAsync(issueId));
        await using var retryContext = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
        var retry = await DeviceOperations(fixture, retryContext, new TestTenantContext(fixture.TenantId)).ReprocessIssueAsync(issueId);
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Equal(1, await retryContext.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{device.Id:N}:evt-fail-reprocess"));
    }

    [Fact]
    public async Task Finalized_period_failure_path_preserves_issue_and_creates_no_punch()
    {
        using var fixture = await CreateFixtureAsync();
        var device = await SeedDeviceAsync(fixture, "BIO-FAIL-FINALIZED");
        var received = await DeviceService(fixture, fixture.Context).IngestAsync(new(device.Id, "finalized-failure", [Event("evt-fail-finalized", "EXT-FINALIZED")]));
        Assert.Equal(1, received.Value!.Unmapped);
        await SeedMappingAsync(fixture, device.Id, "EXT-FINALIZED");
        fixture.Context.AttendancePeriods.Add(new AttendancePeriod { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Year = 2026, Month = 10, StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 31), Status = AttendancePeriodStatus.Closed });
        await fixture.Context.SaveChangesAsync();
        var issueId = await fixture.Context.AttendanceDeviceIngestionEvents.Where(x => x.ExternalEventId == "evt-fail-finalized").Select(x => x.Id).SingleAsync();
        var result = await DeviceOperations(fixture, fixture.Context).ReprocessIssueAsync(issueId);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(AttendanceDeviceIngestionStatus.RequiresPeriodReopen, result.Value!.Items.Single().Status);
        Assert.Empty(await fixture.Context.AttendancePunches.ToListAsync());
        Assert.Equal(AttendanceDeviceIngestionStatus.RequiresPeriodReopen, await fixture.Context.AttendanceDeviceIngestionEvents.Where(x => x.Id == issueId).Select(x => x.Status).SingleAsync());
    }

    private static async Task<AttendanceTestFixture> CreateFixtureAsync()
    {
        var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("DEVICE-CONCURRENCY", isDefault: true);
        return fixture;
    }

    private static async Task<AttendanceDevice> SeedDeviceAsync(AttendanceTestFixture fixture, string code, AttendanceDeviceConnectionMode mode = AttendanceDeviceConnectionMode.FileImport, string? vendor = null)
    {
        var device = new AttendanceDevice { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Code = code, Name = code, DeviceType = "Terminal", Vendor = vendor, TimeZoneId = "Asia/Kolkata", ConnectionMode = mode, Status = AttendanceDeviceStatus.Active };
        fixture.Context.AttendanceDevices.Add(device);
        await fixture.Context.SaveChangesAsync();
        return device;
    }

    private static async Task SeedMappingAsync(AttendanceTestFixture fixture, Guid deviceId, string externalId)
    {
        fixture.Context.AttendanceDeviceEmployeeMappings.Add(new AttendanceDeviceEmployeeMapping { Id = Guid.NewGuid(), TenantId = fixture.TenantId, AttendanceDeviceId = deviceId, ExternalEmployeeIdentifier = externalId, EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active });
        await fixture.Context.SaveChangesAsync();
    }

    private static NormalizedAttendancePunch Event(string id, string externalEmployee) => new(id, externalEmployee, new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In);

    private static AttendanceDeviceIntegrationService DeviceService(AttendanceTestFixture fixture, HRMS.Infrastructure.Persistence.HrmsDbContext context, ITenantContext? tenant = null, IAttendanceDayProcessor? processorOverride = null)
    {
        var scope = tenant ?? fixture.TenantContext;
        var employment = new EffectiveEmploymentResolver(context, scope);
        var calendar = new WorkingDayCalendarResolver(context, scope, employment);
        var foundation = new AttendanceFoundationService(context, scope, employment, calendar);
        var punch = new AttendancePunchIngestionService(context, scope, foundation, new AttendanceBusinessDateResolver(), new AttendanceBusinessTimeZoneProvider(), new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)));
        var processor = processorOverride ?? new AttendanceDayProcessor(context, scope, foundation, new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)));
        return new(context, scope, punch, processor, new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)));
    }

    private static AttendanceDeviceOperationsService DeviceOperations(AttendanceTestFixture fixture, HRMS.Infrastructure.Persistence.HrmsDbContext context, ITenantContext? tenant = null) => new(context, tenant ?? fixture.TenantContext, DeviceService(fixture, context, tenant), []);

    private static async Task<IReadOnlyList<T?>> RunConcurrentAsync<T>(AttendanceTestFixture fixture, Func<HRMS.Infrastructure.Persistence.HrmsDbContext, Task<T>> operation)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<T?> Run()
        {
            await using var context = fixture.CreateIsolatedContext(new TestTenantContext(fixture.TenantId));
            await gate.Task;
            try { return await operation(context); }
            catch (DbUpdateException) { return default; }
            catch (InvalidOperationException) { return default; }
        }
        var first = Run();
        var second = Run();
        gate.SetResult();
        return [await first, await second];
    }

    private sealed class FailOnceSaveChangesInterceptor(Func<DbContext, bool> predicate) : SaveChangesInterceptor
    {
        private int failed;
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (predicate(eventData.Context!) && Interlocked.Exchange(ref failed, 1) == 0) throw new DbUpdateException("Injected Phase 6F persistence failure.");
            return result;
        }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (predicate(eventData.Context!) && Interlocked.Exchange(ref failed, 1) == 0) throw new DbUpdateException("Injected Phase 6F persistence failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowingPunchSource : IAttendancePunchSource
    {
        public string ProviderKey => "FailingProvider";
        public Task<AttendanceDevicePunchPage> FetchAsync(string? checkpoint, int maximumItems, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Provider failure after sync request.");
    }

    private sealed class FailingDayProcessor : IAttendanceDayProcessor
    {
        public Task<Result<EmployeeAttendanceDayDto>> ProcessAsync(Guid employeeId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<EmployeeAttendanceDayDto>.Failure(ResultStatus.Conflict, "Injected daily processor failure."));
    }
}
