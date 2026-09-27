using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Sharding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRMS.Infrastructure.Attendance;

/// <summary>Bounded, tenant-aware scheduler for active Pull devices.</summary>
public sealed class AttendanceDevicePullWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AttendanceDeviceWorkerOptions> configuredOptions,
    ILogger<AttendanceDevicePullWorker> logger) : BackgroundService
{
    private readonly AttendanceDeviceWorkerOptions _options = configuredOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Attendance device Pull worker is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollingIntervalSeconds));
        do
        {
            await ProcessTenantsAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessTenantsAsync(CancellationToken ct)
    {
        List<ShardDescriptor> tenants;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<IHrmsCatalogDbContext>();
            tenants = await catalog.Tenants.AsNoTracking()
                .Where(x => x.Status == Domain.Enums.TenantStatus.Active)
                .OrderBy(x => x.TenantCode)
                .ThenBy(x => x.Id)
                .Select(x => new ShardDescriptor(x.Id, x.TenantCode, x.Host, x.ShardKey, x.Status, x.DatabaseProvider))
                .ToListAsync(ct);
        }

        foreach (var tenant in tenants)
        {
            try { await ProcessTenantAsync(tenant, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger.LogError(exception, "Attendance device worker failed for tenant {TenantId}.", tenant.TenantId); }
        }
    }

    public async Task ProcessTenantAsync(ShardDescriptor tenant, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IShardContext>().Use(tenant);
        scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>().Use(tenant.TenantId);
        var operations = scope.ServiceProvider.GetRequiredService<IAttendanceDeviceOperationsService>();
        var execution = scope.ServiceProvider.GetRequiredService<IAttendanceDeviceSyncExecutionService>();
        var recovery = scope.ServiceProvider.GetRequiredService<IAttendanceDeviceRecoveryService>();
        await recovery.ReconcileStaleRunsAsync(ct);
        var processed = 0;
        var page = 1;

        while (processed < _options.MaxDevicesPerCycle)
        {
            ct.ThrowIfCancellationRequested();
            var pageSize = Math.Min(_options.DevicePageSize, _options.MaxDevicesPerCycle - processed);
            var result = await operations.GetDevicesAsync(
                new AttendanceDeviceQuery(page, pageSize, Status: Domain.Enums.AttendanceDeviceStatus.Active,
                    ConnectionMode: Domain.Enums.AttendanceDeviceConnectionMode.Pull,
                    EligibleAtUtc: DateTime.UtcNow), ct);
            if (!result.Succeeded || result.Value is null) break;
            foreach (var device in result.Value.Items)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var sync = await execution.ExecuteAsync(device.Id, ct);
                    if (!sync.Succeeded) logger.LogWarning("Attendance device sync did not succeed for {DeviceId}: {Message}", device.Id, sync.Message);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception exception) { logger.LogError(exception, "Attendance device sync failed for {DeviceId}.", device.Id); }
                processed++;
            }
            if (result.Value.Items.Count == 0 || page * pageSize >= result.Value.TotalCount) break;
            page++;
        }
    }
}
