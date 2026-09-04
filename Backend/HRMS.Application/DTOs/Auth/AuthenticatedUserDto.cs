namespace HRMS.Application.DTOs.Auth;

/// <summary>
/// The authenticated user's profile, tenant and effective authorization data. Projected from entities
/// so EF types are never serialized to clients, and it deliberately carries no password material.
/// </summary>
public sealed record AuthenticatedUserDto(
    Guid Id,
    Guid TenantId,
    string TenantCode,
    string TenantName,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    DateTime? LastLoginDateUtc,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    EmployeeIdentityDto? EmployeeIdentity = null);

public sealed record EmployeeIdentityDto(
    string Status,
    Guid? Revision,
    Guid? LinkId,
    EmployeeIdentityEmployeeDto? Employee,
    string EmploymentEligibility,
    DateOnly? BusinessDate);

public sealed record EmployeeIdentityEmployeeDto(Guid Id, string DisplayName, string? EmployeeCode);
