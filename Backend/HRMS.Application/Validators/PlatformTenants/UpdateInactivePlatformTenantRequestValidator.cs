using FluentValidation;
using HRMS.Application.DTOs.PlatformTenants;

namespace HRMS.Application.Validators.PlatformTenants;

public sealed class UpdateInactivePlatformTenantRequestValidator : AbstractValidator<UpdateInactivePlatformTenantRequest>
{
    public UpdateInactivePlatformTenantRequestValidator()
    {
        RuleFor(x => x.TenantName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Host).NotEmpty().MaximumLength(253);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).MaximumLength(256);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Address).MaximumLength(500);
    }
}
