using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HRMS.Infrastructure.Sharding;

/// <summary>
/// Resolves a host against the catalog, with a short-lived cache in front of it.
/// <para>
/// The cache is not an optimization detail — without it this is a database round trip on every request,
/// including every unauthenticated one, which makes the catalog the first thing to fall over under load and
/// the easiest thing in the system to attack. Negative results are cached too, for the same reason: unknown
/// hosts are the traffic an attacker gets to choose.
/// </para>
/// </summary>
public sealed class TenantShardResolver : ITenantShardResolver
{
    private const string CacheKeyPrefix = "hrms.shard.host:";

    private readonly IHrmsCatalogDbContext _catalog;
    private readonly IMemoryCache _cache;
    private readonly ShardingOptions _options;

    public TenantShardResolver(IHrmsCatalogDbContext catalog, IMemoryCache cache, IOptions<ShardingOptions> options)
    {
        _catalog = catalog;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<ShardDescriptor?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(host);
        if (normalized is null)
        {
            return null;
        }

        var cacheKey = CacheKeyPrefix + normalized;

        // A cached miss is a hit: TryGetValue reports the key exists and hands back null, which is the
        // difference between remembering "nobody signs in there" and asking the catalog again every time.
        if (_cache.TryGetValue(cacheKey, out ShardDescriptor? cached))
        {
            return cached;
        }

        // Host is stored lowercase and compared to a lowercased input, so this is an exact match on a unique
        // index and behaves identically on SQL Server's case-insensitive default collation and on SQLite's
        // case-sensitive one. Relying on either provider's collation would make the two disagree.
        var resolved = await _catalog.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Host == normalized)
            .Select(tenant => new ShardDescriptor(
                tenant.Id,
                tenant.TenantCode,
                tenant.Host,
                tenant.ShardKey,
                tenant.Status))
            .FirstOrDefaultAsync(cancellationToken);

        _cache.Set(
            cacheKey,
            resolved,
            TimeSpan.FromSeconds(resolved is null ? _options.UnknownHostCacheSeconds : _options.CacheSeconds));

        return resolved;
    }

    /// <summary>
    /// The comparable form of a host, or null when no stored host could possibly equal it.
    /// </summary>
    private static string? Normalize(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        // A fully-qualified host may carry the root label's trailing dot ("demo01.hrms.com."). It is the same
        // host, and dropping it here is the difference between resolving and 404ing for the clients that
        // send it.
        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();

        // Longer than the column can hold, so no row can match. Answered without a query — otherwise an
        // oversized Host header is a free parameterized query against the catalog.
        return normalized.Length is 0 || normalized.Length > TenantMapping.HostMaxLength ? null : normalized;
    }
}
