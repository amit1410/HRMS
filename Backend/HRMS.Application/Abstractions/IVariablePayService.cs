using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IVariablePayService
{
    Task<Result<IReadOnlyList<VariablePayPlanDto>>> GetPlansAsync(CancellationToken ct = default);
    Task<Result<VariablePayPlanDto>> CreatePlanAsync(VariablePayPlanRequest request, CancellationToken ct = default);
    Task<Result<VariablePayPlanDto>> UpdatePlanAsync(Guid id, VariablePayPlanRequest request, CancellationToken ct = default);
    Task<Result<VariablePayPlanDto>> AddVersionAsync(Guid id, VariablePayPlanVersionRequest request, CancellationToken ct = default);
    Task<Result<VariablePayAwardDto>> PreviewAsync(Guid employeeId, VariablePayAwardRequest request, CancellationToken ct = default);
    Task<Result<VariablePayAwardDto>> GenerateAsync(Guid employeeId, VariablePayAwardRequest request, CancellationToken ct = default);
    Task<Result<VariablePayBulkGenerationResult>> GenerateBulkAsync(VariablePayBulkGenerationRequest request, CancellationToken ct = default);
    Task<Result<PagedResult<VariablePayAwardDto>>> GetAwardsAsync(VariablePayAwardQuery query, CancellationToken ct = default);
    Task<Result<VariablePayAwardDto>> SubmitAsync(Guid id, CancellationToken ct = default);
    Task<Result<VariablePayAwardDto>> ApproveAsync(Guid id, CancellationToken ct = default);
    Task<Result<VariablePayAwardDto>> RejectAsync(Guid id, string reason, CancellationToken ct = default);
    Task<Result<VariablePayAwardDto>> OverrideAsync(Guid id, VariablePayOverrideRequest request, CancellationToken ct = default);
    Task<Result<VariablePayAwardDto>> CancelAsync(Guid id, string reason, CancellationToken ct = default);
    Task<Result<VariablePayAwardDto>> SettleAsync(Guid id, VariablePaySettlementRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<VariablePayAwardHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default);
    Task<Result<VariablePayAwardDto>> GetAsync(Guid id, CancellationToken ct = default);
}
