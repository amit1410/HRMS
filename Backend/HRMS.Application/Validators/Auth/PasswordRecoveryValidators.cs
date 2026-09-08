using FluentValidation;
using HRMS.Application.DTOs.Auth;

namespace HRMS.Application.Validators.Auth;

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(x => x.Identifier).NotEmpty().MaximumLength(256);
}

public sealed class SendRecoveryOtpRequestValidator : AbstractValidator<SendRecoveryOtpRequest>
{
    public SendRecoveryOtpRequestValidator() => RuleFor(x => x.ChallengeId).NotEmpty().MaximumLength(128);
}

public sealed class ResendRecoveryOtpRequestValidator : AbstractValidator<ResendRecoveryOtpRequest>
{
    public ResendRecoveryOtpRequestValidator() => RuleFor(x => x.ChallengeId).NotEmpty().MaximumLength(128);
}

public sealed class VerifyRecoveryOtpRequestValidator : AbstractValidator<VerifyRecoveryOtpRequest>
{
    public VerifyRecoveryOtpRequestValidator()
    {
        RuleFor(x => x.ChallengeId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Otp).NotEmpty().Matches("^\\d{6}$");
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.ResetToken).NotEmpty().MaximumLength(256);
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(128).Must(PasswordPolicy.IsValid).WithMessage(PasswordPolicy.Message);
        RuleFor(x => x.ConfirmPassword).Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(128).Must(PasswordPolicy.IsValid).WithMessage(PasswordPolicy.Message);
        RuleFor(x => x.ConfirmPassword).Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}
