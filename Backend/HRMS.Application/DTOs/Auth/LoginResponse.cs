namespace HRMS.Application.DTOs.Auth;

/// <summary>
/// A successful sign-in or refresh. The access token is a signed JWT sent as a Bearer credential; the
/// refresh token is opaque, single-use, and returned in plain text only here (the server keeps a hash).
/// </summary>
public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    int ExpiresInSeconds,
    AuthenticatedUserDto User)
{
    /// <summary>Scheme the client must use on the Authorization header.</summary>
    public string TokenType => "Bearer";
}
