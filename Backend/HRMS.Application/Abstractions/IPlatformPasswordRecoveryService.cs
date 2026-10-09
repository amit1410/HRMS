using HRMS.Application.Common;
using HRMS.Application.DTOs.PlatformAuth;

namespace HRMS.Application.Abstractions;

public interface IPlatformPasswordRecoveryService
{
    Task<Result<PlatformRecoveryResponse>> ForgotUsernameAsync(PlatformForgotUsernameRequest request, CancellationToken cancellationToken = default);
    Task<Result<PlatformRecoveryResponse>> ForgotPasswordAsync(PlatformForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task<Result<PlatformRecoveryResponse>> ResetPasswordAsync(PlatformResetPasswordRequest request, CancellationToken cancellationToken = default);
}
