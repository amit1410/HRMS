namespace HRMS.Application.DTOs.PlatformAuth;

public sealed record PlatformLoginRequest(string Email, string Password);
public sealed record PlatformRefreshRequest(string RefreshToken);
public sealed record PlatformLogoutRequest(string RefreshToken);

public sealed record PlatformIdentityDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record PlatformLoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    int ExpiresInSeconds,
    PlatformIdentityDto User);
