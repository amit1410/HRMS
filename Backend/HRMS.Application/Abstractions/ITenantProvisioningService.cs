namespace HRMS.Application.Abstractions;

/// <summary>
/// Makes one organization's database ready to be used: schema created or migrated, reference data and the
/// organization's own row in place.
/// <para>
/// One operation with two callers, deliberately. Startup provisions every organization already in the
/// catalog, and onboarding provisions the one it has just added — and if those were two pieces of code, a
/// customer created through onboarding would sooner or later differ from one created at startup in a way
/// that only shows up months later. The catalog row must exist first: it is what says which database this
/// organization's data belongs in.
/// </para>
/// </summary>
public interface ITenantProvisioningService
{
    /// <summary>
    /// Prepares and seeds the database for <paramref name="shard"/>. Idempotent — safe on every startup, and
    /// safe to retry after a failure, because every step inserts only what is missing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The organization has no catalog row. The caller resolved this descriptor from somewhere, so a missing
    /// row means the catalog changed underneath it, and seeding a tenant database whose identity cannot be
    /// confirmed is not a recoverable situation.
    /// </exception>
    Task ProvisionAsync(ShardDescriptor shard, CancellationToken cancellationToken = default);
}
