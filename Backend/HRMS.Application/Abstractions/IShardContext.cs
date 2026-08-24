namespace HRMS.Application.Abstractions;

/// <summary>
/// The shard selected for the current unit of work — scoped, and set exactly once: by the host-resolution
/// middleware during a request, or explicitly by whatever drives a non-HTTP unit of work (startup
/// provisioning, seeding, a background job).
/// <para>
/// The companion to <see cref="ITenantContext"/>, and the two must not be confused. <c>ITenantContext</c>
/// comes from the verified JWT and decides <em>which rows</em> are visible; this decides <em>which
/// database</em> is opened. One is proven, the other is asserted by the client through the URL it was
/// typed into, which is why a mismatch between them has to be an authorization failure rather than
/// something either side quietly wins.
/// </para>
/// </summary>
public interface IShardContext
{
    /// <summary>
    /// The resolved shard, or null when nothing has resolved one — an apex host, an unknown host, or any
    /// code path that runs outside a request without selecting a shard first.
    /// </summary>
    ShardDescriptor? Current { get; }

    /// <summary>True when a shard has been selected for this scope.</summary>
    bool HasShard { get; }

    /// <summary>
    /// Selects the shard for this scope. Idempotent for the same organization; selecting a
    /// <em>different</em> one throws, because anything already read or tracked on this scope's connection
    /// belongs to the first — re-pointing halfway through is the failure mode where one tenant's writes
    /// land in another's database.
    /// </summary>
    void Use(ShardDescriptor descriptor);
}
