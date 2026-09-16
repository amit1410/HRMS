using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Roles;

public sealed record RoleAssignmentScopeDto(RoleScopeType ScopeType, Guid ScopeEntityId);

public sealed record RoleAssignmentRequest(
    int RoleId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Reason,
    IReadOnlyList<RoleAssignmentScopeDto>? Scopes);

public sealed record RoleAssignmentRevokeRequest(DateOnly? EffectiveTo, string? Reason);

public sealed record RoleAssignmentDto(
    Guid AssignmentId, Guid UserId, Guid? EmployeeId, string? EmployeeCode, string? EmployeeName,
    int RoleId, string RoleName, RoleAssignmentSource AssignmentSource,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsCurrentlyEffective,
    string? Reason, Guid? AssignedByUserId, IReadOnlyList<RoleAssignmentScopeDto> Scopes,
    bool IsSystemManaged, bool CanRevoke,
    string Status = "Active", bool IsRevoked = false, DateOnly? RevokedEffectiveDate = null,
    string? ScopeSummary = null);

public sealed class RoleAssignmentQuery : PagedQuery
{
    public int? RoleId { get; set; }
    public string? Status { get; set; }
    public RoleAssignmentSource? Source { get; set; }

    public static readonly IReadOnlySet<string> Statuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Active", "Scheduled", "Expired", "Revoked" };
}

public sealed record RoleManagementCandidateDto(
    Guid UserId,
    Guid? EmployeeId,
    string? EmployeeCode,
    string DisplayName,
    string? Department,
    string? Designation,
    string? Location);

public sealed record RoleAssignmentHistoryDto(
    Guid EventId, Guid AssignmentId, Guid UserId, int RoleId, string RoleName,
    UserRoleAssignmentEventType EventType, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    RoleAssignmentSource AssignmentSource, string? Reason, Guid? PerformedByUserId,
    DateTime OccurredAtUtc);
