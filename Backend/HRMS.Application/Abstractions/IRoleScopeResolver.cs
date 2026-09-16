using HRMS.Domain.Entities;

namespace HRMS.Application.Abstractions;

public interface IRoleScopeResolver
{
    Task<bool> AppliesAsync(UserRole assignment, Guid employeeId, DateOnly effectiveDate, CancellationToken cancellationToken = default);
}
