using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeeContactService
{
    Task<Result<EmployeeContactDto>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeContactDto>> UpsertAsync(Guid employeeId, EmployeeContactRequest request, CancellationToken cancellationToken = default);
}
