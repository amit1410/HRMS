using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

internal static class AttendanceDeviceIntegrationProviderAcceptance
{
    public const string SharedRunnerName = nameof(AttendanceDeviceIntegrationProviderAcceptance);
    public const int IntendedScenarioCount = 14;

    public static async Task RunAsync(Func<TestTenantContext, HrmsDbContext> createContext, string provider)
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        Guid otherDeviceId = Guid.Empty;
        Guid otherMappingId = Guid.Empty;
        Guid otherIssueId = Guid.Empty;
        Guid otherSyncRunId = Guid.Empty;

        await using (var setup = createContext(new TestTenantContext()))
        {
            setup.Tenants.AddRange(Tenant(tenantId, "P6F-A"), Tenant(otherTenantId, "P6F-B"));
            setup.Employees.AddRange(Employee(tenantId, employeeId, "P6F-001"), Employee(otherTenantId, otherEmployeeId, "P6F-001"));
            setup.EmployeeEmploymentHistory.AddRange(Employment(tenantId, employeeId), Employment(otherTenantId, otherEmployeeId));
            setup.Shifts.AddRange(Shift(tenantId), Shift(otherTenantId));
            await setup.SaveChangesAsync();
        }

        await using var db = createContext(new TestTenantContext(tenantId, actorId));
        var services = CreateServices(db, tenantId, actorId);
        var operations = services.Operations;
        var ingestion = services.Ingestion;

        var device = await operations.CreateDeviceAsync(new(
            "P6F-DEVICE", "Phase 6F provider device", "Terminal", "NoProvider", "SER-6F",
            null, "Asia/Kolkata", AttendanceDeviceConnectionMode.FileImport, "vault://phase6f-safe-ref"));
        Assert.True(device.Succeeded, $"{provider}: {device.Message}");
        Assert.DoesNotContain("vault://", System.Text.Json.JsonSerializer.Serialize(device.Value));
        Assert.Equal(ResultStatus.Conflict, (await operations.CreateDeviceAsync(new(
            "P6F-DEVICE", "duplicate", "Terminal", "NoProvider", null, null, "Asia/Kolkata",
            AttendanceDeviceConnectionMode.FileImport, null))).Status);

        await using (var other = createContext(new TestTenantContext(otherTenantId, actorId)))
        {
            var otherServices = CreateServices(other, otherTenantId, actorId);
            var otherOps = otherServices.Operations;
            var sameCode = await otherOps.CreateDeviceAsync(new(
                "P6F-DEVICE", "Other tenant device", "Terminal", "NoProvider", null, null,
                "Asia/Kolkata", AttendanceDeviceConnectionMode.FileImport, null));
            Assert.True(sameCode.Succeeded);
            otherDeviceId = sameCode.Value!.Id;
        }

        Assert.Equal(ResultStatus.Success, (await operations.UpdateDeviceAsync(device.Value!.Id, new(
            "P6F-DEVICE-UPDATED", "Updated device", "Terminal", "NoProvider", "SER-6F-2", null,
            "Asia/Kolkata", AttendanceDeviceConnectionMode.FileImport, null))).Status);
        Assert.Equal(ResultStatus.Success, (await operations.SetDeviceStatusAsync(device.Value.Id, AttendanceDeviceStatus.Inactive)).Status);
        Assert.Equal(ResultStatus.Success, (await operations.SetDeviceStatusAsync(device.Value.Id, AttendanceDeviceStatus.Active)).Status);
        var listed = await operations.GetDevicesAsync(new(Page: 1, PageSize: 1, Search: "UPDATED"));
        Assert.True(listed.Succeeded);
        Assert.Single(listed.Value!.Items);

        var mappingRequest = new AttendanceDeviceMappingRequest(device.Value.Id, "EXT-6F", employeeId,
            new(2026, 1, 1), null, AttendanceDeviceMappingStatus.Active);
        var mapping = await operations.CreateMappingAsync(mappingRequest);
        Assert.True(mapping.Succeeded, $"{provider}: {mapping.Message}");
        Assert.Equal(ResultStatus.Conflict, (await operations.CreateMappingAsync(mappingRequest)).Status);
        Assert.Equal(ResultStatus.Conflict, (await operations.CreateMappingAsync(mappingRequest with
        {
            EffectiveFrom = new DateOnly(2026, 6, 1), EffectiveTo = new DateOnly(2026, 12, 31)
        })).Status);
        Assert.Equal(ResultStatus.NotFound, (await operations.CreateMappingAsync(mappingRequest with { DeviceId = Guid.NewGuid() })).Status);

