using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface ISeparationBenefitsService
{
    Task<Result<IReadOnlyList<GratuityPolicyDto>>> GetPoliciesAsync(CancellationToken ct = default);
    Task<Result<GratuityPolicyDto>> CreatePolicyAsync(GratuityPolicyRequest request, CancellationToken ct = default);
    Task<Result<GratuityPolicyDto>> UpdatePolicyAsync(Guid id, GratuityPolicyRequest request, CancellationToken ct = default);
    Task<Result<GratuityPolicyDto>> AddVersionAsync(Guid id, GratuityPolicyVersionRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SeparationBenefitHistoryDto>>> GetPolicyHistoryAsync(Guid policyId, CancellationToken ct = default);
    Task<Result<SeparationBenefitDto>> PreviewAsync(Guid employeeId, SeparationBenefitCalculationRequest request, CancellationToken ct = default);
    Task<Result<SeparationBenefitDto>> CalculateAsync(Guid employeeId, Guid finalSettlementId, SeparationBenefitCalculationRequest request, CancellationToken ct = default);
    Task<Result<SeparationBenefitDto>> GetAsync(Guid employeeId, Guid? finalSettlementId = null, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SeparationBenefitHistoryDto>>> GetHistoryAsync(Guid employeeId, Guid? finalSettlementId = null, CancellationToken ct = default);
    Task<Result<PagedResult<SeparationBenefitRegisterRowDto>>> RegisterAsync(SeparationBenefitQuery query, CancellationToken ct = default);
    Task<Result<GratuityCalculationDto>> OverrideAsync(Guid id, GratuityOverrideRequest request, CancellationToken ct = default);
    Task<Result<GratuityCalculationDto>> ApproveOverrideAsync(Guid id, CancellationToken ct = default);
    Task<Result<bool>> FinalizeAsync(Guid finalSettlementId, CancellationToken ct = default);
}
