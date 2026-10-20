namespace HRMS.Application.Abstractions;

/// <summary>Ensures this tenant's permission catalog contains every permission known by this application.</summary>
public interface IPermissionCatalogProvisioner
{
    Task EnsureProvisionedAsync(CancellationToken cancellationToken = default);
}
