using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeeAdditionalInfoService
{
    Task<Result<EmployeeAdditionalInfoDto>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeAdditionalInfoDto>> UpsertAsync(Guid employeeId, EmployeeAdditionalInfoRequest request, CancellationToken cancellationToken = default);
}
