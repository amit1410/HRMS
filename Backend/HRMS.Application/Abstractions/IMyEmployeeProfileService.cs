using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IMyEmployeeProfileService
{
    Task<Result<MyEmployeeProfileDto>> GetAsync(CancellationToken cancellationToken = default);
}
