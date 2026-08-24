using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;

namespace HRMS.Application.Abstractions;

/// <summary>
/// Authentication use cases: signing in with tenant-scoped credentials, exchanging a refresh token for
/// a fresh token pair, signing out, and describing the caller. All business rules live here — the
/// controller only maps results to HTTP responses.
/// </summary>
public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<Result<LoginResponse>> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>Revokes the supplied refresh token. Idempotent: an unknown or already-revoked token still succeeds.</summary>
    Task<Result<bool>> LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>Describes the currently authenticated user, resolved from the server-side tenant context.</summary>
    Task<Result<AuthenticatedUserDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}
