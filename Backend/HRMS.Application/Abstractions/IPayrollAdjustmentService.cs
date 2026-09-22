using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IPayrollAdjustmentService
{
    Task<Result<IReadOnlyList<PayrollAdjustmentReasonDto>>> GetReasonsAsync(CancellationToken ct = default);
    Task<Result<PayrollAdjustmentReasonDto>> CreateReasonAsync(PayrollAdjustmentReasonRequest request, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollAdjustmentDto>>> GetAsync(PayrollAdjustmentQuery query, CancellationToken ct = default);
    Task<Result<PayrollAdjustmentDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollAdjustmentDto>> CreateAsync(PayrollAdjustmentRequest request, CancellationToken ct = default);
    Task<Result<PayrollAdjustmentDto>> SubmitAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollAdjustmentDto>> ApproveAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollAdjustmentDto>> RejectAsync(Guid id, string reason, CancellationToken ct = default);
    Task<Result<PayrollAdjustmentDto>> CancelAsync(Guid id, string reason, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollAdjustmentHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollRunDto>>> GetOffCycleRunsAsync(PayrollRunQuery query, CancellationToken ct = default);
    Task<Result<PayrollRunDto>> CreateOffCycleRunAsync(PayrollOffCycleRunRequest request, CancellationToken ct = default);
    Task<Result<PayrollRunDto>> PrepareOffCycleRunAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollRunDto>> PreviewOffCycleRunAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollRunDto>> ApproveOffCycleRunAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollRunDto>> ProcessOffCycleRunAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollRunDto>> CancelOffCycleRunAsync(Guid id, string reason, CancellationToken ct = default);
    Task<Result<PayrollReversalDto>> RequestReversalAsync(PayrollReversalRequest request, CancellationToken ct = default);
    Task<Result<PayrollReversalDto>> ApproveReversalAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollReversalDto>> ProcessReversalAsync(Guid id, CancellationToken ct = default);
}
