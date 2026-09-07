using HRMS.Application.Common;
using HRMS.Application.DTOs.PlatformAuth;

namespace HRMS.Application.Abstractions;

public interface IPlatformAuthService
{
    Task<Result<PlatformLoginResponse>> LoginAsync(PlatformLoginRequest request, CancellationToken cancellationToken = default);
    Task<Result<PlatformLoginResponse>> RefreshAsync(PlatformRefreshRequest request, CancellationToken cancellationToken = default);
    Task<Result<bool>> LogoutAsync(PlatformLogoutRequest request, CancellationToken cancellationToken = default);
    Task<Result<PlatformIdentityDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}
