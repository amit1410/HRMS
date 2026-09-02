using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeeSupervisorService
{
    Task<Result<EmployeeSupervisorDto>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeSupervisorDto>> UpsertAsync(Guid employeeId, EmployeeSupervisorRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<SupervisorOptionDto>>> GetSupervisorOptionsAsync(Guid employeeId, string supervisorType, CancellationToken cancellationToken = default);
}
