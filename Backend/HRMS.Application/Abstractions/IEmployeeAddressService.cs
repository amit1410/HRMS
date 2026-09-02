using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeeAddressService
{
    Task<Result<IReadOnlyList<EmployeeAddressDto>>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeAddressDto>> UpsertAsync(Guid employeeId, EmployeeAddressRequest request, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(Guid employeeId, Guid addressId, CancellationToken cancellationToken = default);
}
