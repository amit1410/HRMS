using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface ITaxDeclarationService
{
    Task<Result<IReadOnlyList<TaxDeclarationCycleDto>>> GetCyclesAsync(CancellationToken ct = default);
    Task<Result<TaxDeclarationCycleDto>> CreateCycleAsync(TaxDeclarationCycleRequest request, CancellationToken ct = default);
    Task<Result<TaxDeclarationCategoryDto>> CreateCategoryAsync(TaxDeclarationCategoryRequest request, CancellationToken ct = default);
    Task<Result<TaxDeclarationItemDto>> CreateItemAsync(TaxDeclarationItemRequest request, CancellationToken ct = default);
    Task<Result<EmployeeTaxDeclarationDto>> GetOwnAsync(CancellationToken ct = default);
    Task<Result<EmployeeTaxDeclarationDto>> CreateOwnAsync(Guid cycleId, CancellationToken ct = default);
    Task<Result<EmployeeTaxDeclarationDto>> AddLineOwnAsync(TaxDeclarationLineRequest request, CancellationToken ct = default);
    Task<Result<EmployeeTaxDeclarationDto>> UpdateLineOwnAsync(Guid declarationId, Guid lineId, TaxDeclarationLineUpdateRequest request, CancellationToken ct = default);
    Task<Result<EmployeeTaxDeclarationDto>> DeleteLineOwnAsync(Guid declarationId, Guid lineId, CancellationToken ct = default);
    Task<Result<EmployeeTaxDeclarationDto>> SubmitOwnAsync(CancellationToken ct = default);
    Task<Result<EmployeeTaxDeclarationDto>> ResubmitOwnAsync(Guid declarationId, CancellationToken ct = default);
    Task<Result<TaxDeclarationProofDto>> AddProofOwnAsync(TaxDeclarationProofRequest request, CancellationToken ct = default);
    Task<Result<TaxDeclarationProofDto>> ReplaceProofOwnAsync(Guid declarationId, Guid lineId, Guid proofId, TaxDeclarationProofRequest request, CancellationToken ct = default);
    Task<Result<PagedResult<EmployeeTaxDeclarationDto>>> GetReviewAsync(TaxDeclarationReviewQuery query, CancellationToken ct = default);
    Task<Result<EmployeeTaxDeclarationDto>> ReviewLineAsync(Guid declarationId, TaxDeclarationReviewRequest request, CancellationToken ct = default);
    Task<Result<TaxDeclarationProofDto>> ReviewProofAsync(Guid declarationId, Guid lineId, Guid proofId, TaxDeclarationProofReviewRequest request, CancellationToken ct = default);
    Task<Result<EmployeeTaxDeclarationDto>> ApproveAsync(Guid declarationId, CancellationToken ct = default);
    Task<Result<EmployeeTaxDeclarationDto>> RequestResubmissionAsync(Guid declarationId, string? comment, CancellationToken ct = default);
    Task<Result<EmployeeTaxDeclarationDto>> LockAsync(Guid declarationId, CancellationToken ct = default);
    Task<Result<EmployeeTaxDeclarationDto>> ReopenAsync(Guid declarationId, string? reason, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TaxDeclarationAuditDto>>> GetAuditAsync(Guid declarationId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ApprovedTaxDeclarationInput>>> ResolveApprovedAsync(Guid employeeId, int financialYear, DateOnly asOfDate, CancellationToken ct = default);
}
