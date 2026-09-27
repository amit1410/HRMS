using System.Data;
using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class AttendanceDeviceLeaseService(
    IHrmsDbContext db,
    ITenantContext tenant,
    TimeProvider clock,
    AttendanceDeviceWorkerOptions options) : IAttendanceDeviceLeaseService
{
    public async Task<AttendanceDeviceLeaseResult> TryAcquireAsync(Guid deviceId, string owner, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId;
        if (tenantId is null || tenantId == Guid.Empty) return new(false, null, "No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(owner)) return new(false, null, "Lease owner is required.");

        await using var transaction = await db.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            if (!await db.AttendanceDevices.AnyAsync(x => x.TenantId == tenantId && x.Id == deviceId, ct))
                return new(false, null, "DeviceNotFound");

            var now = clock.GetUtcNow().UtcDateTime;
            var lease = await db.AttendanceDeviceSyncLeases.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.AttendanceDeviceId == deviceId, ct);
            if (lease is not null && lease.LeaseToken is not null && lease.LeaseExpiresAtUtc > now)
            {
                await transaction.RollbackAsync(ct);
                return new(false, null, "DeviceSyncAlreadyRunning");
            }

            var token = Guid.NewGuid();
            if (lease is null)
            {
                lease = new AttendanceDeviceSyncLease
                {
                    Id = Guid.NewGuid(), TenantId = tenantId.Value, AttendanceDeviceId = deviceId,
                    LeaseOwner = owner.Trim(), LeaseToken = token, ClaimedAtUtc = now,
                    LastHeartbeatAtUtc = now, LeaseExpiresAtUtc = now.AddSeconds(options.LeaseDurationSeconds), Version = 1
                };
                db.AttendanceDeviceSyncLeases.Add(lease);
            }
            else
            {
                lease.LeaseOwner = owner.Trim(); lease.LeaseToken = token; lease.ClaimedAtUtc = now;
                lease.LastHeartbeatAtUtc = now; lease.LeaseExpiresAtUtc = now.AddSeconds(options.LeaseDurationSeconds);
                lease.Version++;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(true, new(deviceId, token, owner.Trim(), lease.LeaseExpiresAtUtc!.Value), null);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ClearChangeTracker();
            return new(false, null, "DeviceSyncAlreadyRunning");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ClearChangeTracker();
            return new(false, null, "DeviceSyncAlreadyRunning");
        }
    }

    public async Task<bool> HeartbeatAsync(Guid deviceId, Guid leaseToken, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId;
        if (tenantId is null || tenantId == Guid.Empty) return false;
        var lease = await db.AttendanceDeviceSyncLeases.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.AttendanceDeviceId == deviceId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (lease?.LeaseToken != leaseToken || lease.LeaseExpiresAtUtc <= now) return false;
        lease.LastHeartbeatAtUtc = now; lease.LeaseExpiresAtUtc = now.AddSeconds(options.LeaseDurationSeconds); lease.Version++;
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { db.ClearChangeTracker(); return false; }
    }

    public async Task<bool> ReleaseAsync(Guid deviceId, Guid leaseToken, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId;
        if (tenantId is null || tenantId == Guid.Empty) return false;
        var lease = await db.AttendanceDeviceSyncLeases.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.AttendanceDeviceId == deviceId, ct);
        if (lease?.LeaseToken != leaseToken) return false;
        lease.LeaseOwner = null; lease.LeaseToken = null; lease.ClaimedAtUtc = null; lease.LeaseExpiresAtUtc = null;
        lease.LastHeartbeatAtUtc = clock.GetUtcNow().UtcDateTime; lease.Version++;
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { db.ClearChangeTracker(); return false; }
    }
}
