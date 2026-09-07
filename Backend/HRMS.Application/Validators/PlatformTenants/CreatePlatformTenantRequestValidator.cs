using FluentValidation;
using HRMS.Application.DTOs.PlatformTenants;
using HRMS.Domain.Enums;

namespace HRMS.Application.Validators.PlatformTenants;

public sealed class CreatePlatformTenantRequestValidator : AbstractValidator<CreatePlatformTenantRequest>
{
    public CreatePlatformTenantRequestValidator()
    {
        RuleFor(x => x.TenantName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TenantCode).NotEmpty().Matches("^[A-Za-z0-9_-]{2,20}$");
        RuleFor(x => x.Host).NotEmpty().MaximumLength(253);
        RuleFor(x => x.ShardKey).NotEmpty().Matches("^[a-z0-9][a-z0-9_-]{0,63}$");
        RuleFor(x => x.DatabaseProvider).IsInEnum().Must(x => x is DatabaseProviderType.SqlServer or DatabaseProviderType.MySql);
        RuleFor(x => x.InitialAdminEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Address).MaximumLength(500);
    }
}
