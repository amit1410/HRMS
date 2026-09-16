namespace HRMS.Application.Abstractions;

/// <summary>Provisions the system-managed Employee role for authoritative account links.</summary>
public interface IEmployeeRoleProvisioningService
{
    Task EnsureForLinkedEmployeeAsync(Guid tenantId, Guid userId, Guid employeeId, CancellationToken cancellationToken = default);
}
