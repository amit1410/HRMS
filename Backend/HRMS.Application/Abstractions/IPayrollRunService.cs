using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface IPayrollRunService
{
    Task<Result<PagedResult<PayrollRunDto>>> GetAsync(PayrollRunQuery query, CancellationToken ct = default);
    Task<Result<PayrollRunDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollRunDto>> CreateAsync(PayrollRunRequest request, CancellationToken ct = default);
    Task<Result<PayrollRunDto>> PrepareAsync(Guid id, bool rebuild, CancellationToken ct = default);
    Task<Result<PayrollRunDto>> TransitionAsync(Guid id, PayrollRunStatus target, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollRunEmployeeDto>>> GetEmployeesAsync(Guid id, PagedQuery query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollRunHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default);
}
