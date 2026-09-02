using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeeFamilyService
{
    Task<Result<IReadOnlyList<EmployeeFamilyDto>>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeFamilyDto>> CreateAsync(Guid employeeId, EmployeeFamilyRequest request, CancellationToken cancellationToken = default);
    Task<Result<EmployeeFamilyDto>> UpdateAsync(Guid employeeId, Guid id, EmployeeFamilyRequest request, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(Guid employeeId, Guid id, CancellationToken cancellationToken = default);
}
