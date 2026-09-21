using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IReimbursementService
{
    Task<Result<IReadOnlyList<ReimbursementCategoryDto>>> GetCategoriesAsync(CancellationToken ct = default);
    Task<Result<ReimbursementCategoryDto>> CreateCategoryAsync(ReimbursementCategoryRequest request, CancellationToken ct = default);
    Task<Result<ReimbursementCategoryDto>> UpdateCategoryAsync(Guid id, ReimbursementCategoryRequest request, CancellationToken ct = default);
    Task<Result<ReimbursementCategoryDto>> CreatePolicyVersionAsync(Guid categoryId, ReimbursementPolicyVersionRequest request, CancellationToken ct = default);
    Task<Result<PagedResult<ReimbursementRegisterRowDto>>> GetRegisterAsync(ReimbursementClaimQuery query, CancellationToken ct = default);
    Task<Result<ReimbursementClaimDto>> GetClaimAsync(Guid id, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ReimbursementHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default);
    Task<Result<PagedResult<ReimbursementClaimDto>>> GetClaimsAsync(ReimbursementClaimQuery query, CancellationToken ct = default);
    Task<Result<ReimbursementClaimDto>> CreateClaimAsync(ReimbursementClaimRequest request, CancellationToken ct = default);
    Task<Result<ReimbursementClaimDto>> UpdateClaimAsync(Guid id, ReimbursementClaimRequest request, CancellationToken ct = default);
    Task<Result<ReimbursementClaimDto>> SubmitAsync(Guid id, CancellationToken ct = default);
    Task<Result<ReimbursementClaimDto>> ApproveAsync(Guid id, ReimbursementApprovalRequest request, CancellationToken ct = default);
    Task<Result<ReimbursementClaimDto>> RejectAsync(Guid id, string reason, CancellationToken ct = default);
    Task<Result<ReimbursementClaimDto>> SettleManuallyAsync(Guid id, ReimbursementSettlementRequest request, CancellationToken ct = default);
    Task<Result<ReimbursementClaimDto>> CancelAsync(Guid id, string reason, CancellationToken ct = default);
    Task<Result<ReimbursementAttachmentDto>> AddAttachmentAsync(Guid claimId, ReimbursementAttachmentRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ReimbursementPayrollRecovery>>> GetPayrollRecoveriesAsync(Guid employeeId, DateOnly asOf, CancellationToken ct = default);
    Task<Result<ReimbursementSettlementDto>> RecordPayrollSettlementAsync(Guid claimId, Guid claimLineId, Guid payrollRunId, Guid payrollResultId, decimal amount, DateOnly date, CancellationToken ct = default);
}

public interface IReimbursementPayrollResolver
{
    Task<IReadOnlyList<ReimbursementPayrollRecovery>> ResolveAsync(Guid employeeId, DateOnly asOf, CancellationToken ct = default);
}
