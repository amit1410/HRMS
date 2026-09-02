using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeeBankDetailService
{
    Task<Result<IReadOnlyList<EmployeeBankDetailDto>>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeBankDetailEditDto>> GetForEditAsync(
        Guid employeeId, Guid id, CancellationToken cancellationToken = default);
    Task<Result<EmployeeBankDetailDto>> CreateAsync(Guid employeeId, EmployeeBankDetailRequest request, CancellationToken cancellationToken = default);
    Task<Result<EmployeeBankDetailDto>> UpdateAsync(Guid employeeId, Guid id, EmployeeBankDetailRequest request, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(Guid employeeId, Guid id, CancellationToken cancellationToken = default);
}
