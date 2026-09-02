using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeePreviousEmploymentService
{
    Task<Result<IReadOnlyList<EmployeePreviousEmploymentDto>>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<EmployeePreviousEmploymentDto>> CreateAsync(Guid employeeId, EmployeePreviousEmploymentRequest request, CancellationToken cancellationToken = default);
    Task<Result<EmployeePreviousEmploymentDto>> UpdateAsync(Guid employeeId, Guid id, EmployeePreviousEmploymentRequest request, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(Guid employeeId, Guid id, CancellationToken cancellationToken = default);
}
