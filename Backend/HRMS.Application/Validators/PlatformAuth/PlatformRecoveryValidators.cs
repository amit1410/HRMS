using FluentValidation;
using HRMS.Application.DTOs.PlatformAuth;

namespace HRMS.Application.Validators.PlatformAuth;

public sealed class PlatformForgotUsernameRequestValidator : AbstractValidator<PlatformForgotUsernameRequest>
{
    public PlatformForgotUsernameRequestValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

public sealed class PlatformForgotPasswordRequestValidator : AbstractValidator<PlatformForgotPasswordRequest>
{
    public PlatformForgotPasswordRequestValidator() => RuleFor(x => x.Identifier).NotEmpty().MaximumLength(256);
}

public sealed class PlatformResetPasswordRequestValidator : AbstractValidator<PlatformResetPasswordRequest>
{
    public PlatformResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12).MaximumLength(256)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a number.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a special character.");
        RuleFor(x => x.ConfirmPassword).Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}
