using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class AttendanceDeviceHealthService(
    IHrmsDbContext db,
    ITenantContext tenant,
    AttendanceDeviceWorkerOptions options,
    TimeProvider clock) : IAttendanceDeviceHealthService
{
    public async Task<Result<AttendanceDeviceHealthDto>> GetAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty)
            return Result<AttendanceDeviceHealthDto>.Unauthorized("No authenticated tenant.");
        var now = clock.GetUtcNow().UtcDateTime;
        var recent = now.AddHours(-24);
        var activeDevices = await db.AttendanceDevices.CountAsync(x => x.TenantId == tenantId && x.Status == AttendanceDeviceStatus.Active, ct);
        var activeLeases = await db.AttendanceDeviceSyncLeases.CountAsync(x => x.TenantId == tenantId && x.LeaseToken != null && x.LeaseExpiresAtUtc > now, ct);
        var staleLeases = await db.AttendanceDeviceSyncLeases.CountAsync(x => x.TenantId == tenantId && x.LeaseToken != null && x.LeaseExpiresAtUtc <= now, ct);
        var staleRuns = await db.AttendanceDeviceSyncRuns.CountAsync(x => x.TenantId == tenantId && x.Status == AttendanceDeviceSyncStatus.Running && x.StartedAtUtc <= now.AddSeconds(-options.StaleRunThresholdSeconds), ct);
        var recentFailures = await db.AttendanceDeviceSyncRuns.CountAsync(x => x.TenantId == tenantId && x.Status == AttendanceDeviceSyncStatus.Failed && x.CompletedAtUtc >= recent, ct);
        return Result<AttendanceDeviceHealthDto>.Success(new(options.Enabled, activeDevices, activeLeases, staleLeases, staleRuns, recentFailures));
    }
}
