using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// Builds the <see cref="ShardDescriptor"/> host resolution would produce for a seeded organization's
/// address, from the same catalog rows the resolver reads.
/// <para>
/// One definition, so a test's idea of "resolved at demo01" cannot drift from what the middleware actually
/// puts in the shard context — a descriptor hand-written in a test could carry a tenant id, code or status
/// combination that host resolution would never produce, and then prove something about a state the app
/// cannot reach.
/// </para>
/// </summary>
public static class TestShards
{
    /// <summary>The seeded organizations' own addresses, as the catalog records them.</summary>
    public const string Demo01Host = "demo01.localhost";

    public const string Demo02Host = "demo02.localhost";

    /// <summary>
    /// The organization at the given address, exactly as resolution would report it. Throws for an unseeded
    /// host rather than inventing one — an unresolved address is <c>null</c>, which callers state explicitly.
    /// </summary>
    public static ShardDescriptor For(string host)
    {
        var tenant = SeedData.Tenants.Single(t => t.Host == host);

        return new ShardDescriptor(tenant.Id, tenant.TenantCode, tenant.Host, tenant.ShardKey, tenant.Status);
    }

    /// <summary>
    /// An organization the catalog routes somewhere that holds no row for it — registered but never
    /// provisioned, or a catalog pointing at the wrong shard. Active, because a resolved descriptor always
    /// is: the middleware refuses a suspended organization before it reaches anything.
    /// </summary>
    public static ShardDescriptor Unprovisioned { get; } =
        new(Guid.NewGuid(), "GHOST", "ghost.localhost", "ghost", TenantStatus.Active);
}
