using HRMS.Application.Abstractions;

namespace HRMS.Infrastructure.Sharding;

/// <summary>
/// Holds the shard for one scope. Scoped, and write-once.
/// <para>
/// Not thread-safe, and it does not need to be: a scope belongs to one request, and ASP.NET Core does not
/// run a request's middleware or its action concurrently. Sharing a scope across threads is already
/// unsupported for the <c>DbContext</c> that reads this.
/// </para>
/// </summary>
public sealed class ShardContext : IShardContext
{
    public ShardDescriptor? Current { get; private set; }

    public bool HasShard => Current is not null;

    public void Use(ShardDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (Current is null)
        {
            Current = descriptor;
            return;
        }

        // Records compare by value, so re-selecting the same organization is a no-op rather than a
        // conflict. Selecting a different one is refused: the DbContext for this scope may already be
        // holding an open connection to the first shard and tracking its entities, so the second selection
        // would move subsequent writes to another organization's database while the tenant stamp still says
        // the first. Silence there means data in the wrong customer's database.
        if (Current != descriptor)
        {
            throw new InvalidOperationException(
                $"The shard for this scope is already set to '{Current.ShardKey}' and cannot be changed to "
                + $"'{descriptor.ShardKey}'. Start a new scope for a different organization.");
        }
    }
}
