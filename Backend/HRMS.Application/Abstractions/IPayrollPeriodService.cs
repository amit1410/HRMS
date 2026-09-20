using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IPayrollPeriodService
{
    Task<Result<PagedResult<PayrollPeriodDto>>> GetAsync(PayrollPeriodQuery query, CancellationToken ct = default);
    Task<Result<PayrollPeriodDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollPeriodDto>> CreateAsync(PayrollPeriodRequest request, CancellationToken ct = default);
    Task<Result<PayrollPeriodDto>> UpdateAsync(Guid id, PayrollPeriodRequest request, CancellationToken ct = default);
    Task<Result<PayrollPeriodDto>> TransitionAsync(Guid id, string action, int? expectedVersion, string? reason = null, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollPeriodHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default);
}
