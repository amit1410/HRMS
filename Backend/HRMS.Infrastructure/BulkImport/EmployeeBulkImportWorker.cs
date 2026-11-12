using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRMS.Infrastructure.BulkImport;

/// <summary>Polls one configured active tenant for one configured operation; no process-local work item is authoritative.</summary>
public sealed class EmployeeBulkImportWorker(
    IServiceScopeFactory scopes,
    IOptions<EmployeeBulkImportWorkerOptions> options,
    ILogger<EmployeeBulkImportWorker> logger) : BackgroundService
{
    private readonly EmployeeBulkImportWorkerOptions _options = options.Value;

    /// <summary>The immutable option value captured by the hosted worker at construction.</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>Checks the private worker target without exposing it in diagnostics.</summary>
    public bool IsConfiguredFor(string tenantCode, Guid operationId) =>
        string.Equals(_options.TargetTenantCode, tenantCode, StringComparison.Ordinal)
        && _options.TargetOperationId == operationId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Employee bulk import worker is disabled.");
            return;
        }

        var delay = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var foundWork = false;
            try
            {
                foundWork = await ProcessTenantsAsync(stoppingToken);
                delay = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError("Employee import worker poll failed. ExceptionType={ExceptionType}.", exception.GetType().Name);
                delay = TimeSpan.FromSeconds(Math.Min(Math.Max(delay.TotalSeconds * 2, _options.PollIntervalSeconds), 60));
            }

            if (!foundWork || stoppingToken.IsCancellationRequested)
                await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task<bool> ProcessTenantsAsync(CancellationToken cancellationToken)
    {
        var targetTenantCode = _options.TargetTenantCode!;
        var targetOperationId = _options.TargetOperationId!.Value;
        List<ShardDescriptor> tenants;
        await using (var catalogScope = scopes.CreateAsyncScope())
        {
            tenants = await SelectTargetTenants(
                    catalogScope.ServiceProvider.GetRequiredService<IHrmsCatalogDbContext>().Tenants.AsNoTracking(),
                    targetTenantCode)
                .Select(x => new ShardDescriptor(x.Id, x.TenantCode, x.Host, x.ShardKey, x.Status, x.DatabaseProvider))
                .Take(2)
                .ToListAsync(cancellationToken);
        }

        if (tenants.Count != 1)
        {
            logger.LogWarning("Employee import target tenant {TenantCode} resolved to {MatchCount} active catalog rows; no operation was claimed.",
                targetTenantCode, tenants.Count);
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var shard = tenants[0];
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IShardContext>().Use(shard);
        scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>().Use(shard.TenantId);
        try
        {
            return await scope.ServiceProvider.GetRequiredService<IEmployeeBulkImportExecutionService>()
                .ProcessQueuedOperationAsync(targetOperationId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError("Targeted employee import processing failed. ExceptionType={ExceptionType}.", exception.GetType().Name);
            return false;
        }
    }

    internal static IQueryable<Tenant> SelectTargetTenants(IQueryable<Tenant> tenants, string targetTenantCode) =>
        tenants.Where(x => x.Status == TenantStatus.Active && x.TenantCode == targetTenantCode);
}
