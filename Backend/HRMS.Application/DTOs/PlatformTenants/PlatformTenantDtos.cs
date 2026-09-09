using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.PlatformTenants;

public sealed record PlatformTenantListItemDto(
    Guid Id,
    string TenantCode,
    string TenantName,
    string Host,
    DatabaseProviderType DatabaseProvider,
    string ShardKey,
    TenantStatus Status,
    string WebUrl);

public sealed record PlatformTenantDetailDto(
    Guid Id,
    string TenantCode,
    string TenantName,
    string Host,
    DatabaseProviderType DatabaseProvider,
    string ShardKey,
    TenantStatus Status,
    string? Email,
    string? Phone,
    string? Address,
    string WebUrl,
    string? InitialAdminEmail,
    string? DevelopmentTemporaryPassword);

public sealed class CreatePlatformTenantRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public DatabaseProviderType DatabaseProvider { get; set; } = DatabaseProviderType.MySql;
    public string ShardKey { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string InitialAdminEmail { get; set; } = string.Empty;
}

public sealed class UpdateInactivePlatformTenantRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
}

public sealed class RetryPlatformTenantRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string InitialAdminEmail { get; set; } = string.Empty;
}

public sealed class ResetTenantAdminPasswordRequest
{
    public string? AdminEmail { get; set; }
}

public sealed record ResetTenantAdminPasswordResponse(
    Guid TenantId,
    string TenantCode,
    string TenantName,
    string AdminEmail,
    string TemporaryPassword,
    string Message);
