using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence.Seed;

namespace HRMS.Infrastructure.Persistence;

/// <summary>Repairs a tenant's reference permission catalog without changing role grants.</summary>
public sealed class PermissionCatalogProvisioner(IHrmsDbContext db) : IPermissionCatalogProvisioner
{
    public async Task EnsureProvisionedAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        await DatabaseSeeder.EnsurePermissionsAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
