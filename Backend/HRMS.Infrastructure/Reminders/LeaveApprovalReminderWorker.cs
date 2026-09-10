using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRMS.Infrastructure.Reminders;

public sealed class LeaveApprovalReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LeaveReminderOptions _options;
    private readonly ILogger<LeaveApprovalReminderWorker> _logger;

    public LeaveApprovalReminderWorker(IServiceScopeFactory scopeFactory, IOptions<LeaveReminderOptions> options, ILogger<LeaveApprovalReminderWorker> logger)
    { _scopeFactory = scopeFactory; _options = options.Value; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Leave approval reminders are disabled.");
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
                var result = await scope.ServiceProvider.GetRequiredService<ILeaveApprovalReminderProcessor>().ProcessAsync(ct);
                _logger.LogInformation("Processed Leave reminders for tenant {TenantId}: {Sent} sent, {Skipped} skipped, {Failed} failed.", tenant.TenantId, result.Sent, result.Skipped, result.Failed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Leave reminder processing failed for tenant {TenantId}.", tenant.TenantId);
            }
        }
    }
}
