using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;

namespace HRMS.Application.Abstractions;

public interface IPasswordRecoveryService
{
    Task<Result<RecoveryChallengeDto>> IdentifyAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task<Result<RecoverySendOtpDto>> SendOtpAsync(SendRecoveryOtpRequest request, CancellationToken cancellationToken = default);
    Task<Result<RecoverySendOtpDto>> ResendOtpAsync(ResendRecoveryOtpRequest request, CancellationToken cancellationToken = default);
    Task<Result<RecoveryVerificationDto>> VerifyOtpAsync(VerifyRecoveryOtpRequest request, CancellationToken cancellationToken = default);
    Task<Result<bool>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task<Result<bool>> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
}
