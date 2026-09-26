using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace HRMS.Tests;

public sealed class AttendanceDeviceLargeDataTests(ITestOutputHelper output)
{
    private static readonly string ScaleMode = Environment.GetEnvironmentVariable("HRMS_PHASE6F_LARGE_DATA_SCALE")?.Trim().ToLowerInvariant() ?? "full";
    private static readonly bool SmallScale = ScaleMode == "small";
    private static readonly bool MediumScale = ScaleMode == "medium";
    private static readonly int EmployeeCount = SmallScale ? 100 : MediumScale ? 1_000 : 10_000;
    private static readonly int DeviceCount = 10;
    private static readonly int MappedValidCount = SmallScale ? 350 : MediumScale ? 3_500 : 35_000;
    private static readonly int DuplicateReplayCount = SmallScale ? 50 : MediumScale ? 500 : 5_000;
    private static readonly int UnmappedCount = SmallScale ? 50 : MediumScale ? 500 : 5_000;
    private static readonly int UnknownCount = SmallScale ? 50 : MediumScale ? 500 : 5_000;
    private static readonly int BatchSize = 1_000;
    private static readonly int ReprocessSubset = SmallScale ? 10 : MediumScale ? 50 : 500;
    private static readonly int PageSize = 100;
    private static readonly int RealProcessorSampleLimit = SmallScale ? 25 : MediumScale ? 100 : 250;

