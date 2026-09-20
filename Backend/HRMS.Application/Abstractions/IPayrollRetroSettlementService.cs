using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IPayrollRetroSettlementService
{
    Task<Result<PayrollRetroCaseDto>> CreateRetroCaseAsync(PayrollRetroCaseRequest request, CancellationToken ct = default);
    Task<Result<PayrollRetroCaseDto>> EvaluateRetroAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollRetroCaseDto>> ApproveRetroAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollRetroCaseDto>> ApplyRetroAsync(Guid id, Guid targetPayrollRunId, CancellationToken ct = default);
    Task<Result<FinalSettlementDto>> CreateSettlementAsync(FinalSettlementRequest request, CancellationToken ct = default);
    Task<Result<FinalSettlementDto>> AddSettlementLineAsync(Guid id, FinalSettlementLineRequest request, CancellationToken ct = default);
    Task<Result<FinalSettlementDto>> CalculateSettlementAsync(Guid id, CancellationToken ct = default);
    Task<Result<FinalSettlementDto>> ApproveSettlementAsync(Guid id, CancellationToken ct = default);
    Task<Result<FinalSettlementDto>> FinalizeSettlementAsync(Guid id, CancellationToken ct = default);
}
