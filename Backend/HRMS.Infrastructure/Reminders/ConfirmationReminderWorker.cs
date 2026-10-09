using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRMS.Infrastructure.Reminders;

/// <summary>
/// Hosted worker for the Phase 9b confirmation/probation reminder scheduler. It wakes on a fixed host
/// interval, enumerates active tenants, and runs <see cref="IConfirmationReminderProcessor"/> once per tenant
/// in that tenant's shard scope. Per-tenant enable/frequency gating lives in the processor, so this worker
/// stays a thin, provider-agnostic dispatcher — exactly like <see cref="LeaveApprovalReminderWorker"/>.
/// </summary>
public sealed class ConfirmationReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConfirmationReminderOptions _options;
    private readonly ILogger<ConfirmationReminderWorker> _logger;

    public ConfirmationReminderWorker(IServiceScopeFactory scopeFactory, ConfirmationReminderOptions options, ILogger<ConfirmationReminderWorker> logger)
    { _scopeFactory = scopeFactory; _options = options; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Confirmation/probation reminders are disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.WorkerIntervalMinutes));
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
            tenants = await catalog.Tenants.AsNoTracking()
                .Where(x => x.Status == Domain.Enums.TenantStatus.Active)
                .Select(x => new ShardDescriptor(x.Id, x.TenantCode, x.Host, x.ShardKey, x.Status, x.DatabaseProvider))
                .ToListAsync(ct);
        }

        foreach (var tenant in tenants)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<IShardContext>().Use(tenant);
                var execution = scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>();
                execution.Use(tenant.TenantId);
                var result = await scope.ServiceProvider.GetRequiredService<IConfirmationReminderProcessor>().ProcessAsync(ct);
                if (result.Cases > 0 || result.Sent > 0 || result.Failed > 0)
                    _logger.LogInformation("Processed confirmation reminders for tenant {TenantId}: {Cases} cases, {Sent} sent, {Skipped} skipped, {Failed} failed.", tenant.TenantId, result.Cases, result.Sent, result.Skipped, result.Failed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Confirmation reminder processing failed for tenant {TenantId}.", tenant.TenantId);
            }
        }
    }
}
