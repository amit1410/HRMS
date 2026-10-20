using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRMS.Infrastructure.Communication;

public sealed class WelcomeInvitationWorker(IServiceScopeFactory scopes, WelcomeInvitationOptions options, ILogger<WelcomeInvitationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Max(1, options.WorkerIntervalSeconds);
        logger.LogInformation("Welcome invitation worker starting. Enabled={Enabled} ProcessingIntervalSeconds={ProcessingIntervalSeconds}.", options.Enabled, intervalSeconds);
        if (!options.Enabled)
        {
            logger.LogInformation("Welcome invitation worker is disabled; no processing cycles will run.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        try
        {
            do
            {
                var cycleId = Guid.NewGuid();
                logger.LogInformation("Welcome invitation worker cycle started. CycleId={CycleId}.", cycleId);
                try
                {
                    await ProcessTenantsAsync(cycleId, stoppingToken);
                    logger.LogInformation("Welcome invitation worker cycle completed. CycleId={CycleId}.", cycleId);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogError("Welcome invitation worker cycle failed before completion. CycleId={CycleId} ExceptionType={ExceptionType}.", cycleId, ex.GetType().Name);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Welcome invitation worker stopping after cancellation.");
        }
        catch (Exception ex)
        {
            logger.LogCritical("Welcome invitation worker terminated unexpectedly. ExceptionType={ExceptionType}.", ex.GetType().Name);
            throw;
        }
    }

    private async Task ProcessTenantsAsync(Guid cycleId, CancellationToken ct)
    {
        await using var catalogScope = scopes.CreateAsyncScope();
        var tenants = await catalogScope.ServiceProvider.GetRequiredService<IHrmsCatalogDbContext>().Tenants.AsNoTracking()
            .Where(x => x.Status == Domain.Enums.TenantStatus.Active)
            .Select(x => new ShardDescriptor(x.Id, x.TenantCode, x.Host, x.ShardKey, x.Status, x.DatabaseProvider)).ToListAsync(ct);
        var web1Included = tenants.Any(x => string.Equals(x.TenantCode, "WEB1", StringComparison.OrdinalIgnoreCase));
        logger.LogInformation("Welcome invitation worker discovered active tenants. CycleId={CycleId} ActiveTenantCount={ActiveTenantCount} WEB1Included={WEB1Included}.", cycleId, tenants.Count, web1Included);
        foreach (var tenant in tenants)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<IShardContext>().Use(tenant);
                scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>().Use(tenant.TenantId);
                await scope.ServiceProvider.GetRequiredService<IWelcomeInvitationProcessor>().ProcessAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogError("Welcome invitation processing failed for tenant. CycleId={CycleId} TenantId={TenantId} ExceptionType={ExceptionType}.", cycleId, tenant.TenantId, ex.GetType().Name); }
        }
    }
}
