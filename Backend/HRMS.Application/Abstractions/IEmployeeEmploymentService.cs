using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeeEmploymentService
{
    // Joining information (EmployeeEmployment — 1:1 with Employee)
    Task<Result<EmployeeEmploymentDto>> GetEmploymentAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeEmploymentDto>> UpsertEmploymentAsync(Guid employeeId, EmployeeEmploymentRequest request, CancellationToken cancellationToken = default);

    // Effective-dated position history
    Task<Result<IReadOnlyList<EmployeeEmploymentHistoryDto>>> GetHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeEmploymentHistoryDto>> GetAsOfAsync(Guid employeeId, DateOnly asOfDate, CancellationToken cancellationToken = default);
    Task<Result<EmployeeEmploymentHistoryDto>> GetCurrentAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeEmploymentHistoryDto>> CreateChangeAsync(Guid employeeId, EmploymentChangeRequest request, string changedBy, CancellationToken cancellationToken = default);
}
