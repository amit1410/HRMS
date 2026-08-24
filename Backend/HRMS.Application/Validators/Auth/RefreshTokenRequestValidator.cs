using FluentValidation;
using HRMS.Application.DTOs.Auth;

namespace HRMS.Application.Validators.Auth;

/// <summary>Shape validation for refresh/sign-out requests.</summary>
public class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.")
            .MaximumLength(200).WithMessage("Refresh token is not valid.");
    }
}
