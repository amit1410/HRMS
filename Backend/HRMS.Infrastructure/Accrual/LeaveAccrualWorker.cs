using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRMS.Infrastructure.Accrual;

/// <summary>Catch-up scheduler for policy accrual. Business processing remains in Application.</summary>
public sealed class LeaveAccrualWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LeaveAccrualOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<LeaveAccrualWorker> _logger;

    public LeaveAccrualWorker(IServiceScopeFactory scopeFactory, IOptions<LeaveAccrualOptions> options,
        TimeProvider clock, ILogger<LeaveAccrualWorker> logger)
    { _scopeFactory = scopeFactory; _options = options.Value; _clock = clock; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            await ProcessTenantsAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessTenantsAsync(CancellationToken ct)
    {
        List<ShardDescriptor> tenants;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<IHrmsCatalogDbContext>();
            tenants = await catalog.Tenants.AsNoTracking().Where(x => x.Status == Domain.Enums.TenantStatus.Active)
                .Select(x => new ShardDescriptor(x.Id, x.TenantCode, x.Host, x.ShardKey, x.Status, x.DatabaseProvider)).ToListAsync(ct);
        }
        foreach (var tenant in tenants)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<IShardContext>().Use(tenant);
                scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>().Use(tenant.TenantId);
                var result = await scope.ServiceProvider.GetRequiredService<ILeaveAccrualProcessor>()
                    .ProcessAsync(DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime), ct);
                var expiry = await scope.ServiceProvider.GetRequiredService<ILeaveEntitlementExpiryProcessor>()
                    .ProcessAsync(DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime), ct);
                _logger.LogInformation("Processed Leave accruals for tenant {TenantId}: {Processed} processed, {Skipped} skipped, {Failed} failed, {Credited} credited, {Expired} expired.", tenant.TenantId, result.Processed, result.Skipped, result.Failed, result.CreditedQuantity, expiry.ExpiredQuantity);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) { _logger.LogError(exception, "Leave accrual processing failed for tenant {TenantId}.", tenant.TenantId); }
        }
    }
}
