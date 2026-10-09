using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;

namespace HRMS.Application.Abstractions;

/// <summary>
/// CRUD + lifecycle for tenant confirmation/probation policies, their effective-dated versions
/// (probation configuration + applicability), and read-side resolution for an employee. Mirrors the
/// leave configuration service conventions (Result/PagedResult, tenant-scoped via the DbContext filter).
/// </summary>
public interface IConfirmationPolicyService
{
    Task<Result<PagedResult<ConfirmationPolicyDto>>> GetPoliciesAsync(ConfirmationPolicyQuery query, CancellationToken ct = default);
    Task<Result<ConfirmationPolicyDto>> GetPolicyAsync(Guid policyId, CancellationToken ct = default);
    Task<Result<ConfirmationPolicyDto>> CreatePolicyAsync(ConfirmationPolicyRequest request, CancellationToken ct = default);
    Task<Result<ConfirmationPolicyDto>> UpdatePolicyAsync(Guid policyId, ConfirmationPolicyRequest request, CancellationToken ct = default);

    Task<Result<PagedResult<ConfirmationPolicyVersionDto>>> GetVersionsAsync(Guid policyId, CancellationToken ct = default);
    Task<Result<ConfirmationPolicyEditorDto>> GetEditorAsync(Guid policyId, Guid? versionId, CancellationToken ct = default);
    Task<Result<ConfirmationPolicyEditorDto>> CreateVersionAsync(Guid policyId, ConfirmationPolicyVersionRequest request, CancellationToken ct = default);
    Task<Result<ConfirmationPolicyEditorDto>> UpdateVersionAsync(Guid policyId, Guid versionId, ConfirmationPolicyVersionRequest request, CancellationToken ct = default);
    Task<Result<ConfirmationPolicyVersionDto>> PublishVersionAsync(Guid policyId, Guid versionId, CancellationToken ct = default);

    Task<Result<ConfirmationPolicyResolveDto>> ResolveAsync(ConfirmationPolicyResolveRequest request, CancellationToken ct = default);
    Task<Result<EmployeeConfirmationStatusDto>> GetEmployeeStatusAsync(Guid employeeId, CancellationToken ct = default);
}
