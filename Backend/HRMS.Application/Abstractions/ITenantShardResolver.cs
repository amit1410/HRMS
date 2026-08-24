namespace HRMS.Application.Abstractions;

/// <summary>
/// Maps a request's host to the organization that signs in there. The catalog is the only source: this is
/// the lookup that has to happen before any tenant database can be opened.
/// </summary>
public interface ITenantShardResolver
{
    /// <summary>
    /// The organization signing in at <paramref name="host"/>, or null when no organization does.
    /// <para>
    /// <paramref name="host"/> is a bare host with no port and no scheme; case and surrounding whitespace
    /// are normalized here rather than being the caller's problem. A returned descriptor may be for an
    /// organization that is not <c>Active</c> — see <see cref="ShardDescriptor.Status"/>.
    /// </para>
    /// </summary>
    Task<ShardDescriptor?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default);
}
