namespace HRMS.Application.Abstractions;

/// <summary>Everything the token service needs to mint an access token for a signed-in user.</summary>
public sealed record AccessTokenDescriptor(
    Guid UserId,
    Guid TenantId,
    string TenantCode,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);

/// <summary>A minted access token and the instant it expires (UTC).</summary>
public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

/// <summary>
/// Issues signed JWT access tokens and the opaque refresh tokens that accompany them. Implemented in
/// Infrastructure so the Application layer never references a specific token library.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Creates a signed JWT carrying the user's identity, tenant, roles and permissions.</summary>
    AccessTokenResult CreateAccessToken(AccessTokenDescriptor descriptor);

    /// <summary>
    /// Creates a cryptographically random, opaque refresh token. Returned in plain text to the caller
    /// exactly once; only <see cref="HashRefreshToken"/> output is persisted.
    /// </summary>
    string CreateRefreshToken();

    /// <summary>Hashes a refresh token for storage/lookup. Deterministic, so it can be used as a key.</summary>
    string HashRefreshToken(string refreshToken);
}
