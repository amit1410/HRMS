using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeeEducationService
{
    Task<Result<IReadOnlyList<EmployeeEducationDto>>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeEducationDto>> CreateAsync(Guid employeeId, EmployeeEducationRequest request, CancellationToken cancellationToken = default);
    Task<Result<EmployeeEducationDto>> UpdateAsync(Guid employeeId, Guid id, EmployeeEducationRequest request, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(Guid employeeId, Guid id, CancellationToken cancellationToken = default);
}
