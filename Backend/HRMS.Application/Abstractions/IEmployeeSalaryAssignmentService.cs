using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IEmployeeSalaryAssignmentService
{
    Task<Result<PagedResult<EmployeeSalaryAssignmentDto>>> GetAsync(EmployeeSalaryAssignmentQuery query, CancellationToken ct = default);
    Task<Result<EmployeeSalaryAssignmentDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Result<PagedResult<EmployeeSalaryAssignmentDto>>> GetForEmployeeAsync(Guid employeeId, EmployeeSalaryAssignmentQuery query, CancellationToken ct = default);
    Task<Result<EmployeeSalaryAssignmentDto>> GetEffectiveAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);
    Task<Result<EmployeeSalaryAssignmentDto>> CreateAsync(EmployeeSalaryAssignmentRequest request, CancellationToken ct = default);
    Task<Result<EmployeeSalaryAssignmentDto>> UpdateAsync(Guid id, EmployeeSalaryAssignmentRequest request, CancellationToken ct = default);
    Task<Result<EmployeeSalaryAssignmentDto>> SetActiveAsync(Guid id, bool active, int? expectedConcurrencyVersion, CancellationToken ct = default);
    Task<Result<IReadOnlyList<EmployeeSalaryAssignmentHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default);
    Task<Result<EmployeeSalaryComponentDto>> AddComponentAsync(Guid id, EmployeeSalaryComponentRequest request, CancellationToken ct = default);
    Task<Result<EmployeeSalaryComponentDto>> UpdateComponentAsync(Guid id, Guid componentId, EmployeeSalaryComponentRequest request, CancellationToken ct = default);
    Task<Result<bool>> RemoveComponentAsync(Guid id, Guid componentId, CancellationToken ct = default);
}
