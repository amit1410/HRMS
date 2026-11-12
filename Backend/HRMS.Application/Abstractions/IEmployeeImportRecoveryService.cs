using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeeImportRecoveryService
{
    Task<EmployeeImportRecoveryReport> DryRunAsync(Guid operationId, Guid expectedTenantId, CancellationToken cancellationToken = default);
    Task<EmployeeImportRecoveryReport> RecoverAsync(Guid operationId, Guid expectedTenantId, string operatorName,
        string reason, bool explicitlyApproved, CancellationToken cancellationToken = default);
}
