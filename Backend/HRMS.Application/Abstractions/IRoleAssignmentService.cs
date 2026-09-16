using HRMS.Application.Common;
using HRMS.Application.DTOs.Roles;

namespace HRMS.Application.Abstractions;

public interface IRoleAssignmentService
{
    Task<Result<IReadOnlyList<RoleSummary>>> GetRolesAsync(bool assignableOnly, CancellationToken ct = default);
    Task<Result<PagedResult<RoleAssignmentDto>>> GetAssignmentsAsync(RoleAssignmentQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<RoleManagementCandidateDto>>> GetCandidatesAsync(PagedQuery query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<RoleAssignmentDto>>> GetUserAssignmentsAsync(Guid userId, CancellationToken ct = default);
    Task<Result<RoleAssignmentDto>> AssignAsync(Guid userId, RoleAssignmentRequest request, CancellationToken ct = default);
    Task<Result<RoleAssignmentDto>> RevokeAsync(Guid assignmentId, RoleAssignmentRevokeRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<RoleAssignmentHistoryDto>>> GetAssignmentHistoryAsync(Guid assignmentId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<RoleAssignmentHistoryDto>>> GetUserHistoryAsync(Guid userId, CancellationToken ct = default);
}

public sealed record RoleSummary(int Id, string Name, string? Description);
