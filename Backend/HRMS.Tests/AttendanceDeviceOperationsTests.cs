using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceDeviceOperationsTests
{
    [Fact]
    public async Task Device_crud_is_tenant_scoped_and_never_returns_credential_reference()
    {
        using var f = await FixtureAsync();
        var service = Operations(f);
        var request = DeviceRequest("BIO-A", "vault://credential/opaque");

        var created = await service.CreateDeviceAsync(request);
        Assert.True(created.Succeeded, created.Message);
        Assert.DoesNotContain("CredentialReference", typeof(AttendanceDeviceDto).GetProperties().Select(x => x.Name));
        Assert.DoesNotContain("vault://", System.Text.Json.JsonSerializer.Serialize(created.Value));
        Assert.Equal(AttendanceDeviceStatus.Active, created.Value!.Status);

        var duplicate = await service.CreateDeviceAsync(request);
        Assert.Equal(ResultStatus.Conflict, duplicate.Status);

        var otherTenantId = Guid.NewGuid();
        f.Context.Tenants.Add(new Tenant { Id = otherTenantId, TenantCode = $"T-{otherTenantId:N}"[..20], Host = $"{otherTenantId:N}.test", ShardKey = otherTenantId.ToString("N"), TenantName = "Other" });
        await f.Context.SaveChangesAsync();
        f.SwitchTenant(otherTenantId);
        Assert.Equal(ResultStatus.NotFound, (await service.GetDeviceAsync(created.Value.Id)).Status);
        Assert.True((await service.CreateDeviceAsync(request)).Succeeded);
    }

    [Fact]
    public async Task Device_deactivation_is_terminal_and_pull_without_provider_is_explicit()
    {
        using var f = await FixtureAsync();
        var service = Operations(f);
        var created = await service.CreateDeviceAsync(DeviceRequest("BIO-B"));
        Assert.True(created.Succeeded);
        Assert.True((await service.SetDeviceStatusAsync(created.Value!.Id, AttendanceDeviceStatus.Disabled)).Succeeded);
        Assert.Equal(ResultStatus.Conflict, (await service.SetDeviceStatusAsync(created.Value.Id, AttendanceDeviceStatus.Active)).Status);
        Assert.Equal(ResultStatus.Conflict, (await service.SyncNowAsync(created.Value.Id)).Status);

        var pullDevice = await service.CreateDeviceAsync(DeviceRequest("BIO-PULL", mode: AttendanceDeviceConnectionMode.Pull));
        Assert.True(pullDevice.Succeeded);
        var unsupported = await service.SyncNowAsync(pullDevice.Value!.Id);
        Assert.Equal(ResultStatus.Conflict, unsupported.Status);
        Assert.Contains("UnsupportedProvider", unsupported.Message);
    }

    [Fact]
    public async Task Mapping_identity_is_tenant_safe_and_overlapping_active_mapping_is_denied()
    {
        using var f = await FixtureAsync();
        var service = Operations(f);
        var device = await service.CreateDeviceAsync(DeviceRequest("BIO-C"));
        Assert.True(device.Succeeded);
        var request = new AttendanceDeviceMappingRequest(device.Value!.Id, "EXT-C", f.EmployeeId,
            new(2026, 1, 1), null, AttendanceDeviceMappingStatus.Active);
        var first = await service.CreateMappingAsync(request);
        Assert.True(first.Succeeded, first.Message);
        Assert.Equal(ResultStatus.Conflict, (await service.CreateMappingAsync(request)).Status);

        var alteredIdentity = request with { EmployeeId = Guid.NewGuid() };
        Assert.Equal(ResultStatus.Conflict, (await service.UpdateMappingAsync(first.Value!.Id, alteredIdentity)).Status);
        Assert.True((await service.DeactivateMappingAsync(first.Value.Id)).Succeeded);
        Assert.Equal(1, (await service.GetMappingsAsync(new(DeviceId: device.Value.Id))).Value!.TotalCount);
    }

    [Fact]
    public async Task Unmapped_issue_is_paged_then_reprocessed_once_after_mapping_is_added()
    {
        using var f = await FixtureAsync();
        var device = DeviceEntity(f, "BIO-D");
        f.Context.AttendanceDevices.Add(device);
        await f.Context.SaveChangesAsync();
        var ingestion = Ingestion(f);
        var received = await ingestion.IngestAsync(new(device.Id, "test", [
            new("evt-unmapped", "EXT-D", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
        ]));
        Assert.True(received.Succeeded);
        Assert.Equal(1, received.Value!.Unmapped);
        var operations = Operations(f, ingestion);
        var page = await operations.GetIssuesAsync(new(Status: AttendanceDeviceIngestionStatus.Unmapped, Page: 1, PageSize: 1));
        Assert.True(page.Succeeded);
        Assert.Equal(1, page.Value!.TotalCount);
        Assert.Single(page.Value.Items);

        f.Context.AttendanceDeviceEmployeeMappings.Add(new AttendanceDeviceEmployeeMapping
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, AttendanceDeviceId = device.Id,
            ExternalEmployeeIdentifier = "EXT-D", EmployeeId = f.EmployeeId,
            EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active
        });
        await f.Context.SaveChangesAsync();
        var issueId = page.Value.Items[0].Id;
        var processed = await operations.ReprocessIssueAsync(issueId);
        Assert.True(processed.Succeeded, processed.Message);
        Assert.Equal(1, processed.Value!.Accepted);
        Assert.Equal(1, await f.Context.AttendancePunches.CountAsync());
        var replay = await operations.ReprocessIssueAsync(issueId);
        Assert.True(replay.Succeeded);
        Assert.Equal(1, replay.Value!.Duplicate);
        Assert.Equal(1, await f.Context.AttendancePunches.CountAsync());
        Assert.Equal(AttendanceDeviceIngestionStatus.Accepted,
            await f.Context.AttendanceDeviceIngestionEvents.Where(x => x.Id == issueId).Select(x => x.Status).SingleAsync());
        Assert.Equal(2, (await operations.GetSyncRunsAsync(new(DeviceId: device.Id))).Value!.TotalCount);
    }

    [Fact]
    public async Task Cross_tenant_issue_and_mapping_operations_are_not_visible()
    {
        using var f = await FixtureAsync();
        var device = DeviceEntity(f, "BIO-ISOLATED");
        f.Context.AttendanceDevices.Add(device);
        await f.Context.SaveChangesAsync();
        var ingestion = Ingestion(f);
        var ingested = await ingestion.IngestAsync(new(device.Id, "security-test", [
            new("evt-private", "EXT-PRIVATE", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
        ]));
        Assert.True(ingested.Succeeded);
        var issueId = await f.Context.AttendanceDeviceIngestionEvents.Select(x => x.Id).SingleAsync();
        var otherTenantId = Guid.NewGuid();
        f.Context.Tenants.Add(new Tenant { Id = otherTenantId, TenantCode = $"T-{otherTenantId:N}"[..20], Host = $"{otherTenantId:N}.test", ShardKey = otherTenantId.ToString("N"), TenantName = "Other" });
        await f.Context.SaveChangesAsync();
        f.SwitchTenant(otherTenantId);
        var operations = Operations(f, ingestion);
        Assert.Equal(0, (await operations.GetIssuesAsync(new())).Value!.TotalCount);
        Assert.Equal(0, (await operations.GetSyncRunsAsync(new())).Value!.TotalCount);
        Assert.Equal(ResultStatus.NotFound, (await operations.ReprocessIssueAsync(issueId)).Status);
        Assert.Equal(ResultStatus.NotFound, (await operations.CreateMappingAsync(new(device.Id, "EXT-PRIVATE", f.EmployeeId,
            new(2026, 1, 1), null, AttendanceDeviceMappingStatus.Active))).Status);
    }

    [Fact]
    public async Task Device_mutations_write_atomic_tenant_actor_audit_without_credential_values()
    {
        using var f = await FixtureAsync();
        var service = Operations(f);
        var created = await service.CreateDeviceAsync(DeviceRequest("BIO-AUDIT", "vault://secret-value-one"));
        Assert.True(created.Succeeded);
        Assert.Equal(1, await f.Context.AttendanceDeviceAuditEvents.CountAsync());
        var createAudit = await f.Context.AttendanceDeviceAuditEvents.SingleAsync();
        Assert.Equal(f.TenantId, createAudit.TenantId);
        Assert.Equal(f.TenantContext.UserId, createAudit.ActorUserId);
        Assert.Equal("DeviceCreated", createAudit.Action);
        Assert.DoesNotContain("vault://secret-value-one", createAudit.ContextJson);

        var update = DeviceRequest("BIO-AUDIT-UPDATED", "vault://secret-value-two");
        Assert.True((await service.UpdateDeviceAsync(created.Value!.Id, update)).Succeeded);
        Assert.True((await service.SetDeviceStatusAsync(created.Value.Id, AttendanceDeviceStatus.Inactive)).Succeeded);
        Assert.True((await service.SetDeviceStatusAsync(created.Value.Id, AttendanceDeviceStatus.Active)).Succeeded);
        var audit = await service.GetAuditHistoryAsync(new(DeviceId: created.Value.Id));
        Assert.Equal(4, audit.Value!.TotalCount);
        Assert.Contains(audit.Value.Items, x => x.Action == "DeviceUpdated");
        Assert.Contains(audit.Value.Items, x => x.Action == "DeviceDeactivated");
        Assert.Contains(audit.Value.Items, x => x.Action == "DeviceActivated");
        Assert.DoesNotContain("vault://secret-value-two", string.Join("", audit.Value.Items.Select(x => x.ContextJson)));

        var failed = await service.CreateDeviceAsync(DeviceRequest("", "another-secret-value"));
        Assert.False(failed.Succeeded);
        Assert.Equal(4, await f.Context.AttendanceDeviceAuditEvents.CountAsync());
    }

    [Fact]
    public async Task Mapping_mutations_are_audited_and_history_is_tenant_scoped()
    {
        using var f = await FixtureAsync();
        var service = Operations(f);
        var device = await service.CreateDeviceAsync(DeviceRequest("BIO-MAP-AUDIT"));
        Assert.True(device.Succeeded);
        var map = new AttendanceDeviceMappingRequest(device.Value!.Id, "EXT-AUDIT", f.EmployeeId,
            new(2026, 1, 1), null, AttendanceDeviceMappingStatus.Active);
        var created = await service.CreateMappingAsync(map);
        Assert.True(created.Succeeded);
        Assert.True((await service.UpdateMappingAsync(created.Value!.Id, map with { EffectiveFrom = new(2026, 2, 1) })).Succeeded);
        Assert.True((await service.DeactivateMappingAsync(created.Value.Id)).Succeeded);
        var history = await service.GetAuditHistoryAsync(new(MappingId: created.Value.Id));
        Assert.Equal(3, history.Value!.TotalCount);
        Assert.All(history.Value.Items, row =>
        {
            Assert.Equal(f.TenantId, f.Context.AttendanceDeviceAuditEvents.Single(x => x.Id == row.Id).TenantId);
            Assert.Equal(f.TenantContext.UserId, row.ActorUserId);
        });
        Assert.Contains(history.Value.Items, x => x.Action == "MappingCreated");
        Assert.Contains(history.Value.Items, x => x.Action == "MappingUpdated");
        Assert.Contains(history.Value.Items, x => x.Action == "MappingDeactivated");

        var foreignTenant = Guid.NewGuid();
        f.Context.Tenants.Add(new Tenant { Id = foreignTenant, TenantCode = $"T-{foreignTenant:N}"[..20], Host = $"{foreignTenant:N}.test", ShardKey = foreignTenant.ToString("N"), TenantName = "Other" });
        await f.Context.SaveChangesAsync();
        f.SwitchTenant(foreignTenant);
        Assert.Equal(0, (await service.GetAuditHistoryAsync(new())).Value!.TotalCount);
        using var ownerContext = f.CreateContext(f.TenantId, out _);
        Assert.Equal(4, await ownerContext.AttendanceDeviceAuditEvents.CountAsync());
    }

    [Fact]
    public async Task Finalized_period_issue_reprocessing_is_blocked_without_mutating_snapshot_or_raw_punches()
    {
        using var f = await FixtureAsync();
        var device = DeviceEntity(f, "BIO-FINALIZED");
        f.Context.AttendanceDevices.Add(device);
        await f.Context.SaveChangesAsync();
        var ingestion = Ingestion(f);
        var received = await ingestion.IngestAsync(new(device.Id, "finalized-control", [
            new("evt-late", "EXT-LATE", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero), AttendanceDevicePunchDirection.In)
        ]));
        Assert.Equal(1, received.Value!.Unmapped);
        f.Context.AttendanceDeviceEmployeeMappings.Add(new AttendanceDeviceEmployeeMapping
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, AttendanceDeviceId = device.Id,
            ExternalEmployeeIdentifier = "EXT-LATE", EmployeeId = f.EmployeeId,
            EffectiveFrom = new(2026, 1, 1), Status = AttendanceDeviceMappingStatus.Active
        });
        await f.Context.SaveChangesAsync();

        var monthly = new AttendanceMonthlyProcessor(f.Context, f.TenantContext);
        var period = await monthly.CreatePeriodAsync(new(2026, 10));
        Assert.True(period.Succeeded, period.Message);
        Assert.True((await monthly.ProcessAsync(period.Value!.Id)).Succeeded);
        Assert.True((await monthly.CloseAsync(period.Value.Id)).Succeeded);
        var snapshotBefore = await f.Context.PayrollAttendanceSnapshots.SingleAsync(x => x.AttendancePeriodId == period.Value.Id && x.IsCurrent);
        var immutableSnapshot = new { snapshotBefore.Id, snapshotBefore.Version, snapshotBefore.IsCurrent, snapshotBefore.PayableDays, snapshotBefore.SourceHash };

        var issueId = await f.Context.AttendanceDeviceIngestionEvents.Where(x => x.ExternalEventId == "evt-late").Select(x => x.Id).SingleAsync();
        var reprocessed = await Operations(f, ingestion).ReprocessIssueAsync(issueId);
        Assert.True(reprocessed.Succeeded, reprocessed.Message);
        Assert.Equal(1, reprocessed.Value!.Rejected);
        Assert.Equal(0, await f.Context.AttendancePunches.CountAsync());
        var issue = await f.Context.AttendanceDeviceIngestionEvents.SingleAsync(x => x.Id == issueId);
        Assert.Equal(AttendanceDeviceIngestionStatus.RequiresPeriodReopen, issue.Status);
        Assert.Null(issue.AttendancePunchId);
        Assert.Equal(AttendancePeriodStatus.Closed, (await f.Context.AttendancePeriods.SingleAsync(x => x.Id == period.Value.Id)).Status);
        var snapshotAfter = await f.Context.PayrollAttendanceSnapshots.SingleAsync(x => x.AttendancePeriodId == period.Value.Id && x.IsCurrent);
        Assert.Equal(immutableSnapshot, new { snapshotAfter.Id, snapshotAfter.Version, snapshotAfter.IsCurrent, snapshotAfter.PayableDays, snapshotAfter.SourceHash });
        var snapshots = await f.Context.PayrollAttendanceSnapshots.Where(x => x.AttendancePeriodId == period.Value.Id).ToListAsync();
        Assert.Single(snapshots);
        Assert.Single(snapshots, x => x.IsCurrent);
    }

    [Fact]
    public void Canonical_device_grants_exclude_employee_and_manager_and_allow_time_manager_operations()
    {
        var map = SeedData.RolePermissionMap;
        Assert.DoesNotContain(Permissions.Attendance.DeviceView, map[RoleNames.Employee]);
        Assert.DoesNotContain(Permissions.Attendance.DeviceManage, map[RoleNames.Employee]);
        Assert.DoesNotContain(Permissions.Attendance.DeviceManage, map[RoleNames.Manager]);
        Assert.Contains(Permissions.Attendance.DeviceView, map[RoleNames.TimeManager]);
        Assert.Contains(Permissions.Attendance.DeviceSync, map[RoleNames.TimeManager]);
        Assert.Contains(Permissions.Attendance.DeviceMapping, map[RoleNames.TimeManager]);
        Assert.DoesNotContain(Permissions.Attendance.DeviceManage, map[RoleNames.TimeManager]);
        Assert.Contains(Permissions.Attendance.DeviceManage, map[RoleNames.TenantAdmin]);
    }

    [Fact]
    public void Device_api_actions_require_the_canonical_permission_for_each_operation()
    {
        Assert.NotNull(typeof(AttendanceDevicesController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).SingleOrDefault());
        var actions = typeof(AttendanceDevicesController).GetMethods().Where(x => x.DeclaringType == typeof(AttendanceDevicesController));
        Assert.NotEmpty(actions);
        Assert.All(actions, action => Assert.NotNull(action.GetCustomAttributes(typeof(HasPermissionAttribute), true).SingleOrDefault()));
    }

    private static AttendanceDeviceRequest DeviceRequest(string code, string? credential = null,
        AttendanceDeviceConnectionMode mode = AttendanceDeviceConnectionMode.FileImport) =>
        new(code, code, "Terminal", "NoProvider", null, null, "Asia/Kolkata", mode, credential);

    private static AttendanceDevice DeviceEntity(AttendanceTestFixture f, string code) => new()
    {
        Id = Guid.NewGuid(), TenantId = f.TenantId, Code = code, Name = code, DeviceType = "Terminal",
        TimeZoneId = "Asia/Kolkata", ConnectionMode = AttendanceDeviceConnectionMode.FileImport,
        Status = AttendanceDeviceStatus.Active
    };

    private static AttendanceDeviceOperationsService Operations(AttendanceTestFixture f, IAttendanceDeviceIntegrationService? ingestion = null) =>
        new(f.Context, f.TenantContext, ingestion ?? Ingestion(f), []);

    private static AttendanceDeviceIntegrationService Ingestion(AttendanceTestFixture f)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero));
        var punch = new AttendancePunchIngestionService(f.Context, f.TenantContext, f.CalendarService,
            new AttendanceBusinessDateResolver(), new AttendanceBusinessTimeZoneProvider(), clock);
        var processor = new AttendanceDayProcessor(f.Context, f.TenantContext, f.CalendarService, clock);
        return new(f.Context, f.TenantContext, punch, processor, clock);
    }

    private static async Task<AttendanceTestFixture> FixtureAsync()
    {
        var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("DEVICE-OPS", isDefault: true);
        return fixture;
    }
}