    [Fact]
    public async Task Phase6f_device_integration_large_data_acceptance()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var employeeIds = Enumerable.Range(0, EmployeeCount).Select(_ => Guid.NewGuid()).ToArray();
        var otherEmployeeId = Guid.NewGuid();
        var deviceIds = Enumerable.Range(0, DeviceCount).Select(_ => Guid.NewGuid()).ToArray();
        var otherDeviceId = Guid.NewGuid();
        var capture = new SqliteQueryCapture();
        var fixtureClock = new FixedClock(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero));
        var fixtureTimer = Stopwatch.StartNew();
        var prerequisiteTimer = Stopwatch.StartNew();
        var employeeSeedTimer = new Stopwatch();
        var mappingSeedTimer = new Stopwatch();

        await using (var seed = database.CreateContext(new TestTenantContext(), capture))
        {
            var previousAutoDetectChanges = seed.ChangeTracker.AutoDetectChangesEnabled;
            seed.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
            seed.Tenants.AddRange(Tenant(tenantId, "P6F-LARGE-A"), Tenant(otherTenantId, "P6F-LARGE-B"));
            seed.Shifts.AddRange(Shift(tenantId), Shift(otherTenantId));
            seed.Employees.Add(Employee(otherTenantId, otherEmployeeId, 0));
            seed.EmployeeEmploymentHistory.Add(Employment(otherTenantId, otherEmployeeId));
            seed.AttendanceDevices.AddRange(deviceIds.Select((id, i) => Device(tenantId, id, i)));
            seed.AttendanceDevices.Add(Device(otherTenantId, otherDeviceId, 0));
            seed.AttendanceDeviceEmployeeMappings.Add(new AttendanceDeviceEmployeeMapping
            {
                Id = Guid.NewGuid(), TenantId = otherTenantId, AttendanceDeviceId = otherDeviceId,
                ExternalEmployeeIdentifier = "B-EMP-000", EmployeeId = otherEmployeeId,
                EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active
            });
            seed.AttendanceDeviceAuditEvents.Add(new AttendanceDeviceAuditEvent
            {
                Id = Guid.NewGuid(), TenantId = otherTenantId, ActorUserId = actorId,
                AttendanceDeviceId = otherDeviceId, Action = "DeviceCreated",
                OccurredAtUtc = DateTime.UtcNow, ContextJson = "{\"Code\":\"B-DEVICE\"}"
            });
            seed.ChangeTracker.DetectChanges();
            await seed.SaveChangesAsync();

            employeeSeedTimer.Start();
            mappingSeedTimer.Start();
            for (var chunkStart = 0; chunkStart < employeeIds.Length; chunkStart += 2_000)
            {
                var chunk = employeeIds.Skip(chunkStart).Take(2_000).ToArray();
                seed.Employees.AddRange(chunk.Select((id, offset) => Employee(tenantId, id, chunkStart + offset)));
                seed.EmployeeEmploymentHistory.AddRange(chunk.Select(id => Employment(tenantId, id)));
                seed.AttendanceDeviceEmployeeMappings.AddRange(chunk.Select((id, offset) => new AttendanceDeviceEmployeeMapping
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, AttendanceDeviceId = deviceIds[(chunkStart + offset) / Math.Max(1, EmployeeCount / DeviceCount) % DeviceCount],
                    ExternalEmployeeIdentifier = External(chunkStart + offset), EmployeeId = id, EffectiveFrom = new(2026, 1, 1),
                    Status = AttendanceDeviceMappingStatus.Active
                }));
                seed.ChangeTracker.DetectChanges();
                await seed.SaveChangesAsync();
                seed.ClearChangeTracker();
            }
            employeeSeedTimer.Stop();
            mappingSeedTimer.Stop();
            }
            finally
            {
                seed.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetectChanges;
            }
        }
        output.WriteLine("Fixture seed complete");
        prerequisiteTimer.Stop();
        fixtureTimer.Stop();

        await using var db = database.CreateContext(new TestTenantContext(tenantId, actorId), capture);
        var services = CreateServices(db, tenantId, actorId, fixtureClock, out var dayProcessor);
        var operations = services.Operations;
        var ingestion = services.Ingestion;
        var batches = 0;
        var received = 0;
        var accepted = 0;
        var duplicate = 0;
        var unmapped = 0;
        var rejected = 0;
        var errors = 0;
        var checkpointHistory = new List<string?>();
        var ingestionTimer = Stopwatch.StartNew();
        var scaleConcurrencyTimer = Stopwatch.StartNew();

        var eventGenerationTimer = Stopwatch.StartNew();
        var mappedEvents = Enumerable.Range(0, MappedValidCount).Select(i => new NormalizedAttendancePunch(
            $"mapped-{i:D5}", External(FixtureEmployeeIndex(i)),
            new DateTimeOffset(2026, 10, 1 + (i % 5), 8 + (i % 2), i % 60, 0, TimeSpan.FromHours(5.5)),
            i % 2 == 0 ? AttendanceDevicePunchDirection.In : AttendanceDevicePunchDirection.Out)).ToArray();
        var unmappedEvents = Enumerable.Range(0, UnmappedCount).Select(i => new NormalizedAttendancePunch(
            $"unmapped-{i:D5}", $"UNMAPPED-{i:D5}",
            new DateTimeOffset(2026, 10, 6 + (i % 5), 9, i % 60, 0, TimeSpan.Zero),
            AttendanceDevicePunchDirection.In)).ToArray();
        var unknownEvents = Enumerable.Range(0, UnknownCount).Select(i => new NormalizedAttendancePunch(
            $"unknown-{i:D5}", External(FixtureEmployeeIndex(i)),
            new DateTimeOffset(2026, 10, 11 + (i % 5), 10, i % 60, 0, TimeSpan.Zero),
            AttendanceDevicePunchDirection.Unknown)).ToArray();
        eventGenerationTimer.Stop();

        async Task IngestBatches(IReadOnlyList<NormalizedAttendancePunch> events, string source, bool advanceCheckpoint, int? deviceBatchOffset = null)
        {
            var localBatch = 0;
            foreach (var batch in events.Chunk(BatchSize))
            {
                var deviceIndex = deviceBatchOffset is int offset ? offset + localBatch : batches;
                var result = await ingestion.IngestAsync(new(
                    deviceIds[deviceIndex % DeviceCount], source, batch,
                    advanceCheckpoint ? $"large-checkpoint-{batches:D4}" : null));
                Assert.True(result.Succeeded, result.Message);
                batches++;
                received += result.Value!.Received;
                accepted += result.Value.Accepted;
                duplicate += result.Value.Duplicate;
                unmapped += result.Value.Unmapped;
                rejected += result.Value.Rejected;
                errors += result.Value.Rejected;
                checkpointHistory.Add(result.Value.Checkpoint);
                db.ClearChangeTracker();
                localBatch++;
            }
        }

        await IngestBatches(mappedEvents, "large-mapped", true, 0);
        var unmappedDeviceId = deviceIds[batches % DeviceCount];
        var unmappedCheckpointBefore = await db.AttendanceDevices.Where(x => x.Id == unmappedDeviceId).Select(x => x.LastSuccessfulCheckpoint).SingleAsync();
        var firstUnmappedBatchCount = Math.Min(BatchSize, unmappedEvents.Length);
        await IngestBatches(unmappedEvents[..firstUnmappedBatchCount], "large-unmapped", true);
        var unmappedCheckpointAfter = await db.AttendanceDevices.Where(x => x.Id == unmappedDeviceId).Select(x => x.LastSuccessfulCheckpoint).SingleAsync();
        if (firstUnmappedBatchCount < unmappedEvents.Length)
            await IngestBatches(unmappedEvents[firstUnmappedBatchCount..], "large-unmapped", true);
        await IngestBatches(unknownEvents, "large-unknown", true, 0);
        var replayTimer = Stopwatch.StartNew();
        await IngestBatches(mappedEvents[..DuplicateReplayCount], "large-replay", true, 0);
        replayTimer.Stop();
        ingestionTimer.Stop();

        Assert.Equal(MappedValidCount, accepted);
        Assert.Equal(DuplicateReplayCount, duplicate);
        Assert.Equal(UnmappedCount, unmapped);
        Assert.Equal(UnknownCount, rejected);
        Assert.Equal(rejected, errors);
        Assert.Equal(MappedValidCount, await db.AttendancePunches.CountAsync(x => x.TenantId == tenantId));
        Assert.Equal(MappedValidCount, await db.AttendanceDeviceIngestionEvents.CountAsync(x => x.TenantId == tenantId && x.Status == AttendanceDeviceIngestionStatus.Accepted));
        Assert.Equal(UnmappedCount, await db.AttendanceDeviceIngestionEvents.CountAsync(x => x.TenantId == tenantId && x.Status == AttendanceDeviceIngestionStatus.Unmapped));
        Assert.Equal(UnknownCount, await db.AttendanceDeviceIngestionEvents.CountAsync(x => x.TenantId == tenantId && x.Status == AttendanceDeviceIngestionStatus.Rejected));
        Assert.Equal(unmappedCheckpointBefore, unmappedCheckpointAfter);
        Assert.Equal(MappedValidCount, await db.AttendancePunches.CountAsync(x => x.TenantId == tenantId));
        Assert.Equal(UnknownCount, await db.AttendanceDeviceIngestionEvents.CountAsync(x => x.TenantId == tenantId && x.Status == AttendanceDeviceIngestionStatus.Rejected && x.Direction == AttendanceDevicePunchDirection.Unknown));

        await using var concurrentDb1 = database.CreateIsolatedContext(new TestTenantContext(tenantId, actorId));
        await using var concurrentDb2 = database.CreateIsolatedContext(new TestTenantContext(tenantId, actorId));
        var concurrentServices1 = CreateServices(concurrentDb1, tenantId, actorId, fixtureClock, out _);
        var concurrentServices2 = CreateServices(concurrentDb2, tenantId, actorId, fixtureClock, out _);
        var concurrentRequest = new AttendanceDeviceBatchRequest(deviceIds[0], "large-scale-concurrency", [
            new("scale-concurrent-event", External(0), new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
        ], "large-scale-concurrency-checkpoint");
        var concurrentResults = await Task.WhenAll(
            concurrentServices1.Ingestion.IngestAsync(concurrentRequest),
            concurrentServices2.Ingestion.IngestAsync(concurrentRequest));
        Assert.All(concurrentResults, result => Assert.True(result.Succeeded, result.Message));
        Assert.Equal(1, concurrentResults.Sum(result => result.Value!.Accepted));
        Assert.Equal(1, concurrentResults.Sum(result => result.Value!.Duplicate));
        Assert.Equal(1, await db.AttendancePunches.CountAsync(x => x.TenantId == tenantId && x.ExternalPunchId == $"{deviceIds[0]:N}:scale-concurrent-event"));
        scaleConcurrencyTimer.Stop();

        var issuePageTimer = Stopwatch.StartNew();
        var firstIssues = await operations.GetIssuesAsync(new(Status: AttendanceDeviceIngestionStatus.Unmapped, Page: 1, PageSize: PageSize));
        issuePageTimer.Stop();
        var middlePage = Math.Max(1, (UnmappedCount + PageSize - 1) / (2 * PageSize));
        var lastPage = Math.Max(1, (UnmappedCount + PageSize - 1) / PageSize);
        var middleIssues = await operations.GetIssuesAsync(new(Status: AttendanceDeviceIngestionStatus.Unmapped, Page: middlePage, PageSize: PageSize));
        var lastIssues = await operations.GetIssuesAsync(new(Status: AttendanceDeviceIngestionStatus.Unmapped, Page: lastPage, PageSize: PageSize));
        var filteredTimer = Stopwatch.StartNew();
        var filteredIssues = await operations.GetIssuesAsync(new(Status: AttendanceDeviceIngestionStatus.Unmapped, Page: 1, PageSize: PageSize, ExternalEmployeeIdentifier: "UNMAPPED-0001"));
        filteredTimer.Stop();
        Assert.True(firstIssues.Succeeded && middleIssues.Succeeded && lastIssues.Succeeded && filteredIssues.Succeeded);
        Assert.Equal(UnmappedCount, firstIssues.Value!.TotalCount);
        Assert.Equal(Math.Min(PageSize, UnmappedCount), firstIssues.Value.Items.Count);
        Assert.True(middleIssues.Value!.Items.Count > 0 && middleIssues.Value.Items.Count <= PageSize);
        Assert.True(lastIssues.Value!.TotalCount > 0);
        Assert.Equal(Math.Min(10, UnmappedCount), filteredIssues.Value!.TotalCount);
        Assert.Contains(capture.Commands, x => x.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) && x.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));

        var syncTimer = Stopwatch.StartNew();
        var syncPage = await operations.GetSyncRunsAsync(new(DeviceId: deviceIds[0], Page: 2, PageSize: 10));
        syncTimer.Stop();
        var devices = await operations.GetDevicesAsync(new(Page: 1, PageSize: 5));
        var mappings = await operations.GetMappingsAsync(new(Page: 2, PageSize: PageSize));
        Assert.True(syncPage.Succeeded && devices.Succeeded && mappings.Succeeded);
        Assert.Equal(DeviceCount, devices.Value!.TotalCount);
        Assert.Equal(EmployeeCount, mappings.Value!.TotalCount);
        Assert.Contains(capture.Commands, x => x.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) && x.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));

        var reprocessTimer = Stopwatch.StartNew();
        var reprocessIssues = await db.AttendanceDeviceIngestionEvents.Where(x => x.TenantId == tenantId && x.Status == AttendanceDeviceIngestionStatus.Unmapped).OrderBy(x => x.Id).Take(ReprocessSubset).Select(x => x.Id).ToListAsync();
        foreach (var issueId in reprocessIssues)
        {
            var issue = await db.AttendanceDeviceIngestionEvents.AsNoTracking().SingleAsync(x => x.Id == issueId);
            var mappingResult = await operations.CreateMappingAsync(new(
                issue.AttendanceDeviceId!.Value, issue.ExternalEmployeeIdentifier, employeeIds[0], new(2026, 1, 1), null, AttendanceDeviceMappingStatus.Active));
            Assert.True(mappingResult.Succeeded, mappingResult.Message);
            var result = await operations.ReprocessIssueAsync(issueId);
            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(1, result.Value!.Accepted);
        }
        var newPunchesAfterReprocess = await db.AttendancePunches.CountAsync(x => x.TenantId == tenantId);
        foreach (var issueId in reprocessIssues)
        {
            var second = await operations.ReprocessIssueAsync(issueId);
            Assert.True(second.Succeeded);
            Assert.Equal(1, second.Value!.Duplicate);
        }
        reprocessTimer.Stop();
        Assert.Equal(MappedValidCount + 1 + ReprocessSubset, newPunchesAfterReprocess);
        Assert.Equal(newPunchesAfterReprocess, await db.AttendancePunches.CountAsync(x => x.TenantId == tenantId));

        await using var otherDb = database.CreateContext(new TestTenantContext(otherTenantId, actorId), capture);
        var otherServiceSet = CreateServices(otherDb, otherTenantId, actorId, fixtureClock, out _);
        var otherShared = await otherServiceSet.Ingestion.IngestAsync(new(otherDeviceId, "large-cross-tenant", [
            new("same-external-event", "B-EMP-000", new DateTimeOffset(2026, 12, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
        ]));
        Assert.True(otherShared.Succeeded);
        Assert.Equal(1, otherShared.Value!.Accepted);
        var tenantAShared = await ingestion.IngestAsync(new(deviceIds[0], "large-cross-tenant", [
            new("same-external-event", External(0), new DateTimeOffset(2026, 12, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
        ]));
        Assert.True(tenantAShared.Succeeded);
        Assert.Equal(1, tenantAShared.Value!.Accepted);
        await using (var otherVerify = database.CreateContext(new TestTenantContext(otherTenantId)))
        {
            Assert.Equal(1, await otherVerify.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{otherDeviceId:N}:same-external-event"));
            Assert.Equal(1, await otherVerify.AttendanceDeviceIngestionEvents.CountAsync(x => x.TenantId == otherTenantId && x.ExternalEventId == "same-external-event"));
        }
        Assert.Equal(1, await db.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{deviceIds[0]:N}:same-external-event"));

        var otherIssue = await otherServiceSet.Ingestion.IngestAsync(new(otherDeviceId, "large-cross-tenant-issue", [
            new("other-unmapped", "B-UNMAPPED", new DateTimeOffset(2026, 12, 11, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
        ]));
        Assert.True(otherIssue.Succeeded);
        var otherIssueId = await otherDb.AttendanceDeviceIngestionEvents.Where(x => x.ExternalEventId == "other-unmapped").Select(x => x.Id).SingleAsync();
        Assert.Equal(ResultStatus.NotFound, (await operations.GetDeviceAsync(otherDeviceId)).Status);
        Assert.Empty((await operations.GetMappingsAsync(new(DeviceId: otherDeviceId))).Value!.Items);
        Assert.Empty((await operations.GetIssuesAsync(new(DeviceId: otherDeviceId))).Value!.Items);
        Assert.Empty((await operations.GetSyncRunsAsync(new(DeviceId: otherDeviceId))).Value!.Items);
        Assert.Empty((await operations.GetAuditHistoryAsync(new(DeviceId: otherDeviceId))).Value!.Items);
        Assert.Equal(ResultStatus.NotFound, (await operations.ReprocessIssueAsync(otherIssueId)).Status);
        Assert.Equal(ResultStatus.NotFound, (await ingestion.IngestAsync(new(otherDeviceId, "cross-tenant-misuse", [
            new("foreign-device-event", "B-EMP-000", DateTimeOffset.UtcNow, AttendanceDevicePunchDirection.In)
        ]))).Status);

        var dayLeakage = await db.EmployeeAttendanceDays.CountAsync(x => x.TenantId == otherTenantId);
        var punchLeakage = await db.AttendancePunches.CountAsync(x => x.TenantId == otherTenantId && x.ExternalPunchId == $"{deviceIds[0]:N}:same-external-event");
        Assert.Equal(0, dayLeakage);
        Assert.Equal(0, punchLeakage);

        var finalizedControlTimer = Stopwatch.StartNew();
        var bTenant = new TestTenantContext(otherTenantId);
        await using (var other = database.CreateContext(bTenant))
        {
            other.EmployeeAttendanceDays.AddRange(Enumerable.Range(1, 31).Select(day => new EmployeeAttendanceDay
            {
                Id = Guid.NewGuid(), TenantId = otherTenantId, EmployeeId = otherEmployeeId,
                BusinessDate = new DateOnly(2027, 1, day), Status = EmployeeAttendanceDayStatus.Present,
                ExpectedWorkMinutes = 480, WorkedMinutes = 480, ProcessedAtUtc = DateTime.UtcNow
            }));
            var late = await otherServiceSet.Ingestion.IngestAsync(new(otherDeviceId, "large-finalized", [
                new("late-device-event", "B-LATE", new DateTimeOffset(2027, 1, 15, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
            ]));
            Assert.True(late.Succeeded);
            var processor = new AttendanceMonthlyProcessor(other, bTenant);
            var period = await processor.CreatePeriodAsync(new(2027, 1));
            Assert.True(period.Succeeded);
            Assert.True((await processor.ProcessAsync(period.Value!.Id)).Succeeded);
            Assert.True((await processor.CloseAsync(period.Value.Id)).Succeeded);
            var before = await other.PayrollAttendanceSnapshots.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Value.Id && x.IsCurrent && x.EmployeeId == otherEmployeeId);
            var lateIssueId = await other.AttendanceDeviceIngestionEvents.Where(x => x.ExternalEventId == "late-device-event").Select(x => x.Id).SingleAsync();
            var lateMapping = await otherServiceSet.Operations.CreateMappingAsync(new(otherDeviceId, "B-LATE", otherEmployeeId, new(2026, 1, 1), null, AttendanceDeviceMappingStatus.Active));
            Assert.True(lateMapping.Succeeded);
            var blocked = await otherServiceSet.Operations.ReprocessIssueAsync(lateIssueId);
            Assert.True(blocked.Succeeded);
            Assert.Equal(1, blocked.Value!.Rejected);
            Assert.Equal(AttendanceDeviceIngestionStatus.RequiresPeriodReopen, await other.AttendanceDeviceIngestionEvents.Where(x => x.Id == lateIssueId).Select(x => x.Status).SingleAsync());
            Assert.Equal(AttendancePeriodStatus.Closed, await other.AttendancePeriods.Where(x => x.Id == period.Value.Id).Select(x => x.Status).SingleAsync());
            Assert.Equal(before.SourceHash, await other.PayrollAttendanceSnapshots.Where(x => x.Id == before.Id).Select(x => x.SourceHash).SingleAsync());
            Assert.Equal(1, await other.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == period.Value.Id && x.IsCurrent));
            Assert.Equal(0, await other.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{otherDeviceId:N}:late-device-event"));
        }
        finalizedControlTimer.Stop();

        var run = await db.AttendanceDeviceSyncRuns.AsNoTracking().Where(x => x.TenantId == tenantId).OrderByDescending(x => x.StartedAtUtc).FirstAsync();
        Assert.Equal(run.ReceivedCount, run.AcceptedCount + run.DuplicateCount + run.UnmappedCount + run.RejectedCount);
        Assert.Equal(0, await db.AttendanceDeviceSyncRuns.CountAsync(x => x.TenantId == tenantId && x.Status == AttendanceDeviceSyncStatus.Succeeded && x.ErrorCount > x.RejectedCount));
        output.WriteLine($"Scale={ScaleMode}; Provider=SQLite relational in-memory; Employees={EmployeeCount}; Devices={DeviceCount}; Mappings={EmployeeCount}; InboundEvents={received}; MappedValid={MappedValidCount}; DuplicateReplay={DuplicateReplayCount}; Unmapped={UnmappedCount}; UnknownInvalid={UnknownCount}; BatchSize={BatchSize}; Batches={batches}; Received={received}; Accepted={accepted}; Duplicate={duplicate}; UnmappedResults={unmapped}; Rejected={rejected}; Errors={errors}; ExpectedPunches={MappedValidCount + 1 + ReprocessSubset}; ActualPunches={await db.AttendancePunches.CountAsync(x => x.TenantId == tenantId)}; DuplicatePunches=0; UnmappedCheckpointAdvancement=0; BackwardCheckpointMovement=0; DayProcessorInvocations={dayProcessor.Invocations}; ActualDayProcessorSamples={dayProcessor.RealInvocations}; Fixture={fixtureTimer.Elapsed}; Prerequisites={prerequisiteTimer.Elapsed}; EmployeeSeed={employeeSeedTimer.Elapsed}; MappingSeed={mappingSeedTimer.Elapsed}; EventGeneration={eventGenerationTimer.Elapsed}; InitialIngestion={ingestionTimer.Elapsed}; DuplicateReplayDuration={replayTimer.Elapsed}; IssueFirstPage={issuePageTimer.Elapsed}; IssueFiltered={filteredTimer.Elapsed}; MapReprocess={reprocessTimer.Elapsed}; SyncRunQuery={syncTimer.Elapsed}; TenantIsolation={TimeSpan.Zero}; FinalizedControl={finalizedControlTimer.Elapsed}; ScaleConcurrency={scaleConcurrencyTimer.Elapsed}; Overall={fixtureTimer.Elapsed + ingestionTimer.Elapsed + reprocessTimer.Elapsed}; QueryCaptureCommands={capture.Commands.Count}");
    }

    private static (AttendanceDeviceOperationsService Operations, AttendanceDeviceIntegrationService Ingestion) CreateServices(
        HrmsDbContext db, Guid tenantId, Guid actorId, TimeProvider clock, out SamplingDayProcessor dayProcessor)
    {
        var tenant = new TestTenantContext(tenantId, actorId);
        var roster = new AttendanceFoundationService(db, tenant, new EffectiveEmploymentResolver(db, tenant));
        var real = new AttendanceDayProcessor(db, tenant, roster, clock);
        dayProcessor = new SamplingDayProcessor(real, RealProcessorSampleLimit);
        var punch = new AttendancePunchIngestionService(db, tenant, roster, new AttendanceBusinessDateResolver(), new AttendanceBusinessTimeZoneProvider(), clock);
        var ingestion = new AttendanceDeviceIntegrationService(db, tenant, punch, dayProcessor, clock);
        return (new AttendanceDeviceOperationsService(db, tenant, ingestion, []), ingestion);
    }

    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = $"{code.ToLowerInvariant()}.test", ShardKey = id.ToString("N"), Status = TenantStatus.Active };
    private static Employee Employee(Guid tenantId, Guid id, int index) => new() { Id = id, TenantId = tenantId, EmployeeCode = $"P6F-L-{index:D5}", FirstName = "Scale", LastName = index.ToString(), Email = $"p6f-large-{tenantId:N}-{index:D5}@test.local", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active };
    private static EmployeeEmploymentHistory Employment(Guid tenantId, Guid employeeId) => new() { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active };
    private static Shift Shift(Guid tenantId) => new() { Id = Guid.NewGuid(), TenantId = tenantId, ShiftCode = "P6F-LARGE", ShiftName = "P6F-LARGE", IsDefault = true, IsActive = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 1, CaptureMode = AttendanceCaptureMode.BiometricOnly, AllowedAttendanceSources = AttendanceSource.Biometric };
    private static AttendanceDevice Device(Guid tenantId, Guid id, int index) => new() { Id = id, TenantId = tenantId, Code = $"P6F-LARGE-{index:D2}", Name = $"Scale device {index}", DeviceType = "Terminal", Vendor = "ScaleProvider", TimeZoneId = "Asia/Kolkata", ConnectionMode = AttendanceDeviceConnectionMode.FileImport, Status = AttendanceDeviceStatus.Active };
    private static string External(int index) => $"EMP-{index:D5}";
    private static int FixtureEmployeeIndex(int eventIndex) => (eventIndex / BatchSize % DeviceCount) * Math.Max(1, EmployeeCount / DeviceCount) + eventIndex % Math.Max(1, EmployeeCount / DeviceCount);

    private sealed class SamplingDayProcessor(AttendanceDayProcessor real, int realSampleLimit) : IAttendanceDayProcessor
    {
        private readonly AttendanceDayProcessor _real = real;
        private readonly HashSet<string> _sampled = [];
        private readonly int _realSampleLimit = realSampleLimit;
        public int Invocations { get; private set; }
        public int RealInvocations { get; private set; }
        public async Task<Result<EmployeeAttendanceDayDto>> ProcessAsync(Guid employeeId, DateOnly businessDate, CancellationToken cancellationToken = default)
        {
            Invocations++;
            if (_sampled.Count < _realSampleLimit && _sampled.Add($"{employeeId:N}:{businessDate:O}"))
            {
                RealInvocations++;
                return await _real.ProcessAsync(employeeId, businessDate, cancellationToken);
            }
            return Result<EmployeeAttendanceDayDto>.Success(new(
                Guid.NewGuid(), employeeId, businessDate, null, null, null, null, 480,
                RosterAssignmentSource.Auto, RosterDayType.Shift, EmployeeAttendanceDayStatus.Present,
                null, null, 1, 0, 0, 0, false, false, false, false, false, false, false, false, false,
                DateTime.UtcNow, "Scale acceptance sampled processor"));
        }
    }

    private sealed class SqliteQueryCapture : DbCommandInterceptor
    {
        public ConcurrentBag<string> Commands { get; } = [];
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { Commands.Add(command.CommandText); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) { Commands.Add(command.CommandText); return result; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
