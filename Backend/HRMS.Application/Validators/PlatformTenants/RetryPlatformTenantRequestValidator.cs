using FluentValidation;
using HRMS.Application.DTOs.PlatformTenants;

namespace HRMS.Application.Validators.PlatformTenants;

public sealed class RetryPlatformTenantRequestValidator : AbstractValidator<RetryPlatformTenantRequest>
{
    public RetryPlatformTenantRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.InitialAdminEmail).NotEmpty().EmailAddress().MaximumLength(256);
    }
}
