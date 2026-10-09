namespace HRMS.Application.DTOs.PlatformAuth;

public sealed record PlatformForgotUsernameRequest(string Email);
public sealed record PlatformForgotPasswordRequest(string Identifier);
public sealed record PlatformResetPasswordRequest(string Token, string NewPassword, string ConfirmPassword);
public sealed record PlatformRecoveryResponse(string Message);