        await using (var other = createContext(new TestTenantContext(otherTenantId, actorId)))
        {
            var otherServices = CreateServices(other, otherTenantId, actorId);
            var otherMapping = await otherServices.Operations.CreateMappingAsync(new(
                otherDeviceId, "EXT-6F", otherEmployeeId, new(2026, 1, 1), null, AttendanceDeviceMappingStatus.Active));
            Assert.True(otherMapping.Succeeded, otherMapping.Message);
            otherMappingId = otherMapping.Value!.Id;
            var otherUnmapped = await otherServices.Ingestion.IngestAsync(new(otherDeviceId, "provider-cross-tenant", [
                new("evt-cross-tenant-issue", "EXT-B-MISSING", new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
            ]));
            Assert.True(otherUnmapped.Succeeded);
            otherIssueId = await other.AttendanceDeviceIngestionEvents.Where(x => x.ExternalEventId == "evt-cross-tenant-issue").Select(x => x.Id).SingleAsync();
            otherSyncRunId = otherUnmapped.Value!.SyncRunId;
        }
        var mappingRace = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var contender = createContext(new TestTenantContext(tenantId, actorId));
            var contenderOperations = CreateServices(contender, tenantId, actorId).Operations;
            try { return await contenderOperations.CreateMappingAsync(mappingRequest with { ExternalEmployeeIdentifier = "EXT-RACE" }); }
            catch (Exception) { return null; }
        }));
        Assert.Equal(1, mappingRace.Count(x => x?.Succeeded == true));
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.AttendanceDeviceEmployeeMappings.CountAsync(x => x.TenantId == tenantId && x.ExternalEmployeeIdentifier == "EXT-RACE" && x.Status == AttendanceDeviceMappingStatus.Active));

        var unmapped = await ingestion.IngestAsync(new(device.Value.Id, "provider-acceptance", [
            new("evt-unmapped-6f", "EXT-MISSING", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.FromHours(5.5)), AttendanceDevicePunchDirection.In)
        ], "checkpoint-unmapped"));
        Assert.True(unmapped.Succeeded);
        Assert.Equal(1, unmapped.Value!.Unmapped);
        Assert.Null(unmapped.Value.Checkpoint);
        Assert.Equal(0, await db.AttendancePunches.CountAsync(x => x.TenantId == tenantId));

        var accepted = await ingestion.IngestAsync(new(device.Value.Id, "provider-acceptance", [
            new("evt-in-6f", "EXT-6F", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.FromHours(5.5)), AttendanceDevicePunchDirection.In),
            new("evt-out-6f", "EXT-6F", new DateTimeOffset(2026, 10, 10, 18, 0, 0, TimeSpan.FromHours(5.5)), AttendanceDevicePunchDirection.Out)
        ], "checkpoint-accepted"));
        Assert.True(accepted.Succeeded, accepted.Message);
        Assert.Equal(2, accepted.Value!.Accepted);
        Assert.Equal("checkpoint-accepted", accepted.Value.Checkpoint);
        Assert.Equal(2, await db.AttendancePunches.CountAsync(x => x.TenantId == tenantId));
        var duplicateRace = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var contender = createContext(new TestTenantContext(tenantId, actorId));
            var contenderIngestion = CreateServices(contender, tenantId, actorId).Ingestion;
            try
            {
                return await contenderIngestion.IngestAsync(new(device.Value.Id, "provider-concurrent", [
                    new("evt-concurrent-6f", "EXT-6F", new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
                ]));
            }
            catch (Exception) { return null; }
        }));
        Assert.Contains(duplicateRace, x => x?.Value?.Accepted == 1);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.AttendancePunches.CountAsync(x => x.TenantId == tenantId && x.ExternalPunchId == $"{device.Value.Id:N}:evt-concurrent-6f"));
        Assert.Contains(await db.AttendancePunches.Where(x => x.TenantId == tenantId).ToListAsync(),
            x => x.Direction == PunchDirection.In && x.BusinessDate == new DateOnly(2026, 10, 10));

        var replay = await ingestion.IngestAsync(new(device.Value.Id, "provider-acceptance", [
            new("evt-in-6f", "EXT-6F", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.FromHours(5.5)), AttendanceDevicePunchDirection.In)
        ]));
        Assert.True(replay.Succeeded);
        Assert.Equal(1, replay.Value!.Duplicate);
        Assert.Equal(3, await db.AttendancePunches.CountAsync(x => x.TenantId == tenantId));

        var unknown = await ingestion.IngestAsync(new(device.Value.Id, "provider-acceptance", [
            new("evt-unknown-6f", "EXT-6F", new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.Unknown)
        ]));
        Assert.True(unknown.Succeeded);
        Assert.Equal(1, unknown.Value!.Rejected);
        Assert.Equal(AttendanceDevicePunchDirection.Unknown,
            await db.AttendanceDeviceIngestionEvents.Where(x => x.ExternalEventId == "evt-unknown-6f").Select(x => x.Direction).SingleAsync());

        var issue = await db.AttendanceDeviceIngestionEvents.SingleAsync(x => x.ExternalEventId == "evt-unmapped-6f");
        var missingMapping = await operations.CreateMappingAsync(mappingRequest with { ExternalEmployeeIdentifier = "EXT-MISSING" });
        Assert.True(missingMapping.Succeeded, missingMapping.Message);
        var processed = await operations.ReprocessIssueAsync(issue.Id);
        Assert.True(processed.Succeeded, processed.Message);
        Assert.True(processed.Value!.Accepted == 1, $"{processed.Message}; accepted={processed.Value.Accepted}; duplicate={processed.Value.Duplicate}; rejected={processed.Value.Rejected}; unmapped={processed.Value.Unmapped}");
        Assert.Equal(4, await db.AttendancePunches.CountAsync(x => x.TenantId == tenantId));
        var processedAgain = await operations.ReprocessIssueAsync(issue.Id);
        Assert.True(processedAgain.Succeeded);
        Assert.Equal(1, processedAgain.Value!.Duplicate);
        Assert.Equal(4, await db.AttendancePunches.CountAsync(x => x.TenantId == tenantId));

        // Finalized-period provider control: the issue is retained before close,
        // then reprocessing must require the existing controlled reopen lifecycle.
        var finalizedIssueResult = await ingestion.IngestAsync(new(device.Value.Id, "provider-finalized", [
            new("evt-finalized-6f", "EXT-FINALIZED", new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
        ]));
        Assert.True(finalizedIssueResult.Succeeded);
        Assert.Equal(1, finalizedIssueResult.Value!.Unmapped);
        db.EmployeeAttendanceDays.AddRange(Enumerable.Range(1, 30).Select(day => new EmployeeAttendanceDay
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            BusinessDate = new DateOnly(2026, 11, day), Status = EmployeeAttendanceDayStatus.Present,
            ExpectedWorkMinutes = 480, WorkedMinutes = 480, ProcessedAtUtc = DateTime.UtcNow,
            ProcessingOutcome = "Phase 6F provider finalization fixture"
        }));
        await db.SaveChangesAsync();
        var monthly = new AttendanceMonthlyProcessor(db, new TestTenantContext(tenantId));
        var period = await monthly.CreatePeriodAsync(new(2026, 11));
        Assert.True(period.Succeeded, period.Message);
        Assert.True((await monthly.ProcessAsync(period.Value!.Id)).Succeeded);
        Assert.True((await monthly.CloseAsync(period.Value.Id)).Succeeded);
        var snapshotBefore = await db.PayrollAttendanceSnapshots.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Value.Id && x.IsCurrent && x.EmployeeId == employeeId);
        var issueId = await db.AttendanceDeviceIngestionEvents.Where(x => x.ExternalEventId == "evt-finalized-6f").Select(x => x.Id).SingleAsync();
        var finalizedMapping = await operations.CreateMappingAsync(mappingRequest with { ExternalEmployeeIdentifier = "EXT-FINALIZED" });
        Assert.True(finalizedMapping.Succeeded, finalizedMapping.Message);
        var blocked = await operations.ReprocessIssueAsync(issueId);
        Assert.True(blocked.Succeeded, blocked.Message);
        Assert.Equal(1, blocked.Value!.Rejected);
        Assert.Equal(AttendanceDeviceIngestionStatus.RequiresPeriodReopen,
            await db.AttendanceDeviceIngestionEvents.Where(x => x.Id == issueId).Select(x => x.Status).SingleAsync());
        Assert.Equal(AttendancePeriodStatus.Closed, await db.AttendancePeriods.Where(x => x.Id == period.Value.Id).Select(x => x.Status).SingleAsync());
        var snapshotAfter = await db.PayrollAttendanceSnapshots.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Value.Id && x.IsCurrent && x.EmployeeId == employeeId);
        Assert.Equal(snapshotBefore.Id, snapshotAfter.Id);
        Assert.Equal(snapshotBefore.Version, snapshotAfter.Version);
        Assert.Equal(snapshotBefore.SourceHash, snapshotAfter.SourceHash);
        Assert.Equal(0, await db.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == period.Value.Id && x.IsCurrent && x.Id != snapshotBefore.Id));
        Assert.Equal(4, await db.AttendancePunches.CountAsync(x => x.TenantId == tenantId));

        // Complete provider-backed tenant matrix. Tenant B has persisted data in
        // every operational resource family before tenant A attempts misuse.
        await using (var other = createContext(new TestTenantContext(otherTenantId, actorId)))
        {
            var otherServices = CreateServices(other, otherTenantId, actorId);
            var otherShared = await otherServices.Ingestion.IngestAsync(new(otherDeviceId, "cross-tenant-shared", [
                new("evt-same-tenant-id", "EXT-6F", new DateTimeOffset(2026, 12, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
            ]));
            Assert.True(otherShared.Succeeded, otherShared.Message);
            Assert.Equal(1, otherShared.Value!.Accepted);
            Assert.Equal(1, await other.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{otherDeviceId:N}:evt-same-tenant-id"));
        }

        var tenantBDevice = await operations.GetDeviceAsync(otherDeviceId);
        Assert.Equal(ResultStatus.NotFound, tenantBDevice.Status);
        Assert.Equal(ResultStatus.NotFound, (await operations.UpdateDeviceAsync(otherDeviceId, new(
            "CROSS-TENANT", "Denied", "Terminal", "NoProvider", null, null, "Asia/Kolkata",
            AttendanceDeviceConnectionMode.FileImport, null))).Status);
        Assert.Equal(ResultStatus.NotFound, (await operations.SetDeviceStatusAsync(otherDeviceId, AttendanceDeviceStatus.Inactive)).Status);
        Assert.Empty((await operations.GetMappingsAsync(new(DeviceId: otherDeviceId))).Value!.Items);
        Assert.Equal(ResultStatus.NotFound, (await operations.CreateMappingAsync(new(
            otherDeviceId, "EXT-A-MISUSE", employeeId, new(2026, 1, 1), null, AttendanceDeviceMappingStatus.Active))).Status);
        Assert.Equal(ResultStatus.NotFound, (await operations.CreateMappingAsync(new(
            device.Value.Id, "EXT-B-MISUSE", otherEmployeeId, new(2026, 1, 1), null, AttendanceDeviceMappingStatus.Active))).Status);
        Assert.Equal(ResultStatus.NotFound, (await operations.UpdateMappingAsync(otherMappingId, new(
            otherDeviceId, "EXT-6F", otherEmployeeId, new(2026, 1, 1), null, AttendanceDeviceMappingStatus.Active))).Status);
        Assert.Equal(ResultStatus.NotFound, (await operations.DeactivateMappingAsync(otherMappingId)).Status);
        Assert.Empty((await operations.GetIssuesAsync(new(DeviceId: otherDeviceId))).Value!.Items);
        Assert.Equal(ResultStatus.NotFound, (await operations.ReprocessIssueAsync(otherIssueId)).Status);
        Assert.Empty((await operations.GetSyncRunsAsync(new(DeviceId: otherDeviceId))).Value!.Items);
        Assert.Empty((await operations.GetAuditHistoryAsync(new(DeviceId: otherDeviceId))).Value!.Items);
        Assert.Empty((await operations.GetAuditHistoryAsync(new(MappingId: otherMappingId))).Value!.Items);
        Assert.Equal(ResultStatus.NotFound, (await ingestion.IngestAsync(new(otherDeviceId, "cross-tenant-misuse", [
            new("evt-cross-tenant-misuse", "EXT-6F", new DateTimeOffset(2026, 12, 11, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
        ]))).Status);
        await using (var other = createContext(new TestTenantContext(otherTenantId)))
            Assert.Equal(1, await other.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{otherDeviceId:N}:evt-same-tenant-id"));

        var tenantAShared = await ingestion.IngestAsync(new(device.Value.Id, "cross-tenant-shared", [
            new("evt-same-tenant-id", "EXT-6F", new DateTimeOffset(2026, 12, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
        ]));
        Assert.True(tenantAShared.Succeeded, tenantAShared.Message);
        Assert.Equal(1, tenantAShared.Value!.Accepted);
        Assert.Equal(1, await db.AttendancePunches.CountAsync(x => x.ExternalPunchId == $"{device.Value.Id:N}:evt-same-tenant-id"));

        var runs = await operations.GetSyncRunsAsync(new(DeviceId: device.Value.Id, Page: 1, PageSize: 50));
        Assert.True(runs.Succeeded);
        Assert.True(runs.Value!.TotalCount >= 4);
        Assert.Contains(runs.Value.Items, x => x.CheckpointAfter == "checkpoint-accepted");
        var audit = await operations.GetAuditHistoryAsync(new(DeviceId: device.Value.Id));
        Assert.True(audit.Succeeded);
        Assert.Contains(audit.Value!.Items, x => x.Action == "DeviceCreated" && x.ActorUserId == actorId);
        Assert.Contains(audit.Value.Items, x => x.Action == "DeviceUpdated");
        Assert.Contains(audit.Value.Items, x => x.Action == "DeviceDeactivated");
        Assert.Contains(audit.Value.Items, x => x.Action == "DeviceActivated");
        Assert.Contains(audit.Value.Items, x => x.Action == "MappingCreated");
        Assert.DoesNotContain("vault://phase6f-safe-ref", string.Join("", audit.Value.Items.Select(x => x.ContextJson)));

        var pull = await operations.CreateDeviceAsync(new(
            "P6F-PULL", "Unsupported pull", "UnsupportedProvider", "UnsupportedProvider", null, null,
            "Asia/Kolkata", AttendanceDeviceConnectionMode.Pull, null));
        Assert.True(pull.Succeeded);
        var unsupported = await operations.SyncNowAsync(pull.Value!.Id);
        Assert.Equal(ResultStatus.Conflict, unsupported.Status);
        Assert.Contains("UnsupportedProvider", unsupported.Message);

        await using (var other = createContext(new TestTenantContext(otherTenantId, actorId)))
        {
            var otherOps = CreateServices(other, otherTenantId, actorId).Operations;
            var otherDevices = await otherOps.GetDevicesAsync(new(Search: "P6F-DEVICE-UPDATED"));
            Assert.True(otherDevices.Succeeded);
            Assert.Empty(otherDevices.Value!.Items);
            Assert.Equal(ResultStatus.NotFound, (await otherOps.GetDeviceAsync(device.Value.Id)).Status);
        }
    }

    private static (AttendanceDeviceOperationsService Operations, AttendanceDeviceIntegrationService Ingestion) CreateServices(HrmsDbContext db, Guid tenantId, Guid actorId)
    {
        var tenant = new TestTenantContext(tenantId, actorId);
        var roster = new AttendanceFoundationService(db, tenant, new EffectiveEmploymentResolver(db, tenant));
        var calendar = new WorkingDayCalendarResolver(db, tenant, new EffectiveEmploymentResolver(db, tenant));
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.Zero));
        var punch = new AttendancePunchIngestionService(db, tenant, roster, new AttendanceBusinessDateResolver(), new AttendanceBusinessTimeZoneProvider(), clock);
        var processor = new AttendanceDayProcessor(db, tenant, roster, clock);
        var ingestion = new AttendanceDeviceIntegrationService(db, tenant, punch, processor, clock);
        return (new AttendanceDeviceOperationsService(db, tenant, ingestion, []), ingestion);
    }

    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = $"{code.ToLowerInvariant()}.test", ShardKey = id.ToString("N"), Status = TenantStatus.Active };
    private static Employee Employee(Guid tenantId, Guid id, string code) => new() { Id = id, TenantId = tenantId, EmployeeCode = code, FirstName = "Phase", LastName = "SixF", Email = $"{id:N}@provider.test", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active };
    private static EmployeeEmploymentHistory Employment(Guid tenantId, Guid employeeId) => new() { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active };
    private static Shift Shift(Guid tenantId) => new() { Id = Guid.NewGuid(), TenantId = tenantId, ShiftCode = "P6F-DEFAULT", ShiftName = "P6F-DEFAULT", IsDefault = true, IsActive = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 1, CaptureMode = AttendanceCaptureMode.BiometricOnly, AllowedAttendanceSources = AttendanceSource.Biometric };
}

[Collection("Attendance MySQL")]
public sealed class MySqlAttendanceDeviceIntegrationProviderTests
{
    [Fact]
    public async Task MySql_phase6f_attendance_device_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("Phase 6F MySQL provider acceptance requires HRMS_MYSQL_TEST_CONNECTION.");
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var databaseName = $"HRMS_Phase6F_Integration_{Guid.NewGuid():N}";
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var target = new MySqlConnectionStringBuilder(normalized) { Database = databaseName };
        var created = false;
        try
        {
            await using (var catalog = new MySqlConnection(admin.ConnectionString))
            {
                await catalog.OpenAsync();
                await using var command = catalog.CreateCommand();
                command.CommandText = "CREATE DATABASE `" + databaseName + "`";
                await command.ExecuteNonQueryAsync();
                created = true;
            }
            await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(target.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, new TestTenantContext());
            await db.Database.MigrateAsync();
            var applied = await db.Database.GetAppliedMigrationsAsync();
            Assert.Equal(db.Database.GetMigrations(), applied);
            Assert.Contains(applied, x => x.EndsWith("_AddAttendanceDeviceIntegrationPhase6F", StringComparison.Ordinal));
            Assert.Contains(applied, x => x.EndsWith("_AddAttendanceDeviceAdministrationAuditPhase6F", StringComparison.Ordinal));
            await AttendanceDeviceIntegrationProviderAcceptance.RunAsync(
                tenant => new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(target.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant), "MySQL");
        }
        finally
        {
            if (created)
            {
                await using var catalog = new MySqlConnection(admin.ConnectionString);
                await catalog.OpenAsync();
                await using var drop = catalog.CreateCommand();
                drop.CommandText = "DROP DATABASE IF EXISTS `" + databaseName + "`";
                await drop.ExecuteNonQueryAsync();
                await using var verify = catalog.CreateCommand();
                verify.CommandText = "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @name";
                verify.Parameters.AddWithValue("@name", databaseName);
                Assert.Equal(0L, Convert.ToInt64(await verify.ExecuteScalarAsync()));
            }
        }
    }
}

public sealed class SqlServerAttendanceDeviceIntegrationProviderTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerAttendanceDeviceIntegrationProviderTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [Fact]
    public async Task SqlServer_phase6f_attendance_device_provider_acceptance()
    {
        if (!fixture.IsConfigured) throw SkipException.ForSkip("Phase 6F SQL Server provider acceptance requires HRMS_SQLSERVER_TEST_CONNECTION.");
        await using var disposable = await fixture.CreateDisposableDatabaseAsync("HRMS_Phase6F_Integration_");
        await using var probe = disposable.CreateContext(new TestTenantContext());
        var applied = await probe.Database.GetAppliedMigrationsAsync();
        Assert.Equal(probe.Database.GetMigrations(), applied);
        Assert.Contains(applied, x => x.EndsWith("_AddAttendanceDeviceIntegrationPhase6F", StringComparison.Ordinal));
        Assert.Contains(applied, x => x.EndsWith("_AddAttendanceDeviceAdministrationAuditPhase6F", StringComparison.Ordinal));
        await AttendanceDeviceIntegrationProviderAcceptance.RunAsync(disposable.CreateContext, "SQL Server");
    }
}
