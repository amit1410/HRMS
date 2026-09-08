using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Auth;

public sealed record ForgotPasswordRequest(string Identifier);
public sealed record SendRecoveryOtpRequest(string ChallengeId, PasswordRecoveryChannel Channel);
public sealed record ResendRecoveryOtpRequest(string ChallengeId);
public sealed record VerifyRecoveryOtpRequest(string ChallengeId, string Otp);
public sealed record ResetPasswordRequest(string ResetToken, string NewPassword, string ConfirmPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);

public sealed record RecoveryChannelDto(PasswordRecoveryChannel Channel, string MaskedDestination);
public sealed record RecoveryChallengeDto(string ChallengeId, IReadOnlyList<RecoveryChannelDto> AvailableChannels, string Message);
public sealed record RecoverySendOtpDto(string ChallengeId, PasswordRecoveryChannel Channel, string MaskedDestination, string? DevelopmentOtp, string Message);
public sealed record RecoveryVerificationDto(string ResetToken, string Message);
