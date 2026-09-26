using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceDeviceIntegrationTests
{
    [Fact]
    public async Task Ingested_device_event_is_idempotent_and_reprocesses_authoritative_day()
    {
        using var f = await CreateFixtureAsync();
        var device = Device(f);
        var mapping = new AttendanceDeviceEmployeeMapping
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, AttendanceDeviceId = device.Id,
            ExternalEmployeeIdentifier = "EXT-1", EmployeeId = f.EmployeeId,
            EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active
        };
        f.Context.AttendanceDevices.Add(device);
        f.Context.AttendanceDeviceEmployeeMappings.Add(mapping);
        await f.Context.SaveChangesAsync();

        var service = Service(f);
        var input = new NormalizedAttendancePunch("evt-1", "EXT-1",
            new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.FromHours(5.5)), AttendanceDevicePunchDirection.In);
        var first = await service.IngestAsync(new(device.Id, "neutral-test", [input], "cursor-1"));
        var replay = await service.IngestAsync(new(device.Id, "neutral-test", [input], "cursor-2"));

        Assert.True(first.Succeeded, first.Message);
        Assert.True(replay.Succeeded, replay.Message);
        Assert.Equal(1, first.Value!.Accepted);
        Assert.Equal(1, replay.Value!.Duplicate);
        Assert.Equal(1, await f.Context.AttendancePunches.CountAsync());
        Assert.Equal(AttendanceDeviceSyncStatus.Succeeded, await f.Context.AttendanceDeviceSyncRuns.Where(x => x.Id == first.Value.SyncRunId).Select(x => x.Status).SingleAsync());
        var receipt = await f.Context.AttendanceDeviceIngestionEvents.SingleAsync(x => x.AttendanceDeviceSyncRunId == first.Value.SyncRunId);
        Assert.Equal(AttendanceDeviceIngestionStatus.Accepted, receipt.Status);
        Assert.NotNull(receipt.AttendancePunchId);
        Assert.Equal("cursor-2", await f.Context.AttendanceDevices.Where(x => x.Id == device.Id).Select(x => x.LastSuccessfulCheckpoint).SingleAsync());
        Assert.True(await f.Context.EmployeeAttendanceDays.AnyAsync(x => x.TenantId == f.TenantId && x.EmployeeId == f.EmployeeId && x.BusinessDate == new DateOnly(2026, 10, 10)));
    }

    [Fact]
    public async Task Unmapped_and_unknown_direction_events_are_retained_without_raw_punches()
    {
        using var f = await CreateFixtureAsync();
        var device = Device(f);
        f.Context.AttendanceDevices.Add(device);
        f.Context.AttendanceDeviceEmployeeMappings.Add(new AttendanceDeviceEmployeeMapping
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, AttendanceDeviceId = device.Id,
            ExternalEmployeeIdentifier = "MAPPED-UNKNOWN", EmployeeId = f.EmployeeId,
            EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active
        });
        await f.Context.SaveChangesAsync();
        var service = Service(f);
        var date = new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);
        var result = await service.IngestAsync(new(device.Id, "file-test", [
            new("unmapped", "NO-MAP", date, AttendanceDevicePunchDirection.In),
            new("unknown", "MAPPED-UNKNOWN", date, AttendanceDevicePunchDirection.Unknown)
        ], "next"));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Value!.Unmapped);
        Assert.Equal(1, result.Value.Rejected);
        Assert.Empty(await f.Context.AttendancePunches.ToListAsync());
        Assert.Equal(2, await f.Context.AttendanceDeviceIngestionEvents.CountAsync());
        Assert.Equal(2, await f.Context.AttendanceDeviceSyncRuns.Where(x => x.Id == result.Value.SyncRunId).Select(x => x.ReceivedCount).SingleAsync());
        Assert.Null(result.Value.Checkpoint);
    }

    [Fact]
    public async Task Cross_tenant_device_is_not_found()
    {
        using var f = await CreateFixtureAsync();
        var foreign = Guid.NewGuid();
        f.Context.Tenants.Add(new Tenant { Id = foreign, TenantCode = $"T-{foreign:N}"[..20], Host = $"{foreign:N}.test", ShardKey = foreign.ToString("N"), TenantName = "Other" });
        var device = Device(f);
        f.Context.AttendanceDevices.Add(device);
        await f.Context.SaveChangesAsync();
        f.SwitchTenant(foreign);

        var result = await Service(f).IngestAsync(new(device.Id, "test", [], null));

        Assert.False(result.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.NotFound, result.Status);
    }

    private static AttendanceDeviceIntegrationService Service(AttendanceTestFixture f)
    {
        var ingestion = new AttendancePunchIngestionService(f.Context, f.TenantContext, f.CalendarService,
            new AttendanceBusinessDateResolver(), new AttendanceBusinessTimeZoneProvider(),
            new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)));
        var processor = new AttendanceDayProcessor(f.Context, f.TenantContext, f.CalendarService,
            new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)));
        return new(f.Context, f.TenantContext, ingestion, processor,
            new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)));
    }

    private static AttendanceDevice Device(AttendanceTestFixture f) => new()
    {
        Id = Guid.NewGuid(), TenantId = f.TenantId, Code = "BIO-1", Name = "Biometric 1",
        DeviceType = "Terminal", TimeZoneId = "Asia/Kolkata", ConnectionMode = AttendanceDeviceConnectionMode.FileImport,
        Status = AttendanceDeviceStatus.Active
    };

    private static async Task<AttendanceTestFixture> CreateFixtureAsync()
    {
        var f = await AttendanceTestFixture.CreateAsync();
        await f.AddShiftAsync("DEVICE", isDefault: true);
        return f;
    }
}
