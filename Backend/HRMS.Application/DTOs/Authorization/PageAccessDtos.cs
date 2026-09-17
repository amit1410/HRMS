namespace HRMS.Application.DTOs.Authorization;

using HRMS.Domain.Enums;

public sealed record PageAccessActionDto(string Code, string Name, string Permission, bool Allowed);
public sealed record PageAccessPageDto(string Code, string Name, string ModuleCode, string Route, IReadOnlyList<string> RequiredPermissions, bool CanView, IReadOnlyList<PageAccessActionDto> Actions);
public sealed record PageAccessRoleDto(int Id, string Name, string? Description);
public sealed record PageAccessMatrixDto(PageAccessRoleDto Role, IReadOnlyList<PageAccessPageDto> Pages, IReadOnlyList<string> GrantedPermissions);
public sealed record PageAccessUpdateRequest(IReadOnlyList<string> Permissions, string? Reason = null);
public sealed record PageAccessHistoryQuery(int? RoleId = null, Guid? UserId = null, Guid? UserRoleAssignmentId = null, string? EventType = null, DateTime? FromDate = null, DateTime? ToDate = null, int Page = 1, int PageSize = 25);
public sealed record PageAccessHistoryItemDto(Guid Id, DateTime OccurredAtUtc, string EventType, string Action, int? RoleId, string? RoleName, Guid? UserId, Guid? UserRoleAssignmentId, string? PermissionCode, string? ScopeDimension, Guid? ScopeValueId, string? ScopeValueDisplay, string? OldValue, string? NewValue, Guid ActorUserId, string? ActorDisplayName, string? Reason);
public sealed record NavigationItemDto(string Code, string Name, string Route, string ModuleCode, IReadOnlyList<NavigationItemDto> Children);
public sealed record EffectiveRoleAccessDto(Guid AssignmentId, int RoleId, string RoleName, RoleAssignmentSource Source, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool TenantWide, IReadOnlyList<RoleAssignmentScopePreviewDto> Scopes);
public sealed record RoleAssignmentScopePreviewDto(RoleScopeType ScopeType, Guid ScopeEntityId);
public sealed record UserAccessPreviewDto(Guid UserId, Guid? EmployeeId, IReadOnlyList<EffectiveRoleAccessDto> Roles, IReadOnlyList<string> Permissions, IReadOnlyList<NavigationItemDto> Pages, bool HasManagerAccess, int TenantWideRoleCount);
public sealed record AccessPreviewUserDto(Guid UserId, string DisplayName, string Email);
