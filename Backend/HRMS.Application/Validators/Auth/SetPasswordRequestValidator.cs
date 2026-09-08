using FluentValidation;
using HRMS.Application.DTOs.Auth;

namespace HRMS.Application.Validators.Auth;

public sealed class SetPasswordRequestValidator : AbstractValidator<SetPasswordRequest>
{
    public SetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128).Must(PasswordPolicy.IsValid)
            .WithMessage(PasswordPolicy.Message);
        RuleFor(x => x.ConfirmPassword).Equal(x => x.Password).WithMessage("Passwords do not match.");
    }
}

public static class PasswordPolicy
{
    public const string Message = "Password must be 8-128 characters and include upper, lower, and numeric characters.";

    public static bool IsValid(string? value) => !string.IsNullOrEmpty(value)
        && value.Length is >= 8 and <= 128
        && value.Any(char.IsUpper)
        && value.Any(char.IsLower)
        && value.Any(char.IsDigit);
}
