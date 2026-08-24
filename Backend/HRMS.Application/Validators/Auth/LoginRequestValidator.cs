using FluentValidation;
using HRMS.Application.DTOs.Auth;

namespace HRMS.Application.Validators.Auth;

/// <summary>
/// Shape validation for sign-in. Deliberately does not enforce password complexity: complexity belongs
/// on the endpoints that set a password, and applying it here would tell an attacker which submitted
/// passwords could never be valid.
/// <para>
/// There is no organization rule, because there is no organization field — the host decides that. A
/// <c>NotEmpty</c> rule left behind for a field the sign-in form no longer has would reject every
/// attempt with a message naming something the user cannot see or fill in.
/// </para>
/// </summary>
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.")
            .EmailAddress().WithMessage("Email must be a valid email address.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(128).WithMessage("Password must not exceed 128 characters.");
    }
}
