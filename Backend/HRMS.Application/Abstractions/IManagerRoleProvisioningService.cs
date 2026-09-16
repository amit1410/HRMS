namespace HRMS.Application.Abstractions;

/// <summary>Ensures linked accounts have the capability required by effective manager assignments.</summary>
public interface IManagerRoleProvisioningService
{
    Task EnsureForManagerEmployeeAsync(Guid tenantId, Guid managerEmployeeId, CancellationToken cancellationToken = default);
    Task EnsureForLinkedEmployeeAsync(Guid tenantId, Guid userId, Guid employeeId, CancellationToken cancellationToken = default);
}
